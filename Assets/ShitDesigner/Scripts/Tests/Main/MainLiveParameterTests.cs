using NUnit.Framework;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class MainLiveParameterTests {
		[Test]
		public void CommitPublishesOneImmutableFrameFromLatestQueuedValues() {
			var buffer = new MainLiveParameterBuffer();
			buffer.Enqueue(MainLiveParameterId.Scene, 0f);
			buffer.Enqueue(MainLiveParameterId.Scene, 1f);
			buffer.Enqueue(MainLiveParameterId.Motion, 0.75f);
			buffer.Enqueue(MainLiveParameterId.Scale, 0.25f);

			var frame = buffer.Commit(7, 2);

			Assert.That(frame.FrameNumber, Is.EqualTo(7));
			Assert.That(frame.SceneIndex, Is.EqualTo(1));
			Assert.That(frame.Motion, Is.EqualTo(0.75f));
			Assert.That(frame.Scale, Is.EqualTo(0.25f));
			Assert.That(buffer.PendingCount, Is.Zero);
		}

		[Test]
		public void CommitClampsExternalInputAndMapsAcrossAvailableScenes() {
			var buffer = new MainLiveParameterBuffer();
			buffer.Enqueue(MainLiveParameterId.Scene, 0.51f);
			buffer.Enqueue(MainLiveParameterId.Motion, 2f);
			buffer.Enqueue(MainLiveParameterId.Scale, float.NaN);

			var frame = buffer.Commit(1, 3);

			Assert.That(frame.SceneIndex, Is.EqualTo(1));
			Assert.That(frame.Motion, Is.EqualTo(1f));
			Assert.That(frame.Scale, Is.EqualTo(0f));
		}

		[Test]
		public void CommitRetainsValuesUntilAFollowingInputChangesThem() {
			var buffer = new MainLiveParameterBuffer();
			buffer.Enqueue(MainLiveParameterId.Scene, 1f);
			buffer.Enqueue(MainLiveParameterId.Motion, 0.2f);
			var first = buffer.Commit(1, 2);

			var second = buffer.Commit(2, 2);

			Assert.That(second.SceneIndex, Is.EqualTo(first.SceneIndex));
			Assert.That(second.Motion, Is.EqualTo(first.Motion));
			Assert.That(second.Scale, Is.EqualTo(first.Scale));
		}
	}
}
