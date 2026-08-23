using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace ShitDesigner.Rendering
{
    public enum TextureLeaseState
    {
        Free,
        Leased
    }

    public readonly struct OwnershipSnapshotEntry
    {
        public OutputLeaseId LeaseId { get; }
        public TextureDescriptor Descriptor { get; }
        public long EstimatedBytes { get; }
        public TextureLeaseState State { get; }
        public ResourceOwnerKey Owner { get; }
        public ulong LastUsedFrame { get; }
        public ulong LastReturnedFrame { get; }

        internal OwnershipSnapshotEntry(OutputLeaseId leaseId, TextureDescriptor descriptor, long estimatedBytes, TextureLeaseState state, ResourceOwnerKey owner, ulong lastUsedFrame, ulong lastReturnedFrame)
        {
            LeaseId = leaseId;
            Descriptor = descriptor;
            EstimatedBytes = estimatedBytes;
            State = state;
            Owner = owner;
            LastUsedFrame = lastUsedFrame;
            LastReturnedFrame = lastReturnedFrame;
        }
    }

    public sealed class OwnershipSnapshot
    {
        private readonly IReadOnlyList<OwnershipSnapshotEntry> _entries;
        public long BudgetBytes { get; }
        public long LeasedBytes { get; }
        public long FreeBytes { get; }
        public long HighWaterBytes { get; }
        public bool BudgetWarningActive { get; }
        public double UsageRatio => BudgetBytes <= 0 ? 1d : (LeasedBytes + FreeBytes) / (double)BudgetBytes;
        public IReadOnlyList<OwnershipSnapshotEntry> Entries => _entries;

        internal OwnershipSnapshot(long budgetBytes, long leasedBytes, long freeBytes, long highWaterBytes, bool budgetWarningActive, IEnumerable<OwnershipSnapshotEntry> entries)
        {
            BudgetBytes = budgetBytes;
            LeasedBytes = leasedBytes;
            FreeBytes = freeBytes;
            HighWaterBytes = highWaterBytes;
            BudgetWarningActive = budgetWarningActive;
            _entries = new ReadOnlyCollection<OwnershipSnapshotEntry>((entries ?? Enumerable.Empty<OwnershipSnapshotEntry>()).ToList());
        }
    }

    /// <summary>
    /// Application-lifetime pool. Free textures are the only resources that
    /// budget pressure may reclaim; leased textures are never stolen.
    /// </summary>
    public sealed class RenderTexturePool : IDisposable
    {
        private sealed class Entry
        {
            public RenderTexture Texture;
            public TextureDescriptor Descriptor;
            public long EstimatedBytes;
            public TextureLeaseState State;
            public OutputLeaseId LeaseId;
            public ResourceOwnerKey Owner;
            public ulong LastUsedFrame;
            public ulong LastReturnedFrame;
            public ulong CreationSequence;
            public DateTime LastReturnedAtUtc;
        }

        private readonly Dictionary<RenderTexture, Entry> _entries = new Dictionary<RenderTexture, Entry>();
        private readonly HashSet<TextureLeaseHandle> _handles = new HashSet<TextureLeaseHandle>();
        private long _budgetBytes;
        private ulong _nextLeaseValue;
        private ulong _nextCreationSequence;
        private bool _disposed;
        private long _currentBytes;
        private long _leasedBytes;
        private long _highWaterBytes;
        private bool _budgetWarningActive;
        private readonly List<Diagnostic> _diagnostics = new List<Diagnostic>();

        public long BudgetBytes => _budgetBytes;
        public long CurrentBytes => _currentBytes;
        public long LeasedBytes => _leasedBytes;
        public long FreeBytes => _currentBytes - _leasedBytes;
        public long HighWaterBytes => _highWaterBytes;
        public ulong CurrentFrame { get; private set; }
        public RenderingPlatformCapabilities Capabilities { get; }
        public Diagnostic StartupDiagnostic { get; }
        public RenderingBudgetState CurrentBudgetState => new RenderingBudgetState(_budgetBytes, _leasedBytes, _currentBytes, _budgetWarningActive);
        public bool BudgetWarningActive => _budgetWarningActive;
        public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics.AsReadOnly();

        public RenderTexturePool() : this(RenderingPlatformCapabilities.FromUnity())
        {
        }

        public RenderTexturePool(long budgetBytes)
        {
            if (budgetBytes < 1) throw new ArgumentOutOfRangeException(nameof(budgetBytes));
            _budgetBytes = budgetBytes;
            Capabilities = default;
            StartupDiagnostic = null;
        }

        public RenderTexturePool(RenderingPlatformCapabilities capabilities)
        {
            Capabilities = capabilities;
            _budgetBytes = RenderingBudgetPolicy.DefaultBudget(capabilities, out var startupDiagnostic);
            StartupDiagnostic = startupDiagnostic;
            if (startupDiagnostic != null) _diagnostics.Add(startupDiagnostic);
        }

        public RenderTexturePool(IRenderingPlatformCapabilityPort capabilityPort)
            : this((capabilityPort ?? throw new ArgumentNullException(nameof(capabilityPort))).Capabilities)
        {
        }

        public Result SetBudget(long budgetBytes)
        {
            if (_disposed) return Result.Failure(RenderingDiagnostics.Error("rendering.pool.disposed", "The RenderTexturePool is disposed."));
            var valid = RenderingBudgetPolicy.ValidateUserBudget(Capabilities, budgetBytes, _leasedBytes);
            if (valid.IsFailure) return valid;
            _budgetBytes = budgetBytes;
            TrimToBudget();
            UpdateBudgetWarning();
            return Result.Success();
        }

        public Result<TextureLeaseHandle> Acquire(TextureDescriptor descriptor, ResourceOwnerKey owner, ulong frameNumber)
        {
            if (_disposed) return Result<TextureLeaseHandle>.Failure(RenderingDiagnostics.Error("rendering.pool.disposed", "The RenderTexturePool is disposed."));
            if (!owner.IsValid) return Result<TextureLeaseHandle>.Failure(RenderingDiagnostics.Error("rendering.pool.owner_invalid", "A valid resource owner is required."));
            if (frameNumber == 0) return Result<TextureLeaseHandle>.Failure(RenderingDiagnostics.Error("rendering.pool.frame_invalid", "Frame number must be positive."));
            if (descriptor.SRgb) return Result<TextureLeaseHandle>.Failure(RenderingDiagnostics.Error("rendering.pool.internal_srgb", "Internal pooled surfaces must use a linear, non-sRGB descriptor."));
            CurrentFrame = Math.Max(CurrentFrame, frameNumber);
            var reusable = _entries.Values
                .Where(entry => entry.State == TextureLeaseState.Free && entry.Descriptor == descriptor)
                .OrderBy(entry => entry.LastReturnedFrame)
                .ThenBy(entry => entry.CreationSequence)
                .FirstOrDefault();
            if (reusable != null)
                return Result<TextureLeaseHandle>.Success(Lease(reusable, owner, frameNumber));

            var bytes = EstimateBytes(descriptor);
            if (!MakeRoom(bytes))
                return Result<TextureLeaseHandle>.Failure(RenderingDiagnostics.Error("rendering.pool.budget_exceeded", "The RenderTexture budget cannot satisfy the request without reclaiming a leased texture."));

            RenderTexture texture = null;
            try
            {
                texture = new RenderTexture(descriptor.ToUnityDescriptor()) { name = "ShitDesigner.PooledOutput" };
                texture.Create();
                if (!texture.IsCreated()) throw new InvalidOperationException("Unity failed to create the RenderTexture.");
                texture.filterMode = FilterMode.Bilinear;
            }
            catch (Exception exception)
            {
                if (texture != null) DestroyTexture(texture);
                return Result<TextureLeaseHandle>.Failure(new Diagnostic(new DiagnosticCode("rendering.pool.create_failed"), Severity.Error, "RenderTexture creation failed.", exception: DiagnosticExceptionInfo.FromException(exception)));
            }

            var entry = new Entry
            {
                Texture = texture,
                Descriptor = descriptor,
                EstimatedBytes = bytes,
                State = TextureLeaseState.Free,
                LastReturnedFrame = frameNumber,
                LastUsedFrame = frameNumber,
                CreationSequence = ++_nextCreationSequence,
                LastReturnedAtUtc = DateTime.UtcNow
            };
            _entries.Add(texture, entry);
            _currentBytes += bytes;
            _highWaterBytes = Math.Max(_highWaterBytes, _currentBytes);
            UpdateBudgetWarning();
            return Result<TextureLeaseHandle>.Success(Lease(entry, owner, frameNumber));
        }

        private TextureLeaseHandle Lease(Entry entry, ResourceOwnerKey owner, ulong frameNumber)
        {
            entry.State = TextureLeaseState.Leased;
            entry.LeaseId = new OutputLeaseId(++_nextLeaseValue);
            entry.Owner = owner;
            entry.LastUsedFrame = frameNumber;
            _leasedBytes += entry.EstimatedBytes;
            var handle = new TextureLeaseHandle(this, entry.Texture, entry.LeaseId, entry.Descriptor, owner);
            _handles.Add(handle);
            return handle;
        }

        internal Result<BorrowedOutputSurface> Borrow(TextureLeaseHandle handle, ulong frameNumber)
        {
            if (_disposed) return Result<BorrowedOutputSurface>.Failure(RenderingDiagnostics.Error("rendering.pool.disposed", "The RenderTexturePool is disposed."));
            if (handle == null) return Result<BorrowedOutputSurface>.Failure(RenderingDiagnostics.Error("rendering.pool.lease_invalid", "A lease handle is required."));
            if (frameNumber == 0) return Result<BorrowedOutputSurface>.Failure(RenderingDiagnostics.Error("rendering.pool.frame_invalid", "Frame number must be positive."));
            if (!_entries.TryGetValue(handle.Texture, out var entry) || entry.State != TextureLeaseState.Leased || entry.LeaseId != handle.LeaseId)
                return Result<BorrowedOutputSurface>.Failure(RenderingDiagnostics.Error("rendering.pool.lease_invalid", "The lease is no longer active."));
            if (entry.Owner != handle.Owner)
                return Result<BorrowedOutputSurface>.Failure(RenderingDiagnostics.Error("rendering.pool.owner_mismatch", "The lease owner does not match the current owner."));
            CurrentFrame = Math.Max(CurrentFrame, frameNumber);
            entry.LastUsedFrame = frameNumber;
            try
            {
                return Result<BorrowedOutputSurface>.Success(new BorrowedOutputSurface(new ImageFrame(entry.Texture, new Vector2Int(entry.Descriptor.Width, entry.Descriptor.Height), entry.Descriptor.GraphicsFormat, frameNumber, entry.LeaseId)));
            }
            catch (Exception exception)
            {
                return Result<BorrowedOutputSurface>.Failure(new Diagnostic(new DiagnosticCode("rendering.pool.frame_invalid"), Severity.Error, "The leased texture cannot be exposed as an ImageFrame.", exception: DiagnosticExceptionInfo.FromException(exception)));
            }
        }

        internal Result Release(TextureLeaseHandle handle, ResourceOwnerKey requester, ulong frameNumber)
        {
            if (_disposed) return Result.Failure(RenderingDiagnostics.Error("rendering.pool.disposed", "The RenderTexturePool is disposed."));
            if (handle == null) return Result.Failure(RenderingDiagnostics.Error("rendering.pool.lease_invalid", "A lease handle is required."));
            if (!requester.IsValid) return Result.Failure(RenderingDiagnostics.Error("rendering.pool.owner_invalid", "A valid release owner is required."));
            if (frameNumber == 0) return Result.Failure(RenderingDiagnostics.Error("rendering.pool.frame_invalid", "Frame number must be positive."));
            if (!_entries.TryGetValue(handle.Texture, out var entry) || entry.LeaseId != handle.LeaseId)
                return Result.Failure(RenderingDiagnostics.Error("rendering.pool.double_release", "The lease was already released or is unknown."));
            if (entry.State != TextureLeaseState.Leased)
                return Result.Failure(RenderingDiagnostics.Error("rendering.pool.double_release", "The lease was already released."));
            if (entry.Owner != requester)
                return Result.Failure(RenderingDiagnostics.Error("rendering.pool.owner_mismatch", "The release owner or generation does not match the current lease."));
            CurrentFrame = Math.Max(CurrentFrame, frameNumber);
            entry.State = TextureLeaseState.Free;
            // A returned entry no longer has a live OutputLeaseId.  Keeping
            // the old id in the pool's ownership snapshot makes a retired
            // generation look owned after Phase 9, and can make a later
            // same-descriptor reuse appear to have two owners.  The handle
            // retains its immutable id for double-release diagnostics; the
            // pool entry receives a fresh monotonic id when it is leased
            // again.
            entry.LeaseId = default(OutputLeaseId);
            entry.Owner = default;
            entry.LastReturnedFrame = frameNumber;
            entry.LastUsedFrame = frameNumber;
            entry.LastReturnedAtUtc = DateTime.UtcNow;
            _leasedBytes -= entry.EstimatedBytes;
            UpdateBudgetWarning();
            return Result.Success();
        }

        private bool MakeRoom(long requiredBytes)
        {
            while (_currentBytes + requiredBytes > _budgetBytes)
            {
                var candidate = _entries.Values
                    .Where(entry => entry.State == TextureLeaseState.Free)
                    .OrderBy(entry => entry.LastReturnedFrame)
                    .ThenBy(entry => entry.CreationSequence)
                    .FirstOrDefault();
                if (candidate == null) return false;
                _entries.Remove(candidate.Texture);
                _currentBytes -= candidate.EstimatedBytes;
                DestroyTexture(candidate.Texture);
            }
            UpdateBudgetWarning();
            return true;
        }

        private void TrimToBudget()
        {
            while (_currentBytes > _budgetBytes)
            {
                var candidate = _entries.Values
                    .Where(entry => entry.State == TextureLeaseState.Free)
                    .OrderBy(entry => entry.LastReturnedFrame)
                    .ThenBy(entry => entry.CreationSequence)
                    .FirstOrDefault();
                if (candidate == null) break;
                _entries.Remove(candidate.Texture);
                _currentBytes -= candidate.EstimatedBytes;
                DestroyTexture(candidate.Texture);
            }
        }

        /// <summary>Remove only Free entries unused for at least ten seconds at 60 Hz.</summary>
        public int TrimFree(ulong frameNumber, ulong unusedFrameThreshold = 600)
        {
            if (_disposed) return 0;
            if (frameNumber == 0) throw new ArgumentOutOfRangeException(nameof(frameNumber));
            CurrentFrame = Math.Max(CurrentFrame, frameNumber);
            var removed = 0;
            foreach (var candidate in _entries.Values
                         .Where(entry => entry.State == TextureLeaseState.Free && frameNumber >= entry.LastUsedFrame && frameNumber - entry.LastUsedFrame >= unusedFrameThreshold)
                         .OrderBy(entry => entry.LastReturnedFrame).ThenBy(entry => entry.CreationSequence).ToList())
            {
                _entries.Remove(candidate.Texture);
                _currentBytes -= candidate.EstimatedBytes;
                DestroyTexture(candidate.Texture);
                removed++;
            }
            UpdateBudgetWarning();
            return removed;
        }

        public int TrimFreeUnused(ulong frameNumber, ulong unusedFrameThreshold = 600) => TrimFree(frameNumber, unusedFrameThreshold);

        public int TrimFree(TimeSpan unusedFor)
        {
            if (unusedFor < TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(unusedFor));
            if (_disposed) return 0;
            var cutoff = DateTime.UtcNow - unusedFor;
            var removed = 0;
            foreach (var candidate in _entries.Values.Where(entry => entry.State == TextureLeaseState.Free && entry.LastReturnedAtUtc <= cutoff)
                         .OrderBy(entry => entry.LastReturnedAtUtc).ThenBy(entry => entry.CreationSequence).ToList())
            {
                _entries.Remove(candidate.Texture);
                _currentBytes -= candidate.EstimatedBytes;
                DestroyTexture(candidate.Texture);
                removed++;
            }
            UpdateBudgetWarning();
            return removed;
        }

        private void UpdateBudgetWarning()
        {
            var warning = _currentBytes >= _budgetBytes * RenderingBudgetPolicy.WarningRatio;
            if (warning == _budgetWarningActive) return;
            _budgetWarningActive = warning;
            var code = warning ? "rendering.pool.budget_warning" : "rendering.pool.budget_recovered";
            var severity = warning ? Severity.Warning : Severity.Info;
            _diagnostics.Add(new Diagnostic(new DiagnosticCode(code), severity,
                warning ? "RenderTexture pool usage reached 85 percent of its budget." : "RenderTexture pool usage recovered below 85 percent.",
                detail: new DiagnosticDetail(new[]
                {
                    new KeyValuePair<string, string>("budgetBytes", _budgetBytes.ToString()),
                    new KeyValuePair<string, string>("currentBytes", _currentBytes.ToString())
                })));
        }

        public OwnershipSnapshot CaptureOwnershipSnapshot()
        {
            var entries = _entries.Values
                .OrderBy(entry => entry.LeaseId.Value)
                .Select(entry => new OwnershipSnapshotEntry(entry.LeaseId, entry.Descriptor, entry.EstimatedBytes, entry.State, entry.Owner, entry.LastUsedFrame, entry.LastReturnedFrame));
            return new OwnershipSnapshot(_budgetBytes, _leasedBytes, FreeBytes, _highWaterBytes, _budgetWarningActive, entries);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var handle in _handles) handle.MarkPoolDisposed();
            _handles.Clear();
            foreach (var entry in _entries.Keys.ToList()) DestroyTexture(entry);
            _entries.Clear();
            _currentBytes = 0;
            _leasedBytes = 0;
        }

        public static long EstimateBytes(TextureDescriptor descriptor)
        {
            var colorBytes = EstimateMipChainBytes(descriptor.GraphicsFormat, descriptor.Width, descriptor.Height,
                descriptor.VolumeDepth, descriptor.MsaaSamples, descriptor.Dimension, descriptor.MipMap);
            var depthBytes = descriptor.DepthStencilFormat == GraphicsFormat.None ? 0L : EstimateMipChainBytes(
                descriptor.DepthStencilFormat, descriptor.Width, descriptor.Height, descriptor.VolumeDepth,
                descriptor.MsaaSamples, descriptor.Dimension, descriptor.MipMap);
            return checked(colorBytes + depthBytes);
        }

        public static long EstimateLevelBytes(GraphicsFormat format, int width, int height, int volumeDepth = 1, int msaaSamples = 1)
        {
            if (width < 1) throw new ArgumentOutOfRangeException(nameof(width));
            if (height < 1) throw new ArgumentOutOfRangeException(nameof(height));
            if (volumeDepth < 1) throw new ArgumentOutOfRangeException(nameof(volumeDepth));
            if (msaaSamples < 1) throw new ArgumentOutOfRangeException(nameof(msaaSamples));
            try
            {
                var blockSize = GraphicsFormatUtility.GetBlockSize(format);
                var blockWidth = GraphicsFormatUtility.GetBlockWidth(format);
                var blockHeight = GraphicsFormatUtility.GetBlockHeight(format);
                if (blockSize > 0 && blockWidth > 0 && blockHeight > 0)
                {
                    var blocksX = (width + blockWidth - 1L) / blockWidth;
                    var blocksY = (height + blockHeight - 1L) / blockHeight;
                    return checked(blocksX * blocksY * volumeDepth * blockSize * msaaSamples);
                }
            }
            catch
            {
                // Unsupported formats are still accounted for deterministically
                // so budget diagnostics remain usable before GPU creation.
            }
            return checked((long)width * height * volumeDepth * 4 * msaaSamples);
        }

        private static long EstimateMipChainBytes(GraphicsFormat format, int width, int height, int volumeDepth,
            int msaaSamples, TextureDimension dimension, bool mipMap)
        {
            long total = 0;
            var mipWidth = width;
            var mipHeight = height;
            var mipDepth = volumeDepth;
            do
            {
                total = checked(total + EstimateLevelBytes(format, mipWidth, mipHeight, mipDepth, msaaSamples));
                if (!mipMap || (mipWidth == 1 && mipHeight == 1 && (dimension != TextureDimension.Tex3D || mipDepth == 1))) break;
                mipWidth = Math.Max(1, mipWidth / 2);
                mipHeight = Math.Max(1, mipHeight / 2);
                if (dimension == TextureDimension.Tex3D) mipDepth = Math.Max(1, mipDepth / 2);
            }
            while (true);
            return total;
        }

        private static void DestroyTexture(RenderTexture texture)
        {
            if (texture == null) return;
            // Unity logs an error when an active render target is released or destroyed.
            // Pool disposal can run from node teardown while the last pass still owns
            // the active surface, so detach it before returning the texture to Unity.
            if (RenderTexture.active == texture) RenderTexture.active = null;
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(texture);
            else UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    public sealed class TextureLeaseHandle
    {
        private readonly RenderTexturePool _pool;
        private bool _released;

        internal TextureLeaseHandle(RenderTexturePool pool, RenderTexture texture, OutputLeaseId leaseId, TextureDescriptor descriptor, ResourceOwnerKey owner)
        {
            _pool = pool;
            Texture = texture;
            LeaseId = leaseId;
            Descriptor = descriptor;
            Owner = owner;
        }

        public RenderTexture Texture { get; }
        public OutputLeaseId LeaseId { get; }
        public TextureDescriptor Descriptor { get; }
        public ResourceOwnerKey Owner { get; }
        public bool IsReleased => _released;

        public Result<BorrowedOutputSurface> Borrow(ulong frameNumber)
        {
            if (_released) return Result<BorrowedOutputSurface>.Failure(RenderingDiagnostics.Error("rendering.pool.double_release", "A released lease cannot be borrowed."));
            return _pool.Borrow(this, frameNumber);
        }

        public Result Release() => Release(Owner, _pool.CurrentFrame);

        public Result Release(ResourceOwnerKey requester, ulong frameNumber)
        {
            if (_released) return Result.Failure(RenderingDiagnostics.Error("rendering.pool.double_release", "A lease can only be released once."));
            var result = _pool.Release(this, requester, frameNumber);
            if (result.IsSuccess) _released = true;
            return result;
        }

        internal void MarkPoolDisposed() => _released = true;
    }
}
