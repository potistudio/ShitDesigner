using System;
#if UNITY_STANDALONE_WIN
using System.Collections;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AOT;
#endif
using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;

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

	public static class ProgramDisplayFillLayout {
		public static Vector2 Scale(float sourceAspect, float targetAspect) {
			if (!float.IsFinite(sourceAspect) || sourceAspect <= 0f) sourceAspect = 1f;
			if (!float.IsFinite(targetAspect) || targetAspect <= 0f) targetAspect = 1f;
			return sourceAspect >= targetAspect
				? new Vector2(sourceAspect, 1f)
				: new Vector2(targetAspect, targetAspect / sourceAspect);
		}
	}

	public interface IProgramDisplayPort {
		int DisplayCount { get; }
		Result<ProgramDisplaySelection, Diagnostic> Activate(int requestedDisplay);
		UnitResult<Diagnostic> Present(RenderTexture surface, ProgramDisplaySelection selection);
		void SetOutputActive(bool active);
	}

	/// <summary>Unity boundary for selected Display activation and surface presentation.</summary>
	public sealed class UnityProgramDisplayPort : IProgramDisplayPort, IDisposable {
		private const int DisplayLayer = 31;
		private GameObject _displayCameraObject;
		private Camera _displayCamera;
		private ProgramDisplayBlitCamera _blit;
		private WindowsDisplayWindowController _displayWindowController;
		private bool _disposed;
		public int DisplayCount => Display.displays == null || Display.displays.Length == 0 ? 1 : Display.displays.Length;

		public Result<ProgramDisplaySelection, Diagnostic> Activate(int requestedDisplay) {
			var selection = ProgramDisplayPolicy.Resolve(requestedDisplay, DisplayCount);
			if (selection.UsesProgramMonitor) {
				SetDisplayCameraActive(false);
				return Result.Success<ProgramDisplaySelection, Diagnostic>(selection);
			}
			var mainWindowState = WindowsMainWindowState.Capture();
			try {
				var display = Display.displays[selection.ResolvedDisplay];
				if (!display.active) display.Activate();
			}
			catch (Exception exception) {
				return Result.Failure<ProgramDisplaySelection, Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.display.activate_failed"), Severity.Error,
					"The selected Unity Display could not be activated.", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
			var camera = EnsureDisplayCamera(selection.ResolvedDisplay);
			if (camera.IsFailure) return Result.Failure<ProgramDisplaySelection, Diagnostic>(camera.Error);
			_displayWindowController?.ApplyAfterActivation(mainWindowState);
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
			if (active) {
				_displayWindowController?.SetOutputVisible(true);
				SetDisplayCameraActive(true);
				return;
			}
			ShowStandbyFrame();
			_displayWindowController?.SetOutputVisible(false);
		}

		private void ShowStandbyFrame() {
			if (_displayCamera == null) return;
			if (_blit != null) _blit.Source = null;
			_displayCamera.enabled = true;
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
				_displayCameraObject.transform.position = new Vector3(100000f, 100000f, 100000f);
				_displayCamera.clearFlags = CameraClearFlags.SolidColor;
				_displayCamera.backgroundColor = Color.black;
				_displayCamera.cullingMask = 1 << DisplayLayer;
				_displayCamera.orthographic = true;
				_displayCamera.orthographicSize = 1f;
				_displayCamera.nearClipPlane = 0.01f;
				_displayCamera.farClipPlane = 10f;
				_displayCamera.targetDisplay = targetDisplay;
				_blit = _displayCameraObject.AddComponent<ProgramDisplayBlitCamera>();
				_blit.Initialize(_displayCamera, DisplayLayer);
				_displayWindowController = _displayCameraObject.AddComponent<WindowsDisplayWindowController>();
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
			_displayCameraObject = null; _displayCamera = null; _blit = null; _displayWindowController = null;
		}

		private sealed class WindowsDisplayWindowController : MonoBehaviour {
#if UNITY_STANDALONE_WIN
			private const uint NoSize = 0x0001;
			private const uint NoMove = 0x0002;
			private const uint NoActivate = 0x0010;
			private static readonly IntPtr Topmost = new IntPtr(-1);
			private static readonly EnumWindowsCallback ApplyCallback = ApplyTopmost;
			private static readonly HashSet<IntPtr> OutputWindows = new HashSet<IntPtr>();

			private IntPtr _mainWindow;
			private int _remainingAttempts;
			private Coroutine _restoreMainWindowRoutine;

			internal static IntPtr CaptureMainWindow() {
				var window = GetActiveWindow();
				if (BelongsToCurrentProcess(window)) return window;
				window = GetForegroundWindow();
				return BelongsToCurrentProcess(window) ? window : IntPtr.Zero;
			}

			public void ApplyAfterActivation(WindowsMainWindowState mainWindowState) {
				_mainWindow = mainWindowState.Window;
				_remainingAttempts = 30;
				if (_restoreMainWindowRoutine != null) StopCoroutine(_restoreMainWindowRoutine);
				_restoreMainWindowRoutine = StartCoroutine(RestoreMainWindow(mainWindowState));
			}

			public void SetOutputVisible(bool visible) {
				if (_mainWindow == IntPtr.Zero) return;
				EnumerateOutputWindows();
				foreach (var window in OutputWindows) {
					if (!IsWindow(window)) continue;
					ShowWindow(window, visible ? ShowNoActivate : Hide);
				}
				if (visible) _remainingAttempts = 30;
			}

			private IEnumerator RestoreMainWindow(WindowsMainWindowState state) {
				for (var frame = 0; frame < 3; frame++) {
					yield return new WaitForEndOfFrame();
					state.Restore();
				}
				_restoreMainWindowRoutine = null;
			}

			private void LateUpdate() {
				if (_remainingAttempts <= 0 || _mainWindow == IntPtr.Zero) return;
				_remainingAttempts--;
				EnumerateOutputWindows();
			}

			private void EnumerateOutputWindows() {
				var handle = GCHandle.Alloc(this);
				try {
					EnumWindows(ApplyCallback, GCHandle.ToIntPtr(handle));
				}
				finally {
					handle.Free();
				}
			}

			[MonoPInvokeCallback(typeof(EnumWindowsCallback))]
			private static bool ApplyTopmost(IntPtr window, IntPtr parameter) {
				var handle = GCHandle.FromIntPtr(parameter);
				return handle.Target is WindowsDisplayWindowController controller && controller.Apply(window);
			}

			private bool Apply(IntPtr window) {
				if (window == IntPtr.Zero || window == _mainWindow || !IsWindowVisible(window)) return true;
				if (!BelongsToCurrentProcess(window)) return true;
				OutputWindows.Add(window);
				SetWindowPos(window, Topmost, 0, 0, 0, 0, NoSize | NoMove | NoActivate);
				return true;
			}

			private static bool BelongsToCurrentProcess(IntPtr window) {
				if (window == IntPtr.Zero) return false;
				GetWindowThreadProcessId(window, out var processId);
				return processId == GetCurrentProcessId();
			}

			private delegate bool EnumWindowsCallback(IntPtr window, IntPtr parameter);

			[DllImport("user32.dll")]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool EnumWindows(EnumWindowsCallback callback, IntPtr parameter);

			[DllImport("user32.dll")]
			private static extern IntPtr GetActiveWindow();

			[DllImport("user32.dll")]
			private static extern IntPtr GetForegroundWindow();

			[DllImport("user32.dll")]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool IsWindowVisible(IntPtr window);

			[DllImport("user32.dll")]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool IsWindow(IntPtr window);

			private const int Hide = 0;
			private const int ShowNoActivate = 4;

			[DllImport("user32.dll")]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool ShowWindow(IntPtr window, int command);

			[DllImport("user32.dll")]
			private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

			[DllImport("kernel32.dll")]
			private static extern uint GetCurrentProcessId();

			[DllImport("user32.dll")]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool SetWindowPos(
				IntPtr window,
				IntPtr insertAfter,
				int x,
				int y,
				int width,
				int height,
				uint flags);
#else
			internal static IntPtr CaptureMainWindow() => IntPtr.Zero;
			public void ApplyAfterActivation(WindowsMainWindowState mainWindowState) { }
			public void SetOutputVisible(bool visible) { }
#endif
		}

		private readonly struct WindowsMainWindowState {
#if UNITY_STANDALONE_WIN
			private const int StyleIndex = -16;
			private const int ExtendedStyleIndex = -20;
			private const uint NoZOrder = 0x0004;
			private const uint NoActivate = 0x0010;
			private const uint FrameChanged = 0x0020;
			private const uint NoOwnerZOrder = 0x0200;

			private readonly IntPtr _style;
			private readonly IntPtr _extendedStyle;
			private readonly NativeRect _rect;
			private readonly WindowPlacement _placement;

			public IntPtr Window { get; }

			private WindowsMainWindowState(
				IntPtr window,
				IntPtr style,
				IntPtr extendedStyle,
				NativeRect rect,
				WindowPlacement placement) {
				Window = window;
				_style = style;
				_extendedStyle = extendedStyle;
				_rect = rect;
				_placement = placement;
			}

			public static WindowsMainWindowState Capture() {
				var window = WindowsDisplayWindowController.CaptureMainWindow();
				if (window == IntPtr.Zero) return default;

				if (!GetWindowRect(window, out var rect)) return default;
				var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
				if (!GetWindowPlacement(window, ref placement)) return default;
				return new WindowsMainWindowState(
					window,
					GetWindowLongPtr(window, StyleIndex),
					GetWindowLongPtr(window, ExtendedStyleIndex),
					rect,
					placement);
			}

			public void Restore() {
				if (Window == IntPtr.Zero || !IsWindow(Window)) return;

				SetWindowLongPtr(Window, StyleIndex, _style);
				SetWindowLongPtr(Window, ExtendedStyleIndex, _extendedStyle);
				var placement = _placement;
				placement.Length = Marshal.SizeOf<WindowPlacement>();
				SetWindowPlacement(Window, ref placement);
				SetWindowPos(
					Window,
					IntPtr.Zero,
					_rect.Left,
					_rect.Top,
					Math.Max(1, _rect.Right - _rect.Left),
					Math.Max(1, _rect.Bottom - _rect.Top),
					NoZOrder | NoActivate | FrameChanged | NoOwnerZOrder);
			}

			[StructLayout(LayoutKind.Sequential)]
			private struct NativeRect {
				public int Left;
				public int Top;
				public int Right;
				public int Bottom;
			}

			[StructLayout(LayoutKind.Sequential)]
			private struct NativePoint {
				public int X;
				public int Y;
			}

			[StructLayout(LayoutKind.Sequential)]
			private struct WindowPlacement {
				public int Length;
				public int Flags;
				public int ShowCommand;
				public NativePoint MinimumPosition;
				public NativePoint MaximumPosition;
				public NativeRect NormalPosition;
			}

			[DllImport("user32.dll")]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool IsWindow(IntPtr window);

			[DllImport("user32.dll")]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

			[DllImport("user32.dll")]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacement placement);

			[DllImport("user32.dll")]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool SetWindowPlacement(IntPtr window, ref WindowPlacement placement);

			[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
			private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

			[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
			private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

			[DllImport("user32.dll")]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool SetWindowPos(
				IntPtr window,
				IntPtr insertAfter,
				int x,
				int y,
				int width,
				int height,
				uint flags);
#else
			public IntPtr Window => IntPtr.Zero;
			public static WindowsMainWindowState Capture() => default;
			public void Restore() { }
#endif
		}

		private sealed class ProgramDisplayBlitCamera : MonoBehaviour {
			private const string ShaderName = "Hidden/ShitDesigner/ProgramDisplay";
			private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
			private Camera _camera;
			private GameObject _surface;
			private Material _material;
			private Mesh _mesh;
			private float m_SourceAspect = 16f / 9f;

			public RenderTexture Source {
				set {
					if (_material != null) _material.SetTexture(MainTextureId, value != null && value.IsCreated() ? value : Texture2D.blackTexture);
					if (value != null && value.height > 0) m_SourceAspect = (float)value.width / value.height;
				}
			}

			public void Initialize(Camera camera, int layer) {
				if (camera == null) throw new ArgumentNullException(nameof(camera));
				var shader = Resources.Load<Shader>("ProgramDisplay") ?? Shader.Find(ShaderName);
				if (shader == null) throw new InvalidOperationException("Program display shader is not available.");
				_camera = camera;
				_material = new Material(shader) { name = "ShitDesigner.ProgramDisplay" };
				_material.SetTexture(MainTextureId, Texture2D.blackTexture);
				_mesh = CreateFullscreenMesh();
				_surface = new GameObject("ShitDesigner.ProgramDisplaySurface") { layer = layer };
				_surface.transform.SetParent(transform, false);
				_surface.transform.localPosition = new Vector3(0f, 0f, 1f);
				var filter = _surface.AddComponent<MeshFilter>();
				filter.sharedMesh = _mesh;
				var renderer = _surface.AddComponent<MeshRenderer>();
				renderer.sharedMaterial = _material;
				renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				renderer.receiveShadows = false;
			}

			private void LateUpdate() {
				if (_camera == null || _surface == null) return;
				var scale = ProgramDisplayFillLayout.Scale(m_SourceAspect, _camera.aspect);
				_surface.transform.localScale = new Vector3(scale.x, scale.y, 1f);
			}

			private void OnDestroy() {
				ReleaseRuntimeObject(_material);
				ReleaseRuntimeObject(_mesh);
			}

			private static Mesh CreateFullscreenMesh() {
				var mesh = new Mesh { name = "ShitDesigner.ProgramDisplayMesh" };
				mesh.vertices = new[] {
					new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f),
					new Vector3(1f, 1f, 0f), new Vector3(-1f, 1f, 0f)
				};
				mesh.uv = new[] {
					new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f)
				};
				mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
				mesh.UploadMeshData(true);
				return mesh;
			}

			private static void ReleaseRuntimeObject(UnityEngine.Object instance) {
				if (instance == null) return;
				if (UnityEngine.Application.isPlaying) Destroy(instance);
				else DestroyImmediate(instance);
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
		public bool IsOutputActive { get; private set; }
		public bool MonitorOpen { get; private set; } = true;
		public bool EvaluationContinues => true;

		public ProgramDisplayPresenter(ProgramHoldController program, int requestedDisplay = ProgramDisplayPolicy.DefaultDisplay, int displayCount = 1) {
			_program = program ?? throw new ArgumentNullException(nameof(program));
			Selection = ProgramDisplayPolicy.Resolve(requestedDisplay, displayCount);
		}

		public ProgramDisplayPresenter(ProgramHoldController program, IProgramDisplayPort displayPort, int requestedDisplay = ProgramDisplayPolicy.DefaultDisplay) {
			_program = program ?? throw new ArgumentNullException(nameof(program));
			_displayPort = displayPort ?? throw new ArgumentNullException(nameof(displayPort));
			Selection = ProgramDisplayPolicy.Resolve(requestedDisplay, _displayPort.DisplayCount);
		}

		public void RefreshDisplayCount(int displayCount) {
			var next = ProgramDisplayPolicy.Resolve(Selection.RequestedDisplay, displayCount);
			if (_displayPort != null && IsOutputActive) {
				var activated = _displayPort.Activate(Selection.RequestedDisplay);
				if (activated.IsSuccess) next = activated.Value;
			}
			Selection = next;
		}
		public Result<ProgramDisplaySelection, Diagnostic> SetRequestedDisplay(int requestedDisplay) {
			if (requestedDisplay < 0) return Result.Failure<ProgramDisplaySelection, Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.display.request_invalid"), Severity.Error, "The requested Display must not be negative."));
			var next = _displayPort == null || !IsOutputActive
				? Result.Success<ProgramDisplaySelection, Diagnostic>(ProgramDisplayPolicy.Resolve(requestedDisplay, _displayPort?.DisplayCount ?? 1))
				: _displayPort.Activate(requestedDisplay);
			if (next.IsSuccess) Selection = next.Value;
			return next;
		}
		public int DisplayCount => _displayPort?.DisplayCount ?? 1;
		public Result<ProgramDisplaySelection, Diagnostic> SetOutputActive(bool active) {
			if (active && !IsOutputActive && _displayPort != null) {
				var activated = _displayPort.Activate(Selection.RequestedDisplay);
				if (activated.IsFailure) return activated;
				Selection = activated.Value;
			}
			IsOutputActive = active;
			_displayPort?.SetOutputActive(active);
			return Result.Success<ProgramDisplaySelection, Diagnostic>(Selection);
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
