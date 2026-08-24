using System;
using System.IO;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.Video;

namespace ShitDesigner.Media {
	/// <summary>
	/// The Unity implementation is deliberately APIOnly: Rendering owns the
	/// output surface while this class owns only the VideoPlayer host and its
	/// decoded texture borrow. Audio is disabled because the graph has no
	/// implicit audio route.
	/// </summary>
	public sealed class UnityVideoBackend : VideoBackendHandleBase {
		private readonly GameObject _host;
		private readonly bool _ownsHost;
		private readonly VideoPlayer _player;
		private readonly bool _ownsPlayer;
		private bool _prepared;
		private bool _disposing;

		public UnityVideoBackend(NodeInstanceId nodeId, ulong generationId, GameObject host = null)
			: base(nodeId, generationId, VideoBackendKind.UnityVideoBackend) {
			// Passing a host transfers its lifetime to the backend. This
			// keeps ownership unambiguous: Dispose always detaches callbacks
			// and destroys the VideoPlayer host it created or was given.
			_ownsHost = true;
			_host = host ?? new GameObject("ShitDesigner.VideoBackend." + nodeId.Value);
			_player = _host.GetComponent<VideoPlayer>();
			if (_player == null) {
				_player = _host.AddComponent<VideoPlayer>();
				_ownsPlayer = true;
			}

			_player.playOnAwake = false;
			_player.waitForFirstFrame = true;
			_player.renderMode = VideoRenderMode.APIOnly;
			_player.audioOutputMode = VideoAudioOutputMode.None;
			_player.sendFrameReadyEvents = true;
			_player.prepareCompleted += OnPrepareCompleted;
			_player.seekCompleted += OnSeekCompleted;
			_player.frameReady += OnFrameReady;
			_player.loopPointReached += OnLoopPointReached;
			_player.errorReceived += OnErrorReceived;
		}

		public GameObject Host => _host;
		public VideoPlayer Player => _player;
		public override object BorrowedTexture => _player == null ? null : _player.texture;

		public override Result Prepare(VideoPrepareRequest request) {
			if (_disposing) return Failure("media.lifecycle.disposed", "Unity VideoPlayer backend is disposed.");
			if (request == null) return Failure("media.prepare.request", "Video prepare request is required.");
			if (!request.Probe.Supported) return Failure("media.prepare.unsupported", request.Probe.DiagnosticMessage);
			if ((request.Probe.Container != VideoContainer.Mp4 || request.Probe.Codec != VideoCodec.H264)
				&& (request.Probe.Container != VideoContainer.WebM || request.Probe.Codec != VideoCodec.VP8))
				return Failure("media.codec.unsupported", "Unity VideoPlayer backend accepts only H.264 MP4 or VP8 WebM.");
			if (string.IsNullOrWhiteSpace(request.Url) || !Path.IsPathRooted(request.Url))
				return Failure("media.prepare.path", "Unity VideoPlayer requires a verified absolute local file path.");
			if (!File.Exists(request.Url)) return Failure("media.file_missing", "The project media file is missing.");

			try {
				_player.Stop();
				_player.source = UnityEngine.Video.VideoSource.Url;
				_player.url = request.Url;
				_prepared = false;
				State = VideoBackendState.Preparing;
				Emit(VideoCompletionKind.PrepareStarted);
				_player.Prepare();
				return Result.Success();
			}
			catch (Exception exception) {
				State = VideoBackendState.Faulted;
				return Failure("media.prepare_failed", exception.Message, exception);
			}
		}

		public override Result Play() {
			if (_disposing) return Failure("media.lifecycle.disposed", "Unity VideoPlayer backend is disposed.");
			if (!_prepared) return Failure("media.play.not_ready", "VideoPlayer must finish preparing before playback.");
			try {
				_player.Play();
				State = VideoBackendState.Playing;
				return Result.Success();
			}
			catch (Exception exception) {
				State = VideoBackendState.Faulted;
				return Failure("media.play.failed", exception.Message, exception);
			}
		}

		public override Result Pause() {
			if (_disposing) return Failure("media.lifecycle.disposed", "Unity VideoPlayer backend is disposed.");
			try {
				_player.Pause();
				State = VideoBackendState.Paused;
				return Result.Success();
			}
			catch (Exception exception) {
				State = VideoBackendState.Faulted;
				return Failure("media.pause.failed", exception.Message, exception);
			}
		}

		public override Result Stop() {
			if (_disposing) return Failure("media.lifecycle.disposed", "Unity VideoPlayer backend is disposed.");
			try {
				_player.Stop();
				State = _prepared ? VideoBackendState.Ready : VideoBackendState.Created;
				return Result.Success();
			}
			catch (Exception exception) {
				State = VideoBackendState.Faulted;
				return Failure("media.stop.failed", exception.Message, exception);
			}
		}

		public override Result SetSpeed(double speed) {
			if (_disposing) return Failure("media.lifecycle.disposed", "Unity VideoPlayer backend is disposed.");
			if (double.IsNaN(speed) || double.IsInfinity(speed) || speed < 0d || speed > 4d)
				return Failure("media.transport.speed", "Video speed must be between 0 and 4.");
			try { _player.playbackSpeed = (float)speed; return Result.Success(); }
			catch (Exception exception) { return Failure("media.speed.failed", exception.Message, exception); }
		}

		public override Result SetLoop(bool loop) {
			if (_disposing) return Failure("media.lifecycle.disposed", "Unity VideoPlayer backend is disposed.");
			try { _player.isLooping = loop; return Result.Success(); }
			catch (Exception exception) { return Failure("media.loop.failed", exception.Message, exception); }
		}

		public override Result Seek(double seconds) {
			if (_disposing) return Failure("media.lifecycle.disposed", "Unity VideoPlayer backend is disposed.");
			if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d)
				return Failure("media.seek.invalid", "Video seek position must be finite and non-negative.");
			if (!_prepared) return Failure("media.seek.not_ready", "VideoPlayer must finish preparing before seeking.");
			try {
				State = VideoBackendState.Preparing;
				Emit(VideoCompletionKind.SeekStarted, seconds);
				_player.time = seconds;
				return Result.Success();
			}
			catch (Exception exception) {
				State = VideoBackendState.Faulted;
				return Failure("media.seek_failed", exception.Message, exception);
			}
		}

		public override Result SyncToGraphClock(double logicalSeconds, bool demanded) {
			if (_disposing) return Failure("media.lifecycle.disposed", "Unity VideoPlayer backend is disposed.");
			if (double.IsNaN(logicalSeconds) || double.IsInfinity(logicalSeconds) || logicalSeconds < 0d)
				return Failure("media.clock.invalid", "Graph clock time must be finite and non-negative.");
			try {
				_player.sendFrameReadyEvents = demanded;
				if (demanded) {
					_player.timeReference = VideoTimeReference.ExternalTime;
					_player.externalReferenceTime = logicalSeconds;
				}
				else if (_player.isPlaying) {
					// A node outside the demanded output set must stop frame
					// transfer. The transport remains the source of logical
					// time and can resume on the next demanded evaluation.
					_player.Pause();
					State = VideoBackendState.Paused;
				}
				return Result.Success();
			}
			catch (Exception exception) {
				State = VideoBackendState.Faulted;
				return Failure("media.clock.failed", exception.Message, exception);
			}
		}

		protected override void BeforeDispose() {
			_disposing = true;
			if (_player == null) return;
			_player.prepareCompleted -= OnPrepareCompleted;
			_player.seekCompleted -= OnSeekCompleted;
			_player.frameReady -= OnFrameReady;
			_player.loopPointReached -= OnLoopPointReached;
			_player.errorReceived -= OnErrorReceived;
			try { _player.Stop(); } catch { /* native teardown must remain idempotent */ }
		}

		protected override void DisposeCore() {
			if (_player != null && _ownsPlayer) {
				if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_player);
				else UnityEngine.Object.DestroyImmediate(_player);
			}
			if (_ownsHost && _host != null) {
				if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_host);
				else UnityEngine.Object.DestroyImmediate(_host);
			}
		}

		private void OnPrepareCompleted(VideoPlayer source) {
			if (_disposing) return;
			_prepared = true;
			State = VideoBackendState.Ready;
			Emit(VideoCompletionKind.Prepared, source.length);
		}

		private void OnSeekCompleted(VideoPlayer source) {
			if (_disposing) return;
			State = VideoBackendState.Ready;
			Emit(VideoCompletionKind.SeekCompleted, source.time);
		}

		private void OnFrameReady(VideoPlayer source, long frameIndex) {
			if (_disposing) return;
			Emit(VideoCompletionKind.FrameReady, source.time, frameIndex);
		}

		private void OnLoopPointReached(VideoPlayer source) {
			if (_disposing) return;
			Emit(source.isLooping ? VideoCompletionKind.Looped : VideoCompletionKind.Ended, source.time);
		}

		private void OnErrorReceived(VideoPlayer source, string message) {
			if (_disposing) return;
			State = VideoBackendState.Faulted;
			Emit(VideoCompletionKind.Error, source.time, -1, message);
		}

		private Result Failure(string code, string message, Exception exception = null) {
			return Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message ?? string.Empty, nodeId: NodeId, generationId: GenerationId, module: "media", exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception)));
		}
	}

	public sealed class UnityVideoBackendFactory : IVideoBackendFactory {
		private readonly Func<GameObject> _hostFactory;
		public UnityVideoBackendFactory(Func<GameObject> hostFactory = null) { _hostFactory = hostFactory; }

		public Result<IVideoBackendHandle> Create(NodeInstanceId nodeId, ulong generationId, VideoBackendKind kind) {
			if (kind != VideoBackendKind.UnityVideoBackend)
				return Result<IVideoBackendHandle>.Failure(new Diagnostic(new DiagnosticCode("media.backend.kind"), Severity.Error, "UnityVideoBackendFactory cannot create the requested backend.", module: "media"));
			return Result<IVideoBackendHandle>.Success(new UnityVideoBackend(nodeId, generationId, _hostFactory == null ? null : _hostFactory()));
		}
	}

	/// <summary>Small native API boundary. The production plugin supplies
	/// these calls; keeping the opaque handle here makes ownership explicit
	/// and allows unsupported platforms to fail with a deterministic
	/// diagnostic instead of pretending to decode Hap.</summary>
	public interface IHapNativeApi {
		bool IsSupportedPlatform { get; }
		Result<IntPtr> Open(VideoPrepareRequest request);
		Result Play(IntPtr handle);
		Result Pause(IntPtr handle);
		Result Stop(IntPtr handle);
		Result SetSpeed(IntPtr handle, double speed);
		Result SetLoop(IntPtr handle, bool loop);
		Result Seek(IntPtr handle, double seconds);
		Result SyncToGraphClock(IntPtr handle, double logicalSeconds, bool demanded);
		object GetBorrowedTexture(IntPtr handle);
		void Close(IntPtr handle);
	}

	public interface IHapNativeDecoder : IDisposable {
		bool IsSupportedPlatform { get; }
		object BorrowedTexture { get; }
		event Action<VideoCompletionKind, double, long, string> Completed;
		Result Prepare(VideoPrepareRequest request);
		Result Play();
		Result Pause();
		Result Stop();
		Result SetSpeed(double speed);
		Result SetLoop(bool loop);
		Result Seek(double seconds);
		Result SyncToGraphClock(double logicalSeconds, bool demanded);
	}

	public sealed class HapNativeDecoder : IHapNativeDecoder {
		private readonly IHapNativeApi _api;
		private IntPtr _handle;
		private bool _disposed;
		private bool _ready;
		private double _speed = 1d;
		private bool _loop = true;

		public HapNativeDecoder(IHapNativeApi api) {
			_api = api ?? throw new ArgumentNullException(nameof(api));
		}

		public bool IsSupportedPlatform => _api.IsSupportedPlatform;
		public object BorrowedTexture => _handle == IntPtr.Zero ? null : _api.GetBorrowedTexture(_handle);
		public event Action<VideoCompletionKind, double, long, string> Completed;

		public Result Prepare(VideoPrepareRequest request) {
			if (_disposed) return Failure("media.lifecycle.disposed", "Hap native decoder is disposed.");
			if (!_api.IsSupportedPlatform) return Failure("media.hap.platform_unsupported", "Hap native decoding is unsupported on this platform.");
			if (request == null) return Failure("media.prepare.request", "Video prepare request is required.");
			if (_handle != IntPtr.Zero) {
				_api.Close(_handle);
				_handle = IntPtr.Zero;
			}
			_ready = false;
			var opened = _api.Open(request);
			if (opened.IsFailure) return Result.Failure(opened.Diagnostic);
			_handle = opened.Value;
			if (_handle == IntPtr.Zero) return Failure("media.hap.native_handle", "Hap native plugin returned an empty decode handle.");
			var speed = _api.SetSpeed(_handle, _speed);
			if (speed.IsFailure) return speed;
			var loop = _api.SetLoop(_handle, _loop);
			if (loop.IsFailure) return loop;
			// The concrete P/Invoke implementation validates and decodes the
			// first sample during Open/Prepare. Test doubles retain the old
			// asynchronous contract and publish completion only through their
			// injected callback.
			if ((_api as IHapNativePreparedApi)?.OpensPrepared == true) {
				_ready = true;
				NotifyPrepared();
			}
			return Result.Success();
		}

		/// <summary>Entry points used by the platform plugin callback bridge.
		/// No callback is synthesized by the managed lifecycle wrapper.</summary>
		public void NotifyPrepared(double durationSeconds = 0d) {
			_ready = true;
			Completed?.Invoke(VideoCompletionKind.Prepared, durationSeconds, -1, null);
		}
		public void NotifyFrameReady(double timeSeconds, long frameIndex) => Completed?.Invoke(VideoCompletionKind.FrameReady, timeSeconds, frameIndex, null);
		public void NotifySeekCompleted(double timeSeconds) => Completed?.Invoke(VideoCompletionKind.SeekCompleted, timeSeconds, -1, null);
		public void NotifyError(string message) => Completed?.Invoke(VideoCompletionKind.Error, 0d, -1, message);

		public Result Play() => Invoke(_api.Play);
		public Result Pause() => Invoke(_api.Pause);
		public Result Stop() => Invoke(_api.Stop);
		public Result SetSpeed(double speed) {
			if (double.IsNaN(speed) || double.IsInfinity(speed) || speed < 0d || speed > 4d) return Failure("media.transport.speed", "Video speed must be between 0 and 4.");
			_speed = speed;
			if (_handle == IntPtr.Zero) return Result.Success();
			return Invoke(handle => _api.SetSpeed(handle, speed));
		}
		public Result SetLoop(bool loop) {
			_loop = loop;
			return _handle == IntPtr.Zero ? Result.Success() : Invoke(handle => _api.SetLoop(handle, loop));
		}

		public Result Seek(double seconds) {
			if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0d) return Failure("media.seek.invalid", "Video seek position must be finite and non-negative.");
			if (_handle == IntPtr.Zero || !_ready) return Failure("media.seek.not_ready", "Hap native decoder is not prepared.");
			Completed?.Invoke(VideoCompletionKind.SeekStarted, seconds, -1, null);
			// Seek completion is published only by the native callback after
			// the requested frame is actually decoded.
			return _api.Seek(_handle, seconds);
		}

		public Result SyncToGraphClock(double logicalSeconds, bool demanded) {
			if (double.IsNaN(logicalSeconds) || double.IsInfinity(logicalSeconds) || logicalSeconds < 0d) return Failure("media.clock.invalid", "Graph clock time must be finite and non-negative.");
			// Graph clock anchoring is valid as soon as the native context is
			// opened; the first decoded frame still controls Prepared state.
			return _handle == IntPtr.Zero ? Failure("media.clock.not_ready", "Hap native decoder is not prepared.") : _api.SyncToGraphClock(_handle, logicalSeconds, demanded);
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			if (_handle != IntPtr.Zero) {
				_api.Close(_handle);
				_handle = IntPtr.Zero;
			}
			_ready = false;
			Completed = null;
		}

		private Result Invoke(Func<IntPtr, Result> operation) {
			if (_disposed) return Failure("media.lifecycle.disposed", "Hap native decoder is disposed.");
			if (_handle == IntPtr.Zero || !_ready) return Failure("media.backend.not_ready", "Hap native decoder is not prepared.");
			var result = operation(_handle);
			return result;
		}

		private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media"));
	}

	public sealed class HapVideoBackend : VideoBackendHandleBase {
		private readonly IHapNativeDecoder _decoder;
		private bool _disposing;

		public HapVideoBackend(NodeInstanceId nodeId, ulong generationId, IHapNativeDecoder decoder)
			: base(nodeId, generationId, VideoBackendKind.HapVideoBackend) {
			_decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
			State = decoder.IsSupportedPlatform ? VideoBackendState.Created : VideoBackendState.Unsupported;
			_decoder.Completed += OnCompleted;
		}

		public override object BorrowedTexture => _decoder.BorrowedTexture;
		public override Result Prepare(VideoPrepareRequest request) {
			if (request == null) return Failure("media.prepare.request", "Video prepare request is required.");
			if (!request.Probe.Supported || request.Probe.Container != VideoContainer.Mov || !IsGuaranteedHap(request.Probe.Codec))
				return Failure("media.codec.unsupported", "Hap backend accepts only guaranteed Hap MOV variants.");
			return Invoke(() => _decoder.Prepare(request), VideoBackendState.Preparing);
		}
		public override Result Play() => Invoke(_decoder.Play, VideoBackendState.Playing);
		public override Result Pause() => Invoke(_decoder.Pause, VideoBackendState.Paused);
		public override Result Stop() => Invoke(_decoder.Stop, VideoBackendState.Ready);
		public override Result SetSpeed(double speed) => Invoke(() => _decoder.SetSpeed(speed), State);
		public override Result SetLoop(bool loop) => Invoke(() => _decoder.SetLoop(loop), State);
		public override Result Seek(double seconds) => Invoke(() => _decoder.Seek(seconds), VideoBackendState.Preparing);
		public override Result SyncToGraphClock(double logicalSeconds, bool demanded) => Invoke(() => _decoder.SyncToGraphClock(logicalSeconds, demanded), State);

		protected override void BeforeDispose() {
			_disposing = true;
			_decoder.Completed -= OnCompleted;
		}

		protected override void DisposeCore() { _decoder.Dispose(); }

		private void OnCompleted(VideoCompletionKind kind, double timeSeconds, long frameIndex, string error) {
			if (_disposing) return;
			if (kind == VideoCompletionKind.Prepared) State = VideoBackendState.Ready;
			else if (kind == VideoCompletionKind.SeekCompleted) State = VideoBackendState.Ready;
			else if (kind == VideoCompletionKind.Error) State = VideoBackendState.Faulted;
			Emit(kind, timeSeconds, frameIndex, error);
		}

		private Result Invoke(Func<Result> operation, VideoBackendState successState) {
			if (_disposing) return Result.Failure(new Diagnostic(new DiagnosticCode("media.lifecycle.disposed"), Severity.Error, "Hap video backend is disposed.", nodeId: NodeId, generationId: GenerationId, module: "media"));
			if (State == VideoBackendState.Unsupported) return Result.Failure(new Diagnostic(new DiagnosticCode("media.hap.platform_unsupported"), Severity.Error, "Hap native decoding is unsupported on this platform.", nodeId: NodeId, generationId: GenerationId, module: "media"));
			var result = operation();
			if (result.IsSuccess) State = successState;
			else State = VideoBackendState.Faulted;
			return result;
		}

		private static bool IsGuaranteedHap(VideoCodec codec) {
			return codec == VideoCodec.Hap1 || codec == VideoCodec.Hap5 || codec == VideoCodec.HapY || codec == VideoCodec.HapM;
		}

		private Result Failure(string code, string message) {
			return Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: NodeId, generationId: GenerationId, module: "media"));
		}
	}

	public sealed class HapVideoBackendFactory : IVideoBackendFactory {
		private readonly Func<IHapNativeDecoder> _decoderFactory;
		public HapVideoBackendFactory(Func<IHapNativeDecoder> decoderFactory) { _decoderFactory = decoderFactory ?? throw new ArgumentNullException(nameof(decoderFactory)); }

		public Result<IVideoBackendHandle> Create(NodeInstanceId nodeId, ulong generationId, VideoBackendKind kind) {
			if (kind != VideoBackendKind.HapVideoBackend)
				return Result<IVideoBackendHandle>.Failure(new Diagnostic(new DiagnosticCode("media.backend.kind"), Severity.Error, "HapVideoBackendFactory cannot create the requested backend.", module: "media"));
			return Result<IVideoBackendHandle>.Success(new HapVideoBackend(nodeId, generationId, _decoderFactory()));
		}
	}

	/// <summary>Composition boundary for a Video node. It deliberately
	/// dispatches by the probe-selected backend kind and reports partial
	/// availability instead of silently falling back to a wrong decoder.</summary>
	public sealed class CompositeVideoBackendFactory : IVideoBackendFactory {
		private readonly IVideoBackendFactory _unity;
		private readonly IVideoBackendFactory _hap;

		public CompositeVideoBackendFactory(IVideoBackendFactory unity, IVideoBackendFactory hap) {
			_unity = unity;
			_hap = hap;
		}

		public Result<IVideoBackendHandle> Create(NodeInstanceId nodeId, ulong generationId, VideoBackendKind kind) {
			var factory = kind == VideoBackendKind.UnityVideoBackend ? _unity
				: kind == VideoBackendKind.HapVideoBackend ? _hap : null;
			if (factory == null)
				return Result<IVideoBackendHandle>.Failure(new Diagnostic(new DiagnosticCode("media.backend.unavailable"), Severity.Error, "The selected video backend is not available in this composition.", nodeId: nodeId, generationId: generationId, module: "media"));
			try {
				var result = factory.Create(nodeId, generationId, kind);
				return result.IsFailure
					? Result<IVideoBackendHandle>.Failure(new Diagnostic(new DiagnosticCode("media.backend.unavailable"), Severity.Error, result.Diagnostic.Message, nodeId: nodeId, generationId: generationId, module: "media"))
					: result;
			}
			catch (Exception exception) {
				return Result<IVideoBackendHandle>.Failure(new Diagnostic(new DiagnosticCode("media.backend.create"), Severity.Error, exception.Message, nodeId: nodeId, generationId: generationId, module: "media", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}
	}

	public sealed class UnsupportedHapNativeApi : IHapNativeApi {
		public bool IsSupportedPlatform => false;
		public Result<IntPtr> Open(VideoPrepareRequest request) => Failure<IntPtr>("media.hap.platform_unsupported", "Hap native decoding is unsupported on this platform.");
		public Result Play(IntPtr handle) => Failure("media.hap.platform_unsupported", "Hap native decoding is unsupported on this platform.");
		public Result Pause(IntPtr handle) => Failure("media.hap.platform_unsupported", "Hap native decoding is unsupported on this platform.");
		public Result Stop(IntPtr handle) => Failure("media.hap.platform_unsupported", "Hap native decoding is unsupported on this platform.");
		public Result SetSpeed(IntPtr handle, double speed) => Failure("media.hap.platform_unsupported", "Hap native decoding is unsupported on this platform.");
		public Result SetLoop(IntPtr handle, bool loop) => Failure("media.hap.platform_unsupported", "Hap native decoding is unsupported on this platform.");
		public Result Seek(IntPtr handle, double seconds) => Failure("media.hap.platform_unsupported", "Hap native decoding is unsupported on this platform.");
		public Result SyncToGraphClock(IntPtr handle, double logicalSeconds, bool demanded) => Failure("media.hap.platform_unsupported", "Hap native decoding is unsupported on this platform.");
		public object GetBorrowedTexture(IntPtr handle) => null;
		public void Close(IntPtr handle) { }
		private static Result Failure(string code, string message) => Result.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media"));
		private static Result<T> Failure<T>(string code, string message) => Result<T>.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media"));
	}

}
