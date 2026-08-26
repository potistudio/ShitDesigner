using System;
using System.Collections.Generic;
using System.Linq;
using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Scene {
	[Serializable]
	public sealed class PatchNodeGroup {
		[SerializeField] private string _id;
		[SerializeField] private string _displayName;
		[SerializeField] private Scene3DDefinition[] _nodes = Array.Empty<Scene3DDefinition>();

		public string Id => _id ?? string.Empty;
		public string DisplayName => _displayName ?? string.Empty;
		public IReadOnlyList<Scene3DDefinition> Nodes => _nodes ?? Array.Empty<Scene3DDefinition>();
	}

	[Serializable]
	public sealed class PatchParameter {
		[SerializeField] private string _id;
		[SerializeField] private string _displayName;
		[SerializeField] private string _nodeId;
		[SerializeField] private string _parameterId;

		public string Id => _id ?? string.Empty;
		public string DisplayName => _displayName ?? string.Empty;
		public string NodeId => _nodeId ?? string.Empty;
		public string ParameterId => _parameterId ?? string.Empty;
	}

	/// <summary>Logical live scene composed from Unity scene nodes and published controls.</summary>
	[CreateAssetMenu(fileName = "ShitDesignerSceneDefinition", menuName = "ShitDesigner/Scene Definition")]
	public sealed class PatchDefinition : ScriptableObject {
		[SerializeField] private string _id;
		[SerializeField] private string _displayName;
		[SerializeField] private PatchNodeGroup[] _nodeGroups = Array.Empty<PatchNodeGroup>();
		[SerializeField] private PatchParameter[] _parameters = Array.Empty<PatchParameter>();

		public string Id => _id ?? string.Empty;
		public string DisplayName => _displayName ?? string.Empty;
		public IReadOnlyList<PatchNodeGroup> NodeGroups => _nodeGroups ?? Array.Empty<PatchNodeGroup>();
		public IReadOnlyList<PatchParameter> Parameters => _parameters ?? Array.Empty<PatchParameter>();
		public IEnumerable<Scene3DDefinition> Nodes => NodeGroups.Where(group => group != null).SelectMany(group => group.Nodes);

		public UnitResult<Diagnostic> Validate() {
			if (string.IsNullOrWhiteSpace(Id)) return Failure("scene.definition.id", "A ShitDesigner scene requires an ID.");
			if (string.IsNullOrWhiteSpace(DisplayName)) return Failure("scene.definition.name", "A ShitDesigner scene requires a display name.");
			if (NodeGroups.Count == 0 || NodeGroups.Any(group => group == null || string.IsNullOrWhiteSpace(group.Id) || string.IsNullOrWhiteSpace(group.DisplayName) || group.Nodes.Count == 0))
				return Failure("scene.definition.group", "Every ShitDesigner scene requires named node groups with nodes.");
			if (NodeGroups.GroupBy(group => group.Id, StringComparer.Ordinal).Any(group => group.Count() > 1)) return Failure("scene.definition.group_duplicate", "ShitDesigner scene node group IDs must be unique.");

			var nodes = Nodes.ToArray();
			if (nodes.Any(node => node == null || string.IsNullOrWhiteSpace(node.Id) || node.Validate().IsFailure)) return Failure("scene.definition.node", "Every ShitDesigner scene node must be a valid Scene3DDefinition with an ID.");
			if (nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() != nodes.Length) return Failure("scene.definition.node_duplicate", "A ShitDesigner scene cannot reference a Unity scene node more than once.");
			if (Parameters.Any(parameter => parameter == null || string.IsNullOrWhiteSpace(parameter.Id) || string.IsNullOrWhiteSpace(parameter.DisplayName) || string.IsNullOrWhiteSpace(parameter.NodeId) || string.IsNullOrWhiteSpace(parameter.ParameterId)))
				return Failure("scene.definition.parameter", "Published scene parameters require IDs, names, nodes, and source parameters.");
			if (Parameters.GroupBy(parameter => parameter.Id, StringComparer.Ordinal).Any(group => group.Count() > 1)) return Failure("scene.definition.parameter_duplicate", "Published scene parameter IDs must be unique.");
			if (Parameters.Any(parameter => !nodes.Any(node => string.Equals(node.Id, parameter.NodeId, StringComparison.Ordinal)))) return Failure("scene.definition.parameter_node", "A published scene parameter references an unknown scene node.");
			return UnitResult.Success<Diagnostic>();
		}

		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
	}
}
