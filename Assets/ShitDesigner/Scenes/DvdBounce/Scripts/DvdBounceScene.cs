using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace ShitDesigner.Scene {
	/// <summary>Moves multiple image or video surfaces independently within orthographic camera bounds.</summary>
	[DisallowMultipleComponent]
	public sealed class DvdBounceScene : MonoBehaviour {
		[Header("Visual")]
		[SerializeField] private Texture2D m_Image;
		[SerializeField] private VideoClip m_Video;
		[Min(0.01f)][SerializeField] private float m_VisualSize = 1.6f;
		[Range(1, 32)][SerializeField] private int m_InstanceCount = 1;

		[Header("Additional Sources")]
		[SerializeField] private VisualSource[] m_AdditionalSources = Array.Empty<VisualSource>();

		[Header("Video")]
		[Min(0f)][SerializeField] private float m_VideoPlaybackSpeed = 1f;

		[Header("Motion")]
		[Min(0.01f)][SerializeField] private float m_Speed = 4.5f;
		[SerializeField] private Vector2 m_InitialDirection = new Vector2(1f, 0.63f);
		[SerializeField] private Vector2 m_InitialPosition = Vector2.zero;

		private readonly List<BouncingVisual> m_Visuals = new List<BouncingVisual>();
		private readonly Dictionary<VideoClip, SharedVideoPlayback> m_VideoPlaybacks = new Dictionary<VideoClip, SharedVideoPlayback>();
		private readonly List<Material> m_Materials = new List<Material>();
		private Camera m_Camera;
		private Vector2 m_MotionPosition;
		private Vector2 m_MotionVelocity;

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

			m_MotionPosition += m_MotionVelocity * Time.unscaledDeltaTime;
			ReflectWithinBounds(ref m_MotionPosition, ref m_MotionVelocity, GetSynchronizedMovementBounds());
			SynchronizeVisualPositions();
		}

		private void OnDestroy() {
			ReleaseVisuals();
		}

		private void OnValidate() {
			m_VisualSize = Mathf.Max(0.01f, m_VisualSize);
			m_InstanceCount = Mathf.Clamp(m_InstanceCount, 1, 32);
			m_VideoPlaybackSpeed = Mathf.Max(0f, m_VideoPlaybackSpeed);
			m_Speed = Mathf.Max(0.01f, m_Speed);
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

			if (m_Camera != null && m_Visuals.Count > 0) {
				ReflectWithinBounds(ref m_MotionPosition, ref m_MotionVelocity, GetSynchronizedMovementBounds());
				SynchronizeVisualPositions();
			}
		}

		[ContextMenu("Rebuild Visuals")]
		public void Rebuild() {
			CreateVisuals();
			if (Application.isPlaying)
				InitializeVisuals();
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

			m_MotionPosition = m_InitialPosition;
			m_MotionVelocity = GetInitialVelocity();
			ReflectWithinBounds(ref m_MotionPosition, ref m_MotionVelocity, GetSynchronizedMovementBounds());
			SynchronizeVisualPositions();

			foreach (var playback in m_VideoPlaybacks.Values)
				playback.Player.Play();
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

		private Vector2 GetInitialVelocity() {
			return m_InitialDirection.normalized * m_Speed;
		}

		private Bounds2D GetSynchronizedMovementBounds() {
			var bounds = GetMovementBounds(m_Visuals[0].Renderer);
			for (var index = 1; index < m_Visuals.Count; index++) {
				var visualBounds = GetMovementBounds(m_Visuals[index].Renderer);
				bounds = new Bounds2D(
					Vector2.Max(bounds.Minimum, visualBounds.Minimum),
					Vector2.Min(bounds.Maximum, visualBounds.Maximum));
			}
			return bounds;
		}

		private Bounds2D GetMovementBounds(MeshRenderer renderer) {
			var cameraHalfHeight = m_Camera.orthographicSize;
			var cameraHalfWidth = cameraHalfHeight * m_Camera.aspect;
			var visualExtents = renderer.bounds.extents;
			return new Bounds2D(
				new Vector2(-cameraHalfWidth + visualExtents.x, -cameraHalfHeight + visualExtents.y),
				new Vector2(cameraHalfWidth - visualExtents.x, cameraHalfHeight - visualExtents.y));
		}

		private void SynchronizeVisualPositions() {
			var position = new Vector3(m_MotionPosition.x, m_MotionPosition.y, 0f);
			for (var index = 0; index < m_Visuals.Count; index++)
				m_Visuals[index].Object.transform.position = position;
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
