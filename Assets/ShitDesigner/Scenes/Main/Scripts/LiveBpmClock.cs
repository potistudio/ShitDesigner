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
		public const float MinimumBeatOffsetMilliseconds = -1000f;
		public const float MaximumBeatOffsetMilliseconds = 1000f;

		private float _beatsPerMinute;
		private float m_BeatOffsetMilliseconds;
		private double _totalBeats;

		public float BeatsPerMinute => _beatsPerMinute;
		public float BeatOffsetMilliseconds => m_BeatOffsetMilliseconds;
		public double TotalBeats => _totalBeats;
		public LiveParameterDefinition Definition => new LiveParameterDefinition("bpm", "BPM", MinimumBpm, MaximumBpm, _beatsPerMinute);
		public LiveParameterDefinition BeatOffsetDefinition => new LiveParameterDefinition("beat-offset-ms", "Beat Offset (ms)", MinimumBeatOffsetMilliseconds, MaximumBeatOffsetMilliseconds, m_BeatOffsetMilliseconds);
		public BeatClockFrame Frame => new BeatClockFrame(_beatsPerMinute, _totalBeats, TimingOffsetBeats);

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

		public bool TrySetBeatOffsetMilliseconds(float milliseconds, out string rejectionReason) {
			if (float.IsNaN(milliseconds) || float.IsInfinity(milliseconds)) {
				rejectionReason = "Beat offset must be finite.";
				return false;
			}
			m_BeatOffsetMilliseconds = Math.Min(MaximumBeatOffsetMilliseconds, Math.Max(MinimumBeatOffsetMilliseconds, milliseconds));
			rejectionReason = string.Empty;
			return true;
		}

		public void Advance(double deltaSeconds) {
			if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
				throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "BPM clock delta must be finite and non-negative.");
			_totalBeats += deltaSeconds * _beatsPerMinute / 60d;
			ShaderBeatClock.Publish(Frame);
		}

		private double TimingOffsetBeats => m_BeatOffsetMilliseconds * _beatsPerMinute / 60000d;
	}
}
