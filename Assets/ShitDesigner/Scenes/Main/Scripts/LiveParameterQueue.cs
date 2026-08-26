using System;
using System.Collections.Generic;

namespace ShitDesigner.Main {
	public enum LiveParameterRequestKind {
		PreloadPatch,
		LoadPatch,
		SetParameter,
		TriggerFlash
	}

	public readonly struct LiveParameterRequest {
		public ulong SequenceNumber { get; }
		public LiveParameterRequestKind Kind { get; }
		public string PatchId { get; }
		public string ParameterId { get; }
		public float Value { get; }

		internal LiveParameterRequest(ulong sequenceNumber, LiveParameterRequestKind kind, string patchId, string parameterId, float value) {
			SequenceNumber = sequenceNumber;
			Kind = kind;
			PatchId = patchId;
			ParameterId = parameterId;
			Value = value;
		}
	}

	public readonly struct LiveParameterEnqueueResult {
		public bool Accepted { get; }
		public ulong SequenceNumber { get; }
		public string RejectionReason { get; }

		private LiveParameterEnqueueResult(bool accepted, ulong sequenceNumber, string rejectionReason) {
			Accepted = accepted;
			SequenceNumber = sequenceNumber;
			RejectionReason = rejectionReason;
		}

		internal static LiveParameterEnqueueResult Accept(ulong sequenceNumber)
			=> new LiveParameterEnqueueResult(true, sequenceNumber, string.Empty);

		internal static LiveParameterEnqueueResult Reject(string reason)
			=> new LiveParameterEnqueueResult(false, 0, reason);
	}

	/// <summary>Assigns a common sequence to live-control requests and preserves their acceptance order.</summary>
	public sealed class LiveParameterQueue {
		public const int Capacity = 4096;

		private readonly Queue<LiveParameterRequest> _requests = new Queue<LiveParameterRequest>(Capacity);
		private ulong _nextSequenceNumber = 1;

		public int Count => _requests.Count;

		public LiveParameterEnqueueResult EnqueuePreloadPatch(string patchId)
			=> Enqueue(LiveParameterRequestKind.PreloadPatch, patchId, string.Empty, 0f);

		public LiveParameterEnqueueResult EnqueueLoadPatch(string patchId)
			=> Enqueue(LiveParameterRequestKind.LoadPatch, patchId, string.Empty, 0f);

		public LiveParameterEnqueueResult EnqueueSetParameter(string patchId, string parameterId, float value)
			=> Enqueue(LiveParameterRequestKind.SetParameter, patchId, parameterId, value);

		public LiveParameterEnqueueResult EnqueueTriggerFlash(string patchId)
			=> Enqueue(LiveParameterRequestKind.TriggerFlash, patchId, string.Empty, 0f);

		public int Drain(ICollection<LiveParameterRequest> destination) {
			if (destination == null) throw new ArgumentNullException(nameof(destination));

			var count = _requests.Count;
			while (_requests.Count > 0) destination.Add(_requests.Dequeue());
			return count;
		}

		private LiveParameterEnqueueResult Enqueue(LiveParameterRequestKind kind, string patchId, string parameterId, float value) {
			if (_requests.Count >= Capacity) return LiveParameterEnqueueResult.Reject("The live parameter queue is full.");
			if (string.IsNullOrWhiteSpace(patchId)) return LiveParameterEnqueueResult.Reject("A patch ID is required.");
			if (kind == LiveParameterRequestKind.SetParameter && string.IsNullOrWhiteSpace(parameterId))
				return LiveParameterEnqueueResult.Reject("A parameter ID is required.");

			var sequenceNumber = NextSequenceNumber();
			_requests.Enqueue(new LiveParameterRequest(sequenceNumber, kind, patchId, parameterId, value));
			return LiveParameterEnqueueResult.Accept(sequenceNumber);
		}

		private ulong NextSequenceNumber() {
			var sequenceNumber = _nextSequenceNumber++;
			if (_nextSequenceNumber == 0) _nextSequenceNumber = 1;
			return sequenceNumber;
		}
	}
}
