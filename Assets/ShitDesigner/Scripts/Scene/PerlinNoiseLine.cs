using System;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Renders a Z-axis line whose XY displacement is sampled from moving 2D Perlin noise.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	[RequireComponent(typeof(LineRenderer))]
	public sealed class PerlinNoiseLine : MonoBehaviour, ISceneGraphClockReceiver {
		[Header("Line")]
		[Min(2)][SerializeField] private int m_Segments = 256;
		[Min(0.01f)][SerializeField] private float m_Length = 20f;
		[Min(0f)][SerializeField] private float m_Width = 0.04f;

		[Header("Displacement")]
		[Min(0f)][SerializeField] private Vector2 m_Displacement = new Vector2(2f, 2f);
		[Min(0.0001f)][SerializeField] private float m_NoiseScale = 0.18f;
		[SerializeField] private Vector2 m_NoiseSeed = new Vector2(17.3f, 41.7f);
		[Min(0.0001f)][SerializeField] private float m_NoiseChannelOffset = 53.1f;

		[Header("Noise Motion")]
		[Min(0f)][SerializeField] private float m_NoiseSpeed = 0.8f;
		[SerializeField] private float m_InitialNoiseTravelZ;

		[Header("Appearance")]
		[SerializeField] private Material m_Material;
		[ColorUsage(true, true)][SerializeField] private Color m_Color = new Color(0.15f, 0.85f, 1f, 1f);

		private LineRenderer m_LineRenderer;
		private Material m_GeneratedMaterial;
		private Vector3[] m_LinePositions = Array.Empty<Vector3>();
		private float m_NoiseTravelZ;
		private bool m_GraphClockDriven;

		private void OnEnable() {
			m_GraphClockDriven = false;
			m_NoiseTravelZ = m_InitialNoiseTravelZ;
			ConfigureLine();
			UpdateLine();
		}

		private void Update() {
			if (Application.isPlaying && !m_GraphClockDriven)
				Advance(Time.deltaTime);
		}

		private void OnDisable() {
			ReleaseGeneratedMaterial();
			m_LineRenderer = null;
		}

		private void OnDestroy() {
			ReleaseGeneratedMaterial();
		}

		private void OnValidate() {
			m_Segments = Mathf.Clamp(m_Segments, 2, 4096);
			m_Length = Mathf.Max(0.01f, m_Length);
			m_Width = Mathf.Max(0f, m_Width);
			m_Displacement.x = Mathf.Max(0f, m_Displacement.x);
			m_Displacement.y = Mathf.Max(0f, m_Displacement.y);
			m_NoiseScale = Mathf.Max(0.0001f, m_NoiseScale);
			m_NoiseChannelOffset = Mathf.Max(0.0001f, m_NoiseChannelOffset);
			m_NoiseSpeed = Mathf.Max(0f, m_NoiseSpeed);

			if (!Application.isPlaying) {
				m_NoiseTravelZ = m_InitialNoiseTravelZ;
				ConfigureLine();
				UpdateLine();
			}
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			m_GraphClockDriven = graphClockDriven;
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!m_GraphClockDriven || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds <= 0d)
				return;

			Advance((float)Math.Min(deltaSeconds, float.MaxValue));
		}

		[ContextMenu("Reset Noise Position")]
		public void ResetNoisePosition() {
			m_NoiseTravelZ = m_InitialNoiseTravelZ;
			UpdateLine();
		}

		private void Advance(float deltaSeconds) {
			if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
				return;

			m_NoiseTravelZ += deltaSeconds * m_NoiseSpeed;
			UpdateLine();
		}

		private void ConfigureLine() {
			if (m_LineRenderer == null)
				m_LineRenderer = GetComponent<LineRenderer>();
			if (m_LineRenderer == null)
				return;

			m_LineRenderer.useWorldSpace = false;
			m_LineRenderer.loop = false;
			m_LineRenderer.positionCount = m_Segments + 1;
			m_LineRenderer.startWidth = m_Width;
			m_LineRenderer.endWidth = m_Width;
			m_LineRenderer.startColor = m_Color;
			m_LineRenderer.endColor = m_Color;
			ApplyMaterial();
		}

		private void UpdateLine() {
			if (m_LineRenderer == null)
				m_LineRenderer = GetComponent<LineRenderer>();
			if (m_LineRenderer == null)
				return;

			var pointCount = m_Segments + 1;
			if (m_LinePositions.Length != pointCount)
				m_LinePositions = new Vector3[pointCount];

			var halfLength = m_Length * 0.5f;
			for (var index = 0; index < pointCount; index++) {
				var normalized = index / (float)m_Segments;
				var z = Mathf.Lerp(-halfLength, halfLength, normalized);
				var displacement = SampleDisplacement(z);
				m_LinePositions[index] = new Vector3(displacement.x, displacement.y, z);
			}

			m_LineRenderer.positionCount = pointCount;
			m_LineRenderer.SetPositions(m_LinePositions);
		}

		private Vector2 SampleDisplacement(float z) {
			// Subtracting the travel distance makes the sampled pattern move in
			// the positive Z direction while every point keeps its original Z.
			var noiseCoordinate = (z - m_NoiseTravelZ) * m_NoiseScale + m_NoiseSeed.x;
			var noiseX = Mathf.PerlinNoise(noiseCoordinate, m_NoiseSeed.y) * 2f - 1f;
			var noiseY = Mathf.PerlinNoise(
				noiseCoordinate + m_NoiseChannelOffset,
				m_NoiseSeed.y + m_NoiseChannelOffset) * 2f - 1f;
			return new Vector2(noiseX * m_Displacement.x, noiseY * m_Displacement.y);
		}

		private void ApplyMaterial() {
			if (m_Material != null) {
				ReleaseGeneratedMaterial();
				m_LineRenderer.sharedMaterial = m_Material;
				return;
			}

			if (m_GeneratedMaterial == null) {
				var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
				if (shader == null)
					return;

				m_GeneratedMaterial = new Material(shader) {
					name = "Perlin Noise Line",
					hideFlags = HideFlags.HideAndDontSave
				};
			}

			if (m_GeneratedMaterial.HasProperty("_BaseColor"))
				m_GeneratedMaterial.SetColor("_BaseColor", m_Color);
			if (m_GeneratedMaterial.HasProperty("_Color"))
				m_GeneratedMaterial.SetColor("_Color", m_Color);
			m_LineRenderer.sharedMaterial = m_GeneratedMaterial;
		}

		private void ReleaseGeneratedMaterial() {
			if (m_GeneratedMaterial == null)
				return;

			if (Application.isPlaying)
				Destroy(m_GeneratedMaterial);
			else
				DestroyImmediate(m_GeneratedMaterial);
			m_GeneratedMaterial = null;
		}
	}
}
