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
				var document = root.GetComponent<UIDocument>();

				Assert.That(root.name, Is.EqualTo("Main Live Host"));
				Assert.That(host, Is.Not.Null);
				Assert.That(graph, Is.Not.Null);
				Assert.That(midi, Is.Not.Null);
				Assert.That(capability, Is.Not.Null);
				Assert.That(output, Is.Not.Null);
				Assert.That(ui, Is.Not.Null);
				Assert.That(document, Is.Not.Null);
				Assert.That(document.visualTreeAsset, Is.Not.Null);
				Assert.That(document.panelSettings, Is.Not.Null);
				Assert.That(graph.Patches.Length, Is.EqualTo(5));
				Assert.That(graph.Patches.All(definition => definition != null && !string.IsNullOrWhiteSpace(definition.Id) && definition.NodeGroups.Count > 0), Is.True);
				Assert.That(graph.Patches.Select(definition => definition.Id).Distinct().Count(), Is.EqualTo(5));
				Assert.That(graph.Patches.SelectMany(definition => definition.Nodes).All(node => node.Prefab.GetComponent<LiveSceneRoot>() != null), Is.True);
				var bpmShapes = graph.Patches.Single(definition => definition.Id == "bpm-shapes");
				var bpmShapesPrefab = bpmShapes.Nodes.Single().Prefab;
				Assert.That(bpmShapesPrefab.GetComponent("BpmShapeMotionScene"), Is.Not.Null);
				Assert.That(bpmShapesPrefab.GetComponentInChildren<Camera>().orthographic, Is.True);
				var stage = graph.Patches.Single(definition => definition.Id == "stage");
				var stagePrefab = stage.Nodes.Single().Prefab;
				Assert.That(stage.Parameters.Single().NodeId, Is.EqualTo(stage.Nodes.Single().Id));
				Assert.That(stage.Parameters.Single().ParameterId, Is.EqualTo(LiveGraphClockRateParameter.ParameterId));
				Assert.That(stagePrefab.GetComponent<LiveGraphClockRateParameter>(), Is.Not.Null);
				Assert.That(stagePrefab.GetComponent("BpmAnimatorSpeedController"), Is.Not.Null);
				var serializedGraph = new SerializedObject(graph);
				Assert.That(serializedGraph.FindProperty("_shaderManifest").objectReferenceValue, Is.Not.Null);
				Assert.That(graph.Patches.All(definition => definition.Flash != null && definition.Flash.Image != null), Is.True);

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
	}
}
