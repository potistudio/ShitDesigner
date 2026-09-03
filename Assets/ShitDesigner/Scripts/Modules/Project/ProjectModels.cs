using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;

namespace ShitDesigner.Project {
	public enum PortDirection { Input, Output }
	public enum PortType { ImageFrame, Float, Int, Bool, Vector2, Vector3, Vector4, Color }
	public enum LogicalControlKind { Value, PresetTrigger }
	public enum LogicalOperator { Min, Max }
	public enum PhysicalInputKind { Keyboard, Midi }
	public enum DefaultImageKind { TransparentBlack, OpaqueBlack, OpaqueWhite }
	public enum MediaAssetKind { Image, Video, Audio, Experimental }
	public enum MediaColorSpace { SRgb, Rec709, Linear }
	public enum MediaAlphaMode { Opaque, Straight, Premultiplied }
	public enum ParameterVisibility { Editable, ReadOnly, Hidden }

	public readonly struct ProjectPosition : IEquatable<ProjectPosition> {
		public float X { get; }
		public float Y { get; }
		public ProjectPosition(float x, float y) {
			if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y)) throw new ArgumentOutOfRangeException(nameof(x));
			X = x; Y = y;
		}
		public bool Equals(ProjectPosition other) => X.Equals(other.X) && Y.Equals(other.Y);
		public override bool Equals(object obj) => obj is ProjectPosition other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(X, Y);
	}

	public readonly struct ParameterRange : IEquatable<ParameterRange> {
		public ParameterValue Minimum { get; }
		public ParameterValue Maximum { get; }
		public ParameterRange(ParameterValue minimum, ParameterValue maximum) {
			if (minimum.Type != maximum.Type || !ParameterValue.IsLogicalControlTargetType(minimum.Type)) throw new ArgumentException("Range values must have a supported matching type.");
			if (!IsOrdered(minimum, maximum)) throw new ArgumentException("Range minimum cannot exceed maximum.");
			Minimum = minimum; Maximum = maximum;
		}
		private static bool IsOrdered(ParameterValue min, ParameterValue max) {
			switch (min.Type) {
				case ParameterType.Bool: return true;
				case ParameterType.Float: return min.AsFloat() <= max.AsFloat();
				case ParameterType.Int: return min.AsInt() <= max.AsInt();
				case ParameterType.Vector2: return min.AsVector2().X <= max.AsVector2().X && min.AsVector2().Y <= max.AsVector2().Y;
				case ParameterType.Vector3: return min.AsVector3().X <= max.AsVector3().X && min.AsVector3().Y <= max.AsVector3().Y && min.AsVector3().Z <= max.AsVector3().Z;
				case ParameterType.Vector4: return min.AsVector4().X <= max.AsVector4().X && min.AsVector4().Y <= max.AsVector4().Y && min.AsVector4().Z <= max.AsVector4().Z && min.AsVector4().W <= max.AsVector4().W;
				case ParameterType.Color: return min.AsColor().R <= max.AsColor().R && min.AsColor().G <= max.AsColor().G && min.AsColor().B <= max.AsColor().B && min.AsColor().A <= max.AsColor().A;
				default: return false;
			}
		}
		public bool Equals(ParameterRange other) => Minimum == other.Minimum && Maximum == other.Maximum;
		public override bool Equals(object obj) => obj is ParameterRange other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(Minimum, Maximum);
	}

	/// <summary>
	/// Stable Enum option metadata.  The ID is persisted and the display name is
	/// presentation metadata, so renaming an option does not change saved values.
	/// </summary>
	public sealed class EnumOptionDefinition {
		public ParameterId Id { get; }
		public string DisplayName { get; }

		public EnumOptionDefinition(ParameterId id, string displayName) {
			if (id.IsEmpty) throw new ArgumentException("Enum option ID is required.", nameof(id));
			if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Enum option display name is required.", nameof(displayName));
			Id = id;
			DisplayName = displayName.Trim();
		}
	}

	public sealed class ParameterDefinition {
		public ParameterId Id { get; }
		public string DisplayName { get; }
		public ParameterType Type { get; }
		public ParameterValue DefaultValue { get; }
		public ParameterRange? HardRange { get; }
		public bool RuntimeStateful { get; }
		public string Group { get; }
		public int DisplayOrder { get; }
		public string Description { get; }
		public string Unit { get; }
		public double Step { get; }
		public ParameterVisibility Visibility { get; }
		private readonly IReadOnlyList<ParameterId> _enumOptionIds;
		private readonly IReadOnlyList<EnumOptionDefinition> _enumOptions;
		public IReadOnlyList<ParameterId> EnumOptionIds => _enumOptionIds;
		public IReadOnlyList<EnumOptionDefinition> EnumOptions => _enumOptions;
		public ParameterDefinition(ParameterId id, string displayName, ParameterType type, ParameterValue defaultValue, ParameterRange? hardRange = null, bool runtimeStateful = false, IEnumerable<ParameterId> enumOptionIds = null,
			string group = null, int displayOrder = 0, string description = null, string unit = null, double step = 0d, ParameterVisibility visibility = ParameterVisibility.Editable)
			: this(id, displayName, type, defaultValue, hardRange, runtimeStateful, enumOptionIds, null, group, displayOrder, description, unit, step, visibility) {
		}

		public ParameterDefinition(ParameterId id, string displayName, ParameterType type, ParameterValue defaultValue, ParameterRange? hardRange, bool runtimeStateful, IEnumerable<EnumOptionDefinition> enumOptions,
			string group = null, int displayOrder = 0, string description = null, string unit = null, double step = 0d, ParameterVisibility visibility = ParameterVisibility.Editable)
			: this(id, displayName, type, defaultValue, hardRange, runtimeStateful, enumOptions?.Select(x => x == null ? default(ParameterId) : x.Id), enumOptions, group, displayOrder, description, unit, step, visibility) {
		}

		private ParameterDefinition(ParameterId id, string displayName, ParameterType type, ParameterValue defaultValue, ParameterRange? hardRange, bool runtimeStateful, IEnumerable<ParameterId> enumOptionIds, IEnumerable<EnumOptionDefinition> enumOptions,
			string group, int displayOrder, string description, string unit, double step, ParameterVisibility visibility) {
			if (id.IsEmpty) throw new ArgumentException("Parameter ID is required.", nameof(id));
			if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Parameter display name is required.", nameof(displayName));
			if (defaultValue.Type != type) throw new ArgumentException("Default value type does not match parameter type.", nameof(defaultValue));
			if (hardRange.HasValue && hardRange.Value.Minimum.Type != type) throw new ArgumentException("Hard range type does not match parameter type.", nameof(hardRange));
			if (runtimeStateful && id.Value != "transport.playhead_seconds") throw new ArgumentException("Only transport.playhead_seconds is RuntimeStateful in the initial version.", nameof(runtimeStateful));
			if (double.IsNaN(step) || double.IsInfinity(step) || step < 0d) throw new ArgumentOutOfRangeException(nameof(step));
			var options = (enumOptionIds ?? Enumerable.Empty<ParameterId>()).ToList();
			if (type != ParameterType.Enum && options.Count > 0) throw new ArgumentException("Enum option IDs are only valid for Enum parameters.", nameof(enumOptionIds));
			if (options.Any(x => x.IsEmpty) || options.GroupBy(x => x).Any(x => x.Count() > 1)) throw new ArgumentException("Enum option IDs must be non-empty and unique.", nameof(enumOptionIds));
			var optionDefinitions = (enumOptions ?? Enumerable.Empty<EnumOptionDefinition>()).ToList();
			if (type != ParameterType.Enum && optionDefinitions.Count > 0) throw new ArgumentException("Enum options are only valid for Enum parameters.", nameof(enumOptions));
			if (optionDefinitions.Any(x => x == null) || optionDefinitions.GroupBy(x => x.Id).Any(x => x.Count() > 1)) throw new ArgumentException("Enum options must be non-null and unique.", nameof(enumOptions));
			if (optionDefinitions.Count > 0 && options.Count > 0 && !optionDefinitions.Select(x => x.Id).SequenceEqual(options)) throw new ArgumentException("Enum option IDs and option definitions must match.", nameof(enumOptions));
			if (optionDefinitions.Count == 0 && type == ParameterType.Enum) optionDefinitions = options.Select(x => new EnumOptionDefinition(x, x.Value)).ToList();
			if (optionDefinitions.Count > 0 && options.Count == 0) options = optionDefinitions.Select(x => x.Id).ToList();
			if (type == ParameterType.Enum && !string.IsNullOrEmpty(defaultValue.AsString()) && !options.Contains(new ParameterId(defaultValue.AsString()))) throw new ArgumentException("Enum default must be one of the defined option IDs.", nameof(defaultValue));
			var normalizedDefault = defaultValue;
			if (hardRange.HasValue) {
				var clampedDefault = ParameterValue.Clamp(defaultValue, hardRange.Value.Minimum, hardRange.Value.Maximum);
				if (clampedDefault.IsFailure) throw new ArgumentException("Default value is invalid for the hard range.", nameof(defaultValue));
				normalizedDefault = clampedDefault.Value;
			}
			Id = id; DisplayName = displayName.Trim(); Type = type; DefaultValue = normalizedDefault; HardRange = hardRange; RuntimeStateful = runtimeStateful;
			Group = group ?? string.Empty; DisplayOrder = displayOrder; Description = description ?? string.Empty; Unit = unit ?? string.Empty; Step = step; Visibility = visibility;
			_enumOptionIds = new ReadOnlyCollection<ParameterId>(options);
			_enumOptions = new ReadOnlyCollection<EnumOptionDefinition>(optionDefinitions);
		}
		public Result<ParameterValue, Diagnostic> Clamp(ParameterValue value) {
			if (value.Type != Type) return Result.Failure<ParameterValue, Diagnostic>(ProjectDiagnostics.TypeMismatch(Id));
			if (Type == ParameterType.Enum && !string.IsNullOrEmpty(value.AsString()) && !_enumOptionIds.Contains(new ParameterId(value.AsString()))) return Result.Failure<ParameterValue, Diagnostic>(ProjectDiagnostics.InvalidValue(Id));
			if (Type == ParameterType.MediaAssetReference && value.IsMediaAssetSelected && !value.AsMediaAsset().Value.IsUuidV4) return Result.Failure<ParameterValue, Diagnostic>(ProjectDiagnostics.InvalidValue(Id));
			if (!HardRange.HasValue) return Result.Success<ParameterValue, Diagnostic>(value);
			return ParameterValue.Clamp(value, HardRange.Value.Minimum, HardRange.Value.Maximum);
		}
	}

	public sealed class ParameterRecord {
		public ParameterDefinition Definition { get; }
		public ParameterValue BaseValue { get; }
		public bool IsBroken { get; }
		public string BrokenReason { get; }
		public ParameterRecord(ParameterDefinition definition, ParameterValue baseValue)
			: this(definition, baseValue, false, null) {
		}
		private ParameterRecord(ParameterDefinition definition, ParameterValue baseValue, bool isBroken, string brokenReason) {
			Definition = definition ?? throw new ArgumentNullException(nameof(definition));
			if (baseValue.Type != definition.Type) throw new ArgumentException("Base value type does not match definition.", nameof(baseValue));
			var clamped = definition.Clamp(baseValue);
			BaseValue = clamped.IsSuccess ? clamped.Value : throw new ArgumentException("Base value is invalid.", nameof(baseValue));
			IsBroken = isBroken; BrokenReason = brokenReason;
		}
		public ParameterRecord WithBaseValue(ParameterValue value) => new ParameterRecord(Definition, value);
		public ParameterRecord AsBroken(string reason) => new ParameterRecord(Definition, BaseValue, true, reason);
		public ParameterRecord AsRepaired() => new ParameterRecord(Definition, BaseValue, false, null);
	}

	/// <summary>
	/// A frame-boundary BaseValue update after the Runtime queue has applied
	/// sequence ordering.  Project owns validation and atomic persistence;
	/// Runtime only supplies this immutable value object.
	/// </summary>
	public readonly struct BaseValueUpdate : IEquatable<BaseValueUpdate> {
		public NodeInstanceId NodeId { get; }
		public ParameterId ParameterId { get; }
		public ParameterValue Value { get; }
		public BaseValueUpdate(NodeInstanceId nodeId, ParameterId parameterId, ParameterValue value) {
			if (nodeId.IsEmpty || parameterId.IsEmpty) throw new ArgumentException("Base value update IDs are required.");
			NodeId = nodeId;
			ParameterId = parameterId;
			Value = value;
		}
		public bool Equals(BaseValueUpdate other) => NodeId == other.NodeId && ParameterId == other.ParameterId && Value == other.Value;
		public override bool Equals(object obj) => obj is BaseValueUpdate other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(NodeId, ParameterId, Value);
		public static bool operator ==(BaseValueUpdate left, BaseValueUpdate right) => left.Equals(right);
		public static bool operator !=(BaseValueUpdate left, BaseValueUpdate right) => !left.Equals(right);
	}

	public sealed class PortSnapshotRecord {
		public PortId Id { get; }
		public PortDirection Direction { get; }
		public PortType Type { get; }
		public bool Required { get; }
		public DefaultImageKind? DefaultImage { get; }
		public PortSnapshotRecord(PortId id, PortDirection direction, PortType type, bool required, DefaultImageKind? defaultImage = null) {
			Id = id; Direction = direction; Type = type; Required = required; DefaultImage = defaultImage;
			if (direction == PortDirection.Output && defaultImage.HasValue) throw new ArgumentException("Only optional ImageFrame inputs may define a default image.", nameof(defaultImage));
			if (defaultImage.HasValue && (direction != PortDirection.Input || required || type != PortType.ImageFrame)) throw new ArgumentException("Default image requires an optional ImageFrame input.", nameof(defaultImage));
		}
	}

	public sealed class ConnectionRecord {
		public ConnectionId Id { get; }
		public NodeInstanceId SourceNodeId { get; }
		public PortId SourcePortId { get; }
		public NodeInstanceId DestinationNodeId { get; }
		public PortId DestinationPortId { get; }
		public string ConversionId { get; }
		public bool IsBroken { get; }
		public string BrokenReason { get; }
		public ConnectionRecord(ConnectionId id, NodeInstanceId sourceNodeId, PortId sourcePortId, NodeInstanceId destinationNodeId, PortId destinationPortId, string conversionId = null, bool isBroken = false, string brokenReason = null) {
			Id = id; SourceNodeId = sourceNodeId; SourcePortId = sourcePortId; DestinationNodeId = destinationNodeId; DestinationPortId = destinationPortId;
			ConversionId = string.IsNullOrWhiteSpace(conversionId) ? null : conversionId.Trim(); IsBroken = isBroken; BrokenReason = brokenReason;
		}
		public ConnectionRecord AsBroken(string reason) => new ConnectionRecord(Id, SourceNodeId, SourcePortId, DestinationNodeId, DestinationPortId, ConversionId, true, reason);
		public ConnectionRecord AsRepaired() => new ConnectionRecord(Id, SourceNodeId, SourcePortId, DestinationNodeId, DestinationPortId, ConversionId, false, null);
	}

	public sealed class UnknownNodeRecord {
		public NodeTypeId OriginalNodeTypeId { get; }
		public int OriginalSchemaVersion { get; }
		public string RawJsonValue { get; }
		public UnknownNodeRecord(NodeTypeId originalNodeTypeId, int originalSchemaVersion, string rawJsonValue) {
			if (originalNodeTypeId.IsEmpty) throw new ArgumentException("Original node type is required.", nameof(originalNodeTypeId));
			if (originalNodeTypeId.Value == "system.unknown_node") throw new ArgumentException("UnknownNode original type cannot be the placeholder type.", nameof(originalNodeTypeId));
			if (originalSchemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(originalSchemaVersion));
			if (string.IsNullOrWhiteSpace(rawJsonValue)) throw new ArgumentException("Unknown node raw state is required.", nameof(rawJsonValue));
			RawJsonValue = rawJsonValue;
			OriginalNodeTypeId = originalNodeTypeId;
			OriginalSchemaVersion = originalSchemaVersion;
		}
	}

	public sealed class NodeRecord {
		private readonly IReadOnlyList<ParameterRecord> _parameters;
		private readonly IReadOnlyList<PortSnapshotRecord> _ports;
		public NodeInstanceId Id { get; }
		public NodeTypeId TypeId { get; }
		public int SchemaVersion { get; }
		public string DisplayName { get; }
		public bool Enabled { get; }
		public ProjectPosition Position { get; }
		public bool SystemOwned { get; }
		public bool UserAddable { get; }
		public IReadOnlyList<ParameterRecord> Parameters => _parameters;
		public IReadOnlyList<PortSnapshotRecord> Ports => _ports;
		public string RawState { get; }
		public UnknownNodeRecord Unknown { get; }
		public bool IsUnknown => Unknown != null;
		public NodeRecord(NodeInstanceId id, NodeTypeId typeId, int schemaVersion, string displayName, bool enabled, ProjectPosition position, IEnumerable<ParameterRecord> parameters = null, IEnumerable<PortSnapshotRecord> ports = null, string rawState = "{}", bool systemOwned = false, bool userAddable = true, UnknownNodeRecord unknown = null) {
			if (id.IsEmpty || typeId.IsEmpty) throw new ArgumentException("Node IDs are required.");
			if (schemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
			if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Node display name is required.", nameof(displayName));
			Id = id; TypeId = typeId; SchemaVersion = schemaVersion; DisplayName = displayName.Trim(); Enabled = enabled; Position = position; SystemOwned = systemOwned; UserAddable = userAddable;
			_parameters = new ReadOnlyCollection<ParameterRecord>((parameters ?? Enumerable.Empty<ParameterRecord>()).ToList());
			_ports = new ReadOnlyCollection<PortSnapshotRecord>((ports ?? Enumerable.Empty<PortSnapshotRecord>()).ToList());
			if (_parameters.GroupBy(x => x.Definition.Id).Any(x => x.Count() > 1)) throw new ArgumentException("Parameter IDs must be unique within a node.", nameof(parameters));
			if (_ports.GroupBy(x => x.Id).Any(x => x.Count() > 1)) throw new ArgumentException("Port IDs must be unique within a node.", nameof(ports));
			RawState = rawState ?? "{}"; Unknown = unknown;
			if (typeId.Value == "system.unknown_node" && unknown == null) throw new ArgumentException("Unknown runtime nodes must carry UnknownNode data.", nameof(unknown));
			if (unknown != null && typeId.Value != "system.unknown_node") throw new ArgumentException("Unknown nodes must use system.unknown_node as the runtime type ID.", nameof(typeId));
		}
		public ParameterRecord FindParameter(ParameterId id) => _parameters.FirstOrDefault(x => x.Definition.Id == id);
		public PortSnapshotRecord FindPort(PortId id) => _ports.FirstOrDefault(x => x.Id == id);
		public NodeRecord WithParameter(ParameterId parameterId, ParameterValue value) {
			var parameter = FindParameter(parameterId);
			if (parameter == null) throw new InvalidOperationException("Parameter not found.");
			return new NodeRecord(Id, TypeId, SchemaVersion, DisplayName, Enabled, Position, _parameters.Select(x => x.Definition.Id == parameterId ? x.WithBaseValue(value) : x), _ports, RawState, SystemOwned, UserAddable, Unknown);
		}
		internal NodeRecord WithParameterRecord(ParameterId parameterId, ParameterRecord replacement) {
			if (replacement == null) throw new ArgumentNullException(nameof(replacement));
			if (FindParameter(parameterId) == null) throw new InvalidOperationException("Parameter not found.");
			return new NodeRecord(Id, TypeId, SchemaVersion, DisplayName, Enabled, Position, _parameters.Select(x => x.Definition.Id == parameterId ? replacement : x), _ports, RawState, SystemOwned, UserAddable, Unknown);
		}
		public NodeRecord WithUnknown(UnknownNodeRecord unknown) {
			return new NodeRecord(Id, new NodeTypeId("system.unknown_node"), SchemaVersion, DisplayName, Enabled, Position, _parameters, _ports, RawState, SystemOwned, false, unknown);
		}
		public NodeRecord WithPorts(IEnumerable<PortSnapshotRecord> ports) {
			return new NodeRecord(Id, TypeId, SchemaVersion, DisplayName, Enabled, Position, _parameters, ports, RawState, SystemOwned, UserAddable, Unknown);
		}
		public NodeRecord WithRawState(string rawState) {
			return new NodeRecord(Id, TypeId, SchemaVersion, DisplayName, Enabled, Position, _parameters, _ports, rawState, SystemOwned, UserAddable, Unknown);
		}
	}

	public sealed class BrokenReferenceRecord {
		public string ReferenceKind { get; }
		public string TargetId { get; }
		public string Reason { get; }
		public ParameterValue SavedValue { get; }
		public bool HasSavedValue { get; }
		public BrokenReferenceRecord(string referenceKind, string targetId, string reason, ParameterValue? savedValue = null) {
			ReferenceKind = referenceKind ?? string.Empty; TargetId = targetId ?? string.Empty; Reason = reason ?? string.Empty; HasSavedValue = savedValue.HasValue; SavedValue = savedValue.GetValueOrDefault();
		}
	}

	public sealed class LogicalControlTargetRecord {
		public NodeInstanceId NodeId { get; }
		public ParameterId ParameterId { get; }
		public ParameterType ParameterType { get; }
		public ParameterValue TargetMin { get; }
		public ParameterValue TargetMax { get; }
		public bool Invert { get; }
		public bool IsBroken { get; }
		public string BrokenReason { get; }
		public LogicalControlTargetRecord(NodeInstanceId nodeId, ParameterId parameterId, ParameterType parameterType, ParameterValue targetMin, ParameterValue targetMax, bool invert = false, bool isBroken = false, string brokenReason = null) {
			if (!ParameterValue.IsLogicalControlTargetType(parameterType)) throw new ArgumentException("Logical controls cannot target this parameter type.", nameof(parameterType));
			if (targetMin.Type != parameterType || targetMax.Type != parameterType) throw new ArgumentException("Target range type mismatch.");
			if (!IsOrdered(targetMin, targetMax)) throw new ArgumentException("Target minimum cannot exceed target maximum.");
			NodeId = nodeId; ParameterId = parameterId; ParameterType = parameterType; TargetMin = targetMin; TargetMax = targetMax; Invert = invert; IsBroken = isBroken; BrokenReason = brokenReason;
		}
		private static bool IsOrdered(ParameterValue min, ParameterValue max) => new ParameterRange(min, max).Minimum == min;
		public LogicalControlTargetRecord AsBroken(string reason) => new LogicalControlTargetRecord(NodeId, ParameterId, ParameterType, TargetMin, TargetMax, Invert, true, reason);
		public LogicalControlTargetRecord AsRepaired() => new LogicalControlTargetRecord(NodeId, ParameterId, ParameterType, TargetMin, TargetMax, Invert, false, null);
		public Result<ParameterValue, Diagnostic> Map(float normalizedValue) {
			if (IsBroken) return Result.Failure<ParameterValue, Diagnostic>(ProjectDiagnostics.BrokenReference(NodeId, ParameterId, BrokenReason));
			if (float.IsNaN(normalizedValue) || float.IsInfinity(normalizedValue)) return Result.Failure<ParameterValue, Diagnostic>(ProjectDiagnostics.InvalidValue(ParameterId));
			var t = Math.Min(1f, Math.Max(0f, normalizedValue)); if (Invert) t = 1f - t;
			return ParameterValue.Lerp(TargetMin, TargetMax, t);
		}
	}

	public sealed class ControlMappingRecord {
		public PhysicalInputKind Kind { get; }
		public string PhysicalId { get; }
		public string ControlPath { get; }
		public float RawMin { get; }
		public float RawMax { get; }
		public bool Invert { get; }
		public bool IsBroken { get; }
		public string BrokenReason { get; }
		public ControlMappingRecord(PhysicalInputKind kind, string physicalId, string controlPath, float rawMin = 0, float rawMax = 1, bool invert = false, bool isBroken = false, string brokenReason = null) {
			if (float.IsNaN(rawMin) || float.IsInfinity(rawMin) || float.IsNaN(rawMax) || float.IsInfinity(rawMax) || rawMin >= rawMax) throw new ArgumentException("RawMin must be less than RawMax.");
			Kind = kind; PhysicalId = physicalId ?? string.Empty; ControlPath = controlPath ?? string.Empty; RawMin = rawMin; RawMax = rawMax; Invert = invert; IsBroken = isBroken; BrokenReason = brokenReason;
		}
		public float Normalize(float raw) {
			if (float.IsNaN(raw) || float.IsInfinity(raw)) throw new ArgumentOutOfRangeException(nameof(raw), "Physical input values must be finite.");
			var value = Math.Min(1f, Math.Max(0f, (raw - RawMin) / (RawMax - RawMin)));
			return Invert ? 1f - value : value;
		}
		public ControlMappingRecord AsBroken(string reason) => new ControlMappingRecord(Kind, PhysicalId, ControlPath, RawMin, RawMax, Invert, true, reason);
	}

	public abstract class LogicalExpressionNode {
		public abstract bool IsComplete { get; }
		public abstract IEnumerable<LogicalControlId> ReferencedControls { get; }
	}
	public sealed class LogicalControlLeaf : LogicalExpressionNode {
		public LogicalControlId ControlId { get; }
		public LogicalControlLeaf(LogicalControlId controlId) { ControlId = controlId; }
		public override bool IsComplete => !ControlId.IsEmpty;
		public override IEnumerable<LogicalControlId> ReferencedControls { get { yield return ControlId; } }
	}
	public sealed class BaseValueLeaf : LogicalExpressionNode {
		public override bool IsComplete => true;
		public override IEnumerable<LogicalControlId> ReferencedControls { get { yield break; } }
	}
	public sealed class BrokenExpressionLeaf : LogicalExpressionNode {
		public LogicalControlId OriginalControlId { get; }
		public string Reason { get; }
		public BrokenExpressionLeaf(LogicalControlId originalControlId, string reason) { OriginalControlId = originalControlId; Reason = reason ?? string.Empty; }
		public override bool IsComplete => false;
		public override IEnumerable<LogicalControlId> ReferencedControls { get { yield return OriginalControlId; } }
		public LogicalExpressionNode Revalidate(Func<LogicalControlId, bool> isAvailable) => isAvailable != null && isAvailable(OriginalControlId) ? new LogicalControlLeaf(OriginalControlId) : this;
	}
	public sealed class BinaryLogicalExpression : LogicalExpressionNode {
		public LogicalOperator Operator { get; }
		public LogicalExpressionNode Left { get; }
		public LogicalExpressionNode Right { get; }
		public BinaryLogicalExpression(LogicalOperator @operator, LogicalExpressionNode left, LogicalExpressionNode right) { Operator = @operator; Left = left; Right = right; }
		public override bool IsComplete => Left != null && Right != null && Left.IsComplete && Right.IsComplete;
		public override IEnumerable<LogicalControlId> ReferencedControls => (Left?.ReferencedControls ?? Enumerable.Empty<LogicalControlId>()).Concat(Right?.ReferencedControls ?? Enumerable.Empty<LogicalControlId>());
	}

	public sealed class LogicalControlRecord {
		private readonly IReadOnlyList<LogicalControlTargetRecord> _targets;
		private readonly IReadOnlyList<ControlMappingRecord> _mappings;
		public LogicalControlId Id { get; }
		public string Name { get; }
		public LogicalControlKind Kind { get; }
		public float InitialValue { get; }
		public PresetId? PresetId { get; }
		public bool PresetIsBroken { get; }
		public string BrokenReason { get; }
		public IReadOnlyList<LogicalControlTargetRecord> Targets => _targets;
		public IReadOnlyList<ControlMappingRecord> Mappings => _mappings;
		public LogicalControlRecord(LogicalControlId id, string name, LogicalControlKind kind, float initialValue = 0, IEnumerable<LogicalControlTargetRecord> targets = null, IEnumerable<ControlMappingRecord> mappings = null, PresetId? presetId = null, bool presetIsBroken = false, string brokenReason = null) {
			if (id.IsEmpty) throw new ArgumentException("Logical control ID is required.", nameof(id));
			if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Logical control name is required.", nameof(name));
			if (float.IsNaN(initialValue) || float.IsInfinity(initialValue) || initialValue < 0 || initialValue > 1) throw new ArgumentOutOfRangeException(nameof(initialValue));
			if (kind == LogicalControlKind.PresetTrigger && targets != null && targets.Any()) throw new ArgumentException("PresetTrigger cannot target parameters.", nameof(targets));
			if (kind == LogicalControlKind.Value && presetId.HasValue) throw new ArgumentException("Value controls cannot reference presets.", nameof(presetId));
			if (kind == LogicalControlKind.PresetTrigger && initialValue != 0) throw new ArgumentException("PresetTrigger cannot have a numeric InitialValue.", nameof(initialValue));
			if (kind == LogicalControlKind.Value && presetIsBroken) throw new ArgumentException("Value controls cannot have a preset reference.", nameof(presetIsBroken));
			Id = id; Name = name.Trim(); Kind = kind; InitialValue = kind == LogicalControlKind.Value ? initialValue : 0; PresetId = presetId; PresetIsBroken = presetIsBroken; BrokenReason = brokenReason;
			_targets = new ReadOnlyCollection<LogicalControlTargetRecord>((targets ?? Enumerable.Empty<LogicalControlTargetRecord>()).ToList());
			_mappings = new ReadOnlyCollection<ControlMappingRecord>((mappings ?? Enumerable.Empty<ControlMappingRecord>()).ToList());
		}
		public LogicalControlRecord WithPreset(PresetId? preset) => new LogicalControlRecord(Id, Name, Kind, InitialValue, _targets, _mappings, preset);
		public LogicalControlRecord WithName(string name) => new LogicalControlRecord(Id, name, Kind, InitialValue, _targets, _mappings, PresetId, PresetIsBroken, BrokenReason);
		public LogicalControlRecord AsBrokenPreset(string reason) => new LogicalControlRecord(Id, Name, Kind, InitialValue, _targets, _mappings, PresetId, true, reason);
		public LogicalControlRecord AsRepairedPreset() => new LogicalControlRecord(Id, Name, Kind, InitialValue, _targets, _mappings, PresetId, false, null);
		public LogicalControlRecord WithTargets(IEnumerable<LogicalControlTargetRecord> targets) => new LogicalControlRecord(Id, Name, Kind, InitialValue, targets, _mappings, PresetId, PresetIsBroken, BrokenReason);
		public LogicalControlRecord WithMappings(IEnumerable<ControlMappingRecord> mappings) => new LogicalControlRecord(Id, Name, Kind, InitialValue, _targets, mappings, PresetId, PresetIsBroken, BrokenReason);
	}

	public sealed class ParameterExpressionRecord {
		public NodeInstanceId NodeId { get; }
		public ParameterId ParameterId { get; }
		public LogicalExpressionNode Expression { get; }
		public ParameterRange? OutputRange { get; }
		public bool IsBroken => HasCycle() || !Expression.IsComplete;
		public bool IsValid => !HasCycle() && Expression.IsComplete && IsSupportedTree(Expression) && Expression.ReferencedControls.Any() && CountBaseLeaves(Expression) <= 1;
		public ParameterExpressionRecord(NodeInstanceId nodeId, ParameterId parameterId, LogicalExpressionNode expression, ParameterRange? outputRange = null) {
			if (expression == null) throw new ArgumentNullException(nameof(expression));
			if (outputRange.HasValue && outputRange.Value.Minimum.Type != outputRange.Value.Maximum.Type) throw new ArgumentException("Expression output range must have matching types.", nameof(outputRange));
			NodeId = nodeId; ParameterId = parameterId; Expression = expression; OutputRange = outputRange;
		}
		private static int CountBaseLeaves(LogicalExpressionNode node) {
			if (node is BaseValueLeaf) return 1;
			if (node is BinaryLogicalExpression binary) return CountBaseLeaves(binary.Left) + CountBaseLeaves(binary.Right);
			return 0;
		}
		public bool HasCycle() => HasCycle(Expression, new HashSet<LogicalExpressionNode>());
		private static bool HasCycle(LogicalExpressionNode node, HashSet<LogicalExpressionNode> active) {
			if (node == null) return false;
			if (!active.Add(node)) return true;
			var binary = node as BinaryLogicalExpression;
			var result = binary != null && (HasCycle(binary.Left, active) || HasCycle(binary.Right, active));
			active.Remove(node);
			return result;
		}
		public ParameterExpressionRecord AsBroken(string reason) {
			return new ParameterExpressionRecord(NodeId, ParameterId, BreakNode(Expression, reason), OutputRange);
		}
		public ParameterExpressionRecord Revalidate(Func<LogicalControlId, bool> isAvailable) {
			return new ParameterExpressionRecord(NodeId, ParameterId, RevalidateNode(Expression, isAvailable), OutputRange);
		}
		private static bool IsSupportedTree(LogicalExpressionNode node) {
			if (node is LogicalControlLeaf || node is BaseValueLeaf) return true;
			var binary = node as BinaryLogicalExpression;
			if (binary == null || (binary.Operator != LogicalOperator.Min && binary.Operator != LogicalOperator.Max) || binary.Left == null || binary.Right == null) return false;
			return IsSupportedTree(binary.Left) && IsSupportedTree(binary.Right);
		}
		private static LogicalExpressionNode BreakNode(LogicalExpressionNode node, string reason) {
			if (node == null) return null;
			var leaf = node as LogicalControlLeaf;
			if (leaf != null) return new BrokenExpressionLeaf(leaf.ControlId, reason);
			var binary = node as BinaryLogicalExpression;
			return binary == null ? node : new BinaryLogicalExpression(binary.Operator, BreakNode(binary.Left, reason), BreakNode(binary.Right, reason));
		}
		private static LogicalExpressionNode RevalidateNode(LogicalExpressionNode node, Func<LogicalControlId, bool> isAvailable) {
			if (node == null) return null;
			var broken = node as BrokenExpressionLeaf;
			if (broken != null) return broken.Revalidate(isAvailable);
			var leaf = node as LogicalControlLeaf;
			if (leaf != null) return isAvailable != null && isAvailable(leaf.ControlId) ? leaf : new BrokenExpressionLeaf(leaf.ControlId, "Logical control reference is missing.");
			var binary = node as BinaryLogicalExpression;
			return binary == null ? node : new BinaryLogicalExpression(binary.Operator, RevalidateNode(binary.Left, isAvailable), RevalidateNode(binary.Right, isAvailable));
		}
	}

	public sealed class PresetEntryRecord {
		public NodeInstanceId NodeId { get; }
		public ParameterId ParameterId { get; }
		public ParameterType ParameterType { get; }
		public ParameterValue Value { get; }
		public bool IsBroken { get; }
		public string BrokenReason { get; }
		public PresetEntryRecord(NodeInstanceId nodeId, ParameterId parameterId, ParameterType parameterType, ParameterValue value, bool isBroken = false, string brokenReason = null) {
			if (value.Type != parameterType) throw new ArgumentException("Preset value type mismatch.", nameof(value));
			NodeId = nodeId; ParameterId = parameterId; ParameterType = parameterType; Value = value; IsBroken = isBroken; BrokenReason = brokenReason;
		}
		public PresetEntryRecord AsBroken(string reason) => new PresetEntryRecord(NodeId, ParameterId, ParameterType, Value, true, reason);
		public PresetEntryRecord AsRepaired() => new PresetEntryRecord(NodeId, ParameterId, ParameterType, Value, false, null);
	}

	public sealed class PresetRecord {
		private readonly IReadOnlyList<PresetEntryRecord> _entries;
		public PresetId Id { get; }
		public string Name { get; }
		public string Category { get; }
		public int SortIndex { get; }
		public IReadOnlyList<PresetEntryRecord> Entries => _entries;
		public bool IsBroken => _entries.Any(e => e.IsBroken);
		public PresetRecord(PresetId id, string name, string category = "", int sortIndex = 0, IEnumerable<PresetEntryRecord> entries = null) {
			if (id.IsEmpty) throw new ArgumentException("Preset ID is required.", nameof(id));
			if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Preset name is required.", nameof(name));
			Id = id; Name = name.Trim(); Category = category?.Trim() ?? string.Empty; SortIndex = sortIndex;
			var entryList = (entries ?? Enumerable.Empty<PresetEntryRecord>()).ToList();
			if (entryList.GroupBy(x => new { x.NodeId, x.ParameterId }).Any(x => x.Count() > 1)) throw new ArgumentException("A preset cannot contain duplicate node and parameter entries.", nameof(entries));
			_entries = new ReadOnlyCollection<PresetEntryRecord>(entryList);
		}
		public PresetRecord WithEntries(IEnumerable<PresetEntryRecord> entries) => new PresetRecord(Id, Name, Category, SortIndex, entries);
		public PresetRecord WithName(string name) => new PresetRecord(Id, name, Category, SortIndex, _entries);
	}

	public sealed class MediaAssetRecord {
		public MediaAssetId Id { get; }
		public string DisplayName { get; }
		public string RelativePath { get; }
		public long ByteSize { get; }
		public string IntegrityHash { get; }
		public string IntegrityAlgorithm => "xxh3_128";
		public MediaAssetKind Kind { get; }
		public MediaColorSpace ColorSpace { get; }
		public MediaAlphaMode AlphaMode { get; }
		public MediaAssetRecord(MediaAssetId id, string displayName, string relativePath, long byteSize = 0, string integrityHash = null, MediaAssetKind kind = MediaAssetKind.Experimental, MediaColorSpace colorSpace = MediaColorSpace.SRgb, MediaAlphaMode alphaMode = MediaAlphaMode.Opaque) {
			if (id.IsEmpty) throw new ArgumentException("Media asset ID is required.", nameof(id));
			if (!id.IsUuidV4) throw new ArgumentException("Media asset ID must be UUID v4.", nameof(id));
			if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("Media asset display name is required.", nameof(displayName));
			var normalizedPath = (relativePath ?? string.Empty).Replace('\\', '/');
			var expectedPrefix = "Assets/" + id.Value + "/source.";
			var extension = normalizedPath.StartsWith(expectedPrefix, StringComparison.Ordinal) ? normalizedPath.Substring(expectedPrefix.Length) : string.Empty;
			if (string.IsNullOrWhiteSpace(relativePath) || normalizedPath.StartsWith("/", StringComparison.Ordinal) || normalizedPath.Contains("..") || normalizedPath.Contains(":") || !normalizedPath.StartsWith(expectedPrefix, StringComparison.Ordinal) || extension.Length == 0 || extension.Contains("/")) throw new ArgumentException("Media path must be Assets/{MediaAssetId}/source.ext.", nameof(relativePath));
			if (byteSize < 0) throw new ArgumentOutOfRangeException(nameof(byteSize));
			if (string.IsNullOrEmpty(integrityHash) || integrityHash.Length != 32 || integrityHash.Any(x => !((x >= '0' && x <= '9') || (x >= 'a' && x <= 'f')))) throw new ArgumentException("Integrity hash must be lowercase XXH3-128 hex.", nameof(integrityHash));
			Id = id; DisplayName = displayName.Trim(); RelativePath = normalizedPath; ByteSize = byteSize; IntegrityHash = integrityHash; Kind = kind; ColorSpace = colorSpace; AlphaMode = alphaMode;
		}
	}

	public sealed class DashboardWidgetRecord {
		public string WidgetId { get; }
		public NodeInstanceId NodeId { get; }
		public ParameterId ParameterId { get; }
		public int Column { get; }
		public int Row { get; }
		public int Width { get; }
		public int Height { get; }
		public string Label { get; }
		public bool IsBroken { get; }
		public string BrokenReason { get; }
		public DashboardWidgetRecord(string widgetId, NodeInstanceId nodeId, ParameterId parameterId, int column = 0, int row = 0, int width = 1, int height = 1, string label = null, bool isBroken = false, string brokenReason = null) {
			if (string.IsNullOrWhiteSpace(widgetId)) throw new ArgumentException("Widget ID is required.", nameof(widgetId));
			if (column < 0 || row < 0 || width <= 0 || height <= 0 || column + width > 12) throw new ArgumentOutOfRangeException(nameof(width));
			WidgetId = widgetId.Trim(); NodeId = nodeId; ParameterId = parameterId; Column = column; Row = row; Width = width; Height = height; Label = label?.Trim(); IsBroken = isBroken; BrokenReason = brokenReason;
		}
		public DashboardWidgetRecord AsBroken(string reason) => new DashboardWidgetRecord(WidgetId, NodeId, ParameterId, Column, Row, Width, Height, Label, true, reason);
		public DashboardWidgetRecord AsRepaired() => new DashboardWidgetRecord(WidgetId, NodeId, ParameterId, Column, Row, Width, Height, Label, false, null);
	}

	public sealed class DashboardPageRecord {
		private readonly IReadOnlyList<DashboardWidgetRecord> _widgets;
		public string PageId { get; }
		public string Name { get; }
		public IReadOnlyList<DashboardWidgetRecord> Widgets => _widgets;
		public DashboardPageRecord(string pageId, string name, IEnumerable<DashboardWidgetRecord> widgets = null) {
			if (string.IsNullOrWhiteSpace(pageId) || string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Dashboard page identity is required.");
			PageId = pageId.Trim(); Name = name.Trim(); _widgets = new ReadOnlyCollection<DashboardWidgetRecord>((widgets ?? Enumerable.Empty<DashboardWidgetRecord>()).ToList());
		}
	}

	public sealed class ProjectUiStateRecord {
		private readonly IReadOnlyList<DashboardPageRecord> _dashboardPages;
		private readonly IReadOnlyList<string> _previewNodeIds;
		public IReadOnlyList<DashboardPageRecord> DashboardPages => _dashboardPages;
		public IReadOnlyList<string> PreviewNodeIds => _previewNodeIds;
		public ProjectUiStateRecord(IEnumerable<DashboardPageRecord> dashboardPages = null, IEnumerable<string> previewNodeIds = null) {
			_dashboardPages = new ReadOnlyCollection<DashboardPageRecord>((dashboardPages ?? Enumerable.Empty<DashboardPageRecord>()).ToList());
			_previewNodeIds = new ReadOnlyCollection<string>((previewNodeIds ?? Enumerable.Empty<string>()).Select(x => x ?? string.Empty).ToList());
		}
		public ProjectUiStateRecord WithDashboardPages(IEnumerable<DashboardPageRecord> pages) => new ProjectUiStateRecord(pages, _previewNodeIds);
		public ProjectUiStateRecord WithPreviewNodeIds(IEnumerable<string> ids) => new ProjectUiStateRecord(_dashboardPages, ids);
	}

	internal static class ProjectDiagnostics {
		public static Diagnostic TypeMismatch(ParameterId parameter) => new Diagnostic(new DiagnosticCode("project.parameter.type_mismatch"), Severity.Error, "Parameter value type does not match.", parameterId: parameter);
		public static Diagnostic InvalidValue(ParameterId parameter) => new Diagnostic(new DiagnosticCode("project.parameter.invalid_value"), Severity.Error, "Parameter value must be finite and valid.", parameterId: parameter);
		public static Diagnostic BrokenReference(NodeInstanceId node, ParameterId parameter, string reason) => new Diagnostic(new DiagnosticCode("project.reference.broken"), Severity.Error, reason ?? "Reference is broken.", nodeId: node, parameterId: parameter);
		public static Diagnostic Rejected(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message);
	}
}
