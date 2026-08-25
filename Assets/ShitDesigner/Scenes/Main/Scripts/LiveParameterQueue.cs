using System;
using System.Collections.Generic;

namespace ShitDesigner.Main {
	public enum LiveParameterRequestKind {
		SelectScene,
		SetParameter
	}

	public readonly struct LiveParameterRequest {
		public ulong SequenceNumber { get; }
		public LiveParameterRequestKind Kind { get; }
		public string SceneId { get; }
		public string ParameterId { get; }
		public float Value { get; }

		internal LiveParameterRequest(ulong sequenceNumber, LiveParameterRequestKind kind, string sceneId, string parameterId, float value) {
			SequenceNumber = sequenceNumber;
			Kind = kind;
			SceneId = sceneId;
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

		public LiveParameterEnqueueResult EnqueueSelectScene(string sceneId)
			=> Enqueue(LiveParameterRequestKind.SelectScene, sceneId, string.Empty, 0f);

		public LiveParameterEnqueueResult EnqueueSetParameter(string sceneId, string parameterId, float value)
			=> Enqueue(LiveParameterRequestKind.SetParameter, sceneId, parameterId, value);

		public int Drain(ICollection<LiveParameterRequest> destination) {
			if (destination == null) throw new ArgumentNullException(nameof(destination));

			var count = _requests.Count;
			while (_requests.Count > 0) destination.Add(_requests.Dequeue());
			return count;
		}

		private LiveParameterEnqueueResult Enqueue(LiveParameterRequestKind kind, string sceneId, string parameterId, float value) {
			if (_requests.Count >= Capacity) return LiveParameterEnqueueResult.Reject("The live parameter queue is full.");
			if (string.IsNullOrWhiteSpace(sceneId)) return LiveParameterEnqueueResult.Reject("A scene ID is required.");
			if (kind == LiveParameterRequestKind.SetParameter && string.IsNullOrWhiteSpace(parameterId))
				return LiveParameterEnqueueResult.Reject("A parameter ID is required.");

			var sequenceNumber = NextSequenceNumber();
			_requests.Enqueue(new LiveParameterRequest(sequenceNumber, kind, sceneId, parameterId, value));
			return LiveParameterEnqueueResult.Accept(sequenceNumber);
		}

		private ulong NextSequenceNumber() {
			var sequenceNumber = _nextSequenceNumber++;
			if (_nextSequenceNumber == 0) _nextSequenceNumber = 1;
			return sequenceNumber;
		}
	}
}
