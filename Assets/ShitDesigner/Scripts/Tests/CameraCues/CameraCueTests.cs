using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Scene;
using ShitDesigner.Stage;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools.Utils;
using UnityEngine.Video;

namespace ShitDesigner.CameraCues.Tests {
	[TestFixture]
	public sealed class CameraCueTests {
		[Test]
		public void StageAssetsWireBothHotCuesToTheCameraDirector() {
			var patch = AssetDatabase.LoadAssetAtPath<PatchDefinition>(
				"Assets/ShitDesigner/Scenes/Stage/Stage Patch Definition.asset");
			var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ShitDesigner/Scenes/Stage/Stage.prefab");

			Assert.That(patch, Is.Not.Null);
			Assert.That(patch.Validate().IsSuccess, Is.True);
			var cue1 = patch.GetHotCue(0).ConfiguredValues.Single();
			var cue2 = patch.GetHotCue(1).ConfiguredValues.Single();
			Assert.That(cue1.NodeId, Is.EqualTo("scene"));
			Assert.That(cue1.Id, Is.EqualTo(StageCameraDirector.Cue1ParameterId));
			Assert.That(cue1.Value.AsFloat(), Is.EqualTo(1f));
			Assert.That(cue2.NodeId, Is.EqualTo("scene"));
			Assert.That(cue2.Id, Is.EqualTo(StageCameraDirector.Cue2ParameterId));
			Assert.That(cue2.Value.AsFloat(), Is.EqualTo(1f));
			Assert.That(prefab, Is.Not.Null);
			var director = prefab.GetComponent<StageCameraDirector>();
			Assert.That(director, Is.Not.Null);
			Assert.That(director.LiveParameters, Has.Count.EqualTo(StageCameraDirector.CueCount));
			Assert.That(director.LiveParameters[0].Definition.Id, Is.EqualTo(StageCameraDirector.Cue1ParameterId));
			Assert.That(director.LiveParameters[1].Definition.Id, Is.EqualTo(StageCameraDirector.Cue2ParameterId));
			var cues = (StageCameraCueDefinition[])GetField(typeof(StageCameraDirector), "m_Cues").GetValue(director);
			Assert.That(cues[0].ControlsVideoPlayhead, Is.True);
			Assert.That(cues[0].VideoPlayheadSeconds, Is.Zero);
			Assert.That(cues[1].ControlsVideoPlayhead, Is.True);
			Assert.That(cues[1].VideoPlayheadSeconds, Is.EqualTo(120f));
			var videoPlayer = (VideoPlayer)GetField(typeof(StageCameraDirector), "m_VideoPlayer").GetValue(director);
			var outputTexture = (RenderTexture)GetField(typeof(StageCameraDirector), "m_VideoOutputTexture").GetValue(director);
			Assert.That(videoPlayer, Is.Not.Null);
			Assert.That(videoPlayer.renderMode, Is.EqualTo(VideoRenderMode.RenderTexture));
			Assert.That(videoPlayer.sendFrameReadyEvents, Is.True);
			Assert.That(videoPlayer.targetTexture, Is.Not.Null.And.Not.SameAs(outputTexture));
			Assert.That(outputTexture, Is.Not.Null);
		}

		[Test]
		public void HotCueResolvesAPublishedSceneParameterWithoutAShadowGraphParameter() {
			var patch = ScriptableObject.CreateInstance<PatchDefinition>();
			try {
				SetField(patch, "_programGraph", new PatchProgramGraph("scene", new[] {
					new PatchGraphNode("scene", PatchGraphNode.Scene3DTypeId)
				}, new PatchGraphConnection[0]));
				var parameter = new PatchParameter();
				SetField(parameter, "_id", "camera_cue_1");
				SetField(parameter, "_displayName", "Camera Cue 1");
				SetField(parameter, "_nodeId", "scene");
				SetField(parameter, "_parameterId", "camera_cue_1");
				SetField(patch, "_parameters", new List<PatchParameter> { parameter });
				var value = new PatchGraphParameter("camera_cue_1", ParameterValue.FromFloat(1f), "scene");

				Assert.That(patch.TryResolveHotCueTarget(value, out var resolvedNode, out var expectedType), Is.True);
				Assert.That(resolvedNode.Id, Is.EqualTo("scene"));
				Assert.That(expectedType, Is.EqualTo(ParameterType.Float));
			}
			finally {
				Object.DestroyImmediate(patch);
			}
		}

		[Test]
		public void HotCueBlendUsesAbsoluteBeatsAndHoldsItsFinalPose() {
			var root = new GameObject("Stage Camera Director Test");
			try {
				var target = new GameObject("Camera Target").transform;
				target.SetParent(root.transform, false);
				target.localPosition = new Vector3(0f, 1f, 0f);
				var cameraObject = new GameObject("Main Camera");
				cameraObject.transform.SetParent(root.transform, false);
				cameraObject.transform.localPosition = new Vector3(-4f, 2f, -12f);
				var camera = cameraObject.AddComponent<Camera>();
				camera.fieldOfView = 60f;
				var randomCamera = root.AddComponent<StageRandomCamera>();
				var director = root.AddComponent<StageCameraDirector>();
				ConfigureCue(director, 0, new Vector3(0f, 3f, -8f), 4f, 32f, StageCameraCueCompletion.Hold);
				director.ActivateScene();
				director.SetBpmClock(new BeatClockFrame(120f, 10d));

				Assert.That(director.TriggerCue(0, out var rejectionReason), Is.True, rejectionReason);
				director.SetBpmClock(new BeatClockFrame(120f, 12d));

				Assert.That(camera.transform.localPosition, Is.EqualTo(new Vector3(-2f, 2.5f, -10f)).Using(Vector3ComparerWithEqualsOperator.Instance));
				Assert.That(camera.fieldOfView, Is.EqualTo(46f).Within(.001f));
				Assert.That(director.IsCuePlaying, Is.True);

				director.SetBpmClock(new BeatClockFrame(120f, 14d));
				var heldPosition = camera.transform.localPosition;
				randomCamera.SetGraphClockDriven(true);
				randomCamera.AdvanceGraphClock(1d);

				Assert.That(director.IsCuePlaying, Is.False);
				Assert.That(director.ActiveCueIndex, Is.Zero);
				Assert.That(camera.transform.localPosition, Is.EqualTo(new Vector3(0f, 3f, -8f)).Using(Vector3ComparerWithEqualsOperator.Instance));
				Assert.That(camera.transform.localPosition, Is.EqualTo(heldPosition).Using(Vector3ComparerWithEqualsOperator.Instance));
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void RecallingAnotherHotCueInterruptsAndCanResumeRandomDrift() {
			var root = new GameObject("Stage Camera Cue Interrupt Test");
			try {
				var cameraObject = new GameObject("Main Camera");
				cameraObject.transform.SetParent(root.transform, false);
				cameraObject.AddComponent<Camera>();
				root.AddComponent<StageRandomCamera>();
				var director = root.AddComponent<StageCameraDirector>();
				ConfigureCue(director, 0, new Vector3(-2f, 2f, -8f), 4f, 35f, StageCameraCueCompletion.Hold);
				ConfigureCue(director, 1, new Vector3(3f, 3f, -10f), 2f, 45f, StageCameraCueCompletion.ResumeRandomDrift);
				director.ActivateScene();
				director.SetBpmClock(new BeatClockFrame(120f, 20d));

				Assert.That(director.LiveParameters[0].TrySetValue(1f, out var firstRejection), Is.True, firstRejection);
				director.SetBpmClock(new BeatClockFrame(120f, 21d));
				var interruptedPosition = cameraObject.transform.localPosition;
				Assert.That(director.LiveParameters[1].TrySetValue(1f, out var secondRejection), Is.True, secondRejection);
				director.SetBpmClock(new BeatClockFrame(120f, 22d));

				Assert.That(cameraObject.transform.localPosition, Is.EqualTo(Vector3.Lerp(interruptedPosition,
					new Vector3(3f, 3f, -10f), .5f)).Using(Vector3ComparerWithEqualsOperator.Instance));
				director.SetBpmClock(new BeatClockFrame(120f, 23d));

				Assert.That(director.IsCuePlaying, Is.False);
				Assert.That(director.ActiveCueIndex, Is.EqualTo(-1));
				Assert.That(cameraObject.transform.localPosition, Is.EqualTo(new Vector3(3f, 3f, -10f)).Using(Vector3ComparerWithEqualsOperator.Instance));
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void CameraCueQueuesItsVideoPlayheadUntilThePlayerIsPrepared() {
			var root = new GameObject("Stage Camera Video Cue Test");
			try {
				var cameraObject = new GameObject("Main Camera");
				cameraObject.transform.SetParent(root.transform, false);
				cameraObject.AddComponent<Camera>();
				var videoObject = new GameObject("Video Player");
				videoObject.transform.SetParent(root.transform, false);
				videoObject.AddComponent<VideoPlayer>();
				var director = root.AddComponent<StageCameraDirector>();
				ConfigureCue(director, 0, Vector3.zero, 4f, 45f, StageCameraCueCompletion.Hold, true, 37.5f);
				director.ActivateScene();

				Assert.That(director.TriggerCue(0, out var rejectionReason), Is.True, rejectionReason);
				Assert.That(GetField(typeof(StageCameraDirector), "m_VideoSeekPending").GetValue(director), Is.True);
				Assert.That(GetField(typeof(StageCameraDirector), "m_PendingVideoPlayheadSeconds").GetValue(director), Is.EqualTo(37.5d));
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void BpmClockScalesLedVideoPlaybackFromItsReferenceTempo() {
			var root = new GameObject("Stage LED Video BPM Test");
			try {
				var videoObject = new GameObject("Video Player");
				videoObject.transform.SetParent(root.transform, false);
				var videoPlayer = videoObject.AddComponent<VideoPlayer>();
				var director = root.AddComponent<StageCameraDirector>();
				SetField(director, "m_VideoReferenceBpm", 145f);

				director.SetBpmClock(new BeatClockFrame(72.5f, 0d));

				Assert.That(videoPlayer.playbackSpeed, Is.EqualTo(.5f).Within(.0001f));
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}

		private static void ConfigureCue(StageCameraDirector director, int cueIndex, Vector3 position, float durationBeats,
			float fieldOfView, StageCameraCueCompletion completion, bool controlVideoPlayhead = false, float videoPlayheadSeconds = 0f) {
			var cues = (StageCameraCueDefinition[])GetField(typeof(StageCameraDirector), "m_Cues").GetValue(director);
			var cue = cues[cueIndex];
			SetField(cue, "m_Motion", StageCameraCueMotion.Blend);
			SetField(cue, "m_LocalPosition", position);
			SetField(cue, "m_DurationBeats", durationBeats);
			SetField(cue, "m_Easing", AnimationCurve.Linear(0f, 0f, 1f, 1f));
			SetField(cue, "m_FieldOfView", fieldOfView);
			SetField(cue, "m_ControlVideoPlayhead", controlVideoPlayhead);
			SetField(cue, "m_VideoPlayheadSeconds", videoPlayheadSeconds);
			SetField(cue, "m_Completion", completion);
		}

		private static void SetField(object target, string name, object value)
			=> GetField(target.GetType(), name).SetValue(target, value);

		private static FieldInfo GetField(System.Type type, string name)
			=> type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
	}
}
