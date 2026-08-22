using System;
using ShitDesigner.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace ShitDesigner.Rendering
{
    /// <summary>
    /// Owns Feedback's two history surfaces. A descriptor change is prepared
    /// as a complete pair before the old pair is released, so a one-texture
    /// allocation failure cannot leave the node half initialized.
    /// </summary>
    public sealed class FeedbackHistoryController : IDisposable
    {
        private readonly RenderTexturePool _pool;
        private readonly ResourceOwnerKey _owner;
        private TextureLeaseHandle _previous;
        private TextureLeaseHandle _next;
        private TextureDescriptor _descriptor;
        private bool _hasDescriptor;
        private bool _disposed;

        public TextureDescriptor Descriptor => _descriptor;
        public bool HasHistory => _previous != null && !_previous.IsReleased && _next != null && !_next.IsReleased;
        public TextureLeaseHandle PreviousLease => _previous;
        public TextureLeaseHandle NextLease => _next;
        public ulong LastCommitFrame { get; private set; }
        public ulong LastResetFrame { get; private set; }

        public FeedbackHistoryController(RenderTexturePool pool, ResourceOwnerKey owner)
        {
            _pool = pool ?? throw new ArgumentNullException(nameof(pool));
            if (!owner.IsValid) throw new ArgumentException("A valid resource owner is required.", nameof(owner));
            _owner = owner;
        }

        public Result EnsureDescriptor(TextureDescriptor descriptor, ulong frameNumber)
        {
            if (_disposed) return Result.Failure(RenderingDiagnostics.Error("rendering.feedback.disposed", "The Feedback history is disposed."));
            if (frameNumber == 0) return Result.Failure(RenderingDiagnostics.Error("rendering.feedback.frame_invalid", "Feedback frame number must be positive."));
            if (HasHistory && _hasDescriptor && _descriptor == descriptor) return Result.Success();

            var previousOwner = OwnerFor(LeaseRole.FeedbackPrevious);
            var nextOwner = OwnerFor(LeaseRole.FeedbackNext);
            var first = _pool.Acquire(descriptor, previousOwner, frameNumber);
            if (first.IsFailure) return Result.Failure(first.Diagnostic);
            var second = _pool.Acquire(descriptor, nextOwner, frameNumber);
            if (second.IsFailure)
            {
                first.Value.Release(previousOwner, frameNumber);
                return Result.Failure(second.Diagnostic);
            }

            try
            {
                ClearTransparent(first.Value.Texture);
                ClearTransparent(second.Value.Texture);
            }
            catch (Exception exception)
            {
                first.Value.Release(previousOwner, frameNumber);
                second.Value.Release(nextOwner, frameNumber);
                return Result.Failure(new Diagnostic(new DiagnosticCode("rendering.feedback.clear_failed"), Severity.Error,
                    "Feedback history initialization failed.", exception: DiagnosticExceptionInfo.FromException(exception)));
            }

            ReleasePair(frameNumber);
            _previous = first.Value;
            _next = second.Value;
            _descriptor = descriptor;
            _hasDescriptor = true;
            return Result.Success();
        }

        public Result<BorrowedOutputSurface> BorrowPrevious(ulong frameNumber)
        {
            if (!HasHistory) return Result<BorrowedOutputSurface>.Failure(RenderingDiagnostics.Error("rendering.feedback.history_missing", "Feedback history has not been prepared."));
            return _previous.Borrow(frameNumber);
        }

        /// <summary>Copies current input into the next buffer, then swaps at the boundary.</summary>
        public Result Commit(ImageFrame currentInput, ulong frameNumber)
        {
            if (frameNumber == 0) return Result.Failure(RenderingDiagnostics.Error("rendering.feedback.frame_invalid", "Feedback frame number must be positive."));
            if (!HasHistory) return Result.Failure(RenderingDiagnostics.Error("rendering.feedback.history_missing", "Feedback history has not been prepared."));
            if (currentInput.Texture == null || currentInput.Size.x != _descriptor.Width || currentInput.Size.y != _descriptor.Height || currentInput.ColorFormat != _descriptor.GraphicsFormat)
                return Result.Failure(RenderingDiagnostics.Error("rendering.feedback.input_mismatch", "Feedback input does not match the history descriptor."));
            if (currentInput.Texture == _next.Texture || currentInput.Texture == _previous.Texture)
                return Result.Failure(RenderingDiagnostics.Error("rendering.feedback.self_copy", "Feedback input cannot be one of its history surfaces."));
            try
            {
                Graphics.Blit(currentInput.Texture, _next.Texture);
            }
            catch (Exception exception)
            {
                return Result.Failure(new Diagnostic(new DiagnosticCode("rendering.feedback.commit_failed"), Severity.Error,
                    "Feedback history commit failed; the previous history remains active.", exception: DiagnosticExceptionInfo.FromException(exception)));
            }
            var oldPrevious = _previous;
            _previous = _next;
            _next = oldPrevious;
            LastCommitFrame = frameNumber;
            return Result.Success();
        }

        public Result Reset(ulong frameNumber)
        {
            if (_disposed) return Result.Failure(RenderingDiagnostics.Error("rendering.feedback.disposed", "The Feedback history is disposed."));
            if (frameNumber == 0) return Result.Failure(RenderingDiagnostics.Error("rendering.feedback.frame_invalid", "Feedback frame number must be positive."));
            if (!HasHistory) return Result.Success();
            try
            {
                ClearTransparent(_previous.Texture);
                ClearTransparent(_next.Texture);
                LastResetFrame = frameNumber;
                return Result.Success();
            }
            catch (Exception exception)
            {
                return Result.Failure(new Diagnostic(new DiagnosticCode("rendering.feedback.reset_failed"), Severity.Error,
                    "Feedback history reset failed.", exception: DiagnosticExceptionInfo.FromException(exception)));
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            ReleasePair(_pool.CurrentFrame);
            _hasDescriptor = false;
        }

        private void ReleasePair(ulong frameNumber)
        {
            if (_previous != null && !_previous.IsReleased) _previous.Release(OwnerFor(LeaseRole.FeedbackPrevious), frameNumber);
            if (_next != null && !_next.IsReleased) _next.Release(OwnerFor(LeaseRole.FeedbackNext), frameNumber);
            _previous = null;
            _next = null;
        }

        private ResourceOwnerKey OwnerFor(LeaseRole role) => new ResourceOwnerKey(
            _owner.SessionId, ResourceOwnerKind.Feedback, _owner.OwnerId, _owner.GenerationId, _owner.SlotId, role);

        private static void ClearTransparent(RenderTexture texture)
        {
            var previous = RenderTexture.active;
            try
            {
                RenderTexture.active = texture;
                GL.Clear(true, true, Color.clear);
            }
            finally { RenderTexture.active = previous; }
        }
    }
}
