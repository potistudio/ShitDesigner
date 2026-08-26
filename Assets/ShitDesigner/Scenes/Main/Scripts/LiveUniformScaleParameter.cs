using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Scales an authored target and optionally adjusts its scene camera's field of view.</summary>
	[DisallowMultipleComponent]
	public sealed class LiveUniformScaleParameter : LiveSceneParameter {
		public const string ParameterId = "scale";

		[SerializeField] private string _id = ParameterId;
		[SerializeField] private string _displayName = "Scale";
		[SerializeField] private Transform _target;
		[SerializeField] private Camera _camera;
		[SerializeField] private float _value = 0.5f;
		[SerializeField] private float _minimumMultiplier = 0.75f;
		[SerializeField] private float _maximumMultiplier = 1.25f;

		private Vector3 _baseScale;
		private float _baseFieldOfView;
		private bool _initialized;

		public override LiveParameterDefinition Definition => new LiveParameterDefinition(_id, _displayName, 0f, 1f, _value);

		public override void InitializeParameter() {
			_target = _target == null ? transform : _target;
			_camera = _camera == null ? GetComponentInChildren<Camera>(true) : _camera;
			_baseScale = _target.localScale;
			_baseFieldOfView = _camera == null ? 0f : _camera.fieldOfView;
			_initialized = true;
			ApplyValue();
		}

		public override bool TrySetValue(float value, out string rejectionReason) {
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				rejectionReason = "The parameter value must be finite.";
				return false;
			}
			if (!_initialized || _target == null) {
				rejectionReason = "The scale parameter has not been initialized.";
				return false;
			}

			_value = Mathf.Clamp01(value);
			ApplyValue();
			rejectionReason = string.Empty;
			return true;
		}

		private void ApplyValue() {
			var multiplier = Mathf.Lerp(_minimumMultiplier, _maximumMultiplier, _value);
			_target.localScale = _baseScale * multiplier;
			if (_camera != null) _camera.fieldOfView = Mathf.Clamp(_baseFieldOfView * multiplier, 20f, 120f);
		}
	}
}
