using System.Collections.Generic;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Input;
using UnityEngine;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class MainLiveMidiInputTests {
		[Test]
		public void CaptureMapsFixedMainControlsIntoTheSharedFrameBuffer() {
			var gameObject = new GameObject("Main MIDI input test");
			var source = new FakeMidiInputSource(
				Event(MidiControlKind.ControlChange, 21, 127),
				Event(MidiControlKind.ControlChange, 22, 32),
				Event(MidiControlKind.Note, 37, 127));
			try {
				var input = gameObject.AddComponent<MainLiveInput>();
				var midi = gameObject.AddComponent<MainLiveMidiInput>();
				var buffer = new MainLiveParameterBuffer();
				input.Bind(buffer);
				midi.ConfigureSource(source);
				midi.Initialize(input);

				Assert.That(midi.Capture(2), Is.EqualTo(3));
				var frame = buffer.Commit(1, 2);

				Assert.That(frame.SceneIndex, Is.EqualTo(1));
				Assert.That(frame.Motion, Is.EqualTo(1f));
				Assert.That(frame.Scale, Is.EqualTo(32f / 127f).Within(0.0001f));
			}
			finally {
				Object.DestroyImmediate(gameObject);
				source.Dispose();
			}
		}

		private static MidiInputEvent Event(MidiControlKind kind, int number, int value) =>
			new MidiInputEvent(new MidiControl("Test Device", kind, 1, number), value);

		private sealed class FakeMidiInputSource : IMidiInputSource {
			private readonly Queue<MidiInputEvent> _events;
			public string DeviceName => "Test Device";

			public FakeMidiInputSource(params MidiInputEvent[] events) => _events = new Queue<MidiInputEvent>(events);
			public bool TryDequeue(out MidiInputEvent inputEvent) {
				if (_events.Count != 0) { inputEvent = _events.Dequeue(); return true; }
				inputEvent = default(MidiInputEvent);
				return false;
			}
			public void Dispose() => _events.Clear();
		}
	}
}
