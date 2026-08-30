using System;
using System.Collections.Generic;

namespace ShitDesigner.Main {
	public enum LiveSequencerKind {
		Overlay,
		Effect
	}

	public enum LiveSequencerCellMode {
		Off,
		Normal,
		Add,
		Multiply,
		Subtract,
		Difference,
		Invert
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

	public readonly struct LiveSequencerLayer {
		public int LaneIndex { get; }
		public string PatchId { get; }
		public LiveSequencerCellMode Mode { get; }

		internal LiveSequencerLayer(int laneIndex, string patchId, LiveSequencerCellMode mode) {
			LaneIndex = laneIndex;
			PatchId = patchId;
			Mode = mode;
		}
	}

	public readonly struct LiveSequencerReadModel {
		private readonly int[] m_ActiveLaneMasks;
		private readonly string[] m_LanePatchIds;
		private readonly LiveSequencerCellMode[] m_CellModes;

		public LiveSequencerKind Kind { get; }
		public string DisplayName { get; }
		public int CurrentStep { get; }
		public int SelectedLaneIndex { get; }
		public IReadOnlyList<int> ActiveLaneMasks => m_ActiveLaneMasks ?? Array.Empty<int>();
		public IReadOnlyList<string> LanePatchIds => m_LanePatchIds ?? Array.Empty<string>();

		internal LiveSequencerReadModel(LiveSequencerKind kind, string displayName, int currentStep, int selectedLaneIndex, int[] activeLaneMasks,
			string[] lanePatchIds, LiveSequencerCellMode[] cellModes) {
			Kind = kind;
			DisplayName = displayName;
			CurrentStep = currentStep;
			SelectedLaneIndex = selectedLaneIndex;
			m_ActiveLaneMasks = activeLaneMasks ?? Array.Empty<int>();
			m_LanePatchIds = lanePatchIds ?? Array.Empty<string>();
			m_CellModes = cellModes ?? Array.Empty<LiveSequencerCellMode>();
		}

		public bool IsActive(int laneIndex, int stepIndex) {
			if (laneIndex < 0 || laneIndex >= LiveStepSequencer.LaneCount || stepIndex < 0 || stepIndex >= LiveStepSequencer.StepCount) return false;
			return ActiveLaneMasks.Count > stepIndex && (ActiveLaneMasks[stepIndex] & (1 << laneIndex)) != 0;
		}

		public LiveSequencerCellMode GetCellMode(int laneIndex, int stepIndex) {
			if (laneIndex < 0 || laneIndex >= LiveStepSequencer.LaneCount || stepIndex < 0 || stepIndex >= LiveStepSequencer.StepCount)
				return LiveSequencerCellMode.Off;
			var index = laneIndex * LiveStepSequencer.StepCount + stepIndex;
			return m_CellModes != null && index < m_CellModes.Length ? m_CellModes[index] : LiveSequencerCellMode.Off;
		}

		public IReadOnlyList<LiveSequencerLayer> GetActiveLayers() {
			var layers = new List<LiveSequencerLayer>();
			for (var laneIndex = 0; laneIndex < LiveStepSequencer.LaneCount; laneIndex++) {
				var mode = GetCellMode(laneIndex, CurrentStep);
				var patchId = LanePatchIds.Count > laneIndex ? LanePatchIds[laneIndex] : string.Empty;
				if (mode == LiveSequencerCellMode.Off || string.IsNullOrEmpty(patchId)) continue;
				layers.Add(new LiveSequencerLayer(laneIndex, patchId, mode));
			}
			return layers;
		}
	}

	/// <summary>Stores an independent compositing mode for every lane and step in an eight-beat sequence.</summary>
	public sealed class LiveStepSequencer {
		public const int LaneCount = 4;
		public const int StepCount = 8;

		private readonly int[] m_ActiveLaneMasks = new int[StepCount];
		private readonly string[] m_LanePatchIds = new string[LaneCount];
		private readonly LiveSequencerCellMode[] m_CellModes = new LiveSequencerCellMode[LaneCount * StepCount];

		public LiveSequencerKind Kind { get; }
		public string DisplayName { get; }
		public int SelectedLaneIndex { get; private set; } = -1;

		public LiveStepSequencer(LiveSequencerKind kind, string displayName) {
			if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A sequencer display name is required.", nameof(displayName));
			Kind = kind;
			DisplayName = displayName;
		}

		public LiveSequencerOperationResult CycleCellMode(int laneIndex, int stepIndex) {
			if (laneIndex < 0 || laneIndex >= LaneCount) return LiveSequencerOperationResult.Reject("The sequencer lane does not exist.");
			if (stepIndex < 0 || stepIndex >= StepCount) return LiveSequencerOperationResult.Reject("The sequencer step does not exist.");
			var cellIndex = laneIndex * StepCount + stepIndex;
			var nextMode = m_CellModes[cellIndex] == LiveSequencerCellMode.Invert
				? LiveSequencerCellMode.Off
				: (LiveSequencerCellMode)((int)m_CellModes[cellIndex] + 1);
			m_CellModes[cellIndex] = nextMode;
			if (nextMode == LiveSequencerCellMode.Off) m_ActiveLaneMasks[stepIndex] &= ~(1 << laneIndex);
			else m_ActiveLaneMasks[stepIndex] |= 1 << laneIndex;
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

		public LiveSequencerReadModel CreateReadModel(double adjustedTotalBeats) {
			if (double.IsNaN(adjustedTotalBeats) || double.IsInfinity(adjustedTotalBeats))
				throw new ArgumentOutOfRangeException(nameof(adjustedTotalBeats), "Sequencer timing must be finite.");
			var beat = (long)Math.Floor(adjustedTotalBeats);
			var currentStep = (int)((beat % StepCount + StepCount) % StepCount);
			return new LiveSequencerReadModel(Kind, DisplayName, currentStep, SelectedLaneIndex, (int[])m_ActiveLaneMasks.Clone(),
				(string[])m_LanePatchIds.Clone(), (LiveSequencerCellMode[])m_CellModes.Clone());
		}
	}
}
