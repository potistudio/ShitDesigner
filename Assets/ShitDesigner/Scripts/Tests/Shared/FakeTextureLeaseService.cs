using System;
using System.Collections.Generic;

namespace ShitDesigner.Tests.Shared
{
    public readonly struct FakeTextureDescriptor : IEquatable<FakeTextureDescriptor>
    {
        public FakeTextureDescriptor(int width, int height, string format)
        {
            if (width <= 0 || height <= 0 || string.IsNullOrWhiteSpace(format))
            {
                throw new ArgumentOutOfRangeException(nameof(width), "A valid texture descriptor is required.");
            }

            Width = width;
            Height = height;
            Format = format;
        }

        public int Width { get; }
        public int Height { get; }
        public string Format { get; }

        public bool Equals(FakeTextureDescriptor other)
        {
            return Width == other.Width && Height == other.Height && string.Equals(Format, other.Format, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is FakeTextureDescriptor other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(Width, Height, Format);
        }
    }

    public sealed class FakeTextureLeaseService
    {
        private readonly Dictionary<int, FakeTextureDescriptor> _leases = new Dictionary<int, FakeTextureDescriptor>();
        private int _nextLeaseId = 1;

        public int ActiveLeaseCount => _leases.Count;

        public int Acquire(FakeTextureDescriptor descriptor)
        {
            var leaseId = _nextLeaseId++;
            _leases.Add(leaseId, descriptor);
            return leaseId;
        }

        public bool TryGet(int leaseId, out FakeTextureDescriptor descriptor)
        {
            return _leases.TryGetValue(leaseId, out descriptor);
        }

        public bool Release(int leaseId)
        {
            return _leases.Remove(leaseId);
        }

        public void ReleaseAll()
        {
            _leases.Clear();
        }
    }
}
