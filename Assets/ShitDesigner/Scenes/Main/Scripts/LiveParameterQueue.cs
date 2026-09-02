using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using ShitDesigner.Scene;

namespace ShitDesigner.Main {
	public enum LiveParameterRequestKind {
		PreloadPatch,
		LoadPatch,
		LaunchPatch,
		SetParameter,
		SetBpm,
		AlignBeat,
		SetSceneQuantizeMode,
		SetHotCueQuantizeMode,
		SetMainCueQuantizeMode,
		SetPianoFxQuantizeMode,
		SetTimeEasingEnabled,
		JogSceneTime,
		SetMainCueFader,
		ToggleMainCue,
		SetMainCueComposite,
		ToggleMainCueComposite,
		RecallHotCue,
		RecallOppositeHotCue,
		UnassignMainCue,
		AssignMainCue
	}

	public readonly struct LiveParameterRequest {
		public ulong SequenceNumber { get; }
		public LiveParameterRequestKind Kind { get; }
		public string PatchId { get; }
		public string ParameterId { get; }
		public float Value { get; }
		public ParameterValue ParameterValue { get; }

		internal LiveParameterRequest(ulong sequenceNumber, LiveParameterRequestKind kind, string patchId, string parameterId, ParameterValue parameterValue) {
			SequenceNumber = sequenceNumber;
			Kind = kind;
			PatchId = patchId;
			ParameterId = parameterId;
			ParameterValue = parameterValue;
			Value = parameterValue.Type == ParameterType.Float ? parameterValue.AsFloat() : 0f;
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
			=> Enqueue(LiveParameterRequestKind.PreloadPatch, patchId, string.Empty, ParameterValue.FromFloat(0f));

		public LiveParameterEnqueueResult EnqueueLoadPatch(string patchId)
			=> Enqueue(LiveParameterRequestKind.LoadPatch, patchId, string.Empty, ParameterValue.FromFloat(0f));

		public LiveParameterEnqueueResult EnqueueLaunchPatch(string patchId)
			=> Enqueue(LiveParameterRequestKind.LaunchPatch, patchId, string.Empty, ParameterValue.FromFloat(0f));

		public LiveParameterEnqueueResult EnqueueSetParameter(string patchId, string parameterId, float value)
			=> EnqueueSetParameter(patchId, parameterId, ParameterValue.FromFloat(value));

		public LiveParameterEnqueueResult EnqueueSetParameter(string patchId, string parameterId, ParameterValue value)
			=> Enqueue(LiveParameterRequestKind.SetParameter, patchId, parameterId, value);

		public LiveParameterEnqueueResult EnqueueSetBpm(float bpm)
			=> Enqueue(LiveParameterRequestKind.SetBpm, string.Empty, string.Empty, ParameterValue.FromFloat(bpm));

		public LiveParameterEnqueueResult EnqueueAlignBeat()
			=> Enqueue(LiveParameterRequestKind.AlignBeat, string.Empty, string.Empty, ParameterValue.FromFloat(0f));

		public LiveParameterEnqueueResult EnqueueSetSceneQuantizeMode(bool enabled)
			=> Enqueue(LiveParameterRequestKind.SetSceneQuantizeMode, string.Empty, string.Empty, ParameterValue.FromBool(enabled));

		public LiveParameterEnqueueResult EnqueueSetHotCueQuantizeMode(bool enabled)
			=> Enqueue(LiveParameterRequestKind.SetHotCueQuantizeMode, string.Empty, string.Empty, ParameterValue.FromBool(enabled));

		public LiveParameterEnqueueResult EnqueueSetMainCueQuantizeMode(bool enabled)
			=> Enqueue(LiveParameterRequestKind.SetMainCueQuantizeMode, string.Empty, string.Empty, ParameterValue.FromBool(enabled));

		public LiveParameterEnqueueResult EnqueueSetPianoFxQuantizeMode(bool enabled)
			=> Enqueue(LiveParameterRequestKind.SetPianoFxQuantizeMode, string.Empty, string.Empty, ParameterValue.FromBool(enabled));

		public LiveParameterEnqueueResult EnqueueSetTimeEasingEnabled(bool enabled)
			=> Enqueue(LiveParameterRequestKind.SetTimeEasingEnabled, string.Empty, string.Empty, ParameterValue.FromBool(enabled));

		public LiveParameterEnqueueResult EnqueueJogSceneTime(float speedOffsetDelta)
			=> float.IsNaN(speedOffsetDelta) || float.IsInfinity(speedOffsetDelta)
				? LiveParameterEnqueueResult.Reject("The scene time jog speed delta must be finite.")
				: Enqueue(LiveParameterRequestKind.JogSceneTime, string.Empty, string.Empty, ParameterValue.FromFloat(speedOffsetDelta));

		public LiveParameterEnqueueResult EnqueueRecallHotCue(int hotCueIndex, bool oppositeScene = false)
			=> hotCueIndex < 0 || hotCueIndex >= PatchDefinition.HotCueCount
				? LiveParameterEnqueueResult.Reject("The Hot Cue index must be 0 or 1.")
				: Enqueue(oppositeScene ? LiveParameterRequestKind.RecallOppositeHotCue : LiveParameterRequestKind.RecallHotCue,
					string.Empty, string.Empty, ParameterValue.FromInt(hotCueIndex));

		public LiveParameterEnqueueResult EnqueueSetMainCueFader(float normalizedValue)
			=> float.IsNaN(normalizedValue) || float.IsInfinity(normalizedValue)
				? LiveParameterEnqueueResult.Reject("The Main Cue fader value must be finite.")
				: Enqueue(LiveParameterRequestKind.SetMainCueFader, string.Empty, string.Empty,
					ParameterValue.FromFloat(Math.Max(0f, Math.Min(1f, normalizedValue))));

		public LiveParameterEnqueueResult EnqueueToggleMainCue()
			=> Enqueue(LiveParameterRequestKind.ToggleMainCue, string.Empty, string.Empty, ParameterValue.FromFloat(0f));

		public LiveParameterEnqueueResult EnqueueSetMainCueComposite(bool active)
			=> Enqueue(LiveParameterRequestKind.SetMainCueComposite, string.Empty, string.Empty, ParameterValue.FromBool(active));

		public LiveParameterEnqueueResult EnqueueToggleMainCueComposite()
			=> Enqueue(LiveParameterRequestKind.ToggleMainCueComposite, string.Empty, string.Empty, ParameterValue.FromFloat(0f));

		public LiveParameterEnqueueResult EnqueueUnassignMainCue(int cueIndex)
			=> cueIndex < 0 || cueIndex >= LiveGraphRuntime.MainCueCount
				? LiveParameterEnqueueResult.Reject("The Main Cue Slot does not exist.")
				: Enqueue(LiveParameterRequestKind.UnassignMainCue, string.Empty, string.Empty, ParameterValue.FromInt(cueIndex));

		public LiveParameterEnqueueResult EnqueueAssignMainCue(int cueIndex, string patchId)
			=> cueIndex < 0 || cueIndex >= LiveGraphRuntime.MainCueCount
				? LiveParameterEnqueueResult.Reject("The Main Cue Slot does not exist.")
				: Enqueue(LiveParameterRequestKind.AssignMainCue, patchId, string.Empty, ParameterValue.FromInt(cueIndex));

		public int Drain(ICollection<LiveParameterRequest> destination) {
			if (destination == null) throw new ArgumentNullException(nameof(destination));

			var count = _requests.Count;
			while (_requests.Count > 0) destination.Add(_requests.Dequeue());
			return count;
		}

		private LiveParameterEnqueueResult Enqueue(LiveParameterRequestKind kind, string patchId, string parameterId, ParameterValue value) {
			if (_requests.Count >= Capacity) return LiveParameterEnqueueResult.Reject("The live parameter queue is full.");
			if (!IsGlobalRequest(kind) && string.IsNullOrWhiteSpace(patchId)) return LiveParameterEnqueueResult.Reject("A patch ID is required.");
			if (kind == LiveParameterRequestKind.SetParameter && string.IsNullOrWhiteSpace(parameterId))
				return LiveParameterEnqueueResult.Reject("A parameter ID is required.");

			var sequenceNumber = NextSequenceNumber();
			_requests.Enqueue(new LiveParameterRequest(sequenceNumber, kind, patchId, parameterId, value));
			return LiveParameterEnqueueResult.Accept(sequenceNumber);
		}

		private static bool IsGlobalRequest(LiveParameterRequestKind kind)
			=> kind == LiveParameterRequestKind.SetBpm || kind == LiveParameterRequestKind.AlignBeat || kind == LiveParameterRequestKind.SetSceneQuantizeMode
				|| kind == LiveParameterRequestKind.SetHotCueQuantizeMode || kind == LiveParameterRequestKind.SetMainCueQuantizeMode
				|| kind == LiveParameterRequestKind.SetPianoFxQuantizeMode || kind == LiveParameterRequestKind.SetTimeEasingEnabled
				|| kind == LiveParameterRequestKind.JogSceneTime || kind == LiveParameterRequestKind.RecallHotCue
				|| kind == LiveParameterRequestKind.RecallOppositeHotCue
				|| kind == LiveParameterRequestKind.SetMainCueFader
				|| kind == LiveParameterRequestKind.ToggleMainCue
				|| kind == LiveParameterRequestKind.SetMainCueComposite
				|| kind == LiveParameterRequestKind.ToggleMainCueComposite
				|| kind == LiveParameterRequestKind.UnassignMainCue;

		private ulong NextSequenceNumber() {
			var sequenceNumber = _nextSequenceNumber++;
			if (_nextSequenceNumber == 0) _nextSequenceNumber = 1;
			return sequenceNumber;
		}
	}

	/// <summary>Holds discrete live actions until the next shared beat.</summary>
	public sealed class LiveBeatQuantizedRequestQueue {
		private const double BeatBoundaryTolerance = 1e-9d;
		public const int Capacity = 4096;

		private readonly List<PendingRequest> m_Requests = new List<PendingRequest>();

		private readonly struct PendingRequest {
			public LiveParameterRequest Request { get; }
			public long TargetBeat { get; }

			public PendingRequest(LiveParameterRequest request, long targetBeat) {
				Request = request;
				TargetBeat = targetBeat;
			}
		}

		public bool TryEnqueue(LiveParameterRequest request, double adjustedTotalBeats, out string rejectionReason) {
			if (double.IsNaN(adjustedTotalBeats) || double.IsInfinity(adjustedTotalBeats))
				throw new ArgumentOutOfRangeException(nameof(adjustedTotalBeats));
			if (m_Requests.Count >= Capacity) {
				rejectionReason = "The beat-quantized live action queue is full.";
				return false;
			}
			m_Requests.Add(new PendingRequest(request,
				checked((long)Math.Floor(adjustedTotalBeats + BeatBoundaryTolerance) + 1L)));
			rejectionReason = string.Empty;
			return true;
		}

		public IReadOnlyList<LiveParameterRequest> DrainDue(double adjustedTotalBeats) {
			if (double.IsNaN(adjustedTotalBeats) || double.IsInfinity(adjustedTotalBeats))
				throw new ArgumentOutOfRangeException(nameof(adjustedTotalBeats));
			var reachedBeat = checked((long)Math.Floor(adjustedTotalBeats + BeatBoundaryTolerance));
			var due = new List<PendingRequest>();
			for (var index = m_Requests.Count - 1; index >= 0; index--) {
				if (m_Requests[index].TargetBeat > reachedBeat) continue;
				due.Add(m_Requests[index]);
				m_Requests.RemoveAt(index);
			}
			due.Sort((left, right) => {
				var byBeat = left.TargetBeat.CompareTo(right.TargetBeat);
				return byBeat != 0 ? byBeat : left.Request.SequenceNumber.CompareTo(right.Request.SequenceNumber);
			});
			var requests = new List<LiveParameterRequest>(due.Count);
			foreach (var item in due) requests.Add(item.Request);
			return requests;
		}

		public void Clear() => m_Requests.Clear();
	}
}
