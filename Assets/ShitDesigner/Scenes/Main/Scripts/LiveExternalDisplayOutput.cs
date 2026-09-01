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
		private const int OutputCount = 2;
		[SerializeField] private Shader _displayTransformShader;
		[SerializeField, Min(0.1f)] private float m_TestPatternMotionSpeed = 1f;

		private DisplayTransformPass _displayTransform;
		private Material m_TestPatternMaterial;
		private readonly Dictionary<int, DisplayOutput> _outputs = new Dictionary<int, DisplayOutput>();
		private readonly bool[] m_OutputActive = new bool[OutputCount];
		private LiveProgramFrames m_LatestFrames;
		private ulong _presentedFrameNumber;
		private bool _initialized;
		private bool m_OutputsSwapped;

		public int ConnectedDisplayCount => Display.displays?.Length ?? 0;
		public IReadOnlyList<int> ConnectedExternalDisplayNumbers => Enumerable.Range(2, Math.Min(OutputCount, Math.Max(0, ConnectedDisplayCount - 1))).ToArray();
		public bool IsOutputActive => m_OutputActive.Any(active => active);
		public bool IsTestPatternVisible { get; private set; }
		public ExternalDisplayScalingMode ScalingMode { get; private set; } = ExternalDisplayScalingMode.Fill;
		public ExternalDisplayEmulationAspect EmulationAspect { get; private set; } = ExternalDisplayEmulationAspect.Display;
		public bool IsAvailable => !UnityEngine.Application.isEditor && ConnectedDisplayCount > 1;
		public bool CanSwapOutputs => !UnityEngine.Application.isEditor && ConnectedDisplayCount > OutputCount;
		public ulong PresentedFrameNumber => _presentedFrameNumber;
		public string DisplayIdentity => DescribeDisplays();
		public string LastError { get; private set; } = string.Empty;

		public void Initialize() {
			Shutdown();
			if (_displayTransformShader == null) throw new InvalidOperationException("A DisplayTransform shader is required.");
			_displayTransform = new DisplayTransformPass(_displayTransformShader);
			var testPatternShader = Resources.Load<Shader>("ExternalDisplayTestPattern");
			if (testPatternShader == null) throw new InvalidOperationException("The external Display test pattern shader is required.");
			m_TestPatternMaterial = new Material(testPatternShader) { name = "ShitDesigner.ExternalDisplayTestPattern" };
			_initialized = true;
		}

		public bool IsActive(LiveOutputKind output) => m_OutputActive[OutputIndex(output)];

		public bool IsOutputAvailable(LiveOutputKind output)
			=> !UnityEngine.Application.isEditor && ConnectedDisplayCount >= DisplayNumberForOutput(OutputIndex(output));

		public bool SetOutputActive(LiveOutputKind output, bool active) {
			if (!_initialized) return Fail("External Display output is not initialized.");
			var outputIndex = OutputIndex(output);
			var displayNumber = DisplayNumberForOutput(outputIndex);
			if (!active) {
				m_OutputActive[outputIndex] = false;
				if (_outputs.TryGetValue(displayNumber, out var displayOutput)) {
					displayOutput.Clear();
					displayOutput.Present();
				}
				ApplyOutputVisibility();
				LastError = string.Empty;
				return true;
			}
			if (!IsOutputAvailable(output)) return Fail(UnityEngine.Application.isEditor
				? "External Display output requires a standalone Player."
				: $"Display {displayNumber} is not connected.");

			if (OutputsDoNotMatchConnectedDisplays()) RebuildOutputs();
			m_OutputActive[outputIndex] = true;
			ApplyOutputVisibility();
			LastError = string.Empty;
			return true;
		}

		public bool SwapOutputs() {
			if (!_initialized) return Fail("External Display output is not initialized.");
			if (!CanSwapOutputs) return Fail("Two external Displays are required to swap outputs.");
			m_OutputsSwapped = !m_OutputsSwapped;
			_presentedFrameNumber = 0;
			ApplyOutputVisibility();
			if (IsTestPatternVisible) {
				RenderTestPatterns();
				foreach (var output in _outputs.Values) output.Present();
			}
			else PresentLatestFrames();
			LastError = string.Empty;
			return true;
		}

		public bool SetScalingMode(ExternalDisplayScalingMode mode) {
			if (!Enum.IsDefined(typeof(ExternalDisplayScalingMode), mode)) return Fail("The external Display scaling mode is invalid.");
			if (ScalingMode == mode) return true;
			ScalingMode = mode;
			foreach (var output in _outputs.Values) {
				output.SetScalingMode(mode);
				output.Present();
			}
			LastError = string.Empty;
			return true;
		}

		public bool SetEmulationAspect(ExternalDisplayEmulationAspect aspect) {
			if (!Enum.IsDefined(typeof(ExternalDisplayEmulationAspect), aspect)) return Fail("The external Display emulation aspect is invalid.");
			if (EmulationAspect == aspect) return true;
			EmulationAspect = aspect;
			foreach (var output in _outputs.Values) {
				output.SetEmulationAspect(aspect);
				output.Present();
			}
			LastError = string.Empty;
			return true;
		}

		public bool SetTestPatternVisible(bool visible) {
			if (!_initialized) return Fail("External Display output is not initialized.");
			if (visible && !IsAvailable) return Fail(UnityEngine.Application.isEditor
				? "Display test patterns require a standalone Player."
				: "No external Display is connected.");
			if (IsTestPatternVisible == visible) return true;
			if (visible && OutputsDoNotMatchConnectedDisplays()) RebuildOutputs();

			IsTestPatternVisible = visible;
			_presentedFrameNumber = 0;
			if (visible) RenderTestPatterns();
			else foreach (var output in _outputs.Values) output.Clear();
			ApplyOutputVisibility();
			foreach (var output in _outputs.Values) output.Present();
			LastError = string.Empty;
			return true;
		}

		public void Present(LiveProgramFrames frames) {
			if (!_initialized) return;
			m_LatestFrames = frames;
			if (IsTestPatternVisible) {
				if (OutputsDoNotMatchConnectedDisplays()) {
					RebuildOutputs();
					ApplyOutputVisibility();
				}
				RenderTestPatterns();
				foreach (var output in _outputs.Values) output.Present();
				return;
			}
			if (!IsOutputActive || frames.Count == 0 || frames.Primary.FrameNumber == 0) return;
			var outputsRebuilt = false;
			if (IsOutputActive && OutputsDoNotMatchConnectedDisplays()) {
				RebuildOutputs();
				ApplyOutputVisibility();
				outputsRebuilt = true;
			}
			if (frames.Primary.FrameNumber == _presentedFrameNumber && !outputsRebuilt) return;
			foreach (var output in _outputs) {
				var frameIndex = OutputIndexForDisplay(output.Key);
				if (m_OutputActive[frameIndex] && frameIndex < frames.Count && frames[frameIndex].Texture != null)
					_displayTransform.Blit(frames[frameIndex].Texture, output.Value.Texture, DisplayTransformMode.HdrAces);
				else output.Value.Clear();
			}
			foreach (var output in _outputs) if (m_OutputActive[OutputIndexForDisplay(output.Key)]) output.Value.Present();
			_presentedFrameNumber = frames.Primary.FrameNumber;
		}

		private void PresentLatestFrames() {
			if (m_LatestFrames.Count > 0 && m_LatestFrames.Primary.FrameNumber > 0) {
				Present(m_LatestFrames);
				return;
			}
			foreach (var output in _outputs.Values) {
				output.Clear();
				output.Present();
			}
		}

		public void Shutdown() {
			Array.Clear(m_OutputActive, 0, m_OutputActive.Length);
			IsTestPatternVisible = false;
			m_OutputsSwapped = false;
			_initialized = false;
			m_LatestFrames = default(LiveProgramFrames);
			_presentedFrameNumber = 0;
			DestroyOutputs();
			_displayTransform?.Dispose();
			_displayTransform = null;
			DestroyUnityObject(m_TestPatternMaterial);
			m_TestPatternMaterial = null;
		}

		private void OnDestroy() => Shutdown();

		private bool Fail(string error) {
			LastError = error;
			return false;
		}

		private static int OutputIndex(LiveOutputKind output) {
			var index = (int)output;
			if (index < 0 || index >= OutputCount) throw new ArgumentOutOfRangeException(nameof(output));
			return index;
		}

		internal static int ResolveDisplayNumber(LiveOutputKind output, bool swapped) {
			var outputIndex = OutputIndex(output);
			return 2 + (swapped ? OutputCount - 1 - outputIndex : outputIndex);
		}

		internal static LiveOutputKind ResolveOutput(int displayNumber, bool swapped) {
			var displayIndex = displayNumber - 2;
			if (displayIndex < 0 || displayIndex >= OutputCount) throw new ArgumentOutOfRangeException(nameof(displayNumber));
			return (LiveOutputKind)(swapped ? OutputCount - 1 - displayIndex : displayIndex);
		}

		private int DisplayNumberForOutput(int outputIndex) => ResolveDisplayNumber((LiveOutputKind)outputIndex, m_OutputsSwapped);

		private int OutputIndexForDisplay(int displayNumber) => (int)ResolveOutput(displayNumber, m_OutputsSwapped);

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
			for (var displayNumber = 2; displayNumber <= Math.Min(ConnectedDisplayCount, OutputCount + 1); displayNumber++) {
				var output = CreateOutput(displayNumber);
				// Unity's secondary Metal swapchain intermittently presents the main
				// window on macOS. The native presenter owns that screen instead.
#if !(UNITY_STANDALONE_OSX && !UNITY_EDITOR)
				var display = Display.displays[displayNumber - 1];
				if (!display.active) ActivateDisplay(display, output.WindowController);
#endif
				_outputs.Add(displayNumber, output);
			}
			ApplyOutputVisibility();
		}

		private DisplayOutput CreateOutput(int displayNumber) {
			var display = Display.displays[displayNumber - 1];
			var resolution = ResolveDisplayResolution(display.systemWidth, display.systemHeight);
			// DisplayTransform writes final sRGB-encoded bytes itself. The native
			// macOS presenter must therefore sample an unorm texture; marking this
			// texture as sRGB would make Metal decode it before presentation and
			// lift the midtones when the display layer encodes them again.
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
			var displayTexture = new RenderTexture(resolution.x, resolution.y, 0,
				RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
#else
			var displayTexture = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.ARGB32) {
#endif
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
				var output = new DisplayOutput(new MacExternalDisplayPresenter(displayNumber - 1, displayTexture), displayTexture);
				output.SetScalingMode(ScalingMode);
				output.SetEmulationAspect(EmulationAspect);
				return output;
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
			var output = new DisplayOutput(canvas, presenter, canvasObject.AddComponent<WindowsDisplayWindowController>(), displayTexture);
			output.SetScalingMode(ScalingMode);
			output.SetEmulationAspect(EmulationAspect);
			return output;
#endif
		}

		internal static Vector2Int ResolveDisplayResolution(int systemWidth, int systemHeight) {
			return systemWidth > 0 && systemHeight > 0
				? new Vector2Int(systemWidth, systemHeight)
				: new Vector2Int(LiveGraphRuntime.ProgramWidth, LiveGraphRuntime.ProgramHeight);
		}

		private bool OutputsDoNotMatchConnectedDisplays() {
			var connectedOutputCount = Math.Min(OutputCount, Math.Max(0, ConnectedDisplayCount - 1));
			if (_outputs.Count != connectedOutputCount) return true;
			for (var displayNumber = 2; displayNumber <= connectedOutputCount + 1; displayNumber++)
				if (!_outputs.ContainsKey(displayNumber)) return true;
			return false;
		}

		private void ApplyOutputVisibility() {
			foreach (var output in _outputs) output.Value.SetContentVisible(IsTestPatternVisible || m_OutputActive[OutputIndexForDisplay(output.Key)]);
			foreach (var output in _outputs.Values) output.SetWindowsVisible(IsTestPatternVisible || IsOutputActive);
		}

		private void RenderTestPatterns() {
			m_TestPatternMaterial.SetFloat("_PatternTime", Time.unscaledTime * m_TestPatternMotionSpeed);
			foreach (var output in _outputs) {
				m_TestPatternMaterial.SetFloat("_DisplayNumber", output.Key);
				m_TestPatternMaterial.SetVector("_DisplayResolution", new Vector4(
					output.Value.Texture.width,
					output.Value.Texture.height,
					0f,
					0f));
				Graphics.Blit(Texture2D.blackTexture, output.Value.Texture, m_TestPatternMaterial);
			}
		}

		private void DestroyOutputs() {
			foreach (var output in _outputs.Values) output.Dispose();
			_outputs.Clear();
		}

		private string DescribeDisplays() {
			if (!IsAvailable) return "No external Display is available.";
			return string.Join(", ", ConnectedExternalDisplayNumbers.Select(displayNumber => {
				var display = Display.displays[displayNumber - 1];
				var output = OutputIndexForDisplay(displayNumber) == (int)LiveOutputKind.Program ? "Output 1 Program" : "Output 2 Overlay";
				return $"Display {displayNumber} ({display.systemWidth}x{display.systemHeight}, {output})";
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
			public LiveProgramDisplayCanvas CanvasPresenter { get; }
			public WindowsDisplayWindowController WindowController { get; }
			public RenderTexture Texture { get; }
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
			private readonly MacExternalDisplayPresenter m_MacPresenter;

			public DisplayOutput(MacExternalDisplayPresenter presenter, RenderTexture texture) {
				Canvas = null;
				CanvasPresenter = null;
				WindowController = null;
				Texture = texture;
				m_MacPresenter = presenter;
			}
#endif

			public DisplayOutput(Canvas canvas, LiveProgramDisplayCanvas canvasPresenter, WindowsDisplayWindowController windowController, RenderTexture texture) {
				Canvas = canvas;
				CanvasPresenter = canvasPresenter;
				WindowController = windowController;
				Texture = texture;
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
				m_MacPresenter = null;
#endif
			}

			public void SetContentVisible(bool visible) {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
				if (m_MacPresenter != null) {
					m_MacPresenter.SetVisible(visible);
					return;
				}
#endif
				Canvas.enabled = visible;
			}

			public void SetWindowsVisible(bool visible) => WindowController?.SetOutputVisible(visible);

			public void SetScalingMode(ExternalDisplayScalingMode mode) {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
				if (m_MacPresenter != null) {
					m_MacPresenter.SetScalingMode(mode);
					return;
				}
#endif
				CanvasPresenter?.SetScalingMode(mode);
			}

			public void SetEmulationAspect(ExternalDisplayEmulationAspect aspect) {
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
				if (m_MacPresenter != null) {
					m_MacPresenter.SetEmulationAspect(aspect);
					return;
				}
#endif
				CanvasPresenter?.SetEmulationAspect(aspect);
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

		public void SetScalingMode(ExternalDisplayScalingMode mode) {
			if (!m_Disposed) ShitDesignerMacDisplaySetScalingMode(m_DisplayIndex, (int)mode);
		}

		public void SetEmulationAspect(ExternalDisplayEmulationAspect aspect) {
			if (!m_Disposed) ShitDesignerMacDisplaySetEmulationAspect(m_DisplayIndex, aspect.AspectRatio());
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
		[DllImport(LibraryName)] private static extern void ShitDesignerMacDisplaySetScalingMode(int displayIndex, int scalingMode);
		[DllImport(LibraryName)] private static extern void ShitDesignerMacDisplaySetEmulationAspect(int displayIndex, float aspectRatio);
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
		private RectTransform m_EmulationTransform;
		private AspectRatioFitter m_EmulationAspectRatioFitter;
		private RectTransform m_ImageTransform;
		private AspectRatioFitter m_AspectRatioFitter;

		public Texture Source => m_Image == null ? null : m_Image.texture;

		public void Initialize(Canvas canvas, RenderTexture source) {
			if (canvas == null) throw new ArgumentNullException(nameof(canvas));
			var backgroundObject = new GameObject("Live External Program Display Background", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
			backgroundObject.transform.SetParent(canvas.transform, false);
			FillParent((RectTransform)backgroundObject.transform);
			var background = backgroundObject.GetComponent<Image>();
			background.color = Color.black;
			background.raycastTarget = false;
			var emulationObject = new GameObject("Live External Program Display Emulation", typeof(RectTransform), typeof(AspectRatioFitter));
			emulationObject.transform.SetParent(canvas.transform, false);
			m_EmulationTransform = (RectTransform)emulationObject.transform;
			FillParent(m_EmulationTransform);
			m_EmulationAspectRatioFitter = emulationObject.GetComponent<AspectRatioFitter>();
			var imageObject = new GameObject("Live External Program Display Image", typeof(RectTransform), typeof(CanvasRenderer), typeof(RawImage), typeof(AspectRatioFitter));
			imageObject.transform.SetParent(emulationObject.transform, false);
			m_ImageTransform = (RectTransform)imageObject.transform;
			ResetToParentRect();
			m_Image = imageObject.GetComponent<RawImage>();
			m_Image.texture = source != null && source.IsCreated() ? source : Texture2D.blackTexture;
			m_Image.color = Color.white;
			m_Image.raycastTarget = false;
			m_AspectRatioFitter = imageObject.GetComponent<AspectRatioFitter>();
			m_AspectRatioFitter.aspectRatio = source != null && source.height > 0 ? (float)source.width / source.height : 16f / 9f;
			SetScalingMode(ExternalDisplayScalingMode.Fill);
			SetEmulationAspect(ExternalDisplayEmulationAspect.Display);
		}

		public void SetScalingMode(ExternalDisplayScalingMode mode) {
			if (m_AspectRatioFitter == null || m_ImageTransform == null) return;
			ResetToParentRect();
			switch (mode) {
				case ExternalDisplayScalingMode.Stretch:
					m_AspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.None;
					ResetToParentRect();
					break;
				case ExternalDisplayScalingMode.Fill:
					m_AspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
					break;
				case ExternalDisplayScalingMode.Fit:
					m_AspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
					break;
				default:
					throw new ArgumentOutOfRangeException(nameof(mode));
			}
		}

		public void SetEmulationAspect(ExternalDisplayEmulationAspect aspect) {
			if (m_EmulationAspectRatioFitter == null || m_EmulationTransform == null) return;
			var aspectRatio = aspect.AspectRatio();
			if (aspect == ExternalDisplayEmulationAspect.Display) {
				m_EmulationAspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.None;
				FillParent(m_EmulationTransform);
				return;
			}
			FillParent(m_EmulationTransform);
			m_EmulationAspectRatioFitter.aspectRatio = aspectRatio;
			m_EmulationAspectRatioFitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
		}

		private void ResetToParentRect() {
			FillParent(m_ImageTransform);
		}

		private static void FillParent(RectTransform rectTransform) {
			rectTransform.anchorMin = Vector2.zero;
			rectTransform.anchorMax = Vector2.one;
			rectTransform.offsetMin = Vector2.zero;
			rectTransform.offsetMax = Vector2.zero;
		}
	}
}
