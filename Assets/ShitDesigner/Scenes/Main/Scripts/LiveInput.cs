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
		private readonly Action<int> m_TurnOnOverlaySequencerStep;
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
		private readonly Action m_BeginMomentaryMainComposite;
		private readonly Action m_EndMomentaryMainComposite;
		private readonly Action m_CompleteMainComposite;
		private readonly Action<int> m_AdjustProgramWidth;
		private readonly Action<int, bool> m_FireLiveParameter;
		private readonly Key m_BlackoutKey;
		private readonly Action<bool> m_SetBlackoutActive;
		private bool m_IsPianoMainCueSwitchHeld;
		private bool m_IsMomentaryMainCompositeHeld;
		private bool m_HasCompletedPermanentTakeForCurrentSPress;
		private bool m_IsBlackoutHeld;
		private int m_HeldPianoOverlayTakeMask;
		private int m_HeldLiveParameterMask;
		private readonly List<(Key Key, string PatchId, string ParameterId)> m_HeldPatchKeyboardInputs
			= new List<(Key Key, string PatchId, string ParameterId)>();

		public LiveKeyboardInput(LiveParameterQueue queue, IReadOnlyList<PatchDefinition> patches, Action<int> beginPianoOverlayTake, Action<int, int> moveCatalogSelection, Action launchSelectedPatch, Action<double> tapBpm,
			Action toggleEditMode = null, Action<int> assignInstantEffect = null, Func<bool> isEditMode = null, Action<int> cueInstantEffect = null,
			Action<int> focusInstantEffectParameters = null, Action toggleSelectedEffectCategory = null, Action beginPianoMainCueSwitch = null,
			Action endPianoMainCueSwitch = null, Action completeMainCueSwitch = null, Action<int> endPianoOverlayTake = null,
			Action<int> turnOnOverlaySequencerStep = null, Action<int> adjustProgramWidth = null, Action<int, bool> fireLiveParameter = null,
			Key blackoutKey = Key.Backquote, Action<bool> setBlackoutActive = null, Action beginMomentaryMainComposite = null,
			Action endMomentaryMainComposite = null, Action completeMainComposite = null) {
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
			m_TurnOnOverlaySequencerStep = turnOnOverlaySequencerStep ?? (_ => { });
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
			m_BeginMomentaryMainComposite = beginMomentaryMainComposite ?? (() => { });
			m_EndMomentaryMainComposite = endMomentaryMainComposite ?? (() => { });
			m_CompleteMainComposite = completeMainComposite ?? (() => { });
			m_AdjustProgramWidth = adjustProgramWidth ?? (_ => { });
			m_FireLiveParameter = fireLiveParameter ?? ((_, _) => { });
			m_BlackoutKey = blackoutKey;
			m_SetBlackoutActive = setBlackoutActive ?? (_ => { });
		}

		public void Poll(string loadedPatchId) {
			var keyboard = Keyboard.current;
			if (keyboard == null) {
				SetBlackoutActive(false);
				return;
			}
			SetBlackoutActive(m_BlackoutKey != Key.None && keyboard[m_BlackoutKey].isPressed);
			QueueReleasedPatchKeyboardInputs(keyboard);
			ReleaseLiveParameterKeys(keyboard);
			if (string.IsNullOrWhiteSpace(loadedPatchId)) return;
			if (!keyboard.sKey.isPressed) m_HasCompletedPermanentTakeForCurrentSPress = false;
			EndReleasedPianoOverlayTakes(keyboard);
			if (EndMomentaryMainTakeIfReleased(keyboard)) return;
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
			if (keyboard.rightArrowKey.wasPressedThisFrame) {
				m_AdjustProgramWidth(LiveGraphRuntime.ProgramWidthStep);
				return;
			}
			if (keyboard.leftArrowKey.wasPressedThisFrame) {
				m_AdjustProgramWidth(-LiveGraphRuntime.ProgramWidthStep);
				return;
			}
			if (m_IsEditMode()) {
				if (keyboard.upArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(0, -1);
				if (keyboard.downArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(0, 1);
				if (keyboard.spaceKey.wasPressedThisFrame) m_ToggleSelectedEffectCategory();
				var effectIndex = PressedInstantEffectIndex(keyboard);
				if (effectIndex >= 0) m_AssignInstantEffect(effectIndex);
				return;
			}
			if (keyboard.leftBracketKey.wasPressedThisFrame) {
				RecallHotCue(keyboard, 0);
				return;
			}
			if (keyboard.rightBracketKey.wasPressedThisFrame) {
				RecallHotCue(keyboard, 1);
				return;
			}
			if (keyboard.aKey.wasPressedThisFrame) {
				if (keyboard.shiftKey.isPressed || keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame) {
					m_IsMomentaryMainCompositeHeld = true;
					m_BeginMomentaryMainComposite();
				}
				else {
					m_IsPianoMainCueSwitchHeld = true;
					m_BeginPianoMainCueSwitch();
				}
				EndMomentaryMainTakeIfReleased(keyboard);
				return;
			}
			if (keyboard.sKey.wasPressedThisFrame) {
				CompletePermanentTake(keyboard.shiftKey.isPressed || keyboard.leftShiftKey.wasPressedThisFrame
					|| keyboard.rightShiftKey.wasPressedThisFrame);
				return;
			}

			for (var laneIndex = 0; laneIndex < LiveStepSequencer.OverlayLaneCount; laneIndex++) {
				var key = OverlayTakeKey(keyboard, laneIndex);
				if (!key.wasPressedThisFrame) continue;
				if (keyboard.shiftKey.isPressed || keyboard.leftShiftKey.wasPressedThisFrame || keyboard.rightShiftKey.wasPressedThisFrame) {
					m_HeldPianoOverlayTakeMask &= ~(1 << laneIndex);
					m_TurnOnOverlaySequencerStep(laneIndex);
				}
				else {
					m_HeldPianoOverlayTakeMask |= 1 << laneIndex;
					m_BeginPianoOverlayTake(laneIndex);
					if (!key.isPressed) EndPianoOverlayTake(laneIndex);
				}
			}
			if (keyboard.upArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(0, -1);
			if (keyboard.downArrowKey.wasPressedThisFrame) m_MoveCatalogSelection(0, 1);
			if (keyboard.enterKey.wasPressedThisFrame) m_LaunchSelectedPatch();
			if (keyboard.spaceKey.wasPressedThisFrame) m_TapBpm(Time.unscaledTimeAsDouble);
			if (CuePressedInstantEffects(keyboard)) return;
			if (FirePressedLiveParameters(keyboard)) return;
			QueuePressedPatchKeyboardInputs(keyboard, loadedPatchId);
		}

		private void SetBlackoutActive(bool active) {
			if (m_IsBlackoutHeld == active) return;
			m_IsBlackoutHeld = active;
			m_SetBlackoutActive(active);
		}

		private void RecallHotCue(Keyboard keyboard, int hotCueIndex) {
			var shiftPressed = keyboard.shiftKey.isPressed || keyboard.leftShiftKey.wasPressedThisFrame
				|| keyboard.rightShiftKey.wasPressedThisFrame;
			var shouldCompletePermanentTake = keyboard.sKey.isPressed && !m_HasCompletedPermanentTakeForCurrentSPress;
			m_Queue.EnqueueRecallHotCue(hotCueIndex, shouldCompletePermanentTake || shiftPressed);
			if (shouldCompletePermanentTake) CompletePermanentTake(shiftPressed);
		}

		private void CompletePermanentTake(bool composite) {
			if (m_HasCompletedPermanentTakeForCurrentSPress) return;
			m_HasCompletedPermanentTakeForCurrentSPress = true;
			if (composite) m_CompleteMainComposite();
			else m_CompleteMainCueSwitch();
		}

		private bool EndMomentaryMainTakeIfReleased(Keyboard keyboard) {
			if (keyboard.aKey.isPressed) return false;
			if (m_IsPianoMainCueSwitchHeld) {
				m_IsPianoMainCueSwitchHeld = false;
				m_EndPianoMainCueSwitch();
				return true;
			}
			if (!m_IsMomentaryMainCompositeHeld) return false;
			m_IsMomentaryMainCompositeHeld = false;
			m_EndMomentaryMainComposite();
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

		private bool FirePressedLiveParameters(Keyboard keyboard) {
			var fired = false;
			for (var parameterIndex = 0; parameterIndex < 7; parameterIndex++) {
				if (!LiveParameterKey(keyboard, parameterIndex).wasPressedThisFrame) continue;
				m_HeldLiveParameterMask |= 1 << parameterIndex;
				m_FireLiveParameter(parameterIndex, true);
				fired = true;
			}
			return fired;
		}

		private void ReleaseLiveParameterKeys(Keyboard keyboard) {
			for (var parameterIndex = 0; parameterIndex < 7; parameterIndex++) {
				var mask = 1 << parameterIndex;
				if ((m_HeldLiveParameterMask & mask) == 0 || LiveParameterKey(keyboard, parameterIndex).isPressed) continue;
				m_HeldLiveParameterMask &= ~mask;
				m_FireLiveParameter(parameterIndex, false);
			}
		}

		private static KeyControl LiveParameterKey(Keyboard keyboard, int parameterIndex) {
			switch (parameterIndex) {
				case 0: return keyboard.zKey;
				case 1: return keyboard.xKey;
				case 2: return keyboard.cKey;
				case 3: return keyboard.vKey;
				case 4: return keyboard.bKey;
				case 5: return keyboard.nKey;
				case 6: return keyboard.mKey;
				default: throw new ArgumentOutOfRangeException(nameof(parameterIndex));
			}
		}

		private void QueuePressedPatchKeyboardInputs(Keyboard keyboard, string loadedPatchId) {
			if (!m_PatchesById.TryGetValue(loadedPatchId, out var patch)) return;

			foreach (var key in keyboard.allKeys) {
				if (!key.wasPressedThisFrame) continue;
				if (key.keyCode == m_BlackoutKey) continue;
				foreach (var binding in patch.KeyboardInputs) {
					if (binding == null || !binding.Matches(key.keyCode)) continue;
					m_Queue.EnqueueSetParameter(loadedPatchId, binding.ParameterId, binding.Value(true));
					m_HeldPatchKeyboardInputs.Add((key.keyCode, loadedPatchId, binding.ParameterId));
				}
				if (key.wasReleasedThisFrame) ReleasePatchKeyboardInput(key.keyCode);
			}
		}

		private void QueueReleasedPatchKeyboardInputs(Keyboard keyboard) {
			foreach (var key in keyboard.allKeys)
				if (key.wasReleasedThisFrame) ReleasePatchKeyboardInput(key.keyCode);
		}

		private void ReleasePatchKeyboardInput(Key key) {
			for (var index = m_HeldPatchKeyboardInputs.Count - 1; index >= 0; index--) {
				var held = m_HeldPatchKeyboardInputs[index];
				if (held.Key != key) continue;
				m_Queue.EnqueueSetParameter(held.PatchId, held.ParameterId, 0f);
				m_HeldPatchKeyboardInputs.RemoveAt(index);
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
			int sceneTimeEncoderControlNumber = 77, float sceneTimeJogSpeedPerStep = 1f) {
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
