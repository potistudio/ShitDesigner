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
		private readonly Action<int, int> m_MoveCatalogSelection;
		private readonly Action m_LaunchSelectedPatch;
		private readonly Action<double> m_TapBpm;

		public LiveKeyboardInput(LiveParameterQueue queue, IReadOnlyList<PatchDefinition> patches, Action<int, int> moveCatalogSelection, Action launchSelectedPatch, Action<double> tapBpm) {
			m_Queue = queue ?? throw new ArgumentNullException(nameof(queue));
			if (patches == null) throw new ArgumentNullException(nameof(patches));

			var patchesById = new Dictionary<string, PatchDefinition>(StringComparer.Ordinal);
			foreach (var patch in patches) {
				if (patch == null || string.IsNullOrWhiteSpace(patch.Id)) throw new ArgumentException("Every live patch requires an ID.", nameof(patches));
				if (!patchesById.TryAdd(patch.Id, patch)) throw new ArgumentException("Live patch IDs must be unique.", nameof(patches));
			}
			m_PatchesById = patchesById;
			m_MoveCatalogSelection = moveCatalogSelection ?? throw new ArgumentNullException(nameof(moveCatalogSelection));
			m_LaunchSelectedPatch = launchSelectedPatch ?? throw new ArgumentNullException(nameof(launchSelectedPatch));
			m_TapBpm = tapBpm ?? throw new ArgumentNullException(nameof(tapBpm));
		}

		public void Poll(string loadedPatchId) {
			var keyboard = Keyboard.current;
			if (keyboard == null || string.IsNullOrWhiteSpace(loadedPatchId)) return;

			if (keyboard.leftArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(-1, 0);
			if (keyboard.rightArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(1, 0);
			if (keyboard.upArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(0, -1);
			if (keyboard.downArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(0, 1);
			if (keyboard.enterKey.wasPressedThisFrame) m_LaunchSelectedPatch();
			if (keyboard.spaceKey.wasPressedThisFrame) m_TapBpm(Time.unscaledTimeAsDouble);
			QueuePatchKeyboardInputs(keyboard, loadedPatchId);
		}

		private void QueuePatchKeyboardInputs(Keyboard keyboard, string loadedPatchId) {
			if (!m_PatchesById.TryGetValue(loadedPatchId, out var patch)) return;

			foreach (var key in keyboard.allKeys) {
				if (!key.wasPressedThisFrame) continue;
				foreach (var binding in patch.KeyboardInputs) {
					if (binding == null || !binding.Matches(key.keyCode)) continue;
					m_Queue.EnqueueSetParameter(loadedPatchId, binding.ParameterId, binding.Value());
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
		}

		public void SetSelectedPatch(string patchId) => m_LoadedPatchId = patchId ?? string.Empty;

		public void Dispose() {
			m_Manager.InputReceived -= OnInputReceived;
		}

		private void OnInputReceived(MidiInputEvent inputEvent) {
			var control = inputEvent.Control;
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
