using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Scene {
	/// <summary>Draws one large, unfilled rectangle on the local XY plane.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class StrokeRectangleScene : MonoBehaviour {
		[Header("Rectangle")]
		[Min(.01f)][SerializeField] private float m_Width = 16f;
		[Min(.01f)][SerializeField] private float m_Height = 9f;
		[Min(.001f)][SerializeField] private float m_StrokeWidth = .045f;
		[ColorUsage(false, true)][SerializeField] private Color m_Color = Color.white;

		private LineRenderer m_Stroke;
		private Material m_Material;

		private void OnEnable() {
			EnsureStroke();
			RefreshStroke();
		}

		private void OnDisable() => ReleaseStroke();

		private void OnDestroy() => ReleaseStroke();

		private void OnValidate() {
			m_Width = Mathf.Max(.01f, m_Width);
			m_Height = Mathf.Max(.01f, m_Height);
			m_StrokeWidth = Mathf.Max(.001f, m_StrokeWidth);
			if (!isActiveAndEnabled)
				return;

			EnsureStroke();
			RefreshStroke();
		}

		private void EnsureStroke() {
			if (m_Stroke != null)
				return;

			var strokeObject = new GameObject("XY Stroke") {
				layer = gameObject.layer,
				hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave
			};
			strokeObject.transform.SetParent(transform, false);
			m_Stroke = strokeObject.AddComponent<LineRenderer>();
			m_Material = CreateMaterial();
			m_Stroke.sharedMaterial = m_Material;
			m_Stroke.useWorldSpace = false;
			m_Stroke.loop = true;
			m_Stroke.alignment = LineAlignment.TransformZ;
			m_Stroke.numCornerVertices = 0;
			m_Stroke.numCapVertices = 0;
			m_Stroke.shadowCastingMode = ShadowCastingMode.Off;
			m_Stroke.receiveShadows = false;
			m_Stroke.allowOcclusionWhenDynamic = false;
		}

		private void RefreshStroke() {
			if (m_Stroke == null)
				return;

			var halfWidth = m_Width * .5f;
			var halfHeight = m_Height * .5f;
			m_Stroke.positionCount = 4;
			m_Stroke.SetPositions(new[] {
				new Vector3(-halfWidth, -halfHeight, 0f),
				new Vector3(halfWidth, -halfHeight, 0f),
				new Vector3(halfWidth, halfHeight, 0f),
				new Vector3(-halfWidth, halfHeight, 0f)
			});
			m_Stroke.widthMultiplier = m_StrokeWidth;
			m_Stroke.startColor = m_Color;
			m_Stroke.endColor = m_Color;
		}

		private static Material CreateMaterial() {
			var shader = Shader.Find("Universal Render Pipeline/Unlit");
			if (shader == null)
				shader = Shader.Find("Sprites/Default");
			if (shader == null)
				shader = Shader.Find("Unlit/Color");
			if (shader == null)
				throw new InvalidOperationException("An unlit shader is required for the rectangle stroke.");

			var material = new Material(shader) { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
			if (material.HasProperty("_Cull"))
				material.SetInt("_Cull", (int)CullMode.Off);
			return material;
		}

		private void ReleaseStroke() {
			if (m_Stroke != null)
				DestroyOwned(m_Stroke.gameObject);
			if (m_Material != null)
				DestroyOwned(m_Material);
			m_Stroke = null;
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
