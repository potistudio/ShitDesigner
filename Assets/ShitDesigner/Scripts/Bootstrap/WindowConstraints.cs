using System;
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
	}

	public sealed class WindowAdapter : IWindowAdapter {
		public bool IsSupported => UnityEngine.Application.platform == RuntimePlatform.WindowsPlayer ||
									UnityEngine.Application.platform == RuntimePlatform.OSXPlayer;
		public bool IsFullscreen => Screen.fullScreen;
		public WindowSize CurrentSize => new WindowSize(Screen.width, Screen.height);

		public void SetWindowedSize(WindowSize size) {
			if (!IsSupported) return;
			var clamped = WindowConstraints.Clamp(size);
			Screen.SetResolution(clamped.Width, clamped.Height, FullScreenMode.Windowed);
		}
	}
}
