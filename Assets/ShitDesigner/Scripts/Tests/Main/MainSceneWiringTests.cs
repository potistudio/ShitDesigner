using System.Linq;
using NUnit.Framework;
using ShitDesigner.Input;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class MainSceneWiringTests {
		[Test]
		public void MainKeepsHostUiAlongsideTheStandaloneLiveRuntime() {
			var scene = EditorSceneManager.OpenScene("Assets/ShitDesigner/Scenes/Main/Main.unity", OpenSceneMode.Additive);
			try {
				var mainRoot = scene.GetRootGameObjects().Single(root => root.name == "Main");
				var legacyHost = scene.GetRootGameObjects().Single(root => root.name == "Host");
				var liveRoot = scene.GetRootGameObjects().Single(root => root.name == "Main Live Runtime");
				var bootstrap = liveRoot.GetComponent<MainLiveSceneBootstrap>();
				var input = liveRoot.GetComponent<MainLiveInput>();
				var midiManager = liveRoot.GetComponent<MidiInputManager>();
				var midiInput = liveRoot.GetComponent<MainLiveMidiInput>();
				var output = liveRoot.GetComponent<MainLiveOutput>();

				Assert.That(legacyHost.activeSelf, Is.True, "The existing Host must remain active because it owns the runtime UI composition.");
				Assert.That(legacyHost.transform.Find("UI/Top Bar Panel").gameObject.activeInHierarchy, Is.True);
				Assert.That(bootstrap, Is.Not.Null);
				Assert.That(input, Is.Not.Null);
				Assert.That(midiManager, Is.Not.Null);
				Assert.That(midiInput, Is.Not.Null);
				Assert.That(output, Is.Not.Null);
				Assert.That(bootstrap.Scenes.Count, Is.EqualTo(2));
				Assert.That(bootstrap.Scenes.All(definition => definition != null && definition.Prefab != null), Is.True);
				Assert.That(bootstrap.Scenes.Select(definition => definition.Prefab).Distinct().Count(), Is.EqualTo(2));

				var serializedBootstrap = new SerializedObject(bootstrap);
				Assert.That(serializedBootstrap.FindProperty("_input").objectReferenceValue, Is.SameAs(input));
				Assert.That(serializedBootstrap.FindProperty("_midiInput").objectReferenceValue, Is.SameAs(midiInput));
				Assert.That(serializedBootstrap.FindProperty("_output").objectReferenceValue, Is.SameAs(output));
				var serializedMidiInput = new SerializedObject(midiInput);
				Assert.That(serializedMidiInput.FindProperty("_manager").objectReferenceValue, Is.SameAs(midiManager));
				var applicationHost = legacyHost.GetComponents<MonoBehaviour>().Single(component => component.GetType().Name == "ApplicationHost");
				var serializedApplicationHost = new SerializedObject(applicationHost);
				Assert.That(serializedApplicationHost.FindProperty("m_MidiInputManager").objectReferenceValue, Is.SameAs(midiManager));
				var externalOutput = mainRoot.GetComponents<MonoBehaviour>().Single(component => component.GetType().Name == "SimpleExternalDisplayOutput");
				var serializedExternalOutput = new SerializedObject(externalOutput);
				Assert.That(serializedExternalOutput.FindProperty("_displayNumber").intValue, Is.EqualTo(2));
				Assert.That(serializedExternalOutput.FindProperty("_activateOnStart").boolValue, Is.True);
				Assert.That(serializedExternalOutput.FindProperty("_outputCamera").objectReferenceValue, Is.Not.Null);
				var serializedOutput = new SerializedObject(output);
				Assert.That(serializedOutput.FindProperty("_targetRenderer").objectReferenceValue, Is.Not.Null);
			}
			finally { EditorSceneManager.CloseScene(scene, removeScene: true); }
		}

		[Test]
		public void MainIsTheFirstEnabledBuildScene() {
			var first = EditorBuildSettings.scenes.First(scene => scene.enabled);
			Assert.That(first.path, Is.EqualTo("Assets/ShitDesigner/Scenes/Main/Main.unity"));
		}

		[Test]
		public void OutputPublishesTheExistingRuntimeImageFrameContract() {
			var gameObject = GameObject.CreatePrimitive(PrimitiveType.Quad);
			try {
				var output = gameObject.AddComponent<MainLiveOutput>();
				var serialized = new SerializedObject(output);
				serialized.FindProperty("_targetRenderer").objectReferenceValue = gameObject.GetComponent<Renderer>();
				serialized.FindProperty("_width").intValue = 32;
				serialized.FindProperty("_height").intValue = 18;
				serialized.ApplyModifiedPropertiesWithoutUndo();

				Assert.That(output.Initialize(), Is.True);
				output.Present(4);

				Assert.That(output.CurrentFrame, Is.Not.Null);
				Assert.That(output.CurrentFrame.FrameNumber, Is.EqualTo(4));
				Assert.That(output.CurrentFrame.Width, Is.EqualTo(32));
				Assert.That(output.CurrentFrame.Height, Is.EqualTo(18));
				Assert.That(output.CurrentFrame.NativeSurface, Is.SameAs(output.Target));
				Assert.That(output.CurrentFrame.LeaseId, Is.Not.Zero);
				output.Dispose();
			}
			finally { Object.DestroyImmediate(gameObject); }
		}
	}
}
