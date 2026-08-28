using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using ShitDesigner.Input;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.InputSystem;

namespace ShitDesigner.Main {
	/// <summary>Maps live keyboard controls to live requests without owning a PlayerLoop.</summary>
	public sealed class LiveKeyboardInput {
		private readonly LiveParameterQueue m_Queue;
		private readonly IReadOnlyDictionary<string, PatchDefinition> m_PatchesById;
		private readonly Action<int> m_LaunchPatchSlot;
		private readonly Action<int> m_ClearPatchSlot;
		private readonly Action<int> m_MoveCatalogSelection;
		private readonly Action m_QueueSelectedPatch;
		private readonly Action<double> m_TapBpm;

		public LiveKeyboardInput(LiveParameterQueue queue, IReadOnlyList<PatchDefinition> patches, Action<int> launchPatchSlot, Action<int> clearPatchSlot, Action<int> moveCatalogSelection, Action queueSelectedPatch, Action<double> tapBpm) {
			m_Queue = queue ?? throw new ArgumentNullException(nameof(queue));
			if (patches == null) throw new ArgumentNullException(nameof(patches));

			var patchesById = new Dictionary<string, PatchDefinition>(StringComparer.Ordinal);
			foreach (var patch in patches) {
				if (patch == null || string.IsNullOrWhiteSpace(patch.Id)) throw new ArgumentException("Every live patch requires an ID.", nameof(patches));
				if (!patchesById.TryAdd(patch.Id, patch)) throw new ArgumentException("Live patch IDs must be unique.", nameof(patches));
			}
			m_PatchesById = patchesById;
			m_LaunchPatchSlot = launchPatchSlot ?? throw new ArgumentNullException(nameof(launchPatchSlot));
			m_ClearPatchSlot = clearPatchSlot ?? throw new ArgumentNullException(nameof(clearPatchSlot));
			m_MoveCatalogSelection = moveCatalogSelection ?? throw new ArgumentNullException(nameof(moveCatalogSelection));
			m_QueueSelectedPatch = queueSelectedPatch ?? throw new ArgumentNullException(nameof(queueSelectedPatch));
			m_TapBpm = tapBpm ?? throw new ArgumentNullException(nameof(tapBpm));
		}

		public void Poll(string loadedPatchId) {
			var keyboard = Keyboard.current;
			if (keyboard == null || string.IsNullOrWhiteSpace(loadedPatchId)) return;

			var clearSlot = keyboard.shiftKey.isPressed;
			if (keyboard.digit1Key.wasPressedThisFrame) HandleSlotKey(0, clearSlot);
			if (keyboard.digit2Key.wasPressedThisFrame) HandleSlotKey(1, clearSlot);
			if (keyboard.digit3Key.wasPressedThisFrame) HandleSlotKey(2, clearSlot);
			if (keyboard.digit4Key.wasPressedThisFrame) HandleSlotKey(3, clearSlot);
			if (keyboard.leftArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(-1);
			if (keyboard.rightArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(1);
			if (keyboard.enterKey.wasPressedThisFrame) m_QueueSelectedPatch();
			if (keyboard.spaceKey.wasPressedThisFrame) m_TapBpm(Time.unscaledTimeAsDouble);
			if (keyboard.fKey.wasPressedThisFrame) m_Queue.EnqueueTriggerFlash(loadedPatchId);
			QueuePatchKeyboardInputs(keyboard, loadedPatchId);
		}

		private void HandleSlotKey(int slotIndex, bool clearSlot) {
			if (clearSlot) m_ClearPatchSlot(slotIndex);
			else m_LaunchPatchSlot(slotIndex);
		}

		private void QueuePatchKeyboardInputs(Keyboard keyboard, string loadedPatchId) {
			if (!m_PatchesById.TryGetValue(loadedPatchId, out var patch)) return;

			foreach (var key in keyboard.allKeys) {
				var pressed = key.wasPressedThisFrame;
				var released = key.wasReleasedThisFrame;
				if (!pressed && !released) continue;
				foreach (var binding in patch.KeyboardInputs) {
					if (binding == null || !binding.Matches(key.keyCode)) continue;
					if (pressed) m_Queue.EnqueueSetParameter(loadedPatchId, binding.ParameterId, binding.Value(true));
					if (released) m_Queue.EnqueueSetParameter(loadedPatchId, binding.ParameterId, binding.Value(false));
				}
			}
		}

	}

	/// <summary>Maps MIDI events to live requests without owning the MIDI device lifecycle.</summary>
	public sealed class LiveMidiInput : IDisposable {
		private const int PatchSelectionChannel = 1;
		private readonly MidiInputManager m_Manager;
		private readonly LiveParameterQueue m_Queue;
		private readonly IReadOnlyList<string> m_PatchIds;
		private readonly IReadOnlyDictionary<string, PatchDefinition> m_PatchesById;
		private string m_LoadedPatchId;

		public LiveMidiInput(MidiInputManager manager, LiveParameterQueue queue, IReadOnlyList<PatchDefinition> patches) {
			m_Manager = manager ?? throw new ArgumentNullException(nameof(manager));
			m_Queue = queue ?? throw new ArgumentNullException(nameof(queue));
			if (patches == null) throw new ArgumentNullException(nameof(patches));

			var patchIds = new List<string>(patches.Count);
			var patchesById = new Dictionary<string, PatchDefinition>(StringComparer.Ordinal);
			foreach (var patch in patches) {
				if (patch == null || string.IsNullOrWhiteSpace(patch.Id)) throw new ArgumentException("Every live patch requires an ID.", nameof(patches));
				if (!patchesById.TryAdd(patch.Id, patch)) throw new ArgumentException("Live patch IDs must be unique.", nameof(patches));
				patchIds.Add(patch.Id);
			}
			m_PatchIds = patchIds;
			m_PatchesById = patchesById;
			m_Manager.InputReceived += OnInputReceived;
			m_Manager.TriggerReceived += OnTriggerReceived;
		}

		public void SetSelectedPatch(string patchId) => m_LoadedPatchId = patchId ?? string.Empty;

		public void Dispose() {
			m_Manager.InputReceived -= OnInputReceived;
			m_Manager.TriggerReceived -= OnTriggerReceived;
		}

		private void OnInputReceived(MidiInputEvent inputEvent) {
			var control = inputEvent.Control;
			if (m_Manager.IsTriggerBinding(control)) return;
			if (control.Channel == PatchSelectionChannel && control.Kind == MidiControlKind.Note && inputEvent.RawValue > 0) {
				var sceneIndex = control.Number - 36;
				if (sceneIndex >= 0 && sceneIndex < m_PatchIds.Count) {
					SelectPatch(m_PatchIds[sceneIndex]);
					return;
				}
			}
			if (control.Channel == PatchSelectionChannel && control.Kind == MidiControlKind.ControlChange && control.Number == 20 &&
				!string.IsNullOrWhiteSpace(m_LoadedPatchId) && m_PatchIds.Count > 0) {
				var normalized = inputEvent.RawValue / (float)control.RawMaximum;
				var index = Mathf.RoundToInt(normalized * (m_PatchIds.Count - 1));
				SelectPatch(m_PatchIds[index]);
				return;
			}
			QueuePatchParameterInputs(inputEvent);
		}

		private void OnTriggerReceived(MidiLiveControlBinding binding) {
			if (!string.IsNullOrWhiteSpace(m_LoadedPatchId)) m_Queue.EnqueueTriggerFlash(m_LoadedPatchId);
		}

		private void QueuePatchParameterInputs(MidiInputEvent inputEvent) {
			if (string.IsNullOrWhiteSpace(m_LoadedPatchId) || !m_PatchesById.TryGetValue(m_LoadedPatchId, out var patch)) return;

			foreach (var binding in patch.MidiInputs) {
				if (binding == null || !binding.Matches(inputEvent.Control)) continue;
				m_Queue.EnqueueSetParameter(m_LoadedPatchId, binding.ParameterId, binding.Normalize(inputEvent.RawValue));
			}
		}

		private void SelectPatch(string patchId) {
			m_Queue.EnqueueLoadPatch(patchId);
		}
	}
}
