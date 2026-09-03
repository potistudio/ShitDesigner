using UnityEngine;

namespace ShitDesigner.Main {
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
}
