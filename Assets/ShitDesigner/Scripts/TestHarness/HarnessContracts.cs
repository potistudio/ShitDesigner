using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections.ObjectModel;
using System.Text;
using ShitDesigner.Application;
using ShitDesigner.Bootstrap;
using ShitDesigner.Core;
using ShitDesigner.Media;
using ShitDesigner.Persistence;
using ShitDesigner.Project;
using ShitDesigner.Rendering;
using Unity.Profiling;
using UnityEngine;

namespace ShitDesigner.TestHarness
{
	public enum HarnessCodec
	{
		H264,
		Hap
	}

	public enum HarnessMode
	{
		Performance,
		Acceptance
	}

	public enum HarnessAcceptanceStage
	{
		Initial,
		Reopen,
		Recovery
	}

	public enum HarnessRunStatus
	{
		Passed,
		Failed,
		EnvironmentFailed
	}

	/// <summary>
	/// Public Application task values used by the standalone media-import
	/// flow.  The Application deliberately projects the internal probe stage
	/// as <see cref="ProbeConfirmationStage"/>; harnesses must consume this
	/// read-model value rather than an internal media transaction enum.
	/// </summary>
	public static class HarnessMediaImportContract
	{
		public const string ProbeConfirmationStage = "ProbeConfirmation";
		public const string WaitingStatus = "Waiting";
		public const string CompletedStatus = "Completed";
		public const string FailedStatus = "Failed";

		public static bool RequiresProbeConfirmation(ApplicationTaskReadModel task) =>
			task != null &&
			string.Equals(task.Stage, ProbeConfirmationStage, StringComparison.OrdinalIgnoreCase) &&
			string.Equals(task.Status, WaitingStatus, StringComparison.OrdinalIgnoreCase);

		public static bool ShouldConfirmProbe(ApplicationTaskReadModel task, bool confirmationRequested) =>
			!confirmationRequested && RequiresProbeConfirmation(task);

		public static bool IsCompleted(ApplicationTaskReadModel task) =>
			task != null && string.Equals(task.Status, CompletedStatus, StringComparison.OrdinalIgnoreCase);

		public static bool IsFailed(ApplicationTaskReadModel task) =>
			task != null && string.Equals(task.Status, FailedStatus, StringComparison.OrdinalIgnoreCase);
	}

	[Serializable]
	public sealed class PerformanceCorpusManifest
	{
		public string version;
		public PerformanceCorpusEntry[] entries;
	}

	[Serializable]
	public sealed class PerformanceCorpusEntry
	{
		public string name;
		public string codec;
		public string file;
		public string xxh3_128;
		public long bytes;
		public int width;
		public int height;
		public int fps;
	}

	public sealed class CorpusValidationResult
	{
		public bool IsValid { get; }
		public string Version { get; }
		public PerformanceCorpusEntry Entry { get; }
		public string Root { get; }
		public string Error { get; }

		private CorpusValidationResult(bool valid, string version, PerformanceCorpusEntry entry, string root, string error)
		{
			IsValid = valid;
			Version = version ?? string.Empty;
			Entry = entry;
			Root = root ?? string.Empty;
			Error = error ?? string.Empty;
		}

		public static CorpusValidationResult Success(string root, PerformanceCorpusManifest manifest, PerformanceCorpusEntry entry)
			=> new CorpusValidationResult(true, manifest?.version, entry, root, null);

		public static CorpusValidationResult Failure(string root, string error)
			=> new CorpusValidationResult(false, string.Empty, null, root, error);
	}

	public static class PerformanceCorpusValidator
	{
		public static CorpusValidationResult Validate(string root, HarnessCodec codec)
		{
			if (string.IsNullOrWhiteSpace(root)) return CorpusValidationResult.Failure(root, "Performance corpus root is required.");
			root = Path.GetFullPath(root);
			var manifestPath = Path.Combine(root, "manifest.json");
			if (!File.Exists(manifestPath)) return CorpusValidationResult.Failure(root, "Performance corpus manifest is missing: " + manifestPath);

			PerformanceCorpusManifest manifest;
			try { manifest = JsonUtility.FromJson<PerformanceCorpusManifest>(File.ReadAllText(manifestPath)); }
			catch (Exception exception) { return CorpusValidationResult.Failure(root, "Performance corpus manifest is invalid: " + exception.Message); }
			if (manifest == null || string.IsNullOrWhiteSpace(manifest.version)) return CorpusValidationResult.Failure(root, "Performance corpus version is missing.");

			var expectedCodec = codec == HarnessCodec.H264 ? "H264" : "Hap";
			var entries = manifest.entries ?? Array.Empty<PerformanceCorpusEntry>();
			var entry = entries.FirstOrDefault(x => x != null && string.Equals(x.codec, expectedCodec, StringComparison.OrdinalIgnoreCase));
			if (entry == null) return CorpusValidationResult.Failure(root, "Performance corpus entry is missing for codec " + expectedCodec + ".");
			if (string.IsNullOrWhiteSpace(entry.file) || !AssetIntegrity.IsDigest(entry.xxh3_128) || entry.bytes <= 0 || entry.width != 1920 || entry.height != 1080 || entry.fps != 60)
				return CorpusValidationResult.Failure(root, "Performance corpus entry must be FHD 60fps with a valid XXH3-128 digest for codec " + expectedCodec + ".");

			var file = Path.GetFullPath(Path.Combine(root, entry.file));
			if (!IsContained(root, file)) return CorpusValidationResult.Failure(root, "Performance corpus entry escapes its root: " + entry.file);
			if (!File.Exists(file)) return CorpusValidationResult.Failure(root, "Performance corpus file is missing: " + file);
			try
			{
				var info = new FileInfo(file);
				if (info.Length != entry.bytes) return CorpusValidationResult.Failure(root, "Performance corpus byte count differs for " + entry.file + ".");
				using (var stream = File.OpenRead(file))
				{
					var actual = AssetIntegrity.Hash(stream);
					if (!string.Equals(actual, entry.xxh3_128, StringComparison.OrdinalIgnoreCase))
						return CorpusValidationResult.Failure(root, "Performance corpus XXH3-128 differs for " + entry.file + ".");
				}
			}
			catch (Exception exception) { return CorpusValidationResult.Failure(root, "Performance corpus could not be verified: " + exception.Message); }
			return CorpusValidationResult.Success(root, manifest, entry);
		}

		private static bool IsContained(string root, string path)
		{
			var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
		}
	}

	public readonly struct HarnessTopologyNode
	{
		public string Id { get; }
		public string TypeId { get; }
		public HarnessTopologyNode(string id, string typeId) { Id = id ?? string.Empty; TypeId = typeId ?? string.Empty; }
	}

	public readonly struct HarnessTopologyEdge
	{
		public string SourceId { get; }
		public string DestinationId { get; }
		public HarnessTopologyEdge(string sourceId, string destinationId) { SourceId = sourceId ?? string.Empty; DestinationId = destinationId ?? string.Empty; }
	}

	/// <summary>Contract-only topology check. It verifies reachability from
	/// every required production element to ProgramOutput, so a Video node
	/// connected only to Previews cannot masquerade as a complete scenario.</summary>
	public static class HarnessScenarioTopology
	{
		private static readonly string[] ProgramPathTypes =
		{
			"shitdesigner.scene.3d", "shitdesigner.scene.2d", "shitdesigner.shader.effect",
			"shitdesigner.video.player", "shitdesigner.shader.blend2", "system.feedback", "system.program_output"
		};

		public static string Validate(IEnumerable<HarnessTopologyNode> nodes, IEnumerable<HarnessTopologyEdge> edges)
		{
			var nodeList = (nodes ?? Enumerable.Empty<HarnessTopologyNode>()).ToList();
			var edgeList = (edges ?? Enumerable.Empty<HarnessTopologyEdge>()).ToList();
			var byId = nodeList.Where(x => !string.IsNullOrWhiteSpace(x.Id)).GroupBy(x => x.Id, StringComparer.Ordinal)
				.ToDictionary(x => x.Key, x => x.First().TypeId, StringComparer.Ordinal);
			if (byId.Count != nodeList.Count) return "Scenario contains duplicate or empty node IDs.";
			var output = nodeList.FirstOrDefault(x => string.Equals(x.TypeId, "system.program_output", StringComparison.Ordinal));
			if (string.IsNullOrWhiteSpace(output.Id)) return "Scenario is missing ProgramOutput.";
			var reverse = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			foreach (var edge in edgeList)
			{
				if (!byId.ContainsKey(edge.SourceId) || !byId.ContainsKey(edge.DestinationId)) return "Scenario edge references an unknown node.";
				if (!reverse.TryGetValue(edge.DestinationId, out var sources)) reverse[edge.DestinationId] = sources = new List<string>();
				sources.Add(edge.SourceId);
			}
			var reachesProgram = new HashSet<string>(StringComparer.Ordinal) { output.Id };
			var pending = new Queue<string>(reachesProgram);
			while (pending.Count > 0)
			{
				var current = pending.Dequeue();
				if (!reverse.TryGetValue(current, out var sources)) continue;
				foreach (var source in sources) if (reachesProgram.Add(source)) pending.Enqueue(source);
			}
			foreach (var type in ProgramPathTypes)
				if (!nodeList.Any(x => string.Equals(x.TypeId, type, StringComparison.Ordinal) && reachesProgram.Contains(x.Id)))
					return "Required Program path element is missing or does not reach ProgramOutput: " + type;
			var previews = nodeList.Where(x => string.Equals(x.TypeId, "system.preview", StringComparison.Ordinal)).ToList();
			if (previews.Count != 2) return "Scenario must contain exactly two Preview nodes.";
			var videoIds = new HashSet<string>(nodeList.Where(x => string.Equals(x.TypeId, "shitdesigner.video.player", StringComparison.Ordinal)).Select(x => x.Id), StringComparer.Ordinal);
			foreach (var preview in previews)
				if (!edgeList.Any(x => videoIds.Contains(x.SourceId) && string.Equals(x.DestinationId, preview.Id, StringComparison.Ordinal)))
					return "Each Preview must receive the VideoPlayer output.";
			return string.Empty;
		}
	}

	public static class HarnessOwnershipContract
	{
		public static CompositionOwnershipSnapshot CreateTestSnapshot(int sceneCount, int layerCount, int backendCount, int nativeContextCount,
			int activeOutputLeases, int programWidth, int programHeight, string programFormat, int programTargetFps, bool runtimeDisposed,
			int previewTargetFps = 30)
		{
			var previews = new[]
			{
				new SurfaceOwnershipSnapshot("preview1", "Preview", 640, 360, "R8G8B8A8_UNorm", previewTargetFps, 10),
				new SurfaceOwnershipSnapshot("preview2", "Preview", 640, 360, "R8G8B8A8_UNorm", previewTargetFps, 10)
			};
			return new CompositionOwnershipSnapshot(null, sceneCount, layerCount, backendCount, nativeContextCount, activeOutputLeases,
				new SurfaceOwnershipSnapshot("program", "Program", programWidth, programHeight, programFormat, programTargetFps, 10), previews, runtimeDisposed);
		}

		public static string ValidateTeardown(CompositionOwnershipSnapshot snapshot)
		{
			if (snapshot == null) return "Ownership snapshot is missing after teardown.";
			if (!snapshot.RuntimeDisposed || snapshot.SceneCount != 0 || snapshot.LayerCount != 0 || snapshot.BackendCount != 0 ||
				snapshot.NativeContextCount != 0 || snapshot.ActiveOutputLeaseCount != 0 || snapshot.TexturePool == null || snapshot.TexturePool.Entries.Count != 0)
				return "Session-owned resources remained after teardown.";
			return string.Empty;
		}

		public static string ValidateActiveDescriptors(CompositionOwnershipSnapshot snapshot)
		{
			if (snapshot?.Program == null || snapshot.Program.Width != 1920 || snapshot.Program.Height != 1080 ||
				!HarnessMetricEvaluator.IsPermittedProgramFormat(snapshot.Program.GraphicsFormat) || snapshot.Program.TargetFramesPerSecond != 60)
				return "Program active descriptor is invalid.";
			var previews = snapshot.Previews ?? Array.Empty<SurfaceOwnershipSnapshot>();
			if (previews.Count != 2 || previews.Any(x => x == null || !HarnessPreviewQualityContract.IsValidDescriptor(x.Width, x.Height, x.TargetFramesPerSecond) || string.IsNullOrWhiteSpace(x.GraphicsFormat)))
				return "Preview active descriptors are invalid.";
			return string.Empty;
		}
	}

	public sealed class HarnessDiagnosticResetObservation
	{
		public int CurrentCount { get; }
		public int HistoryCount { get; }
		public ulong FirstFrame { get; }
		public long AggregateCount { get; }
		public ulong LastFrame { get; }
		public HarnessDiagnosticResetObservation(int currentCount, int historyCount, ulong firstFrame, long aggregateCount, ulong lastFrame)
		{ CurrentCount = currentCount; HistoryCount = historyCount; FirstFrame = firstFrame; AggregateCount = aggregateCount; LastFrame = lastFrame; }
	}

	public static class HarnessDiagnosticContract
	{
		public static HarnessDiagnosticResetObservation ObserveResetRebase()
		{
			var hub = new ShitDesigner.Runtime.DiagnosticHub("harness-test");
			var diagnostic = new ShitDesigner.Core.Diagnostic(new ShitDesigner.Core.DiagnosticCode("runtime.test_fault"), ShitDesigner.Core.Severity.Error,
				"fault", nodeId: new ShitDesigner.Core.NodeInstanceId("node"), generationId: 1, frameNumber: 4);
			hub.BeginOrContinueFault(diagnostic);
			hub.ResetMeasurement(10);
			var rebased = hub.HistoryEntries.FirstOrDefault();
			hub.BeginOrContinueFault(diagnostic.WithFrame(11, 0));
			var aggregate = hub.ActiveFaults.Values.FirstOrDefault();
			return new HarnessDiagnosticResetObservation(hub.CurrentConditions.Count, hub.HistoryEntries.Count,
				rebased?.FirstFrame ?? 0, aggregate?.Count ?? 0, aggregate?.LastFrame ?? 0);
		}
	}

	[Serializable]
	public sealed class HarnessPreviewMetric
	{
		public string id;
		public int width;
		public int height;
		public string format;
		public int targetFramesPerSecond;
		public ulong frameNumber;
		public string quality;
		public int qualityStage = -1;
	}

	[Serializable]
	public sealed class HarnessPreviewQualitySample
	{
		public double sampleSeconds;
		public ulong programFrameNumber;
		public HarnessPreviewMetric[] previews;
	}

	public static class HarnessPreviewQualityContract
	{
		private static readonly int[] Widths = { 640, 480, 320, 160, 160 };
		private static readonly int[] Heights = { 360, 270, 180, 90, 90 };
		private static readonly int[] FramesPerSecond = { 30, 30, 20, 10, 5 };

		public static bool TryGetStage(HarnessPreviewMetric preview, out int stage)
		{
			stage = -1;
			if (preview == null) return false;
			if (preview.qualityStage < 0 || preview.qualityStage >= Widths.Length) return false;
			var expectedQuality = "Stage" + preview.qualityStage;
			if (!string.Equals(preview.quality, expectedQuality, StringComparison.Ordinal)) return false;
			if (preview.width != Widths[preview.qualityStage] || preview.height != Heights[preview.qualityStage] ||
				preview.targetFramesPerSecond != FramesPerSecond[preview.qualityStage]) return false;
			stage = preview.qualityStage;
			return true;
		}

		public static bool IsValidDescriptor(int width, int height, int targetFramesPerSecond)
		{
			for (var index = 0; index < Widths.Length; index++)
				if (width == Widths[index] && height == Heights[index] && targetFramesPerSecond == FramesPerSecond[index]) return true;
			return false;
		}

		/// <summary>
		/// Orders artifact-only coverage observations by their measurement
		/// timestamp, then appends one observation at the fixed measurement
		/// boundary. The last actual Preview descriptors and quality are state
		/// evidence for the final partial sub-frame; this must not become a
		/// timing metric or a Presented-frame observation.
		/// </summary>
		public static HarnessPreviewQualitySample[] AppendTerminalSample(
			IReadOnlyList<HarnessPreviewQualitySample> samples, double measureSeconds)
		{
			var existing = samples ?? Array.Empty<HarnessPreviewQualitySample>();
			var result = new List<HarnessPreviewQualitySample>(existing.Count + (existing.Count == 0 ? 0 : 1));

			// FrameTiming completions can be drained after a later
			// presentation has already been added. Keep this ordering local
			// to the Preview-quality artifact; the timing metric accumulator
			// and its Presented-frame denominator remain untouched.
			var ordered = existing.Select((sample, index) => new { sample, index })
				.Where(x => x.sample != null)
				.OrderBy(x => IsFiniteNonNegative(x.sample.sampleSeconds) ? 0 : 1)
				.ThenBy(x => IsFiniteNonNegative(x.sample.sampleSeconds) ? x.sample.sampleSeconds : 0d)
				.ThenBy(x => IsFiniteNonNegative(x.sample.sampleSeconds) ? x.sample.programFrameNumber : 0UL)
				// LINQ OrderBy is stable, but retain the source index in the
				// key so equal/malformed values remain explicitly stable.
				.ThenBy(x => x.index)
				.ToList();
			foreach (var entry in ordered) result.Add(entry.sample);
			// Preserve malformed/null source entries rather than dropping
			// them. Passed artifacts contain none, while failed finalization
			// remains safe and lossless.
			for (var index = 0; index < existing.Count; index++)
				if (existing[index] == null) result.Add(null);

			// A passed run already requires at least one quality sample. Keep
			// failed/partial finalization safe anyway, including a malformed
			// list whose tail is null, without inventing a Preview state.
			var last = ordered.Count == 0 ? null : ordered[ordered.Count - 1].sample;
			if (last == null) return result.ToArray();

			result.Add(new HarnessPreviewQualitySample
			{
				sampleSeconds = measureSeconds,
				programFrameNumber = last.programFrameNumber,
				previews = ClonePreviewMetrics(last.previews)
			});
			return result.ToArray();
		}

		private static bool IsFiniteNonNegative(double value) =>
			value >= 0d && !double.IsNaN(value) && !double.IsInfinity(value);

		private static HarnessPreviewMetric[] ClonePreviewMetrics(IReadOnlyList<HarnessPreviewMetric> previews)
		{
			if (previews == null) return null;
			var clone = new HarnessPreviewMetric[previews.Count];
			for (var index = 0; index < previews.Count; index++)
			{
				var preview = previews[index];
				clone[index] = preview == null ? null : new HarnessPreviewMetric
				{
					id = preview.id,
					width = preview.width,
					height = preview.height,
					format = preview.format,
					targetFramesPerSecond = preview.targetFramesPerSecond,
					frameNumber = preview.frameNumber,
					quality = preview.quality,
					qualityStage = preview.qualityStage
				};
			}
			return clone;
		}
	}

	/// <summary>Public Application parameter contract used by the standalone
	/// scenario to bind and start the VideoPlayer. Keeping these IDs at the
	/// Application boundary prevents a stringly-typed edit from silently
	/// leaving the transport stopped.</summary>
	public static class HarnessVideoTransportContract
	{
		public static readonly IReadOnlyList<string> RequiredParameterIds = new ReadOnlyCollection<string>(new[]
		{
			VideoPlayerContract.MediaAssetParameterId,
			VideoPlayerContract.PlayingParameterId,
			VideoPlayerContract.LoopParameterId
		});

		public static bool HasRequiredParameters(IReadOnlyList<ApplicationParameterReadModel> parameters, string nodeId)
		{
			if (parameters == null || string.IsNullOrWhiteSpace(nodeId)) return false;
			return RequiredParameterIds.All(id => parameters.Any(x => x != null && string.Equals(x.NodeId, nodeId, StringComparison.Ordinal) &&
				string.Equals(x.ParameterId, id, StringComparison.Ordinal)));
		}

		public static bool IsApplied(IReadOnlyList<ApplicationParameterReadModel> parameters, string nodeId, string mediaAssetId)
		{
			if (!HasRequiredParameters(parameters, nodeId) || string.IsNullOrWhiteSpace(mediaAssetId)) return false;
			return Matches(parameters, nodeId, VideoPlayerContract.MediaAssetParameterId, mediaAssetId) &&
				Matches(parameters, nodeId, VideoPlayerContract.PlayingParameterId, bool.TrueString) &&
				Matches(parameters, nodeId, VideoPlayerContract.LoopParameterId, bool.TrueString);
		}

		/// <summary>Requires the public transport state that can produce a
		/// real VideoPlayer frame, without depending on a private backend.</summary>
		public static bool HasActiveBinding(IReadOnlyList<ApplicationParameterReadModel> parameters, string nodeId)
		{
			if (!HasRequiredParameters(parameters, nodeId)) return false;
			var media = parameters.FirstOrDefault(x => x != null && string.Equals(x.NodeId, nodeId, StringComparison.Ordinal) &&
				string.Equals(x.ParameterId, VideoPlayerContract.MediaAssetParameterId, StringComparison.Ordinal));
			return media != null && !media.IsBroken && !string.IsNullOrWhiteSpace(media.BaseValue) &&
				string.Equals(media.BaseValue, media.EffectiveValue, StringComparison.Ordinal) &&
				Matches(parameters, nodeId, VideoPlayerContract.PlayingParameterId, bool.TrueString) &&
				Matches(parameters, nodeId, VideoPlayerContract.LoopParameterId, bool.TrueString);
		}

		private static bool Matches(IReadOnlyList<ApplicationParameterReadModel> parameters, string nodeId, string parameterId, string expected)
		{
			var parameter = parameters.FirstOrDefault(x => x != null && string.Equals(x.NodeId, nodeId, StringComparison.Ordinal) &&
				string.Equals(x.ParameterId, parameterId, StringComparison.Ordinal));
			return parameter != null && !parameter.IsBroken && string.Equals(parameter.BaseValue, expected, StringComparison.Ordinal) &&
				string.Equals(parameter.EffectiveValue, expected, StringComparison.Ordinal);
		}
	}

	public sealed class HarnessWarmupObservation
	{
		public bool ShaderCompilationReady { get; }
		public bool VideoPrepared { get; }
		public bool VideoFrameReady { get; }
		public bool InitialTexturesReady { get; }
		public bool Faulted { get; }
		public string Fault { get; }

		public HarnessWarmupObservation(bool shaderCompilationReady, bool videoPrepared, bool videoFrameReady,
			bool initialTexturesReady, bool faulted = false, string fault = null)
		{
			ShaderCompilationReady = shaderCompilationReady;
			VideoPrepared = videoPrepared;
			VideoFrameReady = videoFrameReady;
			InitialTexturesReady = initialTexturesReady;
			Faulted = faulted;
			Fault = fault ?? string.Empty;
		}
	}

	public sealed class HarnessWarmupEvaluation
	{
		public bool IsReady { get; }
		public bool IsFailure { get; }
		public string Reason { get; }

		private HarnessWarmupEvaluation(bool ready, bool failure, string reason)
		{ IsReady = ready; IsFailure = failure; Reason = reason ?? string.Empty; }

		public static HarnessWarmupEvaluation Ready() => new HarnessWarmupEvaluation(true, false, null);
		public static HarnessWarmupEvaluation Pending(string reason) => new HarnessWarmupEvaluation(false, false, reason);
		public static HarnessWarmupEvaluation Failure(string reason) => new HarnessWarmupEvaluation(false, true, reason);
	}

	public static class HarnessWarmupEvaluator
	{
		/// <summary>
		/// A Preview outside its update interval is deliberately absent from
		/// the current demand set.  Until its first demanded presentation, the
		/// graph can therefore report Blocked without a runtime fault.  The
		/// warm-up still requires both active Preview descriptors before it is
		/// complete, so this only prevents an initial scheduling state from
		/// being misclassified as terminal.
		/// </summary>
		public static bool IsTerminalNodeFailure(string nodeTypeId, string status)
		{
			if (string.Equals(status, "Faulted", StringComparison.OrdinalIgnoreCase)) return true;
			return string.Equals(status, "Blocked", StringComparison.OrdinalIgnoreCase) &&
				!string.Equals(nodeTypeId, "system.preview", StringComparison.Ordinal);
		}

		public static HarnessWarmupEvaluation Evaluate(HarnessWarmupObservation observation)
		{
			if (observation == null) return HarnessWarmupEvaluation.Failure("Warm-up readiness observation is unavailable.");
			if (observation.Faulted) return HarnessWarmupEvaluation.Failure(observation.Fault);
			var pending = new List<string>();
			if (!observation.ShaderCompilationReady) pending.Add("shader compilation");
			if (!observation.VideoPrepared) pending.Add("video preparation");
			if (!observation.VideoFrameReady) pending.Add("video frame ready");
			if (!observation.InitialTexturesReady) pending.Add("initial textures");
			return pending.Count == 0
				? HarnessWarmupEvaluation.Ready()
				: HarnessWarmupEvaluation.Pending("Waiting for " + string.Join(", ", pending) + ".");
		}
	}

	[Serializable]
	public sealed class HarnessDiagnosticInterval
	{
		public string kind;
		public double startSeconds;
		public double endSeconds;
		public double durationSeconds;
		public int samples;
	}

	[Serializable]
	public sealed class HarnessMetricSample
	{
		public double cpuMilliseconds;
		public double gpuMilliseconds;
		public double sampleSeconds;
		public ulong programFrameNumber;
		public int programWidth;
		public int programHeight;
		public string programFormat;
		public int programTargetFramesPerSecond;
		public HarnessPreviewMetric[] previews;
		public long poolBudgetBytes;
		public long poolLeasedBytes;
		public long poolFreeBytes;
		public long poolHighWaterBytes;
		public bool poolBudgetWarning;
		public bool programPresented;
		public bool programHealthy;
		public bool faulted;
		public bool fatal;
		public bool holdingLastFrame;
	}

	/// <summary>Keeps the presentation evidence for a frame until Unity
	/// finishes the delayed FrameTiming record for that exact frame. It is a
	/// join, not a cache of timing values: a completion can be consumed once,
	/// warm-up frames are fenced out, and an end-of-run drain preserves every
	/// unresolved presentation as an explicit unavailable quality sample.
	/// </summary>
	public sealed class HarnessTimingCompletionTracker
	{
		private readonly Dictionary<ulong, HarnessMetricSample> _pending = new Dictionary<ulong, HarnessMetricSample>();
		private ulong _measurementStartFrame;

		public int PendingCount => _pending.Count;

		public void BeginMeasurement(ulong measurementStartFrame)
		{
			_measurementStartFrame = measurementStartFrame;
			_pending.Clear();
		}

		public bool RecordPresentation(ulong presentationFrame, HarnessMetricSample sample)
		{
			if (presentationFrame <= _measurementStartFrame || sample == null || _pending.ContainsKey(presentationFrame)) return false;
			_pending.Add(presentationFrame, sample);
			return true;
		}

		public bool TryTakeCompletion(ulong timingFrame, out HarnessMetricSample sample)
		{
			sample = null;
			if (timingFrame <= _measurementStartFrame || !_pending.TryGetValue(timingFrame, out sample)) return false;
			_pending.Remove(timingFrame);
			return true;
		}

		public IReadOnlyList<HarnessMetricSample> DrainUncompleted()
		{
			var pending = _pending.OrderBy(pair => pair.Key).Select(pair => pair.Value).ToArray();
			_pending.Clear();
			return pending;
		}

		/// <summary>Preserves the original presentation evidence while making
		/// a missing delayed timing explicit. It must remain a NaN bad sample
		/// for that same Presented frame, never a synthetic Program miss.</summary>
		public static HarnessMetricSample MarkUnresolvedTimingUnavailable(HarnessMetricSample sample)
		{
			if (sample == null) throw new ArgumentNullException(nameof(sample));
			sample.cpuMilliseconds = double.NaN;
			sample.gpuMilliseconds = double.NaN;
			sample.programPresented = sample.programHealthy;
			return sample;
		}
	}

	/// <summary>Requires the public Application timing projection to be
	/// initialized before the fixed measurement interval begins. A warm-up
	/// frame number alone is insufficient because FrameTiming completion is
	/// asynchronous and its first completion cannot calculate FPS without a
	/// preceding presentation timestamp.</summary>
	public static class HarnessFrameTimingReadinessContract
	{
		public static bool IsReady(ulong gateStartPerformanceFrame, ulong presentationFrame, ulong performanceFrame,
			double framesPerSecond, double cpuMilliseconds, double gpuMilliseconds) =>
			presentationFrame > 0UL && performanceFrame > gateStartPerformanceFrame && performanceFrame <= presentationFrame &&
			IsPositiveFinite(framesPerSecond) && IsPositiveFinite(cpuMilliseconds) && IsPositiveFinite(gpuMilliseconds);

		private static bool IsPositiveFinite(double value) =>
			value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
	}

	/// <summary>Separates the fixed measurement window from asynchronous
	/// finalization. Releasing a final PresetTrigger or draining delayed
	/// FrameTiming must not extend 600 seconds of input, GC, or presentation
	/// collection, but artifact finalization waits for both to finish.</summary>
	public static class HarnessMeasurementBoundaryContract
	{
		public static bool ShouldCloseWindow(bool measurementWindowOpen, bool deadlineReached) => measurementWindowOpen && deadlineReached;
		public static bool AllowsMeasurementEvidence(bool measurementWindowOpen) => measurementWindowOpen;
		public static bool IsNewProgramPresentation(ulong lastRecordedFrame, ulong currentFrame) => currentFrame > lastRecordedFrame;
		public static bool AllowsInteractionInput(bool measurementWindowOpen) => measurementWindowOpen;
		public static bool ShouldStartPresetTrigger(bool measurementWindowOpen, bool presetVerificationActive,
			double measurementStart, double measurementSeconds, double hostTime, int completedPresetTriggers) =>
			measurementWindowOpen && !presetVerificationActive &&
			HarnessInteractionContract.DuePresetTriggerFiresAt(measurementStart, measurementSeconds, hostTime, completedPresetTriggers) > 0;
		public static bool CanFinalize(bool timingDrainCompleted, bool presetVerificationActive) => timingDrainCompleted && !presetVerificationActive;
	}

	public sealed class HarnessMetricAccumulator
	{
		private readonly List<HarnessMetricSample> _samples = new List<HarnessMetricSample>();
		private int _consecutiveMissing;
		private int _maxConsecutiveMissing;
		private int _faultedFrames;
		private int _fatalFrames;
		private int _holdingFrames;
		private bool _hasObservedProgramFrame;
		private ulong _lastObservedProgramFrame;
		private readonly Dictionary<string, IntervalState> _intervals = new Dictionary<string, IntervalState>(StringComparer.Ordinal);
		private readonly List<HarnessDiagnosticInterval> _completedIntervals = new List<HarnessDiagnosticInterval>();

		public IReadOnlyList<HarnessMetricSample> Samples => _samples;
		public IReadOnlyList<HarnessMetricSample> PresentedSamples => _samples.Where(x => x != null && x.programPresented).ToList();
		public int ConsecutiveMissing => _consecutiveMissing;
		public int MaxConsecutiveMissing => _maxConsecutiveMissing;
		public int FaultedFrames => _faultedFrames;
		public int FatalFrames => _fatalFrames;
		public int HoldingFrames => _holdingFrames;
		public int PresentedFrames => PresentedSamples.Count;
		/// <summary>Presented frames with finite positive CPU and GPU timing.
		/// Unavailable timings stay on their original presented frame and
		/// therefore remain in the quality denominator.</summary>
		public int TimingAvailableFrames => PresentedSamples.Count(HasUsableTiming);
		public int TimingUnavailableFrames => PresentedFrames - TimingAvailableFrames;
		public int GoodFrames => PresentedSamples.Count(x => x.programHealthy && HasUsableTiming(x) && x.cpuMilliseconds <= 16.67d && x.gpuMilliseconds <= 16.67d);
		public double GoodFrameRatio => PresentedFrames == 0 ? 0d : GoodFrames / (double)PresentedFrames;
		// CPU and GPU aggregates are independent. A malformed GPU result must
		// still retain a valid CPU observation (and vice versa); only the
		// paired availability count and good-frame ratio require both.
		public double AverageCpuMilliseconds => CpuTimingSamples().DefaultIfEmpty(double.NaN).Average();
		public double AverageGpuMilliseconds => GpuTimingSamples().DefaultIfEmpty(double.NaN).Average();
		public double MaxCpuMilliseconds => CpuTimingSamples().DefaultIfEmpty(double.NaN).Max();
		public double MaxGpuMilliseconds => GpuTimingSamples().DefaultIfEmpty(double.NaN).Max();
		public bool PoolBudgetExceeded => _samples.Any(x => x == null || x.poolBudgetBytes <= 0 || x.poolLeasedBytes + x.poolFreeBytes > x.poolBudgetBytes ||
			x.poolBudgetWarning || x.poolHighWaterBytes > x.poolBudgetBytes);
		public long PoolHighWaterBytes => _samples.Count == 0 ? 0 : _samples.Max(x => x?.poolHighWaterBytes ?? 0);
		public IReadOnlyList<HarnessDiagnosticInterval> Intervals => _completedIntervals;
		// Diagnostic only. The documented performance gate uses frame-time
		// ratio, consecutive missing frames, and the active 60fps descriptor;
		// this single-worst-interval value must not become an extra gate.
		public double MinimumProgramCadenceFps
		{
			get
			{
				// Cadence and missing-frame detection are Update observations:
				// a repeated/non-presented Program frame must remain visible to
				// the missing counter.  CPU/GPU aggregates use PresentedSamples
				// separately and must never use this Update-sample population.
				var ordered = _samples.Where(x => x != null && x.programFrameNumber > 0)
					.OrderBy(x => x.sampleSeconds).ToList();
				if (ordered.Count < 2) return double.NaN;
				var minimum = double.PositiveInfinity;
				var previous = ordered[0];
				for (var index = 1; index < ordered.Count; index++)
				{
					var current = ordered[index];
					if (current.programFrameNumber == previous.programFrameNumber) continue;
					if (current.programFrameNumber < previous.programFrameNumber) return 0d;
					var elapsed = current.sampleSeconds - previous.sampleSeconds;
					var frames = current.programFrameNumber - previous.programFrameNumber;
					if (elapsed <= 0d || frames == 0UL) return 0d;
					minimum = Math.Min(minimum, frames / elapsed);
					previous = current;
				}
				return double.IsPositiveInfinity(minimum) ? double.NaN : minimum;
			}
		}

		public void Reset()
		{
			_samples.Clear();
			_consecutiveMissing = 0;
			_maxConsecutiveMissing = 0;
			_faultedFrames = 0;
			_fatalFrames = 0;
			_holdingFrames = 0;
			_hasObservedProgramFrame = false;
			_lastObservedProgramFrame = 0;
			_intervals.Clear();
			_completedIntervals.Clear();
		}

		public void Add(HarnessMetricSample sample)
		{
			if (sample == null) throw new ArgumentNullException(nameof(sample));
			_samples.Add(sample);
			ObserveProgramFrame(sample);
			if (sample.faulted) _faultedFrames++;
			if (sample.fatal) _fatalFrames++;
			if (sample.holdingLastFrame) _holdingFrames++;
			UpdateInterval("Fault", sample.faulted, sample.sampleSeconds);
			UpdateInterval("Fatal", sample.fatal, sample.sampleSeconds);
			UpdateInterval("HoldingLastFrame", sample.holdingLastFrame, sample.sampleSeconds);
		}

		private IEnumerable<double> CpuTimingSamples() => PresentedSamples.Select(x => x.cpuMilliseconds).Where(IsPositiveFinite);

		private IEnumerable<double> GpuTimingSamples() => PresentedSamples.Select(x => x.gpuMilliseconds).Where(IsPositiveFinite);

		private static bool HasUsableTiming(HarnessMetricSample sample) => sample != null &&
			IsPositiveFinite(sample.cpuMilliseconds) && IsPositiveFinite(sample.gpuMilliseconds);

		private static bool IsPositiveFinite(double value) => value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);

		private void ObserveProgramFrame(HarnessMetricSample sample)
		{
			// Update can run more often than a Program presentation. Seeing
			// the same Program frame again is therefore not another missing
			// frame. Only a new frame number advances the missing sequence;
			// skipped frame numbers account for the unseen frames between two
			// Update observations.
			var frame = sample.programFrameNumber;
			if (frame == 0UL) return;
			if (!_hasObservedProgramFrame)
			{
				_hasObservedProgramFrame = true;
				_lastObservedProgramFrame = frame;
				if (!sample.programPresented || !sample.programHealthy) RecordMissing(1UL);
				return;
			}
			if (frame == _lastObservedProgramFrame) return;
			if (frame < _lastObservedProgramFrame) return;

			var skipped = frame - _lastObservedProgramFrame - 1UL;
			if (sample.programPresented && sample.programHealthy)
			{
				RecordMissing(skipped);
				_consecutiveMissing = 0;
			}
			else RecordMissing(skipped + 1UL);
			_lastObservedProgramFrame = frame;
		}

		private void RecordMissing(ulong count)
		{
			if (count == 0UL) return;
			var increment = count > int.MaxValue ? int.MaxValue : (int)count;
			_consecutiveMissing = _consecutiveMissing > int.MaxValue - increment
				? int.MaxValue : _consecutiveMissing + increment;
			_maxConsecutiveMissing = Math.Max(_maxConsecutiveMissing, _consecutiveMissing);
		}

		public void CompleteIntervals(double endSeconds)
		{
			foreach (var pair in _intervals.ToList())
			{
				if (!pair.Value.Active) continue;
				_completedIntervals.Add(new HarnessDiagnosticInterval
				{
					kind = pair.Key,
					startSeconds = pair.Value.StartSeconds,
					endSeconds = Math.Max(pair.Value.StartSeconds, endSeconds),
					durationSeconds = Math.Max(0d, endSeconds - pair.Value.StartSeconds),
					samples = pair.Value.Samples
				});
				pair.Value.Active = false;
			}
		}

		private void UpdateInterval(string kind, bool active, double seconds)
		{
			if (!_intervals.TryGetValue(kind, out var state)) _intervals[kind] = state = new IntervalState();
			var time = Math.Max(0d, seconds);
			if (active)
			{
				if (!state.Active) { state.Active = true; state.StartSeconds = time; state.Samples = 0; }
				state.Samples++;
			}
			else if (state.Active)
			{
				_completedIntervals.Add(new HarnessDiagnosticInterval
				{
					kind = kind,
					startSeconds = state.StartSeconds,
					endSeconds = time,
					durationSeconds = Math.Max(0d, time - state.StartSeconds),
					samples = state.Samples
				});
				state.Active = false;
			}
		}

		private sealed class IntervalState
		{
			public bool Active;
			public double StartSeconds;
			public int Samples;
		}
	}

	public sealed class HarnessEvaluationResult
	{
		public bool Passed { get; }
		public string Failure { get; }
		public HarnessMetricAccumulator Metrics { get; }

		private HarnessEvaluationResult(bool passed, string failure, HarnessMetricAccumulator metrics)
		{ Passed = passed; Failure = failure ?? string.Empty; Metrics = metrics; }

		public static HarnessEvaluationResult Pass(HarnessMetricAccumulator metrics) => new HarnessEvaluationResult(true, null, metrics);
		public static HarnessEvaluationResult Fail(HarnessMetricAccumulator metrics, string failure) => new HarnessEvaluationResult(false, failure, metrics);
	}

	public static class HarnessMetricEvaluator
	{
		public static bool IsPermittedProgramFormat(string format) =>
			string.Equals(format, "R16G16B16A16_SFloat", StringComparison.Ordinal) ||
			string.Equals(format, "R8G8B8A8_UNorm", StringComparison.Ordinal);

		public static HarnessEvaluationResult Evaluate(HarnessMetricAccumulator metrics, int programWidth, int programHeight, string programFormat, long currentPoolBytes, long poolBudgetBytes, int endLeases, int endSceneCount, int endLayerCount, int endBackendCount, int endNativeCount)
			=> EvaluateCore(metrics, programWidth, programHeight, programFormat, currentPoolBytes, poolBudgetBytes, endLeases, endSceneCount, endLayerCount, endBackendCount, endNativeCount);

		public static HarnessEvaluationResult Evaluate(HarnessMetricAccumulator metrics, int programWidth, int programHeight, string programFormat, long currentPoolBytes, long poolBudgetBytes, int endLeases, int endSceneCount, int endLayerCount, int endBackendCount, int endNativeCount,
			int logicalControlUpdates, int presetTriggerFires, double measurementSeconds)
		{
			var result = EvaluateCore(metrics, programWidth, programHeight, programFormat, currentPoolBytes, poolBudgetBytes,
				endLeases, endSceneCount, endLayerCount, endBackendCount, endNativeCount);
			if (!result.Passed) return result;
			var interactionFailure = HarnessInteractionContract.Validate(measurementSeconds, logicalControlUpdates, presetTriggerFires);
			return string.IsNullOrEmpty(interactionFailure) ? result : HarnessEvaluationResult.Fail(metrics, interactionFailure);
		}

		private static HarnessEvaluationResult EvaluateCore(HarnessMetricAccumulator metrics, int programWidth, int programHeight, string programFormat, long currentPoolBytes, long poolBudgetBytes, int endLeases, int endSceneCount, int endLayerCount, int endBackendCount, int endNativeCount)
		{
			if (metrics == null || metrics.Samples.Count == 0) return HarnessEvaluationResult.Fail(metrics, "No measured frames were recorded.");
			var presented = metrics.PresentedSamples;
			if (presented.Count == 0) return HarnessEvaluationResult.Fail(metrics, "No Presented Program frames were recorded.");
			if (programWidth != 1920 || programHeight != 1080) return HarnessEvaluationResult.Fail(metrics, "Program resolution was not 1920x1080 for the completed run.");
			if (!IsPermittedProgramFormat(programFormat))
				return HarnessEvaluationResult.Fail(metrics, "Program format is not a permitted linear HDR/LDR format.");
			foreach (var sample in metrics.Samples)
			{
				if (sample == null || sample.programWidth != 1920 || sample.programHeight != 1080 ||
					!IsPermittedProgramFormat(sample.programFormat) || sample.programTargetFramesPerSecond != 60)
					return HarnessEvaluationResult.Fail(metrics, "A measured Program frame had an invalid active descriptor or target cadence.");
				var previews = sample.previews ?? Array.Empty<HarnessPreviewMetric>();
				if (previews.Length != 2 || previews.Any(x => x == null || string.IsNullOrWhiteSpace(x.id) ||
					string.IsNullOrWhiteSpace(x.format) || !HarnessPreviewQualityContract.TryGetStage(x, out _)) ||
					previews.Select(x => x.id).Distinct(StringComparer.Ordinal).Count() != 2)
					return HarnessEvaluationResult.Fail(metrics, "A measured Preview frame did not expose two active descriptors at a valid quality stage.");
			}
			if (metrics.Samples.Any(x => x.programPresented && x.programFrameNumber == 0))
				return HarnessEvaluationResult.Fail(metrics, "A presented Program sample did not expose an active frame number.");
			if (metrics.MaxConsecutiveMissing >= 3) return HarnessEvaluationResult.Fail(metrics, "Program output had three or more consecutive missing/unhealthy frames.");
			// The minimum interval is retained as diagnostic evidence, but it
			// is not a pass/fail gate.  The specification gates Program
			// presentation by the 99% frame-time ratio, consecutive missing
			// frames, and the active descriptor's targetFramesPerSecond=60.
			// Rejecting a run because of one worst interval would add a
			// stricter requirement than the documented 10-minute criteria.
			if (metrics.GoodFrameRatio < 0.99d) return HarnessEvaluationResult.Fail(metrics, "Less than 99 percent of measured frames met the 16.67ms CPU/GPU budget.");
			if (metrics.FaultedFrames != 0 || metrics.FatalFrames != 0) return HarnessEvaluationResult.Fail(metrics, "Faulted or fatal diagnostics occurred during the measured interval.");
			if (poolBudgetBytes <= 0 || currentPoolBytes > poolBudgetBytes) return HarnessEvaluationResult.Fail(metrics, "Texture pool budget was exceeded.");
			if (metrics.PoolBudgetExceeded) return HarnessEvaluationResult.Fail(metrics, "Texture pool budget was exceeded during measurement.");
			if (endLeases != 0 || endSceneCount != 0 || endLayerCount != 0 || endBackendCount != 0 || endNativeCount != 0)
				return HarnessEvaluationResult.Fail(metrics, "Session-owned resources remained after teardown.");
			return HarnessEvaluationResult.Pass(metrics);
		}

	}

	/// <summary>
	/// Defines the only managed-allocation byte measurement accepted by the
	/// standalone performance harness.  Unity's documented
	/// <c>GC Allocated In Frame</c> Memory counter sums managed allocation
	/// bytes across Player threads. The one-sample recorder must wrap so its
	/// LastValue is replaced by each completed frame rather than repeatedly
	/// reporting the first frame's allocation. The harness rejects a recorder whose
	/// advertised unit is not bytes rather than treating a timing/count value
	/// as allocation data.
	/// </summary>
	public static class HarnessGcAllocationContract
	{
		public const string CounterName = "GC Allocated In Frame";
		public const int SampleCapacity = 1;
		public static ProfilerCategory CounterCategory => ProfilerCategory.Memory;
		public const ProfilerRecorderOptions MarkerOptions = ProfilerRecorderOptions.SumAllSamplesInFrame |
														   ProfilerRecorderOptions.WrapAroundWhenCapacityReached;

		public static bool IsAllThreadByteMeasurement(bool recorderIsValid, ProfilerMarkerDataUnit unitType) =>
			recorderIsValid && unitType == ProfilerMarkerDataUnit.Bytes;

		public static long AccumulateBytes(long total, long frameBytes)
		{
			var nonNegativeFrameBytes = Math.Max(0L, frameBytes);
			if (total >= long.MaxValue - nonNegativeFrameBytes) return long.MaxValue;
			return Math.Max(0L, total) + nonNegativeFrameBytes;
		}
	}

	/// <summary>
	/// Deterministic interaction schedule for the performance interval. The
	/// expected values are derived from the requested interval, so fixture
	/// runs use the same 120Hz/10-second contract without a hard-coded
	/// production-sized count.
	/// </summary>
	public static class HarnessInteractionContract
	{
		public const double LogicalControlUpdatesPerSecond = 120d;
		public const double PresetTriggerIntervalSeconds = 10d;

		// The performance graph's required 3D Generator has no parameters.
		// Exercise the existing VideoPlayer speed parameter instead of adding
		// an unrelated Shader Generator merely to host a color target.
		public static ParameterValue PerformanceTickSpeedMinimum => ParameterValue.FromFloat(0.5f);
		public static ParameterValue PerformanceTickSpeedMaximum => ParameterValue.FromFloat(1.5f);
		// The PresetTrigger is verified while the 120 Hz Value control can
		// legitimately hold its mapped maximum.  Keep the preset above that
		// maximum (but inside VideoPlayer's documented 0..4 hard range), so
		// Max(Base, LogicalControl) proves both Base and Effective through
		// one public snapshot instead of depending on input phase.
		public static ParameterValue PerformancePresetSpeedValue => ParameterValue.FromFloat(1.75f);

		public static ApplicationLogicalControlTargetRequest CreatePerformanceTickSpeedTarget(string nodeId) =>
			new ApplicationLogicalControlTargetRequest(nodeId, VideoPlayerContract.SpeedParameterId, PerformanceTickSpeedMinimum, PerformanceTickSpeedMaximum);

		public static string ValidatePerformanceTickSpeedTarget()
		{
			try
			{
				_ = new ParameterRange(PerformanceTickSpeedMinimum, PerformanceTickSpeedMaximum);
				return string.Empty;
			}
			catch (ArgumentException exception) { return exception.Message; }
		}

		public static int ExpectedLogicalControlUpdates(double measurementSeconds) =>
			ToCount(measurementSeconds * LogicalControlUpdatesPerSecond);

		/// <summary>
		/// Returns the number of logical updates whose absolute schedule has
		/// reached <paramref name="hostTime"/>. The host may cross the fixed
		/// measurement deadline between two Update calls, so elapsed time is
		/// clamped to the requested interval before converting it to the
		/// deterministic 120 Hz count. This admits the final partial host
		/// interval without ever scheduling an event beyond the deadline.
		/// </summary>
		public static int ExpectedLogicalControlUpdatesAt(double measurementStart, double measurementSeconds, double hostTime)
		{
			if (double.IsNaN(measurementStart) || double.IsInfinity(measurementStart) || double.IsNaN(hostTime)) return 0;
			var duration = Math.Max(0d, measurementSeconds);
			var elapsed = Math.Max(0d, hostTime - measurementStart);
			return ExpectedLogicalControlUpdates(Math.Min(elapsed, duration));
		}

		/// <summary>
		/// Computes only the not-yet-dispatched portion of the absolute
		/// interaction schedule. The result stays constant after the fixed
		/// measurement boundary, which makes a host frame that crosses the
		/// boundary deterministic.
		/// </summary>
		public static int DueLogicalControlUpdates(double measurementStart, double measurementSeconds, double hostTime, int dispatchedUpdates)
		{
			var expected = ExpectedLogicalControlUpdatesAt(measurementStart, measurementSeconds, hostTime);
			return Math.Max(0, expected - Math.Max(0, dispatchedUpdates));
		}

		public static int ExpectedPresetTriggerFires(double measurementSeconds) =>
			ToCount(measurementSeconds / PresetTriggerIntervalSeconds);

		/// <summary>
		/// Returns the number of preset slots whose absolute schedule has
		/// reached <paramref name="hostTime"/>. The result is clamped to the
		/// fixed measurement interval so a host frame after the deadline can
		/// never create a 61st slot for a 600-second run.
		/// </summary>
		public static int ExpectedPresetTriggerFiresAt(double measurementStart, double measurementSeconds, double hostTime)
		{
			if (double.IsNaN(measurementStart) || double.IsInfinity(measurementStart) ||
				double.IsNaN(hostTime) || double.IsInfinity(hostTime)) return 0;
			var duration = Math.Max(0d, measurementSeconds);
			var elapsed = Math.Max(0d, hostTime - measurementStart);
			return ExpectedPresetTriggerFires(Math.Min(elapsed, duration));
		}

		public static int DuePresetTriggerFiresAt(double measurementStart, double measurementSeconds, double hostTime,
			int completedPresetTriggers) =>
			Math.Max(0, ExpectedPresetTriggerFiresAt(measurementStart, measurementSeconds, hostTime) - Math.Max(0, completedPresetTriggers));

		public static string Validate(double measurementSeconds, int logicalControlUpdates, int presetTriggerFires)
		{
			if (double.IsNaN(measurementSeconds) || double.IsInfinity(measurementSeconds) || measurementSeconds < 0d)
				return "Measured interaction duration is invalid.";
			var expectedControls = ExpectedLogicalControlUpdates(measurementSeconds);
			var expectedPresets = ExpectedPresetTriggerFires(measurementSeconds);
			if (logicalControlUpdates < expectedControls)
				return "Logical control updates were below the expected count: " + logicalControlUpdates + "/" + expectedControls + ".";
			if (presetTriggerFires < expectedPresets)
				return "PresetTrigger fires were below the expected count: " + presetTriggerFires + "/" + expectedPresets + ".";
			return string.Empty;
		}

		private static int ToCount(double value)
		{
			if (double.IsNaN(value) || value <= 0d) return 0;
			if (double.IsInfinity(value) || value >= int.MaxValue) return int.MaxValue;
			return (int)Math.Floor(value + 1e-9d);
		}
	}

	/// <summary>
	/// Owns the deterministic logical-control schedule for one fixed
	/// measurement interval. DispatchDue is intentionally called before the
	/// host closes the measurement window: if a host frame crosses the
	/// deadline, it dispatches the final due slots (clamped to the deadline)
	/// and a subsequent call after Close is a no-op.
	/// </summary>
	public sealed class HarnessInteractionScheduler
	{
		private readonly double _measurementStart;
		private readonly double _measurementSeconds;
		private int _dispatchedUpdates;
		private bool _open = true;

		public HarnessInteractionScheduler(double measurementStart, double measurementSeconds)
		{
			_measurementStart = measurementStart;
			_measurementSeconds = Math.Max(0d, measurementSeconds);
		}

		public bool IsOpen => _open;
		public int DispatchedUpdates => _dispatchedUpdates;

		public int DispatchDue(double hostTime, Func<bool> dispatch)
		{
			if (!_open || dispatch == null) return 0;
			var due = HarnessInteractionContract.DueLogicalControlUpdates(
				_measurementStart, _measurementSeconds, hostTime, _dispatchedUpdates);
			var dispatched = 0;
			while (dispatched < due)
			{
				_dispatchedUpdates++;
				dispatched++;
				if (!dispatch()) break;
			}
			return dispatched;
		}

		public bool CloseIfDue(double hostTime)
		{
			var deadline = _measurementStart + _measurementSeconds;
			if (!_open || double.IsNaN(hostTime) || hostTime < deadline) return false;
			_open = false;
			return true;
		}

		public void Close() { _open = false; }
	}

	[Serializable]
	public sealed class HarnessInteractionArtifact
	{
		public double logicalControlUpdatesPerSecond;
		public double presetTriggerIntervalSeconds;
		public double measurementSeconds;
		public int logicalControlUpdates;
		public int expectedLogicalControlUpdates;
		public int presetTriggerFires;
		public int expectedPresetTriggerFires;
	}

	/// <summary>
	/// Public-read-model observation contract for a preset trigger.  A
	/// keyboard command being Accepted is only the enqueue acknowledgement;
	/// the BaseValue/EffectiveValue update and persisted trigger binding must
	/// prove that the preset was actually applied on a later frame. A
	/// PresetTrigger deliberately has no persistent Value read-model entry.
	/// </summary>
	public static class HarnessPresetApplicationContract
	{
		public static string ValidateObservation(string actualBaseValue, string actualEffectiveValue, string expectedPresetValue,
			string actualPresetId, string expectedPresetId, bool presetIsBroken, bool mappingPresent)
		{
			if (string.IsNullOrEmpty(expectedPresetValue) || !string.Equals(actualBaseValue, expectedPresetValue, StringComparison.Ordinal))
				return "Preset base value was not observed after the trigger.";
			if (!string.Equals(actualEffectiveValue, expectedPresetValue, StringComparison.Ordinal))
				return "Preset effective value was not observed after the trigger.";
			if (string.IsNullOrEmpty(expectedPresetId) || !string.Equals(actualPresetId, expectedPresetId, StringComparison.Ordinal))
				return "Preset trigger binding was not observed in the public project read model.";
			if (presetIsBroken) return "Preset trigger binding is broken in the public project read model.";
			if (!mappingPresent) return "Preset trigger mapping was not observed in the public project read model.";
			return string.Empty;
		}
	}

	[Serializable]
	public sealed class HarnessDiagnosticsExportArtifact
	{
		public bool attempted;
		public bool textWritten;
		public bool jsonWritten;
		public string textPath;
		public string jsonPath;
		public string failure;

		public static HarnessDiagnosticsExportArtifact NotAttempted(string reason)
		{
			return new HarnessDiagnosticsExportArtifact
			{
				attempted = false,
				textWritten = false,
				jsonWritten = false,
				textPath = string.Empty,
				jsonPath = string.Empty,
				failure = reason ?? string.Empty
			};
		}
	}

	public static class HarnessDiagnosticsExportContract
	{
		public static bool RequiredForStatus(string status) =>
			!string.Equals(status, HarnessRunStatus.Passed.ToString(), StringComparison.Ordinal);

		public static HarnessDiagnosticsExportArtifact AttachCandidate(string status, HarnessDiagnosticsExportArtifact candidate)
		{
			return candidate ?? HarnessDiagnosticsExportArtifact.NotAttempted(
				"Public diagnostics export candidate was unavailable before composition teardown.");
		}

		/// <summary>
		/// Export persistence is auxiliary evidence.  Its failure must never
		/// replace the measured/teardown failure, and on a Passed run it must
		/// not create a new gate merely because the optional candidate write
		/// failed.
		/// </summary>
		public static string PreserveOriginalFailure(string originalFailure, string status, string exportFailure) =>
			originalFailure ?? string.Empty;
	}

	[Serializable]
	public sealed class HarnessFailureCaptureArtifact
	{
		public bool attempted;
		public bool screenshotWritten;
		public bool programReadbackAvailable;
		public string screenshotPath;
		public string readbackReason;

		public static HarnessFailureCaptureArtifact PublicProgramReadbackUnavailable()
		{
			return new HarnessFailureCaptureArtifact
			{
				attempted = false,
				screenshotWritten = false,
				programReadbackAvailable = false,
				screenshotPath = string.Empty,
				readbackReason = "Program texture/readback is not exposed by the public Application or Ownership Snapshot API; internal Runtime access is forbidden."
			};
		}
	}

	[Serializable]
	public sealed class HarnessFrameTimingSourceArtifact
	{
		public int rawCount;
		public double rawIdentity;
		/// <summary>Legacy alias retained for artifact readers. It mirrors
		/// rawCpuFrameTimeMilliseconds, which is Unity's wait-inclusive total.</summary>
		public double rawCpuMilliseconds;
		public double rawCpuFrameTimeMilliseconds;
		public double rawCpuMainThreadFrameTimeMilliseconds;
		public double rawCpuRenderThreadFrameTimeMilliseconds;
		public double rawCpuMainThreadPresentWaitMilliseconds;
		public double rawGpuMilliseconds;
		public int pendingBefore;
		public int pendingAfter;
		public string outcome;
		public string candidateOutcome;
		public ulong performanceFrameNumber;
		public string exceptionType;
	}

	[Serializable]
	public sealed class HarnessTimingArtifact
	{
		public int updateSamples;
		public int measuredFrames;
		public int presentedFrames;
		public int timingAvailableFrames;
		public int timingUnavailableFrames;
		public double goodFrameRatio;
		public double averageCpuMilliseconds;
		public double averageGpuMilliseconds;
		public double maxCpuMilliseconds;
		public double maxGpuMilliseconds;
		public double minimumProgramCadenceFps;
		public int maxConsecutiveProgramMissing;
		public long gcAllocatedBytes;
		public int gcCollectionCount0;
		public int gcCollectionCount1;
		public int gcCollectionCount2;
		public HarnessDiagnosticInterval[] diagnosticIntervals;
		public HarnessPreviewQualitySample[] previewQualitySamples;
		public ulong frameTimingGateStartPerformanceFrame;
		public ulong frameTimingGateReadyPerformanceFrame;
		public double frameTimingGateWaitSeconds;
		public HarnessFrameTimingSourceArtifact frameTimingSource;
	}

	[Serializable]
	public sealed class HarnessOutputArtifact
	{
		public int programWidth;
		public int programHeight;
		public string programFormat;
		public int programTargetFps;
		public string programState;
		public int previewCount;
		public int previewWidth;
		public int previewHeight;
		public int previewTargetFps;
		public HarnessPreviewMetric[] previews;
		public string[] previewQualities;
	}

	[Serializable]
	public sealed class HarnessResourceArtifact
	{
		public long poolBudgetBytes;
		public long poolCurrentBytes;
		public long poolLeasedBytes;
		public long poolFreeBytes;
		public long poolHighWaterBytes;
		public int sceneCount;
		public int layerCount;
		public int backendCount;
		public int nativeContextCount;
		public int activeOutputLeases;
		public int poolEntryCount;
		public int endLeases;
		public int endPoolEntryCount;
		public int endActiveOutputLeases;
		public int endSceneCount;
		public int endLayerCount;
		public int endBackendCount;
		public int endNativeContextCount;
	}

	[Serializable]
	public sealed class HarnessDiagnosticsArtifact
	{
		public int faultedFrames;
		public int fatalFrames;
		public int holdingLastFrameFrames;
		public string[] currentCodes;
		public string[] historyCodes;
		public HarnessDiagnosticInterval[] intervals;

		/// <summary>
		/// Creates the non-null diagnostics projection required by failure
		/// artifacts.  A failed read is represented by empty collections;
		/// the harness records the read exception in its failure/log path.
		/// </summary>
		public static HarnessDiagnosticsArtifact Empty()
		{
			return new HarnessDiagnosticsArtifact
			{
				currentCodes = Array.Empty<string>(),
				historyCodes = Array.Empty<string>(),
				intervals = Array.Empty<HarnessDiagnosticInterval>()
			};
		}
	}

	/// <summary>
	/// Public native-plugin probe projection.  The path is deliberately a
	/// public API route rather than a guessed DLL filesystem path: the
	/// production P/Invoke adapter owns the platform-specific loader.
	/// </summary>
	[Serializable]
	public sealed class HarnessNativePluginProbeArtifact
	{
		public string path;
		public bool supportedPlatform;
		public bool passed;
		public uint abiVersion;
		public uint capabilities;
		public string diagnosticCode;
		public string diagnostic;
	}

	/// <summary>
	/// Public codec capability-probe projection.  This records the result of
	/// the same content probe used by the production media import boundary;
	/// no Runtime/backend implementation state is copied here.
	/// </summary>
	[Serializable]
	public sealed class HarnessCodecProbeArtifact
	{
		public string path;
		public bool passed;
		public bool supported;
		public string backend;
		public string container;
		public string codec;
		public bool hasAlpha;
		public bool hasAudio;
		public double durationSeconds;
		public string diagnostic;
	}

	[Serializable]
	public sealed class HarnessOwnershipSurfaceArtifact
	{
		public string id;
		public string targetKind;
		public int width;
		public int height;
		public string graphicsFormat;
		public int targetFramesPerSecond;
		public ulong frameNumber;
	}

	[Serializable]
	public sealed class HarnessOwnershipEntryArtifact
	{
		public ulong leaseId;
		public int width;
		public int height;
		public string graphicsFormat;
		public string depthStencilFormat;
		public int msaaSamples;
		public bool mipMap;
		public bool randomWrite;
		public string dimension;
		public int volumeDepth;
		public bool sRgb;
		public long estimatedBytes;
		public string state;
		public string sessionId;
		public string ownerKind;
		public string ownerId;
		public ulong generationId;
		public string slotId;
		public string role;
		public ulong lastUsedFrame;
		public ulong lastReturnedFrame;
	}

	[Serializable]
	public sealed class HarnessOwnershipTexturePoolArtifact
	{
		public long budgetBytes;
		public long leasedBytes;
		public long freeBytes;
		public long highWaterBytes;
		public bool budgetWarningActive;
		public double usageRatio;
		public HarnessOwnershipEntryArtifact[] entries;
	}

	/// <summary>
	/// Serializable projection of the public production ownership snapshot.
	/// It deliberately contains no Runtime, pool handle, or Unity object.
	/// </summary>
	[Serializable]
	public sealed class HarnessOwnershipSnapshotArtifact
	{
		public bool available;
		public bool runtimeDisposed;
		public int sceneCount;
		public int layerCount;
		public int backendCount;
		public int nativeContextCount;
		public int activeOutputLeaseCount;
		public HarnessOwnershipSurfaceArtifact program;
		public HarnessOwnershipSurfaceArtifact[] previews;
		public HarnessOwnershipTexturePoolArtifact texturePool;

		public static HarnessOwnershipSnapshotArtifact From(CompositionOwnershipSnapshot snapshot)
		{
			if (snapshot == null) return new HarnessOwnershipSnapshotArtifact { available = false, previews = Array.Empty<HarnessOwnershipSurfaceArtifact>() };
			return new HarnessOwnershipSnapshotArtifact
			{
				available = true,
				runtimeDisposed = snapshot.RuntimeDisposed,
				sceneCount = snapshot.SceneCount,
				layerCount = snapshot.LayerCount,
				backendCount = snapshot.BackendCount,
				nativeContextCount = snapshot.NativeContextCount,
				activeOutputLeaseCount = snapshot.ActiveOutputLeaseCount,
				program = ToSurface(snapshot.Program),
				previews = (snapshot.Previews ?? Array.Empty<SurfaceOwnershipSnapshot>()).Select(ToSurface).ToArray(),
				texturePool = ToTexturePool(snapshot.TexturePool)
			};
		}

		private static HarnessOwnershipSurfaceArtifact ToSurface(SurfaceOwnershipSnapshot surface)
		{
			if (surface == null) return null;
			return new HarnessOwnershipSurfaceArtifact
			{
				id = surface.Id,
				targetKind = surface.TargetKind,
				width = surface.Width,
				height = surface.Height,
				graphicsFormat = surface.GraphicsFormat,
				targetFramesPerSecond = surface.TargetFramesPerSecond,
				frameNumber = surface.FrameNumber
			};
		}

		private static HarnessOwnershipTexturePoolArtifact ToTexturePool(OwnershipSnapshot pool)
		{
			if (pool == null) return null;
			return new HarnessOwnershipTexturePoolArtifact
			{
				budgetBytes = pool.BudgetBytes,
				leasedBytes = pool.LeasedBytes,
				freeBytes = pool.FreeBytes,
				highWaterBytes = pool.HighWaterBytes,
				budgetWarningActive = pool.BudgetWarningActive,
				usageRatio = pool.UsageRatio,
				entries = (pool.Entries ?? Array.Empty<OwnershipSnapshotEntry>()).Select(entry => new HarnessOwnershipEntryArtifact
				{
					leaseId = entry.LeaseId.Value,
					width = entry.Descriptor.Width,
					height = entry.Descriptor.Height,
					graphicsFormat = entry.Descriptor.GraphicsFormat.ToString(),
					depthStencilFormat = entry.Descriptor.DepthStencilFormat.ToString(),
					msaaSamples = entry.Descriptor.MsaaSamples,
					mipMap = entry.Descriptor.MipMap,
					randomWrite = entry.Descriptor.RandomWrite,
					dimension = entry.Descriptor.Dimension.ToString(),
					volumeDepth = entry.Descriptor.VolumeDepth,
					sRgb = entry.Descriptor.SRgb,
					estimatedBytes = entry.EstimatedBytes,
					state = entry.State.ToString(),
					sessionId = entry.Owner.SessionId,
					ownerKind = entry.Owner.OwnerKind.ToString(),
					ownerId = entry.Owner.OwnerId,
					generationId = entry.Owner.GenerationId,
					slotId = entry.Owner.SlotId,
					role = entry.Owner.Role.ToString(),
					lastUsedFrame = entry.LastUsedFrame,
					lastReturnedFrame = entry.LastReturnedFrame
				}).ToArray()
			};
		}
	}

	[Serializable]
	public sealed class HarnessAcceptanceFixtureArtifact
	{
		public string codec;
		public string file;
		public bool probePassed;
		public bool prepareObserved;
		public ulong frameBefore;
		public ulong frameAfter;
		public ulong previewFrameBefore;
		public ulong previewFrameAfter;
		public ulong preview1FrameBefore;
		public ulong preview1FrameAfter;
		public ulong preview2FrameBefore;
		public ulong preview2FrameAfter;
		public bool mediaBindingApplied;
		public string mediaAssetId;
		public bool ownershipFramesObserved;
		public bool outputReadyObserved;
		public bool realFrameObserved;
		public bool frameReady;
		public bool nativeProbeRequired;
		public bool nativeProbePassed;
		public string error;
	}

	[Serializable]
	public sealed class HarnessAcceptancePersistenceArtifact
	{
		public string projectRoot;
		public bool saved;
		public bool reopened;
		public bool recovered;
		public bool dirtyAfterRecovery;
		public bool mainFilePreservedAfterRecovery;
		public bool backupReadable;
		public string backupFingerprint;
		public string expectedBackupFingerprint;
		public string backupFingerprintComponents;
		public string expectedBackupFingerprintComponents;
		public string fingerprint;
		public string expectedFingerprint;
		public string fingerprintComponents;
		public string expectedFingerprintComponents;
	}

	[Serializable]
	public sealed class HarnessAcceptanceOutputSurfaceArtifact
	{
		public string id;
		public string state;
		public int width;
		public int height;
		public bool demanded;
		public string reason;
	}

	[Serializable]
	public sealed class HarnessAcceptanceOutputArtifact
	{
		public ulong frameNumber;
		public string programState;
		public int programWidth;
		public int programHeight;
		public string programReason;
		public HarnessAcceptanceOutputSurfaceArtifact[] previews;
	}

	[Serializable]
	public sealed class HarnessAcceptanceUiElementLayoutArtifact
	{
		public string name;
		public int count;
		public float x;
		public float y;
		public float width;
		public float height;
		public string flexDirection;
		public float flexGrow;
		public float flexShrink;
		public string flexBasis;
		public string display;
		public string pickingMode;
		public bool enabled;
	}

	[Serializable]
	public sealed class HarnessAcceptanceUiLayoutArtifact
	{
		public int screenWidth;
		public int screenHeight;
		public bool screenFullScreen;
		public float panelScale;
		public string panelScaleMode;
		public int panelReferenceWidth;
		public int panelReferenceHeight;
		public HarnessAcceptanceUiElementLayoutArtifact[] elements;
	}

	[Serializable]
	public sealed class HarnessAcceptanceUiSaveArtifact
	{
		public int callbackCount;
		public string focusedElement;
		public string bannerText;
		public bool bannerVisible;
		public string taskBeforeId;
		public string taskBeforeKind;
		public string taskBeforeStatus;
		public string taskAfterId;
		public string taskAfterKind;
		public string taskAfterStage;
		public string taskAfterStatus;
		public string taskAfterPath;
		public string taskAfterDiagnosticCode;
		public string taskAfterDiagnosticMessage;
		public string taskAfterExceptionType;
		public string taskAfterExceptionMessage;
		public string taskAfterExceptionStackTrace;
	}

	[Serializable]
	public sealed class HarnessAcceptanceArtifact
	{
		public string mode = "acceptance";
		public string stage;
		public string acceptanceContractVersion;
		public string graphicsApi;
		public string buildId;
		public string fixtureRoot;
		public bool editorAssemblyExcluded;
		public bool productionCompositionUsed;
		public bool productionCatalogUsed;
		public bool presentationRootAvailable;
		public bool programAndPreviewsReady;
		public bool requiredGraphObserved;
		public bool realFrameObserved;
		public bool valueControlUpdated;
		public bool valueControlRemapped;
		public bool presetTriggerFired;
		public bool logicalControlStateObserved;
		public bool mediaPortable;
		public string valueControlId;
		public string presetTriggerId;
		public string presetId;
		public string uiSavePickTarget;
		public HarnessAcceptanceUiSaveArtifact uiSave;
		public HarnessAcceptanceUiLayoutArtifact uiLayout;
		public string[] uiActions;
		public HarnessAcceptanceFixtureArtifact[] fixtures;
		public HarnessAcceptanceOutputArtifact lastOutput;
		public HarnessAcceptancePersistenceArtifact persistence;
		public bool fileProjectReadable;
		public bool fileProjectWritable;
		public bool backupFileReadable;
		public bool nativeProbePassed;
		public string nativeProbeDiagnostic;
		public string manualDisplayCheck = "manual-required";
		public string ownershipTeardown;
	}

	[Serializable]
	public sealed class HarnessArtifact
	{
		public string schemaVersion = "2";
		public string mode = "performance";
		public string stage;
		public string runId;
		public string status;
		public string failure;
		public string scenario;
		public string codec;
		public string corpusVersion;
		public string corpusFile;
		public string platform;
		public string operatingSystem;
		public string graphicsApi;
		public string graphicsDeviceName;
		public string graphicsDeviceVersion;
		public string unityVersion;
		public string packageVersion;
		public string buildId;
		public bool developmentBuild;
		public string buildOptions;
		public string projectRoot;
		public string projectRevision;
		public string seed;
		public bool fixtureMode;
		public bool productionCompositionUsed = true;
		public bool productionCatalogUsed = true;
		public string renderPipeline;
		public double warmupSeconds;
		public double measureSeconds;
		public bool canonicalScenarioSaved;
		public HarnessTimingArtifact timing;
		public HarnessInteractionArtifact interactions;
		public string[] operationSequence;
		public HarnessOutputArtifact output;
		public HarnessResourceArtifact resources;
		public HarnessOwnershipSnapshotArtifact ownership;
		public HarnessDiagnosticsArtifact diagnostics;
		public HarnessDiagnosticsExportArtifact diagnosticsExport;
		public HarnessFailureCaptureArtifact failureCapture;
		public HarnessNativePluginProbeArtifact nativePluginProbe;
		public HarnessCodecProbeArtifact codecProbe;
		public HarnessAcceptanceArtifact acceptance;
		public string artifactWriteError;
	}

	public readonly struct ArtifactWriteResult
	{
		public bool Success { get; }
		public string JsonPath { get; }
		public string TextPath { get; }
		public string Error { get; }
		public ArtifactWriteResult(bool success, string jsonPath, string textPath, string error = null)
		{ Success = success; JsonPath = jsonPath ?? string.Empty; TextPath = textPath ?? string.Empty; Error = error ?? string.Empty; }
	}

	/// <summary>
	/// One-shot cleanup guard used by finalization paths. Once cleanup has
	/// been attempted, a later finally block cannot invoke a non-idempotent
	/// owner a second time, even when the first attempt threw.
	/// </summary>
	public sealed class HarnessFinalizationGuard
	{
		private bool _attempted;

		public bool Attempted => _attempted;

		public bool Try(Action cleanup, Action<Exception> onFailure = null)
		{
			if (_attempted) return false;
			_attempted = true;
			try { cleanup?.Invoke(); }
			catch (Exception exception)
			{
				onFailure?.Invoke(exception);
			}
			return true;
		}
	}

	[Serializable]
	public sealed class HarnessFinalizationDecision
	{
		public string status;
		public string failure;
		public int exitCode;
		public bool quitAttempted;
	}

	/// <summary>
	/// Pure final-result contract for cleanup/write ordering tests. A cleanup
	/// failure becomes a failure only when no earlier failure exists; the
	/// original failure is never replaced. Quit is attempted with the same
	/// exit code that the artifact contract computes.
	/// </summary>
	public static class HarnessFinalizationContract
	{
		/// <summary>
		/// A harness-owned Standalone Player must terminate after its artifact
		/// is committed whether or not it was launched in batch mode. Editor
		/// PlayMode exercises the same component, but must never close the
		/// Editor process.
		/// </summary>
		public static bool ShouldQuitPlayer(bool shouldQuit, bool isEditor) => shouldQuit && !isEditor;

		public static HarnessFinalizationDecision Decide(string originalFailure, string candidateStatus, bool artifactWriteSucceeded,
			string teardownFailure, Action<int> quit)
		{
			var failure = string.IsNullOrEmpty(originalFailure) ? (teardownFailure ?? string.Empty) : originalFailure;
			var status = string.IsNullOrEmpty(failure)
				? (candidateStatus ?? HarnessRunStatus.Failed.ToString())
				: IsEnvironmentFailure(failure) ? HarnessRunStatus.EnvironmentFailed.ToString() : HarnessRunStatus.Failed.ToString();
			var artifact = new HarnessArtifact { status = status, failure = failure };
			var write = new ArtifactWriteResult(artifactWriteSucceeded, null, null,
				artifactWriteSucceeded ? null : "Artifact write failed.");
			var exitCode = HarnessArtifactWriter.GetExitCode(artifact, write);
			var quitAttempted = quit != null;
			try { quit?.Invoke(exitCode); }
			catch
			{
				// The process result remains authoritative if the host quit
				// callback itself throws; callers still attempted Quit.
			}
			return new HarnessFinalizationDecision { status = status, failure = failure, exitCode = exitCode, quitAttempted = quitAttempted };
		}

		private static bool IsEnvironmentFailure(string failure) =>
			!string.IsNullOrEmpty(failure) && failure.StartsWith("ENVIRONMENT:", StringComparison.Ordinal);
	}

	public static class HarnessArtifactWriter
	{
		public static ArtifactWriteResult Write(string directory, HarnessArtifact artifact)
		{
			if (artifact == null) return new ArtifactWriteResult(false, null, null, "Artifact is null.");
			string jsonPath = null;
			string textPath = null;
			string jsonTemp = null;
			string textTemp = null;
			string jsonBackup = null;
			string textBackup = null;
			var jsonBackupCreated = false;
			var textBackupCreated = false;
			var jsonCommitted = false;
			var textCommitted = false;
			try
			{
				if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("Artifact directory is required.", nameof(directory));
				Directory.CreateDirectory(directory);
				var baseName = string.IsNullOrWhiteSpace(artifact.runId) ? "standalone-harness" : artifact.runId;
				jsonPath = Path.Combine(directory, baseName + ".json");
				textPath = Path.Combine(directory, baseName + ".txt");
				var json = JsonUtility.ToJson(artifact, true);
				var text = BuildText(artifact);

				// Stage both files before changing either destination. The
				// final pair is committed together as far as the filesystem
				// permits, and any partial commit is rolled back below.
				var transactionId = Guid.NewGuid().ToString("N");
				jsonTemp = jsonPath + ".tmp-" + transactionId + "-json";
				textTemp = textPath + ".tmp-" + transactionId + "-text";
				File.WriteAllText(jsonTemp, json ?? string.Empty, new UTF8Encoding(false));
				File.WriteAllText(textTemp, text ?? string.Empty, new UTF8Encoding(false));

				jsonBackup = jsonPath + ".bak-" + transactionId;
				textBackup = textPath + ".bak-" + transactionId;
				if (File.Exists(jsonPath))
				{
					File.Move(jsonPath, jsonBackup);
					jsonBackupCreated = true;
				}
				File.Move(jsonTemp, jsonPath);
				jsonCommitted = true;

				if (File.Exists(textPath))
				{
					File.Move(textPath, textBackup);
					textBackupCreated = true;
				}
				File.Move(textTemp, textPath);
				textCommitted = true;

				return new ArtifactWriteResult(true, jsonPath, textPath);
			}
			catch (Exception exception)
			{
				Rollback(jsonPath, jsonBackup, jsonBackupCreated, jsonCommitted);
				Rollback(textPath, textBackup, textBackupCreated, textCommitted);
				return new ArtifactWriteResult(false, null, null, exception.ToString());
			}
			finally
			{
				DeleteIfPresent(jsonTemp);
				DeleteIfPresent(textTemp);
				DeleteIfPresent(jsonBackup);
				DeleteIfPresent(textBackup);
			}
		}

		/// <summary>
		/// Artifact persistence is part of the harness result contract. A
		/// write failure is always a process failure, even when the measured
		/// run itself was otherwise Passed. The artifact's own status and
		/// failure fields remain untouched so the original test outcome is
		/// never replaced by the persistence diagnostic.
		/// </summary>
		public static int GetExitCode(HarnessArtifact artifact, ArtifactWriteResult write)
		{
			if (!write.Success || artifact == null) return 1;
			if (artifact.status == HarnessRunStatus.Passed.ToString()) return 0;
			if (artifact.status == HarnessRunStatus.EnvironmentFailed.ToString()) return 2;
			return 1;
		}

		private static void Rollback(string path, string backup, bool backupCreated, bool committed)
		{
			if (string.IsNullOrWhiteSpace(path)) return;
			try
			{
				if (committed && File.Exists(path)) File.Delete(path);
				if (backupCreated && File.Exists(backup))
				{
					if (File.Exists(path)) File.Delete(path);
					File.Move(backup, path);
				}
			}
			catch
			{
				// Preserve the original write exception. The failed result
				// and process exit code remain authoritative diagnostics.
			}
		}

		private static void DeleteIfPresent(string path)
		{
			try
			{
				if (!string.IsNullOrWhiteSpace(path) && File.Exists(path)) File.Delete(path);
			}
			catch
			{
				// Cleanup must not replace the original write result. A
				// stale transaction file is safer than a false success or a
				// lost diagnostic.
			}
		}

		private static string Inline(string value) => (value ?? string.Empty).Replace("\r", "\\r").Replace("\n", "\\n");

		private static string BuildText(HarnessArtifact artifact)
		{
			var b = new StringBuilder();
			b.AppendLine("ShitDesigner Standalone Player Harness");
			b.AppendLine("status=" + (artifact.status ?? string.Empty));
			b.AppendLine("mode=" + (artifact.mode ?? string.Empty));
			b.AppendLine("stage=" + (artifact.stage ?? string.Empty));
			b.AppendLine("failure=" + (artifact.failure ?? string.Empty));
			b.AppendLine("scenario=" + (artifact.scenario ?? string.Empty));
			b.AppendLine("operationSequence=" + string.Join(";", artifact.operationSequence ?? Array.Empty<string>()));
			b.AppendLine("codec=" + (artifact.codec ?? string.Empty));
			b.AppendLine("corpusVersion=" + (artifact.corpusVersion ?? string.Empty));
			b.AppendLine("platform=" + (artifact.platform ?? string.Empty));
			b.AppendLine("operatingSystem=" + (artifact.operatingSystem ?? string.Empty));
			b.AppendLine("graphicsApi=" + (artifact.graphicsApi ?? string.Empty));
			b.AppendLine("graphicsDeviceName=" + (artifact.graphicsDeviceName ?? string.Empty));
			b.AppendLine("graphicsDeviceVersion=" + (artifact.graphicsDeviceVersion ?? string.Empty));
			b.AppendLine("unityVersion=" + (artifact.unityVersion ?? string.Empty));
			b.AppendLine("packageVersion=" + (artifact.packageVersion ?? string.Empty));
			b.AppendLine("buildId=" + (artifact.buildId ?? string.Empty));
			b.AppendLine("developmentBuild=" + artifact.developmentBuild);
			b.AppendLine("buildOptions=" + (artifact.buildOptions ?? string.Empty));
			b.AppendLine("projectRoot=" + (artifact.projectRoot ?? string.Empty));
			b.AppendLine("projectRevision=" + (artifact.projectRevision ?? string.Empty));
			b.AppendLine("canonicalScenarioSaved=" + artifact.canonicalScenarioSaved);
			if (artifact.nativePluginProbe != null)
			{
				b.AppendLine("nativePluginPath=" + (artifact.nativePluginProbe.path ?? string.Empty));
				b.AppendLine("nativePluginSupportedPlatform=" + artifact.nativePluginProbe.supportedPlatform);
				b.AppendLine("nativePluginProbePassed=" + artifact.nativePluginProbe.passed);
				b.AppendLine("nativePluginAbiVersion=" + artifact.nativePluginProbe.abiVersion);
				b.AppendLine("nativePluginCapabilities=" + artifact.nativePluginProbe.capabilities);
				b.AppendLine("nativePluginDiagnosticCode=" + (artifact.nativePluginProbe.diagnosticCode ?? string.Empty));
				b.AppendLine("nativePluginDiagnostic=" + (artifact.nativePluginProbe.diagnostic ?? string.Empty));
			}
			if (artifact.codecProbe != null)
			{
				b.AppendLine("codecProbePath=" + (artifact.codecProbe.path ?? string.Empty));
				b.AppendLine("codecProbePassed=" + artifact.codecProbe.passed);
				b.AppendLine("codecProbeSupported=" + artifact.codecProbe.supported);
				b.AppendLine("codecProbeBackend=" + (artifact.codecProbe.backend ?? string.Empty));
				b.AppendLine("codecProbeContainer=" + (artifact.codecProbe.container ?? string.Empty));
				b.AppendLine("codecProbeCodec=" + (artifact.codecProbe.codec ?? string.Empty));
				b.AppendLine("codecProbeHasAlpha=" + artifact.codecProbe.hasAlpha);
				b.AppendLine("codecProbeHasAudio=" + artifact.codecProbe.hasAudio);
				b.AppendLine("codecProbeDurationSeconds=" + artifact.codecProbe.durationSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
				b.AppendLine("codecProbeDiagnostic=" + (artifact.codecProbe.diagnostic ?? string.Empty));
			}
			b.AppendLine("productionCompositionUsed=" + artifact.productionCompositionUsed);
			b.AppendLine("productionCatalogUsed=" + artifact.productionCatalogUsed);
			b.AppendLine("renderPipeline=" + (artifact.renderPipeline ?? string.Empty));
			if (artifact.timing != null)
			{
				b.AppendLine("updateSamples=" + artifact.timing.updateSamples);
				b.AppendLine("measuredFrames=" + artifact.timing.measuredFrames);
				b.AppendLine("presentedFrames=" + artifact.timing.presentedFrames);
				b.AppendLine("timingAvailableFrames=" + artifact.timing.timingAvailableFrames);
				b.AppendLine("timingUnavailableFrames=" + artifact.timing.timingUnavailableFrames);
				b.AppendLine("goodFrameRatio=" + artifact.timing.goodFrameRatio.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
				b.AppendLine("averageCpuMilliseconds=" + artifact.timing.averageCpuMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
				b.AppendLine("averageGpuMilliseconds=" + artifact.timing.averageGpuMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
				b.AppendLine("minimumProgramCadenceFps=" + artifact.timing.minimumProgramCadenceFps.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
				b.AppendLine("maxConsecutiveProgramMissing=" + artifact.timing.maxConsecutiveProgramMissing);
				b.AppendLine("gcAllocatedBytes=" + artifact.timing.gcAllocatedBytes);
				b.AppendLine("gcCollectionCount0=" + artifact.timing.gcCollectionCount0);
				b.AppendLine("gcCollectionCount1=" + artifact.timing.gcCollectionCount1);
				b.AppendLine("gcCollectionCount2=" + artifact.timing.gcCollectionCount2);
				b.AppendLine("frameTimingGateStartPerformanceFrame=" + artifact.timing.frameTimingGateStartPerformanceFrame);
				b.AppendLine("frameTimingGateReadyPerformanceFrame=" + artifact.timing.frameTimingGateReadyPerformanceFrame);
				b.AppendLine("frameTimingGateWaitSeconds=" + artifact.timing.frameTimingGateWaitSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
				var frameTiming = artifact.timing.frameTimingSource;
				if (frameTiming != null)
					b.AppendLine("frameTimingSource=outcome=" + Inline(frameTiming.outcome) + ";candidate=" + Inline(frameTiming.candidateOutcome) +
						";rawCount=" + frameTiming.rawCount + ";identity=" + frameTiming.rawIdentity.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
						";cpuFrameTime=" + frameTiming.rawCpuFrameTimeMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
						";cpuMainThread=" + frameTiming.rawCpuMainThreadFrameTimeMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
						";cpuRenderThread=" + frameTiming.rawCpuRenderThreadFrameTimeMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
						";cpuPresentWait=" + frameTiming.rawCpuMainThreadPresentWaitMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
						";gpu=" + frameTiming.rawGpuMilliseconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
						";pending=" + frameTiming.pendingBefore + "->" + frameTiming.pendingAfter + ";performanceFrame=" + frameTiming.performanceFrameNumber +
						";exception=" + Inline(frameTiming.exceptionType));
				b.AppendLine("previewQualitySamples=" + string.Join(";", (artifact.timing.previewQualitySamples ?? Array.Empty<HarnessPreviewQualitySample>()).Select(sample =>
					(sample == null ? string.Empty : sample.sampleSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "@" + sample.programFrameNumber + ":" +
						string.Join(",", (sample.previews ?? Array.Empty<HarnessPreviewMetric>()).Select(preview => preview == null ? string.Empty : preview.id + "=" + preview.quality + "/" + preview.qualityStage + "/" + preview.width + "x" + preview.height + "@" + preview.targetFramesPerSecond))))));
			}
			if (artifact.interactions != null)
			{
				b.AppendLine("logicalControlUpdatesPerSecond=" + artifact.interactions.logicalControlUpdatesPerSecond.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
				b.AppendLine("presetTriggerIntervalSeconds=" + artifact.interactions.presetTriggerIntervalSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
				b.AppendLine("interactionMeasurementSeconds=" + artifact.interactions.measurementSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
				b.AppendLine("logicalControlUpdates=" + artifact.interactions.logicalControlUpdates);
				b.AppendLine("expectedLogicalControlUpdates=" + artifact.interactions.expectedLogicalControlUpdates);
				b.AppendLine("presetTriggerFires=" + artifact.interactions.presetTriggerFires);
				b.AppendLine("expectedPresetTriggerFires=" + artifact.interactions.expectedPresetTriggerFires);
			}
			if (artifact.resources != null)
			{
				b.AppendLine("poolBudgetBytes=" + artifact.resources.poolBudgetBytes);
				b.AppendLine("poolCurrentBytes=" + artifact.resources.poolCurrentBytes);
				b.AppendLine("poolLeasedBytes=" + artifact.resources.poolLeasedBytes);
				b.AppendLine("poolFreeBytes=" + artifact.resources.poolFreeBytes);
				b.AppendLine("poolHighWaterBytes=" + artifact.resources.poolHighWaterBytes);
				b.AppendLine("activeOutputLeases=" + artifact.resources.activeOutputLeases);
				b.AppendLine("poolEntryCount=" + artifact.resources.poolEntryCount);
				b.AppendLine("endLeases=" + artifact.resources.endLeases);
				b.AppendLine("endPoolEntryCount=" + artifact.resources.endPoolEntryCount);
				b.AppendLine("endActiveOutputLeases=" + artifact.resources.endActiveOutputLeases);
				b.AppendLine("endSceneCount=" + artifact.resources.endSceneCount);
				b.AppendLine("endLayerCount=" + artifact.resources.endLayerCount);
				b.AppendLine("endBackendCount=" + artifact.resources.endBackendCount);
				b.AppendLine("endNativeContextCount=" + artifact.resources.endNativeContextCount);
			}
			if (artifact.output != null)
			{
				b.AppendLine("previewCount=" + artifact.output.previewCount);
				b.AppendLine("programWidth=" + artifact.output.programWidth);
				b.AppendLine("programHeight=" + artifact.output.programHeight);
				b.AppendLine("programFormat=" + (artifact.output.programFormat ?? string.Empty));
				b.AppendLine("programTargetFps=" + artifact.output.programTargetFps);
				b.AppendLine("programState=" + (artifact.output.programState ?? string.Empty));
				b.AppendLine("previewWidth=" + artifact.output.previewWidth);
				b.AppendLine("previewHeight=" + artifact.output.previewHeight);
				b.AppendLine("previewTargetFps=" + artifact.output.previewTargetFps);
				b.AppendLine("programTargetFps=" + artifact.output.programTargetFps);
				b.AppendLine("previewDescriptors=" + string.Join(";", (artifact.output.previews ?? Array.Empty<HarnessPreviewMetric>()).Select(x => x == null ? string.Empty : x.id + ":" + x.width + "x" + x.height + "@" + x.targetFramesPerSecond + "/" + x.format)));
				b.AppendLine("previewQualities=" + string.Join(",", artifact.output.previewQualities ?? Array.Empty<string>()));
			}
			if (artifact.ownership != null)
			{
				b.AppendLine("ownershipAvailable=" + artifact.ownership.available);
				b.AppendLine("ownershipRuntimeDisposed=" + artifact.ownership.runtimeDisposed);
				b.AppendLine("ownershipSceneCount=" + artifact.ownership.sceneCount);
				b.AppendLine("ownershipLayerCount=" + artifact.ownership.layerCount);
				b.AppendLine("ownershipBackendCount=" + artifact.ownership.backendCount);
				b.AppendLine("ownershipNativeContextCount=" + artifact.ownership.nativeContextCount);
				b.AppendLine("ownershipActiveOutputLeaseCount=" + artifact.ownership.activeOutputLeaseCount);
				b.AppendLine("ownershipPreviewCount=" + (artifact.ownership.previews ?? Array.Empty<HarnessOwnershipSurfaceArtifact>()).Length);
				if (artifact.ownership.texturePool != null)
				{
					b.AppendLine("ownershipPoolBudgetBytes=" + artifact.ownership.texturePool.budgetBytes);
					b.AppendLine("ownershipPoolLeasedBytes=" + artifact.ownership.texturePool.leasedBytes);
					b.AppendLine("ownershipPoolFreeBytes=" + artifact.ownership.texturePool.freeBytes);
					b.AppendLine("ownershipPoolHighWaterBytes=" + artifact.ownership.texturePool.highWaterBytes);
					b.AppendLine("ownershipPoolEntryCount=" + (artifact.ownership.texturePool.entries ?? Array.Empty<HarnessOwnershipEntryArtifact>()).Length);
				}
			}
			if (artifact.diagnostics?.intervals != null)
				b.AppendLine("diagnosticIntervals=" + string.Join(";", artifact.diagnostics.intervals.Select(x => x == null ? string.Empty : x.kind + ":" + x.startSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "-" + x.endSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + "/" + x.durationSeconds.ToString("R", System.Globalization.CultureInfo.InvariantCulture))));
			if (artifact.diagnostics != null)
			{
				b.AppendLine("diagnosticCurrentCodes=" + string.Join(",", artifact.diagnostics.currentCodes ?? Array.Empty<string>()));
				b.AppendLine("diagnosticHistoryCodes=" + string.Join(",", artifact.diagnostics.historyCodes ?? Array.Empty<string>()));
				b.AppendLine("diagnosticFaultedFrames=" + artifact.diagnostics.faultedFrames);
				b.AppendLine("diagnosticFatalFrames=" + artifact.diagnostics.fatalFrames);
				b.AppendLine("diagnosticHoldingLastFrameFrames=" + artifact.diagnostics.holdingLastFrameFrames);
			}
			if (artifact.diagnosticsExport != null)
			{
				b.AppendLine("diagnosticsExportAttempted=" + artifact.diagnosticsExport.attempted);
				b.AppendLine("diagnosticsExportTextWritten=" + artifact.diagnosticsExport.textWritten);
				b.AppendLine("diagnosticsExportJsonWritten=" + artifact.diagnosticsExport.jsonWritten);
				b.AppendLine("diagnosticsExportTextPath=" + (artifact.diagnosticsExport.textPath ?? string.Empty));
				b.AppendLine("diagnosticsExportJsonPath=" + (artifact.diagnosticsExport.jsonPath ?? string.Empty));
				b.AppendLine("diagnosticsExportFailure=" + (artifact.diagnosticsExport.failure ?? string.Empty));
			}
			if (artifact.failureCapture != null)
			{
				b.AppendLine("failureCaptureAttempted=" + artifact.failureCapture.attempted);
				b.AppendLine("failureCaptureScreenshotWritten=" + artifact.failureCapture.screenshotWritten);
				b.AppendLine("failureCaptureProgramReadbackAvailable=" + artifact.failureCapture.programReadbackAvailable);
				b.AppendLine("failureCaptureReadbackReason=" + (artifact.failureCapture.readbackReason ?? string.Empty));
			}
			if (artifact.acceptance != null)
			{
				b.AppendLine("acceptanceStage=" + (artifact.acceptance.stage ?? string.Empty));
				b.AppendLine("acceptanceContractVersion=" + (artifact.acceptance.acceptanceContractVersion ?? string.Empty));
				b.AppendLine("acceptanceFixtureRoot=" + (artifact.acceptance.fixtureRoot ?? string.Empty));
				b.AppendLine("acceptanceEditorAssemblyExcluded=" + artifact.acceptance.editorAssemblyExcluded);
				b.AppendLine("acceptancePresentationRoot=" + artifact.acceptance.presentationRootAvailable);
				b.AppendLine("acceptanceProgramAndPreviews=" + artifact.acceptance.programAndPreviewsReady);
				b.AppendLine("acceptanceRequiredGraph=" + artifact.acceptance.requiredGraphObserved);
				b.AppendLine("acceptanceRealFrame=" + artifact.acceptance.realFrameObserved);
				b.AppendLine("acceptanceValueControlUpdated=" + artifact.acceptance.valueControlUpdated);
				b.AppendLine("acceptanceValueControlRemapped=" + artifact.acceptance.valueControlRemapped);
				b.AppendLine("acceptancePresetTriggerFired=" + artifact.acceptance.presetTriggerFired);
				b.AppendLine("acceptanceLogicalControlStateObserved=" + artifact.acceptance.logicalControlStateObserved);
				b.AppendLine("acceptanceMediaPortable=" + artifact.acceptance.mediaPortable);
				b.AppendLine("acceptanceValueControlId=" + (artifact.acceptance.valueControlId ?? string.Empty));
				b.AppendLine("acceptancePresetTriggerId=" + (artifact.acceptance.presetTriggerId ?? string.Empty));
				b.AppendLine("acceptancePresetId=" + (artifact.acceptance.presetId ?? string.Empty));
				b.AppendLine("acceptanceUiSavePickTarget=" + (artifact.acceptance.uiSavePickTarget ?? string.Empty));
				if (artifact.acceptance.uiSave != null)
				{
					var save = artifact.acceptance.uiSave;
					b.AppendLine("acceptanceUiSave=callbacks=" + save.callbackCount + ":focused=" + Inline(save.focusedElement) + ":bannerVisible=" + save.bannerVisible + ":bannerText=" + Inline(save.bannerText) + ":taskBefore=" + Inline(save.taskBeforeId) + "/" + Inline(save.taskBeforeKind) + "/" + Inline(save.taskBeforeStatus) + ":taskAfter=" + Inline(save.taskAfterId) + "/" + Inline(save.taskAfterKind) + "/" + Inline(save.taskAfterStage) + "/" + Inline(save.taskAfterStatus) + ":path=" + Inline(save.taskAfterPath) + ":diagnosticCode=" + Inline(save.taskAfterDiagnosticCode) + ":diagnosticMessage=" + Inline(save.taskAfterDiagnosticMessage) + ":exceptionType=" + Inline(save.taskAfterExceptionType) + ":exceptionMessage=" + Inline(save.taskAfterExceptionMessage) + ":exceptionStack=" + Inline(save.taskAfterExceptionStackTrace));
				}
				if (artifact.acceptance.uiLayout != null)
				{
					var layout = artifact.acceptance.uiLayout;
					b.AppendLine("acceptanceUiScreen=" + layout.screenWidth + "x" + layout.screenHeight + ":fullscreen=" + layout.screenFullScreen + ":panelScale=" + layout.panelScale + ":panelScaleMode=" + (layout.panelScaleMode ?? string.Empty) + ":reference=" + layout.panelReferenceWidth + "x" + layout.panelReferenceHeight);
					b.AppendLine("acceptanceUiLayout=" + string.Join(";", (layout.elements ?? Array.Empty<HarnessAcceptanceUiElementLayoutArtifact>()).Select(element => element == null ? string.Empty : element.name + ":count=" + element.count + "@" + element.x + "," + element.y + "," + element.width + "," + element.height + ":direction=" + element.flexDirection + ":grow=" + element.flexGrow + ":shrink=" + element.flexShrink + ":basis=" + element.flexBasis + ":display=" + element.display + ":picking=" + element.pickingMode + ":enabled=" + element.enabled)));
				}
				b.AppendLine("acceptanceFileProjectReadable=" + artifact.acceptance.fileProjectReadable);
				b.AppendLine("acceptanceFileProjectWritable=" + artifact.acceptance.fileProjectWritable);
				b.AppendLine("acceptanceBackupFileReadable=" + artifact.acceptance.backupFileReadable);
				b.AppendLine("acceptanceNativeProbe=" + artifact.acceptance.nativeProbePassed);
				b.AppendLine("acceptanceUiActions=" + string.Join(",", artifact.acceptance.uiActions ?? Array.Empty<string>()));
				b.AppendLine("acceptanceFixtures=" + string.Join(";", (artifact.acceptance.fixtures ?? Array.Empty<HarnessAcceptanceFixtureArtifact>()).Select(x => x == null ? string.Empty : x.codec + ":" + x.frameBefore + "->" + x.frameAfter + "/preview1=" + x.preview1FrameBefore + "->" + x.preview1FrameAfter + "/preview2=" + x.preview2FrameBefore + "->" + x.preview2FrameAfter + ":prepare=" + x.prepareObserved + ":binding=" + x.mediaBindingApplied + ":ownershipFrames=" + x.ownershipFramesObserved + ":output=" + x.outputReadyObserved + ":real=" + x.realFrameObserved + ":ready=" + x.frameReady)));
				if (artifact.acceptance.lastOutput != null)
				{
					var output = artifact.acceptance.lastOutput;
					b.AppendLine("acceptanceLastOutput=frame=" + output.frameNumber + ":program=" + (output.programState ?? string.Empty) + "@" + output.programWidth + "x" + output.programHeight + ":reason=" + (output.programReason ?? string.Empty) + ":previews=" + string.Join(",", (output.previews ?? Array.Empty<HarnessAcceptanceOutputSurfaceArtifact>()).Select(preview => preview == null ? string.Empty : (preview.id ?? string.Empty) + "=" + (preview.state ?? string.Empty) + "@" + preview.width + "x" + preview.height + ":demanded=" + preview.demanded + ":reason=" + (preview.reason ?? string.Empty))));
				}
				b.AppendLine("acceptanceOwnershipTeardown=" + (artifact.acceptance.ownershipTeardown ?? string.Empty));
				if (artifact.acceptance.persistence != null)
				{
					b.AppendLine("acceptanceProjectRoot=" + (artifact.acceptance.persistence.projectRoot ?? string.Empty));
					b.AppendLine("acceptanceSaved=" + artifact.acceptance.persistence.saved);
					b.AppendLine("acceptanceReopened=" + artifact.acceptance.persistence.reopened);
					b.AppendLine("acceptanceRecovered=" + artifact.acceptance.persistence.recovered);
					b.AppendLine("acceptanceDirtyAfterRecovery=" + artifact.acceptance.persistence.dirtyAfterRecovery);
					b.AppendLine("acceptanceBackupReadable=" + artifact.acceptance.persistence.backupReadable);
					b.AppendLine("acceptanceBackupFingerprint=" + (artifact.acceptance.persistence.backupFingerprint ?? string.Empty));
					b.AppendLine("acceptanceExpectedBackupFingerprint=" + (artifact.acceptance.persistence.expectedBackupFingerprint ?? string.Empty));
					b.AppendLine("acceptanceBackupFingerprintComponents=" + (artifact.acceptance.persistence.backupFingerprintComponents ?? string.Empty));
					b.AppendLine("acceptanceExpectedBackupFingerprintComponents=" + (artifact.acceptance.persistence.expectedBackupFingerprintComponents ?? string.Empty));
					b.AppendLine("acceptanceFingerprint=" + (artifact.acceptance.persistence.fingerprint ?? string.Empty));
					b.AppendLine("acceptanceExpectedFingerprint=" + (artifact.acceptance.persistence.expectedFingerprint ?? string.Empty));
					b.AppendLine("acceptanceFingerprintComponents=" + (artifact.acceptance.persistence.fingerprintComponents ?? string.Empty));
					b.AppendLine("acceptanceExpectedFingerprintComponents=" + (artifact.acceptance.persistence.expectedFingerprintComponents ?? string.Empty));
				}
			}
			return b.ToString();
		}
	}
}
