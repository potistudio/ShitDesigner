using System;
using System.Collections.Generic;
using System.Linq;
using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Scene {
	[Serializable]
	public sealed class PatchGraphParameter {
		[SerializeField] private string _id;
		[SerializeField] private ParameterType _type;
		[SerializeField] private float _floatValue;
		[SerializeField] private int _intValue;
		[SerializeField] private bool _boolValue;
		[SerializeField] private Vector2 _vector2Value;
		[SerializeField] private Vector3 _vector3Value;
		[SerializeField] private Vector4 _vector4Value;
		[SerializeField] private Color _colorValue = Color.white;
		[SerializeField] private string _textValue;

		public string Id => (_id ?? string.Empty).Trim();
		public ParameterType Type => _type;
		public ParameterValue Value => ToParameterValue();

		public PatchGraphParameter() { }

		public PatchGraphParameter(string id, ParameterValue value) {
			_id = id;
			SetValue(value);
		}

		public void SetValue(ParameterValue value) {
			_type = value.Type;
			_floatValue = value.Type == ParameterType.Float ? value.AsFloat() : 0f;
			_intValue = value.Type == ParameterType.Int ? value.AsInt() : 0;
			_boolValue = value.Type == ParameterType.Bool && value.AsBool();
			_vector2Value = value.Type == ParameterType.Vector2 ? ToUnityVector2(value.AsVector2()) : Vector2.zero;
			_vector3Value = value.Type == ParameterType.Vector3 ? ToUnityVector3(value.AsVector3()) : Vector3.zero;
			_vector4Value = value.Type == ParameterType.Vector4 ? ToUnityVector4(value.AsVector4()) : Vector4.zero;
			_colorValue = value.Type == ParameterType.Color ? ToUnityColor(value.AsColor()) : Color.white;
			_textValue = value.Type == ParameterType.String || value.Type == ParameterType.Enum || value.Type == ParameterType.MediaAssetReference
				? value.AsString() : string.Empty;
		}

		public bool TryGetValue(out ParameterValue value) {
			try {
				value = ToParameterValue();
				return true;
			}
			catch {
				value = default(ParameterValue);
				return false;
			}
		}

		private ParameterValue ToParameterValue() {
			switch (_type) {
				case ParameterType.Float: return ParameterValue.FromFloat(_floatValue);
				case ParameterType.Int: return ParameterValue.FromInt(_intValue);
				case ParameterType.Bool: return ParameterValue.FromBool(_boolValue);
				case ParameterType.Vector2: return ParameterValue.FromVector2(new Vector2Value(_vector2Value.x, _vector2Value.y));
				case ParameterType.Vector3: return ParameterValue.FromVector3(new Vector3Value(_vector3Value.x, _vector3Value.y, _vector3Value.z));
				case ParameterType.Vector4: return ParameterValue.FromVector4(new Vector4Value(_vector4Value.x, _vector4Value.y, _vector4Value.z, _vector4Value.w));
				case ParameterType.Color: return ParameterValue.FromColor(new ColorValue(_colorValue.r, _colorValue.g, _colorValue.b, _colorValue.a));
				case ParameterType.String: return ParameterValue.FromString(_textValue ?? string.Empty);
				case ParameterType.Enum: return ParameterValue.FromEnum(_textValue ?? string.Empty);
				case ParameterType.MediaAssetReference:
					return ParameterValue.FromMediaAsset(string.IsNullOrWhiteSpace(_textValue) ? (MediaAssetId?)null : new MediaAssetId(_textValue));
				default: throw new ArgumentOutOfRangeException();
			}
		}

		private static Vector2 ToUnityVector2(Vector2Value value) => new Vector2(value.X, value.Y);
		private static Vector3 ToUnityVector3(Vector3Value value) => new Vector3(value.X, value.Y, value.Z);
		private static Vector4 ToUnityVector4(Vector4Value value) => new Vector4(value.X, value.Y, value.Z, value.W);
		private static Color ToUnityColor(ColorValue value) => new Color(value.R, value.G, value.B, value.A);
	}

	[Serializable]
	public sealed class PatchGraphNode {
		[SerializeField] private string _id;
		[SerializeField] private string _typeId;
		[SerializeField] private List<PatchGraphParameter> _parameters = new List<PatchGraphParameter>();

		public string Id => (_id ?? string.Empty).Trim();
		public string TypeId => (_typeId ?? string.Empty).Trim();
		public IReadOnlyList<PatchGraphParameter> Parameters => _parameters ?? (IReadOnlyList<PatchGraphParameter>)Array.Empty<PatchGraphParameter>();

		public PatchGraphNode() { }

		public PatchGraphNode(string id, string typeId, IEnumerable<PatchGraphParameter> parameters = null) {
			_id = id;
			_typeId = typeId;
			_parameters = new List<PatchGraphParameter>(parameters ?? Enumerable.Empty<PatchGraphParameter>());
		}

		public PatchGraphParameter FindParameter(string id) => Parameters.FirstOrDefault(parameter => parameter != null && string.Equals(parameter.Id, id, StringComparison.Ordinal));
	}

	[Serializable]
	public sealed class PatchGraphConnection {
		[SerializeField] private string _sourceNodeId;
		[SerializeField] private string _sourcePortId = PatchProgramGraph.ImagePortId;
		[SerializeField] private string _targetNodeId;
		[SerializeField] private string _targetPortId;

		public string SourceNodeId => (_sourceNodeId ?? string.Empty).Trim();
		public string SourcePortId => (_sourcePortId ?? string.Empty).Trim();
		public string TargetNodeId => (_targetNodeId ?? string.Empty).Trim();
		public string TargetPortId => (_targetPortId ?? string.Empty).Trim();

		public PatchGraphConnection() { }

		public PatchGraphConnection(string sourceNodeId, string targetNodeId, string targetPortId, string sourcePortId = PatchProgramGraph.ImagePortId) {
			_sourceNodeId = sourceNodeId;
			_sourcePortId = sourcePortId;
			_targetNodeId = targetNodeId;
			_targetPortId = targetPortId;
		}
	}

	[Serializable]
	public sealed class PatchProgramGraph {
		public const string ImagePortId = "image";
		public const string SceneInputNodeId = "scene";

		[SerializeField] private string _sourceNodeId = SceneInputNodeId;
		[SerializeField] private string _outputNodeId = "composite";
		[SerializeField] private List<PatchGraphNode> _nodes = new List<PatchGraphNode>();
		[SerializeField] private List<PatchGraphConnection> _connections = new List<PatchGraphConnection>();

		public string SourceNodeId => (_sourceNodeId ?? string.Empty).Trim();
		public string OutputNodeId => (_outputNodeId ?? string.Empty).Trim();
		public IReadOnlyList<PatchGraphNode> Nodes => _nodes ?? (IReadOnlyList<PatchGraphNode>)Array.Empty<PatchGraphNode>();
		public IReadOnlyList<PatchGraphConnection> Connections => _connections ?? (IReadOnlyList<PatchGraphConnection>)Array.Empty<PatchGraphConnection>();

		public PatchProgramGraph() { }

		public PatchProgramGraph(string sourceNodeId, string outputNodeId, IEnumerable<PatchGraphNode> nodes, IEnumerable<PatchGraphConnection> connections) {
			_sourceNodeId = sourceNodeId;
			_outputNodeId = outputNodeId;
			_nodes = new List<PatchGraphNode>(nodes ?? Enumerable.Empty<PatchGraphNode>());
			_connections = new List<PatchGraphConnection>(connections ?? Enumerable.Empty<PatchGraphConnection>());
		}

		public UnitResult<Diagnostic> Validate() {
			if (string.IsNullOrWhiteSpace(SourceNodeId) || string.IsNullOrWhiteSpace(OutputNodeId))
				return Failure("patch.definition.graph.endpoint", "A patch program graph requires source and output node IDs.");
			if (Nodes.Count == 0) return Failure("patch.definition.graph.nodes", "A patch program graph requires at least one node.");
			if (Nodes.Any(node => node == null || string.IsNullOrWhiteSpace(node.Id) || !NodeTypeId.TryParse(node.TypeId, out _)))
				return Failure("patch.definition.graph.node", "Every patch graph node requires an ID and a valid node type ID.");
			if (Nodes.GroupBy(node => node.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
				return Failure("patch.definition.graph.node_duplicate", "Patch graph node IDs must be unique.");
			if (Nodes.Any(node => string.Equals(node.Id, SourceNodeId, StringComparison.Ordinal)))
				return Failure("patch.definition.graph.source_collision", "The patch graph source ID is reserved for the scene input.");
			if (!Nodes.Any(node => string.Equals(node.Id, OutputNodeId, StringComparison.Ordinal)))
				return Failure("patch.definition.graph.output_missing", "The patch graph output must reference a configured node.");
			foreach (var node in Nodes) {
				if (node.Parameters.Any(parameter => parameter == null || string.IsNullOrWhiteSpace(parameter.Id) || !ParameterId.TryParse(parameter.Id, out _) || !parameter.TryGetValue(out _)))
					return Failure("patch.definition.graph.parameter", "Every patch graph parameter requires a valid ID and finite value.");
				if (node.Parameters.GroupBy(parameter => parameter.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
					return Failure("patch.definition.graph.parameter_duplicate", "Patch graph parameter IDs must be unique within a node.");
			}
			if (Connections.Any(connection => connection == null || string.IsNullOrWhiteSpace(connection.SourceNodeId)
				|| string.IsNullOrWhiteSpace(connection.SourcePortId) || !PortId.TryParse(connection.SourcePortId, out _)
				|| string.IsNullOrWhiteSpace(connection.TargetNodeId) || string.IsNullOrWhiteSpace(connection.TargetPortId)
				|| !PortId.TryParse(connection.TargetPortId, out _)))
				return Failure("patch.definition.graph.connection", "Every patch graph connection requires valid source and target ports.");
			if (Connections.Any(connection => !string.Equals(connection.SourcePortId, ImagePortId, StringComparison.Ordinal)))
				return Failure("patch.definition.graph.source_port", "Patch graph shader nodes expose the image output port only.");
			if (Connections.Any(connection => !string.Equals(connection.SourceNodeId, SourceNodeId, StringComparison.Ordinal)
				&& !Nodes.Any(node => string.Equals(node.Id, connection.SourceNodeId, StringComparison.Ordinal))))
				return Failure("patch.definition.graph.connection_source", "A patch graph connection references an unknown source node.");
			if (Connections.Any(connection => !Nodes.Any(node => string.Equals(node.Id, connection.TargetNodeId, StringComparison.Ordinal))))
				return Failure("patch.definition.graph.connection_target", "A patch graph connection references an unknown target node.");
			if (Connections.GroupBy(connection => new { connection.TargetNodeId, connection.TargetPortId }).Any(group => group.Count() > 1))
				return Failure("patch.definition.graph.connection_duplicate", "A patch graph input port can have only one connection.");
			if (HasCycle()) return Failure("patch.definition.graph.cycle", "Patch graph connections must be acyclic.");
			return UnitResult.Success<Diagnostic>();
		}

		private bool HasCycle() {
			var adjacency = Nodes.ToDictionary(node => node.Id, node => new List<string>(), StringComparer.Ordinal);
			foreach (var connection in Connections)
				if (!string.Equals(connection.SourceNodeId, SourceNodeId, StringComparison.Ordinal) && adjacency.ContainsKey(connection.SourceNodeId))
					adjacency[connection.SourceNodeId].Add(connection.TargetNodeId);
			var visiting = new HashSet<string>(StringComparer.Ordinal);
			var visited = new HashSet<string>(StringComparer.Ordinal);
			bool Visit(string nodeId) {
				if (visiting.Contains(nodeId)) return true;
				if (visited.Contains(nodeId)) return false;
				visiting.Add(nodeId);
				foreach (var next in adjacency[nodeId]) if (Visit(next)) return true;
				visiting.Remove(nodeId);
				visited.Add(nodeId);
				return false;
			}
			return adjacency.Keys.Any(Visit);
		}

		private static UnitResult<Diagnostic> Failure(string code, string message)
			=> UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
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

	[Serializable]
	public sealed class PatchFlashDefinition {
		[SerializeField] private Texture2D _image;
		[SerializeField, Min(.01f)] private float _durationSeconds = .25f;

		public Texture2D Image => _image;
		public float DurationSeconds => _durationSeconds;

		public UnitResult<Diagnostic> Validate() {
			if (_image == null) return Failure("patch.definition.flash.image", "A configured patch flash requires an image.");
			if (float.IsNaN(_durationSeconds) || float.IsInfinity(_durationSeconds) || _durationSeconds <= 0f)
				return Failure("patch.definition.flash.duration", "A configured patch flash requires a positive duration.");
			return UnitResult.Success<Diagnostic>();
		}

		private static UnitResult<Diagnostic> Failure(string code, string message)
			=> UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
	}

	/// <summary>Logical live patch composed from Unity scene nodes and published controls.</summary>
	[CreateAssetMenu(fileName = "PatchDefinition", menuName = "ShitDesigner/Patch Definition")]
	public sealed class PatchDefinition : ScriptableObject {
		[SerializeField] private string _id;
		[SerializeField] private string _displayName;
		[SerializeField] private PatchFlashDefinition _flash;
		[SerializeField] private PatchProgramGraph _programGraph = new PatchProgramGraph();
		[SerializeField] private List<Scene3DDefinition> _nodes = new List<Scene3DDefinition>();
		[SerializeField] private List<PatchParameter> _parameters = new List<PatchParameter>();

		public string Id => _id ?? string.Empty;
		public string DisplayName => _displayName ?? string.Empty;
		public PatchFlashDefinition Flash => _flash;
		public PatchProgramGraph ProgramGraph => _programGraph;
		public IReadOnlyList<Scene3DDefinition> Nodes => _nodes ?? (IReadOnlyList<Scene3DDefinition>)Array.Empty<Scene3DDefinition>();
		public IReadOnlyList<PatchParameter> Parameters => _parameters ?? (IReadOnlyList<PatchParameter>)Array.Empty<PatchParameter>();

		public UnitResult<Diagnostic> Validate() {
			if (string.IsNullOrWhiteSpace(Id)) return Failure("patch.definition.id", "A patch requires an ID.");
			if (string.IsNullOrWhiteSpace(DisplayName)) return Failure("patch.definition.name", "A patch requires a display name.");
			if (ProgramGraph == null) return Failure("patch.definition.graph", "A patch requires a program graph.");
			var graphValidation = ProgramGraph.Validate();
			if (graphValidation.IsFailure) return graphValidation;
			if (Nodes.Count == 0) return Failure("patch.definition.nodes", "Every patch requires at least one Scene3DDefinition.");
			var nodes = Nodes.ToArray();
			if (nodes.Any(node => node == null || string.IsNullOrWhiteSpace(node.Id) || node.Validate().IsFailure)) return Failure("patch.definition.node", "Every Scene3DDefinition must have an ID and a prefab.");
			if (nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() != nodes.Length) return Failure("patch.definition.node_duplicate", "A patch cannot reference a Unity scene node more than once.");
			if (Parameters.Any(parameter => parameter == null || string.IsNullOrWhiteSpace(parameter.Id) || string.IsNullOrWhiteSpace(parameter.DisplayName) || string.IsNullOrWhiteSpace(parameter.NodeId) || string.IsNullOrWhiteSpace(parameter.ParameterId)))
				return Failure("patch.definition.parameter", "Published patch parameters require IDs, names, nodes, and source parameters.");
			if (Parameters.GroupBy(parameter => parameter.Id, StringComparer.Ordinal).Any(group => group.Count() > 1)) return Failure("patch.definition.parameter_duplicate", "Published patch parameter IDs must be unique.");
			if (Parameters.Any(parameter => !nodes.Any(node => string.Equals(node.Id, parameter.NodeId, StringComparison.Ordinal)))) return Failure("patch.definition.parameter_node", "A published patch parameter references an unknown scene node.");
			if (Flash != null && Flash.Validate().IsFailure) return Failure("patch.definition.flash", "A patch flash definition is invalid.");
			return UnitResult.Success<Diagnostic>();
		}

		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
	}
}
