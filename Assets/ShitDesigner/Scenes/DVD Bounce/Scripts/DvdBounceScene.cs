using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Video;

namespace ShitDesigner.Scene {
	/// <summary>Moves an image or video surface within the orthographic camera bounds and changes its tint at each impact.</summary>
	[DisallowMultipleComponent]
	public sealed class DvdBounceScene : MonoBehaviour {
		[Header("Visual")]
		[SerializeField] private Texture2D m_Image;
		[SerializeField] private VideoClip m_Video;
		[Min(0.01f)][SerializeField] private Vector2 m_VisualSize = new Vector2(2.8f, 1.6f);

		[Header("Motion")]
		[Min(0.01f)][SerializeField] private float m_Speed = 4.5f;
		[SerializeField] private Vector2 m_InitialDirection = new Vector2(1f, 0.63f);
		[SerializeField] private Vector2 m_InitialPosition = Vector2.zero;

		[Header("Appearance")]
		[ColorUsage(false, true)][SerializeField] private Color[] m_Colors = {
			new Color(1f, 0.22f, 0.36f, 1f), new Color(0.2f, 0.9f, 1f, 1f),
			new Color(1f, 0.78f, 0.16f, 1f), new Color(0.5f, 1f, 0.35f, 1f),
			new Color(0.82f, 0.36f, 1f, 1f)
		};

		private Camera m_Camera;
		private GameObject m_VisualObject;
		private Material m_Material;
		private MeshRenderer m_Renderer;
		private VideoPlayer m_VideoPlayer;
		private Vector2 m_Velocity;
		private int m_ColorIndex;

		private void Awake() {
			m_Camera = Camera.main;
			if (m_Camera == null)
				m_Camera = FindFirstObjectByType<Camera>();
			CreateVisual();
		}

		private void Start() {
			transform.position = new Vector3(m_InitialPosition.x, m_InitialPosition.y, 0f);
			m_Velocity = GetInitialVelocity();
			ApplyColor();
			KeepWithinBounds();
			if (m_VideoPlayer != null)
				m_VideoPlayer.Play();
		}

		private void Update() {
			if (m_Camera == null || m_Renderer == null)
				return;

			var position = (Vector2)transform.position + m_Velocity * Time.unscaledDeltaTime;
			var bounds = GetMovementBounds();
			var impacted = ReflectWithinBounds(ref position, ref m_Velocity, bounds);
			transform.position = new Vector3(position.x, position.y, 0f);
			if (impacted)
				AdvanceColor();
		}

		private void OnDestroy() {
			ReleaseVisual();
		}

		private void OnValidate() {
			m_VisualSize.x = Mathf.Max(0.01f, m_VisualSize.x);
			m_VisualSize.y = Mathf.Max(0.01f, m_VisualSize.y);
			m_Speed = Mathf.Max(0.01f, m_Speed);
			if (m_InitialDirection.sqrMagnitude < 0.0001f)
				m_InitialDirection = new Vector2(1f, 0.63f);
			if (m_VisualObject != null) {
				m_VisualObject.transform.localScale = new Vector3(m_VisualSize.x, m_VisualSize.y, 1f);
				ApplyImage();
				ApplyColor();
			}
		}

		private void CreateVisual() {
			ReleaseVisual();
			m_VisualObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
			m_VisualObject.name = "Bouncing Visual";
			m_VisualObject.transform.SetParent(transform, false);
			m_VisualObject.transform.localScale = new Vector3(m_VisualSize.x, m_VisualSize.y, 1f);
			var collider = m_VisualObject.GetComponent<Collider>();
			if (collider != null)
				Destroy(collider);

			m_Renderer = m_VisualObject.GetComponent<MeshRenderer>();
			m_Material = CreateMaterial();
			m_Renderer.sharedMaterial = m_Material;
			m_Renderer.shadowCastingMode = ShadowCastingMode.Off;
			m_Renderer.receiveShadows = false;
			m_Renderer.allowOcclusionWhenDynamic = false;
			m_Renderer.sortingOrder = 1;
			ApplyImage();
			ConfigureVideo();
		}

		private void ConfigureVideo() {
			if (m_Video == null)
				return;

			m_VideoPlayer = m_VisualObject.AddComponent<VideoPlayer>();
			m_VideoPlayer.source = VideoSource.VideoClip;
			m_VideoPlayer.clip = m_Video;
			m_VideoPlayer.renderMode = VideoRenderMode.MaterialOverride;
			m_VideoPlayer.targetMaterialRenderer = m_Renderer;
			m_VideoPlayer.targetMaterialProperty = GetTexturePropertyName(m_Material);
			m_VideoPlayer.audioOutputMode = VideoAudioOutputMode.None;
			m_VideoPlayer.isLooping = true;
			m_VideoPlayer.playOnAwake = false;
		}

		private void ApplyImage() {
			if (m_Material == null || m_Video != null)
				return;

			m_Material.SetTexture(GetTexturePropertyName(m_Material), m_Image == null ? Texture2D.whiteTexture : m_Image);
		}

		private Vector2 GetInitialVelocity() {
			return m_InitialDirection.normalized * m_Speed;
		}

		private Bounds2D GetMovementBounds() {
			var cameraHalfHeight = m_Camera.orthographicSize;
			var cameraHalfWidth = cameraHalfHeight * m_Camera.aspect;
			var visualExtents = m_Renderer.bounds.extents;
			return new Bounds2D(
				new Vector2(-cameraHalfWidth + visualExtents.x, -cameraHalfHeight + visualExtents.y),
				new Vector2(cameraHalfWidth - visualExtents.x, cameraHalfHeight - visualExtents.y));
		}

		private void KeepWithinBounds() {
			if (m_Camera == null || m_Renderer == null)
				return;

			var position = (Vector2)transform.position;
			var bounds = GetMovementBounds();
			ReflectWithinBounds(ref position, ref m_Velocity, bounds);
			transform.position = new Vector3(position.x, position.y, 0f);
		}

		private static bool ReflectWithinBounds(ref Vector2 position, ref Vector2 velocity, Bounds2D bounds) {
			var impacted = false;
			if (position.x < bounds.Minimum.x || position.x > bounds.Maximum.x) {
				position.x = Mathf.Clamp(position.x, bounds.Minimum.x, bounds.Maximum.x);
				velocity.x = -velocity.x;
				impacted = true;
			}
			if (position.y < bounds.Minimum.y || position.y > bounds.Maximum.y) {
				position.y = Mathf.Clamp(position.y, bounds.Minimum.y, bounds.Maximum.y);
				velocity.y = -velocity.y;
				impacted = true;
			}
			return impacted;
		}

		private void AdvanceColor() {
			if (m_Colors == null || m_Colors.Length == 0)
				return;

			m_ColorIndex = (m_ColorIndex + 1) % m_Colors.Length;
			ApplyColor();
		}

		private void ApplyColor() {
			if (m_Material == null || m_Colors == null || m_Colors.Length == 0)
				return;

			m_ColorIndex = Mathf.Clamp(m_ColorIndex, 0, m_Colors.Length - 1);
			SetMaterialColor(m_Material, m_Colors[m_ColorIndex]);
		}

		private void ReleaseVisual() {
			if (m_VisualObject != null)
				Destroy(m_VisualObject);
			if (m_Material != null)
				Destroy(m_Material);
			m_VisualObject = null;
			m_Material = null;
			m_Renderer = null;
			m_VideoPlayer = null;
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

		private static void SetMaterialColor(Material material, Color color) {
			if (material.HasProperty("_BaseColor"))
				material.SetColor("_BaseColor", color);
			if (material.HasProperty("_Color"))
				material.SetColor("_Color", color);
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
