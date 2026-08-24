using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;

namespace ShitDesigner.Runtime {
	public sealed class DiagnosticHistoryEntry {
		public ulong EntryId { get; }
		public Diagnostic Diagnostic { get; }
		public long Count { get; }
		public ulong FirstFrame { get; }
		public ulong LastFrame { get; }
		internal DiagnosticHistoryEntry(ulong entryId, Diagnostic diagnostic, long count, ulong firstFrame, ulong lastFrame) { EntryId = entryId; Diagnostic = diagnostic; Count = count; FirstFrame = firstFrame; LastFrame = lastFrame; }
	}

	public sealed class FaultTracker {
		public ulong EntryId { get; }
		public Diagnostic FirstDiagnostic { get; }
		public ulong FirstFrame { get; internal set; }
		public ulong LastFrame { get; internal set; }
		public long Count { get; internal set; }
		public bool IsPaused { get; internal set; }
		public bool IsClosed { get; internal set; }
		public string CloseReason { get; internal set; }
		internal NodeInstanceId? NodeId { get { return FirstDiagnostic?.NodeId; } }
		internal ulong? GenerationId { get { return FirstDiagnostic?.GenerationId; } }
		internal ulong LastPublishedFrame { get; set; }
		internal FaultTracker(Diagnostic diagnostic, ulong entryId) {
			FirstDiagnostic = diagnostic; EntryId = entryId;
			FirstFrame = diagnostic == null ? 0UL : (ulong)Math.Max(0, diagnostic.FrameNumber);
			LastFrame = FirstFrame; LastPublishedFrame = FirstFrame; Count = 1;
		}
		internal FaultTracker Clone() {
			return new FaultTracker(FirstDiagnostic, EntryId) { LastFrame = LastFrame, Count = Count, IsPaused = IsPaused, IsClosed = IsClosed, CloseReason = CloseReason, LastPublishedFrame = LastPublishedFrame };
		}

		internal void RebaseForMeasurement(ulong frame) {
			FirstFrame = frame;
			LastFrame = frame;
			LastPublishedFrame = frame;
			Count = 1;
			IsPaused = false;
			IsClosed = false;
			CloseReason = null;
		}
	}

	public sealed class DiagnosticHub : IRuntimeDiagnosticSink {
		private readonly object _gate = new object();
		private readonly Dictionary<CurrentConditionKey, Diagnostic> _current = new Dictionary<CurrentConditionKey, Diagnostic>();
		private readonly Dictionary<string, FaultTracker> _faults = new Dictionary<string, FaultTracker>(StringComparer.Ordinal);
		private readonly LinkedList<DiagnosticHistoryEntry> _history = new LinkedList<DiagnosticHistoryEntry>();
		private readonly int _historyCapacity;
		private readonly HashSet<string> _observedFaults = new HashSet<string>(StringComparer.Ordinal);
		private readonly HashSet<string> _evaluatedNodes = new HashSet<string>(StringComparer.Ordinal);
		private ulong _nextEntryId;
		private long _emergencyCount;
		private long _revision;
		private long _snapshotRevision = long.MinValue;
		private IReadOnlyList<Diagnostic> _historySnapshot = new ReadOnlyCollection<Diagnostic>(new List<Diagnostic>());
		private IReadOnlyList<DiagnosticHistoryEntry> _historyEntrySnapshot = new ReadOnlyCollection<DiagnosticHistoryEntry>(new List<DiagnosticHistoryEntry>());
		private IReadOnlyDictionary<CurrentConditionKey, Diagnostic> _currentSnapshot = new ReadOnlyDictionary<CurrentConditionKey, Diagnostic>(new Dictionary<CurrentConditionKey, Diagnostic>());

		// The capacity argument is retained for deterministic tests; the
		// application default is the required 1000-entry ring.
		public DiagnosticHub(string scopeId = null, int historyCapacity = 1000) {
			if (historyCapacity < 1) throw new ArgumentOutOfRangeException(nameof(historyCapacity));
			ScopeId = string.IsNullOrWhiteSpace(scopeId) ? Guid.NewGuid().ToString("D") : scopeId.Trim();
			_historyCapacity = historyCapacity;
		}

		public string ScopeId { get; }
		public long Revision { get { lock (_gate) return _revision; } }
		public long EmergencyCount { get { lock (_gate) return _emergencyCount; } }
		public IReadOnlyList<Diagnostic> History { get { lock (_gate) { EnsureSnapshots(); return _historySnapshot; } } }
		public IReadOnlyList<DiagnosticHistoryEntry> HistoryEntries { get { lock (_gate) { EnsureSnapshots(); return _historyEntrySnapshot; } } }
		public IReadOnlyDictionary<CurrentConditionKey, Diagnostic> CurrentConditions { get { lock (_gate) { EnsureSnapshots(); return _currentSnapshot; } } }
		public IReadOnlyDictionary<string, FaultTracker> ActiveFaults {
			get { lock (_gate) return new ReadOnlyDictionary<string, FaultTracker>(_faults.ToDictionary(x => x.Key, x => x.Value.Clone(), StringComparer.Ordinal)); }
		}

		public void BeginFrame(ulong frameNumber) {
			lock (_gate) { _observedFaults.Clear(); _evaluatedNodes.Clear(); }
		}

		private void EnsureSnapshots() {
			if (_snapshotRevision == _revision) return;
			_historySnapshot = new ReadOnlyCollection<Diagnostic>(_history.Select(x => x.Diagnostic).ToList());
			_historyEntrySnapshot = new ReadOnlyCollection<DiagnosticHistoryEntry>(_history.ToList());
			_currentSnapshot = new ReadOnlyDictionary<CurrentConditionKey, Diagnostic>(new Dictionary<CurrentConditionKey, Diagnostic>(_current));
			_snapshotRevision = _revision;
		}

		public void MarkNodeEvaluated(NodeInstanceId nodeId, ulong generationId) {
			if (nodeId.IsEmpty || generationId == 0) return;
			lock (_gate) _evaluatedNodes.Add(NodeKey(nodeId, generationId));
		}

		public void Report(Diagnostic diagnostic) {
			if (diagnostic == null) return;
			lock (_gate) {
				// BeginOrContinueFault owns its history row. This prevents the
				// FrameCoordinator's final report pass from duplicating it.
				if ((diagnostic.Severity == Severity.Error || diagnostic.Severity == Severity.Fatal) && _faults.ContainsKey(FaultKey(diagnostic))) return;
				AddHistory(diagnostic, 1, FrameOf(diagnostic), FrameOf(diagnostic));
				_revision++;
			}
		}

		public void IncrementEmergency() { lock (_gate) { _emergencyCount++; _revision++; } }
		public void SetCurrent(CurrentConditionKey key, Diagnostic diagnostic) { if (diagnostic != null) lock (_gate) { _current[key] = diagnostic; _revision++; } }
		public void ClearCurrent(CurrentConditionKey key) { lock (_gate) { if (_current.Remove(key)) _revision++; } }

		/// <summary>Starts a measurement scope. Active conditions remain
		/// visible, while history and fault aggregates are rebased so the
		/// measurement artifact has no warm-up prefix.</summary>
		public void ResetMeasurement(ulong measurementFrame = 0) {
			lock (_gate) {
				_history.Clear();
				_observedFaults.Clear();
				_evaluatedNodes.Clear();
				_emergencyCount = 0;
				foreach (var tracker in _faults.Values) {
					tracker.RebaseForMeasurement(measurementFrame);
					AddHistory(tracker.FirstDiagnostic.WithFrame((long)measurementFrame, 0d), 1, measurementFrame, measurementFrame, tracker.EntryId);
				}
				_revision++;
			}
		}

		/// <summary>Starts a measurement scope. Active conditions remain
		/// visible so a warm-up fault cannot be hidden, while history and
		/// aggregate emergency accounting belong only to the new interval.</summary>
		public void ResetMeasurement() {
			lock (_gate) {
				_history.Clear();
				_observedFaults.Clear();
				_evaluatedNodes.Clear();
				_emergencyCount = 0;
				_revision++;
			}
		}

		public FaultTracker BeginOrContinueFault(Diagnostic diagnostic) {
			if (diagnostic == null) throw new ArgumentNullException(nameof(diagnostic));
			var key = FaultKey(diagnostic);
			lock (_gate) {
				if (!_faults.TryGetValue(key, out var tracker)) {
					tracker = new FaultTracker(diagnostic, ++_nextEntryId);
					_faults.Add(key, tracker);
					AddHistory(diagnostic, 1, tracker.FirstFrame, tracker.LastFrame, tracker.EntryId);
				}
				else {
					tracker.Count++;
					tracker.LastFrame = FrameOf(diagnostic);
					tracker.IsPaused = false;
					if (tracker.LastFrame >= tracker.LastPublishedFrame + 300) {
						tracker.LastPublishedFrame = tracker.LastFrame;
						ReplaceHistoryAggregate(tracker, diagnostic);
					}
				}
				_observedFaults.Add(key);
				if (diagnostic.NodeId.HasValue) _current[ConditionKey(diagnostic)] = diagnostic;
				_revision++;
				return tracker;
			}
		}

		/// <summary>Close a removed node without emitting a recovery history row.</summary>
		public void CloseNode(NodeInstanceId nodeId, ulong generationId, string reason) {
			lock (_gate) {
				foreach (var pair in _faults.Where(x => x.Value.NodeId == nodeId && x.Value.GenerationId.HasValue && x.Value.GenerationId.Value == generationId).ToList()) {
					pair.Value.IsClosed = true; pair.Value.CloseReason = reason ?? "node_removed";
					_faults.Remove(pair.Key); ClearConditions(pair.Value.FirstDiagnostic);
				}
			}
		}

		public void CloseSession(string reason) {
			lock (_gate) {
				foreach (var tracker in _faults.Values) { tracker.IsClosed = true; tracker.CloseReason = reason ?? "session_ended"; }
				_faults.Clear(); _current.Clear(); _observedFaults.Clear(); _evaluatedNodes.Clear();
			}
		}

		/// <summary>
		/// A fault recovers only when its node was evaluated and did not fault.
		/// A node outside the demand plan is paused instead of recovered.
		/// </summary>
		public void CompleteFrame(ulong frameNumber, double graphClockTime) {
			lock (_gate) {
				foreach (var pair in _faults.ToList()) {
					var tracker = pair.Value;
					if (!tracker.NodeId.HasValue) continue;
					var evaluated = tracker.GenerationId.HasValue
						? _evaluatedNodes.Contains(NodeKey(tracker.NodeId.Value, tracker.GenerationId.Value))
						: _evaluatedNodes.Any(key => key.StartsWith(tracker.NodeId.Value.Value + ":", StringComparison.Ordinal));
					if (!evaluated) { tracker.IsPaused = true; continue; }
					tracker.IsPaused = false;
					if (_observedFaults.Contains(pair.Key)) continue;
					_faults.Remove(pair.Key); ClearConditions(tracker.FirstDiagnostic);
					var original = tracker.FirstDiagnostic;
					var recovery = new Diagnostic(new DiagnosticCode("runtime.fault.recovered"), Severity.Info, "Runtime fault recovered.", original.ScopeId ?? ScopeId, original.NodeId, original.NodeTypeId, original.GenerationId, original.PortId, original.ParameterId, new DiagnosticDetail(new[]
					{
						new KeyValuePair<string, string>("original_code", original.Code.Value),
						new KeyValuePair<string, string>("count", tracker.Count.ToString()),
						new KeyValuePair<string, string>("first_frame", tracker.FirstFrame.ToString()),
						new KeyValuePair<string, string>("last_frame", tracker.LastFrame.ToString())
					}), (long)frameNumber, graphClockTime, "runtime");
					AddHistory(recovery, 1, frameNumber, frameNumber);
				}
				_observedFaults.Clear(); _evaluatedNodes.Clear();
			}
		}

		public CSharpFunctionalExtensions.UnitResult<Diagnostic> ResolveFault(Diagnostic diagnostic, ulong frameNumber, double graphClockTime) {
			if (diagnostic == null) return CSharpFunctionalExtensions.UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("runtime.diagnostic.invalid"), Severity.Error, "Diagnostic is required."));
			var key = FaultKey(diagnostic);
			lock (_gate) {
				if (!_faults.TryGetValue(key, out var tracker)) return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
				_faults.Remove(key); ClearConditions(tracker.FirstDiagnostic);
				var original = tracker.FirstDiagnostic;
				var recovery = new Diagnostic(new DiagnosticCode("runtime.fault.recovered"), Severity.Info, "Runtime fault recovered.", original.ScopeId ?? ScopeId, original.NodeId, original.NodeTypeId, original.GenerationId, original.PortId, original.ParameterId, new DiagnosticDetail(new[]
				{
					new KeyValuePair<string, string>("original_code", original.Code.Value),
					new KeyValuePair<string, string>("count", tracker.Count.ToString()),
					new KeyValuePair<string, string>("first_frame", tracker.FirstFrame.ToString()),
					new KeyValuePair<string, string>("last_frame", tracker.LastFrame.ToString())
				}), (long)frameNumber, graphClockTime, "runtime");
				AddHistory(recovery, 1, frameNumber, frameNumber);
				return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
			}
		}

		private void ClearConditions(Diagnostic diagnostic) { if (diagnostic.NodeId.HasValue) _current.Remove(ConditionKey(diagnostic)); }
		private void ReplaceHistoryAggregate(FaultTracker tracker, Diagnostic latest) {
			var node = _history.First;
			while (node != null && node.Value.EntryId != tracker.EntryId) node = node.Next;
			if (node != null) node.Value = new DiagnosticHistoryEntry(tracker.EntryId, latest, tracker.Count, tracker.FirstFrame, tracker.LastFrame);
			if (node != null) _revision++;
		}
		private ulong AddHistory(Diagnostic diagnostic, long count, ulong firstFrame, ulong lastFrame, ulong? entryId = null) {
			var id = entryId ?? ++_nextEntryId;
			_history.AddLast(new DiagnosticHistoryEntry(id, diagnostic, count, firstFrame, lastFrame));
			while (_history.Count > _historyCapacity) _history.RemoveFirst();
			_revision++;
			return id;
		}
		private CurrentConditionKey ConditionKey(Diagnostic diagnostic) => new CurrentConditionKey(diagnostic.ScopeId ?? ScopeId, "Node", diagnostic.NodeId.Value.Value, diagnostic.Code, diagnostic.GenerationId, diagnostic.PortId?.Value ?? diagnostic.ParameterId?.Value);
		private static ulong FrameOf(Diagnostic diagnostic) => (ulong)Math.Max(0, diagnostic.FrameNumber);
		private static string NodeKey(NodeInstanceId nodeId, ulong generationId) => nodeId.Value + ":" + generationId;
		private string FaultKey(Diagnostic diagnostic) {
			var fields = diagnostic.Detail.Fields.OrderBy(x => x.Key, StringComparer.Ordinal).Where(x => !string.Equals(x.Key, "frame", StringComparison.Ordinal) && !string.Equals(x.Key, "graph_clock_time", StringComparison.Ordinal)).Select(x => x.Key + "=" + x.Value);
			return (diagnostic.ScopeId ?? ScopeId) + "|" + (diagnostic.NodeId?.Value ?? string.Empty) + "|" + (diagnostic.GenerationId?.ToString() ?? string.Empty) + "|" + diagnostic.Code.Value + "|" + string.Join(";", fields);
		}
	}
}
