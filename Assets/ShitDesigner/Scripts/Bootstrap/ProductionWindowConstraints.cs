using System;
using UnityEngine;

namespace ShitDesigner.Bootstrap {
	/// <summary>Client-area dimensions exposed by the platform window
	/// boundary. Keeping this value independent of Unity's Screen object lets
	/// the same minimum-size contract be tested by the Player harness.</summary>
	public readonly struct ProductionWindowSize : IEquatable<ProductionWindowSize> {
		public int Width { get; }
		public int Height { get; }

		public ProductionWindowSize(int width, int height) {
			Width = Math.Max(0, width);
			Height = Math.Max(0, height);
		}

		public bool Equals(ProductionWindowSize other) => Width == other.Width && Height == other.Height;
		public override bool Equals(object obj) => obj is ProductionWindowSize other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(Width, Height);
		public static bool operator ==(ProductionWindowSize left, ProductionWindowSize right) => left.Equals(right);
		public static bool operator !=(ProductionWindowSize left, ProductionWindowSize right) => !left.Equals(right);
		public override string ToString() => Width + "x" + Height;
	}

	public static class ProductionWindowConstraints {
		public const int InitialWidth = 1600;
		public const int InitialHeight = 900;
		public const int MinimumWidth = 1280;
		public const int MinimumHeight = 720;

		public static ProductionWindowSize Clamp(ProductionWindowSize current)
			=> new ProductionWindowSize(Math.Max(MinimumWidth, current.Width), Math.Max(MinimumHeight, current.Height));

		public static bool NeedsClamp(ProductionWindowSize current) => Clamp(current) != current;
	}

	/// <summary>Small platform seam for enforcing the main application
	/// window contract. The Unity adapter is the production implementation;
	/// tests and standalone harnesses can use a recording adapter.</summary>
	public interface IProductionWindowAdapter {
		bool IsSupported { get; }
		bool IsWindowed { get; }
		ProductionWindowSize CurrentSize { get; }
		void SetWindowedSize(ProductionWindowSize size);
	}

	public sealed class UnityProductionWindowAdapter : IProductionWindowAdapter {
		public bool IsSupported => UnityEngine.Application.platform == RuntimePlatform.WindowsPlayer ||
									UnityEngine.Application.platform == RuntimePlatform.OSXPlayer;
		public bool IsWindowed => !Screen.fullScreen;
		public ProductionWindowSize CurrentSize => new ProductionWindowSize(Screen.width, Screen.height);

		public void SetWindowedSize(ProductionWindowSize size) {
			if (!IsSupported || !IsWindowed) return;
			var clamped = ProductionWindowConstraints.Clamp(size);
			Screen.SetResolution(clamped.Width, clamped.Height, FullScreenMode.Windowed);
		}
	}
}
