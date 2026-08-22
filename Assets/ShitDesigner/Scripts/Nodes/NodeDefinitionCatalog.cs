using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Runtime;

namespace ShitDesigner.Nodes
{
    public enum NodePortDirection { Input, Output }
    public enum NodePortType { ImageFrame, Float, Int, Bool, Vector2, Vector3, Vector4, Color }

    public sealed class NodePortDefinition
    {
        public PortId Id { get; }
        public string DisplayName { get; }
        public NodePortDirection Direction { get; }
        public NodePortType Type { get; }
        public bool Required { get; }
        public RuntimeDefaultImageKind? DefaultImage { get; }
        public NodePortDefinition(PortId id, string displayName, NodePortDirection direction, NodePortType type, bool required, RuntimeDefaultImageKind? defaultImage = null)
        {
            if (id.IsEmpty || string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Node port identity is required.");
            if (defaultImage.HasValue && (direction != NodePortDirection.Input || required || type != NodePortType.ImageFrame)) throw new ArgumentException("Default image requires an optional ImageFrame input.");
            Id = id; DisplayName = displayName.Trim(); Direction = direction; Type = type; Required = required; DefaultImage = defaultImage;
        }
    }

    public sealed class NodeParameterDefinition
    {
        public ParameterId Id { get; }
        public string DisplayName { get; }
        public ParameterType Type { get; }
        public ParameterValue DefaultValue { get; }
        public ParameterValue? Minimum { get; }
        public ParameterValue? Maximum { get; }
        public bool RuntimeStateful { get; }
        public IReadOnlyList<string> EnumOptions { get; }
        // These fields are presentation metadata, but they live on the neutral
        // catalog descriptor so the composition root can carry them into the
        // persisted Project definition without making Nodes depend on Project.
        public string Group { get; }
        public int DisplayOrder { get; }
        public string Description { get; }
        public string Unit { get; }
        public double Step { get; }
        public bool IsReadOnly { get; }
        public bool IsHidden { get; }
        public NodeParameterDefinition(ParameterId id, string displayName, ParameterType type, ParameterValue defaultValue, ParameterValue? minimum = null, ParameterValue? maximum = null, bool runtimeStateful = false, IEnumerable<string> enumOptions = null,
            string group = null, int displayOrder = 0, string description = null, string unit = null, double step = 0d, bool isReadOnly = false, bool isHidden = false)
        {
            if (id.IsEmpty || string.IsNullOrWhiteSpace(displayName) || defaultValue.Type != type) throw new ArgumentException("Node parameter definition is invalid.");
            if (minimum.HasValue && minimum.Value.Type != type || maximum.HasValue && maximum.Value.Type != type) throw new ArgumentException("Node parameter range type is invalid.");
            if (runtimeStateful && id.Value != "transport.playhead_seconds") throw new ArgumentException("Only the video playhead is runtime-stateful.");
            if (double.IsNaN(step) || double.IsInfinity(step) || step < 0d) throw new ArgumentOutOfRangeException(nameof(step));
            Id = id; DisplayName = displayName.Trim(); Type = type; DefaultValue = defaultValue; Minimum = minimum; Maximum = maximum; RuntimeStateful = runtimeStateful;
            Group = group ?? string.Empty; DisplayOrder = displayOrder; Description = description ?? string.Empty; Unit = unit ?? string.Empty; Step = step; IsReadOnly = isReadOnly; IsHidden = isHidden;
            EnumOptions = new ReadOnlyCollection<string>((enumOptions ?? Enumerable.Empty<string>()).Select(x => x ?? string.Empty).ToList());
        }
    }

    public sealed class NodeDefinition
    {
        private readonly IReadOnlyList<NodePortDefinition> _ports;
        private readonly IReadOnlyList<NodeParameterDefinition> _parameters;
        public NodeTypeId TypeId { get; }
        public int SchemaVersion { get; }
        public string DisplayName { get; }
        public string Category { get; }
        public bool SystemOwned { get; }
        public bool UserAddable { get; }
        public IReadOnlyList<NodePortDefinition> Ports => _ports;
        public IReadOnlyList<NodeParameterDefinition> Parameters => _parameters;
        public NodeDefinition(NodeTypeId typeId, int schemaVersion, string displayName, string category, IEnumerable<NodePortDefinition> ports, IEnumerable<NodeParameterDefinition> parameters = null, bool systemOwned = false, bool userAddable = true)
        {
            if (typeId.IsEmpty || schemaVersion < 1 || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(category)) throw new ArgumentException("Node definition metadata is invalid.");
            var portList = (ports ?? Enumerable.Empty<NodePortDefinition>()).ToList();
            var parameterList = (parameters ?? Enumerable.Empty<NodeParameterDefinition>()).ToList();
            if (portList.Any(x => x == null) || parameterList.Any(x => x == null) || portList.GroupBy(x => x.Id).Any(x => x.Count() > 1) || parameterList.GroupBy(x => x.Id).Any(x => x.Count() > 1)) throw new ArgumentException("Node port and parameter IDs must be unique.");
            if (portList.Any(x => x.Direction == NodePortDirection.Output && x.Id.Value == "image" && (x.Type != NodePortType.ImageFrame || x.DisplayName != "Image"))) throw new ArgumentException("The primary Image output must be an ImageFrame named Image.");
            TypeId = typeId; SchemaVersion = schemaVersion; DisplayName = displayName.Trim(); Category = category.Trim(); SystemOwned = systemOwned; UserAddable = userAddable;
            _ports = new ReadOnlyCollection<NodePortDefinition>(portList); _parameters = new ReadOnlyCollection<NodeParameterDefinition>(parameterList);
        }
        public NodePortDefinition FindPort(PortId id) => _ports.FirstOrDefault(x => x.Id == id);
        public NodeParameterDefinition FindParameter(ParameterId id) => _parameters.FirstOrDefault(x => x.Id == id);
    }

    public interface INodeTypeDefinition
    {
        NodeTypeId TypeId { get; }
        int SchemaVersion { get; }
        string DisplayName { get; }
        string Category { get; }
        IReadOnlyList<NodePortDefinition> Ports { get; }
        IReadOnlyList<NodeParameterDefinition> Parameters { get; }
        bool SystemOwned { get; }
        bool UserAddable { get; }
        INodeFactory Factory { get; }
    }

    public interface INodeFactory : IRuntimeNodeFactory { }

    public sealed class ShaderNodeBinding
    {
        public string ShaderKey { get; }
        public IReadOnlyDictionary<PortId, string> InputProperties { get; }
        public IReadOnlyDictionary<ParameterId, string> ParameterProperties { get; }
        public int OutputPass { get; }
        public ShaderNodeBinding(string shaderKey, IDictionary<PortId, string> inputProperties = null, IDictionary<ParameterId, string> parameterProperties = null, int outputPass = 0)
        {
            if (string.IsNullOrWhiteSpace(shaderKey) || outputPass < 0) throw new ArgumentException("Shader binding metadata is invalid.");
            ShaderKey = shaderKey.Trim(); InputProperties = new ReadOnlyDictionary<PortId, string>(new Dictionary<PortId, string>(inputProperties ?? new Dictionary<PortId, string>())); ParameterProperties = new ReadOnlyDictionary<ParameterId, string>(new Dictionary<ParameterId, string>(parameterProperties ?? new Dictionary<ParameterId, string>())); OutputPass = outputPass;
        }
    }

    public sealed class SceneNodeBinding
    {
        public string PrefabKey { get; }
        public bool RequiresExactlyOneCamera { get; }
        public bool RequiresCanvasValidation { get; }
        public SceneNodeBinding(string prefabKey, bool requiresExactlyOneCamera = true, bool requiresCanvasValidation = false)
        {
            if (string.IsNullOrWhiteSpace(prefabKey)) throw new ArgumentException("Prefab key is required.");
            PrefabKey = prefabKey.Trim(); RequiresExactlyOneCamera = requiresExactlyOneCamera; RequiresCanvasValidation = requiresCanvasValidation;
        }
    }

    public sealed class NodeCatalogEntry : INodeTypeDefinition
    {
        public NodeDefinition Definition { get; }
        public INodeFactory Factory { get; }
        public ShaderNodeBinding ShaderBinding { get; }
        public SceneNodeBinding SceneBinding { get; }
        public NodeTypeId TypeId => Definition.TypeId;
        public int SchemaVersion => Definition.SchemaVersion;
        public string DisplayName => Definition.DisplayName;
        public string Category => Definition.Category;
        public IReadOnlyList<NodePortDefinition> Ports => Definition.Ports;
        public IReadOnlyList<NodeParameterDefinition> Parameters => Definition.Parameters;
        public bool SystemOwned => Definition.SystemOwned;
        public bool UserAddable => Definition.UserAddable;
        public NodeCatalogEntry(NodeDefinition definition, INodeFactory factory, ShaderNodeBinding shaderBinding = null, SceneNodeBinding sceneBinding = null)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition)); Factory = factory ?? throw new ArgumentNullException(nameof(factory));
            if (factory.TypeId != definition.TypeId) throw new ArgumentException("Factory and definition TypeId must match.");
            ShaderBinding = shaderBinding; SceneBinding = sceneBinding;
        }
    }

    public sealed class NodeDefinitionCatalog
    {
        public static readonly IReadOnlyList<string> SpecializedNodeTypeIds = new ReadOnlyCollection<string>(new[] { "shitdesigner.scene.3d", "shitdesigner.scene.2d", "shitdesigner.shader.generator", "shitdesigner.shader.effect", "shitdesigner.shader.blend2", "shitdesigner.video.player", "system.feedback" });
        private readonly IReadOnlyList<NodeCatalogEntry> _entries;
        public IReadOnlyList<NodeCatalogEntry> Entries => _entries;
        public NodeDefinitionCatalog(IEnumerable<NodeCatalogEntry> entries) { _entries = new ReadOnlyCollection<NodeCatalogEntry>((entries ?? Enumerable.Empty<NodeCatalogEntry>()).ToList()); }

        public Result Validate()
        {
            if (_entries.Count == 0) return Failure("nodes.catalog.empty", "Node catalog contains no definitions.");
            if (_entries.Any(x => x == null) || _entries.GroupBy(x => x.TypeId).Any(x => x.Count() > 1)) return Failure("nodes.catalog.duplicate_type", "Node type IDs must be globally unique.");
            foreach (var entry in _entries)
            {
                if (entry.SchemaVersion != 1 || entry.Factory == null || entry.Factory.TypeId != entry.TypeId) return Failure("nodes.catalog.metadata", "Node definition schema or factory metadata is invalid.");
                if (entry.Ports.GroupBy(x => x.Id).Any(x => x.Count() > 1) || entry.Parameters.GroupBy(x => x.Id).Any(x => x.Count() > 1)) return Failure("nodes.catalog.duplicate_member", "Port and parameter IDs must be unique.");
                if (entry.Category.StartsWith("Shader/", StringComparison.Ordinal) && entry.ShaderBinding == null) return Failure("nodes.catalog.shader_binding_missing", "Shader definitions require explicit bindings.");
                if ((entry.Category == "3D" || entry.Category == "2D") && (entry.SceneBinding == null || !entry.SceneBinding.RequiresExactlyOneCamera)) return Failure("nodes.catalog.scene_binding", "Scene definitions require one-camera prefab bindings.");
                if (entry.ShaderBinding != null)
                {
                    foreach (var binding in entry.ShaderBinding.InputProperties)
                    {
                        var port = entry.Definition.FindPort(binding.Key);
                        if (port == null || port.Direction != NodePortDirection.Input || port.Type != NodePortType.ImageFrame) return Failure("nodes.catalog.shader_input_binding", "Shader binding names an unknown image input.");
                    }
                    foreach (var binding in entry.ShaderBinding.ParameterProperties) if (entry.Definition.FindParameter(binding.Key) == null) return Failure("nodes.catalog.shader_parameter_binding", "Shader binding names an unknown parameter.");
                }
            }
            var program = _entries.SingleOrDefault(x => x.TypeId.Value == "system.program_output");
            if (program == null || !program.SystemOwned || program.UserAddable || program.Ports.Count != 1 || program.Ports[0].Id.Value != "image" || program.Ports[0].Direction != NodePortDirection.Input || program.Ports[0].Type != NodePortType.ImageFrame || !program.Ports[0].Required) return Failure("nodes.catalog.program_shape", "ProgramOutput must be fixed and system-owned.");
            var preview = _entries.SingleOrDefault(x => x.TypeId.Value == "system.preview");
            if (preview == null || preview.SystemOwned || !preview.UserAddable || preview.Ports.Count != 1 || preview.Ports[0].Id.Value != "image" || preview.Ports[0].Direction != NodePortDirection.Input || preview.Ports[0].Type != NodePortType.ImageFrame || !preview.Ports[0].Required) return Failure("nodes.catalog.preview_shape", "Preview must be a user-addable required Image input.");
            return Result.Success();
        }

        /// <summary>Registers only the Runtime factory half. Bootstrap adapts
        /// the immutable descriptors to Graph's persistence registry.</summary>
        public Result RegisterFactories(RuntimeSession runtimeSession)
        {
            if (runtimeSession == null) return Failure("nodes.catalog.runtime_missing", "Runtime session is required.");
            var valid = Validate(); if (valid.IsFailure) return valid;
            if (_entries.Any(entry => SpecializedNodeTypeIds.Contains(entry.TypeId.Value, StringComparer.Ordinal) && entry.Factory is CatalogNodeFactory missingFactory && missingFactory.IsPlaceholder)) return Failure("nodes.catalog.binding_missing", "A production visual node factory was not injected.");
            foreach (var entry in _entries)
            {
                var result = runtimeSession.RegisterFactory(entry.Factory); if (result.IsFailure) return result;
            }
            return Result.Success();
        }

        public Result ValidateProductionBindings(NodeFactoryBindings bindings)
        {
            if (bindings == null) return Failure("nodes.catalog.bindings_missing", "Production node service bindings are required.");
            foreach (var type in SpecializedNodeTypeIds)
                if (!bindings.Contains(new NodeTypeId(type))) return Failure("nodes.catalog.binding_missing", "A Scene, Shader, or Video runtime service binding is missing.");
            return Result.Success();
        }

        public static Result<NodeDefinitionCatalog> CreateProduction(NodeFactoryBindings bindings)
        {
            var catalog = CreateInitial(bindings);
            var valid = catalog.ValidateProductionBindings(bindings);
            return valid.IsFailure ? Result<NodeDefinitionCatalog>.Failure(valid.Diagnostic) : Result<NodeDefinitionCatalog>.Success(catalog);
        }

        public static NodeDefinitionCatalog CreateInitial(NodeFactoryBindings bindings = null)
        {
            bindings = bindings ?? new NodeFactoryBindings();
            return new NodeDefinitionCatalog(InitialNodeDefinitions.Create().Select(definition =>
            {
                var creator = bindings.Resolve(definition.TypeId);
                var placeholder = creator == null;
                creator = creator ?? ((node, generation) => Result<IRuntimeNode>.Success(new CatalogRuntimeNode(node, generation, definition)));
                return new NodeCatalogEntry(definition, new CatalogNodeFactory(definition.TypeId, creator, placeholder), InitialNodeDefinitions.ShaderBinding(definition.TypeId), InitialNodeDefinitions.SceneBinding(definition.TypeId));
            }));
        }
        private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "nodes"));
    }

    public sealed class NodeFactoryBindings
    {
        private readonly Dictionary<NodeTypeId, Func<RuntimeNodeCreateInfo, ulong, Result<IRuntimeNode>>> _bindings = new Dictionary<NodeTypeId, Func<RuntimeNodeCreateInfo, ulong, Result<IRuntimeNode>>>();
        private readonly Dictionary<NodeTypeId, IRuntimeVisualNodeBinding> _visualBindings = new Dictionary<NodeTypeId, IRuntimeVisualNodeBinding>();
        public IReadOnlyCollection<NodeTypeId> RegisteredTypeIds => new ReadOnlyCollection<NodeTypeId>(_bindings.Keys.OrderBy(x => x.Value, StringComparer.Ordinal).ToList());
        public NodeFactoryBindingAvailability Availability => new NodeFactoryBindingAvailability(RegisteredTypeIds, NodeDefinitionCatalog.SpecializedNodeTypeIds.Select(x => new NodeTypeId(x)));
        public bool IsProductionComplete => Availability.IsComplete;

        public Result Register(NodeTypeId typeId, Func<RuntimeNodeCreateInfo, ulong, Result<IRuntimeNode>> creator)
        {
            if (typeId.IsEmpty || creator == null) return Result.Failure(new Diagnostic(new DiagnosticCode("nodes.factory.invalid"), Severity.Error, "A node factory binding requires a type and creator.", module: "nodes"));
            if (!_bindings.TryAdd(typeId, creator)) return Result.Failure(new Diagnostic(new DiagnosticCode("nodes.factory.duplicate"), Severity.Error, "Node factory binding is already registered.", module: "nodes"));
            return Result.Success();
        }

        public Result Register(IRuntimeVisualNodeBinding binding)
        {
            if (binding == null) return Result.Failure(new Diagnostic(new DiagnosticCode("nodes.factory.invalid"), Severity.Error, "A visual node binding is required.", module: "nodes"));
            if (!binding.IsAvailable)
                return Result.Failure(binding.AvailabilityDiagnostic ?? new Diagnostic(new DiagnosticCode("nodes.factory.unavailable"), Severity.Error, "The visual node binding is unavailable.", nodeTypeId: binding.TypeId, module: "nodes"));
            var registered = Register(binding.TypeId, binding.Create);
            if (registered.IsSuccess) _visualBindings.Add(binding.TypeId, binding);
            return registered;
        }
        public bool Contains(NodeTypeId typeId) => _bindings.ContainsKey(typeId);
        public bool TryGetVisualBinding(NodeTypeId typeId, out IRuntimeVisualNodeBinding binding) => _visualBindings.TryGetValue(typeId, out binding);
        internal Func<RuntimeNodeCreateInfo, ulong, Result<IRuntimeNode>> Resolve(NodeTypeId typeId) => _bindings.TryGetValue(typeId, out var creator) ? creator : null;
    }

    /// <summary>Immutable read-only view used by Bootstrap and diagnostics to
    /// report exactly which specialized bindings are ready.</summary>
    public sealed class NodeFactoryBindingAvailability
    {
        public IReadOnlyCollection<NodeTypeId> RegisteredTypeIds { get; }
        public IReadOnlyCollection<NodeTypeId> MissingSpecializedTypes { get; }
        public bool IsComplete => MissingSpecializedTypes.Count == 0;

        internal NodeFactoryBindingAvailability(IEnumerable<NodeTypeId> registered, IEnumerable<NodeTypeId> required)
        {
            var registeredSet = new HashSet<NodeTypeId>(registered ?? Enumerable.Empty<NodeTypeId>());
            RegisteredTypeIds = new ReadOnlyCollection<NodeTypeId>(registeredSet.OrderBy(x => x.Value, StringComparer.Ordinal).ToList());
            MissingSpecializedTypes = new ReadOnlyCollection<NodeTypeId>((required ?? Enumerable.Empty<NodeTypeId>()).Where(x => !registeredSet.Contains(x)).OrderBy(x => x.Value, StringComparer.Ordinal).ToList());
        }
    }

    public sealed class CatalogNodeFactory : INodeFactory
    {
        private readonly Func<RuntimeNodeCreateInfo, ulong, Result<IRuntimeNode>> _creator;
        public NodeTypeId TypeId { get; }
        public bool IsPlaceholder { get; }
        public CatalogNodeFactory(NodeTypeId typeId, Func<RuntimeNodeCreateInfo, ulong, Result<IRuntimeNode>> creator, bool isPlaceholder = false) { TypeId = typeId; _creator = creator ?? throw new ArgumentNullException(nameof(creator)); IsPlaceholder = isPlaceholder; }
        public Result<IRuntimeNode> Create(RuntimeNodeCreateInfo node, ulong generationId)
        {
            if (node == null || node.TypeId != TypeId || generationId == 0) return Result<IRuntimeNode>.Failure(new Diagnostic(new DiagnosticCode("nodes.factory.invalid_node"), Severity.Error, "Factory input does not match the registered type.", nodeTypeId: TypeId, module: "nodes"));
            try { return _creator(node, generationId); }
            catch (Exception exception) { return Result<IRuntimeNode>.Failure(new Diagnostic(new DiagnosticCode("nodes.factory.exception"), Severity.Error, "Node factory threw.", nodeId: node.Id, nodeTypeId: TypeId, generationId: generationId, exception: DiagnosticExceptionInfo.FromException(exception), module: "nodes")); }
        }
    }

    public sealed class CatalogRuntimeNode : IRuntimeNode, IRuntimeDemandAwareNode, IFeedbackResetter, IFeedbackCommitter
    {
        private readonly RuntimeNodeCreateInfo _record;
        private readonly NodeDefinition _definition;
        private IRuntimeImageFrame _feedbackPrevious;
        private bool _disposed;
        public NodeInstanceId NodeId => _record.Id;
        public NodeTypeId TypeId => _definition.TypeId;
        public ulong GenerationId { get; }
        public RuntimeNodeState State => _disposed ? RuntimeNodeState.Disposed : RuntimeNodeState.Ready;
        public CatalogRuntimeNode(RuntimeNodeCreateInfo record, ulong generationId, NodeDefinition definition) { _record = record ?? throw new ArgumentNullException(nameof(record)); _definition = definition ?? throw new ArgumentNullException(nameof(definition)); if (generationId == 0) throw new ArgumentOutOfRangeException(nameof(generationId)); GenerationId = generationId; }
        public void OnDemandChanged(bool demanded, FrameEvaluationContext context) { }
        public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs)
        {
            if (_disposed) return;
            foreach (var port in _definition.Ports.Where(x => x.Direction == NodePortDirection.Output && context.RequestedOutputs.Contains(x.Id)))
            {
                if (port.Type == NodePortType.ImageFrame)
                {
                    outputs.SetPreparing(port.Id, Diagnostic("nodes.binding_missing", "This visual node requires an injected Scene, Shader, Media, or Feedback runtime service.", context));
                }
                else if (TryConvert(context, port, out var value)) outputs.SetAvailable(port.Id, value);
                else outputs.SetBlocked(port.Id, Diagnostic("nodes.input.unavailable", "Required input is unavailable.", context));
            }
        }
        public Result Reset(NodeInstanceId nodeId) { if (nodeId != NodeId) return Failure("nodes.feedback.owner", "Feedback reset owner does not match.", nodeId); _feedbackPrevious = null; return Result.Success(); }
        public Result Commit(NodeInstanceId nodeId, NodeOutputResult input, FrameSnapshot snapshot) { if (nodeId != NodeId || TypeId.Value != "system.feedback") return Failure("nodes.feedback.owner", "Feedback commit owner does not match.", nodeId); if (input.IsAvailable && input.Value.IsImageFrame) _feedbackPrevious = input.Value.AsImageFrame(); return Result.Success(); }
        public void Dispose() { _disposed = true; _feedbackPrevious = null; }

        private bool TryConvert(NodeExecutionContext context, NodePortDefinition output, out PortValue value)
        {
            value = default(PortValue); var type = TypeId.Value;
            if (type == "shitdesigner.convert.float_to_int" && TryFloat(context, "value", out var f)) { var rounding = ParameterString(context, "rounding", "round"); var rounded = rounding == "floor" ? Math.Floor(f) : rounding == "ceil" ? Math.Ceiling(f) : rounding == "truncate" ? Math.Truncate(f) : Math.Round(f, MidpointRounding.AwayFromZero); value = PortValue.FromInt((int)rounded); return true; }
            if (type == "shitdesigner.convert.int_to_float" && TryInt(context, "value", out var i)) { value = PortValue.FromFloat(i); return true; }
            if (type == "shitdesigner.convert.float_to_bool" && TryFloat(context, "value", out f)) { var threshold = ParameterFloat(context, "threshold", .5f); var comparison = ParameterString(context, "comparison", "greater_equal"); value = PortValue.FromBool(comparison == "less" ? f < threshold : f >= threshold); return true; }
            if (type == "shitdesigner.convert.bool_to_float" && TryBool(context, "value", out var b)) { value = PortValue.FromFloat(b ? ParameterFloat(context, "true_value", 1f) : ParameterFloat(context, "false_value", 0f)); return true; }
            if (type.StartsWith("shitdesigner.convert.compose_vector", StringComparison.Ordinal)) return Compose(context, type, out value);
            if (type.StartsWith("shitdesigner.convert.split_vector", StringComparison.Ordinal)) return Split(context, output.Id, out value);
            if (type == "shitdesigner.convert.vector_component") return VectorComponent(context, ParameterString(context, "component", "x"), out value);
            if (type == "shitdesigner.convert.color_to_luminance" && TryColor(context, "value", out var color)) { value = PortValue.FromFloat(color.R * .2126f + color.G * .7152f + color.B * .0722f); return true; }
            if (type == "shitdesigner.convert.float_to_color" && TryFloat(context, "value", out f)) { value = PortValue.FromColor(new ColorValue(f, f, f, ParameterFloat(context, "alpha", 1f))); return true; }
            return false;
        }
        private static bool Compose(NodeExecutionContext c, string type, out PortValue value) { value = default(PortValue); if (!TryFloat(c, "x", out var x) || !TryFloat(c, "y", out var y)) return false; if (type.EndsWith("2")) { value = PortValue.FromVector2(new Vector2Value(x, y)); return true; } if (!TryFloat(c, "z", out var z)) return false; if (type.EndsWith("3")) { value = PortValue.FromVector3(new Vector3Value(x, y, z)); return true; } if (!TryFloat(c, "w", out var w)) return false; value = PortValue.FromVector4(new Vector4Value(x, y, z, w)); return true; }
        private static bool Split(NodeExecutionContext c, PortId id, out PortValue value) { value = default(PortValue); if (!c.Inputs.TryGetValue(new PortId("value"), out var input) || !input.HasValue) return false; try { var v = input.Value.AsVector2(); if (id.Value == "x") value = PortValue.FromFloat(v.X); else if (id.Value == "y") value = PortValue.FromFloat(v.Y); else return false; return true; } catch (InvalidOperationException) { } try { var v = input.Value.AsVector3(); if (id.Value == "x") value = PortValue.FromFloat(v.X); else if (id.Value == "y") value = PortValue.FromFloat(v.Y); else if (id.Value == "z") value = PortValue.FromFloat(v.Z); else return false; return true; } catch (InvalidOperationException) { } try { var v = input.Value.AsVector4(); if (id.Value == "x") value = PortValue.FromFloat(v.X); else if (id.Value == "y") value = PortValue.FromFloat(v.Y); else if (id.Value == "z") value = PortValue.FromFloat(v.Z); else if (id.Value == "w") value = PortValue.FromFloat(v.W); else return false; return true; } catch (InvalidOperationException) { return false; } }
        private static bool VectorComponent(NodeExecutionContext c, string component, out PortValue value) { value = default(PortValue); if (!c.Inputs.TryGetValue(new PortId("value"), out var input) || !input.HasValue) return false; try { var v = input.Value.AsVector4(); value = PortValue.FromFloat(component == "y" ? v.Y : component == "z" ? v.Z : component == "w" ? v.W : v.X); return true; } catch (InvalidOperationException) { return false; } }
        private static bool TryFloat(NodeExecutionContext c, string id, out float value) { value = 0; if (!c.Inputs.TryGetValue(new PortId(id), out var input) || !input.HasValue) return false; try { value = input.Value.AsFloat(); return true; } catch (InvalidOperationException) { return false; } }
        private static bool TryInt(NodeExecutionContext c, string id, out int value) { value = 0; if (!c.Inputs.TryGetValue(new PortId(id), out var input) || !input.HasValue) return false; try { value = input.Value.AsInt(); return true; } catch (InvalidOperationException) { return false; } }
        private static bool TryBool(NodeExecutionContext c, string id, out bool value) { value = false; if (!c.Inputs.TryGetValue(new PortId(id), out var input) || !input.HasValue) return false; try { value = input.Value.AsBool(); return true; } catch (InvalidOperationException) { return false; } }
        private static bool TryColor(NodeExecutionContext c, string id, out ColorValue value) { value = default(ColorValue); if (!c.Inputs.TryGetValue(new PortId(id), out var input) || !input.HasValue) return false; try { value = input.Value.AsColor(); return true; } catch (InvalidOperationException) { return false; } }
        private string ParameterString(NodeExecutionContext context, string id, string fallback)
        {
            var key = new ParameterKey(NodeId, new ParameterId(id));
            if (context?.Snapshot?.EffectiveValues != null && context.Snapshot.EffectiveValues.TryGetValue(key, out var effective))
            {
                try { return effective.AsString(); } catch (InvalidOperationException) { }
            }
            var parameter = _record.Parameters.FirstOrDefault(x => x.Id.Value == id);
            if (parameter == null) return fallback;
            try { return parameter.Value.AsString(); } catch (InvalidOperationException) { return fallback; }
        }
        private float ParameterFloat(NodeExecutionContext context, string id, float fallback)
        {
            var key = new ParameterKey(NodeId, new ParameterId(id));
            if (context?.Snapshot?.EffectiveValues != null && context.Snapshot.EffectiveValues.TryGetValue(key, out var effective))
            {
                try { return effective.AsFloat(); } catch (InvalidOperationException) { }
            }
            var parameter = _record.Parameters.FirstOrDefault(x => x.Id.Value == id);
            if (parameter == null) return fallback;
            try { return parameter.Value.AsFloat(); } catch (InvalidOperationException) { return fallback; }
        }
        private Diagnostic Diagnostic(string code, string message, NodeExecutionContext context) => new Diagnostic(new DiagnosticCode(code), Severity.Warning, message, nodeId: NodeId, nodeTypeId: TypeId, generationId: GenerationId, frameNumber: unchecked((long)context.Snapshot.FrameNumber), graphClockTime: context.Snapshot.GraphClockTime, module: "nodes");
        private Result Failure(string code, string message, NodeInstanceId nodeId) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: nodeId, nodeTypeId: TypeId, generationId: GenerationId, module: "nodes"));
    }

    public sealed class SurfaceImageFrame : IRuntimeImageFrameSurface
    {
        private readonly IRuntimeOutputSurface _surface;
        public int Width => _surface.Width;
        public int Height => _surface.Height;
        // The prepared surface owns the actual dynamic-range/graphics format.
        // Keep the neutral Nodes wrapper from manufacturing an HDR format when
        // the project is configured for the LDR R8G8B8A8 path.
        public string ColorFormat => (_surface as IRuntimeOutputSurfaceFormat)?.ColorFormat ?? "R16G16B16A16_SFloat";
        public ulong FrameNumber => _surface.FrameNumber;
        public ulong LeaseId => _surface.LeaseId;
        public object NativeSurface => _surface.NativeSurface;
        public SurfaceImageFrame(IRuntimeOutputSurface surface) { _surface = surface ?? throw new ArgumentNullException(nameof(surface)); }
    }

    internal static class InitialNodeDefinitions
    {
        private static readonly NodePortDefinition ImageOut = new NodePortDefinition(new PortId("image"), "Image", NodePortDirection.Output, NodePortType.ImageFrame, false);
        private static readonly NodePortDefinition RequiredImage = new NodePortDefinition(new PortId("image"), "Image", NodePortDirection.Input, NodePortType.ImageFrame, true);
        private static readonly NodePortDefinition EffectInput = new NodePortDefinition(new PortId("input"), "Input", NodePortDirection.Input, NodePortType.ImageFrame, true);
        public static IReadOnlyList<NodeDefinition> Create() => new ReadOnlyCollection<NodeDefinition>(new List<NodeDefinition> {
            new NodeDefinition(new NodeTypeId("system.program_output"), 1, "ProgramOutput", "System", new[] { RequiredImage }, systemOwned: true, userAddable: false),
            new NodeDefinition(new NodeTypeId("system.preview"), 1, "Preview", "System", new[] { RequiredImage }, new[] { PreviewMode() }),
            new NodeDefinition(new NodeTypeId("system.feedback"), 1, "Feedback", "Processing", new[] { new NodePortDefinition(new PortId("input"), "Input", NodePortDirection.Input, NodePortType.ImageFrame, false, RuntimeDefaultImageKind.TransparentBlack), ImageOut }),
            new NodeDefinition(new NodeTypeId("shitdesigner.scene.3d"), 1, "3D", "3D", new[] { ImageOut }), new NodeDefinition(new NodeTypeId("shitdesigner.scene.2d"), 1, "2D", "2D", new[] { ImageOut }),
            new NodeDefinition(new NodeTypeId("shitdesigner.shader.generator"), 1, "Shader Generator", "Shader/Generator", new[] { ImageOut }, new[] { new NodeParameterDefinition(new ParameterId("color"), "Color", ParameterType.Color, ParameterValue.FromColor(new ColorValue(0, 0, 0, 1)), group: "Shader", displayOrder: 0) }), new NodeDefinition(new NodeTypeId("shitdesigner.shader.effect"), 1, "Shader Effect", "Shader/Effect", new[] { EffectInput, ImageOut }), new NodeDefinition(new NodeTypeId("shitdesigner.shader.blend2"), 1, "Shader 2-input Blend", "Shader/Effect", new[] { new NodePortDefinition(new PortId("a"), "A", NodePortDirection.Input, NodePortType.ImageFrame, true), new NodePortDefinition(new PortId("b"), "B", NodePortDirection.Input, NodePortType.ImageFrame, true), ImageOut }),
            new NodeDefinition(new NodeTypeId("shitdesigner.video.player"), 1, "VideoPlayer", "Video", new[] { ImageOut }, VideoParameters()),
            FloatToInt(), IntToFloat(), FloatToBool(), BoolToFloat(), Compose("2"), Compose("3"), Compose("4"), Split("2"), Split("3"), Split("4"), VectorComponent(), ColorToLuminance(), FloatToColor()
        });
        public static ShaderNodeBinding ShaderBinding(NodeTypeId id) { if (id.Value == "shitdesigner.shader.generator") return new ShaderNodeBinding("builtin.shader.generator", parameterProperties: new Dictionary<ParameterId, string> { { new ParameterId("color"), "_Color" } }); if (id.Value == "shitdesigner.shader.effect") return new ShaderNodeBinding("builtin.shader.effect", new Dictionary<PortId, string> { { new PortId("input"), "_MainTex" } }); if (id.Value == "shitdesigner.shader.blend2") return new ShaderNodeBinding("builtin.shader.blend2", new Dictionary<PortId, string> { { new PortId("a"), "_TexA" }, { new PortId("b"), "_TexB" } }); return null; }
        public static SceneNodeBinding SceneBinding(NodeTypeId id) { if (id.Value == "shitdesigner.scene.3d") return new SceneNodeBinding("builtin.scene.3d"); if (id.Value == "shitdesigner.scene.2d") return new SceneNodeBinding("builtin.scene.2d", true, true); return null; }
        private static NodeParameterDefinition PreviewMode() => new NodeParameterDefinition(new ParameterId("display.mode"), "Display Mode", ParameterType.Enum, ParameterValue.FromEnum("fit"), enumOptions: new[] { "fit", "fill", "stretch" });
        private static IEnumerable<NodeParameterDefinition> VideoParameters() => new[] { new NodeParameterDefinition(new ParameterId("transport.media_asset"), "Media Asset", ParameterType.MediaAssetReference, ParameterValue.Default(ParameterType.MediaAssetReference)), new NodeParameterDefinition(new ParameterId("transport.playing"), "Playing", ParameterType.Bool, ParameterValue.FromBool(false)), new NodeParameterDefinition(new ParameterId("transport.playhead_seconds"), "Playhead", ParameterType.Float, ParameterValue.FromFloat(0), ParameterValue.FromFloat(0), ParameterValue.FromFloat(float.MaxValue), true), new NodeParameterDefinition(new ParameterId("transport.speed"), "Speed", ParameterType.Float, ParameterValue.FromFloat(1), ParameterValue.FromFloat(0), ParameterValue.FromFloat(4)), new NodeParameterDefinition(new ParameterId("transport.loop"), "Loop", ParameterType.Bool, ParameterValue.FromBool(true)) };
        private static NodeDefinition FloatToInt() => Conversion("float_to_int", "Float To Int", NodePortType.Float, NodePortType.Int, new[] { Input("value", NodePortType.Float) }, new[] { new NodeParameterDefinition(new ParameterId("rounding"), "Rounding", ParameterType.Enum, ParameterValue.FromEnum("round"), enumOptions: new[] { "round", "floor", "ceil", "truncate" }) });
        private static NodeDefinition IntToFloat() => Conversion("int_to_float", "Int To Float", NodePortType.Int, NodePortType.Float, new[] { Input("value", NodePortType.Int) });
        private static NodeDefinition FloatToBool() => Conversion("float_to_bool", "Float To Bool", NodePortType.Float, NodePortType.Bool, new[] { Input("value", NodePortType.Float) }, new[] { new NodeParameterDefinition(new ParameterId("threshold"), "Threshold", ParameterType.Float, ParameterValue.FromFloat(.5f), ParameterValue.FromFloat(0), ParameterValue.FromFloat(1)) });
        private static NodeDefinition BoolToFloat() => Conversion("bool_to_float", "Bool To Float", NodePortType.Bool, NodePortType.Float, new[] { Input("value", NodePortType.Bool) }, new[] { new NodeParameterDefinition(new ParameterId("false_value"), "False", ParameterType.Float, ParameterValue.FromFloat(0)), new NodeParameterDefinition(new ParameterId("true_value"), "True", ParameterType.Float, ParameterValue.FromFloat(1)) });
        private static NodeDefinition Compose(string n) { var ports = (n == "2" ? new[] { "x", "y" } : n == "3" ? new[] { "x", "y", "z" } : new[] { "x", "y", "z", "w" }).Select(x => Input(x, NodePortType.Float)).Cast<NodePortDefinition>().ToList(); ports.Add(new NodePortDefinition(new PortId("result"), "Result", NodePortDirection.Output, n == "2" ? NodePortType.Vector2 : n == "3" ? NodePortType.Vector3 : NodePortType.Vector4, false)); return new NodeDefinition(new NodeTypeId("shitdesigner.convert.compose_vector" + n), 1, "Compose Vector" + n, "Conversion", ports); }
        private static NodeDefinition Split(string n) { var ports = new List<NodePortDefinition> { Input("value", n == "2" ? NodePortType.Vector2 : n == "3" ? NodePortType.Vector3 : NodePortType.Vector4) }; foreach (var x in n == "2" ? new[] { "x", "y" } : n == "3" ? new[] { "x", "y", "z" } : new[] { "x", "y", "z", "w" }) ports.Add(new NodePortDefinition(new PortId(x), x.ToUpperInvariant(), NodePortDirection.Output, NodePortType.Float, false)); return new NodeDefinition(new NodeTypeId("shitdesigner.convert.split_vector" + n), 1, "Split Vector" + n, "Conversion", ports); }
        private static NodeDefinition VectorComponent() => new NodeDefinition(new NodeTypeId("shitdesigner.convert.vector_component"), 1, "Vector Component", "Conversion", new[] { Input("value", NodePortType.Vector4), new NodePortDefinition(new PortId("result"), "Result", NodePortDirection.Output, NodePortType.Float, false) }, new[] { new NodeParameterDefinition(new ParameterId("component"), "Component", ParameterType.Enum, ParameterValue.FromEnum("x"), enumOptions: new[] { "x", "y", "z", "w" }) });
        private static NodeDefinition ColorToLuminance() => Conversion("color_to_luminance", "Color To Luminance", NodePortType.Color, NodePortType.Float, new[] { Input("value", NodePortType.Color) });
        private static NodeDefinition FloatToColor() => Conversion("float_to_color", "Float To Color", NodePortType.Float, NodePortType.Color, new[] { Input("value", NodePortType.Float) }, new[] { new NodeParameterDefinition(new ParameterId("alpha"), "Alpha", ParameterType.Float, ParameterValue.FromFloat(1), ParameterValue.FromFloat(0), ParameterValue.FromFloat(1)) });
        private static NodePortDefinition Input(string id, NodePortType type) => new NodePortDefinition(new PortId(id), id.ToUpperInvariant(), NodePortDirection.Input, type, true);
        private static NodeDefinition Conversion(string id, string display, NodePortType input, NodePortType output, IEnumerable<NodePortDefinition> ports, IEnumerable<NodeParameterDefinition> parameters = null) => new NodeDefinition(new NodeTypeId("shitdesigner.convert." + id), 1, display, "Conversion", ports.Concat(new[] { new NodePortDefinition(new PortId("result"), "Result", NodePortDirection.Output, output, false) }), parameters);
    }
}
