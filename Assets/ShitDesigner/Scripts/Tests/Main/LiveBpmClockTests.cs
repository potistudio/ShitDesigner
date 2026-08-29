using NUnit.Framework;
using ShitDesigner.Rendering;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LiveBpmClockTests {
		[Test]
		public void ChangingBpmPreservesTheAccumulatedGlobalBeat() {
			var clock = new LiveBpmClock(120f);

			clock.Advance(.5d);
			var accepted = clock.TrySetBpm(60f, out var rejection);
			clock.Advance(1d);

			Assert.That(accepted, Is.True);
			Assert.That(rejection, Is.Empty);
			Assert.That(clock.BeatsPerMinute, Is.EqualTo(60f));
			Assert.That(clock.TotalBeats, Is.EqualTo(2d).Within(1e-9d));
		}

		[Test]
		public void InvalidBpmIsRejectedWithoutChangingTheClock() {
			var clock = new LiveBpmClock(120f);

			var accepted = clock.TrySetBpm(float.NaN, out var rejection);

			Assert.That(accepted, Is.False);
			Assert.That(rejection, Is.Not.Empty);
			Assert.That(clock.BeatsPerMinute, Is.EqualTo(120f));
		}

		[Test]
		public void AdvancingPublishesBeatAndBarPhasesForShaders() {
			var clock = new LiveBpmClock(120f);

			clock.Advance(.625d);

			var frame = clock.Frame;
			Assert.That(frame.IsAvailable, Is.True);
			Assert.That(frame.Bpm, Is.EqualTo(120f));
			Assert.That(frame.BeatPhase, Is.EqualTo(.25f).Within(1e-6f));
			Assert.That(frame.BarPhase, Is.EqualTo(.3125f).Within(1e-6f));
		}
	}
}
