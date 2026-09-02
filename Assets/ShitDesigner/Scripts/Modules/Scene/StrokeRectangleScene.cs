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

		private MeshFilter m_Filter;
		private MeshRenderer m_Renderer;
		private Mesh m_Mesh;
		private Material m_Material;

		private void OnEnable() {
			EnsureSurface();
			RefreshSurface();
		}

		private void OnDisable() => ReleaseSurface();

		private void OnDestroy() => ReleaseSurface();

		private void OnValidate() {
			m_Width = Mathf.Max(.01f, m_Width);
			m_Height = Mathf.Max(.01f, m_Height);
			m_Depth = Mathf.Max(.001f, m_Depth);
			if (!isActiveAndEnabled)
				return;

			EnsureSurface();
			RefreshSurface();
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
			var halfDepth = m_Depth * .5f;
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
			ApplyColor(m_Material, m_Color);
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
			return material;
		}

		private static void ApplyColor(Material material, Color color) {
			if (material.HasProperty("_BaseColor"))
				material.SetColor("_BaseColor", color);
			else if (material.HasProperty("_Color"))
				material.SetColor("_Color", color);
		}

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
