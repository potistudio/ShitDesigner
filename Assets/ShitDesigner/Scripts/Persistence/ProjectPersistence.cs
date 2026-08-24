using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.IO.Hashing;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using ShitDesigner.Core;
using ShitDesigner.Project;

namespace ShitDesigner.Persistence {
	public static class PersistenceConstants {
		public const int CurrentProjectFormatVersion = 1;
		public const int MaxManifestBytes = 64 * 1024 * 1024;
		public const string MainFileName = "project.json";
		public const string BackupFileName = "project.json.bak";
		public const string TemporaryFileName = "project.json.tmp";
		public const string IntegrityAlgorithm = "xxh3_128";
	}

	public static class MediaPathRules {
		public static Result<string, Diagnostic> Normalize(MediaAssetId assetId, string path) {
			if (assetId.IsEmpty || !assetId.IsUuidV4 || string.IsNullOrWhiteSpace(path)) return Failure("persistence.media.path_invalid", "Media path must identify a UUID v4 asset.");
			var normalized = path.Replace('\\', '/');
			if (normalized.IndexOf('\0') >= 0 || normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains(":") || normalized.Contains("..", StringComparison.Ordinal) || normalized.IndexOfAny(Path.GetInvalidPathChars()) >= 0) return Failure("persistence.media.path_invalid", "Media path must be a relative path without traversal.");
			var segments = normalized.Split('/');
			if (segments.Any(x => string.IsNullOrEmpty(x) || x == "." || x == "..")) return Failure("persistence.media.path_invalid", "Media path contains an invalid segment.");
			var prefix = "Assets/" + assetId.Value + "/source.";
			if (!normalized.StartsWith(prefix, StringComparison.Ordinal) || normalized.Length <= prefix.Length || normalized.Substring(prefix.Length).Contains('/')) return Failure("persistence.media.path_invalid", "Media path must be Assets/{MediaAssetId}/source.ext.");
			return Result.Success<string, Diagnostic>(normalized);
		}

		private static Result<string, Diagnostic> Failure(string code, string message) => Result.Failure<string, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
	}

	public static class AssetIntegrity {
		public static string AlgorithmId => PersistenceConstants.IntegrityAlgorithm;

		public static string Hash(ReadOnlySpan<byte> bytes) {
			return FormatDigest(XxHash128.Hash(bytes));
		}

		private static string FormatDigest(ReadOnlySpan<byte> digest) {
			var builder = new StringBuilder(digest.Length * 2);
			foreach (var value in digest) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
			return builder.ToString();
		}

		public static string Hash(Stream stream) {
			if (stream == null) throw new ArgumentNullException(nameof(stream));
			var hash = new XxHash128();
			var buffer = new byte[64 * 1024];
			int read;
			while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) hash.Append(new ReadOnlySpan<byte>(buffer, 0, read));
			return FormatDigest(hash.GetHashAndReset());
		}

		public static bool IsDigest(string value) => !string.IsNullOrEmpty(value) && value.Length == 32 && value.All(x => (x >= '0' && x <= '9') || (x >= 'a' && x <= 'f'));
	}

	// Explicit source-generation contract. The serializer below writes the
	// canonical DTO directly so Unknown raw JSON remains opaque byte-for-byte.
	[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
	[JsonSerializable(typeof(ProjectDocumentDto))]
	[JsonSerializable(typeof(GeneratedProjectEnvelopeDto))]
	internal partial class ProjectPersistenceJsonContext : JsonSerializerContext { }

	// This is deliberately a wire-shaped DTO.  The domain DTO below is kept
	// convenient for the reflection-free hydration code, while this envelope
	// makes the source-generated metadata part of the real read path (rather
	// than a serializer smoke test).  Semantic validation is still performed
	// by the strict JsonDocument pass immediately afterwards.
	internal sealed class GeneratedProjectEnvelopeDto {
		public int ProjectFormatVersion { get; set; }
		public string ProjectName { get; set; }
		public JsonElement Settings { get; set; }
		public JsonElement Graph { get; set; }
		public JsonElement LogicalControls { get; set; }
		public JsonElement ControlMappings { get; set; }
		public JsonElement Presets { get; set; }
		public JsonElement MediaAssets { get; set; }
		public JsonElement Ui { get; set; }
	}

	internal sealed class RawJsonObjectStringConverter : JsonConverter<string> {
		public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
			if (reader.TokenType == JsonTokenType.String) return reader.GetString();
			using (var value = JsonDocument.ParseValue(ref reader)) {
				if (value.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Raw node state must be a JSON object.");
				return value.RootElement.GetRawText();
			}
		}

		public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) {
			if (string.IsNullOrWhiteSpace(value)) { writer.WriteStartObject(); writer.WriteEndObject(); return; }
			using (var document = JsonDocument.Parse(value)) {
				if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Raw node state must be a JSON object.");
				document.RootElement.WriteTo(writer);
			}
		}
	}

	public sealed class ProjectDocumentDto {
		public int ProjectFormatVersion { get; set; }
		public string ProjectName { get; set; }
		public SettingsDto Settings { get; set; } = new SettingsDto();
		public List<NodeDto> Nodes { get; set; } = new List<NodeDto>();
		public List<ConnectionDto> Connections { get; set; } = new List<ConnectionDto>();
		public List<LogicalControlDto> LogicalControls { get; set; } = new List<LogicalControlDto>();
		public List<ControlMappingDto> ControlMappings { get; set; } = new List<ControlMappingDto>();
		public List<PresetDto> Presets { get; set; } = new List<PresetDto>();
		public List<MediaAssetDto> MediaAssets { get; set; } = new List<MediaAssetDto>();
		public UiDto Ui { get; set; } = new UiDto();
	}

	public sealed class SettingsDto {
		public string DynamicRange { get; set; }
		public int ProgramDisplay { get; set; }
	}

	public sealed class NodeDto {
		public string Id { get; set; }
		public string TypeId { get; set; }
		public int SchemaVersion { get; set; }
		public string DisplayName { get; set; }
		public bool Enabled { get; set; }
		public float X { get; set; }
		public float Y { get; set; }
		public bool SystemOwned { get; set; }
		public bool UserAddable { get; set; }
		[JsonConverter(typeof(RawJsonObjectStringConverter))]
		public string RawState { get; set; }
		public List<ParameterDto> Parameters { get; set; } = new List<ParameterDto>();
		public List<PortDto> Ports { get; set; } = new List<PortDto>();
	}

	public sealed class ParameterDto {
		public string Id { get; set; }
		public string DisplayName { get; set; }
		public string Type { get; set; }
		public ValueDto DefaultValue { get; set; }
		public ValueDto BaseValue { get; set; }
		public ValueDto HardMinimum { get; set; }
		public ValueDto HardMaximum { get; set; }
		public List<string> EnumOptionIds { get; set; } = new List<string>();
		public bool RuntimeStateful { get; set; }
		public bool IsBroken { get; set; }
		public string BrokenReason { get; set; }
		public ExpressionNodeDto Expression { get; set; }
		public ValueDto OutputMinimum { get; set; }
		public ValueDto OutputMaximum { get; set; }
	}

	public sealed class ValueDto {
		public string Type { get; set; }
		public float FloatValue { get; set; }
		public int IntValue { get; set; }
		public bool BoolValue { get; set; }
		public float[] Components { get; set; }
		public string TextValue { get; set; }
	}

	public sealed class PortDto {
		public string Id { get; set; }
		public string Direction { get; set; }
		public string Type { get; set; }
		public bool Required { get; set; }
		public string DefaultImage { get; set; }
	}

	public sealed class ConnectionDto {
		public string Id { get; set; }
		public string SourceNodeId { get; set; }
		public string SourcePortId { get; set; }
		public string DestinationNodeId { get; set; }
		public string DestinationPortId { get; set; }
		public string ConversionId { get; set; }
		public bool IsBroken { get; set; }
		public string BrokenReason { get; set; }
	}

	public sealed class LogicalControlDto {
		public string Id { get; set; }
		public string Name { get; set; }
		public string Kind { get; set; }
		public float InitialValue { get; set; }
		public string PresetId { get; set; }
		public bool PresetIsBroken { get; set; }
		public string BrokenReason { get; set; }
		public List<LogicalTargetDto> Targets { get; set; } = new List<LogicalTargetDto>();
		// Domain hydration attaches root mappings to their owning control for
		// convenience.  They are not serialized here; the top-level array is
		// the sole persisted representation.
		[JsonIgnore]
		public List<ControlMappingDto> Mappings { get; set; } = new List<ControlMappingDto>();
	}

	public sealed class LogicalTargetDto {
		public string NodeId { get; set; }
		public string ParameterId { get; set; }
		public string ParameterType { get; set; }
		public ValueDto Minimum { get; set; }
		public ValueDto Maximum { get; set; }
		public bool Invert { get; set; }
		public bool IsBroken { get; set; }
		public string BrokenReason { get; set; }
	}

	public sealed class ControlMappingDto {
		public string LogicalControlId { get; set; }
		public string Kind { get; set; }
		public string PhysicalId { get; set; }
		public string ControlPath { get; set; }
		public float RawMin { get; set; }
		public float RawMax { get; set; }
		public bool Invert { get; set; }
		public bool IsBroken { get; set; }
		public string BrokenReason { get; set; }
	}

	public sealed class ExpressionNodeDto {
		public string Kind { get; set; }
		public string ControlId { get; set; }
		public string Reason { get; set; }
		public string Operator { get; set; }
		public ExpressionNodeDto Left { get; set; }
		public ExpressionNodeDto Right { get; set; }
	}

	public sealed class PresetDto {
		public string Id { get; set; }
		public string Name { get; set; }
		public string Category { get; set; }
		public int SortIndex { get; set; }
		public List<PresetEntryDto> Entries { get; set; } = new List<PresetEntryDto>();
	}

	public sealed class PresetEntryDto {
		public string NodeId { get; set; }
		public string ParameterId { get; set; }
		public string ParameterType { get; set; }
		public ValueDto Value { get; set; }
		public bool IsBroken { get; set; }
		public string BrokenReason { get; set; }
	}

	public sealed class MediaAssetDto {
		public string Id { get; set; }
		public string DisplayName { get; set; }
		public string RelativePath { get; set; }
		public long ByteSize { get; set; }
		public string IntegrityAlgorithm { get; set; }
		public string IntegrityHash { get; set; }
		public string Kind { get; set; }
		public string ColorSpace { get; set; }
		public string AlphaMode { get; set; }
	}

	public sealed class UiDto {
		public List<DashboardPageDto> DashboardPages { get; set; } = new List<DashboardPageDto>();
		public List<string> PreviewNodeIds { get; set; } = new List<string>();
	}

	public sealed class DashboardPageDto {
		public string PageId { get; set; }
		public string Name { get; set; }
		public List<DashboardWidgetDto> Widgets { get; set; } = new List<DashboardWidgetDto>();
	}

	public sealed class DashboardWidgetDto {
		public string WidgetId { get; set; }
		public string NodeId { get; set; }
		public string ParameterId { get; set; }
		public int Column { get; set; }
		public int Row { get; set; }
		public int Width { get; set; }
		public int Height { get; set; }
		public string Label { get; set; }
		public bool IsBroken { get; set; }
		public string BrokenReason { get; set; }
	}

	public static class ProjectSerializer {
		public static Result<string, Diagnostic> Serialize(ProjectSaveSnapshot snapshot) {
			if (snapshot == null) return Failure("persistence.snapshot.invalid", "Save snapshot is required.");
			try {
				var dto = ToDto(snapshot);
				var json = WriteCanonical(dto);
				var bytes = new UTF8Encoding(false, true).GetBytes(json);
				// The canonical writer owns stable ordering and opaque state,
				// while the source-generated wire contract performs the actual
				// schema-shaped serialization/read-back at this boundary.
				var wire = JsonSerializer.Deserialize(bytes, ProjectPersistenceJsonContext.Default.GeneratedProjectEnvelopeDto);
				if (wire == null) return Failure("persistence.serialize_failed", "Generated manifest metadata could not read the canonical output.");
				_ = JsonSerializer.SerializeToUtf8Bytes(wire, ProjectPersistenceJsonContext.Default.GeneratedProjectEnvelopeDto);
				if (bytes.Length > PersistenceConstants.MaxManifestBytes) return Failure("persistence.manifest_too_large", "project.json exceeds the 64 MiB limit.");
				return Result.Success<string, Diagnostic>(json);
			}
			catch (Exception exception) {
				return Result.Failure<string, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.serialize_failed"), Severity.Error, exception.Message, exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		public static Result<ProjectDocumentDto, Diagnostic> Deserialize(string json) => DeserializeCore(json, true);

		/// <summary>
		/// Reads the strict JSON envelope without applying the current-format gate.
		/// The project-format migration coordinator is the only caller that should
		/// use this entry point for a future version.
		/// </summary>
		public static Result<ProjectDocumentDto, Diagnostic> DeserializeAnyVersion(string json) => DeserializeCore(json, false);

		private static Result<ProjectDocumentDto, Diagnostic> DeserializeCore(string json, bool requireCurrentVersion) {
			if (json == null) return FailureDto("persistence.read.empty", "Manifest content is required.");
			try {
				if (json.Length > 0 && json[0] == '\uFEFF') json = json.Substring(1);
				var bytes = new UTF8Encoding(false, true).GetBytes(json);
				if (bytes.Length > PersistenceConstants.MaxManifestBytes) return FailureDto("persistence.manifest_too_large", "project.json exceeds the 64 MiB limit.");
				// Parse through the source-generated metadata first.  The
				// wire-shaped envelope ensures this is a real deserialization
				// path while the strict DOM pass below enforces required
				// properties, duplicate rejection, and opaque state rules.
				var generated = JsonSerializer.Deserialize(bytes, ProjectPersistenceJsonContext.Default.GeneratedProjectEnvelopeDto);
				if (generated == null) return FailureDto("persistence.json_invalid", "The manifest is empty.");
				var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 });
				RejectDuplicateProperties(document.RootElement);
				var dto = ParseDocument(document.RootElement);
				if (requireCurrentVersion && dto.ProjectFormatVersion != PersistenceConstants.CurrentProjectFormatVersion)
					return FailureDto("persistence.format_unsupported", "The project format version is not supported by this build.");
				return Result.Success<ProjectDocumentDto, Diagnostic>(dto);
			}
			catch (JsonException exception) {
				return FailureDto("persistence.json_invalid", exception.Message);
			}
			catch (DecoderFallbackException exception) {
				return FailureDto("persistence.utf8_invalid", exception.Message);
			}
			catch (Exception exception) {
				return FailureDto("persistence.read_invalid", exception.Message);
			}
		}

		public static Result<ProjectDocumentDto, Diagnostic> Deserialize(byte[] bytes) {
			if (bytes == null) return FailureDto("persistence.read.empty", "Manifest content is required.");
			if (bytes.Length > PersistenceConstants.MaxManifestBytes) return FailureDto("persistence.manifest_too_large", "project.json exceeds the 64 MiB limit.");
			var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
			try {
				var text = new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
				return Deserialize(text);
			}
			catch (DecoderFallbackException exception) {
				return FailureDto("persistence.utf8_invalid", exception.Message);
			}
		}

		public static Result<ProjectDocumentDto, Diagnostic> DeserializeAnyVersion(byte[] bytes) {
			if (bytes == null) return FailureDto("persistence.read.empty", "Manifest content is required.");
			if (bytes.Length > PersistenceConstants.MaxManifestBytes) return FailureDto("persistence.manifest_too_large", "project.json exceeds the 64 MiB limit.");
			var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
			try {
				return DeserializeAnyVersion(new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset));
			}
			catch (DecoderFallbackException exception) {
				return FailureDto("persistence.utf8_invalid", exception.Message);
			}
		}

		public static ProjectDocumentDto ToDto(ProjectSaveSnapshot snapshot) {
			return new ProjectDocumentDto {
				ProjectFormatVersion = snapshot.ProjectFormatVersion,
				ProjectName = snapshot.ProjectName,
				Settings = new SettingsDto { DynamicRange = snapshot.Settings?.DynamicRange.ToString(), ProgramDisplay = snapshot.Settings?.ProgramDisplay ?? ProjectOutputSettings.DefaultProgramDisplay },
				Nodes = snapshot.Nodes.OrderBy(x => x.Id.Value, StringComparer.Ordinal).Select(x => ToDto(x, snapshot.Expressions)).ToList(),
				Connections = snapshot.Connections.OrderBy(x => x.SourceNodeId.Value, StringComparer.Ordinal).ThenBy(x => x.SourcePortId.Value, StringComparer.Ordinal).ThenBy(x => x.DestinationNodeId.Value, StringComparer.Ordinal).ThenBy(x => x.DestinationPortId.Value, StringComparer.Ordinal).ThenBy(x => x.Id.Value, StringComparer.Ordinal).Select(ToDto).ToList(),
				LogicalControls = snapshot.LogicalControls.OrderBy(x => x.Id.Value, StringComparer.Ordinal).Select(ToDto).ToList(),
				ControlMappings = snapshot.LogicalControls.OrderBy(x => x.Id.Value, StringComparer.Ordinal).SelectMany(x => x.Mappings.OrderBy(y => y.Kind).ThenBy(y => y.PhysicalId, StringComparer.Ordinal).ThenBy(y => y.ControlPath, StringComparer.Ordinal).Select(y => ToDto(x.Id, y))).ToList(),
				Presets = snapshot.Presets.OrderBy(x => x.Id.Value, StringComparer.Ordinal).Select(ToDto).ToList(),
				MediaAssets = snapshot.MediaAssets.OrderBy(x => x.Id.Value, StringComparer.Ordinal).Select(ToDto).ToList(),
				Ui = ToDto(snapshot.Ui)
			};
		}

		internal static ProjectDocumentDto CloneDto(ProjectDocumentDto source) {
			if (source == null) return null;
			var json = WriteCanonical(source);
			var parsed = DeserializeAnyVersion(json);
			if (parsed.IsFailure) throw new InvalidDataException(parsed.Error.Message);
			return parsed.Value;
		}

		public static ProjectDocumentDto ParseDocument(JsonElement root) {
			RequireObject(root, "project");
			RejectUnknownProperties(root, "projectFormatVersion", "projectName", "settings", "graph", "logicalControls", "controlMappings", "presets", "mediaAssets", "ui");
			var dto = new ProjectDocumentDto { ProjectFormatVersion = Required(root, "projectFormatVersion").GetInt32(), ProjectName = Required(root, "projectName").GetString() };
			var settings = Required(root, "settings");
			RequireObject(settings, "settings");
			RejectUnknownProperties(settings, "dynamicRange", "programDisplay");
			dto.Settings = new SettingsDto { DynamicRange = Optional<string>(settings, "dynamicRange", ProjectDynamicRange.Hdr.ToString()), ProgramDisplay = Optional(settings, "programDisplay", ProjectOutputSettings.DefaultProgramDisplay) };
			var graph = Required(root, "graph");
			RequireObject(graph, "graph");
			RejectUnknownProperties(graph, "nodes", "connections");
			foreach (var node in Required(graph, "nodes").EnumerateArray()) dto.Nodes.Add(ParseNode(node));
			foreach (var connection in Required(graph, "connections").EnumerateArray()) dto.Connections.Add(ParseConnection(connection));
			var controls = Required(root, "logicalControls");
			foreach (var control in controls.EnumerateArray()) dto.LogicalControls.Add(ParseLogicalControl(control));
			// The controlMappings array is intentionally independent from the
			// logical-control records.  Older DTOs did not hydrate it yet, but
			// the root requirement is enforced so a malformed project cannot
			// silently lose mappings.
			var mappingKeys = new HashSet<string>(StringComparer.Ordinal);
			foreach (var mapping in Required(root, "controlMappings").EnumerateArray()) {
				var parsed = ParseMapping(mapping);
				var mappingKey = parsed.LogicalControlId + "\u001f" + parsed.Kind + "\u001f" + parsed.PhysicalId + "\u001f" + parsed.ControlPath;
				if (!mappingKeys.Add(mappingKey)) throw new JsonException("Duplicate control mapping.");
				dto.ControlMappings.Add(parsed);
				var owner = dto.LogicalControls.FirstOrDefault(x => string.Equals(x.Id, parsed.LogicalControlId, StringComparison.Ordinal));
				if (owner == null) throw new JsonException("Control mapping references a missing logical control.");
				owner.Mappings.Add(parsed);
			}
			var presets = Required(root, "presets");
			foreach (var preset in presets.EnumerateArray()) dto.Presets.Add(ParsePreset(preset));
			var assets = Required(root, "mediaAssets");
			foreach (var asset in assets.EnumerateArray()) dto.MediaAssets.Add(ParseMedia(asset));
			dto.Ui = ParseUi(Required(root, "ui"));
			return dto;
		}

		private static NodeDto ToDto(NodeRecord node, IReadOnlyList<ParameterExpressionRecord> expressions) => new NodeDto {
			Id = node.Id.Value,
			TypeId = node.IsUnknown ? node.Unknown.OriginalNodeTypeId.Value : node.TypeId.Value,
			SchemaVersion = node.IsUnknown ? node.Unknown.OriginalSchemaVersion : node.SchemaVersion,
			DisplayName = node.DisplayName,
			Enabled = node.Enabled,
			X = node.Position.X,
			Y = node.Position.Y,
			SystemOwned = node.SystemOwned,
			UserAddable = node.UserAddable,
			RawState = node.IsUnknown ? node.Unknown.RawJsonValue : node.RawState,
			Parameters = node.Parameters.OrderBy(x => x.Definition.Id.Value, StringComparer.Ordinal).Select(x => { var expression = (expressions ?? new List<ParameterExpressionRecord>()).FirstOrDefault(y => y.NodeId == node.Id && y.ParameterId == x.Definition.Id); return new ParameterDto { Id = x.Definition.Id.Value, DisplayName = x.Definition.DisplayName, Type = x.Definition.Type.ToString(), DefaultValue = ToDto(x.Definition.DefaultValue), BaseValue = ToDto(x.BaseValue), HardMinimum = x.Definition.HardRange.HasValue ? ToDto(x.Definition.HardRange.Value.Minimum) : null, HardMaximum = x.Definition.HardRange.HasValue ? ToDto(x.Definition.HardRange.Value.Maximum) : null, EnumOptionIds = x.Definition.EnumOptionIds.OrderBy(y => y.Value, StringComparer.Ordinal).Select(y => y.Value).ToList(), RuntimeStateful = x.Definition.RuntimeStateful, IsBroken = x.IsBroken, BrokenReason = x.BrokenReason, Expression = expression == null ? null : ToDto(expression.Expression), OutputMinimum = expression?.OutputRange.HasValue == true ? ToDto(expression.OutputRange.Value.Minimum) : null, OutputMaximum = expression?.OutputRange.HasValue == true ? ToDto(expression.OutputRange.Value.Maximum) : null }; }).ToList(),
			Ports = node.Ports.OrderBy(x => x.Id.Value, StringComparer.Ordinal).Select(x => new PortDto { Id = x.Id.Value, Direction = x.Direction.ToString(), Type = x.Type.ToString(), Required = x.Required, DefaultImage = x.DefaultImage?.ToString() }).ToList()
		};

		private static ConnectionDto ToDto(ConnectionRecord x) => new ConnectionDto { Id = x.Id.Value, SourceNodeId = x.SourceNodeId.Value, SourcePortId = x.SourcePortId.Value, DestinationNodeId = x.DestinationNodeId.Value, DestinationPortId = x.DestinationPortId.Value, ConversionId = x.ConversionId, IsBroken = x.IsBroken, BrokenReason = x.BrokenReason };
		private static LogicalControlDto ToDto(LogicalControlRecord x) => new LogicalControlDto { Id = x.Id.Value, Name = x.Name, Kind = x.Kind.ToString(), InitialValue = x.InitialValue, PresetId = x.PresetId?.Value, PresetIsBroken = x.PresetIsBroken, BrokenReason = x.BrokenReason, Targets = x.Targets.OrderBy(y => y.NodeId.Value, StringComparer.Ordinal).ThenBy(y => y.ParameterId.Value, StringComparer.Ordinal).Select(y => new LogicalTargetDto { NodeId = y.NodeId.Value, ParameterId = y.ParameterId.Value, ParameterType = y.ParameterType.ToString(), Minimum = ToDto(y.TargetMin), Maximum = ToDto(y.TargetMax), Invert = y.Invert, IsBroken = y.IsBroken, BrokenReason = y.BrokenReason }).ToList(), Mappings = new List<ControlMappingDto>() };
		private static ControlMappingDto ToDto(LogicalControlId owner, ControlMappingRecord y) => new ControlMappingDto { LogicalControlId = owner.Value, Kind = y.Kind.ToString(), PhysicalId = y.PhysicalId, ControlPath = y.ControlPath, RawMin = y.RawMin, RawMax = y.RawMax, Invert = y.Invert, IsBroken = y.IsBroken, BrokenReason = y.BrokenReason };
		private static ExpressionNodeDto ToDto(LogicalExpressionNode x) {
			if (x is LogicalControlLeaf control) return new ExpressionNodeDto { Kind = "Control", ControlId = control.ControlId.Value };
			if (x is BaseValueLeaf) return new ExpressionNodeDto { Kind = "Base" };
			if (x is BrokenExpressionLeaf broken) return new ExpressionNodeDto { Kind = "Broken", ControlId = broken.OriginalControlId.Value, Reason = broken.Reason };
			if (x is BinaryLogicalExpression binary) return new ExpressionNodeDto { Kind = "Binary", Operator = binary.Operator.ToString(), Left = ToDto(binary.Left), Right = ToDto(binary.Right) };
			throw new InvalidDataException("Unsupported expression node.");
		}
		private static PresetDto ToDto(PresetRecord x) => new PresetDto { Id = x.Id.Value, Name = x.Name, Category = x.Category, SortIndex = x.SortIndex, Entries = x.Entries.OrderBy(y => y.NodeId.Value, StringComparer.Ordinal).ThenBy(y => y.ParameterId.Value, StringComparer.Ordinal).Select(y => new PresetEntryDto { NodeId = y.NodeId.Value, ParameterId = y.ParameterId.Value, ParameterType = y.ParameterType.ToString(), Value = ToDto(y.Value), IsBroken = y.IsBroken, BrokenReason = y.BrokenReason }).ToList() };
		private static MediaAssetDto ToDto(MediaAssetRecord x) => new MediaAssetDto { Id = x.Id.Value, DisplayName = x.DisplayName, RelativePath = x.RelativePath, ByteSize = x.ByteSize, IntegrityAlgorithm = x.IntegrityAlgorithm, IntegrityHash = x.IntegrityHash, Kind = x.Kind.ToString(), ColorSpace = x.ColorSpace.ToString(), AlphaMode = x.AlphaMode.ToString() };
		private static UiDto ToDto(ProjectUiStateRecord x) => new UiDto { PreviewNodeIds = x?.PreviewNodeIds?.ToList() ?? new List<string>(), DashboardPages = x?.DashboardPages?.OrderBy(y => y.PageId, StringComparer.Ordinal).Select(y => new DashboardPageDto { PageId = y.PageId, Name = y.Name, Widgets = y.Widgets.OrderBy(z => z.WidgetId, StringComparer.Ordinal).Select(z => new DashboardWidgetDto { WidgetId = z.WidgetId, NodeId = z.NodeId.Value, ParameterId = z.ParameterId.Value, Column = z.Column, Row = z.Row, Width = z.Width, Height = z.Height, Label = z.Label, IsBroken = z.IsBroken, BrokenReason = z.BrokenReason }).ToList() }).ToList() ?? new List<DashboardPageDto>() };

		private static ValueDto ToDto(ParameterValue value) {
			var dto = new ValueDto { Type = value.Type.ToString() };
			switch (value.Type) {
				case ParameterType.Float: dto.FloatValue = value.AsFloat(); break;
				case ParameterType.Int: dto.IntValue = value.AsInt(); break;
				case ParameterType.Bool: dto.BoolValue = value.AsBool(); break;
				case ParameterType.Vector2: var v2 = value.AsVector2(); dto.Components = new[] { v2.X, v2.Y }; break;
				case ParameterType.Vector3: var v3 = value.AsVector3(); dto.Components = new[] { v3.X, v3.Y, v3.Z }; break;
				case ParameterType.Vector4: var v4 = value.AsVector4(); dto.Components = new[] { v4.X, v4.Y, v4.Z, v4.W }; break;
				case ParameterType.Color: var c = value.AsColor(); dto.Components = new[] { c.R, c.G, c.B, c.A }; break;
				default: dto.TextValue = value.AsString(); break;
			}
			return dto;
		}

		private static string WriteCanonical(ProjectDocumentDto dto) {
			var b = new StringBuilder(4096);
			b.Append("{\n  \"projectFormatVersion\":").Append(dto.ProjectFormatVersion.ToString(CultureInfo.InvariantCulture)).Append(",\n  \"projectName\":"); String(b, dto.ProjectName); b.Append(",\n  \"settings\":"); WriteSettings(b, dto.Settings); b.Append(",\n  \"graph\":{\n    \"nodes\":[");
			for (var i = 0; i < dto.Nodes.Count; i++) { if (i > 0) b.Append(','); b.Append('\n'); Indent(b, 6); WriteNode(b, dto.Nodes[i]); }
			if (dto.Nodes.Count > 0) b.Append("\n  "); b.Append("] ,\n    \"connections\":[");
			for (var i = 0; i < dto.Connections.Count; i++) { if (i > 0) b.Append(','); b.Append('\n'); Indent(b, 6); WriteConnection(b, dto.Connections[i]); }
			if (dto.Connections.Count > 0) b.Append("\n  "); b.Append("]\n  },\n  \"logicalControls\":[");
			for (var i = 0; i < dto.LogicalControls.Count; i++) { if (i > 0) b.Append(','); b.Append('\n'); Indent(b, 4); WriteLogicalControl(b, dto.LogicalControls[i]); }
			if (dto.LogicalControls.Count > 0) b.Append("\n  "); b.Append("],\n  \"controlMappings\":[");
			for (var i = 0; i < dto.ControlMappings.Count; i++) { if (i > 0) b.Append(','); b.Append('\n'); Indent(b, 4); WriteMapping(b, dto.ControlMappings[i]); }
			if (dto.ControlMappings.Count > 0) b.Append("\n  "); b.Append("],\n  \"presets\":[");
			for (var i = 0; i < dto.Presets.Count; i++) { if (i > 0) b.Append(','); b.Append('\n'); Indent(b, 4); WritePreset(b, dto.Presets[i]); }
			if (dto.Presets.Count > 0) b.Append("\n  "); b.Append("],\n  \"mediaAssets\":[");
			for (var i = 0; i < dto.MediaAssets.Count; i++) { if (i > 0) b.Append(','); b.Append('\n'); Indent(b, 4); WriteMedia(b, dto.MediaAssets[i]); }
			if (dto.MediaAssets.Count > 0) b.Append("\n  "); b.Append("],\n  \"ui\":"); WriteUi(b, dto.Ui); b.Append("\n}\n");
			return b.ToString().Replace("] ,", "],");
		}

		private static void WriteNode(StringBuilder b, NodeDto x) {
			b.Append('{'); Prop(b, "id", x.Id); Prop(b, "nodeTypeId", x.TypeId); Prop(b, "schemaVersion", x.SchemaVersion); Prop(b, "displayName", x.DisplayName); Prop(b, "enabled", x.Enabled); Prop(b, "position", "{\"x\":" + Number(x.X) + ",\"y\":" + Number(x.Y) + "}", true); Prop(b, "parameters", x.Parameters, WriteParameters); Prop(b, "ports", x.Ports, WritePorts); if (x.SystemOwned) Prop(b, "systemOwned", true); if (!x.UserAddable) Prop(b, "userAddable", false); if (!ValidObject(x.RawState)) throw new InvalidDataException("Node state must be a JSON object."); PropRaw(b, "state", x.RawState);
			b.Append('}');
		}

		private static void WriteConnection(StringBuilder b, ConnectionDto x) { b.Append('{'); Prop(b, "connectionId", x.Id); Prop(b, "sourceNodeId", x.SourceNodeId); Prop(b, "sourcePortId", x.SourcePortId); Prop(b, "destinationNodeId", x.DestinationNodeId); Prop(b, "destinationPortId", x.DestinationPortId); if (!string.IsNullOrEmpty(x.ConversionId)) Prop(b, "conversionId", x.ConversionId); if (x.IsBroken) { Prop(b, "isBroken", true); Prop(b, "brokenReason", x.BrokenReason); } b.Append('}'); }
		private static void WriteParameters(StringBuilder b, List<ParameterDto> values) { b.Append('['); for (var i = 0; i < values.Count; i++) { if (i > 0) b.Append(','); WriteParameter(b, values[i]); } b.Append(']'); }
		private static void WritePorts(StringBuilder b, List<PortDto> values) { b.Append('['); for (var i = 0; i < values.Count; i++) { if (i > 0) b.Append(','); var x = values[i]; b.Append('{'); Prop(b, "portId", x.Id); Prop(b, "direction", x.Direction); Prop(b, "portTypeId", x.Type); Prop(b, "required", x.Required); if (!string.IsNullOrEmpty(x.DefaultImage)) Prop(b, "defaultImage", x.DefaultImage); b.Append('}'); } b.Append(']'); }
		private static void WriteParameter(StringBuilder b, ParameterDto x) { b.Append('{'); Prop(b, "parameterId", x.Id); Prop(b, "displayName", x.DisplayName); Prop(b, "type", x.Type); Prop(b, "defaultValue", x.DefaultValue, WriteValue); Prop(b, "baseValue", x.BaseValue, WriteValue); if (x.HardMinimum != null && x.HardMaximum != null) { Prop(b, "hardMinimum", x.HardMinimum, WriteValue); Prop(b, "hardMaximum", x.HardMaximum, WriteValue); } if (x.EnumOptionIds.Count > 0) Prop(b, "enumOptionIds", x.EnumOptionIds, (s, v) => { s.Append('['); for (var i = 0; i < v.Count; i++) { if (i > 0) s.Append(','); String(s, v[i]); } s.Append(']'); }); if (x.RuntimeStateful) Prop(b, "runtimeStateful", true); if (x.IsBroken) { Prop(b, "isBroken", true); Prop(b, "brokenReason", x.BrokenReason); } if (x.Expression != null) Prop(b, "expression", x.Expression, WriteExpressionNode); if (x.OutputMinimum != null && x.OutputMaximum != null) { Prop(b, "outputMinimum", x.OutputMinimum, WriteValue); Prop(b, "outputMaximum", x.OutputMaximum, WriteValue); } b.Append('}'); }
		private static void WriteValue(StringBuilder b, ValueDto x) { b.Append('{'); Prop(b, "type", x.Type); if (x.Components != null) { Prop(b, "value", "{\"x\":" + Number(x.Components[0]) + (x.Components.Length > 1 ? ",\"y\":" + Number(x.Components[1]) : string.Empty) + (x.Components.Length > 2 ? ",\"z\":" + Number(x.Components[2]) : string.Empty) + (x.Components.Length > 3 ? ",\"w\":" + Number(x.Components[3]) : string.Empty) + "}", true); } else if (x.Type == ParameterType.Float.ToString()) Prop(b, "value", Number(x.FloatValue), true); else if (x.Type == ParameterType.Int.ToString()) Prop(b, "value", x.IntValue.ToString(CultureInfo.InvariantCulture), true); else if (x.Type == ParameterType.Bool.ToString()) Prop(b, "value", x.BoolValue); else if (x.Type == ParameterType.MediaAssetReference.ToString() && string.IsNullOrEmpty(x.TextValue)) PropNull(b, "value"); else Prop(b, "value", x.TextValue); b.Append('}'); }
		private static void WriteLogicalControl(StringBuilder b, LogicalControlDto x) { b.Append('{'); Prop(b, "logicalControlId", x.Id); Prop(b, "name", x.Name); Prop(b, "kind", x.Kind); if (x.Kind == LogicalControlKind.Value.ToString()) Prop(b, "initialValue", Number(x.InitialValue), true); if (!string.IsNullOrEmpty(x.PresetId)) { Prop(b, "presetId", x.PresetId); if (x.PresetIsBroken) Prop(b, "presetIsBroken", true); } if (!string.IsNullOrEmpty(x.BrokenReason)) Prop(b, "brokenReason", x.BrokenReason); Prop(b, "targets", x.Targets, WriteTargets); b.Append('}'); }
		private static void WriteTargets(StringBuilder b, List<LogicalTargetDto> values) { b.Append('['); for (var i = 0; i < values.Count; i++) { if (i > 0) b.Append(','); var x = values[i]; b.Append('{'); Prop(b, "nodeId", x.NodeId); Prop(b, "parameterId", x.ParameterId); Prop(b, "parameterType", x.ParameterType); Prop(b, "minimum", x.Minimum, WriteValue); Prop(b, "maximum", x.Maximum, WriteValue); if (x.Invert) Prop(b, "invert", true); if (x.IsBroken) { Prop(b, "isBroken", true); Prop(b, "brokenReason", x.BrokenReason); } b.Append('}'); } b.Append(']'); }
		private static void WriteMappings(StringBuilder b, List<ControlMappingDto> values) { b.Append('['); for (var i = 0; i < values.Count; i++) { if (i > 0) b.Append(','); var x = values[i]; b.Append('{'); Prop(b, "kind", x.Kind); Prop(b, "physicalId", x.PhysicalId); Prop(b, "controlPath", x.ControlPath); Prop(b, "rawMin", Number(x.RawMin), true); Prop(b, "rawMax", Number(x.RawMax), true); if (x.Invert) Prop(b, "invert", true); if (x.IsBroken) { Prop(b, "isBroken", true); Prop(b, "brokenReason", x.BrokenReason); } b.Append('}'); } b.Append(']'); }
		private static void WriteSettings(StringBuilder b, SettingsDto x) { x = x ?? new SettingsDto { DynamicRange = ProjectDynamicRange.Hdr.ToString(), ProgramDisplay = ProjectOutputSettings.DefaultProgramDisplay }; b.Append('{'); Prop(b, "dynamicRange", x.DynamicRange); Prop(b, "programDisplay", x.ProgramDisplay); b.Append('}'); }
		private static void WriteMapping(StringBuilder b, ControlMappingDto x) { b.Append('{'); Prop(b, "logicalControlId", x.LogicalControlId); Prop(b, "kind", x.Kind); Prop(b, "physicalId", x.PhysicalId); Prop(b, "controlPath", x.ControlPath); Prop(b, "rawMin", Number(x.RawMin), true); Prop(b, "rawMax", Number(x.RawMax), true); if (x.Invert) Prop(b, "invert", true); if (x.IsBroken) { Prop(b, "isBroken", true); Prop(b, "brokenReason", x.BrokenReason); } b.Append('}'); }
		private static void WriteExpressionNode(StringBuilder b, ExpressionNodeDto x) {
			b.Append('{'); Prop(b, "kind", x.Kind);
			if (!string.IsNullOrEmpty(x.ControlId)) Prop(b, "controlId", x.ControlId);
			if (!string.IsNullOrEmpty(x.Reason)) Prop(b, "reason", x.Reason);
			if (!string.IsNullOrEmpty(x.Operator)) Prop(b, "operator", x.Operator);
			if (x.Left != null) Prop(b, "left", x.Left, WriteExpressionNode);
			if (x.Right != null) Prop(b, "right", x.Right, WriteExpressionNode);
			b.Append('}');
		}
		private static void WritePreset(StringBuilder b, PresetDto x) { b.Append('{'); Prop(b, "presetId", x.Id); Prop(b, "name", x.Name); Prop(b, "category", x.Category); Prop(b, "sortIndex", x.SortIndex); Prop(b, "entries", x.Entries, (s, v) => { s.Append('['); for (var i = 0; i < v.Count; i++) { if (i > 0) s.Append(','); var e = v[i]; s.Append('{'); Prop(s, "nodeId", e.NodeId); Prop(s, "parameterId", e.ParameterId); Prop(s, "parameterType", e.ParameterType); Prop(s, "value", e.Value, WriteValue); if (e.IsBroken) { Prop(s, "isBroken", true); Prop(s, "brokenReason", e.BrokenReason); } s.Append('}'); } s.Append(']'); }); b.Append('}'); }
		private static void WriteMedia(StringBuilder b, MediaAssetDto x) { b.Append('{'); Prop(b, "mediaAssetId", x.Id); Prop(b, "displayName", x.DisplayName); Prop(b, "relativePath", x.RelativePath); Prop(b, "byteSize", x.ByteSize); Prop(b, "integrityAlgorithm", x.IntegrityAlgorithm); Prop(b, "integrityHash", x.IntegrityHash); Prop(b, "kind", x.Kind); Prop(b, "colorSpace", x.ColorSpace); Prop(b, "alphaMode", x.AlphaMode); b.Append('}'); }
		private static void WriteUi(StringBuilder b, UiDto x) { x = x ?? new UiDto(); b.Append('{'); Prop(b, "dashboardPages", x.DashboardPages, (s, v) => { s.Append('['); for (var i = 0; i < v.Count; i++) { if (i > 0) s.Append(','); var p = v[i]; s.Append('{'); Prop(s, "pageId", p.PageId); Prop(s, "name", p.Name); Prop(s, "widgets", p.Widgets, (ss, ww) => { ss.Append('['); for (var j = 0; j < ww.Count; j++) { if (j > 0) ss.Append(','); var w = ww[j]; ss.Append('{'); Prop(ss, "widgetId", w.WidgetId); Prop(ss, "nodeId", w.NodeId); Prop(ss, "parameterId", w.ParameterId); Prop(ss, "column", w.Column); Prop(ss, "row", w.Row); Prop(ss, "width", w.Width); Prop(ss, "height", w.Height); if (!string.IsNullOrEmpty(w.Label)) Prop(ss, "label", w.Label); if (w.IsBroken) { Prop(ss, "isBroken", true); Prop(ss, "brokenReason", w.BrokenReason); } ss.Append('}'); } ss.Append(']'); }); s.Append('}'); } s.Append(']'); }); Prop(b, "previewNodeIds", x.PreviewNodeIds, (s, v) => { s.Append('['); for (var i = 0; i < v.Count; i++) { if (i > 0) s.Append(','); String(s, v[i]); } s.Append(']'); }); b.Append('}'); }

		private static void Prop(StringBuilder b, string name, string value) { Prefix(b, name); String(b, value); }
		private static void Prop(StringBuilder b, string name, bool value) { Prefix(b, name); b.Append(value ? "true" : "false"); }
		private static void Prop(StringBuilder b, string name, int value) { Prefix(b, name); b.Append(value.ToString(CultureInfo.InvariantCulture)); }
		private static void Prop(StringBuilder b, string name, long value) { Prefix(b, name); b.Append(value.ToString(CultureInfo.InvariantCulture)); }
		private static void Prop(StringBuilder b, string name, string raw, bool rawValue) { Prefix(b, name); b.Append(rawValue ? raw : Quote(raw)); }
		private static void PropRaw(StringBuilder b, string name, string raw) { Prefix(b, name); b.Append(raw); }
		private static void PropNull(StringBuilder b, string name) { Prefix(b, name); b.Append("null"); }
		private static void Prop<T>(StringBuilder b, string name, T value, Action<StringBuilder, T> writer) { Prefix(b, name); writer(b, value); }
		private static void Prefix(StringBuilder b, string name) { var index = b.Length - 1; while (index >= 0 && char.IsWhiteSpace(b[index])) index--; if (index >= 0 && b[index] != '{') b.Append(','); String(b, name); b.Append(':'); }
		private static void Indent(StringBuilder b, int count) { b.Append(' ', count); }
		private static void String(StringBuilder b, string value) { b.Append(Quote(value)); }
		private static string Quote(string value) {
			if (value == null) return "null";
			var b = new StringBuilder(value.Length + 2); b.Append('"');
			foreach (var c in value) {
				switch (c) { case '"': b.Append("\\\""); break; case '\\': b.Append("\\\\"); break; case '\b': b.Append("\\b"); break; case '\f': b.Append("\\f"); break; case '\n': b.Append("\\n"); break; case '\r': b.Append("\\r"); break; case '\t': b.Append("\\t"); break; default: if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture)); else b.Append(c); break; }
			}
			return b.Append('"').ToString();
		}
		private static string Number(float value) { if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentException("JSON numbers must be finite."); return value.ToString("G9", CultureInfo.InvariantCulture); }
		private static bool ValidObject(string raw) { try { return !string.IsNullOrWhiteSpace(raw) && JsonDocument.Parse(raw).RootElement.ValueKind == JsonValueKind.Object; } catch { return false; } }

		private static NodeDto ParseNode(JsonElement e) {
			RequireObject(e, "node");
			RejectUnknownProperties(e, "id", "nodeTypeId", "schemaVersion", "displayName", "enabled", "position", "parameters", "ports", "state", "systemOwned", "userAddable");
			var position = Required(e, "position");
			RequireObject(position, "position");
			RejectUnknownProperties(position, "x", "y");
			var result = new NodeDto { Id = Required(e, "id").GetString(), TypeId = Required(e, "nodeTypeId").GetString(), SchemaVersion = Required(e, "schemaVersion").GetInt32(), DisplayName = Required(e, "displayName").GetString(), Enabled = Required(e, "enabled").GetBoolean(), X = Required(position, "x").GetSingle(), Y = Required(position, "y").GetSingle(), RawState = Required(e, "state").GetRawText(), SystemOwned = Optional(e, "systemOwned", false), UserAddable = Optional(e, "userAddable", true) };
			RequireObject(JsonDocument.Parse(result.RawState).RootElement, "state");
			if (Try(e, "parameters", out var parameters)) foreach (var p in parameters.EnumerateArray()) result.Parameters.Add(ParseParameter(p));
			if (Try(e, "ports", out var ports)) foreach (var p in ports.EnumerateArray()) result.Ports.Add(ParsePort(p));
			return result;
		}
		private static ParameterDto ParseParameter(JsonElement e) { RequireObject(e, "parameter"); RejectUnknownProperties(e, "parameterId", "displayName", "type", "defaultValue", "baseValue", "hardMinimum", "hardMaximum", "enumOptionIds", "runtimeStateful", "isBroken", "brokenReason", "expression", "outputMinimum", "outputMaximum"); var result = new ParameterDto { Id = Required(e, "parameterId").GetString(), DisplayName = Required(e, "displayName").GetString(), Type = Required(e, "type").GetString(), DefaultValue = ParseValue(Required(e, "defaultValue")), BaseValue = ParseValue(Required(e, "baseValue")), HardMinimum = Try(e, "hardMinimum", out var min) ? ParseValue(min) : null, HardMaximum = Try(e, "hardMaximum", out var max) ? ParseValue(max) : null, RuntimeStateful = Optional(e, "runtimeStateful", false), IsBroken = Optional(e, "isBroken", false), BrokenReason = Optional<string>(e, "brokenReason", null), Expression = Try(e, "expression", out var expression) ? ParseExpressionNode(expression) : null, OutputMinimum = Try(e, "outputMinimum", out var outputMinimum) ? ParseValue(outputMinimum) : null, OutputMaximum = Try(e, "outputMaximum", out var outputMaximum) ? ParseValue(outputMaximum) : null }; if (Try(e, "enumOptionIds", out var options)) result.EnumOptionIds = options.EnumerateArray().Select(x => x.GetString()).ToList(); ValidateParameterValueType(result.DefaultValue, result.Type); ValidateParameterValueType(result.BaseValue, result.Type); ValidateParameterValueType(result.HardMinimum, result.Type); ValidateParameterValueType(result.HardMaximum, result.Type); ValidateParameterValueType(result.OutputMinimum, result.Type); ValidateParameterValueType(result.OutputMaximum, result.Type); if ((result.HardMinimum == null) != (result.HardMaximum == null)) throw new JsonException("Parameter hard range must provide both bounds."); if (result.Expression == null && (result.OutputMinimum != null || result.OutputMaximum != null)) throw new JsonException("Parameter output range requires an expression."); if ((result.OutputMinimum == null) != (result.OutputMaximum == null)) throw new JsonException("Parameter expression output range must provide both bounds."); return result; }
		private static void ValidateParameterValueType(ValueDto value, string parameterType) { if (value != null && !string.Equals(value.Type, parameterType, StringComparison.Ordinal)) throw new JsonException("Parameter value type does not match the declared parameter type."); }
		private static PortDto ParsePort(JsonElement e) { RequireObject(e, "port"); RejectUnknownProperties(e, "portId", "direction", "portTypeId", "required", "defaultImage"); return new PortDto { Id = Required(e, "portId").GetString(), Direction = Required(e, "direction").GetString(), Type = Required(e, "portTypeId").GetString(), Required = Required(e, "required").GetBoolean(), DefaultImage = Optional<string>(e, "defaultImage", null) }; }
		private static ValueDto ParseValue(JsonElement e) { RequireObject(e, "value"); RejectUnknownProperties(e, "type", "value"); var result = new ValueDto { Type = Required(e, "type").GetString() }; if (!e.TryGetProperty("value", out var value) || value.ValueKind == JsonValueKind.Undefined) throw new JsonException("Required property is missing: value"); if (value.ValueKind == JsonValueKind.Null) { if (result.Type != ParameterType.MediaAssetReference.ToString()) throw new JsonException("Only an unselected media reference may be null."); return result; } switch (result.Type) { case "Float": result.FloatValue = value.GetSingle(); break; case "Int": result.IntValue = value.GetInt32(); break; case "Bool": result.BoolValue = value.GetBoolean(); break; case "Vector2": case "Vector3": case "Vector4": case "Color": RequireObject(value, "vector value"); var count = result.Type == "Vector2" ? 2 : result.Type == "Vector3" ? 3 : 4; RejectUnknownProperties(value, count == 2 ? new[] { "x", "y" } : count == 3 ? new[] { "x", "y", "z" } : new[] { "x", "y", "z", "w" }); result.Components = new[] { Required(value, "x").GetSingle(), Optional(value, "y", 0f), Optional(value, "z", 0f), Optional(value, "w", 0f) }.Take(count).ToArray(); break; case "MediaAssetReference": result.TextValue = value.GetString(); if (string.IsNullOrEmpty(result.TextValue)) throw new JsonException("An unselected media reference must use explicit null."); if (!MediaAssetId.TryParseUuidV4(result.TextValue, out _)) throw new JsonException("A selected media reference must be a UUID v4."); break; default: result.TextValue = value.GetString(); break; } return result; }
		private static ConnectionDto ParseConnection(JsonElement e) { RequireObject(e, "connection"); RejectUnknownProperties(e, "connectionId", "sourceNodeId", "sourcePortId", "destinationNodeId", "destinationPortId", "conversionId", "isBroken", "brokenReason"); return new ConnectionDto { Id = Required(e, "connectionId").GetString(), SourceNodeId = Required(e, "sourceNodeId").GetString(), SourcePortId = Required(e, "sourcePortId").GetString(), DestinationNodeId = Required(e, "destinationNodeId").GetString(), DestinationPortId = Required(e, "destinationPortId").GetString(), ConversionId = Optional<string>(e, "conversionId", null), IsBroken = Optional(e, "isBroken", false), BrokenReason = Optional<string>(e, "brokenReason", null) }; }
		private static LogicalControlDto ParseLogicalControl(JsonElement e) { RequireObject(e, "logicalControl"); RejectUnknownProperties(e, "logicalControlId", "name", "kind", "initialValue", "presetId", "presetIsBroken", "brokenReason", "targets"); var x = new LogicalControlDto { Id = Required(e, "logicalControlId").GetString(), Name = Required(e, "name").GetString(), Kind = Required(e, "kind").GetString(), InitialValue = Optional(e, "initialValue", 0f), PresetId = Optional<string>(e, "presetId", null), PresetIsBroken = Optional(e, "presetIsBroken", false), BrokenReason = Optional<string>(e, "brokenReason", null) }; if (Try(e, "targets", out var targets)) foreach (var t in targets.EnumerateArray()) { RequireObject(t, "logical target"); RejectUnknownProperties(t, "nodeId", "parameterId", "parameterType", "minimum", "maximum", "invert", "isBroken", "brokenReason"); x.Targets.Add(new LogicalTargetDto { NodeId = Required(t, "nodeId").GetString(), ParameterId = Required(t, "parameterId").GetString(), ParameterType = Required(t, "parameterType").GetString(), Minimum = ParseValue(Required(t, "minimum")), Maximum = ParseValue(Required(t, "maximum")), Invert = Optional(t, "invert", false), IsBroken = Optional(t, "isBroken", false), BrokenReason = Optional<string>(t, "brokenReason", null) }); } return x; }
		private static ControlMappingDto ParseMapping(JsonElement e) { RequireObject(e, "control mapping"); RejectUnknownProperties(e, "logicalControlId", "kind", "physicalId", "controlPath", "rawMin", "rawMax", "invert", "isBroken", "brokenReason"); return new ControlMappingDto { LogicalControlId = Required(e, "logicalControlId").GetString(), Kind = Required(e, "kind").GetString(), PhysicalId = Required(e, "physicalId").GetString(), ControlPath = Required(e, "controlPath").GetString(), RawMin = Required(e, "rawMin").GetSingle(), RawMax = Required(e, "rawMax").GetSingle(), Invert = Optional(e, "invert", false), IsBroken = Optional(e, "isBroken", false), BrokenReason = Optional<string>(e, "brokenReason", null) }; }
		private static ExpressionNodeDto ParseExpressionNode(JsonElement e) {
			RequireObject(e, "expression node");
			RejectUnknownProperties(e, "kind", "controlId", "reason", "operator", "left", "right");
			var x = new ExpressionNodeDto { Kind = Required(e, "kind").GetString(), ControlId = Optional<string>(e, "controlId", null), Reason = Optional<string>(e, "reason", null), Operator = Optional<string>(e, "operator", null) };
			if (Try(e, "left", out var left)) x.Left = ParseExpressionNode(left);
			if (Try(e, "right", out var right)) x.Right = ParseExpressionNode(right);
			return x;
		}
		private static PresetDto ParsePreset(JsonElement e) { RequireObject(e, "preset"); RejectUnknownProperties(e, "presetId", "name", "category", "sortIndex", "entries"); var x = new PresetDto { Id = Required(e, "presetId").GetString(), Name = Required(e, "name").GetString(), Category = Optional(e, "category", string.Empty), SortIndex = Optional(e, "sortIndex", 0) }; if (Try(e, "entries", out var entries)) foreach (var item in entries.EnumerateArray()) { RequireObject(item, "preset entry"); RejectUnknownProperties(item, "nodeId", "parameterId", "parameterType", "value", "isBroken", "brokenReason"); x.Entries.Add(new PresetEntryDto { NodeId = Required(item, "nodeId").GetString(), ParameterId = Required(item, "parameterId").GetString(), ParameterType = Required(item, "parameterType").GetString(), Value = ParseValue(Required(item, "value")), IsBroken = Optional(item, "isBroken", false), BrokenReason = Optional<string>(item, "brokenReason", null) }); } return x; }
		private static MediaAssetDto ParseMedia(JsonElement e) { RequireObject(e, "media asset"); RejectUnknownProperties(e, "mediaAssetId", "displayName", "relativePath", "byteSize", "integrityAlgorithm", "integrityHash", "kind", "colorSpace", "alphaMode"); return new MediaAssetDto { Id = Required(e, "mediaAssetId").GetString(), DisplayName = Required(e, "displayName").GetString(), RelativePath = Required(e, "relativePath").GetString(), ByteSize = Required(e, "byteSize").GetInt64(), IntegrityAlgorithm = Required(e, "integrityAlgorithm").GetString(), IntegrityHash = Required(e, "integrityHash").GetString(), Kind = Required(e, "kind").GetString(), ColorSpace = Required(e, "colorSpace").GetString(), AlphaMode = Required(e, "alphaMode").GetString() }; }
		private static UiDto ParseUi(JsonElement e) { RequireObject(e, "ui"); RejectUnknownProperties(e, "previewNodeIds", "dashboardPages"); var x = new UiDto(); if (Try(e, "previewNodeIds", out var ids)) x.PreviewNodeIds = ids.EnumerateArray().Select(y => y.GetString()).ToList(); if (Try(e, "dashboardPages", out var pages)) foreach (var p in pages.EnumerateArray()) { RequireObject(p, "dashboard page"); RejectUnknownProperties(p, "pageId", "name", "widgets"); var page = new DashboardPageDto { PageId = Required(p, "pageId").GetString(), Name = Required(p, "name").GetString() }; if (Try(p, "widgets", out var widgets)) foreach (var w in widgets.EnumerateArray()) { RequireObject(w, "dashboard widget"); RejectUnknownProperties(w, "widgetId", "nodeId", "parameterId", "column", "row", "width", "height", "label", "isBroken", "brokenReason"); page.Widgets.Add(new DashboardWidgetDto { WidgetId = Required(w, "widgetId").GetString(), NodeId = Required(w, "nodeId").GetString(), ParameterId = Required(w, "parameterId").GetString(), Column = Required(w, "column").GetInt32(), Row = Required(w, "row").GetInt32(), Width = Required(w, "width").GetInt32(), Height = Required(w, "height").GetInt32(), Label = Optional<string>(w, "label", null), IsBroken = Optional(w, "isBroken", false), BrokenReason = Optional<string>(w, "brokenReason", null) }); } x.DashboardPages.Add(page); } return x; }

		private static JsonElement Required(JsonElement e, string name) { if (!e.TryGetProperty(name, out var value)) throw new JsonException("Required property is missing: " + name); if (value.ValueKind == JsonValueKind.Null || value.ValueKind == JsonValueKind.Undefined) throw new JsonException("Required property is null: " + name); return value; }
		private static bool Try(JsonElement e, string name, out JsonElement value) => e.TryGetProperty(name, out value);
		private static T Optional<T>(JsonElement e, string name, T fallback) { if (!e.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) return fallback; return (T)Convert.ChangeType(value.ToString(), typeof(T), CultureInfo.InvariantCulture); }
		private static bool Optional(JsonElement e, string name, bool fallback) => !e.TryGetProperty(name, out var value) ? fallback : value.GetBoolean();
		private static int Optional(JsonElement e, string name, int fallback) => !e.TryGetProperty(name, out var value) ? fallback : value.GetInt32();
		private static float Optional(JsonElement e, string name, float fallback) => !e.TryGetProperty(name, out var value) ? fallback : value.GetSingle();
		private static void RequireObject(JsonElement e, string name) { if (e.ValueKind != JsonValueKind.Object) throw new JsonException(name + " must be an object."); }
		private static void RejectDuplicateProperties(JsonElement element) {
			if (element.ValueKind == JsonValueKind.Object) {
				var names = new HashSet<string>(StringComparer.Ordinal);
				foreach (var property in element.EnumerateObject()) { if (!names.Add(property.Name)) throw new JsonException("Duplicate property: " + property.Name); if (property.Name == "state" || property.Name == "rawJsonValue") continue; RejectDuplicateProperties(property.Value); }
			}
			else if (element.ValueKind == JsonValueKind.Array) foreach (var value in element.EnumerateArray()) RejectDuplicateProperties(value);
		}
		private static void RejectUnknownProperties(JsonElement element, params string[] allowed) {
			RequireObject(element, "object");
			var known = new HashSet<string>(allowed ?? Array.Empty<string>(), StringComparer.Ordinal);
			foreach (var property in element.EnumerateObject())
				if (!known.Contains(property.Name)) throw new JsonException("Unknown property: " + property.Name);
		}
		private static Result<string, Diagnostic> Failure(string code, string message) => Result.Failure<string, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
		private static Result<ProjectDocumentDto, Diagnostic> FailureDto(string code, string message) => Result.Failure<ProjectDocumentDto, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
	}

	public interface IProjectFileSystem {
		bool Exists(string path);
		string GetFullPath(string path);
		FileAttributes GetAttributes(string path);
		byte[] ReadAllBytes(string path);
		void WriteAllBytes(string path, byte[] bytes);
		void EnsureDirectory(string path);
		void AtomicReplace(string temporaryPath, string mainPath, string backupPath, bool backupMain);
		IEnumerable<string> EnumerateFiles(string directory);
		void CopyFile(string sourcePath, string destinationPath, bool overwrite);
		void Delete(string path);
	}

	/// <summary>
	/// Platform durability port.  It is separate from the minimal file
	/// contract so fault-injection fakes can opt in explicitly and a writer
	/// never mistakes WriteAllBytes/handle close for durable persistence.
	/// </summary>
	public interface IProjectDurableFileSystem {
		void Flush(string path);
	}

	/// <summary>Same-directory atomic file rename used by media import.</summary>
	public interface IProjectAtomicFileOperations {
		void AtomicMove(string sourcePath, string destinationPath);
	}

	/// <summary>
	/// Platform boundary for committing project.json.  Implementations must
	/// provide the platform's verified same-volume atomic operation; callers
	/// must not fall back to a delete/copy sequence when this port is absent.
	/// </summary>
	public interface IAtomicManifestWriter {
		void Replace(IProjectFileSystem fileSystem, string temporaryPath, string mainPath, string backupPath, bool backupMain);
	}

	/// <summary>Streaming file boundary used by large media imports.</summary>
	public interface IProjectStreamingFileOperations {
		Stream OpenRead(string path);
		Stream OpenWrite(string path, bool overwrite);
	}

	internal static class ProjectFileSystemPorts {
		public static void Flush(IProjectFileSystem fileSystem, string path) {
			var durable = fileSystem as IProjectDurableFileSystem;
			if (durable == null) throw new IOException("The file system does not provide a durable flush port.");
			durable.Flush(path);
		}

		public static void AtomicMove(IProjectFileSystem fileSystem, string sourcePath, string destinationPath) {
			var atomic = fileSystem as IProjectAtomicFileOperations;
			if (atomic == null) throw new IOException("The file system does not provide an atomic file rename port.");
			atomic.AtomicMove(sourcePath, destinationPath);
		}

		public static IAtomicManifestWriter ManifestWriter(IProjectFileSystem fileSystem) {
			var writer = fileSystem as IAtomicManifestWriter;
			if (writer == null) throw new IOException("The file system does not provide an atomic manifest writer port.");
			return writer;
		}
	}

	/// <summary>
	/// Windows uses one same-volume Replace operation for main and backup.
	/// </summary>
	public sealed class WindowsAtomicManifestWriter : IAtomicManifestWriter {
		public void Replace(IProjectFileSystem fileSystem, string temporaryPath, string mainPath, string backupPath, bool backupMain) {
			if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));
			fileSystem.AtomicReplace(temporaryPath, mainPath, backupPath, backupMain);
		}
	}

	/// <summary>
	/// macOS first makes a verified, flushed backup copy and atomically
	/// promotes that copy, then performs the same-directory manifest rename.
	/// </summary>
	public sealed class MacOsAtomicManifestWriter : IAtomicManifestWriter {
		public void Replace(IProjectFileSystem fileSystem, string temporaryPath, string mainPath, string backupPath, bool backupMain) {
			if (fileSystem == null) throw new ArgumentNullException(nameof(fileSystem));
			if (backupMain && fileSystem.Exists(mainPath)) {
				var backupTemporary = backupPath + ".macos-copy-" + Guid.NewGuid().ToString("N");
				try {
					fileSystem.CopyFile(mainPath, backupTemporary, true);
					ProjectFileSystemPorts.Flush(fileSystem, backupTemporary);
					var original = fileSystem.ReadAllBytes(mainPath);
					var copied = fileSystem.ReadAllBytes(backupTemporary);
					if (!original.SequenceEqual(copied)) throw new IOException("The verified backup copy did not match the current manifest.");
					// Replace the backup in place without creating a second
					// persistent sidecar.  The manifest folder has only the
					// documented project.json/.bak/.tmp entries.
					fileSystem.AtomicReplace(backupTemporary, backupPath, null, false);
					backupTemporary = null;
				}
				finally {
					if (backupTemporary != null) try { fileSystem.Delete(backupTemporary); } catch { }
				}
			}

			// The final operation is a same-directory atomic promotion.  A
			// corrupt main is handled by the filesystem adapter without
			// overwriting an existing valid backup.
			fileSystem.AtomicReplace(temporaryPath, mainPath, backupPath, false);
		}
	}

	public sealed class LocalProjectFileSystem : IProjectFileSystem, IProjectDurableFileSystem, IProjectAtomicFileOperations, IProjectStreamingFileOperations, IAtomicManifestWriter, IProjectDirectoryOperations, IProjectDirectoryCleanup {
		private readonly IAtomicManifestWriter _manifestWriter;
		public LocalProjectFileSystem() { _manifestWriter = Path.DirectorySeparatorChar == '\\' ? (IAtomicManifestWriter)new WindowsAtomicManifestWriter() : new MacOsAtomicManifestWriter(); }
		public bool Exists(string path) => File.Exists(path);
		public string GetFullPath(string path) => Path.GetFullPath(path);
		public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
		public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
		public void WriteAllBytes(string path, byte[] bytes) => File.WriteAllBytes(path, bytes);
		public Stream OpenRead(string path) => new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64 * 1024, FileOptions.SequentialScan);
		public Stream OpenWrite(string path, bool overwrite) => new FileStream(path, overwrite ? FileMode.Create : FileMode.CreateNew, FileAccess.Write, FileShare.None, 64 * 1024, FileOptions.SequentialScan);
		public void Flush(string path) {
			using (var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read))
				stream.Flush(true);
		}
		public void EnsureDirectory(string path) => Directory.CreateDirectory(path);
		public void AtomicMove(string sourcePath, string destinationPath) => File.Move(sourcePath, destinationPath);
		public void CopyFile(string sourcePath, string destinationPath, bool overwrite) => File.Copy(sourcePath, destinationPath, overwrite);
		public void Delete(string path) { if (File.Exists(path)) File.Delete(path); }
		public IEnumerable<string> EnumerateFiles(string directory) => Directory.Exists(directory) ? Directory.EnumerateFiles(directory) : Enumerable.Empty<string>();
		public bool DirectoryExists(string path) => Directory.Exists(path);
		public void MoveDirectory(string sourcePath, string destinationPath) => Directory.Move(sourcePath, destinationPath);
		public void DeleteDirectory(string path) { if (Directory.Exists(path)) Directory.Delete(path, true); }
		public void AtomicReplace(string temporaryPath, string mainPath, string backupPath, bool backupMain) {
			if (File.Exists(mainPath)) {
				// File.Replace with a null backup is still one atomic
				// same-volume operation.  This is required for a corrupt
				// Main too: moving it aside first creates a crash gap and
				// must never be used as an atomicity fallback.
				File.Replace(temporaryPath, mainPath, backupMain ? backupPath : null, true);
				return;
			}
			// Initial save has no destination, so same-directory rename is
			// the platform atomic promotion.
			File.Move(temporaryPath, mainPath);
		}
		public void Replace(IProjectFileSystem fileSystem, string temporaryPath, string mainPath, string backupPath, bool backupMain) => _manifestWriter.Replace(fileSystem, temporaryPath, mainPath, backupPath, backupMain);
	}

	/// <summary>
	/// Optional directory operations used by portable Save As.  Keeping them
	/// separate preserves the small fault-injection IProjectFileSystem contract
	/// used by EditMode tests.
	/// </summary>
	public interface IProjectDirectoryOperations {
		bool DirectoryExists(string path);
		void MoveDirectory(string sourcePath, string destinationPath);
	}

	public interface IProjectDirectoryCleanup {
		void DeleteDirectory(string path);
	}

	public sealed class MediaAssetImportResult {
		public MediaAssetRecord Asset { get; }
		public string TemporaryPath { get; }
		internal MediaAssetImportResult(MediaAssetRecord asset, string temporaryPath) { Asset = asset; TemporaryPath = temporaryPath; }
	}

	/// <summary>
	/// Optional codec/image validation boundary.  Implementations receive
	/// only the staged bytes and extension; a user-selected absolute source
	/// path never crosses into diagnostics or project state.
	/// </summary>
	public interface IMediaAssetProbe {
		UnitResult<Diagnostic> Probe(Stream stagedStream, string extension);
	}

	internal sealed class StreamDigest {
		public long Length { get; }
		public string Hash { get; }
		public StreamDigest(long length, string hash) { Length = length; Hash = hash; }
	}

	internal static class ProjectStreamIntegrity {
		internal static StreamDigest CopyAndHash(Stream source, Stream destination) {
			if (source == null || destination == null) throw new ArgumentNullException();
			var hash = new XxHash128();
			var buffer = new byte[64 * 1024];
			long length = 0;
			int read;
			while ((read = source.Read(buffer, 0, buffer.Length)) > 0) {
				destination.Write(buffer, 0, read);
				hash.Append(new ReadOnlySpan<byte>(buffer, 0, read));
				length += read;
			}
			return new StreamDigest(length, FormatDigest(hash.GetHashAndReset()));
		}

		internal static StreamDigest Hash(Stream stream) {
			if (stream == null) throw new ArgumentNullException(nameof(stream));
			var hash = new XxHash128();
			var buffer = new byte[64 * 1024];
			long length = 0;
			int read;
			while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) {
				hash.Append(new ReadOnlySpan<byte>(buffer, 0, read));
				length += read;
			}
			return new StreamDigest(length, FormatDigest(hash.GetHashAndReset()));
		}

		private static string FormatDigest(ReadOnlySpan<byte> digest) {
			var builder = new StringBuilder(digest.Length * 2);
			foreach (var value in digest) builder.Append(value.ToString("x2", CultureInfo.InvariantCulture));
			return builder.ToString();
		}
	}

	public enum MediaAssetImportTransactionStage {
		Copy,
		Verify,
		Probe,
		AwaitingProbeConfirmation,
		Rename,
		Completed,
		Failed,
		Cancelled
	}

	public sealed class MediaAssetImportProgress {
		public MediaAssetImportTransactionStage Stage { get; }
		public MediaAssetRecord Asset { get; }
		public Diagnostic Diagnostic { get; }
		public bool IsCompleted => Stage == MediaAssetImportTransactionStage.Completed;
		public bool IsFailed => Stage == MediaAssetImportTransactionStage.Failed || Stage == MediaAssetImportTransactionStage.Cancelled;
		internal MediaAssetImportProgress(MediaAssetImportTransactionStage stage, MediaAssetRecord asset = null, Diagnostic diagnostic = null) { Stage = stage; Asset = asset; Diagnostic = diagnostic; }
	}

	/// <summary>
	/// Deterministic, one-stage-at-a-time media copy transaction.  The
	/// Application owns scheduling and calls Step from its main-thread pump;
	/// this type owns streaming I/O, integrity, probe and atomic rename.
	/// </summary>
	public sealed class MediaAssetImportTransaction {
		private readonly string _sourcePath;
		private readonly string _projectRoot;
		private readonly IProjectFileSystem _fileSystem;
		private readonly IProjectStreamingFileOperations _streaming;
		private readonly IMediaAssetProbe _probe;
		private readonly string _displayName;
		private readonly MediaAssetKind _kind;
		private readonly MediaColorSpace _colorSpace;
		private readonly MediaAlphaMode _alphaMode;
		private readonly MediaAssetId _id;
		private readonly string _relativePath;
		private readonly string _extension;
		private readonly string _temporaryPath;
		private readonly string _finalPath;
		private readonly string _assetDirectory;
		private StreamDigest _sourceDigest;
		private MediaAssetImportTransactionStage _stage;
		private Diagnostic _probeWarning;
		private bool _cleaned;

		public MediaAssetImportTransactionStage Stage => _stage;
		public MediaAssetRecord Asset { get; private set; }

		public MediaAssetImportTransaction(string sourcePath, string projectRoot, IProjectFileSystem fileSystem, string displayName,
			MediaAssetKind kind = MediaAssetKind.Experimental, MediaColorSpace colorSpace = MediaColorSpace.SRgb,
			MediaAlphaMode alphaMode = MediaAlphaMode.Opaque, IMediaAssetProbe probe = null) {
			if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(projectRoot) || fileSystem == null) throw new ArgumentException("Source path, project root and file system are required.");
			_streaming = fileSystem as IProjectStreamingFileOperations ?? throw new IOException("Media import requires the streaming file port.");
			_sourcePath = sourcePath; _projectRoot = projectRoot; _fileSystem = fileSystem; _probe = probe; _displayName = displayName ?? string.Empty;
			_kind = kind; _colorSpace = colorSpace; _alphaMode = alphaMode;
			_extension = Path.GetExtension(sourcePath);
			if (string.IsNullOrWhiteSpace(_extension) || _extension.Length > 16 || _extension.IndexOfAny(new[] { '/', '\\', '\0', ':' }) >= 0) throw new ArgumentException("Media source extension is invalid.", nameof(sourcePath));
			_id = MediaAssetId.New();
			var normalized = MediaPathRules.Normalize(_id, "Assets/" + _id.Value + "/source" + _extension.ToLowerInvariant());
			if (normalized.IsFailure) throw new InvalidDataException(normalized.Error.Message);
			_relativePath = normalized.Value;
			_assetDirectory = Path.Combine(projectRoot, "Assets", _id.Value);
			fileSystem.EnsureDirectory(_assetDirectory);
			_finalPath = Path.Combine(projectRoot, _relativePath.Replace('/', Path.DirectorySeparatorChar));
			_temporaryPath = _finalPath + ".importing";
			_stage = MediaAssetImportTransactionStage.Copy;
		}

		public MediaAssetImportProgress Step() {
			if (_stage == MediaAssetImportTransactionStage.AwaitingProbeConfirmation || _stage == MediaAssetImportTransactionStage.Completed || _stage == MediaAssetImportTransactionStage.Failed || _stage == MediaAssetImportTransactionStage.Cancelled)
				return new MediaAssetImportProgress(_stage, Asset, _probeWarning);
			try {
				switch (_stage) {
					case MediaAssetImportTransactionStage.Copy:
						using (var source = _streaming.OpenRead(_sourcePath))
						using (var staged = _streaming.OpenWrite(_temporaryPath, true))
							_sourceDigest = ProjectStreamIntegrity.CopyAndHash(source, staged);
						ProjectFileSystemPorts.Flush(_fileSystem, _temporaryPath);
						_stage = MediaAssetImportTransactionStage.Verify;
						return new MediaAssetImportProgress(MediaAssetImportTransactionStage.Copy);
					case MediaAssetImportTransactionStage.Verify:
						using (var staged = _streaming.OpenRead(_temporaryPath)) {
							var digest = ProjectStreamIntegrity.Hash(staged);
							if (digest.Length != _sourceDigest.Length || !string.Equals(digest.Hash, _sourceDigest.Hash, StringComparison.Ordinal)) throw new IOException("Imported media copy failed integrity verification.");
						}
						_stage = MediaAssetImportTransactionStage.Probe;
						return new MediaAssetImportProgress(MediaAssetImportTransactionStage.Verify);
					case MediaAssetImportTransactionStage.Probe:
						if (_probe != null) {
							using (var staged = _streaming.OpenRead(_temporaryPath)) {
								var result = _probe.Probe(staged, _extension);
								if (result.IsFailure) {
									if (result.Error != null && result.Error.Severity == Severity.Warning) {
										_probeWarning = result.Error;
										_stage = MediaAssetImportTransactionStage.AwaitingProbeConfirmation;
										return new MediaAssetImportProgress(MediaAssetImportTransactionStage.AwaitingProbeConfirmation, diagnostic: result.Error);
									}
									throw new InvalidDataException("Media probe rejected staged content.");
								}
							}
						}
						_stage = MediaAssetImportTransactionStage.Rename;
						return new MediaAssetImportProgress(MediaAssetImportTransactionStage.Probe);
					case MediaAssetImportTransactionStage.Rename:
						ProjectFileSystemPorts.AtomicMove(_fileSystem, _temporaryPath, _finalPath);
						ProjectFileSystemPorts.Flush(_fileSystem, _finalPath);
						using (var committed = _streaming.OpenRead(_finalPath)) {
							var digest = ProjectStreamIntegrity.Hash(committed);
							if (digest.Length != _sourceDigest.Length || !string.Equals(digest.Hash, _sourceDigest.Hash, StringComparison.Ordinal)) throw new IOException("Committed media copy failed integrity verification.");
						}
						Asset = new MediaAssetRecord(_id, _displayName, _relativePath, _sourceDigest.Length, _sourceDigest.Hash, _kind, _colorSpace, _alphaMode);
						_stage = MediaAssetImportTransactionStage.Completed;
						return new MediaAssetImportProgress(MediaAssetImportTransactionStage.Rename);
					default: throw new InvalidOperationException("Media import transaction is in an invalid state.");
				}
			}
			catch {
				_stage = MediaAssetImportTransactionStage.Failed;
				Cleanup();
				return new MediaAssetImportProgress(MediaAssetImportTransactionStage.Failed, diagnostic: Failure("persistence.media.import_failed", "Media import transaction failed."));
			}
		}

		public UnitResult<Diagnostic> ConfirmProbe(bool approved) {
			if (_stage != MediaAssetImportTransactionStage.AwaitingProbeConfirmation) return UnitResult.Failure<Diagnostic>(Failure("persistence.media.probe_confirmation_invalid", "This media import is not awaiting probe confirmation."));
			if (!approved) { _stage = MediaAssetImportTransactionStage.Failed; Cleanup(); return UnitResult.Failure<Diagnostic>(_probeWarning ?? Failure("persistence.media.probe_rejected", "Media probe confirmation was rejected.")); }
			_probeWarning = null; _stage = MediaAssetImportTransactionStage.Rename; return UnitResult.Success<Diagnostic>();
		}

		public void Cancel() {
			if (_stage == MediaAssetImportTransactionStage.Completed || _stage == MediaAssetImportTransactionStage.Failed || _stage == MediaAssetImportTransactionStage.Cancelled) return;
			_stage = MediaAssetImportTransactionStage.Cancelled;
			Cleanup();
		}

		private void Cleanup() {
			if (_cleaned) return;
			_cleaned = true;
			try { _fileSystem.Delete(_temporaryPath); } catch { }
			try { _fileSystem.Delete(_finalPath); } catch { }
			try { (_fileSystem as IProjectDirectoryCleanup)?.DeleteDirectory(_assetDirectory); } catch { }
		}

		private static Diagnostic Failure(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "persistence");
	}

	public static class MediaAssetStore {
		/// <summary>
		/// Imports a source file as an isolated copy transaction.  The source
		/// path is never included in the returned record or diagnostics.
		/// </summary>
		public static Result<MediaAssetImportResult, Diagnostic> Import(string sourcePath, string projectRoot, IProjectFileSystem fileSystem, string displayName, MediaAssetKind kind = MediaAssetKind.Experimental, MediaColorSpace colorSpace = MediaColorSpace.SRgb, MediaAlphaMode alphaMode = MediaAlphaMode.Opaque) {
			return Import(sourcePath, projectRoot, fileSystem, displayName, kind, colorSpace, alphaMode, null);
		}

		public static Result<MediaAssetImportResult, Diagnostic> Import(string sourcePath, string projectRoot, IProjectFileSystem fileSystem, string displayName, MediaAssetKind kind, MediaColorSpace colorSpace, MediaAlphaMode alphaMode, IMediaAssetProbe probe) {
			if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(projectRoot) || fileSystem == null) return Failure("persistence.media.import_invalid", "Source path, project root and file system are required.");
			var temporaryPath = (string)null;
			var finalPath = (string)null;
			try {
				var streaming = fileSystem as IProjectStreamingFileOperations;
				if (streaming == null) return Failure("persistence.media.streaming_unsupported", "Media import requires the streaming file port.");
				var extension = Path.GetExtension(sourcePath);
				if (string.IsNullOrWhiteSpace(extension) || extension.Length > 16 || extension.IndexOfAny(new[] { '/', '\\', '\0', ':' }) >= 0) return Failure("persistence.media.extension_invalid", "Media source extension is invalid.");
				var id = MediaAssetId.New();
				var relative = "Assets/" + id.Value + "/source" + extension.ToLowerInvariant();
				var normalized = MediaPathRules.Normalize(id, relative);
				if (normalized.IsFailure) return Result.Failure<MediaAssetImportResult, Diagnostic>(normalized.Error);
				var directory = Path.Combine(projectRoot, "Assets", id.Value);
				fileSystem.EnsureDirectory(directory);
				finalPath = Path.Combine(projectRoot, normalized.Value.Replace('/', Path.DirectorySeparatorChar));
				temporaryPath = finalPath + ".importing";
				StreamDigest sourceDigest;
				using (var source = streaming.OpenRead(sourcePath))
				using (var staged = streaming.OpenWrite(temporaryPath, true))
					sourceDigest = ProjectStreamIntegrity.CopyAndHash(source, staged);
				ProjectFileSystemPorts.Flush(fileSystem, temporaryPath);

				using (var staged = streaming.OpenRead(temporaryPath)) {
					var stagedDigest = ProjectStreamIntegrity.Hash(staged);
					if (stagedDigest.Length != sourceDigest.Length || !string.Equals(stagedDigest.Hash, sourceDigest.Hash, StringComparison.Ordinal)) throw new IOException("Imported media copy failed integrity verification.");
				}
				if (probe != null) {
					using (var staged = streaming.OpenRead(temporaryPath)) {
						var probeResult = probe.Probe(staged, extension);
						if (probeResult.IsFailure) throw new InvalidDataException("Media probe rejected staged content.");
					}
				}
				ProjectFileSystemPorts.AtomicMove(fileSystem, temporaryPath, finalPath);
				temporaryPath = null;
				ProjectFileSystemPorts.Flush(fileSystem, finalPath);
				using (var committed = streaming.OpenRead(finalPath)) {
					var committedDigest = ProjectStreamIntegrity.Hash(committed);
					if (committedDigest.Length != sourceDigest.Length || !string.Equals(committedDigest.Hash, sourceDigest.Hash, StringComparison.Ordinal)) throw new IOException("Committed media copy failed integrity verification.");
				}
				var asset = new MediaAssetRecord(id, displayName, normalized.Value, sourceDigest.Length, sourceDigest.Hash, kind, colorSpace, alphaMode);
				return Result.Success<MediaAssetImportResult, Diagnostic>(new MediaAssetImportResult(asset, null));
			}
			catch {
				try { if (temporaryPath != null) fileSystem.Delete(temporaryPath); if (finalPath != null) fileSystem.Delete(finalPath); } catch { }
				// Do not surface the user-selected absolute source path in a
				// persisted/exported diagnostic.  The operation remains
				// identifiable by its stable diagnostic code.
				return Result.Failure<MediaAssetImportResult, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.media.import_failed"), Severity.Error, "Media import transaction failed."));
			}
		}

		/// <summary>
		/// Completes the import transaction through the public project command
		/// path.  If the command is rejected, the already-renamed asset is
		/// removed and no catalog entry remains.
		/// </summary>
		public static Result<MediaAssetRecord, Diagnostic> ImportAndAdd(string sourcePath, string projectRoot, IProjectFileSystem fileSystem, ProjectDocument document, string displayName, MediaAssetKind kind = MediaAssetKind.Experimental, MediaColorSpace colorSpace = MediaColorSpace.SRgb, MediaAlphaMode alphaMode = MediaAlphaMode.Opaque, IMediaAssetProbe probe = null) {
			if (document == null) return Result.Failure<MediaAssetRecord, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.media.command_invalid"), Severity.Error, "A project document is required."));
			var imported = Import(sourcePath, projectRoot, fileSystem, displayName, kind, colorSpace, alphaMode, probe);
			if (imported.IsFailure) return Result.Failure<MediaAssetRecord, Diagnostic>(imported.Error);
			var command = new ProjectCommandProcessor(document).AddMediaAsset(imported.Value.Asset);
			if (command.IsFailure) {
				try { fileSystem.Delete(Path.Combine(projectRoot, imported.Value.Asset.RelativePath.Replace('/', Path.DirectorySeparatorChar))); } catch { }
				return Result.Failure<MediaAssetRecord, Diagnostic>(command.Error);
			}
			return Result.Success<MediaAssetRecord, Diagnostic>(imported.Value.Asset);
		}

		private static Result<MediaAssetImportResult, Diagnostic> Failure(string code, string message) => Result.Failure<MediaAssetImportResult, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
	}

	public enum ProjectLoadStatus { Loaded, Recovered, Migrated, Repaired }

	public sealed class ProjectLoadResult {
		public ProjectDocument Document { get; }
		public ProjectLoadStatus Status { get; }
		public IReadOnlyList<Diagnostic> Diagnostics { get; }
		public bool IsRecovered => Status == ProjectLoadStatus.Recovered;
		internal ProjectLoadResult(ProjectDocument document, ProjectLoadStatus status, IEnumerable<Diagnostic> diagnostics) { Document = document; Status = status; Diagnostics = diagnostics?.ToList().AsReadOnly() ?? new List<Diagnostic>().AsReadOnly(); }
	}

	public sealed class ProjectSaver {
		private int _saving;
		public UnitResult<Diagnostic> Save(ProjectDocument document, string projectRoot, IProjectFileSystem fileSystem, Action beforeReplace = null) {
			if (document == null || string.IsNullOrWhiteSpace(projectRoot) || fileSystem == null) return Failure("persistence.save_invalid", "Document, project root and file system are required.");
			if (IsReparsePoint(fileSystem, projectRoot)) return Failure("persistence.project_root_reparse_point", "Project root may not be a reparse point.");
			if (Interlocked.CompareExchange(ref _saving, 1, 0) != 0) return Failure("persistence.save_in_progress", "A save for this project is already running.");
			try {
				var savingToken = document.BeginSave();
				var snapshot = document.TryCreateSaveSnapshot();
				if (snapshot.IsFailure) return UnitResult.Failure<Diagnostic>(snapshot.Error);
				var saved = SaveSnapshot(snapshot.Value, projectRoot, fileSystem, beforeReplace);
				if (saved.IsFailure) return saved;
				document.TryMarkSaved(savingToken);
				return saved;
			}
			catch (Exception exception) {
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.save_failed"), Severity.Error, exception.Message, exception: DiagnosticExceptionInfo.FromException(exception)));
			}
			finally { Volatile.Write(ref _saving, 0); }
		}

		/// <summary>
		/// Persists an immutable snapshot without changing the owning
		/// document's SavedToken.  Save As uses this boundary while its
		/// staging directory is not yet the current project.
		/// </summary>
		internal UnitResult<Diagnostic> SaveSnapshot(ProjectSaveSnapshot snapshot, string projectRoot, IProjectFileSystem fileSystem, Action beforeReplace = null) {
			if (snapshot == null || string.IsNullOrWhiteSpace(projectRoot) || fileSystem == null) return Failure("persistence.save_invalid", "Snapshot, project root and file system are required.");
			if (IsReparsePoint(fileSystem, projectRoot)) return Failure("persistence.project_root_reparse_point", "Project root may not be a reparse point.");
			var stage = "manifest.paths";
			var stagePath = projectRoot;
			try {
				var main = Path.Combine(projectRoot, PersistenceConstants.MainFileName); var temp = Path.Combine(projectRoot, PersistenceConstants.TemporaryFileName); var backup = Path.Combine(projectRoot, PersistenceConstants.BackupFileName);
				stage = "manifest.adapter";
				stagePath = main;
				var manifestWriter = ProjectFileSystemPorts.ManifestWriter(fileSystem);
				stage = "serialize";
				stagePath = main;
				var json = ProjectSerializer.Serialize(snapshot);
				if (json.IsFailure) return FailureAt(stage, stagePath, json.Error);
				stage = "directory.ensure";
				stagePath = projectRoot;
				fileSystem.EnsureDirectory(projectRoot);
				stage = "tmp.write";
				stagePath = temp;
				fileSystem.WriteAllBytes(temp, new UTF8Encoding(false, true).GetBytes(json.Value));
				stage = "tmp.flush";
				stagePath = temp;
				ProjectFileSystemPorts.Flush(fileSystem, temp);
				stage = "tmp.readback";
				stagePath = temp;
				var readback = ProjectSerializer.Deserialize(fileSystem.ReadAllBytes(temp));
				if (readback.IsFailure) return FailureAt(stage, stagePath, readback.Error);
				stage = "main.validate";
				stagePath = main;
				beforeReplace?.Invoke();
				var mainIsValid = fileSystem.Exists(main) && ProjectSerializer.Deserialize(fileSystem.ReadAllBytes(main)).IsSuccess;
				stage = "manifest.replace";
				stagePath = main;
				manifestWriter.Replace(fileSystem, temp, main, backup, mainIsValid);
				return UnitResult.Success<Diagnostic>();
			}
			catch (Exception exception) {
				return FailureAt(stage, stagePath, exception);
			}
		}
		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
		private static UnitResult<Diagnostic> FailureAt(string stage, string path, Diagnostic diagnostic) {
			var message = "Save stage '" + stage + "' path '" + path + "' failed: " + (diagnostic == null ? "operation failed." : diagnostic.Message);
			return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.save_failed"), Severity.Error, message, exception: diagnostic?.Exception));
		}
		private static UnitResult<Diagnostic> FailureAt(string stage, string path, Exception exception) {
			var message = "Save stage '" + stage + "' path '" + path + "' failed: " + (exception == null ? "operation failed." : exception.Message);
			return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.save_failed"), Severity.Error, message, exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception)));
		}
		private static bool IsReparsePoint(IProjectFileSystem fileSystem, string path) { try { return (fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; } catch { return false; } }
	}

	public sealed class PortableProjectSaver {
		public UnitResult<Diagnostic> SaveAs(ProjectDocument document, string sourceRoot, string targetRoot, IProjectFileSystem fileSystem) {
			if (document == null || string.IsNullOrWhiteSpace(sourceRoot) || string.IsNullOrWhiteSpace(targetRoot) || fileSystem == null) return Failure("persistence.save_as_invalid", "Document, source root, target root and file system are required.");
			var directories = fileSystem as IProjectDirectoryOperations;
			if (directories == null) return Failure("persistence.save_as_atomic_unsupported", "The file system cannot atomically finalize a portable project directory.");
			var cleanup = fileSystem as IProjectDirectoryCleanup;
			if (cleanup == null) return Failure("persistence.save_as_cleanup_unsupported", "The file system cannot cleanup a failed portable staging directory.");
			var streaming = fileSystem as IProjectStreamingFileOperations;
			if (streaming == null) return Failure("persistence.save_as_streaming_unsupported", "Portable Save As requires the streaming file port.");
			if (IsReparsePoint(fileSystem, sourceRoot) || IsReparsePoint(fileSystem, targetRoot)) return Failure("persistence.save_as_reparse_point", "Save As roots may not be reparse points.");
			if (directories.DirectoryExists(targetRoot) || fileSystem.Exists(targetRoot)) return Failure("persistence.save_as_target_exists", "Save As target already exists.");
			var parent = Path.GetDirectoryName(fileSystem.GetFullPath(targetRoot));
			if (string.IsNullOrEmpty(parent)) parent = ".";
			var staging = Path.Combine(parent, "." + Path.GetFileName(targetRoot) + ".staging-" + Guid.NewGuid().ToString("N"));
			var finalized = false;
			try {
				// Capture the source snapshot and SavingToken before staging
				// starts.  Saving the staging copy must not make the source
				// document look saved until the final directory rename wins.
				var savingToken = document.BeginSave();
				var snapshot = document.TryCreateSaveSnapshot();
				if (snapshot.IsFailure) return UnitResult.Failure<Diagnostic>(snapshot.Error);
				fileSystem.EnsureDirectory(Path.Combine(staging, "Assets"));
				foreach (var asset in document.MediaAssets.OrderBy(x => x.Id.Value, StringComparer.Ordinal)) {
					var path = MediaPathRules.Normalize(asset.Id, asset.RelativePath);
					if (path.IsFailure) return UnitResult.Failure<Diagnostic>(path.Error);
					var source = Path.Combine(sourceRoot, path.Value.Replace('/', Path.DirectorySeparatorChar));
					var destination = Path.Combine(staging, path.Value.Replace('/', Path.DirectorySeparatorChar));
					var destinationDirectory = Path.GetDirectoryName(destination);
					fileSystem.EnsureDirectory(destinationDirectory);
					if (!fileSystem.Exists(source)) return Failure("persistence.save_as_media_missing", "A project asset is missing.");
					StreamDigest sourceDigest;
					using (var sourceStream = streaming.OpenRead(source))
					using (var destinationStream = streaming.OpenWrite(destination, true))
						sourceDigest = ProjectStreamIntegrity.CopyAndHash(sourceStream, destinationStream);
					if (sourceDigest.Length != asset.ByteSize || !string.Equals(sourceDigest.Hash, asset.IntegrityHash, StringComparison.Ordinal)) return Failure("persistence.save_as_media_replaced", "A project asset failed integrity verification.");
					ProjectFileSystemPorts.Flush(fileSystem, destination);
					using (var copiedStream = streaming.OpenRead(destination)) {
						var copied = ProjectStreamIntegrity.Hash(copiedStream);
						if (copied.Length != sourceDigest.Length || !string.Equals(copied.Hash, asset.IntegrityHash, StringComparison.Ordinal)) return Failure("persistence.save_as_media_copy_failed", "A project asset copy failed integrity verification.");
					}
				}
				var save = new ProjectSaver().SaveSnapshot(snapshot.Value, staging, fileSystem);
				if (save.IsFailure) return save;
				// Re-check immediately before finalization so a target that
				// appeared during staging is never overwritten.
				if (directories.DirectoryExists(targetRoot) || fileSystem.Exists(targetRoot)) return Failure("persistence.save_as_target_exists", "Save As target already exists.");
				directories.MoveDirectory(staging, targetRoot);
				finalized = true;
				document.TryMarkSaved(savingToken);
				return UnitResult.Success<Diagnostic>();
			}
			catch (Exception exception) {
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.save_as_failed"), Severity.Error, exception.Message, exception: DiagnosticExceptionInfo.FromException(exception)));
			}
			finally {
				// Only remove the private staging directory we generated;
				// source and target roots are never cleanup targets.
				if (!finalized) {
					try { if (directories.DirectoryExists(staging)) cleanup.DeleteDirectory(staging); } catch { }
				}
			}
		}

		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
		private static bool IsReparsePoint(IProjectFileSystem fileSystem, string path) { try { return (fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; } catch { return false; } }
	}

	public sealed class NewProjectResult {
		public ProjectDocument Document { get; }
		public string ProjectRoot { get; }
		internal NewProjectResult(ProjectDocument document, string projectRoot) { Document = document; ProjectRoot = projectRoot; }
	}

	/// <summary>
	/// Creates a new project entirely in a private sibling staging directory.
	/// Nothing becomes current until the manifest read-back and same-parent
	/// directory rename have both succeeded.
	/// </summary>
	public sealed class NewProjectStager {
		public Result<NewProjectResult, Diagnostic> Create(string projectName, string targetRoot, IProjectFileSystem fileSystem, IProjectIdFactory idFactory = null) {
			if (string.IsNullOrWhiteSpace(projectName) || string.IsNullOrWhiteSpace(targetRoot) || fileSystem == null) return Failure("persistence.new.invalid", "Project name, target root and file system are required.");
			var directories = fileSystem as IProjectDirectoryOperations;
			var cleanup = fileSystem as IProjectDirectoryCleanup;
			if (directories == null) return Failure("persistence.new.atomic_unsupported", "New Project requires an atomic directory finalization port.");
			if (cleanup == null) return Failure("persistence.new.cleanup_unsupported", "New Project requires a staging cleanup port.");
			if (IsReparsePoint(fileSystem, targetRoot)) return Failure("persistence.new.reparse_point", "New Project target may not be a reparse point.");
			if (directories.DirectoryExists(targetRoot) || fileSystem.Exists(targetRoot)) return Failure("persistence.new.target_exists", "New Project target already exists.");
			var parent = Path.GetDirectoryName(fileSystem.GetFullPath(targetRoot));
			if (string.IsNullOrEmpty(parent)) parent = ".";
			var staging = Path.Combine(parent, "." + Path.GetFileName(targetRoot) + ".staging-" + Guid.NewGuid().ToString("N"));
			var finalized = false;
			try {
				var candidate = ProjectDocumentFactory.CreateNew(projectName, idFactory);
				if (candidate.IsFailure) return Result.Failure<NewProjectResult, Diagnostic>(candidate.Error);
				fileSystem.EnsureDirectory(Path.Combine(staging, "Assets"));
				fileSystem.EnsureDirectory(Path.Combine(staging, "Backups"));
				var save = new ProjectSaver().Save(candidate.Value, staging, fileSystem);
				if (save.IsFailure) return Result.Failure<NewProjectResult, Diagnostic>(save.Error);
				var readback = new ProjectLoader().Load(staging, fileSystem);
				if (readback.IsFailure) return Failure("persistence.new.readback_failed", "New Project manifest read-back failed.");
				if (directories.DirectoryExists(targetRoot) || fileSystem.Exists(targetRoot)) return Failure("persistence.new.target_exists", "New Project target appeared during staging.");
				directories.MoveDirectory(staging, targetRoot);
				finalized = true;
				return Result.Success<NewProjectResult, Diagnostic>(new NewProjectResult(readback.Value.Document, targetRoot));
			}
			catch (Exception exception) {
				return Result.Failure<NewProjectResult, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.new.failed"), Severity.Error, "New Project staging failed.", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
			finally {
				if (!finalized) {
					try { if (directories.DirectoryExists(staging)) cleanup.DeleteDirectory(staging); } catch { }
				}
			}
		}

		private static Result<NewProjectResult, Diagnostic> Failure(string code, string message) => Result.Failure<NewProjectResult, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
		private static bool IsReparsePoint(IProjectFileSystem fileSystem, string path) { try { return (fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; } catch { return false; } }
	}

	public sealed class PendingMediaDeletion {
		public MediaAssetId AssetId { get; }
		public string RelativePath { get; }
		public string ProjectRoot { get; }
		public bool IsOrphan { get; internal set; }
		internal PendingMediaDeletion(MediaAssetId assetId, string relativePath, string projectRoot) { AssetId = assetId; RelativePath = relativePath; ProjectRoot = projectRoot; }
	}

	/// <summary>
	/// Holds media directories until a successful manifest commit proves that
	/// the deleted catalog entry is absent from both the current document and
	/// the durable project.json.  Undo cancels the pending item; failures are
	/// retained as explicit orphan cleanup work.
	/// </summary>
	public sealed class MediaDeletionSession {
		private readonly Dictionary<MediaAssetId, PendingMediaDeletion> _pending = new Dictionary<MediaAssetId, PendingMediaDeletion>();
		private long _revision;
		private long _snapshotRevision = long.MinValue;
		private IReadOnlyList<PendingMediaDeletion> _pendingSnapshot = new ReadOnlyCollection<PendingMediaDeletion>(new List<PendingMediaDeletion>());
		private IReadOnlyList<PendingMediaDeletion> _orphanSnapshot = new ReadOnlyCollection<PendingMediaDeletion>(new List<PendingMediaDeletion>());
		public long Revision => _revision;
		public IReadOnlyList<PendingMediaDeletion> Pending { get { RefreshSnapshots(); return _pendingSnapshot; } }
		public IReadOnlyList<PendingMediaDeletion> Orphans { get { RefreshSnapshots(); return _orphanSnapshot; } }

		public UnitResult<Diagnostic> RequestDeletion(ProjectDocument document, MediaAssetId assetId, string projectRoot, IProjectFileSystem fileSystem) {
			if (document == null || assetId.IsEmpty || string.IsNullOrWhiteSpace(projectRoot) || fileSystem == null) return Failure("persistence.media.delete_invalid", "Media deletion inputs are required.");
			var asset = document.FindMediaAsset(assetId);
			if (asset == null) return Failure("persistence.media.delete_missing", "Media asset does not exist.");
			return Track(asset, projectRoot);
		}

		/// <summary>Tracks an asset captured before the delete command commits.</summary>
		public UnitResult<Diagnostic> Track(MediaAssetRecord asset, string projectRoot) {
			if (asset == null || asset.Id.IsEmpty || string.IsNullOrWhiteSpace(projectRoot)) return Failure("persistence.media.delete_invalid", "Media deletion inputs are required.");
			var normalized = MediaPathRules.Normalize(asset.Id, asset.RelativePath);
			if (normalized.IsFailure) return UnitResult.Failure<Diagnostic>(normalized.Error);
			if (!_pending.ContainsKey(asset.Id)) { _pending.Add(asset.Id, new PendingMediaDeletion(asset.Id, normalized.Value, projectRoot)); _revision++; }
			return UnitResult.Success<Diagnostic>();
		}

		public UnitResult<Diagnostic> Cancel(MediaAssetId assetId) {
			if (_pending.Remove(assetId)) _revision++;
			return UnitResult.Success<Diagnostic>();
		}

		public UnitResult<Diagnostic> OnUndo(ProjectDocument document, MediaAssetId assetId) {
			if (document != null && document.FindMediaAsset(assetId) != null) return Cancel(assetId);
			return UnitResult.Success<Diagnostic>();
		}

		public UnitResult<Diagnostic> FinalizeAfterSave(ProjectDocument document, IProjectFileSystem fileSystem) {
			if (document == null || fileSystem == null) return Failure("persistence.media.delete_invalid", "Media deletion finalization inputs are required.");
			var results = UnitResult.Success<Diagnostic>();
			foreach (var item in _pending.Values.ToList()) {
				if (document.FindMediaAsset(item.AssetId) != null) { if (_pending.Remove(item.AssetId)) _revision++; continue; }
				var result = TryDelete(item, document, fileSystem);
				if (result.IsFailure) { if (!item.IsOrphan) { item.IsOrphan = true; _revision++; } results = result; }
				else if (_pending.Remove(item.AssetId)) _revision++;
			}
			return results;
		}

		public UnitResult<Diagnostic> CleanupOrphan(MediaAssetId assetId, ProjectDocument document, IProjectFileSystem fileSystem) {
			if (!_pending.TryGetValue(assetId, out var item) || !item.IsOrphan) return Failure("persistence.media.orphan_missing", "Orphan media deletion is not pending.");
			var result = TryDelete(item, document, fileSystem);
			if (result.IsSuccess && _pending.Remove(assetId)) _revision++;
			return result;
		}

		private void RefreshSnapshots() {
			if (_snapshotRevision == _revision) return;
			_pendingSnapshot = new ReadOnlyCollection<PendingMediaDeletion>(_pending.Values.Where(x => !x.IsOrphan).ToList());
			_orphanSnapshot = new ReadOnlyCollection<PendingMediaDeletion>(_pending.Values.Where(x => x.IsOrphan).ToList());
			_snapshotRevision = _revision;
		}

		private static UnitResult<Diagnostic> TryDelete(PendingMediaDeletion item, ProjectDocument document, IProjectFileSystem fileSystem) {
			if (document == null || document.FindMediaAsset(item.AssetId) != null) return Failure("persistence.media.delete_still_referenced", "Media asset is still referenced by the current project.");
			var main = Path.Combine(item.ProjectRoot, PersistenceConstants.MainFileName);
			if (!fileSystem.Exists(main)) return Failure("persistence.media.delete_manifest_missing", "The committed project manifest is missing.");
			var manifest = ProjectSerializer.Deserialize(fileSystem.ReadAllBytes(main));
			if (manifest.IsFailure) return Failure("persistence.media.delete_manifest_invalid", "The committed project manifest could not be verified.");
			if (manifest.Value.MediaAssets.Any(x => string.Equals(x.Id, item.AssetId.Value, StringComparison.Ordinal))) return Failure("persistence.media.delete_manifest_references", "The committed project manifest still references the media asset.");
			var cleanup = fileSystem as IProjectDirectoryCleanup;
			if (cleanup == null) return Failure("persistence.media.delete_cleanup_unsupported", "The file system cannot safely remove an asset directory.");
			try {
				cleanup.DeleteDirectory(Path.Combine(item.ProjectRoot, "Assets", item.AssetId.Value));
				return UnitResult.Success<Diagnostic>();
			}
			catch (Exception exception) {
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.media.orphan"), Severity.Warning, "Media directory deletion failed; the asset is retained as an orphan.", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
	}

	public interface INodeStateMigrator {
		NodeTypeId NodeTypeId { get; }
		int FromVersion { get; }
		int ToVersion { get; }
		Result<string, Diagnostic> Migrate(string rawJson);
	}

	public sealed class NodeMigrationRegistry {
		private readonly Dictionary<Tuple<NodeTypeId, int>, INodeStateMigrator> _migrators = new Dictionary<Tuple<NodeTypeId, int>, INodeStateMigrator>();
		public UnitResult<Diagnostic> Register(INodeStateMigrator migrator) {
			if (migrator == null || migrator.NodeTypeId.IsEmpty || migrator.FromVersion < 1 || migrator.ToVersion != migrator.FromVersion + 1) return Failure("persistence.migration.invalid", "Node migrator must advance exactly one schema version.");
			var key = Tuple.Create(migrator.NodeTypeId, migrator.FromVersion); if (_migrators.ContainsKey(key)) return Failure("persistence.migration.duplicate", "Node migrator is already registered."); _migrators.Add(key, migrator); return UnitResult.Success<Diagnostic>();
		}
		public Result<string, Diagnostic> Migrate(NodeTypeId typeId, int fromVersion, int targetVersion, string rawJson) {
			var current = rawJson;
			for (var version = fromVersion; version < targetVersion; version++) {
				if (!_migrators.TryGetValue(Tuple.Create(typeId, version), out var migrator)) return Result.Failure<string, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.migration.missing"), Severity.Error, "A sequential node migrator is missing."));
				Result<string, Diagnostic> result;
				try { result = migrator.Migrate(current); }
				catch (Exception exception) { return Result.Failure<string, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.migration.failed"), Severity.Error, "Node migration failed.", exception: DiagnosticExceptionInfo.FromException(exception))); }
				if (result.IsFailure) return result;
				current = result.Value;
			}
			return Result.Success<string, Diagnostic>(current);
		}
		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
	}

	public interface IProjectFormatMigrator {
		int FromVersion { get; }
		int ToVersion { get; }
		Result<ProjectDocumentDto, Diagnostic> Migrate(ProjectDocumentDto sourceCopy);
	}

	public sealed class ProjectFormatMigrationRegistry {
		private readonly Dictionary<int, IProjectFormatMigrator> _migrators = new Dictionary<int, IProjectFormatMigrator>();

		public UnitResult<Diagnostic> Register(IProjectFormatMigrator migrator) {
			if (migrator == null || migrator.FromVersion < 1 || migrator.ToVersion != migrator.FromVersion + 1)
				return Failure("persistence.project_migration.invalid", "Project migrators must advance exactly one format version.");
			if (_migrators.ContainsKey(migrator.FromVersion)) return Failure("persistence.project_migration.duplicate", "A project migrator is already registered for this source version.");
			_migrators.Add(migrator.FromVersion, migrator);
			return UnitResult.Success<Diagnostic>();
		}

		public Result<ProjectDocumentDto, Diagnostic> Migrate(ProjectDocumentDto source, int targetVersion = PersistenceConstants.CurrentProjectFormatVersion) {
			if (source == null) return FailureDto("persistence.project_migration.source_invalid", "A project DTO is required.");
			if (source.ProjectFormatVersion > targetVersion) return FailureDto("persistence.format_unsupported", "The project format is newer than this build.");
			var current = Clone(source);
			for (var version = current.ProjectFormatVersion; version < targetVersion; version++) {
				if (!_migrators.TryGetValue(version, out var migrator)) return FailureDto("persistence.project_migration.missing", "A sequential project migrator is missing.");
				Result<ProjectDocumentDto, Diagnostic> migrated;
				try { migrated = migrator.Migrate(Clone(current)); }
				catch (Exception exception) { return FailureDto("persistence.project_migration.failed", "Project migration failed: " + exception.Message); }
				if (migrated.IsFailure) return migrated;
				if (migrated.Value == null || migrated.Value.ProjectFormatVersion != version + 1) return FailureDto("persistence.project_migration.invalid_result", "A project migrator returned the wrong version.");
				current = Clone(migrated.Value);
			}
			return Result.Success<ProjectDocumentDto, Diagnostic>(current);
		}

		private static ProjectDocumentDto Clone(ProjectDocumentDto source) {
			return ProjectSerializer.CloneDto(source);
		}
		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
		private static Result<ProjectDocumentDto, Diagnostic> FailureDto(string code, string message) => Result.Failure<ProjectDocumentDto, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
	}

	public static class ProjectMigrationBackup {
		public static Result<string, Diagnostic> Write(IProjectFileSystem fileSystem, string projectRoot, byte[] originalBytes, int originalVersion) {
			if (fileSystem == null || string.IsNullOrWhiteSpace(projectRoot) || originalBytes == null) return Result.Failure<string, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.migration.backup_invalid"), Severity.Error, "Migration backup inputs are required."));
			var path = (string)null;
			try {
				var digest = AssetIntegrity.Hash(originalBytes);
				var directory = Path.Combine(projectRoot, "Backups");
				fileSystem.EnsureDirectory(directory);
				var stem = "pre-migration-" + DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture) + "-v" + originalVersion.ToString(CultureInfo.InvariantCulture) + "-" + digest;
				path = Path.Combine(directory, stem + ".json");
				if (fileSystem.Exists(path)) path = Path.Combine(directory, stem + "-" + Guid.NewGuid().ToString("N") + ".json");
				fileSystem.WriteAllBytes(path, originalBytes);
				ProjectFileSystemPorts.Flush(fileSystem, path);
				if (!fileSystem.Exists(path) || !originalBytes.SequenceEqual(fileSystem.ReadAllBytes(path))) { try { fileSystem.Delete(path); } catch { } return Result.Failure<string, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.migration.backup_verify"), Severity.Error, "Migration backup readback did not match.")); }
				var backups = fileSystem.EnumerateFiles(directory).Where(x => Path.GetFileName(x).StartsWith("pre-migration-", StringComparison.Ordinal)).OrderBy(x => x, StringComparer.Ordinal).ToList();
				foreach (var old in backups.Take(Math.Max(0, backups.Count - 5))) {
					try { fileSystem.Delete(old); } catch { /* pruning is advisory after a verified backup */ }
				}
				return Result.Success<string, Diagnostic>(path);
			}
			catch (Exception exception) {
				try { if (path != null) fileSystem.Delete(path); } catch { }
				return Result.Failure<string, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.migration.backup_failed"), Severity.Error, exception.Message, exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}
	}

	public interface INodeSchemaCatalog {
		bool TryGetCurrentSchema(NodeTypeId nodeTypeId, out int currentSchemaVersion);
	}

	/// <summary>Optional catalog contract used to detect published-port drift.</summary>
	public interface INodePortSnapshotCatalog : INodeSchemaCatalog {
		bool TryGetCurrentPorts(NodeTypeId nodeTypeId, out IReadOnlyList<PortSnapshotRecord> ports);
	}

	public sealed class ProjectLoader {
		public Result<ProjectLoadResult, Diagnostic> Load(string projectRoot, IProjectFileSystem fileSystem, ProjectDocument currentProject = null, INodeSchemaCatalog catalog = null, NodeMigrationRegistry migrations = null) {
			return Load(projectRoot, fileSystem, currentProject, catalog, migrations, null);
		}

		public Result<ProjectLoadResult, Diagnostic> Load(string projectRoot, IProjectFileSystem fileSystem, ProjectDocument currentProject, INodeSchemaCatalog catalog, NodeMigrationRegistry migrations, ProjectFormatMigrationRegistry projectMigrations) {
			if (string.IsNullOrWhiteSpace(projectRoot) || fileSystem == null) return Failure("persistence.load_invalid", "Project root and file system are required.");
			if (IsReparsePoint(fileSystem, projectRoot)) return Failure("persistence.project_root_reparse_point", "Project root may not be a reparse point.");
			var main = Path.Combine(projectRoot, PersistenceConstants.MainFileName); var backup = Path.Combine(projectRoot, PersistenceConstants.BackupFileName); var temporary = Path.Combine(projectRoot, PersistenceConstants.TemporaryFileName);
			var mainBytesResult = ReadOptional(fileSystem, main);
			if (mainBytesResult.IsFailure) return Result.Failure<ProjectLoadResult, Diagnostic>(mainBytesResult.Error);
			var mainBytes = mainBytesResult.Value;
			var mainRead = mainBytes != null ? ProjectSerializer.DeserializeAnyVersion(mainBytes) : FailureDto("persistence.main_missing", "project.json is missing.");
			ProjectDocumentDto dto = null; var status = ProjectLoadStatus.Loaded; var diagnostics = new List<Diagnostic>();
			byte[] sourceBytes = mainBytes;
			try {
				if (fileSystem.Exists(temporary)) diagnostics.Add(new Diagnostic(new DiagnosticCode("persistence.temporary_present"), Severity.Warning, "A previous temporary manifest was found; it was not adopted."));
			}
			catch (Exception exception) {
				return Result.Failure<ProjectLoadResult, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.temporary_probe_failed"), Severity.Error, "The temporary manifest could not be inspected.", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
			if (mainRead.IsSuccess) dto = mainRead.Value;
			else {
				diagnostics.Add(mainRead.Error);
				var backupBytesResult = ReadOptional(fileSystem, backup);
				if (backupBytesResult.IsFailure) return Result.Failure<ProjectLoadResult, Diagnostic>(backupBytesResult.Error);
				var backupBytes = backupBytesResult.Value;
				var backupRead = backupBytes != null ? ProjectSerializer.DeserializeAnyVersion(backupBytes) : FailureDto("persistence.backup_missing", "project.json.bak is missing.");
				if (backupRead.IsFailure) {
					diagnostics.Add(backupRead.Error);
					return Result.Failure<ProjectLoadResult, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.main_and_backup_invalid"), Severity.Error, "Neither project.json nor project.json.bak is valid."));
				}
				dto = backupRead.Value; sourceBytes = backupBytes; status = ProjectLoadStatus.Recovered;
			}
			try {
				var migrationNeeded = dto.ProjectFormatVersion < PersistenceConstants.CurrentProjectFormatVersion || NeedsNodeMigration(dto, catalog);
				if (dto.ProjectFormatVersion > PersistenceConstants.CurrentProjectFormatVersion) return Result.Failure<ProjectLoadResult, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.format_unsupported"), Severity.Error, "The project format version is newer than this build."));
				if (migrationNeeded) {
					if (sourceBytes == null) return Result.Failure<ProjectLoadResult, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.migration.source_missing"), Severity.Error, "Migration source bytes are missing."));
					var backupResult = ProjectMigrationBackup.Write(fileSystem, projectRoot, sourceBytes, dto.ProjectFormatVersion);
					if (backupResult.IsFailure) return Result.Failure<ProjectLoadResult, Diagnostic>(backupResult.Error);
					if (dto.ProjectFormatVersion < PersistenceConstants.CurrentProjectFormatVersion) {
						if (projectMigrations == null) return Result.Failure<ProjectLoadResult, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.project_migration.missing"), Severity.Error, "A project format migrator is required."));
						var migrated = projectMigrations.Migrate(dto, PersistenceConstants.CurrentProjectFormatVersion);
						if (migrated.IsFailure) return Result.Failure<ProjectLoadResult, Diagnostic>(migrated.Error);
						dto = migrated.Value;
					}
					status = status == ProjectLoadStatus.Recovered ? status : ProjectLoadStatus.Migrated;
				}
				var candidateResult = Hydrate(dto, catalog, migrations, diagnostics, status == ProjectLoadStatus.Recovered, out var changed);
				if (candidateResult.IsFailure) return Result.Failure<ProjectLoadResult, Diagnostic>(candidateResult.Error);
				var candidate = candidateResult.Value;
				ValidateMedia(candidate, dto, projectRoot, fileSystem, diagnostics);
				if (changed && status != ProjectLoadStatus.Recovered && status != ProjectLoadStatus.Migrated) status = ProjectLoadStatus.Repaired;
				return Result.Success<ProjectLoadResult, Diagnostic>(new ProjectLoadResult(candidate, status, diagnostics));
			}
			catch (Exception exception) {
				return Result.Failure<ProjectLoadResult, Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.candidate_invalid"), Severity.Error, exception.Message, exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		private static bool NeedsNodeMigration(ProjectDocumentDto dto, INodeSchemaCatalog catalog) {
			if (catalog == null) return false;
			foreach (var node in dto.Nodes ?? new List<NodeDto>()) {
				if (node == null || string.IsNullOrWhiteSpace(node.TypeId)) continue;
				var type = new NodeTypeId(node.TypeId);
				if (catalog.TryGetCurrentSchema(type, out var current) && node.SchemaVersion < current) return true;
			}
			return false;
		}

		private static Result<ProjectDocument, Diagnostic> Hydrate(ProjectDocumentDto dto, INodeSchemaCatalog catalog, NodeMigrationRegistry migrations, List<Diagnostic> diagnostics, bool markDirty, out bool changed) {
			changed = false;
			var assets = (dto.MediaAssets ?? new List<MediaAssetDto>()).Select(ToMedia).ToList();
			var nodes = new List<NodeRecord>();
			foreach (var node in dto.Nodes ?? new List<NodeDto>()) {
				var nodeRecord = ToNode(node, catalog, migrations, diagnostics, ref changed);
				nodes.Add(nodeRecord);
			}
			var controls = (dto.LogicalControls ?? new List<LogicalControlDto>()).Select(ToLogicalControl).ToList();
			// v1 has no top-level expressions member.  Expressions belong to
			// the parameter record they evaluate, which keeps the expression
			// and its typed output clamp together during round-trip.
			var expressions = new List<ParameterExpressionRecord>();
			foreach (var nodeDto in dto.Nodes ?? new List<NodeDto>()) {
				foreach (var parameterDto in nodeDto.Parameters ?? new List<ParameterDto>()) {
					if (parameterDto.Expression == null) continue;
					var parameterType = ParseEnum<ParameterType>(parameterDto.Type);
					var minimum = parameterDto.OutputMinimum == null ? (ParameterValue?)null : ToValue(parameterDto.OutputMinimum, parameterType);
					var maximum = parameterDto.OutputMaximum == null ? (ParameterValue?)null : ToValue(parameterDto.OutputMaximum, parameterType);
					if (minimum.HasValue != maximum.HasValue) throw new InvalidDataException("Expression output range must provide both bounds.");
					ParameterRange? range = minimum.HasValue ? new ParameterRange(minimum.Value, maximum.Value) : (ParameterRange?)null;
					expressions.Add(new ParameterExpressionRecord(new NodeInstanceId(nodeDto.Id), new ParameterId(parameterDto.Id), ToExpressionNode(parameterDto.Expression), range));
				}
			}
			var presets = (dto.Presets ?? new List<PresetDto>()).Select(ToPreset).ToList();
			var connections = (dto.Connections ?? new List<ConnectionDto>()).Select(ToConnection).ToList();
			// Old manifests may not have a port snapshot for an UnknownNode.
			// Recreate opaque stubs from connection endpoints so the graph can
			// still display and retain those edges.  Their type is deliberately
			// generic and never treated as a catalog definition.
			for (var i = 0; i < nodes.Count; i++) {
				if (!nodes[i].IsUnknown) continue;
				var ports = nodes[i].Ports.ToList();
				foreach (var connection in connections) {
					if (connection.SourceNodeId == nodes[i].Id && !ports.Any(x => x.Id == connection.SourcePortId)) ports.Add(new PortSnapshotRecord(connection.SourcePortId, PortDirection.Output, PortType.ImageFrame, false));
					if (connection.DestinationNodeId == nodes[i].Id && !ports.Any(x => x.Id == connection.DestinationPortId)) ports.Add(new PortSnapshotRecord(connection.DestinationPortId, PortDirection.Input, PortType.ImageFrame, false));
				}
				if (ports.Count != nodes[i].Ports.Count) { nodes[i] = nodes[i].WithPorts(ports); changed = true; diagnostics.Add(new Diagnostic(new DiagnosticCode("persistence.node_port_stub"), Severity.Warning, "Missing UnknownNode ports were reconstructed from connection endpoints.", nodeId: nodes[i].Id)); }
			}
			var settings = ToSettings(dto.Settings);
			return ProjectDocumentFactory.TryCreate(dto.ProjectName, dto.ProjectFormatVersion, nodes, connections, controls, expressions, presets, assets, ToUi(dto.Ui), settings, markDirty || changed);
		}

		private static NodeRecord ToNode(NodeDto dto, INodeSchemaCatalog catalog, NodeMigrationRegistry migrations, List<Diagnostic> diagnostics, ref bool changed) {
			var typeId = new NodeTypeId(dto.TypeId); var schemaVersion = dto.SchemaVersion; var raw = dto.RawState ?? "{}"; var originalRaw = raw; var unknownType = (string)null; var currentVersion = schemaVersion;
			var known = catalog == null || catalog.TryGetCurrentSchema(typeId, out currentVersion);
			var publishedPortsChanged = false;
			var portCatalog = catalog as INodePortSnapshotCatalog;
			// An empty snapshot is an old-manifest omission, not evidence of
			// catalog drift.  A non-empty saved snapshot is authoritative and
			// mismatches isolate the node as UnknownNode.
			if (known && portCatalog != null && schemaVersion == currentVersion && (dto.Ports ?? new List<PortDto>()).Count > 0 && portCatalog.TryGetCurrentPorts(typeId, out var currentPorts)) publishedPortsChanged = !PortsEqual(dto.Ports, currentPorts);
			if (!known || (catalog != null && schemaVersion > currentVersion) || publishedPortsChanged) { unknownType = unknownType ?? dto.TypeId; changed = true; if (publishedPortsChanged) diagnostics.Add(new Diagnostic(new DiagnosticCode("persistence.node_port_snapshot_mismatch"), Severity.Warning, "Node port snapshot differs from the registered catalog; node was retained as UnknownNode.")); }
			else if (catalog != null && schemaVersion < currentVersion) {
				var migrated = migrations == null ? (Result<string, Diagnostic>?)null : migrations.Migrate(typeId, schemaVersion, currentVersion, raw);
				if (!migrated.HasValue || migrated.Value.IsFailure) { unknownType = unknownType ?? dto.TypeId; changed = true; diagnostics.Add(new Diagnostic(new DiagnosticCode("persistence.node_unknown"), Severity.Warning, "Node schema migration failed; node was retained as UnknownNode.")); }
				else if (!IsJsonObject(migrated.Value.Value)) { unknownType = unknownType ?? dto.TypeId; changed = true; diagnostics.Add(new Diagnostic(new DiagnosticCode("persistence.node_unknown"), Severity.Warning, "Node schema migration returned a non-object state; node was retained as UnknownNode.")); }
				else { raw = migrated.Value.Value; schemaVersion = currentVersion; changed = true; }
			}
			var parameters = new List<ParameterRecord>();
			foreach (var parameterDto in dto.Parameters ?? new List<ParameterDto>()) parameters.Add(ToParameter(parameterDto, diagnostics, ref changed));
			var ports = (dto.Ports ?? new List<PortDto>()).Select(ToPort).ToList();
			if (!string.IsNullOrEmpty(unknownType)) {
				var original = new UnknownNodeRecord(new NodeTypeId(unknownType), dto.SchemaVersion, originalRaw);
				return new NodeRecord(new NodeInstanceId(dto.Id), new NodeTypeId("system.unknown_node"), schemaVersion, dto.DisplayName, dto.Enabled, new ProjectPosition(dto.X, dto.Y), parameters, ports, raw, dto.SystemOwned, false, original);
			}
			return new NodeRecord(new NodeInstanceId(dto.Id), typeId, schemaVersion, dto.DisplayName, dto.Enabled, new ProjectPosition(dto.X, dto.Y), parameters, ports, raw, dto.SystemOwned, dto.UserAddable);
		}

		private static bool PortsEqual(IReadOnlyList<PortDto> saved, IReadOnlyList<PortSnapshotRecord> current) {
			var left = (saved ?? new List<PortDto>()).OrderBy(x => x.Id, StringComparer.Ordinal).ToList();
			var right = (current ?? new List<PortSnapshotRecord>()).OrderBy(x => x.Id.Value, StringComparer.Ordinal).ToList();
			if (left.Count != right.Count) return false;
			for (var i = 0; i < left.Count; i++) {
				var image = string.IsNullOrEmpty(left[i].DefaultImage) ? (DefaultImageKind?)null : ParseEnum<DefaultImageKind>(left[i].DefaultImage);
				if (!string.Equals(left[i].Id, right[i].Id.Value, StringComparison.Ordinal) || !string.Equals(left[i].Direction, right[i].Direction.ToString(), StringComparison.Ordinal) || !string.Equals(left[i].Type, right[i].Type.ToString(), StringComparison.Ordinal) || left[i].Required != right[i].Required || image != right[i].DefaultImage) return false;
			}
			return true;
		}
		private static bool IsJsonObject(string raw) { try { using (var document = JsonDocument.Parse(raw ?? string.Empty)) return document.RootElement.ValueKind == JsonValueKind.Object; } catch { return false; } }
		private static ParameterRecord ToParameter(ParameterDto dto, List<Diagnostic> diagnostics, ref bool changed) {
			var type = ParseEnum<ParameterType>(dto.Type);
			var defaultValue = ToValue(dto.DefaultValue, type);
			ParameterRange? hardRange = null;
			if (dto.HardMinimum != null && dto.HardMaximum != null) hardRange = new ParameterRange(ToValue(dto.HardMinimum, type), ToValue(dto.HardMaximum, type));
			var definition = new ParameterDefinition(new ParameterId(dto.Id), dto.DisplayName, type, defaultValue, hardRange, dto.RuntimeStateful, (dto.EnumOptionIds ?? new List<string>()).Select(x => new ParameterId(x)));
			var original = ToValue(dto.BaseValue ?? dto.DefaultValue, type);
			var value = definition.Clamp(original);
			if (value.IsFailure) throw new InvalidDataException("Parameter base value is invalid.");
			if (value.Value != original) { changed = true; diagnostics.Add(new Diagnostic(new DiagnosticCode("persistence.parameter_clamped"), Severity.Warning, "Parameter base value was clamped to its hard range.", parameterId: new ParameterId(dto.Id))); }
			var record = new ParameterRecord(definition, value.Value);
			return dto.IsBroken ? record.AsBroken(dto.BrokenReason) : record;
		}
		private static PortSnapshotRecord ToPort(PortDto dto) => new PortSnapshotRecord(new PortId(dto.Id), ParseEnum<PortDirection>(dto.Direction), ParseEnum<PortType>(dto.Type), dto.Required, string.IsNullOrEmpty(dto.DefaultImage) ? (DefaultImageKind?)null : ParseEnum<DefaultImageKind>(dto.DefaultImage));
		private static ConnectionRecord ToConnection(ConnectionDto dto) => new ConnectionRecord(new ConnectionId(dto.Id), new NodeInstanceId(dto.SourceNodeId), new PortId(dto.SourcePortId), new NodeInstanceId(dto.DestinationNodeId), new PortId(dto.DestinationPortId), dto.ConversionId, dto.IsBroken, dto.BrokenReason);
		private static MediaAssetRecord ToMedia(MediaAssetDto dto) { if (!string.Equals(dto.IntegrityAlgorithm, PersistenceConstants.IntegrityAlgorithm, StringComparison.Ordinal)) throw new InvalidDataException("Unsupported media integrity algorithm."); return new MediaAssetRecord(new MediaAssetId(dto.Id), dto.DisplayName, dto.RelativePath, dto.ByteSize, dto.IntegrityHash, ParseEnum<MediaAssetKind>(dto.Kind), ParseEnum<MediaColorSpace>(dto.ColorSpace), ParseEnum<MediaAlphaMode>(dto.AlphaMode)); }
		private static LogicalControlRecord ToLogicalControl(LogicalControlDto dto) { var targets = (dto.Targets ?? new List<LogicalTargetDto>()).Select(x => new LogicalControlTargetRecord(new NodeInstanceId(x.NodeId), new ParameterId(x.ParameterId), ParseEnum<ParameterType>(x.ParameterType), ToValue(x.Minimum, ParseEnum<ParameterType>(x.ParameterType)), ToValue(x.Maximum, ParseEnum<ParameterType>(x.ParameterType)), x.Invert, x.IsBroken, x.BrokenReason)); var mappings = (dto.Mappings ?? new List<ControlMappingDto>()).Select(x => new ControlMappingRecord(ParseEnum<PhysicalInputKind>(x.Kind), x.PhysicalId, x.ControlPath, x.RawMin, x.RawMax, x.Invert, x.IsBroken, x.BrokenReason)); return new LogicalControlRecord(new LogicalControlId(dto.Id), dto.Name, ParseEnum<LogicalControlKind>(dto.Kind), dto.InitialValue, targets, mappings, string.IsNullOrEmpty(dto.PresetId) ? (PresetId?)null : new PresetId(dto.PresetId), dto.PresetIsBroken, dto.BrokenReason); }
		private static PresetRecord ToPreset(PresetDto dto) => new PresetRecord(new PresetId(dto.Id), dto.Name, dto.Category, dto.SortIndex, (dto.Entries ?? new List<PresetEntryDto>()).Select(x => new PresetEntryRecord(new NodeInstanceId(x.NodeId), new ParameterId(x.ParameterId), ParseEnum<ParameterType>(x.ParameterType), ToValue(x.Value, ParseEnum<ParameterType>(x.ParameterType)), x.IsBroken, x.BrokenReason)));
		private static ProjectUiStateRecord ToUi(UiDto dto) => new ProjectUiStateRecord((dto.DashboardPages ?? new List<DashboardPageDto>()).Select(x => new DashboardPageRecord(x.PageId, x.Name, (x.Widgets ?? new List<DashboardWidgetDto>()).Select(y => new DashboardWidgetRecord(y.WidgetId, new NodeInstanceId(y.NodeId), new ParameterId(y.ParameterId), y.Column, y.Row, y.Width, y.Height, y.Label, y.IsBroken, y.BrokenReason)))), dto.PreviewNodeIds);
		private static ParameterValue ToValue(ValueDto dto, ParameterType type) { if (dto == null) return ParameterValue.Default(type); switch (type) { case ParameterType.Float: return ParameterValue.FromFloat(dto.FloatValue); case ParameterType.Int: return ParameterValue.FromInt(dto.IntValue); case ParameterType.Bool: return ParameterValue.FromBool(dto.BoolValue); case ParameterType.Vector2: return ParameterValue.FromVector2(new Vector2Value(dto.Components[0], dto.Components[1])); case ParameterType.Vector3: return ParameterValue.FromVector3(new Vector3Value(dto.Components[0], dto.Components[1], dto.Components[2])); case ParameterType.Vector4: return ParameterValue.FromVector4(new Vector4Value(dto.Components[0], dto.Components[1], dto.Components[2], dto.Components[3])); case ParameterType.Color: return ParameterValue.FromColor(new ColorValue(dto.Components[0], dto.Components[1], dto.Components[2], dto.Components[3])); case ParameterType.String: return ParameterValue.FromString(dto.TextValue ?? string.Empty); case ParameterType.Enum: return ParameterValue.FromEnum(dto.TextValue ?? string.Empty); case ParameterType.MediaAssetReference: return ParameterValue.FromMediaAsset(string.IsNullOrEmpty(dto.TextValue) ? (MediaAssetId?)null : new MediaAssetId(dto.TextValue)); default: throw new InvalidDataException("Unsupported parameter value type."); } }
		private static ProjectOutputSettings ToSettings(SettingsDto dto) => dto == null ? ProjectOutputSettings.CreateDefault() : new ProjectOutputSettings(ParseEnum<ProjectDynamicRange>(dto.DynamicRange), dto.ProgramDisplay);
		private static LogicalExpressionNode ToExpressionNode(ExpressionNodeDto dto) {
			if (dto == null) throw new InvalidDataException("Expression node is required.");
			switch (dto.Kind) {
				case "Control": return new LogicalControlLeaf(new LogicalControlId(dto.ControlId));
				case "Base": return new BaseValueLeaf();
				case "Broken": return new BrokenExpressionLeaf(new LogicalControlId(dto.ControlId), dto.Reason);
				case "Binary": return new BinaryLogicalExpression(ParseEnum<LogicalOperator>(dto.Operator), ToExpressionNode(dto.Left), ToExpressionNode(dto.Right));
				default: throw new InvalidDataException("Unknown expression node kind: " + dto.Kind);
			}
		}
		private static T ParseEnum<T>(string value) where T : struct {
			if (typeof(T) == typeof(MediaColorSpace) && string.Equals(value, "sRGB", StringComparison.Ordinal)) return (T)(object)MediaColorSpace.SRgb;
			if (typeof(T) == typeof(ProjectDynamicRange) && string.Equals(value, "HDR", StringComparison.Ordinal)) return (T)(object)ProjectDynamicRange.Hdr;
			if (typeof(T) == typeof(ProjectDynamicRange) && string.Equals(value, "LDR", StringComparison.Ordinal)) return (T)(object)ProjectDynamicRange.Ldr;
			if (!Enum.TryParse<T>(value, false, out var result)) throw new InvalidDataException("Invalid enum value: " + value);
			return result;
		}
		private static void ValidateMedia(ProjectDocument document, ProjectDocumentDto dto, string root, IProjectFileSystem fs, List<Diagnostic> diagnostics) {
			var fullRoot = NormalizeFsPath(fs.GetFullPath(root));
			var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
			var rootPrefix = fullRoot.EndsWith("/", StringComparison.Ordinal) ? fullRoot : fullRoot + "/";

			foreach (var asset in dto.MediaAssets ?? new List<MediaAssetDto>()) {
				var path = MediaPathRules.Normalize(new MediaAssetId(asset.Id), asset.RelativePath);
				if (path.IsFailure) { diagnostics.Add(path.Error); continue; }
				var absolute = NormalizeFsPath(fs.GetFullPath(Path.Combine(root, path.Value.Replace('/', Path.DirectorySeparatorChar))));
				if (!absolute.StartsWith(rootPrefix, comparison)) {
					diagnostics.Add(new Diagnostic(new DiagnosticCode("persistence.media_outside_project"), Severity.Error, "Media path resolves outside the project root."));
					continue;
				}

				// A managed entry may not be a symlink/junction/reparse point.
				// Walk existing components so a reparse directory cannot hide
				// an escaped file behind an otherwise valid relative path.
				var relative = absolute.Substring(rootPrefix.Length);
				var current = fullRoot;
				var unsafePath = false;
				try {
					if ((fs.GetAttributes(fullRoot) & FileAttributes.ReparsePoint) != 0) unsafePath = true;
				}
				catch { }
				foreach (var segment in relative.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries)) {
					current = NormalizeFsPath(Path.Combine(current, segment));
					try {
						if ((fs.GetAttributes(current) & FileAttributes.ReparsePoint) != 0) unsafePath = true;
					}
					catch { if (!fs.Exists(current)) break; }
				}
				if (unsafePath) {
					diagnostics.Add(new Diagnostic(new DiagnosticCode("persistence.media_reparse_point"), Severity.Error, "Managed media paths may not contain reparse points."));
					document.MarkMediaAssetBroken(new MediaAssetId(asset.Id), "Managed media path is a reparse point.");
					continue;
				}
				if (!fs.Exists(absolute)) {
					diagnostics.Add(new Diagnostic(new DiagnosticCode("persistence.media_missing"), Severity.Error, "Media file is missing; references remain blocked until it is restored."));
					document.MarkMediaAssetBroken(new MediaAssetId(asset.Id), "Media file is missing.");
					continue;
				}
				var bytes = fs.ReadAllBytes(absolute);
				if (bytes.LongLength != asset.ByteSize || !string.Equals(AssetIntegrity.Hash(bytes), asset.IntegrityHash, StringComparison.Ordinal)) {
					diagnostics.Add(new Diagnostic(new DiagnosticCode("persistence.media_hash_mismatch"), Severity.Error, "Media integrity hash does not match; references remain blocked."));
					diagnostics.Add(new Diagnostic(new DiagnosticCode("persistence.media_replaced"), Severity.Warning, "The managed media file was replaced or modified."));
					document.MarkMediaAssetBroken(new MediaAssetId(asset.Id), "Media integrity hash does not match.");
				}
			}
		}
		private static string NormalizeFsPath(string path) => (path ?? string.Empty).Replace('\\', '/').TrimEnd('/');
		private static Result<ProjectLoadResult, Diagnostic> Failure(string code, string message) => Result.Failure<ProjectLoadResult, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
		private static Result<ProjectDocumentDto, Diagnostic> FailureDto(string code, string message) => Result.Failure<ProjectDocumentDto, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
		private static bool IsReparsePoint(IProjectFileSystem fileSystem, string path) { try { return (fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; } catch { return false; } }
		private static Result<byte[], Diagnostic> ReadOptional(IProjectFileSystem fileSystem, string path) {
			try {
				return Result.Success<byte[], Diagnostic>(fileSystem.Exists(path) ? fileSystem.ReadAllBytes(path) : null);
			}
			catch (Exception exception) {
				return Result.Failure<byte[], Diagnostic>(new Diagnostic(new DiagnosticCode("persistence.read_failed"), Severity.Error, "Project file could not be read.", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}
	}
}
