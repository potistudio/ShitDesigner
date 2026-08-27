using System;
using ShitDesigner.Scene;

namespace ShitDesigner.Main {
	/// <summary>Owns the tempo shared by every patch in the live graph.</summary>
	public sealed class LiveBpmClock {
		public const float MinimumBpm = 30f;
		public const float MaximumBpm = 300f;
		public const float DefaultBpm = 138f;

		private float _beatsPerMinute;
		private double _totalBeats;

		public float BeatsPerMinute => _beatsPerMinute;
		public double TotalBeats => _totalBeats;
		public LiveParameterDefinition Definition => new LiveParameterDefinition("bpm", "BPM", MinimumBpm, MaximumBpm, _beatsPerMinute);
		public BpmClockState State => new BpmClockState(_beatsPerMinute, _totalBeats);

		public LiveBpmClock(float beatsPerMinute = DefaultBpm) {
			if (!TrySetBpm(beatsPerMinute, out var rejectionReason)) throw new ArgumentOutOfRangeException(nameof(beatsPerMinute), rejectionReason);
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

		public void Advance(double deltaSeconds) {
			if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
				throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "BPM clock delta must be finite and non-negative.");
			_totalBeats += deltaSeconds * _beatsPerMinute / 60d;
		}
	}
}
