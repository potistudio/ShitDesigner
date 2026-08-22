using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;

namespace ShitDesigner.Rendering
{
    /// <summary>Explicit shader/material metadata. Runtime reflection is not
    /// used: every input and parameter property is registered by the catalog
    /// builder.</summary>
    public sealed class ShaderMaterialBinding
    {
        public string Key { get; }
        public Shader Shader { get; }
        public IReadOnlyDictionary<PortId, string> InputProperties { get; }
        public IReadOnlyDictionary<ParameterId, string> ParameterProperties { get; }
        public int OutputPass { get; }

        public ShaderMaterialBinding(string key, Shader shader,
            IDictionary<PortId, string> inputProperties = null,
            IDictionary<ParameterId, string> parameterProperties = null,
            int outputPass = 0)
        {
            if (string.IsNullOrWhiteSpace(key) || shader == null || outputPass < 0) throw new ArgumentException("Shader binding metadata is invalid.");
            Key = key.Trim(); Shader = shader; OutputPass = outputPass;
            InputProperties = new ReadOnlyDictionary<PortId, string>(new Dictionary<PortId, string>(inputProperties ?? new Dictionary<PortId, string>()));
            ParameterProperties = new ReadOnlyDictionary<ParameterId, string>(new Dictionary<ParameterId, string>(parameterProperties ?? new Dictionary<ParameterId, string>()));
            if (InputProperties.Any(x => string.IsNullOrWhiteSpace(x.Value)) || ParameterProperties.Any(x => string.IsNullOrWhiteSpace(x.Value))) throw new ArgumentException("Shader property names are required.");
        }
    }

    public sealed class ShaderMaterialRegistry
    {
        private readonly Dictionary<string, ShaderMaterialBinding> _bindings = new Dictionary<string, ShaderMaterialBinding>(StringComparer.Ordinal);
        public IReadOnlyCollection<string> Keys => new ReadOnlyCollection<string>(_bindings.Keys.OrderBy(x => x, StringComparer.Ordinal).ToList());

        public Result Register(ShaderMaterialBinding binding)
        {
            if (binding == null) return Failure("rendering.shader.binding", "Shader material binding is required.");
            if (!_bindings.TryAdd(binding.Key, binding)) return Failure("rendering.shader.duplicate", "Shader material binding is already registered.");
            return Result.Success();
        }

        public bool TryGet(string key, out ShaderMaterialBinding binding) => _bindings.TryGetValue(key ?? string.Empty, out binding);
        private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "rendering"));
    }

    /// <summary>Production factory for the three explicit shader roles. It
    /// creates an instance-owned Material and never mutates the catalog
    /// template.</summary>
    public sealed class ShaderVisualNodeBinding : IRuntimeVisualNodeBinding
    {
        private readonly ShaderMaterialRegistry _registry;
        private readonly string _shaderKey;
        private readonly bool _generator;
        private readonly bool _blend;
        public NodeTypeId TypeId { get; }
        public bool IsAvailable => _registry != null && _registry.TryGet(_shaderKey, out _);
        public Diagnostic AvailabilityDiagnostic => IsAvailable ? null : new Diagnostic(new DiagnosticCode("rendering.shader.binding_missing"), Severity.Error, "The explicit shader/material binding is unavailable.", nodeTypeId: TypeId, module: "rendering");

        public ShaderVisualNodeBinding(NodeTypeId typeId, string shaderKey, ShaderMaterialRegistry registry, bool generator = false, bool blend = false)
        {
            if (typeId.IsEmpty || string.IsNullOrWhiteSpace(shaderKey)) throw new ArgumentException("Shader visual binding identity is required.");
            TypeId = typeId; _shaderKey = shaderKey.Trim(); _registry = registry; _generator = generator; _blend = blend;
        }

        public Result<IRuntimeNode> Create(RuntimeNodeCreateInfo node, ulong generationId)
        {
            if (node == null || node.TypeId != TypeId || generationId == 0) return FailureNode("rendering.shader.node", "Shader factory input does not match its binding.", node, generationId);
            if (!IsAvailable) return Result<IRuntimeNode>.Failure(AvailabilityDiagnostic);
            if (!_registry.TryGet(_shaderKey, out var binding)) return Result<IRuntimeNode>.Failure(AvailabilityDiagnostic);
            try { return Result<IRuntimeNode>.Success(new ShaderRuntimeNode(node, generationId, binding, _generator, _blend)); }
            catch (Exception exception) { return FailureNode("rendering.shader.create", exception.Message, node, generationId, exception); }
        }

        private Result<IRuntimeNode> FailureNode(string code, string message, RuntimeNodeCreateInfo node, ulong generationId, Exception exception = null) =>
            Result<IRuntimeNode>.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: node?.Id ?? default(NodeInstanceId), nodeTypeId: TypeId, generationId: generationId, module: "rendering", exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception)));
    }

    public sealed class ShaderRuntimeNode : IRuntimeNode, IRuntimeDemandAwareNode
    {
        private readonly RuntimeNodeCreateInfo _record;
        private readonly ShaderMaterialBinding _binding;
        private readonly Material _material;
        private readonly bool _generator;
        private readonly bool _blend;
        private IRuntimeImageFrame _lastFrame;
        private bool _disposed;
        private double _lastClock;

        public NodeInstanceId NodeId => _record.Id;
        public NodeTypeId TypeId => _record.TypeId;
        public ulong GenerationId { get; }
        public RuntimeNodeState State { get; private set; } = RuntimeNodeState.Preparing;

        internal ShaderRuntimeNode(RuntimeNodeCreateInfo record, ulong generationId, ShaderMaterialBinding binding, bool generator, bool blend)
        {
            _record = record ?? throw new ArgumentNullException(nameof(record));
            _binding = binding ?? throw new ArgumentNullException(nameof(binding));
            if (generationId == 0) throw new ArgumentOutOfRangeException(nameof(generationId));
            GenerationId = generationId; _generator = generator; _blend = blend;
            _material = new Material(binding.Shader) { name = "ShitDesigner.NodeMaterial." + record.Id.Value };
        }

        public void OnDemandChanged(bool demanded, FrameEvaluationContext context)
        {
            if (!demanded) return;
            if (context != null) _lastClock = context.Snapshot.GraphClockTime;
        }

        public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs)
        {
            var image = new PortId("image");
            if (_disposed) { outputs.SetFaulted(image, Failure("rendering.shader.disposed", "Shader node is disposed.", context)); return; }
            if (!context.RequestedOutputs.Contains(image)) return;
            if (!TryDemand(context, out var demand))
            {
                WriteLastOrFailure(context, outputs, image, Failure("rendering.shader.demand_missing", "Shader output has no propagated Phase-4 resolution demand.", context));
                State = RuntimeNodeState.Preparing;
                return;
            }
            var surface = context.OutputSurfaces?.TryGetPrepared(NodeId, image, demand.Width, demand.Height, context.Snapshot.FrameNumber);
            if (!surface.HasValue || surface.Value.IsFailure || surface.Value.Value == null)
            {
                WriteLastOrFailure(context, outputs, image, surface.HasValue && surface.Value.IsFailure ? surface.Value.Diagnostic : Failure("rendering.shader.surface_missing", "Shader output requires a prepared Phase-5 surface.", context));
                State = RuntimeNodeState.Faulted;
                return;
            }

            var prepared = surface.Value.Value;
            if (!(prepared.NativeSurface is RenderTexture target) || prepared.LeaseId == 0)
            {
                WriteLastOrFailure(context, outputs, image, Failure("rendering.shader.surface_invalid", "Prepared shader surface is not a live RenderTexture lease.", context));
                State = RuntimeNodeState.Faulted;
                return;
            }
            try
            {
                ApplyParameters(context.Snapshot);
                if (!_generator)
                {
                    if (!TryInput(context, _blend ? new PortId("a") : new PortId("input"), out var first))
                    {
                        WriteLastOrFailure(context, outputs, image, Failure("rendering.shader.input_missing", "Shader effect input is unavailable.", context));
                        State = RuntimeNodeState.Preparing;
                        return;
                    }
                    _material.SetTexture(PropertyFor(_blend ? new PortId("a") : new PortId("input"), "_MainTex"), first);
                    if (_blend)
                    {
                        if (!TryInput(context, new PortId("b"), out var second))
                        {
                            WriteLastOrFailure(context, outputs, image, Failure("rendering.shader.input_missing", "Shader blend input B is unavailable.", context));
                            State = RuntimeNodeState.Preparing;
                            return;
                        }
                        _material.SetTexture(PropertyFor(new PortId("b"), "_TexB"), second);
                    }
                }
                var source = _generator ? Texture2D.blackTexture : (TryInputTexture(context, _blend ? new PortId("a") : new PortId("input")) ?? Texture2D.blackTexture);
                Graphics.Blit(source, target, _material, _binding.OutputPass);
                if (prepared is IRuntimeOutputSurfaceCompletion completion) completion.MarkRendered();
                var frame = new RenderingRuntimeImageFrame(prepared, context.Snapshot.FrameNumber);
                _lastFrame = frame; State = RuntimeNodeState.Ready;
                outputs.SetAvailable(image, PortValue.FromImageFrame(frame));
            }
            catch (Exception exception)
            {
                State = RuntimeNodeState.Faulted;
                WriteLastOrFailure(context, outputs, image, Failure("rendering.shader.render_failed", exception.Message, context, exception));
            }
        }

        private bool TryDemand(NodeExecutionContext context, out RuntimeOutputResolutionDemand demand)
        {
            demand = null;
            return RuntimeOutputResolutionDemandAccess.TryGet(context, NodeId, new PortId("image"), out demand);
        }
        private bool TryInput(NodeExecutionContext context, PortId id, out Texture texture)
        {
            texture = TryInputTexture(context, id);
            return texture != null;
        }
        private Texture TryInputTexture(NodeExecutionContext context, PortId id)
        {
            if (!context.Inputs.TryGetValue(id, out var input) || !input.HasValue || !input.Value.IsImageFrame) return null;
            return (input.Value.AsImageFrame() as IRuntimeImageFrameSurface)?.NativeSurface as Texture;
        }
        private string PropertyFor(PortId id, string fallback) => _binding.InputProperties.TryGetValue(id, out var property) ? property : fallback;
        private void ApplyParameters(FrameSnapshot snapshot)
        {
            foreach (var binding in _binding.ParameterProperties)
            {
                if (!snapshot.EffectiveValues.TryGetValue(new ParameterKey(NodeId, binding.Key), out var value)) continue;
                switch (value.Type)
                {
                    case ParameterType.Float: _material.SetFloat(binding.Value, value.AsFloat()); break;
                    case ParameterType.Int: _material.SetInt(binding.Value, value.AsInt()); break;
                    case ParameterType.Bool: _material.SetFloat(binding.Value, value.AsBool() ? 1f : 0f); break;
                    case ParameterType.Color:
                        // Color parameters are already Linear at the Runtime
                        // boundary. SetVector avoids Unity's sRGB conversion
                        // performed by SetColor for Color-typed properties.
                        var c = value.AsColor();
                        _material.SetVector(binding.Value, new Vector4(c.R, c.G, c.B, c.A));
                        break;
                    case ParameterType.Vector2: var v2 = value.AsVector2(); _material.SetVector(binding.Value, new Vector4(v2.X, v2.Y, 0, 0)); break;
                    case ParameterType.Vector3: var v3 = value.AsVector3(); _material.SetVector(binding.Value, new Vector4(v3.X, v3.Y, v3.Z, 0)); break;
                    case ParameterType.Vector4: var v4 = value.AsVector4(); _material.SetVector(binding.Value, new Vector4(v4.X, v4.Y, v4.Z, v4.W)); break;
                }
            }
        }
        private void WriteLastOrFailure(NodeExecutionContext context, NodeOutputWriter outputs, PortId image, Diagnostic diagnostic)
        {
            if (_lastFrame != null) { outputs.SetAvailable(image, PortValue.FromImageFrame(_lastFrame)); context.Diagnostics.Report(diagnostic); }
            else outputs.SetPreparing(image, diagnostic);
        }
        private Diagnostic Failure(string code, string message, NodeExecutionContext context, Exception exception = null) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: NodeId, nodeTypeId: TypeId, generationId: GenerationId, frameNumber: context == null ? 0 : unchecked((long)context.Snapshot.FrameNumber), module: "rendering", exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception));
        public void Dispose() { if (_disposed) return; _disposed = true; if (_material != null) UnityEngine.Object.DestroyImmediate(_material); _lastFrame = null; State = RuntimeNodeState.Disposed; }
    }

    /// <summary>Rendering-owned neutral frame view. It carries only the
    /// borrowed surface; the pool remains the owner and the node cannot
    /// release it.</summary>
    public sealed class RenderingRuntimeImageFrame : IRuntimeImageFrameSurface
    {
        private readonly IRuntimeOutputSurface _surface;
        public int Width => _surface.Width;
        public int Height => _surface.Height;
        public string ColorFormat => (_surface as IRuntimeOutputSurfaceFormat)?.ColorFormat ?? "R16G16B16A16_SFloat";
        public ulong FrameNumber { get; }
        public ulong LeaseId => _surface.LeaseId;
        public object NativeSurface => _surface.NativeSurface;
        public RenderingRuntimeImageFrame(IRuntimeOutputSurface surface, ulong frameNumber)
        {
            _surface = surface ?? throw new ArgumentNullException(nameof(surface));
            if (surface.LeaseId == 0 || frameNumber == 0) throw new ArgumentException("A live output surface and frame are required.");
            FrameNumber = frameNumber;
        }
    }

    /// <summary>Feedback factory and Phase-9 committer. The node only borrows
    /// the previous lease; history mutation happens through this committer.</summary>
    public sealed class FeedbackVisualNodeBinding : IRuntimeVisualNodeBinding, IFeedbackCommitter, IFeedbackResetter, IRuntimeResourcePreparationWithPlan, IDisposable
    {
        private readonly RenderTexturePool _pool;
        private readonly string _sessionId;
        private readonly IRuntimeOutputFormatPolicy _formatPolicy;
        private readonly Dictionary<NodeInstanceId, FeedbackRuntimeNode> _nodes = new Dictionary<NodeInstanceId, FeedbackRuntimeNode>();
        private bool _disposed;
        public NodeTypeId TypeId { get; } = new NodeTypeId("system.feedback");
        public bool IsAvailable => !_disposed && _pool != null && !string.IsNullOrWhiteSpace(_sessionId);
        public Diagnostic AvailabilityDiagnostic => IsAvailable ? null : new Diagnostic(new DiagnosticCode("rendering.feedback.binding_missing"), Severity.Error, "Feedback requires a live RenderTexturePool and session ID.", nodeTypeId: TypeId, module: "rendering");
        public FeedbackVisualNodeBinding(RenderTexturePool pool, string sessionId, IRuntimeOutputFormatPolicy formatPolicy = null)
        { _pool = pool; _sessionId = sessionId; _formatPolicy = formatPolicy ?? new RuntimeOutputFormatPolicy(); }
        public Result<IRuntimeNode> Create(RuntimeNodeCreateInfo node, ulong generationId)
        {
            if (!IsAvailable) return Result<IRuntimeNode>.Failure(AvailabilityDiagnostic);
            if (node == null || node.TypeId != TypeId || generationId == 0) return FailureNode("rendering.feedback.node", "Feedback factory input does not match its binding.", node, generationId);
            var owner = new ResourceOwnerKey(_sessionId, ResourceOwnerKind.Feedback, node.Id.Value, generationId, "history", LeaseRole.FeedbackPrevious);
            var history = new FeedbackHistoryController(_pool, owner);
            var runtime = new FeedbackRuntimeNode(node, generationId, history);
            _nodes[node.Id] = runtime;
            return Result<IRuntimeNode>.Success(runtime);
        }
        public Result Commit(NodeInstanceId nodeId, NodeOutputResult input, FrameSnapshot snapshot)
        {
            if (!_nodes.TryGetValue(nodeId, out var node)) return Failure("rendering.feedback.node_missing", "Feedback node is not registered.");
            return node.Commit(input, snapshot);
        }
        public Result Reset(NodeInstanceId nodeId)
        {
            if (!_nodes.TryGetValue(nodeId, out var node)) return Failure("rendering.feedback.node_missing", "Feedback node is not registered.");
            return node.Reset(nodeId);
        }
        public Result Prepare(FrameSnapshot snapshot) => Prepare(snapshot, null);
        public Result Prepare(FrameSnapshot snapshot, FrameEvaluationContext evaluation)
        {
            if (!IsAvailable) return Result.Failure(AvailabilityDiagnostic);
            if (snapshot == null) return Failure("rendering.feedback.snapshot", "Feedback preparation requires a FrameSnapshot.");
            var resolutions = evaluation != null
                ? RuntimeOutputResolutionDemandAccess.GetAll(evaluation)
                : RuntimeOutputResolutionDemandAccess.GetAll(snapshot);
            foreach (var pair in _nodes)
            {
                var resolution = resolutions.FirstOrDefault(x => x.NodeId == pair.Key && x.PortId.Value == "image");
                if (resolution == null) continue;
                var demand = resolution.Demand;
                var prepared = pair.Value.Prepare(new TextureDescriptor(demand.Width, demand.Height, _formatPolicy.DynamicRange == RuntimeDynamicRange.Hdr ? UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat : UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm), snapshot.FrameNumber);
                if (prepared.IsFailure) return prepared;
            }
            return Result.Success();
        }
        public void Dispose() { if (_disposed) return; _disposed = true; foreach (var node in _nodes.Values) node.Dispose(); _nodes.Clear(); }
        private Result<IRuntimeNode> FailureNode(string code, string message, RuntimeNodeCreateInfo node, ulong generationId) => Result<IRuntimeNode>.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: node?.Id ?? default(NodeInstanceId), nodeTypeId: TypeId, generationId: generationId, module: "rendering"));
        private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "rendering"));
    }

    public sealed class FeedbackRuntimeNode : IRuntimeNode, IRuntimeDemandAwareNode
    {
        private readonly RuntimeNodeCreateInfo _record;
        private readonly FeedbackHistoryController _history;
        private IRuntimeImageFrame _lastFrame;
        private bool _disposed;
        public NodeInstanceId NodeId => _record.Id;
        public NodeTypeId TypeId => _record.TypeId;
        public ulong GenerationId { get; }
        public RuntimeNodeState State { get; private set; } = RuntimeNodeState.Preparing;
        internal FeedbackRuntimeNode(RuntimeNodeCreateInfo record, ulong generationId, FeedbackHistoryController history) { _record = record; GenerationId = generationId; _history = history ?? throw new ArgumentNullException(nameof(history)); }
        public void OnDemandChanged(bool demanded, FrameEvaluationContext context) { }
        public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs)
        {
            var image = new PortId("image");
            if (!context.RequestedOutputs.Contains(image)) return;
            if (_disposed) { outputs.SetFaulted(image, Failure("rendering.feedback.disposed", "Feedback node is disposed.", context)); return; }
            // History leases are prepared by FeedbackVisualNodeBinding in
            // Phase 5. Evaluation only borrows the already prepared pair.
            var previous = _history.BorrowPrevious(context.Snapshot.FrameNumber);
            if (previous.IsFailure) { WriteLast(context, outputs, image, previous.Diagnostic); State = RuntimeNodeState.Preparing; return; }
            if (!TryDemand(context, out var demand))
            {
                WriteLast(context, outputs, image, Failure("rendering.feedback.demand_missing", "Feedback output has no propagated Phase-4 resolution demand.", context));
                State = RuntimeNodeState.Preparing; return;
            }
            var prepared = context.OutputSurfaces?.TryGetPrepared(NodeId, image, demand.Width, demand.Height, context.Snapshot.FrameNumber);
            var target = prepared.HasValue && prepared.Value.IsSuccess ? prepared.Value.Value.NativeSurface as RenderTexture : null;
            if (!prepared.HasValue || prepared.Value.IsFailure || target == null)
            {
                WriteLast(context, outputs, image, prepared.HasValue && prepared.Value.IsFailure ? prepared.Value.Diagnostic : Failure("rendering.feedback.surface_missing", "Feedback output requires a prepared Phase-5 surface.", context));
                State = RuntimeNodeState.Preparing; return;
            }
            Graphics.Blit(previous.Value.Texture, target);
            if (prepared.Value.Value is IRuntimeOutputSurfaceCompletion completion) completion.MarkRendered();
            var frame = new RenderingRuntimeImageFrame(prepared.Value.Value, context.Snapshot.FrameNumber);
            _lastFrame = frame; State = RuntimeNodeState.Ready; outputs.SetAvailable(image, PortValue.FromImageFrame(frame));
        }
        internal Result Prepare(TextureDescriptor descriptor, ulong frameNumber) => _history.EnsureDescriptor(descriptor, frameNumber);
        internal Result Commit(NodeOutputResult input, FrameSnapshot snapshot)
        {
            if (!input.IsAvailable || !input.Value.IsImageFrame) return Result.Success();
            if (!((input.Value.AsImageFrame() as IRuntimeImageFrameSurface)?.NativeSurface is RenderTexture texture)) return Failure("rendering.feedback.input", "Feedback input is not a RenderTexture-backed frame.");
            var image = input.Value.AsImageFrame();
            var format = image.ColorFormat == UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm.ToString() ? UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm : UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat;
            var frame = new ImageFrame(texture, new Vector2Int(image.Width, image.Height), format, snapshot.FrameNumber, new OutputLeaseId(image.LeaseId));
            return _history.Commit(frame, snapshot.FrameNumber);
        }
        internal Result Reset(NodeInstanceId nodeId) => nodeId == NodeId ? _history.Reset(_history.LastCommitFrame == 0 ? 1 : _history.LastCommitFrame + 1) : Failure("rendering.feedback.owner", "Feedback reset owner does not match.");
        public void Dispose() { if (_disposed) return; _disposed = true; _history.Dispose(); _lastFrame = null; State = RuntimeNodeState.Disposed; }
        private bool TryDemand(NodeExecutionContext context, out RuntimeOutputResolutionDemand demand)
        {
            demand = null;
            return RuntimeOutputResolutionDemandAccess.TryGet(context, NodeId, new PortId("image"), out demand);
        }
        private void WriteLast(NodeExecutionContext context, NodeOutputWriter outputs, PortId image, Diagnostic diagnostic) { if (_lastFrame != null) { outputs.SetAvailable(image, PortValue.FromImageFrame(_lastFrame)); context.Diagnostics.Report(diagnostic); } else outputs.SetPreparing(image, diagnostic); }
        private Diagnostic Failure(string code, string message, NodeExecutionContext context = null) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: NodeId, nodeTypeId: TypeId, generationId: GenerationId, module: "rendering");
        private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "rendering"));
    }

    internal sealed class SurfaceRuntimeOutputSurface : IRuntimeOutputSurface
    {
        private readonly BorrowedOutputSurface _borrowed;
        public NodeInstanceId NodeId { get; }
        public PortId PortId { get; }
        public int Width => _borrowed.Size.x;
        public int Height => _borrowed.Size.y;
        public ulong LeaseId => _borrowed.LeaseId.Value;
        public ulong FrameNumber => _borrowed.Frame.FrameNumber;
        public object NativeSurface => _borrowed.Texture;
        internal SurfaceRuntimeOutputSurface(NodeInstanceId nodeId, PortId portId, BorrowedOutputSurface borrowed) { NodeId = nodeId; PortId = portId; _borrowed = borrowed; }
    }
}
