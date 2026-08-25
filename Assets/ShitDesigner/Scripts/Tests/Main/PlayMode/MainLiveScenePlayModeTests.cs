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

			for (var frame = 0; frame < 30 && output.CurrentFrame == null && string.IsNullOrEmpty(bootstrap.LastError); frame++)
				yield return null;
			Assert.That(bootstrap.LastError, Is.Empty);
			Assert.That(bootstrap.IsRunning, Is.True);
			Assert.That(bootstrap.ActiveSceneIndex, Is.Zero);
			Assert.That(output.CurrentFrame, Is.Not.Null);
			Assert.That(output.CurrentFrame.NativeSurface, Is.SameAs(output.Target));

			input.SetSceneIndex(1, bootstrap.Scenes.Count);
			for (var frame = 0; frame < 30 && bootstrap.ActiveSceneIndex != 1 && string.IsNullOrEmpty(bootstrap.LastError); frame++)
				yield return null;
			Assert.That(bootstrap.LastError, Is.Empty);
			Assert.That(bootstrap.ActiveSceneIndex, Is.EqualTo(1));
			Assert.That(output.CurrentFrame.FrameNumber, Is.GreaterThan(1));
		}
	}
}
