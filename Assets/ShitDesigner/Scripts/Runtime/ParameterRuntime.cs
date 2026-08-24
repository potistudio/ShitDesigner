using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Project;

namespace ShitDesigner.Runtime {
	public enum ParameterUpdateSource {
		Ui,
		CustomUi,
		Preset,
		PhysicalInput,
		LogicalControl
	}

	public enum RuntimeParameterEventKind {
		BaseValue,
		ControlValue,
		Preset
	}

	public enum ParameterEventStatus {
		Applied,
		Rejected,
		Superseded
	}

	public sealed class RuntimeParameterEvent {
		public ulong SequenceNumber { get; }
		public RuntimeParameterEventKind Kind { get; }
		public ParameterUpdateSource Source { get; }
		public NodeInstanceId NodeId { get; }
		public ParameterId ParameterId { get; }
		public ParameterValue Value { get; }
		public LogicalControlId ControlId { get; }
		public PresetId? PresetId { get; }

		private RuntimeParameterEvent(ulong sequenceNumber, RuntimeParameterEventKind kind, ParameterUpdateSource source, NodeInstanceId nodeId, ParameterId parameterId, ParameterValue value, LogicalControlId controlId, PresetId? presetId) {
			SequenceNumber = sequenceNumber; Kind = kind; Source = source; NodeId = nodeId; ParameterId = parameterId; Value = value; ControlId = controlId; PresetId = presetId;
		}

		public static RuntimeParameterEvent BaseValue(ulong sequence, NodeInstanceId nodeId, ParameterId parameterId, ParameterValue value, ParameterUpdateSource source = ParameterUpdateSource.Ui) => new RuntimeParameterEvent(sequence, RuntimeParameterEventKind.BaseValue, source, nodeId, parameterId, value, default(LogicalControlId), null);
		public static RuntimeParameterEvent ControlValue(ulong sequence, LogicalControlId controlId, float value) => new RuntimeParameterEvent(sequence, RuntimeParameterEventKind.ControlValue, ParameterUpdateSource.LogicalControl, default(NodeInstanceId), default(ParameterId), ParameterValue.FromFloat(value), controlId, null);
		public static RuntimeParameterEvent Preset(ulong sequence, PresetId presetId, ParameterUpdateSource source = ParameterUpdateSource.Preset) => new RuntimeParameterEvent(sequence, RuntimeParameterEventKind.Preset, source, default(NodeInstanceId), default(ParameterId), default(ParameterValue), default(LogicalControlId), presetId);
	}

	public sealed class ParameterCommitResult {
		private readonly IReadOnlyDictionary<ParameterKey, ParameterValue> _changed;
		private readonly IReadOnlyDictionary<LogicalControlId, float> _controls;
		private readonly IReadOnlyList<Diagnostic> _diagnostics;
		private readonly IReadOnlyCollection<LogicalControlId> _firedTriggers;
		private readonly IReadOnlyList<ParameterEventResult> _eventResults;
		internal ParameterCommitResult(IDictionary<ParameterKey, ParameterValue> changed, IDictionary<LogicalControlId, float> controls, IEnumerable<Diagnostic> diagnostics, IEnumerable<LogicalControlId> firedTriggers = null, IEnumerable<ParameterEventResult> eventResults = null) {
			_changed = new ReadOnlyDictionary<ParameterKey, ParameterValue>(new Dictionary<ParameterKey, ParameterValue>(changed ?? new Dictionary<ParameterKey, ParameterValue>()));
			_controls = new ReadOnlyDictionary<LogicalControlId, float>(new Dictionary<LogicalControlId, float>(controls ?? new Dictionary<LogicalControlId, float>()));
			_diagnostics = new ReadOnlyCollection<Diagnostic>((diagnostics ?? Enumerable.Empty<Diagnostic>()).ToList());
			_firedTriggers = new ReadOnlyCollection<LogicalControlId>((firedTriggers ?? Enumerable.Empty<LogicalControlId>()).Distinct().ToList());
			_eventResults = new ReadOnlyCollection<ParameterEventResult>((eventResults ?? Enumerable.Empty<ParameterEventResult>()).ToList());
		}
		public IReadOnlyDictionary<ParameterKey, ParameterValue> ChangedBaseValues => _changed;
		public IReadOnlyDictionary<LogicalControlId, float> ControlValues => _controls;
		public IReadOnlyList<Diagnostic> Diagnostics => _diagnostics;
		public bool HasFailures => _diagnostics.Any(x => x.Severity == Severity.Error || x.Severity == Severity.Fatal);
		public IReadOnlyCollection<LogicalControlId> FiredTriggers => _firedTriggers;
		public IReadOnlyList<ParameterEventResult> EventResults => _eventResults;
	}

	/// <summary>Outcome for one detached Phase 2 parameter event.</summary>
	public sealed class ParameterEventResult {
		public ulong SequenceNumber { get; }
		public RuntimeParameterEventKind Kind { get; }
		public bool Applied { get; }
		public ParameterEventStatus Status { get; }
		public bool IsSuperseded => Status == ParameterEventStatus.Superseded;
		public Diagnostic Diagnostic { get; }
		internal ParameterEventResult(ulong sequenceNumber, RuntimeParameterEventKind kind, bool applied, Diagnostic diagnostic = null, ParameterEventStatus? status = null) {
			SequenceNumber = sequenceNumber;
			Kind = kind;
			Applied = applied;
			Status = status ?? (applied ? ParameterEventStatus.Applied : ParameterEventStatus.Rejected);
			Diagnostic = diagnostic;
		}
	}

	internal sealed class ParameterStoreState {
		internal readonly Dictionary<ParameterKey, ParameterValue> BaseValues;
		internal readonly Dictionary<ParameterKey, ParameterValue> EffectiveValues;
		internal readonly Dictionary<LogicalControlId, float> ControlValues;
		internal readonly HashSet<LogicalControlId> ActiveTriggers;
		internal readonly HashSet<LogicalControlId> KnownTriggers;
		internal readonly HashSet<LogicalControlId> FiringTriggers;
		internal ParameterStoreState(IDictionary<ParameterKey, ParameterValue> baseValues, IDictionary<ParameterKey, ParameterValue> effectiveValues, IDictionary<LogicalControlId, float> controlValues, IEnumerable<LogicalControlId> activeTriggers, IEnumerable<LogicalControlId> knownTriggers, IEnumerable<LogicalControlId> firingTriggers) {
			BaseValues = new Dictionary<ParameterKey, ParameterValue>(baseValues);
			EffectiveValues = new Dictionary<ParameterKey, ParameterValue>(effectiveValues);
			ControlValues = new Dictionary<LogicalControlId, float>(controlValues);
			ActiveTriggers = new HashSet<LogicalControlId>(activeTriggers ?? Enumerable.Empty<LogicalControlId>());
			KnownTriggers = new HashSet<LogicalControlId>(knownTriggers ?? Enumerable.Empty<LogicalControlId>());
			FiringTriggers = new HashSet<LogicalControlId>(firingTriggers ?? Enumerable.Empty<LogicalControlId>());
		}
	}

	/// <summary>Trusted Runtime-only pair of copy-on-write ParameterStore
	/// snapshots.  Both dictionaries are already detached from mutable store
	/// state; FrameSnapshot may retain this pair without a second copy.</summary>
	internal sealed class ParameterFrameValues {
		internal IReadOnlyDictionary<ParameterKey, ParameterValue> EffectiveValues { get; }
		internal IReadOnlyDictionary<LogicalControlId, float> ControlValues { get; }
		internal ParameterFrameValues(IReadOnlyDictionary<ParameterKey, ParameterValue> effectiveValues, IReadOnlyDictionary<LogicalControlId, float> controlValues) {
			EffectiveValues = effectiveValues ?? throw new ArgumentNullException(nameof(effectiveValues));
			ControlValues = controlValues ?? throw new ArgumentNullException(nameof(controlValues));
		}
	}

	/// <summary>
	/// Frame-local parameter state. It never mutates ProjectDocument; the
	/// Application command path owns persistence while Runtime owns the
	/// immutable values used by the current evaluation frame.
	/// </summary>
	public sealed class ParameterStore {
		public sealed class ControlRuntimeSnapshot {
			public float Value { get; }
			public bool HasValue { get; }
			public bool IsFiring { get; }
			internal ControlRuntimeSnapshot(float value, bool hasValue, bool isFiring) { Value = value; HasValue = hasValue; IsFiring = isFiring; }
		}

		private readonly Dictionary<ParameterKey, ParameterValue> _base = new Dictionary<ParameterKey, ParameterValue>();
		private readonly Dictionary<ParameterKey, ParameterValue> _effective = new Dictionary<ParameterKey, ParameterValue>();
		private readonly Dictionary<LogicalControlId, float> _controls = new Dictionary<LogicalControlId, float>();
		private readonly HashSet<LogicalControlId> _activeTriggers = new HashSet<LogicalControlId>();
		private readonly HashSet<LogicalControlId> _firingTriggers = new HashSet<LogicalControlId>();
		private readonly HashSet<LogicalControlId> _knownTriggers = new HashSet<LogicalControlId>();
		private IReadOnlyDictionary<ParameterKey, ParameterValue> _effectiveSnapshot = new ReadOnlyDictionary<ParameterKey, ParameterValue>(new Dictionary<ParameterKey, ParameterValue>());
		private IReadOnlyDictionary<LogicalControlId, float> _controlSnapshot = new ReadOnlyDictionary<LogicalControlId, float>(new Dictionary<LogicalControlId, float>());
		private IReadOnlyDictionary<LogicalControlId, ControlRuntimeSnapshot> _controlRuntimeSnapshot = new ReadOnlyDictionary<LogicalControlId, ControlRuntimeSnapshot>(new Dictionary<LogicalControlId, ControlRuntimeSnapshot>());
		private long _effectiveRevision;
		private long _controlRevision;
		private IReadOnlyCollection<ParameterKey> _changedEffectiveKeys = new ReadOnlyCollection<ParameterKey>(new List<ParameterKey>());
		private ParameterFrameValues _frameValues;

		public ParameterStore() {
			_frameValues = new ParameterFrameValues(_effectiveSnapshot, _controlSnapshot);
		}

		public IReadOnlyDictionary<ParameterKey, ParameterValue> BaseValues => new ReadOnlyDictionary<ParameterKey, ParameterValue>(new Dictionary<ParameterKey, ParameterValue>(_base));
		public IReadOnlyDictionary<ParameterKey, ParameterValue> EffectiveValues => _effectiveSnapshot;
		public IReadOnlyDictionary<LogicalControlId, float> ControlValues => _controlSnapshot;
		public IReadOnlyDictionary<LogicalControlId, ControlRuntimeSnapshot> ControlRuntime => _controlRuntimeSnapshot;
		public long EffectiveRevision => _effectiveRevision;
		public long ControlRevision => _controlRevision;
		public IReadOnlyCollection<ParameterKey> ChangedEffectiveKeys => _changedEffectiveKeys;
		internal ParameterFrameValues FrameValues => _frameValues;

		internal ParameterStoreState CaptureState() => new ParameterStoreState(_base, _effective, _controls, _activeTriggers, _knownTriggers, _firingTriggers);

		internal void RestoreState(ParameterStoreState state) {
			if (state == null) throw new ArgumentNullException(nameof(state));
			_base.Clear(); foreach (var pair in state.BaseValues) _base[pair.Key] = pair.Value;
			_effective.Clear(); foreach (var pair in state.EffectiveValues) _effective[pair.Key] = pair.Value;
			_controls.Clear(); foreach (var pair in state.ControlValues) _controls[pair.Key] = pair.Value;
			_activeTriggers.Clear(); foreach (var id in state.ActiveTriggers) _activeTriggers.Add(id);
			_knownTriggers.Clear(); foreach (var id in state.KnownTriggers) _knownTriggers.Add(id);
			_firingTriggers.Clear(); foreach (var id in state.FiringTriggers) _firingTriggers.Add(id);
			PublishEffectiveSnapshot();
			PublishControlSnapshot();
		}

		/// <summary>Ends one-frame PresetTrigger pulses before intake for the next Runtime frame.</summary>
		public void BeginFrame() {
			if (_firingTriggers.Count == 0) return;
			_firingTriggers.Clear();
			PublishControlSnapshot();
		}

		public void Synchronize(GraphState graph, ProjectDocument document) {
			if (graph == null) throw new ArgumentNullException(nameof(graph));
			var valid = new HashSet<ParameterKey>();
			foreach (var node in graph.Nodes) {
				foreach (var parameter in node.Parameters) {
					var key = new ParameterKey(node.Id, parameter.Definition.Id);
					valid.Add(key);
					if (!_base.ContainsKey(key)) _base[key] = parameter.BaseValue;
					if (!_effective.ContainsKey(key)) _effective[key] = parameter.BaseValue;
				}
			}
			foreach (var key in _base.Keys.ToList()) if (!valid.Contains(key)) { _base.Remove(key); _effective.Remove(key); }
			if (document != null) {
				foreach (var control in document.LogicalControls) {
					if (control.Kind == LogicalControlKind.Value) {
						if (!_controls.ContainsKey(control.Id)) _controls[control.Id] = control.InitialValue;
					}
					else {
						_controls.Remove(control.Id);
						_knownTriggers.Add(control.Id);
					}
				}
				foreach (var id in _controls.Keys.ToList()) if (document.FindLogicalControl(id) == null) _controls.Remove(id);
				foreach (var trigger in _knownTriggers.ToList()) {
					var record = document.FindLogicalControl(trigger);
					if (record == null || record.Kind != LogicalControlKind.PresetTrigger) _knownTriggers.Remove(trigger);
				}
				foreach (var trigger in _firingTriggers.ToList()) {
					var record = document.FindLogicalControl(trigger);
					if (record == null || record.Kind != LogicalControlKind.PresetTrigger) _firingTriggers.Remove(trigger);
				}
				foreach (var trigger in _activeTriggers.ToList()) {
					var record = document.FindLogicalControl(trigger);
					if (record == null || record.Kind != LogicalControlKind.PresetTrigger) _activeTriggers.Remove(trigger);
				}
			}
			PublishEffectiveSnapshot();
			PublishControlSnapshot();
		}

		public ParameterCommitResult ApplyEvents(IEnumerable<RuntimeParameterEvent> events, GraphState graph, ProjectDocument document) {
			if (graph == null) throw new ArgumentNullException(nameof(graph));
			if (document == null) throw new ArgumentNullException(nameof(document));
			Synchronize(graph, document);
			var stagedBase = new Dictionary<ParameterKey, ParameterValue>(_base);
			var stagedControls = new Dictionary<LogicalControlId, float>(_controls);
			var diagnostics = new List<Diagnostic>();
			var changed = new Dictionary<ParameterKey, ParameterValue>();
			var firedTriggers = new HashSet<LogicalControlId>();
			var appliedPresets = new HashSet<PresetId>();
			var stagedTriggers = new HashSet<LogicalControlId>(_activeTriggers);
			var eventResults = new List<ParameterEventResult>();
			var orderedEvents = (events ?? Enumerable.Empty<RuntimeParameterEvent>()).Where(x => x != null).OrderBy(x => x.SequenceNumber).ToList();
			for (var eventIndex = 0; eventIndex < orderedEvents.Count; eventIndex++) {
				var item = orderedEvents[eventIndex];
				var diagnosticCount = diagnostics.Count;
				switch (item.Kind) {
					case RuntimeParameterEventKind.BaseValue:
						ApplyBaseValue(item, graph, stagedBase, diagnostics);
						break;
					case RuntimeParameterEventKind.ControlValue:
						ApplyControlValue(item, document, stagedControls, diagnostics, graph, stagedBase, firedTriggers, appliedPresets, stagedTriggers);
						break;
					case RuntimeParameterEventKind.Preset:
						ApplyPreset(item.PresetId, graph, document, stagedBase, diagnostics, appliedPresets, false);
						break;
				}
				var eventDiagnostic = diagnostics.Skip(diagnosticCount).FirstOrDefault(x => x.Severity == Severity.Error || x.Severity == Severity.Fatal);
				var superseded = orderedEvents.Skip(eventIndex + 1).Any(next => SameCoalescingTarget(item, next));
				eventResults.Add(new ParameterEventResult(item.SequenceNumber, item.Kind, eventDiagnostic == null && !superseded, eventDiagnostic, superseded ? ParameterEventStatus.Superseded : (eventDiagnostic == null ? ParameterEventStatus.Applied : ParameterEventStatus.Rejected)));
			}
			foreach (var pair in stagedBase) {
				if (!_base.TryGetValue(pair.Key, out var previous) || previous != pair.Value) changed[pair.Key] = pair.Value;
			}
			_base.Clear(); foreach (var pair in stagedBase) _base[pair.Key] = pair.Value;
			_controls.Clear(); foreach (var pair in stagedControls) _controls[pair.Key] = pair.Value;
			_activeTriggers.Clear(); foreach (var id in stagedTriggers) _activeTriggers.Add(id);
			_firingTriggers.Clear(); foreach (var id in firedTriggers) _firingTriggers.Add(id);
			PublishControlSnapshot();
			return new ParameterCommitResult(changed, stagedControls, diagnostics, firedTriggers, eventResults);
		}

		public UnitResult<Diagnostic> EvaluateEffective(GraphState graph, ProjectDocument document, IRuntimeDiagnosticSink diagnostics = null) {
			if (graph == null || document == null) return UnitResult.Failure<Diagnostic>(Failure("runtime.parameter.invalid", "Graph and document are required."));
			Synchronize(graph, document);
			var computed = new Dictionary<ParameterKey, ParameterValue>();
			foreach (var node in graph.Nodes) {
				foreach (var parameter in node.Parameters) {
					var key = new ParameterKey(node.Id, parameter.Definition.Id);
					var value = _base[key];
					var expression = document.FindExpression(node.Id, parameter.Definition.Id);
					if (expression != null && expression.IsValid) {
						var evaluated = EvaluateExpression(expression.Expression, key, value, graph, document);
						if (evaluated.IsSuccess) {
							value = evaluated.Value;
							if (expression.OutputRange.HasValue) {
								var clamped = ParameterValue.Clamp(value, expression.OutputRange.Value.Minimum, expression.OutputRange.Value.Maximum);
								if (clamped.IsSuccess) value = clamped.Value;
							}
						}
						else {
							diagnostics?.Report(evaluated.Error);
						}
					}
					var hard = parameter.Definition.Clamp(value);
					if (hard.IsFailure) {
						diagnostics?.Report(hard.Error);
						value = parameter.BaseValue;
					}
					else value = hard.Value;
					computed[key] = value;
				}
			}
			_effective.Clear(); foreach (var pair in computed) _effective[pair.Key] = pair.Value;
			PublishEffectiveSnapshot();
			return UnitResult.Success<Diagnostic>();
		}

		private void PublishEffectiveSnapshot() {
			if (Same(_effectiveSnapshot, _effective)) return;
			var changed = new List<ParameterKey>();
			foreach (var pair in _effective)
				if (!_effectiveSnapshot.TryGetValue(pair.Key, out var previous) || !EqualityComparer<ParameterValue>.Default.Equals(previous, pair.Value)) changed.Add(pair.Key);
			foreach (var pair in _effectiveSnapshot)
				if (!_effective.ContainsKey(pair.Key)) changed.Add(pair.Key);
			_effectiveSnapshot = new ReadOnlyDictionary<ParameterKey, ParameterValue>(new Dictionary<ParameterKey, ParameterValue>(_effective));
			_changedEffectiveKeys = new ReadOnlyCollection<ParameterKey>(changed);
			_effectiveRevision++;
			PublishFrameValues();
		}

		private void PublishControlSnapshot() {
			var runtime = new Dictionary<LogicalControlId, ControlRuntimeSnapshot>();
			foreach (var pair in _controls) runtime[pair.Key] = new ControlRuntimeSnapshot(pair.Value, true, false);
			foreach (var trigger in _knownTriggers) runtime[trigger] = new ControlRuntimeSnapshot(0f, false, _firingTriggers.Contains(trigger));
			foreach (var trigger in _firingTriggers) if (!runtime.ContainsKey(trigger)) runtime[trigger] = new ControlRuntimeSnapshot(0f, false, true);
			if (Same(_controlSnapshot, _controls) && SameControlRuntime(_controlRuntimeSnapshot, runtime)) return;
			_controlSnapshot = new ReadOnlyDictionary<LogicalControlId, float>(new Dictionary<LogicalControlId, float>(_controls));
			_controlRuntimeSnapshot = new ReadOnlyDictionary<LogicalControlId, ControlRuntimeSnapshot>(runtime);
			_controlRevision++;
			PublishFrameValues();
		}

		private void PublishFrameValues() {
			if (ReferenceEquals(_frameValues?.EffectiveValues, _effectiveSnapshot) && ReferenceEquals(_frameValues?.ControlValues, _controlSnapshot)) return;
			_frameValues = new ParameterFrameValues(_effectiveSnapshot, _controlSnapshot);
		}

		private static bool SameControlRuntime(IReadOnlyDictionary<LogicalControlId, ControlRuntimeSnapshot> left, IDictionary<LogicalControlId, ControlRuntimeSnapshot> right) {
			if (left == null || left.Count != right.Count) return false;
			foreach (var pair in right)
				if (!left.TryGetValue(pair.Key, out var previous) || previous.Value != pair.Value.Value || previous.HasValue != pair.Value.HasValue || previous.IsFiring != pair.Value.IsFiring) return false;
			return true;
		}

		private static bool Same<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> left, IDictionary<TKey, TValue> right) {
			if (left == null || left.Count != right.Count) return false;
			foreach (var pair in right)
				if (!left.TryGetValue(pair.Key, out var previous) || !EqualityComparer<TValue>.Default.Equals(previous, pair.Value)) return false;
			return true;
		}

		private void ApplyBaseValue(RuntimeParameterEvent item, GraphState graph, IDictionary<ParameterKey, ParameterValue> staged, ICollection<Diagnostic> diagnostics) {
			var node = graph.FindNode(item.NodeId);
			var parameter = node?.FindParameter(item.ParameterId);
			if (node == null || parameter == null) { diagnostics.Add(Failure("runtime.parameter.target_missing", "Base value target does not exist.", item.NodeId, item.ParameterId)); return; }
			var clamped = parameter.Definition.Clamp(item.Value);
			if (clamped.IsFailure) { diagnostics.Add(clamped.Error); return; }
			staged[new ParameterKey(item.NodeId, item.ParameterId)] = clamped.Value;
		}

		private void ApplyControlValue(RuntimeParameterEvent item, ProjectDocument document, IDictionary<LogicalControlId, float> staged, ICollection<Diagnostic> diagnostics, GraphState graph, IDictionary<ParameterKey, ParameterValue> stagedBase, ISet<LogicalControlId> firedTriggers, ISet<PresetId> appliedPresets, ISet<LogicalControlId> activeTriggers) {
			var control = document.FindLogicalControl(item.ControlId);
			var value = item.Value.AsFloat();
			if (control == null) { diagnostics.Add(Failure("runtime.control.target_missing", "Logical control does not exist.", codeNode: null)); return; }
			if (float.IsNaN(value) || float.IsInfinity(value)) { diagnostics.Add(Failure("runtime.control.invalid_value", "Logical control value must be finite.")); return; }
			value = Math.Min(1f, Math.Max(0f, value));
			if (control.Kind == LogicalControlKind.Value) {
				staged[item.ControlId] = value;
				return;
			}

			var wasActive = activeTriggers.Contains(item.ControlId);
			var fire = !wasActive && value >= 0.5f;
			if (wasActive && value < 0.4f) activeTriggers.Remove(item.ControlId);
			if (!fire || !firedTriggers.Add(item.ControlId)) return;
			activeTriggers.Add(item.ControlId);
			if (!control.PresetId.HasValue) {
				diagnostics.Add(new Diagnostic(new DiagnosticCode("runtime.preset_trigger.unassigned"), Severity.Warning, "PresetTrigger fired without an assigned preset.", module: "runtime"));
				return;
			}
			if (control.PresetIsBroken) {
				diagnostics.Add(new Diagnostic(new DiagnosticCode("runtime.preset_trigger.broken"), Severity.Warning, control.BrokenReason ?? "PresetTrigger references a broken preset.", module: "runtime"));
				return;
			}
			ApplyPreset(control.PresetId, graph, document, stagedBase, diagnostics, appliedPresets, true);
		}

		private void ApplyPreset(PresetId? presetId, GraphState graph, ProjectDocument document, IDictionary<ParameterKey, ParameterValue> staged, ICollection<Diagnostic> diagnostics, ISet<PresetId> appliedPresets, bool triggered) {
			if (!presetId.HasValue) { diagnostics.Add(Failure("runtime.preset.invalid", "Preset ID is required.")); return; }
			if (appliedPresets != null && !appliedPresets.Add(presetId.Value)) return;
			var preset = document.FindPreset(presetId.Value);
			if (preset == null || preset.IsBroken) {
				diagnostics.Add(new Diagnostic(new DiagnosticCode(triggered ? "runtime.preset_trigger.broken" : "runtime.preset.rejected"), triggered ? Severity.Warning : Severity.Error, "Preset is missing or contains a broken item.", module: "runtime"));
				return;
			}
			var replacements = new List<KeyValuePair<ParameterKey, ParameterValue>>();
			foreach (var entry in preset.Entries) {
				var node = graph.FindNode(entry.NodeId);
				var parameter = node?.FindParameter(entry.ParameterId);
				if (node == null || parameter == null || parameter.Definition.Type != entry.ParameterType) {
					diagnostics.Add(Failure("runtime.preset.rejected", "Preset target parameter is missing or has an incompatible type.", entry.NodeId, entry.ParameterId));
					return;
				}
				var value = parameter.Definition.Clamp(entry.Value);
				if (value.IsFailure) { diagnostics.Add(value.Error); return; }
				replacements.Add(new KeyValuePair<ParameterKey, ParameterValue>(new ParameterKey(entry.NodeId, entry.ParameterId), value.Value));
			}
			foreach (var replacement in replacements) staged[replacement.Key] = replacement.Value;
		}

		private Result<ParameterValue, Diagnostic> EvaluateExpression(LogicalExpressionNode node, ParameterKey key, ParameterValue baseValue, GraphState graph, ProjectDocument document) {
			var control = node as LogicalControlLeaf;
			if (control != null) {
				if (!_controls.TryGetValue(control.ControlId, out var normalized)) return Result.Failure<ParameterValue, Diagnostic>(Failure("runtime.expression.control_missing", "Expression control value is unavailable."));
				var record = document.FindLogicalControl(control.ControlId);
				var target = record?.Targets.FirstOrDefault(x => x.NodeId == key.NodeId && x.ParameterId == key.ParameterId && !x.IsBroken);
				return target == null ? Result.Failure<ParameterValue, Diagnostic>(Failure("runtime.expression.target_missing", "Expression target mapping is unavailable.")) : target.Map(normalized);
			}
			if (node is BaseValueLeaf) return Result.Success<ParameterValue, Diagnostic>(baseValue);
			var binary = node as BinaryLogicalExpression;
			if (binary == null || binary.Left == null || binary.Right == null) return Result.Failure<ParameterValue, Diagnostic>(Failure("runtime.expression.invalid", "Expression is incomplete."));
			var left = EvaluateExpression(binary.Left, key, baseValue, graph, document); if (left.IsFailure) return left;
			var right = EvaluateExpression(binary.Right, key, baseValue, graph, document); if (right.IsFailure) return right;
			return binary.Operator == LogicalOperator.Min ? ParameterValue.Min(left.Value, right.Value) : ParameterValue.Max(left.Value, right.Value);
		}

		private static Diagnostic Failure(string code, string message, NodeInstanceId? codeNode = null, ParameterId? parameterId = null) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: codeNode, parameterId: parameterId);

		private static bool SameCoalescingTarget(RuntimeParameterEvent left, RuntimeParameterEvent right) {
			if (left == null || right == null || left.Kind != right.Kind) return false;
			if (left.Kind == RuntimeParameterEventKind.BaseValue)
				return left.NodeId == right.NodeId && left.ParameterId == right.ParameterId;
			if (left.Kind == RuntimeParameterEventKind.ControlValue)
				return left.ControlId == right.ControlId;
			return false; // Presets are explicit atomic commands and never coalesce.
		}
	}
}
