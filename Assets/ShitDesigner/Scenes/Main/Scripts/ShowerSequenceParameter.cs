using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Triggers a three-frame monochrome and rearrangement sequence for a shower scene.</summary>
	[DisallowMultipleComponent]
	public sealed class ShowerSequenceParameter : LiveSceneParameter, ISceneGraphClockReceiver {
		public const string ParameterId = "shower_sequence";

		[SerializeField] private string _id = ParameterId;
		[SerializeField] private string _displayName = "Shower Sequence";
		[SerializeField] private FallingObjectShower _shower;
		[SerializeField] private float _value;

		private SequencePhase _phase;
		private bool _initialized;
		private bool _graphClockDriven;

		public override LiveParameterDefinition Definition => new LiveParameterDefinition(_id, _displayName, 0f, 1f, _value);

		private void Update() {
			if (UnityEngine.Application.isPlaying && !_graphClockDriven)
				AdvanceFrame();
		}

		public override void InitializeParameter() {
			if (_shower == null)
				_shower = GetComponentInChildren<FallingObjectShower>(true);
			SetMonochromeEnabled(false);
			_initialized = true;
		}

		public override bool TrySetValue(float value, out string rejectionReason) {
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				rejectionReason = "The parameter value must be finite.";
				return false;
			}
			if (!_initialized || _shower == null) {
				rejectionReason = "The shower sequence parameter has not been initialized.";
				return false;
			}

			_value = Mathf.Clamp01(value);
			StartSequence();
			rejectionReason = string.Empty;
			return true;
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			_graphClockDriven = graphClockDriven;
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!_graphClockDriven)
				return;

			AdvanceFrame();
		}

		private void StartSequence() {
			SetMonochromeEnabled(true);
			_phase = SequencePhase.Monochrome;
		}

		private void AdvanceFrame() {
			switch (_phase) {
				case SequencePhase.Monochrome:
					_shower.Rearrange();
					_phase = SequencePhase.Rearranging;
					break;
				case SequencePhase.Rearranging:
					SetMonochromeEnabled(false);
					_phase = SequencePhase.Released;
					break;
				case SequencePhase.Released:
					_phase = SequencePhase.Idle;
					break;
			}
		}

		private void SetMonochromeEnabled(bool enabled) {
			if (_shower == null)
				return;

			foreach (var applicator in _shower.GetComponentsInChildren<ShowerMonochromeApplicator>(true))
				applicator.SetMonochromeEnabled(enabled);
		}

		private enum SequencePhase {
			Idle,
			Monochrome,
			Rearranging,
			Released
		}
	}
}
