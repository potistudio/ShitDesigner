using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using ShitDesigner.Input;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShitDesigner.Main {
	/// <summary>Maps live keyboard controls to live requests without owning a PlayerLoop.</summary>
	public sealed class LiveKeyboardInput {
		private readonly LiveParameterQueue _queue;
		private readonly Action<int> _launchPatchSlot;
		private readonly Action<int> _clearPatchSlot;
		private readonly Action<int> _moveCatalogSelection;
		private readonly Action _queueSelectedPatch;
		private readonly Action<double> _tapBpm;

		public LiveKeyboardInput(LiveParameterQueue queue, Action<int> launchPatchSlot, Action<int> clearPatchSlot, Action<int> moveCatalogSelection, Action queueSelectedPatch, Action<double> tapBpm) {
			_queue = queue ?? throw new ArgumentNullException(nameof(queue));
			_launchPatchSlot = launchPatchSlot ?? throw new ArgumentNullException(nameof(launchPatchSlot));
			_clearPatchSlot = clearPatchSlot ?? throw new ArgumentNullException(nameof(clearPatchSlot));
			_moveCatalogSelection = moveCatalogSelection ?? throw new ArgumentNullException(nameof(moveCatalogSelection));
			_queueSelectedPatch = queueSelectedPatch ?? throw new ArgumentNullException(nameof(queueSelectedPatch));
			_tapBpm = tapBpm ?? throw new ArgumentNullException(nameof(tapBpm));
		}

		public void Poll(string loadedPatchId) {
			var keyboard = Keyboard.current;
			if (keyboard == null || string.IsNullOrWhiteSpace(loadedPatchId)) return;

			var clearSlot = keyboard.shiftKey.isPressed;
			if (keyboard.digit1Key.wasPressedThisFrame) HandleSlotKey(0, clearSlot);
			if (keyboard.digit2Key.wasPressedThisFrame) HandleSlotKey(1, clearSlot);
			if (keyboard.digit3Key.wasPressedThisFrame) HandleSlotKey(2, clearSlot);
			if (keyboard.digit4Key.wasPressedThisFrame) HandleSlotKey(3, clearSlot);
			if (keyboard.leftArrowKey.wasPressedThisFrame) _moveCatalogSelection(-1);
			if (keyboard.rightArrowKey.wasPressedThisFrame) _moveCatalogSelection(1);
			if (keyboard.enterKey.wasPressedThisFrame) _queueSelectedPatch();
			if (keyboard.spaceKey.wasPressedThisFrame) _tapBpm(Time.unscaledTimeAsDouble);
			if (keyboard.fKey.wasPressedThisFrame) _queue.EnqueueTriggerFlash(loadedPatchId);
		}

		private void HandleSlotKey(int slotIndex, bool clearSlot) {
			if (clearSlot) _clearPatchSlot(slotIndex);
			else _launchPatchSlot(slotIndex);
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
			_queue.EnqueueLoadPatch(patchId);
		}
	}
}
