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
			var programOutput = Object.FindAnyObjectByType<MainLiveProgramOutput>();
			Assert.That(bootstrap, Is.Not.Null);
			Assert.That(input, Is.Not.Null);
			Assert.That(output, Is.Not.Null);
			Assert.That(programOutput, Is.Not.Null);
			Assert.That(host.activeInHierarchy, Is.True);
			Assert.That(host.transform.Find("UI/Top Bar Panel").gameObject.activeInHierarchy, Is.True);

			for (var frame = 0; frame < 30 && (output.CurrentFrame == null || programOutput.ConsumedFrameNumber == 0) && string.IsNullOrEmpty(bootstrap.LastError); frame++)
				yield return null;
			Assert.That(bootstrap.LastError, Is.Empty);
			Assert.That(bootstrap.IsRunning, Is.True);
			Assert.That(bootstrap.ActiveSceneIndex, Is.Zero);
			Assert.That(output.CurrentFrame, Is.Not.Null);
			Assert.That(output.CurrentFrame.NativeSurface, Is.SameAs(output.Target));
			Assert.That(programOutput.IsBound, Is.True);
			Assert.That(programOutput.SubmittedFrameNumber, Is.EqualTo(output.CurrentFrame.FrameNumber));
			Assert.That(programOutput.ConsumedFrameNumber, Is.GreaterThan(0));
			Assert.That(programOutput.ConsumedFrameNumber, Is.LessThanOrEqualTo(programOutput.SubmittedFrameNumber));

			input.SetSceneIndex(1, bootstrap.Scenes.Count);
			for (var frame = 0; frame < 30 && bootstrap.ActiveSceneIndex != 1 && string.IsNullOrEmpty(bootstrap.LastError); frame++)
				yield return null;
			Assert.That(bootstrap.LastError, Is.Empty);
			Assert.That(bootstrap.ActiveSceneIndex, Is.EqualTo(1));
			Assert.That(output.CurrentFrame.FrameNumber, Is.GreaterThan(1));
		}
	}
}
