using System;
using System.Collections.Generic;

namespace ShitDesigner.Main {
	public readonly struct LivePatchSlotReadModel {
		public int Index { get; }
		public string PatchId { get; }

		public LivePatchSlotReadModel(int index, string patchId) {
			Index = index;
			PatchId = patchId ?? string.Empty;
		}

		public bool IsEmpty => string.IsNullOrEmpty(PatchId);
	}

	public readonly struct LivePatchSlotOperationResult {
		public bool Accepted { get; }
		public int SlotIndex { get; }
		public string RejectionReason { get; }

		private LivePatchSlotOperationResult(bool accepted, int slotIndex, string rejectionReason) {
			Accepted = accepted;
			SlotIndex = slotIndex;
			RejectionReason = rejectionReason;
		}

		internal static LivePatchSlotOperationResult Accept(int slotIndex) => new LivePatchSlotOperationResult(true, slotIndex, string.Empty);
		internal static LivePatchSlotOperationResult Reject(string reason) => new LivePatchSlotOperationResult(false, -1, reason);
	}

	/// <summary>Maintains the eight fixed patch slots used to cue and launch live patches.</summary>
	public sealed class LivePatchSlots {
		public const int Capacity = 8;

		private readonly string[] _patchIds = new string[Capacity];

		public IReadOnlyList<LivePatchSlotReadModel> ReadModel {
			get {
				var slots = new LivePatchSlotReadModel[Capacity];
				for (var index = 0; index < Capacity; index++) slots[index] = new LivePatchSlotReadModel(index, _patchIds[index]);
				return slots;
			}
		}

		public LivePatchSlotOperationResult Queue(string patchId) {
			if (string.IsNullOrWhiteSpace(patchId)) return LivePatchSlotOperationResult.Reject("A patch ID is required.");
			for (var index = 0; index < Capacity; index++) {
				if (!string.IsNullOrEmpty(_patchIds[index])) continue;
				_patchIds[index] = patchId;
				return LivePatchSlotOperationResult.Accept(index);
			}
			return LivePatchSlotOperationResult.Reject("All patch slots are occupied.");
		}

		public LivePatchSlotOperationResult Clear(int slotIndex) {
			if (!IsValidSlotIndex(slotIndex)) return LivePatchSlotOperationResult.Reject("The patch slot does not exist.");
			_patchIds[slotIndex] = string.Empty;
			return LivePatchSlotOperationResult.Accept(slotIndex);
		}

		public bool TryGetPatchId(int slotIndex, out string patchId) {
			if (!IsValidSlotIndex(slotIndex) || string.IsNullOrEmpty(_patchIds[slotIndex])) {
				patchId = string.Empty;
				return false;
			}
			patchId = _patchIds[slotIndex];
			return true;
		}

		public static bool IsValidSlotIndex(int slotIndex) => slotIndex >= 0 && slotIndex < Capacity;
	}
}
