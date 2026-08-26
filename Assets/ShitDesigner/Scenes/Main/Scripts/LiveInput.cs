using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using ShitDesigner.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShitDesigner.Main {
	/// <summary>Maps live keyboard controls to parameter requests without owning a PlayerLoop.</summary>
	public sealed class LiveKeyboardInput {
		private readonly LiveParameterQueue _queue;
		private readonly float _adjustmentStep;
		private float _motion = 0.5f;
		private float _scale = 0.5f;

		public LiveKeyboardInput(LiveParameterQueue queue, float adjustmentStep = 0.05f) {
			_queue = queue ?? throw new ArgumentNullException(nameof(queue));
			_adjustmentStep = Mathf.Clamp(adjustmentStep, 0.001f, 1f);
		}

		public void Poll(string loadedPatchId, IReadOnlyList<string> patchIds) {
			var keyboard = Keyboard.current;
			if (keyboard == null || string.IsNullOrWhiteSpace(loadedPatchId)) return;

			if (patchIds != null) {
				if (patchIds.Count > 0 && keyboard.digit1Key.wasPressedThisFrame) SelectPatch(patchIds[0]);
				if (patchIds.Count > 1 && keyboard.digit2Key.wasPressedThisFrame) SelectPatch(patchIds[1]);
			}
			if (keyboard.leftArrowKey.wasPressedThisFrame) SetScale(loadedPatchId, _scale - _adjustmentStep);
			if (keyboard.rightArrowKey.wasPressedThisFrame) SetScale(loadedPatchId, _scale + _adjustmentStep);
			if (keyboard.downArrowKey.wasPressedThisFrame) SetMotion(loadedPatchId, _motion - _adjustmentStep);
			if (keyboard.upArrowKey.wasPressedThisFrame) SetMotion(loadedPatchId, _motion + _adjustmentStep);
			if (keyboard.spaceKey.wasPressedThisFrame) _queue.EnqueueTriggerFlash(loadedPatchId);
		}

		private void SelectPatch(string patchId) {
			_queue.EnqueuePreloadPatch(patchId);
			_queue.EnqueueLoadPatch(patchId);
		}

		private void SetMotion(string sceneId, float value) {
			_motion = Mathf.Clamp01(value);
			_queue.EnqueueSetParameter(sceneId, LiveGraphClockRateParameter.ParameterId, _motion);
		}

		private void SetScale(string sceneId, float value) {
			_scale = Mathf.Clamp01(value);
			_queue.EnqueueSetParameter(sceneId, LiveUniformScaleParameter.ParameterId, _scale);
		}
	}

	/// <summary>Maps MIDI events to live requests without owning the MIDI device lifecycle.</summary>
	public sealed class LiveMidiInput : IDisposable {
		private readonly MidiInputManager _manager;
		private readonly LiveParameterQueue _queue;
		private readonly IReadOnlyList<string> _sceneIds;
		private readonly int _channel;
		private string _loadedPatchId;

		public LiveMidiInput(MidiInputManager manager, LiveParameterQueue queue, IReadOnlyList<string> sceneIds, int channel = 1) {
			_manager = manager ?? throw new ArgumentNullException(nameof(manager));
			_queue = queue ?? throw new ArgumentNullException(nameof(queue));
			_sceneIds = sceneIds ?? throw new ArgumentNullException(nameof(sceneIds));
			_channel = Mathf.Clamp(channel, 1, 16);
			_manager.InputReceived += OnInputReceived;
			_manager.TriggerReceived += OnTriggerReceived;
		}

		public void SetSelectedPatch(string patchId) => _loadedPatchId = patchId ?? string.Empty;

		public void Dispose() {
			_manager.InputReceived -= OnInputReceived;
			_manager.TriggerReceived -= OnTriggerReceived;
		}

		private void OnInputReceived(MidiInputEvent inputEvent) {
			var control = inputEvent.Control;
			if (control.Channel != _channel) return;
			if (_manager.IsTriggerBinding(control)) return;
			if (control.Kind == MidiControlKind.Note && inputEvent.RawValue > 0) {
				var sceneIndex = control.Number - 36;
				if (sceneIndex >= 0 && sceneIndex < _sceneIds.Count) SelectPatch(_sceneIds[sceneIndex]);
				return;
			}
			if (control.Kind != MidiControlKind.ControlChange || string.IsNullOrWhiteSpace(_loadedPatchId)) return;

			var normalized = inputEvent.RawValue / (float)control.RawMaximum;
			if (control.Number == 20 && _sceneIds.Count > 0) {
				var index = Mathf.RoundToInt(normalized * (_sceneIds.Count - 1));
				SelectPatch(_sceneIds[index]);
			}
			else if (control.Number == 21) _queue.EnqueueSetParameter(_loadedPatchId, LiveGraphClockRateParameter.ParameterId, normalized);
			else if (control.Number == 22) _queue.EnqueueSetParameter(_loadedPatchId, LiveUniformScaleParameter.ParameterId, normalized);
		}

		private void OnTriggerReceived(MidiLiveControlBinding binding) {
			if (!string.IsNullOrWhiteSpace(_loadedPatchId)) _queue.EnqueueTriggerFlash(_loadedPatchId);
		}

		private void SelectPatch(string patchId) {
			_queue.EnqueuePreloadPatch(patchId);
			_queue.EnqueueLoadPatch(patchId);
		}
	}
}
