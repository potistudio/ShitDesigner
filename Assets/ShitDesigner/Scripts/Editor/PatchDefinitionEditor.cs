using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using ShitDesigner.Core;
using ShitDesigner.Nodes;
using ShitDesigner.Scene;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	[CustomEditor(typeof(PatchDefinition))]
	[CanEditMultipleObjects]
	public sealed class PatchDefinitionEditor : UnityEditor.Editor {
		private const string ManifestAssetPath = "Assets/ShitDesigner/Scripts/Nodes/ShaderNodeManifest.asset";

		private SerializedProperty _id;
		private SerializedProperty _displayName;
		private SerializedProperty _programGraph;
		private SerializedProperty _nodeGroups;
		private SerializedProperty _parameters;
		private SerializedProperty _flash;
		private ShaderNodeManifestAsset _manifest;
		private ShaderNodeManifest _runtimeManifest;
		private string _manifestFingerprint = string.Empty;
		private string _manifestError = string.Empty;
		private Scene3DDefinition[] _sceneDefinitions = Array.Empty<Scene3DDefinition>();
		private readonly Dictionary<string, int> _parameterAddSelections = new Dictionary<string, int>(StringComparer.Ordinal);
		private int _nodeAddSelection = -1;

		private void OnEnable() {
			_id = serializedObject.FindProperty("_id");
			_displayName = serializedObject.FindProperty("_displayName");
			_programGraph = serializedObject.FindProperty("_programGraph");
			_nodeGroups = serializedObject.FindProperty("_nodeGroups");
			_parameters = serializedObject.FindProperty("_parameters");
			_flash = serializedObject.FindProperty("_flash");
			RefreshManifest(true);
			RefreshSceneDefinitions();
		}

		public override void OnInspectorGUI() {
			serializedObject.Update();
			RefreshManifest(false);
			if (Event.current.type == EventType.Layout) RefreshSceneDefinitions();

			if (string.IsNullOrWhiteSpace(_id.stringValue) && !string.IsNullOrWhiteSpace(_displayName.stringValue))
				_id.stringValue = Slug(_displayName.stringValue);

			EditorGUILayout.LabelField("Patch", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(_id, new GUIContent("ID", "Stable patch ID. It is generated from Display Name when empty."));
			EditorGUILayout.PropertyField(_displayName, new GUIContent("Display Name"));

			EditorGUILayout.Space(6f);
			DrawProgramGraph();

			EditorGUILayout.Space(6f);
			DrawSceneNodeGroups();

			EditorGUILayout.Space(6f);
			DrawPublishedParameters();

			EditorGUILayout.Space(6f);
			EditorGUILayout.LabelField("Flash", EditorStyles.boldLabel);
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

			EnsureGraphNodeIds(nodes);
			var nodeIds = GetGraphNodeIds(nodes, out var nodeLabels);
			if (nodeIds.Count > 0) DrawPopup("Output Node", outputNodeId, nodeIds, nodeLabels);
			else EditorGUILayout.HelpBox("Add a graph node to choose the program output.", MessageType.Info);
			EditorGUILayout.LabelField("Output Node selects a node already listed in Nodes.", EditorStyles.miniLabel);

			DrawGraphNodes(nodes);
			DrawGraphConnections(connections, nodes);
			if (!string.IsNullOrWhiteSpace(_manifestError)) EditorGUILayout.HelpBox(_manifestError, MessageType.Warning);
		}

		private void DrawGraphNodes(SerializedProperty nodes) {
			EditorGUILayout.LabelField("Nodes", EditorStyles.miniBoldLabel);
			for (var index = 0; index < nodes.arraySize; index++) {
				var node = nodes.GetArrayElementAtIndex(index);
				var nodeId = node.FindPropertyRelative("_id");
				var typeId = node.FindPropertyRelative("_typeId");
				var entry = FindRuntimeEntry(typeId.stringValue);

				EditorGUILayout.BeginVertical("box");
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField("Node " + (index + 1), EditorStyles.boldLabel);
				if (GUILayout.Button("Remove", GUILayout.Width(70f))) {
					RemoveGraphNode(nodes, index, nodeId.stringValue);
					EditorGUILayout.EndHorizontal();
					EditorGUILayout.EndVertical();
					break;
				}
				EditorGUILayout.EndHorizontal();

				EditorGUI.BeginDisabledGroup(true);
				EditorGUILayout.TextField(new GUIContent("Generated ID", "Graph node IDs are generated and used by connection selectors."), nodeId.stringValue);
				EditorGUI.EndDisabledGroup();

				var typeChanged = DrawNodeTypeSelector(typeId);
				if (typeChanged) {
					entry = FindRuntimeEntry(typeId.stringValue);
					SyncGraphNodeParameters(node, entry);
				}
				if (entry != null) DrawGraphParameters(node, entry);
				else {
					EditorGUILayout.PropertyField(node.FindPropertyRelative("_parameters"), new GUIContent("Parameters"), true);
					EditorGUILayout.HelpBox("The selected node type is not available in the generated Shader Manifest.", MessageType.Warning);
				}
				EditorGUILayout.EndVertical();
			}

			var addable = GetManifestEntries().Where(entry => entry.UserAddable).ToList();
			if (addable.Count == 0) {
				if (GUILayout.Button("Add Node")) AddGraphNode(nodes, null);
				return;
			}

			if (_nodeAddSelection < 0) {
				_nodeAddSelection = addable.FindIndex(entry => string.Equals(entry.TypeId, "shitdesigner.shader.generator.solid-color", StringComparison.Ordinal));
				if (_nodeAddSelection < 0) _nodeAddSelection = 0;
			}
			_nodeAddSelection = Mathf.Clamp(_nodeAddSelection, 0, addable.Count - 1);
			EditorGUILayout.BeginHorizontal();
			_nodeAddSelection = EditorGUILayout.Popup(new GUIContent("New Node Type", "Choose the node type before adding it to the graph."), _nodeAddSelection, addable.Select(FormatNodeLabel).ToArray());
			if (GUILayout.Button("Add Node", GUILayout.Width(85f))) AddGraphNode(nodes, addable[_nodeAddSelection]);
			EditorGUILayout.EndHorizontal();
		}

		private bool DrawNodeTypeSelector(SerializedProperty typeId) {
			var entries = GetManifestEntries().ToList();
			if (entries.Count == 0) {
				EditorGUILayout.PropertyField(typeId, new GUIContent("Type ID"));
				return false;
			}

			var values = entries.Select(entry => entry.TypeId).ToList();
			var labels = entries.Select(FormatNodeLabel).ToList();
			var current = typeId.stringValue ?? string.Empty;
			if (string.IsNullOrWhiteSpace(current)) {
				current = values[0];
				typeId.stringValue = current;
			}
			if (!values.Contains(current, StringComparer.Ordinal)) {
				values.Insert(0, current);
				labels.Insert(0, "Missing: " + current);
			}

			var selected = values.IndexOf(current);
			var next = EditorGUILayout.Popup(new GUIContent("Node Type", "Select a generated shader node; the Type ID is stored automatically."), selected, labels.ToArray());
			if (next == selected) return false;
			typeId.stringValue = values[next];
			return true;
		}

		private void DrawGraphParameters(SerializedProperty node, ShaderNodeManifestEntry entry) {
			var parameters = node.FindPropertyRelative("_parameters");
			EditorGUILayout.LabelField("Parameters", EditorStyles.miniBoldLabel);
			for (var index = 0; index < parameters.arraySize; index++) {
				var parameter = parameters.GetArrayElementAtIndex(index);
				var id = parameter.FindPropertyRelative("_id");
				var definition = entry.Parameters.FirstOrDefault(candidate => candidate != null && string.Equals(candidate.Id.Value, id.stringValue, StringComparison.Ordinal));

				EditorGUILayout.BeginVertical("box");
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField(definition == null ? "Parameter" : definition.DisplayName, EditorStyles.boldLabel);
				if (GUILayout.Button("Remove", GUILayout.Width(70f))) {
					parameters.DeleteArrayElementAtIndex(index);
					EditorGUILayout.EndHorizontal();
					EditorGUILayout.EndVertical();
					break;
				}
				EditorGUILayout.EndHorizontal();

				DrawParameterSelector(parameter, entry);
				definition = entry.Parameters.FirstOrDefault(candidate => candidate != null && string.Equals(candidate.Id.Value, id.stringValue, StringComparison.Ordinal));
				if (definition != null) {
					SetParameterType(parameter, definition.Type);
					EditorGUI.BeginDisabledGroup(definition.Definition.IsReadOnly);
					DrawParameterValue(parameter, definition);
					EditorGUI.EndDisabledGroup();
					EditorGUI.BeginDisabledGroup(true);
					EditorGUILayout.EnumPopup(new GUIContent("Type"), definition.Type);
					EditorGUI.EndDisabledGroup();
				}
				else {
					EditorGUILayout.PropertyField(parameter.FindPropertyRelative("_id"), new GUIContent("Parameter ID"));
					EditorGUILayout.PropertyField(parameter.FindPropertyRelative("_type"), new GUIContent("Type"));
					EditorGUILayout.HelpBox("This parameter is not provided by the selected node type.", MessageType.Warning);
				}
				EditorGUILayout.EndVertical();
			}

			var addable = entry.Parameters.Where(parameter => parameter != null && !parameter.Definition.IsHidden
				&& Enumerable.Range(0, parameters.arraySize).All(index => !string.Equals(parameters.GetArrayElementAtIndex(index).FindPropertyRelative("_id").stringValue, parameter.Id.Value, StringComparison.Ordinal))).ToList();
			if (addable.Count == 0) return;
			var key = node.propertyPath;
			if (!_parameterAddSelections.TryGetValue(key, out var selected)) selected = 0;
			selected = Mathf.Clamp(selected, 0, addable.Count - 1);
			EditorGUILayout.BeginHorizontal();
			selected = EditorGUILayout.Popup(new GUIContent("Add Parameter"), selected, addable.Select(FormatParameterLabel).ToArray());
			_parameterAddSelections[key] = selected;
			if (GUILayout.Button("Add", GUILayout.Width(55f))) AddGraphParameter(parameters, addable[selected]);
			EditorGUILayout.EndHorizontal();
			EditorGUILayout.LabelField("Parameters not added here use the manifest defaults.", EditorStyles.miniLabel);
		}

		private void DrawParameterSelector(SerializedProperty parameter, ShaderNodeManifestEntry entry) {
			var id = parameter.FindPropertyRelative("_id");
			var candidates = entry.Parameters.Where(candidate => candidate != null).ToList();
			if (candidates.Count == 0) return;
			var values = candidates.Select(candidate => candidate.Id.Value).ToList();
			var labels = candidates.Select(FormatParameterLabel).ToList();
			var current = id.stringValue ?? string.Empty;
			if (string.IsNullOrWhiteSpace(current)) {
				current = values[0];
				id.stringValue = current;
			}
			if (!values.Contains(current, StringComparer.Ordinal)) {
				values.Insert(0, current);
				labels.Insert(0, "Missing: " + current);
			}

			var selected = values.IndexOf(current);
			var next = EditorGUILayout.Popup(new GUIContent("Parameter", "Select a manifest parameter; its type and default value are derived automatically."), selected, labels.ToArray());
			if (next == selected) return;
			id.stringValue = values[next];
			var definition = candidates.FirstOrDefault(candidate => string.Equals(candidate.Id.Value, id.stringValue, StringComparison.Ordinal));
			if (definition != null) SetParameterValue(parameter, definition.DefaultValue);
		}

		private void DrawParameterValue(SerializedProperty parameter, ShaderNodeManifestParameter definition) {
			var type = definition.Type;
			var valueLabel = string.IsNullOrWhiteSpace(definition.DisplayName) ? "Value" : definition.DisplayName;
			var value = ValueProperty(parameter, type);
			if (value == null) return;
			switch (type) {
				case ParameterType.Float:
					if (definition.Minimum.HasValue && definition.Maximum.HasValue) value.floatValue = EditorGUILayout.Slider(valueLabel, value.floatValue, definition.Minimum.Value.AsFloat(), definition.Maximum.Value.AsFloat());
					else EditorGUILayout.PropertyField(value, new GUIContent(valueLabel));
					break;
				case ParameterType.Int:
					if (definition.Minimum.HasValue && definition.Maximum.HasValue) value.intValue = EditorGUILayout.IntSlider(valueLabel, value.intValue, definition.Minimum.Value.AsInt(), definition.Maximum.Value.AsInt());
					else EditorGUILayout.PropertyField(value, new GUIContent(valueLabel));
					break;
				case ParameterType.Bool:
					value.boolValue = EditorGUILayout.Toggle(valueLabel, value.boolValue);
					break;
				case ParameterType.Enum:
					DrawEnumValue(value, valueLabel, definition.Definition.EnumOptions);
					break;
				default:
					EditorGUILayout.PropertyField(value, new GUIContent(valueLabel));
					break;
			}
		}

		private static void DrawEnumValue(SerializedProperty value, string label, IReadOnlyList<string> options) {
			var values = (options ?? Array.Empty<string>()).Where(option => !string.IsNullOrWhiteSpace(option)).ToList();
			if (values.Count == 0) {
				EditorGUILayout.PropertyField(value, new GUIContent(label));
				return;
			}
			var current = value.stringValue ?? string.Empty;
			if (!values.Contains(current, StringComparer.Ordinal)) values.Insert(0, current);
			var labels = values.Select(option => string.IsNullOrWhiteSpace(option) ? "<None>" : option).ToArray();
			var selected = values.IndexOf(current);
			var next = EditorGUILayout.Popup(label, selected, labels);
			if (next != selected) value.stringValue = values[next];
		}

		private void DrawGraphConnections(SerializedProperty connections, SerializedProperty nodes) {
			EditorGUILayout.LabelField("Connections", EditorStyles.miniBoldLabel);
			var sourceIds = GetSourceNodeIds(nodes, out var sourceLabels);
			for (var index = 0; index < connections.arraySize; index++) {
				var connection = connections.GetArrayElementAtIndex(index);
				var sourceNodeId = connection.FindPropertyRelative("_sourceNodeId");
				var sourcePortId = connection.FindPropertyRelative("_sourcePortId");
				var targetNodeId = connection.FindPropertyRelative("_targetNodeId");
				var targetPortId = connection.FindPropertyRelative("_targetPortId");
				if (sourcePortId != null) sourcePortId.stringValue = PatchProgramGraph.ImagePortId;

				EditorGUILayout.BeginVertical("box");
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField("Connection " + (index + 1), EditorStyles.boldLabel);
				if (GUILayout.Button("Remove", GUILayout.Width(70f))) {
					connections.DeleteArrayElementAtIndex(index);
					EditorGUILayout.EndHorizontal();
					EditorGUILayout.EndVertical();
					break;
				}
				EditorGUILayout.EndHorizontal();

				if (sourceIds.Count > 0) DrawPopup("Source Node", sourceNodeId, sourceIds, sourceLabels);
				else EditorGUILayout.HelpBox("Add graph nodes before creating a connection.", MessageType.Info);
				EditorGUI.BeginDisabledGroup(true);
				EditorGUILayout.TextField(new GUIContent("Source Port", "Shader graph nodes expose the image output port."), PatchProgramGraph.ImagePortId);
				EditorGUI.EndDisabledGroup();

				var targetIds = GetGraphNodeIds(nodes, out var targetLabels);
				if (targetIds.Count > 0) {
					var targetChanged = DrawPopup("Target Node", targetNodeId, targetIds, targetLabels);
					var targetEntry = FindRuntimeEntryForGraphNode(nodes, targetNodeId.stringValue);
					var ports = GetImageInputPorts(targetEntry, out var portLabels);
					if (targetChanged && ports.Count > 0) targetPortId.stringValue = ports[0];
					if (ports.Count > 0) DrawPopup("Target Port", targetPortId, ports, portLabels);
					else EditorGUILayout.HelpBox("The target node has no image input ports.", MessageType.Warning);
				}
				else EditorGUILayout.HelpBox("Add a graph node before selecting a target.", MessageType.Info);
				EditorGUILayout.EndVertical();
			}

			if (GUILayout.Button("Add Connection")) AddGraphConnection(connections, nodes);
		}

		private void DrawSceneNodeGroups() {
			EditorGUILayout.LabelField("Scene Node Groups", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Groups organize Scene3DDefinition assets. Shader graph nodes are configured under Program Graph.", MessageType.None);
			for (var groupIndex = 0; groupIndex < _nodeGroups.arraySize; groupIndex++) {
				var group = _nodeGroups.GetArrayElementAtIndex(groupIndex);
				var id = group.FindPropertyRelative("_id");
				var displayName = group.FindPropertyRelative("_displayName");
				if (string.IsNullOrWhiteSpace(id.stringValue)) id.stringValue = CreateGroupId(displayName.stringValue, groupIndex);
				var nodes = group.FindPropertyRelative("_nodes");

				EditorGUILayout.BeginVertical("box");
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField("Group " + (groupIndex + 1), EditorStyles.boldLabel);
				if (GUILayout.Button("Remove", GUILayout.Width(70f))) {
					_nodeGroups.DeleteArrayElementAtIndex(groupIndex);
					EditorGUILayout.EndHorizontal();
					EditorGUILayout.EndVertical();
					break;
				}
				EditorGUILayout.EndHorizontal();
				EditorGUILayout.PropertyField(displayName, new GUIContent("Display Name"));
				EditorGUI.BeginDisabledGroup(true);
				EditorGUILayout.TextField(new GUIContent("Generated ID", "Group IDs are generated because they are only used for stable validation."), id.stringValue);
				EditorGUI.EndDisabledGroup();

				for (var nodeIndex = 0; nodeIndex < nodes.arraySize; nodeIndex++) {
					var node = nodes.GetArrayElementAtIndex(nodeIndex);
					EditorGUILayout.BeginHorizontal();
					DrawSceneDefinitionSelector(node, new GUIContent("Scene Node " + (nodeIndex + 1)));
					if (GUILayout.Button("Remove", GUILayout.Width(70f))) {
						node.objectReferenceValue = null;
						nodes.DeleteArrayElementAtIndex(nodeIndex);
						EditorGUILayout.EndHorizontal();
						break;
					}
					EditorGUILayout.EndHorizontal();
				}
				if (GUILayout.Button("Add Scene Node")) nodes.arraySize++;
				EditorGUILayout.EndVertical();
			}
			if (GUILayout.Button("Add Scene Node Group")) AddSceneNodeGroup();
		}

		private void DrawPublishedParameters() {
			EditorGUILayout.LabelField("Published Parameters", EditorStyles.boldLabel);
			var sceneNodes = GetConfiguredSceneNodes();
			for (var index = 0; index < _parameters.arraySize; index++) {
				var parameter = _parameters.GetArrayElementAtIndex(index);
				var id = parameter.FindPropertyRelative("_id");
				var displayName = parameter.FindPropertyRelative("_displayName");
				var nodeId = parameter.FindPropertyRelative("_nodeId");
				var parameterId = parameter.FindPropertyRelative("_parameterId");
				var nodeOptions = sceneNodes.ToList();
				var nodeValues = nodeOptions.Select(node => node.Id).ToList();
				var nodeLabels = nodeOptions.Select(node => node.Label).ToList();
				var selectedNode = sceneNodes.FirstOrDefault(node => string.Equals(node.Id, nodeId.stringValue, StringComparison.Ordinal));

				EditorGUILayout.BeginVertical("box");
				EditorGUILayout.BeginHorizontal();
				EditorGUILayout.LabelField("Parameter " + (index + 1), EditorStyles.boldLabel);
				if (GUILayout.Button("Remove", GUILayout.Width(70f))) {
					_parameters.DeleteArrayElementAtIndex(index);
					EditorGUILayout.EndHorizontal();
					EditorGUILayout.EndVertical();
					break;
				}
				EditorGUILayout.EndHorizontal();

				if (nodeValues.Count > 0) {
					var nodeChanged = DrawPopup("Scene Node", nodeId, nodeValues, nodeLabels);
					selectedNode = sceneNodes.FirstOrDefault(node => string.Equals(node.Id, nodeId.stringValue, StringComparison.Ordinal));
					var parameterOptions = GetSceneParameterOptions(selectedNode == null ? null : selectedNode.Definition);
					var parameterValues = parameterOptions.Select(option => option.Id).ToList();
					var parameterLabels = parameterOptions.Select(option => option.Label).ToList();
					if (nodeChanged && parameterValues.Count > 0) parameterId.stringValue = parameterValues[0];
					if (parameterValues.Count > 0) DrawPopup("Scene Parameter", parameterId, parameterValues, parameterLabels);
					else {
						EditorGUILayout.PropertyField(parameterId, new GUIContent("Parameter ID"));
						EditorGUILayout.HelpBox("The selected prefab does not expose discoverable live parameters. The ID field is retained as a fallback.", MessageType.Info);
					}
					if (string.IsNullOrWhiteSpace(id.stringValue)) id.stringValue = CreatePublishedParameterId(parameterId.stringValue, index);
					if (string.IsNullOrWhiteSpace(displayName.stringValue)) displayName.stringValue = GetSelectedLabel(parameterOptions, parameterId.stringValue, parameterId.stringValue);
				}
				else {
					EditorGUILayout.HelpBox("Add a Scene3DDefinition to a Scene Node Group before publishing a parameter.", MessageType.Info);
					EditorGUILayout.PropertyField(nodeId, new GUIContent("Scene Node ID"));
					EditorGUILayout.PropertyField(parameterId, new GUIContent("Parameter ID"));
				}
				EditorGUI.BeginDisabledGroup(true);
				EditorGUILayout.TextField(new GUIContent("Generated ID", "The public parameter ID is generated when empty."), id.stringValue);
				EditorGUI.EndDisabledGroup();
				EditorGUILayout.PropertyField(displayName, new GUIContent("Display Name"));
				EditorGUILayout.EndVertical();
			}

			if (sceneNodes.Count > 0 && GUILayout.Button("Add Published Parameter")) AddPublishedParameter(sceneNodes);
		else if (sceneNodes.Count == 0) EditorGUILayout.HelpBox("Published parameter choices become available after a Scene3DDefinition is assigned to a group.", MessageType.Info);
		}

		private void DrawSceneDefinitionSelector(SerializedProperty property, GUIContent label) {
			if (_sceneDefinitions.Length == 0) {
				EditorGUILayout.PropertyField(property, label);
				return;
			}
			var values = _sceneDefinitions.ToList();
			var labels = values.Select(FormatSceneDefinitionLabel).ToList();
			var current = property.objectReferenceValue as Scene3DDefinition;
			if (current != null && !values.Contains(current)) {
				values.Insert(0, current);
				labels.Insert(0, "Missing: " + FormatSceneDefinitionLabel(current));
			}
			var selected = current == null ? -1 : values.IndexOf(current);
			var next = EditorGUILayout.Popup(label, selected + 1, new[] { "<None>" }.Concat(labels).ToArray()) - 1;
			if (next >= 0 && next < values.Count) property.objectReferenceValue = values[next];
			else if (next < 0) property.objectReferenceValue = null;
		}

		private void DrawValidationMessage() {
			var definition = target as PatchDefinition;
			if (definition == null) return;
			var validation = definition.Validate();
			if (validation.IsFailure) EditorGUILayout.HelpBox(validation.Error.Message, MessageType.Error);
		}

		private void RefreshManifest(bool force) {
			var resolved = ResolveManifest();
			var fingerprint = resolved == null ? string.Empty : resolved.SourceFingerprint;
			if (!force && ReferenceEquals(resolved, _manifest) && string.Equals(fingerprint, _manifestFingerprint, StringComparison.Ordinal)) return;
			_manifest = resolved;
			_manifestFingerprint = fingerprint;
			_runtimeManifest = null;
			_manifestError = string.Empty;
			if (_manifest == null) return;
			try { _runtimeManifest = _manifest.BuildRuntimeManifest(); }
			catch (Exception exception) { _manifestError = "Shader Manifest could not be read: " + exception.Message; }
		}

		private static ShaderNodeManifestAsset ResolveManifest() {
			var direct = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(ManifestAssetPath);
			if (direct != null) return direct;
			foreach (var guid in AssetDatabase.FindAssets("t:ShaderNodeManifestAsset").OrderBy(value => value, StringComparer.Ordinal)) {
				var asset = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(AssetDatabase.GUIDToAssetPath(guid));
				if (asset != null) return asset;
			}
			foreach (var guid in AssetDatabase.FindAssets("t:NodeTypeCatalog").OrderBy(value => value, StringComparer.Ordinal)) {
				var catalog = AssetDatabase.LoadAssetAtPath<NodeTypeCatalog>(AssetDatabase.GUIDToAssetPath(guid));
				if (catalog != null && catalog.ShaderManifest != null) return catalog.ShaderManifest;
			}
			return null;
		}

		private void RefreshSceneDefinitions() {
			var definitions = new List<Scene3DDefinition>();
			foreach (var guid in AssetDatabase.FindAssets("t:Scene3DDefinition").OrderBy(value => value, StringComparer.Ordinal)) {
				var definition = AssetDatabase.LoadAssetAtPath<Scene3DDefinition>(AssetDatabase.GUIDToAssetPath(guid));
				if (definition != null) definitions.Add(definition);
			}
			_sceneDefinitions = definitions.OrderBy(definition => definition.name, StringComparer.Ordinal).ThenBy(definition => definition.Id, StringComparer.Ordinal).ToArray();
		}

		private IEnumerable<ShaderNodeManifestAssetEntry> GetManifestEntries() {
			return (_manifest == null ? Array.Empty<ShaderNodeManifestAssetEntry>() : _manifest.Entries)
				.Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.TypeId))
				.OrderBy(entry => entry.Category ?? string.Empty, StringComparer.Ordinal)
				.ThenBy(entry => entry.DisplayName ?? string.Empty, StringComparer.Ordinal)
				.ThenBy(entry => entry.TypeId, StringComparer.Ordinal);
		}

		private ShaderNodeManifestEntry FindRuntimeEntry(string typeId) => _runtimeManifest == null ? null : _runtimeManifest.Find(typeId);

		private ShaderNodeManifestEntry FindRuntimeEntryForGraphNode(SerializedProperty nodes, string nodeId) {
			for (var index = 0; index < nodes.arraySize; index++) {
				var node = nodes.GetArrayElementAtIndex(index);
				if (string.Equals(node.FindPropertyRelative("_id").stringValue, nodeId, StringComparison.Ordinal))
					return FindRuntimeEntry(node.FindPropertyRelative("_typeId").stringValue);
			}
			return null;
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

		private static List<string> GetSourceNodeIds(SerializedProperty nodes, out List<string> labels) {
			var ids = new List<string> { PatchProgramGraph.SceneInputNodeId };
			labels = new List<string> { "Scene Input" };
			var graphIds = GetGraphNodeIds(nodes, out var graphLabels);
			for (var index = 0; index < graphIds.Count; index++) {
				if (ids.Contains(graphIds[index], StringComparer.Ordinal)) continue;
				ids.Add(graphIds[index]);
				labels.Add(graphLabels[index]);
			}
			return ids;
		}

		private static List<string> GetImageInputPorts(ShaderNodeManifestEntry entry, out List<string> labels) {
			var ports = new List<string>();
			labels = new List<string>();
			if (entry == null) return ports;
			foreach (var input in entry.Inputs.Where(input => input != null && input.Type == NodePortType.ImageFrame)) {
				ports.Add(input.Id.Value);
				labels.Add(input.Id.Value + " · " + input.DisplayName);
			}
			return ports;
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

		private void EnsureGraphNodeIds(SerializedProperty nodes) {
			var used = new HashSet<string>(StringComparer.Ordinal);
			for (var index = 0; index < nodes.arraySize; index++) {
				var node = nodes.GetArrayElementAtIndex(index);
				var id = node.FindPropertyRelative("_id");
				if (!string.IsNullOrWhiteSpace(id.stringValue)) {
					used.Add(id.stringValue);
					continue;
				}
				var typeId = node.FindPropertyRelative("_typeId").stringValue;
				var entry = GetManifestEntries().FirstOrDefault(candidate => string.Equals(candidate.TypeId, typeId, StringComparison.Ordinal));
				id.stringValue = CreateUniqueId(Slug(entry == null ? "node" : entry.DisplayName), used);
				used.Add(id.stringValue);
			}
		}

		private void AddGraphNode(SerializedProperty nodes, ShaderNodeManifestAssetEntry entry) {
			var index = nodes.arraySize;
			nodes.arraySize++;
			var node = nodes.GetArrayElementAtIndex(index);
			var used = Enumerable.Range(0, index).Select(value => nodes.GetArrayElementAtIndex(value).FindPropertyRelative("_id").stringValue);
			var nodeId = CreateUniqueId(Slug(entry == null ? "node" : entry.DisplayName), used);
			node.FindPropertyRelative("_id").stringValue = nodeId;
			node.FindPropertyRelative("_typeId").stringValue = entry == null ? string.Empty : entry.TypeId;
			node.FindPropertyRelative("_parameters").arraySize = 0;
			var outputNodeId = _programGraph.FindPropertyRelative("_outputNodeId");
			var hasOutput = Enumerable.Range(0, nodes.arraySize)
				.Any(value => string.Equals(nodes.GetArrayElementAtIndex(value).FindPropertyRelative("_id").stringValue, outputNodeId.stringValue, StringComparison.Ordinal));
			if (string.IsNullOrWhiteSpace(outputNodeId.stringValue) || !hasOutput) outputNodeId.stringValue = nodeId;
		}

		private void RemoveGraphNode(SerializedProperty nodes, int index, string nodeId) {
			var output = _programGraph.FindPropertyRelative("_outputNodeId");
			var connections = _programGraph.FindPropertyRelative("_connections");
			if (string.Equals(output.stringValue, nodeId, StringComparison.Ordinal))
				output.stringValue = index > 0 ? nodes.GetArrayElementAtIndex(index - 1).FindPropertyRelative("_id").stringValue : string.Empty;
			for (var connectionIndex = connections.arraySize - 1; connectionIndex >= 0; connectionIndex--) {
				var connection = connections.GetArrayElementAtIndex(connectionIndex);
				var source = connection.FindPropertyRelative("_sourceNodeId").stringValue;
				var target = connection.FindPropertyRelative("_targetNodeId").stringValue;
				if (string.Equals(source, nodeId, StringComparison.Ordinal) || string.Equals(target, nodeId, StringComparison.Ordinal)) connections.DeleteArrayElementAtIndex(connectionIndex);
			}
			nodes.DeleteArrayElementAtIndex(index);
		}

		private static void SyncGraphNodeParameters(SerializedProperty node, ShaderNodeManifestEntry entry) {
			var parameters = node.FindPropertyRelative("_parameters");
			if (entry == null) return;
			for (var index = parameters.arraySize - 1; index >= 0; index--) {
				var id = parameters.GetArrayElementAtIndex(index).FindPropertyRelative("_id").stringValue;
				if (entry.Parameters.All(parameter => parameter == null || !string.Equals(parameter.Id.Value, id, StringComparison.Ordinal))) parameters.DeleteArrayElementAtIndex(index);
			}
			for (var index = 0; index < parameters.arraySize; index++) {
				var parameter = parameters.GetArrayElementAtIndex(index);
				var definition = entry.Parameters.FirstOrDefault(candidate => candidate != null && string.Equals(candidate.Id.Value, parameter.FindPropertyRelative("_id").stringValue, StringComparison.Ordinal));
				if (definition != null) SetParameterType(parameter, definition.Type);
			}
		}

		private static void AddGraphParameter(SerializedProperty parameters, ShaderNodeManifestParameter definition) {
			var index = parameters.arraySize;
			parameters.arraySize++;
			var parameter = parameters.GetArrayElementAtIndex(index);
			parameter.FindPropertyRelative("_id").stringValue = definition.Id.Value;
			SetParameterValue(parameter, definition.DefaultValue);
		}

		private void AddGraphConnection(SerializedProperty connections, SerializedProperty nodes) {
			var index = connections.arraySize;
			connections.arraySize++;
			var connection = connections.GetArrayElementAtIndex(index);
			connection.FindPropertyRelative("_sourceNodeId").stringValue = PatchProgramGraph.SceneInputNodeId;
			connection.FindPropertyRelative("_sourcePortId").stringValue = PatchProgramGraph.ImagePortId;
			var targetIds = GetGraphNodeIds(nodes, out _);
			var targetNodeId = targetIds.Count == 0 ? string.Empty : targetIds[0];
			connection.FindPropertyRelative("_targetNodeId").stringValue = targetNodeId;
			var entry = FindRuntimeEntryForGraphNode(nodes, targetNodeId);
			var ports = GetImageInputPorts(entry, out _);
			connection.FindPropertyRelative("_targetPortId").stringValue = ports.Count == 0 ? string.Empty : ports[0];
		}

		private void AddSceneNodeGroup() {
			var index = _nodeGroups.arraySize;
			_nodeGroups.arraySize++;
			var group = _nodeGroups.GetArrayElementAtIndex(index);
			group.FindPropertyRelative("_id").stringValue = CreateGroupId("group", index);
			group.FindPropertyRelative("_displayName").stringValue = "Group " + (index + 1);
		}

		private void AddPublishedParameter(IReadOnlyList<SceneNodeOption> sceneNodes) {
			var index = _parameters.arraySize;
			_parameters.arraySize++;
			var parameter = _parameters.GetArrayElementAtIndex(index);
			var node = sceneNodes[0];
			var options = GetSceneParameterOptions(node.Definition);
			var selected = options.FirstOrDefault();
			parameter.FindPropertyRelative("_nodeId").stringValue = node.Id;
			parameter.FindPropertyRelative("_parameterId").stringValue = selected == null ? string.Empty : selected.Id;
			parameter.FindPropertyRelative("_id").stringValue = CreatePublishedParameterId(selected == null ? string.Empty : selected.Id, index);
			parameter.FindPropertyRelative("_displayName").stringValue = selected == null ? string.Empty : selected.Label;
		}

		private List<SceneNodeOption> GetConfiguredSceneNodes() {
			var result = new List<SceneNodeOption>();
			var used = new HashSet<string>(StringComparer.Ordinal);
			for (var groupIndex = 0; groupIndex < _nodeGroups.arraySize; groupIndex++) {
				var nodes = _nodeGroups.GetArrayElementAtIndex(groupIndex).FindPropertyRelative("_nodes");
				for (var nodeIndex = 0; nodeIndex < nodes.arraySize; nodeIndex++) {
					var definition = nodes.GetArrayElementAtIndex(nodeIndex).objectReferenceValue as Scene3DDefinition;
					if (definition == null || string.IsNullOrWhiteSpace(definition.Id) || !used.Add(definition.Id)) continue;
					result.Add(new SceneNodeOption(definition.Id, FormatSceneDefinitionLabel(definition), definition));
				}
			}
			return result;
		}

		private static List<SceneParameterOption> GetSceneParameterOptions(Scene3DDefinition definition) {
			var result = new List<SceneParameterOption>();
			if (definition == null || definition.Prefab == null) return result;
			var seen = new HashSet<string>(StringComparer.Ordinal);
			foreach (var component in definition.Prefab.GetComponentsInChildren<MonoBehaviour>(true)) {
				if (component == null) continue;
				var definitionProperty = component.GetType().GetProperty("Definition", BindingFlags.Instance | BindingFlags.Public);
				if (definitionProperty == null || definitionProperty.GetIndexParameters().Length != 0) continue;
				object parameterDefinition;
				try { parameterDefinition = definitionProperty.GetValue(component, null); }
				catch { continue; }
				if (parameterDefinition == null) continue;
				var id = ReadStringProperty(parameterDefinition, "Id");
				if (string.IsNullOrWhiteSpace(id) || !seen.Add(id)) continue;
				var displayName = ReadStringProperty(parameterDefinition, "DisplayName");
				result.Add(new SceneParameterOption(id, string.IsNullOrWhiteSpace(displayName) ? id : displayName));
			}
			return result.OrderBy(option => option.Label, StringComparer.Ordinal).ThenBy(option => option.Id, StringComparer.Ordinal).ToList();
		}

		private static string ReadStringProperty(object value, string propertyName) {
			var property = value.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
			if (property == null || property.PropertyType != typeof(string)) return string.Empty;
			try { return property.GetValue(value, null) as string ?? string.Empty; }
			catch { return string.Empty; }
		}

		private static SerializedProperty ValueProperty(SerializedProperty parameter, ParameterType type) {
			switch (type) {
				case ParameterType.Float: return parameter.FindPropertyRelative("_floatValue");
				case ParameterType.Int: return parameter.FindPropertyRelative("_intValue");
				case ParameterType.Bool: return parameter.FindPropertyRelative("_boolValue");
				case ParameterType.Vector2: return parameter.FindPropertyRelative("_vector2Value");
				case ParameterType.Vector3: return parameter.FindPropertyRelative("_vector3Value");
				case ParameterType.Vector4: return parameter.FindPropertyRelative("_vector4Value");
				case ParameterType.Color: return parameter.FindPropertyRelative("_colorValue");
				case ParameterType.String:
				case ParameterType.Enum:
				case ParameterType.MediaAssetReference: return parameter.FindPropertyRelative("_textValue");
				default: return null;
			}
		}

		private static void SetParameterType(SerializedProperty parameter, ParameterType type) {
			parameter.FindPropertyRelative("_type").enumValueIndex = (int)type;
		}

		private static void SetParameterValue(SerializedProperty parameter, ParameterValue value) {
			SetParameterType(parameter, value.Type);
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

		private string CreateGroupId(string preferred, int currentIndex) {
			var used = Enumerable.Range(0, _nodeGroups.arraySize).Where(index => index != currentIndex)
				.Select(index => _nodeGroups.GetArrayElementAtIndex(index).FindPropertyRelative("_id").stringValue);
			return CreateUniqueId(Slug(string.IsNullOrWhiteSpace(preferred) ? "group" : preferred), used);
		}

		private string CreatePublishedParameterId(string parameterId, int currentIndex) {
			var used = Enumerable.Range(0, _parameters.arraySize).Where(index => index != currentIndex)
				.Select(index => _parameters.GetArrayElementAtIndex(index).FindPropertyRelative("_id").stringValue);
			var preferred = string.IsNullOrWhiteSpace(parameterId) ? "parameter" : parameterId;
			return CreateUniqueId(Slug(preferred), used);
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

		private static string FormatNodeLabel(ShaderNodeManifestAssetEntry entry) {
			var name = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.TypeId : entry.DisplayName;
			return name + " · " + entry.Category + " (" + entry.TypeId + ")";
		}

		private static string FormatParameterLabel(ShaderNodeManifestParameter parameter)
			=> string.IsNullOrWhiteSpace(parameter.DisplayName) ? parameter.Id.Value : parameter.DisplayName + " (" + parameter.Id.Value + ")";

		private static string FormatSceneDefinitionLabel(Scene3DDefinition definition)
			=> string.IsNullOrWhiteSpace(definition.name) ? definition.Id : definition.name + " (" + definition.Id + ")";

		private static string GetSelectedLabel(IReadOnlyList<SceneParameterOption> options, string id, string fallback)
			=> options.FirstOrDefault(option => string.Equals(option.Id, id, StringComparison.Ordinal))?.Label ?? fallback;

		private sealed class SceneNodeOption {
			public string Id { get; }
			public string Label { get; }
			public Scene3DDefinition Definition { get; }
			public SceneNodeOption(string id, string label, Scene3DDefinition definition) { Id = id; Label = label; Definition = definition; }
		}

		private sealed class SceneParameterOption {
			public string Id { get; }
			public string Label { get; }
			public SceneParameterOption(string id, string label) { Id = id; Label = label; }
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
