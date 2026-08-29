using System;
using ShitDesigner.Core;
using ShitDesigner.Rendering;
using ShitDesigner.Scene;

namespace ShitDesigner.Main {
	/// <summary>
	/// Owns the tempo shared by every patch in the live graph.
	/// </summary>
	public sealed class LiveBpmClock {
		public const float MinimumBpm = 30f;
		public const float MaximumBpm = 300f;
		public const float DefaultBpm = 138f;

		private float _beatsPerMinute;
		private double m_BeatAlignmentBeats;
		private double _totalBeats;

		public float BeatsPerMinute => _beatsPerMinute;
		public double TotalBeats => _totalBeats;
		public LiveParameterDefinition Definition => new LiveParameterDefinition("bpm", "BPM", MinimumBpm, MaximumBpm, _beatsPerMinute);
		public BeatClockFrame Frame => new BeatClockFrame(_beatsPerMinute, _totalBeats, m_BeatAlignmentBeats);

		public LiveBpmClock(float beatsPerMinute = DefaultBpm) {
			if (!TrySetBpm(beatsPerMinute, out var rejectionReason)) throw new ArgumentOutOfRangeException(nameof(beatsPerMinute), rejectionReason);
			ShaderBeatClock.Publish(Frame);
		}

		public bool TrySetBpm(float beatsPerMinute, out string rejectionReason) {
			if (float.IsNaN(beatsPerMinute) || float.IsInfinity(beatsPerMinute)) {
				rejectionReason = "BPM must be finite.";
				return false;
			}
			_beatsPerMinute = Math.Min(MaximumBpm, Math.Max(MinimumBpm, beatsPerMinute));
			rejectionReason = string.Empty;
			return true;
		}

		public bool TryAlignToNearestBeat(out string rejectionReason) {
			var adjustedTotalBeats = Frame.AdjustedTotalBeats;
			if (double.IsNaN(adjustedTotalBeats) || double.IsInfinity(adjustedTotalBeats)) {
				rejectionReason = "Beat alignment requires finite clock values.";
				return false;
			}
			var nearestBeat = Math.Round(adjustedTotalBeats, MidpointRounding.AwayFromZero);
			m_BeatAlignmentBeats += adjustedTotalBeats - nearestBeat;
			rejectionReason = string.Empty;
			return true;
		}

		public void Advance(double deltaSeconds) {
			if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
				throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "BPM clock delta must be finite and non-negative.");
			_totalBeats += deltaSeconds * _beatsPerMinute / 60d;
			ShaderBeatClock.Publish(Frame);
		}
	}
}
