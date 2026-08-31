using System.Reflection;
using NUnit.Framework;
using ShitDesigner.Rendering;
using UnityEngine;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LiveBpmClockTests {
		[Test]
		public void BootstrapDefaultsToVisibleNonLinearTimeEasing() {
			var host = new GameObject("Global Time Easing Bootstrap Test");
			try {
				var bootstrap = host.AddComponent<LiveGraphBootstrap>();
				var field = typeof(LiveGraphBootstrap).GetField("m_GlobalTimeEasing", BindingFlags.Instance | BindingFlags.NonPublic);
				var curve = field?.GetValue(bootstrap) as AnimationCurve;

				Assert.That(curve, Is.Not.Null);
				Assert.That(curve.Evaluate(.25f), Is.LessThan(.25f));
				Assert.That(curve.Evaluate(.75f), Is.GreaterThan(.75f));
			}
			finally { Object.DestroyImmediate(host); }
		}

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
		public void AlignToNearestBeatShiftsResolvedTimingWithoutChangingAccumulatedBeats() {
			var clock = new LiveBpmClock(120f);
			clock.Advance(.625d);

			var accepted = clock.TryAlignToNearestBeat(out var rejection);

			Assert.That(accepted, Is.True);
			Assert.That(rejection, Is.Empty);
			Assert.That(clock.TotalBeats, Is.EqualTo(1.25d).Within(1e-9d));
			Assert.That(clock.Frame.BeatAlignmentBeats, Is.EqualTo(.25d).Within(1e-9d));
			Assert.That(clock.Frame.AdjustedTotalBeats, Is.EqualTo(1d).Within(1e-9d));
			Assert.That(clock.Frame.BeatPhase, Is.Zero);
		}

		[Test]
		public void AlignToNearestBeatCanMoveToTheNextBeat() {
			var clock = new LiveBpmClock(120f);
			clock.Advance(.875d);

			var accepted = clock.TryAlignToNearestBeat(out var rejection);

			Assert.That(accepted, Is.True);
			Assert.That(rejection, Is.Empty);
			Assert.That(clock.Frame.TotalBeats, Is.EqualTo(1.75d).Within(1e-9d));
			Assert.That(clock.Frame.BeatAlignmentBeats, Is.EqualTo(-.25d).Within(1e-9d));
			Assert.That(clock.Frame.AdjustedTotalBeats, Is.EqualTo(2d).Within(1e-9d));
		}

		[Test]
		public void BeatAlignmentPreservesPhaseWhenBpmChanges() {
			var clock = new LiveBpmClock(120f);
			clock.Advance(.625d);
			clock.TryAlignToNearestBeat(out _);

			var accepted = clock.TrySetBpm(60f, out var rejection);

			Assert.That(accepted, Is.True);
			Assert.That(rejection, Is.Empty);
			Assert.That(clock.Frame.TotalBeats, Is.EqualTo(1.25d).Within(1e-9d));
			Assert.That(clock.Frame.AdjustedTotalBeats, Is.EqualTo(1d).Within(1e-9d));
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

		[Test]
		public void LinearTimeEasingPreservesTheSourceDelta() {
			var clock = new LiveBpmClock(120f, AnimationCurve.Linear(0f, 0f, 1f, 1f));

			var first = clock.Advance(.125d);
			var second = clock.Advance(.375d);

			Assert.That(first, Is.EqualTo(.125d).Within(1e-9d));
			Assert.That(second, Is.EqualTo(.375d).Within(1e-9d));
		}

		[Test]
		public void GlobalTimeEasingRedistributesTimeWithoutChangingBeatDuration() {
			var clock = new LiveBpmClock(120f, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));

			var easedFirstQuarter = clock.Advance(.125d);
			var easedRemainder = clock.Advance(.375d);

			Assert.That(easedFirstQuarter, Is.LessThan(.125d));
			Assert.That(easedFirstQuarter + easedRemainder, Is.EqualTo(.5d).Within(1e-6d));
			Assert.That(clock.BeatsPerMinute, Is.EqualTo(120f));
			Assert.That(clock.TotalBeats, Is.EqualTo(1d).Within(1e-9d));
		}

		[Test]
		public void GlobalTimeEasingPreservesElapsedTimeAcrossMultipleBeats() {
			var clock = new LiveBpmClock(120f, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));

			var easedDelta = clock.Advance(1.25d);

			Assert.That(easedDelta, Is.EqualTo(1.25d).Within(1e-6d));
			Assert.That(clock.TotalBeats, Is.EqualTo(2.5d).Within(1e-9d));
		}

		[Test]
		public void ProjectedGraphDeltaUsesTheEaseWithoutAdvancingTheBeatClock() {
			var clock = new LiveBpmClock(120f, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));
			clock.Advance(.125d);

			var projected = clock.ProjectGraphDelta(.125d);

			Assert.That(projected, Is.GreaterThan(.125d));
			Assert.That(clock.TotalBeats, Is.EqualTo(.25d).Within(1e-9d));
		}

		[Test]
		public void TimeEasingCanBeDisabledAndReenabledWithoutChangingTheBeatClock() {
			var clock = new LiveBpmClock(120f, AnimationCurve.EaseInOut(0f, 0f, 1f, 1f));

			var easedFirstQuarter = clock.Advance(.125d);
			clock.SetTimeEasingEnabled(false);
			var linearSecondQuarter = clock.Advance(.125d);
			var linearProjection = clock.ProjectGraphDelta(.125d);
			clock.SetTimeEasingEnabled(true);
			var easedThirdQuarter = clock.Advance(.125d);

			Assert.That(easedFirstQuarter, Is.LessThan(.125d));
			Assert.That(linearSecondQuarter, Is.EqualTo(.125d).Within(1e-9d));
			Assert.That(linearProjection, Is.EqualTo(.125d).Within(1e-9d));
			Assert.That(easedThirdQuarter, Is.GreaterThan(.125d));
			Assert.That(clock.IsTimeEasingEnabled, Is.True);
			Assert.That(clock.TotalBeats, Is.EqualTo(.75d).Within(1e-9d));
		}
	}
}
