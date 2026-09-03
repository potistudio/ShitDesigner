using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Scene;
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

		[Test]
		public void HotCueTriggerPulsesWithinTheApplyingFrame() {
			_root = new GameObject("Live Trigger Scene");
			var parameter = _root.AddComponent<RecordingTriggerParameter>();
			var scene = _root.AddComponent<LiveSceneRoot>();
			scene.Initialize("trigger-scene");
			var publishedDefinition = new PatchParameter();
			SetField(publishedDefinition, "_id", "published-trigger");
			SetField(publishedDefinition, "_displayName", "Published Trigger");
			SetField(publishedDefinition, "_parameterId", "trigger");
			var published = new LivePublishedParameter(publishedDefinition, scene, parameter.Definition);

			Assert.That(published.TrySetHotCueParameter(ParameterValue.FromFloat(1f), out var setRejection), Is.True, setRejection);
			Assert.That(published.TryApplyResolvedValue(new BeatClockFrame(120f, .25d), out var applyRejection), Is.True, applyRejection);

			Assert.That(parameter.Values, Is.EqualTo(new[] { 1f, 0f }));
			Assert.That(published.ToDefinition().Value, Is.Zero);
		}

		[Test]
		public void RandomFieldOfViewTriggerChangesOnlyTheCameraFieldOfView() {
			_root = new GameObject("Live Random Field Of View");
			var cameraObject = new GameObject("Camera");
			cameraObject.transform.SetParent(_root.transform, false);
			var camera = cameraObject.AddComponent<Camera>();
			camera.fieldOfView = 1f;
			var startingCameraPosition = camera.transform.localPosition;
			var startingCameraRotation = camera.transform.localRotation;
			var trigger = _root.AddComponent<LiveRandomFieldOfViewTrigger>();
			SetField(trigger, "m_FieldOfViewRange", new Vector2(35f, 85f));
			var scene = _root.AddComponent<LiveSceneRoot>();
			scene.Initialize("random-field-of-view-scene");

			var accepted = scene.TrySetParameter(LiveRandomFieldOfViewTrigger.ParameterId, 1f, out var rejection);

			Assert.That(scene.IsTriggerParameter(LiveRandomFieldOfViewTrigger.ParameterId), Is.True);
			Assert.That(accepted, Is.True, rejection);
			Assert.That(camera.fieldOfView, Is.InRange(35f, 85f));
			Assert.That(camera.transform.localPosition, Is.EqualTo(startingCameraPosition));
			Assert.That(camera.transform.localRotation, Is.EqualTo(startingCameraRotation));
			var randomizedFieldOfView = camera.fieldOfView;
			Assert.That(scene.TrySetParameter(LiveRandomFieldOfViewTrigger.ParameterId, 0f, out rejection), Is.True, rejection);
			Assert.That(camera.fieldOfView, Is.EqualTo(randomizedFieldOfView));
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

		private sealed class RecordingTriggerParameter : MonoBehaviour, ILiveSceneParameter, ILiveSceneTriggerParameter {
			public List<float> Values { get; } = new List<float>();
			public LiveParameterDefinition Definition => new LiveParameterDefinition("trigger", "Trigger", 0f, 1f, 0f);

			public bool TrySetValue(float value, out string rejectionReason) {
				Values.Add(value);
				rejectionReason = string.Empty;
				return true;
			}
		}

		private static void SetField(object target, string fieldName, object value) {
			target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(target, value);
		}
	}
}
