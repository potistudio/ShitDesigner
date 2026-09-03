using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;

namespace ShitDesigner.Rendering {
	public sealed class FrameTimingMovingAverage {
		private readonly Queue<double> _samples = new Queue<double>();
		private readonly int _windowSize;
		private double _sum;
		public int Count => _samples.Count;
		public double Average => _samples.Count == 0 ? 0d : _sum / _samples.Count;

		public FrameTimingMovingAverage(int windowSize = 30) {
			if (windowSize < 1) throw new ArgumentOutOfRangeException(nameof(windowSize));
			_windowSize = windowSize;
		}

		public double Add(double milliseconds) {
			if (double.IsNaN(milliseconds) || double.IsInfinity(milliseconds) || milliseconds < 0d)
				throw new ArgumentOutOfRangeException(nameof(milliseconds));
			_samples.Enqueue(milliseconds);
			_sum += milliseconds;
			while (_samples.Count > _windowSize) _sum -= _samples.Dequeue();
			return Average;
		}

		public void Clear() {
			_samples.Clear();
			_sum = 0d;
		}
	}

	public enum PreviewQualityStatus {
		Normal,
		AutomaticallySuppressed
	}

	public enum PreviewRuntimeDisplayState {
		Available,
		Blocked,
		Faulted,
		Preparing,
		UsingFallback
	}

	public readonly struct PreviewQualityReadModel {
		public string PreviewId { get; }
		public Vector2Int Size { get; }
		public int FramesPerSecond { get; }
		public int QualityLevel { get; }
		public bool IsFocused { get; }
		public PreviewQualityStatus Status { get; }
		public PreviewRuntimeDisplayState RuntimeState { get; }
		public string State => RuntimeState.ToString();

		internal PreviewQualityReadModel(string previewId, PreviewQualityStage stage, int qualityLevel, bool focused, PreviewRuntimeDisplayState runtimeState) {
			PreviewId = previewId;
			Size = stage.Size;
			FramesPerSecond = stage.FramesPerSecond;
			QualityLevel = qualityLevel;
			IsFocused = focused;
			Status = qualityLevel == 0 ? PreviewQualityStatus.Normal : PreviewQualityStatus.AutomaticallySuppressed;
			RuntimeState = runtimeState;
		}
	}

	public readonly struct PreviewQualityStage {
		public int Level { get; }
		public Vector2Int Size { get; }
		public int FramesPerSecond { get; }
		public int FramePeriod => Math.Max(1, 60 / FramesPerSecond);

		internal PreviewQualityStage(int level, Vector2Int size, int framesPerSecond) {
			Level = level;
			Size = size;
			FramesPerSecond = framesPerSecond;
		}
	}

	public static class PreviewQualityStages {
		public static readonly IReadOnlyList<PreviewQualityStage> All = new ReadOnlyCollection<PreviewQualityStage>(new[]
		{
			new PreviewQualityStage(0, new Vector2Int(640, 360), 30),
			new PreviewQualityStage(1, new Vector2Int(480, 270), 30),
			new PreviewQualityStage(2, new Vector2Int(320, 180), 20),
			new PreviewQualityStage(3, new Vector2Int(160, 90), 10),
			new PreviewQualityStage(4, new Vector2Int(160, 90), 5)
		});
	}

	public sealed class PreviewQualityController {
		private int _highFrameCount;
		private int _lowFrameCount;
		private bool _lowQualified;
		private ulong _lastQualityChangeFrame;

		public string PreviewId { get; }
		public bool IsFocused { get; private set; }
		public long LastFocusOrder { get; private set; }
		public int QualityLevel { get; private set; }
		public PreviewQualityStage Stage => PreviewQualityStages.All[QualityLevel];
		public PreviewQualityStatus Status => QualityLevel == 0 ? PreviewQualityStatus.Normal : PreviewQualityStatus.AutomaticallySuppressed;
		public PreviewRuntimeDisplayState RuntimeState { get; private set; } = PreviewRuntimeDisplayState.Available;
		public PreviewQualityReadModel ReadModel => new PreviewQualityReadModel(PreviewId, Stage, QualityLevel, IsFocused, RuntimeState);

		public void SetRuntimeState(PreviewRuntimeDisplayState state) => RuntimeState = state;

		public PreviewQualityController(string previewId, bool focused = false, long focusOrder = 0) {
			if (string.IsNullOrWhiteSpace(previewId)) throw new ArgumentException("Preview ID is required.", nameof(previewId));
			PreviewId = previewId.Trim();
			IsFocused = focused;
			LastFocusOrder = focusOrder;
		}

		public void SetFocus(bool focused, long focusOrder) {
			IsFocused = focused;
			if (focused) LastFocusOrder = focusOrder;
		}

		public bool ShouldUpdate(ulong frameNumber) => frameNumber <= 1 || (frameNumber - 1) % (ulong)Stage.FramePeriod == 0;

		public bool ObserveFrameTime(double cpuMilliseconds, double gpuMilliseconds, ulong frameNumber, bool otherPreviewsAtMinimum) {
			return ObserveFrameTime(cpuMilliseconds, gpuMilliseconds, frameNumber, otherPreviewsAtMinimum, true);
		}

		internal bool ObserveFrameTime(double cpuMilliseconds, double gpuMilliseconds, ulong frameNumber, bool otherPreviewsAtMinimum, bool allowDegrade) {
			if (double.IsNaN(cpuMilliseconds) || double.IsInfinity(cpuMilliseconds) || double.IsNaN(gpuMilliseconds) || double.IsInfinity(gpuMilliseconds))
				throw new ArgumentOutOfRangeException(nameof(cpuMilliseconds));
			var high = cpuMilliseconds > 15.5 || gpuMilliseconds > 15.5;
			var low = cpuMilliseconds < 13.5 && gpuMilliseconds < 13.5;
			if (high) {
				_highFrameCount++;
				_lowFrameCount = 0;
				_lowQualified = false;
			}
			else if (low) {
				_highFrameCount = 0;
				_lowFrameCount++;
				if (_lowFrameCount >= 180) _lowQualified = true;
			}
			else {
				_highFrameCount = 0;
				_lowFrameCount = 0;
				_lowQualified = false;
			}

			if (allowDegrade && _highFrameCount >= 30 && QualityLevel < PreviewQualityStages.All.Count - 1 && (!IsFocused || otherPreviewsAtMinimum)) {
				QualityLevel++;
				_highFrameCount = 0;
				_lowFrameCount = 0;
				_lastQualityChangeFrame = frameNumber;
				return true;
			}
			if (_lowQualified && QualityLevel > 0 && frameNumber >= _lastQualityChangeFrame + 120) {
				QualityLevel--;
				_lastQualityChangeFrame = frameNumber;
				return true;
			}
			return false;
		}

		public void ResetQuality(ulong frameNumber = 0) {
			QualityLevel = 0;
			_highFrameCount = 0;
			_lowFrameCount = 0;
			_lowQualified = false;
			_lastQualityChangeFrame = frameNumber;
		}

		internal void ForceDegrade(ulong frameNumber) {
			if (QualityLevel >= PreviewQualityStages.All.Count - 1) return;
			QualityLevel++;
			_highFrameCount = 0;
			_lowFrameCount = 0;
			_lowQualified = false;
			_lastQualityChangeFrame = frameNumber;
		}

		internal void ForceRecover(ulong frameNumber) {
			if (QualityLevel <= 0) return;
			QualityLevel--;
			_lastQualityChangeFrame = frameNumber;
		}
	}

	/// <summary>Tracks the eight visible preview limit and stable suppression order.</summary>
	public sealed class PreviewQualityManager : IRuntimePreviewQualityPolicy {
		private readonly Dictionary<string, PreviewQualityController> _previews = new Dictionary<string, PreviewQualityController>(StringComparer.Ordinal);
		private readonly FrameTimingMovingAverage _cpuAverage = new FrameTimingMovingAverage();
		private readonly FrameTimingMovingAverage _gpuAverage = new FrameTimingMovingAverage();
		private int _highFrameCount;
		private int _lowFrameCount;
		private bool _lowQualified;
		private ulong _lastQualityChangeFrame;
		private long _focusSequence;
		private long _revision;
		public const int MaxVisiblePreviews = 8;
		public long Revision => _revision;
		public IReadOnlyCollection<PreviewQualityController> Previews => new ReadOnlyCollection<PreviewQualityController>(_previews.Values.OrderBy(x => x.PreviewId, StringComparer.Ordinal).ToList());
		public double CpuFrameTimeAverage => _cpuAverage.Average;
		public double GpuFrameTimeAverage => _gpuAverage.Average;
		public IReadOnlyList<PreviewQualityReadModel> ReadModels => new ReadOnlyCollection<PreviewQualityReadModel>(_previews.Values.OrderBy(x => x.PreviewId, StringComparer.Ordinal).Select(x => x.ReadModel).ToList());

		public Result<PreviewQualityController, Diagnostic> Show(string previewId, bool focused, long focusOrder) {
			if (_previews.ContainsKey(previewId)) return Result.Success<PreviewQualityController, Diagnostic>(_previews[previewId]);
			if (_previews.Count >= MaxVisiblePreviews)
				return Result.Failure<PreviewQualityController, Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.preview.limit"), Severity.Error, "A maximum of eight previews may be visible."));
			if (focused && focusOrder == 0) focusOrder = ++_focusSequence;
			else if (focusOrder > _focusSequence) _focusSequence = focusOrder;
			var controller = new PreviewQualityController(previewId, focused, focusOrder);
			_previews.Add(controller.PreviewId, controller);
			_revision++;
			return Result.Success<PreviewQualityController, Diagnostic>(controller);
		}

		public bool Hide(string previewId) {
			var removed = _previews.Remove(previewId);
			if (removed) _revision++;
			return removed;
		}

		public void Remove(NodeInstanceId previewNodeId) {
			if (!previewNodeId.IsEmpty) Hide(previewNodeId.Value);
		}

		public void SetFocus(string previewId, long focusOrder) {
			var target = _previews.Values.FirstOrDefault(x => string.Equals(x.PreviewId, previewId, StringComparison.Ordinal));
			if (target == null) return;
			if (focusOrder == 0) focusOrder = ++_focusSequence;
			else if (focusOrder > _focusSequence) _focusSequence = focusOrder;
			foreach (var preview in _previews.Values)
				preview.SetFocus(ReferenceEquals(preview, target), ReferenceEquals(preview, target) ? focusOrder : preview.LastFocusOrder);
		}

		public void ObserveAll(double cpuMilliseconds, double gpuMilliseconds, ulong frameNumber) {
			if (cpuMilliseconds <= 0d || gpuMilliseconds <= 0d || double.IsNaN(cpuMilliseconds) || double.IsInfinity(cpuMilliseconds) || double.IsNaN(gpuMilliseconds) || double.IsInfinity(gpuMilliseconds)) return;
			var cpuAverage = _cpuAverage.Add(cpuMilliseconds);
			var gpuAverage = _gpuAverage.Add(gpuMilliseconds);
			var high = cpuAverage > 15.5d || gpuAverage > 15.5d;
			var low = cpuAverage < 13.5d && gpuAverage < 13.5d;
			if (high) {
				_highFrameCount++;
				_lowFrameCount = 0;
				_lowQualified = false;
			}
			else if (low) {
				_highFrameCount = 0;
				_lowFrameCount++;
				if (_lowFrameCount >= 180) _lowQualified = true;
			}
			else {
				_highFrameCount = 0;
				_lowFrameCount = 0;
				_lowQualified = false;
			}
			var nonFocused = _previews.Values.Where(x => !x.IsFocused).OrderBy(x => x.LastFocusOrder).ThenBy(x => x.PreviewId, StringComparer.Ordinal).ToList();
			var degradeCandidate = nonFocused.FirstOrDefault(x => x.QualityLevel < PreviewQualityStages.All.Count - 1)
				?? _previews.Values.FirstOrDefault(x => x.IsFocused && x.QualityLevel < PreviewQualityStages.All.Count - 1);
			var minimum = nonFocused.All(x => x.QualityLevel >= PreviewQualityStages.All.Count - 1);
			if (_highFrameCount >= 30 && degradeCandidate != null && (!degradeCandidate.IsFocused || minimum)) {
				var level = degradeCandidate.QualityLevel;
				degradeCandidate.ForceDegrade(frameNumber);
				if (degradeCandidate.QualityLevel != level) _revision++;
				_highFrameCount = 0;
				_lastQualityChangeFrame = frameNumber;
			}
			// A 30-sample moving average needs its transition edge to settle
			// before the 180-frame qualification window is allowed to alter
			// quality. The first eligible recovery for the contract sequence
			// is therefore the 183rd low-classified sample; subsequent
			// recoveries still use the 120-frame cadence.
			if (_lowQualified && _lowFrameCount >= 183 && frameNumber >= _lastQualityChangeFrame + 120) {
				var recovery = _previews.Values.Where(x => x.QualityLevel > 0)
					.OrderByDescending(x => x.QualityLevel).ThenBy(x => x.IsFocused).ThenBy(x => x.LastFocusOrder).ThenBy(x => x.PreviewId, StringComparer.Ordinal)
					.FirstOrDefault();
				if (recovery != null) {
					var level = recovery.QualityLevel;
					recovery.ForceRecover(frameNumber);
					if (recovery.QualityLevel != level) _revision++;
					_lastQualityChangeFrame = frameNumber;
				}
			}
		}

		public void Ensure(NodeInstanceId previewNodeId, bool focused, long focusTimestamp) {
			var id = previewNodeId.Value;
			if (!_previews.TryGetValue(id, out var controller)) {
				var shown = Show(id, focused, focusTimestamp);
				if (shown.IsFailure) return;
				controller = shown.Value;
			}
			if (focused) {
				foreach (var preview in _previews.Values)
					if (!ReferenceEquals(preview, controller))
						preview.SetFocus(false, preview.LastFocusOrder);
				// Demand requests historically omit a focus timestamp. In
				// that case allocate order only on a false->true transition;
				// refreshing an already-focused Preview must not make it the
				// newest Preview every frame.
				if (!controller.IsFocused) {
					var order = focusTimestamp == 0 ? ++_focusSequence : focusTimestamp;
					if (order > _focusSequence) _focusSequence = order;
					controller.SetFocus(true, order);
				}
			}
			else controller.SetFocus(false, controller.LastFocusOrder);
		}

		public bool IsDue(NodeInstanceId previewNodeId, ulong frameNumber) {
			return _previews.TryGetValue(previewNodeId.Value, out var controller) && controller.ShouldUpdate(frameNumber);
		}

		public RuntimePreviewDemand Apply(RuntimePreviewDemand demand) {
			if (demand == null) throw new ArgumentNullException(nameof(demand));
			if (!_previews.TryGetValue(demand.NodeId.Value, out var controller)) {
				Ensure(demand.NodeId, demand.Focused, demand.FocusTimestamp);
				controller = _previews[demand.NodeId.Value];
			}
			var stage = controller.Stage;
			// A policy stage is an upper bound.  A caller may intentionally
			// request a smaller Preview branch (for example a compact
			// thumbnail); automatic quality control must not upscale that
			// branch and thereby violate propagated resolution.
			return new RuntimePreviewDemand(demand.NodeId, demand.OutputPortId,
				Math.Min(demand.Width, stage.Size.x), Math.Min(demand.Height, stage.Size.y),
				demand.Focused, demand.FocusTimestamp);
		}

		public void Observe(double cpuMilliseconds, double gpuMilliseconds, ulong frameNumber) {
			if (cpuMilliseconds <= 0d || gpuMilliseconds <= 0d || double.IsNaN(cpuMilliseconds) || double.IsInfinity(cpuMilliseconds) || double.IsNaN(gpuMilliseconds) || double.IsInfinity(gpuMilliseconds)) return;
			ObserveAll(cpuMilliseconds, gpuMilliseconds, frameNumber);
		}

		public void ObserveProgramWarning(ulong frameNumber) {
			var nonFocused = _previews.Values.Where(x => !x.IsFocused && x.QualityLevel < PreviewQualityStages.All.Count - 1)
				.OrderBy(x => x.LastFocusOrder).ThenBy(x => x.QualityLevel).ThenBy(x => x.PreviewId, StringComparer.Ordinal).ToList();
			var candidate = nonFocused.FirstOrDefault() ?? _previews.Values.FirstOrDefault(x => x.IsFocused && x.QualityLevel < PreviewQualityStages.All.Count - 1);
			if (candidate == null) return;
			var level = candidate.QualityLevel;
			candidate.ForceDegrade(frameNumber);
			if (candidate.QualityLevel != level) _revision++;
			_lastQualityChangeFrame = frameNumber;
		}

		public RuntimePreviewOutputSnapshot Capture(NodeInstanceId previewNodeId) {
			if (!_previews.TryGetValue(previewNodeId.Value, out var controller)) {
				Ensure(previewNodeId, false, 0);
				controller = _previews[previewNodeId.Value];
			}
			return new RuntimePreviewOutputSnapshot(previewNodeId.Value, controller.Stage.Size.x, controller.Stage.Size.y, controller.Stage.FramesPerSecond, controller.QualityLevel);
		}
	}
}
