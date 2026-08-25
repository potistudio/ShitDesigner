using System;
#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif
using UnityEngine;

namespace ShitDesigner.Bootstrap {
	/// <summary>
	/// Client-area dimensions exposed by the platform window
	/// boundary. Keeping this value independent of Unity's Screen object lets
	/// the same minimum-size contract be tested by the Player harness.
	/// </summary>
	public readonly struct WindowSize : IEquatable<WindowSize> {
		public int Width { get; }
		public int Height { get; }

		public WindowSize(int width, int height) {
			Width = Math.Max(0, width);
			Height = Math.Max(0, height);
		}

		public bool Equals(WindowSize other) => Width == other.Width && Height == other.Height;
		public override bool Equals(object obj) => obj is WindowSize other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(Width, Height);
		public static bool operator ==(WindowSize left, WindowSize right) => left.Equals(right);
		public static bool operator !=(WindowSize left, WindowSize right) => !left.Equals(right);
		public override string ToString() => Width + "x" + Height;
	}

	public static class WindowConstraints {
		public const int InitialWidth = 1600;
		public const int InitialHeight = 900;
		public const int MinimumWidth = 1280;
		public const int MinimumHeight = 720;

		public static WindowSize Clamp(WindowSize current)
			=> new WindowSize(Math.Max(MinimumWidth, current.Width), Math.Max(MinimumHeight, current.Height));

		public static bool NeedsClamp(WindowSize current) => Clamp(current) != current;
	}

	/// <summary>
	/// Small platform seam for enforcing the main application
	/// window contract.
	/// </summary>
	public interface IWindowAdapter {
		bool IsSupported { get; }
		bool IsFullscreen { get; }
		WindowSize CurrentSize { get; }
		void SetWindowedSize(WindowSize size);
		void MaintainWindowFrame();
	}

	public sealed class WindowAdapter : IWindowAdapter {
#if UNITY_STANDALONE_WIN
		private const int StyleIndex = -16;
		private const int ExtendedStyleIndex = -20;
		private const uint NoSize = 0x0001;
		private const uint NoMove = 0x0002;
		private const uint NoZOrder = 0x0004;
		private const uint NoActivate = 0x0010;
		private const uint FrameChanged = 0x0020;
		private IntPtr _window;
		private IntPtr _windowStyle;
		private IntPtr _windowExtendedStyle;
		private bool _hasCapturedWindowFrame;
#endif

		public bool IsSupported => UnityEngine.Application.platform == RuntimePlatform.WindowsPlayer ||
									UnityEngine.Application.platform == RuntimePlatform.OSXPlayer;
		public bool IsFullscreen => Screen.fullScreen;
		public WindowSize CurrentSize {
			get {
#if UNITY_STANDALONE_WIN
				if (UnityEngine.Application.platform == RuntimePlatform.WindowsPlayer && TryGetWindowsClientSize(out var size)) return size;
#endif
				return new WindowSize(Screen.width, Screen.height);
			}
		}

		public void SetWindowedSize(WindowSize size) {
			if (!IsSupported || IsFullscreen) return;
			var clamped = WindowConstraints.Clamp(size);
#if UNITY_STANDALONE_WIN
			if (UnityEngine.Application.platform == RuntimePlatform.WindowsPlayer) {
				TrySetWindowsClientSize(clamped);
				return;
			}
#endif
			Screen.SetResolution(clamped.Width, clamped.Height, FullScreenMode.Windowed);
		}

		public void MaintainWindowFrame() {
#if UNITY_STANDALONE_WIN
			if (UnityEngine.Application.platform != RuntimePlatform.WindowsPlayer || IsFullscreen || !TryGetWindow(out var window)) return;
			if (GetWindowLongPtr(window, StyleIndex) == _windowStyle &&
				GetWindowLongPtr(window, ExtendedStyleIndex) == _windowExtendedStyle) return;
			// Multi-display updates can replace the controller's native frame
			// bits after a user resize; retain the frame captured at startup.
			SetWindowLongPtr(window, StyleIndex, _windowStyle);
			SetWindowLongPtr(window, ExtendedStyleIndex, _windowExtendedStyle);
			SetWindowPos(window, IntPtr.Zero, 0, 0, 0, 0, NoSize | NoMove | NoZOrder | NoActivate | FrameChanged);
#endif
		}

#if UNITY_STANDALONE_WIN
		private bool TryGetWindowsClientSize(out WindowSize size) {
			size = default;
			if (!TryGetWindow(out var window) || !GetClientRect(window, out var client)) return false;
			size = new WindowSize(client.Right - client.Left, client.Bottom - client.Top);
			return true;
		}

		private bool TrySetWindowsClientSize(WindowSize size) {
			if (!TryGetWindow(out var window) ||
				!GetClientRect(window, out var client) ||
				!GetWindowRect(window, out var frame)) return false;
			var frameWidth = Math.Max(0, frame.Right - frame.Left - (client.Right - client.Left));
			var frameHeight = Math.Max(0, frame.Bottom - frame.Top - (client.Bottom - client.Top));
			return SetWindowPos(
				window,
				IntPtr.Zero,
				0,
				0,
				Math.Max(1, size.Width + frameWidth),
				Math.Max(1, size.Height + frameHeight),
				NoMove | NoZOrder | NoActivate);
		}

		private bool TryGetWindow(out IntPtr window) {
			if (_window != IntPtr.Zero && IsWindow(_window)) {
				window = _window;
				return true;
			}

			window = GetActiveWindow();
			if (!BelongsToCurrentProcess(window)) window = GetForegroundWindow();
			if (!BelongsToCurrentProcess(window)) {
				window = IntPtr.Zero;
				return false;
			}
			_window = window;
			if (!_hasCapturedWindowFrame) {
				_windowStyle = GetWindowLongPtr(window, StyleIndex);
				_windowExtendedStyle = GetWindowLongPtr(window, ExtendedStyleIndex);
				_hasCapturedWindowFrame = true;
			}
			return true;
		}

		private static bool BelongsToCurrentProcess(IntPtr window) {
			if (window == IntPtr.Zero) return false;
			GetWindowThreadProcessId(window, out var processId);
			return processId == GetCurrentProcessId();
		}

		[StructLayout(LayoutKind.Sequential)]
		private struct NativeRect {
			public int Left;
			public int Top;
			public int Right;
			public int Bottom;
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
		private static extern bool GetClientRect(IntPtr window, out NativeRect rect);

		[DllImport("user32.dll")]
		[return: MarshalAs(UnmanagedType.Bool)]
		private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

		[DllImport("user32.dll")]
		private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

		[DllImport("kernel32.dll")]
		private static extern uint GetCurrentProcessId();

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
#endif
	}
}
