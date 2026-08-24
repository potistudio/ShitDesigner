using System;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Scene-owned production binding. Prefabs are resolved by an
	/// injected catalog callback so Scene never reads Project persistence or
	/// a global Resources path.</summary>
	public sealed class SceneVisualNodeBinding : IRuntimeVisualNodeBinding {
		private readonly SceneIsolationManager _manager;
		private readonly Func<RuntimeNodeCreateInfo, GameObject> _prefabResolver;
		private readonly Action<SceneNodeRuntime, FrameSnapshot> _applyEffectiveParameters;
		public NodeTypeId TypeId { get; }
		public SceneNodeKind Kind { get; }
		public bool IsAvailable => _manager != null && _prefabResolver != null;
		public Diagnostic AvailabilityDiagnostic => IsAvailable ? null : new Diagnostic(new DiagnosticCode("scene.binding_missing"), Severity.Error, "Scene production binding requires an isolation manager and explicit prefab resolver.", nodeTypeId: TypeId, module: "scene");

		public SceneVisualNodeBinding(NodeTypeId typeId, SceneNodeKind kind, SceneIsolationManager manager,
			Func<RuntimeNodeCreateInfo, GameObject> prefabResolver,
			Action<SceneNodeRuntime, FrameSnapshot> applyEffectiveParameters = null) {
			if (typeId.IsEmpty || manager == null || prefabResolver == null) throw new ArgumentException("Scene production binding requires explicit dependencies.");
			TypeId = typeId; Kind = kind; _manager = manager; _prefabResolver = prefabResolver; _applyEffectiveParameters = applyEffectiveParameters;
		}

		public Result<IRuntimeNode> Create(RuntimeNodeCreateInfo node, ulong generationId) {
			if (!IsAvailable) return Result<IRuntimeNode>.Failure(AvailabilityDiagnostic);
			if (node == null || node.TypeId != TypeId || generationId == 0) return FailureNode("scene.factory.node", "Scene factory input does not match its binding.", node, generationId);
			GameObject prefab;
			try { prefab = _prefabResolver(node); }
			catch (Exception exception) { return FailureNode("scene.prefab.resolve", exception.Message, node, generationId, exception); }
			if (prefab == null) return FailureNode("scene.prefab.missing", "Scene node requires an explicit prefab/camera binding.", node, generationId);
			var created = _manager.Create(new SceneCreateRequest(node.Id, Kind, "ShitDesigner." + TypeId.Value + "." + node.Id.Value, generationId, prefab));
			if (created.IsFailure) return Result<IRuntimeNode>.Failure(created.Diagnostic);
			return Result<IRuntimeNode>.Success(new SceneRuntimeNode(node, generationId, created.Value, _applyEffectiveParameters));
		}

		private Result<IRuntimeNode> FailureNode(string code, string message, RuntimeNodeCreateInfo node, ulong generationId, Exception exception = null) =>
			Result<IRuntimeNode>.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: node?.Id ?? default(NodeInstanceId), nodeTypeId: TypeId, generationId: generationId, module: "scene", exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception)));
	}

	public sealed class SceneRuntimeNode : IRuntimeNode, IRuntimeDemandAwareNode {
		private readonly RuntimeNodeCreateInfo _record;
		private readonly SceneNodeRuntime _scene;
		private readonly Action<SceneNodeRuntime, FrameSnapshot> _applyEffectiveParameters;
		private IRuntimeImageFrame _lastFrame;
		private double _lastClock;
		private bool _disposed;
		public NodeInstanceId NodeId => _record.Id;
		public NodeTypeId TypeId => _record.TypeId;
		public ulong GenerationId { get; }
		public RuntimeNodeState State { get; private set; } = RuntimeNodeState.Preparing;
		public SceneNodeRuntime Scene => _scene;

		internal SceneRuntimeNode(RuntimeNodeCreateInfo record, ulong generationId, SceneNodeRuntime scene, Action<SceneNodeRuntime, FrameSnapshot> applyEffectiveParameters) { _record = record ?? throw new ArgumentNullException(nameof(record)); GenerationId = generationId; _scene = scene ?? throw new ArgumentNullException(nameof(scene)); _applyEffectiveParameters = applyEffectiveParameters; _lastClock = 0d; }
		public void OnDemandChanged(bool demanded, FrameEvaluationContext context) { if (context != null) _lastClock = context.Snapshot.GraphClockTime; }
		public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs) {
			var image = new PortId("image");
			if (!context.RequestedOutputs.Contains(image)) return;
			if (_disposed) { outputs.SetFaulted(image, Failure("scene.node.disposed", "Scene node is disposed.", context)); return; }
			if (!RuntimeOutputResolutionDemandAccess.TryGet(context, NodeId, image, out var demand)) {
				outputs.SetPreparing(image, Failure("scene.demand_missing", "Scene output has no propagated Phase-4 resolution demand.", context));
				State = RuntimeNodeState.Preparing;
				return;
			}
			var width = demand.Width; var height = demand.Height;
			var surface = context.OutputSurfaces?.TryGetPrepared(NodeId, image, width, height, context.Snapshot.FrameNumber);
			if (!surface.HasValue || surface.Value.IsFailure || surface.Value.Value == null) {
				WriteLast(context, outputs, image, surface.HasValue && surface.Value.IsFailure ? surface.Value.Diagnostic : Failure("scene.surface_missing", "Scene output requires a prepared Phase-5 surface.", context));
				State = RuntimeNodeState.Faulted; return;
			}
			var prepared = surface.Value.Value;
			try {
				_applyEffectiveParameters?.Invoke(_scene, context.Snapshot);
				var delta = Math.Max(0d, context.Snapshot.GraphClockTime - _lastClock);
				var physics = _scene.AdvancePhysics(delta);
				if (physics.IsFailure) throw new InvalidOperationException(physics.Diagnostic.Message);
				_lastClock = context.Snapshot.GraphClockTime;
				var rendered = _scene.Render(prepared.NativeSurface, width, height, context.Snapshot.FrameNumber);
				if (rendered.IsFailure || rendered.Value == null || !rendered.Value.Rendered) throw new InvalidOperationException(rendered.IsFailure ? rendered.Diagnostic.Message : "Scene render source did not render.");
				if (prepared is IRuntimeOutputSurfaceCompletion completion) completion.MarkRendered();
				var frame = new SceneRuntimeImageFrame(prepared, context.Snapshot.FrameNumber);
				_lastFrame = frame; State = RuntimeNodeState.Ready; outputs.SetAvailable(image, PortValue.FromImageFrame(frame));
			}
			catch (Exception exception) {
				State = RuntimeNodeState.Faulted;
				WriteLast(context, outputs, image, Failure("scene.render_failed", exception.Message, context, exception));
			}
		}
		public void Dispose() { if (_disposed) return; _disposed = true; _scene.Dispose(); _lastFrame = null; State = RuntimeNodeState.Disposed; }
		private void WriteLast(NodeExecutionContext context, NodeOutputWriter outputs, PortId image, Diagnostic diagnostic) { if (_lastFrame != null) { outputs.SetAvailable(image, PortValue.FromImageFrame(_lastFrame)); context.Diagnostics.Report(diagnostic); } else outputs.SetPreparing(image, diagnostic); }
		private Diagnostic Failure(string code, string message, NodeExecutionContext context, Exception exception = null) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: NodeId, nodeTypeId: TypeId, generationId: GenerationId, frameNumber: context == null ? 0 : unchecked((long)context.Snapshot.FrameNumber), module: "scene", exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception));
	}

	public sealed class SceneRuntimeImageFrame : IRuntimeImageFrameSurface {
		private readonly IRuntimeOutputSurface _surface;
		public int Width => _surface.Width;
		public int Height => _surface.Height;
		public string ColorFormat => (_surface as IRuntimeOutputSurfaceFormat)?.ColorFormat ?? "R16G16B16A16_SFloat";
		public ulong FrameNumber { get; }
		public ulong LeaseId => _surface.LeaseId;
		public object NativeSurface => _surface.NativeSurface;
		public SceneRuntimeImageFrame(IRuntimeOutputSurface surface, ulong frameNumber) { _surface = surface ?? throw new ArgumentNullException(nameof(surface)); if (surface.LeaseId == 0 || frameNumber == 0) throw new ArgumentException("A live output surface and frame are required."); FrameNumber = frameNumber; }
	}
}
