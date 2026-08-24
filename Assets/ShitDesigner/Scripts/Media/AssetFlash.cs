using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;

namespace ShitDesigner.Media
{
    public static class AssetFlashContract
    {
        public const string NodeTypeId = "shitdesigner.media.asset_flash";
        public const string ImagePortId = "image";
        public const string DurationParameterId = "flash.duration_seconds";
        public const int SlotCount = 8;
        public const int SchemaVersion = 1;

        public static string TriggerPortId(int slot) => "trigger_" + ValidateSlot(slot);
        public static string AssetParameterId(int slot) => "slot_" + ValidateSlot(slot) + ".media_asset";

        private static int ValidateSlot(int slot)
        {
            if (slot < 1 || slot > SlotCount) throw new ArgumentOutOfRangeException(nameof(slot));
            return slot;
        }
    }

    public enum AssetFlashMediaKind { Image, Video }

    /// <summary>Verified, runtime-ready media description. Image bytes cross
    /// the resolver boundary so the Media node never bypasses project path,
    /// containment, or integrity policy.</summary>
    public sealed class AssetFlashPrepareRequest
    {
        public AssetFlashMediaKind Kind { get; }
        public byte[] ImageBytes { get; private set; }
        public VideoPrepareRequest Video { get; }
        public VideoFrameConversionMetadata ConversionMetadata { get; }

        private AssetFlashPrepareRequest(AssetFlashMediaKind kind, byte[] imageBytes, VideoPrepareRequest video,
            VideoFrameConversionMetadata conversionMetadata)
        {
            Kind = kind;
            ImageBytes = imageBytes;
            Video = video;
            ConversionMetadata = conversionMetadata ?? new VideoFrameConversionMetadata(VideoColorEncoding.Linear, VideoAlphaMode.Premultiplied);
        }

        public static AssetFlashPrepareRequest Image(byte[] bytes, VideoFrameConversionMetadata conversionMetadata)
        {
            if (bytes == null || bytes.Length == 0) throw new ArgumentException("Image bytes are required.", nameof(bytes));
            return new AssetFlashPrepareRequest(AssetFlashMediaKind.Image, bytes, null, conversionMetadata);
        }

        public static AssetFlashPrepareRequest VideoFile(VideoPrepareRequest request)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            return new AssetFlashPrepareRequest(AssetFlashMediaKind.Video, null, request, request.ConversionMetadata);
        }

        public void ReleaseImageBytes() { ImageBytes = null; }
    }

    public interface IAssetFlashPrepareResolver
    {
        Result<AssetFlashPrepareRequest> Resolve(MediaAssetId mediaAssetId);
    }

    /// <summary>Pure rising-edge and expiry policy shared by production and
    /// deterministic tests. Later slot numbers win when multiple edges occur
    /// in the same frame.</summary>
    public sealed class AssetFlashTriggerState
    {
        private readonly bool[] _previous = new bool[AssetFlashContract.SlotCount];
        public int ActiveSlot { get; private set; } = -1;
        public int LastFiredSlot { get; private set; } = -1;
        public double VisibleUntil { get; private set; }

        public int Sample(IReadOnlyList<bool> triggers, double graphClockTime, double durationSeconds)
        {
            if (triggers == null || triggers.Count != AssetFlashContract.SlotCount) throw new ArgumentException("Exactly eight trigger values are required.", nameof(triggers));
            if (!Finite(graphClockTime) || !Finite(durationSeconds) || durationSeconds <= 0d) throw new ArgumentOutOfRangeException(nameof(durationSeconds));
            var fired = -1;
            for (var index = 0; index < _previous.Length; index++)
            {
                if (triggers[index] && !_previous[index]) fired = index;
                _previous[index] = triggers[index];
            }
            LastFiredSlot = fired;
            if (fired >= 0)
            {
                ActiveSlot = fired;
                VisibleUntil = graphClockTime + durationSeconds;
            }
            if (ActiveSlot >= 0 && graphClockTime + 0.000000001d >= VisibleUntil) ActiveSlot = -1;
            return ActiveSlot;
        }

        public void Clear()
        {
            Array.Clear(_previous, 0, _previous.Length);
            ActiveSlot = -1; LastFiredSlot = -1;
            VisibleUntil = 0d;
        }

        private static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }

    /// <summary>Eight-slot image/video flash node. Configured media is
    /// prepared while demanded; only the active video is allowed to play.</summary>
    public sealed class AssetFlashVisualNodeBinding : IRuntimeVisualNodeBinding
    {
        private readonly IAssetFlashPrepareResolver _resolver;
        private readonly IVideoBackendFactory _backends;
        private readonly IVideoFrameAdapter _frames;
        private readonly IVideoGraphicsCapabilities _graphics;
        public NodeTypeId TypeId { get; } = new NodeTypeId(AssetFlashContract.NodeTypeId);
        public bool IsAvailable => _resolver != null && _backends != null && _frames != null;
        public Diagnostic AvailabilityDiagnostic => IsAvailable ? null : Error("media.flash.binding_missing", "Asset Flash requires a verified media resolver, video backends, and a frame adapter.");

        public AssetFlashVisualNodeBinding(IAssetFlashPrepareResolver resolver, IVideoBackendFactory backends,
            IVideoFrameAdapter frames, IVideoGraphicsCapabilities graphics = null)
        { _resolver = resolver; _backends = backends; _frames = frames; _graphics = graphics; }

        public Result<IRuntimeNode> Create(RuntimeNodeCreateInfo node, ulong generationId)
        {
            if (!IsAvailable) return Result<IRuntimeNode>.Failure(AvailabilityDiagnostic);
            if (node == null || node.TypeId != TypeId || generationId == 0)
                return Result<IRuntimeNode>.Failure(Error("media.flash.node", "Asset Flash factory input does not match its binding."));
            return Result<IRuntimeNode>.Success(new AssetFlashRuntimeNode(node, generationId, _resolver, _backends, _frames, _graphics));
        }

        private static Diagnostic Error(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeTypeId: new NodeTypeId(AssetFlashContract.NodeTypeId), module: "media");
    }

    public sealed class AssetFlashRuntimeNode : IRuntimeNode, IRuntimeDemandAwareNode
    {
        private sealed class Slot : IDisposable
        {
            public MediaAssetId? Asset;
            public AssetFlashPrepareRequest Request;
            public Texture2D Image;
            public IVideoBackendHandle Video;
            public bool PendingPlay;
            public Diagnostic Diagnostic;

            public object Texture => Request == null ? null : Request.Kind == AssetFlashMediaKind.Image ? (object)Image : Video?.BorrowedTexture;
            public bool Ready => Request != null && (Request.Kind == AssetFlashMediaKind.Image
                ? Image != null
                : Video != null && Video.State != VideoBackendState.Faulted && Video.State != VideoBackendState.Unsupported
                  && Video.State != VideoBackendState.Disposed && Video.BorrowedTexture != null);

            public void Dispose()
            {
                if (Video != null) { try { Video.Dispose(); } catch { } Video = null; }
                if (Image != null)
                {
                    if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(Image);
                    else UnityEngine.Object.DestroyImmediate(Image);
                    Image = null;
                }
                Request = null; Diagnostic = null; PendingPlay = false;
            }
        }

        private readonly RuntimeNodeCreateInfo _record;
        private readonly IAssetFlashPrepareResolver _resolver;
        private readonly IVideoBackendFactory _backends;
        private readonly IVideoFrameAdapter _frames;
        private readonly IVideoGraphicsCapabilities _graphics;
        private readonly Slot[] _slots = Enumerable.Range(0, AssetFlashContract.SlotCount).Select(_ => new Slot()).ToArray();
        private readonly AssetFlashTriggerState _triggers = new AssetFlashTriggerState();
        private readonly bool[] _triggerValues = new bool[AssetFlashContract.SlotCount];
        private Texture2D _transparent;
        private int _lastActive = -1;
        private bool _demanded = true;
        private bool _resumeRequested;
        private bool _disposed;

        public NodeInstanceId NodeId => _record.Id;
        public NodeTypeId TypeId { get; } = new NodeTypeId(AssetFlashContract.NodeTypeId);
        public ulong GenerationId { get; }
        public RuntimeNodeState State { get; private set; } = RuntimeNodeState.Preparing;

        public AssetFlashRuntimeNode(RuntimeNodeCreateInfo record, ulong generationId, IAssetFlashPrepareResolver resolver,
            IVideoBackendFactory backends, IVideoFrameAdapter frames, IVideoGraphicsCapabilities graphics = null)
        {
            _record = record ?? throw new ArgumentNullException(nameof(record));
            if (record.TypeId != TypeId || generationId == 0) throw new ArgumentException("Asset Flash node identity is invalid.");
            GenerationId = generationId; _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _backends = backends ?? throw new ArgumentNullException(nameof(backends)); _frames = frames ?? throw new ArgumentNullException(nameof(frames)); _graphics = graphics;
        }

        public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs)
        {
            if (context == null || outputs == null) throw new ArgumentNullException(context == null ? nameof(context) : nameof(outputs));
            var imagePort = new PortId(AssetFlashContract.ImagePortId);
            if (!context.RequestedOutputs.Contains(imagePort)) return;
            if (_disposed) { outputs.SetFaulted(imagePort, Error("media.flash.disposed", "Asset Flash node is disposed.", context)); return; }

            SynchronizeSlots(context);
            for (var index = 0; index < _triggerValues.Length; index++) _triggerValues[index] = ReadTrigger(context, index + 1);
            var duration = Math.Max(.01d, ReadFloat(context, AssetFlashContract.DurationParameterId, .25f));
            var active = _triggers.Sample(_triggerValues, context.Snapshot.GraphClockTime, duration);
            if (active != _lastActive || _triggers.LastFiredSlot >= 0 || _resumeRequested) Activate(active, context);
            _resumeRequested = false;
            _lastActive = active;

            var source = active >= 0 && _slots[active].Ready ? _slots[active].Texture : TransparentTexture();
            var metadata = active >= 0 && _slots[active].Ready ? _slots[active].Request.ConversionMetadata
                : new VideoFrameConversionMetadata(VideoColorEncoding.Linear, VideoAlphaMode.Premultiplied);
            Publish(context, outputs, imagePort, source, metadata);
        }

        public void OnDemandChanged(bool demanded, FrameEvaluationContext context)
        {
            _demanded = demanded;
            if (demanded) { _resumeRequested = true; return; }
            foreach (var slot in _slots.Where(x => x.Video != null && x.Video.State == VideoBackendState.Playing))
                try { slot.Video.Pause(); } catch { }
        }

        private void SynchronizeSlots(NodeExecutionContext context)
        {
            for (var index = 0; index < _slots.Length; index++)
            {
                var asset = ReadAsset(context, index + 1);
                if (_slots[index].Asset == asset) continue;
                _slots[index].Dispose();
                _slots[index].Asset = asset;
                if (!asset.HasValue) continue;
                var resolved = _resolver.Resolve(asset.Value);
                if (resolved.IsFailure) { _slots[index].Diagnostic = resolved.Diagnostic; context.Diagnostics.Report(resolved.Diagnostic); continue; }
                _slots[index].Request = resolved.Value;
                if (resolved.Value.Kind == AssetFlashMediaKind.Image) PrepareImage(index, context);
                else PrepareVideo(index, context);
            }
        }

        private void PrepareImage(int index, NodeExecutionContext context)
        {
            Texture2D texture = null;
            try
            {
                texture = new Texture2D(2, 2, TextureFormat.RGBA32, false, true) { name = "ShitDesigner.AssetFlash.Slot" + (index + 1) };
                if (!texture.LoadImage(_slots[index].Request.ImageBytes, true)) throw new InvalidDataException("Unity could not decode the selected image.");
                texture.wrapMode = TextureWrapMode.Clamp; texture.filterMode = FilterMode.Bilinear;
                _slots[index].Image = texture;
                texture = null;
            }
            catch (Exception exception)
            {
                _slots[index].Diagnostic = Error("media.flash.image_decode", exception.Message, context, exception);
                context.Diagnostics.Report(_slots[index].Diagnostic);
            }
            finally
            {
                if (texture != null)
                {
                    if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(texture);
                    else UnityEngine.Object.DestroyImmediate(texture);
                }
                _slots[index].Request.ReleaseImageBytes();
            }
        }

        private void PrepareVideo(int index, NodeExecutionContext context)
        {
            var selected = VideoBackendSelector.Select(_slots[index].Request.Video.Probe, _graphics);
            if (selected.IsFailure) { _slots[index].Diagnostic = selected.Diagnostic; context.Diagnostics.Report(selected.Diagnostic); return; }
            var created = _backends.Create(NodeId, GenerationId, selected.Value);
            if (created.IsFailure) { _slots[index].Diagnostic = created.Diagnostic; context.Diagnostics.Report(created.Diagnostic); return; }
            var slot = _slots[index]; slot.Video = created.Value;
            slot.Video.Completed += completion => OnVideoCompletion(index, completion);
            var loop = slot.Video.SetLoop(false); if (loop.IsFailure) slot.Diagnostic = loop.Diagnostic;
            var speed = slot.Video.SetSpeed(1d); if (speed.IsFailure) slot.Diagnostic = speed.Diagnostic;
            var prepared = slot.Video.Prepare(slot.Request.Video);
            if (prepared.IsFailure) { slot.Diagnostic = prepared.Diagnostic; context.Diagnostics.Report(prepared.Diagnostic); }
        }

        private void Activate(int active, NodeExecutionContext context)
        {
            for (var index = 0; index < _slots.Length; index++)
            {
                var video = _slots[index].Video;
                if (video == null) continue;
                if (index != active)
                {
                    _slots[index].PendingPlay = false;
                    if (video.State == VideoBackendState.Playing) Report(video.Pause(), context);
                    continue;
                }
                RestartVideo(index, context);
            }
        }

        private void RestartVideo(int index, NodeExecutionContext context)
        {
            var slot = _slots[index];
            if (!_demanded || slot.Video == null || slot.Request?.Kind != AssetFlashMediaKind.Video) return;
            if (slot.Video.State == VideoBackendState.Preparing || slot.Video.State == VideoBackendState.Created)
            { slot.PendingPlay = true; return; }
            if (slot.Video.State == VideoBackendState.Playing) Report(slot.Video.Pause(), context);
            var seek = slot.Video.Seek(0d);
            if (seek.IsFailure) { Report(seek, context); return; }
            slot.PendingPlay = slot.Video.State == VideoBackendState.Preparing;
            if (!slot.PendingPlay) TryPlay(index, context);
        }

        private void OnVideoCompletion(int index, VideoBackendCompletion completion)
        {
            if (_disposed || index < 0 || index >= _slots.Length || completion == null) return;
            var slot = _slots[index];
            if (completion.Kind == VideoCompletionKind.Error)
            {
                slot.Diagnostic = new Diagnostic(new DiagnosticCode("media.flash.video_decode"), Severity.Error, completion.ErrorMessage, nodeId: NodeId, nodeTypeId: TypeId, generationId: GenerationId, module: "media");
                slot.PendingPlay = false;
            }
            else if ((completion.Kind == VideoCompletionKind.Prepared || completion.Kind == VideoCompletionKind.SeekCompleted || completion.Kind == VideoCompletionKind.FrameReady)
                && slot.PendingPlay && _lastActive == index && _demanded)
            {
                slot.PendingPlay = false;
                var played = slot.Video.Play();
                if (played.IsFailure) slot.Diagnostic = played.Diagnostic;
            }
        }

        private void TryPlay(int index, NodeExecutionContext context)
        {
            var slot = _slots[index];
            if (!_demanded || slot.Video == null || slot.Video.State == VideoBackendState.Playing) return;
            var played = slot.Video.Play();
            if (played.IsFailure) Report(played, context);
        }

        private void Publish(NodeExecutionContext context, NodeOutputWriter outputs, PortId imagePort, object source, VideoFrameConversionMetadata metadata)
        {
            if (context.OutputSurfaces == null || !RuntimeOutputResolutionDemandAccess.TryGet(context, NodeId, imagePort, out var demand))
            { State = RuntimeNodeState.Preparing; outputs.SetPreparing(imagePort, Error("media.flash.surface_missing", "Asset Flash output has no prepared surface demand.", context)); return; }
            var surface = context.OutputSurfaces.TryGetPrepared(NodeId, imagePort, demand.Width, demand.Height, context.Snapshot.FrameNumber);
            if (surface.IsFailure || surface.Value == null)
            { State = RuntimeNodeState.Faulted; outputs.SetFaulted(imagePort, surface.IsFailure ? surface.Diagnostic : Error("media.flash.surface_invalid", "Asset Flash received an invalid output surface.", context)); return; }
            var frame = _frames is IVideoOutputSurfaceFrameAdapterWithConversion conversion
                ? conversion.Create(source, surface.Value, context.Snapshot.FrameNumber, metadata)
                : _frames is IVideoOutputSurfaceFrameAdapter adapter
                    ? adapter.Create(source, surface.Value, context.Snapshot.FrameNumber)
                    : _frames.Create(source, surface.Value.Width, surface.Value.Height, context.Snapshot.FrameNumber, surface.Value.LeaseId);
            if (frame.IsFailure) { State = RuntimeNodeState.Faulted; outputs.SetFaulted(imagePort, frame.Diagnostic); return; }
            State = RuntimeNodeState.Ready; outputs.SetAvailable(imagePort, PortValue.FromImageFrame(frame.Value));
        }

        private Texture2D TransparentTexture()
        {
            if (_transparent != null) return _transparent;
            _transparent = new Texture2D(1, 1, TextureFormat.RGBA32, false, true) { name = "ShitDesigner.AssetFlash.Transparent" };
            _transparent.SetPixel(0, 0, Color.clear); _transparent.Apply(false, true);
            return _transparent;
        }

        private MediaAssetId? ReadAsset(NodeExecutionContext context, int slot)
        {
            var key = new ParameterKey(NodeId, new ParameterId(AssetFlashContract.AssetParameterId(slot)));
            if (context.Snapshot.EffectiveValues.TryGetValue(key, out var value) && value.Type == ParameterType.MediaAssetReference) return value.AsMediaAsset();
            var initial = _record.Parameters.FirstOrDefault(x => x.Id == key.ParameterId);
            return initial != null && initial.Value.Type == ParameterType.MediaAssetReference ? initial.Value.AsMediaAsset() : (MediaAssetId?)null;
        }

        private bool ReadTrigger(NodeExecutionContext context, int slot)
        {
            if (!context.Inputs.TryGetValue(new PortId(AssetFlashContract.TriggerPortId(slot)), out var input) || !input.HasValue) return false;
            try { return input.Value.AsBool(); } catch (InvalidOperationException) { return false; }
        }

        private float ReadFloat(NodeExecutionContext context, string id, float fallback)
        {
            var key = new ParameterKey(NodeId, new ParameterId(id));
            if (context.Snapshot.EffectiveValues.TryGetValue(key, out var value) && value.Type == ParameterType.Float) return value.AsFloat();
            var initial = _record.Parameters.FirstOrDefault(x => x.Id == key.ParameterId);
            return initial != null && initial.Value.Type == ParameterType.Float ? initial.Value.AsFloat() : fallback;
        }

        private void Report(Result result, NodeExecutionContext context)
        { if (result.IsFailure) context.Diagnostics.Report(result.Diagnostic); }

        private Diagnostic Error(string code, string message, NodeExecutionContext context, Exception exception = null)
            => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: NodeId, nodeTypeId: TypeId, generationId: GenerationId,
                frameNumber: unchecked((long)context.Snapshot.FrameNumber), graphClockTime: context.Snapshot.GraphClockTime, module: "media",
                exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception));

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            foreach (var slot in _slots) slot.Dispose();
            if (_transparent != null)
            {
                if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_transparent);
                else UnityEngine.Object.DestroyImmediate(_transparent);
                _transparent = null;
            }
            _triggers.Clear(); State = RuntimeNodeState.Disposed;
        }
    }
}
