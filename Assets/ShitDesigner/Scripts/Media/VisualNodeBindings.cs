using System;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;

namespace ShitDesigner.Media {
	public interface IVideoFrameConversionPass : IDisposable {
		CSharpFunctionalExtensions.UnitResult<Diagnostic> Convert(Texture source, RenderTexture target, VideoFrameConversionMetadata metadata);
	}

	/// <summary>Unity-side source conversion. Rec.709/sRGB transfer and
	/// straight-alpha premultiplication happen in one explicit pass before a
	/// frame enters the Runtime ImageFrame contract.</summary>
	public sealed class UnityVideoFrameConversionPass : IVideoFrameConversionPass {
		private Material _material;
		private readonly bool _ownsMaterial;
		private bool _disposed;
		public UnityVideoFrameConversionPass(Material material = null) { _material = material; _ownsMaterial = material == null; }

		public CSharpFunctionalExtensions.UnitResult<Diagnostic> Convert(Texture source, RenderTexture target, VideoFrameConversionMetadata metadata) {
			if (_disposed) return Failure("media.frame.converter_disposed", "Video frame conversion pass is disposed.");
			if (source == null || target == null) return Failure("media.frame.converter_surface", "Video conversion requires source and destination textures.");
			metadata = metadata ?? new VideoFrameConversionMetadata(VideoColorEncoding.Rec709, VideoAlphaMode.Opaque);
			if (metadata.IsIdentity) {
				Graphics.Blit(source, target);
				return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
			}
			if (_material == null) {
				var shader = Shader.Find("Hidden/ShitDesigner/VideoToLinearPremultiplied");
				if (shader == null) return Failure("media.frame.converter_shader", "The VideoToLinearPremultiplied shader is unavailable.");
				_material = new Material(shader) { name = "ShitDesigner.VideoFrameConversion" };
			}
			_material.SetFloat("_ColorEncoding", (float)metadata.ColorEncoding);
			_material.SetFloat("_AlphaMode", (float)metadata.AlphaMode);
			Graphics.Blit(source, target, _material);
			return CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>();
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			if (_ownsMaterial && _material != null) {
				if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_material);
				else UnityEngine.Object.DestroyImmediate(_material);
			}
			_material = null;
		}
		private static CSharpFunctionalExtensions.UnitResult<Diagnostic> Failure(string code, string message) => CSharpFunctionalExtensions.UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media"));
	}

	/// <summary>Runtime adapter that copies a decoded Unity/Hap texture into
	/// the Phase-5 prepared output lease. It never acquires or releases a
	/// RenderTexture itself.</summary>
	public sealed class VideoOutputSurfaceFrameAdapter : IVideoOutputSurfaceFrameAdapterWithConversion, IDisposable {
		private readonly IVideoFrameConversionPass _conversion;
		public VideoOutputSurfaceFrameAdapter(IVideoFrameConversionPass conversion = null) { _conversion = conversion ?? new UnityVideoFrameConversionPass(); }

		public CSharpFunctionalExtensions.Result<IRuntimeImageFrame, Diagnostic> Create(object borrowedTexture, IRuntimeOutputSurface preparedSurface, ulong frameNumber)
			=> Create(borrowedTexture, preparedSurface, frameNumber, null);

		public CSharpFunctionalExtensions.Result<IRuntimeImageFrame, Diagnostic> Create(object borrowedTexture, IRuntimeOutputSurface preparedSurface, ulong frameNumber, VideoFrameConversionMetadata metadata) {
			if (!(borrowedTexture is Texture source)) return Failure("media.frame.source", "Video backend did not expose a Unity texture.");
			if (!(preparedSurface?.NativeSurface is RenderTexture target) || preparedSurface.LeaseId == 0 || frameNumber == 0)
				return Failure("media.frame.surface", "Video output requires a live prepared surface lease.");
			if (preparedSurface.Width != target.width || preparedSurface.Height != target.height) return Failure("media.frame.descriptor", "Prepared video surface dimensions do not match its texture.");
			try {
				var converted = _conversion?.Convert(source, target, metadata);
				if (converted.HasValue && converted.Value.IsFailure) return CSharpFunctionalExtensions.Result.Failure<IRuntimeImageFrame, Diagnostic>(converted.Value.Error);
				if (preparedSurface is IRuntimeOutputSurfaceCompletion completion) completion.MarkRendered();
				return CSharpFunctionalExtensions.Result.Success<IRuntimeImageFrame, Diagnostic>(new MediaRuntimeImageFrame(preparedSurface, frameNumber));
			}
			catch (Exception exception) {
				return CSharpFunctionalExtensions.Result.Failure<IRuntimeImageFrame, Diagnostic>(new Diagnostic(new DiagnosticCode("media.frame.blit_failed"), Severity.Error, exception.Message, module: "media", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		public CSharpFunctionalExtensions.Result<IRuntimeImageFrame, Diagnostic> Create(object borrowedTexture, int width, int height, ulong frameNumber, ulong leaseId) {
			return Failure("media.frame.phase5_required", "Video frame conversion must receive the prepared Phase-5 output surface.");
		}

		private static CSharpFunctionalExtensions.Result<IRuntimeImageFrame, Diagnostic> Failure(string code, string message) => CSharpFunctionalExtensions.Result.Failure<IRuntimeImageFrame, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media"));
		public void Dispose() { if (_conversion != null) _conversion.Dispose(); }
	}

	public sealed class MediaRuntimeImageFrame : IRuntimeImageFrameSurface {
		private readonly IRuntimeOutputSurface _surface;
		public int Width => _surface.Width;
		public int Height => _surface.Height;
		public string ColorFormat => (_surface as IRuntimeOutputSurfaceFormat)?.ColorFormat ?? "R16G16B16A16_SFloat";
		public ulong FrameNumber { get; }
		public ulong LeaseId => _surface.LeaseId;
		public object NativeSurface => _surface.NativeSurface;
		public MediaRuntimeImageFrame(IRuntimeOutputSurface surface, ulong frameNumber) { _surface = surface ?? throw new ArgumentNullException(nameof(surface)); if (surface.LeaseId == 0 || frameNumber == 0) throw new ArgumentException("A live output surface and frame are required."); FrameNumber = frameNumber; }
	}

	/// <summary>Concrete Media factory. The backend factory is injected by
	/// Bootstrap, while probe/asset containment stays behind Media's resolver
	/// boundary.</summary>
	public sealed class VideoPlayerVisualNodeBinding : IRuntimeVisualNodeBinding {
		private readonly IVideoBackendFactory _backendFactory;
		private readonly IVideoPrepareResolver _resolver;
		private readonly IVideoFrameAdapter _frameAdapter;
		private readonly IVideoGraphicsCapabilities _graphics;
		public NodeTypeId TypeId { get; } = new NodeTypeId(VideoPlayerContract.NodeTypeId);
		public bool IsAvailable => _backendFactory != null && _frameAdapter != null;
		public Diagnostic AvailabilityDiagnostic => IsAvailable ? null : new Diagnostic(new DiagnosticCode("media.binding_missing"), Severity.Error, "Video production binding requires a backend factory and output frame adapter.", nodeTypeId: TypeId, module: "media");

		public VideoPlayerVisualNodeBinding(IVideoBackendFactory backendFactory, IVideoFrameAdapter frameAdapter, IVideoPrepareResolver resolver = null, IVideoGraphicsCapabilities graphics = null) { _backendFactory = backendFactory; _frameAdapter = frameAdapter; _resolver = resolver; _graphics = graphics; }

		public CSharpFunctionalExtensions.Result<IRuntimeNode, Diagnostic> Create(RuntimeNodeCreateInfo node, ulong generationId) {
			if (!IsAvailable) return CSharpFunctionalExtensions.Result.Failure<IRuntimeNode, Diagnostic>(AvailabilityDiagnostic);
			if (node == null || node.TypeId != TypeId || generationId == 0) return FailureNode("media.factory.node", "Video factory input does not match its binding.", node, generationId);
			var backendKind = VideoBackendKind.UnityVideoBackend;
			var asset = node.Parameters.FirstOrDefault(x => x.Id.Value == VideoPlayerContract.MediaAssetParameterId);
			if (asset != null && asset.Value.Type == ParameterType.MediaAssetReference && asset.Value.AsMediaAsset().HasValue) {
				if (_resolver == null) return FailureNode("media.asset.resolver", "A selected MediaAsset requires a verified MediaAsset resolver.", node, generationId);
				var request = _resolver.Resolve(asset.Value.AsMediaAsset().Value);
				if (request.IsFailure) return CSharpFunctionalExtensions.Result.Failure<IRuntimeNode, Diagnostic>(request.Error);
				var selected = VideoBackendSelector.Select(request.Value.Probe, _graphics);
				if (selected.IsFailure) return CSharpFunctionalExtensions.Result.Failure<IRuntimeNode, Diagnostic>(selected.Error);
				backendKind = selected.Value;
			}
			var backend = _backendFactory.Create(node.Id, generationId, backendKind);
			if (backend.IsFailure) return CSharpFunctionalExtensions.Result.Failure<IRuntimeNode, Diagnostic>(backend.Error);
			try {
				var session = new VideoPlaybackSession(node.Id, generationId, backend.Value);
				return CSharpFunctionalExtensions.Result.Success<IRuntimeNode, Diagnostic>(new VideoPlayerRuntimeNode(node.Id, generationId, session, new VideoTransportState(), _frameAdapter, prepareResolver: _resolver, backendFactory: _backendFactory, graphics: _graphics));
			}
			catch (Exception exception) { return FailureNode("media.factory.create", exception.Message, node, generationId, exception); }
		}

		private CSharpFunctionalExtensions.Result<IRuntimeNode, Diagnostic> FailureNode(string code, string message, RuntimeNodeCreateInfo node, ulong generationId, Exception exception = null) =>
			CSharpFunctionalExtensions.Result.Failure<IRuntimeNode, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: node?.Id ?? default(NodeInstanceId), nodeTypeId: TypeId, generationId: generationId, module: "media", exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception)));
	}
}
