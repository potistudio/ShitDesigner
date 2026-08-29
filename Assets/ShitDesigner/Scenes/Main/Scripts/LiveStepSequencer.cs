using System;
using System.Collections.Generic;

namespace ShitDesigner.Main {
	public enum LiveSequencerKind {
		Overlay,
		Effect,
		CompositingMode
	}

	public readonly struct LiveSequencerOperationResult {
		public bool Accepted { get; }
		public string RejectionReason { get; }

		private LiveSequencerOperationResult(bool accepted, string rejectionReason) {
			Accepted = accepted;
			RejectionReason = rejectionReason;
		}

		internal static LiveSequencerOperationResult Accept() => new LiveSequencerOperationResult(true, string.Empty);
		internal static LiveSequencerOperationResult Reject(string reason) => new LiveSequencerOperationResult(false, reason);
	}

	public readonly struct LiveSequencerReadModel {
		private readonly int[] m_ActiveLaneMasks;
		private readonly string[] m_LanePatchIds;

		public LiveSequencerKind Kind { get; }
		public string DisplayName { get; }
		public int CurrentStep { get; }
		public int SelectedLaneIndex { get; }
		public IReadOnlyList<int> ActiveLaneMasks => m_ActiveLaneMasks ?? Array.Empty<int>();
		public IReadOnlyList<string> LanePatchIds => m_LanePatchIds ?? Array.Empty<string>();

		internal LiveSequencerReadModel(LiveSequencerKind kind, string displayName, int currentStep, int selectedLaneIndex, int[] activeLaneMasks, string[] lanePatchIds) {
			Kind = kind;
			DisplayName = displayName;
			CurrentStep = currentStep;
			SelectedLaneIndex = selectedLaneIndex;
			m_ActiveLaneMasks = activeLaneMasks ?? Array.Empty<int>();
			m_LanePatchIds = lanePatchIds ?? Array.Empty<string>();
		}

		public bool IsActive(int laneIndex, int stepIndex) {
			if (laneIndex < 0 || laneIndex >= LiveStepSequencer.LaneCount || stepIndex < 0 || stepIndex >= LiveStepSequencer.StepCount) return false;
			return ActiveLaneMasks.Count > stepIndex && (ActiveLaneMasks[stepIndex] & (1 << laneIndex)) != 0;
		}
	}

	/// <summary>Stores independent lane selections for each step in an eight-beat sequence.</summary>
	public sealed class LiveStepSequencer {
		public const int LaneCount = 4;
		public const int StepCount = 8;

		private readonly int[] m_ActiveLaneMasks = new int[StepCount];
		private readonly string[] m_LanePatchIds = new string[LaneCount];

		public LiveSequencerKind Kind { get; }
		public string DisplayName { get; }
		public int SelectedLaneIndex { get; private set; } = -1;

		public LiveStepSequencer(LiveSequencerKind kind, string displayName) {
			if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A sequencer display name is required.", nameof(displayName));
			Kind = kind;
			DisplayName = displayName;
		}

		public LiveSequencerOperationResult Toggle(int laneIndex, int stepIndex) {
			if (laneIndex < 0 || laneIndex >= LaneCount) return LiveSequencerOperationResult.Reject("The sequencer lane does not exist.");
			if (stepIndex < 0 || stepIndex >= StepCount) return LiveSequencerOperationResult.Reject("The sequencer step does not exist.");
			m_ActiveLaneMasks[stepIndex] ^= 1 << laneIndex;
			return LiveSequencerOperationResult.Accept();
		}

		public LiveSequencerOperationResult SelectLane(int laneIndex) {
			if (laneIndex < 0 || laneIndex >= LaneCount) return LiveSequencerOperationResult.Reject("The sequencer lane does not exist.");
			SelectedLaneIndex = SelectedLaneIndex == laneIndex ? -1 : laneIndex;
			return LiveSequencerOperationResult.Accept();
		}

		public LiveSequencerOperationResult AssignSelectedLane(string patchId) {
			if (SelectedLaneIndex < 0) return LiveSequencerOperationResult.Reject("Select a sequencer lane first.");
			if (string.IsNullOrWhiteSpace(patchId)) return LiveSequencerOperationResult.Reject("An overlay scene ID is required.");
			m_LanePatchIds[SelectedLaneIndex] = patchId;
			SelectedLaneIndex = -1;
			return LiveSequencerOperationResult.Accept();
		}

		public IReadOnlyList<string> GetTriggeredPatchIds(int stepIndex) {
			if (stepIndex < 0 || stepIndex >= StepCount) return Array.Empty<string>();
			var triggered = new List<string>();
			for (var laneIndex = 0; laneIndex < LaneCount; laneIndex++) {
				if ((m_ActiveLaneMasks[stepIndex] & (1 << laneIndex)) == 0 || string.IsNullOrEmpty(m_LanePatchIds[laneIndex])) continue;
				triggered.Add(m_LanePatchIds[laneIndex]);
			}
			return triggered;
		}

		public LiveSequencerReadModel CreateReadModel(double adjustedTotalBeats) {
			if (double.IsNaN(adjustedTotalBeats) || double.IsInfinity(adjustedTotalBeats))
				throw new ArgumentOutOfRangeException(nameof(adjustedTotalBeats), "Sequencer timing must be finite.");
			var beat = (long)Math.Floor(adjustedTotalBeats);
			var currentStep = (int)((beat % StepCount + StepCount) % StepCount);
			return new LiveSequencerReadModel(Kind, DisplayName, currentStep, SelectedLaneIndex, (int[])m_ActiveLaneMasks.Clone(), (string[])m_LanePatchIds.Clone());
		}
	}
}
