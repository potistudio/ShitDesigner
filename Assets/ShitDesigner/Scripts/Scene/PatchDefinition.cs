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

	/// <summary>Logical live patch composed from Unity scene nodes and published controls.</summary>
	[CreateAssetMenu(fileName = "PatchDefinition", menuName = "ShitDesigner/Patch Definition")]
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
			if (string.IsNullOrWhiteSpace(Id)) return Failure("patch.definition.id", "A patch requires an ID.");
			if (string.IsNullOrWhiteSpace(DisplayName)) return Failure("patch.definition.name", "A patch requires a display name.");
			if (NodeGroups.Count == 0 || NodeGroups.Any(group => group == null || string.IsNullOrWhiteSpace(group.Id) || string.IsNullOrWhiteSpace(group.DisplayName) || group.Nodes.Count == 0))
				return Failure("patch.definition.group", "Every patch requires named node groups with nodes.");
			if (NodeGroups.GroupBy(group => group.Id, StringComparer.Ordinal).Any(group => group.Count() > 1)) return Failure("patch.definition.group_duplicate", "Patch node group IDs must be unique.");

			var nodes = Nodes.ToArray();
			if (nodes.Any(node => node == null || string.IsNullOrWhiteSpace(node.Id) || node.Validate().IsFailure)) return Failure("patch.definition.node", "Every patch node must be a valid Scene3DDefinition with an ID.");
			if (nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() != nodes.Length) return Failure("patch.definition.node_duplicate", "A patch cannot reference a Unity scene node more than once.");
			if (Parameters.Any(parameter => parameter == null || string.IsNullOrWhiteSpace(parameter.Id) || string.IsNullOrWhiteSpace(parameter.DisplayName) || string.IsNullOrWhiteSpace(parameter.NodeId) || string.IsNullOrWhiteSpace(parameter.ParameterId)))
				return Failure("patch.definition.parameter", "Published patch parameters require IDs, names, nodes, and source parameters.");
			if (Parameters.GroupBy(parameter => parameter.Id, StringComparer.Ordinal).Any(group => group.Count() > 1)) return Failure("patch.definition.parameter_duplicate", "Published patch parameter IDs must be unique.");
			if (Parameters.Any(parameter => !nodes.Any(node => string.Equals(node.Id, parameter.NodeId, StringComparison.Ordinal)))) return Failure("patch.definition.parameter_node", "A published patch parameter references an unknown scene node.");
			return UnitResult.Success<Diagnostic>();
		}

		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
	}
}
