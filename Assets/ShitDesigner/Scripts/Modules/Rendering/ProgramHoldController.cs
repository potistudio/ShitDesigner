using System;
using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace ShitDesigner.Rendering {
	public enum ProgramDynamicRange {
		Hdr,
		Ldr
	}

	public static class ProgramHoldFormatPolicy {
		public static GraphicsFormat FormatFor(ProgramDynamicRange range) =>
			range == ProgramDynamicRange.Ldr ? GraphicsFormat.R8G8B8A8_UNorm : GraphicsFormat.R16G16B16A16_SFloat;

		public static DisplayTransformMode DisplayModeFor(ProgramDynamicRange range) =>
			range == ProgramDynamicRange.Ldr ? DisplayTransformMode.Ldr : DisplayTransformMode.HdrAces;
	}

	public enum ProgramOutputState {
		OpaqueBlack,
		Available,
		HoldingLastFrame
	}

	/// <summary>
	/// Owns the stable Program Hold surface. Unavailable input never overwrites
	/// the last normal frame, and no diagnostic pixels are drawn onto it.
	/// </summary>
	public sealed class ProgramHoldController : IDisposable {
		public static readonly Vector2Int ProgramSize = new Vector2Int(1920, 1080);
		public static readonly GraphicsFormat DefaultColorFormat = GraphicsFormat.R16G16B16A16_SFloat;

		private readonly RenderTexturePool _pool;
		private readonly ResourceOwnerKey _owner;
		private readonly TextureDescriptor _descriptor;
		private TextureLeaseHandle _hold;
		private bool _disposed;
		private ulong _lastNormalFrame;

		public ProgramOutputState State { get; private set; } = ProgramOutputState.OpaqueBlack;
		public bool HasNormalFrame { get; private set; }
		public ulong LastNormalFrame => _lastNormalFrame;
		public TextureLeaseHandle HoldLease => _hold;
		public TextureDescriptor Descriptor => _descriptor;
		public ProgramDynamicRange DynamicRange => _descriptor.GraphicsFormat == GraphicsFormat.R8G8B8A8_UNorm ? ProgramDynamicRange.Ldr : ProgramDynamicRange.Hdr;
		public DisplayTransformMode DisplayMode => ProgramHoldFormatPolicy.DisplayModeFor(DynamicRange);

		// Program Hold must preserve the internal linear HDR pipeline even
		// while the source graph is unavailable.
		public ProgramHoldController(RenderTexturePool pool, ResourceOwnerKey owner, GraphicsFormat colorFormat = GraphicsFormat.R16G16B16A16_SFloat) {
			_pool = pool ?? throw new ArgumentNullException(nameof(pool));
			if (!owner.IsValid) throw new ArgumentException("A valid Program Hold owner is required.", nameof(owner));
			_owner = owner;
			if (colorFormat != GraphicsFormat.R16G16B16A16_SFloat && colorFormat != GraphicsFormat.R8G8B8A8_UNorm)
				throw new ArgumentException("Program Hold supports only the fixed HDR RGBA16F or LDR RGBA8 format.", nameof(colorFormat));
			_descriptor = new TextureDescriptor(ProgramSize.x, ProgramSize.y, colorFormat, roleDepth(), 1, false, false, TextureDimension.Tex2D, 1, false);
		}

		public ProgramHoldController(RenderTexturePool pool, ResourceOwnerKey owner, ProgramDynamicRange dynamicRange)
			: this(pool, owner, ProgramHoldFormatPolicy.FormatFor(dynamicRange)) { }

		private static GraphicsFormat roleDepth() => GraphicsFormat.None;

		public UnitResult<Diagnostic> Ensure(ulong frameNumber) {
			if (_disposed) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.program.disposed", "The Program Hold controller is disposed."));
			if (frameNumber == 0) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.program.frame_invalid", "Program frame number must be positive."));
			if (_hold != null && !_hold.IsReleased) return UnitResult.Success<Diagnostic>();
			var acquired = _pool.Acquire(_descriptor, _owner, frameNumber);
			if (acquired.IsFailure) return UnitResult.Failure<Diagnostic>(acquired.Error);
			_hold = acquired.Value;
			try {
				ClearOpaqueBlack(_hold.Texture);
				State = ProgramOutputState.OpaqueBlack;
				HasNormalFrame = false;
				_lastNormalFrame = 0;
				return UnitResult.Success<Diagnostic>();
			}
			catch (Exception exception) {
				_hold.Release(_owner, frameNumber);
				_hold = null;
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.program.clear_failed"), Severity.Error, "Program Hold initialization failed.", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		public UnitResult<Diagnostic> SubmitAvailable(ImageFrame source, ulong frameNumber) {
			if (frameNumber == 0) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.program.frame_invalid", "Program frame number must be positive."));
			var ensured = Ensure(frameNumber);
			if (ensured.IsFailure) return ensured;
			if (source.Texture == null || source.Size != ProgramSize || source.ColorFormat != _descriptor.GraphicsFormat)
				return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.program.input_mismatch", "Program input must match the fixed Program Hold descriptor."));
			if (source.Texture == _hold.Texture) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.program.self_copy", "Program input cannot be the Program Hold texture."));
			try {
				Graphics.Blit(source.Texture, _hold.Texture);
				HasNormalFrame = true;
				_lastNormalFrame = source.FrameNumber;
				State = ProgramOutputState.Available;
				return UnitResult.Success<Diagnostic>();
			}
			catch (Exception exception) {
				State = HasNormalFrame ? ProgramOutputState.HoldingLastFrame : ProgramOutputState.OpaqueBlack;
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.program.copy_failed"), Severity.Error, "Program Hold copy failed.", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		/// <summary>Bootstrap bridge for Runtime's neutral image contract.
		/// Runtime never receives the pool or this controller; the concrete
		/// Rendering boundary performs the copy into the session-owned Hold
		/// lease after Phase 8.</summary>
		public UnitResult<Diagnostic> SubmitAvailable(IRuntimeImageFrame source, ulong frameNumber) {
			if (source == null || !(source is IRuntimeImageFrameSurface surface) || !(surface.NativeSurface is RenderTexture texture))
				return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.program.input_invalid", "Program Hold requires a RenderTexture-backed Runtime image frame."));
			if (source.Width != ProgramSize.x || source.Height != ProgramSize.y)
				return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.program.input_mismatch", "Program input must match the fixed Program Hold dimensions."));
			// The neutral frame seam carries the actual surface format. Do
			// this check before Ensure so a cross HDR/LDR submission cannot
			// allocate or overwrite the existing Hold frame.
			if (!string.Equals(source.ColorFormat, _descriptor.GraphicsFormat.ToString(), StringComparison.Ordinal))
				return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.program.format_mismatch", "Program input format must match the configured Hold dynamic range."));
			var ensured = Ensure(frameNumber);
			if (ensured.IsFailure) return ensured;
			if (ReferenceEquals(texture, _hold.Texture)) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.program.self_copy", "Program input cannot be the Program Hold texture."));
			try {
				Graphics.Blit(texture, _hold.Texture);
				HasNormalFrame = true;
				_lastNormalFrame = source.FrameNumber == 0 ? frameNumber : source.FrameNumber;
				State = ProgramOutputState.Available;
				return UnitResult.Success<Diagnostic>();
			}
			catch (Exception exception) {
				State = HasNormalFrame ? ProgramOutputState.HoldingLastFrame : ProgramOutputState.OpaqueBlack;
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("rendering.program.copy_failed"), Severity.Error, "Program Hold copy failed.", exception: DiagnosticExceptionInfo.FromException(exception), module: "rendering"));
			}
		}

		public UnitResult<Diagnostic> SubmitUnavailable(ulong frameNumber) {
			if (frameNumber == 0) return UnitResult.Failure<Diagnostic>(RenderingDiagnostics.Error("rendering.program.frame_invalid", "Program frame number must be positive."));
			var ensured = Ensure(frameNumber);
			if (ensured.IsFailure) return ensured;
			State = HasNormalFrame ? ProgramOutputState.HoldingLastFrame : ProgramOutputState.OpaqueBlack;
			return UnitResult.Success<Diagnostic>();
		}

		public Result<ImageFrame, Diagnostic> GetFrame(ulong frameNumber) {
			if (_hold == null || _hold.IsReleased) return Result.Failure<ImageFrame, Diagnostic>(RenderingDiagnostics.Error("rendering.program.hold_missing", "Program Hold has not been acquired."));
			var borrowed = _hold.Borrow(frameNumber);
			if (borrowed.IsFailure) return Result.Failure<ImageFrame, Diagnostic>(borrowed.Error);
			var frame = borrowed.Value.Frame;
			return Result.Success<ImageFrame, Diagnostic>(new ImageFrame(frame.Texture, frame.Size, frame.ColorFormat, HasNormalFrame ? _lastNormalFrame : frameNumber, frame.LeaseId));
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			if (_hold != null && !_hold.IsReleased) _hold.Release(_owner, _pool.CurrentFrame);
			_hold = null;
		}

		private static void ClearOpaqueBlack(RenderTexture texture) {
			var previous = RenderTexture.active;
			try {
				RenderTexture.active = texture;
				GL.Clear(true, true, Color.black);
			}
			finally {
				RenderTexture.active = previous;
			}
		}
	}
}
