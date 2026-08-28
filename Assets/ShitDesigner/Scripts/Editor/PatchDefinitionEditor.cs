using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
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
		private SerializedProperty _nodes;
		private SerializedProperty _parameters;
		private SerializedProperty _flash;

		private void OnEnable() {
			_id = serializedObject.FindProperty("_id");
			_displayName = serializedObject.FindProperty("_displayName");
			_programGraph = serializedObject.FindProperty("_programGraph");
			_nodes = serializedObject.FindProperty("_nodes");
			_parameters = serializedObject.FindProperty("_parameters");
			_flash = serializedObject.FindProperty("_flash");
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
			EditorGUILayout.PropertyField(_nodes, new GUIContent("Scene Nodes"), true);

			EditorGUILayout.Space(6f);
			EditorGUILayout.PropertyField(_parameters, new GUIContent("Published Parameters"), true);

			EditorGUILayout.Space(6f);
			EditorGUILayout.PropertyField(_flash, new GUIContent("Flash"));

			serializedObject.ApplyModifiedProperties();
			DrawValidationMessage();
		}

		private void DrawProgramGraph() {
			EditorGUILayout.LabelField("Program Graph", EditorStyles.boldLabel);
			if (_programGraph == null) {
				EditorGUILayout.HelpBox("Program graph data is missing.", MessageType.Error);
				return;
			}

			var sourceNodeId = _programGraph.FindPropertyRelative("_sourceNodeId");
			var outputNodeId = _programGraph.FindPropertyRelative("_outputNodeId");
			var nodes = _programGraph.FindPropertyRelative("_nodes");
			var connections = _programGraph.FindPropertyRelative("_connections");
			if (sourceNodeId != null && !string.Equals(sourceNodeId.stringValue, PatchProgramGraph.SceneInputNodeId, StringComparison.Ordinal))
				sourceNodeId.stringValue = PatchProgramGraph.SceneInputNodeId;

			EditorGUI.BeginDisabledGroup(true);
			EditorGUILayout.TextField(new GUIContent("Scene Input", "The rendered Scene3DDefinition is exposed to the shader graph with this fixed ID."), PatchProgramGraph.SceneInputNodeId);
			EditorGUI.EndDisabledGroup();

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

	[CustomPropertyDrawer(typeof(PatchGraphNode))]
	public sealed class PatchGraphNodeDrawer : PropertyDrawer {
		private const string ManifestAssetPath = "Assets/ShitDesigner/Scripts/Nodes/ShaderNodeManifest.asset";
		private const float LineSpacing = 2f;
		private static ShaderNodeManifestAsset _manifest;
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
				DrawTypeIdPopup(Line(position, ref y), typeId);
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
			var height = EditorGUIUtility.singleLineHeight * 3f + LineSpacing * 3f
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
			if (GUI.Button(button, "Add")) AddParameter(parameters, typeId, addable[selected].Id);
		}

		private static List<ShaderNodeManifestAssetParameter> GetAddableParameters(string typeId, SerializedProperty parameters) {
			var entry = ResolveManifest()?.Find(typeId);
			if (entry == null) return new List<ShaderNodeManifestAssetParameter>();
			var existing = new HashSet<string>(StringComparer.Ordinal);
			for (var index = 0; index < parameters.arraySize; index++)
				existing.Add(parameters.GetArrayElementAtIndex(index).FindPropertyRelative("_id").stringValue);
			return entry.Parameters
				.Where(parameter => parameter != null && !parameter.IsHidden && !existing.Contains(parameter.Id))
				.OrderBy(parameter => parameter.DisplayOrder)
				.ThenBy(parameter => parameter.DisplayName, StringComparer.Ordinal)
				.ToList();
		}

		private static void AddParameter(SerializedProperty parameters, string typeId, string parameterId) {
			var runtimeDefinition = ResolveManifest()?.BuildRuntimeManifest().Find(typeId)?.Parameters
				.FirstOrDefault(parameter => string.Equals(parameter.Id.Value, parameterId, StringComparison.Ordinal));
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

		private static void DrawTypeIdPopup(Rect position, SerializedProperty typeId) {
			var manifest = ResolveManifest();
			if (manifest == null) {
				EditorGUI.PropertyField(position, typeId, new GUIContent("Type ID"));
				return;
			}

			var entries = manifest.Entries
				.Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.TypeId))
				.OrderBy(entry => entry.Category, StringComparer.Ordinal)
				.ThenBy(entry => entry.DisplayName, StringComparer.Ordinal)
				.ThenBy(entry => entry.TypeId, StringComparer.Ordinal)
				.ToList();
			if (entries.Count == 0) {
				EditorGUI.PropertyField(position, typeId, new GUIContent("Type ID"));
				return;
			}

			var values = entries.Select(entry => entry.TypeId).ToList();
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
			if (next != selected) typeId.stringValue = values[next];
		}

		private static ShaderNodeManifestAsset ResolveManifest() {
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

		private static string FormatLabel(ShaderNodeManifestAssetEntry entry)
			=> string.IsNullOrWhiteSpace(entry.Category)
				? entry.DisplayName + " (" + entry.TypeId + ")"
				: entry.Category + "/" + entry.DisplayName + " (" + entry.TypeId + ")";

		private static string FormatParameterLabel(ShaderNodeManifestAssetParameter parameter)
			=> string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Id : parameter.DisplayName + " (" + parameter.Id + ")";

		private static Rect Line(Rect position, ref float y) {
			var line = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
			y += EditorGUIUtility.singleLineHeight + LineSpacing;
			return line;
		}
	}

	[CustomPropertyDrawer(typeof(PatchGraphParameter))]
	public sealed class PatchGraphParameterDrawer : PropertyDrawer {
		private const float LineSpacing = 2f;

		public override void OnGUI(Rect position, SerializedProperty property, GUIContent label) {
			var id = property.FindPropertyRelative("_id");
			var type = property.FindPropertyRelative("_type");
			var y = position.y;
			EditorGUI.PropertyField(Line(position, ref y), id, new GUIContent("ID"));
			EditorGUI.PropertyField(Line(position, ref y), type, new GUIContent("Type"));
			EditorGUI.PropertyField(Line(position, ref y), ValueProperty(property, type), new GUIContent("Value"));
		}

		public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
			=> EditorGUIUtility.singleLineHeight * 3f + LineSpacing * 2f;

		private static Rect Line(Rect position, ref float y) {
			var line = new Rect(position.x, y, position.width, EditorGUIUtility.singleLineHeight);
			y += EditorGUIUtility.singleLineHeight + LineSpacing;
			return line;
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
