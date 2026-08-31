using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace ShitDesigner.Scene {
	/// <summary>Moves multiple image or video surfaces independently within orthographic camera bounds.</summary>
	[DisallowMultipleComponent]
	public sealed class DvdBounceScene : MonoBehaviour {
		public const int MinimumInstanceCount = 1;
		public const int MaximumInstanceCount = 64;
		public const float MinimumSpeed = 0.01f;

		[Header("Visual")]
		[SerializeField] private Texture2D m_Image;
		[SerializeField] private VideoClip m_Video;
		[Min(0.01f)][SerializeField] private float m_VisualSize = 1.6f;
		[Range(MinimumInstanceCount, MaximumInstanceCount)][SerializeField] private int m_InstanceCount = 1;

		[Header("Additional Sources")]
		[SerializeField] private VisualSource[] m_AdditionalSources = Array.Empty<VisualSource>();

		[Header("Video")]
		[Min(0f)][SerializeField] private float m_VideoPlaybackSpeed = 1f;

		[Header("Motion")]
		[Min(MinimumSpeed)][SerializeField] private float m_Speed = 4.5f;
		[SerializeField] private Vector2 m_InitialDirection = new Vector2(1f, 0.63f);
		[SerializeField] private Vector2 m_InitialPosition = Vector2.zero;

		private readonly List<BouncingVisual> m_Visuals = new List<BouncingVisual>();
		private readonly Dictionary<VideoClip, SharedVideoPlayback> m_VideoPlaybacks = new Dictionary<VideoClip, SharedVideoPlayback>();
		private readonly List<Material> m_Materials = new List<Material>();
		private Camera m_Camera;

		public int InstanceCount => m_InstanceCount;
		public float Speed => m_Speed;

		private void Awake() {
			m_Camera = Camera.main;
			if (m_Camera == null)
				m_Camera = FindAnyObjectByType<Camera>();
			CreateVisuals();
		}

		private void Start() {
			InitializeVisuals();
		}

		private void Update() {
			UpdateVideoTextures();
			if (m_Camera == null)
				return;
			if (m_Visuals.Count != m_InstanceCount)
				SynchronizeInstanceCount();

			for (var index = 0; index < m_Visuals.Count; index++) {
				var visual = m_Visuals[index];
				var velocity = GetVelocityAtCurrentSpeed(visual.Velocity, index);
				var position = (Vector2)visual.Object.transform.position + velocity * Time.unscaledDeltaTime;
				ReflectWithinBounds(ref position, ref velocity, GetMovementBounds(visual.Renderer));
				visual.Object.transform.position = new Vector3(position.x, position.y, 0f);
				visual.Velocity = velocity;
			}
		}

		private void OnDestroy() {
			ReleaseVisuals();
		}

		private void OnValidate() {
			m_VisualSize = Mathf.Max(0.01f, m_VisualSize);
			m_InstanceCount = Mathf.Clamp(m_InstanceCount, MinimumInstanceCount, MaximumInstanceCount);
			m_VideoPlaybackSpeed = Mathf.Max(0f, m_VideoPlaybackSpeed);
			m_Speed = Mathf.Max(MinimumSpeed, m_Speed);
			m_AdditionalSources ??= Array.Empty<VisualSource>();
			if (m_InitialDirection.sqrMagnitude < 0.0001f)
				m_InitialDirection = new Vector2(1f, 0.63f);

			for (var index = 0; index < m_Visuals.Count; index++) {
				var visual = m_Visuals[index];
				visual.Object.transform.localScale = ToScale(GetVisualSize(visual.Source));
				ApplyImage(visual);
			}

			foreach (var playback in m_VideoPlaybacks.Values)
				playback.Player.playbackSpeed = m_VideoPlaybackSpeed;
		}

		[ContextMenu("Rebuild Visuals")]
		public void Rebuild() {
			CreateVisuals();
			if (UnityEngine.Application.isPlaying)
				InitializeVisuals();
		}

		public void SetInstanceCount(int instanceCount) {
			m_InstanceCount = Mathf.Clamp(instanceCount, MinimumInstanceCount, MaximumInstanceCount);
		}

		public void SetSpeed(float speed) {
			m_Speed = Mathf.Max(MinimumSpeed, speed);
		}

		private void CreateVisuals() {
			ReleaseVisuals();
			for (var index = 0; index < m_InstanceCount; index++)
				m_Visuals.Add(CreateVisual(index));
		}

		private BouncingVisual CreateVisual(int index) {
			var source = GetSource(index);
			var visualObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
			visualObject.name = $"Bouncing Visual {index + 1:00}";
			visualObject.layer = gameObject.layer;
			visualObject.transform.SetParent(transform, false);
			visualObject.transform.localScale = ToScale(GetVisualSize(source));
			var collider = visualObject.GetComponent<Collider>();
			if (collider != null)
				Destroy(collider);

			var renderer = visualObject.GetComponent<MeshRenderer>();
			var material = source.Video == null
				? CreateMaterial()
				: GetOrCreateVideoPlayback(source.Video).Material;
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			renderer.allowOcclusionWhenDynamic = false;
			renderer.sortingOrder = 1;

			var visual = new BouncingVisual(visualObject, renderer, material, source);
			ApplyImage(visual);
			return visual;
		}

		private void InitializeVisuals() {
			if (m_Camera == null)
				return;

			for (var index = 0; index < m_Visuals.Count; index++)
				InitializeVisual(m_Visuals[index], index);

			PlayStoppedVideoPlaybacks();
		}

		private void InitializeVisual(BouncingVisual visual, int index) {
			var position = index == 0 ? m_InitialPosition : GetSpawnPosition(index, visual.Renderer);
			visual.Object.transform.position = new Vector3(position.x, position.y, 0f);
			visual.Velocity = GetInitialVelocity(index);
			KeepWithinBounds(visual);
		}

		private void SynchronizeInstanceCount() {
			while (m_Visuals.Count > m_InstanceCount)
				RemoveVisualAt(m_Visuals.Count - 1);

			while (m_Visuals.Count < m_InstanceCount) {
				var index = m_Visuals.Count;
				var visual = CreateVisual(index);
				m_Visuals.Add(visual);
				InitializeVisual(visual, index);
			}

			PlayStoppedVideoPlaybacks();
		}

		private void RemoveVisualAt(int index) {
			var visual = m_Visuals[index];
			m_Visuals.RemoveAt(index);
			if (visual.Object != null)
				Destroy(visual.Object);

			if (visual.Source.Video == null) {
				ReleaseMaterial(visual.Material);
				return;
			}

			ReleaseVideoPlaybackIfUnused(visual.Source.Video);
		}

		private void ReleaseVideoPlaybackIfUnused(VideoClip video) {
			for (var index = 0; index < m_Visuals.Count; index++) {
				if (m_Visuals[index].Source.Video == video)
					return;
			}

			if (!m_VideoPlaybacks.Remove(video, out var playback))
				return;
			if (playback.Host != null)
				Destroy(playback.Host);
			ReleaseMaterial(playback.Material);
		}

		private void PlayStoppedVideoPlaybacks() {
			foreach (var playback in m_VideoPlaybacks.Values) {
				if (!playback.Player.isPlaying)
					playback.Player.Play();
			}
		}

		private SharedVideoPlayback GetOrCreateVideoPlayback(VideoClip video) {
			if (m_VideoPlaybacks.TryGetValue(video, out var existing))
				return existing;

			var host = new GameObject($"Video {video.name}");
			host.layer = gameObject.layer;
			host.transform.SetParent(transform, false);
			var player = host.AddComponent<VideoPlayer>();
			player.source = VideoSource.VideoClip;
			player.clip = video;
			player.renderMode = VideoRenderMode.APIOnly;
			player.audioOutputMode = VideoAudioOutputMode.None;
			player.isLooping = true;
			player.playOnAwake = false;
			player.playbackSpeed = m_VideoPlaybackSpeed;

			var material = CreateMaterial();
			var playback = new SharedVideoPlayback(host, player, material, GetTexturePropertyName(material));
			m_VideoPlaybacks.Add(video, playback);
			return playback;
		}

		private void UpdateVideoTextures() {
			foreach (var playback in m_VideoPlaybacks.Values) {
				var texture = playback.Player.texture;
				if (texture != null && playback.Material.GetTexture(playback.TexturePropertyName) != texture)
					playback.Material.SetTexture(playback.TexturePropertyName, texture);
			}
		}

		private void ApplyImage(BouncingVisual visual) {
			if (visual.Source.Video != null)
				return;

			visual.Material.SetTexture(GetTexturePropertyName(visual.Material), visual.Source.Image == null ? Texture2D.whiteTexture : visual.Source.Image);
		}

		private VisualSource GetSource(int index) {
			var sourceCount = m_AdditionalSources == null ? 0 : m_AdditionalSources.Length;
			var sourceIndex = index % (sourceCount + 1);
			if (sourceIndex == 0)
				return new VisualSource(m_Image, m_Video);

			return m_AdditionalSources[sourceIndex - 1] ?? new VisualSource();
		}

		private Vector2 GetVisualSize(VisualSource source) {
			var size = Mathf.Max(0.01f, m_VisualSize);
			var aspectRatio = GetSourceAspectRatio(source);
			return aspectRatio >= 1f
				? new Vector2(size, size / aspectRatio)
				: new Vector2(size * aspectRatio, size);
		}

		private static float GetSourceAspectRatio(VisualSource source) {
			if (source.Video != null && source.Video.width > 0 && source.Video.height > 0)
				return (float)source.Video.width / source.Video.height;
			if (source.Image != null && source.Image.width > 0 && source.Image.height > 0)
				return (float)source.Image.width / source.Image.height;
			return 16f / 9f;
		}

		private static Vector3 ToScale(Vector2 size) {
			return new Vector3(size.x, size.y, 1f);
		}

		private Vector2 GetInitialVelocity(int index) {
			var direction = Quaternion.Euler(0f, 0f, index * 137.5f) * m_InitialDirection;
			return direction.normalized * m_Speed;
		}

		private Vector2 GetVelocityAtCurrentSpeed(Vector2 velocity, int index) {
			return velocity.sqrMagnitude > 0.0001f
				? velocity.normalized * m_Speed
				: GetInitialVelocity(index);
		}

		private Vector2 GetSpawnPosition(int index, MeshRenderer renderer) {
			var bounds = GetMovementBounds(renderer);
			var horizontalProgress = Mathf.Repeat(index * 0.618034f, 1f);
			var verticalProgress = Mathf.Repeat(index * 0.414214f, 1f);
			return new Vector2(
				Mathf.Lerp(bounds.Minimum.x, bounds.Maximum.x, horizontalProgress),
				Mathf.Lerp(bounds.Minimum.y, bounds.Maximum.y, verticalProgress));
		}

		private Bounds2D GetMovementBounds(MeshRenderer renderer) {
			var cameraHalfHeight = m_Camera.orthographicSize;
			var cameraHalfWidth = cameraHalfHeight * m_Camera.aspect;
			var visualExtents = renderer.bounds.extents;
			return new Bounds2D(
				new Vector2(-cameraHalfWidth + visualExtents.x, -cameraHalfHeight + visualExtents.y),
				new Vector2(cameraHalfWidth - visualExtents.x, cameraHalfHeight - visualExtents.y));
		}

		private void KeepWithinBounds(BouncingVisual visual) {
			var position = (Vector2)visual.Object.transform.position;
			var velocity = visual.Velocity;
			ReflectWithinBounds(ref position, ref velocity, GetMovementBounds(visual.Renderer));
			visual.Object.transform.position = new Vector3(position.x, position.y, 0f);
			visual.Velocity = velocity;
		}

		private static void ReflectWithinBounds(ref Vector2 position, ref Vector2 velocity, Bounds2D bounds) {
			if (position.x < bounds.Minimum.x || position.x > bounds.Maximum.x) {
				position.x = Mathf.Clamp(position.x, bounds.Minimum.x, bounds.Maximum.x);
				velocity.x = -velocity.x;
			}
			if (position.y < bounds.Minimum.y || position.y > bounds.Maximum.y) {
				position.y = Mathf.Clamp(position.y, bounds.Minimum.y, bounds.Maximum.y);
				velocity.y = -velocity.y;
			}
		}

		private void ReleaseVisuals() {
			for (var index = 0; index < m_Visuals.Count; index++) {
				var visual = m_Visuals[index];
				if (visual.Object != null)
					Destroy(visual.Object);
			}
			m_Visuals.Clear();

			foreach (var playback in m_VideoPlaybacks.Values) {
				if (playback.Host != null)
					Destroy(playback.Host);
			}
			m_VideoPlaybacks.Clear();

			for (var index = 0; index < m_Materials.Count; index++) {
				if (m_Materials[index] != null)
					Destroy(m_Materials[index]);
			}
			m_Materials.Clear();
		}

		private void ReleaseMaterial(Material material) {
			m_Materials.Remove(material);
			if (material != null)
				Destroy(material);
		}

		private Material CreateMaterial() {
			var shader = Shader.Find("Universal Render Pipeline/Unlit")
				?? Shader.Find("Unlit/Texture")
				?? throw new InvalidOperationException("An unlit shader is required for the DVD bounce scene.");
			var material = new Material(shader) { name = "DVD Bounce Visual" };
			if (material.HasProperty("_Cull"))
				material.SetInt("_Cull", (int)CullMode.Off);
			if (material.HasProperty("_Surface")) {
				material.SetFloat("_Surface", 1f);
				material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			}
			if (material.HasProperty("_SrcBlend"))
				material.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
			if (material.HasProperty("_DstBlend"))
				material.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
			if (material.HasProperty("_ZWrite"))
				material.SetFloat("_ZWrite", 0f);
			material.renderQueue = (int)RenderQueue.Transparent;
			m_Materials.Add(material);
			return material;
		}

		private static string GetTexturePropertyName(Material material) {
			return material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
		}

		private sealed class BouncingVisual {
			public GameObject Object { get; }
			public MeshRenderer Renderer { get; }
			public Material Material { get; }
			public VisualSource Source { get; }
			public Vector2 Velocity { get; set; }

			public BouncingVisual(GameObject visualObject, MeshRenderer renderer, Material material, VisualSource source) {
				Object = visualObject;
				Renderer = renderer;
				Material = material;
				Source = source;
			}
		}

		private sealed class SharedVideoPlayback {
			public GameObject Host { get; }
			public VideoPlayer Player { get; }
			public Material Material { get; }
			public string TexturePropertyName { get; }

			public SharedVideoPlayback(GameObject host, VideoPlayer player, Material material, string texturePropertyName) {
				Host = host;
				Player = player;
				Material = material;
				TexturePropertyName = texturePropertyName;
			}
		}

		[Serializable]
		private sealed class VisualSource {
			[SerializeField] private Texture2D m_Image;
			[SerializeField] private VideoClip m_Video;

			public Texture2D Image => m_Image;
			public VideoClip Video => m_Video;

			public VisualSource() { }

			public VisualSource(Texture2D image, VideoClip video) {
				m_Image = image;
				m_Video = video;
			}
		}

		private readonly struct Bounds2D {
			public readonly Vector2 Minimum;
			public readonly Vector2 Maximum;

			public Bounds2D(Vector2 minimum, Vector2 maximum) {
				Minimum = minimum;
				Maximum = maximum;
			}
		}
	}
}
