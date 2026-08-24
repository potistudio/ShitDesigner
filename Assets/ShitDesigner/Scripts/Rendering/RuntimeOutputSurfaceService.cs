using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace ShitDesigner.Rendering {
	/// <summary>
	/// Production Phase-5/Phase-9 output lease coordinator. EvaluationPlan's
	/// propagated node/port resolution map is the sole source of dimensions;
	/// visual nodes only borrow the candidate returned here and never touch
	/// the pool directly.
	/// </summary>
	public sealed class RuntimeOutputSurfaceService : IRuntimeOutputSurfacePort, IRuntimeResourcePreparationWithPlan, IRuntimeResourceFinalizationWithPlan, IDisposable {
		private readonly RuntimeSession _session;
		private readonly RenderTexturePool _pool;
		private readonly string _sessionId;
		private readonly IRuntimeOutputFormatPolicy _formatPolicy;
		private readonly Dictionary<OutputKey, OutputPortController> _outputs = new Dictionary<OutputKey, OutputPortController>();
		private readonly HashSet<RenderedCandidateKey> _renderedCandidateLeases = new HashSet<RenderedCandidateKey>();
		private readonly List<RetiringOutput> _retiring = new List<RetiringOutput>();
		// Reused only when a topology change removes an output owner.  The
		// normal Phase-5/9 path must not allocate a temporary LINQ list.
		private readonly List<OutputKey> _staleOutputKeys = new List<OutputKey>();
		private long _lastTopologyRevision = long.MinValue;
		private bool _disposed;

		public IReadOnlyCollection<OutputKey> RequiredOutputKeys => new ReadOnlyCollection<OutputKey>(_outputs.Keys.OrderBy(x => x.NodeId.Value, StringComparer.Ordinal).ThenBy(x => x.PortId.Value, StringComparer.Ordinal).ToList());
		public RenderTexturePool Pool => _pool;
		public bool IsDisposed => _disposed;

		public RuntimeOutputSurfaceService(RuntimeSession session, RenderTexturePool pool, string sessionId, IRuntimeOutputFormatPolicy formatPolicy = null) {
			_session = session ?? throw new ArgumentNullException(nameof(session));
			_pool = pool ?? throw new ArgumentNullException(nameof(pool));
			if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("A session ID is required.", nameof(sessionId));
			_sessionId = sessionId.Trim(); _formatPolicy = formatPolicy ?? new RuntimeOutputFormatPolicy();
		}

		public CSharpFunctionalExtensions.UnitResult<Diagnostic> Prepare(FrameSnapshot snapshot) => Prepare(snapshot, null);

		public CSharpFunctionalExtensions.UnitResult<Diagnostic> Prepare(FrameSnapshot snapshot, FrameEvaluationContext evaluation) {
			if (_disposed) return Failure("rendering.output.disposed", "Output surface service is disposed.");
			if (snapshot == null) return Failure("rendering.output.snapshot", "Output preparation requires a FrameSnapshot.");
			var resolutions = evaluation != null
				? RuntimeOutputResolutionDemandAccess.GetVisualOutputs(_session, evaluation)
				// A snapshot captured before OutputDemand/GraphEdit may hold
				// a null or stale plan. Direct callers (including a graph
				// replacement between Phase 9 and the next preparation)
				// must use the session's current installed plan.
				: RuntimeOutputResolutionDemandAccess.GetVisualOutputs(_session);
			if (resolutions.Count == 0) return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
			foreach (var resolution in resolutions) {
				if (resolution.PortId.Value != "image") continue;
				var handle = _session.FindNode(resolution.NodeId);
				if (handle == null) continue;
				var key = new OutputKey(resolution.NodeId, resolution.PortId);
				if (_outputs.TryGetValue(key, out var existing)) {
					var existingGeneration = existing.ActiveLease != null ? existing.ActiveLease.Owner.GenerationId
						: existing.CandidateLease != null ? existing.CandidateLease.Owner.GenerationId : handle.GenerationId;
					if (existingGeneration != handle.GenerationId) {
						// A same-ID Undo/recreate is a new owner. Keep
						// the old controller through Phase 9 so its
						// generation lease cannot be returned during
						// Phase 5 while the previous presentation still
						// refers to it.
						_outputs.Remove(key);
						_retiring.Add(new RetiringOutput(key, existing));
					}
				}
				if (!_outputs.TryGetValue(key, out var controller)) {
					var owner = new ResourceOwnerKey(_sessionId, ResourceOwnerKind.RuntimeNode, resolution.NodeId.Value, handle.GenerationId, resolution.PortId.Value, LeaseRole.Output);
					controller = new OutputPortController(_pool, owner);
					_outputs.Add(key, controller);
				}
				// URP 17.5's StandardRequest accepts only one destination
				// RenderTexture.  Scene cameras therefore require the
				// pooled Image output itself to carry a depth attachment;
				// a separate depth lease cannot be supplied to that API.
				// Keep the attachment in the descriptor so pooling,
				// reuse and budget accounting remain exact.
				var descriptor = new TextureDescriptor(resolution.Demand.Width, resolution.Demand.Height, InternalFormat,
					DepthStencilFormatFor(handle.TypeId));
				if (!controller.HasCandidate || controller.CandidateLease.Descriptor != descriptor) {
					var ensured = controller.EnsureDemand(descriptor, snapshot.FrameNumber);
					if (ensured.IsFailure && !(controller.HasCandidate && controller.CandidateLease.Descriptor == descriptor)) return ensured;
				}
			}
			// Preview visibility/demand is deliberately not an ownership
			// signal: an acquired node lease remains valid while the node is
			// alive and can be reused on re-open. Node deletion is collected
			// by Finalize after this frame's presentation boundary.
			return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
		}

		public GraphicsFormat InternalFormat => _formatPolicy.DynamicRange == RuntimeDynamicRange.Hdr ? GraphicsFormat.R16G16B16A16_SFloat : GraphicsFormat.R8G8B8A8_UNorm;

		private static GraphicsFormat DepthStencilFormatFor(NodeTypeId typeId) {
			// These are the only production nodes that submit an isolated
			// Camera through RenderPipeline.StandardRequest.  Other visual
			// nodes render fullscreen passes and must retain their current
			// colour-only descriptors.
			return typeId.Value == "shitdesigner.scene.3d" || typeId.Value == "shitdesigner.scene.2d"
				? GraphicsFormat.D32_SFloat
				: GraphicsFormat.None;
		}

		public CSharpFunctionalExtensions.Result<IRuntimeOutputSurface, Diagnostic> TryGetPrepared(NodeInstanceId nodeId, PortId portId, int width, int height, ulong frameNumber) {
			if (_disposed) return CSharpFunctionalExtensions.Result.Failure<IRuntimeOutputSurface, Diagnostic>(FailureDiagnostic("rendering.output.disposed", "Output surface service is disposed."));
			if (nodeId.IsEmpty || portId.IsEmpty || frameNumber == 0 || width <= 0 || height <= 0) return CSharpFunctionalExtensions.Result.Failure<IRuntimeOutputSurface, Diagnostic>(FailureDiagnostic("rendering.output.request", "Output surface request is invalid."));
			if (!_outputs.TryGetValue(new OutputKey(nodeId, portId), out var controller)) return CSharpFunctionalExtensions.Result.Failure<IRuntimeOutputSurface, Diagnostic>(FailureDiagnostic("rendering.output.not_prepared", "The requested output was not prepared in Phase 5."));
			CSharpFunctionalExtensions.Result<BorrowedOutputSurface, Diagnostic> borrowed;
			if (controller.HasCandidate && controller.CandidateLease.Descriptor.Width == width && controller.CandidateLease.Descriptor.Height == height) borrowed = controller.BorrowCandidate(frameNumber);
			else if (controller.HasActive && controller.ActiveLease.Descriptor.Width == width && controller.ActiveLease.Descriptor.Height == height) borrowed = controller.BorrowActive(frameNumber);
			else return CSharpFunctionalExtensions.Result.Failure<IRuntimeOutputSurface, Diagnostic>(FailureDiagnostic("rendering.output.descriptor", "Prepared output dimensions do not match the propagated resolution demand."));
			if (borrowed.IsFailure) return CSharpFunctionalExtensions.Result.Failure<IRuntimeOutputSurface, Diagnostic>(borrowed.Error);
			var outputKey = new OutputKey(nodeId, portId);
			return CSharpFunctionalExtensions.Result.Success<IRuntimeOutputSurface, Diagnostic>(new RuntimeOutputSurface(outputKey, borrowed.Value, (key, lease) => {
				if (_outputs.TryGetValue(key, out var owner) && owner.HasCandidate && owner.CandidateLease.LeaseId.Value == lease)
					_renderedCandidateLeases.Add(new RenderedCandidateKey(key, lease));
			}));
		}

		public CSharpFunctionalExtensions.UnitResult<Diagnostic> Finalize(FrameSnapshot snapshot, FrameEvaluationContext evaluation, bool frameSucceeded) {
			if (_disposed) return Failure("rendering.output.disposed", "Output surface service is disposed.");
			if (snapshot == null) return Failure("rendering.output.snapshot", "Output finalization requires a FrameSnapshot.");
			foreach (var pair in _outputs) {
				var key = pair.Key; var controller = pair.Value;
				if (!controller.HasCandidate) continue;
				if (!frameSucceeded || !_session.TryGetOutputResults(key.NodeId, out var results) || !results.TryGetValue(key.PortId, out var result) || !result.IsAvailable || !result.Value.IsImageFrame || !_renderedCandidateLeases.Contains(new RenderedCandidateKey(key, result.Value.AsImageFrame().LeaseId))) {
					if (controller.HasCandidate) _renderedCandidateLeases.Remove(new RenderedCandidateKey(key, controller.CandidateLease.LeaseId.Value));
					controller.FailCandidate(snapshot.FrameNumber);
					continue;
				}
				var image = result.Value.AsImageFrame();
				if (!((image as IRuntimeImageFrameSurface)?.NativeSurface is RenderTexture texture) || image.LeaseId != controller.CandidateLease.LeaseId.Value) {
					controller.FailCandidate(snapshot.FrameNumber);
					continue;
				}
				try {
					var frame = new ImageFrame(texture, new Vector2Int(controller.CandidateLease.Descriptor.Width, controller.CandidateLease.Descriptor.Height), controller.CandidateLease.Descriptor.GraphicsFormat, snapshot.FrameNumber, controller.CandidateLease.LeaseId);
					var marked = controller.MarkCandidateRendered(frame);
					if (marked.IsFailure) { _renderedCandidateLeases.Remove(new RenderedCandidateKey(key, controller.CandidateLease.LeaseId.Value)); controller.FailCandidate(snapshot.FrameNumber); continue; }
					var committed = controller.CommitCandidate(frame, snapshot.FrameNumber);
					_renderedCandidateLeases.Remove(new RenderedCandidateKey(key, frame.LeaseId.Value));
					if (committed.IsFailure) controller.FailCandidate(snapshot.FrameNumber);
				}
				catch { controller.FailCandidate(snapshot.FrameNumber); }
			}
			foreach (var retiring in _retiring) retiring.Controller.Dispose();
			_retiring.Clear();
			if (_lastTopologyRevision != _session.GraphTopologyRevision) {
				_staleOutputKeys.Clear();
				foreach (var key in _outputs.Keys)
					if (_session.FindNode(key.NodeId) == null) _staleOutputKeys.Add(key);
				foreach (var key in _staleOutputKeys) {
					_outputs[key].Dispose(); _outputs.Remove(key);
				}
				_staleOutputKeys.Clear();
				_lastTopologyRevision = _session.GraphTopologyRevision;
			}
			return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
		}

		public CSharpFunctionalExtensions.UnitResult<Diagnostic> Finalize(FrameSnapshot snapshot, bool frameSucceeded) => Finalize(snapshot, null, frameSucceeded);
		public void Dispose() { if (_disposed) return; _disposed = true; foreach (var output in _outputs.Values) output.Dispose(); foreach (var retiring in _retiring) retiring.Controller.Dispose(); _outputs.Clear(); _retiring.Clear(); _staleOutputKeys.Clear(); _renderedCandidateLeases.Clear(); }

		private static Diagnostic FailureDiagnostic(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "rendering");
		private static CSharpFunctionalExtensions.UnitResult<Diagnostic> Failure(string code, string message) => CSharpFunctionalExtensions.UnitResult.Failure<Diagnostic>(FailureDiagnostic(code, message));

		public readonly struct OutputKey : IEquatable<OutputKey> {
			public NodeInstanceId NodeId { get; }
			public PortId PortId { get; }
			public OutputKey(NodeInstanceId nodeId, PortId portId) { NodeId = nodeId; PortId = portId; }
			public bool Equals(OutputKey other) => NodeId == other.NodeId && PortId == other.PortId;
			public override bool Equals(object obj) => obj is OutputKey other && Equals(other);
			public override int GetHashCode() => HashCode.Combine(NodeId, PortId);
		}

		private readonly struct RenderedCandidateKey : IEquatable<RenderedCandidateKey> {
			private readonly OutputKey _output;
			private readonly ulong _lease;
			public RenderedCandidateKey(OutputKey output, ulong lease) { _output = output; _lease = lease; }
			public bool Equals(RenderedCandidateKey other) => _output.Equals(other._output) && _lease == other._lease;
			public override bool Equals(object obj) => obj is RenderedCandidateKey other && Equals(other);
			public override int GetHashCode() => HashCode.Combine(_output, _lease);
		}

		private sealed class RetiringOutput {
			public OutputKey Key { get; }
			public OutputPortController Controller { get; }
			public RetiringOutput(OutputKey key, OutputPortController controller) { Key = key; Controller = controller; }
		}
	}

	internal sealed class RuntimeOutputSurface : IRuntimeOutputSurface, IRuntimeOutputSurfaceCompletion, IRuntimeOutputSurfaceFormat {
		private readonly RuntimeOutputSurfaceService.OutputKey _outputKey;
		private readonly BorrowedOutputSurface _borrowed;
		private readonly Action<RuntimeOutputSurfaceService.OutputKey, ulong> _markRendered;
		private bool _rendered;
		public NodeInstanceId NodeId => _outputKey.NodeId;
		public PortId PortId => _outputKey.PortId;
		public int Width => _borrowed.Size.x;
		public int Height => _borrowed.Size.y;
		public ulong LeaseId => _borrowed.LeaseId.Value;
		public ulong FrameNumber => _borrowed.Frame.FrameNumber;
		public object NativeSurface => _borrowed.Texture;
		public string ColorFormat => _borrowed.ColorFormat.ToString();
		public bool IsRendered => _rendered;
		internal RuntimeOutputSurface(RuntimeOutputSurfaceService.OutputKey outputKey, BorrowedOutputSurface borrowed, Action<RuntimeOutputSurfaceService.OutputKey, ulong> markRendered) { _outputKey = outputKey; _borrowed = borrowed; _markRendered = markRendered; }
		public CSharpFunctionalExtensions.UnitResult<Diagnostic> MarkRendered() { if (_rendered) return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); _rendered = true; _markRendered?.Invoke(_outputKey, LeaseId); return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>(); }
	}
}
