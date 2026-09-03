using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Generates an irregular closed mesh centered at the configured scene position.</summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
	public sealed class RandomMeshGenerator : MonoBehaviour {
		[Header("Shape")]
		[Min(3)][SerializeField] private int _segments = 12;
		[Min(1)][SerializeField] private int _rings = 4;
		[Min(0.01f)][SerializeField] private float _radius = 1f;
		[Min(0.01f)][SerializeField] private float _height = 2f;
		[Range(0f, 0.45f)][SerializeField] private float _radialVariation = 0.25f;
		[Range(0f, 0.45f)][SerializeField] private float _verticalVariation = 0.25f;
		[SerializeField] private Vector3 _center = Vector3.zero;

		[Header("Randomness")]
		[SerializeField] private bool _randomizeSeed = true;
		[SerializeField] private int _seed = 12345;

		[Header("Appearance")]
		[SerializeField] private Material _material;

		private MeshFilter _meshFilter;
		private MeshRenderer _meshRenderer;
		private Mesh _generatedMesh;

		private void Start() {
			Generate();
		}

		private void OnDestroy() {
			ReleaseGeneratedMesh();
		}

		private void OnValidate() {
			_segments = Mathf.Clamp(_segments, 3, 256);
			_rings = Mathf.Clamp(_rings, 1, 64);
			_radius = Mathf.Max(0.01f, _radius);
			_height = Mathf.Max(0.01f, _height);
			_radialVariation = Mathf.Clamp(_radialVariation, 0f, 0.45f);
			_verticalVariation = Mathf.Clamp(_verticalVariation, 0f, 0.45f);
		}

		[ContextMenu("Generate Random Mesh")]
		public void Generate() {
			if (_meshFilter == null) _meshFilter = GetComponent<MeshFilter>();
			if (_meshRenderer == null) _meshRenderer = GetComponent<MeshRenderer>();

			ReleaseGeneratedMesh();
			transform.position = _center;
			_generatedMesh = BuildMesh();
			_meshFilter.sharedMesh = _generatedMesh;
			_meshRenderer.sharedMaterial = _material;
		}

		private Mesh BuildMesh() {
			var random = _randomizeSeed ? new System.Random() : new System.Random(_seed);
			var vertexCount = 2 + _rings * _segments;
			var vertices = new Vector3[vertexCount];
			var triangles = new int[_segments * _rings * 6];
			var halfHeight = _height * 0.5f;
			var ringSpacing = _height / (_rings + 1);
			var angleStep = Mathf.PI * 2f / _segments;
			var bottomIndex = 0;
			var topIndex = vertexCount - 1;

			vertices[bottomIndex] = new Vector3(0f, -halfHeight, 0f);
			vertices[topIndex] = new Vector3(0f, halfHeight, 0f);

			for (var ring = 0; ring < _rings; ring++) {
				var ringPosition = (ring + 1f) / (_rings + 1f);
				var y = Mathf.Lerp(-halfHeight, halfHeight, ringPosition);
				var ringStart = 1 + ring * _segments;

				for (var segment = 0; segment < _segments; segment++) {
					var angle = segment * angleStep + NextFloat(random, -angleStep * 0.15f, angleStep * 0.15f);
					var radius = _radius * NextFloat(random, 1f - _radialVariation, 1f + _radialVariation);
					var yOffset = NextFloat(random, -ringSpacing * _verticalVariation, ringSpacing * _verticalVariation);
					vertices[ringStart + segment] = new Vector3(
						Mathf.Cos(angle) * radius,
						y + yOffset,
						Mathf.Sin(angle) * radius);
				}
			}

			var triangleIndex = 0;
			var firstRingStart = 1;
			var lastRingStart = 1 + (_rings - 1) * _segments;
			for (var segment = 0; segment < _segments; segment++) {
				var nextSegment = (segment + 1) % _segments;
				AddTriangle(triangles, ref triangleIndex, bottomIndex, firstRingStart + segment, firstRingStart + nextSegment);
				AddTriangle(triangles, ref triangleIndex, lastRingStart + segment, topIndex, lastRingStart + nextSegment);
			}

			for (var ring = 0; ring < _rings - 1; ring++) {
				var lowerRingStart = 1 + ring * _segments;
				var upperRingStart = lowerRingStart + _segments;
				for (var segment = 0; segment < _segments; segment++) {
					var nextSegment = (segment + 1) % _segments;
					var lowerCurrent = lowerRingStart + segment;
					var lowerNext = lowerRingStart + nextSegment;
					var upperCurrent = upperRingStart + segment;
					var upperNext = upperRingStart + nextSegment;
					AddTriangle(triangles, ref triangleIndex, lowerCurrent, upperCurrent, lowerNext);
					AddTriangle(triangles, ref triangleIndex, lowerNext, upperCurrent, upperNext);
				}
			}

			var mesh = new Mesh {
				name = "Random Generated Mesh",
				hideFlags = HideFlags.DontSave
			};
			mesh.vertices = vertices;
			mesh.triangles = triangles;
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		private void ReleaseGeneratedMesh() {
			if (_meshFilter != null && _meshFilter.sharedMesh == _generatedMesh)
				_meshFilter.sharedMesh = null;
			if (_generatedMesh == null) return;
			if (Application.isPlaying) Destroy(_generatedMesh); else DestroyImmediate(_generatedMesh);
			_generatedMesh = null;
		}

		private static void AddTriangle(int[] triangles, ref int index, int first, int second, int third) {
			triangles[index++] = first;
			triangles[index++] = second;
			triangles[index++] = third;
		}

		private static float NextFloat(System.Random random, float minimum, float maximum) {
			return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
		}
	}
}
