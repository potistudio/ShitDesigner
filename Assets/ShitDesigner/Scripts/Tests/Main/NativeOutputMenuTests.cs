using System.Collections.Generic;
using NUnit.Framework;

namespace ShitDesigner.Main.Tests {
	public sealed class NativeOutputMenuTests {
		[Test]
		public void ControllerReflectsAvailabilityAndDispatchesNativeCommands() {
			var target = new RecordingOutputTarget { IsAvailable = true };
			var backend = new RecordingOutputMenuBackend();
			using (var controller = new LiveOutputMenuController(target, backend)) {
				controller.Tick();
				Assert.That(backend.LastState.CanStart, Is.True);
				Assert.That(backend.LastState.CanStop, Is.False);
				Assert.That(backend.LastState.CanIdentifyDisplays, Is.True);

				backend.Enqueue(OutputMenuCommand.Start);
				controller.Tick();
				Assert.That(target.IsOutputActive, Is.True);
				Assert.That(backend.LastState.CanStart, Is.False);
				Assert.That(backend.LastState.CanStop, Is.True);

				backend.Enqueue(OutputMenuCommand.IdentifyDisplays);
				backend.Enqueue(OutputMenuCommand.Stop);
				controller.Tick();
				Assert.That(target.IdentifyCount, Is.EqualTo(1));
				Assert.That(target.IsOutputActive, Is.False);
			}

			Assert.That(backend.Disposed, Is.True);
		}

		[Test]
		public void ControllerIgnoresCommandsThatAreUnavailable() {
			var target = new RecordingOutputTarget();
			var backend = new RecordingOutputMenuBackend();
			using (var controller = new LiveOutputMenuController(target, backend)) {
				backend.Enqueue(OutputMenuCommand.Start);
				backend.Enqueue(OutputMenuCommand.Stop);
				backend.Enqueue(OutputMenuCommand.IdentifyDisplays);
				controller.Tick();
			}

			Assert.That(target.SetActiveCount, Is.Zero);
			Assert.That(target.IdentifyCount, Is.Zero);
			Assert.That(backend.LastState.CanStart, Is.False);
			Assert.That(backend.LastState.CanStop, Is.False);
			Assert.That(backend.LastState.CanIdentifyDisplays, Is.False);
		}

		private sealed class RecordingOutputTarget : ILiveOutputMenuTarget {
			public bool IsOutputActive { get; private set; }
			public bool IsAvailable { get; set; }
			public int SetActiveCount { get; private set; }
			public int IdentifyCount { get; private set; }

			public bool SetOutputActive(bool active) {
				SetActiveCount++;
				IsOutputActive = active;
				return true;
			}

			public void IdentifyDisplay() => IdentifyCount++;
		}

		private sealed class RecordingOutputMenuBackend : INativeOutputMenuBackend {
			private readonly Queue<OutputMenuCommand> m_Commands = new Queue<OutputMenuCommand>();
			public OutputMenuState LastState { get; private set; }
			public bool Disposed { get; private set; }

			public void Enqueue(OutputMenuCommand command) => m_Commands.Enqueue(command);

			public bool TryDequeueCommand(out OutputMenuCommand command) {
				if (m_Commands.Count == 0) { command = default; return false; }
				command = m_Commands.Dequeue();
				return true;
			}

			public void Refresh(OutputMenuState state) => LastState = state;
			public void Dispose() => Disposed = true;
		}
	}
}
