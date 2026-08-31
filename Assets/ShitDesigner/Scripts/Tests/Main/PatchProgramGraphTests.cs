using System.Linq;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Media;
using ShitDesigner.Scene;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class PatchProgramGraphTests {
		[Test]
		public void Validate_AllowsGraphWithoutA3DSceneNode() {
			var graph = new PatchProgramGraph(
				"generator",
				new[] { new PatchGraphNode("generator", "shitdesigner.shader.generator") },
				new PatchGraphConnection[0]);

			var result = graph.Validate();

			Assert.That(result.IsSuccess, Is.True);
		}

		[Test]
		public void VideoTransportAcceptsHotCueStateParameters() {
			var transport = new LiveVideoTransportState(true, 12d, 2f, true);

			Assert.That(transport.TrySetParameter(VideoPlayerContract.PlayheadParameterId, ParameterValue.FromFloat(30f),
				3d, 120d, out var rejectionReason), Is.True, rejectionReason);
			Assert.That(transport.TrySetParameter(VideoPlayerContract.SpeedParameterId, ParameterValue.FromFloat(.5f),
				3d, 120d, out rejectionReason), Is.True, rejectionReason);
			Assert.That(transport.TrySetParameter(VideoPlayerContract.LoopParameterId, ParameterValue.FromBool(false),
				3d, 120d, out rejectionReason), Is.True, rejectionReason);
			Assert.That(transport.TrySetParameter(VideoPlayerContract.PlayingParameterId, ParameterValue.FromBool(false),
				3d, 120d, out rejectionReason), Is.True, rejectionReason);

			Assert.That(transport.Playing, Is.False);
			Assert.That(transport.PlayheadSeconds, Is.EqualTo(30d));
			Assert.That(transport.Speed, Is.EqualTo(.5f));
			Assert.That(transport.Loop, Is.False);
			Assert.That(transport.LogicalPosition(12d, 120d), Is.EqualTo(30d));
			Assert.That(transport.SeekPending, Is.True);
			Assert.That(transport.SettingsPending, Is.True);
		}

		[Test]
		public void VideoTransportRejectsInvalidHotCueValuesWithoutChangingItsState() {
			var transport = new LiveVideoTransportState(true, 4d, 1f, false);

			var accepted = transport.TrySetParameter(VideoPlayerContract.SpeedParameterId, ParameterValue.FromBool(true),
				2d, 60d, out var rejectionReason);

			Assert.That(accepted, Is.False);
			Assert.That(rejectionReason, Is.Not.Empty);
			Assert.That(transport.Speed, Is.EqualTo(1f));
			Assert.That(transport.LogicalPosition(2d, 60d), Is.EqualTo(6d));
		}

		[Test]
		public void VideoHotCueStateIsOwnedByAnIndependentPatchHotCue() {
			var hotCue = new PatchHotCue(new[] {
				new PatchGraphParameter("video_playing", ParameterValue.FromBool(true)),
				new PatchGraphParameter("video_playhead", ParameterValue.FromFloat(18.5f)),
				new PatchGraphParameter("video_speed", ParameterValue.FromFloat(1.25f)),
				new PatchGraphParameter("video_loop", ParameterValue.FromBool(false))
			});

			Assert.That(PatchDefinition.HotCueCount, Is.EqualTo(2));
			Assert.That(hotCue.Values.Single(value => value.Id == "video_playing").Value.AsBool(), Is.True);
			Assert.That(hotCue.Values.Single(value => value.Id == "video_playhead").Value.AsFloat(), Is.EqualTo(18.5f));
			Assert.That(hotCue.Values.Single(value => value.Id == "video_speed").Value.AsFloat(), Is.EqualTo(1.25f));
			Assert.That(hotCue.Values.Single(value => value.Id == "video_loop").Value.AsBool(), Is.False);
		}
	}
}
