using System.Collections.Generic;
using NUnit.Framework;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LiveParameterQueueTests {
		[Test]
		public void DrainPreservesAcceptanceOrderAcrossSources() {
			var queue = new LiveParameterQueue();
			var scene = queue.EnqueueSelectScene("scene-b");
			var parameter = queue.EnqueueSetParameter("scene-a", "motion", 0.75f);

			var requests = new List<LiveParameterRequest>();
			var drained = queue.Drain(requests);

			Assert.That(scene.Accepted, Is.True);
			Assert.That(parameter.Accepted, Is.True);
			Assert.That(parameter.SequenceNumber, Is.GreaterThan(scene.SequenceNumber));
			Assert.That(drained, Is.EqualTo(2));
			Assert.That(requests[0].Kind, Is.EqualTo(LiveParameterRequestKind.SelectScene));
			Assert.That(requests[0].SceneId, Is.EqualTo("scene-b"));
			Assert.That(requests[1].Kind, Is.EqualTo(LiveParameterRequestKind.SetParameter));
			Assert.That(requests[1].SceneId, Is.EqualTo("scene-a"));
			Assert.That(requests[1].ParameterId, Is.EqualTo("motion"));
			Assert.That(requests[1].Value, Is.EqualTo(0.75f));
			Assert.That(queue.Count, Is.Zero);
		}

		[Test]
		public void FlashTriggerQueuesForTheSelectedScene() {
			var queue = new LiveParameterQueue();

			var result = queue.EnqueueTriggerFlash("scene-a");

			var requests = new List<LiveParameterRequest>();
			queue.Drain(requests);
			Assert.That(result.Accepted, Is.True);
			Assert.That(requests, Has.Count.EqualTo(1));
			Assert.That(requests[0].Kind, Is.EqualTo(LiveParameterRequestKind.TriggerFlash));
			Assert.That(requests[0].SceneId, Is.EqualTo("scene-a"));
		}

		[Test]
		public void FullQueueRejectsNewRequestsWithoutAssigningASequence() {
			var queue = new LiveParameterQueue();
			for (var index = 0; index < LiveParameterQueue.Capacity; index++)
				Assert.That(queue.EnqueueSelectScene("scene-a").Accepted, Is.True);

			var rejected = queue.EnqueueSetParameter("scene-a", "motion", 0.5f);

			Assert.That(rejected.Accepted, Is.False);
			Assert.That(rejected.SequenceNumber, Is.Zero);
			Assert.That(rejected.RejectionReason, Is.Not.Empty);
			Assert.That(queue.Count, Is.EqualTo(LiveParameterQueue.Capacity));
		}

		[TestCase(null, "motion")]
		[TestCase("", "motion")]
		[TestCase("scene-a", null)]
		[TestCase("scene-a", "")]
		public void InvalidIdentifiersAreRejected(string sceneId, string parameterId) {
			var queue = new LiveParameterQueue();
			var result = queue.EnqueueSetParameter(sceneId, parameterId, 0f);

			Assert.That(result.Accepted, Is.False);
			Assert.That(result.SequenceNumber, Is.Zero);
		}
	}
}
