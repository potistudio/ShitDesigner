using System;

namespace ShitDesigner.Tests.Shared
{
    public sealed class FakeVideoBackend
    {
        public bool IsPrepared { get; private set; }
        public bool IsPlaying { get; private set; }
        public double PlayheadSeconds { get; private set; }
        public double DurationSeconds { get; set; }

        public void Prepare(double durationSeconds = 0d)
        {
            if (durationSeconds < 0d || double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            }

            DurationSeconds = durationSeconds;
            PlayheadSeconds = 0d;
            IsPrepared = true;
            IsPlaying = false;
        }

        public void Play()
        {
            EnsurePrepared();
            IsPlaying = true;
        }

        public void Pause()
        {
            EnsurePrepared();
            IsPlaying = false;
        }

        public void Seek(double seconds)
        {
            EnsurePrepared();
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
            {
                throw new ArgumentOutOfRangeException(nameof(seconds));
            }

            PlayheadSeconds = Math.Min(seconds, DurationSeconds);
        }

        public void Advance(double deltaSeconds)
        {
            EnsurePrepared();
            if (deltaSeconds < 0d || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds))
            {
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            }

            if (IsPlaying)
            {
                PlayheadSeconds = Math.Min(DurationSeconds, PlayheadSeconds + deltaSeconds);
                if (PlayheadSeconds >= DurationSeconds)
                {
                    IsPlaying = false;
                }
            }
        }

        private void EnsurePrepared()
        {
            if (!IsPrepared)
            {
                throw new InvalidOperationException("Video backend is not prepared.");
            }
        }
    }
}
