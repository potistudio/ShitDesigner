using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShitDesigner.Main {
	public readonly struct LiveParameterDefinition {
		public string Id { get; }
		public string DisplayName { get; }
		public float Minimum { get; }
		public float Maximum { get; }
		public float Value { get; }

		public LiveParameterDefinition(string id, string displayName, float minimum, float maximum, float value) {
			Id = id;
			DisplayName = displayName;
			Minimum = minimum;
			Maximum = maximum;
			Value = value;
		}
	}

	/// <summary>Owns the public parameters and their effects for one live scene.</summary>
	[DisallowMultipleComponent]
	public sealed class LiveSceneRoot : MonoBehaviour {
		public const string MotionParameterId = "motion";
		public const string ScaleParameterId = "scale";

		private static readonly string[] ParameterIds = { MotionParameterId, ScaleParameterId };
		private string _sceneId = string.Empty;
		private Vector3 _baseScale;
		private Camera _camera;
		private float _baseFieldOfView;
		private float _motion = 0.5f;
		private float _scale = 0.5f;

		public string SceneId => _sceneId;
		public float Motion => _motion;
		public IReadOnlyList<string> PublicParameterIds => ParameterIds;

		public void Initialize(string sceneId) {
			if (string.IsNullOrWhiteSpace(sceneId)) throw new ArgumentException("A scene ID is required.", nameof(sceneId));
			_sceneId = sceneId;
			_baseScale = transform.localScale;
			_camera = GetComponentInChildren<Camera>(true);
			if (_camera == null) throw new InvalidOperationException("A live scene requires a Camera.");
			_baseFieldOfView = _camera.fieldOfView;
			ApplyScale();
		}

		public LiveParameterDefinition[] GetParameterDefinitions() => new[] {
			new LiveParameterDefinition(MotionParameterId, "Motion", 0f, 1f, _motion),
			new LiveParameterDefinition(ScaleParameterId, "Scale", 0f, 1f, _scale)
		};

		public bool TrySetParameter(string parameterId, float value, out string rejectionReason) {
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				rejectionReason = "The parameter value must be finite.";
				return false;
			}

			var normalized = Mathf.Clamp01(value);
			switch (parameterId) {
				case MotionParameterId:
					_motion = normalized;
					break;
				case ScaleParameterId:
					_scale = normalized;
					ApplyScale();
					break;
				default:
					rejectionReason = "The parameter is not published by this live scene.";
					return false;
			}

			rejectionReason = string.Empty;
			return true;
		}

		private void ApplyScale() {
			var multiplier = Mathf.Lerp(0.75f, 1.25f, _scale);
			transform.localScale = _baseScale * multiplier;
			if (_camera != null) _camera.fieldOfView = Mathf.Clamp(_baseFieldOfView * multiplier, 20f, 120f);
		}
	}
}
