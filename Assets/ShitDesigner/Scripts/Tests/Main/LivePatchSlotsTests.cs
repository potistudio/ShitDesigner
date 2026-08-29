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
		public void AssignReplacesOnlyTheRequestedSlot() {
			var slots = new LivePatchSlots();

			var first = slots.Assign(2, "patch-a");
			var second = slots.Assign(3, "patch-b");
			var replacement = slots.Assign(2, "patch-c");

			Assert.That(first.Accepted, Is.True);
			Assert.That(first.SlotIndex, Is.EqualTo(2));
			Assert.That(second.Accepted, Is.True);
			Assert.That(second.SlotIndex, Is.EqualTo(3));
			Assert.That(replacement.Accepted, Is.True);
			Assert.That(replacement.SlotIndex, Is.EqualTo(2));
			Assert.That(slots.ReadModel[2].PatchId, Is.EqualTo("patch-c"));
			Assert.That(slots.ReadModel[3].PatchId, Is.EqualTo("patch-b"));
		}

		[Test]
		public void ClearingSlotDoesNotAffectAssignmentToAnotherSlot() {
			var slots = new LivePatchSlots();
			slots.Assign(0, "patch-a");
			slots.Assign(1, "patch-b");

			var cleared = slots.Clear(0);
			var assigned = slots.Assign(1, "patch-c");

			Assert.That(cleared.Accepted, Is.True);
			Assert.That(assigned.Accepted, Is.True);
			Assert.That(slots.ReadModel[0].IsEmpty, Is.True);
			Assert.That(slots.ReadModel[1].PatchId, Is.EqualTo("patch-c"));
		}

		[Test]
		public void AssignRejectsInvalidSlotAndPatchId() {
			var slots = new LivePatchSlots();

			Assert.That(slots.Assign(-1, "patch-a").Accepted, Is.False);
			Assert.That(slots.Assign(LivePatchSlots.Capacity, "patch-a").Accepted, Is.False);
			Assert.That(slots.Assign(0, string.Empty).Accepted, Is.False);
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
