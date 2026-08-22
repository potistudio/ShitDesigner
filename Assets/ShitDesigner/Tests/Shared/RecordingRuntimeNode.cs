using System.Collections.Generic;

namespace ShitDesigner.Tests.Shared
{
    public sealed class RecordingRuntimeNode : FakeRuntimeNode
    {
        private readonly List<int> _frames = new List<int>();

        public RecordingRuntimeNode(string nodeId) : base(nodeId)
        {
        }

        public IReadOnlyList<int> EvaluatedFrames => _frames;

        public void Evaluate(int frameNumber)
        {
            base.Evaluate();
            _frames.Add(frameNumber);
        }
    }
}
