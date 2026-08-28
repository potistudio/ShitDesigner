using System;
using UnityEngine;

namespace ShitDesigner.Rendering {
	public readonly struct ShaderBeatClockFrame {
		public bool IsAvailable { get; }
		public float BeatPhase { get; }
		public float BeatPulse { get; }
		public float BarPhase { get; }

		internal ShaderBeatClockFrame(bool isAvailable, float beatPhase, float beatPulse, float barPhase) {
			IsAvailable = isAvailable;
			BeatPhase = beatPhase;
			BeatPulse = beatPulse;
			BarPhase = barPhase;
		}
	}

	/// <summary>Publishes the application tempo to the automatic shader-uniform boundary.</summary>
	public static class ShaderBeatClock {
		private static ShaderBeatClockFrame m_Current;

		public static ShaderBeatClockFrame Current => m_Current;

		public static void Publish(double totalBeats) {
			if (double.IsNaN(totalBeats) || double.IsInfinity(totalBeats) || totalBeats < 0d)
				throw new ArgumentOutOfRangeException(nameof(totalBeats));
			var beatPhase = Fraction(totalBeats);
			var barPhase = Fraction(totalBeats / 4d);
			var beatPulse = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(beatPhase * 8f));
			m_Current = new ShaderBeatClockFrame(true, beatPhase, beatPulse, barPhase);
		}

		private static float Fraction(double value) => (float)(value - Math.Floor(value));
	}
}
