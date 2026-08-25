using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using ShitDesigner.Bootstrap;
using ShitDesigner.Presentation;

namespace ShitDesigner.Bootstrap.Editor {
	/// <summary>Authoring boundary for the production entry scene. Keeping
	/// this in Editor prevents Player code from discovering or creating UI
	/// assets while still making the checked-in scene self-contained.</summary>
	public static class BootstrapSceneAuthoring {
		private const string ScenePath = "Assets/ShitDesigner/Scenes/ShitDesignerBootstrap.unity";
		private const string PanelPath = "Assets/ShitDesigner/Scripts/Bootstrap/ShitDesignerPanelSettings.asset";
		private const string ThemePath = "Assets/ShitDesigner/Scripts/Presentation/Resources/PresentationTheme.uss";
		private const string UiFontSourcePath = "Assets/ShitDesigner/Scripts/Presentation/Resources/Fonts/NotoSans.ttf";
		private const string MonoFontSourcePath = "Assets/ShitDesigner/Scripts/Presentation/Resources/Fonts/NotoSansMono.ttf";
		private const string JapaneseFontSourcePath = "Assets/ShitDesigner/Scripts/Presentation/Resources/Fonts/NotoSansJP.ttf";
		private const string UiFontAssetPath = "Assets/ShitDesigner/Scripts/Presentation/Resources/NotoSans.asset";
		private const string MonoFontAssetPath = "Assets/ShitDesigner/Scripts/Presentation/Resources/NotoSansMono.asset";
		private const string JapaneseFontAssetPath = "Assets/ShitDesigner/Scripts/Presentation/Resources/NotoSansJP.asset";
		private const string DisplayTransformShaderPath = "Assets/ShitDesigner/Scripts/Rendering/DisplayTransform.shader";

		public static void Ensure() {
			EnsurePresentationFonts();
			var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
			if (panel == null) {
				panel = ScriptableObject.CreateInstance<PanelSettings>();
				panel.name = "ShitDesignerPanelSettings";
				AssetDatabase.CreateAsset(panel, PanelPath);
			}
			panel.referenceResolution = new Vector2Int(1600, 900);
			panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
			EditorUtility.SetDirty(panel);
			AssetDatabase.SaveAssets();

			var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
			var root = GameObject.Find("Host");
			if (root == null) throw new InvalidOperationException("Bootstrap scene root is missing.");
			var presentation = root.GetComponent<PresentationRoot>() ?? root.AddComponent<PresentationRoot>();
			var document = root.GetComponent<UIDocument>() ?? root.AddComponent<UIDocument>();
			var documentSerialized = new SerializedObject(document);
			var panelProperty = documentSerialized.FindProperty("m_PanelSettings");
			if (panelProperty == null) throw new InvalidOperationException("UIDocument panel settings property is missing.");
			panelProperty.objectReferenceValue = panel;
			documentSerialized.ApplyModifiedPropertiesWithoutUndo();
			document.panelSettings = panel;
			EditorUtility.SetDirty(document);
			var presentationSerialized = new SerializedObject(presentation);
			presentationSerialized.FindProperty("_document").objectReferenceValue = document;
			presentationSerialized.FindProperty("_theme").objectReferenceValue =
				AssetDatabase.LoadAssetAtPath<StyleSheet>(ThemePath);
			presentationSerialized.ApplyModifiedPropertiesWithoutUndo();
			var behaviour = root.GetComponent<ApplicationHost>();
			if (behaviour == null) throw new InvalidOperationException("ApplicationHost is missing.");
			var midiInputManager = root.GetComponent<ShitDesigner.Input.MidiInputManager>() ?? root.AddComponent<ShitDesigner.Input.MidiInputManager>();
			var assets = root.GetComponent<BootstrapAssets>();
			if (assets == null) throw new InvalidOperationException("BootstrapAssets is missing.");
			var displayTransform = AssetDatabase.LoadAssetAtPath<Shader>(DisplayTransformShaderPath);
			if (displayTransform == null) throw new InvalidOperationException("DisplayTransform shader is missing.");
			var assetsSerialized = new SerializedObject(assets);
			var displayTransformProperty = assetsSerialized.FindProperty("_displayTransformShader");
			if (displayTransformProperty == null) throw new InvalidOperationException("DisplayTransform shader property is missing.");
			displayTransformProperty.objectReferenceValue = displayTransform;
			assetsSerialized.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(assets);
			var behaviourSerialized = new SerializedObject(behaviour);
			var behaviourPanelProperty = behaviourSerialized.FindProperty("_panelSettings");
			if (behaviourPanelProperty == null) throw new InvalidOperationException("Bootstrap panel settings property is missing.");
			behaviourPanelProperty.objectReferenceValue = panel;
			var behaviourMidiProperty = behaviourSerialized.FindProperty("_midiInputManager");
			if (behaviourMidiProperty == null) throw new InvalidOperationException("Bootstrap MIDI Input Manager property is missing.");
			behaviourMidiProperty.objectReferenceValue = midiInputManager;
			behaviourSerialized.ApplyModifiedPropertiesWithoutUndo();
			EditorUtility.SetDirty(behaviour);
			EditorSceneManager.MarkSceneDirty(scene);
			if (!EditorSceneManager.SaveScene(scene)) throw new InvalidOperationException("Could not save bootstrap scene.");
			AssetDatabase.Refresh();
		}

		private static void EnsurePresentationFonts() {
			var ui = EnsureFontAsset(UiFontSourcePath, UiFontAssetPath);
			var mono = EnsureFontAsset(MonoFontSourcePath, MonoFontAssetPath);
			var japanese = EnsureFontAsset(JapaneseFontSourcePath, JapaneseFontAssetPath);
			EnsureFallback(ui, japanese);
			EnsureFallback(mono, japanese);
			AssetDatabase.SaveAssets();
		}

		private static FontAsset EnsureFontAsset(string sourcePath, string assetPath) {
			AssetDatabase.ImportAsset(sourcePath, ImportAssetOptions.ForceSynchronousImport);
			var source = AssetDatabase.LoadAssetAtPath<Font>(sourcePath);
			if (source == null) throw new InvalidOperationException("Bundled Noto source font is missing: " + sourcePath);
			var asset = AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath);
			if (asset != null && HasOwnedFontSubassets(asset, assetPath)) return asset;
			if (asset != null && !AssetDatabase.DeleteAsset(assetPath))
				throw new InvalidOperationException("Could not replace incomplete TextCore FontAsset: " + assetPath);
			asset = FontAsset.CreateFontAsset(source, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
			if (asset == null) throw new InvalidOperationException("Could not create TextCore FontAsset from bundled font: " + sourcePath);
			asset.name = System.IO.Path.GetFileNameWithoutExtension(assetPath);
			AssetDatabase.CreateAsset(asset, assetPath);
			PersistFontSubassets(asset, assetPath);
			if (!HasOwnedFontSubassets(asset, assetPath))
				throw new InvalidOperationException("Generated TextCore FontAsset is missing its owned atlas texture or material: " + assetPath);
			return asset;
		}

		private static void PersistFontSubassets(FontAsset asset, string assetPath) {
			if (asset.atlasTextures == null || asset.atlasTextures.Length == 0 || asset.atlasTextures.Any(texture => texture == null))
				throw new InvalidOperationException("Generated TextCore FontAsset did not create an atlas texture: " + assetPath);
			if (asset.material == null)
				throw new InvalidOperationException("Generated TextCore FontAsset did not create a material: " + assetPath);
			foreach (var texture in asset.atlasTextures)
				AssetDatabase.AddObjectToAsset(texture, asset);
			AssetDatabase.AddObjectToAsset(asset.material, asset);
			EditorUtility.SetDirty(asset);
		}

		private static bool HasOwnedFontSubassets(FontAsset asset, string assetPath) {
			return asset.atlasTextures != null
				   && asset.atlasTextures.Length != 0
				   && asset.atlasTextures.All(texture => texture != null && AssetDatabase.GetAssetPath(texture) == assetPath)
				   && asset.material != null
				   && AssetDatabase.GetAssetPath(asset.material) == assetPath;
		}

		private static void EnsureFallback(FontAsset primary, FontAsset fallback) {
			if (primary == null || fallback == null) return;
			var fallbacks = primary.fallbackFontAssetTable;
			if (fallbacks == null) {
				fallbacks = new List<FontAsset>();
				primary.fallbackFontAssetTable = fallbacks;
			}
			if (fallbacks.Contains(fallback)) return;
			fallbacks.Add(fallback);
			EditorUtility.SetDirty(primary);
		}
	}
}
