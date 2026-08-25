using System.Collections.Generic;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Input;
using UnityEngine;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LiveInputTests {
		[Test]
		public void MidiMappingQueuesSceneAndParameterRequestsInEventOrder() {
			var owner = new GameObject("MIDI");
			try {
				var manager = owner.AddComponent<MidiInputManager>();
				var source = new QueueMidiInputSource();
				manager.Configure(new NullMidiApplication(), new NullLiveControlApplication(), source);
				var queue = new LiveParameterQueue();
				using (var input = new LiveMidiInput(manager, queue, new[] { "scene-a", "scene-b" })) {
					input.SetSelectedScene("scene-a");
					source.Enqueue(new MidiInputEvent(new MidiControl(MidiControlKind.Note, 1, 37), 127));
					source.Enqueue(new MidiInputEvent(new MidiControl(MidiControlKind.ControlChange, 1, 21), 64));
					manager.Poll();
				}

				var requests = new List<LiveParameterRequest>();
				queue.Drain(requests);
				Assert.That(requests.Count, Is.EqualTo(2));
				Assert.That(requests[0].SceneId, Is.EqualTo("scene-b"));
				Assert.That(requests[1].SceneId, Is.EqualTo("scene-a"));
				Assert.That(requests[1].ParameterId, Is.EqualTo(LiveSceneRoot.MotionParameterId));
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
			public void HandleMidi(MidiInputEvent inputEvent) { }
		}

		private sealed class NullLiveControlApplication : ILiveControlApplicationPort {
			public void SetLiveControlValue(LogicalControlId id, float normalizedValue) { }
		}
	}
}
