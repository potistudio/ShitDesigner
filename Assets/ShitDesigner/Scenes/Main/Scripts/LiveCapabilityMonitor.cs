using ShitDesigner.Input;
using UnityEngine;

namespace ShitDesigner.Main {
	public readonly struct LiveCapabilitySnapshot {
		public bool MidiAvailable { get; }
		public string MidiDeviceName { get; }
		public string MidiError { get; }
		public bool ExternalDisplayAvailable { get; }
		public int ConnectedDisplayCount { get; }
		public string ExternalDisplayError { get; }

		internal LiveCapabilitySnapshot(MidiInputManager midi, LiveExternalDisplayOutput output) {
			MidiAvailable = midi != null && midi.IsOpen;
			MidiDeviceName = midi?.DeviceName ?? string.Empty;
			MidiError = midi?.LastError ?? "MIDI Input Manager is unavailable.";
			ExternalDisplayAvailable = output != null && output.IsAvailable;
			ConnectedDisplayCount = output?.ConnectedDisplayCount ?? 0;
			ExternalDisplayError = output?.LastError ?? "External Display output is unavailable.";
		}
	}

	/// <summary>Checks optional live capabilities on the first Player frame and at one-second intervals.</summary>
	[DisallowMultipleComponent]
	[DefaultExecutionOrder(900)]
	public sealed class LiveCapabilityMonitor : MonoBehaviour {
		private MidiInputManager _midi;
		private LiveExternalDisplayOutput _output;
		private float _nextCheckTime;
		private bool _configured;

		public LiveCapabilitySnapshot Snapshot { get; private set; }

		public void Initialize(MidiInputManager midi, LiveExternalDisplayOutput output) {
			_midi = midi;
			_output = output;
			_nextCheckTime = 0f;
			_configured = true;
			Snapshot = new LiveCapabilitySnapshot(_midi, _output);
		}

		public void Shutdown() {
			_configured = false;
			_midi = null;
			_output = null;
		}

		private void Update() {
			if (!_configured || Time.unscaledTime < _nextCheckTime) return;
			if (_midi != null && !_midi.IsOpen) _midi.TryReconnect();
			Snapshot = new LiveCapabilitySnapshot(_midi, _output);
			_nextCheckTime = Time.unscaledTime + 1f;
		}
	}
}
