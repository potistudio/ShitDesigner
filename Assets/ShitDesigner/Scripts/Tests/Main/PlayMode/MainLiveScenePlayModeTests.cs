using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ShitDesigner.Main.Tests {
	public sealed class MainLiveScenePlayModeTests {
		[UnityTest]
		public IEnumerator MainRendersAndSwitchesItsStandaloneSceneOutput() {
			SceneManager.LoadScene("Main", LoadSceneMode.Single);
			yield return null;

			var host = SceneManager.GetActiveScene().GetRootGameObjects().Single(root => root.name == "Host");
			var bootstrap = Object.FindAnyObjectByType<MainLiveSceneBootstrap>();
			var input = Object.FindAnyObjectByType<MainLiveInput>();
			var output = Object.FindAnyObjectByType<MainLiveOutput>();
			Assert.That(bootstrap, Is.Not.Null);
			Assert.That(input, Is.Not.Null);
			Assert.That(output, Is.Not.Null);
			Assert.That(host.activeInHierarchy, Is.True);
			Assert.That(host.transform.Find("UI/Top Bar Panel").gameObject.activeInHierarchy, Is.True);

			for (var frame = 0; frame < 30 && (output.CurrentFrame == null || output.ConsumedFrameNumber == 0) && string.IsNullOrEmpty(bootstrap.LastError); frame++)
				yield return null;
			Assert.That(bootstrap.LastError, Is.Empty);
			Assert.That(bootstrap.IsRunning, Is.True);
			Assert.That(bootstrap.ActiveSceneIndex, Is.Zero);
			Assert.That(output.CurrentFrame, Is.Not.Null);
			Assert.That(output.CurrentFrame.NativeSurface, Is.SameAs(output.Target));
			Assert.That(output.IsBound, Is.True);
			Assert.That(output.SubmittedFrameNumber, Is.EqualTo(output.CurrentFrame.FrameNumber));
			Assert.That(output.ConsumedFrameNumber, Is.GreaterThan(0));
			Assert.That(output.ConsumedFrameNumber, Is.LessThanOrEqualTo(output.SubmittedFrameNumber));
			var hasVisiblePixels = false;
			for (var frame = 0; frame < 30 && !hasVisiblePixels; frame++) {
				hasVisiblePixels = HasVisiblePixels(output.Target);
				if (!hasVisiblePixels) yield return null;
			}
			Assert.That(hasVisiblePixels, Is.True, "Scene 1 output remained black.");
			var flythrough = Object.FindAnyObjectByType<ShitDesigner.Scene.CylindricalObjectFlythrough>();
			Assert.That(flythrough, Is.Not.Null);
			Assert.That(flythrough.GeneratedObjectCount, Is.GreaterThan(0));
			Assert.That(flythrough.GetComponentsInChildren<Transform>(true).All(child => child.gameObject.layer == flythrough.gameObject.layer), Is.True);

			input.SetSceneIndex(1, bootstrap.Scenes.Count);
			for (var frame = 0; frame < 30 && bootstrap.ActiveSceneIndex != 1 && string.IsNullOrEmpty(bootstrap.LastError); frame++)
				yield return null;
			Assert.That(bootstrap.LastError, Is.Empty);
			Assert.That(bootstrap.ActiveSceneIndex, Is.EqualTo(1));
			Assert.That(output.CurrentFrame.FrameNumber, Is.GreaterThan(1));
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
