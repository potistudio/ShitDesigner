using System;
using System.Collections.Generic;

namespace ShitDesigner.Tests.Shared
{
    public sealed class FakeCapabilityProbe
    {
        private readonly Dictionary<string, bool> _capabilities = new Dictionary<string, bool>(StringComparer.Ordinal);

        public void Set(string capability, bool supported)
        {
            if (string.IsNullOrWhiteSpace(capability))
            {
                throw new ArgumentException("A capability name is required.", nameof(capability));
            }

            _capabilities[capability] = supported;
        }

        public bool IsSupported(string capability)
        {
            return _capabilities.TryGetValue(capability, out var supported) && supported;
        }
    }
}
