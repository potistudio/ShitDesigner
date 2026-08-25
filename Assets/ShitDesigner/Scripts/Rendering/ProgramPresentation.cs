using System;
using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Rendering {
	public readonly struct ProgramDisplaySelection {
		public int RequestedDisplay { get; }
		public int ResolvedDisplay { get; }
		public bool UsesProgramMonitor { get; }

		internal ProgramDisplaySelection(int requestedDisplay, int resolvedDisplay, bool usesProgramMonitor) {
			RequestedDisplay = requestedDisplay;
			ResolvedDisplay = resolvedDisplay;
			UsesProgramMonitor = usesProgramMonitor;
		}
	}

	public static class ProgramDisplayPolicy {
		/// <summary>Display2 (index 1) is the default; missing displays use the monitor.</summary>
		public const int DefaultDisplay = 1;

		/// <summary>Project settings are human-facing 1-based display
		/// numbers. Unity's Display array is zero-based; Bootstrap performs
		/// this conversion once at the presentation boundary.</summary>
		public static int ToUnityIndex(int projectDisplayNumber) {
			if (projectDisplayNumber < 1) throw new ArgumentOutOfRangeException(nameof(projectDisplayNumber));
			return projectDisplayNumber - 1;
		}

		public static ProgramDisplaySelection Resolve(int requestedDisplay = DefaultDisplay, int displayCount = 1) {
			if (requestedDisplay < 0) throw new ArgumentOutOfRangeException(nameof(requestedDisplay));
			if (displayCount < 1) throw new ArgumentOutOfRangeException(nameof(displayCount));
			var external = requestedDisplay < displayCount;
			return new ProgramDisplaySelection(requestedDisplay, external ? requestedDisplay : 0, !external);
		}
	}

	public interface IProgramDisplayPort {
		int DisplayCount { get; }
		Result<ProgramDisplaySelection, Diagnostic> Activate(int requestedDisplay);
		UnitResult<Diagnostic> Present(RenderTexture surface, ProgramDisplaySelection selection);
	}

	/// <summary>Unity boundary for selected Display activation and surface presentation.</summary>
	public sealed class UnityProgramDisplayPort : IProgramDisplayPort, IDisposable {
		private GameObject _displayCameraObject;
		private Camera _displayCamera;
		private ProgramDisplayBlitCamera _blit;
		private bool _disposed;
		public int DisplayCount => Display.displays == null || Display.displays.Length == 0 ? 1 : Display.displays.Length;

		public Result<ProgramDisplaySelection, Diagnostic> Activate(int requestedDisplay) {
			var selection = ProgramDisplayPolicy.Resolve(requestedDisplay, DisplayCount);
			if (selection.UsesProgramMonitor) {
				SetDisplayCameraActive(false);
				return Result.Success<ProgramDisplaySelection, Diagnostic>(selection);
			}
			try { Display.displays[selection.ResolvedDisplay].Activate(); }
			catch (Exception exception) {
				return Result.Failure<ProgramDisplaySelection, Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.display.activate_failed"), Severity.Error,
					"The selected Unity Display could not be activated.", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
			var camera = EnsureDisplayCamera(selection.ResolvedDisplay);
			if (camera.IsFailure) return Result.Failure<ProgramDisplaySelection, Diagnostic>(camera.Error);
			return Result.Success<ProgramDisplaySelection, Diagnostic>(selection);
		}

		public UnitResult<Diagnostic> Present(RenderTexture surface, ProgramDisplaySelection selection) {
			if (surface == null || !surface.IsCreated())
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.display.surface_invalid"), Severity.Error, "A created Program surface is required."));
			if (_disposed) return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.display.disposed"), Severity.Error, "The Program display port is disposed."));
			var selected = ProgramDisplayPolicy.Resolve(selection.RequestedDisplay, DisplayCount);
			if (selected.UsesProgramMonitor) {
				SetDisplayCameraActive(false);
				return UnitResult.Success<Diagnostic>();
			}
			var ensured = EnsureDisplayCamera(selected.ResolvedDisplay);
			if (ensured.IsFailure) return ensured;
			_blit.Source = surface;
			_displayCamera.targetDisplay = selected.ResolvedDisplay;
			_displayCamera.enabled = true;
			try {
				// The camera is enabled for exactly one normal PlayerLoop
				// render. Built-in invokes OnRenderImage; SRP invokes the
				// end-camera hook below. Calling Camera.Render here would
				// render a second time in the same frame and would also be
				// invalid for URP/SRP.
				return UnitResult.Success<Diagnostic>();
			}
			catch (Exception exception) {
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.display.present_failed"), Severity.Error,
					"The Program surface could not be presented to the selected Display.", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		public void SetOutputActive(bool active) {
			SetDisplayCameraActive(active);
		}

		private void SetDisplayCameraActive(bool active) {
			if (_displayCamera == null) return;
			_displayCamera.enabled = active;
			if (!active && _blit != null) _blit.Source = null;
		}

		private UnitResult<Diagnostic> EnsureDisplayCamera(int targetDisplay) {
			if (targetDisplay < 0 || targetDisplay >= DisplayCount) return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.display.target_invalid"), Severity.Error, "The selected Display is not available."));
			if (_displayCamera != null) {
				_displayCamera.targetDisplay = targetDisplay;
				return UnitResult.Success<Diagnostic>();
			}
			try {
				_displayCameraObject = new GameObject("ShitDesigner.ProgramDisplay");
				if (UnityEngine.Application.isPlaying) UnityEngine.Object.DontDestroyOnLoad(_displayCameraObject);
				_displayCamera = _displayCameraObject.AddComponent<Camera>();
				_displayCamera.clearFlags = CameraClearFlags.SolidColor;
				_displayCamera.backgroundColor = Color.black;
				_displayCamera.cullingMask = 0;
				_displayCamera.orthographic = true;
				_displayCamera.orthographicSize = 1f;
				_displayCamera.nearClipPlane = 0.01f;
				_displayCamera.farClipPlane = 10f;
				_displayCamera.targetDisplay = targetDisplay;
				_blit = _displayCameraObject.AddComponent<ProgramDisplayBlitCamera>();
				_displayCamera.enabled = false;
				return UnitResult.Success<Diagnostic>();
			}
			catch (Exception exception) {
				Dispose();
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.display.camera_create_failed"), Severity.Error, "The Program display camera could not be created.", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			if (_displayCameraObject != null) UnityEngine.Object.DestroyImmediate(_displayCameraObject);
			_displayCameraObject = null; _displayCamera = null; _blit = null;
		}

		private sealed class ProgramDisplayBlitCamera : MonoBehaviour {
			public RenderTexture Source;
			private Camera _camera;
			private void OnEnable() {
				_camera = GetComponent<Camera>();
				RenderPipelineManager.endCameraRendering += OnEndCameraRendering;
			}
			private void OnDisable() { RenderPipelineManager.endCameraRendering -= OnEndCameraRendering; }
			private void OnRenderImage(RenderTexture source, RenderTexture destination) {
				Graphics.Blit(Source != null && Source.IsCreated() ? Source : Texture2D.blackTexture, destination);
			}
			private void OnEndCameraRendering(ScriptableRenderContext context, Camera camera) {
				if (camera != _camera || GraphicsSettings.currentRenderPipeline == null) return;
				var target = Source != null && Source.IsCreated() ? (Texture)Source : Texture2D.blackTexture;
				var command = new CommandBuffer { name = "ShitDesigner.ProgramDisplay" };
				try {
					command.Blit(target, BuiltinRenderTextureType.CameraTarget);
					context.ExecuteCommandBuffer(command);
				}
				finally { command.Release(); }
			}
		}
	}

	/// <summary>
	/// Presentation owns only the stable Program surface. Closing the monitor
	/// removes a view; it never stops evaluation or releases Program Hold.
	/// </summary>
	public sealed class ProgramDisplayPresenter : IDisposable {
		private readonly ProgramHoldController _program;
		private readonly IProgramDisplayPort _displayPort;
		public ProgramDisplaySelection Selection { get; private set; }
		public bool IsOutputActive { get; private set; } = true;
		public bool MonitorOpen { get; private set; } = true;
		public bool EvaluationContinues => true;

		public ProgramDisplayPresenter(ProgramHoldController program, int requestedDisplay = ProgramDisplayPolicy.DefaultDisplay, int displayCount = 1) {
			_program = program ?? throw new ArgumentNullException(nameof(program));
			Selection = ProgramDisplayPolicy.Resolve(requestedDisplay, displayCount);
		}

		public ProgramDisplayPresenter(ProgramHoldController program, IProgramDisplayPort displayPort, int requestedDisplay = ProgramDisplayPolicy.DefaultDisplay) {
			_program = program ?? throw new ArgumentNullException(nameof(program));
			_displayPort = displayPort ?? throw new ArgumentNullException(nameof(displayPort));
			var selected = _displayPort.Activate(requestedDisplay);
			if (selected.IsFailure) throw new InvalidOperationException(selected.Error.Message);
			Selection = selected.Value;
		}

		public void RefreshDisplayCount(int displayCount) {
			var next = ProgramDisplayPolicy.Resolve(Selection.RequestedDisplay, displayCount);
			if (_displayPort != null) {
				var activated = _displayPort.Activate(Selection.RequestedDisplay);
				if (activated.IsSuccess) next = activated.Value;
			}
			Selection = next;
		}
		public Result<ProgramDisplaySelection, Diagnostic> SetRequestedDisplay(int requestedDisplay) {
			if (requestedDisplay < 0) return Result.Failure<ProgramDisplaySelection, Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.display.request_invalid"), Severity.Error, "The requested Display must not be negative."));
			var next = _displayPort == null
				? Result.Success<ProgramDisplaySelection, Diagnostic>(ProgramDisplayPolicy.Resolve(requestedDisplay))
				: _displayPort.Activate(requestedDisplay);
			if (next.IsSuccess) Selection = next.Value;
			return next;
		}
		public int DisplayCount => _displayPort?.DisplayCount ?? 1;
		public void SetOutputActive(bool active) {
			IsOutputActive = active;
			if (_displayPort is UnityProgramDisplayPort unityPort) unityPort.SetOutputActive(active);
		}
		public void CloseMonitor() => MonitorOpen = false;
		public void OpenMonitor() => MonitorOpen = true;
		public Result<ImageFrame, Diagnostic> GetPresentedFrame(ulong frameNumber) => _program.GetFrame(frameNumber);
		public UnitResult<Diagnostic> Present(RenderTexture surface) {
			if (!IsOutputActive) return UnitResult.Success<Diagnostic>();
			return _displayPort == null ? UnitResult.Success<Diagnostic>() : _displayPort.Present(surface, Selection);
		}
		public void Dispose() { if (_displayPort is IDisposable disposable) disposable.Dispose(); }
	}

	public readonly struct ProgramPerformanceReadModel {
		public double FramesPerSecond { get; }
		public double CpuFrameMilliseconds { get; }
		public double GpuFrameMilliseconds { get; }
		public int ConsecutiveBadFrames { get; }
		public bool WarningActive { get; }

		internal ProgramPerformanceReadModel(double fps, double cpu, double gpu, int badFrames, bool warningActive) {
			FramesPerSecond = fps;
			CpuFrameMilliseconds = cpu;
			GpuFrameMilliseconds = gpu;
			ConsecutiveBadFrames = badFrames;
			WarningActive = warningActive;
		}
	}

	public sealed class ProgramPerformanceMonitor : IRuntimeProgramPerformanceSink {
		public const int WarningFrameCount = 60;
		public const double MinimumFramesPerSecond = 59d;
		public const double MaximumFrameMilliseconds = 16.67d;
		private int _consecutiveBadFrames;
		public ProgramPerformanceReadModel Current { get; private set; }

		public ProgramPerformanceMonitor() {
			Current = UnavailableReadModel();
		}

		/// <summary>Observes the delayed FrameTiming completion. The CPU
		/// argument is the main/render critical-path workload supplied by
		/// Bootstrap, not Unity's wait-inclusive cpuFrameTime.</summary>
		public bool Observe(double framesPerSecond, double cpuWorkloadMilliseconds, double gpuFrameMilliseconds) {
			if (double.IsNaN(framesPerSecond) || double.IsInfinity(framesPerSecond) ||
				double.IsNaN(cpuWorkloadMilliseconds) || double.IsInfinity(cpuWorkloadMilliseconds) ||
				double.IsNaN(gpuFrameMilliseconds) || double.IsInfinity(gpuFrameMilliseconds))
				throw new ArgumentOutOfRangeException(nameof(framesPerSecond));
			var bad = framesPerSecond < MinimumFramesPerSecond || cpuWorkloadMilliseconds > MaximumFrameMilliseconds || gpuFrameMilliseconds > MaximumFrameMilliseconds;
			_consecutiveBadFrames = bad ? _consecutiveBadFrames + 1 : 0;
			var warning = _consecutiveBadFrames >= WarningFrameCount;
			Current = new ProgramPerformanceReadModel(framesPerSecond, cpuWorkloadMilliseconds, gpuFrameMilliseconds, _consecutiveBadFrames, warning);
			return warning;
		}

		void IRuntimeProgramPerformanceSink.Reset() {
			_consecutiveBadFrames = 0;
			Current = UnavailableReadModel();
		}

		RuntimeProgramPerformanceSnapshot IRuntimeProgramPerformanceSink.Capture() {
			var available = IsFinitePositive(Current.FramesPerSecond) && IsFinitePositive(Current.CpuFrameMilliseconds) && IsFinitePositive(Current.GpuFrameMilliseconds);
			return available
				? new RuntimeProgramPerformanceSnapshot(Current.FramesPerSecond, Current.CpuFrameMilliseconds,
					Current.GpuFrameMilliseconds, Current.ConsecutiveBadFrames, Current.WarningActive, true)
				: RuntimeProgramPerformanceSnapshot.Unavailable;
		}

		void IRuntimeProgramPerformanceSink.Observe(double framesPerSecond, double cpuFrameMilliseconds, double gpuFrameMilliseconds) {
			Observe(framesPerSecond, cpuFrameMilliseconds, gpuFrameMilliseconds);
		}

		private static ProgramPerformanceReadModel UnavailableReadModel()
			=> new ProgramPerformanceReadModel(double.NaN, double.NaN, double.NaN, 0, false);

		private static bool IsFinitePositive(double value)
			=> value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
	}
}
