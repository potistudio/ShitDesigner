using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ShitDesigner.Core;
using ShitDesigner.Main;
using ShitDesigner.Nodes;
using ShitDesigner.Scene;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	[CustomEditor(typeof(PatchDefinition))]
	[CanEditMultipleObjects]
	public sealed class PatchDefinitionEditor : UnityEditor.Editor {
		private SerializedProperty _id;
		private SerializedProperty _displayName;
		private SerializedProperty _programGraph;
		private SerializedProperty _parameters;
		private SerializedProperty m_HotCue1;
		private SerializedProperty m_HotCue2;
		private SerializedProperty m_KeyboardInputs;
		private SerializedProperty m_MidiInputs;

		private void OnEnable() {
			_id = serializedObject.FindProperty("_id");
			_displayName = serializedObject.FindProperty("_displayName");
			_programGraph = serializedObject.FindProperty("_programGraph");
			_parameters = serializedObject.FindProperty("_parameters");
			m_HotCue1 = serializedObject.FindProperty("m_HotCue1");
			m_HotCue2 = serializedObject.FindProperty("m_HotCue2");
			m_KeyboardInputs = serializedObject.FindProperty("m_KeyboardInputs");
			m_MidiInputs = serializedObject.FindProperty("m_MidiInputs");
		}

		public override void OnInspectorGUI() {
			serializedObject.Update();
			if (string.IsNullOrWhiteSpace(_id.stringValue) && !string.IsNullOrWhiteSpace(_displayName.stringValue))
				_id.stringValue = Slug(_displayName.stringValue);

			EditorGUILayout.LabelField("Patch", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(_id, new GUIContent("ID", "Stable patch ID. It is generated from Display Name when empty."));
			EditorGUILayout.PropertyField(_displayName, new GUIContent("Display Name"));

			EditorGUILayout.Space(6f);
			DrawProgramGraph();

			EditorGUILayout.Space(6f);
			EditorGUILayout.PropertyField(_parameters, new GUIContent("Published Parameters"), true);
			EditorGUILayout.LabelField("Hot Cues", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(m_HotCue1, new GUIContent("Hot Cue 1 ([)", "Values reference Program Graph parameters. Node ID is optional when the parameter ID is unique."), true);
			EditorGUILayout.PropertyField(m_HotCue2, new GUIContent("Hot Cue 2 (])", "Values reference Program Graph parameters. Node ID is optional when the parameter ID is unique."), true);

			EditorGUILayout.Space(6f);
			EditorGUILayout.PropertyField(m_KeyboardInputs, new GUIContent("Keyboard Inputs", "Maps key presses to published parameters while this patch is loaded. A press sends 1.0; release sends no request."), true);

			EditorGUILayout.Space(6f);
			EditorGUILayout.PropertyField(m_MidiInputs, new GUIContent("MIDI Inputs", "Maps MIDI controls to published parameters while this patch is loaded."), true);

			serializedObject.ApplyModifiedProperties();
			DrawValidationMessage();
		}

		private void DrawProgramGraph() {
			EditorGUILayout.LabelField("Program Graph", EditorStyles.boldLabel);
			if (_programGraph == null) {
				EditorGUILayout.HelpBox("Program graph data is missing.", MessageType.Error);
				return;
			}

			var outputNodeId = _programGraph.FindPropertyRelative("_outputNodeId");
			var nodes = _programGraph.FindPropertyRelative("_nodes");
			var connections = _programGraph.FindPropertyRelative("_connections");

			EditorGUILayout.PropertyField(nodes, new GUIContent("Nodes"), true);
			EnsureGraphNodeIds(nodes);
			var nodeIds = GetGraphNodeIds(nodes, out var nodeLabels);
			if (nodeIds.Count > 0) DrawPopup("Output Node", outputNodeId, nodeIds, nodeLabels);
			else EditorGUILayout.HelpBox("Add a graph node to choose the program output.", MessageType.Info);
			EditorGUILayout.PropertyField(connections, new GUIContent("Connections"), true);
		}

		private void DrawValidationMessage() {
			var definition = target as PatchDefinition;
			if (definition == null) return;
			var validation = definition.Validate();
			if (validation.IsFailure) EditorGUILayout.HelpBox(validation.Error.Message, MessageType.Error);
		}

		private static bool DrawPopup(string label, SerializedProperty property, IReadOnlyList<string> values, IReadOnlyList<string> labels) {
			if (property == null || values == null || values.Count == 0) return false;
			var current = property.stringValue ?? string.Empty;
			var popupValues = values.ToList();
			var popupLabels = (labels == null ? values : labels).ToList();
			if (string.IsNullOrWhiteSpace(current)) {
				current = popupValues[0];
				property.stringValue = current;
			}
			if (!popupValues.Contains(current, StringComparer.Ordinal)) {
				popupValues.Insert(0, current);
				popupLabels.Insert(0, "Missing: " + current);
			}
			var selected = popupValues.IndexOf(current);
			var next = EditorGUILayout.Popup(label, selected, popupLabels.ToArray());
			if (next == selected) return false;
			property.stringValue = popupValues[next];
			return true;
		}

		private static List<string> GetGraphNodeIds(SerializedProperty nodes, out List<string> labels) {
			var ids = new List<string>();
			labels = new List<string>();
			for (var index = 0; index < nodes.arraySize; index++) {
				var node = nodes.GetArrayElementAtIndex(index);
				var id = node.FindPropertyRelative("_id").stringValue;
				if (string.IsNullOrWhiteSpace(id)) continue;
				ids.Add(id);
				labels.Add(id + " · " + node.FindPropertyRelative("_typeId").stringValue);
			}
			return ids;
		}

		private static void EnsureGraphNodeIds(SerializedProperty nodes) {
			var used = new HashSet<string>(StringComparer.Ordinal);
			for (var index = 0; index < nodes.arraySize; index++) {
				var node = nodes.GetArrayElementAtIndex(index);
				var id = node.FindPropertyRelative("_id");
				if (!string.IsNullOrWhiteSpace(id.stringValue)) {
					used.Add(id.stringValue);
					continue;
				}
				id.stringValue = CreateUniqueId(Slug(node.FindPropertyRelative("_typeId").stringValue), used);
				used.Add(id.stringValue);
			}
		}

		private static string CreateUniqueId(string preferred, IEnumerable<string> existing) {
			var used = new HashSet<string>((existing ?? Enumerable.Empty<string>()).Where(value => !string.IsNullOrWhiteSpace(value)), StringComparer.Ordinal);
			var baseId = string.IsNullOrWhiteSpace(preferred) ? "item" : preferred;
			if (!used.Contains(baseId)) return baseId;
			for (var index = 2; ; index++) {
				var candidate = baseId + "_" + index;
				if (!used.Contains(candidate)) return candidate;
			}
		}

		private static string Slug(string value) {
			var result = new System.Text.StringBuilder();
			foreach (var character in value ?? string.Empty) {
				if (char.IsLetterOrDigit(character)) result.Append(char.ToLowerInvariant(character));
				else if (result.Length > 0 && result[result.Length - 1] != '_') result.Append('_');
			}
			while (result.Length > 0 && result[result.Length - 1] == '_') result.Length--;
			if (result.Length == 0) return "item";
			if (char.IsDigit(result[0])) result.Insert(0, "item_");
			return result.ToString();
		}
	}

	[CustomPropertyDrawer(typeof(PatchKeyboardInputBinding))]
	public sealed class PatchKeyboardInputBindingDrawer : PropertyDrawer {
		private const float LineSpacing = 2f;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
			EditorGUI.BeginProperty(position, label, property);
			var key = property.FindPropertyRelative("m_Key");
			var parameterId = property.FindPropertyRelative("m_ParameterId");
			var y = position.y;

			EditorGUI.PropertyField(Line(position, ref y), key, new GUIContent("Key"));
			PatchInputBindingDrawerUtility.DrawParameterPopup(Line(position, ref y), parameterId,
				property.serializedObject.FindProperty("_parameters"));

			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
			=> EditorGUIUtility.singleLineHeight * 2f + LineSpacing;

		private static Rect Line(Rect position, ref float y) {
			var line = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
			y += EditorGUIUtility.singleLineHeight + LineSpacing;
			return line;
		}
	}

	[CustomPropertyDrawer(typeof(PatchMidiInputBinding))]
	public sealed class PatchMidiInputBindingDrawer : PropertyDrawer {
		private const float LineSpacing = 2f;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
			EditorGUI.BeginProperty(position, label, property);
			var messageType = property.FindPropertyRelative("m_MessageType");
			var channel = property.FindPropertyRelative("m_Channel");
			var number = property.FindPropertyRelative("m_Number");
			var rawMinimum = property.FindPropertyRelative("m_RawMinimum");
			var rawMaximum = property.FindPropertyRelative("m_RawMaximum");
			var invert = property.FindPropertyRelative("m_Invert");
			var parameterId = property.FindPropertyRelative("m_ParameterId");
			var y = position.y;
			var previousType = (MidiControlKind)messageType.enumValueIndex;

			EditorGUI.PropertyField(Line(position, ref y), messageType, new GUIContent("Message Type"));
			var currentType = (MidiControlKind)messageType.enumValueIndex;
			if (previousType != currentType && rawMaximum.intValue == NativeMaximum(previousType))
				rawMaximum.intValue = NativeMaximum(currentType);

			channel.intValue = EditorGUI.IntSlider(Line(position, ref y), new GUIContent("Channel"), channel.intValue, 1, 16);
			number.intValue = EditorGUI.IntSlider(Line(position, ref y), new GUIContent("Number"), number.intValue, 0,
				currentType == MidiControlKind.PitchBend ? 0 : 127);
			rawMinimum.intValue = EditorGUI.IntField(Line(position, ref y), new GUIContent("Raw Minimum"), rawMinimum.intValue);
			rawMaximum.intValue = EditorGUI.IntField(Line(position, ref y), new GUIContent("Raw Maximum"), rawMaximum.intValue);
			EditorGUI.PropertyField(Line(position, ref y), invert, new GUIContent("Invert"));
			PatchInputBindingDrawerUtility.DrawParameterPopup(Line(position, ref y), parameterId,
				property.serializedObject.FindProperty("_parameters"));

			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
			=> EditorGUIUtility.singleLineHeight * 7f + LineSpacing * 6f;

		private static int NativeMaximum(MidiControlKind type) => type == MidiControlKind.PitchBend ? 16383 : 127;

		private static Rect Line(Rect position, ref float y) {
			var line = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
			y += EditorGUIUtility.singleLineHeight + LineSpacing;
			return line;
		}
	}

	internal static class PatchInputBindingDrawerUtility {
		internal static void DrawParameterPopup(Rect position, SerializedProperty parameterId, SerializedProperty parameters) {
			if (parameters == null || parameters.arraySize == 0) {
				EditorGUI.PropertyField(position, parameterId, new GUIContent("Parameter ID"));
				return;
			}

			var values = new List<string>();
			var labels = new List<string>();
			for (var index = 0; index < parameters.arraySize; index++) {
				var parameter = parameters.GetArrayElementAtIndex(index);
				var id = parameter.FindPropertyRelative("_id").stringValue;
				if (string.IsNullOrWhiteSpace(id)) continue;
				values.Add(id);
				var displayName = parameter.FindPropertyRelative("_displayName").stringValue;
				labels.Add(string.IsNullOrWhiteSpace(displayName) ? id : displayName + " (" + id + ")");
			}
			if (values.Count == 0) {
				EditorGUI.PropertyField(position, parameterId, new GUIContent("Parameter ID"));
				return;
			}

			var current = parameterId.stringValue ?? string.Empty;
			if (string.IsNullOrWhiteSpace(current)) {
				values.Insert(0, string.Empty);
				labels.Insert(0, "<Select Parameter>");
			}
			else if (!values.Contains(current, StringComparer.Ordinal)) {
				values.Insert(0, current);
				labels.Insert(0, "Missing: " + current);
			}

			var selected = values.IndexOf(current);
			var next = EditorGUI.Popup(position, "Parameter", selected, labels.ToArray());
			if (next != selected) parameterId.stringValue = values[next];
		}
	}

	[CustomPropertyDrawer(typeof(PatchGraphNode))]
	public sealed class PatchGraphNodeDrawer : PropertyDrawer {
		private const string ManifestAssetPath = "Assets/ShitDesigner/Scripts/Modules/Nodes/ShaderNodeManifest.asset";
		private const string VideoPlayerTypeId = "shitdesigner.video.player";
		private const float LineSpacing = 2f;
		private static ShaderNodeManifestAsset _manifest;
		private static NodeDefinitionCatalog _catalog;
		private static ShaderNodeManifestAsset _catalogManifest;
		private static readonly Dictionary<string, int> ParameterAddSelections = new Dictionary<string, int>(StringComparer.Ordinal);

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
			EditorGUI.BeginProperty(position, label, property);
			var id = property.FindPropertyRelative("_id");
			var typeId = property.FindPropertyRelative("_typeId");
			var parameters = property.FindPropertyRelative("_parameters");
			var y = position.y;
			property.isExpanded = EditorGUI.Foldout(Line(position, ref y), property.isExpanded, label, true);
			if (property.isExpanded) {
				var indent = EditorGUI.indentLevel;
				EditorGUI.indentLevel++;
				EditorGUI.PropertyField(Line(position, ref y), id, new GUIContent("ID"));
				var typeChanged = DrawTypeIdPopup(Line(position, ref y), typeId);
				var sceneDefinition = property.FindPropertyRelative("m_SceneDefinition");
				var videoPath = property.FindPropertyRelative("m_VideoPath");
				var videoClip = property.FindPropertyRelative("m_VideoClip");
				var isSceneNode = IsSceneNode(typeId.stringValue);
				if (isSceneNode && sceneDefinition != null) {
					EditorGUI.PropertyField(Line(position, ref y), sceneDefinition, new GUIContent("Scene Definition"));
				}
				else if (typeChanged && sceneDefinition != null) {
					sceneDefinition.objectReferenceValue = null;
				}
				if (IsVideoPlayer(typeId.stringValue) && videoClip != null) {
					EditorGUI.BeginChangeCheck();
					EditorGUI.PropertyField(Line(position, ref y), videoClip, new GUIContent("Video Clip"));
					if (EditorGUI.EndChangeCheck() && videoPath != null)
						videoPath.stringValue = string.Empty;
				}
				else if (typeChanged && videoPath != null) {
					videoPath.stringValue = string.Empty;
				}
				if (!IsVideoPlayer(typeId.stringValue) && typeChanged && videoClip != null) {
					videoClip.objectReferenceValue = null;
				}
				if (isSceneNode && typeChanged) {
					if (videoPath != null) videoPath.stringValue = string.Empty;
					if (videoClip != null) videoClip.objectReferenceValue = null;
				}
				var parametersHeight = EditorGUI.GetPropertyHeight(parameters, new GUIContent("Parameters"), true);
				EditorGUI.PropertyField(new Rect(position.x, y, position.width, parametersHeight), parameters, new GUIContent("Parameters"), true);
				y += parametersHeight + LineSpacing;
				DrawAddParameter(Line(position, ref y), property.propertyPath, typeId.stringValue, parameters);
				EditorGUI.indentLevel = indent;
			}
			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
			if (!property.isExpanded) return EditorGUIUtility.singleLineHeight;
			var parameters = property.FindPropertyRelative("_parameters");
			var typeId = property.FindPropertyRelative("_typeId").stringValue;
			var isVideoPlayer = IsVideoPlayer(typeId);
			var isSceneNode = IsSceneNode(typeId);
			var fixedLineCount = isVideoPlayer || isSceneNode ? 4f : 3f;
			var fixedSpacingCount = isVideoPlayer || isSceneNode ? 4f : 3f;
			var height = EditorGUIUtility.singleLineHeight * fixedLineCount + LineSpacing * fixedSpacingCount
				+ EditorGUI.GetPropertyHeight(parameters, new GUIContent("Parameters"), true);
			return GetAddableParameters(property.FindPropertyRelative("_typeId").stringValue, parameters).Count == 0
				? height
				: height + EditorGUIUtility.singleLineHeight + LineSpacing;
		}

		private static void DrawAddParameter(Rect position, string key, string typeId, SerializedProperty parameters) {
			var addable = GetAddableParameters(typeId, parameters);
			if (addable.Count == 0) return;
			if (!ParameterAddSelections.TryGetValue(key, out var selected)) selected = 0;
			selected = Mathf.Clamp(selected, 0, addable.Count - 1);
			var field = EditorGUI.PrefixLabel(position, new GUIContent("Add Parameter"));
			const float buttonWidth = 48f;
			var popup = new Rect(field.x, field.y, field.width - buttonWidth - LineSpacing, field.height);
			var button = new Rect(popup.xMax + LineSpacing, field.y, buttonWidth, field.height);
			selected = EditorGUI.Popup(popup, selected, addable.Select(FormatParameterLabel).ToArray());
			ParameterAddSelections[key] = selected;
			if (GUI.Button(button, "Add")) AddParameter(parameters, typeId, addable[selected].Id.Value);
		}

		private static List<NodeParameterDefinition> GetAddableParameters(string typeId, SerializedProperty parameters) {
			var entry = ResolveNodeEntry(typeId);
			if (entry == null) return new List<NodeParameterDefinition>();
			var existing = new HashSet<string>(StringComparer.Ordinal);
			for (var index = 0; index < parameters.arraySize; index++)
				existing.Add(parameters.GetArrayElementAtIndex(index).FindPropertyRelative("_id").stringValue);
			return entry.Parameters
				.Where(parameter => parameter != null && !parameter.IsHidden && !existing.Contains(parameter.Id.Value))
				.OrderBy(parameter => parameter.DisplayOrder)
				.ThenBy(parameter => parameter.DisplayName, StringComparer.Ordinal)
				.ToList();
		}

		private static void AddParameter(SerializedProperty parameters, string typeId, string parameterId) {
			var runtimeDefinition = ResolveNodeEntry(typeId)?.Definition.FindParameter(new ParameterId(parameterId));
			if (runtimeDefinition == null) return;
			var index = parameters.arraySize;
			parameters.arraySize++;
			var parameter = parameters.GetArrayElementAtIndex(index);
			parameter.FindPropertyRelative("_id").stringValue = runtimeDefinition.Id.Value;
			SetParameterValue(parameter, runtimeDefinition.DefaultValue);
		}

		private static void SetParameterValue(SerializedProperty parameter, ParameterValue value) {
			parameter.FindPropertyRelative("_type").enumValueIndex = (int)value.Type;
			switch (value.Type) {
				case ParameterType.Float: parameter.FindPropertyRelative("_floatValue").floatValue = value.AsFloat(); break;
				case ParameterType.Int: parameter.FindPropertyRelative("_intValue").intValue = value.AsInt(); break;
				case ParameterType.Bool: parameter.FindPropertyRelative("_boolValue").boolValue = value.AsBool(); break;
				case ParameterType.Vector2:
					var vector2 = value.AsVector2(); parameter.FindPropertyRelative("_vector2Value").vector2Value = new Vector2(vector2.X, vector2.Y); break;
				case ParameterType.Vector3:
					var vector3 = value.AsVector3(); parameter.FindPropertyRelative("_vector3Value").vector3Value = new Vector3(vector3.X, vector3.Y, vector3.Z); break;
				case ParameterType.Vector4:
					var vector4 = value.AsVector4(); parameter.FindPropertyRelative("_vector4Value").vector4Value = new Vector4(vector4.X, vector4.Y, vector4.Z, vector4.W); break;
				case ParameterType.Color:
					var color = value.AsColor(); parameter.FindPropertyRelative("_colorValue").colorValue = new Color(color.R, color.G, color.B, color.A); break;
				case ParameterType.String:
				case ParameterType.Enum:
				case ParameterType.MediaAssetReference: parameter.FindPropertyRelative("_textValue").stringValue = value.AsString() ?? string.Empty; break;
			}
		}

		private static bool DrawTypeIdPopup(Rect position, SerializedProperty typeId) {
			var catalog = ResolveCatalog();
			if (catalog == null) {
				EditorGUI.PropertyField(position, typeId, new GUIContent("Type ID"));
				return false;
			}

			var entries = catalog.Entries
				.Where(entry => entry != null && entry.UserAddable && !entry.SystemOwned
					&& (entry.ShaderBinding != null || entry.TypeId.Value == VideoPlayerTypeId || IsSceneNode(entry.TypeId.Value)))
				.OrderBy(entry => entry.Category, StringComparer.Ordinal)
				.ThenBy(entry => entry.DisplayName, StringComparer.Ordinal)
				.ThenBy(entry => entry.TypeId.Value, StringComparer.Ordinal)
				.ToList();
			if (entries.Count == 0) {
				EditorGUI.PropertyField(position, typeId, new GUIContent("Type ID"));
				return false;
			}

			var values = entries.Select(entry => entry.TypeId.Value).ToList();
			var labels = entries.Select(FormatLabel).ToList();
			var current = typeId.stringValue ?? string.Empty;
			if (string.IsNullOrWhiteSpace(current)) {
				values.Insert(0, string.Empty);
				labels.Insert(0, "<None>");
			}
			else if (!values.Contains(current, StringComparer.Ordinal)) {
				values.Insert(0, current);
				labels.Insert(0, "Missing: " + current);
			}

			var selected = values.IndexOf(current);
			var field = EditorGUI.PrefixLabel(position, new GUIContent("Type ID"));
			var next = EditorGUI.Popup(field, selected, labels.ToArray());
			if (next == selected) return false;
			typeId.stringValue = values[next];
			return true;
		}

		internal static ShaderNodeManifestAsset ResolveManifest() {
			if (_manifest != null) return _manifest;
			_manifest = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(ManifestAssetPath);
			if (_manifest != null) return _manifest;
			var matches = AssetDatabase.FindAssets("t:ShaderNodeManifestAsset");
			for (var index = 0; index < matches.Length; index++) {
				var candidate = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(AssetDatabase.GUIDToAssetPath(matches[index]));
				if (candidate == null) continue;
				_manifest = candidate;
				return _manifest;
			}
			return null;
		}

		private static NodeDefinitionCatalog ResolveCatalog() {
			var manifest = ResolveManifest();
			if (manifest == null) return null;
			if (_catalog != null && ReferenceEquals(_catalogManifest, manifest)) return _catalog;
			try {
				_catalog = NodeDefinitionCatalog.CreateInitial(manifest.BuildRuntimeManifest());
				_catalogManifest = manifest;
				return _catalog;
			}
			catch (Exception exception) {
				Debug.LogException(exception);
				_catalog = null;
				_catalogManifest = null;
				return null;
			}
		}

		private static NodeCatalogEntry ResolveNodeEntry(string typeId)
			=> ResolveCatalog()?.Entries.FirstOrDefault(entry => entry != null && string.Equals(entry.TypeId.Value, typeId ?? string.Empty, StringComparison.Ordinal));

		internal static NodeCatalogEntry ResolveNodeEntryForConnection(string typeId) => ResolveNodeEntry(typeId);

		private static bool IsVideoPlayer(string typeId) => string.Equals(typeId, VideoPlayerTypeId, StringComparison.Ordinal);

		internal static bool IsSceneNode(string typeId) => string.Equals(typeId, PatchGraphNode.Scene3DTypeId, StringComparison.Ordinal);

		private static string FormatLabel(NodeCatalogEntry entry)
			=> string.IsNullOrWhiteSpace(entry.Category)
				? entry.DisplayName + " (" + entry.TypeId.Value + ")"
				: entry.Category + "/" + entry.DisplayName + " (" + entry.TypeId.Value + ")";

		private static string FormatParameterLabel(NodeParameterDefinition parameter)
			=> string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Id.Value : parameter.DisplayName + " (" + parameter.Id.Value + ")";

		private static Rect Line(Rect position, ref float y) {
			var line = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
			y += EditorGUIUtility.singleLineHeight + LineSpacing;
			return line;
		}
	}

	[CustomPropertyDrawer(typeof(PatchGraphConnection))]
	public sealed class PatchGraphConnectionDrawer : PropertyDrawer {
		private const float LineSpacing = 2f;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
			EditorGUI.BeginProperty(position, label, property);
			var sourceNodeId = property.FindPropertyRelative("_sourceNodeId");
			var sourcePortId = property.FindPropertyRelative("_sourcePortId");
			var targetNodeId = property.FindPropertyRelative("_targetNodeId");
			var targetPortId = property.FindPropertyRelative("_targetPortId");
			var graphNodes = property.serializedObject.FindProperty("_programGraph").FindPropertyRelative("_nodes");
			var y = position.y;
			property.isExpanded = EditorGUI.Foldout(Line(position, ref y), property.isExpanded, label, true);
			if (property.isExpanded) {
				var indent = EditorGUI.indentLevel;
				EditorGUI.indentLevel++;
				var sourceChanged = DrawNodePopup(Line(position, ref y), "Source Node", sourceNodeId, graphNodes);
				if (sourceChanged) sourcePortId.stringValue = PatchProgramGraph.ImagePortId;
				DrawSourcePortPopup(Line(position, ref y), sourcePortId);
				var targetChanged = DrawNodePopup(Line(position, ref y), "Target Node", targetNodeId, graphNodes);
				if (targetChanged) AssignFirstTargetPort(targetPortId, graphNodes, targetNodeId.stringValue);
				DrawTargetPortPopup(Line(position, ref y), targetPortId, graphNodes, targetNodeId.stringValue);
				EditorGUI.indentLevel = indent;
			}
			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
			=> property.isExpanded
				? EditorGUIUtility.singleLineHeight * 5f + LineSpacing * 4f
				: EditorGUIUtility.singleLineHeight;

		private static bool DrawNodePopup(Rect position, string label, SerializedProperty selectedNode, SerializedProperty graphNodes) {
			var values = new List<string>();
			var labels = new List<string>();
			for (var index = 0; index < graphNodes.arraySize; index++) {
				var node = graphNodes.GetArrayElementAtIndex(index);
				var id = node.FindPropertyRelative("_id").stringValue;
				if (string.IsNullOrWhiteSpace(id)) continue;
				values.Add(id);
				labels.Add(id + " (" + node.FindPropertyRelative("_typeId").stringValue + ")");
			}
			return DrawPopup(position, label, selectedNode, values, labels);
		}

		private static void DrawSourcePortPopup(Rect position, SerializedProperty sourcePortId) {
			DrawPopup(position, "Source Port", sourcePortId,
				new List<string> { PatchProgramGraph.ImagePortId }, new List<string> { "Image (image)" });
		}

		private static void DrawTargetPortPopup(Rect position, SerializedProperty targetPortId, SerializedProperty graphNodes, string targetNodeId) {
			var ports = GetTargetPorts(graphNodes, targetNodeId);
			if (ports.Count == 0) {
				EditorGUI.PropertyField(position, targetPortId, new GUIContent("Target Port"));
				return;
			}
			DrawPopup(position, "Target Port", targetPortId, ports.Select(port => port.Id.Value).ToList(), ports.Select(FormatPortLabel).ToList());
		}

		private static bool DrawPopup(Rect position, string label, SerializedProperty property, List<string> values, List<string> labels) {
			if (values.Count == 0) {
				EditorGUI.PropertyField(position, property, new GUIContent(label));
				return false;
			}
			var current = property.stringValue ?? string.Empty;
			if (!values.Contains(current, StringComparer.Ordinal)) {
				values.Insert(0, current);
				labels.Insert(0, string.IsNullOrWhiteSpace(current) ? "<None>" : "Missing: " + current);
			}
			var selected = values.IndexOf(current);
			var field = EditorGUI.PrefixLabel(position, new GUIContent(label));
			var next = EditorGUI.Popup(field, selected, labels.ToArray());
			if (next == selected) return false;
			property.stringValue = values[next];
			return true;
		}

		private static void AssignFirstTargetPort(SerializedProperty targetPortId, SerializedProperty graphNodes, string targetNodeId) {
			var ports = GetTargetPorts(graphNodes, targetNodeId);
			if (ports.Count > 0) targetPortId.stringValue = ports[0].Id.Value;
		}

		private static List<NodePortDefinition> GetTargetPorts(SerializedProperty graphNodes, string targetNodeId) {
			for (var index = 0; index < graphNodes.arraySize; index++) {
				var node = graphNodes.GetArrayElementAtIndex(index);
				if (!string.Equals(node.FindPropertyRelative("_id").stringValue, targetNodeId, StringComparison.Ordinal)) continue;
				var entry = PatchGraphNodeDrawer.ResolveNodeEntryForConnection(node.FindPropertyRelative("_typeId").stringValue);
				return entry == null
					? new List<NodePortDefinition>()
					: entry.Ports.Where(port => port != null && port.Direction == NodePortDirection.Input).ToList();
			}
			return new List<NodePortDefinition>();
		}

		private static string FormatPortLabel(NodePortDefinition port)
			=> string.IsNullOrWhiteSpace(port.DisplayName) ? port.Id.Value : port.DisplayName + " (" + port.Id.Value + ")";

		private static Rect Line(Rect position, ref float y) {
			var line = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
			y += EditorGUIUtility.singleLineHeight + LineSpacing;
			return line;
		}
	}

	[CustomPropertyDrawer(typeof(PatchParameter))]
	public sealed class PatchParameterDrawer : PropertyDrawer {
		private const float LineSpacing = 2f;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
			EditorGUI.BeginProperty(position, label, property);
			var id = property.FindPropertyRelative("_id");
			var displayName = property.FindPropertyRelative("_displayName");
			var nodeId = property.FindPropertyRelative("_nodeId");
			var parameterId = property.FindPropertyRelative("_parameterId");
			var beatModulation = property.FindPropertyRelative("m_BeatModulation");
			var graphNodes = property.serializedObject.FindProperty("_programGraph").FindPropertyRelative("_nodes");
			var y = position.y;
			property.isExpanded = EditorGUI.Foldout(Line(position, ref y), property.isExpanded, label, true);
			if (property.isExpanded) {
				var indent = EditorGUI.indentLevel;
				EditorGUI.indentLevel++;
				EditorGUI.PropertyField(Line(position, ref y), id, new GUIContent("ID"));
				EditorGUI.PropertyField(Line(position, ref y), displayName, new GUIContent("Display Name"));
				var nodeChanged = DrawNodePopup(Line(position, ref y), nodeId, graphNodes);
				var parameters = GetNodeParameters(graphNodes, nodeId.stringValue);
				if (nodeChanged && parameters.Count > 0) parameterId.stringValue = parameters[0].Id;
				DrawParameterPopup(Line(position, ref y), parameterId, parameters);
				EditorGUI.PropertyField(new Rect(position.x, y, position.width, EditorGUI.GetPropertyHeight(beatModulation, true)), beatModulation, new GUIContent("Beat Modulation"), true);
				EditorGUI.indentLevel = indent;
			}
			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
			=> property.isExpanded
				? EditorGUIUtility.singleLineHeight * 5f + LineSpacing * 5f + EditorGUI.GetPropertyHeight(property.FindPropertyRelative("m_BeatModulation"), true)
				: EditorGUIUtility.singleLineHeight;

		private static bool DrawNodePopup(Rect position, SerializedProperty nodeId, SerializedProperty nodes) {
			var values = new List<string>();
			var labels = new List<string>();
			for (var index = 0; index < nodes.arraySize; index++) {
				var node = nodes.GetArrayElementAtIndex(index);
				var id = node.FindPropertyRelative("_id").stringValue;
				if (string.IsNullOrWhiteSpace(id)) continue;
				values.Add(id);
				labels.Add(id + " (" + node.FindPropertyRelative("_typeId").stringValue + ")");
			}
			if (values.Count == 0) {
				EditorGUI.PropertyField(position, nodeId, new GUIContent("Node"));
				return false;
			}
			return DrawPopup(position, "Node", nodeId, values, labels);
		}

		private static void DrawParameterPopup(Rect position, SerializedProperty parameterId, List<ParameterOption> parameters) {
			if (parameters.Count == 0) {
				EditorGUI.PropertyField(position, parameterId, new GUIContent("Parameter"));
				return;
			}
			DrawPopup(position, "Parameter", parameterId, parameters.Select(parameter => parameter.Id).ToList(), parameters.Select(parameter => parameter.Label).ToList());
		}

		private static bool DrawPopup(Rect position, string label, SerializedProperty property, List<string> values, List<string> labels) {
			var current = property.stringValue ?? string.Empty;
			if (!values.Contains(current, StringComparer.Ordinal)) {
				values.Insert(0, current);
				labels.Insert(0, string.IsNullOrWhiteSpace(current) ? "<None>" : "Missing: " + current);
			}
			var selected = values.IndexOf(current);
			var field = EditorGUI.PrefixLabel(position, new GUIContent(label));
			var next = EditorGUI.Popup(field, selected, labels.ToArray());
			if (next == selected) return false;
			property.stringValue = values[next];
			return true;
		}

		private static List<ParameterOption> GetSceneParameters(Scene3DDefinition definition) {
			var options = new List<ParameterOption>();
			if (definition == null || definition.Prefab == null) return options;
			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (var component in definition.Prefab.GetComponentsInChildren<MonoBehaviour>(true)) {
				if (component == null) continue;
				if (component is ILiveSceneParameterProvider provider) {
					foreach (var liveParameter in provider.LiveParameters ?? Array.Empty<ILiveSceneParameter>()) {
						if (liveParameter == null) continue;
						var liveDefinition = liveParameter.Definition;
						if (string.IsNullOrWhiteSpace(liveDefinition.Id) || !seen.Add(liveDefinition.Id)) continue;
						options.Add(new ParameterOption(liveDefinition.Id,
							string.IsNullOrWhiteSpace(liveDefinition.DisplayName)
								? liveDefinition.Id
								: liveDefinition.DisplayName + " (" + liveDefinition.Id + ")"));
					}
				}
				var definitionProperty = component.GetType().GetProperty("Definition", BindingFlags.Instance | BindingFlags.Public);
				if (definitionProperty == null || definitionProperty.GetIndexParameters().Length != 0) continue;
				object parameter;
				try { parameter = definitionProperty.GetValue(component); }
				catch { continue; }
				var id = ReadStringProperty(parameter, "Id");
				if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
				var displayName = ReadStringProperty(parameter, "DisplayName");
				options.Add(new ParameterOption(id, string.IsNullOrWhiteSpace(displayName) ? id : displayName + " (" + id + ")"));
			}
			return options.OrderBy(option => option.Label, StringComparer.Ordinal).ToList();
		}

		private static List<ParameterOption> GetNodeParameters(SerializedProperty nodes, string nodeId) {
			for (var index = 0; index < nodes.arraySize; index++) {
				var node = nodes.GetArrayElementAtIndex(index);
				if (!string.Equals(node.FindPropertyRelative("_id").stringValue, nodeId, StringComparison.Ordinal)) continue;
				var typeId = node.FindPropertyRelative("_typeId").stringValue;
				if (PatchGraphNodeDrawer.IsSceneNode(typeId))
					return GetSceneParameters(node.FindPropertyRelative("m_SceneDefinition")?.objectReferenceValue as Scene3DDefinition);
				return GetGraphParameters(node.FindPropertyRelative("_parameters"));
			}
			return new List<ParameterOption>();
		}

		private static List<ParameterOption> GetGraphParameters(SerializedProperty parameters) {
			var options = new List<ParameterOption>();
			if (parameters == null) return options;
			for (var parameterIndex = 0; parameterIndex < parameters.arraySize; parameterIndex++) {
				var parameter = parameters.GetArrayElementAtIndex(parameterIndex);
				var type = (ParameterType)parameter.FindPropertyRelative("_type").enumValueIndex;
				if (!PatchGraphParameter.IsLiveControllable(type)) continue;
				var id = parameter.FindPropertyRelative("_id").stringValue;
				if (!string.IsNullOrWhiteSpace(id)) options.Add(new ParameterOption(id, id + " (" + type + ")"));
			}
			return options;
		}

		private static string ReadStringProperty(object value, string propertyName) {
			if (value == null) return string.Empty;
			var property = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
			if (property == null || property.PropertyType != typeof(string)) return string.Empty;
			try { return property.GetValue(value) as string ?? string.Empty; }
			catch { return string.Empty; }
		}

		private static Rect Line(Rect position, ref float y) {
			var line = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
			y += EditorGUIUtility.singleLineHeight + LineSpacing;
			return line;
		}

		private sealed class ParameterOption {
			public string Id { get; }
			public string Label { get; }
			public ParameterOption(string id, string label) { Id = id; Label = label; }
		}
	}

	[CustomPropertyDrawer(typeof(PatchGraphParameter))]
	public sealed class PatchGraphParameterDrawer : PropertyDrawer {
		private const float LineSpacing = 2f;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
			EditorGUI.BeginProperty(position, label, property);
			var nodeId = property.FindPropertyRelative("m_NodeId");
			var id = property.FindPropertyRelative("_id");
			var type = property.FindPropertyRelative("_type");
			var y = position.y;
			if (IsHotCueValue(property))
				EditorGUI.PropertyField(Line(position, ref y), nodeId, new GUIContent("Node ID", "Optional when the parameter ID exists on exactly one Program Graph node."));
			EditorGUI.PropertyField(Line(position, ref y), id, new GUIContent("ID"));
			EditorGUI.PropertyField(Line(position, ref y), type, new GUIContent("Type"));
			DrawValue(Line(position, ref y), ValueProperty(property, type), new GUIContent("Value"));
			EditorGUI.EndProperty();
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label) {
			var lineCount = IsHotCueValue(property) ? 4f : 3f;
			return EditorGUIUtility.singleLineHeight * lineCount + LineSpacing * (lineCount - 1f);
		}

		private static bool IsHotCueValue(SerializedProperty property)
			=> property.propertyPath.IndexOf("m_HotCue", StringComparison.Ordinal) >= 0;

		private static Rect Line(Rect position, ref float y) {
			var line = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
			y += EditorGUIUtility.singleLineHeight + LineSpacing;
			return line;
		}

		private static void DrawValue(Rect position, SerializedProperty value, GUIContent label) {
			if (value == null) return;
			if (value.propertyType != SerializedPropertyType.Vector4) {
				EditorGUI.PropertyField(position, value, label);
				return;
			}

			var current = value.vector4Value;
			var next = EditorGUI.Vector4Field(position, label.text, current);
			if (next != current) value.vector4Value = next;
		}

		private static SerializedProperty ValueProperty(SerializedProperty property, SerializedProperty type) {
			switch ((ParameterType)type.enumValueIndex) {
				case ParameterType.Float: return property.FindPropertyRelative("_floatValue");
				case ParameterType.Int: return property.FindPropertyRelative("_intValue");
				case ParameterType.Bool: return property.FindPropertyRelative("_boolValue");
				case ParameterType.Vector2: return property.FindPropertyRelative("_vector2Value");
				case ParameterType.Vector3: return property.FindPropertyRelative("_vector3Value");
				case ParameterType.Vector4: return property.FindPropertyRelative("_vector4Value");
				case ParameterType.Color: return property.FindPropertyRelative("_colorValue");
				case ParameterType.String:
				case ParameterType.Enum:
				case ParameterType.MediaAssetReference: return property.FindPropertyRelative("_textValue");
				default: return property.FindPropertyRelative("_floatValue");
			}
		}
	}
}
