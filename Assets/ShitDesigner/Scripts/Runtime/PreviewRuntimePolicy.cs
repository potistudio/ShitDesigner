using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;

namespace ShitDesigner.Runtime
{
    /// <summary>Runtime-neutral demand passed to the Preview quality policy.</summary>
    public sealed class RuntimePreviewDemand
    {
        public NodeInstanceId NodeId { get; }
        public PortId OutputPortId { get; }
        public int Width { get; }
        public int Height { get; }
        public bool Focused { get; }
        public long FocusTimestamp { get; }

        public RuntimePreviewDemand(NodeInstanceId nodeId, PortId outputPortId, int width, int height,
            bool focused = false, long focusTimestamp = 0)
        {
            if (nodeId.IsEmpty || outputPortId.IsEmpty || width <= 0 || height <= 0)
                throw new System.ArgumentException("Preview demand identity and dimensions are required.");
            NodeId = nodeId;
            OutputPortId = outputPortId;
            Width = width;
            Height = height;
            Focused = focused;
            FocusTimestamp = focusTimestamp;
        }
    }

    /// <summary>
    /// Runtime-owned seam for the Preview quality policy. Rendering supplies
    /// the production implementation, while Runtime keeps demand planning
    /// independent from Unity and Rendering assemblies.
    /// </summary>
    public interface IRuntimePreviewQualityPolicy
    {
        /// <summary>Advances only when a captured Preview descriptor can
        /// change (membership or quality stage), not for ordinary cadence.</summary>
        long Revision { get; }
        void Ensure(NodeInstanceId previewNodeId, bool focused, long focusTimestamp);
        /// <summary>Releases a Preview's quality-controller state when its
        /// Viewer tab is closed. Hiding a host must not call this method,
        /// because the saved tab assignment is expected to resume at its
        /// current quality when the host is shown again.</summary>
        void Remove(NodeInstanceId previewNodeId);
        bool IsDue(NodeInstanceId previewNodeId, ulong frameNumber);
        RuntimePreviewDemand Apply(RuntimePreviewDemand demand);
        void Observe(double cpuMilliseconds, double gpuMilliseconds, ulong frameNumber);
        void ObserveProgramWarning(ulong frameNumber);
        RuntimePreviewOutputSnapshot Capture(NodeInstanceId previewNodeId);
    }

    /// <summary>Runtime-only fallback used by headless and EditMode hosts.</summary>
    public sealed class DefaultPreviewQualityPolicy : IRuntimePreviewQualityPolicy
    {
        private readonly Dictionary<NodeInstanceId, PreviewQualityController> _controllers = new Dictionary<NodeInstanceId, PreviewQualityController>();
        private readonly Queue<double> _cpuSamples = new Queue<double>();
        private readonly Queue<double> _gpuSamples = new Queue<double>();
        private ulong _focusSequence;
        private double _cpuSum;
        private double _gpuSum;
        private int _highFrameCount;
        private int _lowFrameCount;
        private bool _lowQualified;
        private ulong _lastQualityChangeFrame;
        private long _revision;

        public long Revision => _revision;

        private PreviewQualityController EnsureController(NodeInstanceId id)
        {
            if (!_controllers.TryGetValue(id, out var controller))
            {
                controller = new PreviewQualityController();
                // A never-focused Preview still needs a deterministic stable
                // order. A supplied FocusTimestamp supersedes this creation
                // order once the viewer focuses the Preview.
                controller.SetFocus(false, (long)++_focusSequence);
                _controllers[id] = controller;
                _revision++;
            }
            return controller;
        }

        public void Ensure(NodeInstanceId previewNodeId, bool focused, long focusTimestamp)
        {
            var controller = EnsureController(previewNodeId);
            if (focused)
            {
                foreach (var pair in _controllers)
                    if (pair.Key != previewNodeId)
                        pair.Value.SetFocus(false, pair.Value.LastFocusSequence);
                // Re-publishing the selected tab each frame must not change
                // stable focus order. Allocate/update order only on the
                // false-to-true transition.
                if (!controller.IsFocused)
                {
                    var sequence = focusTimestamp != 0 ? focusTimestamp : (long)++_focusSequence;
                    if (sequence > (long)_focusSequence) _focusSequence = (ulong)sequence;
                    controller.SetFocus(true, sequence);
                }
            }
            else controller.SetFocus(false, controller.LastFocusSequence);
        }

        public void Remove(NodeInstanceId previewNodeId)
        {
            if (!previewNodeId.IsEmpty && _controllers.Remove(previewNodeId)) _revision++;
        }

        public bool IsDue(NodeInstanceId previewNodeId, ulong frameNumber) => EnsureController(previewNodeId).IsDue(frameNumber);

        public RuntimePreviewDemand Apply(RuntimePreviewDemand demand) => EnsureController(demand.NodeId).Apply(demand);

        public void Observe(double cpuMilliseconds, double gpuMilliseconds, ulong frameNumber)
        {
            if (!IsFinite(cpuMilliseconds) || !IsFinite(gpuMilliseconds)) return;
            Add(_cpuSamples, ref _cpuSum, cpuMilliseconds);
            Add(_gpuSamples, ref _gpuSum, gpuMilliseconds);
            var cpuAverage = _cpuSum / _cpuSamples.Count;
            var gpuAverage = _gpuSum / _gpuSamples.Count;
            var high = cpuAverage > 15.5d || gpuAverage > 15.5d;
            var low = cpuAverage < 13.5d && gpuAverage < 13.5d;
            if (high)
            {
                _highFrameCount++;
                _lowFrameCount = 0;
                _lowQualified = false;
            }
            else if (low)
            {
                _highFrameCount = 0;
                _lowFrameCount++;
                if (_lowFrameCount >= 180) _lowQualified = true;
            }
            else
            {
                _highFrameCount = 0;
                _lowFrameCount = 0;
                _lowQualified = false;
            }
            var nonFocused = _controllers
                .OrderBy(x => x.Value.LastFocusSequence)
                .ThenBy(x => x.Key.Value, StringComparer.Ordinal)
                .Where(x => !x.Value.IsFocused)
                .Select(x => x.Value)
                .ToList();
            var candidate = nonFocused.FirstOrDefault(x => x.QualityStage < 4) ?? _controllers.Values.FirstOrDefault(x => x.IsFocused && x.QualityStage < 4);
            var nonFocusedAtMinimum = nonFocused.All(x => x.QualityStage >= 4);
            if (_highFrameCount >= 30 && candidate != null && (!candidate.IsFocused || nonFocusedAtMinimum))
            {
                var stage = candidate.QualityStage;
                candidate.Degrade(frameNumber);
                if (candidate.QualityStage != stage) _revision++;
                _highFrameCount = 0;
                _lastQualityChangeFrame = frameNumber;
            }
            // Let the 30-sample moving-average transition edge settle before
            // the 180-frame stability qualification can change quality. This
            // makes the first contract recovery the 183rd low-classified
            // sample; later steps retain the 120-frame cadence.
            if (_lowQualified && _lowFrameCount >= 183 && frameNumber >= _lastQualityChangeFrame + 120)
            {
                var recovery = _controllers
                    .Where(x => x.Value.QualityStage > 0)
                    .OrderByDescending(x => x.Value.QualityStage)
                    .ThenBy(x => x.Value.IsFocused)
                    .ThenBy(x => x.Value.LastFocusSequence)
                    .ThenBy(x => x.Key.Value, StringComparer.Ordinal)
                    .Select(x => x.Value)
                    .FirstOrDefault();
                if (recovery != null)
                {
                    var stage = recovery.QualityStage;
                    recovery.Recover(frameNumber);
                    if (recovery.QualityStage != stage) _revision++;
                    _lastQualityChangeFrame = frameNumber;
                }
            }
        }

        public void ObserveProgramWarning(ulong frameNumber)
        {
            var candidate = _controllers
                .Where(x => !x.Value.IsFocused && x.Value.QualityStage < 4)
                .OrderBy(x => x.Value.LastFocusSequence)
                .ThenBy(x => x.Key.Value, StringComparer.Ordinal)
                .Select(x => x.Value)
                .FirstOrDefault()
                ?? _controllers.Values.FirstOrDefault(x => x.IsFocused && x.QualityStage < 4);
            if (candidate != null)
            {
                var stage = candidate.QualityStage;
                candidate.Degrade(frameNumber);
                if (candidate.QualityStage != stage) _revision++;
                _lastQualityChangeFrame = frameNumber;
            }
        }

        public RuntimePreviewOutputSnapshot Capture(NodeInstanceId previewNodeId)
        {
            var controller = EnsureController(previewNodeId);
            return new RuntimePreviewOutputSnapshot(previewNodeId.Value, controller.Width, controller.Height, controller.TargetFramesPerSecond, controller.QualityStage);
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value) && value > 0d;

        private static void Add(Queue<double> samples, ref double sum, double value)
        {
            samples.Enqueue(value);
            sum += value;
            while (samples.Count > 30) sum -= samples.Dequeue();
        }
    }

    /// <summary>Pure, clocked Preview quality policy; Program demand never uses it.</summary>
    public sealed class PreviewQualityController
    {
        private static readonly int[] Widths = { 640, 480, 320, 160, 160 };
        private static readonly int[] Heights = { 360, 270, 180, 90, 90 };
        private static readonly int[] Intervals = { 2, 2, 3, 6, 12 };
        private int _highCostFrames;
        private int _lowCostFrames;
        private bool _lowQualified;
        private ulong _lastRecoveryFrame;
        private long _lastFocusSequence;
        private bool _focused;
        public int QualityStage { get; private set; }
        public int Width => Widths[QualityStage];
        public int Height => Heights[QualityStage];
        public int FrameInterval => Intervals[QualityStage];
        public int TargetFramesPerSecond => Math.Max(1, 60 / FrameInterval);
        public bool IsFocused => _focused;
        public long LastFocusSequence => _lastFocusSequence;

        internal void SetFocus(bool focused, long focusSequence)
        {
            _focused = focused;
            if (focused || _lastFocusSequence == 0)
                _lastFocusSequence = focusSequence;
        }

        public void Observe(double cpuMilliseconds, double gpuMilliseconds, ulong frameNumber)
        {
            Observe(cpuMilliseconds, gpuMilliseconds, frameNumber,
                cpuMilliseconds > 15.5d || gpuMilliseconds > 15.5d,
                cpuMilliseconds < 13.5d && gpuMilliseconds < 13.5d);
        }

        internal void Observe(double cpuMilliseconds, double gpuMilliseconds, ulong frameNumber, bool high, bool low)
        {
            if (high)
            {
                _highCostFrames++;
                _lowCostFrames = 0;
                _lowQualified = false;
            }
            else if (low)
            {
                _lowCostFrames++;
                _highCostFrames = 0;
                if (_lowCostFrames >= 180) _lowQualified = true;
            }
            else
            {
                _highCostFrames = 0;
                _lowCostFrames = 0;
                _lowQualified = false;
            }
            if (_highCostFrames >= 30)
            {
                if (QualityStage < 4) QualityStage++;
                _highCostFrames = 0;
            }
            if (_lowQualified && QualityStage > 0 && frameNumber >= _lastRecoveryFrame + 120)
            {
                QualityStage--;
                _lastRecoveryFrame = frameNumber;
            }
        }

        internal void Degrade(ulong frameNumber)
        {
            if (QualityStage >= 4) return;
            QualityStage++;
            _highCostFrames = 0;
            _lowCostFrames = 0;
            _lowQualified = false;
            _lastRecoveryFrame = frameNumber;
        }

        internal void Recover(ulong frameNumber)
        {
            if (QualityStage <= 0) return;
            QualityStage--;
            _lastRecoveryFrame = frameNumber;
        }

        public bool IsDue(ulong frameNumber) => frameNumber <= 1 || (frameNumber - 1) % (ulong)FrameInterval == 0;
        public RuntimePreviewDemand Apply(RuntimePreviewDemand demand)
        {
            if (demand == null) throw new ArgumentNullException(nameof(demand));
            return new RuntimePreviewDemand(demand.NodeId, demand.OutputPortId,
                System.Math.Min(demand.Width, Width), System.Math.Min(demand.Height, Height),
                demand.Focused, demand.FocusTimestamp);
        }
    }
}
