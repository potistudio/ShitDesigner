using System;
using UnityEngine;

namespace ShitDesigner.Rendering
{
    /// <summary>GPU-only terminal transform for Preview Fit/Fill/Stretch.</summary>
    public sealed class PreviewDisplayPass : IDisposable
    {
        private readonly Material _material;
        private bool _disposed;

        public PreviewDisplayPass()
        {
            var shader = Shader.Find("Hidden/ShitDesigner/PreviewDisplay");
            if (shader == null) throw new InvalidOperationException("Preview display shader is not available.");
            _material = new Material(shader) { name = "ShitDesigner.PreviewDisplay" };
        }

        public void Blit(RenderTexture source, RenderTexture destination, PreviewDisplayMode mode = PreviewDisplayMode.Fit)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(PreviewDisplayPass));
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            if (!source.IsCreated() || !destination.IsCreated()) throw new ArgumentException("Preview display requires created textures.");
            source.filterMode = FilterMode.Bilinear;
            // A Preview must not wrap its terminal sample into the opposite
            // edge when Fit/Fill reaches a border; wrapping would blend the
            // image with unrelated texels and produce a half-alpha seam.
            source.wrapMode = TextureWrapMode.Clamp;
            _material.SetVector("_SourceSize", new Vector4(source.width, source.height, 0f, 0f));
            _material.SetVector("_DestinationSize", new Vector4(destination.width, destination.height, 0f, 0f));
            _material.SetFloat("_DisplayMode", (float)mode);
            // PreviewDisplay is a geometry-only terminal pass.  Its source
            // and destination are internal Linear surfaces; leave the
            // display transfer to DisplayTransform and prevent a stale
            // global sRGBWrite flag from changing edge/bilinear samples.
            var previousSrgbWrite = GL.sRGBWrite;
            GL.sRGBWrite = false;
            try
            {
                Graphics.Blit(source, destination, _material);
            }
            finally
            {
                GL.sRGBWrite = previousSrgbWrite;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_material);
            else UnityEngine.Object.DestroyImmediate(_material);
        }
    }
}
