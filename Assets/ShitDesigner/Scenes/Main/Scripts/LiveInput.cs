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
