using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Scene {
	/// <summary>Expands a centered rectangular outline while reducing its line width.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class CenterRectangleExpansion : MonoBehaviour {
		[Header("Motion")]
		[Min(0.05f)][SerializeField] private float m_DurationSeconds = 1.4f;
		[Range(0.001f, 0.25f)][SerializeField] private float m_InitialScale = 0.02f;
		[SerializeField] private bool m_Loop = true;
		[SerializeField] private AnimationCurve m_Easing = CreateEaseOutCurve();

		[Header("Rectangle")]
		[Min(0.01f)][SerializeField] private Vector2 m_TargetSize = new Vector2(8f, 4.5f);
		[Min(0.0001f)][SerializeField] private float m_MaxLineWidth = 0.18f;
		[Min(0.0001f)][SerializeField] private float m_MinLineWidth = 0.018f;

		[Header("Appearance")]
		[ColorUsage(false, true)][SerializeField] private Color m_Color = new Color(0.12f, 0.82f, 1f, 1f);

		private GameObject m_GeneratedObject;
		private Mesh m_Mesh;
		private Material m_Material;
		private MeshRenderer m_Renderer;
		private Vector3[] m_Vertices;
		private double m_ElapsedSeconds;

		private void OnEnable() {
			m_ElapsedSeconds = 0d;
			CreateGeneratedContent();
			ApplyFrame(0f);
		}

		private void Update() {
			if (!Application.isPlaying)
				return;

			m_ElapsedSeconds += Time.unscaledDeltaTime;
			ApplyFrame(GetNormalizedTime());
		}

		private void OnDisable() {
			ReleaseGeneratedContent();
		}

		private void OnDestroy() {
			ReleaseGeneratedContent();
		}

		private void OnValidate() {
			m_DurationSeconds = Mathf.Max(0.05f, m_DurationSeconds);
			m_InitialScale = Mathf.Clamp(m_InitialScale, 0.001f, 0.25f);
			m_TargetSize.x = Mathf.Max(0.01f, m_TargetSize.x);
			m_TargetSize.y = Mathf.Max(0.01f, m_TargetSize.y);
			m_MaxLineWidth = Mathf.Max(0.0001f, m_MaxLineWidth);
			m_MinLineWidth = Mathf.Clamp(m_MinLineWidth, 0.0001f, m_MaxLineWidth);

			if (m_Material != null)
				SetMaterialColor(m_Material, m_Color);
			if (m_Mesh != null && isActiveAndEnabled)
				ApplyFrame(GetNormalizedTime());
		}

		[ContextMenu("Rebuild Rectangle")]
		public void Rebuild() {
			ReleaseGeneratedContent();
			CreateGeneratedContent();
			ApplyFrame(GetNormalizedTime());
		}

		private float GetNormalizedTime() {
			if (m_DurationSeconds <= Mathf.Epsilon)
				return 1f;

			var normalizedTime = (float)(m_ElapsedSeconds / m_DurationSeconds);
			return m_Loop ? Mathf.Repeat(normalizedTime, 1f) : Mathf.Clamp01(normalizedTime);
		}

		private void ApplyFrame(float normalizedTime) {
			if (m_Mesh == null || m_Vertices == null)
				return;

			var easedProgress = EvaluateEasing(Mathf.Clamp01(normalizedTime));
			var size = m_TargetSize * Mathf.Lerp(m_InitialScale, 1f, easedProgress);
			var lineWidth = Mathf.Lerp(m_MaxLineWidth, m_MinLineWidth, easedProgress);
			UpdateOutline(size, lineWidth);
		}

		private float EvaluateEasing(float normalizedTime) {
			if (m_Easing != null && m_Easing.length > 0)
				return Mathf.Clamp01(m_Easing.Evaluate(normalizedTime));

			return 1f - Mathf.Pow(1f - normalizedTime, 3f);
		}

		private void CreateGeneratedContent() {
			m_Material = CreateMaterial();
			SetMaterialColor(m_Material, m_Color);
			m_Mesh = new Mesh {
				name = "Center Rectangle Outline",
				hideFlags = HideFlags.HideAndDontSave
			};
			m_Mesh.MarkDynamic();
			m_Vertices = new Vector3[8];
			m_Mesh.vertices = m_Vertices;
			m_Mesh.triangles = new[] {
				0, 1, 5, 0, 5, 4,
				1, 2, 6, 1, 6, 5,
				2, 3, 7, 2, 7, 6,
				3, 0, 4, 3, 4, 7
			};

			m_GeneratedObject = new GameObject("Center Rectangle") {
				layer = gameObject.layer,
				hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave
			};
			m_GeneratedObject.transform.SetParent(transform, false);

			var filter = m_GeneratedObject.AddComponent<MeshFilter>();
			filter.sharedMesh = m_Mesh;
			m_Renderer = m_GeneratedObject.AddComponent<MeshRenderer>();
			m_Renderer.sharedMaterial = m_Material;
			m_Renderer.shadowCastingMode = ShadowCastingMode.Off;
			m_Renderer.receiveShadows = false;
			m_Renderer.allowOcclusionWhenDynamic = false;
			m_Renderer.sortingOrder = 1;
		}

		private void UpdateOutline(Vector2 size, float lineWidth) {
			var halfWidth = Mathf.Max(0.0001f, size.x * 0.5f);
			var halfHeight = Mathf.Max(0.0001f, size.y * 0.5f);
			var innerHalfWidth = Mathf.Max(0f, halfWidth - lineWidth);
			var innerHalfHeight = Mathf.Max(0f, halfHeight - lineWidth);

			SetRectangleCorners(halfWidth, halfHeight, 0);
			SetRectangleCorners(innerHalfWidth, innerHalfHeight, 4);
			m_Mesh.vertices = m_Vertices;
			m_Mesh.bounds = new Bounds(Vector3.zero, new Vector3(size.x, size.y, 0.1f));
		}

		private void SetRectangleCorners(float halfWidth, float halfHeight, int offset) {
			m_Vertices[offset] = new Vector3(-halfWidth, -halfHeight, 0f);
			m_Vertices[offset + 1] = new Vector3(halfWidth, -halfHeight, 0f);
			m_Vertices[offset + 2] = new Vector3(halfWidth, halfHeight, 0f);
			m_Vertices[offset + 3] = new Vector3(-halfWidth, halfHeight, 0f);
		}

		private static Material CreateMaterial() {
			var shader = Shader.Find("Universal Render Pipeline/Unlit")
				?? Shader.Find("Sprites/Default")
				?? Shader.Find("Unlit/Color");
			if (shader == null)
				throw new InvalidOperationException("An unlit shader is required for the center rectangle scene.");

			var material = new Material(shader) {
				name = "Center Rectangle Material",
				hideFlags = HideFlags.HideAndDontSave
			};
			return material;
		}

		private static void SetMaterialColor(Material material, Color color) {
			if (material.HasProperty("_BaseColor"))
				material.SetColor("_BaseColor", color);
			if (material.HasProperty("_Color"))
				material.SetColor("_Color", color);
		}

		private void ReleaseGeneratedContent() {
			if (m_GeneratedObject != null)
				DestroyOwnedObject(m_GeneratedObject);
			if (m_Material != null)
				DestroyOwnedObject(m_Material);
			if (m_Mesh != null)
				DestroyOwnedObject(m_Mesh);

			m_GeneratedObject = null;
			m_Material = null;
			m_Mesh = null;
			m_Renderer = null;
			m_Vertices = null;
		}

		private static void DestroyOwnedObject(UnityEngine.Object value) {
			if (value == null)
				return;

			if (Application.isPlaying)
				Destroy(value);
			else
				DestroyImmediate(value);
		}

		private static AnimationCurve CreateEaseOutCurve() {
			return new AnimationCurve(
				new Keyframe(0f, 0f, 0f, 3f),
				new Keyframe(1f, 1f, 0f, 0f));
		}
	}
}
