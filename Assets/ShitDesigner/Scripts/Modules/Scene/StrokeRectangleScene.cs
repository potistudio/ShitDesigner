using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Scene {
	/// <summary>Draws a shallow cuboid using only its four side surfaces.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class StrokeRectangleScene : MonoBehaviour {
		[Header("Cuboid")]
		[Min(.01f)][SerializeField] private float m_Width = 16f;
		[Min(.01f)][SerializeField] private float m_Height = 9f;
		[Min(.001f)][SerializeField] private float m_Depth = .35f;
		[ColorUsage(false, true)][SerializeField] private Color m_Color = Color.white;
		[Range(0f, 1f)][SerializeField] private float m_Opacity = 1f;
		[Header("Trigger Motion")]
		[Min(0f)][SerializeField] private float m_UpDistance = 2f;
		[Min(.001f)][SerializeField] private float m_Duration = .5f;
		[SerializeField] private AnimationCurve m_EaseOut = CreateEaseOutCurve();
		[Header("Trigger Appearance")]
		[Range(0f, 1f)][SerializeField] private float m_TriggerStartOpacity;
		[Min(.001f)][SerializeField] private float m_TriggerStartDepth = .001f;
		[SerializeField] private bool m_TriggerOnColliderEnter = true;
		[SerializeField] private bool m_ResetToOriginOnTrigger = true;

		private MeshFilter m_Filter;
		private MeshRenderer m_Renderer;
		private Mesh m_Mesh;
		private Material m_Material;
		private Vector3 m_OriginLocalPosition;
		private Vector3 m_MotionStartPosition;
		private Vector3 m_MotionEndPosition;
		private float m_MotionStartOpacity;
		private float m_MotionEndOpacity;
		private float m_MotionStartDepth;
		private float m_MotionEndDepth;
		private float m_CurrentOpacity;
		private float m_CurrentDepth;
		private float m_MotionElapsed;
		private bool m_HasOrigin;
		private bool m_IsMoving;

		private void OnEnable() {
			CaptureOrigin();
			m_CurrentOpacity = m_Opacity;
			m_CurrentDepth = m_Depth;
			EnsureSurface();
			RefreshSurface();
		}

		private void OnDisable() {
			m_IsMoving = false;
			m_HasOrigin = false;
			ReleaseSurface();
		}

		private void OnDestroy() => ReleaseSurface();

		private void OnValidate() {
			m_Width = Mathf.Max(.01f, m_Width);
			m_Height = Mathf.Max(.01f, m_Height);
			m_Depth = Mathf.Max(.001f, m_Depth);
			m_Opacity = Mathf.Clamp01(m_Opacity);
			m_TriggerStartOpacity = Mathf.Clamp01(m_TriggerStartOpacity);
			m_TriggerStartDepth = Mathf.Max(.001f, m_TriggerStartDepth);
			m_UpDistance = Mathf.Max(0f, m_UpDistance);
			m_Duration = Mathf.Max(.001f, m_Duration);
			m_EaseOut ??= CreateEaseOutCurve();
			if (!Application.isPlaying || !m_IsMoving) {
				m_CurrentOpacity = m_Opacity;
				m_CurrentDepth = m_Depth;
			}

			if (!isActiveAndEnabled || m_Filter == null)
				return;

			RefreshSurface();
		}

		private void Update() {
			if (!Application.isPlaying || !m_IsMoving)
				return;

			m_MotionElapsed += Time.unscaledDeltaTime;
			var progress = Mathf.Clamp01(m_MotionElapsed / m_Duration);
			var easedProgress = Mathf.Clamp01(m_EaseOut.Evaluate(progress));
			transform.localPosition = Vector3.LerpUnclamped(m_MotionStartPosition, m_MotionEndPosition, easedProgress);
			m_CurrentOpacity = Mathf.LerpUnclamped(m_MotionStartOpacity, m_MotionEndOpacity, easedProgress);
			m_CurrentDepth = Mathf.LerpUnclamped(m_MotionStartDepth, m_MotionEndDepth, easedProgress);
			RefreshSurface();
			if (progress >= 1f)
				m_IsMoving = false;
		}

		private void OnTriggerEnter(Collider other) {
			if (m_TriggerOnColliderEnter)
				TriggerMoveUp();
		}

		/// <summary>Starts one upward ease-out motion. This is suitable for a UnityEvent trigger.</summary>
		[ContextMenu("Trigger Up / Fade / Depth")]
		public void TriggerMoveUp() {
			CaptureOrigin();
			m_MotionStartPosition = m_ResetToOriginOnTrigger ? m_OriginLocalPosition : transform.localPosition;
			transform.localPosition = m_MotionStartPosition;
			m_MotionEndPosition = m_MotionStartPosition + Vector3.up * m_UpDistance;
			m_MotionStartOpacity = m_TriggerStartOpacity;
			m_MotionEndOpacity = m_Opacity;
			m_MotionStartDepth = m_TriggerStartDepth;
			m_MotionEndDepth = m_Depth;
			m_CurrentOpacity = m_MotionStartOpacity;
			m_CurrentDepth = m_MotionStartDepth;
			m_MotionElapsed = 0f;
			m_IsMoving = true;
			RefreshSurface();
		}

		private void CaptureOrigin() {
			if (m_HasOrigin)
				return;

			m_OriginLocalPosition = transform.localPosition;
			m_HasOrigin = true;
		}

		private void EnsureSurface() {
			if (m_Filter != null)
				return;

			var surfaceObject = new GameObject("Open Cuboid Surfaces") {
				layer = gameObject.layer,
				hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave
			};
			surfaceObject.transform.SetParent(transform, false);
			m_Filter = surfaceObject.AddComponent<MeshFilter>();
			m_Renderer = surfaceObject.AddComponent<MeshRenderer>();
			m_Mesh = new Mesh { name = "ShitDesigner.OpenCuboidSideSurfaces" };
			m_Material = CreateMaterial();
			m_Filter.sharedMesh = m_Mesh;
			m_Renderer.sharedMaterial = m_Material;
			m_Renderer.shadowCastingMode = ShadowCastingMode.Off;
			m_Renderer.receiveShadows = false;
			m_Renderer.allowOcclusionWhenDynamic = false;
		}

		private void RefreshSurface() {
			if (m_Mesh == null || m_Material == null)
				return;

			var halfWidth = m_Width * .5f;
			var halfHeight = m_Height * .5f;
			var halfDepth = m_CurrentDepth * .5f;
			m_Mesh.Clear();
			m_Mesh.vertices = new[] {
				// Bottom: normal -Y.
				new Vector3(-halfWidth, -halfHeight, -halfDepth), new Vector3(halfWidth, -halfHeight, -halfDepth),
				new Vector3(halfWidth, -halfHeight, halfDepth), new Vector3(-halfWidth, -halfHeight, halfDepth),
				// Right: normal +X.
				new Vector3(halfWidth, -halfHeight, -halfDepth), new Vector3(halfWidth, halfHeight, -halfDepth),
				new Vector3(halfWidth, halfHeight, halfDepth), new Vector3(halfWidth, -halfHeight, halfDepth),
				// Top: normal +Y.
				new Vector3(halfWidth, halfHeight, -halfDepth), new Vector3(-halfWidth, halfHeight, -halfDepth),
				new Vector3(-halfWidth, halfHeight, halfDepth), new Vector3(halfWidth, halfHeight, halfDepth),
				// Left: normal -X.
				new Vector3(-halfWidth, halfHeight, -halfDepth), new Vector3(-halfWidth, -halfHeight, -halfDepth),
				new Vector3(-halfWidth, -halfHeight, halfDepth), new Vector3(-halfWidth, halfHeight, halfDepth)
			};
			m_Mesh.triangles = new[] {
				0, 1, 2, 2, 3, 0,
				4, 5, 6, 6, 7, 4,
				8, 9, 10, 10, 11, 8,
				12, 13, 14, 14, 15, 12
			};
			m_Mesh.RecalculateNormals();
			m_Mesh.RecalculateBounds();
			ApplyColor(m_Material, m_Color, m_CurrentOpacity);
		}

		private static Material CreateMaterial() {
			var shader = Shader.Find("Universal Render Pipeline/Unlit");
			if (shader == null)
				shader = Shader.Find("Unlit/Color");
			if (shader == null)
				throw new InvalidOperationException("An unlit shader is required for the open cuboid surfaces.");

			var material = new Material(shader) { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
			if (material.HasProperty("_Cull"))
				material.SetInt("_Cull", (int)CullMode.Off);
			ConfigureTransparency(material);
			return material;
		}

		private static void ApplyColor(Material material, Color color, float opacity) {
			color.a *= opacity;
			if (material.HasProperty("_BaseColor"))
				material.SetColor("_BaseColor", color);
			else if (material.HasProperty("_Color"))
				material.SetColor("_Color", color);
		}

		private static void ConfigureTransparency(Material material) {
			if (!material.HasProperty("_Surface"))
				return;

			material.SetFloat("_Surface", 1f);
			if (material.HasProperty("_SrcBlend"))
				material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
			if (material.HasProperty("_DstBlend"))
				material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
			if (material.HasProperty("_ZWrite"))
				material.SetInt("_ZWrite", 0);
			material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			material.renderQueue = (int)RenderQueue.Transparent;
		}

		private static AnimationCurve CreateEaseOutCurve() => new AnimationCurve(
			new Keyframe(0f, 0f, 0f, 2f),
			new Keyframe(1f, 1f, 0f, 0f));

		private void ReleaseSurface() {
			if (m_Filter != null)
				DestroyOwned(m_Filter.gameObject);
			if (m_Mesh != null)
				DestroyOwned(m_Mesh);
			if (m_Material != null)
				DestroyOwned(m_Material);
			m_Filter = null;
			m_Renderer = null;
			m_Mesh = null;
			m_Material = null;
		}

		private static void DestroyOwned(UnityEngine.Object value) {
			if (Application.isPlaying)
				Destroy(value);
			else
				DestroyImmediate(value);
		}
	}
}
