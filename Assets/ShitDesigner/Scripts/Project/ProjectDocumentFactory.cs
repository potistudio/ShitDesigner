using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;

namespace ShitDesigner.Project {
	public interface IProjectIdFactory {
		NodeInstanceId NewNodeInstanceId();
	}

	public sealed class DefaultProjectIdFactory : IProjectIdFactory {
		public NodeInstanceId NewNodeInstanceId() => NodeInstanceId.New();
	}

	public sealed class ProjectRepairInfo {
		public string Code { get; }
		public string Message { get; }
		public NodeInstanceId? NodeId { get; }

		public ProjectRepairInfo(string code, string message, NodeInstanceId? nodeId = null) {
			Code = code ?? string.Empty;
			Message = message ?? string.Empty;
			NodeId = nodeId;
		}
	}

	/// <summary>Newly built document plus non-fatal load repairs.</summary>
	public sealed class ProjectDocumentFactoryResult {
		public ProjectDocument Document { get; }
		public IReadOnlyList<ProjectRepairInfo> Repairs { get; }
		public bool WasRepaired => Repairs.Count != 0;

		internal ProjectDocumentFactoryResult(ProjectDocument document, IEnumerable<ProjectRepairInfo> repairs) {
			Document = document ?? throw new ArgumentNullException(nameof(document));
			Repairs = new ReadOnlyCollection<ProjectRepairInfo>((repairs ?? Enumerable.Empty<ProjectRepairInfo>()).ToList());
		}
	}

	/// <summary>
	/// Public, reflection-free hydration boundary for Persistence.  A complete
	/// candidate is validated and built in isolation; callers can then decide
	/// whether to replace the currently open document.
	/// </summary>
	public static class ProjectDocumentFactory {
		private const string ProgramOutputType = "system.program_output";
		private const string PreviewType = "system.preview";
		private const string UnknownNodeType = "system.unknown_node";

		/// <summary>
		/// Builds the canonical empty project used by the New Project use
		/// case.  The normalizer supplies the required ProgramOutput node;
		/// the factory supplies the project-owned Main dashboard page.
		/// </summary>
		public static CSharpFunctionalExtensions.Result<ProjectDocument, Diagnostic> CreateNew(string projectName, IProjectIdFactory idFactory = null) {
			var ui = new ProjectUiStateRecord(new[] { new DashboardPageRecord("main", "Main") });
			return TryCreate(projectName, 1, Enumerable.Empty<NodeRecord>(), Enumerable.Empty<ConnectionRecord>(), Enumerable.Empty<LogicalControlRecord>(), Enumerable.Empty<ParameterExpressionRecord>(), Enumerable.Empty<PresetRecord>(), Enumerable.Empty<MediaAssetRecord>(), ui, ProjectOutputSettings.CreateDefault(), true, idFactory);
		}

		public static CSharpFunctionalExtensions.Result<ProjectDocument, Diagnostic> TryCreate(
			string projectName,
			int projectFormatVersion,
			IEnumerable<NodeRecord> nodes,
			IEnumerable<ConnectionRecord> connections,
			IEnumerable<LogicalControlRecord> logicalControls,
			IEnumerable<ParameterExpressionRecord> expressions,
			IEnumerable<PresetRecord> presets,
			IEnumerable<MediaAssetRecord> mediaAssets,
			ProjectUiStateRecord ui = null,
			ProjectOutputSettings settings = null,
			bool markDirty = false,
			IProjectIdFactory idFactory = null) {
			var detailed = TryCreateDetailed(projectName, projectFormatVersion, nodes, connections, logicalControls, expressions, presets, mediaAssets, ui, settings, markDirty, idFactory);
			return detailed.IsFailure
				? CSharpFunctionalExtensions.Result.Failure<ProjectDocument, Diagnostic>(detailed.Error)
				: CSharpFunctionalExtensions.Result.Success<ProjectDocument, Diagnostic>(detailed.Value.Document);
		}

		public static CSharpFunctionalExtensions.Result<ProjectDocumentFactoryResult, Diagnostic> TryCreateDetailed(
			string projectName,
			int projectFormatVersion,
			IEnumerable<NodeRecord> nodes,
			IEnumerable<ConnectionRecord> connections,
			IEnumerable<LogicalControlRecord> logicalControls,
			IEnumerable<ParameterExpressionRecord> expressions,
			IEnumerable<PresetRecord> presets,
			IEnumerable<MediaAssetRecord> mediaAssets,
			ProjectUiStateRecord ui = null,
			ProjectOutputSettings settings = null,
			bool markDirty = false,
			IProjectIdFactory idFactory = null) {
			if (string.IsNullOrWhiteSpace(projectName)) return Failure<ProjectDocumentFactoryResult>("project.factory.name", "Project name is required.");
			if (projectFormatVersion < 1) return Failure<ProjectDocumentFactoryResult>("project.factory.version", "Project format version must be positive.");
			var repairInfo = new List<ProjectRepairInfo>();
			var nodeList = NormalizeProgramOutput((nodes ?? Enumerable.Empty<NodeRecord>()).ToList(), repairInfo, idFactory ?? new DefaultProjectIdFactory());
			nodeList = NormalizePreviewNodes(nodeList, repairInfo);
			var connectionList = (connections ?? Enumerable.Empty<ConnectionRecord>()).ToList();
			var controlList = (logicalControls ?? Enumerable.Empty<LogicalControlRecord>()).ToList();
			var expressionList = (expressions ?? Enumerable.Empty<ParameterExpressionRecord>()).ToList();
			var presetList = (presets ?? Enumerable.Empty<PresetRecord>()).ToList();
			var assetList = (mediaAssets ?? Enumerable.Empty<MediaAssetRecord>()).ToList();

			var identity = ValidateIdentity(nodeList, connectionList, controlList, expressionList, presetList, assetList);
			if (identity.IsFailure) return CSharpFunctionalExtensions.Result.Failure<ProjectDocumentFactoryResult, Diagnostic>(identity.Error);
			if (connectionList.Count > 4096) return Failure<ProjectDocumentFactoryResult>("project.factory.connection_limit", "The project connection limit is 4096.");
			if (presetList.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1)) return Failure<ProjectDocumentFactoryResult>("project.factory.preset_name_duplicate", "Preset names must be unique ignoring case.");
			if (expressionList.GroupBy(x => new { x.NodeId, x.ParameterId }).Any(x => x.Count() > 1)) return Failure<ProjectDocumentFactoryResult>("project.factory.expression_duplicate", "Expression targets must be unique.");

			var nodeMap = nodeList.ToDictionary(x => x.Id, x => x);
			var assetMap = assetList.ToDictionary(x => x.Id, x => x);
			var normalizedConnections = connectionList.Select(x => NormalizeConnection(x, nodeMap)).ToList();
			if (normalizedConnections.Count(x => !x.IsBroken) > 4096) return Failure<ProjectDocumentFactoryResult>("project.factory.connection_limit", "The project connection limit is 4096.");
			var activeDestinations = new HashSet<Tuple<NodeInstanceId, PortId>>();
			foreach (var connection in normalizedConnections.Where(x => !x.IsBroken)) {
				if (!activeDestinations.Add(Tuple.Create(connection.DestinationNodeId, connection.DestinationPortId))) return Failure<ProjectDocumentFactoryResult>("project.factory.input_occupied", "An input port may have only one active connection.");
			}
			if (HasCycle(normalizedConnections, nodeList)) return Failure<ProjectDocumentFactoryResult>("project.factory.cycle", "The project graph contains a same-frame cycle.");

			var normalizedControls = controlList.Select(control =>
				control.WithTargets(control.Targets.Select(target => NormalizeTarget(target, nodeMap)))).ToList();
			var normalizedExpressions = expressionList.Select(expression => {
				var target = nodeMap.TryGetValue(expression.NodeId, out var node) ? node.FindParameter(expression.ParameterId) : null;
				return target == null ? expression.AsBroken("Expression target is missing.") : expression;
			}).ToList();
			var normalizedPresets = presetList.Select(preset => preset.WithEntries(preset.Entries.Select(entry => NormalizeEntry(entry, nodeMap, assetMap)))).ToList();

			var candidate = new ProjectDocument(projectName, projectFormatVersion, ui, settings);
			foreach (var asset in assetList) candidate.AddMediaAsset(asset);
			foreach (var node in nodeList) candidate.AddNode(node);
			foreach (var connection in normalizedConnections) candidate.AddConnection(connection);
			foreach (var control in normalizedControls) candidate.AddLogicalControl(control);
			foreach (var expression in normalizedExpressions) candidate.AddExpression(expression);
			foreach (var preset in normalizedPresets) candidate.AddPreset(preset);
			candidate.RevalidateBrokenReferences();
			if (markDirty || repairInfo.Count != 0) candidate.CommitMutation();
			return CSharpFunctionalExtensions.Result.Success<ProjectDocumentFactoryResult, Diagnostic>(new ProjectDocumentFactoryResult(candidate, repairInfo));
		}


		/// <summary>
		/// Rehydrates a replacement document without mutating the currently
		/// open document.  Persistence can discard the returned candidate on
		/// failure and keep <paramref name="current"/> untouched.
		/// </summary>
		public static CSharpFunctionalExtensions.Result<ProjectDocument, Diagnostic> Rehydrate(
			ProjectDocument current,
			string projectName,
			int projectFormatVersion,
			IEnumerable<NodeRecord> nodes,
			IEnumerable<ConnectionRecord> connections,
			IEnumerable<LogicalControlRecord> logicalControls,
			IEnumerable<ParameterExpressionRecord> expressions,
			IEnumerable<PresetRecord> presets,
			IEnumerable<MediaAssetRecord> mediaAssets,
			ProjectUiStateRecord ui = null,
			ProjectOutputSettings settings = null,
			bool markDirty = false,
			IProjectIdFactory idFactory = null) {
			if (current == null) return Failure<ProjectDocument>("project.factory.current_null", "An existing project document is required for rehydration.");
			return TryCreate(projectName, projectFormatVersion, nodes, connections, logicalControls, expressions, presets, mediaAssets, ui, settings, markDirty, idFactory);
		}

		private static List<NodeRecord> NormalizeProgramOutput(IReadOnlyList<NodeRecord> source, IList<ProjectRepairInfo> repairs, IProjectIdFactory idFactory) {
			var result = source.ToList();
			var programIndices = result.Select((node, index) => new { node, index })
				.Where(x => x.node != null && x.node.TypeId.Value == ProgramOutputType)
				.Select(x => x.index).ToList();

			if (programIndices.Count == 0) {
				var generated = CreateProgramOutput(idFactory.NewNodeInstanceId(), new ProjectPosition(1000, 0), "{}");
				result.Add(generated);
				repairs.Add(new ProjectRepairInfo("project.program_output.created", "The required ProgramOutput node was created.", generated.Id));
				return result;
			}

			var firstIndex = programIndices[0];
			var first = result[firstIndex];
			var normalized = NormalizeProgramOutputNode(first, repairs);
			result[firstIndex] = normalized;
			for (var i = 1; i < programIndices.Count; i++) {
				var duplicate = result[programIndices[i]];
				var unknown = duplicate.WithUnknown(new UnknownNodeRecord(new NodeTypeId(ProgramOutputType), duplicate.SchemaVersion, duplicate.RawState));
				result[programIndices[i]] = unknown;
				repairs.Add(new ProjectRepairInfo("project.program_output.duplicate", "An extra ProgramOutput node was retained as UnknownNode.", duplicate.Id));
			}
			return result;
		}

		private static NodeRecord NormalizeProgramOutputNode(NodeRecord node, IList<ProjectRepairInfo> repairs) {
			var expectedPort = new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true);
			var valid = node.DisplayName == "Image" && node.Enabled && node.SystemOwned && !node.UserAddable &&
				node.Parameters.Count == 0 && node.Ports.Count == 1 &&
				node.Ports[0].Id == expectedPort.Id && node.Ports[0].Direction == expectedPort.Direction &&
				node.Ports[0].Type == expectedPort.Type && node.Ports[0].Required == expectedPort.Required &&
				!node.Ports[0].DefaultImage.HasValue;
			if (valid) return node;

			repairs.Add(new ProjectRepairInfo("project.program_output.repaired", "The ProgramOutput node shape and ownership were repaired.", node.Id));
			return CreateProgramOutput(node.Id, node.Position, node.RawState);
		}

		private static NodeRecord CreateProgramOutput(NodeInstanceId id, ProjectPosition position, string rawState) {
			return new NodeRecord(id, new NodeTypeId(ProgramOutputType), 1, "Image", true, position,
				parameters: Enumerable.Empty<ParameterRecord>(),
				ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) },
				rawState: rawState ?? "{}", systemOwned: true, userAddable: false);
		}

		// Preview is user-owned and may occur more than once. Its graph
		// boundary is fixed, but display.mode is a persisted per-Preview
		// parameter (Fit/Fill/Stretch), not optional node metadata.
		private static List<NodeRecord> NormalizePreviewNodes(IReadOnlyList<NodeRecord> source, IList<ProjectRepairInfo> repairs) {
			var result = source.ToList();
			for (var i = 0; i < result.Count; i++) {
				var node = result[i];
				if (node == null || node.TypeId.Value != PreviewType) continue;
				var valid = !string.IsNullOrWhiteSpace(node.DisplayName) && node.Enabled && !node.SystemOwned && node.UserAddable
					&& node.Parameters.Count == 1 && IsPreviewMode(node.Parameters[0]) && node.Ports.Count == 1
					&& node.Ports[0].Id == new PortId("image")
					&& node.Ports[0].Direction == PortDirection.Input
					&& node.Ports[0].Type == PortType.ImageFrame
					&& node.Ports[0].Required && !node.Ports[0].DefaultImage.HasValue;
				if (valid) continue;
				repairs.Add(new ProjectRepairInfo("project.preview.repaired", "The Preview node shape and ownership were repaired.", node.Id));
				var mode = node.FindParameter(new ParameterId("display.mode"));
				result[i] = CreatePreview(node.Id, node.Position, node.RawState, node.DisplayName, mode?.BaseValue);
			}
			return result;
		}

		private static bool IsPreviewMode(ParameterRecord parameter) {
			if (parameter == null) return false;
			var definition = parameter.Definition;
			return definition.Id.Value == "display.mode"
				&& definition.DisplayName == "Display Mode"
				&& definition.Type == ParameterType.Enum
				&& definition.DefaultValue.Type == ParameterType.Enum
				&& definition.DefaultValue.AsString() == "fit"
				&& !definition.HardRange.HasValue
				&& !definition.RuntimeStateful
				&& definition.EnumOptionIds.Count == 3
				&& definition.EnumOptionIds.Select(x => x.Value).OrderBy(x => x, StringComparer.Ordinal).SequenceEqual(new[] { "fill", "fit", "stretch" })
				&& IsPreviewModeValue(parameter.BaseValue);
		}

		private static bool IsPreviewModeValue(ParameterValue value) {
			if (value.Type != ParameterType.Enum) return false;
			var mode = value.AsString();
			return mode == "fit" || mode == "fill" || mode == "stretch";
		}

		private static NodeRecord CreatePreview(NodeInstanceId id, ProjectPosition position, string rawState, string displayName = "Preview", ParameterValue? displayMode = null) {
			var definition = CreatePreviewModeDefinition();
			var value = displayMode.HasValue && IsPreviewModeValue(displayMode.Value) ? displayMode.Value : definition.DefaultValue;
			return new NodeRecord(id, new NodeTypeId(PreviewType), 1, string.IsNullOrWhiteSpace(displayName) ? "Preview" : displayName, true, position,
				parameters: new[] { new ParameterRecord(definition, value) },
				ports: new[] { new PortSnapshotRecord(new PortId("image"), PortDirection.Input, PortType.ImageFrame, true) },
				rawState: rawState ?? "{}", systemOwned: false, userAddable: true);
		}

		private static ParameterDefinition CreatePreviewModeDefinition() {
			return new ParameterDefinition(new ParameterId("display.mode"), "Display Mode", ParameterType.Enum, ParameterValue.FromEnum("fit"),
				enumOptionIds: new[] { new ParameterId("fit"), new ParameterId("fill"), new ParameterId("stretch") });
		}

		private static CSharpFunctionalExtensions.UnitResult<Diagnostic> ValidateIdentity(IEnumerable<NodeRecord> nodes, IEnumerable<ConnectionRecord> connections, IEnumerable<LogicalControlRecord> controls, IEnumerable<ParameterExpressionRecord> expressions, IEnumerable<PresetRecord> presets, IEnumerable<MediaAssetRecord> assets) {
			if (nodes.Any(x => x == null) || connections.Any(x => x == null) || controls.Any(x => x == null) || expressions.Any(x => x == null) || presets.Any(x => x == null) || assets.Any(x => x == null)) return Failure("project.factory.null_record", "Project candidate records cannot be null.");
			if (nodes.GroupBy(x => x.Id).Any(x => x.Count() > 1)) return Failure("project.factory.node_duplicate", "Node IDs must be unique.");
			if (connections.GroupBy(x => x.Id).Any(x => x.Count() > 1)) return Failure("project.factory.connection_duplicate", "Connection IDs must be unique.");
			if (controls.GroupBy(x => x.Id).Any(x => x.Count() > 1)) return Failure("project.factory.control_duplicate", "Logical control IDs must be unique.");
			if (presets.GroupBy(x => x.Id).Any(x => x.Count() > 1)) return Failure("project.factory.preset_duplicate", "Preset IDs must be unique.");
			if (assets.GroupBy(x => x.Id).Any(x => x.Count() > 1)) return Failure("project.factory.asset_duplicate", "Media asset IDs must be unique.");
			foreach (var node in nodes) if (!node.Id.IsUuidV4) return Failure("project.factory.node_uuid", "NodeInstanceId must be UUID v4.");
			foreach (var control in controls) if (!control.Id.IsUuidV4) return Failure("project.factory.control_uuid", "LogicalControlId must be UUID v4.");
			foreach (var preset in presets) if (!preset.Id.IsUuidV4) return Failure("project.factory.preset_uuid", "PresetId must be UUID v4.");
			foreach (var asset in assets) if (!asset.Id.IsUuidV4) return Failure("project.factory.asset_uuid", "MediaAssetId must be UUID v4.");
			return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
		}

		private static ConnectionRecord NormalizeConnection(ConnectionRecord connection, IReadOnlyDictionary<NodeInstanceId, NodeRecord> nodes) {
			if (connection.IsBroken) return connection;
			if (!nodes.TryGetValue(connection.SourceNodeId, out var sourceNode) || !nodes.TryGetValue(connection.DestinationNodeId, out var destinationNode)) return connection.AsBroken("Connection endpoint node is missing.");
			var source = sourceNode.FindPort(connection.SourcePortId);
			var destination = destinationNode.FindPort(connection.DestinationPortId);
			if (source == null || destination == null || source.Direction != PortDirection.Output || destination.Direction != PortDirection.Input) return connection.AsBroken("Connection endpoint port is missing.");
			if (source.Type == destination.Type ? !string.IsNullOrEmpty(connection.ConversionId) : string.IsNullOrEmpty(connection.ConversionId)) return connection.AsBroken("Connection conversion is incompatible.");
			return connection;
		}

		private static LogicalControlTargetRecord NormalizeTarget(LogicalControlTargetRecord target, IReadOnlyDictionary<NodeInstanceId, NodeRecord> nodes) {
			return nodes.TryGetValue(target.NodeId, out var node) && node.FindParameter(target.ParameterId)?.Definition.Type == target.ParameterType
				? target
				: target.AsBroken("Logical control target is missing.");
		}

		private static PresetEntryRecord NormalizeEntry(PresetEntryRecord entry, IReadOnlyDictionary<NodeInstanceId, NodeRecord> nodes, IReadOnlyDictionary<MediaAssetId, MediaAssetRecord> assets) {
			var parameter = nodes.TryGetValue(entry.NodeId, out var node) ? node.FindParameter(entry.ParameterId) : null;
			var mediaAvailable = !entry.Value.IsMediaAssetSelected || assets.ContainsKey(entry.Value.AsMediaAsset().Value);
			return parameter != null && parameter.Definition.Type == entry.ParameterType && mediaAvailable ? entry : entry.AsBroken("Preset target is missing.");
		}

		private static bool HasCycle(IEnumerable<ConnectionRecord> connections, IEnumerable<NodeRecord> nodes) {
			var list = nodes.ToList();
			var adjacency = list.ToDictionary(x => x.Id, x => new List<NodeInstanceId>());
			foreach (var edge in connections.Where(x => !x.IsBroken)) {
				if (!adjacency.ContainsKey(edge.SourceNodeId) || !adjacency.ContainsKey(edge.DestinationNodeId)) continue;
				if (list.First(x => x.Id == edge.SourceNodeId).TypeId.Value == "system.feedback") continue;
				adjacency[edge.SourceNodeId].Add(edge.DestinationNodeId);
			}
			var visiting = new HashSet<NodeInstanceId>();
			var visited = new HashSet<NodeInstanceId>();
			Func<NodeInstanceId, bool> visit = null;
			visit = id => {
				if (visiting.Contains(id)) return true;
				if (visited.Contains(id)) return false;
				visiting.Add(id);
				foreach (var next in adjacency[id]) if (visit(next)) return true;
				visiting.Remove(id); visited.Add(id); return false;
			};
			return adjacency.Keys.Any(visit);
		}

		private static CSharpFunctionalExtensions.Result<T, Diagnostic> Failure<T>(string code, string message) => CSharpFunctionalExtensions.Result.Failure<T, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
		private static CSharpFunctionalExtensions.UnitResult<Diagnostic> Failure(string code, string message) => CSharpFunctionalExtensions.UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
	}
}
