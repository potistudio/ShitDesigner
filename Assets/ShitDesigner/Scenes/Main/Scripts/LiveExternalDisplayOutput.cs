using System;
using System.Collections.Generic;
using System.Linq;
#if UNITY_STANDALONE_WIN
using System.Collections;
using System.Runtime.InteropServices;
using AOT;
#endif
#if UNITY_STANDALONE_OSX
using System.Runtime.InteropServices;
#endif
using ShitDesigner.Rendering;
using UnityEngine;
using UnityEngine.UI;

namespace ShitDesigner.Main {
	/// <summary>Owns external Display activation, display transform, and Program frame presentation.</summary>
	[DisallowMultipleComponent]
	public sealed class LiveExternalDisplayOutput : MonoBehaviour, ILiveOutputMenuTarget {
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
				foreach (var output in _outputs.Values) {
					output.Clear();
					output.SetVisible(false);
				}
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
			if (!_initialized || !IsOutputActive || frames.Count == 0 || frames.Primary.FrameNumber == 0) return;
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
			foreach (var output in _outputs.Values) output.Present();
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
				var output = CreateOutput(displayNumber);
				// Unity's secondary Metal swapchain intermittently presents the main
				// window on macOS. The native presenter owns that screen instead.
#if !(UNITY_STANDALONE_OSX && !UNITY_EDITOR)
				var display = Display.displays[displayNumber - 1];
				if (!display.active) ActivateDisplay(display, output.WindowController);
#endif
				_outputs.Add(displayNumber, output);
			}
		}

		private DisplayOutput CreateOutput(int displayNumber) {
			var displayTexture = new RenderTexture(LiveGraphRuntime.ProgramWidth, LiveGraphRuntime.ProgramHeight, 0, RenderTextureFormat.ARGB32) {
				name = "ShitDesigner.Main.ExternalDisplay." + displayNumber,
				useMipMap = false,
				autoGenerateMips = false
			};
			if (!displayTexture.Create()) {
				DestroyUnityObject(displayTexture);
				throw new InvalidOperationException("An external Display texture could not be created.");
			}
			ClearTexture(displayTexture);
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
			try {
				return new DisplayOutput(new MacExternalDisplayPresenter(displayNumber - 1, displayTexture), displayTexture);
			}
			catch {
				displayTexture.Release();
				DestroyUnityObject(displayTexture);
				throw;
			}
#else
			var canvasObject = new GameObject($"Live External Display Canvas {displayNumber}");
			canvasObject.transform.SetParent(transform, false);
			var canvas = canvasObject.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			canvas.targetDisplay = displayNumber - 1;
			canvas.enabled = false;
			var presenter = canvasObject.AddComponent<LiveProgramDisplayCanvas>();
			presenter.Initialize(canvas, displayTexture);
			return new DisplayOutput(canvas, canvasObject.AddComponent<WindowsDisplayWindowController>(), displayTexture);
#endif
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
			public Canvas Canvas { get; }
			public WindowsDisplayWindowController WindowController { get; }
			public RenderTexture Texture { get; }
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
			private readonly MacExternalDisplayPresenter m_MacPresenter;

			public DisplayOutput(MacExternalDisplayPresenter presenter, RenderTexture texture) {
				Canvas = null;
				WindowController = null;
				Texture = texture;
				m_MacPresenter = presenter;
			}
#endif

			public DisplayOutput(Canvas canvas, WindowsDisplayWindowController windowController, RenderTexture texture) {
				Canvas = canvas;
				WindowController = windowController;
				Texture = texture;
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
				m_MacPresenter = null;
#endif
			}

			public void SetVisible(bool visible) {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
				if (m_MacPresenter != null) {
					m_MacPresenter.SetVisible(visible);
					return;
				}
#endif
				Canvas.enabled = visible;
				WindowController.SetOutputVisible(visible);
			}

			public void Clear() => ClearTexture(Texture);

			public void Present() {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
				m_MacPresenter?.Present();
#endif
			}

			public void Dispose() {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
				m_MacPresenter?.Dispose();
#endif
				Texture.Release();
				DestroyUnityObject(Texture);
				if (Canvas != null) DestroyUnityObject(Canvas.gameObject);
			}
		}
	}

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
	internal sealed class MacExternalDisplayPresenter : IDisposable {
		private const string LibraryName = "shitdesigner_mac_display";
		private readonly int m_DisplayIndex;
		private readonly IntPtr m_RenderEvent;
		private bool m_Disposed;

		public MacExternalDisplayPresenter(int displayIndex, RenderTexture source) {
			m_DisplayIndex = displayIndex;
			if (!ShitDesignerMacDisplayCreate(displayIndex))
				throw new InvalidOperationException("The native macOS external Display window could not be created.");
			m_RenderEvent = ShitDesignerMacDisplayGetRenderEvent();
			if (m_RenderEvent == IntPtr.Zero) {
				ShitDesignerMacDisplayDestroy(displayIndex);
				throw new InvalidOperationException("The native macOS external Display renderer is unavailable.");
			}
			ShitDesignerMacDisplaySetSource(displayIndex, source.GetNativeTexturePtr());
		}

		public void SetVisible(bool visible) {
			if (!m_Disposed) ShitDesignerMacDisplaySetVisible(m_DisplayIndex, visible);
		}

		public void Present() {
			if (!m_Disposed) GL.IssuePluginEvent(m_RenderEvent, m_DisplayIndex);
		}

		public void Dispose() {
			if (m_Disposed) return;
			m_Disposed = true;
			ShitDesignerMacDisplayDestroy(m_DisplayIndex);
		}

		[DllImport(LibraryName)] [return: MarshalAs(UnmanagedType.I1)]
		private static extern bool ShitDesignerMacDisplayCreate(int displayIndex);
		[DllImport(LibraryName)] private static extern void ShitDesignerMacDisplaySetSource(int displayIndex, IntPtr sourceTexture);
		[DllImport(LibraryName)] private static extern void ShitDesignerMacDisplaySetVisible(int displayIndex, [MarshalAs(UnmanagedType.I1)] bool visible);
		[DllImport(LibraryName)] private static extern void ShitDesignerMacDisplayDestroy(int displayIndex);
		[DllImport(LibraryName)] private static extern IntPtr ShitDesignerMacDisplayGetRenderEvent();
	}
#endif

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
#else
		public void SetOutputVisible(bool visible) { }
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
	public sealed class LiveProgramDisplayCanvas : MonoBehaviour {
		private RawImage m_Image;

		public Texture Source => m_Image == null ? null : m_Image.texture;

		public void Initialize(Canvas canvas, RenderTexture source) {
			if (canvas == null) throw new ArgumentNullException(nameof(canvas));
			var imageObject = new GameObject("Live External Program Display Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage));
			imageObject.transform.SetParent(canvas.transform, false);
			var rectTransform = (RectTransform)imageObject.transform;
			rectTransform.anchorMin = Vector2.zero;
			rectTransform.anchorMax = Vector2.one;
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
			m_Image = imageObject.GetComponent<RawImage>();
			m_Image.texture = source != null && source.IsCreated() ? source : Texture2D.blackTexture;
			m_Image.color = Color.white;
			m_Image.raycastTarget = false;
		}
	}
}
