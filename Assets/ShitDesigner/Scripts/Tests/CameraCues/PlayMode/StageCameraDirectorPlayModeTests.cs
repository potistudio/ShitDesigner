using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using ShitDesigner.Stage;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Video;

namespace ShitDesigner.CameraCues.Tests {
	public sealed class StageCameraDirectorPlayModeTests {
		[UnityTest]
		public IEnumerator VideoCuePausesUntilSeekCompletesThenResumesPlayback() {
			var root = new GameObject("Stage Camera Video Seek Test");
			try {
				var cameraObject = new GameObject("Main Camera");
				cameraObject.transform.SetParent(root.transform, false);
				cameraObject.AddComponent<Camera>();

				var videoObject = new GameObject("Video Player");
				videoObject.transform.SetParent(root.transform, false);
				var videoPlayer = videoObject.AddComponent<VideoPlayer>();
				videoPlayer.playOnAwake = false;
				videoPlayer.source = VideoSource.Url;
				videoPlayer.url = Path.Combine(Application.dataPath,
					"ShitDesigner/Scripts/Tests/Media/Fixtures/h264-audio.mp4");
				videoPlayer.renderMode = VideoRenderMode.APIOnly;
				videoPlayer.audioOutputMode = VideoAudioOutputMode.None;
				videoPlayer.isLooping = true;

				var director = root.AddComponent<StageCameraDirector>();
				ConfigureVideoCue(director, .5f);
				director.ActivateScene();

				string videoError = null;
				videoPlayer.errorReceived += (_, message) => videoError = message;
				videoPlayer.Prepare();
				var prepareDeadline = Time.realtimeSinceStartup + 10f;
				while (!videoPlayer.isPrepared && videoError == null && Time.realtimeSinceStartup < prepareDeadline)
					yield return null;
				Assert.That(videoError, Is.Null);
				Assert.That(videoPlayer.isPrepared, Is.True, "The H.264 fixture did not prepare within 10 seconds.");

				videoPlayer.Play();
				var playDeadline = Time.realtimeSinceStartup + 2f;
				while (!videoPlayer.isPlaying && Time.realtimeSinceStartup < playDeadline)
					yield return null;
				Assert.That(videoPlayer.isPlaying, Is.True);
				var firstFrameDeadline = Time.realtimeSinceStartup + 10f;
				while (videoPlayer.texture == null && videoError == null && Time.realtimeSinceStartup < firstFrameDeadline)
					yield return null;
				Assert.That(videoError, Is.Null);
				Assert.That(videoPlayer.texture, Is.Not.Null,
					"Normal playback must decode a frame before the seek-race regression is exercised.");

				var targetFrameReady = false;
				var resumedInsideFrameReady = false;
				var completedTime = double.NaN;
				var observedFrames = new List<string>();
				videoPlayer.frameReady += (source, frameIndex) => {
					observedFrames.Add($"time={source.time:R}, frame={frameIndex}");
					if (System.Math.Abs(source.time - .5d) > .05d)
						return;
					targetFrameReady = true;
					resumedInsideFrameReady = source.isPlaying;
					completedTime = source.time;
				};

				Assert.That(director.TriggerCue(0, out var rejectionReason), Is.True, rejectionReason);
				Assert.That(videoPlayer.isPlaying, Is.False,
					"Normal playback must remain paused while Unity completes the asynchronous seek.");

				var seekDeadline = Time.realtimeSinceStartup + 5f;
				while (!targetFrameReady && videoError == null && Time.realtimeSinceStartup < seekDeadline)
					yield return null;
				Assert.That(videoError, Is.Null);
				Assert.That(targetFrameReady, Is.True,
					"The target video frame was not displayed within 5 seconds. Observed: " + string.Join("; ", observedFrames));
				Assert.That(completedTime, Is.EqualTo(.5d).Within(.05d));
				Assert.That(resumedInsideFrameReady, Is.False,
					"Playback must not resume reentrantly from inside VideoPlayer.frameReady.");

				var resumeDeadline = Time.realtimeSinceStartup + 2f;
				while (!videoPlayer.isPlaying && Time.realtimeSinceStartup < resumeDeadline)
					yield return null;
				Assert.That(videoPlayer.isPlaying, Is.True,
					"A cue triggered during playback must resume playback after the seek completes.");
			}
			finally {
				Object.Destroy(root);
			}
		}

		private static void ConfigureVideoCue(StageCameraDirector director, float playheadSeconds) {
			var cues = (StageCameraCueDefinition[])GetField(typeof(StageCameraDirector), "m_Cues").GetValue(director);
			SetField(cues[0], "m_Motion", StageCameraCueMotion.Cut);
			SetField(cues[0], "m_ControlVideoPlayhead", true);
			SetField(cues[0], "m_VideoPlayheadSeconds", playheadSeconds);
		}

		private static void SetField(object target, string name, object value)
			=> GetField(target.GetType(), name).SetValue(target, value);

		private static FieldInfo GetField(System.Type type, string name)
			=> type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
	}
}
