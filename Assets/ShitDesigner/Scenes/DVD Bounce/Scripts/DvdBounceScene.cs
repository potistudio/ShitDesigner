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

		[Header("Video")]
		[Min(0f)][SerializeField] private float m_VideoPlaybackSpeed = 1f;

		[Header("Motion")]
		[Min(0.01f)][SerializeField] private float m_Speed = 4.5f;
		[SerializeField] private Vector2 m_InitialDirection = new Vector2(1f, 0.63f);
		[SerializeField] private Vector2 m_InitialPosition = Vector2.zero;

		private readonly List<BouncingVisual> m_Visuals = new List<BouncingVisual>();
		private Camera m_Camera;

		private void Awake() {
			m_Camera = Camera.main;
			if (m_Camera == null)
				m_Camera = FindFirstObjectByType<Camera>();
			CreateVisuals();
		}

		private void Start() {
			InitializeVisuals();
		}

		private void Update() {
			if (m_Camera == null)
				return;

			for (var index = 0; index < m_Visuals.Count; index++) {
				var visual = m_Visuals[index];
				var position = (Vector2)visual.Object.transform.position + visual.Velocity * Time.unscaledDeltaTime;
				ReflectWithinBounds(ref position, ref visual.Velocity, GetMovementBounds(visual.Renderer));
				visual.Object.transform.position = new Vector3(position.x, position.y, 0f);
			}
		}

		private void OnDestroy() {
			ReleaseVisuals();
		}

		private void OnValidate() {
			m_VisualSize = Mathf.Max(0.01f, m_VisualSize);
			m_InstanceCount = Mathf.Clamp(m_InstanceCount, 1, 32);
			m_VideoPlaybackSpeed = Mathf.Max(0f, m_VideoPlaybackSpeed);
			m_Speed = Mathf.Max(0.01f, m_Speed);
			if (m_InitialDirection.sqrMagnitude < 0.0001f)
				m_InitialDirection = new Vector2(1f, 0.63f);

			for (var index = 0; index < m_Visuals.Count; index++) {
				var visual = m_Visuals[index];
				visual.Object.transform.localScale = ToScale(GetVisualSize());
				ApplyImage(visual);
				if (visual.VideoPlayer != null)
					visual.VideoPlayer.playbackSpeed = m_VideoPlaybackSpeed;
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
			var visualObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
			visualObject.name = $"Bouncing Visual {index + 1:00}";
			visualObject.layer = gameObject.layer;
			visualObject.transform.SetParent(transform, false);
			visualObject.transform.localScale = ToScale(GetVisualSize());
			var collider = visualObject.GetComponent<Collider>();
			if (collider != null)
				Destroy(collider);

			var renderer = visualObject.GetComponent<MeshRenderer>();
			var material = CreateMaterial();
			renderer.sharedMaterial = material;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			renderer.allowOcclusionWhenDynamic = false;
			renderer.sortingOrder = 1;

			var visual = new BouncingVisual(visualObject, renderer, material);
			ApplyImage(visual);
			ConfigureVideo(visual);
			return visual;
		}

		private void InitializeVisuals() {
			if (m_Camera == null)
				return;

			for (var index = 0; index < m_Visuals.Count; index++) {
				var visual = m_Visuals[index];
				var position = index == 0 ? m_InitialPosition : GetSpawnPosition(index, visual.Renderer);
				visual.Object.transform.position = new Vector3(position.x, position.y, 0f);
				visual.Velocity = GetInitialVelocity(index);
				KeepWithinBounds(visual);
				if (visual.VideoPlayer != null)
					visual.VideoPlayer.Play();
			}
		}

		private void ConfigureVideo(BouncingVisual visual) {
			if (m_Video == null)
				return;

			var player = visual.Object.AddComponent<VideoPlayer>();
			player.source = VideoSource.VideoClip;
			player.clip = m_Video;
			player.renderMode = VideoRenderMode.MaterialOverride;
			player.targetMaterialRenderer = visual.Renderer;
			player.targetMaterialProperty = GetTexturePropertyName(visual.Material);
			player.audioOutputMode = VideoAudioOutputMode.None;
			player.isLooping = true;
			player.playOnAwake = false;
			player.playbackSpeed = m_VideoPlaybackSpeed;
			visual.VideoPlayer = player;
		}

		private void ApplyImage(BouncingVisual visual) {
			if (m_Video != null)
				return;

			visual.Material.SetTexture(GetTexturePropertyName(visual.Material), m_Image == null ? Texture2D.whiteTexture : m_Image);
		}

		private Vector2 GetVisualSize() {
			var size = Mathf.Max(0.01f, m_VisualSize);
			var aspectRatio = GetSourceAspectRatio();
			return aspectRatio >= 1f
				? new Vector2(size, size / aspectRatio)
				: new Vector2(size * aspectRatio, size);
		}

		private float GetSourceAspectRatio() {
			if (m_Video != null && m_Video.width > 0 && m_Video.height > 0)
				return (float)m_Video.width / m_Video.height;
			if (m_Image != null && m_Image.width > 0 && m_Image.height > 0)
				return (float)m_Image.width / m_Image.height;
			return 16f / 9f;
		}

		private static Vector3 ToScale(Vector2 size) {
			return new Vector3(size.x, size.y, 1f);
		}

		private Vector2 GetInitialVelocity(int index) {
			var direction = Quaternion.Euler(0f, 0f, index * 137.5f) * m_InitialDirection;
			return direction.normalized * m_Speed;
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
			ReflectWithinBounds(ref position, ref visual.Velocity, GetMovementBounds(visual.Renderer));
			visual.Object.transform.position = new Vector3(position.x, position.y, 0f);
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
				if (visual.Material != null)
					Destroy(visual.Material);
			}
			m_Visuals.Clear();
		}

		private static Material CreateMaterial() {
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
			return material;
		}

		private static string GetTexturePropertyName(Material material) {
			return material.HasProperty("_BaseMap") ? "_BaseMap" : "_MainTex";
		}

		private sealed class BouncingVisual {
			public GameObject Object { get; }
			public MeshRenderer Renderer { get; }
			public Material Material { get; }
			public VideoPlayer VideoPlayer { get; set; }
			public Vector2 Velocity { get; set; }

			public BouncingVisual(GameObject visualObject, MeshRenderer renderer, Material material) {
				Object = visualObject;
				Renderer = renderer;
				Material = material;
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
