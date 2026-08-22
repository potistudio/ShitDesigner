using System;

namespace ShitDesigner.Tests.Shared
{
    /// <summary>
    /// Deterministic clock for contract tests. It never reads Unity or wall-clock time.
    /// </summary>
    public sealed class ManualClock
    {
        private double _now;

        public ManualClock(double initialTime = 0d)
        {
            Set(initialTime);
        }

        public double Now => _now;

        public void Set(double time)
        {
            if (double.IsNaN(time) || double.IsInfinity(time))
            {
                throw new ArgumentOutOfRangeException(nameof(time), "Time must be finite.");
            }

            _now = time;
        }

        public double Advance(double delta)
        {
            if (double.IsNaN(delta) || double.IsInfinity(delta) || delta < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(delta), "Delta must be finite and non-negative.");
            }

            _now += delta;
            return _now;
        }
    }
}
