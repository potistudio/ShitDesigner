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
		private readonly int m_Output2CopyLaneMask;

		public LiveSequencerKind Kind { get; }
		public string DisplayName { get; }
		public int CurrentStep { get; }
		public int SelectedLaneIndex { get; }
		public int LaneCount => m_LanePatchIds?.Length ?? 0;
		public IReadOnlyList<int> ActiveLaneMasks => m_ActiveLaneMasks ?? Array.Empty<int>();
		public IReadOnlyList<string> LanePatchIds => m_LanePatchIds ?? Array.Empty<string>();

		internal LiveSequencerReadModel(LiveSequencerKind kind, string displayName, int currentStep, int selectedLaneIndex, int[] activeLaneMasks,
			string[] lanePatchIds, LiveSequencerCellMode[] cellModes, int output2CopyLaneMask) {
			Kind = kind;
			DisplayName = displayName;
			CurrentStep = currentStep;
			SelectedLaneIndex = selectedLaneIndex;
			m_ActiveLaneMasks = activeLaneMasks ?? Array.Empty<int>();
			m_LanePatchIds = lanePatchIds ?? Array.Empty<string>();
			m_CellModes = cellModes ?? Array.Empty<LiveSequencerCellMode>();
			m_Output2CopyLaneMask = output2CopyLaneMask;
		}

		public bool IsActive(int laneIndex, int stepIndex) {
			if (laneIndex < 0 || laneIndex >= LaneCount || stepIndex < 0 || stepIndex >= LiveStepSequencer.StepCount) return false;
			return ActiveLaneMasks.Count > stepIndex && (ActiveLaneMasks[stepIndex] & (1 << laneIndex)) != 0;
		}

		public LiveSequencerCellMode GetCellMode(int laneIndex, int stepIndex) {
			if (laneIndex < 0 || laneIndex >= LaneCount || stepIndex < 0 || stepIndex >= LiveStepSequencer.StepCount)
				return LiveSequencerCellMode.Off;
			var index = laneIndex * LiveStepSequencer.StepCount + stepIndex;
			return m_CellModes != null && index < m_CellModes.Length ? m_CellModes[index] : LiveSequencerCellMode.Off;
		}

		public bool IsCopiedToOutput2(int laneIndex) {
			return laneIndex >= 0 && laneIndex < LaneCount && (m_Output2CopyLaneMask & (1 << laneIndex)) != 0;
		}

		public IReadOnlyList<LiveSequencerLayer> GetActiveLayers() {
			var layers = new List<LiveSequencerLayer>();
			for (var laneIndex = 0; laneIndex < LaneCount; laneIndex++) {
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
		public const int OverlayLaneCount = 16;
		public const int EffectLaneCount = 4;
		public const int StepCount = 8;

		private readonly int[] m_ActiveLaneMasks = new int[StepCount];
		private readonly string[] m_LanePatchIds;
		private readonly LiveSequencerCellMode[] m_CellModes;
		private int m_Output2CopyLaneMask;

		public LiveSequencerKind Kind { get; }
		public string DisplayName { get; }
		public int LaneCount { get; }
		public int SelectedLaneIndex { get; private set; } = -1;

		public LiveStepSequencer(LiveSequencerKind kind, string displayName) {
			if (string.IsNullOrWhiteSpace(displayName)) throw new ArgumentException("A sequencer display name is required.", nameof(displayName));
			Kind = kind;
			DisplayName = displayName;
			LaneCount = kind == LiveSequencerKind.Overlay ? OverlayLaneCount : EffectLaneCount;
			m_LanePatchIds = new string[LaneCount];
			m_CellModes = new LiveSequencerCellMode[LaneCount * StepCount];
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

		public LiveSequencerOperationResult TurnOnCell(int laneIndex, int stepIndex) {
			if (laneIndex < 0 || laneIndex >= LaneCount) return LiveSequencerOperationResult.Reject("The sequencer lane does not exist.");
			if (stepIndex < 0 || stepIndex >= StepCount) return LiveSequencerOperationResult.Reject("The sequencer step does not exist.");
			var cellIndex = laneIndex * StepCount + stepIndex;
			if (m_CellModes[cellIndex] == LiveSequencerCellMode.Off) m_CellModes[cellIndex] = LiveSequencerCellMode.Normal;
			m_ActiveLaneMasks[stepIndex] |= 1 << laneIndex;
			return LiveSequencerOperationResult.Accept();
		}

		public LiveSequencerOperationResult SelectLane(int laneIndex) {
			if (laneIndex < 0 || laneIndex >= LaneCount) return LiveSequencerOperationResult.Reject("The sequencer lane does not exist.");
			SelectedLaneIndex = SelectedLaneIndex == laneIndex ? -1 : laneIndex;
			return LiveSequencerOperationResult.Accept();
		}

		public LiveSequencerOperationResult AssignSelectedLane(string patchId) {
			if (SelectedLaneIndex < 0) return LiveSequencerOperationResult.Reject("Select a sequencer lane first.");
			var result = AssignLane(SelectedLaneIndex, patchId);
			if (result.Accepted) SelectedLaneIndex = -1;
			return result;
		}

		public LiveSequencerOperationResult AssignLane(int laneIndex, string patchId) {
			if (laneIndex < 0 || laneIndex >= LaneCount) return LiveSequencerOperationResult.Reject("The sequencer lane does not exist.");
			if (string.IsNullOrWhiteSpace(patchId)) return LiveSequencerOperationResult.Reject("An overlay scene ID is required.");
			m_LanePatchIds[laneIndex] = patchId;
			return LiveSequencerOperationResult.Accept();
		}

		public LiveSequencerOperationResult ClearLane(int laneIndex) {
			if (laneIndex < 0 || laneIndex >= LaneCount) return LiveSequencerOperationResult.Reject("The sequencer lane does not exist.");
			m_LanePatchIds[laneIndex] = string.Empty;
			for (var stepIndex = 0; stepIndex < StepCount; stepIndex++) {
				m_CellModes[laneIndex * StepCount + stepIndex] = LiveSequencerCellMode.Off;
				m_ActiveLaneMasks[stepIndex] &= ~(1 << laneIndex);
			}
			if (SelectedLaneIndex == laneIndex) SelectedLaneIndex = -1;
			return LiveSequencerOperationResult.Accept();
		}

		public LiveSequencerOperationResult ToggleOutput2Copy(int laneIndex) {
			if (Kind != LiveSequencerKind.Overlay) return LiveSequencerOperationResult.Reject("Only overlay lanes can be copied to Output 2.");
			if (laneIndex < 0 || laneIndex >= LaneCount) return LiveSequencerOperationResult.Reject("The sequencer lane does not exist.");
			m_Output2CopyLaneMask ^= 1 << laneIndex;
			return LiveSequencerOperationResult.Accept();
		}

		public LiveSequencerReadModel CreateReadModel(double adjustedTotalBeats, IReadOnlyList<int> laneTakeOverrides = null) {
			if (double.IsNaN(adjustedTotalBeats) || double.IsInfinity(adjustedTotalBeats))
				throw new ArgumentOutOfRangeException(nameof(adjustedTotalBeats), "Sequencer timing must be finite.");
			var beat = (long)Math.Floor(adjustedTotalBeats);
			var currentStep = (int)((beat % StepCount + StepCount) % StepCount);
			var activeLaneMasks = (int[])m_ActiveLaneMasks.Clone();
			var cellModes = (LiveSequencerCellMode[])m_CellModes.Clone();
			if (laneTakeOverrides != null) {
				for (var laneIndex = 0; laneIndex < Math.Min(LaneCount, laneTakeOverrides.Count); laneIndex++) {
					var takeOverride = laneTakeOverrides[laneIndex];
					if (takeOverride < 0) continue;
					var cellIndex = laneIndex * StepCount + currentStep;
					cellModes[cellIndex] = takeOverride == 0 ? LiveSequencerCellMode.Off : LiveSequencerCellMode.Normal;
					if (takeOverride == 0) activeLaneMasks[currentStep] &= ~(1 << laneIndex);
					else activeLaneMasks[currentStep] |= 1 << laneIndex;
				}
			}
			return new LiveSequencerReadModel(Kind, DisplayName, currentStep, SelectedLaneIndex, activeLaneMasks,
				(string[])m_LanePatchIds.Clone(), cellModes, m_Output2CopyLaneMask);
		}
	}
}
