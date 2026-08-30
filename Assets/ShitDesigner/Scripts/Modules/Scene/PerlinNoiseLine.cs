using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Scene {
	/// <summary>Generates a tubular Z-axis line whose XY displacement is sampled from moving 2D Perlin noise.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public sealed class PerlinNoiseLine : MonoBehaviour {
		[Header("Line")]
		[Min(2)][SerializeField] private int m_Segments = 256;
		[Min(0.01f)][SerializeField] private float m_Length = 20f;
		[Min(0.0001f)][SerializeField] private float m_Width = 0.04f;
		[Range(3, 32)][SerializeField] private int m_RadialSegments = 8;

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

		private MeshFilter m_MeshFilter;
		private MeshRenderer m_MeshRenderer;
		private Mesh m_GeneratedMesh;
		private Material m_GeneratedMaterial;
		private Vector3[] m_CenterlinePositions = Array.Empty<Vector3>();
		private Vector3[] m_MeshVertices = Array.Empty<Vector3>();
		private Vector3[] m_MeshNormals = Array.Empty<Vector3>();
		private float m_NoiseTravelZ;
		private bool m_GraphClockDriven;

		private void OnEnable() {
			var clockController = GetComponentInParent<PerlinNoiseLineClockController>();
			m_GraphClockDriven = clockController != null && clockController.isActiveAndEnabled;
			m_NoiseTravelZ = m_InitialNoiseTravelZ;
			RebuildMesh();
		}

		private void Update() {
			if (UnityEngine.Application.isPlaying && !m_GraphClockDriven)
				Advance(Time.deltaTime);
		}

		private void OnDisable() {
			m_GraphClockDriven = false;
			ReleaseGeneratedMaterial();
			ReleaseGeneratedMesh();
			m_MeshFilter = null;
			m_MeshRenderer = null;
		}

		private void OnDestroy() {
			ReleaseGeneratedMaterial();
			ReleaseGeneratedMesh();
		}

		private void OnValidate() {
			m_Segments = Mathf.Clamp(m_Segments, 2, 4096);
			m_Length = Mathf.Max(0.01f, m_Length);
			m_Width = Mathf.Max(0.0001f, m_Width);
			m_RadialSegments = Mathf.Clamp(m_RadialSegments, 3, 32);
			m_Displacement.x = Mathf.Max(0f, m_Displacement.x);
			m_Displacement.y = Mathf.Max(0f, m_Displacement.y);
			m_NoiseScale = Mathf.Max(0.0001f, m_NoiseScale);
			m_NoiseChannelOffset = Mathf.Max(0.0001f, m_NoiseChannelOffset);
			m_NoiseSpeed = Mathf.Max(0f, m_NoiseSpeed);

			if (!UnityEngine.Application.isPlaying) {
				m_NoiseTravelZ = m_InitialNoiseTravelZ;
				if (isActiveAndEnabled)
					RebuildMesh();
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
			UpdateMesh();
		}

		[ContextMenu("Rebuild Noise Line")]
		public void RebuildMesh() {
			CacheComponents();
			if (m_MeshFilter == null || m_MeshRenderer == null)
				return;

			ReleaseGeneratedMesh();
			AllocateMeshBuffers();
			m_GeneratedMesh = BuildMeshTopology();
			m_GeneratedMesh.MarkDynamic();
			m_MeshFilter.sharedMesh = m_GeneratedMesh;
			ApplyMaterial();
			UpdateMesh();
		}

		private void Advance(float deltaSeconds) {
			if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
				return;

			m_NoiseTravelZ += deltaSeconds * m_NoiseSpeed;
			UpdateMesh();
		}

		private void CacheComponents() {
			if (m_MeshFilter == null)
				m_MeshFilter = GetComponent<MeshFilter>();
			if (m_MeshRenderer == null)
				m_MeshRenderer = GetComponent<MeshRenderer>();
		}

		private void AllocateMeshBuffers() {
			var pointCount = m_Segments + 1;
			var vertexCount = pointCount * m_RadialSegments + 2;
			m_CenterlinePositions = new Vector3[pointCount];
			m_MeshVertices = new Vector3[vertexCount];
			m_MeshNormals = new Vector3[vertexCount];
		}

		private Mesh BuildMeshTopology() {
			var pointCount = m_Segments + 1;
			var vertexCount = pointCount * m_RadialSegments + 2;
			var triangleCount = m_Segments * m_RadialSegments * 2 + m_RadialSegments * 2;
			var triangles = new int[triangleCount * 3];
			var triangleIndex = 0;

			for (var point = 0; point < m_Segments; point++) {
				var lowerRing = point * m_RadialSegments;
				var upperRing = lowerRing + m_RadialSegments;
				for (var radial = 0; radial < m_RadialSegments; radial++) {
					var nextRadial = (radial + 1) % m_RadialSegments;
					var lowerCurrent = lowerRing + radial;
					var lowerNext = lowerRing + nextRadial;
					var upperCurrent = upperRing + radial;
					var upperNext = upperRing + nextRadial;
					AddTriangle(triangles, ref triangleIndex, lowerCurrent, upperCurrent, lowerNext);
					AddTriangle(triangles, ref triangleIndex, lowerNext, upperCurrent, upperNext);
				}
			}

			var startCenter = pointCount * m_RadialSegments;
			var endCenter = startCenter + 1;
			var startRing = 0;
			var endRing = (pointCount - 1) * m_RadialSegments;
			for (var radial = 0; radial < m_RadialSegments; radial++) {
				var nextRadial = (radial + 1) % m_RadialSegments;
				AddTriangle(triangles, ref triangleIndex, startCenter, startRing + radial, startRing + nextRadial);
				AddTriangle(triangles, ref triangleIndex, endCenter, endRing + nextRadial, endRing + radial);
			}

			var mesh = new Mesh {
				name = "Perlin Noise Line",
				hideFlags = HideFlags.HideAndDontSave
			};
			if (vertexCount > 65535)
				mesh.indexFormat = IndexFormat.UInt32;
			mesh.vertices = m_MeshVertices;
			mesh.normals = m_MeshNormals;
			mesh.triangles = triangles;
			mesh.RecalculateBounds();
			return mesh;
		}

		private void UpdateMesh() {
			CacheComponents();
			if (m_GeneratedMesh == null || m_MeshFilter == null)
				return;

			var expectedPointCount = m_Segments + 1;
			var expectedVertexCount = expectedPointCount * m_RadialSegments + 2;
			if (m_CenterlinePositions.Length != expectedPointCount || m_MeshVertices.Length != expectedVertexCount
				|| m_MeshNormals.Length != expectedVertexCount) {
				RebuildMesh();
				return;
			}

			UpdateCenterline();
			var radius = m_Width * 0.5f;
			for (var point = 0; point < expectedPointCount; point++) {
				var tangent = GetTangent(point);
				CalculateFrame(tangent, out var axisA, out var axisB);
				for (var radial = 0; radial < m_RadialSegments; radial++) {
					var angle = radial * Mathf.PI * 2f / m_RadialSegments;
					var radialNormal = (axisA * Mathf.Cos(angle) + axisB * Mathf.Sin(angle)).normalized;
					var vertexIndex = point * m_RadialSegments + radial;
					m_MeshVertices[vertexIndex] = m_CenterlinePositions[point] + radialNormal * radius;
					m_MeshNormals[vertexIndex] = radialNormal;
				}
			}

			var startCenter = expectedPointCount * m_RadialSegments;
			var endCenter = startCenter + 1;
			m_MeshVertices[startCenter] = m_CenterlinePositions[0];
			m_MeshVertices[endCenter] = m_CenterlinePositions[expectedPointCount - 1];
			m_MeshNormals[startCenter] = -GetTangent(0);
			m_MeshNormals[endCenter] = GetTangent(expectedPointCount - 1);

			m_GeneratedMesh.vertices = m_MeshVertices;
			m_GeneratedMesh.normals = m_MeshNormals;
			m_GeneratedMesh.RecalculateBounds();
		}

		private void UpdateCenterline() {
			var halfLength = m_Length * 0.5f;
			for (var index = 0; index < m_CenterlinePositions.Length; index++) {
				var normalized = index / (float)m_Segments;
				var z = Mathf.Lerp(-halfLength, halfLength, normalized);
				var displacement = SampleDisplacement(z);
				m_CenterlinePositions[index] = new Vector3(displacement.x, displacement.y, z);
			}
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

		private Vector3 GetTangent(int point) {
			var lastPoint = m_CenterlinePositions.Length - 1;
			Vector3 tangent;
			if (point == 0)
				tangent = m_CenterlinePositions[1] - m_CenterlinePositions[0];
			else if (point == lastPoint)
				tangent = m_CenterlinePositions[lastPoint] - m_CenterlinePositions[lastPoint - 1];
			else
				tangent = m_CenterlinePositions[point + 1] - m_CenterlinePositions[point - 1];

			return tangent.sqrMagnitude <= 0.000001f ? Vector3.forward : tangent.normalized;
		}

		private static void CalculateFrame(Vector3 tangent, out Vector3 axisA, out Vector3 axisB) {
			var reference = Mathf.Abs(Vector3.Dot(tangent, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
			axisA = Vector3.Cross(tangent, reference).normalized;
			axisB = Vector3.Cross(axisA, tangent).normalized;
		}

		private void ApplyMaterial() {
			if (m_MeshRenderer == null)
				return;

			if (m_Material != null) {
				ReleaseGeneratedMaterial();
				m_MeshRenderer.sharedMaterial = m_Material;
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
			m_MeshRenderer.sharedMaterial = m_GeneratedMaterial;
		}

		private void ReleaseGeneratedMesh() {
			if (m_MeshFilter != null && m_MeshFilter.sharedMesh == m_GeneratedMesh)
				m_MeshFilter.sharedMesh = null;
			if (m_GeneratedMesh == null)
				return;

			if (UnityEngine.Application.isPlaying)
				Destroy(m_GeneratedMesh);
			else
				DestroyImmediate(m_GeneratedMesh);
			m_GeneratedMesh = null;
			m_MeshVertices = Array.Empty<Vector3>();
			m_MeshNormals = Array.Empty<Vector3>();
		}

		private void ReleaseGeneratedMaterial() {
			if (m_GeneratedMaterial == null)
				return;

			if (m_MeshRenderer != null && m_MeshRenderer.sharedMaterial == m_GeneratedMaterial)
				m_MeshRenderer.sharedMaterial = null;
			if (UnityEngine.Application.isPlaying)
				Destroy(m_GeneratedMaterial);
			else
				DestroyImmediate(m_GeneratedMaterial);
			m_GeneratedMaterial = null;
		}

		private static void AddTriangle(int[] triangles, ref int index, int first, int second, int third) {
			triangles[index++] = first;
			triangles[index++] = second;
			triangles[index++] = third;
		}
	}
}
