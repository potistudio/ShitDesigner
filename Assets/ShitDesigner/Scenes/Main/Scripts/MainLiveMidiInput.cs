using ShitDesigner.Core;
using ShitDesigner.Input;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Maps the Main scene's fixed WinMM MIDI controls to normalized live parameters.</summary>
	[DisallowMultipleComponent]
	public sealed class MainLiveMidiInput : MonoBehaviour {
		private const int MaximumEventsPerFrame = 4096;
		[SerializeField, Range(1, 16)] private int _channel = 1;
		[SerializeField, Range(0, 127)] private int _sceneControlChange = 20;
		[SerializeField, Range(0, 127)] private int _motionControlChange = 21;
		[SerializeField, Range(0, 127)] private int _scaleControlChange = 22;
		[SerializeField, Range(0, 127)] private int _firstSceneNote = 36;
		[SerializeField, Range(0, 127)] private int _secondSceneNote = 37;
		[SerializeField] private bool _openDefaultDevice = true;
		private MainLiveInput _target;
		private IMidiInputSource _source;
		private bool _ownsSource;

		public bool IsConnected => _source != null && (!(_source is IMidiInputAvailability availability) || availability.IsAvailable);
		public string DeviceName => _source?.DeviceName ?? string.Empty;
		public string LastError { get; private set; } = string.Empty;

		public void Initialize(MainLiveInput target) {
			_target = target;
			if (_source != null || !_openDefaultDevice) return;
			if (WindowsMidiInputSource.TryOpenDefault(out var source, out var error)) {
				_source = source;
				_ownsSource = true;
				LastError = string.Empty;
			}
			else {
				LastError = error ?? "The default MIDI input device could not be opened.";
				Debug.LogWarning("[MainLiveMidi] " + LastError + " Keyboard input remains available.", this);
			}
		}

		public void ConfigureSource(IMidiInputSource source, bool ownsSource = false) {
			if (_ownsSource) _source?.Dispose();
			_source = source;
			_ownsSource = ownsSource;
			LastError = string.Empty;
		}

		public int Capture(int sceneCount) {
			if (_target == null || _source == null) return 0;
			var count = 0;
			while (count < MaximumEventsPerFrame && _source.TryDequeue(out var inputEvent)) {
				Route(inputEvent, sceneCount);
				count++;
			}
			return count;
		}

		public void Stop() {
			if (_ownsSource) _source?.Dispose();
			_source = null;
			_ownsSource = false;
			_target = null;
		}

		private void Route(MidiInputEvent inputEvent, int sceneCount) {
			var control = inputEvent.Control;
			if (control.Channel != _channel) return;
			if (control.Kind == MidiControlKind.Note && inputEvent.RawValue > 0) {
				if (control.Number == _firstSceneNote) _target.SetSceneIndex(0, sceneCount);
				else if (control.Number == _secondSceneNote) _target.SetSceneIndex(1, sceneCount);
				return;
			}
			if (control.Kind != MidiControlKind.ControlChange) return;
			var normalized = inputEvent.RawValue / (float)control.RawMaximum;
			if (control.Number == _sceneControlChange) _target.SetScene(normalized);
			else if (control.Number == _motionControlChange) _target.SetMotion(normalized);
			else if (control.Number == _scaleControlChange) _target.SetScale(normalized);
		}

		private void OnDestroy() => Stop();
	}
}
