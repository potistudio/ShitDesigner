using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Project;

namespace ShitDesigner.Runtime {
	public static class InstantEffectTriggerContract {
		public const string NodeTypeId = "shitdesigner.input.instant_effect_triggers";
		public const int TriggerCount = 16;

		public static string PortId(int triggerNumber) => "trigger_" + Validate(triggerNumber);

		public static int Validate(int triggerNumber) {
			if (triggerNumber < 1 || triggerNumber > TriggerCount) throw new ArgumentOutOfRangeException(nameof(triggerNumber));
			return triggerNumber;
		}
	}

	/// <summary>Process-wide interaction mode for the global instant-effect keys.</summary>
	public static class InstantEffectInputMode {
		public static bool IsEditing { get; private set; }

		public static void SetEditing(bool isEditing) => IsEditing = isEditing;
	}

	/// <summary>Optional node-local scalar health seam. Runtime aggregates
	/// without depending on Media or other concrete node modules.</summary>
	public interface IRuntimePerformanceHealthNode {
		bool HasActiveBackend { get; }
		bool HasNativeContext { get; }
	}

	/// <summary>Project-neutral port metadata consumed by built-in node factories.</summary>
	public enum RuntimePortDirection { Input, Output }
	public enum RuntimePortType { ImageFrame, Float, Int, Bool, Vector2, Vector3, Vector4, Color }
	public enum RuntimeDefaultImageKind { TransparentBlack, OpaqueBlack, OpaqueWhite }
	public enum RuntimeDynamicRange { Hdr, Ldr }

	public interface IRuntimeOutputFormatPolicy {
		RuntimeDynamicRange DynamicRange { get; }
		string ColorFormat { get; }
	}

	public sealed class RuntimeOutputFormatPolicy : IRuntimeOutputFormatPolicy {
		public RuntimeDynamicRange DynamicRange { get; }
		public string ColorFormat => DynamicRange == RuntimeDynamicRange.Hdr ? "R16G16B16A16_SFloat" : "R8G8B8A8_UNorm";
		public RuntimeOutputFormatPolicy(RuntimeDynamicRange dynamicRange = RuntimeDynamicRange.Hdr) { DynamicRange = dynamicRange; }
	}

	/// <summary>Runtime-neutral view of Graph's propagated resolution DTO.
	/// Rendering/Scene/Media consume this view through the helper below and
	/// therefore do not reference the Graph assembly directly.</summary>
	public sealed class RuntimeOutputResolutionDemand {
		public int Width { get; }
		public int Height { get; }
		public double AspectRatio { get; }
		public RuntimeOutputResolutionDemand(int width, int height, double aspectRatio) { Width = width; Height = height; AspectRatio = aspectRatio; }
	}

	/// <summary>One entry from the graph-owned resolution map projected into
	/// the Runtime boundary.  Rendering, Scene and Media must consume this
	/// DTO instead of touching EvaluationPlan (and therefore Graph types).</summary>
	public sealed class RuntimeOutputResolutionEntry {
		public NodeInstanceId NodeId { get; }
		public PortId PortId { get; }
		public RuntimePortDirection Direction { get; }
		public RuntimePortType Type { get; }
		public RuntimeOutputResolutionDemand Demand { get; }
		public RuntimeOutputResolutionEntry(NodeInstanceId nodeId, PortId portId, RuntimeOutputResolutionDemand demand,
			RuntimePortDirection direction = RuntimePortDirection.Output, RuntimePortType type = RuntimePortType.ImageFrame) {
			if (nodeId.IsEmpty || portId.IsEmpty || demand == null) throw new ArgumentException("A resolution entry requires node, port and demand.");
			NodeId = nodeId; PortId = portId; Direction = direction; Type = type; Demand = demand;
		}
	}

	public static class RuntimeOutputResolutionDemandAccess {
		private static readonly IReadOnlyList<RuntimeOutputResolutionEntry> EmptyEntries =
			new ReadOnlyCollection<RuntimeOutputResolutionEntry>(new List<RuntimeOutputResolutionEntry>());

		public static bool TryGet(FrameEvaluationContext context, NodeInstanceId nodeId, PortId portId, out RuntimeOutputResolutionDemand demand)
			=> TryGet(context?.ResolutionProjection, nodeId, portId, out demand);

		public static bool TryGet(FrameSnapshot snapshot, NodeInstanceId nodeId, PortId portId, out RuntimeOutputResolutionDemand demand)
			=> TryGet(snapshot?.ResolutionProjection, nodeId, portId, out demand);

		public static bool TryGet(NodeExecutionContext context, NodeInstanceId nodeId, PortId portId, out RuntimeOutputResolutionDemand demand)
			=> TryGet(context?.ResolutionProjection, nodeId, portId, out demand);

		public static IReadOnlyList<RuntimeOutputResolutionEntry> GetAll(FrameEvaluationContext context)
			=> GetAll(context?.ResolutionProjection);

		public static IReadOnlyList<RuntimeOutputResolutionEntry> GetAll(FrameSnapshot snapshot)
			=> GetAll(snapshot?.ResolutionProjection);

		/// <summary>Returns only real ImageFrame output ports. The graph's
		/// resolution map also contains terminal input ports (Program/Preview
		/// image), which must never acquire an output lease.</summary>
		public static IReadOnlyList<RuntimeOutputResolutionEntry> GetVisualOutputs(RuntimeSession session, FrameEvaluationContext context)
			=> GetVisualOutputs(context?.ResolutionProjection);

		public static IReadOnlyList<RuntimeOutputResolutionEntry> GetVisualOutputs(RuntimeSession session, FrameSnapshot snapshot)
			=> GetVisualOutputs(snapshot?.ResolutionProjection);

		/// <summary>
		/// Returns the current plan projection when a caller is preparing a
		/// resource outside the normal FrameEvaluationContext path. A
		/// FrameSnapshot is intentionally immutable and may contain the plan
		/// from before a graph transaction; resource preparation must follow
		/// the session's newly installed plan in that case.
		/// </summary>
		public static IReadOnlyList<RuntimeOutputResolutionEntry> GetVisualOutputs(RuntimeSession session)
			=> GetVisualOutputs(session?.ResolutionProjection);

		private static bool TryGet(RuntimeOutputResolutionProjection projection, NodeInstanceId nodeId, PortId portId, out RuntimeOutputResolutionDemand demand) {
			demand = null;
			return projection != null && projection.TryGet(nodeId, portId, out demand);
		}

		private static IReadOnlyList<RuntimeOutputResolutionEntry> GetAll(RuntimeOutputResolutionProjection projection) {
			return projection?.Entries ?? EmptyEntries;
		}

		private static IReadOnlyList<RuntimeOutputResolutionEntry> GetVisualOutputs(RuntimeOutputResolutionProjection projection)
			=> projection?.VisualEntries ?? EmptyEntries;
	}

	/// <summary>Graph-free immutable demand projection carried by public
	/// Runtime contexts. Its construction remains inside Runtime so consumer
	/// asmdefs never need a Graph assembly reference.</summary>
	internal sealed class RuntimeOutputResolutionProjection {
		private readonly Dictionary<ResolutionKey, RuntimeOutputResolutionDemand> _demands;
		internal IReadOnlyList<RuntimeOutputResolutionEntry> Entries { get; }
		internal IReadOnlyList<RuntimeOutputResolutionEntry> VisualEntries { get; }

		internal RuntimeOutputResolutionProjection(IEnumerable<RuntimeOutputResolutionEntry> entries, IEnumerable<RuntimeOutputResolutionEntry> visualEntries) {
			_demands = new Dictionary<ResolutionKey, RuntimeOutputResolutionDemand>();
			var all = new List<RuntimeOutputResolutionEntry>(entries ?? Enumerable.Empty<RuntimeOutputResolutionEntry>());
			foreach (var entry in all) _demands[new ResolutionKey(entry.NodeId, entry.PortId)] = entry.Demand;
			Entries = new ReadOnlyCollection<RuntimeOutputResolutionEntry>(all);
			VisualEntries = new ReadOnlyCollection<RuntimeOutputResolutionEntry>(new List<RuntimeOutputResolutionEntry>(visualEntries ?? Enumerable.Empty<RuntimeOutputResolutionEntry>()));
		}

		internal bool TryGet(NodeInstanceId nodeId, PortId portId, out RuntimeOutputResolutionDemand demand)
			=> _demands.TryGetValue(new ResolutionKey(nodeId, portId), out demand);
	}

	internal readonly struct ResolutionKey : IEquatable<ResolutionKey> {
		private readonly NodeInstanceId _nodeId;
		private readonly PortId _portId;
		public ResolutionKey(NodeInstanceId nodeId, PortId portId) { _nodeId = nodeId; _portId = portId; }
		public bool Equals(ResolutionKey other) => _nodeId == other._nodeId && _portId == other._portId;
		public override bool Equals(object obj) => obj is ResolutionKey other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(_nodeId, _portId);
	}

	public sealed class RuntimePortSnapshot {
		public PortId Id { get; }
		public string DisplayName { get; }
		public RuntimePortDirection Direction { get; }
		public RuntimePortType Type { get; }
		public bool Required { get; }
		public RuntimeDefaultImageKind? DefaultImage { get; }
		public RuntimePortSnapshot(PortId id, string displayName, RuntimePortDirection direction, RuntimePortType type, bool required, RuntimeDefaultImageKind? defaultImage = null) {
			if (id.IsEmpty || string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Runtime port identity is required.");
			if (defaultImage.HasValue && (direction != RuntimePortDirection.Input || required || type != RuntimePortType.ImageFrame)) throw new ArgumentException("Default image requires an optional ImageFrame input.");
			Id = id; DisplayName = displayName.Trim(); Direction = direction; Type = type; Required = required; DefaultImage = defaultImage;
		}
	}

	public sealed class RuntimeParameterSnapshot {
		public ParameterId Id { get; }
		public ParameterType Type { get; }
		public ParameterValue Value { get; }
		public bool RuntimeStateful { get; }
		public RuntimeParameterSnapshot(ParameterId id, ParameterType type, ParameterValue value, bool runtimeStateful = false) {
			if (id.IsEmpty || value.Type != type) throw new ArgumentException("Runtime parameter identity or type is invalid.");
			Id = id; Type = type; Value = value; RuntimeStateful = runtimeStateful;
		}
	}

	/// <summary>Immutable Project-to-Runtime handoff; Nodes never sees Project.NodeRecord.</summary>
	public sealed class RuntimeNodeCreateInfo {
		public NodeInstanceId Id { get; }
		public NodeTypeId TypeId { get; }
		public int SchemaVersion { get; }
		public string DisplayName { get; }
		public bool Enabled { get; }
		public float PositionX { get; }
		public float PositionY { get; }
		public IReadOnlyList<RuntimeParameterSnapshot> Parameters { get; }
		public IReadOnlyList<RuntimePortSnapshot> Ports { get; }
		public string RawState { get; }
		public bool SystemOwned { get; }
		public bool UserAddable { get; }

		public RuntimeNodeCreateInfo(NodeInstanceId id, NodeTypeId typeId, int schemaVersion, string displayName, bool enabled, float positionX, float positionY, IEnumerable<RuntimeParameterSnapshot> parameters = null, IEnumerable<RuntimePortSnapshot> ports = null, string rawState = "{}", bool systemOwned = false, bool userAddable = true) {
			if (id.IsEmpty || typeId.IsEmpty || schemaVersion < 1 || string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Runtime node identity is invalid.");
			Id = id; TypeId = typeId; SchemaVersion = schemaVersion; DisplayName = displayName.Trim(); Enabled = enabled; PositionX = positionX; PositionY = positionY;
			Parameters = new ReadOnlyCollection<RuntimeParameterSnapshot>((parameters ?? Enumerable.Empty<RuntimeParameterSnapshot>()).ToList());
			Ports = new ReadOnlyCollection<RuntimePortSnapshot>((ports ?? Enumerable.Empty<RuntimePortSnapshot>()).ToList());
			if (Parameters.Any(x => x == null) || Ports.Any(x => x == null) || Parameters.GroupBy(x => x.Id).Any(x => x.Count() > 1) || Ports.GroupBy(x => x.Id).Any(x => x.Count() > 1)) throw new ArgumentException("Runtime node members must be unique.");
			RawState = rawState ?? "{}"; SystemOwned = systemOwned; UserAddable = userAddable;
		}

		internal static RuntimeNodeCreateInfo FromProject(NodeRecord node) {
			if (node == null) throw new ArgumentNullException(nameof(node));
			return new RuntimeNodeCreateInfo(node.Id, node.TypeId, node.SchemaVersion, node.DisplayName, node.Enabled, node.Position.X, node.Position.Y,
				node.Parameters.Select(x => new RuntimeParameterSnapshot(x.Definition.Id, x.Definition.Type, x.BaseValue, x.Definition.RuntimeStateful)),
				node.Ports.Select(x => new RuntimePortSnapshot(x.Id, x.Id.Value, x.Direction == PortDirection.Input ? RuntimePortDirection.Input : RuntimePortDirection.Output, ToRuntimeType(x.Type), x.Required, x.DefaultImage.HasValue ? ToRuntimeDefaultImage(x.DefaultImage.Value) : null)),
				node.RawState, node.SystemOwned, node.UserAddable);
		}

		private static RuntimePortType ToRuntimeType(PortType type) => (RuntimePortType)Enum.Parse(typeof(RuntimePortType), type.ToString(), true);
		private static RuntimeDefaultImageKind ToRuntimeDefaultImage(DefaultImageKind kind) => kind == DefaultImageKind.OpaqueWhite ? RuntimeDefaultImageKind.OpaqueWhite : kind == DefaultImageKind.OpaqueBlack ? RuntimeDefaultImageKind.OpaqueBlack : RuntimeDefaultImageKind.TransparentBlack;
	}

	/// <summary>
	/// Runtime-side read-only boundary for an ImageFrame. Rendering owns the
	/// concrete texture and lease; Runtime only carries the validated value.
	/// </summary>
	public interface IRuntimeImageFrame {
		int Width { get; }
		int Height { get; }
		string ColorFormat { get; }
		ulong FrameNumber { get; }
		ulong LeaseId { get; }
	}

	/// <summary>Optional Unity/resource view. Runtime graph code only needs
	/// the metadata above; Rendering/Media/Scene adapters may implement this
	/// extension when a Phase-9 copy or GPU handoff is required.</summary>
	public interface IRuntimeImageFrameSurface : IRuntimeImageFrame {
		object NativeSurface { get; }
	}

	public enum NodeOutputStatus {
		Available,
		Blocked,
		Faulted,
		Preparing
	}

	public enum RuntimeNodeState {
		Creating,
		Preparing,
		Ready,
		Faulted,
		Retiring,
		Disposed
	}

	public enum RuntimePhase {
		BoundaryIntake = 0,
		GraphEdit = 1,
		ParameterAndControlCommit = 2,
		FrameSnapshot = 3,
		OutputDemand = 4,
		ResourcePreparation = 5,
		NodeEvaluation = 6,
		FeedbackCommit = 7,
		Presentation = 8,
		Finalization = 9
	}

	public enum InputResolutionStatus {
		Available,
		UsingFallback,
		Unavailable,
		Faulted
	}

	public readonly struct PortValue : IEquatable<PortValue> {
		private readonly PortType _type;
		private readonly ParameterValue _value;
		private readonly IRuntimeImageFrame _image;
		private readonly bool _hasValue;

		public PortType Type => _type;
		public bool HasValue => _hasValue;
		public bool IsImageFrame => _hasValue && _type == PortType.ImageFrame;

		private PortValue(PortType type, ParameterValue value, IRuntimeImageFrame image) {
			_type = type;
			_value = value;
			_image = image;
			_hasValue = true;
		}

		public static PortValue FromImageFrame(IRuntimeImageFrame image) {
			if (image == null) throw new ArgumentNullException(nameof(image));
			if (image.Width < 1 || image.Height < 1) throw new ArgumentOutOfRangeException(nameof(image));
			if (string.IsNullOrWhiteSpace(image.ColorFormat)) throw new ArgumentException("Image format is required.", nameof(image));
			if (image.FrameNumber == 0) throw new ArgumentException("ImageFrame must belong to a positive runtime frame.", nameof(image));
			if (image.LeaseId == 0) throw new ArgumentException("ImageFrame must reference a live output lease.", nameof(image));
			return new PortValue(PortType.ImageFrame, default(ParameterValue), image);
		}

		public static PortValue FromFloat(float value) => new PortValue(PortType.Float, ParameterValue.FromFloat(value), null);
		public static PortValue FromInt(int value) => new PortValue(PortType.Int, ParameterValue.FromInt(value), null);
		public static PortValue FromBool(bool value) => new PortValue(PortType.Bool, ParameterValue.FromBool(value), null);
		public static PortValue FromVector2(Vector2Value value) => new PortValue(PortType.Vector2, ParameterValue.FromVector2(value), null);
		public static PortValue FromVector3(Vector3Value value) => new PortValue(PortType.Vector3, ParameterValue.FromVector3(value), null);
		public static PortValue FromVector4(Vector4Value value) => new PortValue(PortType.Vector4, ParameterValue.FromVector4(value), null);
		public static PortValue FromColor(ColorValue value) => new PortValue(PortType.Color, ParameterValue.FromColor(value), null);

		public IRuntimeImageFrame AsImageFrame() => IsImageFrame ? _image : throw TypeError(PortType.ImageFrame);
		public float AsFloat() => GetParameter(PortType.Float, ParameterType.Float).AsFloat();
		public int AsInt() => GetParameter(PortType.Int, ParameterType.Int).AsInt();
		public bool AsBool() => GetParameter(PortType.Bool, ParameterType.Bool).AsBool();
		public Vector2Value AsVector2() => GetParameter(PortType.Vector2, ParameterType.Vector2).AsVector2();
		public Vector3Value AsVector3() => GetParameter(PortType.Vector3, ParameterType.Vector3).AsVector3();
		public Vector4Value AsVector4() => GetParameter(PortType.Vector4, ParameterType.Vector4).AsVector4();
		public ColorValue AsColor() => GetParameter(PortType.Color, ParameterType.Color).AsColor();

		public ParameterValue AsParameterValue() {
			if (!_hasValue || _type == PortType.ImageFrame) throw TypeError(_type);
			return _value;
		}

		public static PortValue FromParameterValue(ParameterValue value) {
			switch (value.Type) {
				case ParameterType.Float: return FromFloat(value.AsFloat());
				case ParameterType.Int: return FromInt(value.AsInt());
				case ParameterType.Bool: return FromBool(value.AsBool());
				case ParameterType.Vector2: return FromVector2(value.AsVector2());
				case ParameterType.Vector3: return FromVector3(value.AsVector3());
				case ParameterType.Vector4: return FromVector4(value.AsVector4());
				case ParameterType.Color: return FromColor(value.AsColor());
				default: throw new ArgumentException("Only initial port value types can be used.", nameof(value));
			}
		}

		public static PortValue Default(PortType type) {
			switch (type) {
				case PortType.Float: return FromFloat(0f);
				case PortType.Int: return FromInt(0);
				case PortType.Bool: return FromBool(false);
				case PortType.Vector2: return FromVector2(new Vector2Value(0f, 0f));
				case PortType.Vector3: return FromVector3(new Vector3Value(0f, 0f, 0f));
				case PortType.Vector4: return FromVector4(new Vector4Value(0f, 0f, 0f, 0f));
				case PortType.Color: return FromColor(new ColorValue(0f, 0f, 0f, 0f));
				default: throw new ArgumentException("ImageFrame has no dummy default value.", nameof(type));
			}
		}

		private ParameterValue GetParameter(PortType expectedPort, ParameterType expectedValue) {
			// PortType and ParameterType are intentionally separate enums. Do not
			// rely on their underlying numeric values remaining aligned.
			if (!_hasValue || _type != expectedPort)
				throw TypeError(expectedPort);
			if (_value.Type != expectedValue)
				throw TypeError(expectedPort);
			return _value;
		}

		private InvalidOperationException TypeError(PortType expected) => new InvalidOperationException("PortValue is " + _type + "; expected " + expected + ".");
		public bool Equals(PortValue other) => _type == other._type && _hasValue == other._hasValue && (_type == PortType.ImageFrame ? Equals(_image, other._image) : _value == other._value);
		public override bool Equals(object obj) => obj is PortValue && Equals((PortValue)obj);
		public override int GetHashCode() => HashCode.Combine(_type, _hasValue, _type == PortType.ImageFrame ? (_image == null ? 0 : _image.GetHashCode()) : _value.GetHashCode());
		public static bool operator ==(PortValue left, PortValue right) => left.Equals(right);
		public static bool operator !=(PortValue left, PortValue right) => !left.Equals(right);
	}

	public readonly struct ResolvedInput {
		public PortId PortId { get; }
		public PortType ExpectedType { get; }
		public InputResolutionStatus Status { get; }
		public bool HasValue { get; }
		public PortValue Value { get; }
		public Diagnostic Diagnostic { get; }

		private ResolvedInput(PortId portId, PortType expectedType, InputResolutionStatus status, bool hasValue, PortValue value, Diagnostic diagnostic) {
			PortId = portId; ExpectedType = expectedType; Status = status; HasValue = hasValue; Value = value; Diagnostic = diagnostic;
		}

		public static ResolvedInput Available(PortId id, PortType type, PortValue value) => new ResolvedInput(id, type, InputResolutionStatus.Available, true, value, null);
		public static ResolvedInput Fallback(PortId id, PortType type, PortValue value, Diagnostic diagnostic = null) => new ResolvedInput(id, type, InputResolutionStatus.UsingFallback, true, value, diagnostic);
		public static ResolvedInput Unavailable(PortId id, PortType type, Diagnostic diagnostic) => new ResolvedInput(id, type, InputResolutionStatus.Unavailable, false, default(PortValue), diagnostic);
		public static ResolvedInput Faulted(PortId id, PortType type, Diagnostic diagnostic) => new ResolvedInput(id, type, InputResolutionStatus.Faulted, false, default(PortValue), diagnostic);
	}

	public readonly struct NodeOutputResult {
		public NodeOutputStatus Status { get; }
		public bool HasValue { get; }
		public PortValue Value { get; }
		public Diagnostic Diagnostic { get; }
		public bool IsAvailable => Status == NodeOutputStatus.Available;

		private NodeOutputResult(NodeOutputStatus status, bool hasValue, PortValue value, Diagnostic diagnostic) {
			Status = status; HasValue = hasValue; Value = value; Diagnostic = diagnostic;
		}

		public static NodeOutputResult Available(PortValue value) => new NodeOutputResult(NodeOutputStatus.Available, true, value, null);
		public static NodeOutputResult Blocked(Diagnostic diagnostic) => Failure(NodeOutputStatus.Blocked, diagnostic);
		public static NodeOutputResult Faulted(Diagnostic diagnostic) => Failure(NodeOutputStatus.Faulted, diagnostic);
		public static NodeOutputResult Preparing(Diagnostic diagnostic) => Failure(NodeOutputStatus.Preparing, diagnostic);
		private static NodeOutputResult Failure(NodeOutputStatus status, Diagnostic diagnostic) => new NodeOutputResult(status, false, default(PortValue), diagnostic ?? throw new ArgumentNullException(nameof(diagnostic)));
	}

	public sealed class NodeOutputWriter {
		private readonly IReadOnlyList<PortId> _requested;
		private readonly HashSet<PortId> _requestedSet;
		private readonly Dictionary<PortId, NodeOutputResult> _outputs = new Dictionary<PortId, NodeOutputResult>();
		private IReadOnlyDictionary<PortId, NodeOutputResult> _sealedOutputs;
		private bool _sealed;
		public IReadOnlyDictionary<PortId, NodeOutputResult> Outputs => _sealedOutputs ?? new ReadOnlyDictionary<PortId, NodeOutputResult>(_outputs);

		public NodeOutputWriter(IEnumerable<PortId> requestedOutputs) {
			var requested = new List<PortId>(requestedOutputs ?? Enumerable.Empty<PortId>());
			_requested = new ReadOnlyCollection<PortId>(requested);
			_requestedSet = new HashSet<PortId>(requested);
		}

		/// <summary>FrameCoordinator owns the immutable plan list. This
		/// narrow constructor avoids cloning it into a per-node HashSet.</summary>
		internal NodeOutputWriter(IReadOnlyList<PortId> requestedOutputs, bool trustedFrozenRequestedOutputs) {
			_requested = requestedOutputs ?? new ReadOnlyCollection<PortId>(new List<PortId>());
			_requestedSet = null;
		}

		public UnitResult<Diagnostic> Set(PortId portId, NodeOutputResult result) {
			if (_sealed) return UnitResult.Failure<Diagnostic>(Failure("runtime.output.writer_sealed", "Output writer is already sealed.", portId));
			if (portId.IsEmpty || !IsRequested(portId)) return UnitResult.Failure<Diagnostic>(Failure("runtime.output.port_not_requested", "Output port was not requested.", portId));
			if (_outputs.ContainsKey(portId)) return UnitResult.Failure<Diagnostic>(Failure("runtime.output.duplicate", "Output port was set more than once.", portId));
			_outputs.Add(portId, result);
			return UnitResult.Success<Diagnostic>();
		}

		public UnitResult<Diagnostic> SetAvailable(PortId portId, PortValue value) => Set(portId, NodeOutputResult.Available(value));
		public UnitResult<Diagnostic> SetBlocked(PortId portId, Diagnostic diagnostic) => Set(portId, NodeOutputResult.Blocked(diagnostic));
		public UnitResult<Diagnostic> SetFaulted(PortId portId, Diagnostic diagnostic) => Set(portId, NodeOutputResult.Faulted(diagnostic));
		public UnitResult<Diagnostic> SetPreparing(PortId portId, Diagnostic diagnostic) => Set(portId, NodeOutputResult.Preparing(diagnostic));

		internal IReadOnlyDictionary<PortId, NodeOutputResult> Seal() {
			if (_sealed) return _sealedOutputs;
			_sealed = true;
			foreach (var requested in _requested)
				if (!_outputs.ContainsKey(requested)) _outputs[requested] = NodeOutputResult.Faulted(Failure("runtime.output.not_set", "Runtime node did not set a requested output.", requested));
			_sealedOutputs = new ReadOnlyDictionary<PortId, NodeOutputResult>(_outputs);
			return _sealedOutputs;
		}

		private bool IsRequested(PortId portId) {
			if (_requestedSet != null) return _requestedSet.Contains(portId);
			for (var index = 0; index < _requested.Count; index++)
				if (_requested[index] == portId) return true;
			return false;
		}

		private static Diagnostic Failure(string code, string message, PortId portId) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, portId: portId);
	}

	public interface IRuntimeDiagnosticSink {
		void Report(Diagnostic diagnostic);
	}

	/// <summary>
	/// Persistence is deliberately injected at the Runtime boundary. The
	/// default ProjectDocument implementation lives in RuntimeSession, while
	/// hosts can provide a transaction adapter without introducing a module
	/// dependency in the opposite direction.
	/// </summary>
	public interface IRuntimeProjectMutationPort {
		UnitResult<Diagnostic> ApplyGraphPatch(GraphPatch patch);
		UnitResult<Diagnostic> ApplyParameterTransaction(IReadOnlyList<BaseValueUpdate> updates);
	}

	/// <summary>Creates the shared read-only ImageFrame used by optional Image inputs.</summary>
	public interface IRuntimeDefaultImageProvider {
		Result<PortValue, Diagnostic> Get(RuntimeDefaultImageKind kind, int width, int height, ulong frameNumber);
	}

	/// <summary>Phase-5 output surface boundary. Rendering prepares/reuses the
	/// candidate surface before node evaluation; nodes only borrow it and must
	/// never acquire or release pool leases themselves.</summary>
	public interface IRuntimeOutputSurface {
		NodeInstanceId NodeId { get; }
		PortId PortId { get; }
		int Width { get; }
		int Height { get; }
		ulong LeaseId { get; }
		ulong FrameNumber { get; }
		object NativeSurface { get; }
	}

	public interface IRuntimeOutputSurfaceFormat {
		string ColorFormat { get; }
	}

	/// <summary>Optional Phase-6 completion port. A node calls this only
	/// after its actual render/copy succeeds; Phase 9 may then promote that
	/// candidate. Returning a held last frame without marking leaves the
	/// candidate uncommitted.</summary>
	public interface IRuntimeOutputSurfaceCompletion {
		UnitResult<Diagnostic> MarkRendered();
		bool IsRendered { get; }
	}

	public interface IRuntimeOutputSurfacePort {
		Result<IRuntimeOutputSurface, Diagnostic> TryGetPrepared(NodeInstanceId nodeId, PortId portId, int width, int height, ulong frameNumber);
	}

	public interface IRuntimeResourcePreparation {
		UnitResult<Diagnostic> Prepare(FrameSnapshot snapshot);
	}

	/// <summary>Optional Phase-5 extension that receives the Phase-4 demand
	/// plan without changing the immutable state snapshot contract.</summary>
	public interface IRuntimeResourcePreparationWithPlan : IRuntimeResourcePreparation {
		UnitResult<Diagnostic> Prepare(FrameSnapshot snapshot, FrameEvaluationContext evaluation);
	}

	public interface IRuntimeResourceFinalization {
		UnitResult<Diagnostic> Finalize(FrameSnapshot snapshot, bool frameSucceeded);
	}

	/// <summary>Optional Phase-9 extension paired with the Phase-5 plan-aware
	/// preparation contract.</summary>
	public interface IRuntimeResourceFinalizationWithPlan : IRuntimeResourceFinalization {
		UnitResult<Diagnostic> Finalize(FrameSnapshot snapshot, FrameEvaluationContext evaluation, bool frameSucceeded);
	}

	/// <summary>Per-frame plan produced at Phase 4. It is intentionally
	/// separate from FrameSnapshot: demand scheduling may rebuild the plan,
	/// but it must never replace or mutate the Phase-3 snapshot.</summary>
	public sealed class FrameEvaluationContext {
		private static readonly IReadOnlyList<OutputDemand> EmptyOutputDemands = new ReadOnlyCollection<OutputDemand>(new List<OutputDemand>());
		public FrameSnapshot Snapshot { get; }
		// Graph-owned plan is deliberately absent from this public context.
		// Consumer modules receive only the neutral resolution projection.
		internal RuntimeOutputResolutionProjection ResolutionProjection { get; }
		public IReadOnlyList<OutputDemand> OutputDemands { get; }

		internal FrameEvaluationContext(FrameSnapshot snapshot, RuntimeOutputResolutionProjection resolutionProjection, IReadOnlyList<OutputDemand> demands) {
			Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
			ResolutionProjection = resolutionProjection;
			OutputDemands = demands ?? EmptyOutputDemands;
		}
	}

	public sealed class RuntimeCompletion {
		public NodeInstanceId NodeId { get; }
		public ulong GenerationId { get; }
		public long? DocumentRevision { get; }
		public long? GraphRevision { get; }
		public Func<RuntimeSession, UnitResult<Diagnostic>> Apply { get; }
		public Func<RuntimeSession, UnitResult<Diagnostic>> Discard { get; }

		public RuntimeCompletion(NodeInstanceId nodeId, ulong generationId, Func<RuntimeSession, UnitResult<Diagnostic>> apply, Func<RuntimeSession, UnitResult<Diagnostic>> discard = null, long? documentRevision = null, long? graphRevision = null) {
			if (nodeId.IsEmpty || generationId == 0) throw new ArgumentException("Completion owner identity is required.");
			NodeId = nodeId;
			GenerationId = generationId;
			DocumentRevision = documentRevision;
			GraphRevision = graphRevision;
			Apply = apply ?? throw new ArgumentNullException(nameof(apply));
			Discard = discard ?? (_ => UnitResult.Success<Diagnostic>());
		}

		public RuntimeCompletion(NodeInstanceId nodeId, ulong generationId, long documentRevision, Func<RuntimeSession, UnitResult<Diagnostic>> apply, Func<RuntimeSession, UnitResult<Diagnostic>> discard = null)
			: this(nodeId, generationId, apply, discard, documentRevision, null) { }
	}

	public sealed class NodeExecutionContext {
		private static readonly IReadOnlyList<OutputDemand> EmptyOutputDemands = new ReadOnlyCollection<OutputDemand>(new List<OutputDemand>());
		private static readonly IReadOnlyList<PortId> EmptyPortIds = new ReadOnlyCollection<PortId>(new List<PortId>());
		private static readonly IReadOnlyDictionary<PortId, ResolvedInput> EmptyInputs = new ReadOnlyDictionary<PortId, ResolvedInput>(new Dictionary<PortId, ResolvedInput>());
		private readonly IReadOnlyDictionary<PortId, ResolvedInput> _inputs;
		private readonly IReadOnlyList<PortId> _requestedOutputs;
		public FrameSnapshot Snapshot { get; }
		/// <summary>Neutral Phase-4 demand projection paired with the
		/// immutable Phase-3 snapshot. Graph internals never cross this
		/// public node-binding boundary.</summary>
		internal RuntimeOutputResolutionProjection ResolutionProjection { get; }
		public IReadOnlyList<OutputDemand> OutputDemands { get; }
		public NodeInstanceId NodeId { get; }
		public int RuntimeIndex { get; }
		public IReadOnlyDictionary<PortId, ResolvedInput> Inputs => _inputs;
		public IReadOnlyList<PortId> RequestedOutputs => _requestedOutputs;
		public IRuntimeDiagnosticSink Diagnostics { get; }
		public IRuntimeOutputSurfacePort OutputSurfaces { get; }

		internal NodeExecutionContext(FrameSnapshot snapshot, RuntimeOutputResolutionProjection resolutionProjection, IReadOnlyList<OutputDemand> outputDemands, NodeInstanceId nodeId, int runtimeIndex, IReadOnlyList<PortId> requestedOutputs, IReadOnlyDictionary<PortId, ResolvedInput> inputs, IRuntimeDiagnosticSink diagnostics, IRuntimeOutputSurfacePort outputSurfaces = null) {
			Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
			ResolutionProjection = resolutionProjection;
			OutputDemands = outputDemands ?? EmptyOutputDemands;
			NodeId = nodeId; RuntimeIndex = runtimeIndex;
			_requestedOutputs = requestedOutputs ?? EmptyPortIds;
			_inputs = inputs ?? EmptyInputs;
			Diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
			OutputSurfaces = outputSurfaces;
		}
	}

	public interface IRuntimeNode : IDisposable {
		NodeInstanceId NodeId { get; }
		NodeTypeId TypeId { get; }
		ulong GenerationId { get; }
		RuntimeNodeState State { get; }
		void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs);
	}

	/// <summary>Phase-4 demand transition seam. Nodes that own continuous
	/// decoders or clocks receive a transition even when they are omitted
	/// from Phase-6 evaluation for that frame.</summary>
	public interface IRuntimeDemandAwareNode {
		void OnDemandChanged(bool demanded, FrameEvaluationContext context);
	}

	public interface IRuntimeNodeFactory {
		NodeTypeId TypeId { get; }
		Result<IRuntimeNode, Diagnostic> Create(RuntimeNodeCreateInfo node, ulong generationId);
	}

	/// <summary>
	/// Neutral composition boundary for built-in visual nodes.  Scene,
	/// Rendering and Media implement this contract in their own assemblies;
	/// Nodes and Runtime only see the immutable create info and the runtime
	/// node result.  Bootstrap is the only place that is allowed to compose
	/// concrete implementations.
	/// </summary>
	public interface IRuntimeVisualNodeBinding : IRuntimeNodeFactory {
		bool IsAvailable { get; }
		Diagnostic AvailabilityDiagnostic { get; }
	}

	public interface IFeedbackCommitter {
		UnitResult<Diagnostic> Commit(NodeInstanceId nodeId, NodeOutputResult input, FrameSnapshot snapshot);
	}

	/// <summary>Optional boundary command implemented by temporal Feedback nodes.</summary>
	public interface IFeedbackResetter {
		UnitResult<Diagnostic> Reset(NodeInstanceId nodeId);
	}

	public readonly struct OutputPresentation {
		private static readonly IReadOnlyDictionary<NodeInstanceId, NodeOutputResult> EmptyPreviews = new ReadOnlyDictionary<NodeInstanceId, NodeOutputResult>(new Dictionary<NodeInstanceId, NodeOutputResult>());
		public NodeOutputResult Program { get; }
		public IReadOnlyDictionary<NodeInstanceId, NodeOutputResult> Previews { get; }
		internal OutputPresentation(NodeOutputResult program, IReadOnlyDictionary<NodeInstanceId, NodeOutputResult> previews) {
			Program = program;
			Previews = previews ?? EmptyPreviews;
		}
	}

	public sealed class FrameSnapshot {
		private static readonly IReadOnlyList<OutputDemand> EmptyOutputDemands = new ReadOnlyCollection<OutputDemand>(new List<OutputDemand>());
		private static readonly IReadOnlyCollection<int> m_EmptyInstantEffectTriggers = new ReadOnlyCollection<int>(new List<int>());
		private readonly IReadOnlyDictionary<ParameterKey, ParameterValue> _effectiveValues;
		private readonly IReadOnlyDictionary<LogicalControlId, float> _controlValues;
		private readonly IReadOnlyList<OutputDemand> _demands;
		private readonly IReadOnlyCollection<int> m_InstantEffectTriggers;
		public ulong FrameNumber { get; }
		public double GraphClockTime { get; }
		public bool IsGraphClockPaused { get; }
		public long DocumentRevision { get; }
		public long GraphRevision { get; }
		internal RuntimeOutputResolutionProjection ResolutionProjection { get; }
		public IReadOnlyDictionary<ParameterKey, ParameterValue> EffectiveValues => _effectiveValues;
		public IReadOnlyDictionary<LogicalControlId, float> ControlValues => _controlValues;
		public IReadOnlyList<OutputDemand> OutputDemands => _demands;
		public IReadOnlyCollection<int> InstantEffectTriggers => m_InstantEffectTriggers;

		internal FrameSnapshot(ulong frameNumber, double graphClockTime, bool paused, long documentRevision, long graphRevision, RuntimeOutputResolutionProjection resolutionProjection, IDictionary<ParameterKey, ParameterValue> effectiveValues, IDictionary<LogicalControlId, float> controlValues, IEnumerable<OutputDemand> demands, IEnumerable<int> instantEffectTriggers = null) {
			FrameNumber = frameNumber; GraphClockTime = graphClockTime; IsGraphClockPaused = paused; DocumentRevision = documentRevision; GraphRevision = graphRevision; ResolutionProjection = resolutionProjection;
			_effectiveValues = new ReadOnlyDictionary<ParameterKey, ParameterValue>(new Dictionary<ParameterKey, ParameterValue>(effectiveValues ?? new Dictionary<ParameterKey, ParameterValue>()));
			_controlValues = new ReadOnlyDictionary<LogicalControlId, float>(new Dictionary<LogicalControlId, float>(controlValues ?? new Dictionary<LogicalControlId, float>()));
			_demands = new ReadOnlyCollection<OutputDemand>((demands ?? Enumerable.Empty<OutputDemand>()).ToList());
			m_InstantEffectTriggers = SnapshotInstantEffectTriggers(instantEffectTriggers);
		}

		/// <summary>Runtime-internal fast path. ParameterFrameValues are
		/// immutable copy-on-write ParameterStore snapshots; OutputDemands
		/// deliberately remain copied because Phase 4 can rebuild the plan.</summary>
		internal FrameSnapshot(ulong frameNumber, double graphClockTime, bool paused, long documentRevision, long graphRevision, RuntimeOutputResolutionProjection resolutionProjection, ParameterFrameValues parameterValues, IReadOnlyList<OutputDemand> demands, IEnumerable<int> instantEffectTriggers = null) {
			if (parameterValues == null) throw new ArgumentNullException(nameof(parameterValues));
			FrameNumber = frameNumber; GraphClockTime = graphClockTime; IsGraphClockPaused = paused; DocumentRevision = documentRevision; GraphRevision = graphRevision; ResolutionProjection = resolutionProjection;
			_effectiveValues = parameterValues.EffectiveValues;
			_controlValues = parameterValues.ControlValues;
			_demands = demands ?? EmptyOutputDemands;
			m_InstantEffectTriggers = SnapshotInstantEffectTriggers(instantEffectTriggers);
		}

		private static IReadOnlyCollection<int> SnapshotInstantEffectTriggers(IEnumerable<int> triggers) {
			if (triggers == null) return m_EmptyInstantEffectTriggers;
			return new ReadOnlyCollection<int>(triggers.Select(InstantEffectTriggerContract.Validate).Distinct().OrderBy(value => value).ToList());
		}
	}

	public readonly struct ParameterKey : IEquatable<ParameterKey> {
		public NodeInstanceId NodeId { get; }
		public ParameterId ParameterId { get; }
		public ParameterKey(NodeInstanceId nodeId, ParameterId parameterId) {
			if (nodeId.IsEmpty || parameterId.IsEmpty) throw new ArgumentException("Parameter key IDs are required.");
			NodeId = nodeId; ParameterId = parameterId;
		}
		public bool Equals(ParameterKey other) => NodeId == other.NodeId && ParameterId == other.ParameterId;
		public override bool Equals(object obj) => obj is ParameterKey && Equals((ParameterKey)obj);
		public override int GetHashCode() => HashCode.Combine(NodeId, ParameterId);
		public static bool operator ==(ParameterKey left, ParameterKey right) => left.Equals(right);
		public static bool operator !=(ParameterKey left, ParameterKey right) => !left.Equals(right);
	}
}
