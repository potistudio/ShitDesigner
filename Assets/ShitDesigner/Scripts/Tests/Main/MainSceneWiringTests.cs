using System.Linq;
using NUnit.Framework;
using ShitDesigner.Bootstrap;
using ShitDesigner.Input;
using ShitDesigner.Rendering;
using ShitDesigner.Scene;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class MainSceneWiringTests {
		[Test]
		public void MainUsesOnlyTheDedicatedLiveHostComposition() {
			var scene = EditorSceneManager.OpenScene("Assets/ShitDesigner/Scenes/Main/Main.unity", OpenSceneMode.Additive);
			try {
				var root = scene.GetRootGameObjects().Single();
				var host = root.GetComponent<ApplicationLiveHost>();
				var graph = root.GetComponent<LiveGraphBootstrap>();
				var midi = root.GetComponent<MidiInputManager>();
				var capability = root.GetComponent<LiveCapabilityMonitor>();
				var output = root.GetComponent<LiveExternalDisplayOutput>();
				var ui = root.GetComponent<LiveUiController>();
				var panelRenderer = root.GetComponent<PanelRenderer>();

				Assert.That(root.name, Is.EqualTo("Main Live Host"));
				Assert.That(host, Is.Not.Null);
				Assert.That(graph, Is.Not.Null);
				Assert.That(midi, Is.Not.Null);
				Assert.That(capability, Is.Not.Null);
				Assert.That(output, Is.Not.Null);
				Assert.That(ui, Is.Not.Null);
				Assert.That(panelRenderer, Is.Not.Null);
				Assert.That(panelRenderer.visualTreeAsset, Is.Not.Null);
				Assert.That(panelRenderer.panelSettings, Is.Not.Null);
				Assert.That(root.GetComponent<UIDocument>(), Is.Null);
				var serializedUi = new SerializedObject(ui);
				Assert.That(serializedUi.FindProperty("m_PanelRenderer").objectReferenceValue, Is.SameAs(panelRenderer));
				Assert.That(graph.Patches.Length, Is.EqualTo(5));
				Assert.That(graph.ProgramOutputCount, Is.EqualTo(graph.Patches.Length));
				Assert.That(graph.Patches.All(definition => definition != null && !string.IsNullOrWhiteSpace(definition.Id)
					&& definition.ProgramGraph.Nodes.Any(node => node != null && node.IsSceneNode && node.SceneDefinition != null)), Is.True);
				Assert.That(graph.Patches.All(definition => definition.ProgramGraph.Nodes.Count > 0 && definition.ProgramGraph.Connections.Count > 0), Is.True);
				Assert.That(graph.Patches.Select(definition => definition.Id).Distinct().Count(), Is.EqualTo(5));
				Assert.That(graph.Patches.SelectMany(definition => definition.ProgramGraph.Nodes).Where(node => node.IsSceneNode)
					.Select(node => node.SceneDefinition.Prefab).All(prefab => prefab.GetComponent<LiveSceneRoot>() != null), Is.True);
				var bpmShapes = graph.Patches.Single(definition => definition.Id == "bpm-shapes");
				var bpmShapesSceneNode = bpmShapes.ProgramGraph.Nodes.Single(node => node.IsSceneNode);
				var bpmShapesPrefab = bpmShapesSceneNode.SceneDefinition.Prefab;
				Assert.That(bpmShapesPrefab.GetComponent("BpmShapeMotionScene"), Is.Not.Null);
				Assert.That(bpmShapesPrefab.GetComponentInChildren<Camera>().orthographic, Is.True);
				var stage = graph.Patches.Single(definition => definition.Id == "stage");
				var stageSceneNode = stage.ProgramGraph.Nodes.Single(node => node.IsSceneNode);
				var stagePrefab = stageSceneNode.SceneDefinition.Prefab;
				Assert.That(stage.Parameters.Single().NodeId, Is.EqualTo(stageSceneNode.Id));
				Assert.That(stage.Parameters.Single().ParameterId, Is.EqualTo(LiveGraphClockRateParameter.ParameterId));
				Assert.That(stagePrefab.GetComponent<LiveGraphClockRateParameter>(), Is.Not.Null);
				Assert.That(stagePrefab.GetComponent("BpmAnimatorSpeedController"), Is.Not.Null);
				var penlightCrowd = stagePrefab.GetComponent("InstancedPenlightCrowd");
				Assert.That(penlightCrowd, Is.Not.Null);
				Assert.That(penlightCrowd, Is.InstanceOf<IBpmClockReceiver>());
				var serializedPenlightCrowd = new SerializedObject(penlightCrowd);
				Assert.That(serializedPenlightCrowd.FindProperty("_count").intValue, Is.GreaterThan(1023));
				Assert.That(((Material)serializedPenlightCrowd.FindProperty("_material").objectReferenceValue).enableInstancing, Is.True);
				var serializedGraph = new SerializedObject(graph);
				Assert.That(serializedGraph.FindProperty("_shaderManifest").objectReferenceValue, Is.Not.Null);
				var serializedHost = new SerializedObject(host);
				Assert.That(serializedHost.FindProperty("_graphBootstrap").objectReferenceValue, Is.SameAs(graph));
				Assert.That(serializedHost.FindProperty("_midiInputManager").objectReferenceValue, Is.SameAs(midi));
				Assert.That(serializedHost.FindProperty("_capabilityMonitor").objectReferenceValue, Is.SameAs(capability));
				Assert.That(serializedHost.FindProperty("_externalDisplay").objectReferenceValue, Is.SameAs(output));
				Assert.That(serializedHost.FindProperty("_uiController").objectReferenceValue, Is.SameAs(ui));
				Assert.That(serializedHost.FindProperty("_bootOnAwake").boolValue, Is.True);
				var serializedOutput = new SerializedObject(output);
				Assert.That(serializedOutput.FindProperty("_displayTransformShader").objectReferenceValue, Is.Not.Null);

				Assert.That(root.GetComponent<ApplicationHost>(), Is.Null);
				Assert.That(root.GetComponent<ApplicationLoopDriver>(), Is.Null);
				Assert.That(root.GetComponent<StartupSceneGraphBootstrap>(), Is.Null);
				Assert.That(root.GetComponent<Scene3DNode>(), Is.Null);
				Assert.That(root.GetComponent<SimpleExternalDisplayOutput>(), Is.Null);
			}
			finally { EditorSceneManager.CloseScene(scene, removeScene: true); }
		}

		[Test]
		public void MainIsTheFirstEnabledBuildScene() {
			var first = EditorBuildSettings.scenes.First(scene => scene.enabled);
			Assert.That(first.path, Is.EqualTo("Assets/ShitDesigner/Scenes/Main/Main.unity"));
		}

		[Test]
		public void MacExternalDisplayPluginIsIncludedInMacPlayers() {
			const string path = "Assets/ShitDesigner/Plugins/macOS/shitdesigner_mac_display.dylib";
			var importer = AssetImporter.GetAtPath(path) as PluginImporter;

			Assert.That(importer, Is.Not.Null);
			Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX), Is.True);
			Assert.That(importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64), Is.False);
		}

		[Test]
		public void MainUiDefinesAllPatchSlotButtons() {
			var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/ShitDesigner/Scenes/Main/MainUI.uxml");
			Assert.That(asset, Is.Not.Null);
			var root = new VisualElement();
			asset.CloneTree(root);
			var controls = root.Q<VisualElement>("patch-slot-controls");
			Assert.That(controls, Is.Not.Null);

			var buttons = controls.Query<Button>().ToList();
			Assert.That(buttons, Has.Count.EqualTo(LivePatchSlots.Capacity));
			Assert.That(buttons.Select(button => button.name).ToArray(), Is.EqualTo(Enumerable.Range(0, LivePatchSlots.Capacity).Select(index => "patch-slot-" + index).ToArray()));
			Assert.That(root.Q<Button>("cue-patch-slot"), Is.Null);
			Assert.That(root.Q<Button>("launch-patch-slot"), Is.Null);
			Assert.That(root.Q<Button>("clear-patch-slot"), Is.Null);
		}

		[Test]
		public void MainUiDefinesOverlayAndEffectSequencerHosts() {
			var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/ShitDesigner/Scenes/Main/MainUI.uxml");
			Assert.That(asset, Is.Not.Null);
			var root = new VisualElement();
			asset.CloneTree(root);

			var controls = root.Q<VisualElement>("sequencer-controls");

			Assert.That(controls, Is.Not.Null);
			Assert.That(controls.childCount, Is.EqualTo(2));
			Assert.That(controls.parent.ClassListContains("preview-stack"), Is.True);
			Assert.That(controls.parent.ClassListContains("inspector-column"), Is.False);
			Assert.That(root.Q<VisualElement>("overlay-sequencer"), Is.Not.Null);
			Assert.That(root.Q<VisualElement>("effect-sequencer"), Is.Not.Null);
			Assert.That(root.Q<VisualElement>("compositing-mode-sequencer"), Is.Null);
		}

		[Test]
		public void MainUiDefinesThreeSidebarTabs() {
			var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/ShitDesigner/Scenes/Main/MainUI.uxml");
			Assert.That(asset, Is.Not.Null);
			var root = new VisualElement();
			asset.CloneTree(root);

			var tabs = root.Query<Button>(className: "sidebar-tab").ToList();

			Assert.That(tabs.Select(tab => tab.name).ToArray(), Is.EqualTo(new[] { "inspector-tab", "logical-controls-tab", "diagnostics-tab" }));
			Assert.That(tabs[0].ClassListContains("is-selected"), Is.True);
			Assert.That(root.Q<Label>("capability-status").parent.name, Is.EqualTo("diagnostics-tab-content"));
			Assert.That(root.Q<Label>("diagnostic-status").parent.name, Is.EqualTo("diagnostics-tab-content"));
		}

		[Test]
		public void ExternalProgramDisplayCamera_UsesAnUrpRenderableSurface() {
			var host = new GameObject("External Program Display Camera Test");
			var source = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32);
			try {
				Assert.That(source.Create(), Is.True);
				var camera = host.AddComponent<Camera>();
				var presenter = host.AddComponent<LiveProgramDisplayCamera>();

				presenter.Initialize(camera, source);

				Assert.That(camera.cullingMask, Is.EqualTo(1 << 31));
				var renderer = host.GetComponentInChildren<MeshRenderer>();
				Assert.That(renderer, Is.Not.Null);
				Assert.That(renderer.sharedMaterial.shader.name, Is.EqualTo("Hidden/ShitDesigner/ProgramDisplay"));
			}
			finally {
				source.Release();
				Object.DestroyImmediate(source);
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void ExternalProgramDisplayCanvas_FillsTheTargetWithoutACamera() {
			var host = new GameObject("External Program Display Canvas Test");
			var source = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32);
			try {
				Assert.That(source.Create(), Is.True);
				var canvas = host.AddComponent<Canvas>();
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				var presenter = host.AddComponent<LiveProgramDisplayCanvas>();

				presenter.Initialize(canvas, source);

				Assert.That(host.GetComponent<Camera>(), Is.Null);
				Assert.That(presenter.Source, Is.SameAs(source));
				var image = (RectTransform)presenter.transform.GetChild(0);
				Assert.That(image.anchorMin, Is.EqualTo(Vector2.zero));
				Assert.That(image.anchorMax, Is.EqualTo(Vector2.one));
				Assert.That(image.offsetMin, Is.EqualTo(Vector2.zero));
				Assert.That(image.offsetMax, Is.EqualTo(Vector2.zero));
			}
			finally {
				source.Release();
				Object.DestroyImmediate(source);
				Object.DestroyImmediate(host);
			}
		}
	}
}
