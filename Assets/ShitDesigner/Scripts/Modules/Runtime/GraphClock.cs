using System;
using System.Diagnostics;

namespace ShitDesigner.Runtime {
	public interface IMonotonicClock {
		double Now { get; }
	}

	public sealed class SystemMonotonicClock : IMonotonicClock {
		private readonly long _origin = Stopwatch.GetTimestamp();
		public double Now => (Stopwatch.GetTimestamp() - _origin) / (double)Stopwatch.Frequency;
	}

	public readonly struct PhysicsStepResult {
		public int StepCount { get; }
		public double RemainderSeconds { get; }
		public PhysicsStepResult(int stepCount, double remainderSeconds) {
			StepCount = stepCount;
			RemainderSeconds = remainderSeconds;
		}
	}

	/// <summary>
	/// Project-local clock. It is driven by a monotonic source and is not
	/// affected by Unity Time.timeScale or the active display state.
	/// </summary>
	public sealed class GraphClock {
		public const double FixedStepSeconds = 1d / 60d;
		public const int MaxPhysicsStepsPerFrame = 4;

		private readonly IMonotonicClock _source;
		private double _lastSourceTime;
		private double _time;
		private double _physicsAccumulator;
		private bool _initialized;

		public double Time => _time;
		public bool IsPaused { get; private set; }
		public double LastDelta { get; private set; }
		public PhysicsStepResult LastPhysicsSteps { get; private set; }

		public GraphClock(IMonotonicClock source = null) {
			_source = source ?? new SystemMonotonicClock();
			_lastSourceTime = _source.Now;
			_initialized = true;
			LastPhysicsSteps = new PhysicsStepResult(0, 0d);
		}

		public void Pause() {
			IsPaused = true;
			LastDelta = 0d;
		}

		public void Resume() {
			IsPaused = false;
			LastDelta = 0d;
		}

		public PhysicsStepResult Update() {
			return Update(_source.Now);
		}

		/// <summary>Reads the monotonic source without advancing graph time.</summary>
		public double ReadMonotonicTime() => _source.Now;

		public PhysicsStepResult Update(double monotonicTime) {
			EnsureFinite(monotonicTime, nameof(monotonicTime));
			if (!_initialized) {
				_lastSourceTime = monotonicTime;
				_initialized = true;
				LastDelta = 0d;
				LastPhysicsSteps = new PhysicsStepResult(0, _physicsAccumulator);
				return LastPhysicsSteps;
			}

			var delta = Math.Max(0d, monotonicTime - _lastSourceTime);
			_lastSourceTime = monotonicTime;
			if (IsPaused) {
				LastDelta = 0d;
				LastPhysicsSteps = new PhysicsStepResult(0, _physicsAccumulator);
				return LastPhysicsSteps;
			}

			_time += delta;
			LastDelta = delta;
			_physicsAccumulator += delta;
			var steps = Math.Min(MaxPhysicsStepsPerFrame, (int)Math.Floor(_physicsAccumulator / FixedStepSeconds));
			_physicsAccumulator -= steps * FixedStepSeconds;
			LastPhysicsSteps = new PhysicsStepResult(steps, _physicsAccumulator);
			return LastPhysicsSteps;
		}

		public void Reset(double time = 0d) {
			Reset(time, ReadMonotonicTime());
		}

		/// <summary>Resets using the already captured Phase-0 source time.</summary>
		public void Reset(double time, double monotonicTime) {
			EnsureFinite(time, nameof(time));
			EnsureFinite(monotonicTime, nameof(monotonicTime));
			_time = time;
			_physicsAccumulator = 0d;
			_lastSourceTime = monotonicTime;
			LastDelta = 0d;
			LastPhysicsSteps = new PhysicsStepResult(0, 0d);
		}

		private static void EnsureFinite(double value, string name) {
			if (double.IsNaN(value) || double.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, "Graph clock time must be finite.");
		}
	}
}
