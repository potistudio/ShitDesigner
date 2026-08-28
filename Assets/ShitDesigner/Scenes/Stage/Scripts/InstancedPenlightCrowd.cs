using System;
using System.Collections.Generic;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Stage {
	[DisallowMultipleComponent]
	public sealed class InstancedPenlightCrowd : MonoBehaviour, IBpmClockReceiver {
		private const int MaximumInstancesPerBatch = 1023;
		private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
		private static readonly int BeatPositionId = Shader.PropertyToID("_BeatPosition");
		private static readonly int DirectionId = Shader.PropertyToID("_Direction");
		private static readonly int PhaseId = Shader.PropertyToID("_Phase");

		[SerializeField] private Material _material;
		[SerializeField, Range(30f, 300f)] private float _previewBpm = 145f;
		[SerializeField, Min(1)] private int _count = 4096;
		[SerializeField] private Vector3 _center = new(8f, 0.2f, -3f);
		[SerializeField, Min(0.1f)] private float _width = 24f;
		[SerializeField, Min(0.1f)] private float _depth = 16f;
		[SerializeField, Min(0.01f)] private float _minimumScale = 0.65f;
		[SerializeField, Min(0.01f)] private float _maximumScale = 1.15f;
		[SerializeField, Range(0f, 1f)] private float _phaseSpread;
		[SerializeField, Range(0f, 1f)] private float _directionSpread = 1f;
		[SerializeField] private Color[] _colors = {
			new(0.1f, 0.8f, 1f),
			new(1f, 0.15f, 0.7f),
			new(0.6f, 0.25f, 1f),
			new(1f, 0.45f, 0.1f)
		};

		private readonly List<Batch> _batches = new();
		private Mesh _mesh;
		private double _totalBeats;
		private bool _usesExternalClock;

		private void OnEnable() {
			Rebuild();
		}

		private void Update() {
			if (!_usesExternalClock) _totalBeats += Time.unscaledDeltaTime * _previewBpm / 60d;
			if (_batches.Count == 0) Rebuild();
			if (_mesh == null || _material == null || !SystemInfo.supportsInstancing) return;

			foreach (var batch in _batches) {
				batch.Properties.SetFloat(BeatPositionId, (float)_totalBeats);
				Graphics.DrawMeshInstanced(
					_mesh,
					0,
					_material,
					batch.Matrices,
					batch.Count,
					batch.Properties,
					ShadowCastingMode.Off,
					false,
					gameObject.layer);
			}
		}

		private void OnValidate() {
			_previewBpm = Mathf.Clamp(_previewBpm, 30f, 300f);
			_phaseSpread = Mathf.Clamp01(_phaseSpread);
			_directionSpread = Mathf.Clamp01(_directionSpread);
			if (!isActiveAndEnabled) return;
			Rebuild();
		}

		private void OnDrawGizmosSelected() {
			var previousMatrix = Gizmos.matrix;
			var previousColor = Gizmos.color;
			Gizmos.matrix = transform.localToWorldMatrix;
			Gizmos.color = new Color(0.1f, 0.8f, 1f, 0.9f);
			Gizmos.DrawWireCube(_center, new Vector3(_width, 0.05f, _depth));
			Gizmos.matrix = previousMatrix;
			Gizmos.color = previousColor;
		}

		public void SetBpmClock(BpmClockState clock) {
			if (double.IsNaN(clock.TotalBeats) || double.IsInfinity(clock.TotalBeats)) return;

			_usesExternalClock = true;
			_totalBeats = clock.TotalBeats;
		}

		private void OnDestroy() {
			if (_mesh != null) Destroy(_mesh);
		}

		private void Rebuild() {
			if (_material == null || !SystemInfo.supportsInstancing) return;

			_mesh ??= CreatePenlightMesh();
			_batches.Clear();

			var count = Mathf.Max(1, _count);
			var colors = _colors is { Length: > 0 } ? _colors : new[] { Color.white };
			var columns = Mathf.CeilToInt(Mathf.Sqrt(count * _width / _depth));
			var rows = Mathf.CeilToInt((float)count / columns);
			for (var batchStart = 0; batchStart < count; batchStart += MaximumInstancesPerBatch) {
				var batchCount = Mathf.Min(MaximumInstancesPerBatch, count - batchStart);
				var matrices = new Matrix4x4[batchCount];
				var baseColors = new Vector4[batchCount];
				var directions = new float[batchCount];
				var phases = new float[batchCount];
				for (var batchIndex = 0; batchIndex < batchCount; batchIndex++) {
					var instanceIndex = batchStart + batchIndex;
					var column = instanceIndex % columns;
					var row = instanceIndex / columns;
					var x = ((column + Hash01(instanceIndex * 3 + 1)) / columns - 0.5f) * _width;
					var z = ((row + Hash01(instanceIndex * 3 + 2)) / rows - 0.5f) * _depth;
					var position = _center + new Vector3(x, Hash01(instanceIndex * 3 + 3) * 0.35f, z);
					var scale = Mathf.Lerp(_minimumScale, Mathf.Max(_minimumScale, _maximumScale), Hash01(instanceIndex * 3 + 4));
					var motionSeed = Hash01(instanceIndex * 3 + 5);
					matrices[batchIndex] = transform.localToWorldMatrix * Matrix4x4.TRS(position, Quaternion.identity, Vector3.one * scale);
					baseColors[batchIndex] = colors[instanceIndex % colors.Length];
					directions[batchIndex] = motionSeed * _directionSpread;
					phases[batchIndex] = motionSeed * _phaseSpread;
				}

				var properties = new MaterialPropertyBlock();
				properties.SetVectorArray(BaseColorId, baseColors);
				properties.SetFloatArray(DirectionId, directions);
				properties.SetFloatArray(PhaseId, phases);
				_batches.Add(new Batch(matrices, properties));
			}
		}

		private static float Hash01(int value) {
			return Mathf.Repeat(Mathf.Sin(value * 12.9898f) * 43758.5453f, 1f);
		}

		private static Mesh CreatePenlightMesh() {
			const int sides = 8;
			const float radius = 0.045f;
			const float height = 1.25f;
			var vertices = new Vector3[sides * 2 + 2];
			var triangles = new int[sides * 12];
			for (var side = 0; side < sides; side++) {
				var angle = side * Mathf.PI * 2f / sides;
				var point = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
				vertices[side] = point;
				vertices[side + sides] = point + Vector3.up * height;
			}

			var bottomCenter = sides * 2;
			var topCenter = bottomCenter + 1;
			vertices[bottomCenter] = Vector3.zero;
			vertices[topCenter] = Vector3.up * height;
			var triangleIndex = 0;
			for (var side = 0; side < sides; side++) {
				var next = (side + 1) % sides;
				triangles[triangleIndex++] = side;
				triangles[triangleIndex++] = side + sides;
				triangles[triangleIndex++] = next + sides;
				triangles[triangleIndex++] = side;
				triangles[triangleIndex++] = next + sides;
				triangles[triangleIndex++] = next;
				triangles[triangleIndex++] = bottomCenter;
				triangles[triangleIndex++] = next;
				triangles[triangleIndex++] = side;
				triangles[triangleIndex++] = topCenter;
				triangles[triangleIndex++] = side + sides;
				triangles[triangleIndex++] = next + sides;
			}

			var mesh = new Mesh { name = "Instanced Penlight Mesh" };
			mesh.vertices = vertices;
			mesh.triangles = triangles;
			mesh.RecalculateBounds();
			return mesh;
		}

		private sealed class Batch {
			public Batch(Matrix4x4[] matrices, MaterialPropertyBlock properties) {
				Matrices = matrices;
				Properties = properties;
			}

			public Matrix4x4[] Matrices { get; }
			public MaterialPropertyBlock Properties { get; }
			public int Count => Matrices.Length;
		}
	}
}
