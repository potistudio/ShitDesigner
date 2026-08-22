using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using ShitDesigner.Core;
using ShitDesigner.Project;

namespace ShitDesigner.Graph
{
    /// <summary>
    /// Stable IDs and limits owned by the graph module.  The graph module never
    /// invents a second representation for the persisted Project port types.
    /// </summary>
    public static class GraphConstants
    {
        public const int MaxConnections = 4096;
        public const int MaxUndoEntries = 200;
        public const string ProgramOutputTypeId = "system.program_output";
        public const string PreviewTypeId = "system.preview";
        public const string FeedbackTypeId = "system.feedback";
        public const string UnknownNodeTypeId = "system.unknown_node";
        public const string ImagePortId = "image";
        public const string ColorToVector4ConversionId = "core.color_to_vector4.v1";
        public const string Vector4ToColorConversionId = "core.vector4_to_color.v1";
    }

    /// <summary>Stable persisted IDs for the initial eight port value types.</summary>
    public static class PortTypeCatalog
    {
        public static string GetId(PortType type)
        {
            switch (type)
            {
                case PortType.ImageFrame: return "core.image_frame";
                case PortType.Float: return "core.float32";
                case PortType.Int: return "core.int32";
                case PortType.Bool: return "core.bool";
                case PortType.Vector2: return "core.vector2f";
                case PortType.Vector3: return "core.vector3f";
                case PortType.Vector4: return "core.vector4f";
                case PortType.Color: return "core.color_linear";
                default: throw new ArgumentOutOfRangeException(nameof(type));
            }
        }

        public static bool TryParse(string id, out PortType type)
        {
            switch (id)
            {
                case "core.image_frame": type = PortType.ImageFrame; return true;
                case "core.float32": type = PortType.Float; return true;
                case "core.int32": type = PortType.Int; return true;
                case "core.bool": type = PortType.Bool; return true;
                case "core.vector2f": type = PortType.Vector2; return true;
                case "core.vector3f": type = PortType.Vector3; return true;
                case "core.vector4f": type = PortType.Vector4; return true;
                case "core.color_linear": type = PortType.Color; return true;
                default: type = default(PortType); return false;
            }
        }
    }

    public sealed class PortDefinition
    {
        public PortId Id { get; }
        public string DisplayName { get; }
        public PortDirection Direction { get; }
        public PortType Type { get; }
        public bool Required { get; }
        public DefaultImageKind? DefaultImage { get; }

        public PortDefinition(PortId id, string displayName, PortDirection direction, PortType type, bool required, DefaultImageKind? defaultImage = null)
        {
            if (id.IsEmpty) throw new ArgumentException("Port ID is required.", nameof(id));
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Port display name is required.", nameof(displayName));
            if (direction == PortDirection.Output && defaultImage.HasValue) throw new ArgumentException("Only inputs may have a default image.", nameof(defaultImage));
            if (defaultImage.HasValue && (direction != PortDirection.Input || required || type != PortType.ImageFrame)) throw new ArgumentException("Default image requires an optional ImageFrame input.", nameof(defaultImage));
            Id = id;
            DisplayName = displayName.Trim();
            Direction = direction;
            Type = type;
            Required = required;
            DefaultImage = defaultImage;
        }

        public PortSnapshotRecord ToSnapshot() => new PortSnapshotRecord(Id, Direction, Type, Required, DefaultImage);
    }

    public sealed class NodeTypeDefinition
    {
        private readonly IReadOnlyList<PortDefinition> _ports;
        private readonly IReadOnlyList<ParameterDefinition> _parameters;

        public NodeTypeId TypeId { get; }
        public int SchemaVersion { get; }
        public string DisplayName { get; }
        public string Category { get; }
        public bool SystemOwned { get; }
        public bool UserAddable { get; }
        public IReadOnlyList<PortDefinition> Ports => _ports;
        public IReadOnlyList<ParameterDefinition> Parameters => _parameters;

        public NodeTypeDefinition(NodeTypeId typeId, int schemaVersion, string displayName, string category, IEnumerable<PortDefinition> ports, IEnumerable<ParameterDefinition> parameters = null, bool systemOwned = false, bool userAddable = true)
        {
            if (typeId.IsEmpty) throw new ArgumentException("Node type ID is required.", nameof(typeId));
            if (schemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Node display name is required.", nameof(displayName));
            if (string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Node category is required.", nameof(category));
            var portList = (ports ?? Enumerable.Empty<PortDefinition>()).ToList();
            var parameterList = (parameters ?? Enumerable.Empty<ParameterDefinition>()).ToList();
            if (portList.GroupBy(x => x.Id).Any(x => x.Count() > 1)) throw new ArgumentException("Port IDs must be unique.", nameof(ports));
            if (parameterList.GroupBy(x => x.Id).Any(x => x.Count() > 1)) throw new ArgumentException("Parameter IDs must be unique.", nameof(parameters));
            if (portList.Any(x => x.Direction == PortDirection.Output && x.Id.Value == GraphConstants.ImagePortId && (x.Type != PortType.ImageFrame || !string.Equals(x.DisplayName, "Image", StringComparison.Ordinal)))) throw new ArgumentException("The primary Image output must be an ImageFrame named Image.", nameof(ports));
            if (!systemOwned && portList.Any(x => x.Id.Value.StartsWith("system_", StringComparison.Ordinal))) throw new ArgumentException("system_ ports are reserved for system-owned node types.", nameof(ports));
            TypeId = typeId;
            SchemaVersion = schemaVersion;
            DisplayName = displayName.Trim();
            Category = category.Trim();
            SystemOwned = systemOwned;
            UserAddable = userAddable;
            _ports = new ReadOnlyCollection<PortDefinition>(portList);
            _parameters = new ReadOnlyCollection<ParameterDefinition>(parameterList);
        }

        public PortDefinition FindPort(PortId id) => _ports.FirstOrDefault(x => x.Id == id);
        public ParameterDefinition FindParameter(ParameterId id) => _parameters.FirstOrDefault(x => x.Id == id);
    }

    public sealed class PortConversionDefinition
    {
        private static readonly Regex StableIdPattern = new Regex("^[a-z0-9]+\\.[a-z0-9]+_to_[a-z0-9]+\\.v[1-9][0-9]*$", RegexOptions.CultureInvariant);
        public string Id { get; }
        public PortType SourceType { get; }
        public PortType DestinationType { get; }
        public bool IsLossless { get; }
        public bool IsDefault { get; }

        public PortConversionDefinition(string id, PortType sourceType, PortType destinationType, bool isLossless = true, bool isDefault = true)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Conversion ID is required.", nameof(id));
            Id = id.Trim();
            if (!StableIdPattern.IsMatch(Id)) throw new ArgumentException("Conversion ID must use vendor.source_to_target.vN format.", nameof(id));
            SourceType = sourceType;
            DestinationType = destinationType;
            IsLossless = isLossless;
            IsDefault = isDefault;
        }
    }

    /// <summary>Central registry for direct, lossless implicit conversions.</summary>
    public sealed class PortConversionRegistry
    {
        private readonly Dictionary<string, PortConversionDefinition> _byId = new Dictionary<string, PortConversionDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<Tuple<PortType, PortType>, string> _defaults = new Dictionary<Tuple<PortType, PortType>, string>();

        public IReadOnlyCollection<PortConversionDefinition> Definitions => new ReadOnlyCollection<PortConversionDefinition>(_byId.Values.OrderBy(x => x.Id, StringComparer.Ordinal).ToList());

        public static PortConversionRegistry CreateInitial()
        {
            var registry = new PortConversionRegistry();
            registry.Register(new PortConversionDefinition(GraphConstants.ColorToVector4ConversionId, PortType.Color, PortType.Vector4));
            registry.Register(new PortConversionDefinition(GraphConstants.Vector4ToColorConversionId, PortType.Vector4, PortType.Color));
            return registry;
        }

        public Result Register(PortConversionDefinition definition)
        {
            if (definition == null) return Failure("graph.conversion.invalid", "Conversion definition is required.");
            if (!definition.IsLossless) return Failure("graph.conversion.lossy_implicit", "Lossy conversions must be explicit nodes.");
            if (_byId.ContainsKey(definition.Id)) return Failure("graph.conversion.duplicate_id", "Conversion ID is already registered.");
            var key = Tuple.Create(definition.SourceType, definition.DestinationType);
            _byId.Add(definition.Id, definition);
            if (definition.IsDefault)
            {
                if (_defaults.ContainsKey(key))
                {
                    _byId.Remove(definition.Id);
                    return Failure("graph.conversion.duplicate_default", "Only one implicit default is allowed for a type pair.");
                }
                _defaults.Add(key, definition.Id);
            }
            return Result.Success();
        }

        public bool TryGet(string id, out PortConversionDefinition definition)
        {
            if (id == null)
            {
                definition = null;
                return false;
            }
            return _byId.TryGetValue(id, out definition);
        }

        public Result<string> Resolve(PortType source, PortType destination)
        {
            if (source == destination) return Result<string>.Success(null);
            return _defaults.TryGetValue(Tuple.Create(source, destination), out var id)
                ? Result<string>.Success(id)
                : Result<string>.Failure(FailureDiagnostic("graph.conversion.not_found", "No direct implicit conversion is registered."));
        }

        public bool IsCompatible(PortType source, PortType destination, string conversionId)
        {
            if (source == destination) return string.IsNullOrEmpty(conversionId);
            if (string.IsNullOrEmpty(conversionId)) return Resolve(source, destination).IsSuccess;
            return TryGet(conversionId, out var definition)
                && definition.IsLossless
                && definition.SourceType == source
                && definition.DestinationType == destination;
        }

        /// <summary>
        /// Validates a connection that was read from storage.  Storage never
        /// gets an implicit conversion selected on its behalf: a differing
        /// pair must carry the exact registered conversion ID that was saved.
        /// </summary>
        public bool IsCompatibleSaved(PortType source, PortType destination, string conversionId)
        {
            if (source == destination) return string.IsNullOrEmpty(conversionId);
            if (string.IsNullOrEmpty(conversionId)) return false;
            return TryGet(conversionId, out var definition)
                && definition.IsLossless
                && definition.SourceType == source
                && definition.DestinationType == destination;
        }

        private static Result Failure(string code, string message) => Result.Failure(FailureDiagnostic(code, message));
        private static Diagnostic FailureDiagnostic(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message);
    }

    public sealed class NodeTypeRegistry
    {
        private readonly Dictionary<NodeTypeId, NodeTypeDefinition> _definitions = new Dictionary<NodeTypeId, NodeTypeDefinition>();
        public PortConversionRegistry Conversions { get; }
        /// <summary>Monotonically advances only when a definition is accepted.
        /// Consumers can avoid materializing Definitions on unchanged frames.</summary>
        public long Revision { get; private set; }
        public IReadOnlyCollection<NodeTypeDefinition> Definitions => new ReadOnlyCollection<NodeTypeDefinition>(_definitions.Values.OrderBy(x => x.TypeId.Value, StringComparer.Ordinal).ToList());

        public NodeTypeRegistry(PortConversionRegistry conversions = null)
        {
            Conversions = conversions ?? PortConversionRegistry.CreateInitial();
        }

        public Result Register(NodeTypeDefinition definition)
        {
            if (definition == null) return Failure("graph.registry.invalid", "Node type definition is required.");
            if (_definitions.ContainsKey(definition.TypeId)) return Failure("graph.registry.duplicate_type", "Node type ID is already registered.");
            _definitions.Add(definition.TypeId, definition);
            Revision++;
            return Result.Success();
        }

        public bool TryGet(NodeTypeId typeId, out NodeTypeDefinition definition) => _definitions.TryGetValue(typeId, out definition);
        public bool Contains(NodeTypeId typeId) => _definitions.ContainsKey(typeId);
        private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
    }

    /// <summary>
    /// Mutable working copy of the persisted graph.  It intentionally contains
    /// only graph structure; ProjectDocument remains the owner of persistence.
    /// </summary>
    public sealed class GraphState
    {
        private readonly List<NodeRecord> _nodes;
        private readonly List<ConnectionRecord> _connections;

        public long Revision { get; internal set; }
        public IReadOnlyList<NodeRecord> Nodes => new ReadOnlyCollection<NodeRecord>(_nodes);
        public IReadOnlyList<ConnectionRecord> Connections => new ReadOnlyCollection<ConnectionRecord>(_connections);

        public GraphState(IEnumerable<NodeRecord> nodes = null, IEnumerable<ConnectionRecord> connections = null, long revision = 0)
        {
            _nodes = (nodes ?? Enumerable.Empty<NodeRecord>()).ToList();
            _connections = (connections ?? Enumerable.Empty<ConnectionRecord>()).ToList();
            Revision = revision;
        }

        public static GraphState FromProject(ProjectDocument document)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            // DocumentRevision belongs to ProjectDocument.  GraphRevision is a
            // runtime structural revision and starts at zero for a loaded graph;
            // callers may supply the last graph revision when restoring a live
            // session through the constructor.
            return new GraphState(document.Nodes, document.Connections, 0);
        }

        public GraphState Clone() => new GraphState(_nodes, _connections, Revision);
        public NodeRecord FindNode(NodeInstanceId id) => _nodes.FirstOrDefault(x => x.Id == id);
        public ConnectionRecord FindConnection(ConnectionId id) => _connections.FirstOrDefault(x => x.Id == id);
        public ConnectionRecord FindInputConnection(NodeInstanceId nodeId, PortId portId) => _connections.FirstOrDefault(x => x.DestinationNodeId == nodeId && x.DestinationPortId == portId);
        public bool HasDuplicateIds() => _nodes.GroupBy(x => x.Id).Any(x => x.Count() > 1) || _connections.GroupBy(x => x.Id).Any(x => x.Count() > 1);
        internal void AddNode(NodeRecord node) => _nodes.Add(node);
        internal void ReplaceNode(NodeRecord node) { var index = _nodes.FindIndex(x => x.Id == node.Id); if (index < 0) throw new InvalidOperationException("Node not found."); _nodes[index] = node; }
        internal void RemoveNode(NodeInstanceId id) { _nodes.RemoveAll(x => x.Id == id); _connections.RemoveAll(x => x.SourceNodeId == id || x.DestinationNodeId == id); }
        internal void AddConnection(ConnectionRecord connection) { _connections.RemoveAll(x => x.DestinationNodeId == connection.DestinationNodeId && x.DestinationPortId == connection.DestinationPortId); _connections.Add(connection); }
        internal void RemoveConnection(ConnectionId id) => _connections.RemoveAll(x => x.Id == id);
        internal void ReplaceConnection(ConnectionRecord connection) { var index = _connections.FindIndex(x => x.Id == connection.Id); if (index < 0) throw new InvalidOperationException("Connection not found."); _connections[index] = connection; }
        internal void ReplaceAll(IEnumerable<NodeRecord> nodes, IEnumerable<ConnectionRecord> connections) { _nodes.Clear(); _nodes.AddRange(nodes); _connections.Clear(); _connections.AddRange(connections); }
    }

    public enum GraphEditCommandKind
    {
        AddNode,
        DeleteNode,
        RestoreNodes,
        Connect,
        Disconnect,
        ReplaceInputConnection,
        SetEnabled,
        RestoreUnknownNode,
        Undo,
        Redo
    }

    public abstract class GraphEditCommand
    {
        public string CommandRequestId { get; }
        public long RequestedDocumentRevision { get; }
        public GraphEditCommandKind Kind { get; }

        protected GraphEditCommand(GraphEditCommandKind kind, string commandRequestId = null, long requestedDocumentRevision = -1)
        {
            CommandRequestId = string.IsNullOrWhiteSpace(commandRequestId) ? Guid.NewGuid().ToString("D") : commandRequestId.Trim();
            RequestedDocumentRevision = requestedDocumentRevision;
            Kind = kind;
        }

        internal abstract Result Apply(GraphBatchWorkspace workspace);
    }

    public sealed class AddNodeEditCommand : GraphEditCommand
    {
        public NodeRecord Node { get; }
        public AddNodeEditCommand(NodeRecord node, string commandRequestId = null, long requestedDocumentRevision = -1) : base(GraphEditCommandKind.AddNode, commandRequestId, requestedDocumentRevision) { Node = node ?? throw new ArgumentNullException(nameof(node)); }
        internal override Result Apply(GraphBatchWorkspace workspace) => workspace.AddNode(Node);
    }

    public sealed class DeleteNodeEditCommand : GraphEditCommand
    {
        public NodeInstanceId NodeId { get; }
        public DeleteNodeEditCommand(NodeInstanceId nodeId, string commandRequestId = null, long requestedDocumentRevision = -1) : base(GraphEditCommandKind.DeleteNode, commandRequestId, requestedDocumentRevision) { NodeId = nodeId; }
        internal override Result Apply(GraphBatchWorkspace workspace) => workspace.DeleteNode(NodeId);
    }

    public sealed class RestoreNodesEditCommand : GraphEditCommand
    {
        public IReadOnlyList<NodeRecord> Nodes { get; }
        public IReadOnlyList<ConnectionRecord> Connections { get; }
        public RestoreNodesEditCommand(IEnumerable<NodeRecord> nodes, IEnumerable<ConnectionRecord> connections, string commandRequestId = null, long requestedDocumentRevision = -1) : base(GraphEditCommandKind.RestoreNodes, commandRequestId, requestedDocumentRevision)
        {
            Nodes = new ReadOnlyCollection<NodeRecord>((nodes ?? Enumerable.Empty<NodeRecord>()).ToList());
            Connections = new ReadOnlyCollection<ConnectionRecord>((connections ?? Enumerable.Empty<ConnectionRecord>()).ToList());
        }
        internal override Result Apply(GraphBatchWorkspace workspace) => workspace.RestoreNodes(Nodes, Connections);
    }

    public sealed class ConnectEditCommand : GraphEditCommand
    {
        public ConnectionRecord Connection { get; }
        public ConnectEditCommand(ConnectionRecord connection, string commandRequestId = null, long requestedDocumentRevision = -1) : base(GraphEditCommandKind.Connect, commandRequestId, requestedDocumentRevision) { Connection = connection ?? throw new ArgumentNullException(nameof(connection)); }
        internal override Result Apply(GraphBatchWorkspace workspace) => workspace.Connect(Connection, false);
    }

    public sealed class ReplaceInputConnectionEditCommand : GraphEditCommand
    {
        public ConnectionRecord Connection { get; }
        public ReplaceInputConnectionEditCommand(ConnectionRecord connection, string commandRequestId = null, long requestedDocumentRevision = -1) : base(GraphEditCommandKind.ReplaceInputConnection, commandRequestId, requestedDocumentRevision) { Connection = connection ?? throw new ArgumentNullException(nameof(connection)); }
        internal override Result Apply(GraphBatchWorkspace workspace) => workspace.Connect(Connection, true);
    }

    public sealed class DisconnectEditCommand : GraphEditCommand
    {
        public ConnectionId ConnectionId { get; }
        public DisconnectEditCommand(ConnectionId connectionId, string commandRequestId = null, long requestedDocumentRevision = -1) : base(GraphEditCommandKind.Disconnect, commandRequestId, requestedDocumentRevision) { ConnectionId = connectionId; }
        internal override Result Apply(GraphBatchWorkspace workspace) => workspace.Disconnect(ConnectionId);
    }

    public sealed class SetNodeEnabledEditCommand : GraphEditCommand
    {
        public NodeInstanceId NodeId { get; }
        public bool Enabled { get; }
        public SetNodeEnabledEditCommand(NodeInstanceId nodeId, bool enabled, string commandRequestId = null, long requestedDocumentRevision = -1) : base(GraphEditCommandKind.SetEnabled, commandRequestId, requestedDocumentRevision) { NodeId = nodeId; Enabled = enabled; }
        internal override Result Apply(GraphBatchWorkspace workspace) => workspace.SetEnabled(NodeId, Enabled);
    }

    public sealed class RestoreUnknownNodeEditCommand : GraphEditCommand
    {
        public NodeInstanceId NodeId { get; }
        public UnknownNodeRecord Unknown { get; }
        public RestoreUnknownNodeEditCommand(NodeInstanceId nodeId, UnknownNodeRecord unknown, string commandRequestId = null, long requestedDocumentRevision = -1) : base(GraphEditCommandKind.RestoreUnknownNode, commandRequestId, requestedDocumentRevision) { NodeId = nodeId; Unknown = unknown ?? throw new ArgumentNullException(nameof(unknown)); }
        internal override Result Apply(GraphBatchWorkspace workspace) => workspace.RestoreUnknown(NodeId, Unknown);
    }

    public sealed class GraphBatchWorkspace
    {
        private readonly NodeTypeRegistry _registry;
        private readonly GraphState _original;
        public GraphState State { get; }
        public IReadOnlyList<GraphEditCommand> AppliedCommands => new ReadOnlyCollection<GraphEditCommand>(_applied);
        private readonly List<GraphEditCommand> _applied = new List<GraphEditCommand>();

        public GraphBatchWorkspace(GraphState source, NodeTypeRegistry registry)
        {
            _original = source?.Clone() ?? throw new ArgumentNullException(nameof(source));
            State = source.Clone();
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public Result Apply(GraphEditCommand command)
        {
            if (command == null) return Failure("graph.command.invalid", "Graph command is required.");
            var before = State.Clone();
            var result = command.Apply(this);
            if (result.IsSuccess) _applied.Add(command);
            else State.ReplaceAll(before.Nodes, before.Connections);
            return result;
        }

        internal Result AddNode(NodeRecord node)
        {
            if (State.FindNode(node.Id) != null) return Failure("graph.node.exists", "Node ID already exists.");
            if (node.SystemOwned || !node.UserAddable || node.TypeId.Value == GraphConstants.ProgramOutputTypeId) return Failure("graph.node.not_addable", "Node is not user-addable.");
            if (!_registry.Contains(node.TypeId) && !node.IsUnknown) return Failure("graph.node.type_unknown", "Node type is not registered.");
            State.AddNode(node);
            return Result.Success();
        }

        internal Result DeleteNode(NodeInstanceId nodeId)
        {
            var node = State.FindNode(nodeId);
            if (node == null) return Failure("graph.node.missing", "Node does not exist.");
            if (node.SystemOwned || node.TypeId.Value == GraphConstants.ProgramOutputTypeId) return Failure("graph.node.protected", "System-owned nodes cannot be deleted.");
            State.RemoveNode(nodeId);
            return Result.Success();
        }

        internal Result RestoreNodes(IReadOnlyList<NodeRecord> nodes, IReadOnlyList<ConnectionRecord> connections)
        {
            if (nodes.GroupBy(x => x.Id).Any(x => x.Count() > 1)) return Failure("graph.restore.node_duplicate", "A restore batch contains duplicate node IDs.");
            if (connections.GroupBy(x => x.Id).Any(x => x.Count() > 1)) return Failure("graph.restore.connection_duplicate", "A restore batch contains duplicate connection IDs.");
            foreach (var node in nodes)
            {
                if (State.FindNode(node.Id) != null) return Failure("graph.restore.node_exists", "A node to restore already exists.");
                if (node.TypeId.Value == GraphConstants.ProgramOutputTypeId) return Failure("graph.restore.protected", "ProgramOutput cannot be restored as a user node.");
                if (!node.IsUnknown && !_registry.Contains(node.TypeId)) return Failure("graph.restore.type_unknown", "A node type to restore is not registered.");
            }
            if (State.Connections.Count + connections.Count > GraphConstants.MaxConnections) return Failure("graph.connection.limit", "The project connection limit is 4096.");
            foreach (var connection in connections)
            {
                if (State.FindConnection(connection.Id) != null) return Failure("graph.restore.connection_exists", "A connection to restore already exists.");
            }
            foreach (var node in nodes) State.AddNode(node);
            foreach (var connection in connections)
            {
                // Broken records are deliberately retained.  They are
                // persistence evidence, not active edges, and may become
                // repairable when a missing node/type is restored later.
                if (connection.IsBroken)
                {
                    State.AddConnection(connection);
                    continue;
                }
                var result = ValidateConnection(connection, false, true);
                if (result.IsFailure)
                {
                    // A malformed saved edge is reclassified, never silently
                    // dropped and never repaired by selecting a new conversion.
                    if (IsSaveEdgeRepairableFailure(result.Diagnostic))
                    {
                        State.AddConnection(connection.AsBroken(result.Diagnostic.Message));
                        continue;
                    }
                    return result;
                }
                State.AddConnection(connection);
            }
            return ValidateAcyclic();
        }

        internal Result Connect(ConnectionRecord connection, bool replace)
        {
            var resolved = ResolveConnectionConversion(connection);
            if (resolved.IsFailure) return Result.Failure(resolved.Diagnostic);
            var candidate = resolved.Value;
            var validation = ValidateConnection(candidate, replace);
            if (validation.IsFailure) return validation;
            State.AddConnection(candidate);
            return ValidateAcyclic();
        }

        private Result<ConnectionRecord> ResolveConnectionConversion(ConnectionRecord connection)
        {
            if (connection == null) return Result<ConnectionRecord>.Failure(new Diagnostic(new DiagnosticCode("graph.connection.invalid"), Severity.Error, "Connection is required."));
            var sourceNode = State.FindNode(connection.SourceNodeId);
            var destinationNode = State.FindNode(connection.DestinationNodeId);
            if (sourceNode == null || destinationNode == null) return Result<ConnectionRecord>.Failure(new Diagnostic(new DiagnosticCode("graph.connection.node_missing"), Severity.Error, "Connection endpoint node does not exist."));
            var source = sourceNode.FindPort(connection.SourcePortId);
            var destination = destinationNode.FindPort(connection.DestinationPortId);
            if (source == null || destination == null) return Result<ConnectionRecord>.Success(connection);
            if (source.Type == destination.Type)
            {
                if (!string.IsNullOrEmpty(connection.ConversionId)) return Result<ConnectionRecord>.Failure(new Diagnostic(new DiagnosticCode("graph.connection.conversion_mismatch"), Severity.Error, "An exact type connection cannot carry a conversion."));
                return Result<ConnectionRecord>.Success(connection);
            }
            if (!string.IsNullOrEmpty(connection.ConversionId)) return Result<ConnectionRecord>.Success(connection);
            var resolved = _registry.Conversions.Resolve(source.Type, destination.Type);
            return resolved.IsSuccess
                ? Result<ConnectionRecord>.Success(new ConnectionRecord(connection.Id, connection.SourceNodeId, connection.SourcePortId, connection.DestinationNodeId, connection.DestinationPortId, resolved.Value, connection.IsBroken, connection.BrokenReason))
                : Result<ConnectionRecord>.Failure(resolved.Diagnostic);
        }

        internal Result Disconnect(ConnectionId id)
        {
            if (State.FindConnection(id) == null) return Failure("graph.connection.missing", "Connection does not exist.");
            State.RemoveConnection(id);
            return Result.Success();
        }

        internal Result SetEnabled(NodeInstanceId nodeId, bool enabled)
        {
            var node = State.FindNode(nodeId);
            if (node == null) return Failure("graph.node.missing", "Node does not exist.");
            if (node.TypeId.Value == GraphConstants.ProgramOutputTypeId && !enabled) return Failure("graph.node.program_enabled", "ProgramOutput must remain enabled.");
            State.ReplaceNode(CopyNode(node, enabled));
            return Result.Success();
        }

        internal Result RestoreUnknown(NodeInstanceId nodeId, UnknownNodeRecord unknown)
        {
            var node = State.FindNode(nodeId);
            if (node == null) return Failure("graph.node.missing", "Node does not exist.");
            if (!node.IsUnknown) return Failure("graph.unknown.not_placeholder", "Only an UnknownNode placeholder can be restored.");
            if (unknown == null) return Failure("graph.unknown.invalid", "UnknownNode metadata is required.");
            if (node.Unknown.OriginalNodeTypeId != unknown.OriginalNodeTypeId
                || node.Unknown.OriginalSchemaVersion != unknown.OriginalSchemaVersion
                || !string.Equals(node.Unknown.RawJsonValue, unknown.RawJsonValue, StringComparison.Ordinal))
                return Failure("graph.unknown.metadata_mismatch", "Restore metadata does not match the preserved UnknownNode metadata.");
            if (!_registry.TryGet(unknown.OriginalNodeTypeId, out var definition)) return Failure("graph.unknown.type_missing", "The original node type is not registered.");
            var parameters = definition.Parameters.Select(x => new ParameterRecord(x, x.DefaultValue));
            var restored = new NodeRecord(node.Id, definition.TypeId, definition.SchemaVersion, definition.DisplayName, node.Enabled, node.Position, parameters, definition.Ports.Select(x => x.ToSnapshot()), node.RawState, definition.SystemOwned, definition.UserAddable);
            State.ReplaceNode(restored);
            return Result.Success();
        }

        public Result ValidateFinalPlan(IEnumerable<OutputDemand> demands = null)
        {
            return EvaluationPlan.TryBuild(State, _registry, demands, out _, out var diagnostic)
                ? Result.Success()
                : Result.Failure(diagnostic);
        }

        private Result ValidateConnection(ConnectionRecord connection, bool replace, bool savedConnection = false)
        {
            if (connection == null) return Failure("graph.connection.invalid", "Connection is required.");
            if (connection.IsBroken) return Failure("graph.connection.broken_active", "Broken connections cannot be activated.");
            if (State.FindConnection(connection.Id) != null) return Failure("graph.connection.exists", "Connection ID already exists.");
            var sourceNode = State.FindNode(connection.SourceNodeId);
            var destinationNode = State.FindNode(connection.DestinationNodeId);
            if (sourceNode == null || destinationNode == null) return Failure("graph.connection.node_missing", "Connection endpoint node does not exist.");
            var source = sourceNode.FindPort(connection.SourcePortId);
            var destination = destinationNode.FindPort(connection.DestinationPortId);
            if (source == null || destination == null || source.Direction != PortDirection.Output || destination.Direction != PortDirection.Input) return Failure("graph.connection.port_invalid", "Connection endpoint port is invalid.");
            var compatible = savedConnection
                ? _registry.Conversions.IsCompatibleSaved(source.Type, destination.Type, connection.ConversionId)
                : _registry.Conversions.IsCompatible(source.Type, destination.Type, connection.ConversionId);
            if (!compatible) return Failure("graph.connection.type_mismatch", "Ports are not compatible with the selected direct conversion.");
            var existing = State.FindInputConnection(connection.DestinationNodeId, connection.DestinationPortId);
            if (existing != null && !replace) return Failure("graph.connection.input_occupied", "Input port already has a connection; use replacement.");
            if (State.Connections.Count - (existing == null ? 0 : 1) >= GraphConstants.MaxConnections) return Failure("graph.connection.limit", "The project connection limit is 4096.");
            if (WouldCycle(connection, existing)) return Failure("graph.connection.cycle", "The connection would create a same-frame cycle.");
            return Result.Success();
        }

        private static bool IsSaveEdgeRepairableFailure(Diagnostic diagnostic)
        {
            if (diagnostic == null) return false;
            var code = diagnostic.Code.Value;
            return code == "graph.connection.node_missing"
                || code == "graph.connection.port_invalid"
                || code == "graph.connection.type_mismatch"
                || code == "graph.connection.conversion_mismatch";
        }

        private bool WouldCycle(ConnectionRecord candidate, ConnectionRecord replaced)
        {
            var edges = State.Connections.Where(x => x != replaced && !x.IsBroken).ToList();
            edges.Add(candidate);
            return HasCycle(edges, State.Nodes);
        }

        private Result ValidateAcyclic()
        {
            return HasCycle(State.Connections.Where(x => !x.IsBroken).ToList(), State.Nodes)
                ? Failure("graph.plan.cycle", "The graph contains a cycle not separated by Feedback.")
                : Result.Success();
        }

        internal static bool HasCycle(IEnumerable<ConnectionRecord> edges, IEnumerable<NodeRecord> nodes)
        {
            var nodeList = nodes.ToList();
            if (nodeList.GroupBy(x => x.Id).Any(x => x.Count() > 1)) return true;
            var nodeMap = nodeList.ToDictionary(x => x.Id, x => x);
            var adjacency = new Dictionary<NodeInstanceId, List<NodeInstanceId>>();
            foreach (var node in nodeList) adjacency[node.Id] = new List<NodeInstanceId>();
            foreach (var edge in edges)
            {
                if (!nodeMap.TryGetValue(edge.SourceNodeId, out var source) || !nodeMap.ContainsKey(edge.DestinationNodeId)) continue;
                // Feedback input is a frame boundary: it is committed after
                // this frame evaluates. Its output is the previous-frame
                // image and remains a normal dependency for this frame.
                if (nodeMap[edge.DestinationNodeId].TypeId.Value == GraphConstants.FeedbackTypeId) continue;
                adjacency[edge.SourceNodeId].Add(edge.DestinationNodeId);
            }
            var visiting = new HashSet<NodeInstanceId>();
            var visited = new HashSet<NodeInstanceId>();
            bool Visit(NodeInstanceId id)
            {
                if (visiting.Contains(id)) return true;
                if (visited.Contains(id)) return false;
                visiting.Add(id);
                foreach (var next in adjacency[id]) if (Visit(next)) return true;
                visiting.Remove(id);
                visited.Add(id);
                return false;
            }
            return adjacency.Keys.Any(Visit);
        }

        private static NodeRecord CopyNode(NodeRecord node, bool enabled)
        {
            return new NodeRecord(node.Id, node.TypeId, node.SchemaVersion, node.DisplayName, enabled, node.Position, node.Parameters, node.Ports, node.RawState, node.SystemOwned, node.UserAddable, node.Unknown);
        }

        private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
        internal GraphState Original => _original;
        internal NodeTypeRegistry Registry => _registry;
    }

    public sealed class GraphPatch
    {
        public GraphState Before { get; }
        public GraphState After { get; }
        public IReadOnlyList<GraphEditCommand> Commands { get; }
        public long SourceRevision { get; }
        public long TargetRevision { get; }
        internal GraphPatch(GraphState before, GraphState after, IEnumerable<GraphEditCommand> commands, long sourceRevision, long targetRevision)
        {
            Before = before.Clone();
            After = after.Clone();
            Commands = new ReadOnlyCollection<GraphEditCommand>((commands ?? Enumerable.Empty<GraphEditCommand>()).ToList());
            SourceRevision = sourceRevision;
            TargetRevision = targetRevision;
        }

        internal GraphState RestoreBefore(long revision)
        {
            var state = Before.Clone();
            state.Revision = revision;
            return state;
        }

        internal GraphState RestoreAfter(long revision)
        {
            var state = After.Clone();
            state.Revision = revision;
            return state;
        }
    }

    /// <summary>Per-command outcome for a graph batch.</summary>
    public sealed class GraphBatchResult
    {
        public GraphPatch Patch { get; }
        /// <summary>
        /// Plan built for the committed candidate.  Keeping this on the batch
        /// result lets the frame coordinator install the exact plan that was
        /// validated, instead of rebuilding the graph a second time.
        /// </summary>
        public EvaluationPlan Plan { get; }
        public IReadOnlyList<Result> CommandResults { get; }
        /// <summary>True only after the candidate has been installed in the editor.</summary>
        public bool IsCommitted { get; }
        public Diagnostic Diagnostic { get; }

        internal GraphBatchResult(GraphPatch patch, EvaluationPlan plan, IEnumerable<Result> commandResults, Diagnostic diagnostic, bool isCommitted = false)
        {
            Patch = patch; Plan = plan;
            CommandResults = new ReadOnlyCollection<Result>((commandResults ?? Enumerable.Empty<Result>()).ToList());
            Diagnostic = diagnostic; IsCommitted = isCommitted;
        }

        internal GraphBatchResult MarkCommitted() => new GraphBatchResult(Patch, Plan, CommandResults, Diagnostic, true);
    }

    public sealed class GraphEditor
    {
        private readonly NodeTypeRegistry _registry;
        private readonly List<GraphPatch> _undo = new List<GraphPatch>();
        private readonly List<GraphPatch> _redo = new List<GraphPatch>();
        private GraphState _state;

        public GraphState State => _state.Clone();
        public NodeTypeRegistry Registry => _registry;
        public int UndoCount => _undo.Count;
        public int RedoCount => _redo.Count;
        public GraphEditor(GraphState initialState, NodeTypeRegistry registry)
        {
            _state = initialState?.Clone() ?? throw new ArgumentNullException(nameof(initialState));
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        }

        public Result<GraphPatch> ApplyBatch(IEnumerable<GraphEditCommand> commands, IEnumerable<OutputDemand> demands = null)
        {
            var detailed = ApplyBatchDetailed(commands, demands);
            return detailed.IsCommitted
                ? Result<GraphPatch>.Success(detailed.Patch)
                : Result<GraphPatch>.Failure(detailed.Diagnostic ?? FailureDiagnostic("graph.batch.failed", "No graph command was accepted."));
        }

        /// <summary>
        /// Applies all independent commands in order, collecting failures while
        /// continuing with later commands.  Only after the whole candidate is
        /// assembled is the EvaluationPlan built and the successful subset
        /// committed atomically.  The persistence-aware caller should use
        /// PrepareBatchDetailed followed by CommitCandidate so a failed
        /// document write cannot touch graph state or history.
        /// </summary>
        public GraphBatchResult ApplyBatchDetailed(IEnumerable<GraphEditCommand> commands, IEnumerable<OutputDemand> demands = null, bool deferRedoClear = false)
        {
            var candidate = PrepareBatchDetailed(commands, demands);
            if (candidate.Patch == null) return candidate;
            var committed = CommitCandidate(candidate.Patch, clearRedo: !deferRedoClear);
            if (committed.IsFailure)
                return new GraphBatchResult(null, null, candidate.CommandResults, committed.Diagnostic);
            return candidate.MarkCommitted();
        }

        /// <summary>
        /// Builds a graph candidate without changing state, undo, redo, or
        /// revision.  This is the first half of the graph/document transaction.
        /// </summary>
        public GraphBatchResult PrepareBatchDetailed(IEnumerable<GraphEditCommand> commands, IEnumerable<OutputDemand> demands = null)
        {
            var commandList = (commands ?? Enumerable.Empty<GraphEditCommand>()).ToList();
            if (commandList.Count == 0)
                return new GraphBatchResult(null, null, null, FailureDiagnostic("graph.batch.empty", "At least one graph command is required."));
            var workspace = new GraphBatchWorkspace(_state, _registry);
            var commandResults = new List<Result>(commandList.Count);
            Diagnostic firstFailure = null;
            foreach (var command in commandList)
            {
                var result = workspace.Apply(command);
                commandResults.Add(result);
                if (result.IsFailure && firstFailure == null) firstFailure = result.Diagnostic;
            }
            if (workspace.AppliedCommands.Count == 0)
                return new GraphBatchResult(null, null, commandResults, firstFailure ?? FailureDiagnostic("graph.batch.failed", "No graph command was accepted."));
            var before = _state.Clone();
            var after = workspace.State.Clone();
            after.Revision = before.Revision + 1;
            // Plan validation is performed against the candidate revision. The
            // builder returns a normalized clone, so malformed saved edges are
            // retained as Broken without mutating the caller's state.
            if (!EvaluationPlan.TryBuild(after, _registry, demands, out var candidatePlan, out var planDiagnostic, out var normalized))
                return new GraphBatchResult(null, null, commandResults, planDiagnostic);
            after.ReplaceAll(normalized.Nodes, normalized.Connections);
            var patch = new GraphPatch(before, after, workspace.AppliedCommands, before.Revision, after.Revision);
            return new GraphBatchResult(patch, candidatePlan, commandResults, firstFailure);
        }

        /// <summary>
        /// Installs a previously prepared candidate.  All conflict checks happen
        /// before the first mutation, so a caller can persist the patch first
        /// and this second phase cannot partially update graph history.
        /// </summary>
        public Result CommitCandidate(GraphPatch patch, bool clearRedo = true)
        {
            if (patch == null) return Failure("graph.commit.invalid", "Graph patch is required.");
            if (_state.Revision != patch.SourceRevision || !SameState(_state, patch.Before))
                return Failure("graph.commit.conflict", "The graph changed after the candidate was prepared.");
            var committed = patch.After.Clone();
            _state = committed;
            _undo.Add(patch);
            if (_undo.Count > GraphConstants.MaxUndoEntries) _undo.RemoveAt(0);
            if (clearRedo) _redo.Clear();
            return Result.Success();
        }

        /// <summary>Completes the second phase of a deferred graph commit.</summary>
        public Result FinalizeCommit(GraphPatch patch)
        {
            if (patch == null || _undo.Count == 0 || !ReferenceEquals(_undo[_undo.Count - 1], patch))
                return Failure("graph.commit.invalid", "The graph patch is not the current commit.");
            _redo.Clear();
            return Result.Success();
        }

        /// <summary>
        /// Reverts a just-committed candidate without creating an undo entry or
        /// advancing the structural revision. This is the rollback half of the
        /// Runtime two-phase graph/document commit.
        /// </summary>
        public Result Rollback(GraphPatch patch)
        {
            if (patch == null) return Failure("graph.rollback.invalid", "Graph patch is required.");
            if (_state.Revision != patch.TargetRevision || !SameState(_state, patch.After))
                return Failure("graph.rollback.conflict", "The graph changed after the candidate was committed.");
            if (_undo.Count == 0 || !ReferenceEquals(_undo[_undo.Count - 1], patch))
                return Failure("graph.rollback.history", "The candidate is not the current graph history entry.");
            _state = patch.Before.Clone();
            _undo.RemoveAt(_undo.Count - 1);
            return Result.Success();
        }

        /// <summary>Builds a normalization repair patch without mutating history.</summary>
        public Result<GraphPatch> PrepareNormalized(GraphState normalized)
        {
            if (normalized == null) return Result<GraphPatch>.Failure(FailureDiagnostic("graph.normalization.invalid", "Normalized graph state is required."));
            if (normalized.HasDuplicateIds()) return Result<GraphPatch>.Failure(FailureDiagnostic("graph.normalization.duplicate_id", "Normalized graph contains duplicate stable IDs."));
            var before = _state.Clone();
            var after = normalized.Clone();
            after.Revision = before.Revision + 1;
            return Result<GraphPatch>.Success(new GraphPatch(before, after, Enumerable.Empty<GraphEditCommand>(), before.Revision, after.Revision));
        }

        /// <summary>
        /// Installs a persisted normalization repair. Repairs are structural
        /// load/runtime maintenance, not user actions, so they never enter the
        /// regular undo stack. The revision still advances monotonically and
        /// redo is cleared because it no longer describes the repaired state.
        /// </summary>
        public Result CommitNormalizedRepair(GraphPatch patch)
        {
            if (patch == null) return Failure("graph.normalization.invalid", "Normalization patch is required.");
            if (_state.Revision != patch.SourceRevision || !SameState(_state, patch.Before))
                return Failure("graph.normalization.conflict", "The graph changed after normalization was prepared.");
            _state = patch.After.Clone();
            _redo.Clear();
            return Result.Success();
        }

        /// <summary>
        /// Compatibility helper for callers that already hold a normalized
        /// state. It uses the repair path and intentionally does not create an
        /// ordinary user undo entry.
        /// </summary>
        public Result<GraphPatch> CommitNormalized(GraphState normalized, bool deferRedoClear = false)
        {
            var prepared = PrepareNormalized(normalized);
            if (prepared.IsFailure) return prepared;
            var committed = CommitNormalizedRepair(prepared.Value);
            return committed.IsSuccess ? prepared : Result<GraphPatch>.Failure(committed.Diagnostic);
        }

        public Result Undo()
        {
            if (_undo.Count == 0) return Failure("graph.history.empty", "There is nothing to undo.");
            var patch = _undo[_undo.Count - 1];
            var restored = patch.RestoreBefore(_state.Revision + 1);
            _state = restored;
            _undo.RemoveAt(_undo.Count - 1);
            _redo.Add(patch);
            return Result.Success();
        }

        public Result Redo()
        {
            if (_redo.Count == 0) return Failure("graph.history.empty", "There is nothing to redo.");
            var patch = _redo[_redo.Count - 1];
            var restored = patch.RestoreAfter(_state.Revision + 1);
            _state = restored;
            _redo.RemoveAt(_redo.Count - 1);
            _undo.Add(patch);
            if (_undo.Count > GraphConstants.MaxUndoEntries) _undo.RemoveAt(0);
            return Result.Success();
        }

        private static Diagnostic FailureDiagnostic(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message);
        private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));

        private static bool SameState(GraphState left, GraphState right)
        {
            if (left == null || right == null || left.Revision != right.Revision) return false;
            return left.Nodes.SequenceEqual(right.Nodes) && left.Connections.SequenceEqual(right.Connections);
        }
    }

    public enum OutputTargetKind { Program, Preview }

    public sealed class OutputDemand
    {
        public OutputTargetKind TargetKind { get; }
        public NodeInstanceId NodeId { get; }
        public PortId OutputPortId { get; }
        public int Width { get; }
        public int Height { get; }
        public double AspectRatio { get; }
        public bool Focused { get; }
        public long FocusTimestamp { get; }

        public OutputDemand(OutputTargetKind targetKind, NodeInstanceId nodeId, PortId outputPortId, int width, int height, bool focused = false, long focusTimestamp = 0)
            : this(targetKind, nodeId, outputPortId, width, height, (double)width / height, focused, focusTimestamp)
        {
        }

        internal OutputDemand(OutputTargetKind targetKind, NodeInstanceId nodeId, PortId outputPortId, int width, int height, double aspectRatio, bool focused, long focusTimestamp)
        {
            if (nodeId.IsEmpty || outputPortId.IsEmpty) throw new ArgumentException("Demand target IDs are required.");
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            if (double.IsNaN(aspectRatio) || double.IsInfinity(aspectRatio) || aspectRatio <= 0) throw new ArgumentOutOfRangeException(nameof(aspectRatio));
            TargetKind = targetKind;
            NodeId = nodeId;
            OutputPortId = outputPortId;
            Width = width;
            Height = height;
            AspectRatio = aspectRatio;
            Focused = focused;
            FocusTimestamp = focusTimestamp;
        }
    }

    /// <summary>Resolution propagated to one required node output.  The map
    /// is part of the immutable EvaluationPlan so Phase 5 does not have to
    /// guess a terminal resolution while preparing upstream leases.</summary>
    public sealed class OutputResolutionDemand
    {
        public NodeInstanceId NodeId { get; }
        public PortId OutputPortId { get; }
        public int Width { get; }
        public int Height { get; }
        public double AspectRatio { get; }
        public OutputTargetKind TargetKind { get; }
        public bool Focused { get; }
        public long FocusTimestamp { get; }

        public OutputResolutionDemand(NodeInstanceId nodeId, PortId outputPortId, int width, int height, double aspectRatio,
            OutputTargetKind targetKind, bool focused, long focusTimestamp)
        {
            if (nodeId.IsEmpty || outputPortId.IsEmpty || width <= 0 || height <= 0 || double.IsNaN(aspectRatio) || double.IsInfinity(aspectRatio) || aspectRatio <= 0d)
                throw new ArgumentException("Output resolution demand is invalid.");
            NodeId = nodeId; OutputPortId = outputPortId; Width = width; Height = height; AspectRatio = aspectRatio;
            TargetKind = targetKind; Focused = focused; FocusTimestamp = focusTimestamp;
        }
    }

    /// <summary>Persisted/runtime hand-off for a Feedback frame commit.</summary>
    public sealed class FeedbackCommitTarget
    {
        public NodeInstanceId FeedbackNodeId { get; }
        public NodeInstanceId InputNodeId { get; }
        public PortId InputPortId { get; }
        public PortId FeedbackInputPortId { get; }

        public FeedbackCommitTarget(NodeInstanceId feedbackNodeId, NodeInstanceId inputNodeId, PortId inputPortId, PortId feedbackInputPortId)
        {
            if (feedbackNodeId.IsEmpty || inputNodeId.IsEmpty || inputPortId.IsEmpty || feedbackInputPortId.IsEmpty) throw new ArgumentException("Feedback commit target IDs are required.");
            FeedbackNodeId = feedbackNodeId;
            InputNodeId = inputNodeId;
            InputPortId = inputPortId;
            FeedbackInputPortId = feedbackInputPortId;
        }
    }

    public sealed class EvaluationPlan
    {
        public long SourceRevision { get; }
        public NodeInstanceId ProgramOutputNodeId { get; }
        public IReadOnlyList<NodeInstanceId> PreviewOutputNodeIds { get; }
        public IReadOnlyList<NodeInstanceId> RequiredNodeIds { get; }
        public IReadOnlyList<NodeInstanceId> EvaluationOrder { get; }
        /// <summary>Stable execution position for every required node.
        /// FrameCoordinator uses this map instead of linearly searching
        /// RequiredNodeIds for every node on every frame.</summary>
        public IReadOnlyDictionary<NodeInstanceId, int> EvaluationIndices { get; }
        public IReadOnlyList<NodeInstanceId> FeedbackCommitNodeIds { get; }
        public IReadOnlyList<FeedbackCommitTarget> FeedbackCommitTargets { get; }
        public IReadOnlyDictionary<NodeInstanceId, IReadOnlyList<PortId>> RequestedOutputs { get; }
        public IReadOnlyDictionary<NodeInstanceId, IReadOnlyDictionary<PortId, OutputResolutionDemand>> RequiredOutputResolutions { get; }
        public IReadOnlyList<OutputDemand> MergedDemands { get; }
        public double ProgramAspectRatio { get; }

        private EvaluationPlan(long sourceRevision, NodeInstanceId program, IEnumerable<NodeInstanceId> previews, IEnumerable<NodeInstanceId> required, IEnumerable<NodeInstanceId> order, IEnumerable<NodeInstanceId> feedback, IEnumerable<FeedbackCommitTarget> feedbackTargets, IDictionary<NodeInstanceId, IReadOnlyList<PortId>> requested, IDictionary<NodeInstanceId, IReadOnlyDictionary<PortId, OutputResolutionDemand>> requiredResolutions, IEnumerable<OutputDemand> mergedDemands, double programAspectRatio)
        {
            SourceRevision = sourceRevision;
            ProgramOutputNodeId = program;
            PreviewOutputNodeIds = new ReadOnlyCollection<NodeInstanceId>((previews ?? Enumerable.Empty<NodeInstanceId>()).ToList());
            RequiredNodeIds = new ReadOnlyCollection<NodeInstanceId>((required ?? Enumerable.Empty<NodeInstanceId>()).ToList());
            EvaluationOrder = new ReadOnlyCollection<NodeInstanceId>((order ?? Enumerable.Empty<NodeInstanceId>()).ToList());
            var evaluationIndices = new Dictionary<NodeInstanceId, int>();
            for (var index = 0; index < RequiredNodeIds.Count; index++)
                evaluationIndices[RequiredNodeIds[index]] = index;
            EvaluationIndices = new ReadOnlyDictionary<NodeInstanceId, int>(evaluationIndices);
            FeedbackCommitNodeIds = new ReadOnlyCollection<NodeInstanceId>((feedback ?? Enumerable.Empty<NodeInstanceId>()).ToList());
            FeedbackCommitTargets = new ReadOnlyCollection<FeedbackCommitTarget>((feedbackTargets ?? Enumerable.Empty<FeedbackCommitTarget>()).ToList());
            RequestedOutputs = new ReadOnlyDictionary<NodeInstanceId, IReadOnlyList<PortId>>(new Dictionary<NodeInstanceId, IReadOnlyList<PortId>>(requested ?? new Dictionary<NodeInstanceId, IReadOnlyList<PortId>>()));
            RequiredOutputResolutions = new ReadOnlyDictionary<NodeInstanceId, IReadOnlyDictionary<PortId, OutputResolutionDemand>>(
                (requiredResolutions ?? new Dictionary<NodeInstanceId, IReadOnlyDictionary<PortId, OutputResolutionDemand>>()).ToDictionary(
                    pair => pair.Key,
                    pair => (IReadOnlyDictionary<PortId, OutputResolutionDemand>)new ReadOnlyDictionary<PortId, OutputResolutionDemand>(new Dictionary<PortId, OutputResolutionDemand>(pair.Value ?? new Dictionary<PortId, OutputResolutionDemand>()))));
            MergedDemands = new ReadOnlyCollection<OutputDemand>((mergedDemands ?? Enumerable.Empty<OutputDemand>()).ToList());
            ProgramAspectRatio = programAspectRatio;
        }

        public bool TryGetEvaluationIndex(NodeInstanceId nodeId, out int index)
            => EvaluationIndices.TryGetValue(nodeId, out index);

        public EvaluationPlan WithSourceRevision(long sourceRevision)
        {
            return new EvaluationPlan(sourceRevision, ProgramOutputNodeId, PreviewOutputNodeIds, RequiredNodeIds, EvaluationOrder,
                FeedbackCommitNodeIds, FeedbackCommitTargets, RequestedOutputs.ToDictionary(x => x.Key, x => x.Value), RequiredOutputResolutions.ToDictionary(x => x.Key, x => x.Value), MergedDemands, ProgramAspectRatio);
        }

        public static bool TryBuild(GraphState state, NodeTypeRegistry registry, IEnumerable<OutputDemand> demands, out EvaluationPlan plan, out Diagnostic diagnostic)
        {
            return TryBuild(state, registry, demands, out plan, out diagnostic, out _);
        }

        /// <summary>
        /// Builds an immutable plan from a private graph copy.  The normalized
        /// copy is returned so the owning transaction can persist Broken edge
        /// classification deliberately; callers that only need a plan can use
        /// the five-argument overload and are guaranteed no input mutation.
        /// </summary>
        public static bool TryBuild(GraphState state, NodeTypeRegistry registry, IEnumerable<OutputDemand> demands, out EvaluationPlan plan, out Diagnostic diagnostic, out GraphState normalizedState)
        {
            plan = null;
            diagnostic = null;
            normalizedState = state?.Clone();
            if (state == null || registry == null)
            {
                diagnostic = FailureDiagnostic("graph.plan.invalid", "Graph state and registry are required.");
                return false;
            }
            var working = state.Clone();
            normalizedState = working;
            if (working.HasDuplicateIds())
            {
                diagnostic = FailureDiagnostic("graph.plan.duplicate_id", "Graph state contains duplicate stable IDs.");
                return false;
            }
            var allPrograms = working.Nodes.Where(x => x.TypeId.Value == GraphConstants.ProgramOutputTypeId).ToList();
            var nodes = working.Nodes.Where(x => x.Enabled && !x.IsUnknown).ToList();
            var programs = nodes.Where(x => x.TypeId.Value == GraphConstants.ProgramOutputTypeId).ToList();
            if (allPrograms.Count != 1 || programs.Count != 1)
            {
                diagnostic = FailureDiagnostic("graph.plan.program_invalid", "Exactly one ProgramOutput node is required.");
                return false;
            }
            var program = programs[0];
            if (!HasProgramShape(program))
            {
                diagnostic = FailureDiagnostic("graph.plan.program_shape_invalid", "ProgramOutput must be the fixed system-owned Image input node.");
                return false;
            }
            foreach (var preview in nodes.Where(x => x.TypeId.Value == GraphConstants.PreviewTypeId))
            {
                if (!HasPreviewShape(preview))
                {
                    diagnostic = FailureDiagnostic("graph.plan.preview_shape_invalid", "Preview must use the fixed Image input shape.");
                    return false;
                }
            }
            var demandList = (demands ?? Enumerable.Empty<OutputDemand>()).ToList();
            if (demandList.Count == 0) demandList.Add(new OutputDemand(OutputTargetKind.Program, program.Id, new PortId(GraphConstants.ImagePortId), 1920, 1080));
            var programDemands = demandList.Where(x => x.TargetKind == OutputTargetKind.Program).ToList();
            if (programDemands.Count == 0) programDemands.Add(new OutputDemand(OutputTargetKind.Program, program.Id, new PortId(GraphConstants.ImagePortId), 1920, 1080));
            if (programDemands.Any(x => x.NodeId != program.Id || x.OutputPortId.Value != GraphConstants.ImagePortId))
            {
                diagnostic = FailureDiagnostic("graph.plan.program_target_invalid", "Program demand must target the fixed ProgramOutput node.");
                return false;
            }
            var previews = demandList.Where(x => x.TargetKind == OutputTargetKind.Preview).OrderByDescending(x => x.Focused).ThenByDescending(x => x.FocusTimestamp).ThenBy(x => x.NodeId.Value, StringComparer.Ordinal).ToList();
            var previewIds = previews.Select(x => x.NodeId).Distinct().ToList();
            if (previewIds.Count > 8)
            {
                diagnostic = FailureDiagnostic("graph.plan.preview_limit", "At most eight Preview outputs may be demanded at once.");
                return false;
            }
            var nodeMap = nodes.ToDictionary(x => x.Id, x => x);
            foreach (var demand in previews)
            {
                if (demand.OutputPortId.Value != GraphConstants.ImagePortId
                    || !nodeMap.TryGetValue(demand.NodeId, out var previewNode)
                    || previewNode.TypeId.Value != GraphConstants.PreviewTypeId)
                {
                    diagnostic = FailureDiagnostic("graph.plan.preview_target_invalid", "Preview demand must target an enabled Preview node.");
                    return false;
                }
            }
            var mergedDemands = MergeDemands(programDemands.Concat(previews));
            var programDemand = mergedDemands.First(x => x.TargetKind == OutputTargetKind.Program);
            NormalizeInvalidActiveEdges(working, registry);
            normalizedState = working;
            var validEdges = working.Connections.Where(x => !x.IsBroken && nodeMap.ContainsKey(x.SourceNodeId) && nodeMap.ContainsKey(x.DestinationNodeId)).ToList();
            if (validEdges.GroupBy(x => Tuple.Create(x.DestinationNodeId, x.DestinationPortId)).Any(x => x.Count() > 1))
            {
                diagnostic = FailureDiagnostic("graph.plan.input_multiple", "An input port has more than one active connection.");
                return false;
            }
            foreach (var edge in validEdges)
            {
                var source = nodeMap[edge.SourceNodeId];
                var destination = nodeMap[edge.DestinationNodeId];
                if (source.FindPort(edge.SourcePortId)?.Direction != PortDirection.Output || destination.FindPort(edge.DestinationPortId)?.Direction != PortDirection.Input)
                {
                    diagnostic = FailureDiagnostic("graph.plan.edge_invalid", "An active edge has an invalid endpoint.");
                    return false;
                }
                var sourcePort = source.FindPort(edge.SourcePortId);
                var destinationPort = destination.FindPort(edge.DestinationPortId);
                if (!registry.Conversions.IsCompatibleSaved(sourcePort.Type, destinationPort.Type, edge.ConversionId))
                {
                    // This branch should be unreachable after normalization;
                    // retaining the explicit guard makes the invariant clear
                    // if a future catalog changes while a plan is built.
                    diagnostic = FailureDiagnostic("graph.plan.conversion_invalid", "An active edge has a missing or incompatible conversion.");
                    return false;
                }
            }
            if (GraphBatchWorkspace.HasCycle(validEdges, nodes))
            {
                diagnostic = FailureDiagnostic("graph.plan.cycle", "The evaluation graph contains a cycle outside Feedback.");
                return false;
            }
            var required = new HashSet<NodeInstanceId>();
            var requested = new Dictionary<NodeInstanceId, HashSet<PortId>>();
            void AddDemand(OutputDemand demand)
            {
                if (!nodeMap.ContainsKey(demand.NodeId)) return;
                if (!requested.TryGetValue(demand.NodeId, out var outputs)) requested[demand.NodeId] = outputs = new HashSet<PortId>();
                outputs.Add(demand.OutputPortId);
                var queue = new Queue<NodeInstanceId>();
                queue.Enqueue(demand.NodeId);
                while (queue.Count > 0)
                {
                    var current = queue.Dequeue();
                    if (!required.Add(current)) continue;
                    foreach (var edge in validEdges.Where(x => x.DestinationNodeId == current))
                    {
                        if (!requested.TryGetValue(edge.SourceNodeId, out var sourceOutputs)) requested[edge.SourceNodeId] = sourceOutputs = new HashSet<PortId>();
                        sourceOutputs.Add(edge.SourcePortId);
                        queue.Enqueue(edge.SourceNodeId);
                    }
                }
            }
            AddDemand(programDemand);
            foreach (var demand in mergedDemands.Where(x => x.TargetKind == OutputTargetKind.Preview)) AddDemand(demand);
            if (!required.Contains(program.Id)) required.Add(program.Id);
            var feedbackCommit = required.Where(id => nodeMap[id].TypeId.Value == GraphConstants.FeedbackTypeId).OrderBy(x => x.Value, StringComparer.Ordinal).ToList();
            var feedbackTargets = feedbackCommit
                .SelectMany(feedbackId => validEdges
                    .Where(x => x.DestinationNodeId == feedbackId)
                    .OrderBy(x => x.DestinationPortId.Value, StringComparer.Ordinal)
                    .Take(1)
                    .Select(x => new FeedbackCommitTarget(feedbackId, x.SourceNodeId, x.SourcePortId, x.DestinationPortId)))
                .ToList();

            // Feedback input crosses the temporal boundary and is committed
            // after evaluation. Feedback output is the previous-frame image,
            // so it must remain in the same-frame order before its consumers.
            var requiredEdges = validEdges.Where(x => required.Contains(x.SourceNodeId) && required.Contains(x.DestinationNodeId)
                && nodeMap[x.DestinationNodeId].TypeId.Value != GraphConstants.FeedbackTypeId).ToList();
            var indegree = required.ToDictionary(x => x, _ => 0);
            foreach (var edge in requiredEdges) indegree[edge.DestinationNodeId]++;
            var programBranch = CollectReverse(program.Id, validEdges);
            var previewBranches = previews.Select(x => new { Demand = x, Nodes = CollectReverse(x.NodeId, validEdges) }).ToList();
            int Rank(NodeInstanceId id)
            {
                if (programBranch.Contains(id)) return 0;
                var preview = previewBranches.FirstOrDefault(x => x.Nodes.Contains(id));
                if (preview == null) return 3;
                return preview.Demand.Focused ? 1 : 2;
            }
            var ready = new List<NodeInstanceId>(indegree.Where(x => x.Value == 0).Select(x => x.Key));
            var order = new List<NodeInstanceId>();
            while (ready.Count > 0)
            {
                ready.Sort((left, right) => Rank(left) != Rank(right) ? Rank(left).CompareTo(Rank(right)) : string.CompareOrdinal(left.Value, right.Value));
                var current = ready[0];
                ready.RemoveAt(0);
                order.Add(current);
                foreach (var edge in requiredEdges.Where(x => x.SourceNodeId == current))
                {
                    indegree[edge.DestinationNodeId]--;
                    if (indegree[edge.DestinationNodeId] == 0) ready.Add(edge.DestinationNodeId);
                }
            }
            if (order.Count != required.Count)
            {
                diagnostic = FailureDiagnostic("graph.plan.topology_failed", "Could not construct a stable DAG evaluation order.");
                return false;
            }
            var outputMap = requested.ToDictionary(x => x.Key, x => (IReadOnlyList<PortId>)new ReadOnlyCollection<PortId>(x.Value.OrderBy(y => y.Value, StringComparer.Ordinal).ToList()));
            var resolutionMap = BuildResolutionMap(mergedDemands, validEdges);
            plan = new EvaluationPlan(working.Revision, program.Id, previewIds, required.OrderBy(x => x.Value, StringComparer.Ordinal), order, feedbackCommit, feedbackTargets, outputMap, resolutionMap, mergedDemands, programDemand.AspectRatio);
            return true;
        }

        private static Dictionary<NodeInstanceId, IReadOnlyDictionary<PortId, OutputResolutionDemand>> BuildResolutionMap(IEnumerable<OutputDemand> terminalDemands, IReadOnlyList<ConnectionRecord> edges)
        {
            var map = new Dictionary<NodeInstanceId, Dictionary<PortId, OutputResolutionDemand>>();
            var visited = new HashSet<Tuple<NodeInstanceId, PortId, NodeInstanceId, PortId>>();
            var queue = new Queue<ResolutionWork>((terminalDemands ?? Enumerable.Empty<OutputDemand>()).Select(x => new ResolutionWork(x, x.NodeId, x.OutputPortId)));
            while (queue.Count > 0)
            {
                var work = queue.Dequeue();
                var demand = work.Demand;
                var visitKey = Tuple.Create(work.OriginNodeId, work.OriginPortId, demand.NodeId, demand.OutputPortId);
                if (!visited.Add(visitKey)) continue;
                if (!map.TryGetValue(demand.NodeId, out var outputs)) map[demand.NodeId] = outputs = new Dictionary<PortId, OutputResolutionDemand>();
                var propagated = new OutputResolutionDemand(demand.NodeId, demand.OutputPortId, demand.Width, demand.Height, demand.AspectRatio, demand.TargetKind, demand.Focused, demand.FocusTimestamp);
                outputs[demand.OutputPortId] = outputs.TryGetValue(demand.OutputPortId, out var current) ? MergeResolution(current, propagated) : propagated;
                foreach (var edge in edges.Where(x => x.DestinationNodeId == demand.NodeId))
                {
                    queue.Enqueue(new ResolutionWork(new OutputDemand(demand.TargetKind, edge.SourceNodeId, edge.SourcePortId, demand.Width, demand.Height, demand.Focused, demand.FocusTimestamp), work.OriginNodeId, work.OriginPortId));
                }
            }
            return map.OrderBy(pair => pair.Key.Value, StringComparer.Ordinal).ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<PortId, OutputResolutionDemand>)new ReadOnlyDictionary<PortId, OutputResolutionDemand>(
                    pair.Value.OrderBy(port => port.Key.Value, StringComparer.Ordinal).ToDictionary(port => port.Key, port => port.Value)));
        }

        private readonly struct ResolutionWork
        {
            public readonly OutputDemand Demand;
            public readonly NodeInstanceId OriginNodeId;
            public readonly PortId OriginPortId;
            public ResolutionWork(OutputDemand demand, NodeInstanceId originNodeId, PortId originPortId) { Demand = demand; OriginNodeId = originNodeId; OriginPortId = originPortId; }
        }

        private static OutputResolutionDemand MergeResolution(OutputResolutionDemand left, OutputResolutionDemand right)
        {
            var preferred = IsPreferred(right, left) ? right : left;
            var width = Math.Max(left.Width, right.Width);
            var height = Math.Max(left.Height, right.Height);
            width = Math.Max(width, (int)Math.Ceiling(height * preferred.AspectRatio));
            height = Math.Max(height, (int)Math.Ceiling(width / preferred.AspectRatio));
            return new OutputResolutionDemand(left.NodeId, left.OutputPortId, width, height, preferred.AspectRatio, preferred.TargetKind, preferred.Focused, preferred.FocusTimestamp);
        }

        private static int ResolutionPriority(OutputResolutionDemand demand)
        {
            if (demand.TargetKind == OutputTargetKind.Program) return 0;
            return demand.Focused ? 1 : 2;
        }

        private static bool IsPreferred(OutputResolutionDemand candidate, OutputResolutionDemand current)
        {
            var candidatePriority = ResolutionPriority(candidate);
            var currentPriority = ResolutionPriority(current);
            if (candidatePriority != currentPriority) return candidatePriority < currentPriority;
            if (candidate.FocusTimestamp != current.FocusTimestamp) return candidate.FocusTimestamp > current.FocusTimestamp;
            if (candidate.Focused != current.Focused) return candidate.Focused;
            var aspect = candidate.AspectRatio.CompareTo(current.AspectRatio);
            if (aspect != 0) return aspect < 0;
            if (candidate.Width != current.Width) return candidate.Width < current.Width;
            if (candidate.Height != current.Height) return candidate.Height < current.Height;
            return StringComparer.Ordinal.Compare(candidate.OutputPortId.Value, current.OutputPortId.Value) < 0;
        }

        private static HashSet<NodeInstanceId> CollectReverse(NodeInstanceId root, IReadOnlyList<ConnectionRecord> edges)
        {
            var result = new HashSet<NodeInstanceId>();
            var queue = new Queue<NodeInstanceId>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                var current = queue.Dequeue();
                if (!result.Add(current)) continue;
                foreach (var edge in edges.Where(x => x.DestinationNodeId == current)) queue.Enqueue(edge.SourceNodeId);
            }
            return result;
        }

        private static bool HasProgramShape(NodeRecord node)
        {
            if (node == null || !node.Enabled || !node.SystemOwned || node.UserAddable) return false;
            if (node.Ports.Count != 1) return false;
            var port = node.Ports[0];
            return port.Id.Value == GraphConstants.ImagePortId
                && port.Direction == PortDirection.Input
                && port.Type == PortType.ImageFrame
                && port.Required;
        }

        private static bool HasPreviewShape(NodeRecord node)
        {
            if (node == null || node.Ports.Count != 1) return false;
            var port = node.Ports[0];
            return port.Id.Value == GraphConstants.ImagePortId
                && port.Direction == PortDirection.Input
                && port.Type == PortType.ImageFrame
                && port.Required;
        }

        private static void NormalizeInvalidActiveEdges(GraphState state, NodeTypeRegistry registry)
        {
            var allNodes = state.Nodes.ToDictionary(x => x.Id, x => x);
            foreach (var edge in state.Connections.Where(x => !x.IsBroken).ToList())
            {
                if (!allNodes.TryGetValue(edge.SourceNodeId, out var sourceNode)
                    || !allNodes.TryGetValue(edge.DestinationNodeId, out var destinationNode))
                {
                    state.ReplaceConnection(edge.AsBroken("Connection endpoint node is missing."));
                    continue;
                }
                var source = sourceNode.FindPort(edge.SourcePortId);
                var destination = destinationNode.FindPort(edge.DestinationPortId);
                if (source == null || destination == null || source.Direction != PortDirection.Output || destination.Direction != PortDirection.Input)
                {
                    state.ReplaceConnection(edge.AsBroken("Connection endpoint port is invalid."));
                    continue;
                }
                if (!registry.Conversions.IsCompatibleSaved(source.Type, destination.Type, edge.ConversionId))
                {
                    state.ReplaceConnection(edge.AsBroken("Saved conversion is missing or is not registered for the endpoint types."));
                }
            }
        }

        private static List<OutputDemand> MergeDemands(IEnumerable<OutputDemand> demands)
        {
            var merged = new Dictionary<Tuple<OutputTargetKind, NodeInstanceId, PortId>, OutputDemand>();
            foreach (var demand in demands ?? Enumerable.Empty<OutputDemand>())
            {
                var key = Tuple.Create(demand.TargetKind, demand.NodeId, demand.OutputPortId);
                if (!merged.TryGetValue(key, out var current))
                {
                    merged[key] = demand;
                    continue;
                }
                var preferred = IsPreferred(demand, current) ? demand : current;
                var aspect = preferred.AspectRatio;
                var width = Math.Max(current.Width, demand.Width);
                var height = Math.Max(current.Height, demand.Height);
                width = Math.Max(width, (int)Math.Ceiling(height * aspect));
                height = Math.Max(height, (int)Math.Ceiling(width / aspect));
                merged[key] = new OutputDemand(preferred.TargetKind, preferred.NodeId, preferred.OutputPortId, width, height, aspect, preferred.Focused, preferred.FocusTimestamp);
            }
            return merged.Values
                .OrderBy(x => x.TargetKind == OutputTargetKind.Program ? 0 : x.Focused ? 1 : 2)
                .ThenBy(x => x.NodeId.Value, StringComparer.Ordinal)
                .ThenBy(x => x.OutputPortId.Value, StringComparer.Ordinal)
                .ToList();
        }

        private static int DemandPriority(OutputDemand demand)
        {
            if (demand.TargetKind == OutputTargetKind.Program) return 0;
            return demand.Focused ? 1 : 2;
        }

        private static bool IsPreferred(OutputDemand candidate, OutputDemand current)
        {
            var candidatePriority = DemandPriority(candidate);
            var currentPriority = DemandPriority(current);
            if (candidatePriority != currentPriority) return candidatePriority < currentPriority;
            if (candidate.FocusTimestamp != current.FocusTimestamp) return candidate.FocusTimestamp > current.FocusTimestamp;
            if (candidate.Focused != current.Focused) return candidate.Focused;
            var aspect = candidate.AspectRatio.CompareTo(current.AspectRatio);
            if (aspect != 0) return aspect < 0;
            if (candidate.Width != current.Width) return candidate.Width < current.Width;
            if (candidate.Height != current.Height) return candidate.Height < current.Height;
            return StringComparer.Ordinal.Compare(candidate.OutputPortId.Value, current.OutputPortId.Value) < 0;
        }

        private static Diagnostic FailureDiagnostic(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message);
    }
}
