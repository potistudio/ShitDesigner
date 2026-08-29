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
		public const float MinimumBeatAlignmentMilliseconds = -1000f;
		public const float MaximumBeatAlignmentMilliseconds = 1000f;

		private float _beatsPerMinute;
		private float m_BeatAlignmentMilliseconds;
		private double m_BeatAlignmentBeats;
		private double _totalBeats;

		public float BeatsPerMinute => _beatsPerMinute;
		public float BeatAlignmentMilliseconds => m_BeatAlignmentMilliseconds;
		public double TotalBeats => _totalBeats;
		public LiveParameterDefinition Definition => new LiveParameterDefinition("bpm", "BPM", MinimumBpm, MaximumBpm, _beatsPerMinute);
		public LiveParameterDefinition BeatAlignmentDefinition => new LiveParameterDefinition("beat-alignment-ms", "Beat Alignment (ms)", MinimumBeatAlignmentMilliseconds, MaximumBeatAlignmentMilliseconds, BeatAlignmentMilliseconds);
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

		public bool TrySetBeatAlignmentMilliseconds(float milliseconds, out string rejectionReason) {
			if (float.IsNaN(milliseconds) || float.IsInfinity(milliseconds)) {
				rejectionReason = "Beat alignment must be finite.";
				return false;
			}
			var clampedMilliseconds = Math.Min(MaximumBeatAlignmentMilliseconds, Math.Max(MinimumBeatAlignmentMilliseconds, milliseconds));
			m_BeatAlignmentMilliseconds = clampedMilliseconds;
			m_BeatAlignmentBeats = clampedMilliseconds * _beatsPerMinute / 60000d;
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
