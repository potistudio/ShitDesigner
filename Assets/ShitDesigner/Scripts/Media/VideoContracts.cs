using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Runtime;

namespace ShitDesigner.Media
{
    public enum VideoContainer
    {
        Unknown,
        Mp4,
        WebM,
        Mov
    }

    public enum VideoCodec
    {
        Unknown,
        H264,
        VP8,
        Hap1,
        Hap5,
        HapY,
        HapM,
        HapR,
        HapHdr,
        AlphaOnly
    }

    public enum VideoBackendKind
    {
        UnityVideoBackend,
        HapVideoBackend
    }

    public enum VideoColorEncoding { Rec709, Srgb, Linear }
    public enum VideoAlphaMode { Opaque, Straight, Premultiplied }

    /// <summary>Authoritative source semantics from the codec probe. The
    /// output adapter converts this into the session's linear premultiplied
    /// internal format; it is never inferred from a Unity texture name.</summary>
    public sealed class VideoFrameConversionMetadata
    {
        public VideoColorEncoding ColorEncoding { get; }
        public VideoAlphaMode AlphaMode { get; }
        public VideoFrameConversionMetadata(VideoColorEncoding colorEncoding, VideoAlphaMode alphaMode)
        { ColorEncoding = colorEncoding; AlphaMode = alphaMode; }
        public bool IsIdentity => ColorEncoding == VideoColorEncoding.Linear && AlphaMode == VideoAlphaMode.Premultiplied;
    }

    public sealed class VideoSource : IEquatable<VideoSource>
    {
        public string Value { get; }

        private VideoSource(string value)
        {
            Value = value;
        }

        /// <summary>Creates the runtime-only absolute path view. Callers that
        /// start from project state must use FromProjectFile so containment is
        /// checked before this method is reached.</summary>
        public static VideoSource FromFile(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !Path.IsPathRooted(absolutePath))
                throw new ArgumentException("Video file input must be an absolute path.", nameof(absolutePath));
            return new VideoSource(Path.GetFullPath(absolutePath));
        }

        /// <summary>Parses a local absolute path. A file URI is accepted as a
        /// compatibility alias, but network URLs are never a source kind.</summary>
        public static VideoSource Parse(string absoluteFilePath)
        {
            if (Uri.TryCreate(absoluteFilePath, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeFile)
                return FromFile(uri.LocalPath);
            return FromFile(absoluteFilePath);
        }

        public static VideoSource FromProjectFile(string projectRoot, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(projectRoot) || !Path.IsPathRooted(projectRoot)) throw new ArgumentException("Project root must be absolute.", nameof(projectRoot));
            if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath)) throw new ArgumentException("Media path must be project-relative.", nameof(relativePath));
            var root = Path.GetFullPath(projectRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var full = Path.GetFullPath(Path.Combine(root, relativePath));
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Media path escapes the project root.", nameof(relativePath));
            return FromFile(full);
        }

        public bool Equals(VideoSource other) => other != null && string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => Equals(obj as VideoSource);
        public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value;
    }

    public enum VideoBackendState
    {
        Created,
        Preparing,
        Ready,
        Playing,
        Paused,
        Faulted,
        Unsupported,
        Disposed
    }

    public enum VideoCompletionKind
    {
        PrepareStarted,
        Prepared,
        SeekStarted,
        SeekCompleted,
        FrameReady,
        Error,
        Ended,
        Looped,
        Disposed
    }

    public enum VideoPlaybackStatus
    {
        NoSource,
        Preparing,
        Ready,
        Playing,
        Paused,
        Ended,
        Faulted,
        Disposed
    }

    public enum HapDecodePath
    {
        DirectCompressed,
        Compute,
        Cpu,
        Unsupported
    }

    public sealed class VideoProbeResult
    {
        public bool Supported { get; }
        public VideoContainer Container { get; }
        public VideoCodec Codec { get; }
        public bool HasAlpha { get; }
        public bool HasAudio { get; }
        public double DurationSeconds { get; }
        public string DiagnosticMessage { get; }
        public VideoFrameConversionMetadata ConversionMetadata { get; }

        private VideoProbeResult(bool supported, VideoContainer container, VideoCodec codec, bool hasAlpha, bool hasAudio, double durationSeconds, string diagnosticMessage, VideoFrameConversionMetadata conversionMetadata)
        {
            Supported = supported; Container = container; Codec = codec; HasAlpha = hasAlpha; HasAudio = hasAudio;
            DurationSeconds = durationSeconds; DiagnosticMessage = diagnosticMessage ?? string.Empty;
            ConversionMetadata = conversionMetadata ?? new VideoFrameConversionMetadata(VideoColorEncoding.Rec709, hasAlpha ? VideoAlphaMode.Straight : VideoAlphaMode.Opaque);
        }

        public static VideoProbeResult SupportedVideo(VideoContainer container, VideoCodec codec, bool hasAlpha = false, bool hasAudio = false, double durationSeconds = 0d, VideoFrameConversionMetadata conversionMetadata = null)
        {
            if (container == VideoContainer.Unknown || codec == VideoCodec.Unknown) throw new ArgumentException("A supported container and codec are required.");
            if (double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) || durationSeconds < 0d) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            if (conversionMetadata == null && (codec == VideoCodec.Hap1 || codec == VideoCodec.Hap5 || codec == VideoCodec.HapY || codec == VideoCodec.HapM))
                conversionMetadata = new VideoFrameConversionMetadata(VideoColorEncoding.Linear, VideoAlphaMode.Premultiplied);
            return new VideoProbeResult(true, container, codec, hasAlpha, hasAudio, durationSeconds, string.Empty, conversionMetadata);
        }

        public static VideoProbeResult UnsupportedVideo(VideoContainer container, VideoCodec codec, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("An unsupported diagnostic is required.", nameof(message));
            return new VideoProbeResult(false, container, codec, false, false, 0d, message, null);
        }
    }

    public sealed class VideoPrepareRequest
    {
        public VideoSource Source { get; }
        public string AbsolutePath { get; }
        /// <summary>The verified absolute local path passed to Unity
        /// VideoPlayer.url at runtime. It is never persisted as project state.</summary>
        public string Url => Source.Value;
        public VideoProbeResult Probe { get; }
        public VideoFrameConversionMetadata ConversionMetadata => Probe.ConversionMetadata;

        public VideoPrepareRequest(string absolutePath, VideoProbeResult probe)
        {
            Source = VideoSource.Parse(absolutePath);
            Probe = probe ?? throw new ArgumentNullException(nameof(probe));
            AbsolutePath = Source.Value;
        }

        public VideoPrepareRequest(VideoSource source, VideoProbeResult probe)
        {
            Source = source ?? throw new ArgumentNullException(nameof(source));
            Probe = probe ?? throw new ArgumentNullException(nameof(probe));
            AbsolutePath = Source.Value;
        }
    }

    public sealed class VideoBackendCompletion
    {
        public NodeInstanceId NodeId { get; }
        public ulong GenerationId { get; }
        public VideoCompletionKind Kind { get; }
        public double TimeSeconds { get; }
        public long FrameIndex { get; }
        public string ErrorMessage { get; }

        public VideoBackendCompletion(NodeInstanceId nodeId, ulong generationId, VideoCompletionKind kind, double timeSeconds = 0d, long frameIndex = -1, string errorMessage = null)
        {
            if (nodeId.IsEmpty || generationId == 0) throw new ArgumentException("Video callback owner identity is required.");
            NodeId = nodeId; GenerationId = generationId; Kind = kind; TimeSeconds = timeSeconds; FrameIndex = frameIndex; ErrorMessage = errorMessage ?? string.Empty;
        }
    }

    public interface IVideoCapabilityProbe
    {
        Result<VideoProbeResult> Probe(string absolutePath);
    }

    /// <summary>Codec/container metadata boundary. A real implementation may
    /// call Media Foundation/AVFoundation or a native Hap parser; it stays
    /// outside the node contract so deterministic tests can inject metadata.</summary>
    public interface IVideoMetadataProbe
    {
        Result<VideoProbeResult> Probe(string absolutePath);
    }

    public interface IVideoGraphicsCapabilities
    {
        bool SupportsCompressedTexturePath { get; }
        bool SupportsComputePath { get; }
        bool SupportsCpuPath { get; }
    }

    public sealed class VideoGraphicsCapabilities : IVideoGraphicsCapabilities
    {
        public bool SupportsCompressedTexturePath { get; }
        public bool SupportsComputePath { get; }
        public bool SupportsCpuPath { get; }
        public VideoGraphicsCapabilities(bool supportsCompressedTexturePath, bool supportsComputePath, bool supportsCpuPath)
        {
            SupportsCompressedTexturePath = supportsCompressedTexturePath;
            SupportsComputePath = supportsComputePath;
            SupportsCpuPath = supportsCpuPath;
        }
    }

    public sealed class ExtensionVideoCapabilityProbe : IVideoCapabilityProbe
    {
        private readonly IVideoMetadataProbe _metadataProbe;

        public ExtensionVideoCapabilityProbe(IVideoMetadataProbe metadataProbe = null)
        {
            _metadataProbe = metadataProbe;
        }

        public Result<VideoProbeResult> Probe(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !Path.IsPathRooted(absolutePath)) return Failure("media.probe.path", "Video probe requires a verified absolute path.");
            if (_metadataProbe != null)
            {
                var metadata = _metadataProbe.Probe(Path.GetFullPath(absolutePath));
                if (metadata.IsSuccess) return metadata;
                // A metadata reader owns the authoritative codec result. Do
                // not silently guess from an extension after it reports a
                // malformed/unsupported file.
                return metadata;
            }
            return Failure("media.probe.metadata_required", "Codec metadata is required; extension-only probing cannot identify the guaranteed video variants.");
        }

        private static Result<VideoProbeResult> Failure(string code, string message) => Result<VideoProbeResult>.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
    }

    /// <summary>Content probe used by the production resolver. It checks the
    /// container/sample markers in the file itself; an extension can never
    /// claim a codec. The small marker reader is deliberately conservative:
    /// malformed/truncated content and VP9 are rejected before Unity's
    /// VideoPlayer receives a URL. Hap MOVs use the strict sample-table parser
    /// so their duration and codec come from the file.</summary>
    public sealed class FileVideoMetadataProbe : IVideoMetadataProbe
    {
        public Result<VideoProbeResult> Probe(string absolutePath)
        {
            if (string.IsNullOrWhiteSpace(absolutePath) || !Path.IsPathRooted(absolutePath)) return Failure("media.probe.path", "Video probe requires a verified absolute path.");
            if (!File.Exists(absolutePath)) return Failure("media.probe.missing", "The video file does not exist.");
            var extension = Path.GetExtension(absolutePath).ToLowerInvariant();
            try
            {
                if (extension == ".mov")
                {
                    if (!HapMovie.TryOpen(absolutePath, out var movie, out var error)) return Result<VideoProbeResult>.Success(VideoProbeResult.UnsupportedVideo(VideoContainer.Mov, VideoCodec.Unknown, error ?? "The MOV file did not contain a supported Hap video track."));
                    var duration = movie.TimeScale == 0 ? 0d : movie.DurationTicks / (double)movie.TimeScale;
                    var alpha = movie.Codec == VideoCodec.Hap5 || movie.Codec == VideoCodec.HapM;
                    return IsGuaranteedHap(movie.Codec)
                        ? Result<VideoProbeResult>.Success(VideoProbeResult.SupportedVideo(VideoContainer.Mov, movie.Codec, hasAlpha: alpha, durationSeconds: duration))
                        : Result<VideoProbeResult>.Success(VideoProbeResult.UnsupportedVideo(VideoContainer.Mov, movie.Codec, "This Hap MOV variant is outside the guaranteed production codec set."));
                }

                var bytes = File.ReadAllBytes(absolutePath);
                if (extension == ".mp4")
                {
                    if (!Contains(bytes, "ftyp") || !Contains(bytes, "moov") || !Contains(bytes, "mdat")) return Result<VideoProbeResult>.Success(VideoProbeResult.UnsupportedVideo(VideoContainer.Mp4, VideoCodec.Unknown, "The MP4 atom table is truncated or malformed."));
                    if (!Contains(bytes, "avc1") && !Contains(bytes, "avc3")) return Result<VideoProbeResult>.Success(VideoProbeResult.UnsupportedVideo(VideoContainer.Mp4, VideoCodec.Unknown, "MP4 content did not advertise a guaranteed H.264 sample entry."));
                    return Result<VideoProbeResult>.Success(VideoProbeResult.SupportedVideo(VideoContainer.Mp4, VideoCodec.H264, hasAudio: Contains(bytes, "mp4a")));
                }
                if (extension == ".webm")
                {
                    if (!Contains(bytes, "webm") || !Contains(bytes, "V_VP8")) return Result<VideoProbeResult>.Success(VideoProbeResult.UnsupportedVideo(VideoContainer.WebM, VideoCodec.Unknown, Contains(bytes, "V_VP9") ? "VP9 WebM is outside the guaranteed Unity backend contract." : "WebM content did not advertise a guaranteed VP8 sample."));
                    return Result<VideoProbeResult>.Success(VideoProbeResult.SupportedVideo(VideoContainer.WebM, VideoCodec.VP8, hasAlpha: Contains(bytes, "ALPHA") || Contains(bytes, "ALPH")));
                }
                return Result<VideoProbeResult>.Success(VideoProbeResult.UnsupportedVideo(VideoContainer.Unknown, VideoCodec.Unknown, "The production video metadata adapter does not guarantee this container/codec."));
            }
            catch (IOException exception) { return Failure("media.probe.read", exception.Message); }
            catch (UnauthorizedAccessException exception) { return Failure("media.probe.read", exception.Message); }
        }

        private static bool IsGuaranteedHap(VideoCodec codec) => codec == VideoCodec.Hap1 || codec == VideoCodec.Hap5 || codec == VideoCodec.HapY || codec == VideoCodec.HapM;
        private static bool Contains(byte[] bytes, string marker)
        {
            if (bytes == null || string.IsNullOrEmpty(marker)) return false;
            var needle = System.Text.Encoding.ASCII.GetBytes(marker);
            for (var i = 0; i <= bytes.Length - needle.Length; i++)
            {
                var match = true;
                for (var n = 0; n < needle.Length; n++) if (bytes[i + n] != needle[n]) { match = false; break; }
                if (match) return true;
            }
            return false;
        }

        private static Result<VideoProbeResult> Failure(string code, string message) => Result<VideoProbeResult>.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media"));
    }

    public static class VideoBackendSelector
    {
        public static Result<VideoBackendKind> Select(VideoProbeResult probe, IVideoGraphicsCapabilities graphics = null)
        {
            if (probe == null) return Failure("media.probe.missing", "Video probe result is required.");
            if (!probe.Supported) return Failure("media.probe.unsupported", probe.DiagnosticMessage);
            if ((probe.Container == VideoContainer.Mp4 && probe.Codec == VideoCodec.H264)
                || (probe.Container == VideoContainer.WebM && probe.Codec == VideoCodec.VP8))
                return Result<VideoBackendKind>.Success(VideoBackendKind.UnityVideoBackend);
            if (probe.Codec == VideoCodec.Hap1 || probe.Codec == VideoCodec.Hap5 || probe.Codec == VideoCodec.HapY || probe.Codec == VideoCodec.HapM)
            {
                if (probe.Container != VideoContainer.Mov) return Failure("media.container.unsupported", "Guaranteed Hap variants require a MOV container.");
                var path = HapGraphicsPath.Select(graphics);
                return path == HapDecodePath.Unsupported ? Failure("media.hap.unsupported", "No supported Hap decode path is available.") : Result<VideoBackendKind>.Success(VideoBackendKind.HapVideoBackend);
            }
            return Failure("media.codec.unsupported", "The codec is outside the initial video contract.");
        }

        private static Result<VideoBackendKind> Failure(string code, string message) => Result<VideoBackendKind>.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
    }

    public static class HapGraphicsPath
    {
        public static HapDecodePath Select(IVideoGraphicsCapabilities graphics)
        {
            if (graphics == null) return HapDecodePath.Unsupported;
            if (graphics.SupportsCompressedTexturePath) return HapDecodePath.DirectCompressed;
            if (graphics.SupportsComputePath) return HapDecodePath.Compute;
            if (graphics.SupportsCpuPath) return HapDecodePath.Cpu;
            return HapDecodePath.Unsupported;
        }
    }

    public interface IVideoBackendHandle : IDisposable
    {
        NodeInstanceId NodeId { get; }
        ulong GenerationId { get; }
        VideoBackendKind BackendKind { get; }
        VideoBackendState State { get; }
        object BorrowedTexture { get; }
        event Action<VideoBackendCompletion> Completed;
        Result Prepare(VideoPrepareRequest request);
        Result Play();
        Result Pause();
        Result Stop();
        Result SetSpeed(double speed);
        Result SetLoop(bool loop);
        Result Seek(double seconds);
        Result SyncToGraphClock(double logicalSeconds, bool demanded);
    }

    /// <summary>Small lifecycle base for concrete Unity and Hap backends. It
    /// centralizes owner identity, disposal and callback emission so both
    /// implementations obey the same stale-callback rules.</summary>
    public abstract class VideoBackendHandleBase : IVideoBackendHandle
    {
        private bool _disposed;
        protected VideoBackendHandleBase(NodeInstanceId nodeId, ulong generationId, VideoBackendKind backendKind)
        {
            if (nodeId.IsEmpty || generationId == 0) throw new ArgumentException("Video backend owner identity is required.");
            NodeId = nodeId;
            GenerationId = generationId;
            BackendKind = backendKind;
            State = VideoBackendState.Created;
        }

        public NodeInstanceId NodeId { get; }
        public ulong GenerationId { get; }
        public VideoBackendKind BackendKind { get; }
        public VideoBackendState State { get; protected set; }
        public abstract object BorrowedTexture { get; }
        public event Action<VideoBackendCompletion> Completed;

        public abstract Result Prepare(VideoPrepareRequest request);
        public abstract Result Play();
        public abstract Result Pause();
        public abstract Result Stop();
        public abstract Result SetSpeed(double speed);
        public abstract Result SetLoop(bool loop);
        public abstract Result Seek(double seconds);
        public abstract Result SyncToGraphClock(double logicalSeconds, bool demanded);

        protected void Emit(VideoCompletionKind kind, double timeSeconds = 0d, long frameIndex = -1, string errorMessage = null)
        {
            if (_disposed) return;
            Completed?.Invoke(new VideoBackendCompletion(NodeId, GenerationId, kind, timeSeconds, frameIndex, errorMessage));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            BeforeDispose();
            State = VideoBackendState.Disposed;
            Completed?.Invoke(new VideoBackendCompletion(NodeId, GenerationId, VideoCompletionKind.Disposed));
            DisposeCore();
        }

        /// <summary>Detaches engine/native callbacks before the terminal
        /// completion is published. Concrete backends override this hook to
        /// make host destruction and late callbacks harmless.</summary>
        protected virtual void BeforeDispose() { }

        protected abstract void DisposeCore();
    }

    /// <summary>Factory boundary used by VideoPlayer nodes.  Native and Unity
    /// implementations stay behind this interface so the node can be tested
    /// with a deterministic backend.</summary>
    public interface IVideoBackendFactory
    {
        Result<IVideoBackendHandle> Create(NodeInstanceId nodeId, ulong generationId, VideoBackendKind kind);
    }

    public enum VideoTransportEventKind
    {
        MediaChanged,
        Play,
        Pause,
        SeekStarted,
        SeekCompleted,
        FrameReady,
        Ended,
        Looped,
        Error
    }

    public sealed class VideoTransportEvent
    {
        public VideoTransportEventKind Kind { get; }
        public double TimeSeconds { get; }
        public long FrameIndex { get; }
        public Diagnostic Diagnostic { get; }

        public VideoTransportEvent(VideoTransportEventKind kind, double timeSeconds = 0d, long frameIndex = -1, Diagnostic diagnostic = null)
        {
            if (double.IsNaN(timeSeconds) || double.IsInfinity(timeSeconds) || timeSeconds < 0d)
                throw new ArgumentOutOfRangeException(nameof(timeSeconds));
            Kind = kind;
            TimeSeconds = timeSeconds;
            FrameIndex = frameIndex;
            Diagnostic = diagnostic;
        }
    }

    /// <summary>Deterministic transport state shared by Unity and Hap backends.
    /// It owns logical time, not a backend clock or a Unity object.</summary>
    public sealed class VideoTransportState
    {
        private MediaAssetId? _mediaAsset;
        private double _durationSeconds;
        private double _playheadSeconds;
        private double _speed = 1d;
        private bool _playing;
        private bool _loop = true;

        public MediaAssetId? MediaAsset => _mediaAsset;
        public bool HasMediaAsset => _mediaAsset.HasValue;
        public bool Playing => _playing;
        public double PlayheadSeconds => _playheadSeconds;
        public double DurationSeconds => _durationSeconds;
        public double Speed => _speed;
        public bool Loop => _loop;
        public event Action<VideoTransportEvent> Changed;

        public Result SetMediaAsset(MediaAssetId? mediaAsset)
        {
            if (mediaAsset.HasValue && !mediaAsset.Value.IsUuidV4) return Failure("media.transport.asset", "Video transport requires a UUID v4 MediaAssetId.");
            if (_mediaAsset == mediaAsset) return Result.Success();
            _mediaAsset = mediaAsset;
            _playheadSeconds = 0d;
            Raise(new VideoTransportEvent(VideoTransportEventKind.MediaChanged));
            return Result.Success();
        }

        public Result SetDuration(double durationSeconds)
        {
            if (double.IsNaN(durationSeconds) || double.IsInfinity(durationSeconds) || durationSeconds < 0d)
                return Failure("media.transport.duration", "Video duration must be finite and non-negative.");
            _durationSeconds = durationSeconds;
            if (_playheadSeconds > durationSeconds) _playheadSeconds = durationSeconds;
            return Result.Success();
        }

        public Result SetPlaying(bool playing)
        {
            _playing = playing;
            Raise(new VideoTransportEvent(playing ? VideoTransportEventKind.Play : VideoTransportEventKind.Pause, _playheadSeconds));
            return Result.Success();
        }

        public Result SetSpeed(double speed)
        {
            if (double.IsNaN(speed) || double.IsInfinity(speed) || speed < 0d || speed > 4d)
                return Failure("media.transport.speed", "Video speed must be between 0 and 4.");
            _speed = speed;
            return Result.Success();
        }

        public Result SetLoop(bool loop)
        {
            _loop = loop;
            return Result.Success();
        }

        public Result Seek(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
                return Failure("media.transport.seek", "Video seek position must be finite and non-negative.");
            Raise(new VideoTransportEvent(VideoTransportEventKind.SeekStarted, Math.Min(seconds, _durationSeconds)));
            _playheadSeconds = _durationSeconds > 0d ? Math.Min(seconds, _durationSeconds) : seconds;
            Raise(new VideoTransportEvent(VideoTransportEventKind.SeekCompleted, _playheadSeconds));
            return Result.Success();
        }

        /// <summary>Updates runtime playhead projection without emitting a
        /// user-edit/seek event. This is derived GraphClock state, not a
        /// persisted parameter mutation.</summary>
        public Result SetRuntimePlayhead(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
                return Failure("media.transport.playhead", "Runtime playhead must be finite and non-negative.");
            _playheadSeconds = _durationSeconds > 0d ? Math.Min(seconds, _durationSeconds) : seconds;
            return Result.Success();
        }

        /// <summary>Updates backend-owned playback state without emitting a
        /// persisted transport edit. EOF and loop callbacks are runtime
        /// observations, not user parameter changes.</summary>
        public Result SetRuntimePlaying(bool playing)
        {
            _playing = playing;
            return Result.Success();
        }

        public Result Advance(double deltaSeconds)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
                return Failure("media.transport.advance", "Video clock delta must be finite and non-negative.");
            if (!_playing || _speed == 0d || _durationSeconds <= 0d) return Result.Success();
            var next = _playheadSeconds + deltaSeconds * _speed;
            if (next < _durationSeconds)
            {
                _playheadSeconds = next;
                return Result.Success();
            }

            if (_loop)
            {
                _playheadSeconds = next % _durationSeconds;
                Raise(new VideoTransportEvent(VideoTransportEventKind.Looped, _playheadSeconds));
            }
            else
            {
                _playheadSeconds = _durationSeconds;
                _playing = false;
                Raise(new VideoTransportEvent(VideoTransportEventKind.Ended, _playheadSeconds));
            }
            return Result.Success();
        }

        private void Raise(VideoTransportEvent value) => Changed?.Invoke(value);
        private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
    }

    /// <summary>Owns a backend handle and filters stale asynchronous callbacks
    /// by node/generation identity.  A retired node can therefore never apply
    /// a late frame or error to a newly created instance.</summary>
    public sealed class VideoPlaybackSession : IDisposable
    {
        private readonly VideoCompletionGate _gate;
        private IVideoBackendHandle _backend;
        private bool _disposed;

        public NodeInstanceId NodeId { get; }
        public ulong GenerationId { get; }
        public IVideoBackendHandle Backend => _backend;
        public VideoPlaybackStatus Status { get; private set; } = VideoPlaybackStatus.NoSource;
        public Diagnostic LastDiagnostic { get; private set; }
        public VideoPrepareRequest CurrentPrepareRequest { get; private set; }
        public event Action<VideoBackendCompletion> CompletionAccepted;
        public event Action<VideoPlaybackStatus> StatusChanged;

        public VideoPlaybackSession(NodeInstanceId nodeId, ulong generationId, IVideoBackendHandle backend, VideoCompletionGate gate = null)
        {
            if (nodeId.IsEmpty || generationId == 0) throw new ArgumentException("Video playback owner identity is required.");
            NodeId = nodeId;
            GenerationId = generationId;
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            if (_backend.NodeId != nodeId || _backend.GenerationId != generationId) throw new ArgumentException("Backend owner identity does not match the playback session.", nameof(backend));
            _gate = gate ?? new VideoCompletionGate();
            _gate.Register(nodeId, generationId);
            _backend.Completed += OnCompleted;
        }

        public Result Prepare(VideoPrepareRequest request)
        {
            if (_disposed) return Failure("media.lifecycle.disposed", "Video playback session is disposed.");
            if (request == null) return Failure("media.prepare.request", "Video prepare request is required.");
            SetStatus(VideoPlaybackStatus.Preparing);
            Result result;
            try { result = _backend.Prepare(request); }
            catch (Exception exception) { result = Result.Failure(ExceptionDiagnostic("media.prepare.failed", exception)); }
            if (result.IsFailure)
            {
                LastDiagnostic = result.Diagnostic;
                SetStatus(VideoPlaybackStatus.Faulted);
            }
            else
            {
                CurrentPrepareRequest = request;
                if (_backend.State == VideoBackendState.Ready) SetStatus(VideoPlaybackStatus.Ready);
            }
            return result;
        }

        /// <summary>Replaces a backend when a live MediaAsset probe selects a
        /// different implementation. The old callback is detached before it
        /// is disposed; its node/generation can never complete the new
        /// backend's session.</summary>
        public Result ReplaceBackend(IVideoBackendHandle replacement)
        {
            if (_disposed) return Failure("media.lifecycle.disposed", "Video playback session is disposed.");
            if (replacement == null) return Failure("media.backend.missing", "A replacement video backend is required.");
            if (replacement.NodeId != NodeId || replacement.GenerationId != GenerationId)
                return Failure("media.backend.owner", "Replacement backend owner identity does not match the playback session.");
            if (ReferenceEquals(replacement, _backend)) return Result.Success();
            var old = _backend;
            old.Completed -= OnCompleted;
            try { old.Dispose(); }
            catch (Exception exception) { LastDiagnostic = ExceptionDiagnostic("media.backend.dispose", exception); }
            _backend = replacement;
            _backend.Completed += OnCompleted;
            CurrentPrepareRequest = null;
            SetStatus(VideoPlaybackStatus.NoSource);
            return Result.Success();
        }

        public Result Play() => Invoke(_backend.Play, VideoPlaybackStatus.Playing, "media.play.failed");
        public Result Pause() => Invoke(_backend.Pause, VideoPlaybackStatus.Paused, "media.pause.failed");
        public Result Stop() => Invoke(_backend.Stop, VideoPlaybackStatus.Ready, "media.stop.failed");
        public Result SetSpeed(double speed) => InvokeTransport(() => _backend.SetSpeed(speed), "media.speed.failed");
        public Result SetLoop(bool loop) => InvokeTransport(() => _backend.SetLoop(loop), "media.loop.failed");

        public Result Seek(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d) return Failure("media.seek.invalid", "Video seek position must be finite and non-negative.");
            SetStatus(VideoPlaybackStatus.Preparing);
            Result result;
            try { result = _backend.Seek(seconds); }
            catch (Exception exception) { result = Result.Failure(ExceptionDiagnostic("media.seek.failed", exception)); }
            if (result.IsFailure)
            {
                LastDiagnostic = result.Diagnostic;
                SetStatus(VideoPlaybackStatus.Faulted);
            }
            return result;
        }

        public Result SyncToGraphClock(double logicalSeconds, bool demanded)
        {
            if (_disposed) return Failure("media.lifecycle.disposed", "Video playback session is disposed.");
            if (double.IsNaN(logicalSeconds) || double.IsInfinity(logicalSeconds) || logicalSeconds < 0d) return Failure("media.clock.invalid", "Graph clock time must be finite and non-negative.");
            try
            {
                var result = _backend.SyncToGraphClock(logicalSeconds, demanded);
                if (result.IsFailure) LastDiagnostic = result.Diagnostic;
                return result;
            }
            catch (Exception exception)
            {
                LastDiagnostic = ExceptionDiagnostic("media.clock.failed", exception);
                return Result.Failure(LastDiagnostic);
            }
        }

        private Result Invoke(Func<Result> operation, VideoPlaybackStatus successState, string code)
        {
            if (_disposed) return Failure("media.lifecycle.disposed", "Video playback session is disposed.");
            Result result;
            try { result = operation(); }
            catch (Exception exception) { result = Result.Failure(ExceptionDiagnostic(code, exception)); }
            if (result.IsFailure)
            {
                LastDiagnostic = result.Diagnostic;
                SetStatus(VideoPlaybackStatus.Faulted);
            }
            else SetStatus(successState);
            // Preserve the backend's precise diagnostic (file missing,
            // platform unsupported, seek failure, ...); wrapping it in a
            // generic operation code would discard the contract details.
            return result.IsFailure ? Result.Failure(result.Diagnostic) : result;
        }

        private Result InvokeTransport(Func<Result> operation, string code)
        {
            if (_disposed) return Failure("media.lifecycle.disposed", "Video playback session is disposed.");
            Result result;
            try { result = operation(); }
            catch (Exception exception) { result = Result.Failure(ExceptionDiagnostic(code, exception)); }
            if (result.IsFailure) LastDiagnostic = result.Diagnostic;
            return result;
        }

        private void OnCompleted(VideoBackendCompletion completion)
        {
            if (!_gate.TryApply(completion, accepted =>
            {
                switch (accepted.Kind)
                {
                    case VideoCompletionKind.Prepared: LastDiagnostic = null; SetStatus(VideoPlaybackStatus.Ready); break;
                    case VideoCompletionKind.SeekStarted: SetStatus(VideoPlaybackStatus.Preparing); break;
                    case VideoCompletionKind.SeekCompleted: SetStatus(VideoPlaybackStatus.Ready); break;
                    case VideoCompletionKind.Ended: SetStatus(VideoPlaybackStatus.Ended); break;
                    case VideoCompletionKind.Looped: SetStatus(VideoPlaybackStatus.Playing); break;
                    case VideoCompletionKind.FrameReady:
                        if (Status == VideoPlaybackStatus.Preparing) SetStatus(VideoPlaybackStatus.Ready);
                        break;
                    case VideoCompletionKind.Error:
                        LastDiagnostic = new Diagnostic(new DiagnosticCode("media.decode_failed"), Severity.Error, accepted.ErrorMessage, module: "media");
                        SetStatus(VideoPlaybackStatus.Faulted);
                        break;
                    case VideoCompletionKind.Disposed: SetStatus(VideoPlaybackStatus.Disposed); break;
                }
                CompletionAccepted?.Invoke(accepted);
            })) return;
        }

        private void SetStatus(VideoPlaybackStatus status)
        {
            if (Status == status) return;
            Status = status;
            StatusChanged?.Invoke(status);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _gate.Unregister(NodeId, GenerationId);
            _backend.Completed -= OnCompleted;
            _backend.Dispose();
            SetStatus(VideoPlaybackStatus.Disposed);
        }

        private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message));
        private static Diagnostic ExceptionDiagnostic(string code, Exception exception) => new Diagnostic(new DiagnosticCode(code), Severity.Error, exception.Message, exception: DiagnosticExceptionInfo.FromException(exception), module: "media");
    }

    public static class VideoPlayerContract
    {
        public const string NodeTypeId = "shitdesigner.video.player";
        public const string ImagePortId = "image";
        public const string MediaAssetParameterId = "transport.media_asset";
        public const string PlayingParameterId = "transport.playing";
        public const string PlayheadParameterId = "transport.playhead_seconds";
        public const string SpeedParameterId = "transport.speed";
        public const string LoopParameterId = "transport.loop";
        public const int SchemaVersion = 1;
    }

    /// <summary>Converts a backend-owned decoded texture into the shared Runtime
    /// image boundary. Rendering owns the actual texture and lease.</summary>
    public interface IVideoFrameAdapter
    {
        Result<IRuntimeImageFrame> Create(object borrowedTexture, int width, int height, ulong frameNumber, ulong leaseId);
    }

    /// <summary>Optional richer adapter used by a Rendering integration. It
    /// receives the Phase-5 prepared destination surface so the conversion
    /// can copy into that lease without acquiring a second surface.</summary>
    public interface IVideoOutputSurfaceFrameAdapter : IVideoFrameAdapter
    {
        Result<IRuntimeImageFrame> Create(object borrowedTexture, IRuntimeOutputSurface preparedSurface, ulong frameNumber);
    }

    public interface IVideoOutputSurfaceFrameAdapterWithConversion : IVideoOutputSurfaceFrameAdapter
    {
        Result<IRuntimeImageFrame> Create(object borrowedTexture, IRuntimeOutputSurface preparedSurface, ulong frameNumber, VideoFrameConversionMetadata metadata);
    }

    /// <summary>Resolves a persisted MediaAssetReference into the verified
    /// runtime file and authoritative codec probe. The resolver is injected so
    /// Media never owns Project catalog or filesystem policy.</summary>
    public interface IVideoPrepareResolver
    {
        Result<VideoPrepareRequest> Resolve(MediaAssetId mediaAssetId);
    }

    /// <summary>Stateful transport bridge. Parameter values are sampled from
    /// an immutable FrameSnapshot, while anchor/playhead state stays runtime
    /// only and never creates a per-frame Project dirty event.</summary>
    public sealed class VideoTransportController
    {
        private readonly VideoPlaybackSession _session;
        private readonly VideoTransportState _transport;
        private readonly IVideoPrepareResolver _resolver;
        private bool _initialized;
        private bool _demanded;
        private bool _lastPlaying;
        private bool _pendingPlay;
        private bool _pendingSeek;
        private bool _lastLoop;
        private bool _eofLatched;
        private double _lastSpeed = 1d;
        private double _lastPlayhead;
        private MediaAssetId? _lastAsset;
        private double _anchorClock;
        private double _anchorPlayhead;
        private ulong _demandSyncFrame;
        private bool _demandSyncApplied;
        private Diagnostic _lastDiagnostic;
        private const double InitialSeekEpsilon = 0.000001d;

        public VideoTransportState Transport => _transport;
        public bool Demanded => _demanded;
        /// <summary>True after a non-looping backend EOF. A persisted
        /// Playing=true value stays stopped until an explicit false->true
        /// transport transition clears this latch.</summary>
        public bool IsEofLatched => _eofLatched;
        public Diagnostic LastDiagnostic => _lastDiagnostic;

        public VideoTransportController(VideoPlaybackSession session, VideoTransportState transport, IVideoPrepareResolver resolver = null, IVideoBackendFactory backendFactory = null, IVideoGraphicsCapabilities graphics = null)
        {
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _resolver = resolver;
            _backendFactory = backendFactory;
            _graphics = graphics;
            _session.CompletionAccepted += OnCompletionAccepted;
        }

        private readonly IVideoBackendFactory _backendFactory;
        private readonly IVideoGraphicsCapabilities _graphics;

        public double LogicalPosition(double graphClockTime)
        {
            if (!_transport.Playing) return _transport.PlayheadSeconds;
            var delta = graphClockTime - _anchorClock;
            if (delta < 0d) delta = 0d;
            var projected = Math.Max(0d, _anchorPlayhead + delta * _transport.Speed);
            if (_transport.DurationSeconds <= 0d) return projected;
            return _transport.Loop ? projected % _transport.DurationSeconds : Math.Min(projected, _transport.DurationSeconds);
        }

        public Result Apply(FrameSnapshot snapshot, bool demanded)
        {
            if (snapshot == null) return Failure("media.transport.snapshot", "Video transport requires a FrameSnapshot.");
            var result = ApplyParameters(snapshot);
            if (result.IsFailure) return result;
            var logical = LogicalPosition(snapshot.GraphClockTime);
            _transport.SetRuntimePlayhead(logical);
            if (demanded != _demanded)
            {
                result = SetDemanded(demanded, logical);
                if (result.IsFailure) return result;
            }
            result = TryResume(logical);
            if (result.IsFailure) return result;
            if (_demandSyncApplied && _demandSyncFrame == snapshot.FrameNumber)
            {
                _demandSyncApplied = false;
                return Result.Success();
            }
            _demandSyncApplied = false;
            return Remember(_session.SyncToGraphClock(logical, demanded));
        }

        public Result OnDemandChanged(bool demanded, FrameEvaluationContext context)
        {
            if (context == null) return Failure("media.transport.context", "Video demand transition requires a frame context.");
            var logical = LogicalPosition(context.Snapshot.GraphClockTime);
            return OnDemandChanged(demanded, context.Snapshot, logical);
        }

        private Result OnDemandChanged(bool demanded, FrameSnapshot snapshot, double logical)
        {
            var result = SetDemanded(demanded, logical);
            if (!demanded && _session.Status == VideoPlaybackStatus.NoSource) return Result.Success();
            if (result.IsFailure) return Remember(result);
            result = _session.SyncToGraphClock(logical, demanded);
            if (result.IsFailure) return Remember(result);
            result = TryResume(logical);
            if (result.IsFailure) return Remember(result);
            _demandSyncFrame = snapshot.FrameNumber;
            _demandSyncApplied = true;
            return Result.Success();
        }

        private Result SetDemanded(bool demanded, double logical)
        {
            _demanded = demanded;
            if (!demanded) return Result.Success();
            // Demand can be published before this fresh controller has sampled
            // persisted transport values.  Prepare starts media at zero, so a
            // restored zero playhead must not manufacture a Seek(0) and wait
            // for a completion the backend is not required to publish.
            _pendingSeek = _initialized;
            _pendingPlay = _transport.Playing && !_eofLatched;
            return Result.Success();
        }

        private Result ApplyParameters(FrameSnapshot snapshot)
        {
            var asset = ReadAsset(snapshot, VideoPlayerContract.MediaAssetParameterId, _transport.MediaAsset);
            var requestedPlaying = ReadBool(snapshot, VideoPlayerContract.PlayingParameterId, _transport.Playing);
            if (!requestedPlaying)
            {
                if (_eofLatched) _pendingSeek = _demanded;
                _eofLatched = false;
            }
            var playing = _eofLatched && requestedPlaying ? false : requestedPlaying;
            var speed = ReadFloat(snapshot, VideoPlayerContract.SpeedParameterId, _transport.Speed);
            var loop = ReadBool(snapshot, VideoPlayerContract.LoopParameterId, _transport.Loop);
            var playhead = ReadFloat(snapshot, VideoPlayerContract.PlayheadParameterId, _transport.PlayheadSeconds);
            var clock = snapshot.GraphClockTime;
            var wasInitialized = _initialized;
            var priorPlayhead = _lastPlayhead;
            var assetChanged = !wasInitialized || asset != _lastAsset;
            var priorLogical = _initialized ? LogicalPosition(clock) : _transport.PlayheadSeconds;

            if (assetChanged)
            {
                var changed = _transport.SetMediaAsset(asset);
                if (changed.IsFailure) return Remember(changed);
                _lastAsset = asset;
                _pendingSeek = false;
                // A backend Prepare stops the prior asset. Preserve the
                // requested transport state so an already-playing node
                // resumes after its replacement asset becomes ready.
                _pendingPlay = playing && !_eofLatched;
                _eofLatched = false;
                _lastPlayhead = 0d;
                _anchorClock = clock;
                _anchorPlayhead = 0d;
                if (asset.HasValue && _resolver != null)
                {
                    var request = _resolver.Resolve(asset.Value);
                    if (request.IsFailure) return Remember(Result.Failure(request.Diagnostic));
                    var selected = VideoBackendSelector.Select(request.Value.Probe, _graphics);
                    if (selected.IsFailure) return Remember(Result.Failure(selected.Diagnostic));
                    if (_session.Backend.BackendKind != selected.Value)
                    {
                        if (_backendFactory == null) return Remember(Failure("media.backend.factory_missing", "A live backend switch requires an injected backend factory."));
                        var replacement = _backendFactory.Create(_session.NodeId, _session.GenerationId, selected.Value);
                        if (replacement.IsFailure) return Remember(Result.Failure(replacement.Diagnostic));
                        var switched = _session.ReplaceBackend(replacement.Value);
                        if (switched.IsFailure)
                        {
                            replacement.Value.Dispose();
                            return Remember(switched);
                        }
                    }
                    var duration = _transport.SetDuration(request.Value.Probe.DurationSeconds);
                    if (duration.IsFailure) return Remember(duration);
                    var prepared = _session.Prepare(request.Value);
                    if (prepared.IsFailure) return Remember(prepared);
                }
            }

            if (!_initialized || Math.Abs(speed - _lastSpeed) > 0.000001d)
            {
                var setSpeed = _transport.SetSpeed(speed);
                if (setSpeed.IsFailure) return Remember(setSpeed);
                var backendSpeed = _session.SetSpeed(speed);
                if (backendSpeed.IsFailure && _session.Status != VideoPlaybackStatus.NoSource && _session.Status != VideoPlaybackStatus.Preparing) return Remember(backendSpeed);
                _lastSpeed = speed;
                _anchorClock = clock;
                _anchorPlayhead = priorLogical;
            }
            if (!_initialized || loop != _lastLoop)
            {
                var setLoop = _transport.SetLoop(loop);
                if (setLoop.IsFailure) return Remember(setLoop);
                var backendLoop = _session.SetLoop(loop);
                if (backendLoop.IsFailure && _session.Status != VideoPlaybackStatus.NoSource && _session.Status != VideoPlaybackStatus.Preparing) return Remember(backendLoop);
                _lastLoop = loop;
            }
            // Prepare opens an uninitialized asset at zero. Do not turn that
            // persisted default into a transport seek: some backends correctly
            // publish Prepare but have no separate Seek(0) completion. Once
            // initialized, every actual playhead edit—including 2 -> 0 on an
            // asset replacement—remains a real seek.
            if ((wasInitialized && Math.Abs(playhead - priorPlayhead) > InitialSeekEpsilon)
                || (assetChanged && Math.Abs(playhead) > InitialSeekEpsilon))
            {
                var seek = _transport.Seek(playhead);
                if (seek.IsFailure) return Remember(seek);
                _lastPlayhead = playhead;
                _anchorClock = clock;
                _anchorPlayhead = _transport.PlayheadSeconds;
                if (_demanded && _session.Status != VideoPlaybackStatus.NoSource && _session.Status != VideoPlaybackStatus.Preparing)
                {
                    var backendSeek = _session.Seek(_transport.PlayheadSeconds);
                    if (backendSeek.IsFailure) return Remember(backendSeek);
                }
                // This branch is a real explicit seek (including an edit
                // back to zero after initialization).  If Prepare is still
                // pending, preserve it so TryResume performs exactly that
                // seek once the backend becomes ready.  Fresh persisted zero
                // never reaches this branch.
                else _pendingSeek = _demanded;
            }
            if (!_initialized || playing != _lastPlaying)
            {
                var setPlaying = _transport.SetPlaying(playing);
                if (setPlaying.IsFailure) return Remember(setPlaying);
                _lastPlaying = playing;
                _anchorClock = clock;
                _anchorPlayhead = playing ? _transport.PlayheadSeconds : _transport.PlayheadSeconds;
                _pendingPlay = playing;
                if (_session.Status != VideoPlaybackStatus.NoSource && _session.Status != VideoPlaybackStatus.Preparing)
                {
                    var backendPlaying = playing ? _session.Play() : _session.Pause();
                    if (backendPlaying.IsFailure) return Remember(backendPlaying);
                    _pendingPlay = false;
                }
            }
            _initialized = true;
            return Result.Success();
        }

        private Result TryResume(double logical)
        {
            if (!_demanded) return Result.Success();
            var ready = _session.Status == VideoPlaybackStatus.Ready || _session.Status == VideoPlaybackStatus.Playing || _session.Status == VideoPlaybackStatus.Paused || _session.Status == VideoPlaybackStatus.Ended;
            if (!ready) return Result.Success();
            if (_pendingSeek)
            {
                var seek = _session.Seek(logical);
                if (seek.IsFailure) return Remember(seek);
                _pendingSeek = false;
            }
            if (_pendingPlay && _session.Status != VideoPlaybackStatus.Playing)
            {
                var play = _session.Play();
                if (play.IsFailure) return Remember(play);
                _pendingPlay = false;
            }
            return Result.Success();
        }

        private Result Remember(Result result)
        {
            if (result.IsFailure) _lastDiagnostic = result.Diagnostic;
            return result;
        }

        private Result<T> Remember<T>(Result<T> result)
        {
            if (result.IsFailure) _lastDiagnostic = result.Diagnostic;
            return result;
        }

        internal void RememberDiagnostic(Diagnostic diagnostic)
        {
            if (diagnostic != null) _lastDiagnostic = diagnostic;
        }

        private void OnCompletionAccepted(VideoBackendCompletion completion)
        {
            if (completion == null) return;
            if (completion.Kind == VideoCompletionKind.Ended && !_transport.Loop)
            {
                _transport.SetRuntimePlayhead(completion.TimeSeconds);
                _transport.SetRuntimePlaying(false);
                _eofLatched = true;
                _lastPlaying = false;
                _pendingPlay = false;
                _anchorPlayhead = _transport.PlayheadSeconds;
            }
            else if (completion.Kind == VideoCompletionKind.Looped)
            {
                _transport.SetRuntimePlayhead(completion.TimeSeconds);
                _transport.SetRuntimePlaying(true);
            }
        }

        public Result Apply(FrameSnapshot snapshot, NodeInstanceId nodeId, bool demanded)
        {
            _nodeId = nodeId;
            return Apply(snapshot, demanded);
        }

        private NodeInstanceId _nodeId;
        private MediaAssetId? ReadAsset(FrameSnapshot snapshot, string parameter, MediaAssetId? fallback)
        {
            return snapshot.EffectiveValues.TryGetValue(new ParameterKey(_nodeId, new ParameterId(parameter)), out var value) && value.Type == ParameterType.MediaAssetReference ? value.AsMediaAsset() : fallback;
        }
        private bool ReadBool(FrameSnapshot snapshot, string parameter, bool fallback) => snapshot.EffectiveValues.TryGetValue(new ParameterKey(_nodeId, new ParameterId(parameter)), out var value) && value.Type == ParameterType.Bool ? value.AsBool() : fallback;
        private double ReadFloat(FrameSnapshot snapshot, string parameter, double fallback) => snapshot.EffectiveValues.TryGetValue(new ParameterKey(_nodeId, new ParameterId(parameter)), out var value) && value.Type == ParameterType.Float ? value.AsFloat() : fallback;

        private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media"));
    }

    /// <summary>Runtime node integration without depending on a concrete Unity
    /// VideoPlayer or native Hap object.  The bootstrap supplies backend,
    /// texture and asset-path adapters.</summary>
    public sealed class VideoPlayerRuntimeNode : IRuntimeNode, IRuntimeDemandAwareNode, IRuntimePerformanceHealthNode
    {
        private readonly VideoPlaybackSession _session;
        private readonly VideoTransportState _transport;
        private readonly VideoTransportController _transportController;
        private readonly IVideoFrameAdapter _frameAdapter;
        private readonly int _width;
        private readonly int _height;
        private IRuntimeImageFrame _lastFrame;
        private int _pendingPreparingPublications;
        private bool _disposed;

        public NodeInstanceId NodeId { get; }
        public NodeTypeId TypeId { get; }
        public ulong GenerationId { get; }
        public RuntimeNodeState State { get; private set; } = RuntimeNodeState.Preparing;
        public VideoBackendKind BackendKind => _session?.Backend?.BackendKind ?? VideoBackendKind.UnityVideoBackend;
        public VideoBackendState BackendState => _session?.Backend?.State ?? VideoBackendState.Disposed;
        public bool NativeContextActive => BackendKind == VideoBackendKind.HapVideoBackend && BackendState != VideoBackendState.Disposed && BackendState != VideoBackendState.Unsupported;
        public bool HasActiveBackend => BackendState != VideoBackendState.Disposed;
        public bool HasNativeContext => NativeContextActive;

        public VideoPlayerRuntimeNode(NodeInstanceId nodeId, ulong generationId, VideoPlaybackSession session, VideoTransportState transport, IVideoFrameAdapter frameAdapter, int width = 1920, int height = 1080, IVideoPrepareResolver prepareResolver = null, IVideoBackendFactory backendFactory = null, IVideoGraphicsCapabilities graphics = null)
        {
            if (nodeId.IsEmpty || generationId == 0) throw new ArgumentException("Video node identity is required.");
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            NodeId = nodeId;
            TypeId = new NodeTypeId(VideoPlayerContract.NodeTypeId);
            GenerationId = generationId;
            _session = session ?? throw new ArgumentNullException(nameof(session));
            _transport = transport ?? throw new ArgumentNullException(nameof(transport));
            _transportController = new VideoTransportController(_session, _transport, prepareResolver, backendFactory, graphics);
            _frameAdapter = frameAdapter ?? throw new ArgumentNullException(nameof(frameAdapter));
            _width = width;
            _height = height;
            _session.StatusChanged += OnStatusChanged;
        }

        public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs)
        {
            if (_disposed) { outputs.SetFaulted(new PortId(VideoPlayerContract.ImagePortId), Failure("media.node.disposed", "Video node is disposed.")); return; }
            if (context == null || outputs == null) throw new ArgumentNullException(nameof(context));
            var imagePort = new PortId(VideoPlayerContract.ImagePortId);
            if (!context.RequestedOutputs.Contains(imagePort)) return;
            var transport = _transportController.Apply(context.Snapshot, NodeId, true);
            if (transport.IsFailure)
            {
                State = RuntimeNodeState.Faulted;
                WriteLastOrPreparing(context, outputs, imagePort, transport.Diagnostic);
                return;
            }
            if (!_transport.HasMediaAsset)
            {
                State = RuntimeNodeState.Faulted;
                outputs.SetFaulted(imagePort, Failure("media.node.asset_missing", "VideoPlayer has no selected MediaAsset."));
                return;
            }
            if (_session.Status == VideoPlaybackStatus.Faulted)
            {
                State = RuntimeNodeState.Faulted;
                WriteLastOrPreparing(context, outputs, imagePort, _session.LastDiagnostic ?? Failure("media.node.decode", "Video backend failed to provide a frame."));
                return;
            }
            if (PublishLatchedPreparing(context, outputs, imagePort)) return;
            if (_session.Status == VideoPlaybackStatus.Preparing || _session.Status == VideoPlaybackStatus.NoSource)
            {
                State = RuntimeNodeState.Preparing;
                WriteLastOrPreparing(context, outputs, imagePort, Failure("media.node.preparing", "Video frame is preparing."));
                return;
            }

            if (context.OutputSurfaces == null)
            {
                State = RuntimeNodeState.Faulted;
                WriteLastOrPreparing(context, outputs, imagePort, Failure("media.node.surface_missing", "Video output requires a prepared Runtime phase-5 surface."));
                return;
            }
            if (!RuntimeOutputResolutionDemandAccess.TryGet(context, NodeId, imagePort, out var demand))
            {
                State = RuntimeNodeState.Preparing;
                WriteLastOrPreparing(context, outputs, imagePort, Failure("media.node.demand_missing", "Video output has no propagated Phase-4 resolution demand."));
                return;
            }
            var outputWidth = demand.Width;
            var outputHeight = demand.Height;
            var surface = context.OutputSurfaces.TryGetPrepared(NodeId, imagePort, outputWidth, outputHeight, context.Snapshot.FrameNumber);
            if (surface.IsFailure || surface.Value == null || surface.Value.LeaseId == 0)
            {
                State = RuntimeNodeState.Faulted;
                var diagnostic = surface.IsFailure ? surface.Diagnostic : Failure("media.node.surface_invalid", "Runtime returned an invalid prepared output surface.");
                WriteLastOrPreparing(context, outputs, imagePort, diagnostic);
                return;
            }
            var prepared = surface.Value;
            if (prepared.Width != outputWidth || prepared.Height != outputHeight || prepared.NodeId != NodeId || prepared.PortId != imagePort)
            {
                State = RuntimeNodeState.Faulted;
                WriteLastOrPreparing(context, outputs, imagePort, Failure("media.node.surface_descriptor", "Prepared output surface descriptor does not match the requested output."));
                return;
            }
            var borrowedTexture = _session.Backend.BorrowedTexture;
            if (borrowedTexture == null)
            {
                State = RuntimeNodeState.Faulted;
                WriteLastOrPreparing(context, outputs, imagePort, Failure("media.node.decode", "Video backend has no decoded texture for the prepared output surface."));
                return;
            }
            var converted = _frameAdapter is IVideoOutputSurfaceFrameAdapter outputSurfaceAdapter
                ? (_frameAdapter is IVideoOutputSurfaceFrameAdapterWithConversion conversionAdapter
                    ? conversionAdapter.Create(borrowedTexture, prepared, context.Snapshot.FrameNumber, _session.CurrentPrepareRequest?.ConversionMetadata)
                    : outputSurfaceAdapter.Create(borrowedTexture, prepared, context.Snapshot.FrameNumber))
                : _frameAdapter.Create(borrowedTexture, prepared.Width, prepared.Height, context.Snapshot.FrameNumber, prepared.LeaseId);
            if (converted.IsFailure)
            {
                State = RuntimeNodeState.Faulted;
                WriteLastOrPreparing(context, outputs, imagePort, converted.Diagnostic);
                return;
            }
            _lastFrame = converted.Value;
            State = RuntimeNodeState.Ready;
            outputs.SetAvailable(imagePort, PortValue.FromImageFrame(converted.Value));
        }

        public void OnDemandChanged(bool demanded, FrameEvaluationContext context)
        {
            if (_disposed || context == null) return;
            var result = _transportController.OnDemandChanged(demanded, context);
            if (result.IsFailure) _transportController.RememberDiagnostic(result.Diagnostic);
        }

        private void WriteLastOrPreparing(NodeExecutionContext context, NodeOutputWriter outputs, PortId imagePort, Diagnostic diagnostic)
        {
            if (_lastFrame != null)
            {
                outputs.SetAvailable(imagePort, PortValue.FromImageFrame(_lastFrame));
                context.Diagnostics.Report(diagnostic);
            }
            else outputs.SetPreparing(imagePort, diagnostic);
        }

        private bool PublishLatchedPreparing(NodeExecutionContext context, NodeOutputWriter outputs, PortId imagePort)
        {
            if (_pendingPreparingPublications <= 0) return false;
            _pendingPreparingPublications--;
            State = RuntimeNodeState.Preparing;
            WriteLastOrPreparing(context, outputs, imagePort, Failure("media.node.preparing", "Video frame is preparing."));
            return true;
        }

        private void OnStatusChanged(VideoPlaybackStatus status)
        {
            if (status == VideoPlaybackStatus.Faulted)
            {
                _pendingPreparingPublications = 0;
                State = RuntimeNodeState.Faulted;
            }
            else if (status == VideoPlaybackStatus.Disposed)
            {
                _pendingPreparingPublications = 0;
                State = RuntimeNodeState.Disposed;
            }
            else if (status == VideoPlaybackStatus.Preparing)
            {
                if (_pendingPreparingPublications < int.MaxValue) _pendingPreparingPublications++;
                State = RuntimeNodeState.Preparing;
            }
            else if ((status == VideoPlaybackStatus.Ready || status == VideoPlaybackStatus.Playing || status == VideoPlaybackStatus.Paused) && _pendingPreparingPublications == 0)
            {
                State = RuntimeNodeState.Ready;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.StatusChanged -= OnStatusChanged;
            _session.Dispose();
            State = RuntimeNodeState.Disposed;
        }

        private Diagnostic Failure(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: NodeId, nodeTypeId: TypeId, generationId: GenerationId);
    }

    public sealed class VideoCompletionGate
    {
        private readonly HashSet<string> _active = new HashSet<string>(StringComparer.Ordinal);
        public void Register(NodeInstanceId nodeId, ulong generationId)
        {
            if (nodeId.IsEmpty || generationId == 0) throw new ArgumentException("A completion owner identity is required.");
            _active.Add(Key(nodeId, generationId));
        }
        public void Unregister(NodeInstanceId nodeId, ulong generationId) => _active.Remove(Key(nodeId, generationId));
        public bool IsActive(NodeInstanceId nodeId, ulong generationId) => _active.Contains(Key(nodeId, generationId));
        public bool TryApply(VideoBackendCompletion completion, Action<VideoBackendCompletion> apply)
        {
            if (completion == null || !IsActive(completion.NodeId, completion.GenerationId)) return false;
            apply?.Invoke(completion);
            return true;
        }
        private static string Key(NodeInstanceId nodeId, ulong generationId) => nodeId.Value + ":" + generationId.ToString();
    }

    public static class VideoDiagnostics
    {
        public static Diagnostic FileMissing(MediaAssetId? assetId, string relativePath, NodeInstanceId? nodeId = null, double requestedTime = 0d)
            => Create("media.file_missing", "The project media file is missing.", assetId, relativePath, nodeId, requestedTime);

        public static Diagnostic ProbeFailed(MediaAssetId? assetId, string relativePath, string message, NodeInstanceId? nodeId = null, double requestedTime = 0d)
            => Create("media.probe_failed", message, assetId, relativePath, nodeId, requestedTime);

        public static Diagnostic PrepareFailed(MediaAssetId? assetId, string relativePath, VideoProbeResult probe, string message, NodeInstanceId? nodeId = null, double requestedTime = 0d)
            => Create("media.prepare_failed", message, assetId, relativePath, nodeId, requestedTime, probe);

        public static Diagnostic SeekFailed(MediaAssetId? assetId, string relativePath, VideoProbeResult probe, string message, NodeInstanceId? nodeId = null, double requestedTime = 0d)
            => Create("media.seek_failed", message, assetId, relativePath, nodeId, requestedTime, probe);

        public static Diagnostic DecodeFailed(MediaAssetId? assetId, string relativePath, VideoProbeResult probe, string message, NodeInstanceId? nodeId = null, double requestedTime = 0d)
            => Create("media.decode_failed", message, assetId, relativePath, nodeId, requestedTime, probe);

        private static Diagnostic Create(string code, string message, MediaAssetId? assetId, string relativePath, NodeInstanceId? nodeId, double requestedTime, VideoProbeResult probe = null)
        {
            var fields = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("relativePath", relativePath ?? string.Empty),
                new KeyValuePair<string, string>("requestedTime", requestedTime.ToString(System.Globalization.CultureInfo.InvariantCulture))
            };
            if (assetId.HasValue) fields.Add(new KeyValuePair<string, string>("mediaAssetId", assetId.Value.Value));
            if (probe != null)
            {
                fields.Add(new KeyValuePair<string, string>("container", probe.Container.ToString()));
                fields.Add(new KeyValuePair<string, string>("codec", probe.Codec.ToString()));
            }
            return new Diagnostic(new DiagnosticCode(code), Severity.Error, message ?? string.Empty, nodeId: nodeId, detail: new DiagnosticDetail(fields), module: "media");
        }
    }
}
