using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Nodes;
using ShitDesigner.Rendering;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	/// <summary>Inspector authoring for <see cref="VJShaderPlayground"/>.
	/// The generated manifest is the source of truth for this popup, so the
	/// selected Type ID cannot drift away from a direct Shader reference.</summary>
	[CustomEditor(typeof(VJShaderPlayground))]
	[CanEditMultipleObjects]
	public sealed class VJShaderPlaygroundEditor : UnityEditor.Editor {
		private SerializedProperty _catalog;
		private SerializedProperty _manifest;
		private SerializedProperty _selectedTypeId;
		private SerializedProperty _shaderOverride;
		private string[] _typeIds = Array.Empty<string>();
		private string[] _labels = Array.Empty<string>();
		private UnityEngine.Object _lastCatalog;
		private UnityEngine.Object _lastManifest;
		private int _lastEntryCount = -1;
		private string _lastFingerprint = string.Empty;

		private void OnEnable() {
			_catalog = serializedObject.FindProperty("nodeTypeCatalog");
			_manifest = serializedObject.FindProperty("shaderManifest");
			_selectedTypeId = serializedObject.FindProperty("selectedTypeId");
			_shaderOverride = serializedObject.FindProperty("shaderOverride");
			RefreshOptions(true);
		}

		public override void OnInspectorGUI() {
			serializedObject.Update();
			EditorGUILayout.LabelField("Catalog and node", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(_catalog, new GUIContent("Node Type Catalog"));
			EditorGUILayout.PropertyField(_manifest, new GUIContent("Shader Manifest"));
			RefreshOptions(false);
			DrawTypeSelector();
			EditorGUILayout.PropertyField(_shaderOverride, new GUIContent("Shader Override"));

			EditorGUILayout.Space(4f);
			DrawRemainingProperties();
			serializedObject.ApplyModifiedProperties();
		}

		private void DrawTypeSelector() {
			if (_selectedTypeId == null) return;
			if (_typeIds.Length == 0) {
				EditorGUILayout.HelpBox(
					"Assign the generated Shader Manifest or Node Type Catalog to choose a shader node. If the manifest is unavailable, use Shader Override below.",
					MessageType.Info);
				return;
			}

			var selected = Array.IndexOf(_typeIds, _selectedTypeId.stringValue);
			if (selected < 0) selected = 0;
			EditorGUI.BeginChangeCheck();
			selected = EditorGUILayout.Popup(new GUIContent("Shader Node", "Select a generated shader node by display name."), selected, _labels);
			if (EditorGUI.EndChangeCheck()) _selectedTypeId.stringValue = _typeIds[selected];
			EditorGUILayout.LabelField("Selected Type ID", _typeIds[selected], EditorStyles.miniLabel);
		}

		private void DrawRemainingProperties() {
			var iterator = serializedObject.GetIterator();
			var enterChildren = true;
			while (iterator.NextVisible(enterChildren)) {
				enterChildren = false;
				if (iterator.propertyPath == "m_Script"
					|| iterator.propertyPath == _catalog.propertyPath
					|| iterator.propertyPath == _manifest.propertyPath
					|| iterator.propertyPath == _selectedTypeId.propertyPath
					|| iterator.propertyPath == _shaderOverride.propertyPath) continue;
				EditorGUILayout.PropertyField(iterator, true);
			}
		}

		private void RefreshOptions(bool force) {
			var manifest = _manifest == null ? null : _manifest.objectReferenceValue as ShaderNodeManifestAsset;
			var catalog = _catalog == null ? null : _catalog.objectReferenceValue as NodeTypeCatalog;
			var effectiveManifest = manifest != null ? manifest : catalog == null ? null : catalog.ShaderManifest;
			var entryCount = effectiveManifest == null ? 0 : effectiveManifest.Entries.Count;
			var fingerprint = effectiveManifest == null ? string.Empty : effectiveManifest.SourceFingerprint;
			if (!force && ReferenceEquals(effectiveManifest, _lastManifest) && ReferenceEquals(catalog, _lastCatalog)
				&& entryCount == _lastEntryCount && string.Equals(fingerprint, _lastFingerprint, StringComparison.Ordinal)) return;
			_lastManifest = effectiveManifest;
			_lastCatalog = catalog;
			_lastEntryCount = entryCount;
			_lastFingerprint = fingerprint;
			if (effectiveManifest == null) {
				_typeIds = Array.Empty<string>();
				_labels = Array.Empty<string>();
				return;
			}

			var options = effectiveManifest.Entries
				.Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.TypeId))
				.OrderBy(entry => entry.Category ?? string.Empty, StringComparer.Ordinal)
				.ThenBy(entry => entry.DisplayName ?? string.Empty, StringComparer.Ordinal)
				.ThenBy(entry => entry.TypeId, StringComparer.Ordinal)
				.ToList();
			_typeIds = options.Select(entry => entry.TypeId).ToArray();
			_labels = options.Select(entry => FormatLabel(entry)).ToArray();
		}

		private static string FormatLabel(ShaderNodeManifestAssetEntry entry) {
			var name = string.IsNullOrWhiteSpace(entry.DisplayName) ? entry.TypeId : entry.DisplayName;
			var family = entry.Family.ToString();
			var category = string.IsNullOrWhiteSpace(entry.Category) || string.Equals(entry.Category, family, StringComparison.OrdinalIgnoreCase)
				? string.Empty : " · " + entry.Category;
			return name + " · " + family + category + " (" + entry.TypeId + ")";
		}
	}
}
