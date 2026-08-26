using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Publishes one authored live-scene control.</summary>
	public interface ILiveSceneParameter {
		LiveParameterDefinition Definition { get; }
		bool TrySetValue(float value, out string rejectionReason);
	}

	/// <summary>Supplies the graph-clock rate for a live scene.</summary>
	public interface ILiveSceneTimeScaleProvider {
		float TimeScale { get; }
	}

	/// <summary>Base component for authored live-scene controls.</summary>
	public abstract class LiveSceneParameter : MonoBehaviour, ILiveSceneParameter {
		public abstract LiveParameterDefinition Definition { get; }
		public abstract bool TrySetValue(float value, out string rejectionReason);
		public virtual void InitializeParameter() { }
	}

	/// <summary>Controls the rate used to advance an isolated scene's graph clock and physics.</summary>
	[DisallowMultipleComponent]
	public sealed class LiveGraphClockRateParameter : LiveSceneParameter, ILiveSceneTimeScaleProvider {
		public const string ParameterId = "motion";

		[SerializeField] private string _id = ParameterId;
		[SerializeField] private string _displayName = "Motion";
		[SerializeField] private float _value = 0.5f;
		[SerializeField] private float _minimumTimeScale;
		[SerializeField] private float _maximumTimeScale = 2f;

		public override LiveParameterDefinition Definition => new LiveParameterDefinition(_id, _displayName, 0f, 1f, _value);
		public float TimeScale => Mathf.Lerp(_minimumTimeScale, _maximumTimeScale, _value);

		public override bool TrySetValue(float value, out string rejectionReason) {
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				rejectionReason = "The parameter value must be finite.";
				return false;
			}

			_value = Mathf.Clamp01(value);
			rejectionReason = string.Empty;
			return true;
		}
	}

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
