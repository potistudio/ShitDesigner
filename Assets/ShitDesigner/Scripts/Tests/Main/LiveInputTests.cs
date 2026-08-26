using System.Collections.Generic;
using NUnit.Framework;
using ShitDesigner.Application;
using ShitDesigner.Core;
using ShitDesigner.Input;
using UnityEngine;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LiveInputTests {
		[Test]
		public void MidiMappingQueuesPreloadedPatchLoadAndParameterRequestsInEventOrder() {
			var owner = new GameObject("MIDI");
			try {
				var manager = owner.AddComponent<MidiInputManager>();
				var source = new QueueMidiInputSource();
				manager.Configure(new NullMidiApplication(), new NullLiveControlApplication(), source);
				var queue = new LiveParameterQueue();
				using (var input = new LiveMidiInput(manager, queue, new[] { "patch-a", "patch-b" })) {
					input.SetSelectedPatch("patch-a");
					source.Enqueue(new MidiInputEvent(new MidiControl("Test", MidiControlKind.Note, 1, 37), 127));
					source.Enqueue(new MidiInputEvent(new MidiControl("Test", MidiControlKind.ControlChange, 1, 21), 64));
					manager.Poll();
				}

				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(requests.Count, Is.EqualTo(2));
				Assert.That(requests[0].Kind, Is.EqualTo(LiveParameterRequestKind.LoadPatch));
				Assert.That(requests[0].PatchId, Is.EqualTo("patch-b"));
				Assert.That(requests[1].PatchId, Is.EqualTo("patch-a"));
				Assert.That(requests[1].ParameterId, Is.EqualTo(LiveGraphClockRateParameter.ParameterId));
			}
			finally { Object.DestroyImmediate(owner); }
		}

		[Test]
		public void MidiTriggerBindingQueuesFlashForTheLoadedPatch() {
			var owner = new GameObject("MIDI");
			try {
				var manager = owner.AddComponent<MidiInputManager>();
				var source = new QueueMidiInputSource();
				manager.SetBindings(new[] { new MidiLiveControlBinding(string.Empty, MidiControlKind.Note, 1, 36, output: MidiLiveControlBindingOutput.Trigger) });
				manager.Configure(new NullMidiApplication(), new NullLiveControlApplication(), source);
				var queue = new LiveParameterQueue();
				using (var input = new LiveMidiInput(manager, queue, new[] { "patch-a" })) {
					input.SetSelectedPatch("patch-a");
					source.Enqueue(new MidiInputEvent(new MidiControl("Test", MidiControlKind.Note, 1, 36), 127));
					manager.Poll();
				}

				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(requests, Has.Count.EqualTo(1));
				Assert.That(requests[0].Kind, Is.EqualTo(LiveParameterRequestKind.TriggerFlash));
				Assert.That(requests[0].PatchId, Is.EqualTo("patch-a"));
			}
			finally { Object.DestroyImmediate(owner); }
		}

		[Test]
		public void MidiTriggerBindingFiresOnlyOnTheRisingEdge() {
			var owner = new GameObject("MIDI");
			try {
				var manager = owner.AddComponent<MidiInputManager>();
				var source = new QueueMidiInputSource();
				manager.SetBindings(new[] { new MidiLiveControlBinding(string.Empty, MidiControlKind.Note, 1, 60, output: MidiLiveControlBindingOutput.Trigger) });
				manager.Configure(new NullMidiApplication(), new NullLiveControlApplication(), source);
				var queue = new LiveParameterQueue();
				using (var input = new LiveMidiInput(manager, queue, new[] { "patch-a" })) {
					input.SetSelectedPatch("patch-a");
					source.Enqueue(new MidiInputEvent(new MidiControl("Test", MidiControlKind.Note, 1, 60), 127));
					source.Enqueue(new MidiInputEvent(new MidiControl("Test", MidiControlKind.Note, 1, 60), 127));
					source.Enqueue(new MidiInputEvent(new MidiControl("Test", MidiControlKind.Note, 1, 60), 0));
					source.Enqueue(new MidiInputEvent(new MidiControl("Test", MidiControlKind.Note, 1, 60), 127));
					manager.Poll();
				}

				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(requests, Has.Count.EqualTo(2));
				Assert.That(requests, Has.All.Matches<LiveParameterRequest>(request => request.Kind == LiveParameterRequestKind.TriggerFlash));
			}
			finally { Object.DestroyImmediate(owner); }
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
