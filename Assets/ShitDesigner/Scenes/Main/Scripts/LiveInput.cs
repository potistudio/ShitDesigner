using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Input;
using ShitDesigner.Runtime;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace ShitDesigner.Main {
	/// <summary>Maps live keyboard controls to live requests without owning a PlayerLoop.</summary>
	public sealed class LiveKeyboardInput {
		private readonly LiveParameterQueue m_Queue;
		private readonly IReadOnlyDictionary<string, PatchDefinition> m_PatchesById;
		private readonly Action<int> m_BeginPianoOverlayTake;
		private readonly Action<int> m_EndPianoOverlayTake;
		private readonly Action<int> m_CompleteOverlayTake;
		private readonly Action<int, int> m_MoveCatalogSelection;
		private readonly Action m_LaunchSelectedPatch;
		private readonly Action<double> m_TapBpm;
		private readonly Action m_ToggleEditMode;
		private readonly Action<int> m_AssignInstantEffect;
		private readonly Func<bool> m_IsEditMode;
		private readonly Action<int> m_CueInstantEffect;
		private readonly Action<int> m_FocusInstantEffectParameters;
		private readonly Action m_ToggleSelectedEffectCategory;
		private readonly Action m_BeginPianoMainCueSwitch;
		private readonly Action m_EndPianoMainCueSwitch;
		private readonly Action m_CompleteMainCueSwitch;
		private bool m_IsPianoMainCueSwitchHeld;
		private int m_HeldPianoOverlayTakeMask;

		public LiveKeyboardInput(LiveParameterQueue queue, IReadOnlyList<PatchDefinition> patches, Action<int> beginPianoOverlayTake, Action<int, int> moveCatalogSelection, Action launchSelectedPatch, Action<double> tapBpm,
			Action toggleEditMode = null, Action<int> assignInstantEffect = null, Func<bool> isEditMode = null, Action<int> cueInstantEffect = null,
			Action<int> focusInstantEffectParameters = null, Action toggleSelectedEffectCategory = null, Action beginPianoMainCueSwitch = null,
			Action endPianoMainCueSwitch = null, Action completeMainCueSwitch = null, Action<int> endPianoOverlayTake = null,
			Action<int> completeOverlayTake = null) {
			m_Queue = queue ?? throw new ArgumentNullException(nameof(queue));
			if (patches == null) throw new ArgumentNullException(nameof(patches));

			var patchesById = new Dictionary<string, PatchDefinition>(StringComparer.Ordinal);
			foreach (var patch in patches) {
				if (patch == null || string.IsNullOrWhiteSpace(patch.Id)) throw new ArgumentException("Every live patch requires an ID.", nameof(patches));
				if (!patchesById.TryAdd(patch.Id, patch)) throw new ArgumentException("Live patch IDs must be unique.", nameof(patches));
			}
			m_PatchesById = patchesById;
			m_BeginPianoOverlayTake = beginPianoOverlayTake ?? throw new ArgumentNullException(nameof(beginPianoOverlayTake));
			m_EndPianoOverlayTake = endPianoOverlayTake ?? (_ => { });
			m_CompleteOverlayTake = completeOverlayTake ?? (_ => { });
			m_MoveCatalogSelection = moveCatalogSelection ?? throw new ArgumentNullException(nameof(moveCatalogSelection));
			m_LaunchSelectedPatch = launchSelectedPatch ?? throw new ArgumentNullException(nameof(launchSelectedPatch));
			m_TapBpm = tapBpm ?? throw new ArgumentNullException(nameof(tapBpm));
			m_ToggleEditMode = toggleEditMode ?? (() => { });
			m_AssignInstantEffect = assignInstantEffect ?? (_ => { });
			m_IsEditMode = isEditMode ?? (() => false);
			m_CueInstantEffect = cueInstantEffect ?? (_ => { });
			m_FocusInstantEffectParameters = focusInstantEffectParameters ?? (_ => { });
			m_ToggleSelectedEffectCategory = toggleSelectedEffectCategory ?? (() => { });
			m_BeginPianoMainCueSwitch = beginPianoMainCueSwitch ?? (() => { });
			m_EndPianoMainCueSwitch = endPianoMainCueSwitch ?? (() => { });
			m_CompleteMainCueSwitch = completeMainCueSwitch ?? (() => { });
		}

		public void Poll(string loadedPatchId) {
			var keyboard = Keyboard.current;
			if (keyboard == null || string.IsNullOrWhiteSpace(loadedPatchId)) return;
			EndReleasedPianoOverlayTakes(keyboard);
			if (EndPianoMainCueSwitchIfReleased(keyboard)) return;
			if (keyboard.tabKey.wasPressedThisFrame && keyboard.shiftKey.isPressed) {
				m_ToggleEditMode();
				return;
			}
			if (keyboard.shiftKey.isPressed) {
				var parameterCueIndex = PressedInstantEffectIndex(keyboard);
				if (parameterCueIndex >= 0) {
					m_FocusInstantEffectParameters(parameterCueIndex);
					return;
				}
			}
			if (m_IsEditMode()) {
				if (keyboard.upArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(0, -1);
				if (keyboard.downArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(0, 1);
				if (keyboard.spaceKey.wasPressedThisFrame) m_ToggleSelectedEffectCategory();
				var effectIndex = PressedInstantEffectIndex(keyboard);
				if (effectIndex >= 0) m_AssignInstantEffect(effectIndex);
				return;
			}
			if (keyboard.aKey.wasPressedThisFrame) {
				if (keyboard.shiftKey.isPressed || keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame) {
					m_IsPianoMainCueSwitchHeld = false;
					m_CompleteMainCueSwitch();
				}
				else {
					m_IsPianoMainCueSwitchHeld = true;
					m_BeginPianoMainCueSwitch();
					EndPianoMainCueSwitchIfReleased(keyboard);
				}
				return;
			}

			for (var laneIndex = 0; laneIndex < LiveStepSequencer.OverlayLaneCount; laneIndex++) {
				var key = OverlayTakeKey(keyboard, laneIndex);
				if (!key.wasPressedThisFrame) continue;
				if (keyboard.shiftKey.isPressed || keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame) {
					m_HeldPianoOverlayTakeMask &= ~(1 << laneIndex);
					m_CompleteOverlayTake(laneIndex);
				}
				else {
					m_HeldPianoOverlayTakeMask |= 1 << laneIndex;
					m_BeginPianoOverlayTake(laneIndex);
					if (!key.isPressed) EndPianoOverlayTake(laneIndex);
				}
			}
			if (keyboard.leftArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(-1, 0);
			if (keyboard.rightArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(1, 0);
			if (keyboard.upArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(0, -1);
			if (keyboard.downArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(0, 1);
			if (keyboard.enterKey.wasPressedThisFrame) m_LaunchSelectedPatch();
			if (keyboard.spaceKey.wasPressedThisFrame) m_TapBpm(Time.unscaledTimeAsDouble);
			if (CuePressedInstantEffects(keyboard)) return;
			QueuePatchKeyboardInputs(keyboard, loadedPatchId);
		}

		private bool EndPianoMainCueSwitchIfReleased(Keyboard keyboard) {
			if (!m_IsPianoMainCueSwitchHeld || keyboard.aKey.isPressed) return false;
			m_IsPianoMainCueSwitchHeld = false;
			m_EndPianoMainCueSwitch();
			return true;
		}

		private void EndReleasedPianoOverlayTakes(Keyboard keyboard) {
			for (var laneIndex = 0; laneIndex < LiveStepSequencer.OverlayLaneCount; laneIndex++)
				if ((m_HeldPianoOverlayTakeMask & (1 << laneIndex)) != 0 && !OverlayTakeKey(keyboard, laneIndex).isPressed)
					EndPianoOverlayTake(laneIndex);
		}

		private void EndPianoOverlayTake(int laneIndex) {
			m_HeldPianoOverlayTakeMask &= ~(1 << laneIndex);
			m_EndPianoOverlayTake(laneIndex);
		}

		private static KeyControl OverlayTakeKey(Keyboard keyboard, int laneIndex) {
			switch (laneIndex) {
				case 0: return keyboard.digit1Key;
				case 1: return keyboard.digit2Key;
				case 2: return keyboard.digit3Key;
				case 3: return keyboard.digit4Key;
				case 4: return keyboard.digit5Key;
				case 5: return keyboard.digit6Key;
				case 6: return keyboard.digit7Key;
				case 7: return keyboard.digit8Key;
				default: throw new ArgumentOutOfRangeException(nameof(laneIndex));
			}
		}

		private bool CuePressedInstantEffects(Keyboard keyboard) {
			var fired = false;
			if (keyboard.qKey.wasPressedThisFrame) { m_CueInstantEffect(1); fired = true; }
			if (keyboard.wKey.wasPressedThisFrame) { m_CueInstantEffect(2); fired = true; }
			if (keyboard.eKey.wasPressedThisFrame) { m_CueInstantEffect(3); fired = true; }
			if (keyboard.rKey.wasPressedThisFrame) { m_CueInstantEffect(4); fired = true; }
			if (keyboard.tKey.wasPressedThisFrame) { m_CueInstantEffect(5); fired = true; }
			if (keyboard.yKey.wasPressedThisFrame) { m_CueInstantEffect(6); fired = true; }
			if (keyboard.uKey.wasPressedThisFrame) { m_CueInstantEffect(7); fired = true; }
			if (keyboard.iKey.wasPressedThisFrame) { m_CueInstantEffect(8); fired = true; }
			if (keyboard.oKey.wasPressedThisFrame) { m_CueInstantEffect(9); fired = true; }
			if (keyboard.pKey.wasPressedThisFrame) { m_CueInstantEffect(10); fired = true; }
			return fired;
		}

		private static int PressedInstantEffectIndex(Keyboard keyboard) {
			if (keyboard.qKey.wasPressedThisFrame) return 0;
			if (keyboard.wKey.wasPressedThisFrame) return 1;
			if (keyboard.eKey.wasPressedThisFrame) return 2;
			if (keyboard.rKey.wasPressedThisFrame) return 3;
			if (keyboard.tKey.wasPressedThisFrame) return 4;
			if (keyboard.yKey.wasPressedThisFrame) return 5;
			if (keyboard.uKey.wasPressedThisFrame) return 6;
			if (keyboard.iKey.wasPressedThisFrame) return 7;
			if (keyboard.oKey.wasPressedThisFrame) return 8;
			if (keyboard.pKey.wasPressedThisFrame) return 9;
			return -1;
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

	public sealed class LiveBeatQuantizedTriggerQueue {
		private const double BeatBoundaryTolerance = 1e-9d;
		private readonly Dictionary<int, long> m_TargetBeats = new Dictionary<int, long>();

		public void Enqueue(int triggerNumber, double adjustedTotalBeats) {
			InstantEffectTriggerContract.Validate(triggerNumber);
			if (double.IsNaN(adjustedTotalBeats) || double.IsInfinity(adjustedTotalBeats))
				throw new ArgumentOutOfRangeException(nameof(adjustedTotalBeats));
			var targetBeat = checked((long)Math.Floor(adjustedTotalBeats + BeatBoundaryTolerance) + 1L);
			if (!m_TargetBeats.TryGetValue(triggerNumber, out var existingTarget) || targetBeat < existingTarget)
				m_TargetBeats[triggerNumber] = targetBeat;
		}

		public IReadOnlyList<int> DrainDue(double adjustedTotalBeats) {
			if (double.IsNaN(adjustedTotalBeats) || double.IsInfinity(adjustedTotalBeats))
				throw new ArgumentOutOfRangeException(nameof(adjustedTotalBeats));
			var reachedBeat = checked((long)Math.Floor(adjustedTotalBeats + BeatBoundaryTolerance));
			var due = m_TargetBeats.Where(item => item.Value <= reachedBeat).Select(item => item.Key).OrderBy(value => value).ToArray();
			foreach (var triggerNumber in due) m_TargetBeats.Remove(triggerNumber);
			return due;
		}
	}

	public sealed class LiveBeatEffectGate {
		private const double BeatBoundaryTolerance = 1e-9d;
		private readonly Dictionary<int, long> m_ActiveUntilBeats = new Dictionary<int, long>();

		public void Activate(IEnumerable<int> triggerNumbers, double adjustedTotalBeats) {
			if (double.IsNaN(adjustedTotalBeats) || double.IsInfinity(adjustedTotalBeats))
				throw new ArgumentOutOfRangeException(nameof(adjustedTotalBeats));
			var activeUntilBeat = checked((long)Math.Floor(adjustedTotalBeats + BeatBoundaryTolerance) + 1L);
			foreach (var triggerNumber in triggerNumbers ?? Enumerable.Empty<int>()) {
				InstantEffectTriggerContract.Validate(triggerNumber);
				m_ActiveUntilBeats[triggerNumber] = activeUntilBeat;
			}
		}

		public IReadOnlyList<int> GetActive(double adjustedTotalBeats) {
			if (double.IsNaN(adjustedTotalBeats) || double.IsInfinity(adjustedTotalBeats))
				throw new ArgumentOutOfRangeException(nameof(adjustedTotalBeats));
			var reachedBeat = checked((long)Math.Floor(adjustedTotalBeats + BeatBoundaryTolerance));
			foreach (var triggerNumber in m_ActiveUntilBeats.Where(item => item.Value <= reachedBeat).Select(item => item.Key).ToArray())
				m_ActiveUntilBeats.Remove(triggerNumber);
			return m_ActiveUntilBeats.Keys.OrderBy(value => value).ToArray();
		}
	}

	/// <summary>Maps MIDI events to live requests without owning the MIDI device lifecycle.</summary>
	public sealed class LiveMidiInput : IDisposable {
		private const int PatchSelectionChannel = 1;
		private const int RelativeEncoderPivot = 64;
		private readonly MidiInputManager m_Manager;
		private readonly LiveParameterQueue m_Queue;
		private readonly IReadOnlyList<string> m_PatchIds;
		private readonly IReadOnlyDictionary<string, PatchDefinition> m_PatchesById;
		private readonly int m_MainCueFaderChannel;
		private readonly int m_MainCueFaderControlNumber;
		private readonly int m_SceneTimeEncoderChannel;
		private readonly int m_SceneTimeEncoderControlNumber;
		private readonly float m_SceneTimeJogSpeedPerStep;
		private string m_LoadedPatchId;

		public LiveMidiInput(MidiInputManager manager, LiveParameterQueue queue, IReadOnlyList<PatchDefinition> patches,
			int mainCueFaderChannel = 16, int mainCueFaderControlNumber = 5, int sceneTimeEncoderChannel = 16,
			int sceneTimeEncoderControlNumber = 77, float sceneTimeJogSpeedPerStep = .25f) {
			m_Manager = manager ?? throw new ArgumentNullException(nameof(manager));
			m_Queue = queue ?? throw new ArgumentNullException(nameof(queue));
			if (patches == null) throw new ArgumentNullException(nameof(patches));
			if (mainCueFaderChannel < 1 || mainCueFaderChannel > 16) throw new ArgumentOutOfRangeException(nameof(mainCueFaderChannel));
			if (mainCueFaderControlNumber < 0 || mainCueFaderControlNumber > 127) throw new ArgumentOutOfRangeException(nameof(mainCueFaderControlNumber));
			if (sceneTimeEncoderChannel < 1 || sceneTimeEncoderChannel > 16) throw new ArgumentOutOfRangeException(nameof(sceneTimeEncoderChannel));
			if (sceneTimeEncoderControlNumber < 0 || sceneTimeEncoderControlNumber > 127) throw new ArgumentOutOfRangeException(nameof(sceneTimeEncoderControlNumber));
			if (float.IsNaN(sceneTimeJogSpeedPerStep) || float.IsInfinity(sceneTimeJogSpeedPerStep) || sceneTimeJogSpeedPerStep <= 0f)
				throw new ArgumentOutOfRangeException(nameof(sceneTimeJogSpeedPerStep));
			m_MainCueFaderChannel = mainCueFaderChannel;
			m_MainCueFaderControlNumber = mainCueFaderControlNumber;
			m_SceneTimeEncoderChannel = sceneTimeEncoderChannel;
			m_SceneTimeEncoderControlNumber = sceneTimeEncoderControlNumber;
			m_SceneTimeJogSpeedPerStep = sceneTimeJogSpeedPerStep;

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
			if (control.Kind == MidiControlKind.ControlChange && control.Channel == m_SceneTimeEncoderChannel
				&& control.Number == m_SceneTimeEncoderControlNumber) {
				var steps = inputEvent.RawValue - RelativeEncoderPivot;
				if (steps != 0) m_Queue.EnqueueJogSceneTime(steps * m_SceneTimeJogSpeedPerStep);
				return;
			}
			if (control.Kind == MidiControlKind.ControlChange && control.Channel == m_MainCueFaderChannel
				&& control.Number == m_MainCueFaderControlNumber) {
				m_Queue.EnqueueSetMainCueFader(inputEvent.RawValue / (float)control.RawMaximum);
				return;
			}
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
