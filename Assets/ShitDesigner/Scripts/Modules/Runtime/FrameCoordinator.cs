using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Project;

namespace ShitDesigner.Runtime {
	public enum RuntimeCommandKind {
		PauseClock,
		ResumeClock,
		ResetClock,
		ResetFeedback
	}

	public sealed class RuntimeCommand {
		public RuntimeCommandKind Kind { get; }
		public NodeInstanceId? NodeId { get; }
		public string CommandRequestId { get; }
		private RuntimeCommand(RuntimeCommandKind kind, NodeInstanceId? nodeId, string commandRequestId) {
			Kind = kind;
			NodeId = nodeId;
			CommandRequestId = string.IsNullOrWhiteSpace(commandRequestId) ? Guid.NewGuid().ToString("D") : commandRequestId.Trim();
		}
		public static RuntimeCommand PauseClock(string commandRequestId = null) => new RuntimeCommand(RuntimeCommandKind.PauseClock, null, commandRequestId);
		public static RuntimeCommand ResumeClock(string commandRequestId = null) => new RuntimeCommand(RuntimeCommandKind.ResumeClock, null, commandRequestId);
		public static RuntimeCommand ResetClock(string commandRequestId = null) => new RuntimeCommand(RuntimeCommandKind.ResetClock, null, commandRequestId);
		public static RuntimeCommand ResetFeedback(NodeInstanceId nodeId, string commandRequestId = null) => new RuntimeCommand(RuntimeCommandKind.ResetFeedback, nodeId, commandRequestId);
	}

	public sealed class GraphCommandExecutionResult {
		public string CommandRequestId { get; }
		public UnitResult<Diagnostic> Result { get; }
		internal GraphCommandExecutionResult(string commandRequestId, UnitResult<Diagnostic> result) { CommandRequestId = commandRequestId ?? string.Empty; Result = result; }
	}

	public sealed class RuntimeCommandExecutionResult {
		public string CommandRequestId { get; }
		public RuntimeCommandKind Kind { get; }
		public bool Applied { get; }
		public Diagnostic Diagnostic { get; }
		internal RuntimeCommandExecutionResult(string commandRequestId, RuntimeCommandKind kind, bool applied, Diagnostic diagnostic = null) {
			CommandRequestId = commandRequestId ?? string.Empty;
			Kind = kind;
			Applied = applied;
			Diagnostic = diagnostic;
		}
	}

	public sealed class FrameExecutionReport {
		private readonly IReadOnlyList<RuntimePhase> _phases;
		public ulong FrameNumber { get; }
		public bool Succeeded { get; }
		public FrameSnapshot Snapshot { get; }
		public OutputPresentation Presentation { get; }
		public ProgramRuntimeState ProgramState { get; }
		public IReadOnlyList<RuntimePhase> Phases => _phases;
		public IReadOnlyList<Diagnostic> Diagnostics { get; }
		public IReadOnlyList<UnitResult<Diagnostic>> GraphCommandResults { get; }
		public IReadOnlyList<GraphCommandExecutionResult> GraphCommandExecutionResults { get; }
		public IReadOnlyList<ParameterEventResult> ParameterEventResults { get; }
		public IReadOnlyList<RuntimeCommandExecutionResult> RuntimeCommandResults { get; }
		internal FrameExecutionReport(ulong frameNumber, bool succeeded, FrameSnapshot snapshot, OutputPresentation presentation, ProgramRuntimeState programState, IEnumerable<RuntimePhase> phases, IEnumerable<Diagnostic> diagnostics, IEnumerable<UnitResult<Diagnostic>> graphCommandResults = null, IEnumerable<GraphCommandExecutionResult> graphCommandExecutionResults = null, IEnumerable<ParameterEventResult> parameterEventResults = null, IEnumerable<RuntimeCommandExecutionResult> runtimeCommandResults = null) {
			FrameNumber = frameNumber; Succeeded = succeeded; Snapshot = snapshot; Presentation = presentation; ProgramState = programState;
			_phases = new ReadOnlyCollection<RuntimePhase>((phases ?? Enumerable.Empty<RuntimePhase>()).ToList());
			Diagnostics = new ReadOnlyCollection<Diagnostic>((diagnostics ?? Enumerable.Empty<Diagnostic>()).ToList());
			GraphCommandResults = new ReadOnlyCollection<UnitResult<Diagnostic>>((graphCommandResults ?? Enumerable.Empty<UnitResult<Diagnostic>>()).ToList());
			GraphCommandExecutionResults = new ReadOnlyCollection<GraphCommandExecutionResult>((graphCommandExecutionResults ?? Enumerable.Empty<GraphCommandExecutionResult>()).ToList());
			ParameterEventResults = new ReadOnlyCollection<ParameterEventResult>((parameterEventResults ?? Enumerable.Empty<ParameterEventResult>()).ToList());
			RuntimeCommandResults = new ReadOnlyCollection<RuntimeCommandExecutionResult>((runtimeCommandResults ?? Enumerable.Empty<RuntimeCommandExecutionResult>()).ToList());
		}
	}

	internal sealed class RuntimeQueue<T> {
		private readonly object _gate = new object();
		private readonly Queue<T> _items = new Queue<T>();
		private readonly int _capacity;
		public RuntimeQueue(int capacity) { _capacity = capacity; }
		public bool TryEnqueue(T item) {
			lock (_gate) {
				if (_items.Count >= _capacity) return false;
				_items.Enqueue(item); return true;
			}
		}

		/// <summary>Atomically replaces every pending value with the latest
		/// state snapshot. This is only for state-setting channels such as
		/// output demand; ordered command/event queues continue to use
		/// TryEnqueue and retain their overflow semantics.</summary>
		public void ReplaceLatest(T item) {
			lock (_gate) {
				_items.Clear();
				_items.Enqueue(item);
			}
		}
		public List<T> Drain() {
			lock (_gate) {
				var result = _items.ToList();
				_items.Clear();
				return result;
			}
		}
	}

	/// <summary>
	/// Main-thread frame boundary coordinator. Every Tick owns one immutable
	/// FrameSnapshot and catches node-local failures without stopping peers.
	/// </summary>
	public sealed class FrameCoordinator {
		public const int QueueCapacity = 4096;
		private readonly RuntimeSession _session;
		private readonly GraphClock _clock;
		private readonly RuntimeQueue<GraphEditCommand> _graphQueue = new RuntimeQueue<GraphEditCommand>(QueueCapacity);
		private readonly RuntimeQueue<RuntimeParameterEvent> _parameterQueue = new RuntimeQueue<RuntimeParameterEvent>(QueueCapacity);
		private readonly RuntimeQueue<RuntimeCommand> _runtimeQueue = new RuntimeQueue<RuntimeCommand>(QueueCapacity);
		private bool _ticking;
		private ulong _frameNumber;
		private readonly List<UnitResult<Diagnostic>> _graphCommandResults = new List<UnitResult<Diagnostic>>();
		private readonly List<GraphCommandExecutionResult> _graphCommandExecutionResults = new List<GraphCommandExecutionResult>();
		private readonly List<ParameterEventResult> _parameterEventResults = new List<ParameterEventResult>();
		private readonly List<RuntimeCommandExecutionResult> _runtimeCommandResults = new List<RuntimeCommandExecutionResult>();
		private readonly Dictionary<NodeInstanceId, NodeOutputResult> _heldPreviews = new Dictionary<NodeInstanceId, NodeOutputResult>();

		public RuntimeSession Session => _session;
		public GraphClock Clock => _clock;
		public ulong FrameNumber => _frameNumber;
		public bool IsTicking => _ticking;

		public FrameCoordinator(RuntimeSession session, GraphClock clock = null) {
			_session = session ?? throw new ArgumentNullException(nameof(session));
			_clock = clock ?? new GraphClock();
		}

		public UnitResult<Diagnostic> EnqueueGraphEdit(GraphEditCommand command) {
			if (command == null) return UnitResult.Failure<Diagnostic>(Failure("runtime.queue.invalid", "Graph command is required."));
			return _graphQueue.TryEnqueue(command) ? UnitResult.Success<Diagnostic>() : QueueFull("graph");
		}

		public UnitResult<Diagnostic> EnqueueParameterEvent(RuntimeParameterEvent item) {
			if (item == null) return UnitResult.Failure<Diagnostic>(Failure("runtime.queue.invalid", "Parameter event is required."));
			return _parameterQueue.TryEnqueue(item) ? UnitResult.Success<Diagnostic>() : QueueFull("parameter");
		}

		public UnitResult<Diagnostic> EnqueueRuntimeCommand(RuntimeCommand command) {
			if (command == null) return UnitResult.Failure<Diagnostic>(Failure("runtime.queue.invalid", "Runtime command is required."));
			return _runtimeQueue.TryEnqueue(command) ? UnitResult.Success<Diagnostic>() : QueueFull("runtime");
		}

		public FrameExecutionReport Tick() {
			return Tick(_clock == null ? 0d : double.NaN);
		}

		public FrameExecutionReport Tick(double monotonicTime) {
			if (_ticking) {
				var diagnostic = Failure("runtime.frame.reentrant", "FrameCoordinator.Tick cannot be re-entered.");
				_session.Diagnostics.Report(diagnostic);
				return new FrameExecutionReport(_frameNumber, false, _session.LastSnapshot, EmptyPresentation(), ProgramRuntimeState.HoldingLastFrame, new[] { RuntimePhase.Finalization }, new[] { diagnostic });
			}

			_ticking = true;
			var frame = ++_frameNumber;
			var phases = new List<RuntimePhase>();
			var diagnostics = new List<Diagnostic>();
			var graphCommands = _graphQueue.Drain();
			var parameterEvents = _parameterQueue.Drain();
			var runtimeCommands = _runtimeQueue.Drain();
			var completions = _session.DrainCompletions();
			var demandRequests = _session.DrainDemandRequests();
			// Phase 0 reads the source exactly once. Graph time itself is
			// advanced at Phase 3 after parameter/control commits. Keep the
			// read inside the guarded Tick body so a faulty clock source still
			// releases the re-entry guard and reaches safe finalization.
			var frameMonotonicTime = monotonicTime;
			FrameSnapshot snapshot = null;
			FrameEvaluationContext evaluation = null;
			OutputPresentation presentation = EmptyPresentation();
			var succeeded = true;
			try {
				frameMonotonicTime = double.IsNaN(monotonicTime) ? _clock.ReadMonotonicTime() : monotonicTime;
				_session.Diagnostics.BeginFrame(frame);
				phases.Add(RuntimePhase.BoundaryIntake);
				_session.Parameters.BeginFrame();
				ApplyRuntimeCommands(runtimeCommands, diagnostics, frameMonotonicTime);
				ProcessCompletions(completions, diagnostics);

				phases.Add(RuntimePhase.GraphEdit);
				ProcessGraphEdits(graphCommands, diagnostics);

				phases.Add(RuntimePhase.ParameterAndControlCommit);
				ProcessParameterEvents(parameterEvents, diagnostics);

				phases.Add(RuntimePhase.FrameSnapshot);
				_clock.Update(frameMonotonicTime);
				var effective = _session.Parameters.EvaluateEffective(_session.GraphEditor.State, _session.Document, _session.Diagnostics);
				if (effective.IsFailure) diagnostics.Add(effective.Error);
				snapshot = new FrameSnapshot(frame, _clock.Time, _clock.IsPaused, _session.Document.DocumentRevision, _session.Plan == null ? _session.GraphEditor.State.Revision : _session.Plan.SourceRevision, _session.ResolutionProjection, _session.Parameters.FrameValues, _session.OutputDemandSnapshot);

				phases.Add(RuntimePhase.OutputDemand);
				evaluation = ProcessOutputDemandPhase(snapshot, demandRequests, frame, diagnostics);
				_session.NotifyDemandChanges(evaluation, diagnostics);
				_session.LastSnapshot = snapshot;

				phases.Add(RuntimePhase.ResourcePreparation);
				if (_session.ResourcePreparation != null) {
					try {
						var prepared = _session.ResourcePreparation is IRuntimeResourcePreparationWithPlan withPlan
							? withPlan.Prepare(snapshot, evaluation)
							: _session.ResourcePreparation.Prepare(snapshot);
						if (prepared.IsFailure) {
							// A failed Phase-5 preparation means this frame
							// cannot satisfy its propagated resource demand.
							// Keep the last normal presentation, but expose a
							// failed frame result so callers can retain an
							// active lease and retry the candidate later.
							succeeded = false;
							diagnostics.Add(prepared.Error);
						}
					}
					catch (Exception exception) {
						// A preparation exception is still a Phase-5
						// failure.  Do not let evaluation continue and then
						// report a successful frame with an unprepared
						// output; the active lease must remain the last
						// valid presentation and the candidate can be
						// retried on a later frame.
						succeeded = false;
						diagnostics.Add(new Diagnostic(new DiagnosticCode("runtime.resource.preparation_failed"), Severity.Error, "Resource preparation failed.", frameNumber: (long)frame, graphClockTime: _clock.Time, module: "runtime", exception: DiagnosticExceptionInfo.FromException(exception)));
					}
				}

				phases.Add(RuntimePhase.NodeEvaluation);
				_session.ClearFrameResults();
				EvaluateNodes(evaluation, diagnostics);

				phases.Add(RuntimePhase.FeedbackCommit);
				CommitFeedback(evaluation, diagnostics);

				phases.Add(RuntimePhase.Presentation);
				presentation = Present(evaluation, diagnostics);
				_session.LastPresentation = presentation;
			}
			catch (Exception exception) {
				succeeded = false;
				var diagnostic = new Diagnostic(new DiagnosticCode("runtime.frame.exception"), Severity.Error, "FrameCoordinator encountered an unexpected exception.", frameNumber: (long)frame, graphClockTime: _clock.Time, module: "runtime", exception: DiagnosticExceptionInfo.FromException(exception));
				diagnostics.Add(diagnostic);
				_session.Diagnostics.BeginOrContinueFault(diagnostic);
				// Do not manufacture phase entries after a failure. Feedback
				// and presentation remain uncommitted unless their phase was
				// actually reached; the last successful Program is retained.
				presentation = PresentLastProgram(diagnostics);
			}
			finally {
				// Retiring node handles leave the active plan at Phase 1 and
				// release Unity-owned resources only at the Phase 9 boundary.
				try { _session.FinalizeRetiring(); }
				catch (Exception exception) {
					succeeded = false;
					diagnostics.Add(new Diagnostic(new DiagnosticCode("runtime.node.finalization_failed"), Severity.Error, "Retiring runtime node cleanup failed.", frameNumber: (long)frame, graphClockTime: _clock.Time, module: "runtime", exception: DiagnosticExceptionInfo.FromException(exception)));
				}
				if (snapshot != null && _session.ResourceFinalization != null) {
					try {
						var finalized = _session.ResourceFinalization is IRuntimeResourceFinalizationWithPlan withPlan
							? withPlan.Finalize(snapshot, evaluation, succeeded)
							: _session.ResourceFinalization.Finalize(snapshot, succeeded);
						if (finalized.IsFailure) { succeeded = false; diagnostics.Add(finalized.Error); }
					}
					catch (Exception exception) {
						succeeded = false;
						diagnostics.Add(new Diagnostic(new DiagnosticCode("runtime.resource.finalization_failed"), Severity.Error, "Resource finalization failed.", frameNumber: (long)frame, graphClockTime: _clock.Time, module: "runtime", exception: DiagnosticExceptionInfo.FromException(exception)));
					}
				}
				if (!phases.Contains(RuntimePhase.Finalization)) phases.Add(RuntimePhase.Finalization);
				foreach (var diagnostic in diagnostics) _session.Diagnostics.Report(diagnostic);
				_session.Diagnostics.CompleteFrame(frame, _clock.Time);
				_ticking = false;
			}
			return new FrameExecutionReport(frame, succeeded, snapshot, presentation, _session.ProgramState, phases, diagnostics, _graphCommandResults, _graphCommandExecutionResults, _parameterEventResults, _runtimeCommandResults);
		}

		private void ApplyRuntimeCommands(IEnumerable<RuntimeCommand> commands, ICollection<Diagnostic> diagnostics, double frameMonotonicTime) {
			_runtimeCommandResults.Clear();
			foreach (var command in commands ?? Enumerable.Empty<RuntimeCommand>()) {
				if (command == null) continue;
				Diagnostic failure = null;
				switch (command.Kind) {
					case RuntimeCommandKind.PauseClock: _clock.Pause(); break;
					case RuntimeCommandKind.ResumeClock: _clock.Resume(); break;
					case RuntimeCommandKind.ResetClock: _clock.Reset(0d, frameMonotonicTime); break;
					case RuntimeCommandKind.ResetFeedback:
						if (!command.NodeId.HasValue) {
							failure = Failure("runtime.feedback.reset_target_missing", "Feedback reset requires a target node.");
							diagnostics.Add(failure);
						}
						else if (_session.FeedbackCommitter is IFeedbackResetter resetter) {
							try {
								var reset = resetter.Reset(command.NodeId.Value);
								if (reset.IsFailure) { failure = reset.Error; diagnostics.Add(reset.Error); }
							}
							catch (Exception exception) {
								failure = new Diagnostic(new DiagnosticCode("runtime.feedback.reset_failed"), Severity.Error, "Feedback history reset failed.", nodeId: command.NodeId, frameNumber: (long)_frameNumber, graphClockTime: _clock.Time, module: "runtime", exception: DiagnosticExceptionInfo.FromException(exception));
								diagnostics.Add(failure);
							}
						}
						else {
							failure = Failure("runtime.feedback.reset_unavailable", "Feedback reset is unavailable for this runtime session.");
							diagnostics.Add(failure);
						}
						break;
				}
				_runtimeCommandResults.Add(new RuntimeCommandExecutionResult(command.CommandRequestId, command.Kind, failure == null, failure));
			}
		}

		private void ProcessGraphEdits(IEnumerable<GraphEditCommand> commands, ICollection<Diagnostic> diagnostics) {
			_graphCommandResults.Clear();
			_graphCommandExecutionResults.Clear();
			var batch = (commands ?? Enumerable.Empty<GraphEditCommand>()).Where(x => x != null).ToList();
			if (batch.Count > 0) {
				// Build without touching graph state/history. Persistence is
				// the first half of the transaction; only a successful write
				// permits the non-failing candidate commit below.
				var detailed = _session.GraphEditor.PrepareBatchDetailed(batch, _session.OutputDemandSnapshot);
				_graphCommandResults.AddRange(detailed.CommandResults);
				for (var i = 0; i < detailed.CommandResults.Count && i < batch.Count; i++)
					_graphCommandExecutionResults.Add(new GraphCommandExecutionResult(batch[i].CommandRequestId, detailed.CommandResults[i]));
				if (detailed.Patch != null) {
					var persisted = _session.Persistence.ApplyGraphPatch(detailed.Patch);
					if (persisted.IsFailure) {
						// PrepareBatchDetailed is non-destructive, so a failed
						// document write leaves every graph/history value intact.
						diagnostics.Add(persisted.Error);
						ReplaceSuccessfulGraphResultsWithFailure(persisted.Error);
					}
					else {
						var committed = _session.GraphEditor.CommitCandidate(detailed.Patch);
						if (committed.IsFailure) {
							// CommitCandidate validates the unchanged source
							// state before mutating. A conflict is unexpected
							// on the main-thread boundary; report it clearly.
							diagnostics.Add(committed.Error);
							ReplaceSuccessfulGraphResultsWithFailure(committed.Error);
							return;
						}
						// ApplyBatchDetailed already built and validated this
						// exact candidate plan. Installing it avoids a second
						// plan build in the same frame.
						_session.InstallPlan(detailed.Plan);
					}
				}
				else if (detailed.Diagnostic != null) {
					diagnostics.Add(detailed.Diagnostic);
					ReplaceSuccessfulGraphResultsWithFailure(detailed.Diagnostic);
				}
			}
		}

		private void ReplaceSuccessfulGraphResultsWithFailure(Diagnostic diagnostic) {
			for (var i = 0; i < _graphCommandExecutionResults.Count; i++) {
				var item = _graphCommandExecutionResults[i];
				if (!item.Result.IsSuccess) continue;
				_graphCommandExecutionResults[i] = new GraphCommandExecutionResult(item.CommandRequestId, UnitResult.Failure<Diagnostic>(diagnostic));
				if (i < _graphCommandResults.Count) _graphCommandResults[i] = UnitResult.Failure<Diagnostic>(diagnostic);
			}
		}

		private void ProcessParameterEvents(IEnumerable<RuntimeParameterEvent> events, ICollection<Diagnostic> diagnostics) {
			var state = _session.Parameters.CaptureState();
			var result = _session.Parameters.ApplyEvents(events, _session.GraphEditor.State, _session.Document);
			_parameterEventResults.Clear();
			_parameterEventResults.AddRange(result.EventResults);
			foreach (var diagnostic in result.Diagnostics) {
				diagnostics.Add(diagnostic);
			}
			if (result.ChangedBaseValues.Count > 0) {
				var updates = result.ChangedBaseValues.Select(x => new BaseValueUpdate(x.Key.NodeId, x.Key.ParameterId, x.Value)).ToList();
				var persisted = _session.Persistence.ApplyParameterTransaction(updates);
				if (persisted.IsFailure) {
					_session.Parameters.RestoreState(state);
					diagnostics.Add(persisted.Error);
					for (var i = 0; i < _parameterEventResults.Count; i++) {
						var item = _parameterEventResults[i];
						if (item.Applied)
							_parameterEventResults[i] = new ParameterEventResult(item.SequenceNumber, item.Kind, false, persisted.Error);
					}
				}
			}
		}

		private void ProcessCompletions(IEnumerable<RuntimeCompletion> completions, ICollection<Diagnostic> diagnostics) {
			foreach (var completion in completions ?? Enumerable.Empty<RuntimeCompletion>()) {
				var handle = _session.FindNode(completion.NodeId);
				var graphRevision = _session.GraphEditor.State.Revision;
				if (handle == null || handle.GenerationId != completion.GenerationId
					|| (completion.DocumentRevision.HasValue && completion.DocumentRevision.Value != _session.Document.DocumentRevision)
					|| (completion.GraphRevision.HasValue && completion.GraphRevision.Value != graphRevision)) {
					try { completion.Discard(_session); } catch (Exception exception) { diagnostics.Add(new Diagnostic(new DiagnosticCode("runtime.completion.discard_failed"), Severity.Error, "Stale runtime completion cleanup failed.", nodeId: completion.NodeId, generationId: completion.GenerationId, frameNumber: (long)_frameNumber, graphClockTime: _clock.Time, module: "runtime", exception: DiagnosticExceptionInfo.FromException(exception))); }
					continue;
				}
				try {
					var result = completion.Apply(_session);
					if (result.IsFailure) diagnostics.Add(result.Error);
				}
				catch (Exception exception) {
					diagnostics.Add(new Diagnostic(new DiagnosticCode("runtime.completion.failed"), Severity.Error, "Runtime completion failed.", nodeId: completion.NodeId, generationId: completion.GenerationId, frameNumber: (long)_frameNumber, graphClockTime: _clock.Time, module: "runtime", exception: DiagnosticExceptionInfo.FromException(exception)));
				}
			}
		}

		private FrameEvaluationContext ProcessOutputDemandPhase(FrameSnapshot snapshot, IEnumerable<IReadOnlyList<OutputDemand>> requests, ulong frame, ICollection<Diagnostic> diagnostics) {
			var requestChanged = false;
			foreach (var request in requests ?? Enumerable.Empty<IReadOnlyList<OutputDemand>>())
				requestChanged |= _session.ApplyDemandRequest(request);
			var scheduledChanged = _session.PlanDemandsForFrame(frame);

			// Demand is intentionally applied here, rather than at SetOutput-
			// Demands time. Rebuild only when the effective frame demand or
			// graph plan is different; this keeps a steady frame at one plan.
			if (requestChanged || scheduledChanged || _session.Plan == null) {
				var rebuilt = _session.RebuildPlan();
				if (rebuilt.IsFailure) {
					diagnostics.Add(rebuilt.Error);
					return new FrameEvaluationContext(snapshot, _session.ResolutionProjection ?? snapshot.ResolutionProjection, _session.OutputDemandSnapshot);
				}
			}
			return new FrameEvaluationContext(snapshot, _session.ResolutionProjection ?? snapshot.ResolutionProjection, _session.OutputDemandSnapshot);
		}

		private void EvaluateNodes(FrameEvaluationContext evaluation, ICollection<Diagnostic> diagnostics) {
			var snapshot = evaluation?.Snapshot;
			var plan = _session.Plan;
			if (plan == null) return;
			var state = _session.GraphEditor.State;
			foreach (var nodeId in plan.EvaluationOrder) {
				var record = state.FindNode(nodeId);
				if (record == null || !record.Enabled || record.IsUnknown) continue;
				var existingHandle = _session.FindNode(record.Id);
				if (existingHandle != null) _session.Diagnostics.MarkNodeEvaluated(record.Id, existingHandle.GenerationId);
				var requested = plan.RequestedOutputs.TryGetValue(nodeId, out var requestedOutputs) ? requestedOutputs : new ReadOnlyCollection<PortId>(new List<PortId>());
				var inputs = ResolveInputs(record, state, snapshot, evaluation.OutputDemands, diagnostics);
				var blocked = inputs.Values.FirstOrDefault(x => (x.Status == InputResolutionStatus.Unavailable || x.Status == InputResolutionStatus.Faulted) && record.FindPort(x.PortId)?.Required == true);
				if (blocked.PortId.IsEmpty == false) {
					var reason = blocked.Diagnostic ?? Failure("runtime.input.blocked", "Required input is unavailable.", record.Id, blocked.PortId);
					_session.SetNodeResults(record.Id, blocked.Status == InputResolutionStatus.Faulted ? FaultAll(requested, reason) : BlockAll(requested, reason));
					continue;
				}

				// Program and Preview are terminal presentation nodes.  Their
				// image input is the presentation result; they intentionally
				// have no catalog/runtime factory output port.  Keeping this
				// resolver here also means a healthy input reaches Preview
				// surfaces instead of being treated as a missing output.
				if (record.TypeId.Value == GraphConstants.ProgramOutputTypeId
					|| record.TypeId.Value == GraphConstants.PreviewTypeId) {
					var programInput = inputs.TryGetValue(new PortId(GraphConstants.ImagePortId), out var resolved) ? resolved : ResolvedInput.Unavailable(new PortId(GraphConstants.ImagePortId), PortType.ImageFrame, Failure("runtime.program.input_missing", "Program input is unavailable.", record.Id, new PortId(GraphConstants.ImagePortId)));
					var programResult = programInput.HasValue ? NodeOutputResult.Available(programInput.Value) : NodeOutputResult.Blocked(programInput.Diagnostic ?? Failure("runtime.program.input_missing", "Program input is unavailable.", record.Id, new PortId(GraphConstants.ImagePortId)));
					_session.SetNodeResults(record.Id, new ReadOnlyDictionary<PortId, NodeOutputResult>(new Dictionary<PortId, NodeOutputResult> { [new PortId(GraphConstants.ImagePortId)] = programResult }));
					continue;
				}

				var handle = existingHandle;
				if (handle == null) {
					var unavailable = Failure("runtime.node.unavailable", "Runtime node factory is not registered.", record.Id, typeId: record.TypeId);
					_session.Diagnostics.BeginOrContinueFault(unavailable);
					diagnostics.Add(unavailable);
					_session.SetNodeResults(record.Id, FaultAll(requested, unavailable));
					continue;
				}
				var evaluationIndex = plan.TryGetEvaluationIndex(record.Id, out var resolvedIndex) ? resolvedIndex : -1;
				var context = new NodeExecutionContext(snapshot, evaluation.ResolutionProjection, evaluation.OutputDemands, record.Id, evaluationIndex, requested, inputs, _session.Diagnostics, _session.OutputSurfaces);
				var writer = new NodeOutputWriter(requested, trustedFrozenRequestedOutputs: true);
				try {
					handle.Node.Evaluate(context, writer);
					var output = ValidateOutputs(record, writer.Seal(), snapshot, diagnostics);
					_session.SetNodeResults(record.Id, output);
					var hasFault = false;
					foreach (var result in output.Values.Where(x => x.Status == NodeOutputStatus.Faulted && x.Diagnostic != null)) {
						hasFault = true;
						var fault = result.Diagnostic;
						_session.Diagnostics.BeginOrContinueFault(fault);
						diagnostics.Add(fault);
					}
					// A node can hold its last valid image while its next
					// frame prepares. Its runtime state must still reach the
					// public graph; output availability alone would erase
					// that normal asynchronous transition.
					if (hasFault || handle.Node.State == RuntimeNodeState.Faulted) handle.MarkFaulted();
					else if (handle.Node.State == RuntimeNodeState.Preparing || output.Values.Any(x => x.Status == NodeOutputStatus.Preparing)) handle.MarkPreparing();
					else handle.MarkReady();
				}
				catch (Exception exception) {
					var fault = new Diagnostic(new DiagnosticCode("runtime.node.evaluate_failed"), Severity.Error, "Runtime node evaluation failed.", nodeId: record.Id, nodeTypeId: record.TypeId, generationId: handle.GenerationId, frameNumber: (long)snapshot.FrameNumber, graphClockTime: snapshot.GraphClockTime, module: "runtime", exception: DiagnosticExceptionInfo.FromException(exception));
					_session.Diagnostics.BeginOrContinueFault(fault);
					diagnostics.Add(fault);
					_session.SetNodeResults(record.Id, FaultAll(requested, fault));
					handle.MarkFaulted();
				}
			}
		}

		private void CommitFeedback(FrameEvaluationContext evaluation, ICollection<Diagnostic> diagnostics) {
			var snapshot = evaluation?.Snapshot;
			var plan = _session.Plan;
			if (_session.FeedbackCommitter == null || plan == null) return;
			foreach (var nodeId in plan.FeedbackCommitNodeIds) {
				var input = ResolveFeedbackInput(nodeId);
				var inputResult = input.HasValue ? NodeOutputResult.Available(input.Value) : NodeOutputResult.Blocked(input.Diagnostic ?? Failure("runtime.feedback.input_missing", "Feedback input is unavailable."));
				var result = _session.FeedbackCommitter.Commit(nodeId, inputResult, snapshot);
				if (result.IsFailure) { diagnostics.Add(result.Error); _session.Diagnostics.BeginOrContinueFault(result.Error); }
			}
		}

		private OutputPresentation Present(FrameEvaluationContext evaluation, ICollection<Diagnostic> diagnostics) {
			var plan = _session.Plan;
			if (plan == null) return PresentLastProgram(diagnostics);
			var programResult = FindOutput(plan.ProgramOutputNodeId, new PortId(GraphConstants.ImagePortId));
			if (programResult.HasValue && programResult.Status == NodeOutputStatus.Available) {
				_session.LastProgramResult = programResult;
				_session.HasLastProgramFrame = true;
				_session.ProgramState = ProgramRuntimeState.Available;
			}
			else _session.ProgramState = _session.HasLastProgramFrame ? ProgramRuntimeState.HoldingLastFrame : ProgramRuntimeState.OpaqueBlack;
			var shownProgram = _session.HasLastProgramFrame ? _session.LastProgramResult : programResult;
			var previews = new Dictionary<NodeInstanceId, NodeOutputResult>();
			var previewPort = new PortId(GraphConstants.ImagePortId);
			// A Preview is evaluated only on its quality-policy cadence, but
			// its operator surface stays visible between due frames. Iterate
			// the requested views here so a non-due frame retains the last
			// presented result rather than disappearing from the public model.
			foreach (var demand in _session.RequestedOutputDemands.Where(x => x.TargetKind == OutputTargetKind.Preview)) {
				NodeOutputResult preview;
				var due = (evaluation.OutputDemands ?? new List<OutputDemand>()).Any(x => x.TargetKind == OutputTargetKind.Preview
					&& x.NodeId == demand.NodeId && x.OutputPortId == demand.OutputPortId);
				if (due && plan.PreviewOutputNodeIds.Contains(demand.NodeId)) {
					preview = FindOutput(demand.NodeId, previewPort);
					_heldPreviews[demand.NodeId] = preview;
				}
				else if (!_heldPreviews.TryGetValue(demand.NodeId, out preview))
					preview = NodeOutputResult.Preparing(Failure("runtime.preview.waiting", "Preview is waiting for its next due frame."));
				previews[demand.NodeId] = preview;
			}
			return new OutputPresentation(shownProgram, new ReadOnlyDictionary<NodeInstanceId, NodeOutputResult>(previews));
		}

		private OutputPresentation PresentLastProgram(ICollection<Diagnostic> diagnostics) {
			var program = _session.HasLastProgramFrame ? _session.LastProgramResult : default(NodeOutputResult);
			_session.ProgramState = _session.HasLastProgramFrame ? ProgramRuntimeState.HoldingLastFrame : ProgramRuntimeState.OpaqueBlack;
			return new OutputPresentation(program, new ReadOnlyDictionary<NodeInstanceId, NodeOutputResult>(new Dictionary<NodeInstanceId, NodeOutputResult>()));
		}

		private NodeOutputResult FindOutput(NodeInstanceId nodeId, PortId portId) {
			return _session.TryGetResults(nodeId, out var results) && results.TryGetValue(portId, out var result)
				? result
				: NodeOutputResult.Blocked(Failure("runtime.output.missing", "Requested output is unavailable.", nodeId, portId));
		}

		private IReadOnlyDictionary<PortId, ResolvedInput> ResolveInputs(NodeRecord record, GraphState state, FrameSnapshot snapshot, IReadOnlyList<OutputDemand> demands, ICollection<Diagnostic> diagnostics) {
			var result = new Dictionary<PortId, ResolvedInput>();
			foreach (var port in record.Ports.Where(x => x.Direction == PortDirection.Input)) {
				var edge = state.Connections.FirstOrDefault(x => x.DestinationNodeId == record.Id && x.DestinationPortId == port.Id && !x.IsBroken);
				if (edge == null) {
					if (!port.Required && port.Type == PortType.ImageFrame && port.DefaultImage.HasValue && _session.DefaultImageProvider != null) {
						var image = _session.DefaultImageProvider.Get(ToRuntimeDefaultImageKind(port.DefaultImage.Value), snapshot == null ? 1920 : demands.FirstOrDefault(x => x.TargetKind == OutputTargetKind.Program)?.Width ?? 1920, snapshot == null ? 1080 : demands.FirstOrDefault(x => x.TargetKind == OutputTargetKind.Program)?.Height ?? 1080, snapshot == null ? _frameNumber : snapshot.FrameNumber);
						result[port.Id] = image.IsSuccess ? ResolvedInput.Fallback(port.Id, port.Type, image.Value, Failure("runtime.input.fallback", "Optional Image input is using its declared default.", record.Id, port.Id)) : ResolvedInput.Unavailable(port.Id, port.Type, image.Error);
					}
					else if (!port.Required && port.Type != PortType.ImageFrame) result[port.Id] = ResolvedInput.Fallback(port.Id, port.Type, PortValue.Default(port.Type), Failure("runtime.input.fallback", "Optional input is using its default value.", record.Id, port.Id));
					else result[port.Id] = ResolvedInput.Unavailable(port.Id, port.Type, Failure("runtime.input.missing", "Input is not connected.", record.Id, port.Id));
					continue;
				}
				var sourceResult = FindOutput(edge.SourceNodeId, edge.SourcePortId);
				if (sourceResult.Status != NodeOutputStatus.Available || !sourceResult.HasValue) {
					if (!port.Required && port.Type == PortType.ImageFrame && port.DefaultImage.HasValue && _session.DefaultImageProvider != null) {
						var image = _session.DefaultImageProvider.Get(ToRuntimeDefaultImageKind(port.DefaultImage.Value), demands.FirstOrDefault(x => x.TargetKind == OutputTargetKind.Program)?.Width ?? 1920, demands.FirstOrDefault(x => x.TargetKind == OutputTargetKind.Program)?.Height ?? 1080, snapshot.FrameNumber);
						result[port.Id] = image.IsSuccess ? ResolvedInput.Fallback(port.Id, port.Type, image.Value, sourceResult.Diagnostic) : ResolvedInput.Unavailable(port.Id, port.Type, image.Error);
					}
					else if (!port.Required && port.Type != PortType.ImageFrame) result[port.Id] = ResolvedInput.Fallback(port.Id, port.Type, PortValue.Default(port.Type), sourceResult.Diagnostic);
					else if (sourceResult.Status == NodeOutputStatus.Faulted) result[port.Id] = ResolvedInput.Faulted(port.Id, port.Type, sourceResult.Diagnostic ?? Failure("runtime.input.upstream_faulted", "Upstream output is faulted.", record.Id, port.Id));
					else result[port.Id] = ResolvedInput.Unavailable(port.Id, port.Type, sourceResult.Diagnostic ?? Failure("runtime.input.upstream_unavailable", "Upstream output is unavailable.", record.Id, port.Id));
					continue;
				}
				var converted = Convert(edge, sourceResult.Value, port.Type);
				result[port.Id] = converted.IsSuccess ? ResolvedInput.Available(port.Id, port.Type, converted.Value) : ResolvedInput.Unavailable(port.Id, port.Type, converted.Error);
			}
			return new ReadOnlyDictionary<PortId, ResolvedInput>(result);
		}

		private static RuntimeDefaultImageKind ToRuntimeDefaultImageKind(DefaultImageKind kind) {
			switch (kind) {
				case DefaultImageKind.OpaqueWhite: return RuntimeDefaultImageKind.OpaqueWhite;
				case DefaultImageKind.OpaqueBlack: return RuntimeDefaultImageKind.OpaqueBlack;
				default: return RuntimeDefaultImageKind.TransparentBlack;
			}
		}

		private Result<PortValue, Diagnostic> Convert(ConnectionRecord edge, PortValue value, PortType destination) {
			if (value.Type == destination) return Result.Success<PortValue, Diagnostic>(value);
			if (value.Type == PortType.Color && destination == PortType.Vector4 && edge.ConversionId == GraphConstants.ColorToVector4ConversionId) {
				var color = value.AsColor(); return Result.Success<PortValue, Diagnostic>(PortValue.FromVector4(new Vector4Value(color.R, color.G, color.B, color.A)));
			}
			if (value.Type == PortType.Vector4 && destination == PortType.Color && edge.ConversionId == GraphConstants.Vector4ToColorConversionId) {
				var vector = value.AsVector4(); return Result.Success<PortValue, Diagnostic>(PortValue.FromColor(new ColorValue(vector.X, vector.Y, vector.Z, vector.W)));
			}
			return Result.Failure<PortValue, Diagnostic>(Failure("runtime.input.conversion_failed", "Saved port conversion is missing or incompatible."));
		}

		private ResolvedInput ResolveFeedbackInput(NodeInstanceId nodeId) {
			var record = _session.GraphEditor.State.FindNode(nodeId);
			if (record == null) return ResolvedInput.Unavailable(new PortId("input"), PortType.ImageFrame, Failure("runtime.feedback.missing", "Feedback node is missing."));
			var state = _session.GraphEditor.State;
			var edge = state.Connections.FirstOrDefault(x => x.DestinationNodeId == nodeId && x.DestinationPortId.Value == "input" && !x.IsBroken);
			if (edge == null) return ResolvedInput.Unavailable(new PortId("input"), PortType.ImageFrame, Failure("runtime.feedback.input_missing", "Feedback input is unavailable."));
			var output = FindOutput(edge.SourceNodeId, edge.SourcePortId);
			return output.Status == NodeOutputStatus.Available ? ResolvedInput.Available(new PortId("input"), PortType.ImageFrame, output.Value) : ResolvedInput.Unavailable(new PortId("input"), PortType.ImageFrame, output.Diagnostic);
		}

		private static IReadOnlyDictionary<PortId, NodeOutputResult> ValidateOutputs(NodeRecord record, IReadOnlyDictionary<PortId, NodeOutputResult> outputs, FrameSnapshot snapshot, ICollection<Diagnostic> diagnostics) {
			Dictionary<PortId, NodeOutputResult> repaired = null;
			foreach (var pair in outputs) {
				var port = record.FindPort(pair.Key);
				NodeOutputResult replacement = default(NodeOutputResult);
				var invalid = false;
				if (port == null || port.Direction != PortDirection.Output) {
					replacement = NodeOutputResult.Faulted(Failure("runtime.output.invalid_port", "Runtime node wrote an undefined output port.", record.Id, pair.Key));
					invalid = true;
				}
				else if (pair.Value.Status == NodeOutputStatus.Available && (!pair.Value.HasValue || pair.Value.Value.Type != port.Type)) {
					replacement = NodeOutputResult.Faulted(Failure("runtime.output.type_mismatch", "Runtime node returned a value with the wrong port type.", record.Id, pair.Key));
					invalid = true;
				}
				if (!invalid) continue;
				if (repaired == null) repaired = new Dictionary<PortId, NodeOutputResult>(outputs);
				repaired[pair.Key] = replacement;
			}
			return repaired == null ? outputs : new ReadOnlyDictionary<PortId, NodeOutputResult>(repaired);
		}

		private static IReadOnlyDictionary<PortId, NodeOutputResult> BlockAll(IEnumerable<PortId> ports, Diagnostic diagnostic) {
			return new ReadOnlyDictionary<PortId, NodeOutputResult>((ports ?? Enumerable.Empty<PortId>()).ToDictionary(x => x, _ => NodeOutputResult.Blocked(diagnostic)));
		}

		private static IReadOnlyDictionary<PortId, NodeOutputResult> FaultAll(IEnumerable<PortId> ports, Diagnostic diagnostic) {
			return new ReadOnlyDictionary<PortId, NodeOutputResult>((ports ?? Enumerable.Empty<PortId>()).ToDictionary(x => x, _ => NodeOutputResult.Faulted(diagnostic)));
		}

		private static OutputPresentation EmptyPresentation() => new OutputPresentation(default(NodeOutputResult), new ReadOnlyDictionary<NodeInstanceId, NodeOutputResult>(new Dictionary<NodeInstanceId, NodeOutputResult>()));
		private static UnitResult<Diagnostic> QueueFull(string name) => UnitResult.Failure<Diagnostic>(Failure("runtime.queue.overloaded", name + " command queue is full."));
		private static Diagnostic Failure(string code, string message, NodeInstanceId? nodeId = null, PortId? portId = null, NodeTypeId? typeId = null) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: nodeId, portId: portId, nodeTypeId: typeId);

	}
}
