using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace ShitDesigner.Main.Tests {
	public sealed class NativeOutputMenuTests {
		[Test]
		public void DisplayCanvasSupportsStretchFillAndFit() {
			var host = new GameObject("External Display Scaling Test");
			var source = new RenderTexture(16, 9, 0, RenderTextureFormat.ARGB32);
			try {
				Assert.That(source.Create(), Is.True);
				var canvas = host.AddComponent<Canvas>();
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				var presenter = host.AddComponent<LiveProgramDisplayCanvas>();
				presenter.Initialize(canvas, source);

				var background = presenter.transform.GetChild(0).GetComponent<Image>();
				var emulation = presenter.transform.GetChild(1);
				var emulationAspectRatioFitter = emulation.GetComponent<AspectRatioFitter>();
				var image = emulation.GetChild(0);
				var aspectRatioFitter = image.GetComponent<AspectRatioFitter>();
				Assert.That(background.color, Is.EqualTo(Color.black));
				Assert.That(emulationAspectRatioFitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.None));
				Assert.That(aspectRatioFitter.aspectRatio, Is.EqualTo(16f / 9f).Within(0.0001f));
				Assert.That(aspectRatioFitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.EnvelopeParent));

				presenter.SetScalingMode(ExternalDisplayScalingMode.Stretch);
				Assert.That(aspectRatioFitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.None));
				presenter.SetScalingMode(ExternalDisplayScalingMode.Fit);
				Assert.That(aspectRatioFitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.FitInParent));
				presenter.SetScalingMode(ExternalDisplayScalingMode.Fill);
				Assert.That(aspectRatioFitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.EnvelopeParent));

				presenter.SetEmulationAspect(ExternalDisplayEmulationAspect.Ratio3x4);
				Assert.That(emulationAspectRatioFitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.FitInParent));
				Assert.That(emulationAspectRatioFitter.aspectRatio, Is.EqualTo(3f / 4f).Within(0.0001f));
				Assert.That(aspectRatioFitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.EnvelopeParent));
				presenter.SetEmulationAspect(ExternalDisplayEmulationAspect.Display);
				Assert.That(emulationAspectRatioFitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.None));
			}
			finally {
				source.Release();
				Object.DestroyImmediate(source);
				Object.DestroyImmediate(host);
			}
		}

		[TestCase(ExternalDisplayEmulationAspect.Display, 0f)]
		[TestCase(ExternalDisplayEmulationAspect.Ratio16x9, 16f / 9f)]
		[TestCase(ExternalDisplayEmulationAspect.Ratio16x10, 16f / 10f)]
		[TestCase(ExternalDisplayEmulationAspect.Ratio4x3, 4f / 3f)]
		[TestCase(ExternalDisplayEmulationAspect.Ratio3x4, 3f / 4f)]
		[TestCase(ExternalDisplayEmulationAspect.Ratio1x1, 1f)]
		[TestCase(ExternalDisplayEmulationAspect.Ratio9x16, 9f / 16f)]
		[TestCase(ExternalDisplayEmulationAspect.Ratio21x9, 21f / 9f)]
		public void EmulationAspectMapsToExpectedRatio(ExternalDisplayEmulationAspect aspect, float expected) {
			Assert.That(aspect.AspectRatio(), Is.EqualTo(expected).Within(0.0001f));
		}

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
				Assert.That(backend.LastState.IsTestPatternVisible, Is.False);
				Assert.That(backend.LastState.CanSwapOutputs, Is.True);
				Assert.That(backend.LastState.ScalingMode, Is.EqualTo(ExternalDisplayScalingMode.Fill));
				Assert.That(backend.LastState.EmulationAspect, Is.EqualTo(ExternalDisplayEmulationAspect.Display));

				backend.Enqueue(OutputMenuCommand.StartProgram);
				backend.Enqueue(OutputMenuCommand.StartOverlay);
				controller.Tick();
				Assert.That(target.IsActive(LiveOutputKind.Program), Is.True);
				Assert.That(target.IsActive(LiveOutputKind.Overlay), Is.True);
				Assert.That(backend.LastState.CanStartProgram, Is.False);
				Assert.That(backend.LastState.CanStopProgram, Is.True);
				Assert.That(backend.LastState.CanStartOverlay, Is.False);
				Assert.That(backend.LastState.CanStopOverlay, Is.True);

				backend.Enqueue(OutputMenuCommand.ToggleTestPattern);
				backend.Enqueue(OutputMenuCommand.SwapOutputs);
				backend.Enqueue(OutputMenuCommand.StopProgram);
				controller.Tick();
				Assert.That(target.TestPatternChangeCount, Is.EqualTo(1));
				Assert.That(target.IsTestPatternVisible, Is.True);
				Assert.That(backend.LastState.IsTestPatternVisible, Is.True);
				Assert.That(target.SwapCount, Is.EqualTo(1));
				Assert.That(target.IsActive(LiveOutputKind.Program), Is.False);
				Assert.That(target.IsActive(LiveOutputKind.Overlay), Is.True);

				backend.Enqueue(OutputMenuCommand.ToggleTestPattern);
				controller.Tick();
				Assert.That(target.TestPatternChangeCount, Is.EqualTo(2));
				Assert.That(target.IsTestPatternVisible, Is.False);
				Assert.That(backend.LastState.IsTestPatternVisible, Is.False);

				backend.Enqueue(OutputMenuCommand.SetScalingStretch);
				controller.Tick();
				Assert.That(target.ScalingMode, Is.EqualTo(ExternalDisplayScalingMode.Stretch));
				Assert.That(backend.LastState.ScalingMode, Is.EqualTo(ExternalDisplayScalingMode.Stretch));

				backend.Enqueue(OutputMenuCommand.SetScalingFit);
				backend.Enqueue(OutputMenuCommand.SetScalingFill);
				controller.Tick();
				Assert.That(target.ScalingMode, Is.EqualTo(ExternalDisplayScalingMode.Fill));
				Assert.That(target.ScalingChangeCount, Is.EqualTo(3));

				backend.Enqueue(OutputMenuCommand.SetEmulation3x4);
				controller.Tick();
				Assert.That(target.EmulationAspect, Is.EqualTo(ExternalDisplayEmulationAspect.Ratio3x4));
				Assert.That(backend.LastState.EmulationAspect, Is.EqualTo(ExternalDisplayEmulationAspect.Ratio3x4));
				backend.Enqueue(OutputMenuCommand.SetEmulation21x9);
				backend.Enqueue(OutputMenuCommand.SetEmulationDisplay);
				controller.Tick();
				Assert.That(target.EmulationAspect, Is.EqualTo(ExternalDisplayEmulationAspect.Display));
				Assert.That(target.EmulationChangeCount, Is.EqualTo(3));
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
				backend.Enqueue(OutputMenuCommand.ToggleTestPattern);
				backend.Enqueue(OutputMenuCommand.SwapOutputs);
				backend.Enqueue(OutputMenuCommand.SetScalingFit);
				backend.Enqueue(OutputMenuCommand.SetEmulation3x4);
				controller.Tick();
			}

			Assert.That(target.SetActiveCount, Is.Zero);
			Assert.That(target.TestPatternChangeCount, Is.Zero);
			Assert.That(target.SwapCount, Is.Zero);
			Assert.That(target.ScalingMode, Is.EqualTo(ExternalDisplayScalingMode.Fit));
			Assert.That(target.ScalingChangeCount, Is.EqualTo(1));
			Assert.That(target.EmulationAspect, Is.EqualTo(ExternalDisplayEmulationAspect.Ratio3x4));
			Assert.That(target.EmulationChangeCount, Is.EqualTo(1));
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
			public int TestPatternChangeCount { get; private set; }
			public int SwapCount { get; private set; }
			public int ScalingChangeCount { get; private set; }
			public int EmulationChangeCount { get; private set; }
			public bool IsTestPatternVisible { get; private set; }
			public bool CanSwapOutputs => m_Available.All(available => available);
			public ExternalDisplayScalingMode ScalingMode { get; private set; } = ExternalDisplayScalingMode.Fill;
			public ExternalDisplayEmulationAspect EmulationAspect { get; private set; } = ExternalDisplayEmulationAspect.Display;

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

			public bool SetTestPatternVisible(bool visible) {
				TestPatternChangeCount++;
				IsTestPatternVisible = visible;
				return true;
			}
			public bool SwapOutputs() { SwapCount++; return true; }
			public bool SetScalingMode(ExternalDisplayScalingMode mode) {
				ScalingChangeCount++;
				ScalingMode = mode;
				return true;
			}
			public bool SetEmulationAspect(ExternalDisplayEmulationAspect aspect) {
				EmulationChangeCount++;
				EmulationAspect = aspect;
				return true;
			}
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
