using System;
using System.Collections.Generic;

namespace ShitDesigner.Tests.Shared
{
    public sealed class FaultInjectingFileSystem
    {
        private readonly Dictionary<string, byte[]> _files = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        public bool FailReads { get; set; }
        public bool FailWrites { get; set; }

        public bool Exists(string path)
        {
            return _files.ContainsKey(Normalize(path));
        }

        public void Write(string path, byte[] contents)
        {
            if (FailWrites)
            {
                throw new InvalidOperationException("Injected file write failure.");
            }

            if (contents == null)
            {
                throw new ArgumentNullException(nameof(contents));
            }

            _files[Normalize(path)] = (byte[])contents.Clone();
        }

        public byte[] Read(string path)
        {
            if (FailReads)
            {
                throw new InvalidOperationException("Injected file read failure.");
            }

            if (!_files.TryGetValue(Normalize(path), out var contents))
            {
                throw new System.IO.FileNotFoundException(path);
            }

            return (byte[])contents.Clone();
        }

        public bool Delete(string path)
        {
            return _files.Remove(Normalize(path));
        }

        private static string Normalize(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("A path is required.", nameof(path));
            }

            return path.Replace('\\', '/');
        }
    }
}
