using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;

namespace ShitDesigner.Project
{
    public interface IProjectCommand
    {
        string Name { get; }
        Result Apply(ProjectDocument document);
    }

    public sealed class ProjectCommandProcessor
    {
        private sealed class HistoryEntry
        {
            public DocumentSnapshot Before { get; }
            public DocumentSnapshot After { get; }
            public HistoryEntry(DocumentSnapshot before, DocumentSnapshot after) { Before = before; After = after; }
        }
        private readonly ProjectDocument _document;
        private readonly List<HistoryEntry> _undo = new List<HistoryEntry>();
        private readonly List<HistoryEntry> _redo = new List<HistoryEntry>();
        public const int MaxUndoEntries = 200;
        public ProjectDocument Document => _document;
        public int UndoCount => _undo.Count;
        public int RedoCount => _redo.Count;
        public bool CanUndo => _undo.Count > 0;
        public bool CanRedo => _redo.Count > 0;
        public ProjectCommandProcessor(ProjectDocument document) { _document = document ?? throw new ArgumentNullException(nameof(document)); }

        public Result Execute(IProjectCommand command)
        {
            if (command == null) return Result.Failure(ProjectDiagnostics.Rejected("project.command.invalid", "Command is required."));
            var before = _document.CaptureSnapshot();
            var revision = _document.DocumentRevision;
            Result result;
            try { result = command.Apply(_document); }
            catch (Exception exception) { result = Result.Failure(new Diagnostic(new DiagnosticCode("project.command.exception"), Severity.Error, "Project command failed.", exception: DiagnosticExceptionInfo.FromException(exception))); }
            if (result.IsFailure || _document.HasDuplicateIds())
            {
                if (result.IsSuccess) result = Result.Failure(ProjectDiagnostics.Rejected("project.command.duplicate_id", "Command would create duplicate stable IDs."));
                _document.RestoreSnapshot(before, true, revision);
                return result;
            }
            _document.CommitMutation();
            var after = _document.CaptureSnapshot();
            _undo.Add(new HistoryEntry(before, after));
            if (_undo.Count > MaxUndoEntries) _undo.RemoveAt(0);
            _redo.Clear();
            return Result.Success();
        }

        public Result Undo()
        {
            if (_undo.Count == 0) return Result.Failure(ProjectDiagnostics.Rejected("project.history.empty", "There is nothing to undo."));
            var entry = _undo[_undo.Count - 1]; _undo.RemoveAt(_undo.Count - 1);
            _document.RestoreHistorySnapshot(entry.Before, _document.DocumentRevision + 1);
            _redo.Add(entry);
            return Result.Success();
        }
        public Result Redo()
        {
            if (_redo.Count == 0) return Result.Failure(ProjectDiagnostics.Rejected("project.history.empty", "There is nothing to redo."));
            var entry = _redo[_redo.Count - 1]; _redo.RemoveAt(_redo.Count - 1);
            _document.RestoreHistorySnapshot(entry.After, _document.DocumentRevision + 1);
            _undo.Add(entry);
            return Result.Success();
        }

        public Result AddNode(NodeRecord node) => Execute(new AddNodeCommand(node));
        public Result DeleteNode(NodeInstanceId nodeId) => Execute(new DeleteNodeCommand(nodeId));
        public Result Connect(ConnectionRecord connection) => Execute(new ConnectCommand(connection));
        public Result Disconnect(ConnectionId connectionId) => Execute(new DisconnectCommand(connectionId));
        public Result SetBaseValue(NodeInstanceId nodeId, ParameterId parameterId, ParameterValue value) => Execute(new SetBaseValueCommand(nodeId, parameterId, value));
        /// <summary>Commits the already sequence-ordered frame updates as one history entry.</summary>
        public Result ApplyBaseValues(IEnumerable<BaseValueUpdate> updates) => Execute(new ApplyBaseValuesCommand(updates));
        public Result AddLogicalControl(LogicalControlRecord control) => Execute(new AddLogicalControlCommand(control));
        public Result RenameLogicalControl(LogicalControlId id, string name) => Execute(new RenameLogicalControlCommand(id, name));
        public Result SetLogicalControlTargets(LogicalControlId id, IEnumerable<LogicalControlTargetRecord> targets) => Execute(new SetLogicalControlTargetsCommand(id, targets));
        public Result SetNodeRawState(NodeInstanceId id, string rawState) => Execute(new SetNodeRawStateCommand(id, rawState));
        public Result SetLogicalControlMappings(LogicalControlId id, IEnumerable<ControlMappingRecord> mappings) => Execute(new SetLogicalControlMappingsCommand(id, mappings));
        public Result SetPresetTriggerBinding(LogicalControlId id, PresetId? presetId) => Execute(new SetPresetTriggerBindingCommand(id, presetId));
        public Result AddPreset(PresetRecord preset) => Execute(new AddPresetCommand(preset));
        public Result RenamePreset(PresetId id, string name) => Execute(new RenamePresetCommand(id, name));
        public Result SetPresetEntries(PresetId id, IEnumerable<PresetEntryRecord> entries) => Execute(new SetPresetEntriesCommand(id, entries));
        public Result ApplyPreset(PresetId presetId) => Execute(new ApplyPresetCommand(presetId));
        public Result AddExpression(ParameterExpressionRecord expression) => Execute(new AddExpressionCommand(expression));
        public Result AddMediaAsset(MediaAssetRecord asset) => Execute(new AddMediaAssetCommand(asset));
        public Result AddMediaAssets(IEnumerable<MediaAssetRecord> assets) => Execute(new AddMediaAssetsCommand(assets));
        public Result DeleteLogicalControl(LogicalControlId id) => Execute(new DeleteLogicalControlCommand(id));
        public Result DeletePreset(PresetId id) => Execute(new DeletePresetCommand(id));
        public Result DeleteMediaAsset(MediaAssetId id) => Execute(new DeleteMediaAssetCommand(id));
        public Result ReplaceUi(ProjectUiStateRecord ui) => Execute(new ReplaceUiCommand(ui));
        public Result SetOutputSettings(ProjectOutputSettings settings) => Execute(new SetOutputSettingsCommand(settings));
        /// <summary>
        /// Commits a successfully planned graph snapshot as one ProjectDocument
        /// mutation.  The Graph assembly remains independent from persistence;
        /// callers pass the public immutable state lists from GraphPatch.After.
        /// </summary>
        public Result CommitGraphState(IEnumerable<NodeRecord> nodes, IEnumerable<ConnectionRecord> connections) => Execute(new CommitGraphStateCommand(nodes, connections));

        /// <summary>
        /// Applies an already validated graph repair without adding a normal
        /// user undo entry. The document revision/token still advance so the
        /// repair is durable and observable as a dirty project state.
        /// </summary>
        public Result CommitGraphRepair(IEnumerable<NodeRecord> nodes, IEnumerable<ConnectionRecord> connections)
        {
            var snapshot = _document.CaptureSnapshot();
            try
            {
                var result = new CommitGraphStateCommand(nodes, connections).Apply(_document);
                if (result.IsFailure)
                {
                    _document.RestoreSnapshot(snapshot, true, _document.DocumentRevision);
                    return result;
                }
                _document.CommitMutation();
                return Result.Success();
            }
            catch (Exception exception)
            {
                _document.RestoreSnapshot(snapshot, true, _document.DocumentRevision);
                return Result.Failure(ProjectDiagnostics.Rejected("project.graph.repair_exception", exception.Message));
            }
        }
    }

    public sealed class AddNodeCommand : IProjectCommand
    {
        private readonly NodeRecord _node;
        public string Name => "Add Node";
        public AddNodeCommand(NodeRecord node) { _node = node ?? throw new ArgumentNullException(nameof(node)); }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Node(_node.Id); if (identity.IsFailure) return identity;
            if (document.FindNode(_node.Id) != null) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.node_exists", "Node ID already exists."));
            document.AddNode(_node); document.RevalidateBrokenReferences(); return Result.Success();
        }
    }

    public sealed class DeleteNodeCommand : IProjectCommand
    {
        private readonly NodeInstanceId _nodeId;
        public string Name => "Delete Node";
        public DeleteNodeCommand(NodeInstanceId nodeId) { _nodeId = nodeId; }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Node(_nodeId); if (identity.IsFailure) return identity;
            var node = document.FindNode(_nodeId);
            if (node == null) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.node_missing", "Node does not exist."));
            if (node.SystemOwned) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.node_protected", "System-owned node cannot be deleted."));
            document.RemoveNode(_nodeId); return Result.Success();
        }
    }

    public sealed class ConnectCommand : IProjectCommand
    {
        private readonly ConnectionRecord _connection;
        public string Name => "Connect";
        public ConnectCommand(ConnectionRecord connection) { _connection = connection ?? throw new ArgumentNullException(nameof(connection)); }
        public Result Apply(ProjectDocument document)
        {
            var sourceIdentity = ProjectCommandValidation.Node(_connection.SourceNodeId); if (sourceIdentity.IsFailure) return sourceIdentity;
            var destinationIdentity = ProjectCommandValidation.Node(_connection.DestinationNodeId); if (destinationIdentity.IsFailure) return destinationIdentity;
            if (document.FindNode(_connection.SourceNodeId) == null || document.FindNode(_connection.DestinationNodeId) == null) return Result.Failure(ProjectDiagnostics.Rejected("project.connection.node_missing", "Connection endpoint node does not exist."));
            var source = document.FindNode(_connection.SourceNodeId).FindPort(_connection.SourcePortId);
            var destination = document.FindNode(_connection.DestinationNodeId).FindPort(_connection.DestinationPortId);
            if (source == null || destination == null || source.Direction != PortDirection.Output || destination.Direction != PortDirection.Input) return Result.Failure(ProjectDiagnostics.Rejected("project.connection.port_invalid", "Connection endpoint port is invalid."));
            if (!AreCompatible(source.Type, destination.Type, _connection.ConversionId)) return Result.Failure(ProjectDiagnostics.Rejected("project.connection.type_mismatch", "Ports are not compatible."));
            if (_connection.IsBroken) return Result.Failure(ProjectDiagnostics.Rejected("project.connection.broken_active", "Broken connections cannot be activated."));
            var replaces = document.Connections.Any(x => x.DestinationNodeId == _connection.DestinationNodeId && x.DestinationPortId == _connection.DestinationPortId);
            if (!replaces && document.Connections.Count >= 4096) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.connection_limit", "The project connection limit is 4096."));
            document.AddConnection(_connection); return Result.Success();
        }
        private static bool AreCompatible(PortType source, PortType destination, string conversionId)
        {
            if (source == destination) return string.IsNullOrEmpty(conversionId);
            return !string.IsNullOrEmpty(conversionId);
        }
    }

    public sealed class DisconnectCommand : IProjectCommand
    {
        private readonly ConnectionId _connectionId;
        public string Name => "Disconnect";
        public DisconnectCommand(ConnectionId connectionId) { _connectionId = connectionId; }
        public Result Apply(ProjectDocument document)
        {
            if (!document.Connections.Any(x => x.Id == _connectionId)) return Result.Failure(ProjectDiagnostics.Rejected("project.connection.missing", "Connection does not exist."));
            document.RemoveConnection(_connectionId); return Result.Success();
        }
    }

    public sealed class SetBaseValueCommand : IProjectCommand
    {
        private readonly NodeInstanceId _nodeId; private readonly ParameterId _parameterId; private readonly ParameterValue _value;
        public string Name => "Set Base Value";
        public SetBaseValueCommand(NodeInstanceId nodeId, ParameterId parameterId, ParameterValue value) { _nodeId = nodeId; _parameterId = parameterId; _value = value; }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Node(_nodeId); if (identity.IsFailure) return identity;
            var node = document.FindNode(_nodeId); if (node == null) return Result.Failure(ProjectDiagnostics.Rejected("project.parameter.node_missing", "Target node does not exist."));
            var parameter = node.FindParameter(_parameterId); if (parameter == null) return Result.Failure(ProjectDiagnostics.Rejected("project.parameter.missing", "Target parameter does not exist."));
            var clamped = parameter.Definition.Clamp(_value); if (clamped.IsFailure) return Result.Failure(clamped.Diagnostic);
            document.ReplaceNode(node.WithParameter(_parameterId, clamped.Value)); return Result.Success();
        }
    }

    public sealed class ApplyBaseValuesCommand : IProjectCommand
    {
        private readonly IReadOnlyList<BaseValueUpdate> _updates;
        public string Name => "Apply Base Values";
        public ApplyBaseValuesCommand(IEnumerable<BaseValueUpdate> updates)
        {
            _updates = new ReadOnlyCollection<BaseValueUpdate>((updates ?? Enumerable.Empty<BaseValueUpdate>()).ToList());
        }
        public Result Apply(ProjectDocument document)
        {
            if (_updates.Count == 0) return Result.Failure(ProjectDiagnostics.Rejected("project.parameter.empty_batch", "At least one BaseValue update is required."));
            if (_updates.GroupBy(x => new { x.NodeId, x.ParameterId }).Any(x => x.Count() > 1)) return Result.Failure(ProjectDiagnostics.Rejected("project.parameter.duplicate_update", "A BaseValue batch cannot contain duplicate parameter updates."));
            var replacements = new List<Tuple<NodeRecord, ParameterId, ParameterValue>>();
            foreach (var update in _updates)
            {
                var identity = ProjectCommandValidation.Node(update.NodeId); if (identity.IsFailure) return identity;
                var node = document.FindNode(update.NodeId);
                var parameter = node?.FindParameter(update.ParameterId);
                if (node == null || parameter == null) return Result.Failure(ProjectDiagnostics.Rejected("project.parameter.target_missing", "BaseValue update target does not exist."));
                var value = parameter.Definition.Clamp(update.Value);
                if (value.IsFailure) return value.Diagnostic == null ? Result.Failure(ProjectDiagnostics.Rejected("project.parameter.invalid", "BaseValue update is invalid.")) : Result.Failure(value.Diagnostic);
                replacements.Add(Tuple.Create(node, update.ParameterId, value.Value));
            }
            foreach (var replacement in replacements)
            {
                var current = document.FindNode(replacement.Item1.Id);
                document.ReplaceNode(current.WithParameter(replacement.Item2, replacement.Item3));
            }
            return Result.Success();
        }
    }

    public sealed class CommitGraphStateCommand : IProjectCommand
    {
        private readonly IReadOnlyList<NodeRecord> _nodes;
        private readonly IReadOnlyList<ConnectionRecord> _connections;
        public string Name => "Commit Graph State";
        public CommitGraphStateCommand(IEnumerable<NodeRecord> nodes, IEnumerable<ConnectionRecord> connections)
        {
            _nodes = new ReadOnlyCollection<NodeRecord>((nodes ?? Enumerable.Empty<NodeRecord>()).ToList());
            _connections = new ReadOnlyCollection<ConnectionRecord>((connections ?? Enumerable.Empty<ConnectionRecord>()).ToList());
        }
        public Result Apply(ProjectDocument document)
        {
            if (_nodes.Count == 0) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.empty", "A graph snapshot must contain at least one node."));
            if (_nodes.Count > 0 && _nodes.GroupBy(x => x.Id).Any(x => x.Count() > 1)) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.node_duplicate", "Graph snapshot contains duplicate node IDs."));
            foreach (var node in _nodes)
            {
                var identity = ProjectCommandValidation.Node(node.Id); if (identity.IsFailure) return identity;
            }
            if (_connections.Count > 4096) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.connection_limit", "The project connection limit is 4096."));
            if (_connections.GroupBy(x => x.Id).Any(x => x.Count() > 1)) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.connection_duplicate", "Graph snapshot contains duplicate connection IDs."));
            var nodeMap = _nodes.ToDictionary(x => x.Id, x => x);
            var destinationSet = new HashSet<Tuple<NodeInstanceId, PortId>>();
            foreach (var connection in _connections)
            {
                if (connection.Id.IsEmpty) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.connection_id", "Connection ID is required."));
                if (connection.IsBroken) continue;
                if (!nodeMap.TryGetValue(connection.SourceNodeId, out var sourceNode) || !nodeMap.TryGetValue(connection.DestinationNodeId, out var destinationNode)) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.connection_node", "Connection endpoint node does not exist."));
                var source = sourceNode.FindPort(connection.SourcePortId);
                var destination = destinationNode.FindPort(connection.DestinationPortId);
                if (source == null || destination == null || source.Direction != PortDirection.Output || destination.Direction != PortDirection.Input) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.connection_port", "Connection endpoint port is invalid."));
                if (!destinationSet.Add(Tuple.Create(connection.DestinationNodeId, connection.DestinationPortId))) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.input_occupied", "An input port may have only one active connection."));
                if (source.Type == destination.Type ? !string.IsNullOrEmpty(connection.ConversionId) : string.IsNullOrEmpty(connection.ConversionId)) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.connection_conversion", "Connection conversion does not match endpoint types."));
            }
            if (HasCycle(_connections, _nodes)) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.cycle", "Graph snapshot contains a same-frame cycle."));
            document.ReplaceGraph(_nodes, _connections);
            return Result.Success();
        }
        private static bool HasCycle(IEnumerable<ConnectionRecord> connections, IEnumerable<NodeRecord> nodes)
        {
            var nodeList = nodes.ToList();
            var adjacency = nodeList.ToDictionary(x => x.Id, x => new List<NodeInstanceId>());
            foreach (var edge in connections.Where(x => !x.IsBroken))
            {
                if (!adjacency.ContainsKey(edge.SourceNodeId) || !adjacency.ContainsKey(edge.DestinationNodeId)) continue;
                var source = nodeList.First(x => x.Id == edge.SourceNodeId);
                if (source.TypeId.Value == "system.feedback") continue;
                adjacency[edge.SourceNodeId].Add(edge.DestinationNodeId);
            }
            var active = new HashSet<NodeInstanceId>();
            var visited = new HashSet<NodeInstanceId>();
            Func<NodeInstanceId, bool> visit = null;
            visit = id =>
            {
                if (!active.Add(id)) return true;
                if (!visited.Add(id)) { active.Remove(id); return false; }
                foreach (var next in adjacency[id]) if (visit(next)) return true;
                active.Remove(id);
                return false;
            };
            return adjacency.Keys.Any(visit);
        }
    }

    public sealed class AddLogicalControlCommand : IProjectCommand
    {
        private readonly LogicalControlRecord _control;
        public string Name => "Add Logical Control";
        public AddLogicalControlCommand(LogicalControlRecord control) { _control = control ?? throw new ArgumentNullException(nameof(control)); }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.LogicalControl(_control); if (identity.IsFailure) return identity;
            if (document.FindLogicalControl(_control.Id) != null) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.exists", "Logical control ID already exists."));
            document.AddLogicalControl(_control); document.RevalidateBrokenReferences(); return Result.Success();
        }
    }

    public sealed class AddExpressionCommand : IProjectCommand
    {
        private readonly ParameterExpressionRecord _expression;
        public string Name => "Set Logical Expression";
        public AddExpressionCommand(ParameterExpressionRecord expression) { _expression = expression ?? throw new ArgumentNullException(nameof(expression)); }
        public Result Apply(ProjectDocument document)
        {
            if (_expression.HasCycle()) return Result.Failure(ProjectDiagnostics.Rejected("project.expression.cycle", "Expression contains a cycle."));
            var identity = ProjectCommandValidation.Expression(_expression); if (identity.IsFailure) return identity;
            if (!_expression.IsValid) return Result.Failure(ProjectDiagnostics.Rejected("project.expression.invalid", "Expression is incomplete or contains more than one Base leaf."));
            var node = document.FindNode(_expression.NodeId); var parameter = node?.FindParameter(_expression.ParameterId); if (node == null || parameter == null) return Result.Failure(ProjectDiagnostics.Rejected("project.expression.parameter_missing", "Expression target does not exist."));
            if (_expression.OutputRange.HasValue)
            {
                var outputRange = _expression.OutputRange.Value;
                if (outputRange.Minimum.Type != parameter.Definition.Type) return Result.Failure(ProjectDiagnostics.Rejected("project.expression.output_range_invalid", "Expression output range is incompatible with the target parameter."));
                if (parameter.Definition.HardRange.HasValue)
                {
                    var min = ParameterValue.Clamp(outputRange.Minimum, parameter.Definition.HardRange.Value.Minimum, parameter.Definition.HardRange.Value.Maximum);
                    var max = ParameterValue.Clamp(outputRange.Maximum, parameter.Definition.HardRange.Value.Minimum, parameter.Definition.HardRange.Value.Maximum);
                    if (min.IsFailure || max.IsFailure || min.Value != outputRange.Minimum || max.Value != outputRange.Maximum) return Result.Failure(ProjectDiagnostics.Rejected("project.expression.output_range_invalid", "Expression output range must be within the hard range."));
                }
            }
            foreach (var id in _expression.Expression.ReferencedControls)
            {
                var control = document.FindLogicalControl(id);
                if (control == null) return Result.Failure(ProjectDiagnostics.Rejected("project.expression.control_missing", "Expression references a missing logical control."));
                if (control.Kind != LogicalControlKind.Value) return Result.Failure(ProjectDiagnostics.Rejected("project.expression.trigger_invalid", "PresetTrigger cannot be used in an expression."));
                if (!control.Targets.Any(x => x.NodeId == _expression.NodeId && x.ParameterId == _expression.ParameterId && x.ParameterType == parameter.Definition.Type && !x.IsBroken)) return Result.Failure(ProjectDiagnostics.Rejected("project.expression.target_missing", "Expression control has no compatible target mapping."));
            }
            document.AddExpression(_expression); return Result.Success();
        }
    }

    public sealed class SetLogicalControlMappingsCommand : IProjectCommand
    {
        private readonly LogicalControlId _id;
        private readonly IReadOnlyList<ControlMappingRecord> _mappings;
        public string Name => "Set Logical Control Mappings";
        public SetLogicalControlMappingsCommand(LogicalControlId id, IEnumerable<ControlMappingRecord> mappings)
        {
            _id = id;
            _mappings = new ReadOnlyCollection<ControlMappingRecord>((mappings ?? Enumerable.Empty<ControlMappingRecord>()).ToList());
        }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Logical(_id); if (identity.IsFailure) return identity;
            var control = document.FindLogicalControl(_id);
            if (control == null) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.missing", "Logical control does not exist."));
            if (_mappings.Any(x => x == null)) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.mapping_invalid", "Control mappings cannot be null."));
            if (_mappings.GroupBy(x => new { x.Kind, x.PhysicalId, x.ControlPath }).Any(x => x.Count() > 1)) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.mapping_duplicate", "Control mappings must be unique."));
            document.ReplaceLogicalControl(control.WithMappings(_mappings));
            return Result.Success();
        }
    }

    public sealed class RenameLogicalControlCommand : IProjectCommand
    {
        private readonly LogicalControlId _id; private readonly string _name;
        public string Name => "Rename Logical Control";
        public RenameLogicalControlCommand(LogicalControlId id, string name) { _id = id; _name = name; }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Logical(_id); if (identity.IsFailure) return identity;
            if (string.IsNullOrWhiteSpace(_name)) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.name_invalid", "Logical control name is required."));
            var control = document.FindLogicalControl(_id); if (control == null) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.missing", "Logical control does not exist."));
            if (document.LogicalControls.Any(x => x.Id != _id && string.Equals(x.Name, _name.Trim(), StringComparison.OrdinalIgnoreCase))) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.name_exists", "Logical control name must be unique."));
            document.ReplaceLogicalControl(control.WithName(_name)); return Result.Success();
        }
    }

    public sealed class SetLogicalControlTargetsCommand : IProjectCommand
    {
        private readonly LogicalControlId _id; private readonly IReadOnlyList<LogicalControlTargetRecord> _targets;
        public string Name => "Set Logical Control Targets";
        public SetLogicalControlTargetsCommand(LogicalControlId id, IEnumerable<LogicalControlTargetRecord> targets) { _id = id; _targets = new ReadOnlyCollection<LogicalControlTargetRecord>((targets ?? Enumerable.Empty<LogicalControlTargetRecord>()).ToList()); }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Logical(_id); if (identity.IsFailure) return identity;
            var control = document.FindLogicalControl(_id); if (control == null) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.missing", "Logical control does not exist."));
            if (control.Kind != LogicalControlKind.Value) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.target_kind", "Only Value controls can target parameters."));
            if (_targets.GroupBy(x => new { x.NodeId, x.ParameterId }).Any(x => x.Count() > 1)) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.target_duplicate", "Logical control targets must be unique."));
            foreach (var target in _targets)
            {
                var node = document.FindNode(target.NodeId); var parameter = node?.FindParameter(target.ParameterId);
                if (node == null || parameter == null || parameter.Definition.Type != target.ParameterType) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.target_missing", "Logical control target does not exist or has an incompatible type."));
            }
            document.ReplaceLogicalControl(control.WithTargets(_targets)); return Result.Success();
        }
    }

    public sealed class SetNodeRawStateCommand : IProjectCommand
    {
        private readonly NodeInstanceId _id; private readonly string _rawState;
        public string Name => "Update Node State";
        public SetNodeRawStateCommand(NodeInstanceId id, string rawState) { _id = id; _rawState = rawState; }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Node(_id); if (identity.IsFailure) return identity;
            if (string.IsNullOrWhiteSpace(_rawState)) return Result.Failure(ProjectDiagnostics.Rejected("project.node.state_invalid", "Node state is required."));
            var node = document.FindNode(_id); if (node == null) return Result.Failure(ProjectDiagnostics.Rejected("project.graph.node_missing", "Node does not exist."));
            document.ReplaceNode(node.WithRawState(_rawState)); return Result.Success();
        }
    }

    public sealed class SetPresetTriggerBindingCommand : IProjectCommand
    {
        private readonly LogicalControlId _id;
        private readonly PresetId? _presetId;
        public string Name => "Set Preset Trigger Binding";
        public SetPresetTriggerBindingCommand(LogicalControlId id, PresetId? presetId) { _id = id; _presetId = presetId; }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Logical(_id); if (identity.IsFailure) return identity;
            var control = document.FindLogicalControl(_id);
            if (control == null || control.Kind != LogicalControlKind.PresetTrigger) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.trigger_invalid", "A preset binding requires a PresetTrigger control."));
            if (_presetId.HasValue && document.FindPreset(_presetId.Value) == null) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.preset_missing", "Preset binding target does not exist."));
            document.ReplaceLogicalControl(control.WithPreset(_presetId));
            return Result.Success();
        }
    }

    public sealed class AddPresetCommand : IProjectCommand
    {
        private readonly PresetRecord _preset;
        public string Name => "Add Preset";
        public AddPresetCommand(PresetRecord preset) { _preset = preset ?? throw new ArgumentNullException(nameof(preset)); }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Preset(_preset); if (identity.IsFailure) return identity;
            if (document.FindPreset(_preset.Id) != null) return Result.Failure(ProjectDiagnostics.Rejected("project.preset.exists", "Preset ID already exists."));
            if (document.Presets.Any(x => string.Equals(x.Name, _preset.Name, StringComparison.OrdinalIgnoreCase))) return Result.Failure(ProjectDiagnostics.Rejected("project.preset.name_exists", "Preset name must be unique ignoring case."));
            var normalized = _preset.WithEntries(_preset.Entries.Select(entry =>
            {
                var node = document.FindNode(entry.NodeId); var parameter = node?.FindParameter(entry.ParameterId);
                var mediaAvailable = !entry.Value.IsMediaAssetSelected || document.FindMediaAsset(entry.Value.AsMediaAsset().Value) != null;
                return node != null && parameter != null && parameter.Definition.Type == entry.ParameterType && mediaAvailable ? entry : entry.AsBroken("Preset target is missing.");
            }));
            // Keep an explicitly broken entry broken. Revalidation is for
            // references that become valid after a later graph/media repair;
            // running the broad pass here would erase the user's broken entry
            // marker even when it was intentionally loaded as invalid. A
            // deleted-and-restored preset is a narrower case: its trigger
            // reference is now provably present and must leave Broken state.
            document.AddPreset(normalized);
            foreach (var control in document.LogicalControls.Where(x => x.PresetIsBroken && x.PresetId.HasValue && x.PresetId.Value == _preset.Id).ToList())
                document.ReplaceLogicalControl(control.AsRepairedPreset());
            return Result.Success();
        }
    }

    public sealed class RenamePresetCommand : IProjectCommand
    {
        private readonly PresetId _id; private readonly string _name;
        public string Name => "Rename Preset";
        public RenamePresetCommand(PresetId id, string name) { _id = id; _name = name; }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Preset(_id); if (identity.IsFailure) return identity;
            if (string.IsNullOrWhiteSpace(_name)) return Result.Failure(ProjectDiagnostics.Rejected("project.preset.name_invalid", "Preset name is required."));
            var preset = document.FindPreset(_id); if (preset == null) return Result.Failure(ProjectDiagnostics.Rejected("project.preset.missing", "Preset does not exist."));
            if (document.Presets.Any(x => x.Id != _id && string.Equals(x.Name, _name.Trim(), StringComparison.OrdinalIgnoreCase))) return Result.Failure(ProjectDiagnostics.Rejected("project.preset.name_exists", "Preset name must be unique ignoring case."));
            document.ReplacePreset(preset.WithName(_name)); return Result.Success();
        }
    }

    public sealed class SetPresetEntriesCommand : IProjectCommand
    {
        private readonly PresetId _id; private readonly IReadOnlyList<PresetEntryRecord> _entries;
        public string Name => "Set Preset Entries";
        public SetPresetEntriesCommand(PresetId id, IEnumerable<PresetEntryRecord> entries) { _id = id; _entries = new ReadOnlyCollection<PresetEntryRecord>((entries ?? Enumerable.Empty<PresetEntryRecord>()).ToList()); }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Preset(_id); if (identity.IsFailure) return identity;
            var preset = document.FindPreset(_id); if (preset == null) return Result.Failure(ProjectDiagnostics.Rejected("project.preset.missing", "Preset does not exist."));
            if (_entries.GroupBy(x => new { x.NodeId, x.ParameterId }).Any(x => x.Count() > 1)) return Result.Failure(ProjectDiagnostics.Rejected("project.preset.entry_duplicate", "Preset entries must be unique."));
            var normalized = _entries.Select(entry =>
            {
                var node = document.FindNode(entry.NodeId); var parameter = node?.FindParameter(entry.ParameterId);
                return node != null && parameter != null && parameter.Definition.Type == entry.ParameterType ? entry : entry.AsBroken("Preset target is missing.");
            });
            document.ReplacePreset(preset.WithEntries(normalized)); return Result.Success();
        }
    }

    public sealed class ReplaceUiCommand : IProjectCommand
    {
        private readonly ProjectUiStateRecord _ui;
        public string Name => "Update Project UI State";
        public ReplaceUiCommand(ProjectUiStateRecord ui) { _ui = ui; }
        public Result Apply(ProjectDocument document)
        {
            if (_ui == null) return Result.Failure(ProjectDiagnostics.Rejected("project.ui.invalid", "UI state is required."));
            document.ReplaceUi(_ui); return Result.Success();
        }
    }

    public sealed class SetOutputSettingsCommand : IProjectCommand
    {
        private readonly ProjectOutputSettings _settings;
        public string Name => "Update Output Settings";
        public SetOutputSettingsCommand(ProjectOutputSettings settings) { _settings = settings; }
        public Result Apply(ProjectDocument document)
        {
            if (_settings == null) return Result.Failure(ProjectDiagnostics.Rejected("project.settings.invalid", "Output settings are required."));
            document.ReplaceSettings(_settings); return Result.Success();
        }
    }

    public sealed class ApplyPresetCommand : IProjectCommand
    {
        private readonly PresetId _presetId;
        public string Name => "Apply Preset";
        public ApplyPresetCommand(PresetId presetId) { _presetId = presetId; }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Preset(_presetId); if (identity.IsFailure) return identity;
            var preset = document.FindPreset(_presetId); if (preset == null) return Result.Failure(ProjectDiagnostics.Rejected("project.preset.missing", "Preset does not exist."));
            if (preset.IsBroken) return Result.Failure(ProjectDiagnostics.Rejected("project.preset.broken", "Preset contains a broken item and is applied atomically."));
            var replacements = new List<Tuple<NodeRecord, ParameterId, ParameterValue>>();
            foreach (var entry in preset.Entries)
            {
                var entryIdentity = ProjectCommandValidation.Node(entry.NodeId); if (entryIdentity.IsFailure) return entryIdentity;
                var node = document.FindNode(entry.NodeId); if (node == null) return Result.Failure(ProjectDiagnostics.Rejected("project.preset.node_missing", "Preset target node does not exist."));
                var parameter = node.FindParameter(entry.ParameterId); if (parameter == null || parameter.Definition.Type != entry.ParameterType || entry.Value.Type != parameter.Definition.Type) return Result.Failure(ProjectDiagnostics.Rejected("project.preset.parameter_invalid", "Preset target parameter is missing or has an incompatible type."));
                if (entry.Value.IsMediaAssetSelected && document.FindMediaAsset(entry.Value.AsMediaAsset().Value) == null) return Result.Failure(ProjectDiagnostics.Rejected("project.preset.media_missing", "Preset references a missing media asset."));
                var value = parameter.Definition.Clamp(entry.Value); if (value.IsFailure) return Result.Failure(value.Diagnostic);
                replacements.Add(Tuple.Create(node, entry.ParameterId, value.Value));
            }
            foreach (var replacement in replacements)
            {
                var current = document.FindNode(replacement.Item1.Id);
                document.ReplaceNode(current.WithParameter(replacement.Item2, replacement.Item3));
            }
            return Result.Success();
        }
    }

    public sealed class AddMediaAssetCommand : IProjectCommand
    {
        private readonly MediaAssetRecord _asset;
        public string Name => "Add Media Asset";
        public AddMediaAssetCommand(MediaAssetRecord asset) { _asset = asset ?? throw new ArgumentNullException(nameof(asset)); }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Media(_asset.Id); if (identity.IsFailure) return identity;
            if (document.FindMediaAsset(_asset.Id) != null) return Result.Failure(ProjectDiagnostics.Rejected("project.media.exists", "Media asset ID already exists."));
            document.AddMediaAsset(_asset); document.RevalidateBrokenReferences(); return Result.Success();
        }
    }

    public sealed class AddMediaAssetsCommand : IProjectCommand
    {
        private readonly IReadOnlyList<MediaAssetRecord> _assets;
        public string Name => "Add Media Assets";
        public AddMediaAssetsCommand(IEnumerable<MediaAssetRecord> assets) { _assets = new ReadOnlyCollection<MediaAssetRecord>((assets ?? Enumerable.Empty<MediaAssetRecord>()).ToList()); }
        public Result Apply(ProjectDocument document)
        {
            if (_assets.Count == 0) return Result.Failure(ProjectDiagnostics.Rejected("project.media.empty_batch", "At least one media asset is required."));
            if (_assets.Any(x => x == null) || _assets.GroupBy(x => x.Id).Any(x => x.Count() > 1)) return Result.Failure(ProjectDiagnostics.Rejected("project.media.duplicate_id", "Media asset IDs must be unique."));
            foreach (var asset in _assets)
            {
                var identity = ProjectCommandValidation.Media(asset.Id); if (identity.IsFailure) return identity;
                if (document.FindMediaAsset(asset.Id) != null) return Result.Failure(ProjectDiagnostics.Rejected("project.media.exists", "Media asset ID already exists."));
            }
            foreach (var asset in _assets) document.AddMediaAsset(asset);
            document.RevalidateBrokenReferences(); return Result.Success();
        }
    }

    public sealed class DeleteLogicalControlCommand : IProjectCommand
    {
        private readonly LogicalControlId _id;
        public string Name => "Delete Logical Control";
        public DeleteLogicalControlCommand(LogicalControlId id) { _id = id; }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Logical(_id); if (identity.IsFailure) return identity;
            if (document.FindLogicalControl(_id) == null) return Result.Failure(ProjectDiagnostics.Rejected("project.logical_control.missing", "Logical control does not exist."));
            document.RemoveLogicalControl(_id); return Result.Success();
        }
    }

    public sealed class DeletePresetCommand : IProjectCommand
    {
        private readonly PresetId _id;
        public string Name => "Delete Preset";
        public DeletePresetCommand(PresetId id) { _id = id; }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Preset(_id); if (identity.IsFailure) return identity;
            if (document.FindPreset(_id) == null) return Result.Failure(ProjectDiagnostics.Rejected("project.preset.missing", "Preset does not exist."));
            document.RemovePreset(_id); return Result.Success();
        }
    }

    public sealed class DeleteMediaAssetCommand : IProjectCommand
    {
        private readonly MediaAssetId _id;
        public string Name => "Delete Media Asset";
        public DeleteMediaAssetCommand(MediaAssetId id) { _id = id; }
        public Result Apply(ProjectDocument document)
        {
            var identity = ProjectCommandValidation.Media(_id); if (identity.IsFailure) return identity;
            if (document.FindMediaAsset(_id) == null) return Result.Failure(ProjectDiagnostics.Rejected("project.media.missing", "Media asset does not exist."));
            document.RemoveMediaAsset(_id); return Result.Success();
        }
    }

    internal static class ProjectCommandValidation
    {
        public static Result Node(NodeInstanceId id) => id.IsUuidV4 ? Result.Success() : Result.Failure(ProjectDiagnostics.Rejected("project.identity.node_uuid", "NodeInstanceId must be UUID v4 at the command boundary."));
        public static Result Media(MediaAssetId id) => id.IsUuidV4 ? Result.Success() : Result.Failure(ProjectDiagnostics.Rejected("project.identity.media_uuid", "MediaAssetId must be UUID v4 at the command boundary."));
        public static Result Preset(PresetId id) => id.IsUuidV4 ? Result.Success() : Result.Failure(ProjectDiagnostics.Rejected("project.identity.preset_uuid", "PresetId must be UUID v4 at the command boundary."));
        public static Result Logical(LogicalControlId id) => id.IsUuidV4 ? Result.Success() : Result.Failure(ProjectDiagnostics.Rejected("project.identity.logical_control_uuid", "LogicalControlId must be UUID v4 at the command boundary."));
        public static Result LogicalControl(LogicalControlRecord control)
        {
            var result = Logical(control.Id); if (result.IsFailure) return result;
            foreach (var target in control.Targets) { result = Node(target.NodeId); if (result.IsFailure) return result; }
            if (control.PresetId.HasValue) { result = Preset(control.PresetId.Value); if (result.IsFailure) return result; }
            return Result.Success();
        }
        public static Result Expression(ParameterExpressionRecord expression)
        {
            var result = Node(expression.NodeId); if (result.IsFailure) return result;
            foreach (var id in expression.Expression.ReferencedControls) { result = Logical(id); if (result.IsFailure) return result; }
            return Result.Success();
        }
        public static Result Preset(PresetRecord preset)
        {
            var result = Preset(preset.Id); if (result.IsFailure) return result;
            foreach (var entry in preset.Entries)
            {
                result = Node(entry.NodeId); if (result.IsFailure) return result;
                if (entry.Value.IsMediaAssetSelected) { result = Media(entry.Value.AsMediaAsset().Value); if (result.IsFailure) return result; }
            }
            return Result.Success();
        }
    }
}
