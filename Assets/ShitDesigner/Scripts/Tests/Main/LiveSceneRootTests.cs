using System.Linq;
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
			_root.AddComponent<LiveGraphClockRateParameter>();
			_root.AddComponent<LiveUniformScaleParameter>();
			scene.Initialize("scene-a");

			var scaleAccepted = scene.TrySetParameter(LiveUniformScaleParameter.ParameterId, 1f, out var scaleRejection);
			var motionAccepted = scene.TrySetParameter(LiveGraphClockRateParameter.ParameterId, 0.75f, out var motionRejection);
			var unknownAccepted = scene.TrySetParameter("unknown", 0.5f, out var unknownRejection);

			Assert.That(scaleAccepted, Is.True);
			Assert.That(scaleRejection, Is.Empty);
			Assert.That(motionAccepted, Is.True);
			Assert.That(motionRejection, Is.Empty);
			Assert.That(scene.TimeScale, Is.EqualTo(1.5f));
			Assert.That(_root.transform.localScale, Is.EqualTo(Vector3.one * 1.25f));
			Assert.That(camera.fieldOfView, Is.EqualTo(75f));
			Assert.That(unknownAccepted, Is.False);
			Assert.That(unknownRejection, Is.Not.Empty);
		}

		[Test]
		public void DelegatesToAnAuthoredSceneSpecificParameter() {
			_root = new GameObject("Live Scene");
			var cameraObject = new GameObject("Camera");
			cameraObject.transform.SetParent(_root.transform, false);
			cameraObject.AddComponent<Camera>();
			var parameter = _root.AddComponent<RecordingParameter>();
			var scene = _root.AddComponent<LiveSceneRoot>();
			scene.Initialize("scene-a");

			var accepted = scene.TrySetParameter("scene-specific", 2.5f, out var rejection);

			Assert.That(accepted, Is.True);
			Assert.That(rejection, Is.Empty);
			Assert.That(parameter.Value, Is.EqualTo(2.5f));
			Assert.That(scene.GetParameterDefinitions().Single().Id, Is.EqualTo("scene-specific"));
		}

		private sealed class RecordingParameter : MonoBehaviour, ILiveSceneParameter {
			public float Value { get; private set; }
			public LiveParameterDefinition Definition => new LiveParameterDefinition("scene-specific", "Scene Specific", -5f, 5f, Value);

			public bool TrySetValue(float value, out string rejectionReason) {
				Value = value;
				rejectionReason = string.Empty;
				return true;
			}
		}
	}
}
