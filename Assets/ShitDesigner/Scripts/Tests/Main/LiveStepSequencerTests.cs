using NUnit.Framework;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LiveStepSequencerTests {
		[Test]
		public void AStepSelectsAtMostOneOfFourLanes() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Effect, "EFFECT");

			Assert.That(sequencer.Toggle(1, 3).Accepted, Is.True);
			Assert.That(sequencer.CreateReadModel(3d).ActiveLanes[3], Is.EqualTo(1));
			Assert.That(sequencer.Toggle(2, 3).Accepted, Is.True);

			var readModel = sequencer.CreateReadModel(3d);
			Assert.That(readModel.ActiveLanes, Has.Count.EqualTo(LiveStepSequencer.StepCount));
			Assert.That(readModel.ActiveLanes[3], Is.EqualTo(2));
		}

		[Test]
		public void SelectingTheActiveCellClearsTheStep() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");
			sequencer.Toggle(0, 0);

			var result = sequencer.Toggle(0, 0);

			Assert.That(result.Accepted, Is.True);
			Assert.That(sequencer.CreateReadModel(0d).ActiveLanes[0], Is.EqualTo(-1));
		}

		[Test]
		public void PlayheadRepeatsEveryEightBeats() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.CompositingMode, "COMPOSITING MODE");

			Assert.That(sequencer.CreateReadModel(7.99d).CurrentStep, Is.EqualTo(7));
			Assert.That(sequencer.CreateReadModel(8d).CurrentStep, Is.Zero);
			Assert.That(sequencer.CreateReadModel(17.25d).CurrentStep, Is.EqualTo(1));
		}

		[Test]
		public void InvalidLaneAndStepAreRejectedWithoutChangingState() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");

			Assert.That(sequencer.Toggle(LiveStepSequencer.LaneCount, 0).Accepted, Is.False);
			Assert.That(sequencer.Toggle(0, LiveStepSequencer.StepCount).Accepted, Is.False);
			Assert.That(sequencer.CreateReadModel(0d).ActiveLanes, Is.All.EqualTo(-1));
		}
	}
}
