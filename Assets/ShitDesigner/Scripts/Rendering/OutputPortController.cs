using System;
using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace ShitDesigner.Rendering {
	/// <summary>
	/// Owns the Active/Candidate lease pair for one output port. Candidate
	/// promotion is explicit so callers can perform it at the Phase 9 boundary.
	/// </summary>
	public sealed class OutputPortController : IDisposable {
		private readonly RenderTexturePool _pool;
		private readonly ResourceOwnerKey _owner;
		private TextureLeaseHandle _active;
		private TextureLeaseHandle _candidate;
		private ImageFrame _candidateRenderFrame;
		private bool _candidateRendered;
		private bool _disposed;

		public TextureLeaseHandle ActiveLease => _active;
		public TextureLeaseHandle CandidateLease => _candidate;
		public bool HasActive => _active != null && !_active.IsReleased;
		public bool HasCandidate => _candidate != null && !_candidate.IsReleased;
		public bool CandidateRenderSucceeded => HasCandidate && _candidateRendered;

		public OutputPortController(RenderTexturePool pool, ResourceOwnerKey owner) {
			_pool = pool ?? throw new ArgumentNullException(nameof(pool));
			if (!owner.IsValid) throw new ArgumentException("A valid resource owner is required.", nameof(owner));
			_owner = owner;
		}

		/// <summary>Every new descriptor starts as Candidate until a normal frame is rendered and committed.</summary>
		public UnitResult<Diagnostic> EnsureDemand(TextureDescriptor descriptor, ulong frameNumber) {
			if (_disposed) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.output.disposed", "The output port is disposed."));
			if (frameNumber == 0) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.output.frame_invalid", "Output demand frame number must be positive."));
			if (HasActive && _active.Descriptor == descriptor) return UnitResult.Success<Diagnostic>();
			if (HasCandidate) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.output.candidate_pending", "A candidate lease is already pending."));
			if (!HasActive) {
				var first = BeginCandidate(descriptor, frameNumber);
				return first.IsSuccess ? UnitResult.Success<Diagnostic>() : UnitResult.Failure<Diagnostic>(first.Error);
			}
			var candidate = BeginCandidate(descriptor, frameNumber);
			return candidate.IsSuccess ? UnitResult.Success<Diagnostic>() : UnitResult.Failure<Diagnostic>(candidate.Error);
		}

		public Result<TextureLeaseHandle, Diagnostic> BeginCandidate(TextureDescriptor descriptor, ulong frameNumber) {
			if (_disposed) return Result.Failure<TextureLeaseHandle, Diagnostic>(RenderingDiagnostics.Error("rendering.output.disposed", "The output port is disposed."));
			if (frameNumber == 0) return Result.Failure<TextureLeaseHandle, Diagnostic>(RenderingDiagnostics.Error("rendering.output.frame_invalid", "Candidate frame number must be positive."));
			if (HasCandidate) return Result.Failure<TextureLeaseHandle, Diagnostic>(RenderingDiagnostics.Error("rendering.output.candidate_pending", "A candidate lease is already pending."));
			if (HasActive && _active.Descriptor == descriptor)
				return Result.Failure<TextureLeaseHandle, Diagnostic>(RenderingDiagnostics.Error("rendering.output.descriptor_unchanged", "The requested descriptor is already Active."));
			var acquired = _pool.Acquire(descriptor, _owner, frameNumber);
			if (acquired.IsFailure) return acquired;
			_candidate = acquired.Value;
			_candidateRendered = false;
			return acquired;
		}

		/// <summary>Phase 6 reports the first successful render into Candidate.</summary>
		public UnitResult<Diagnostic> MarkCandidateRendered(ImageFrame candidateFrame) {
			if (!HasCandidate) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.output.candidate_missing", "There is no candidate lease to mark rendered."));
			var valid = ValidateCandidateFrame(candidateFrame);
			if (valid.IsFailure) return valid;
			_candidateRenderFrame = candidateFrame;
			_candidateRendered = true;
			return UnitResult.Success<Diagnostic>();
		}

		/// <summary>Promote only a validated candidate frame at the frame boundary.</summary>
		public UnitResult<Diagnostic> CommitCandidate(ImageFrame candidateFrame, ulong frameNumber) {
			if (_disposed) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.output.disposed", "The output port is disposed."));
			if (frameNumber == 0) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.output.frame_invalid", "Output commit frame number must be positive."));
			if (!HasCandidate) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.output.candidate_missing", "There is no candidate lease to promote."));
			if (!_candidateRendered) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.output.candidate_not_rendered", "Candidate promotion requires a successful first render."));
			var valid = ValidateCandidateFrame(candidateFrame);
			if (valid.IsFailure) return valid;
			if (candidateFrame != _candidateRenderFrame)
				return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.output.candidate_frame_changed", "The candidate frame differs from the frame that was marked rendered."));
			var oldActive = _active;
			if (oldActive != null) {
				var released = oldActive.Release(_owner, frameNumber);
				if (released.IsFailure) return released;
			}
			_active = _candidate;
			_candidate = null;
			_candidateRendered = false;
			return UnitResult.Success<Diagnostic>();
		}

		public UnitResult<Diagnostic> FailCandidate(ulong frameNumber) {
			if (!HasCandidate) return UnitResult.Success<Diagnostic>();
			var candidate = _candidate;
			var result = candidate.Release(_owner, frameNumber);
			if (result.IsSuccess) {
				_candidate = null;
				_candidateRendered = false;
			}
			return result;
		}

		public Result<BorrowedOutputSurface, Diagnostic> BorrowActive(ulong frameNumber) {
			if (!HasActive) return Result.Failure<BorrowedOutputSurface, Diagnostic>(RenderingDiagnostics.Error("rendering.output.active_missing", "The output port has not received a demand."));
			return _active.Borrow(frameNumber);
		}

		/// <summary>Phase-6 borrow for a newly prepared candidate. The
		/// candidate is promoted only by Phase-9 finalization after a normal
		/// frame has been written.</summary>
		public Result<BorrowedOutputSurface, Diagnostic> BorrowCandidate(ulong frameNumber) {
			if (!HasCandidate) return Result.Failure<BorrowedOutputSurface, Diagnostic>(RenderingDiagnostics.Error("rendering.output.candidate_missing", "The output port has no prepared candidate."));
			return _candidate.Borrow(frameNumber);
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			if (_candidate != null && !_candidate.IsReleased) _candidate.Release(_owner, _pool.CurrentFrame);
			if (_active != null && !_active.IsReleased) _active.Release(_owner, _pool.CurrentFrame);
			_candidate = null;
			_active = null;
		}

		private UnitResult<Diagnostic> ValidateCandidateFrame(ImageFrame candidateFrame) {
			if (candidateFrame.LeaseId != _candidate.LeaseId || candidateFrame.Texture != _candidate.Texture || candidateFrame.ColorFormat != _candidate.Descriptor.GraphicsFormat || candidateFrame.Size.x != _candidate.Descriptor.Width || candidateFrame.Size.y != _candidate.Descriptor.Height)
				return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.output.candidate_invalid", "The candidate frame does not match the candidate lease descriptor."));
			return UnitResult.Success<Diagnostic>();
		}
	}
}
