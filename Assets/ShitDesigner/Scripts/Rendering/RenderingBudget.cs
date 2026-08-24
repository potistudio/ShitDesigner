using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Rendering {
	public enum RenderingMemoryKind {
		DedicatedGpu,
		Unified
	}

	/// <summary>
	/// Port for the small set of platform memory facts used by the pool.  It
	/// is deliberately a value type so tests can supply a deterministic
	/// capability set without touching Unity's global SystemInfo state.
	/// </summary>
	public readonly struct RenderingPlatformCapabilities {
		public RenderingMemoryKind MemoryKind { get; }
		public long DedicatedMemoryBytes { get; }
		public long SystemMemoryBytes { get; }
		public bool DedicatedMemoryKnown { get; }
		public bool SystemMemoryKnown { get; }

		public bool IsUnified => MemoryKind == RenderingMemoryKind.Unified;

		public RenderingPlatformCapabilities(RenderingMemoryKind memoryKind,
			long dedicatedMemoryBytes = 0, long systemMemoryBytes = 0,
			bool dedicatedMemoryKnown = false, bool systemMemoryKnown = false) {
			if (dedicatedMemoryBytes < 0) throw new ArgumentOutOfRangeException(nameof(dedicatedMemoryBytes));
			if (systemMemoryBytes < 0) throw new ArgumentOutOfRangeException(nameof(systemMemoryBytes));
			MemoryKind = memoryKind;
			DedicatedMemoryBytes = dedicatedMemoryBytes;
			SystemMemoryBytes = systemMemoryBytes;
			DedicatedMemoryKnown = dedicatedMemoryKnown && dedicatedMemoryBytes > 0;
			SystemMemoryKnown = systemMemoryKnown && systemMemoryBytes > 0;
		}

		public static RenderingPlatformCapabilities FromUnity() {
			var graphicsMemory = (long)Math.Max(0, SystemInfo.graphicsMemorySize) * RenderingBudgetPolicy.MiB;
			var systemMemory = (long)Math.Max(0, SystemInfo.systemMemorySize) * RenderingBudgetPolicy.MiB;
			// Metal devices expose a shared pool.  The explicit memory kind
			// can still be supplied by a host integration when Unity reports
			// incomplete values.
			var unified = UnityEngine.Application.platform == RuntimePlatform.OSXEditor ||
						  UnityEngine.Application.platform == RuntimePlatform.OSXPlayer ||
						  (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Metal && graphicsMemory == 0);
			return new RenderingPlatformCapabilities(
				unified ? RenderingMemoryKind.Unified : RenderingMemoryKind.DedicatedGpu,
				graphicsMemory, systemMemory, graphicsMemory > 0, systemMemory > 0);
		}
	}

	public readonly struct RenderingBudgetState {
		public long BudgetBytes { get; }
		public long LeasedBytes { get; }
		public long CurrentBytes { get; }
		public bool WarningActive { get; }
		public double UsageRatio => BudgetBytes <= 0 ? 1d : CurrentBytes / (double)BudgetBytes;

		internal RenderingBudgetState(long budgetBytes, long leasedBytes, long currentBytes, bool warningActive) {
			BudgetBytes = budgetBytes;
			LeasedBytes = leasedBytes;
			CurrentBytes = currentBytes;
			WarningActive = warningActive;
		}
	}

	public static class RenderingBudgetPolicy {
		public const long MiB = 1024L * 1024L;
		public const long GiB = 1024L * MiB;
		public const long UnknownMemoryBytes = 2L * GiB;
		public const long MinimumStartupBudgetBytes = 512L * MiB;
		public const long DedicatedReservedBytes = 1L * GiB;
		public const double WarningRatio = 0.85d;

		public static long DefaultBudget(RenderingPlatformCapabilities capabilities, out Diagnostic startupDiagnostic) {
			startupDiagnostic = null;
			long budget;
			if (capabilities.IsUnified) {
				// A missing platform memory query uses the documented 2 GiB
				// conservative budget directly; a known query applies the
				// unified-memory percentage rule.
				budget = capabilities.SystemMemoryKnown ? Math.Min(capabilities.SystemMemoryBytes / 4L, 4L * GiB) : UnknownMemoryBytes;
			}
			else {
				// Unknown dedicated VRAM also falls back to a conservative 2
				// GiB budget instead of manufacturing a fake VRAM capacity.
				budget = capabilities.DedicatedMemoryKnown
					? Math.Min(capabilities.DedicatedMemoryBytes / 2L, Math.Max(1L, capabilities.DedicatedMemoryBytes - (3L * GiB) / 2L))
					: UnknownMemoryBytes;
				if (capabilities.DedicatedMemoryKnown && budget < MinimumStartupBudgetBytes) {
					startupDiagnostic = new Diagnostic(new DiagnosticCode("rendering.pool.startup_budget_low"), Severity.Warning,
						"The dedicated GPU memory budget is below 512 MiB.",
						detail: new DiagnosticDetail(new[] { new KeyValuePair<string, string>("budgetBytes", budget.ToString()) }));
				}
			}
			return Math.Max(1L, budget);
		}

		public static CSharpFunctionalExtensions.UnitResult<Diagnostic> ValidateUserBudget(RenderingPlatformCapabilities capabilities, long requestedBytes, long leasedBytes) {
			if (requestedBytes < 1)
				return CSharpFunctionalExtensions.UnitResult.Failure<Diagnostic>(Error("rendering.pool.budget_invalid", "The rendering budget must be positive."));
			if (requestedBytes < leasedBytes)
				return CSharpFunctionalExtensions.UnitResult.Failure<Diagnostic>(Error("rendering.pool.budget_below_leased", "The rendering budget cannot be smaller than currently leased resources."));
			if (!capabilities.IsUnified && capabilities.DedicatedMemoryKnown && requestedBytes > capabilities.DedicatedMemoryBytes - DedicatedReservedBytes)
				return CSharpFunctionalExtensions.UnitResult.Failure<Diagnostic>(Error("rendering.pool.budget_dedicated_limit", "The dedicated GPU budget must leave at least 1 GiB available to the application."));
			if (capabilities.IsUnified && capabilities.SystemMemoryKnown && requestedBytes > capabilities.SystemMemoryBytes * 0.40d)
				return CSharpFunctionalExtensions.UnitResult.Failure<Diagnostic>(Error("rendering.pool.budget_unified_limit", "The unified-memory budget cannot exceed 40 percent of system memory."));
			return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
		}

		private static Diagnostic Error(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "rendering");
	}
}
