using System;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;

namespace ShitDesigner.Media
{
    public interface IVideoFrameConversionPass : IDisposable
    {
        Result Convert(Texture source, RenderTexture target, VideoFrameConversionMetadata metadata);
    }

    /// <summary>Unity-side source conversion. Rec.709/sRGB transfer and
    /// straight-alpha premultiplication happen in one explicit pass before a
    /// frame enters the Runtime ImageFrame contract.</summary>
    public sealed class UnityVideoFrameConversionPass : IVideoFrameConversionPass
    {
        private Material _material;
        private readonly bool _ownsMaterial;
        private bool _disposed;
        public UnityVideoFrameConversionPass(Material material = null) { _material = material; _ownsMaterial = material == null; }

        public Result Convert(Texture source, RenderTexture target, VideoFrameConversionMetadata metadata)
        {
            if (_disposed) return Failure("media.frame.converter_disposed", "Video frame conversion pass is disposed.");
            if (source == null || target == null) return Failure("media.frame.converter_surface", "Video conversion requires source and destination textures.");
            metadata = metadata ?? new VideoFrameConversionMetadata(VideoColorEncoding.Rec709, VideoAlphaMode.Opaque);
            if (metadata.IsIdentity)
            {
                Graphics.Blit(source, target);
                return Result.Success();
            }
            if (_material == null)
            {
                var shader = Shader.Find("Hidden/ShitDesigner/VideoToLinearPremultiplied");
                if (shader == null) return Failure("media.frame.converter_shader", "The VideoToLinearPremultiplied shader is unavailable.");
                _material = new Material(shader) { name = "ShitDesigner.VideoFrameConversion" };
            }
            _material.SetFloat("_ColorEncoding", (float)metadata.ColorEncoding);
            _material.SetFloat("_AlphaMode", (float)metadata.AlphaMode);
            Graphics.Blit(source, target, _material);
            return Result.Success();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsMaterial && _material != null)
            {
                if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_material);
                else UnityEngine.Object.DestroyImmediate(_material);
            }
            _material = null;
        }
        private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media"));
    }

    /// <summary>Runtime adapter that copies a decoded Unity/Hap texture into
    /// the Phase-5 prepared output lease. It never acquires or releases a
    /// RenderTexture itself.</summary>
    public sealed class VideoOutputSurfaceFrameAdapter : IVideoOutputSurfaceFrameAdapterWithConversion, IDisposable
    {
        private readonly IVideoFrameConversionPass _conversion;
        public VideoOutputSurfaceFrameAdapter(IVideoFrameConversionPass conversion = null) { _conversion = conversion ?? new UnityVideoFrameConversionPass(); }

        public Result<IRuntimeImageFrame> Create(object borrowedTexture, IRuntimeOutputSurface preparedSurface, ulong frameNumber)
            => Create(borrowedTexture, preparedSurface, frameNumber, null);

        public Result<IRuntimeImageFrame> Create(object borrowedTexture, IRuntimeOutputSurface preparedSurface, ulong frameNumber, VideoFrameConversionMetadata metadata)
        {
            if (!(borrowedTexture is Texture source)) return Failure("media.frame.source", "Video backend did not expose a Unity texture.");
            if (!(preparedSurface?.NativeSurface is RenderTexture target) || preparedSurface.LeaseId == 0 || frameNumber == 0)
                return Failure("media.frame.surface", "Video output requires a live prepared surface lease.");
            if (preparedSurface.Width != target.width || preparedSurface.Height != target.height) return Failure("media.frame.descriptor", "Prepared video surface dimensions do not match its texture.");
            try
            {
                var converted = _conversion?.Convert(source, target, metadata);
                if (converted.HasValue && converted.Value.IsFailure) return Result<IRuntimeImageFrame>.Failure(converted.Value.Diagnostic);
                if (preparedSurface is IRuntimeOutputSurfaceCompletion completion) completion.MarkRendered();
                return Result<IRuntimeImageFrame>.Success(new MediaRuntimeImageFrame(preparedSurface, frameNumber));
            }
            catch (Exception exception)
            {
                return Result<IRuntimeImageFrame>.Failure(new Diagnostic(new DiagnosticCode("media.frame.blit_failed"), Severity.Error, exception.Message, module: "media", exception: DiagnosticExceptionInfo.FromException(exception)));
            }
        }

        public Result<IRuntimeImageFrame> Create(object borrowedTexture, int width, int height, ulong frameNumber, ulong leaseId)
        {
            return Failure("media.frame.phase5_required", "Video frame conversion must receive the prepared Phase-5 output surface.");
        }

        private static Result<IRuntimeImageFrame> Failure(string code, string message) => Result<IRuntimeImageFrame>.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media"));
        public void Dispose() { if (_conversion != null) _conversion.Dispose(); }
    }

    public sealed class MediaRuntimeImageFrame : IRuntimeImageFrameSurface
    {
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
    public sealed class VideoPlayerVisualNodeBinding : IRuntimeVisualNodeBinding
    {
        private readonly IVideoBackendFactory _backendFactory;
        private readonly IVideoPrepareResolver _resolver;
        private readonly IVideoFrameAdapter _frameAdapter;
        private readonly IVideoGraphicsCapabilities _graphics;
        public NodeTypeId TypeId { get; } = new NodeTypeId(VideoPlayerContract.NodeTypeId);
        public bool IsAvailable => _backendFactory != null && _frameAdapter != null;
        public Diagnostic AvailabilityDiagnostic => IsAvailable ? null : new Diagnostic(new DiagnosticCode("media.binding_missing"), Severity.Error, "Video production binding requires a backend factory and output frame adapter.", nodeTypeId: TypeId, module: "media");

        public VideoPlayerVisualNodeBinding(IVideoBackendFactory backendFactory, IVideoFrameAdapter frameAdapter, IVideoPrepareResolver resolver = null, IVideoGraphicsCapabilities graphics = null)
        { _backendFactory = backendFactory; _frameAdapter = frameAdapter; _resolver = resolver; _graphics = graphics; }

        public Result<IRuntimeNode> Create(RuntimeNodeCreateInfo node, ulong generationId)
        {
            if (!IsAvailable) return Result<IRuntimeNode>.Failure(AvailabilityDiagnostic);
            if (node == null || node.TypeId != TypeId || generationId == 0) return FailureNode("media.factory.node", "Video factory input does not match its binding.", node, generationId);
            var backendKind = VideoBackendKind.UnityVideoBackend;
            var asset = node.Parameters.FirstOrDefault(x => x.Id.Value == VideoPlayerContract.MediaAssetParameterId);
            if (asset != null && asset.Value.Type == ParameterType.MediaAssetReference && asset.Value.AsMediaAsset().HasValue)
            {
                if (_resolver == null) return FailureNode("media.asset.resolver", "A selected MediaAsset requires a verified MediaAsset resolver.", node, generationId);
                var request = _resolver.Resolve(asset.Value.AsMediaAsset().Value);
                if (request.IsFailure) return Result<IRuntimeNode>.Failure(request.Diagnostic);
                var selected = VideoBackendSelector.Select(request.Value.Probe, _graphics);
                if (selected.IsFailure) return Result<IRuntimeNode>.Failure(selected.Diagnostic);
                backendKind = selected.Value;
            }
            var backend = _backendFactory.Create(node.Id, generationId, backendKind);
            if (backend.IsFailure) return Result<IRuntimeNode>.Failure(backend.Diagnostic);
            try
            {
                var session = new VideoPlaybackSession(node.Id, generationId, backend.Value);
                return Result<IRuntimeNode>.Success(new VideoPlayerRuntimeNode(node.Id, generationId, session, new VideoTransportState(), _frameAdapter, prepareResolver: _resolver, backendFactory: _backendFactory, graphics: _graphics));
            }
            catch (Exception exception) { return FailureNode("media.factory.create", exception.Message, node, generationId, exception); }
        }

        private Result<IRuntimeNode> FailureNode(string code, string message, RuntimeNodeCreateInfo node, ulong generationId, Exception exception = null) =>
            Result<IRuntimeNode>.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: node?.Id ?? default(NodeInstanceId), nodeTypeId: TypeId, generationId: generationId, module: "media", exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception)));
    }
}
