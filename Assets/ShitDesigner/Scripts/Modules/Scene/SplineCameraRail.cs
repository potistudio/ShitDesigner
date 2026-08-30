using System;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace ShitDesigner.Scene {
	/// <summary>Moves a camera along an inspector-authored spline using the scene graph clock.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class SplineCameraRail : MonoBehaviour, ISceneGraphClockReceiver {
		[Header("Path")]
		[SerializeField] private SplineContainer path;
		[SerializeField] private Transform cameraTransform;
		[Header("Framing")]
		[SerializeField] private Transform target;
		[Min(0f)][SerializeField] private float speed = 1.5f;
		[Range(0f, 1f)][SerializeField] private float startOffset;
		[SerializeField] private bool loop = true;
		[SerializeField] private bool alignToPath = true;

		private bool _graphClockDriven;
		private bool _initialized;
		private float _pathLength;
		private float _distance;

		public float NormalizedPosition => _pathLength <= Mathf.Epsilon ? 0f : Mathf.Clamp01(_distance / _pathLength);

		private void OnEnable() {
			ResetProgress();
		}

		private void Update() {
			if (Application.isPlaying && !_graphClockDriven) Advance(Time.deltaTime);
		}

		private void OnValidate() {
			speed = Mathf.Max(0f, speed);
			startOffset = Mathf.Clamp01(startOffset);
			_initialized = false;
			if (!Application.isPlaying) ResetProgress();
		}

		public void SetGraphClockDriven(bool graphClockDriven) => _graphClockDriven = graphClockDriven;

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!_graphClockDriven || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds <= 0d) return;
			Advance((float)Math.Min(deltaSeconds, float.MaxValue));
		}

		[ContextMenu("Reset Camera Rail")]
		public void ResetProgress() {
			RefreshPathState();
			_distance = _pathLength * startOffset;
			ApplyPosition();
		}

		private void Advance(float deltaSeconds) {
			if (deltaSeconds <= 0f || path == null || cameraTransform == null) return;
			if (!_initialized) RefreshPathState();
			if (_pathLength <= Mathf.Epsilon) return;

			_distance += speed * deltaSeconds;
			_distance = loop ? Mathf.Repeat(_distance, _pathLength) : Mathf.Clamp(_distance, 0f, _pathLength);
			ApplyPosition();
		}

		private void RefreshPathState() {
			_pathLength = path == null || path.Spline == null ? 0f : path.CalculateLength();
			_initialized = true;
		}

		private void ApplyPosition() {
			if (path == null || cameraTransform == null || _pathLength <= Mathf.Epsilon) return;

			var normalized = SplineUtility.ConvertIndexUnit(path.Spline, _distance, PathIndexUnit.Distance, PathIndexUnit.Normalized);
			if (!path.Evaluate(normalized, out float3 position, out float3 tangent, out float3 upVector)) return;

			cameraTransform.position = (Vector3)position;
			if (target != null) {
				var targetDirection = target.position - cameraTransform.position;
				if (targetDirection.sqrMagnitude > 0.000001f) {
					cameraTransform.rotation = Quaternion.LookRotation(targetDirection, Vector3.up);
					return;
				}
			}
			if (alignToPath && math.lengthsq(tangent) > 0.000001f && math.lengthsq(upVector) > 0.000001f)
				cameraTransform.rotation = Quaternion.LookRotation((Vector3)tangent, (Vector3)upVector);
		}
	}
}
