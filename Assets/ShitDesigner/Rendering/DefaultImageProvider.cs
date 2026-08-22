using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace ShitDesigner.Rendering
{
    /// <summary>
    /// Session-scoped, read-only fallback images. A single pooled lease is
    /// shared by all nodes requesting the same descriptor and fallback kind.
    /// </summary>
    public sealed class DefaultImageProvider : IRuntimeDefaultImageProvider, IDisposable
    {
        private sealed class Entry
        {
            public TextureDescriptor Descriptor;
            public TextureLeaseHandle Lease;
            public ResourceOwnerKey Owner;
        }

        private readonly RenderTexturePool _pool;
        private readonly ResourceOwnerKey _owner;
        private readonly RuntimeDynamicRange _dynamicRange;
        private readonly Dictionary<RuntimeDefaultImageKind, Entry> _entries = new Dictionary<RuntimeDefaultImageKind, Entry>();
        private bool _disposed;

        public DefaultImageProvider(RenderTexturePool pool, ResourceOwnerKey owner, RuntimeDynamicRange dynamicRange = RuntimeDynamicRange.Hdr)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            if (!owner.IsValid) throw new ArgumentException("A valid resource owner is required.", nameof(owner));
            if (dynamicRange != RuntimeDynamicRange.Hdr && dynamicRange != RuntimeDynamicRange.Ldr) throw new ArgumentOutOfRangeException(nameof(dynamicRange));
            _owner = owner;
            _dynamicRange = dynamicRange;
        }

        public Result<PortValue> Get(RuntimeDefaultImageKind kind, int width, int height, ulong frameNumber)
        {
            if (_disposed) return Result<PortValue>.Failure(Error("rendering.default_image.disposed", "The default image provider is disposed."));
            TextureDescriptor descriptor;
            try
            {
                descriptor = new TextureDescriptor(width, height, _dynamicRange == RuntimeDynamicRange.Hdr ? GraphicsFormat.R16G16B16A16_SFloat : GraphicsFormat.R8G8B8A8_UNorm,
                    GraphicsFormat.None, 1, false, false, TextureDimension.Tex2D, 1, false);
            }
            catch (Exception exception)
            {
                return Result<PortValue>.Failure(new Diagnostic(new DiagnosticCode("rendering.default_image.descriptor_invalid"), Severity.Error, "The default image descriptor is invalid.", exception: DiagnosticExceptionInfo.FromException(exception)));
            }

            if (!_entries.TryGetValue(kind, out var entry) || entry.Descriptor != descriptor || entry.Lease == null || entry.Lease.IsReleased)
            {
                var owner = OwnerFor(kind);
                var acquired = _pool.Acquire(descriptor, owner, frameNumber);
                if (acquired.IsFailure) return Result<PortValue>.Failure(acquired.Diagnostic);
                var candidate = new Entry { Descriptor = descriptor, Lease = acquired.Value, Owner = owner };
                try { Clear(candidate.Lease.Texture, kind); }
                catch (Exception exception)
                {
                    candidate.Lease.Release(owner, frameNumber);
                    return Result<PortValue>.Failure(new Diagnostic(new DiagnosticCode("rendering.default_image.clear_failed"), Severity.Error, "The default image could not be initialized.", exception: DiagnosticExceptionInfo.FromException(exception)));
                }
                if (entry?.Lease != null && !entry.Lease.IsReleased) entry.Lease.Release(entry.Owner, frameNumber);
                entry = candidate;
                _entries[kind] = entry;
            }

            var borrowed = entry.Lease.Borrow(frameNumber);
            if (borrowed.IsFailure) return Result<PortValue>.Failure(borrowed.Diagnostic);
            return Result<PortValue>.Success(PortValue.FromImageFrame(borrowed.Value.Frame));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var entry in _entries.Values)
                if (entry.Lease != null && !entry.Lease.IsReleased) entry.Lease.Release(entry.Owner, _pool.CurrentFrame);
            _entries.Clear();
        }

        private static void Clear(RenderTexture texture, RuntimeDefaultImageKind kind)
        {
            var color = kind == RuntimeDefaultImageKind.OpaqueWhite ? Color.white : kind == RuntimeDefaultImageKind.OpaqueBlack ? Color.black : Color.clear;
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture;
                GL.Clear(true, true, color);
            }
            finally { RenderTexture.active = previous; }
        }

        private static Diagnostic Error(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "rendering");

        private ResourceOwnerKey OwnerFor(RuntimeDefaultImageKind kind) => new ResourceOwnerKey(
            _owner.SessionId, ResourceOwnerKind.DefaultImageProvider, _owner.OwnerId,
            _owner.GenerationId, _owner.SlotId + "." + kind, LeaseRole.DefaultImage);
    }
}
