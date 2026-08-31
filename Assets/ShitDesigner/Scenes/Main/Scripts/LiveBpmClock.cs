using System;
using ShitDesigner.Core;
using ShitDesigner.Rendering;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Main {
	internal sealed class LiveTimeWarp {
		private const int SegmentCount = 256;
		private readonly double[] m_Samples = new double[SegmentCount + 1];

		public LiveTimeWarp(AnimationCurve curve) {
			var source = curve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
			var start = source.Evaluate(0f);
			var end = source.Evaluate(1f);
			if (!IsFinite(start) || !IsFinite(end) || Math.Abs(end - start) < 1e-6f) {
				FillLinear();
				return;
			}

			m_Samples[0] = 0d;
			var previous = 0d;
			for (var index = 1; index < SegmentCount; index++) {
				var phase = index / (float)SegmentCount;
				var value = source.Evaluate(phase);
				double normalized = IsFinite(value) ? (value - start) / (end - start) : phase;
				normalized = Math.Min(1d, Math.Max(0d, normalized));
				previous = Math.Max(previous, normalized);
				m_Samples[index] = previous;
			}
			m_Samples[SegmentCount] = 1d;
		}

		public double DeltaSeconds(double previousAdjustedBeats, double currentAdjustedBeats, float bpm) {
			if (currentAdjustedBeats <= previousAdjustedBeats) return 0d;
			var warpedBeatDelta = Warp(currentAdjustedBeats) - Warp(previousAdjustedBeats);
			return Math.Max(0d, warpedBeatDelta) * 60d / bpm;
		}

		private double Warp(double adjustedBeats) {
			var wholeBeats = Math.Floor(adjustedBeats);
			return wholeBeats + Evaluate(adjustedBeats - wholeBeats);
		}

		private double Evaluate(double phase) {
			var scaled = Math.Min(1d, Math.Max(0d, phase)) * SegmentCount;
			var lower = Math.Min(SegmentCount - 1, (int)Math.Floor(scaled));
			var fraction = scaled - lower;
			return m_Samples[lower] + (m_Samples[lower + 1] - m_Samples[lower]) * fraction;
		}

		private void FillLinear() {
			for (var index = 0; index <= SegmentCount; index++) m_Samples[index] = index / (double)SegmentCount;
		}

		private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
	}

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
		private readonly LiveTimeWarp m_TimeWarp;
		private bool m_IsTimeEasingEnabled = true;

		public float BeatsPerMinute => _beatsPerMinute;
		public double TotalBeats => _totalBeats;
		public bool IsTimeEasingEnabled => m_IsTimeEasingEnabled;
		public LiveParameterDefinition Definition => new LiveParameterDefinition("bpm", "BPM", MinimumBpm, MaximumBpm, _beatsPerMinute);
		public BeatClockFrame Frame => new BeatClockFrame(_beatsPerMinute, _totalBeats, m_BeatAlignmentBeats);

		public LiveBpmClock(float beatsPerMinute = DefaultBpm, AnimationCurve globalTimeEasing = null) {
			m_TimeWarp = new LiveTimeWarp(globalTimeEasing);
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

		public void SetTimeEasingEnabled(bool enabled) => m_IsTimeEasingEnabled = enabled;

		public double Advance(double deltaSeconds) {
			if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
				throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "BPM clock delta must be finite and non-negative.");
			var previousAdjustedBeats = _totalBeats - m_BeatAlignmentBeats;
			_totalBeats += deltaSeconds * _beatsPerMinute / 60d;
			var graphDeltaSeconds = m_IsTimeEasingEnabled
				? m_TimeWarp.DeltaSeconds(previousAdjustedBeats, _totalBeats - m_BeatAlignmentBeats, _beatsPerMinute)
				: deltaSeconds;
			ShaderBeatClock.Publish(Frame);
			return graphDeltaSeconds;
		}

		public double ProjectGraphDelta(double deltaSeconds) {
			if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
				throw new ArgumentOutOfRangeException(nameof(deltaSeconds), "Projected graph delta must be finite and non-negative.");
			if (!m_IsTimeEasingEnabled) return deltaSeconds;
			var previousAdjustedBeats = _totalBeats - m_BeatAlignmentBeats;
			var projectedAdjustedBeats = previousAdjustedBeats + deltaSeconds * _beatsPerMinute / 60d;
			return m_TimeWarp.DeltaSeconds(previousAdjustedBeats, projectedAdjustedBeats, _beatsPerMinute);
		}
	}
}
