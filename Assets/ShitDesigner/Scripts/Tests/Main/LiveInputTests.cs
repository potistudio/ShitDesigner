using System.Collections.Generic;
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
		public void KeyboardMappingQueuesPressedParameterRequestAndIgnoresReleaseForLoadedPatch() {
			var patch = CreateKeyboardPatch("patch-a", new PatchKeyboardInputBinding("motion", Key.A));
			Keyboard keyboard = null;
			try {
				keyboard = InputSystem.AddDevice<Keyboard>();
				keyboard.MakeCurrent();
				var queue = new LiveParameterQueue();
				var input = new LiveKeyboardInput(queue, new[] { patch }, (_, _) => { }, () => { }, _ => { });

				InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.A));
				InputSystem.Update();
				input.Poll("patch-a");
				InputSystem.QueueStateEvent(keyboard, new KeyboardState());
				InputSystem.Update();
				input.Poll("patch-a");

				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(requests, Has.Count.EqualTo(1));
				Assert.That(requests[0].Kind, Is.EqualTo(LiveParameterRequestKind.SetParameter));
				Assert.That(requests[0].ParameterId, Is.EqualTo("motion"));
				Assert.That(requests[0].Value, Is.EqualTo(1f));
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
				var input = new LiveKeyboardInput(new LiveParameterQueue(), new[] { patch },
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
				var input = new LiveKeyboardInput(new LiveParameterQueue(), new[] { patch }, (_, _) => { }, () => { launched = true; }, _ => { });

				PollKey(input, keyboard, Key.Enter);

				Assert.That(launched, Is.True);
			}
			finally {
				if (keyboard != null) InputSystem.RemoveDevice(keyboard);
				Object.DestroyImmediate(patch);
			}
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

		private static void PollKey(LiveKeyboardInput input, Keyboard keyboard, Key key) {
			InputSystem.QueueStateEvent(keyboard, new KeyboardState(key));
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
