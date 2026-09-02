using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Scene {
	/// <summary>Draws a large, shallow, face-free cuboid around the local XY plane.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class StrokeRectangleScene : MonoBehaviour {
		[Header("Cuboid")]
		[Min(.01f)][SerializeField] private float m_Width = 16f;
		[Min(.01f)][SerializeField] private float m_Height = 9f;
		[Min(.001f)][SerializeField] private float m_Depth = .35f;
		[Min(.001f)][SerializeField] private float m_StrokeWidth = .045f;
		[ColorUsage(false, true)][SerializeField] private Color m_Color = Color.white;

		private LineRenderer[] m_Strokes = Array.Empty<LineRenderer>();
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
			m_Depth = Mathf.Max(.001f, m_Depth);
			m_StrokeWidth = Mathf.Max(.001f, m_StrokeWidth);
			if (!isActiveAndEnabled)
				return;

			EnsureStroke();
			RefreshStroke();
		}

		private void EnsureStroke() {
			if (m_Strokes.Length != 0)
				return;

			m_Material = CreateMaterial();
			m_Strokes = new[] {
				CreateStroke("Front XY Stroke", true),
				CreateStroke("Rear XY Stroke", true),
				CreateStroke("Bottom Z Stroke", false),
				CreateStroke("Right Z Stroke", false),
				CreateStroke("Top Z Stroke", false),
				CreateStroke("Left Z Stroke", false)
			};
		}

		private void RefreshStroke() {
			if (m_Strokes.Length != 6)
				return;

			var halfWidth = m_Width * .5f;
			var halfHeight = m_Height * .5f;
			var halfDepth = m_Depth * .5f;
			var corners = new[] {
				new Vector2(-halfWidth, -halfHeight),
				new Vector2(halfWidth, -halfHeight),
				new Vector2(halfWidth, halfHeight),
				new Vector2(-halfWidth, halfHeight)
			};
			m_Strokes[0].positionCount = 4;
			m_Strokes[0].SetPositions(RectangleAtDepth(corners, -halfDepth));
			m_Strokes[1].positionCount = 4;
			m_Strokes[1].SetPositions(RectangleAtDepth(corners, halfDepth));
			for (var index = 0; index < corners.Length; index++) {
				var stroke = m_Strokes[index + 2];
				stroke.positionCount = 2;
				stroke.SetPositions(new[] {
					new Vector3(corners[index].x, corners[index].y, -halfDepth),
					new Vector3(corners[index].x, corners[index].y, halfDepth)
				});
			}

			for (var index = 0; index < m_Strokes.Length; index++) {
				m_Strokes[index].widthMultiplier = m_StrokeWidth;
				m_Strokes[index].startColor = m_Color;
				m_Strokes[index].endColor = m_Color;
			}
		}

		private LineRenderer CreateStroke(string name, bool loop) {
			var strokeObject = new GameObject(name) {
				layer = gameObject.layer,
				hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave
			};
			strokeObject.transform.SetParent(transform, false);
			var stroke = strokeObject.AddComponent<LineRenderer>();
			stroke.sharedMaterial = m_Material;
			stroke.useWorldSpace = false;
			stroke.loop = loop;
			stroke.alignment = LineAlignment.View;
			stroke.numCornerVertices = 0;
			stroke.numCapVertices = 0;
			stroke.shadowCastingMode = ShadowCastingMode.Off;
			stroke.receiveShadows = false;
			stroke.allowOcclusionWhenDynamic = false;
			return stroke;
		}

		private static Vector3[] RectangleAtDepth(Vector2[] corners, float depth) {
			var positions = new Vector3[corners.Length];
			for (var index = 0; index < corners.Length; index++)
				positions[index] = new Vector3(corners[index].x, corners[index].y, depth);
			return positions;
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
			for (var index = 0; index < m_Strokes.Length; index++)
				if (m_Strokes[index] != null)
					DestroyOwned(m_Strokes[index].gameObject);
			if (m_Material != null)
				DestroyOwned(m_Material);
			m_Strokes = Array.Empty<LineRenderer>();
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
