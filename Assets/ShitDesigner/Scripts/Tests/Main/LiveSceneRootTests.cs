using NUnit.Framework;
using UnityEngine;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LiveSceneRootTests {
		private GameObject _root;

		[TearDown]
		public void TearDown() {
			if (_root != null) Object.DestroyImmediate(_root);
		}

		[Test]
		public void AppliesOnlyPublishedParameters() {
			_root = new GameObject("Live Scene");
			var cameraObject = new GameObject("Camera");
			cameraObject.transform.SetParent(_root.transform, false);
			var camera = cameraObject.AddComponent<Camera>();
			camera.fieldOfView = 60f;
			var scene = _root.AddComponent<LiveSceneRoot>();
			scene.Initialize("scene-a");

			var scaleAccepted = scene.TrySetParameter(LiveSceneRoot.ScaleParameterId, 1f, out var scaleRejection);
			var unknownAccepted = scene.TrySetParameter("unknown", 0.5f, out var unknownRejection);

			Assert.That(scaleAccepted, Is.True);
			Assert.That(scaleRejection, Is.Empty);
			Assert.That(_root.transform.localScale, Is.EqualTo(Vector3.one * 1.25f));
			Assert.That(camera.fieldOfView, Is.EqualTo(75f));
			Assert.That(unknownAccepted, Is.False);
			Assert.That(unknownRejection, Is.Not.Empty);
		}
	}
}
