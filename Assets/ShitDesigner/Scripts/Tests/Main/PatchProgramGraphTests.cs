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
		public void VideoTransportRecallRestoresTheStateAuthoredByThePatch() {
			var transport = new LiveVideoTransportState(true, 12d, 2f, true);

			Assert.That(transport.TrySetParameter(VideoPlayerContract.PlayheadParameterId, ParameterValue.FromFloat(30f),
				3d, 120d, out var rejectionReason), Is.True, rejectionReason);
			Assert.That(transport.TrySetParameter(VideoPlayerContract.SpeedParameterId, ParameterValue.FromFloat(.5f),
				3d, 120d, out rejectionReason), Is.True, rejectionReason);
			Assert.That(transport.TrySetParameter(VideoPlayerContract.PlayingParameterId, ParameterValue.FromBool(false),
				5d, 120d, out rejectionReason), Is.True, rejectionReason);

			transport.RecallAuthoredState();

			Assert.That(transport.Playing, Is.True);
			Assert.That(transport.PlayheadSeconds, Is.EqualTo(12d));
			Assert.That(transport.Speed, Is.EqualTo(2f));
			Assert.That(transport.Loop, Is.True);
			Assert.That(transport.LogicalPosition(10d, 120d), Is.EqualTo(12d));
			Assert.That(transport.LogicalPosition(12d, 120d), Is.EqualTo(16d));
			Assert.That(transport.SeekPending, Is.True);
			Assert.That(transport.SettingsPending, Is.True);
		}

		[Test]
		public void VideoTransportRejectsInvalidLiveValuesWithoutChangingThePatchState() {
			var transport = new LiveVideoTransportState(true, 4d, 1f, false);

			var accepted = transport.TrySetParameter(VideoPlayerContract.SpeedParameterId, ParameterValue.FromBool(true),
				2d, 60d, out var rejectionReason);

			Assert.That(accepted, Is.False);
			Assert.That(rejectionReason, Is.Not.Empty);
			Assert.That(transport.Speed, Is.EqualTo(1f));
			Assert.That(transport.LogicalPosition(2d, 60d), Is.EqualTo(6d));
		}

		[Test]
		public void VideoHotCueStateIsOwnedByThePatchGraphNode() {
			var node = new PatchGraphNode("video", VideoPlayerContract.NodeTypeId, new[] {
				new PatchGraphParameter(VideoPlayerContract.PlayingParameterId, ParameterValue.FromBool(true)),
				new PatchGraphParameter(VideoPlayerContract.PlayheadParameterId, ParameterValue.FromFloat(18.5f)),
				new PatchGraphParameter(VideoPlayerContract.SpeedParameterId, ParameterValue.FromFloat(1.25f)),
				new PatchGraphParameter(VideoPlayerContract.LoopParameterId, ParameterValue.FromBool(false))
			});

			Assert.That(node.FindParameter(VideoPlayerContract.PlayingParameterId).Value.AsBool(), Is.True);
			Assert.That(node.FindParameter(VideoPlayerContract.PlayheadParameterId).Value.AsFloat(), Is.EqualTo(18.5f));
			Assert.That(node.FindParameter(VideoPlayerContract.SpeedParameterId).Value.AsFloat(), Is.EqualTo(1.25f));
			Assert.That(node.FindParameter(VideoPlayerContract.LoopParameterId).Value.AsBool(), Is.False);
		}
	}
}
