using NUnit.Framework;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LiveBpmTapTests {
		[Test]
		public void TapUsesTheAverageOfTheFourMostRecentIntervals() {
			var tap = new LiveBpmTap();

			Assert.That(tap.TryTap(0d, out _), Is.False);
			Assert.That(tap.TryTap(0.5d, out var firstBpm), Is.True);
			Assert.That(firstBpm, Is.EqualTo(120f).Within(0.001f));
			Assert.That(tap.TryTap(1.5d, out var secondBpm), Is.True);
			Assert.That(secondBpm, Is.EqualTo(80f).Within(0.001f));
			Assert.That(tap.TryTap(2d, out var thirdBpm), Is.True);
			Assert.That(thirdBpm, Is.EqualTo(90f).Within(0.001f));
			Assert.That(tap.TryTap(3d, out var fourthBpm), Is.True);
			Assert.That(fourthBpm, Is.EqualTo(80f).Within(0.001f));
			Assert.That(tap.TryTap(3.5d, out var fifthBpm), Is.True);
			Assert.That(fifthBpm, Is.EqualTo(80f).Within(0.001f));
		}

		[Test]
		public void TapAfterTheTimeoutStartsANewMeasurement() {
			var tap = new LiveBpmTap();

			tap.TryTap(0d, out _);
			tap.TryTap(0.5d, out _);

			Assert.That(tap.TryTap(3d, out _), Is.False);
			Assert.That(tap.TryTap(3.75d, out var bpm), Is.True);
			Assert.That(bpm, Is.EqualTo(80f).Within(0.001f));
		}
	}
}
