using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Triggers a three-frame monochrome and rearrangement sequence for a shower scene.</summary>
	[DisallowMultipleComponent]
	public sealed class ShowerSequenceParameter : LiveSceneParameter, ISceneGraphClockReceiver {
		public const string ParameterId = "shower_sequence";
		private const float SequenceFrameDuration = 1f / 30f;

		[SerializeField] private string _id = ParameterId;
		[SerializeField] private string _displayName = "Shower Sequence";
		[SerializeField] private FallingObjectShower _shower;
		[SerializeField] private Color _monochromeColor = Color.white;
		[SerializeField] private float _value;

		private SequencePhase _phase;
		private float _remainingSeconds;
		private ILiveParameterTriggerReceiver m_TriggerReceiver;
		private bool _initialized;
		private bool _graphClockDriven;

		public override LiveParameterDefinition Definition => new LiveParameterDefinition(_id, _displayName, 0f, 1f, _value);

		private void Update() {
			if (UnityEngine.Application.isPlaying && !_graphClockDriven)
				Advance(Time.unscaledDeltaTime);
		}

		public override void InitializeParameter() {
			if (_shower == null)
				_shower = GetComponentInChildren<FallingObjectShower>(true);
			if (m_TriggerReceiver == null)
				m_TriggerReceiver = GetComponent<ILiveParameterTriggerReceiver>();
			if (_shower == null) {
				_initialized = false;
				return;
			}

			SetMonochromeEnabled(false);
			ShowerMonochromeColor.SetRuntimeColor(_monochromeColor);
			_initialized = true;
		}

		private void OnValidate() {
			ShowerMonochromeColor.SetRuntimeColor(_monochromeColor);
		}

		public override bool TrySetValue(float value, out string rejectionReason) {
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				rejectionReason = "The parameter value must be finite.";
				return false;
			}
			if (!EnsureInitialized()) {
				rejectionReason = "The shower sequence parameter has not been initialized.";
				return false;
			}

			_value = Mathf.Clamp01(value);
			StartSequence();
			rejectionReason = string.Empty;
			return true;
		}

		private bool EnsureInitialized() {
			if (_initialized && _shower != null)
				return true;

			InitializeParameter();
			return _initialized;
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			_graphClockDriven = graphClockDriven;
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!_graphClockDriven || deltaSeconds <= 0d)
				return;

			Advance((float)System.Math.Min(deltaSeconds, float.MaxValue));
		}

		private void StartSequence() {
			m_TriggerReceiver?.OnLiveParameterTriggered();
			SetMonochromeEnabled(true);
			_phase = SequencePhase.Monochrome;
			_remainingSeconds = SequenceFrameDuration;
		}

		private void Advance(float deltaSeconds) {
			while (deltaSeconds > 0f && _phase != SequencePhase.Idle) {
				var elapsed = Mathf.Min(deltaSeconds, _remainingSeconds);
				deltaSeconds -= elapsed;
				_remainingSeconds -= elapsed;
				if (_remainingSeconds > 0f)
					return;

				switch (_phase) {
					case SequencePhase.Monochrome:
						_shower.Rearrange();
						_phase = SequencePhase.Rearranging;
						_remainingSeconds = SequenceFrameDuration;
						break;
					case SequencePhase.Rearranging:
						SetMonochromeEnabled(false);
						_phase = SequencePhase.Released;
						_remainingSeconds = SequenceFrameDuration;
						break;
					default:
						_phase = SequencePhase.Idle;
						break;
				}
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
