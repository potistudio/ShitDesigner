using System;
using System.Collections.Generic;

namespace ShitDesigner.Main {
	/// <summary>Converts recent tap intervals into a tempo estimate for live input sources.</summary>
	public sealed class LiveBpmTap {
		private const double TimeoutSeconds = 2d;
		private const int SampleCount = 4;

		private readonly Queue<double> _intervals = new Queue<double>(SampleCount);
		private double _lastTapTime = double.NaN;

		public bool TryTap(double time, out float bpm) {
			bpm = 0f;
			if (double.IsNaN(time) || double.IsInfinity(time)) return false;
			if (!double.IsNaN(_lastTapTime)) {
				var interval = time - _lastTapTime;
				if (interval <= 0d || interval > TimeoutSeconds) Reset();
				else {
					_intervals.Enqueue(interval);
					while (_intervals.Count > SampleCount) _intervals.Dequeue();
					var total = 0d;
					foreach (var sample in _intervals) total += sample;
					bpm = (float)(60d / (total / _intervals.Count));
				}
			}
			_lastTapTime = time;
			return bpm > 0f;
		}

		public void Reset() {
			_intervals.Clear();
			_lastTapTime = double.NaN;
		}
	}
}
