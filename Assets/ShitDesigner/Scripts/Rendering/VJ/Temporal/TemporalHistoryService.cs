using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace ShitDesigner.Rendering.VJ.Temporal
{
    /// <summary>Immutable allocation key for a temporal history chain.</summary>
    public readonly struct TemporalHistoryDescriptor : IEquatable<TemporalHistoryDescriptor>
    {
        public int Width { get; }
        public int Height { get; }
        public GraphicsFormat ColorFormat { get; }

        public TemporalHistoryDescriptor(int width, int height, GraphicsFormat colorFormat)
        {
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));
            if (colorFormat == GraphicsFormat.None) throw new ArgumentException("A concrete history graphics format is required.", nameof(colorFormat));
            Width = width;
            Height = height;
            ColorFormat = colorFormat;
        }

        public bool Equals(TemporalHistoryDescriptor other) => Width == other.Width && Height == other.Height && ColorFormat == other.ColorFormat;
        public override bool Equals(object obj) => obj is TemporalHistoryDescriptor && Equals((TemporalHistoryDescriptor)obj);
        public override int GetHashCode() => (Width * 397) ^ (Height * 17) ^ (int)ColorFormat;
        public static bool operator ==(TemporalHistoryDescriptor left, TemporalHistoryDescriptor right) => left.Equals(right);
        public static bool operator !=(TemporalHistoryDescriptor left, TemporalHistoryDescriptor right) => !left.Equals(right);
        public override string ToString() => Width + "x" + Height + " " + ColorFormat;
    }

    public readonly struct TemporalHistorySnapshot
    {
        private readonly IReadOnlyList<RenderTexture> _historyTextures;
        public string Key { get; }
        public TemporalHistoryDescriptor Descriptor { get; }
        public int HistorySlotCount { get; }
        public int ReadSlot { get; }
        public int WriteSlot { get; }
        public ulong LastFrame { get; }
        public double GraphTime { get; }
        public ulong Generation { get; }
        public int WarmupRemaining { get; }
        public bool IsPaused { get; }
        public bool IsValid { get; }
        /// <summary>Service-user leases, not the pool's slot leases.</summary>
        public int ActiveLeaseCount { get; }
        public RenderTexture ReadTexture { get; }
        public RenderTexture WriteTexture { get; }
        /// <summary>Textures in newest-to-oldest order.</summary>
        public IReadOnlyList<RenderTexture> HistoryTextures => _historyTextures;

        internal TemporalHistorySnapshot(string key, TemporalHistoryDescriptor descriptor, int historySlotCount, int readSlot, int writeSlot,
            ulong lastFrame, double graphTime, ulong generation, int warmupRemaining, bool isPaused, bool isValid, int activeLeaseCount,
            IReadOnlyList<RenderTexture> historyTextures, RenderTexture readTexture, RenderTexture writeTexture)
        {
            Key = key;
            Descriptor = descriptor;
            HistorySlotCount = historySlotCount;
            ReadSlot = readSlot;
            WriteSlot = writeSlot;
            LastFrame = lastFrame;
            GraphTime = graphTime;
            Generation = generation;
            WarmupRemaining = warmupRemaining;
            IsPaused = isPaused;
            IsValid = isValid;
            ActiveLeaseCount = activeLeaseCount;
            _historyTextures = historyTextures ?? new ReadOnlyCollection<RenderTexture>(new List<RenderTexture>());
            ReadTexture = readTexture;
            WriteTexture = writeTexture;
        }
    }

    /// <summary>A generation-bound service-user lease. Pool slot ownership is
    /// held by TemporalHistoryService and is released at reset/resize or
    /// service disposal.</summary>
    public sealed class TemporalHistoryLease : IDisposable
    {
        private TemporalHistoryService _owner;
        private readonly string _key;
        private readonly ulong _generation;

        internal TemporalHistoryLease(TemporalHistoryService owner, string key, ulong generation)
        {
            _owner = owner;
            _key = key;
            _generation = generation;
        }

        public string Key => _key;
        public ulong Generation => _generation;
        public bool IsReleased => _owner == null;

        public void Dispose()
        {
            var owner = _owner;
            _owner = null;
            owner?.ReleaseLease(_key, _generation);
        }
    }

    /// <summary>
    /// Pool-backed temporal history ring. BeginFrame selects a write slot,
    /// consumers bind the newest-to-oldest texture view, and Commit publishes
    /// the rendered frame only after the pass graph succeeds. The parameterless
    /// constructor remains a graphics-free lifecycle model for EditMode tests.
    /// </summary>
    public sealed class TemporalHistoryService : IDisposable
    {
        private sealed class Entry
        {
            public string Key;
            public TemporalHistoryDescriptor Descriptor;
            public int SlotCount;
            public int ReadSlot;
            public int WriteSlot;
            public ulong LastFrame;
            public double GraphTime;
            public ulong Generation;
            public int WarmupRemaining;
            public int WarmupFrames;
            public bool IsPaused;
            public bool IsValid;
            public int ActiveLeaseCount;
            public bool FrameBegun;
            public List<TextureLeaseHandle> TextureLeases = new List<TextureLeaseHandle>();
        }

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
        private readonly RenderTexturePool _pool;
        private readonly ResourceOwnerKey _owner;
        private ulong _nextGeneration = 1;
        private bool _disposed;

        public TemporalHistoryService() { }

        public TemporalHistoryService(RenderTexturePool pool, ResourceOwnerKey owner)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            if (!owner.IsValid) throw new ArgumentException("A valid history owner is required.", nameof(owner));
            _owner = owner;
        }

        public int HistoryCount => _entries.Count;
        public int ActiveLeaseCount
        {
            get
            {
                var total = 0;
                foreach (var entry in _entries.Values) total += entry.ActiveLeaseCount;
                return total;
            }
        }
        public int PoolLeaseCount
        {
            get
            {
                var total = 0;
                foreach (var entry in _entries.Values) total += entry.TextureLeases.Count(x => x != null && !x.IsReleased);
                return total;
            }
        }

        public bool Ensure(string key, TemporalHistoryDescriptor descriptor, int historySlotCount = 2, int warmupFrames = 1)
            => Ensure(key, descriptor, historySlotCount, warmupFrames, 1UL);

        public bool Ensure(string key, TemporalHistoryDescriptor descriptor, int historySlotCount, int warmupFrames, ulong frameNumber)
        {
            ThrowIfDisposed();
            ValidateKey(key);
            ValidateSlotCount(historySlotCount);
            if (warmupFrames < 0) throw new ArgumentOutOfRangeException(nameof(warmupFrames));
            if (frameNumber == 0) frameNumber = 1;
            if (_entries.TryGetValue(key, out var existing))
            {
                if (existing.Descriptor == descriptor && existing.SlotCount == historySlotCount)
                {
                    existing.WarmupFrames = warmupFrames;
                    return true;
                }
                return ResizeInternal(existing, descriptor, historySlotCount, warmupFrames, frameNumber);
            }

            var created = new Entry
            {
                Key = key,
                Descriptor = descriptor,
                SlotCount = historySlotCount,
                ReadSlot = 0,
                WriteSlot = 1 % historySlotCount,
                LastFrame = 0,
                GraphTime = 0d,
                Generation = NextGeneration(),
                WarmupRemaining = warmupFrames,
                WarmupFrames = warmupFrames,
                IsPaused = false,
                IsValid = false,
                ActiveLeaseCount = 0,
                FrameBegun = false
            };
            if (!TryAllocateTextures(created, frameNumber)) return false;
            _entries.Add(key, created);
            return true;
        }

        public bool BeginFrame(string key, ulong frame, double graphTime, bool paused)
        {
            ThrowIfDisposed();
            if (!TryGetEntry(key, out var entry) || !IsFinite(graphTime)) return false;
            entry.IsPaused = paused;
            if (paused)
            {
                // A paused call is observational: frame/time and slot state
                // remain exactly as they were before the pause.
                entry.FrameBegun = false;
                return true;
            }
            if (entry.FrameBegun && frame < entry.LastFrame) return false;
            entry.LastFrame = frame;
            entry.GraphTime = graphTime;
            entry.FrameBegun = true;
            return true;
        }

        public bool Commit(string key, ulong frame) => Commit(key, frame, null);

        public bool Commit(string key, ulong frame, RenderTexture renderedTexture)
        {
            ThrowIfDisposed();
            if (!TryGetEntry(key, out var entry)) return false;
            if (entry.IsPaused || !entry.FrameBegun) return true;
            if (frame < entry.LastFrame) return false;
            if (renderedTexture != null && entry.TextureLeases.Count > 0)
            {
                var writeTexture = entry.TextureLeases[entry.WriteSlot]?.Texture;
                if (writeTexture == null || renderedTexture == writeTexture) return false;
                try { Graphics.Blit(renderedTexture, writeTexture); }
                catch { return false; }
            }
            var oldRead = entry.ReadSlot;
            entry.ReadSlot = entry.WriteSlot;
            entry.WriteSlot = oldRead;
            entry.LastFrame = frame;
            entry.IsValid = true;
            if (entry.WarmupRemaining > 0) entry.WarmupRemaining--;
            entry.FrameBegun = false;
            return true;
        }

        public bool Reset(string key, ulong frame = 0UL)
        {
            ThrowIfDisposed();
            if (!TryGetEntry(key, out var entry)) return false;
            try
            {
                foreach (var lease in entry.TextureLeases)
                    if (lease != null && !lease.IsReleased) ClearTransparent(lease.Texture);
            }
            catch { return false; }
            entry.ReadSlot = 0;
            entry.WriteSlot = 1 % entry.SlotCount;
            entry.LastFrame = frame;
            entry.GraphTime = 0d;
            entry.WarmupRemaining = entry.WarmupFrames;
            entry.IsPaused = false;
            entry.IsValid = false;
            entry.FrameBegun = false;
            entry.Generation = NextGeneration();
            return true;
        }

        public bool Resize(string key, TemporalHistoryDescriptor descriptor, int historySlotCount = 2, ulong frame = 0UL)
        {
            ThrowIfDisposed();
            ValidateSlotCount(historySlotCount);
            if (!TryGetEntry(key, out var entry)) return false;
            if (frame == 0) frame = 1;
            return ResizeInternal(entry, descriptor, historySlotCount, entry.WarmupFrames, frame);
        }

        public bool TryAcquire(string key, out TemporalHistoryLease lease)
        {
            ThrowIfDisposed();
            lease = null;
            if (!TryGetEntry(key, out var entry)) return false;
            entry.ActiveLeaseCount++;
            lease = new TemporalHistoryLease(this, key, entry.Generation);
            return true;
        }

        public bool TryGetSnapshot(string key, out TemporalHistorySnapshot snapshot)
        {
            ThrowIfDisposed();
            if (!TryGetEntry(key, out var entry))
            {
                snapshot = default(TemporalHistorySnapshot);
                return false;
            }
            snapshot = Snapshot(entry);
            return true;
        }

        /// <summary>Returns offset 0 as HistoryTex, offset 1 as HistoryTex2,
        /// and so on, relative to the newest committed slot.</summary>
        public bool TryGetTexture(string key, int historyOffset, out RenderTexture texture)
        {
            ThrowIfDisposed();
            texture = null;
            if (!TryGetEntry(key, out var entry) || historyOffset < 0 || historyOffset >= entry.SlotCount) return false;
            var slot = (entry.ReadSlot - historyOffset) % entry.SlotCount;
            if (slot < 0) slot += entry.SlotCount;
            texture = entry.TextureLeases.Count == 0 ? null : entry.TextureLeases[slot]?.Texture;
            return texture != null;
        }

        /// <summary>Removes a history chain once all service-user leases have
        /// been released. Pool leases are returned before the key disappears.</summary>
        public bool Release(string key)
        {
            ThrowIfDisposed();
            if (!_entries.TryGetValue(key, out var entry)) return false;
            if (entry.ActiveLeaseCount > 0) return false;
            ReleaseTextures(entry, _pool == null ? 1UL : Math.Max(1UL, _pool.CurrentFrame));
            _entries.Remove(key);
            return true;
        }

        internal void ReleaseLease(string key, ulong generation)
        {
            if (_disposed || !_entries.TryGetValue(key, out var entry)) return;
            if (entry.ActiveLeaseCount > 0) entry.ActiveLeaseCount--;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            var frame = _pool == null ? 1UL : Math.Max(1UL, _pool.CurrentFrame);
            foreach (var entry in _entries.Values) ReleaseTextures(entry, frame);
            _entries.Clear();
        }

        private bool ResizeInternal(Entry entry, TemporalHistoryDescriptor descriptor, int historySlotCount, int warmupFrames, ulong frame)
        {
            var replacement = new Entry
            {
                Key = entry.Key,
                Descriptor = descriptor,
                SlotCount = historySlotCount,
                ReadSlot = 0,
                WriteSlot = 1 % historySlotCount,
                LastFrame = frame,
                GraphTime = 0d,
                Generation = NextGeneration(),
                WarmupRemaining = warmupFrames,
                WarmupFrames = warmupFrames,
                IsPaused = false,
                IsValid = false,
            };
            if (!TryAllocateTextures(replacement, frame)) return false;
            ReleaseTextures(entry, frame);
            entry.Descriptor = replacement.Descriptor;
            entry.SlotCount = replacement.SlotCount;
            entry.ReadSlot = replacement.ReadSlot;
            entry.WriteSlot = replacement.WriteSlot;
            entry.LastFrame = replacement.LastFrame;
            entry.GraphTime = replacement.GraphTime;
            entry.Generation = replacement.Generation;
            entry.WarmupRemaining = replacement.WarmupRemaining;
            entry.WarmupFrames = replacement.WarmupFrames;
            entry.IsPaused = replacement.IsPaused;
            entry.IsValid = replacement.IsValid;
            entry.FrameBegun = false;
            entry.TextureLeases = replacement.TextureLeases;
            return true;
        }

        private bool TryAllocateTextures(Entry entry, ulong frame)
        {
            if (_pool == null) return true;
            var acquired = new List<TextureLeaseHandle>(entry.SlotCount);
            var descriptor = new TextureDescriptor(entry.Descriptor.Width, entry.Descriptor.Height, entry.Descriptor.ColorFormat);
            for (var index = 0; index < entry.SlotCount; index++)
            {
                var result = _pool.Acquire(descriptor, OwnerFor(entry.Key, index), Math.Max(1UL, frame));
                if (result.IsFailure)
                {
                    ReleaseAcquired(entry.Key, acquired, frame);
                    return false;
                }
                acquired.Add(result.Value);
                try { ClearTransparent(result.Value.Texture); }
                catch
                {
                    ReleaseAcquired(entry.Key, acquired, frame);
                    return false;
                }
            }
            entry.TextureLeases = acquired;
            return true;
        }

        private void ReleaseAcquired(string key, List<TextureLeaseHandle> acquired, ulong frame)
        {
            for (var index = 0; index < acquired.Count; index++)
            {
                var lease = acquired[index];
                if (lease == null || lease.IsReleased) continue;
                lease.Release(OwnerFor(key, index), Math.Max(1UL, frame));
            }
        }

        private void ReleaseTextures(Entry entry, ulong frame)
        {
            for (var index = 0; index < entry.TextureLeases.Count; index++)
            {
                var lease = entry.TextureLeases[index];
                if (lease == null || lease.IsReleased) continue;
                lease.Release(OwnerFor(entry.Key, index), Math.Max(1UL, frame));
            }
            entry.TextureLeases.Clear();
        }

        private ResourceOwnerKey OwnerFor(string key, int index)
        {
            if (_pool == null) return default(ResourceOwnerKey);
            return new ResourceOwnerKey(_owner.SessionId, ResourceOwnerKind.RuntimeNode, _owner.OwnerId,
                _owner.GenerationId, key + ".history." + index, LeaseRole.Output);
        }

        private ulong NextGeneration() => _nextGeneration++;

        private bool TryGetEntry(string key, out Entry entry)
        {
            ValidateKey(key);
            return _entries.TryGetValue(key, out entry);
        }

        private TemporalHistorySnapshot Snapshot(Entry entry)
        {
            var textures = new List<RenderTexture>(entry.SlotCount);
            for (var offset = 0; offset < entry.SlotCount; offset++)
            {
                var slot = (entry.ReadSlot - offset) % entry.SlotCount;
                if (slot < 0) slot += entry.SlotCount;
                textures.Add(entry.TextureLeases.Count == 0 ? null : entry.TextureLeases[slot]?.Texture);
            }
            var read = textures.Count == 0 ? null : textures[0];
            var write = entry.TextureLeases.Count == 0 ? null : entry.TextureLeases[entry.WriteSlot]?.Texture;
            return new TemporalHistorySnapshot(entry.Key, entry.Descriptor, entry.SlotCount,
                entry.ReadSlot, entry.WriteSlot, entry.LastFrame, entry.GraphTime, entry.Generation,
                entry.WarmupRemaining, entry.IsPaused, entry.IsValid, entry.ActiveLeaseCount,
                new ReadOnlyCollection<RenderTexture>(textures), read, write);
        }

        private static void ClearTransparent(RenderTexture texture)
        {
            if (texture == null) return;
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture;
                GL.Clear(true, true, Color.clear);
            }
            finally { RenderTexture.active = previous; }
        }

        private static void ValidateSlotCount(int historySlotCount)
        {
            if (historySlotCount < 2) throw new ArgumentOutOfRangeException(nameof(historySlotCount), "A temporal chain needs at least two slots.");
        }

        private static void ValidateKey(string key)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentException("A non-empty history key is required.", nameof(key));
        }

        private void ThrowIfDisposed()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(TemporalHistoryService));
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
