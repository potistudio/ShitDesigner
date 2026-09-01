using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Application;
using ShitDesigner.Core;
using ShitDesigner.Input;
using ShitDesigner.Scene;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LiveInputTests {
		[Test]
		public void KeyboardMappingQueuesPressedAndReleasedParameterRequestsForLoadedPatch() {
			var patch = CreateKeyboardPatch("patch-a", new PatchKeyboardInputBinding("motion", Key.B));
			Keyboard keyboard = null;
			try {
				keyboard = InputSystem.AddDevice<Keyboard>();
				keyboard.MakeCurrent();
				var queue = new LiveParameterQueue();
				var input = new LiveKeyboardInput(queue, new[] { patch }, _ => { }, (_, _) => { }, () => { }, _ => { });

				InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.B));
				InputSystem.Update();
				input.Poll("patch-a");
				InputSystem.QueueStateEvent(keyboard, new KeyboardState());
				InputSystem.Update();
				input.Poll("patch-a");

				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(requests, Has.Count.EqualTo(2));
				Assert.That(requests.All(request => request.Kind == LiveParameterRequestKind.SetParameter), Is.True);
				Assert.That(requests.All(request => request.ParameterId == "motion"), Is.True);
				Assert.That(requests.Select(request => request.Value), Is.EqualTo(new[] { 1f, 0f }));
			}
			finally {
				if (keyboard != null) InputSystem.RemoveDevice(keyboard);
				Object.DestroyImmediate(patch);
			}
		}

		[Test]
		public void KeyboardArrowsMoveCatalogBetweenTabsAndWithinLists() {
			var patch = CreatePatch("patch-a");
			Keyboard keyboard = null;
			try {
				keyboard = InputSystem.AddDevice<Keyboard>();
				keyboard.MakeCurrent();
				var movements = new List<(int Horizontal, int Vertical)>();
				var input = new LiveKeyboardInput(new LiveParameterQueue(), new[] { patch }, _ => { },
					(horizontal, vertical) => movements.Add((horizontal, vertical)), () => { }, _ => { });

				PollKey(input, keyboard, Key.LeftArrow);
				PollKey(input, keyboard, Key.RightArrow);
				PollKey(input, keyboard, Key.UpArrow);
				PollKey(input, keyboard, Key.DownArrow);

				Assert.That(movements, Is.EqualTo(new[] { (-1, 0), (1, 0), (0, -1), (0, 1) }));
			}
			finally {
				if (keyboard != null) InputSystem.RemoveDevice(keyboard);
				Object.DestroyImmediate(patch);
			}
		}

		[Test]
		public void KeyboardEnterLaunchesSelectedCatalogPatch() {
			var patch = CreatePatch("patch-a");
			Keyboard keyboard = null;
			try {
				keyboard = InputSystem.AddDevice<Keyboard>();
				keyboard.MakeCurrent();
				var launched = false;
				var input = new LiveKeyboardInput(new LiveParameterQueue(), new[] { patch }, _ => { }, (_, _) => { }, () => { launched = true; }, _ => { });

				PollKey(input, keyboard, Key.Enter);

				Assert.That(launched, Is.True);
			}
			finally {
				if (keyboard != null) InputSystem.RemoveDevice(keyboard);
				Object.DestroyImmediate(patch);
			}
		}

		[Test]
		public void KeyboardBracketsRecallOnlyTheTwoHotCueSlots() {
			Keyboard keyboard = null;
			try {
				keyboard = InputSystem.AddDevice<Keyboard>();
				keyboard.MakeCurrent();
				var queue = new LiveParameterQueue();
				var input = new LiveKeyboardInput(queue, new PatchDefinition[0], _ => { }, (_, _) => { }, () => { }, _ => { });

				PollKey(input, keyboard, Key.LeftBracket);
				PollKey(input, keyboard, Key.RightBracket);

				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(requests.Select(request => request.Kind), Is.EqualTo(new[] {
					LiveParameterRequestKind.RecallHotCue,
					LiveParameterRequestKind.RecallHotCue
				}));
				Assert.That(requests.Select(request => request.ParameterValue.AsInt()), Is.EqualTo(new[] { 0, 1 }));
			}
			finally {
				if (keyboard != null) InputSystem.RemoveDevice(keyboard);
			}
		}

		[Test]
		public void ShiftBracketsRecallTheOppositeScenesHotCueSlots() {
			Keyboard keyboard = null;
			try {
				keyboard = InputSystem.AddDevice<Keyboard>();
				keyboard.MakeCurrent();
				var queue = new LiveParameterQueue();
				var input = new LiveKeyboardInput(queue, new PatchDefinition[0], _ => { }, (_, _) => { }, () => { }, _ => { });

				PollKey(input, keyboard, Key.LeftBracket, Key.LeftShift);
				PollKey(input, keyboard, Key.RightBracket, Key.LeftShift);

				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(requests.Select(request => request.Kind), Is.EqualTo(new[] {
					LiveParameterRequestKind.RecallOppositeHotCue,
					LiveParameterRequestKind.RecallOppositeHotCue
				}));
				Assert.That(requests.Select(request => request.ParameterValue.AsInt()), Is.EqualTo(new[] { 0, 1 }));
			}
			finally {
				if (keyboard != null) InputSystem.RemoveDevice(keyboard);
			}
		}

		[Test]
		public void ShiftAAndBracketRecallTheOppositeHotCueBeforeSwitching() {
			Keyboard keyboard = null;
			try {
				keyboard = InputSystem.AddDevice<Keyboard>();
				keyboard.MakeCurrent();
				var queue = new LiveParameterQueue();
				var input = new LiveKeyboardInput(queue, new PatchDefinition[0], _ => { }, (_, _) => { }, () => { }, _ => { },
					completeMainCueSwitch: () => queue.EnqueueToggleMainCue());

				PollKey(input, keyboard, Key.LeftShift, Key.A, Key.LeftBracket);

				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(requests.Select(request => request.Kind), Is.EqualTo(new[] {
					LiveParameterRequestKind.RecallOppositeHotCue,
					LiveParameterRequestKind.ToggleMainCue
				}));
				Assert.That(requests[0].ParameterValue.AsInt(), Is.Zero);
			}
			finally {
				if (keyboard != null) InputSystem.RemoveDevice(keyboard);
			}
		}

		[Test]
		public void ShiftAAndBracketStayOnTargetAcrossAdjacentFrameOrder() {
			Keyboard keyboard = null;
			try {
				keyboard = InputSystem.AddDevice<Keyboard>();
				keyboard.MakeCurrent();
				var queue = new LiveParameterQueue();
				var input = new LiveKeyboardInput(queue, new PatchDefinition[0], _ => { }, (_, _) => { }, () => { }, _ => { },
					completeMainCueSwitch: () => queue.EnqueueToggleMainCue());

				InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift, Key.A));
				InputSystem.Update();
				input.Poll("patch-a");
				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(requests.Select(request => request.Kind), Is.EqualTo(new[] { LiveParameterRequestKind.ToggleMainCue }));

				InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift, Key.A, Key.LeftBracket));
				InputSystem.Update();
				input.Poll("patch-a");
				requests.Clear();
				queue.Drain(requests);
				Assert.That(requests.Select(request => request.Kind), Is.EqualTo(new[] { LiveParameterRequestKind.RecallHotCue }));
				Assert.That(requests[0].ParameterValue.AsInt(), Is.Zero);

				InputSystem.QueueStateEvent(keyboard, new KeyboardState());
				InputSystem.Update();
				input.Poll("patch-a");
				InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift, Key.LeftBracket));
				InputSystem.Update();
				input.Poll("patch-a");
				InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift, Key.A));
				InputSystem.Update();
				input.Poll("patch-a");
				requests.Clear();
				queue.Drain(requests);
				Assert.That(requests.Select(request => request.Kind), Is.EqualTo(new[] {
					LiveParameterRequestKind.RecallOppositeHotCue,
					LiveParameterRequestKind.ToggleMainCue
				}));
			}
			finally {
				if (keyboard != null) InputSystem.RemoveDevice(keyboard);
			}
		}

		[Test]
		public void KeyboardAUsesPianoSwitchAndShiftASwitchesCompletely() {
			var patch = CreateKeyboardPatch("patch-a", new PatchKeyboardInputBinding("motion", Key.A));
			Keyboard keyboard = null;
			try {
				keyboard = InputSystem.AddDevice<Keyboard>();
				keyboard.MakeCurrent();
				var queue = new LiveParameterQueue();
				var switches = new List<string>();
				var input = new LiveKeyboardInput(queue, new[] { patch }, _ => { }, (_, _) => { }, () => { }, _ => { },
					beginPianoMainCueSwitch: () => switches.Add("begin"),
					endPianoMainCueSwitch: () => switches.Add("end"),
					completeMainCueSwitch: () => switches.Add("complete"));

				InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A));
				InputSystem.Update();
				input.Poll("patch-a");
				InputSystem.QueueStateEvent(keyboard, new KeyboardState());
				InputSystem.Update();
				input.Poll("patch-a");
				InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A));
				InputSystem.QueueStateEvent(keyboard, new KeyboardState());
				InputSystem.Update();
				input.Poll("patch-a");
				InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift, Key.A));
				InputSystem.QueueStateEvent(keyboard, new KeyboardState());
				InputSystem.Update();
				input.Poll("patch-a");

				Assert.That(switches, Is.EqualTo(new[] { "begin", "end", "begin", "end", "complete" }));
				Assert.That(queue.Count, Is.Zero);
			}
			finally {
				if (keyboard != null) InputSystem.RemoveDevice(keyboard);
				Object.DestroyImmediate(patch);
			}
		}

		[Test]
		public void MainCueFaderUsesItsCurrentPositionAsTheToggleReference() {
			var fader = new LiveMainCueFader();

			fader.SetPosition(1f);
			Assert.That(fader.ReferenceCueIndex, Is.Zero);
			Assert.That(fader.AlternateOpacity, Is.Zero);

			fader.SetPosition(0f);
			Assert.That(fader.DominantCueIndex, Is.EqualTo(1));
			Assert.That(fader.AlternateOpacity, Is.EqualTo(1f));

			fader.ToggleReferenceCue();
			Assert.That(fader.ReferenceCueIndex, Is.Zero);
			Assert.That(fader.AlternateOpacity, Is.Zero);

			fader.SetPosition(1f);
			Assert.That(fader.DominantCueIndex, Is.EqualTo(1));
			Assert.That(fader.AlternateOpacity, Is.EqualTo(1f));
		}

		[Test]
		public void KeyboardDigitsUsePianoAndShiftTurnsOnTheCurrentBeatForMatchingOverlayLanes() {
			var patch = CreatePatch("patch-a");
			Keyboard keyboard = null;
			try {
				keyboard = InputSystem.AddDevice<Keyboard>();
				keyboard.MakeCurrent();
				var takes = new List<string>();
				var input = new LiveKeyboardInput(new LiveParameterQueue(), new[] { patch },
					laneIndex => takes.Add("begin:" + laneIndex), (_, _) => { }, () => { }, _ => { },
					endPianoOverlayTake: laneIndex => takes.Add("end:" + laneIndex),
					turnOnOverlaySequencerStep: laneIndex => takes.Add("turn-on:" + laneIndex));

				foreach (var key in new[] { Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8 })
					PollKey(input, keyboard, key);
				InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift, Key.Digit4));
				InputSystem.Update();
				input.Poll("patch-a");

				Assert.That(takes, Is.EqualTo(new[] {
					"begin:0", "end:0", "begin:1", "end:1", "begin:2", "end:2", "begin:3", "end:3",
					"begin:4", "end:4", "begin:5", "end:5", "begin:6", "end:6", "begin:7", "end:7",
					"turn-on:3"
				}));
			}
			finally {
				if (keyboard != null) InputSystem.RemoveDevice(keyboard);
				Object.DestroyImmediate(patch);
			}
		}

		[Test]
		public void EditModeUsesQwertyRowForEffectReplacementAndSuppressesLiveControls() {
			var patch = CreateKeyboardPatch("patch-a", new PatchKeyboardInputBinding("motion", Key.Q));
			Keyboard keyboard = null;
			try {
				keyboard = InputSystem.AddDevice<Keyboard>();
				keyboard.MakeCurrent();
				var queue = new LiveParameterQueue();
				var editMode = false;
				var assignedEffects = new List<int>();
				var launched = false;
				var categoryToggleCount = 0;
				var input = new LiveKeyboardInput(queue, new[] { patch }, _ => { }, (_, _) => { }, () => { launched = true; }, _ => { },
					() => { editMode = !editMode; }, assignedEffects.Add, () => editMode,
					toggleSelectedEffectCategory: () => { categoryToggleCount++; });

				InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift, Key.Tab));
				InputSystem.Update();
				input.Poll("patch-a");
				InputSystem.QueueStateEvent(keyboard, new KeyboardState());
				InputSystem.Update();
				input.Poll("patch-a");
				PollKey(input, keyboard, Key.Q);
				PollKey(input, keyboard, Key.Space);
				PollKey(input, keyboard, Key.Enter);

				Assert.That(editMode, Is.True);
				Assert.That(assignedEffects, Is.EqualTo(new[] { 0 }));
				Assert.That(launched, Is.False);
				Assert.That(categoryToggleCount, Is.EqualTo(1));
				Assert.That(queue.Count, Is.Zero);
			}
			finally {
				if (keyboard != null) InputSystem.RemoveDevice(keyboard);
				Object.DestroyImmediate(patch);
			}
		}

		[Test]
		public void InstantEffectKeysQueueGlobalCuesWithoutDrivingPatchBindings() {
			var patch = CreateKeyboardPatch("patch-a", new PatchKeyboardInputBinding("motion", Key.Q));
			Keyboard keyboard = null;
			try {
				keyboard = InputSystem.AddDevice<Keyboard>();
				keyboard.MakeCurrent();
				var queue = new LiveParameterQueue();
				var cues = new List<int>();
				var input = new LiveKeyboardInput(queue, new[] { patch }, _ => { }, (_, _) => { }, () => { }, _ => { },
					cueInstantEffect: cues.Add);

				PollKey(input, keyboard, Key.Q);

				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(cues, Is.EqualTo(new[] { 1 }));
				Assert.That(requests, Is.Empty);
			}
			finally {
				if (keyboard != null) InputSystem.RemoveDevice(keyboard);
				Object.DestroyImmediate(patch);
			}
		}

		[Test]
		public void ShiftQwertyRowFocusesInstantEffectParametersWithoutTriggeringOrAssigning() {
			var patch = CreateKeyboardPatch("patch-a", new PatchKeyboardInputBinding("motion", Key.Q));
			Keyboard keyboard = null;
			try {
				keyboard = InputSystem.AddDevice<Keyboard>();
				keyboard.MakeCurrent();
				var queue = new LiveParameterQueue();
				var assigned = new List<int>();
				var triggered = new List<int>();
				var focused = new List<int>();
				var input = new LiveKeyboardInput(queue, new[] { patch }, _ => { }, (_, _) => { }, () => { }, _ => { },
					assignInstantEffect: assigned.Add, cueInstantEffect: triggered.Add, focusInstantEffectParameters: focused.Add);

				InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.LeftShift, Key.Q));
				InputSystem.Update();
				input.Poll("patch-a");

				Assert.That(focused, Is.EqualTo(new[] { 0 }));
				Assert.That(assigned, Is.Empty);
				Assert.That(triggered, Is.Empty);
				Assert.That(queue.Count, Is.Zero);
			}
			finally {
				if (keyboard != null) InputSystem.RemoveDevice(keyboard);
				Object.DestroyImmediate(patch);
			}
		}

		[Test]
		public void InstantEffectTriggersWaitForTheNextBeatAndDeduplicatePerCue() {
			var queue = new LiveBeatQuantizedTriggerQueue();

			queue.Enqueue(3, 12.25d);
			queue.Enqueue(3, 12.75d);
			queue.Enqueue(1, 12.9d);

			Assert.That(queue.DrainDue(12.999d), Is.Empty);
			Assert.That(queue.DrainDue(13d), Is.EqualTo(new[] { 1, 3 }));
			Assert.That(queue.DrainDue(14d), Is.Empty);
		}

		[Test]
		public void InstantEffectTriggerOnABeatWaitsForTheFollowingBeat() {
			var queue = new LiveBeatQuantizedTriggerQueue();

			queue.Enqueue(1, 8d);

			Assert.That(queue.DrainDue(8.999d), Is.Empty);
			Assert.That(queue.DrainDue(9d), Is.EqualTo(new[] { 1 }));
		}

		[Test]
		public void FiredInstantEffectRemainsActiveUntilTheFollowingBeat() {
			var gate = new LiveBeatEffectGate();

			gate.Activate(new[] { 3, 1 }, 13d);

			Assert.That(gate.GetActive(13d), Is.EqualTo(new[] { 1, 3 }));
			Assert.That(gate.GetActive(13.999d), Is.EqualTo(new[] { 1, 3 }));
			Assert.That(gate.GetActive(14d), Is.Empty);
		}

		[Test]
		public void MidiMappingQueuesPreloadedPatchLoadAndParameterRequestsInEventOrder() {
			var owner = new GameObject("MIDI");
			var patchA = CreatePatch("patch-a", new PatchMidiInputBinding("motion", MidiControlKind.ControlChange, 1, 74));
			var patchB = CreatePatch("patch-b");
			try {
				var manager = owner.AddComponent<MidiInputManager>();
				var source = new QueueMidiInputSource();
				manager.Configure(new NullMidiApplication(), new NullLiveControlApplication(), source);
				var queue = new LiveParameterQueue();
				using (var input = new LiveMidiInput(manager, queue, new[] { patchA, patchB })) {
					input.SetSelectedPatch("patch-a");
					source.Enqueue(new MidiInputEvent(new MidiControl("Test", MidiControlKind.Note, 1, 37), 127));
					source.Enqueue(new MidiInputEvent(new MidiControl("Test", MidiControlKind.ControlChange, 1, 74), 64));
					manager.Poll();
				}

				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(requests.Count, Is.EqualTo(2));
				Assert.That(requests[0].Kind, Is.EqualTo(LiveParameterRequestKind.LoadPatch));
				Assert.That(requests[0].PatchId, Is.EqualTo("patch-b"));
				Assert.That(requests[1].PatchId, Is.EqualTo("patch-a"));
				Assert.That(requests[1].ParameterId, Is.EqualTo("motion"));
				Assert.That(requests[1].Value, Is.EqualTo(64f / 127f).Within(0.0001f));
			}
			finally {
				Object.DestroyImmediate(owner);
				Object.DestroyImmediate(patchA);
				Object.DestroyImmediate(patchB);
			}
		}

		[Test]
		public void LaunchControlFirstFaderQueuesMainCueOpacityWithoutDrivingPatchParameters() {
			var owner = new GameObject("MIDI");
			var patch = CreatePatch("patch-a", new PatchMidiInputBinding("motion", MidiControlKind.ControlChange, 1, 5));
			try {
				var manager = owner.AddComponent<MidiInputManager>();
				var source = new QueueMidiInputSource();
				manager.Configure(new NullMidiApplication(), new NullLiveControlApplication(), source);
				var queue = new LiveParameterQueue();
				using (var input = new LiveMidiInput(manager, queue, new[] { patch })) {
					Assert.That(manager.IsRoutingConnected, Is.True);
					input.SetSelectedPatch("patch-a");
					source.Enqueue(new MidiInputEvent(new MidiControl("Launch Control XL 3", MidiControlKind.ControlChange, 1, 5), 32));
					manager.Poll();
				}
				Assert.That(manager.IsRoutingConnected, Is.False);

				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(requests, Has.Count.EqualTo(1));
				Assert.That(requests[0].Kind, Is.EqualTo(LiveParameterRequestKind.SetMainCueFader));
				Assert.That(requests[0].Value, Is.EqualTo(32f / 127f).Within(.0001f));
			}
			finally {
				Object.DestroyImmediate(owner);
				Object.DestroyImmediate(patch);
			}
		}

		[Test]
		public void LaunchControlRelativeEncoderQueuesSceneTimeJogSpeedChanges() {
			var owner = new GameObject("MIDI");
			var patch = CreatePatch("patch-a", new PatchMidiInputBinding("motion", MidiControlKind.ControlChange, 16, 77));
			try {
				var manager = owner.AddComponent<MidiInputManager>();
				var source = new QueueMidiInputSource();
				manager.Configure(new NullMidiApplication(), new NullLiveControlApplication(), source);
				var queue = new LiveParameterQueue();
				using (var input = new LiveMidiInput(manager, queue, new[] { patch })) {
					input.SetSelectedPatch("patch-a");
					source.Enqueue(new MidiInputEvent(new MidiControl("Launch Control XL 3", MidiControlKind.ControlChange, 16, 77), 66));
					source.Enqueue(new MidiInputEvent(new MidiControl("Launch Control XL 3", MidiControlKind.ControlChange, 16, 77), 63));
					source.Enqueue(new MidiInputEvent(new MidiControl("Launch Control XL 3", MidiControlKind.ControlChange, 16, 77), 64));
					manager.Poll();
				}

				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(requests, Has.Count.EqualTo(2));
				Assert.That(requests.Select(request => request.Kind), Is.EqualTo(new[] {
					LiveParameterRequestKind.JogSceneTime,
					LiveParameterRequestKind.JogSceneTime
				}));
				Assert.That(requests[0].Value, Is.EqualTo(2f).Within(.0001f));
				Assert.That(requests[1].Value, Is.EqualTo(-1f).Within(.0001f));
			}
			finally {
				Object.DestroyImmediate(owner);
				Object.DestroyImmediate(patch);
			}
		}

		private static PatchDefinition CreatePatch(string id, params PatchMidiInputBinding[] midiInputs) {
			var patch = ScriptableObject.CreateInstance<PatchDefinition>();
			var serialized = new SerializedObject(patch);
			serialized.FindProperty("_id").stringValue = id;
			serialized.FindProperty("_displayName").stringValue = id;
			var inputs = serialized.FindProperty("m_MidiInputs");
			inputs.arraySize = midiInputs.Length;
			for (var index = 0; index < midiInputs.Length; index++) {
				var input = inputs.GetArrayElementAtIndex(index);
				var binding = midiInputs[index];
				input.FindPropertyRelative("m_MessageType").enumValueIndex = (int)binding.MessageType;
				input.FindPropertyRelative("m_Channel").intValue = binding.Channel;
				input.FindPropertyRelative("m_Number").intValue = binding.Number;
				input.FindPropertyRelative("m_RawMinimum").intValue = binding.RawMinimum;
				input.FindPropertyRelative("m_RawMaximum").intValue = binding.RawMaximum;
				input.FindPropertyRelative("m_Invert").boolValue = binding.Invert;
				input.FindPropertyRelative("m_ParameterId").stringValue = binding.ParameterId;
			}
			serialized.ApplyModifiedPropertiesWithoutUndo();
			return patch;
		}

		private static void PollKey(LiveKeyboardInput input, Keyboard keyboard, params Key[] keys) {
			InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
			InputSystem.Update();
			input.Poll("patch-a");
			InputSystem.QueueStateEvent(keyboard, new KeyboardState());
			InputSystem.Update();
			input.Poll("patch-a");
		}

		private static PatchDefinition CreateKeyboardPatch(string id, params PatchKeyboardInputBinding[] keyboardInputs) {
			var patch = CreatePatch(id);
			var serialized = new SerializedObject(patch);
			var inputs = serialized.FindProperty("m_KeyboardInputs");
			inputs.arraySize = keyboardInputs.Length;
			for (var index = 0; index < keyboardInputs.Length; index++) {
				var input = inputs.GetArrayElementAtIndex(index);
				var binding = keyboardInputs[index];
				input.FindPropertyRelative("m_Key").enumValueIndex = (int)binding.Key;
				input.FindPropertyRelative("m_ParameterId").stringValue = binding.ParameterId;
			}
			serialized.ApplyModifiedPropertiesWithoutUndo();
			return patch;
		}

		private sealed class QueueMidiInputSource : IMidiInputSource {
			private readonly Queue<MidiInputEvent> _events = new Queue<MidiInputEvent>();
			public string DeviceName => "Test";
			public void Enqueue(MidiInputEvent inputEvent) => _events.Enqueue(inputEvent);
			public bool TryDequeue(out MidiInputEvent inputEvent) {
				if (_events.Count > 0) { inputEvent = _events.Dequeue(); return true; }
				inputEvent = default(MidiInputEvent);
				return false;
			}
			public void Dispose() { }
		}

		private sealed class NullMidiApplication : IMidiInputApplicationPort {
			public ApplicationCommandResult HandleMidi(MidiInputEvent inputEvent) => ApplicationCommandResult.Ignored();
		}

		private sealed class NullLiveControlApplication : ILiveControlApplicationPort {
			public ApplicationCommandResult SetLiveControlValue(LogicalControlId id, float normalizedValue) => ApplicationCommandResult.Ignored();
		}
	}
}
