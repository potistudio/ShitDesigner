using System;
using System.Collections.Generic;
using System.Linq;
using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Nodes;
using ShitDesigner.Project;
using UnityEngine;

namespace ShitDesigner.Bootstrap {
	/// <summary>Unity authoring boundary for one persisted graph node.</summary>
	public abstract class UnityGraphNode : MonoBehaviour {
		[SerializeField] private string _displayName = string.Empty;
		[SerializeField] private Vector2 _graphPosition;

		public string NodeId { get; private set; } = string.Empty;
		public abstract string TypeId { get; }

		internal Result<NodeRecord, Diagnostic> Build(NodeDefinitionCatalog catalog) {
			if (catalog == null) return Failure("bootstrap.graph.catalog_missing", "A node catalog is required.");
			var typeId = new NodeTypeId(TypeId);
			var entry = catalog.Entries.FirstOrDefault(candidate => candidate.TypeId == typeId);
			if (entry == null) return Failure("bootstrap.graph.type_missing", "The authored node type is not registered: " + TypeId);
			var adapted = NodeCatalogBootstrap.Adapt(entry);
			if (adapted.IsFailure) return Result.Failure<NodeRecord, Diagnostic>(adapted.Error);

			NodeId = NodeInstanceId.New().Value;
			var definition = adapted.Value;
			var parameters = definition.Parameters.Select(parameter => new ParameterRecord(
				parameter, parameter.DefaultValue));
			var ports = definition.Ports.Select(port => port.ToSnapshot());
			var displayName = string.IsNullOrWhiteSpace(_displayName) ? definition.DisplayName : _displayName.Trim();
			return Result.Success<NodeRecord, Diagnostic>(new NodeRecord(new NodeInstanceId(NodeId), definition.TypeId,
				definition.SchemaVersion, displayName, true, new ProjectPosition(_graphPosition.x, _graphPosition.y),
				parameters, ports, "{}", definition.SystemOwned, definition.UserAddable));
		}

		private static Result<NodeRecord, Diagnostic> Failure(string code, string message) =>
			Result.Failure<NodeRecord, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "bootstrap"));
	}

	internal static class UnityGraphProjectBuilder {
		internal static Result<ProjectDocument, Diagnostic> Build(string projectName, NodeDefinitionCatalog catalog,
			IEnumerable<UnityGraphNode> nodes, UnityGraphNode programSource) {
			if (string.IsNullOrWhiteSpace(projectName)) return Failure("bootstrap.graph.project_name", "The authored project name is required.");
			var components = (nodes ?? Enumerable.Empty<UnityGraphNode>()).Where(node => node != null).Distinct().ToList();
			if (programSource == null || !components.Contains(programSource))
				return Failure("bootstrap.graph.program_source", "The Program source must be one of the authored node components.");

			var records = new List<NodeRecord>(components.Count);
			foreach (var component in components) {
				var built = component.Build(catalog);
				if (built.IsFailure) return Result.Failure<ProjectDocument, Diagnostic>(built.Error);
				records.Add(built.Value);
			}

			var normalized = ProjectDocumentFactory.TryCreate(projectName.Trim(), 1, records,
				Enumerable.Empty<ConnectionRecord>(), Enumerable.Empty<LogicalControlRecord>(),
				Enumerable.Empty<ParameterExpressionRecord>(), Enumerable.Empty<PresetRecord>(), Enumerable.Empty<MediaAssetRecord>());
			if (normalized.IsFailure) return normalized;
			var program = normalized.Value.Nodes.Single(node => node.TypeId.Value == GraphConstants.ProgramOutputTypeId);
			var connection = new ConnectionRecord(ConnectionId.New(), new NodeInstanceId(programSource.NodeId),
				new PortId(GraphConstants.ImagePortId), program.Id, new PortId(GraphConstants.ImagePortId));
			return ProjectDocumentFactory.TryCreate(projectName.Trim(), 1, normalized.Value.Nodes, new[] { connection },
				normalized.Value.LogicalControls, normalized.Value.Expressions, normalized.Value.Presets, normalized.Value.MediaAssets,
				normalized.Value.Ui, normalized.Value.Settings);
		}

		private static Result<ProjectDocument, Diagnostic> Failure(string code, string message) =>
			Result.Failure<ProjectDocument, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "bootstrap"));
	}
}
