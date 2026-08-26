using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.TestTools;

namespace ShitDesigner.Main.Tests {
	public sealed class MainLiveScenePlayModeTests {
		[UnityTest]
		public IEnumerator MainBootsRendersAndSwitchesItsFixedLiveGraph() {
			SceneManager.LoadScene("Main", LoadSceneMode.Single);
			yield return null;

			var host = Object.FindAnyObjectByType<ApplicationLiveHost>();
			Assert.That(host, Is.Not.Null);
			for (var frame = 0; frame < 60 && (host.State != ApplicationLiveHostState.Running || host.ReadModel == null || host.ReadModel.ProgramFrameNumber == 0); frame++)
				yield return null;

			Assert.That(host.State, Is.EqualTo(ApplicationLiveHostState.Running), host.LastDiagnostic);
			Assert.That(host.ReadModel, Is.Not.Null);
			Assert.That(host.ReadModel.Scenes.Count, Is.EqualTo(2));
			Assert.That(host.ReadModel.ProgramTexture, Is.Not.Null);
			Assert.That(host.ReadModel.ProgramTexture.width, Is.EqualTo(1920));
			Assert.That(host.ReadModel.ProgramTexture.height, Is.EqualTo(1080));
			Assert.That(host.ReadModel.ProgramTexture.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
			Assert.That(host.ReadModel.ProgramFrameNumber, Is.GreaterThan(0));
			var runtime = (LiveGraphRuntime)typeof(ApplicationLiveHost).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
			Assert.That(runtime.CurrentFrames.Count, Is.EqualTo(1));
			Assert.That(runtime.CurrentFrames[0].Texture, Is.SameAs(host.ReadModel.ProgramTexture));
			Assert.That(HasVisiblePixels(host.ReadModel.ProgramTexture), Is.True);

			var previousScene = host.ReadModel.SelectedSceneId;
			var nextScene = host.ReadModel.Scenes.Single(scene => scene.Id != previousScene);
			var enqueue = host.ParameterQueue.EnqueueSelectScene(nextScene.Id);
			Assert.That(enqueue.Accepted, Is.True);
			for (var frame = 0; frame < 60 && host.ReadModel.SelectedSceneId != nextScene.Id; frame++) yield return null;

			Assert.That(host.ReadModel.SelectedSceneId, Is.EqualTo(nextScene.Id));
			Assert.That(host.ReadModel.RequestResults.Any(result => result.SequenceNumber == enqueue.SequenceNumber && result.Applied), Is.True);
			Assert.That(host.ReadModel.ProgramFrameNumber, Is.GreaterThan(1));
			var parameter = host.ParameterQueue.EnqueueSetParameter(nextScene.Id, "scale", 1f);
			for (var frame = 0; frame < 60 && !host.ReadModel.RequestResults.Any(result => result.SequenceNumber == parameter.SequenceNumber); frame++) yield return null;
			Assert.That(host.ReadModel.RequestResults.Any(result => result.SequenceNumber == parameter.SequenceNumber && result.Applied), Is.True);
			Assert.That(host.ReadModel.Parameters.Single(item => item.Id == "scale").Value, Is.EqualTo(1f));
			var ui = host.GetComponent<UIDocument>().rootVisualElement;
			Assert.That(ui.Q<VisualElement>("parameter-channel-scale"), Is.Not.Null);
			Assert.That(ui.Q<Slider>("parameter-scale").direction, Is.EqualTo(SliderDirection.Vertical));
			Assert.That(ui.Q<Label>("parameter-value-scale").text, Is.EqualTo("1.00"));

			var midi = (Component)typeof(ApplicationLiveHost).GetField("_midiInputManager", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
			host.Shutdown();
			Assert.That(host.State, Is.EqualTo(ApplicationLiveHostState.Offline));
			Assert.That((bool)midi.GetType().GetProperty("IsOpen")?.GetValue(midi), Is.False);
		}

		private static bool HasVisiblePixels(RenderTexture source) {
			var sample = RenderTexture.GetTemporary(64, 36, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
			var texture = new Texture2D(64, 36, TextureFormat.RGB24, false, true);
			var previous = RenderTexture.active;
			try {
				Graphics.Blit(source, sample);
				RenderTexture.active = sample;
				texture.ReadPixels(new Rect(0f, 0f, sample.width, sample.height), 0, 0);
				texture.Apply();
				return texture.GetPixels().Any(color => color.maxColorComponent > 0.01f);
			}
			finally {
				RenderTexture.active = previous;
				Object.Destroy(texture);
				RenderTexture.ReleaseTemporary(sample);
			}
		}
	}
}
