using NUnit.Framework;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LivePatchSlotsTests {
		[Test]
		public void ReadModelContainsEightFixedSlots() {
			var slots = new LivePatchSlots();

			Assert.That(LivePatchSlots.Capacity, Is.EqualTo(8));
			Assert.That(slots.ReadModel, Has.Count.EqualTo(8));
		}

		[Test]
		public void QueueFillsSlotsInOrderAndPreservesTheirPatchIds() {
			var slots = new LivePatchSlots();

			var first = slots.Queue("patch-a");
			var second = slots.Queue("patch-b");

			Assert.That(first.Accepted, Is.True);
			Assert.That(first.SlotIndex, Is.Zero);
			Assert.That(second.Accepted, Is.True);
			Assert.That(second.SlotIndex, Is.EqualTo(1));
			Assert.That(slots.ReadModel[0].PatchId, Is.EqualTo("patch-a"));
			Assert.That(slots.ReadModel[1].PatchId, Is.EqualTo("patch-b"));
		}

		[Test]
		public void ClearingSlotMakesItAvailableToTheNextQueuedPatch() {
			var slots = new LivePatchSlots();
			slots.Queue("patch-a");
			slots.Queue("patch-b");

			var cleared = slots.Clear(0);
			var queued = slots.Queue("patch-c");

			Assert.That(cleared.Accepted, Is.True);
			Assert.That(queued.Accepted, Is.True);
			Assert.That(queued.SlotIndex, Is.Zero);
			Assert.That(slots.ReadModel[0].PatchId, Is.EqualTo("patch-c"));
		}

		[Test]
		public void QueueRejectsWhenAllSlotsAreOccupied() {
			var slots = new LivePatchSlots();
			for (var index = 0; index < LivePatchSlots.Capacity; index++)
				Assert.That(slots.Queue("patch-" + index).Accepted, Is.True);

			var result = slots.Queue("overflow");

			Assert.That(result.Accepted, Is.False);
			Assert.That(result.SlotIndex, Is.EqualTo(-1));
			Assert.That(result.RejectionReason, Is.Not.Empty);
		}

		[Test]
		public void EmptyAndInvalidSlotsCannotBeRead() {
			var slots = new LivePatchSlots();

			Assert.That(slots.TryGetPatchId(0, out _), Is.False);
			Assert.That(slots.TryGetPatchId(LivePatchSlots.Capacity, out _), Is.False);
			Assert.That(slots.Clear(-1).Accepted, Is.False);
		}
	}
}
