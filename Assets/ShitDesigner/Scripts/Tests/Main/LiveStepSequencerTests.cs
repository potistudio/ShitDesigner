using System.Linq;
using NUnit.Framework;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LiveStepSequencerTests {
		[Test]
		public void MultipleLanesCanBeSelectedInTheSameStep() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Effect, "EFFECT");

			Assert.That(sequencer.CycleCellMode(1, 3).Accepted, Is.True);
			Assert.That(sequencer.CycleCellMode(2, 3).Accepted, Is.True);

			var readModel = sequencer.CreateReadModel(3d);
			Assert.That(readModel.ActiveLaneMasks.Count, Is.EqualTo(LiveStepSequencer.StepCount));
			Assert.That(readModel.IsActive(1, 3), Is.True);
			Assert.That(readModel.IsActive(2, 3), Is.True);
		}

		[Test]
		public void TurningOnACellChangesOnlyTheRequestedBeatAndPreservesItsMode() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");
			sequencer.CycleCellMode(2, 3);
			sequencer.CycleCellMode(2, 3);

			Assert.That(sequencer.TurnOnCell(2, 3).Accepted, Is.True);
			Assert.That(sequencer.TurnOnCell(2, 5).Accepted, Is.True);

			var readModel = sequencer.CreateReadModel(0d);
			Assert.That(readModel.GetCellMode(2, 3), Is.EqualTo(LiveSequencerCellMode.Add));
			Assert.That(readModel.GetCellMode(2, 5), Is.EqualTo(LiveSequencerCellMode.Normal));
			Assert.That(readModel.IsActive(2, 4), Is.False);
		}

		[Test]
		public void TogglingAStepTurnsEveryLaneOnThenOff() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");
			sequencer.CycleCellMode(2, 3);
			sequencer.CycleCellMode(2, 3);

			Assert.That(sequencer.ToggleStep(3).Accepted, Is.True);

			var enabled = sequencer.CreateReadModel(0d);
			Assert.That(Enumerable.Range(0, sequencer.LaneCount).All(laneIndex => enabled.IsActive(laneIndex, 3)), Is.True);
			Assert.That(enabled.GetCellMode(2, 3), Is.EqualTo(LiveSequencerCellMode.Add));

			Assert.That(sequencer.ToggleStep(3).Accepted, Is.True);

			var disabled = sequencer.CreateReadModel(0d);
			Assert.That(Enumerable.Range(0, sequencer.LaneCount).Any(laneIndex => disabled.IsActive(laneIndex, 3)), Is.False);
			Assert.That(Enumerable.Range(0, sequencer.LaneCount).All(laneIndex => disabled.GetCellMode(laneIndex, 3) == LiveSequencerCellMode.Off), Is.True);
		}

		[Test]
		public void InvalidStepToggleIsRejectedWithoutChangingState() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Effect, "EFFECT");

			Assert.That(sequencer.ToggleStep(LiveStepSequencer.StepCount).Accepted, Is.False);
			Assert.That(sequencer.CreateReadModel(0d).ActiveLaneMasks, Is.All.Zero);
		}

		[Test]
		public void TogglingALaneTurnsEveryStepOnThenOff() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");
			sequencer.CycleCellMode(2, 3);
			sequencer.CycleCellMode(2, 3);

			Assert.That(sequencer.ToggleLane(2).Accepted, Is.True);

			var enabled = sequencer.CreateReadModel(0d);
			Assert.That(Enumerable.Range(0, LiveStepSequencer.StepCount).All(stepIndex => enabled.IsActive(2, stepIndex)), Is.True);
			Assert.That(enabled.GetCellMode(2, 3), Is.EqualTo(LiveSequencerCellMode.Add));

			Assert.That(sequencer.ToggleLane(2).Accepted, Is.True);

			var disabled = sequencer.CreateReadModel(0d);
			Assert.That(Enumerable.Range(0, LiveStepSequencer.StepCount).Any(stepIndex => disabled.IsActive(2, stepIndex)), Is.False);
			Assert.That(Enumerable.Range(0, LiveStepSequencer.StepCount).All(stepIndex => disabled.GetCellMode(2, stepIndex) == LiveSequencerCellMode.Off), Is.True);
		}

		[Test]
		public void InvalidLaneToggleIsRejectedWithoutChangingState() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Effect, "EFFECT");

			Assert.That(sequencer.ToggleLane(sequencer.LaneCount).Accepted, Is.False);
			Assert.That(sequencer.CreateReadModel(0d).ActiveLaneMasks, Is.All.Zero);
		}

		[Test]
		public void CellModeCyclesThroughTheSupportedModes() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");
			var expectedModes = new[] {
				LiveSequencerCellMode.Normal,
				LiveSequencerCellMode.Add,
				LiveSequencerCellMode.Multiply,
				LiveSequencerCellMode.Subtract,
				LiveSequencerCellMode.Difference,
				LiveSequencerCellMode.Invert,
				LiveSequencerCellMode.Off
			};

			foreach (var expectedMode in expectedModes) {
				Assert.That(sequencer.CycleCellMode(0, 0).Accepted, Is.True);
				Assert.That(sequencer.CreateReadModel(0d).GetCellMode(0, 0), Is.EqualTo(expectedMode));
			}
			Assert.That(sequencer.CreateReadModel(0d).IsActive(0, 0), Is.False);
		}

		[Test]
		public void PlayheadRepeatsEveryEightBeats() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Effect, "EFFECT");

			Assert.That(sequencer.CreateReadModel(7.99d).CurrentStep, Is.EqualTo(7));
			Assert.That(sequencer.CreateReadModel(8d).CurrentStep, Is.Zero);
			Assert.That(sequencer.CreateReadModel(17.25d).CurrentStep, Is.EqualTo(1));
		}

		[Test]
		public void InvalidLaneAndStepAreRejectedWithoutChangingState() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");

			Assert.That(sequencer.CycleCellMode(sequencer.LaneCount, 0).Accepted, Is.False);
			Assert.That(sequencer.CycleCellMode(0, LiveStepSequencer.StepCount).Accepted, Is.False);
			Assert.That(sequencer.CreateReadModel(0d).ActiveLaneMasks, Is.All.Zero);
		}

		[Test]
		public void OverlayHasEightLanesAndEffectKeepsFour() {
			var overlay = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");
			var effect = new LiveStepSequencer(LiveSequencerKind.Effect, "EFFECT");

			Assert.That(overlay.LaneCount, Is.EqualTo(8));
			Assert.That(effect.LaneCount, Is.EqualTo(4));
		}

		[Test]
		public void AssigningASceneDirectlyUpdatesTheRequestedLane() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");

			Assert.That(sequencer.AssignLane(2, "overlay-c").Accepted, Is.True);

			var readModel = sequencer.CreateReadModel(0d);
			Assert.That(readModel.LanePatchIds[2], Is.EqualTo("overlay-c"));
		}

		[Test]
		public void UnassigningASceneClearsOnlyTheLaneAssignment() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");
			sequencer.AssignLane(2, "overlay-c");
			sequencer.CycleCellMode(2, 3);
			sequencer.ToggleOutput2Copy(2);

			Assert.That(sequencer.UnassignLane(2).Accepted, Is.True);

			var readModel = sequencer.CreateReadModel(3d);
			Assert.That(readModel.LanePatchIds[2], Is.Empty);
			Assert.That(readModel.GetCellMode(2, 3), Is.EqualTo(LiveSequencerCellMode.Normal));
			Assert.That(readModel.IsCopiedToOutput2(2), Is.True);
			Assert.That(readModel.GetActiveLayers(), Is.Empty);
		}

		[Test]
		public void UnassigningAnInvalidLaneIsRejectedWithoutChangingAssignments() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");
			sequencer.AssignLane(0, "overlay-a");

			Assert.That(sequencer.UnassignLane(sequencer.LaneCount).Accepted, Is.False);
			Assert.That(sequencer.CreateReadModel(0d).LanePatchIds[0], Is.EqualTo("overlay-a"));
		}

		[Test]
		public void ActiveLayersContainAssignedScenesModesAndLaneOrder() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");
			sequencer.AssignLane(2, "overlay-c");
			sequencer.AssignLane(0, "overlay-a");
			sequencer.CycleCellMode(2, 5);
			sequencer.CycleCellMode(2, 5);
			sequencer.CycleCellMode(0, 5);

			var layers = sequencer.CreateReadModel(5d).GetActiveLayers();

			Assert.That(layers.Select(layer => layer.LaneIndex), Is.EqualTo(new[] { 0, 2 }));
			Assert.That(layers.Select(layer => layer.PatchId), Is.EqualTo(new[] { "overlay-a", "overlay-c" }));
			Assert.That(layers.Select(layer => layer.Mode), Is.EqualTo(new[] { LiveSequencerCellMode.Normal, LiveSequencerCellMode.Add }));
			Assert.That(sequencer.CreateReadModel(4d).GetActiveLayers(), Is.Empty);
		}

		[Test]
		public void OverlayTakeOverridesOnlyTheCurrentStepWithoutChangingTheSequence() {
			var sequencer = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");
			sequencer.AssignLane(0, "overlay-a");
			sequencer.AssignLane(1, "overlay-b");
			sequencer.CycleCellMode(0, 3);

			var taken = sequencer.CreateReadModel(3d, new[] { 0, 1 });

			Assert.That(taken.GetActiveLayers().Select(layer => layer.LaneIndex), Is.EqualTo(new[] { 1 }));
			Assert.That(taken.GetCellMode(1, 3), Is.EqualTo(LiveSequencerCellMode.Normal));
			Assert.That(sequencer.CreateReadModel(3d).GetActiveLayers().Select(layer => layer.LaneIndex), Is.EqualTo(new[] { 0 }));
		}

		[Test]
		public void OverlayLaneOutput2CopyIsOffByDefaultAndTogglesIndependently() {
			var overlay = new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY");
			var effect = new LiveStepSequencer(LiveSequencerKind.Effect, "EFFECT");

			Assert.That(overlay.CreateReadModel(0d).IsCopiedToOutput2(3), Is.False);
			Assert.That(overlay.ToggleOutput2Copy(3).Accepted, Is.True);
			Assert.That(overlay.CreateReadModel(0d).IsCopiedToOutput2(3), Is.True);
			Assert.That(overlay.CreateReadModel(0d).IsCopiedToOutput2(2), Is.False);
			Assert.That(effect.ToggleOutput2Copy(0).Accepted, Is.False);
		}
	}
}
