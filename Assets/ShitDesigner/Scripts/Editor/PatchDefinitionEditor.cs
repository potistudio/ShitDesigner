using ShitDesigner.Core;
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
		private SerializedProperty _nodeGroups;
		private SerializedProperty _parameters;
		private SerializedProperty _flash;

		private void OnEnable() {
			_id = serializedObject.FindProperty("_id");
			_displayName = serializedObject.FindProperty("_displayName");
			_programGraph = serializedObject.FindProperty("_programGraph");
			_nodeGroups = serializedObject.FindProperty("_nodeGroups");
			_parameters = serializedObject.FindProperty("_parameters");
			_flash = serializedObject.FindProperty("_flash");
		}

		public override void OnInspectorGUI() {
			serializedObject.Update();

			EditorGUILayout.LabelField("Patch", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(_id, new GUIContent("ID"));
			EditorGUILayout.PropertyField(_displayName, new GUIContent("Display Name"));

			EditorGUILayout.Space(6f);
			EditorGUILayout.LabelField("Program Graph", EditorStyles.boldLabel);
			if (_programGraph != null) {
				var sourceNodeId = _programGraph.FindPropertyRelative("_sourceNodeId");
				var outputNodeId = _programGraph.FindPropertyRelative("_outputNodeId");
				var nodes = _programGraph.FindPropertyRelative("_nodes");
				var connections = _programGraph.FindPropertyRelative("_connections");
				EditorGUILayout.PropertyField(sourceNodeId, new GUIContent("Input Node ID"));
				EditorGUILayout.PropertyField(outputNodeId, new GUIContent("Output Node ID"));
				EditorGUILayout.PropertyField(nodes, new GUIContent("Nodes"), true);
				EditorGUILayout.PropertyField(connections, new GUIContent("Connections"), true);
			}
			else {
				EditorGUILayout.HelpBox("Program graph data is missing.", MessageType.Error);
			}

			EditorGUILayout.Space(6f);
			EditorGUILayout.LabelField("Scene Nodes", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(_nodeGroups, new GUIContent("Node Groups"), true);

			EditorGUILayout.Space(6f);
			EditorGUILayout.LabelField("Published Parameters", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(_parameters, new GUIContent("Parameters"), true);

			EditorGUILayout.Space(6f);
			EditorGUILayout.LabelField("Flash", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(_flash, new GUIContent("Flash"), true);

			serializedObject.ApplyModifiedProperties();
			DrawValidationMessage();
		}

		private void DrawValidationMessage() {
			var definition = target as PatchDefinition;
			if (definition == null) return;
			var validation = definition.Validate();
			if (validation.IsFailure) EditorGUILayout.HelpBox(validation.Error.Message, MessageType.Error);
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
				case ParameterType.MediaAssetReference:
					return property.FindPropertyRelative("_textValue");
				default: return property.FindPropertyRelative("_floatValue");
			}
		}
	}
}
