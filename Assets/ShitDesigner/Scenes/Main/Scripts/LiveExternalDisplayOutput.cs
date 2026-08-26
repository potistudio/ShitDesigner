using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_STANDALONE_WIN
using System.Collections;
using System.Runtime.InteropServices;
using AOT;
#endif
using ShitDesigner.Rendering;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Owns external Display activation, display transform, and Program frame presentation.</summary>
	[DisallowMultipleComponent]
	public sealed class LiveExternalDisplayOutput : MonoBehaviour {
		[SerializeField] private Shader _displayTransformShader;

		private DisplayTransformPass _displayTransform;
		private readonly Dictionary<int, DisplayOutput> _outputs = new Dictionary<int, DisplayOutput>();
		private ulong _presentedFrameNumber;
		private bool _initialized;

		public int ConnectedDisplayCount => Display.displays?.Length ?? 0;
		public IReadOnlyList<int> ConnectedExternalDisplayNumbers => Enumerable.Range(2, Math.Max(0, ConnectedDisplayCount - 1)).ToArray();
		public bool IsOutputActive { get; private set; }
		public bool IsAvailable => !UnityEngine.Application.isEditor && ConnectedDisplayCount > 1;
		public ulong PresentedFrameNumber => _presentedFrameNumber;
		public string DisplayIdentity => DescribeDisplays();
		public string LastError { get; private set; } = string.Empty;

		public void Initialize() {
			Shutdown();
			if (_displayTransformShader == null) throw new InvalidOperationException("A DisplayTransform shader is required.");
			_displayTransform = new DisplayTransformPass(_displayTransformShader);
			_initialized = true;
		}

		public bool SetOutputActive(bool active) {
			if (!_initialized) return Fail("External Display output is not initialized.");
			if (!active) {
				IsOutputActive = false;
				foreach (var output in _outputs.Values) output.SetVisible(false);
				LastError = string.Empty;
				return true;
			}
			if (!IsAvailable) return Fail(UnityEngine.Application.isEditor
				? "External Display output requires a standalone Player."
				: "No external Display is connected.");

			if (OutputsDoNotMatchConnectedDisplays()) RebuildOutputs();
			foreach (var output in _outputs.Values) output.SetVisible(true);
			IsOutputActive = true;
			LastError = string.Empty;
			return true;
		}

		public void IdentifyDisplay() => Debug.Log(DisplayIdentity, this);

		public void Present(LiveProgramFrames frames) {
			if (!_initialized || frames.Count == 0 || frames.Primary.FrameNumber == 0) return;
			var outputsRebuilt = false;
			if (IsOutputActive && OutputsDoNotMatchConnectedDisplays()) {
				RebuildOutputs();
				foreach (var output in _outputs.Values) output.SetVisible(true);
				outputsRebuilt = true;
			}
			if (frames.Primary.FrameNumber == _presentedFrameNumber && !outputsRebuilt) return;
			foreach (var output in _outputs) {
				var frameIndex = output.Key - 2;
				if (frameIndex < frames.Count && frames[frameIndex].Texture != null)
					_displayTransform.Blit(frames[frameIndex].Texture, output.Value.Texture, DisplayTransformMode.HdrAces);
				else output.Value.Clear();
			}
			_presentedFrameNumber = frames.Primary.FrameNumber;
		}

		public void Shutdown() {
			IsOutputActive = false;
			_initialized = false;
			_presentedFrameNumber = 0;
			DestroyOutputs();
			_displayTransform?.Dispose();
			_displayTransform = null;
		}

		private void OnDestroy() => Shutdown();

		private bool Fail(string error) {
			LastError = error;
			IsOutputActive = false;
			return false;
		}

		private void ActivateDisplay(Display display, WindowsDisplayWindowController windowController) {
#if UNITY_STANDALONE_WIN
			var mainWindowState = WindowsMainWindowState.Capture();
#endif
			display.Activate();
#if UNITY_STANDALONE_WIN
			windowController.ApplyAfterActivation(mainWindowState);
#endif
		}

		private void RebuildOutputs() {
			DestroyOutputs();
			for (var displayNumber = 2; displayNumber <= ConnectedDisplayCount; displayNumber++) {
				var display = Display.displays[displayNumber - 1];
				var output = CreateOutput(displayNumber);
				if (!display.active) ActivateDisplay(display, output.WindowController);
				output.Camera.targetDisplay = displayNumber - 1;
				_outputs.Add(displayNumber, output);
			}
		}

		private DisplayOutput CreateOutput(int displayNumber) {
			var cameraObject = new GameObject($"Live External Display Camera {displayNumber}");
			cameraObject.transform.SetParent(transform, false);
			var camera = cameraObject.AddComponent<Camera>();
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = Color.black;
			camera.cullingMask = 0;
			camera.enabled = false;
			var displayTexture = new RenderTexture(LiveGraphRuntime.ProgramWidth, LiveGraphRuntime.ProgramHeight, 0, RenderTextureFormat.ARGB32) {
				name = "ShitDesigner.Main.ExternalDisplay." + displayNumber,
				useMipMap = false,
				autoGenerateMips = false
			};
			if (!displayTexture.Create()) {
				DestroyUnityObject(cameraObject);
				throw new InvalidOperationException("An external Display texture could not be created.");
			}
			ClearTexture(displayTexture);
			var presenter = cameraObject.AddComponent<LiveProgramDisplayCamera>();
			presenter.Source = displayTexture;
			return new DisplayOutput(camera, cameraObject.AddComponent<WindowsDisplayWindowController>(), displayTexture);
		}

		private bool OutputsDoNotMatchConnectedDisplays() {
			if (_outputs.Count != ConnectedDisplayCount - 1) return true;
			for (var displayNumber = 2; displayNumber <= ConnectedDisplayCount; displayNumber++)
				if (!_outputs.ContainsKey(displayNumber)) return true;
			return false;
		}

		private void DestroyOutputs() {
			foreach (var output in _outputs.Values) output.Dispose();
			_outputs.Clear();
		}

		private string DescribeDisplays() {
			if (!IsAvailable) return "No external Display is available.";
			return string.Join(", ", ConnectedExternalDisplayNumbers.Select(displayNumber => {
				var display = Display.displays[displayNumber - 1];
				return $"Display {displayNumber} ({display.systemWidth}x{display.systemHeight})";
			}));
		}

		private static void ClearTexture(RenderTexture texture) {
			var previous = RenderTexture.active;
			RenderTexture.active = texture;
			GL.Clear(true, true, Color.black);
			RenderTexture.active = previous;
		}

		private static void DestroyUnityObject(UnityEngine.Object value) {
			if (value == null) return;
			if (UnityEngine.Application.isPlaying) Destroy(value); else DestroyImmediate(value);
		}

		private readonly struct DisplayOutput {
			public Camera Camera { get; }
			public WindowsDisplayWindowController WindowController { get; }
			public RenderTexture Texture { get; }

			public DisplayOutput(Camera camera, WindowsDisplayWindowController windowController, RenderTexture texture) {
				Camera = camera;
				WindowController = windowController;
				Texture = texture;
			}

			public void SetVisible(bool visible) {
				Camera.enabled = visible;
				WindowController.SetOutputVisible(visible);
			}

			public void Clear() => ClearTexture(Texture);

			public void Dispose() {
				Texture.Release();
				DestroyUnityObject(Texture);
				DestroyUnityObject(Camera.gameObject);
			}
		}
	}

	[AddComponentMenu("")]
	public sealed class WindowsDisplayWindowController : MonoBehaviour {
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
		private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
#endif
	}

	public readonly struct WindowsMainWindowState {
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

		private WindowsMainWindowState(IntPtr window, IntPtr style, IntPtr extendedStyle, NativeRect rect, WindowPlacement placement) {
			Window = window;
			_style = style;
			_extendedStyle = extendedStyle;
			_rect = rect;
			_placement = placement;
		}

		public static WindowsMainWindowState Capture() {
			var window = WindowsDisplayWindowController.CaptureMainWindow();
			if (window == IntPtr.Zero || !GetWindowRect(window, out var rect)) return default;
			var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
			if (!GetWindowPlacement(window, ref placement)) return default;
			return new WindowsMainWindowState(window, GetWindowLongPtr(window, StyleIndex), GetWindowLongPtr(window, ExtendedStyleIndex), rect, placement);
		}

		public void Restore() {
			if (Window == IntPtr.Zero || !IsWindow(Window)) return;
			SetWindowLongPtr(Window, StyleIndex, _style);
			SetWindowLongPtr(Window, ExtendedStyleIndex, _extendedStyle);
			var placement = _placement;
			placement.Length = Marshal.SizeOf<WindowPlacement>();
			SetWindowPlacement(Window, ref placement);
			SetWindowPos(Window, IntPtr.Zero, _rect.Left, _rect.Top, Math.Max(1, _rect.Right - _rect.Left), Math.Max(1, _rect.Bottom - _rect.Top),
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
		private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y, int width, int height, uint flags);
#else
		public IntPtr Window => IntPtr.Zero;
		public static WindowsMainWindowState Capture() => default;
		public void Restore() { }
#endif
	}

	[AddComponentMenu("")]
	public sealed class LiveProgramDisplayCamera : MonoBehaviour {
		public RenderTexture Source { private get; set; }

		private void OnRenderImage(RenderTexture source, RenderTexture destination) {
			if (Source != null) Graphics.Blit(Source, destination);
			else Graphics.Blit(source, destination);
		}
	}
}
