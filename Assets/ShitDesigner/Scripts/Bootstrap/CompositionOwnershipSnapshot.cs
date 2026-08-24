using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Rendering;

namespace ShitDesigner.Bootstrap {
	/// <summary>Allocation-free per-frame output descriptor for Performance
	/// measurement. Complete ownership entries remain available through
	/// CompositionOwnershipSnapshot for warm-up, failure and teardown.</summary>
	public readonly struct PerformanceSurfaceSnapshot {
		public string Id { get; }
		public int Width { get; }
		public int Height { get; }
		public string GraphicsFormat { get; }
		public int TargetFramesPerSecond { get; }
		public ulong FrameNumber { get; }
		public bool IsBound { get; }
		public PerformanceSurfaceSnapshot(string id, int width, int height, string graphicsFormat, int targetFramesPerSecond, ulong frameNumber, bool isBound) { Id = id ?? string.Empty; Width = width; Height = height; GraphicsFormat = graphicsFormat ?? string.Empty; TargetFramesPerSecond = targetFramesPerSecond; FrameNumber = frameNumber; IsBound = isBound; }
	}

	/// <summary>Scalar performance health captured without sorting/copying the
	/// pool or Runtime node graph. Preview descriptors are written into a
	/// caller-owned buffer.</summary>
	public readonly struct PerformanceHealthSnapshot {
		public long PoolBudgetBytes { get; }
		public long PoolLeasedBytes { get; }
		public long PoolFreeBytes { get; }
		public long PoolHighWaterBytes { get; }
		public bool PoolBudgetWarning { get; }
		public int SceneCount { get; }
		public int LayerCount { get; }
		public int BackendCount { get; }
		public int NativeContextCount { get; }
		public int ActiveOutputLeaseCount { get; }
		public int RequiredPreviewCount { get; }
		public bool RuntimeDisposed { get; }
		public PerformanceSurfaceSnapshot Program { get; }
		public PerformanceHealthSnapshot(long poolBudgetBytes, long poolLeasedBytes, long poolFreeBytes, long poolHighWaterBytes, bool poolBudgetWarning,
			int sceneCount, int layerCount, int backendCount, int nativeContextCount, int activeOutputLeaseCount, int requiredPreviewCount,
			bool runtimeDisposed, PerformanceSurfaceSnapshot program) { PoolBudgetBytes = poolBudgetBytes; PoolLeasedBytes = poolLeasedBytes; PoolFreeBytes = poolFreeBytes; PoolHighWaterBytes = poolHighWaterBytes; PoolBudgetWarning = poolBudgetWarning; SceneCount = sceneCount; LayerCount = layerCount; BackendCount = backendCount; NativeContextCount = nativeContextCount; ActiveOutputLeaseCount = activeOutputLeaseCount; RequiredPreviewCount = requiredPreviewCount; RuntimeDisposed = runtimeDisposed; Program = program; }
	}

	/// <summary>Read-only ownership counts supplied by the concrete production
	/// visual binding provider. The harness can observe lifetime state without
	/// gaining a mutation path into Scene or Media.</summary>
	public sealed class BindingOwnershipSnapshot {
		public int SceneCount { get; }
		public int LayerCount { get; }
		public BindingOwnershipSnapshot(int sceneCount, int layerCount) {
			SceneCount = Math.Max(0, sceneCount);
			LayerCount = Math.Max(0, layerCount);
		}
	}

	public sealed class SurfaceOwnershipSnapshot {
		public string Id { get; }
		public string TargetKind { get; }
		public int Width { get; }
		public int Height { get; }
		public string GraphicsFormat { get; }
		public int TargetFramesPerSecond { get; }
		public ulong FrameNumber { get; }
		public SurfaceOwnershipSnapshot(string id, string targetKind, int width, int height, string graphicsFormat, int targetFramesPerSecond, ulong frameNumber) {
			Id = id ?? string.Empty;
			TargetKind = targetKind ?? string.Empty;
			Width = width;
			Height = height;
			GraphicsFormat = graphicsFormat ?? string.Empty;
			TargetFramesPerSecond = targetFramesPerSecond;
			FrameNumber = frameNumber;
		}
	}

	/// <summary>Complete public, immutable composition ownership projection.
	/// Counts and descriptors are captured from active production objects, not
	/// inferred from the persisted graph.</summary>
	public sealed class CompositionOwnershipSnapshot {
		public OwnershipSnapshot TexturePool { get; }
		public int SceneCount { get; }
		public int LayerCount { get; }
		public int BackendCount { get; }
		public int NativeContextCount { get; }
		public int ActiveOutputLeaseCount { get; }
		public SurfaceOwnershipSnapshot Program { get; }
		public IReadOnlyList<SurfaceOwnershipSnapshot> Previews { get; }
		public bool RuntimeDisposed { get; }

		public CompositionOwnershipSnapshot(OwnershipSnapshot texturePool, int sceneCount, int layerCount, int backendCount, int nativeContextCount,
			int activeOutputLeaseCount, SurfaceOwnershipSnapshot program, IEnumerable<SurfaceOwnershipSnapshot> previews, bool runtimeDisposed) {
			TexturePool = texturePool;
			SceneCount = Math.Max(0, sceneCount);
			LayerCount = Math.Max(0, layerCount);
			BackendCount = Math.Max(0, backendCount);
			NativeContextCount = Math.Max(0, nativeContextCount);
			ActiveOutputLeaseCount = Math.Max(0, activeOutputLeaseCount);
			Program = program;
			Previews = new ReadOnlyCollection<SurfaceOwnershipSnapshot>((previews ?? Enumerable.Empty<SurfaceOwnershipSnapshot>()).Where(x => x != null).ToList());
			RuntimeDisposed = runtimeDisposed;
		}
	}
}
