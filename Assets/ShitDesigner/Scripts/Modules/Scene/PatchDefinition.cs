using System;
using System.Collections.Generic;
using System.Linq;
using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;

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

		public static bool IsLiveControllable(ParameterType type) {
			switch (type) {
				case ParameterType.Float:
				case ParameterType.Int:
				case ParameterType.Bool:
				case ParameterType.Vector2:
				case ParameterType.Vector3:
				case ParameterType.Vector4:
				case ParameterType.Color:
				case ParameterType.Enum:
					return true;
				default:
					return false;
			}
		}

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
		public const string Scene3DTypeId = "shitdesigner.scene.3d";

		[SerializeField] private string _id;
		[SerializeField] private string _typeId;
		[SerializeField] private Scene3DDefinition m_SceneDefinition;
		// Retain the Unity reference for existing patches. New video sources use
		// the path so codecs that Unity cannot import can still be selected.
		[SerializeField] private string m_VideoPath;
		[SerializeField] private VideoClip m_VideoClip;
		[SerializeField] private List<PatchGraphParameter> _parameters = new List<PatchGraphParameter>();

		public string Id => (_id ?? string.Empty).Trim();
		public string TypeId => (_typeId ?? string.Empty).Trim();
		public Scene3DDefinition SceneDefinition => m_SceneDefinition;
		public string VideoPath => (m_VideoPath ?? string.Empty).Trim();
		public VideoClip VideoClip => m_VideoClip;
		public IReadOnlyList<PatchGraphParameter> Parameters => _parameters ?? (IReadOnlyList<PatchGraphParameter>)Array.Empty<PatchGraphParameter>();
		public bool IsSceneNode => string.Equals(TypeId, Scene3DTypeId, StringComparison.Ordinal);

		public PatchGraphNode() { }

		public PatchGraphNode(string id, string typeId, IEnumerable<PatchGraphParameter> parameters = null, VideoClip videoClip = null,
			Scene3DDefinition sceneDefinition = null) {
			_id = id;
			_typeId = typeId;
			m_SceneDefinition = sceneDefinition;
			m_VideoClip = videoClip;
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

		[SerializeField] private string _outputNodeId = "composite";
		[SerializeField] private List<PatchGraphNode> _nodes = new List<PatchGraphNode>();
		[SerializeField] private List<PatchGraphConnection> _connections = new List<PatchGraphConnection>();

		public string OutputNodeId => (_outputNodeId ?? string.Empty).Trim();
		public IReadOnlyList<PatchGraphNode> Nodes => _nodes ?? (IReadOnlyList<PatchGraphNode>)Array.Empty<PatchGraphNode>();
		public IReadOnlyList<PatchGraphConnection> Connections => _connections ?? (IReadOnlyList<PatchGraphConnection>)Array.Empty<PatchGraphConnection>();

		public PatchProgramGraph() { }

		public PatchProgramGraph(string outputNodeId, IEnumerable<PatchGraphNode> nodes, IEnumerable<PatchGraphConnection> connections) {
			_outputNodeId = outputNodeId;
			_nodes = new List<PatchGraphNode>(nodes ?? Enumerable.Empty<PatchGraphNode>());
			_connections = new List<PatchGraphConnection>(connections ?? Enumerable.Empty<PatchGraphConnection>());
		}

		public UnitResult<Diagnostic> Validate() {
			if (string.IsNullOrWhiteSpace(OutputNodeId))
				return Failure("patch.definition.graph.endpoint", "A patch program graph requires an output node ID.");
			if (Nodes.Count == 0) return Failure("patch.definition.graph.nodes", "A patch program graph requires at least one node.");
			if (Nodes.Any(node => node == null || string.IsNullOrWhiteSpace(node.Id) || !NodeTypeId.TryParse(node.TypeId, out _)))
				return Failure("patch.definition.graph.node", "Every patch graph node requires an ID and a valid node type ID.");
			if (Nodes.GroupBy(node => node.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
				return Failure("patch.definition.graph.node_duplicate", "Patch graph node IDs must be unique.");
			if (!Nodes.Any(node => string.Equals(node.Id, OutputNodeId, StringComparison.Ordinal)))
				return Failure("patch.definition.graph.output_missing", "The patch graph output must reference a configured node.");
			var sceneNodes = Nodes.Where(node => node.IsSceneNode).ToArray();
			if (sceneNodes.Any(node => node.SceneDefinition == null || string.IsNullOrWhiteSpace(node.SceneDefinition.Id)
				|| node.SceneDefinition.Validate().IsFailure))
				return Failure("patch.definition.graph.scene", "Every 3D scene node requires a valid Scene3DDefinition.");
			if (Nodes.Any(node => !node.IsSceneNode && node.SceneDefinition != null))
				return Failure("patch.definition.graph.scene_asset", "Only 3D scene nodes may reference a Scene3DDefinition.");
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
				return Failure("patch.definition.graph.source_port", "Patch graph nodes expose the image output port only.");
			if (Connections.Any(connection => !Nodes.Any(node => string.Equals(node.Id, connection.SourceNodeId, StringComparison.Ordinal))))
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
				if (adjacency.ContainsKey(connection.SourceNodeId))
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

	public enum PatchBeatSignal {
		BeatPulse
	}

	[Serializable]
	public sealed class PatchBeatModulation {
		[SerializeField] private bool m_Enabled;
		[SerializeField] private PatchBeatSignal m_Signal = PatchBeatSignal.BeatPulse;
		[SerializeField] private float m_Strength;
		[SerializeField] private float m_Minimum;
		[SerializeField] private float m_Maximum = 1f;

		public bool IsEnabled => m_Enabled;
		public PatchBeatSignal Signal => m_Signal;
		public float Strength => m_Strength;
		public float Minimum => m_Minimum;
		public float Maximum => m_Maximum;

		public PatchBeatModulation() { }

		public PatchBeatModulation(bool enabled, float strength, float minimum = 0f, float maximum = 1f) {
			m_Enabled = enabled;
			m_Strength = strength;
			m_Minimum = minimum;
			m_Maximum = maximum;
			if (Validate().IsFailure) throw new ArgumentException("Patch beat modulation values are invalid.");
		}

		public float Resolve(float baseValue, BeatClockFrame frame) {
			if (!m_Enabled) return baseValue;
			var signal = m_Signal == PatchBeatSignal.BeatPulse ? frame.BeatPulse : throw new InvalidOperationException("The beat modulation signal is unknown.");
			return Mathf.Clamp(baseValue + signal * m_Strength, m_Minimum, m_Maximum);
		}

		public UnitResult<Diagnostic> Validate() {
			if (!Enum.IsDefined(typeof(PatchBeatSignal), m_Signal))
				return Failure("patch.definition.beat_modulation.signal", "A patch beat modulation signal is unknown.");
			if (float.IsNaN(m_Strength) || float.IsInfinity(m_Strength) || float.IsNaN(m_Minimum) || float.IsInfinity(m_Minimum)
				|| float.IsNaN(m_Maximum) || float.IsInfinity(m_Maximum) || m_Minimum > m_Maximum)
				return Failure("patch.definition.beat_modulation.range", "A patch beat modulation requires finite ordered values.");
			return UnitResult.Success<Diagnostic>();
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
		[SerializeField] private PatchBeatModulation m_BeatModulation = new PatchBeatModulation();

		public string Id => _id ?? string.Empty;
		public string DisplayName => _displayName ?? string.Empty;
		public string NodeId => _nodeId ?? string.Empty;
		public string ParameterId => _parameterId ?? string.Empty;
		public PatchBeatModulation BeatModulation => m_BeatModulation;
	}

	[Serializable]
	public sealed class PatchHotCue {
		[SerializeField] private List<PatchGraphParameter> m_Values = new List<PatchGraphParameter>();

		public IReadOnlyList<PatchGraphParameter> Values
			=> m_Values ?? (IReadOnlyList<PatchGraphParameter>)Array.Empty<PatchGraphParameter>();

		public PatchHotCue() { }

		public PatchHotCue(IEnumerable<PatchGraphParameter> values) {
			m_Values = new List<PatchGraphParameter>(values ?? Enumerable.Empty<PatchGraphParameter>());
		}
	}

	[Serializable]
	public sealed class PatchKeyboardInputBinding {
		[SerializeField] private Key m_Key = Key.None;
		[SerializeField] private string m_ParameterId;

		public Key Key => m_Key;
		public string ParameterId => (m_ParameterId ?? string.Empty).Trim();

		public PatchKeyboardInputBinding() { }

		public PatchKeyboardInputBinding(string parameterId, Key key) {
			m_ParameterId = parameterId ?? string.Empty;
			m_Key = key;
		}

		public bool Matches(Key key) => m_Key == key;
		public float Value() => 1f;

		public UnitResult<Diagnostic> Validate() {
			if (m_Key == Key.None || !Enum.IsDefined(typeof(Key), m_Key))
				return Failure("patch.definition.keyboard_input.key", "A patch keyboard input requires a valid key.");
			if (string.IsNullOrWhiteSpace(ParameterId))
				return Failure("patch.definition.keyboard_input.parameter", "A patch keyboard input requires a target parameter.");
			return UnitResult.Success<Diagnostic>();
		}

		private static UnitResult<Diagnostic> Failure(string code, string message)
			=> UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
	}

	[Serializable]
	public sealed class PatchMidiInputBinding {
		[SerializeField] private MidiControlKind m_MessageType = MidiControlKind.ControlChange;
		[SerializeField, Range(1, 16)] private int m_Channel = 1;
		[SerializeField, Range(0, 127)] private int m_Number;
		[SerializeField] private int m_RawMinimum;
		[SerializeField] private int m_RawMaximum = 127;
		[SerializeField] private bool m_Invert;
		[SerializeField] private string m_ParameterId;

		public MidiControlKind MessageType => m_MessageType;
		public int Channel => m_Channel;
		public int Number => m_Number;
		public int RawMinimum => m_RawMinimum;
		public int RawMaximum => m_RawMaximum;
		public bool Invert => m_Invert;
		public string ParameterId => (m_ParameterId ?? string.Empty).Trim();

		public PatchMidiInputBinding() { }

		public PatchMidiInputBinding(string parameterId, MidiControlKind messageType, int channel, int number,
			int rawMinimum = 0, int rawMaximum = 127, bool invert = false) {
			m_ParameterId = parameterId ?? string.Empty;
			m_MessageType = messageType;
			m_Channel = channel;
			m_Number = number;
			m_RawMinimum = rawMinimum;
			m_RawMaximum = rawMaximum;
			m_Invert = invert;
		}

		public bool Matches(MidiControl control)
			=> control.Kind == MessageType && control.Channel == Channel && control.Number == Number;

		public float Normalize(int rawValue) {
			if (RawMinimum >= RawMaximum) throw new InvalidOperationException("Raw Minimum must be less than Raw Maximum.");
			var normalized = Mathf.Clamp01((rawValue - RawMinimum) / (float)(RawMaximum - RawMinimum));
			return Invert ? 1f - normalized : normalized;
		}

		public UnitResult<Diagnostic> Validate() {
			if (!Enum.IsDefined(typeof(MidiControlKind), MessageType))
				return Failure("patch.definition.midi_input.type", "A patch MIDI input requires a valid message type.");
			if (Channel < 1 || Channel > 16)
				return Failure("patch.definition.midi_input.channel", "A patch MIDI input channel must be between 1 and 16.");
			if ((MessageType == MidiControlKind.PitchBend && Number != 0) || Number < 0 || Number > 127)
				return Failure("patch.definition.midi_input.number", "A patch MIDI input number is outside the supported range.");

			var nativeMaximum = MessageType == MidiControlKind.PitchBend ? 16383 : 127;
			if (RawMinimum < 0 || RawMaximum > nativeMaximum || RawMinimum >= RawMaximum)
				return Failure("patch.definition.midi_input.range", "A patch MIDI input raw range is invalid.");
			if (string.IsNullOrWhiteSpace(ParameterId))
				return Failure("patch.definition.midi_input.parameter", "A patch MIDI input requires a target parameter.");
			return UnitResult.Success<Diagnostic>();
		}

		private static UnitResult<Diagnostic> Failure(string code, string message)
			=> UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
	}

	/// <summary>Logical live patch composed from graph nodes and published controls.</summary>
	[CreateAssetMenu(fileName = "PatchDefinition", menuName = "ShitDesigner/Patch Definition")]
	public sealed class PatchDefinition : ScriptableObject {
		public const int HotCueCount = 2;

		[SerializeField] private string _id;
		[SerializeField] private string _displayName;
		[SerializeField] private PatchProgramGraph _programGraph = new PatchProgramGraph();
		[SerializeField] private List<PatchParameter> _parameters = new List<PatchParameter>();
		[SerializeField] private PatchHotCue m_HotCue1 = new PatchHotCue();
		[SerializeField] private PatchHotCue m_HotCue2 = new PatchHotCue();
		[SerializeField] private List<PatchKeyboardInputBinding> m_KeyboardInputs = new List<PatchKeyboardInputBinding>();
		[SerializeField] private List<PatchMidiInputBinding> m_MidiInputs = new List<PatchMidiInputBinding>();

		public string Id => _id ?? string.Empty;
		public string DisplayName => _displayName ?? string.Empty;
		public PatchProgramGraph ProgramGraph => _programGraph;
		public IReadOnlyList<PatchParameter> Parameters => _parameters ?? (IReadOnlyList<PatchParameter>)Array.Empty<PatchParameter>();
		public IReadOnlyList<PatchKeyboardInputBinding> KeyboardInputs => m_KeyboardInputs ?? (IReadOnlyList<PatchKeyboardInputBinding>)Array.Empty<PatchKeyboardInputBinding>();
		public IReadOnlyList<PatchMidiInputBinding> MidiInputs => m_MidiInputs ?? (IReadOnlyList<PatchMidiInputBinding>)Array.Empty<PatchMidiInputBinding>();

		public PatchHotCue GetHotCue(int index) {
			switch (index) {
				case 0: return m_HotCue1;
				case 1: return m_HotCue2;
				default: throw new ArgumentOutOfRangeException(nameof(index));
			}
		}

		public UnitResult<Diagnostic> Validate() {
			if (string.IsNullOrWhiteSpace(Id)) return Failure("patch.definition.id", "A patch requires an ID.");
			if (string.IsNullOrWhiteSpace(DisplayName)) return Failure("patch.definition.name", "A patch requires a display name.");
			if (ProgramGraph == null) return Failure("patch.definition.graph", "A patch requires a program graph.");
			var graphValidation = ProgramGraph.Validate();
			if (graphValidation.IsFailure) return graphValidation;
			if (Parameters.Any(parameter => parameter == null || string.IsNullOrWhiteSpace(parameter.Id) || string.IsNullOrWhiteSpace(parameter.DisplayName) || string.IsNullOrWhiteSpace(parameter.NodeId) || string.IsNullOrWhiteSpace(parameter.ParameterId)))
				return Failure("patch.definition.parameter", "Published patch parameters require IDs, names, nodes, and parameter IDs.");
			if (Parameters.GroupBy(parameter => parameter.Id, StringComparer.Ordinal).Any(group => group.Count() > 1)) return Failure("patch.definition.parameter_duplicate", "Published patch parameter IDs must be unique.");
			foreach (var parameter in Parameters) {
				if (parameter.BeatModulation != null && parameter.BeatModulation.Validate().IsFailure)
					return Failure("patch.definition.parameter_modulation", "A published patch parameter has invalid beat modulation settings.");
				var graphNode = ProgramGraph.Nodes.FirstOrDefault(node => string.Equals(node.Id, parameter.NodeId, StringComparison.Ordinal));
				if (graphNode == null)
					return Failure("patch.definition.parameter_node", "A published patch parameter references an unknown patch graph node.");
				if (graphNode.IsSceneNode) continue;
				var graphParameter = graphNode.FindParameter(parameter.ParameterId);
				if (graphParameter == null || !PatchGraphParameter.IsLiveControllable(graphParameter.Type))
					return Failure("patch.definition.parameter_graph", "A published graph parameter must reference a configured parameter supported by the live renderer.");
				if (parameter.BeatModulation != null && parameter.BeatModulation.IsEnabled && graphParameter.Type != ParameterType.Float)
					return Failure("patch.definition.parameter_modulation_type", "Beat-modulated graph parameters must use the float type.");
			}
			for (var hotCueIndex = 0; hotCueIndex < HotCueCount; hotCueIndex++) {
				var hotCue = GetHotCue(hotCueIndex);
				if (hotCue == null) continue;
				if (hotCue.Values.Any(value => value == null || string.IsNullOrWhiteSpace(value.Id) || !value.TryGetValue(out _)))
					return Failure("patch.definition.hot_cue.value", "Every Hot Cue value requires a published parameter ID and finite value.");
				if (hotCue.Values.GroupBy(value => value.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
					return Failure("patch.definition.hot_cue.duplicate", "A Hot Cue cannot assign the same published parameter more than once.");
				foreach (var value in hotCue.Values) {
					var parameter = Parameters.FirstOrDefault(candidate => string.Equals(candidate.Id, value.Id, StringComparison.Ordinal));
					if (parameter == null)
						return Failure("patch.definition.hot_cue.parameter", "A Hot Cue references an unknown published parameter.");
					var graphNode = ProgramGraph.Nodes.First(node => string.Equals(node.Id, parameter.NodeId, StringComparison.Ordinal));
					var expectedType = graphNode.IsSceneNode ? ParameterType.Float : graphNode.FindParameter(parameter.ParameterId).Type;
					if (value.Type != expectedType)
						return Failure("patch.definition.hot_cue.type", "A Hot Cue value type does not match its published parameter.");
				}
			}
			if (KeyboardInputs.Any(binding => binding == null || binding.Validate().IsFailure)) return Failure("patch.definition.keyboard_input", "Every patch keyboard input must be valid.");
			if (KeyboardInputs.Any(binding => !Parameters.Any(parameter => parameter != null && string.Equals(parameter.Id, binding.ParameterId, StringComparison.Ordinal))))
				return Failure("patch.definition.keyboard_input_parameter", "A patch keyboard input references an unknown published parameter.");
			if (MidiInputs.Any(binding => binding == null || binding.Validate().IsFailure)) return Failure("patch.definition.midi_input", "Every patch MIDI input must be valid.");
			if (MidiInputs.Any(binding => !Parameters.Any(parameter => parameter != null && string.Equals(parameter.Id, binding.ParameterId, StringComparison.Ordinal))))
				return Failure("patch.definition.midi_input_parameter", "A patch MIDI input references an unknown published parameter.");
			return UnitResult.Success<Diagnostic>();
		}

		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
	}
}
