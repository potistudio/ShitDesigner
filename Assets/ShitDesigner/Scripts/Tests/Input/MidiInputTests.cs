using System.Collections.Generic;
using NUnit.Framework;
using ShitDesigner.Application;
using ShitDesigner.Core;
using ShitDesigner.Input;

namespace ShitDesigner.Input.Tests {
	[TestFixture]
	public sealed class MidiInputTests {
		[Test]
		public void DecoderConvertsControlChangeToStableControlIdentity() {
			var packed = Pack(0xb2, 74, 96);
			Assert.That(MidiShortMessageDecoder.TryDecode("LCXL3 1 MIDI", packed, out var inputEvent), Is.True);
			Assert.That(inputEvent.Control.Kind, Is.EqualTo(MidiControlKind.ControlChange));
			Assert.That(inputEvent.Control.Channel, Is.EqualTo(3));
			Assert.That(inputEvent.Control.Number, Is.EqualTo(74));
			Assert.That(inputEvent.RawValue, Is.EqualTo(96));
			Assert.That(inputEvent.Control.PhysicalId, Is.EqualTo("LCXL3 1 MIDI:controlchange:3:74"));
			Assert.That(inputEvent.Control.ControlPath, Is.EqualTo("<MIDI>/LCXL3 1 MIDI/controlchange/3/74"));
		}

		[Test]
		public void DecoderTreatsNoteOffAndZeroVelocityNoteOnAsZero() {
			Assert.That(MidiShortMessageDecoder.TryDecode("Device", Pack(0x80, 60, 100), out var noteOff), Is.True);
			Assert.That(MidiShortMessageDecoder.TryDecode("Device", Pack(0x90, 60, 0), out var zeroVelocity), Is.True);
			Assert.That(noteOff.Control, Is.EqualTo(zeroVelocity.Control));
			Assert.That(noteOff.RawValue, Is.Zero);
			Assert.That(zeroVelocity.RawValue, Is.Zero);
		}

		[Test]
		public void DecoderIgnoresRealtimeClockMessages() {
			Assert.That(MidiShortMessageDecoder.TryDecode("Device", 0xf8, out _), Is.False);
		}

		[Test]
		public void DesktopDeviceDiscoveryUsesTheCurrentPlatformBackend() {
			Assert.That(MidiInputDevices.GetDevices(), Is.Not.Null);
		}

		[Test]
		public void LaunchControlProtocolMapsRelativeEncoderRowsAndMessages() {
			Assert.That(LaunchControlXl3DawModeProtocol.EnableDawModeMessage, Is.EqualTo(0x007f0c9f));
			Assert.That(LaunchControlXl3DawModeProtocol.DisableDawModeMessage, Is.EqualTo(0x00000c9f));
			Assert.That(LaunchControlXl3DawModeProtocol.TryResolveRelativeEncoderRow(16, 77, out var firstRow), Is.True);
			Assert.That(firstRow, Is.EqualTo(1));
			Assert.That(LaunchControlXl3DawModeProtocol.TryResolveRelativeEncoderRow(16, 92, out var secondRow), Is.True);
			Assert.That(secondRow, Is.EqualTo(2));
			Assert.That(LaunchControlXl3DawModeProtocol.TryResolveRelativeEncoderRow(16, 100, out var thirdRow), Is.True);
			Assert.That(thirdRow, Is.EqualTo(3));
			Assert.That(LaunchControlXl3DawModeProtocol.TryResolveRelativeEncoderRow(1, 13, out _), Is.False);
			Assert.That(LaunchControlXl3DawModeProtocol.EnableRelativeEncoderRowMessage(1), Is.EqualTo(0x007f45b6));
			Assert.That(LaunchControlXl3DawModeProtocol.ResolveDawInputName("LCXL3 1 DAW Out"), Is.EqualTo("LCXL3 1 DAW In"));
		}

		[Test]
		public void RouterDrainsQueuedEventsOnPoll() {
			var first = new MidiInputEvent(new MidiControl("Device", MidiControlKind.ControlChange, 1, 10), 1);
			var second = new MidiInputEvent(new MidiControl("Device", MidiControlKind.Note, 1, 64), 127);
			var source = new FakeSource(first, second);
			var application = new FakeApplication();
			var router = new MidiInputRouter(application, source);

			Assert.That(router.Poll(), Is.EqualTo(2));
			Assert.That(application.Events, Is.EqualTo(new[] { first, second }));
			Assert.That(router.Poll(), Is.Zero);
		}

		[Test]
		public void InspectorBindingNormalizesAndInvertsRawValue() {
			var id = LogicalControlId.New();
			var binding = new MidiLiveControlBinding(id.Value, MidiControlKind.ControlChange, 2, 21, 10, 110, invert: true);
			Assert.That(binding.TryResolve(out var resolved, out var error), Is.True, error);
			Assert.That(resolved, Is.EqualTo(id));
			Assert.That(binding.Matches(new MidiControl("Device", MidiControlKind.ControlChange, 2, 21)), Is.True);
			Assert.That(binding.Normalize(10), Is.EqualTo(1f));
			Assert.That(binding.Normalize(60), Is.EqualTo(0.5f));
			Assert.That(binding.Normalize(110), Is.EqualTo(0f));
		}

		[Test]
		public void InspectorBindingReportsUnselectedLiveControlClearly() {
			var binding = new MidiLiveControlBinding(string.Empty, MidiControlKind.ControlChange, 1, 0);

			Assert.That(binding.TryResolve(out _, out var error), Is.False);
			Assert.That(error, Is.EqualTo("Select a Live Control."));
		}

		private static uint Pack(byte status, byte data1, byte data2) => (uint)(status | (data1 << 8) | (data2 << 16));

		private sealed class FakeSource : IMidiInputSource {
			private readonly Queue<MidiInputEvent> _events;
			public string DeviceName => "Device";
			public FakeSource(params MidiInputEvent[] events) { _events = new Queue<MidiInputEvent>(events); }
			public bool TryDequeue(out MidiInputEvent inputEvent) {
				if (_events.Count == 0) { inputEvent = default(MidiInputEvent); return false; }
				inputEvent = _events.Dequeue();
				return true;
			}
			public void Dispose() { }
		}

		private sealed class FakeApplication : IMidiInputApplicationPort {
			public List<MidiInputEvent> Events { get; } = new List<MidiInputEvent>();
			public ApplicationCommandResult HandleMidi(MidiInputEvent inputEvent) {
				Events.Add(inputEvent);
				return ApplicationCommandResult.Ignored();
			}
		}
	}
}
