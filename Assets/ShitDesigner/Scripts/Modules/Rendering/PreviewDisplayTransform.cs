using System;
using UnityEngine;

namespace ShitDesigner.Rendering {
	public enum PreviewDisplayMode {
		Fit,
		Fill,
		Stretch
	}

	public readonly struct PreviewDisplayGeometry {
		public RectInt DestinationRect { get; }
		public Rect SourceRect { get; }
		public Color PaddingColor { get; }
		public bool HasTransparentPadding => DestinationRect.width < DestinationSize.x || DestinationRect.height < DestinationSize.y;
		public Vector2Int SourceSize { get; }
		public Vector2Int DestinationSize { get; }

		internal PreviewDisplayGeometry(RectInt destinationRect, Rect sourceRect, Vector2Int sourceSize, Vector2Int destinationSize) {
			DestinationRect = destinationRect;
			SourceRect = sourceRect;
			SourceSize = sourceSize;
			DestinationSize = destinationSize;
			PaddingColor = Color.clear;
		}
	}

	public static class PreviewDisplayTransform {
		public static PreviewDisplayGeometry Calculate(Vector2Int sourceSize, Vector2Int destinationSize, PreviewDisplayMode mode = PreviewDisplayMode.Fit) {
			if (sourceSize.x < 1 || sourceSize.y < 1) throw new ArgumentOutOfRangeException(nameof(sourceSize));
			if (destinationSize.x < 1 || destinationSize.y < 1) throw new ArgumentOutOfRangeException(nameof(destinationSize));
			if (mode == PreviewDisplayMode.Stretch)
				return new PreviewDisplayGeometry(new RectInt(0, 0, destinationSize.x, destinationSize.y), new Rect(0, 0, 1, 1), sourceSize, destinationSize);

			var sourceAspect = sourceSize.x / (double)sourceSize.y;
			var destinationAspect = destinationSize.x / (double)destinationSize.y;
			if (mode == PreviewDisplayMode.Fit) {
				var width = destinationAspect >= sourceAspect ? (int)Math.Round(destinationSize.y * sourceAspect) : destinationSize.x;
				var height = destinationAspect >= sourceAspect ? destinationSize.y : (int)Math.Round(destinationSize.x / sourceAspect);
				width = Math.Max(1, Math.Min(destinationSize.x, width));
				height = Math.Max(1, Math.Min(destinationSize.y, height));
				return new PreviewDisplayGeometry(new RectInt((destinationSize.x - width) / 2, (destinationSize.y - height) / 2, width, height), new Rect(0, 0, 1, 1), sourceSize, destinationSize);
			}

			// Fill keeps the source aspect and crops symmetrically at center.
			var cropWidth = destinationAspect >= sourceAspect ? 1d : destinationAspect / sourceAspect;
			var cropHeight = destinationAspect >= sourceAspect ? sourceAspect / destinationAspect : 1d;
			return new PreviewDisplayGeometry(new RectInt(0, 0, destinationSize.x, destinationSize.y),
				new Rect((float)((1d - cropWidth) / 2d), (float)((1d - cropHeight) / 2d), (float)cropWidth, (float)cropHeight), sourceSize, destinationSize);
		}

		public static PreviewDisplayGeometry Resolve(Vector2Int sourceSize, Vector2Int destinationSize, PreviewDisplayMode mode = PreviewDisplayMode.Fit) => Calculate(sourceSize, destinationSize, mode);
	}
}
