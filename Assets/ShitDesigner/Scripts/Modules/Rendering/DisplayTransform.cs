using System;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace ShitDesigner.Rendering {
	public enum DisplayTransformMode {
		Ldr,
		HdrAces
	}

	/// <summary>Small GPU boundary for the shared Program/Preview display path.</summary>
	public sealed class DisplayTransformPass : IDisposable {
		private readonly Material _material;
		private bool _disposed;

		public DisplayTransformPass(Shader shader) {
			if (shader == null) throw new InvalidOperationException("Display transform shader is not available.");
			_material = new Material(shader) { name = "ShitDesigner.DisplayTransform" };
		}

		public void Blit(RenderTexture source, RenderTexture destination, DisplayTransformMode mode)
			=> Blit(source, destination, mode, false, false);

		public void Blit(RenderTexture source, RenderTexture destination, DisplayTransformMode mode, bool sourceIsSrgb, bool premultiplySource) {
			if (_disposed) throw new ObjectDisposedException(nameof(DisplayTransformPass));
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (destination == null) throw new ArgumentNullException(nameof(destination));
			if (!source.IsCreated() || !destination.IsCreated()) throw new ArgumentException("Display transform requires created textures.");
			source.filterMode = FilterMode.Bilinear;
			source.wrapMode = TextureWrapMode.Clamp;
			_material.SetFloat("_Mode", mode == DisplayTransformMode.HdrAces ? 1f : 0f);
			_material.SetFloat("_SourceSrgb", sourceIsSrgb ? 1f : 0f);
			_material.SetFloat("_Premultiply", premultiplySource ? 1f : 0f);
			// The shader owns the Linear -> sRGB transfer at this terminal
			// boundary.  Unity's global sRGB write state is mutable and can
			// be left enabled by an earlier camera or blit; allowing it to
			// run here would apply a second transfer (and can clamp HDR
			// setup values before ACES on D3D12).  Isolate this pass and
			// restore the caller's state afterward.
			var previousSrgbWrite = GL.sRGBWrite;
			GL.sRGBWrite = false;
			try {
				Graphics.Blit(source, destination, _material);
			}
			finally {
				GL.sRGBWrite = previousSrgbWrite;
			}
		}

		public static bool IsSupported(GraphicsFormat format) {
			return SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Render) && SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.Sample);
		}

		public static Color Premultiply(Color straight) {
			var alpha = Mathf.Clamp01(straight.a);
			return new Color(straight.r * alpha, straight.g * alpha, straight.b * alpha, alpha);
		}

		public static Color CompositeOpaqueBlack(Color premultiplied) {
			return new Color(Mathf.Max(0f, premultiplied.r), Mathf.Max(0f, premultiplied.g), Mathf.Max(0f, premultiplied.b), 1f);
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_material);
			else UnityEngine.Object.DestroyImmediate(_material);
		}
	}
}
