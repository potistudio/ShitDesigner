using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Bootstrap;
using ShitDesigner.Input;
using ShitDesigner.Nodes;
using ShitDesigner.Rendering;
using ShitDesigner.Runtime;
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
				var serializedPanelSettings = new SerializedObject(panelRenderer.panelSettings);
				Assert.That(serializedPanelSettings.FindProperty("themeUss").objectReferenceValue, Is.Not.Null);
				Assert.That(root.GetComponent<UIDocument>(), Is.Null);
				var serializedUi = new SerializedObject(ui);
				Assert.That(serializedUi.FindProperty("m_PanelRenderer").objectReferenceValue, Is.SameAs(panelRenderer));
				Assert.That(graph.Patches.Length, Is.EqualTo(5));
				Assert.That(graph.ProgramOutputCount, Is.EqualTo(graph.Patches.Length));
				Assert.That(graph.EffectNodes, Is.Not.Empty);
				Assert.That(graph.EffectNodes.All(node => node.UserAddable && node.Inputs.Any(input =>
					input.Type == ShitDesigner.Runtime.NodePortType.ImageFrame && input.Role != ShitDesigner.Nodes.ShaderInputRole.History)), Is.True);
				Assert.That(graph.EffectNodes.Select(node => node.TypeId).Distinct().Count(), Is.EqualTo(graph.EffectNodes.Count));
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
				Assert.That(serializedHost.FindProperty("m_SceneTimeEncoderChannel").intValue, Is.EqualTo(16));
				Assert.That(serializedHost.FindProperty("m_SceneTimeEncoderControlNumber").intValue, Is.EqualTo(77));
				Assert.That(serializedHost.FindProperty("m_SceneTimeJogSpeedPerStep").floatValue, Is.EqualTo(1f));
				Assert.That(serializedHost.FindProperty("m_SceneTimeJogMaximumSpeedOffset").floatValue, Is.EqualTo(4f));
				Assert.That(midi.DeviceId, Is.EqualTo(1));
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
		public void InmKingPublishesTheLedScreenVideoPlayhead() {
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ShitDesigner/Scenes/INM KING/INM KING.prefab");
			var patch = AssetDatabase.LoadAssetAtPath<PatchDefinition>("Assets/ShitDesigner/Scenes/INM KING/INM KING Patch.asset");
			var parameter = prefab.GetComponent<LiveVideoPlayheadParameter>();
			var player = prefab.transform.Find("Stage/LED Screen/Video Player").GetComponent<UnityEngine.Video.VideoPlayer>();
			var serializedParameter = new SerializedObject(parameter);

			Assert.That(prefab.GetComponent<LiveSceneRoot>(), Is.Not.Null);
			Assert.That(parameter, Is.Not.Null);
			Assert.That(serializedParameter.FindProperty("m_VideoPlayer").objectReferenceValue, Is.SameAs(player));
			Assert.That(parameter.Definition.Id, Is.EqualTo(LiveVideoPlayheadParameter.ParameterId));
			Assert.That(parameter.Definition.Maximum, Is.EqualTo((float)player.clip.length).Within(.001f));
			var published = patch.Parameters.Single(candidate => candidate.Id == LiveVideoPlayheadParameter.ParameterId);
			Assert.That(published.NodeId, Is.EqualTo("scene"));
			Assert.That(published.ParameterId, Is.EqualTo(LiveVideoPlayheadParameter.ParameterId));
		}

		[Test]
		public void EffectCatalog_ContainsImageProcessingNodesButNotGenerators() {
			var effects = LiveGraphBootstrap.BuildEffectNodeCatalog(ShitDesigner.Nodes.ShaderNodeManifest.CreateBuiltIn());

			Assert.That(effects.Select(entry => entry.TypeId.Value), Is.EqualTo(new[] {
				"shitdesigner.shader.blend2",
				"shitdesigner.shader.effect"
			}));
		}

		[Test]
		public void InstantEffectWiringConnectsTheProgramImageToPrimaryAndRequiredImageInputs() {
			var entry = new ShaderNodeManifestEntry(new NodeTypeId("test.instant.effect"), "Test", "Test",
				ShaderNodeFamily.Custom, "test.shader", inputs: new[] {
					new ShaderNodeManifestInput(new PortId("input"), "Input", "_MainTex"),
					new ShaderNodeManifestInput(new PortId("secondary"), "Secondary", "_SecondaryTex", ShaderInputRole.Secondary),
					new ShaderNodeManifestInput(new PortId("mask"), "Mask", "_MaskTex", ShaderInputRole.Mask,
						required: false, defaultImage: RuntimeDefaultImageKind.White)
				});

			var inputs = LiveInstantEffectRenderer.BuildInputs(entry.ToShaderBinding(), Texture2D.blackTexture);

			Assert.That(inputs.Keys.Select(port => port.Value), Is.EquivalentTo(new[] { "input", "secondary" }));
			Assert.That(inputs.Values.All(texture => texture == Texture2D.blackTexture), Is.True);
		}

		[Test]
		public void InstantEffectLiveParameterAddressIdentifiesCueAndParameter() {
			var address = LiveInstantEffectRenderer.ParameterAddress(7, "amount");

			Assert.That(LiveInstantEffectRenderer.TryParseParameterAddress(address, out var cueIndex, out var parameterId), Is.True);
			Assert.That(cueIndex, Is.EqualTo(7));
			Assert.That(parameterId, Is.EqualTo("amount"));
			Assert.That(LiveInstantEffectRenderer.LiveParameterCount, Is.EqualTo(8));
		}

		[Test]
		public void InstantEffectRendererChangesProgramPixelsWhenInvertCueFires() {
			var asset = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(
				"Assets/ShitDesigner/Scripts/Modules/Nodes/ShaderNodeManifest.asset");
			var entry = asset.BuildRuntimeManifest().Find("shitdesigner.shader.color.invert");
			var assetEntry = asset.Find(entry.TypeId.Value);
			var blurEntry = asset.BuildRuntimeManifest().Find("shitdesigner.shader.blur.box-blur");
			var blurAssetEntry = asset.Find(blurEntry.TypeId.Value);
			var definitions = new Dictionary<NodeTypeId, LiveProgramShaderDefinition> {
				{ entry.TypeId, new LiveProgramShaderDefinition(entry, assetEntry.Shader) },
				{ blurEntry.TypeId, new LiveProgramShaderDefinition(blurEntry, blurAssetEntry.Shader) }
			};
			var pool = new RenderTexturePool();
			var renderer = new LiveInstantEffectRenderer(definitions, pool, new LiveRenderSize(4, 4));
			var source = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGBHalf);
			var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
			var previous = RenderTexture.active;
			try {
				Assert.That(source.Create(), Is.True);
				RenderTexture.active = source;
				GL.Clear(true, true, new Color(.2f, .3f, .4f, 1f));
				Assert.That(renderer.TryAssign(0, entry.TypeId.Value, out var rejectionReason), Is.True, rejectionReason);
				var parameters = renderer.GetParameterDefinitions(0);
				Assert.That(parameters, Has.Length.LessThanOrEqualTo(LiveInstantEffectRenderer.LiveParameterCount));
				Assert.That(parameters.Single(parameter => parameter.Id.EndsWith("/amount")).Value, Is.EqualTo(1f));
				Assert.That(renderer.TryAssign(1, blurEntry.TypeId.Value, out rejectionReason), Is.True, rejectionReason);
				var blurParameters = renderer.GetParameterDefinitions(1);
				Assert.That(blurParameters.Single(parameter => parameter.Id.EndsWith("/radius")).Value, Is.EqualTo(1f));

				var output = renderer.Render(source, new[] { 1 }, 1UL, 0d);
				RenderTexture.active = output;
				readback.ReadPixels(new Rect(1f, 1f, 1f, 1f), 0, 0);
				readback.Apply(false, false);
				var pixel = readback.GetPixel(0, 0);

				Assert.That(output, Is.Not.SameAs(source));
				Assert.That(pixel.r, Is.EqualTo(.8f).Within(.03f));
				Assert.That(pixel.g, Is.EqualTo(.7f).Within(.03f));
				Assert.That(pixel.b, Is.EqualTo(.6f).Within(.03f));
			}
			finally {
				RenderTexture.active = previous;
				renderer.Dispose();
				pool.Dispose();
				source.Release();
				Object.DestroyImmediate(source);
				Object.DestroyImmediate(readback);
			}
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
		public void MainUiDoesNotDefineCueControls() {
			var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/ShitDesigner/Scenes/Main/MainUI.uxml");
			Assert.That(asset, Is.Not.Null);
			var root = new VisualElement();
			asset.CloneTree(root);

			Assert.That(root.Q<VisualElement>("patch-slot-controls"), Is.Null);
			Assert.That(root.Q<Button>("cue-patch-slot"), Is.Null);
			Assert.That(root.Q<Button>("launch-patch-slot"), Is.Null);
			Assert.That(root.Q<Button>("clear-patch-slot"), Is.Null);
		}

		[Test]
		public void MainUiDefinesGlobalTimeEasingToggle() {
			var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/ShitDesigner/Scenes/Main/MainUI.uxml");
			Assert.That(asset, Is.Not.Null);
			var root = new VisualElement();
			asset.CloneTree(root);

			var button = root.Q<Button>("time-easing-button");

			Assert.That(button, Is.Not.Null);
			Assert.That(button.text, Is.EqualTo("TIME EASE ON"));
			Assert.That(button.ClassListContains("tempo-alignment-button"), Is.True);
			Assert.That(button.ClassListContains("tempo-time-easing-button"), Is.True);
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
		public void MainUiDefinesTwoCueSlotsBelowOutputOne() {
			var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/ShitDesigner/Scenes/Main/MainUI.uxml");
			Assert.That(asset, Is.Not.Null);
			var root = new VisualElement();
			asset.CloneTree(root);

			var outputOne = root.Q<VisualElement>("program-monitor");
			var cueSlots = root.Q<VisualElement>("cue-slots");
			var slots = cueSlots.Query<VisualElement>(className: "cue-slot").ToList();

			Assert.That(cueSlots.parent, Is.SameAs(outputOne.parent));
			Assert.That(cueSlots.parent.IndexOf(cueSlots), Is.EqualTo(cueSlots.parent.IndexOf(outputOne) + 1));
			Assert.That(slots.Select(slot => slot.name), Is.EqualTo(new[] { "cue-slot-1", "cue-slot-2" }));
			Assert.That(slots.Select(slot => slot.Q<Label>()?.text), Is.EqualTo(new[] { "Cue Slot 1", "Cue Slot 2" }));
		}

		[Test]
		public void MainUiDefinesInstantEffectCueRowInKeyboardOrder() {
			var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/ShitDesigner/Scenes/Main/MainUI.uxml");
			Assert.That(asset, Is.Not.Null);
			var root = new VisualElement();
			asset.CloneTree(root);

			var cues = root.Q<VisualElement>("instant-effect-cues");
			var buttons = cues.Query<Button>(className: "instant-effect-cue-button").ToList();

			Assert.That(cues.parent.ClassListContains("preview-stack"), Is.True);
			Assert.That(cues.parent.IndexOf(cues), Is.EqualTo(cues.parent.IndexOf(root.Q<VisualElement>("sequencer-controls")) + 1));
			Assert.That(cues.Query<Label>().ToList(), Is.Empty);
			Assert.That(buttons.Select(button => button.name), Is.EqualTo(Enumerable.Range(1, ShitDesigner.Runtime.InstantEffectTriggerContract.TriggerCount)
				.Select(index => "instant-effect-cue-" + index)));
			Assert.That(buttons.Select(button => button.text), Is.EqualTo(new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" }));
		}

		[Test]
		public void MainUiDefinesSceneCatalogSidebarTabs() {
			var asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/ShitDesigner/Scenes/Main/MainUI.uxml");
			Assert.That(asset, Is.Not.Null);
			var root = new VisualElement();
			asset.CloneTree(root);

			var tabs = root.Query<Button>(className: "sidebar-tab").ToList();

			Assert.That(tabs.Select(tab => tab.name).ToArray(), Is.EqualTo(new[] { "main-tab", "overlay-tab", "effect-tab" }));
			Assert.That(tabs[0].ClassListContains("is-selected"), Is.True);
			var patchControls = root.Q<VisualElement>("patch-controls");
			Assert.That(root.Q<ScrollView>("main-patch-controls").parent, Is.SameAs(patchControls));
			Assert.That(root.Q<ScrollView>("overlay-patch-controls").parent, Is.SameAs(patchControls));
			Assert.That(root.Q<ScrollView>("effect-node-controls").parent, Is.SameAs(patchControls));
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
		public void ExternalProgramDisplayCanvas_FillsTheTargetWithoutStretching() {
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
				var aspectRatioFitter = image.GetComponent<AspectRatioFitter>();
				Assert.That(aspectRatioFitter, Is.Not.Null);
				Assert.That(aspectRatioFitter.aspectMode, Is.EqualTo(AspectRatioFitter.AspectMode.EnvelopeParent));
				Assert.That(aspectRatioFitter.aspectRatio, Is.EqualTo(1f).Within(0.0001f));
			}
			finally {
				source.Release();
				Object.DestroyImmediate(source);
				Object.DestroyImmediate(host);
			}
		}
	}
}
