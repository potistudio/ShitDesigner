using System;
#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
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
		private WindowsSecondaryDisplayWindow _secondaryDisplayWindow;
		private bool _disposed;
		public int DisplayCount => Display.displays == null || Display.displays.Length == 0 ? 1 : Display.displays.Length;

		public Result<ProgramDisplaySelection, Diagnostic> Activate(int requestedDisplay) {
			var selection = ProgramDisplayPolicy.Resolve(requestedDisplay, DisplayCount);
			if (selection.UsesProgramMonitor) {
				SetDisplayCameraActive(false);
				return Result.Success<ProgramDisplaySelection, Diagnostic>(selection);
			}
			var primaryWindow = WindowsSecondaryDisplayWindow.CapturePrimaryWindow();
			try {
				var display = Display.displays[selection.ResolvedDisplay];
				display.Activate();
#if UNITY_STANDALONE_WIN
				if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D11 ||
					SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Direct3D12)
					display.SetParams(display.systemWidth, display.systemHeight, 0, 0);
#endif
			}
			catch (Exception exception) {
				return Result.Failure<ProgramDisplaySelection, Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.display.activate_failed"), Severity.Error,
					"The selected Unity Display could not be activated.", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
			var camera = EnsureDisplayCamera(selection.ResolvedDisplay);
			if (camera.IsFailure) return Result.Failure<ProgramDisplaySelection, Diagnostic>(camera.Error);
			_secondaryDisplayWindow?.SetPrimaryWindow(primaryWindow);
			_secondaryDisplayWindow?.RequestFullscreen();
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
				SetDisplayCameraActive(true);
				return;
			}
			ShowStandbyFrame();
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
				_secondaryDisplayWindow = _displayCameraObject.AddComponent<WindowsSecondaryDisplayWindow>();
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
			_displayCameraObject = null; _displayCamera = null; _blit = null; _secondaryDisplayWindow = null;
		}

		private sealed class WindowsSecondaryDisplayWindow : MonoBehaviour {
#if UNITY_STANDALONE_WIN
			private const int StyleIndex = -16;
			private const uint Caption = 0x00C00000;
			private const uint ThickFrame = 0x00040000;
			private const uint SystemMenu = 0x00080000;
			private const uint MinimizeBox = 0x00020000;
			private const uint MaximizeBox = 0x00010000;
			private const uint Popup = 0x80000000;
			private const uint FrameChanged = 0x0020;
			private const uint NoActivate = 0x0010;
			private const uint ShowWindow = 0x0040;
			private const uint MonitorDefaultToNearest = 2;
			private static readonly IntPtr Topmost = new IntPtr(-1);
			private static readonly EnumWindowsCallback ConfigureSecondaryWindowCallback = ConfigureSecondaryWindow;

			private IntPtr _primaryWindow;
			private int _remainingAttempts;

			public static IntPtr CapturePrimaryWindow() {
				var window = GetActiveWindow();
				return window == IntPtr.Zero ? GetForegroundWindow() : window;
			}

			public void SetPrimaryWindow(IntPtr window) {
				if (window != IntPtr.Zero) _primaryWindow = window;
			}

			public void RequestFullscreen() {
				_remainingAttempts = 30;
			}

			private void LateUpdate() {
				if (_remainingAttempts <= 0) return;
				_remainingAttempts--;
				var handle = GCHandle.Alloc(this);
				try {
					EnumWindows(ConfigureSecondaryWindowCallback, GCHandle.ToIntPtr(handle));
				}
				finally {
					handle.Free();
				}
			}

			private static bool ConfigureSecondaryWindow(IntPtr window, IntPtr parameter) {
				var handle = GCHandle.FromIntPtr(parameter);
				return handle.Target is WindowsSecondaryDisplayWindow controller && controller.ConfigureWindow(window);
			}

			private bool ConfigureWindow(IntPtr window) {
				if (_primaryWindow == IntPtr.Zero || window == IntPtr.Zero || window == _primaryWindow || !IsWindowVisible(window)) return true;
				GetWindowThreadProcessId(window, out var processId);
				if (processId != GetCurrentProcessId()) return true;

				var monitor = MonitorFromWindow(window, MonitorDefaultToNearest);
				if (monitor == IntPtr.Zero) return true;
				var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
				if (!GetMonitorInfo(monitor, ref monitorInfo)) return true;

				var style = unchecked((uint)GetWindowLongPtr(window, StyleIndex).ToInt64());
				style = (style & ~(Caption | ThickFrame | SystemMenu | MinimizeBox | MaximizeBox)) | Popup;
				SetWindowLongPtr(window, StyleIndex, new IntPtr(unchecked((int)style)));
				var bounds = monitorInfo.Monitor;
				SetWindowPos(window, Topmost, bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
					FrameChanged | NoActivate | ShowWindow);
				return true;
			}

			[StructLayout(LayoutKind.Sequential)]
			private struct NativeRect {
				public int Left;
				public int Top;
				public int Right;
				public int Bottom;
			}

			[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
			private struct MonitorInfo {
				public int Size;
				public NativeRect Monitor;
				public NativeRect Work;
				public uint Flags;
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
			private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

			[DllImport("kernel32.dll")]
			private static extern uint GetCurrentProcessId();

			[DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
			private static extern IntPtr GetWindowLongPtr(IntPtr window, int index);

			[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
			private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);

			[DllImport("user32.dll")]
			private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);

			[DllImport("user32.dll", CharSet = CharSet.Auto)]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo monitorInfo);

			[DllImport("user32.dll")]
			[return: MarshalAs(UnmanagedType.Bool)]
			private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
#else
			public static IntPtr CapturePrimaryWindow() => IntPtr.Zero;
			public void SetPrimaryWindow(IntPtr window) { }
			public void RequestFullscreen() { }
#endif
		}

		private sealed class ProgramDisplayBlitCamera : MonoBehaviour {
			private const string ShaderName = "Hidden/ShitDesigner/ProgramDisplay";
			private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");
			private Camera _camera;
			private GameObject _surface;
			private Material _material;
			private Mesh _mesh;

			public RenderTexture Source {
				set {
					if (_material != null) _material.SetTexture(MainTextureId, value != null && value.IsCreated() ? value : Texture2D.blackTexture);
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
				_surface.transform.localScale = new Vector3(_camera.aspect, 1f, 1f);
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
			_displayPort?.SetOutputActive(active);
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
