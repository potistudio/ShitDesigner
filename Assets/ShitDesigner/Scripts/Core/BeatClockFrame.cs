using System;

namespace ShitDesigner.Core {
	/// <summary>Resolved shared tempo values for one live-graph evaluation frame.</summary>
	public readonly struct BeatClockFrame {
		public float Bpm { get; }
		public float BeatsPerMinute => Bpm;
		public double TotalBeats { get; }
		public float BeatPhase { get; }
		public float BeatPulse { get; }
		public float BarPhase { get; }
		public bool IsAvailable { get; }

		public BeatClockFrame(float bpm, double totalBeats) {
			if (float.IsNaN(bpm) || float.IsInfinity(bpm) || bpm <= 0f)
				throw new ArgumentOutOfRangeException(nameof(bpm), "BPM must be positive and finite.");
			if (double.IsNaN(totalBeats) || double.IsInfinity(totalBeats) || totalBeats < 0d)
				throw new ArgumentOutOfRangeException(nameof(totalBeats), "Total beats must be non-negative and finite.");

			Bpm = bpm;
			TotalBeats = totalBeats;
			BeatPhase = Fraction(totalBeats);
			BeatPulse = 1f - SmoothStep(Clamp01(BeatPhase * 8f));
			BarPhase = Fraction(totalBeats / 4d);
			IsAvailable = true;
		}

		private static float Fraction(double value) => (float)(value - Math.Floor(value));
		private static float Clamp01(float value) => Math.Min(1f, Math.Max(0f, value));
		private static float SmoothStep(float value) => value * value * (3f - 2f * value);
	}
}
