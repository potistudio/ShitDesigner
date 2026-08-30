using NUnit.Framework;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LiveStepSequencerTests {
		[Test]
		public void MultipleLanesCanBeSelectedInTheSameStep() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Effect, "EFFECT");

			Assert.That(sequencer.Toggle(1, 3).Accepted, Is.True);
			Assert.That(sequencer.Toggle(2, 3).Accepted, Is.True);

			var readModel = sequencer.CreateReadModel(3d);
			Assert.That(readModel.ActiveLaneMasks, Has.Count.EqualTo(LiveStepSequencer.StepCount));
			Assert.That(readModel.IsActive(1, 3), Is.True);
			Assert.That(readModel.IsActive(2, 3), Is.True);
		}

		[Test]
		public void SelectingTheActiveCellClearsTheStep() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");
			sequencer.Toggle(0, 0);

			var result = sequencer.Toggle(0, 0);

			Assert.That(result.Accepted, Is.True);
			Assert.That(sequencer.CreateReadModel(0d).IsActive(0, 0), Is.False);
		}

		[Test]
		public void SelectingACompositingModeReplacesTheModeAtThatStep() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.CompositingMode, "COMPOSITING MODE");
			sequencer.Toggle(0, 3);

			var result = sequencer.Toggle(2, 3);

			Assert.That(result.Accepted, Is.True);
			var readModel = sequencer.CreateReadModel(3d);
			Assert.That(readModel.IsActive(0, 3), Is.False);
			Assert.That(readModel.IsActive(2, 3), Is.True);
		}

		[Test]
		public void SelectingTheActiveCompositingModeKeepsItSelected() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.CompositingMode, "COMPOSITING MODE");
			sequencer.Toggle(1, 5);

			var result = sequencer.Toggle(1, 5);

			Assert.That(result.Accepted, Is.True);
			Assert.That(sequencer.CreateReadModel(5d).IsActive(1, 5), Is.True);
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
			Assert.That(sequencer.CreateReadModel(0d).ActiveLaneMasks, Is.All.Zero);
		}

		[Test]
		public void SelectingALaneAndAssigningASceneUpdatesItsReadModel() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");

			Assert.That(sequencer.SelectLane(2).Accepted, Is.True);
			Assert.That(sequencer.CreateReadModel(0d).SelectedLaneIndex, Is.EqualTo(2));
			Assert.That(sequencer.AssignSelectedLane("overlay-c").Accepted, Is.True);

			var readModel = sequencer.CreateReadModel(0d);
			Assert.That(readModel.SelectedLaneIndex, Is.EqualTo(-1));
			Assert.That(readModel.LanePatchIds[2], Is.EqualTo("overlay-c"));
		}

		[Test]
		public void EveryHighlightedLaneWithAnAssignedSceneTriggersAtTheStep() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");
			sequencer.SelectLane(0);
			sequencer.AssignSelectedLane("overlay-a");
			sequencer.SelectLane(2);
			sequencer.AssignSelectedLane("overlay-c");
			sequencer.Toggle(0, 5);
			sequencer.Toggle(2, 5);

			var triggered = sequencer.GetTriggeredPatchIds(5);

			Assert.That(triggered, Is.EqualTo(new[] { "overlay-a", "overlay-c" }));
			Assert.That(sequencer.GetTriggeredPatchIds(4), Is.Empty);
		}
	}
}
