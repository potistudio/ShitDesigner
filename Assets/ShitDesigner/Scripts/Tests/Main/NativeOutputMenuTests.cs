using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace ShitDesigner.Main.Tests {
	public sealed class NativeOutputMenuTests {
		[Test]
		public void SwapReversesProgramAndOverlayDisplayRouting() {
			Assert.That(LiveExternalDisplayOutput.ResolveDisplayNumber(LiveOutputKind.Program, swapped: false), Is.EqualTo(2));
			Assert.That(LiveExternalDisplayOutput.ResolveDisplayNumber(LiveOutputKind.Overlay, swapped: false), Is.EqualTo(3));
			Assert.That(LiveExternalDisplayOutput.ResolveDisplayNumber(LiveOutputKind.Program, swapped: true), Is.EqualTo(3));
			Assert.That(LiveExternalDisplayOutput.ResolveDisplayNumber(LiveOutputKind.Overlay, swapped: true), Is.EqualTo(2));
			Assert.That(LiveExternalDisplayOutput.ResolveOutput(2, swapped: true), Is.EqualTo(LiveOutputKind.Overlay));
			Assert.That(LiveExternalDisplayOutput.ResolveOutput(3, swapped: true), Is.EqualTo(LiveOutputKind.Program));
		}

		[Test]
		public void ControllerReflectsAvailabilityAndDispatchesNativeCommands() {
			var target = new RecordingOutputTarget(programAvailable: true, overlayAvailable: true);
			var backend = new RecordingOutputMenuBackend();
			using (var controller = new LiveOutputMenuController(target, backend)) {
				controller.Tick();
				Assert.That(backend.LastState.CanStartProgram, Is.True);
				Assert.That(backend.LastState.CanStopProgram, Is.False);
				Assert.That(backend.LastState.CanStartOverlay, Is.True);
				Assert.That(backend.LastState.CanStopOverlay, Is.False);
				Assert.That(backend.LastState.CanIdentifyDisplays, Is.True);
				Assert.That(backend.LastState.CanSwapOutputs, Is.True);

				backend.Enqueue(OutputMenuCommand.StartProgram);
				backend.Enqueue(OutputMenuCommand.StartOverlay);
				controller.Tick();
				Assert.That(target.IsActive(LiveOutputKind.Program), Is.True);
				Assert.That(target.IsActive(LiveOutputKind.Overlay), Is.True);
				Assert.That(backend.LastState.CanStartProgram, Is.False);
				Assert.That(backend.LastState.CanStopProgram, Is.True);
				Assert.That(backend.LastState.CanStartOverlay, Is.False);
				Assert.That(backend.LastState.CanStopOverlay, Is.True);

				backend.Enqueue(OutputMenuCommand.IdentifyDisplays);
				backend.Enqueue(OutputMenuCommand.SwapOutputs);
				backend.Enqueue(OutputMenuCommand.StopProgram);
				controller.Tick();
				Assert.That(target.IdentifyCount, Is.EqualTo(1));
				Assert.That(target.SwapCount, Is.EqualTo(1));
				Assert.That(target.IsActive(LiveOutputKind.Program), Is.False);
				Assert.That(target.IsActive(LiveOutputKind.Overlay), Is.True);
			}

			Assert.That(backend.Disposed, Is.True);
		}

		[Test]
		public void ControllerIgnoresCommandsThatAreUnavailable() {
			var target = new RecordingOutputTarget(programAvailable: false, overlayAvailable: false);
			var backend = new RecordingOutputMenuBackend();
			using (var controller = new LiveOutputMenuController(target, backend)) {
				backend.Enqueue(OutputMenuCommand.StartProgram);
				backend.Enqueue(OutputMenuCommand.StopProgram);
				backend.Enqueue(OutputMenuCommand.StartOverlay);
				backend.Enqueue(OutputMenuCommand.StopOverlay);
				backend.Enqueue(OutputMenuCommand.IdentifyDisplays);
				backend.Enqueue(OutputMenuCommand.SwapOutputs);
				controller.Tick();
			}

			Assert.That(target.SetActiveCount, Is.Zero);
			Assert.That(target.IdentifyCount, Is.Zero);
			Assert.That(target.SwapCount, Is.Zero);
			Assert.That(backend.LastState.CanStartProgram, Is.False);
			Assert.That(backend.LastState.CanStopProgram, Is.False);
			Assert.That(backend.LastState.CanStartOverlay, Is.False);
			Assert.That(backend.LastState.CanStopOverlay, Is.False);
			Assert.That(backend.LastState.CanIdentifyDisplays, Is.False);
			Assert.That(backend.LastState.CanSwapOutputs, Is.False);
		}

		private sealed class RecordingOutputTarget : ILiveOutputMenuTarget {
			private readonly bool[] m_Active = new bool[2];
			private readonly bool[] m_Available;
			public int SetActiveCount { get; private set; }
			public int IdentifyCount { get; private set; }
			public int SwapCount { get; private set; }
			public bool CanSwapOutputs => m_Available.All(available => available);

			public RecordingOutputTarget(bool programAvailable, bool overlayAvailable) {
				m_Available = new[] { programAvailable, overlayAvailable };
			}

			public bool IsActive(LiveOutputKind output) => m_Active[(int)output];
			public bool IsOutputAvailable(LiveOutputKind output) => m_Available[(int)output];

			public bool SetOutputActive(LiveOutputKind output, bool active) {
				SetActiveCount++;
				m_Active[(int)output] = active;
				return true;
			}

			public void IdentifyDisplay() => IdentifyCount++;
			public bool SwapOutputs() { SwapCount++; return true; }
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
