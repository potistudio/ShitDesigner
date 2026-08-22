using System.Collections.Generic;

namespace ShitDesigner.Tests.Shared
{
    public sealed class OwnershipSnapshot
    {
        private readonly HashSet<string> _owned = new HashSet<string>();

        public IReadOnlyCollection<string> OwnedResources => _owned;

        public void Add(string resourceId)
        {
            _owned.Add(resourceId);
        }

        public bool Remove(string resourceId)
        {
            return _owned.Remove(resourceId);
        }

        public bool Contains(string resourceId)
        {
            return _owned.Contains(resourceId);
        }

        public void Clear()
        {
            _owned.Clear();
        }
    }
}
