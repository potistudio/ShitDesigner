using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.CompilerServices;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Project;

[assembly: InternalsVisibleTo("ShitDesigner.Bootstrap.Tests.EditMode")]
[assembly: InternalsVisibleTo("ShitDesigner.Bootstrap.Tests.PlayMode")]
[assembly: InternalsVisibleTo("ShitDesigner.Rendering")]

namespace ShitDesigner.Runtime {
	/// <summary>Read-only runtime projection used by the production Player
	/// acceptance harness. It contains the effective Preview demand after the
	/// quality policy, never a mutable controller.</summary>
	public sealed class RuntimePreviewOutputSnapshot {
		public string PreviewId { get; }
		public int Width { get; }
		public int Height { get; }
		public int TargetFramesPerSecond { get; }
		public int QualityStage { get; }
		public RuntimePreviewOutputSnapshot(string previewId, int width, int height, int targetFramesPerSecond, int qualityStage) {
			PreviewId = previewId ?? string.Empty;
			Width = width;
			Height = height;
			TargetFramesPerSecond = targetFramesPerSecond;
			QualityStage = qualityStage;
		}
	}

	internal sealed class ProjectDocumentMutationPort : IRuntimeProjectMutationPort {
		private readonly ProjectCommandProcessor _commands;
		internal ProjectDocumentMutationPort(ProjectDocument document) { _commands = new ProjectCommandProcessor(document ?? throw new ArgumentNullException(nameof(document))); }
		public UnitResult<Diagnostic> ApplyGraphPatch(GraphPatch patch) {
			if (patch == null) return UnitResult.Failure<Diagnostic>(Failure("runtime.persistence.graph_missing", "Graph patch is required."));
			return patch.Commands.Count == 0
				? _commands.CommitGraphRepair(patch.After.Nodes, patch.After.Connections)
				: _commands.CommitGraphState(patch.After.Nodes, patch.After.Connections);
		}
		public UnitResult<Diagnostic> ApplyParameterTransaction(IReadOnlyList<BaseValueUpdate> updates) {
			if (updates == null || updates.Count == 0) return UnitResult.Success<Diagnostic>();
			return _commands.ApplyBaseValues(updates);
		}
		private static Diagnostic Failure(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "runtime");
	}

	public sealed class RuntimeNodeHandle : IDisposable {
		private readonly IRuntimeNode _node;
		private bool _disposed;
		private bool _retiring;
		private RuntimeNodeState? _runtimeState;
		internal RuntimeNodeHandle(IRuntimeNode node) { _node = node ?? throw new ArgumentNullException(nameof(node)); }
		public IRuntimeNode Node => _node;
		public NodeInstanceId NodeId => _node.NodeId;
		public NodeTypeId TypeId => _node.TypeId;
		public ulong GenerationId => _node.GenerationId;
		public RuntimeNodeState State => _disposed ? RuntimeNodeState.Disposed : _retiring ? RuntimeNodeState.Retiring : _runtimeState ?? _node.State;
		internal void MarkRetiring() { if (!_disposed) _retiring = true; }
		internal void MarkFaulted() { if (!_disposed && !_retiring) _runtimeState = RuntimeNodeState.Faulted; }
		internal void MarkPreparing() { if (!_disposed && !_retiring) _runtimeState = RuntimeNodeState.Preparing; }
		internal void MarkReady() { if (!_disposed && !_retiring) _runtimeState = RuntimeNodeState.Ready; }
		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			_node.Dispose();
		}
	}

	public enum ProgramRuntimeState {
		OpaqueBlack,
		Available,
		HoldingLastFrame
	}

	public sealed class RuntimeSession : IDisposable {
		private readonly Dictionary<NodeTypeId, IRuntimeNodeFactory> _factories = new Dictionary<NodeTypeId, IRuntimeNodeFactory>();
		private readonly Dictionary<NodeInstanceId, RuntimeNodeHandle> _nodes = new Dictionary<NodeInstanceId, RuntimeNodeHandle>();
		private readonly List<RuntimeNodeHandle> _retiring = new List<RuntimeNodeHandle>();
		private readonly Dictionary<NodeInstanceId, IReadOnlyDictionary<PortId, NodeOutputResult>> _results = new Dictionary<NodeInstanceId, IReadOnlyDictionary<PortId, NodeOutputResult>>();
		private static readonly IReadOnlyList<OutputDemand> EmptyOutputDemands = new ReadOnlyCollection<OutputDemand>(new List<OutputDemand>());
		// Phase 3/4 share these copy-on-write frozen lists internally. Public
		// getters below still make defensive snapshots for external callers.
		private IReadOnlyList<OutputDemand> _demands = EmptyOutputDemands;
		private IReadOnlyList<OutputDemand> _requestedDemands = EmptyOutputDemands;
		private readonly List<OutputDemand> _desiredDemands = new List<OutputDemand>();
		private readonly object _demandStateGate = new object();
		private IRuntimePreviewQualityPolicy _previewQualityPolicy = new DefaultPreviewQualityPolicy();
		private RuntimeProgramPerformanceSnapshot _programPerformance = RuntimeProgramPerformanceSnapshot.Unavailable;
		private bool _programWarningWasActive;
		private readonly Dictionary<NodeInstanceId, bool> _nodeDemanded = new Dictionary<NodeInstanceId, bool>();
		private readonly RuntimeQueue<IReadOnlyList<OutputDemand>> _demandQueue = new RuntimeQueue<IReadOnlyList<OutputDemand>>(64);
		private readonly RuntimeQueue<RuntimeCompletion> _completionQueue = new RuntimeQueue<RuntimeCompletion>(4096);
		private ulong _nextGeneration;
		private bool _disposed;
		private long _requestedDemandRevision;
		private long _cachedPreviewDemandRevision = long.MinValue;
		private long _cachedPreviewDocumentRevision = long.MinValue;
		private long _cachedPreviewGraphRevision = long.MinValue;
		private long _cachedPreviewQualityRevision = long.MinValue;
		private IRuntimePreviewQualityPolicy _cachedPreviewQualityPolicy;
		private IReadOnlyList<RuntimePreviewOutputSnapshot> _previewOutputSnapshots = new ReadOnlyCollection<RuntimePreviewOutputSnapshot>(new List<RuntimePreviewOutputSnapshot>());
		private HashSet<NodeInstanceId> _requestedPreviewMembership = new HashSet<NodeInstanceId>();

		public ProjectDocument Document { get; }
		public NodeTypeRegistry Registry { get; }
		public GraphEditor GraphEditor { get; }
		public ParameterStore Parameters { get; } = new ParameterStore();
		public DiagnosticHub Diagnostics { get; }
		public IRuntimeProjectMutationPort Persistence { get; }
		public IRuntimeDefaultImageProvider DefaultImageProvider { get; set; }
		public IRuntimeOutputSurfacePort OutputSurfaces { get; set; }
		public IRuntimeResourcePreparation ResourcePreparation { get; set; }
		public IRuntimeResourceFinalization ResourceFinalization { get; set; }
		public IFeedbackCommitter FeedbackCommitter { get; set; }
		/// <summary>Rendering injects its moving-average quality policy at the
		/// production composition boundary. Replacing it is only allowed
		/// before frame demands are evaluated.</summary>
		public IRuntimePreviewQualityPolicy PreviewQualityPolicy {
			get => _previewQualityPolicy;
			set {
				_previewQualityPolicy = value ?? throw new ArgumentNullException(nameof(value));
				_cachedPreviewQualityPolicy = null;
			}
		}
		public IRuntimeProgramPerformanceSink ProgramPerformanceSink { get; set; }
		public RuntimeProgramPerformanceSnapshot ProgramPerformance => _programPerformance;
		// Graph-owned evaluation plans stay behind the Runtime boundary.
		// Consumers receive FrameEvaluationContext/FrameSnapshot neutral
		// projections and must not inspect Graph types directly.
		internal EvaluationPlan Plan { get; private set; }
		internal RuntimeOutputResolutionProjection ResolutionProjection { get; private set; }
		/// <summary>Internal topology stamp for runtime services. Public node
		/// snapshots remain defensive; services that own resources can avoid
		/// re-scanning stable node membership every frame.</summary>
		internal long GraphTopologyRevision => GraphEditor?.State?.Revision ?? 0L;
		public FrameSnapshot LastSnapshot { get; internal set; }
		/// <summary>
		/// The presentation selected at the Phase-8 boundary. Preview consumers
		/// must use this projection rather than the current evaluation results:
		/// a quality-throttled Preview intentionally retains its last presented
		/// frame on a non-due evaluation.
		/// </summary>
		public OutputPresentation LastPresentation { get; internal set; }
		public ProgramRuntimeState ProgramState { get; internal set; } = ProgramRuntimeState.OpaqueBlack;
		public NodeOutputResult LastProgramResult { get; internal set; }
		public bool HasLastProgramFrame { get; internal set; }
		/// <summary>Effective Program cadence supplied by production policy.
		/// Zero means the host did not expose a target and is observable as a
		/// contract failure.</summary>
		public int ProgramTargetFramesPerSecond { get; }
		public IReadOnlyDictionary<NodeInstanceId, RuntimeNodeHandle> Nodes => new ReadOnlyDictionary<NodeInstanceId, RuntimeNodeHandle>(new Dictionary<NodeInstanceId, RuntimeNodeHandle>(_nodes));
		public IReadOnlyDictionary<NodeInstanceId, IReadOnlyDictionary<PortId, NodeOutputResult>> OutputResults => new ReadOnlyDictionary<NodeInstanceId, IReadOnlyDictionary<PortId, NodeOutputResult>>(new Dictionary<NodeInstanceId, IReadOnlyDictionary<PortId, NodeOutputResult>>(_results));
		public IReadOnlyList<OutputDemand> OutputDemands => new ReadOnlyCollection<OutputDemand>(new List<OutputDemand>(_demands));
		public IReadOnlyList<OutputDemand> RequestedOutputDemands => new ReadOnlyCollection<OutputDemand>(new List<OutputDemand>(_requestedDemands));
		internal IReadOnlyList<OutputDemand> OutputDemandSnapshot => _demands;
		internal IReadOnlyList<OutputDemand> RequestedOutputDemandSnapshot => _requestedDemands;
		public bool IsDisposed => _disposed;

		public IReadOnlyList<RuntimePreviewOutputSnapshot> CapturePreviewOutputSnapshots() {
			RefreshPreviewOutputSnapshots();
			return _previewOutputSnapshots;
		}

		/// <summary>Internal bridge membership probe.  It reuses the same
		/// copy-on-write requested-preview cache as CapturePreviewOutputSnapshots
		/// and therefore never copies RequestedOutputDemands per Player frame.</summary>
		public bool IsPreviewRequested(NodeInstanceId nodeId) {
			if (nodeId.IsEmpty) return false;
			RefreshPreviewOutputSnapshots();
			return _requestedPreviewMembership.Contains(nodeId);
		}

		/// <summary>Direct scalar health probe for the performance harness.
		/// Unlike Nodes, it does not allocate a defensive dictionary copy.</summary>
		public void CaptureMediaBackendCounts(out int backendCount, out int nativeContextCount) {
			backendCount = 0;
			nativeContextCount = 0;
			foreach (var handle in _nodes.Values) {
				if (!(handle?.Node is IRuntimePerformanceHealthNode health)) continue;
				if (health.HasActiveBackend) backendCount++;
				if (health.HasNativeContext) nativeContextCount++;
			}
		}

		private void RefreshPreviewOutputSnapshots() {
			var documentRevision = Document?.DocumentRevision ?? 0L;
			var graphRevision = GraphEditor?.State?.Revision ?? 0L;
			var quality = _previewQualityPolicy;
			var qualityRevision = quality?.Revision ?? 0L;
			if (_cachedPreviewDemandRevision == _requestedDemandRevision &&
				_cachedPreviewDocumentRevision == documentRevision &&
				_cachedPreviewGraphRevision == graphRevision &&
				_cachedPreviewQualityRevision == qualityRevision &&
				ReferenceEquals(_cachedPreviewQualityPolicy, quality)) return;

			var membership = new HashSet<NodeInstanceId>();
			var ids = new List<NodeInstanceId>();
			foreach (var demand in _requestedDemands)
				if (demand != null && demand.TargetKind == OutputTargetKind.Preview && membership.Add(demand.NodeId)) ids.Add(demand.NodeId);
			ids.Sort((left, right) => string.CompareOrdinal(left.Value, right.Value));
			var snapshots = new List<RuntimePreviewOutputSnapshot>(ids.Count);
			foreach (var id in ids) snapshots.Add(quality.Capture(id));

			_requestedPreviewMembership = membership;
			_previewOutputSnapshots = new ReadOnlyCollection<RuntimePreviewOutputSnapshot>(snapshots);
			_cachedPreviewDemandRevision = _requestedDemandRevision;
			_cachedPreviewDocumentRevision = documentRevision;
			_cachedPreviewGraphRevision = graphRevision;
			_cachedPreviewQualityRevision = quality.Revision;
			_cachedPreviewQualityPolicy = quality;
		}

		public RuntimeSession(ProjectDocument document, NodeTypeRegistry registry, DiagnosticHub diagnostics = null, IRuntimeProjectMutationPort persistence = null, int programTargetFramesPerSecond = 0) {
			Document = document ?? throw new ArgumentNullException(nameof(document));
			Registry = registry ?? throw new ArgumentNullException(nameof(registry));
			GraphEditor = new GraphEditor(GraphState.FromProject(document), registry);
			Diagnostics = diagnostics ?? new DiagnosticHub("runtime.session");
			Persistence = persistence ?? new ProjectDocumentMutationPort(document);
			ProgramTargetFramesPerSecond = Math.Max(0, programTargetFramesPerSecond);
			Parameters.Synchronize(GraphEditor.State, Document);
		}

		public UnitResult<Diagnostic> EnqueueCompletion(RuntimeCompletion completion) {
			if (completion == null) return UnitResult.Failure<Diagnostic>(FailureDiagnostic("runtime.queue.invalid", "Completion is required."));
			if (_disposed) {
				try { completion.Discard(this); } catch { Diagnostics.IncrementEmergency(); }
				return UnitResult.Failure<Diagnostic>(FailureDiagnostic("runtime.session.disposed", "RuntimeSession is disposed."));
			}
			if (_completionQueue.TryEnqueue(completion)) return UnitResult.Success<Diagnostic>();
			try { completion?.Discard(this); } catch { /* emergency cleanup cannot throw across the queue boundary */ }
			Diagnostics.IncrementEmergency();
			return UnitResult.Failure<Diagnostic>(FailureDiagnostic("runtime.queue.overloaded", "Completion queue is full."));
		}

		internal List<RuntimeCompletion> DrainCompletions() => _completionQueue.Drain();

		public UnitResult<Diagnostic> RegisterFactory(IRuntimeNodeFactory factory) {
			if (_disposed) return Failure("runtime.session.disposed", "RuntimeSession is disposed.");
			if (factory == null) return Failure("runtime.factory.invalid", "Runtime node factory is required.");
			if (_factories.ContainsKey(factory.TypeId)) return Failure("runtime.factory.duplicate", "Runtime node factory is already registered.");
			_factories.Add(factory.TypeId, factory);
			return SynchronizeNodes();
		}

		public UnitResult<Diagnostic> SetOutputDemands(IEnumerable<OutputDemand> demands) {
			if (_disposed) return Failure("runtime.session.disposed", "RuntimeSession is disposed.");
			var list = (demands ?? Enumerable.Empty<OutputDemand>()).ToList();
			// Output demand is a complete desired-state snapshot, not an
			// ordered command. Coalescing here prevents an old show request
			// from being replayed after a close/hide and avoids an artificial
			// 64-request backlog during rapid Viewer interaction.
			ReplaceDesiredDemands(list);
			return UnitResult.Success<Diagnostic>();
		}

		/// <summary>Queues an explicit Viewer-host hide for one Preview. The
		/// quality-controller state remains allocated so a later host show
		/// resumes the same Preview assignment and quality stage.</summary>
		public UnitResult<Diagnostic> HidePreview(NodeInstanceId previewNodeId) {
			if (_disposed) return Failure("runtime.session.disposed", "RuntimeSession is disposed.");
			if (previewNodeId.IsEmpty) return Failure("runtime.preview.invalid", "Preview node ID is required.");
			lock (_demandStateGate)
				ReplaceDesiredDemandsLocked(_desiredDemands.Where(x => x != null && x.NodeId != previewNodeId).ToList());
			return UnitResult.Success<Diagnostic>();
		}

		/// <summary>Queues removal of a closed Preview and releases its
		/// quality-controller slot. This is deliberately distinct from
		/// HidePreview: closing a tab must permit the slot to be reused.</summary>
		public UnitResult<Diagnostic> RemovePreview(NodeInstanceId previewNodeId) {
			var hidden = HidePreview(previewNodeId);
			if (hidden.IsFailure) return hidden;
			_previewQualityPolicy.Remove(previewNodeId);
			return hidden;
		}

		/// <summary>Queues a host-wide hide while retaining all Preview
		/// assignments and quality-controller state.</summary>
		public UnitResult<Diagnostic> HideAllPreviews() {
			if (_disposed) return Failure("runtime.session.disposed", "RuntimeSession is disposed.");
			ReplaceDesiredDemands(Array.Empty<OutputDemand>());
			return UnitResult.Success<Diagnostic>();
		}

		private void ReplaceDesiredDemands(IReadOnlyList<OutputDemand> demands) {
			lock (_demandStateGate) ReplaceDesiredDemandsLocked(demands);
		}

		private void ReplaceDesiredDemandsLocked(IReadOnlyList<OutputDemand> demands) {
			_desiredDemands.Clear();
			if (demands != null) _desiredDemands.AddRange(demands);
			_demandQueue.ReplaceLatest(new ReadOnlyCollection<OutputDemand>(_desiredDemands.ToList()));
		}

		internal List<IReadOnlyList<OutputDemand>> DrainDemandRequests() => _demandQueue.Drain();
		internal bool ApplyDemandRequest(IReadOnlyList<OutputDemand> demands) {
			var next = new ReadOnlyCollection<OutputDemand>(new List<OutputDemand>(demands ?? EmptyOutputDemands));
			if (DemandListsEqual(_requestedDemands, next)) return false;
			_requestedDemands = next;
			_requestedDemandRevision++;
			return true;
		}
		internal void InvalidatePlan() { Plan = null; }
		internal bool PlanDemandsForFrame(ulong frameNumber) {
			var previous = _demands;
			// Program is the stable external contract. A caller may request a
			// preview freely, but Program is always the fixed 1920x1080 image
			// stream regardless of an incoming demand's dimensions or node.
			var programNode = GraphEditor.State.Nodes.FirstOrDefault(x => x.TypeId.Value == GraphConstants.ProgramOutputTypeId);
			if (_requestedDemands.Count == 0 && IsStableProgramOnlyDemand(previous, programNode)) return false;

			var next = new List<OutputDemand>();
			if (programNode != null)
				next.Add(new OutputDemand(OutputTargetKind.Program, programNode.Id, new PortId(GraphConstants.ImagePortId), 1920, 1080));
			foreach (var demand in _requestedDemands.Where(x => x.TargetKind == OutputTargetKind.Preview)) {
				_previewQualityPolicy.Ensure(demand.NodeId, demand.Focused, demand.FocusTimestamp);
				// Cadence suppresses ordinary repeated Preview evaluations,
				// but it must not suppress a changed resolution/port
				// contract.  A descriptor transition is a Phase-5 resource
				// demand even when the next scheduled presentation frame is
				// not due; otherwise a larger candidate can never be
				// prepared, and a failed allocation is reported as a
				// successful no-op frame.
				var resolutionChanged = PreviewDemandResolutionChanged(previous, demand);
				if (resolutionChanged || _previewQualityPolicy.IsDue(demand.NodeId, frameNumber)) {
					var runtimeDemand = _previewQualityPolicy.Apply(new RuntimePreviewDemand(
						demand.NodeId, demand.OutputPortId, demand.Width, demand.Height,
						demand.Focused, demand.FocusTimestamp));
					next.Add(new OutputDemand(OutputTargetKind.Preview, runtimeDemand.NodeId,
						runtimeDemand.OutputPortId, runtimeDemand.Width, runtimeDemand.Height,
						runtimeDemand.Focused, runtimeDemand.FocusTimestamp));
				}
			}
			if (DemandListsEqual(previous, next)) return false;
			_demands = new ReadOnlyCollection<OutputDemand>(next);
			return true;
		}

		private static bool IsStableProgramOnlyDemand(IReadOnlyList<OutputDemand> demands, NodeRecord programNode) {
			if (programNode == null) return demands == null || demands.Count == 0;
			if (demands == null || demands.Count != 1) return false;
			var demand = demands[0];
			return demand != null && demand.TargetKind == OutputTargetKind.Program && demand.NodeId == programNode.Id &&
				demand.OutputPortId.Value == GraphConstants.ImagePortId && demand.Width == 1920 && demand.Height == 1080;
		}

		private static bool PreviewDemandResolutionChanged(IReadOnlyList<OutputDemand> previous, OutputDemand requested) {
			if (requested == null) return false;
			var prior = (previous ?? new List<OutputDemand>())
				.FirstOrDefault(x => x != null
					&& x.TargetKind == OutputTargetKind.Preview
					&& x.NodeId == requested.NodeId
					&& x.OutputPortId == requested.OutputPortId);
			return prior == null || prior.Width != requested.Width || prior.Height != requested.Height;
		}

		public void ObservePreviewTiming(NodeInstanceId previewNodeId, double cpuMilliseconds, double gpuMilliseconds, ulong frameNumber) {
			if (previewNodeId.IsEmpty || double.IsNaN(cpuMilliseconds) || double.IsInfinity(cpuMilliseconds) || double.IsNaN(gpuMilliseconds) || double.IsInfinity(gpuMilliseconds)) return;
			_previewQualityPolicy.Observe(cpuMilliseconds, gpuMilliseconds, frameNumber);
		}

		/// <summary>Consumes one timing sample for the already-presented
		/// Program frame. An unavailable Unity sample is explicitly exposed as
		/// unavailable; it is never converted to a zero or guessed value.</summary>
		public void ObserveFrameTiming(RuntimeFrameTimingSample sample) {
			if (_disposed) return;
			if (!sample.IsAvailable) {
				ProgramPerformanceSink?.Reset();
				_programPerformance = RuntimeProgramPerformanceSnapshot.UnavailableAt(sample.FrameNumber);
				_programWarningWasActive = false;
				return;
			}
			// FrameTiming's wait-inclusive cpuFrameTime is intentionally not
			// used here. Bootstrap supplies the CPU critical-path workload so
			// target-fps/Present pacing cannot suppress Preview quality.
			_previewQualityPolicy.Observe(sample.CpuWorkloadMilliseconds, sample.GpuFrameMilliseconds, sample.FrameNumber);
			var sink = ProgramPerformanceSink;
			if (sink != null) {
				sink.Observe(sample.FramesPerSecond, sample.CpuWorkloadMilliseconds, sample.GpuFrameMilliseconds);
				var captured = sink.Capture();
				_programPerformance = new RuntimeProgramPerformanceSnapshot(captured.FramesPerSecond, captured.CpuFrameMilliseconds,
					captured.GpuFrameMilliseconds, captured.ConsecutiveBadFrames, captured.WarningActive, captured.IsAvailable, sample.FrameNumber);
				if (_programPerformance.WarningActive && !_programWarningWasActive) _previewQualityPolicy.ObserveProgramWarning(sample.FrameNumber);
				_programWarningWasActive = _programPerformance.WarningActive;
			}
			else {
				_programPerformance = new RuntimeProgramPerformanceSnapshot(sample.FramesPerSecond, sample.CpuFrameMilliseconds, sample.GpuFrameMilliseconds, 0, false, true, sample.FrameNumber);
				_programWarningWasActive = false;
			}
		}

		/// <summary>Resets the runtime-owned Program performance projection at
		/// a measurement boundary. This is deliberately limited to timing
		/// state: graph, project, demand, and node state remain untouched.</summary>
		public void ResetPerformanceForMeasurement(ulong measurementFrame = 0) {
			if (_disposed) return;
			ProgramPerformanceSink?.Reset();
			_programPerformance = RuntimeProgramPerformanceSnapshot.UnavailableAt(measurementFrame);
			_programWarningWasActive = false;
		}

		public UnitResult<Diagnostic> RebuildPlan() {
			if (_disposed) return Failure("runtime.session.disposed", "RuntimeSession is disposed.");
			var current = GraphEditor.State;
			if (!EvaluationPlan.TryBuild(current, Registry, _demands, out var plan, out var diagnostic, out var normalized)) {
				Plan = null;
				ResolutionProjection = null;
				return UnitResult.Failure<Diagnostic>(diagnostic ?? FailureDiagnostic("runtime.plan.invalid", "EvaluationPlan could not be built."));
			}
			if (!GraphStatesEqual(current, normalized)) {
				// EvaluationPlan never mutates its input. Persist the Broken
				// classification through a repair transaction. Repairs are
				// deliberately outside ordinary user undo history, while the
				// structural revision still advances and Project is marked dirty.
				var normalization = GraphEditor.PrepareNormalized(normalized);
				if (normalization.IsFailure) return UnitResult.Failure<Diagnostic>(normalization.Error);
				var persisted = Persistence.ApplyGraphPatch(normalization.Value);
				if (persisted.IsFailure) {
					Plan = null;
					return persisted;
				}
				var normalizedCommit = GraphEditor.CommitNormalizedRepair(normalization.Value);
				if (normalizedCommit.IsFailure) {
					return normalizedCommit;
				}
				plan = plan.WithSourceRevision(GraphEditor.State.Revision);
			}
			Plan = plan;
			ResolutionProjection = RuntimeOutputResolutionProjectionFactory.Get(plan, GraphEditor.State);
			Parameters.Synchronize(GraphEditor.State, Document);
			return SynchronizeNodes();
		}

		/// <summary>Installs the plan produced by a committed graph batch.</summary>
		internal void InstallPlan(EvaluationPlan plan) {
			if (plan == null) { Plan = null; ResolutionProjection = null; return; }
			Plan = plan;
			ResolutionProjection = RuntimeOutputResolutionProjectionFactory.Get(plan, GraphEditor.State);
			Parameters.Synchronize(GraphEditor.State, Document);
			SynchronizeNodes();
		}

		public UnitResult<Diagnostic> ApplyGraphCommand(GraphEditCommand command) {
			if (_disposed) return Failure("runtime.session.disposed", "RuntimeSession is disposed.");
			var detailed = GraphEditor.PrepareBatchDetailed(new[] { command }, _demands);
			if (detailed.Patch == null) return UnitResult.Failure<Diagnostic>(detailed.Diagnostic);
			var persisted = Persistence.ApplyGraphPatch(detailed.Patch);
			if (persisted.IsFailure) {
				return persisted;
			}
			var committed = GraphEditor.CommitCandidate(detailed.Patch);
			if (committed.IsFailure) {
				return committed;
			}
			InstallPlan(detailed.Plan);
			return UnitResult.Success<Diagnostic>();
		}

		public RuntimeNodeHandle FindNode(NodeInstanceId id) => _nodes.TryGetValue(id, out var handle) ? handle : null;

		/// <summary>Allocation-free runtime service probe. The returned map
		/// is already a frozen node result projection; callers must treat it
		/// as read-only. <see cref="OutputResults"/> remains the defensive
		/// public snapshot for callers that need a complete dictionary.</summary>
		internal bool TryGetOutputResults(NodeInstanceId nodeId, out IReadOnlyDictionary<PortId, NodeOutputResult> results) => _results.TryGetValue(nodeId, out results);
		internal bool TryGetResults(NodeInstanceId nodeId, out IReadOnlyDictionary<PortId, NodeOutputResult> results) => TryGetOutputResults(nodeId, out results);
		internal void SetNodeResults(NodeInstanceId nodeId, IReadOnlyDictionary<PortId, NodeOutputResult> results) => _results[nodeId] = results;
		internal void ClearFrameResults() => _results.Clear();

		/// <summary>Phase-4 demand transition delivery. This is intentionally
		/// separate from Evaluate: a node may be absent from the evaluation
		/// order while its decoder still must stop transferring frames.</summary>
		internal void NotifyDemandChanges(FrameEvaluationContext evaluation, ICollection<Diagnostic> diagnostics) {
			var demandedNodes = new HashSet<NodeInstanceId>();
			var plan = Plan;
			if (plan != null) {
				foreach (var pair in plan.RequestedOutputs)
					if (pair.Value != null && pair.Value.Count > 0) demandedNodes.Add(pair.Key);
			}
			foreach (var handle in _nodes.Values.ToList()) {
				var demanded = demandedNodes.Contains(handle.NodeId);
				if (_nodeDemanded.TryGetValue(handle.NodeId, out var previous) && previous == demanded) continue;
				_nodeDemanded[handle.NodeId] = demanded;
				if (!(handle.Node is IRuntimeDemandAwareNode demandAware)) continue;
				try { demandAware.OnDemandChanged(demanded, evaluation); }
				catch (Exception exception) {
					diagnostics?.Add(new Diagnostic(new DiagnosticCode("runtime.demand.transition_failed"), Severity.Error, "Runtime demand transition failed.", nodeId: handle.NodeId, generationId: handle.GenerationId, module: "runtime", exception: DiagnosticExceptionInfo.FromException(exception)));
				}
			}
		}

		internal UnitResult<Diagnostic> SynchronizeNodes() {
			// Disabled nodes are outside the evaluation plan but retain their
			// runtime generation and leases so re-enabling does not thrash GPU
			// resources. Only deletion/unknown replacement retires a handle.
			var active = new HashSet<NodeInstanceId>(GraphEditor.State.Nodes.Where(x => !x.IsUnknown).Select(x => x.Id));
			foreach (var id in _nodes.Keys.ToList()) {
				if (active.Contains(id)) continue;
				var generation = _nodes[id].GenerationId;
				Diagnostics.CloseNode(id, generation, "node_removed");
				_nodeDemanded.Remove(id);
				_nodes[id].MarkRetiring();
				_retiring.Add(_nodes[id]);
				_nodes.Remove(id);
			}
			foreach (var record in GraphEditor.State.Nodes.Where(x => x.Enabled && !x.IsUnknown)) {
				if (_nodes.TryGetValue(record.Id, out var existing)) {
					if (existing.TypeId == record.TypeId) continue;
					Diagnostics.CloseNode(record.Id, existing.GenerationId, "node_type_replaced");
					_nodeDemanded.Remove(record.Id);
					existing.MarkRetiring();
					_retiring.Add(existing);
					_nodes.Remove(record.Id);
				}
				if (!_factories.TryGetValue(record.TypeId, out var factory)) continue;
				var generation = ++_nextGeneration;
				Result<IRuntimeNode, Diagnostic> created;
				try { created = factory.Create(RuntimeNodeCreateInfo.FromProject(record), generation); }
				catch (Exception exception) {
					Diagnostics.Report(new Diagnostic(new DiagnosticCode("runtime.node.create_failed"), Severity.Error, "Runtime node creation failed.", nodeId: record.Id, nodeTypeId: record.TypeId, generationId: generation, exception: DiagnosticExceptionInfo.FromException(exception)));
					continue;
				}
				if (created.IsFailure) {
					Diagnostics.Report(created.Error);
					continue;
				}
				_nodes.Add(record.Id, new RuntimeNodeHandle(created.Value));
			}
			return UnitResult.Success<Diagnostic>();
		}

		/// <summary>Phase 9 disposal boundary for nodes removed from the plan.</summary>
		internal void FinalizeRetiring() {
			foreach (var handle in _retiring) handle.Dispose();
			_retiring.Clear();
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			Diagnostics.CloseSession("session_ended");
			foreach (var node in _nodes.Values) node.Dispose();
			foreach (var node in _retiring) node.Dispose();
			_nodes.Clear(); _retiring.Clear(); _results.Clear(); _nodeDemanded.Clear();
			lock (_demandStateGate) _desiredDemands.Clear();
			Plan = null;
			ResolutionProjection = null;
			if (DefaultImageProvider is IDisposable disposableProvider) disposableProvider.Dispose();
			if (ResourcePreparation is IDisposable disposablePreparation) disposablePreparation.Dispose();
			if (ResourceFinalization is IDisposable disposableFinalization && !ReferenceEquals(disposableFinalization, ResourcePreparation)) disposableFinalization.Dispose();
		}

		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(FailureDiagnostic(code, message));
		private static Diagnostic FailureDiagnostic(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message);

		private static bool GraphStatesEqual(GraphState left, GraphState right) {
			if (left == null || right == null || left.Revision != right.Revision) return false;
			return left.Nodes.SequenceEqual(right.Nodes) && left.Connections.SequenceEqual(right.Connections);
		}

		private static bool DemandListsEqual(IReadOnlyList<OutputDemand> left, IReadOnlyList<OutputDemand> right) {
			if (left == null || right == null || left.Count != right.Count) return false;
			for (var i = 0; i < left.Count; i++) {
				var a = left[i]; var b = right[i];
				if (a == null || b == null) {
					if (!ReferenceEquals(a, b)) return false;
					continue;
				}
				if (a.TargetKind != b.TargetKind || a.NodeId != b.NodeId || a.OutputPortId != b.OutputPortId ||
					a.Width != b.Width || a.Height != b.Height || a.Focused != b.Focused || a.FocusTimestamp != b.FocusTimestamp ||
					Math.Abs(a.AspectRatio - b.AspectRatio) > 0.0000001d) return false;
			}
			return true;
		}
	}

	/// <summary>One presented-frame timing observation crossing the Unity
	/// boundary. Values are NaN when Unity did not return a timing sample.</summary>
	/// <summary>Runtime-only bridge from an immutable Graph plan to the
	/// Graph-free DTO projection handed to node-binding assemblies. The weak
	/// cache is keyed by plan identity, so demand/quality/topology rebuilds
	/// naturally create a new projection while stable frames reuse it.</summary>
	internal static class RuntimeOutputResolutionProjectionFactory {
		private static readonly ConditionalWeakTable<EvaluationPlan, RuntimeOutputResolutionProjection> Projections =
			new ConditionalWeakTable<EvaluationPlan, RuntimeOutputResolutionProjection>();

		internal static RuntimeOutputResolutionProjection Get(EvaluationPlan plan, GraphState state) {
			if (plan == null) return null;
			return Projections.GetValue(plan, candidate => Build(candidate, state));
		}

		private static RuntimeOutputResolutionProjection Build(EvaluationPlan plan, GraphState state) {
			var entries = new List<RuntimeOutputResolutionEntry>();
			var visualEntries = new List<RuntimeOutputResolutionEntry>();
			foreach (var node in plan.RequiredOutputResolutions.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)) {
				var record = state?.FindNode(node.Key);
				foreach (var port in node.Value.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal)) {
					var demand = new RuntimeOutputResolutionDemand(port.Value.Width, port.Value.Height, port.Value.AspectRatio);
					var entry = new RuntimeOutputResolutionEntry(node.Key, port.Key, demand);
					entries.Add(entry);
					var definition = record?.FindPort(port.Key);
					if (definition != null && definition.Direction == PortDirection.Output && definition.Type == PortType.ImageFrame)
						visualEntries.Add(entry);
				}
			}
			return new RuntimeOutputResolutionProjection(entries, visualEntries);
		}
	}

	/// <summary>Immutable timing sample crossing from the Unity Bootstrap
	/// boundary into Runtime. The retained CPU property name is historical:
	/// its value is now the positive critical-path workload, not Unity's
	/// wait-inclusive cpuFrameTime.</summary>
	public readonly struct RuntimeFrameTimingSample {
		public ulong FrameNumber { get; }
		public double FramesPerSecond { get; }
		public double CpuFrameMilliseconds { get; }
		public double CpuWorkloadMilliseconds => CpuFrameMilliseconds;
		public double GpuFrameMilliseconds { get; }
		public bool IsAvailable { get; }

		public RuntimeFrameTimingSample(ulong frameNumber, double framesPerSecond, double cpuWorkloadMilliseconds, double gpuFrameMilliseconds, bool isAvailable = true) {
			FrameNumber = frameNumber;
			FramesPerSecond = framesPerSecond;
			CpuFrameMilliseconds = cpuWorkloadMilliseconds;
			GpuFrameMilliseconds = gpuFrameMilliseconds;
			IsAvailable = isAvailable && IsFinite(framesPerSecond) && IsFinite(cpuWorkloadMilliseconds) && IsFinite(gpuFrameMilliseconds)
				&& framesPerSecond > 0d && cpuWorkloadMilliseconds > 0d && gpuFrameMilliseconds > 0d;
			if (!IsAvailable) {
				FramesPerSecond = double.NaN;
				CpuFrameMilliseconds = double.NaN;
				GpuFrameMilliseconds = double.NaN;
			}
		}

		public static RuntimeFrameTimingSample Unavailable(ulong frameNumber) => new RuntimeFrameTimingSample(frameNumber, double.NaN, double.NaN, double.NaN, false);
		private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0d;
	}

	/// <summary>Public Program performance projection. CpuFrameMilliseconds
	/// remains the compatibility name for the CPU workload critical path.</summary>
	public readonly struct RuntimeProgramPerformanceSnapshot {
		public ulong FrameNumber { get; }
		public double FramesPerSecond { get; }
		public double CpuFrameMilliseconds { get; }
		public double CpuWorkloadMilliseconds => CpuFrameMilliseconds;
		public double GpuFrameMilliseconds { get; }
		public int ConsecutiveBadFrames { get; }
		public bool WarningActive { get; }
		public bool IsAvailable { get; }

		public RuntimeProgramPerformanceSnapshot(double framesPerSecond, double cpuFrameMilliseconds, double gpuFrameMilliseconds, int consecutiveBadFrames, bool warningActive, bool isAvailable, ulong frameNumber = 0) {
			FrameNumber = frameNumber;
			FramesPerSecond = framesPerSecond;
			CpuFrameMilliseconds = cpuFrameMilliseconds;
			GpuFrameMilliseconds = gpuFrameMilliseconds;
			ConsecutiveBadFrames = Math.Max(0, consecutiveBadFrames);
			WarningActive = warningActive;
			IsAvailable = isAvailable;
		}

		public static RuntimeProgramPerformanceSnapshot Unavailable => new RuntimeProgramPerformanceSnapshot(double.NaN, double.NaN, double.NaN, 0, false, false);
		public static RuntimeProgramPerformanceSnapshot UnavailableAt(ulong frameNumber) => new RuntimeProgramPerformanceSnapshot(double.NaN, double.NaN, double.NaN, 0, false, false, frameNumber);
	}

	/// <summary>Rendering owns the concrete monitor. Runtime observes only
	/// this immutable snapshot boundary and never references Rendering.</summary>
	public interface IRuntimeProgramPerformanceSink {
		void Reset();
		/// <param name="cpuWorkloadMilliseconds">Maximum positive main/render
		/// thread frame time; this deliberately excludes target-fps waits.</param>
		void Observe(double framesPerSecond, double cpuWorkloadMilliseconds, double gpuFrameMilliseconds);
		RuntimeProgramPerformanceSnapshot Capture();
	}
}
