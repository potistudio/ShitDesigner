using System;
using System.Collections;
using System.Runtime.InteropServices;
using UnityEngine;

namespace ShitDesigner.Rendering {
	/// <summary>
	/// Minimal standalone output for checking a physical secondary display.
	/// Assign a source Camera to mirror its view, or leave it empty to show a solid test image.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class SimpleExternalDisplayOutput : MonoBehaviour {
		[SerializeField, Min(2)] private int _displayNumber = 2;
		[SerializeField] private Camera _outputCamera;
		[SerializeField] private bool _createTestCameraWhenMissing = true;
		[SerializeField] private bool _activateOnStart;
		[SerializeField] private bool _preserveMainWindowMode = true;
		[SerializeField] private Color _testBackground = new Color(0.04f, 0.12f, 0.2f, 1f);
		[SerializeField] private Color _standbyBackground = Color.black;

		private Camera _runtimeCamera;
		private Camera _sourceCamera;
		private Camera _activeOutputCamera;
		private Coroutine _restoreMainWindowRoutine;

		public int DisplayNumber => _displayNumber;
		public int ConnectedDisplayCount => Display.displays?.Length ?? 0;
		public bool IsOutputActive { get; private set; }
		public string LastError { get; private set; } = string.Empty;
		public event Action<bool> OutputActiveChanged;

		private void Start() {
			if (_activateOnStart) ActivateExternalDisplay();
		}

		private void LateUpdate() {
			if (!IsOutputActive || _runtimeCamera == null || _sourceCamera == null) return;
			_runtimeCamera.transform.SetPositionAndRotation(
				_sourceCamera.transform.position,
				_sourceCamera.transform.rotation);
		}

		private void OnDisable() {
			if (_restoreMainWindowRoutine == null) return;
			StopCoroutine(_restoreMainWindowRoutine);
			_restoreMainWindowRoutine = null;
		}

		[ContextMenu("Activate External Display")]
		public void ActivateExternalDisplay() {
			SetOutputActive(true);
		}

		[ContextMenu("Deactivate External Display")]
		public void DeactivateExternalDisplay() {
			SetOutputActive(false);
		}

		public bool CanActivate(out string error) {
			return CanActivate(_displayNumber, out error);
		}

		public bool CanActivate(int displayNumber, out string error) {
			if (Application.isEditor) {
				error = "Unity Editor exposes only Display 1. Run a standalone build to use Display 2.";
				return false;
			}

			var normalizedDisplayNumber = Mathf.Max(2, displayNumber);
			var displayIndex = normalizedDisplayNumber - 1;
			if (Display.displays == null || displayIndex >= Display.displays.Length) {
				error = $"Display {normalizedDisplayNumber} is not connected. Connected displays: {Display.displays?.Length ?? 0}.";
				return false;
			}

			error = string.Empty;
			return true;
		}

		public bool SelectDisplay(int displayNumber) {
			if (IsOutputActive) return false;
			_displayNumber = Mathf.Max(2, displayNumber);
			return true;
		}

		public bool SetOutputActive(bool active) {
			if (!active) {
				var camera = CurrentOutputCamera();
				if (camera != null) ShowStandbyFrame(camera);
				LastError = string.Empty;
				SetActiveState(false);
				Debug.Log($"External display output stopped on Display {_displayNumber}.", this);
				return true;
			}

			return TryActivateExternalDisplay();
		}

		private bool TryActivateExternalDisplay() {
			var displayIndex = Mathf.Max(2, _displayNumber) - 1;
			if (!CanActivate(out var activationError)) {
				LastError = activationError;
				Debug.LogWarning(LastError, this);
				SetActiveState(false);
				return false;
			}

			var display = Display.displays[displayIndex];
			if (!display.active) {
				var mainWindowWidth = Screen.width;
				var mainWindowHeight = Screen.height;
				var mainWindowMode = Screen.fullScreenMode;
#if UNITY_STANDALONE_WIN
				var mainWindowState = WindowsMainWindowState.Capture();
#endif
				display.Activate();

				if (_preserveMainWindowMode) {
					if (_restoreMainWindowRoutine != null) StopCoroutine(_restoreMainWindowRoutine);
#if UNITY_STANDALONE_WIN
					_restoreMainWindowRoutine = StartCoroutine(RestoreWindowsMainWindow(mainWindowState));
#else
                    _restoreMainWindowRoutine = StartCoroutine(RestoreMainWindowMode(
                        mainWindowWidth,
                        mainWindowHeight,
                        mainWindowMode));
#endif
				}
			}

			var camera = ResolveOutputCamera();
			if (camera == null) {
				LastError = "No Camera is available for external display output.";
				Debug.LogWarning(LastError, this);
				SetActiveState(false);
				return false;
			}

			// A target texture takes precedence over targetDisplay, so direct
			// display output needs the back buffer instead.
			camera.targetTexture = null;
			camera.targetDisplay = displayIndex;
			camera.enabled = true;
			_activeOutputCamera = camera;
			LastError = string.Empty;
			SetActiveState(true);

			Debug.Log(
				$"External display output started on Display {_displayNumber} " +
				$"({display.systemWidth}x{display.systemHeight}).",
				this);
			return true;
		}

		private IEnumerator RestoreMainWindowMode(int width, int height, FullScreenMode mode) {
			// Display activation updates native windows after the current frame.
			// Restore the primary window only after that update has completed.
			yield return null;
			Screen.SetResolution(Mathf.Max(1, width), Mathf.Max(1, height), mode);
			yield return null;
			if (Screen.fullScreenMode != mode) Screen.fullScreenMode = mode;
			_restoreMainWindowRoutine = null;
		}

#if UNITY_STANDALONE_WIN
		private IEnumerator RestoreWindowsMainWindow(WindowsMainWindowState state) {
			// Unity applies native multi-display window changes asynchronously.
			// Restore the original Win32 frame after each of those updates.
			for (var frame = 0; frame < 3; frame++) {
				yield return new WaitForEndOfFrame();
				state.Restore();
			}
			_restoreMainWindowRoutine = null;
		}

		private readonly struct WindowsMainWindowState {
			private const int StyleIndex = -16;
			private const int ExtendedStyleIndex = -20;
			private const uint NoZOrder = 0x0004;
			private const uint NoActivate = 0x0010;
			private const uint FrameChanged = 0x0020;
			private const uint NoOwnerZOrder = 0x0200;

			private readonly IntPtr _window;
			private readonly IntPtr _style;
			private readonly IntPtr _extendedStyle;
			private readonly NativeRect _rect;
			private readonly WindowPlacement _placement;

			private WindowsMainWindowState(
				IntPtr window,
				IntPtr style,
				IntPtr extendedStyle,
				NativeRect rect,
				WindowPlacement placement) {
				_window = window;
				_style = style;
				_extendedStyle = extendedStyle;
				_rect = rect;
				_placement = placement;
			}

			public static WindowsMainWindowState Capture() {
				var window = GetActiveWindow();
				if (window == IntPtr.Zero) window = GetForegroundWindow();

				var rect = default(NativeRect);
				GetWindowRect(window, out rect);
				var placement = new WindowPlacement { Length = Marshal.SizeOf<WindowPlacement>() };
				GetWindowPlacement(window, ref placement);
				return new WindowsMainWindowState(
					window,
					GetWindowLongPtr(window, StyleIndex),
					GetWindowLongPtr(window, ExtendedStyleIndex),
					rect,
					placement);
			}

			public void Restore() {
				if (_window == IntPtr.Zero || !IsWindow(_window)) return;

				SetWindowLongPtr(_window, StyleIndex, _style);
				SetWindowLongPtr(_window, ExtendedStyleIndex, _extendedStyle);
				var placement = _placement;
				placement.Length = Marshal.SizeOf<WindowPlacement>();
				SetWindowPlacement(_window, ref placement);
				SetWindowPos(
					_window,
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
			private static extern IntPtr GetActiveWindow();

			[DllImport("user32.dll")]
			private static extern IntPtr GetForegroundWindow();

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
		}
#endif

		private Camera CurrentOutputCamera() {
			if (_activeOutputCamera != null) return _activeOutputCamera;
			return _runtimeCamera;
		}

		private void ShowStandbyFrame(Camera camera) {
			camera.targetTexture = null;
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = _standbyBackground;
			camera.cullingMask = 0;
			camera.enabled = true;
		}

		private void SetActiveState(bool active) {
			if (IsOutputActive == active) return;
			IsOutputActive = active;
			OutputActiveChanged?.Invoke(active);
		}

		private Camera ResolveOutputCamera() {
			_sourceCamera = _outputCamera;
			if (_sourceCamera == null) _sourceCamera = GetComponent<Camera>();
			if (_sourceCamera == null) _sourceCamera = Camera.main;
			if (_sourceCamera == null && !_createTestCameraWhenMissing) return null;

			if (_runtimeCamera == null) {
				var cameraObject = new GameObject("External Display Output Camera");
				cameraObject.transform.SetParent(transform, false);
				_runtimeCamera = cameraObject.AddComponent<Camera>();
			}

			if (_sourceCamera != null) {
				_runtimeCamera.CopyFrom(_sourceCamera);
				_runtimeCamera.transform.SetPositionAndRotation(
					_sourceCamera.transform.position,
					_sourceCamera.transform.rotation);
			}
			else {
				_runtimeCamera.clearFlags = CameraClearFlags.SolidColor;
				_runtimeCamera.backgroundColor = _testBackground;
				_runtimeCamera.cullingMask = 0;
			}

			_runtimeCamera.enabled = false;
			return _runtimeCamera;
		}
	}
}
