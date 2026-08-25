using ShitDesigner.Core;
using ShitDesigner.Input;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>
	/// Maps events from the shared MIDI input manager to the Main scene's fixed live parameters.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class MainLiveMidiInput : MonoBehaviour {
		[SerializeField] private MidiInputManager _manager;
		[SerializeField, Range(1, 16)] private int _channel = 1;
		[SerializeField, Range(0, 127)] private int _sceneControlChange = 20;
		[SerializeField, Range(0, 127)] private int _motionControlChange = 21;
		[SerializeField, Range(0, 127)] private int _scaleControlChange = 22;
		[SerializeField, Range(0, 127)] private int _firstSceneNote = 36;
		[SerializeField, Range(0, 127)] private int _secondSceneNote = 37;
		private MainLiveInput _target;
		private int _sceneCount;

		public bool IsConnected => _manager != null && _manager.IsOpen;
		public string DeviceName => _manager?.DeviceName ?? string.Empty;
		public string LastError => _manager == null ? "MIDI Input Manager is required." : _manager.LastError;

		public bool Initialize(MainLiveInput target, int sceneCount) {
			Stop();
			if (target == null || _manager == null || sceneCount < 1) return false;
			_target = target;
			_sceneCount = sceneCount;
			_manager.InputReceived += OnInputReceived;
			return true;
		}

		public void Stop() {
			if (_manager != null) _manager.InputReceived -= OnInputReceived;
			_target = null;
			_sceneCount = 0;
		}

		private void OnInputReceived(MidiInputEvent inputEvent) {
			if (_target == null) return;
			var control = inputEvent.Control;
			if (control.Channel != _channel) return;
			if (control.Kind == MidiControlKind.Note && inputEvent.RawValue > 0) {
				if (control.Number == _firstSceneNote) _target.SetSceneIndex(0, _sceneCount);
				else if (control.Number == _secondSceneNote) _target.SetSceneIndex(1, _sceneCount);
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
