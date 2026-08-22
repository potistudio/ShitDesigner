using System;

namespace ShitDesigner.Tests.Shared
{
    /// <summary>
    /// Stable, sequence-based identifiers for tests. Production UUID generation is never used.
    /// </summary>
    public sealed class FixedIdFactory
    {
        private readonly string _prefix;
        private int _next;

        public FixedIdFactory(string prefix = "test")
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                throw new ArgumentException("A non-empty prefix is required.", nameof(prefix));
            }

            _prefix = prefix;
        }

        public string Next()
        {
            return $"{_prefix}-{_next++:D4}";
        }

        public string Next(string kind)
        {
            if (string.IsNullOrWhiteSpace(kind))
            {
                throw new ArgumentException("A non-empty kind is required.", nameof(kind));
            }

            return $"{_prefix}.{kind}-{_next++:D4}";
        }

        public void Reset(int next = 0)
        {
            if (next < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(next));
            }

            _next = next;
        }
    }
}
