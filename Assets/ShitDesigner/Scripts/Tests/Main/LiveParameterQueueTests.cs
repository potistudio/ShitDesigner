using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Core;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LiveParameterQueueTests {
		[Test]
		public void DrainPreservesAcceptanceOrderAcrossSources() {
			var queue = new LiveParameterQueue();
			var preload = queue.EnqueuePreloadPatch("patch-b");
			var load = queue.EnqueueLoadPatch("patch-b");
			var parameter = queue.EnqueueSetParameter("patch-a", "motion", 0.75f);

			var requests = new List<LiveParameterRequest>();
			var drained = queue.Drain(requests);

			Assert.That(preload.Accepted, Is.True);
			Assert.That(load.Accepted, Is.True);
			Assert.That(parameter.Accepted, Is.True);
			Assert.That(parameter.SequenceNumber, Is.GreaterThan(load.SequenceNumber));
			Assert.That(drained, Is.EqualTo(3));
			Assert.That(requests[0].Kind, Is.EqualTo(LiveParameterRequestKind.PreloadPatch));
			Assert.That(requests[0].PatchId, Is.EqualTo("patch-b"));
			Assert.That(requests[1].Kind, Is.EqualTo(LiveParameterRequestKind.LoadPatch));
			Assert.That(requests[1].PatchId, Is.EqualTo("patch-b"));
			Assert.That(requests[2].Kind, Is.EqualTo(LiveParameterRequestKind.SetParameter));
			Assert.That(requests[2].PatchId, Is.EqualTo("patch-a"));
			Assert.That(requests[2].ParameterId, Is.EqualTo("motion"));
			Assert.That(requests[2].Value, Is.EqualTo(0.75f));
			Assert.That(queue.Count, Is.Zero);
		}

		[Test]
		public void TypedParameterValueIsPreserved() {
			var queue = new LiveParameterQueue();

			var result = queue.EnqueueSetParameter("patch-a", "tint", ParameterValue.FromColor(new ColorValue(.1f, .2f, .3f, 1f)));

			var requests = new List<LiveParameterRequest>();
			queue.Drain(requests);
			Assert.That(result.Accepted, Is.True);
			Assert.That(requests[0].ParameterValue, Is.EqualTo(ParameterValue.FromColor(new ColorValue(.1f, .2f, .3f, 1f))));
		}

		[Test]
		public void LaunchPatchQueuesAsOneAtomicRequest() {
			var queue = new LiveParameterQueue();

			var result = queue.EnqueueLaunchPatch("patch-b");

			var requests = new List<LiveParameterRequest>();
			queue.Drain(requests);
			Assert.That(result.Accepted, Is.True);
			Assert.That(requests, Has.Count.EqualTo(1));
			Assert.That(requests[0].Kind, Is.EqualTo(LiveParameterRequestKind.LaunchPatch));
			Assert.That(requests[0].PatchId, Is.EqualTo("patch-b"));
		}

		[Test]
		public void SetBpmQueuesAGlobalRequestWithoutAPatchId() {
			var queue = new LiveParameterQueue();

			var result = queue.EnqueueSetBpm(138f);

			var requests = new List<LiveParameterRequest>();
			queue.Drain(requests);
			Assert.That(result.Accepted, Is.True);
			Assert.That(requests, Has.Count.EqualTo(1));
			Assert.That(requests[0].Kind, Is.EqualTo(LiveParameterRequestKind.SetBpm));
			Assert.That(requests[0].PatchId, Is.Empty);
			Assert.That(requests[0].Value, Is.EqualTo(138f));
		}

		[Test]
		public void AlignBeatQueuesAGlobalRequestWithoutAPatchId() {
			var queue = new LiveParameterQueue();

			var result = queue.EnqueueAlignBeat();

			var requests = new List<LiveParameterRequest>();
			queue.Drain(requests);
			Assert.That(result.Accepted, Is.True);
			Assert.That(requests, Has.Count.EqualTo(1));
			Assert.That(requests[0].Kind, Is.EqualTo(LiveParameterRequestKind.AlignBeat));
			Assert.That(requests[0].PatchId, Is.Empty);
			Assert.That(requests[0].Value, Is.Zero);
		}

		[Test]
		public void SceneTimeJogQueuesAGlobalRequestWithoutAPatchId() {
			var queue = new LiveParameterQueue();

			var result = queue.EnqueueJogSceneTime(-.25f);

			var requests = new List<LiveParameterRequest>();
			queue.Drain(requests);
			Assert.That(result.Accepted, Is.True);
			Assert.That(requests, Has.Count.EqualTo(1));
			Assert.That(requests[0].Kind, Is.EqualTo(LiveParameterRequestKind.JogSceneTime));
			Assert.That(requests[0].PatchId, Is.Empty);
			Assert.That(requests[0].Value, Is.EqualTo(-.25f));
		}

		[Test]
		public void HotCueQueueAcceptsExactlyTwoGlobalSlots() {
			var queue = new LiveParameterQueue();

			Assert.That(queue.EnqueueRecallHotCue(-1).Accepted, Is.False);
			Assert.That(queue.EnqueueRecallHotCue(0).Accepted, Is.True);
			Assert.That(queue.EnqueueRecallHotCue(1).Accepted, Is.True);
			Assert.That(queue.EnqueueRecallHotCue(2).Accepted, Is.False);

			var requests = new List<LiveParameterRequest>();
			queue.Drain(requests);
			Assert.That(requests, Has.Count.EqualTo(2));
			Assert.That(requests, Has.All.Matches<LiveParameterRequest>(request =>
				request.Kind == LiveParameterRequestKind.RecallHotCue && request.PatchId == string.Empty));
			Assert.That(requests.Select(request => request.ParameterValue.AsInt()), Is.EqualTo(new[] { 0, 1 }));
		}

		[Test]
		public void SetTimeEasingEnabledQueuesAGlobalBooleanWithoutAPatchId() {
			var queue = new LiveParameterQueue();

			var result = queue.EnqueueSetTimeEasingEnabled(false);

			var requests = new List<LiveParameterRequest>();
			queue.Drain(requests);
			Assert.That(result.Accepted, Is.True);
			Assert.That(requests, Has.Count.EqualTo(1));
			Assert.That(requests[0].Kind, Is.EqualTo(LiveParameterRequestKind.SetTimeEasingEnabled));
			Assert.That(requests[0].PatchId, Is.Empty);
			Assert.That(requests[0].ParameterValue.Type, Is.EqualTo(ParameterType.Bool));
			Assert.That(requests[0].ParameterValue.AsBool(), Is.False);
		}

		[Test]
		public void MainCueControlsQueueGlobalRequestsWithoutPatchIds() {
			var queue = new LiveParameterQueue();

			var fader = queue.EnqueueSetMainCueFader(.75f);
			var toggle = queue.EnqueueToggleMainCue();

			var requests = new List<LiveParameterRequest>();
			queue.Drain(requests);
			Assert.That(fader.Accepted, Is.True);
			Assert.That(toggle.Accepted, Is.True);
			Assert.That(requests.Select(request => request.Kind), Is.EqualTo(new[] {
				LiveParameterRequestKind.SetMainCueFader,
				LiveParameterRequestKind.ToggleMainCue
			}));
			Assert.That(requests, Has.All.Matches<LiveParameterRequest>(request => request.PatchId == string.Empty));
			Assert.That(requests[0].Value, Is.EqualTo(.75f));
		}

		[Test]
		public void FullQueueRejectsNewRequestsWithoutAssigningASequence() {
			var queue = new LiveParameterQueue();
			for (var index = 0; index < LiveParameterQueue.Capacity; index++)
				Assert.That(queue.EnqueuePreloadPatch("patch-a").Accepted, Is.True);

			var rejected = queue.EnqueueSetParameter("patch-a", "motion", 0.5f);

			Assert.That(rejected.Accepted, Is.False);
			Assert.That(rejected.SequenceNumber, Is.Zero);
			Assert.That(rejected.RejectionReason, Is.Not.Empty);
			Assert.That(queue.Count, Is.EqualTo(LiveParameterQueue.Capacity));
		}

		[TestCase(null, "motion")]
		[TestCase("", "motion")]
		[TestCase("patch-a", null)]
		[TestCase("patch-a", "")]
		public void InvalidIdentifiersAreRejected(string patchId, string parameterId) {
			var queue = new LiveParameterQueue();
			var result = queue.EnqueueSetParameter(patchId, parameterId, 0f);

			Assert.That(result.Accepted, Is.False);
			Assert.That(result.SequenceNumber, Is.Zero);
		}
	}
}
