using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Media;
using ShitDesigner.Nodes;
using ShitDesigner.Rendering;
using ShitDesigner.Runtime;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.Video;

namespace ShitDesigner.Main {
	public readonly struct LiveProgramFrame {
		public RenderTexture Texture { get; }
		public ulong FrameNumber { get; }
		public int Width { get; }
		public int Height { get; }
		public RenderTextureFormat Format { get; }

		internal LiveProgramFrame(RenderTexture texture, ulong frameNumber) {
			Texture = texture;
			FrameNumber = frameNumber;
			Width = texture != null ? texture.width : 0;
			Height = texture != null ? texture.height : 0;
			Format = texture != null ? texture.format : default(RenderTextureFormat);
		}
	}

	public readonly struct LiveProgramFrames {
		private readonly LiveProgramFrame[] _frames;

		internal LiveProgramFrames(IEnumerable<LiveProgramFrame> frames) {
			_frames = (frames ?? Array.Empty<LiveProgramFrame>()).ToArray();
		}

		public int Count => _frames?.Length ?? 0;
		public LiveProgramFrame this[int index] => _frames[index];
		public LiveProgramFrame Primary => Count > 0 ? _frames[0] : default(LiveProgramFrame);
		public IReadOnlyList<LiveProgramFrame> Frames => _frames ?? (IReadOnlyList<LiveProgramFrame>)Array.Empty<LiveProgramFrame>();
	}

	internal readonly struct LiveRenderSize {
		public int Width { get; }
		public int Height { get; }

		public LiveRenderSize(int width, int height) {
			if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
			if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
			Width = width;
			Height = height;
		}
	}

	public readonly struct LiveParameterApplicationResult {
		public ulong SequenceNumber { get; }
		public bool Applied { get; }
		public string RejectionReason { get; }

		internal LiveParameterApplicationResult(ulong sequenceNumber, bool applied, string rejectionReason) {
			SequenceNumber = sequenceNumber;
			Applied = applied;
			RejectionReason = rejectionReason;
		}
	}

	internal sealed class LiveGraph : IDisposable {
		private readonly SceneIsolationManager _sceneManager;
		private readonly RenderTexturePool _renderPool;
		private readonly Func<PatchDefinition, PatchFlashDefinition, LiveRenderSize, LiveProgramOutput> _createOutput;
		public IReadOnlyList<PatchDefinition> PatchDefinitions { get; }

		public LiveGraph(SceneIsolationManager sceneManager, RenderTexturePool renderPool, IEnumerable<PatchDefinition> patchDefinitions,
			Func<PatchDefinition, PatchFlashDefinition, LiveRenderSize, LiveProgramOutput> createOutput) {
			_sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
			_renderPool = renderPool ?? throw new ArgumentNullException(nameof(renderPool));
			_createOutput = createOutput ?? throw new ArgumentNullException(nameof(createOutput));
			PatchDefinitions = (patchDefinitions ?? throw new ArgumentNullException(nameof(patchDefinitions))).ToArray();
			if (PatchDefinitions.Count == 0) throw new ArgumentException("A live graph requires patches.");
		}

		public LiveProgramOutput CreateOutput(PatchDefinition patch, PatchFlashDefinition flashPatch, LiveRenderSize renderSize)
			=> _createOutput(patch, flashPatch, renderSize);

		public void Dispose() {
			_sceneManager.Dispose();
			_renderPool.Dispose();
		}
	}

	internal sealed class GraphDefinition {
		public string OutputNodeId { get; }
		public IReadOnlyList<NodeDefinition> Nodes { get; }
		public IReadOnlyList<NodeConnection> Connections { get; }
		public IReadOnlyList<NodeDefinition> EvaluationOrder { get; }

		public GraphDefinition(string outputNodeId,
			IEnumerable<NodeDefinition> nodes, IEnumerable<NodeConnection> connections) {
			OutputNodeId = RequireId(outputNodeId, nameof(outputNodeId));
			Nodes = (nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray();
			Connections = (connections ?? throw new ArgumentNullException(nameof(connections))).ToArray();
			if (Nodes.Count == 0) throw new ArgumentException("A live Program graph requires at least one node.", nameof(nodes));
			if (Nodes.Any(node => node == null) || Nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() != Nodes.Count)
				throw new ArgumentException("Live Program graph node IDs must be unique.", nameof(nodes));
			if (!Nodes.Any(node => node.Id == OutputNodeId)) throw new ArgumentException("The live Program graph output must be a configured node.", nameof(outputNodeId));
			if (Connections.Any(connection => connection == null)) throw new ArgumentException("Live Program graph connections cannot be null.", nameof(connections));
			if (Connections.Any(connection => !Nodes.Any(node => node.Id == connection.TargetNodeId)))
				throw new ArgumentException("Every live Program graph connection target must exist.", nameof(connections));
			if (Connections.Any(connection => !Nodes.Any(node => node.Id == connection.SourceNodeId)))
				throw new ArgumentException("Every live Program graph connection source must exist.", nameof(connections));
			if (Connections.GroupBy(connection => new { connection.TargetNodeId, connection.TargetPortId }).Any(group => group.Count() > 1))
				throw new ArgumentException("A live Program graph input port can have only one connection.", nameof(connections));
			EvaluationOrder = BuildEvaluationOrder();
		}

		private IReadOnlyList<NodeDefinition> BuildEvaluationOrder() {
			var remaining = Nodes.ToList();
			var resolved = new HashSet<string>(StringComparer.Ordinal);
			var ordered = new List<NodeDefinition>(Nodes.Count);
			while (remaining.Count > 0) {
				var next = remaining.FirstOrDefault(node => Connections.Where(connection => connection.TargetNodeId == node.Id)
					.All(connection => resolved.Contains(connection.SourceNodeId)));
				if (next == null) throw new ArgumentException("Live Program graph connections must be acyclic.", nameof(Connections));
				ordered.Add(next);
				resolved.Add(next.Id);
				remaining.Remove(next);
			}
			return ordered;
		}

		private static string RequireId(string value, string parameterName) {
			if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException("A live Program graph ID is required.", parameterName);
			return value.Trim();
		}
	}

	internal sealed class NodeDefinition {
		public string Id { get; }
		public NodeTypeId TypeId { get; }

		public NodeDefinition(string id, string typeId) {
			if (string.IsNullOrWhiteSpace(id))
				throw new ArgumentException("A live Program graph node ID is required.", nameof(id));

			Id = id.Trim();
			TypeId = new NodeTypeId(typeId);
		}
	}

	internal sealed class NodeConnection {
		public string SourceNodeId { get; private set; }
		public PortId SourcePortId { get; private set; }
		public string TargetNodeId { get; private set; }
		public PortId TargetPortId { get; private set; }

		public NodeConnection(string sourceNodeId, string targetNodeId, string targetPortId) {
			Initialize(sourceNodeId, PatchProgramGraph.ImagePortId, targetNodeId, targetPortId);
		}

		public NodeConnection(string sourceNodeId, string sourcePortId, string targetNodeId, string targetPortId) {
			Initialize(sourceNodeId, sourcePortId, targetNodeId, targetPortId);
		}

		private void Initialize(string sourceNodeId, string sourcePortId, string targetNodeId, string targetPortId) {
			if (string.IsNullOrWhiteSpace(sourceNodeId) || string.IsNullOrWhiteSpace(targetNodeId))
				throw new ArgumentException("Live Program graph connection node IDs are required.");
			SourceNodeId = sourceNodeId.Trim();
			SourcePortId = new PortId(sourcePortId);
			TargetNodeId = targetNodeId.Trim();
			TargetPortId = new PortId(targetPortId);
		}
	}

	internal sealed class LiveProgramOutput : IDisposable {
		public RenderTexture ProgramTexture { get; }
		private readonly RenderTexture _shaderGraphTexture;
		private readonly LiveProgramGraph _programGraph;
		private readonly LiveProgramFlash _flash;
		private readonly PatchFlashDefinition _flashPatch;

		public LiveProgramOutput(RenderTexture programTexture, RenderTexture shaderGraphTexture,
			LiveProgramGraph programGraph, LiveProgramFlash flash, PatchFlashDefinition flashPatch) {
			ProgramTexture = programTexture ?? throw new ArgumentNullException(nameof(programTexture));
			_shaderGraphTexture = shaderGraphTexture ?? throw new ArgumentNullException(nameof(shaderGraphTexture));
			_programGraph = programGraph ?? throw new ArgumentNullException(nameof(programGraph));
			_flash = flash ?? throw new ArgumentNullException(nameof(flash));
			_flashPatch = flashPatch;
		}

		public void Evaluate(double deltaSeconds, BeatClockFrame bpmFrame) => _programGraph.Evaluate(deltaSeconds, bpmFrame);

		public void SceneUpdate(double deltaSeconds) => _programGraph.SceneUpdate(deltaSeconds);

		public void Render(double graphTime, ulong frameNumber) {
			_programGraph.Render(_shaderGraphTexture, graphTime, frameNumber);
			_flash.Render(_shaderGraphTexture, ProgramTexture, graphTime);
		}

		public void TriggerFlash(double graphTime) {
			_flash.Trigger(graphTime);
			if (_flashPatch?.Image == null) return;
			_flash.TriggerAsset(graphTime, _flashPatch.Image, _flashPatch.DurationSeconds);
		}

		public bool TrySetGraphParameter(string nodeId, string parameterId, ParameterValue value, out string rejectionReason)
			=> _programGraph.TrySetParameter(nodeId, parameterId, value, out rejectionReason);

		public bool TryGetSceneParameter(string nodeId, string parameterId, out LiveSceneRoot root, out LiveParameterDefinition definition)
			=> _programGraph.TryGetSceneParameter(nodeId, parameterId, out root, out definition);

		public void Dispose() {
			_flash.Dispose();
			_programGraph.Dispose();
			ReleaseTexture(_shaderGraphTexture);
			ReleaseTexture(ProgramTexture);
		}

		private static void ReleaseTexture(RenderTexture texture) {
			if (texture == null) return;
			texture.Release();
			if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(texture);
			else UnityEngine.Object.DestroyImmediate(texture);
		}
	}

	internal sealed class LiveProgramShaderDefinition {
		public ShaderNodeManifestEntry Entry { get; }
		public Shader Shader { get; }

		public LiveProgramShaderDefinition(ShaderNodeManifestEntry entry, Shader shader) {
			Entry = entry ?? throw new ArgumentNullException(nameof(entry));
			Shader = shader ?? throw new ArgumentNullException(nameof(shader));
		}
	}

	internal interface ILiveProgramGraphNode : IDisposable {
		string Id { get; }
		RenderTexture Target { get; }
		void Evaluate(double deltaSeconds, BeatClockFrame bpmFrame);
		void SceneUpdate(double deltaSeconds);
		void Render(IReadOnlyDictionary<string, Texture> outputs, double graphTime, ulong frameNumber);
		bool TrySetParameter(string parameterId, ParameterValue value, out string rejectionReason);
	}

	internal sealed class LiveProgramSceneGraphNode : ILiveProgramGraphNode {
		private readonly SceneNodeRuntime m_Runtime;
		private readonly LiveSceneRoot m_Root;
		private readonly RenderTexture m_Target;
		private readonly LiveRenderSize m_RenderSize;
		private bool m_Disposed;

		public string Id { get; }
		public RenderTexture Target => m_Target;
		public Scene3DDefinition Definition { get; }
		public LiveSceneRoot Root => m_Root;

		public LiveProgramSceneGraphNode(string id, Scene3DDefinition definition, SceneNodeRuntime runtime, LiveSceneRoot root,
			RenderTexture target, LiveRenderSize renderSize) {
			if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A live Program scene node ID is required.", nameof(id));
			Definition = definition ?? throw new ArgumentNullException(nameof(definition));
			m_Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
			m_Root = root ?? throw new ArgumentNullException(nameof(root));
			m_Target = target ?? throw new ArgumentNullException(nameof(target));
			m_RenderSize = renderSize;
			Id = id.Trim();
		}

		public void Evaluate(double deltaSeconds, BeatClockFrame bpmFrame) {
			var scaledDeltaSeconds = Math.Max(0d, deltaSeconds) * m_Root.TimeScale;
			var animation = m_Runtime.AdvanceGraphClock(scaledDeltaSeconds);
			if (animation.IsFailure) throw new InvalidOperationException(animation.Error.Message);
			var bpm = m_Runtime.ApplyBpmClock(bpmFrame);
			if (bpm.IsFailure) throw new InvalidOperationException(bpm.Error.Message);
		}

		public void SceneUpdate(double deltaSeconds) {
			var result = m_Runtime.AdvancePhysics(Math.Max(0d, deltaSeconds) * m_Root.TimeScale);
			if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
		}

		public void Render(IReadOnlyDictionary<string, Texture> outputs, double graphTime, ulong frameNumber) {
			if (m_Disposed) throw new ObjectDisposedException(nameof(LiveProgramSceneGraphNode));
			var result = m_Runtime.Render(m_Target, m_RenderSize.Width, m_RenderSize.Height, frameNumber);
			if (result.IsFailure || result.Value == null || !result.Value.Rendered)
				throw new InvalidOperationException(result.IsFailure ? result.Error.Message : "A live scene node did not render.");
		}

		public bool TrySetParameter(string parameterId, ParameterValue value, out string rejectionReason) {
			if (value.Type != ParameterType.Float) {
				rejectionReason = "The published scene parameter requires a float value.";
				return false;
			}
			return m_Root.TrySetParameter(parameterId, value.AsFloat(), out rejectionReason);
		}

		public bool TryGetParameter(string parameterId, out LiveParameterDefinition definition) {
			definition = m_Root.GetParameterDefinitions().FirstOrDefault(candidate => candidate.Id == parameterId);
			return !string.IsNullOrWhiteSpace(definition.Id);
		}

		public void Dispose() {
			if (m_Disposed) return;
			m_Disposed = true;
			m_Runtime.Dispose();
			ReleaseTexture(m_Target);
		}

		private static void ReleaseTexture(RenderTexture texture) {
			if (texture == null) return;
			texture.Release();
			if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(texture);
			else UnityEngine.Object.DestroyImmediate(texture);
		}
	}

	internal sealed class LiveProgramShaderGraphNode : ILiveProgramGraphNode {
		private readonly ShaderPassGraphRuntimeNode _runtime;
		public string Id { get; }
		public RenderTexture Target { get; }
		public IReadOnlyDictionary<PortId, string> Inputs { get; }

		public LiveProgramShaderGraphNode(string id, ShaderPassGraphRuntimeNode runtime, RenderTexture target, IReadOnlyDictionary<PortId, string> inputs) {
			Id = id;
			_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
			Target = target ?? throw new ArgumentNullException(nameof(target));
			Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
		}

		public void Evaluate(double deltaSeconds, BeatClockFrame bpmFrame) { }

		public void SceneUpdate(double deltaSeconds) { }

		public void Render(IReadOnlyDictionary<string, Texture> outputs, double graphTime, ulong frameNumber) {
			var inputs = Inputs.ToDictionary(input => input.Key, input => outputs.TryGetValue(input.Value, out var texture) ? texture : null);
			var result = _runtime.Render(inputs, Target, frameNumber, graphTime);
			if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
		}

		public bool TrySetParameter(string parameterId, ParameterValue value, out string rejectionReason)
			=> _runtime.TrySetDirectParameter(parameterId, value, out rejectionReason);

		public void Dispose() {
			_runtime.Dispose();
			ReleaseTexture(Target);
		}

		private static void ReleaseTexture(RenderTexture texture) {
			if (texture == null) return;
			texture.Release();
			if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(texture);
			else UnityEngine.Object.DestroyImmediate(texture);
		}
	}

	internal sealed class LiveProgramFileVideoGraphNode : ILiveProgramGraphNode {
		private readonly IVideoBackendHandle m_Backend;
		private readonly HapUnityGraphicsBridge m_HapBridge;
		private readonly IVideoFrameConversionPass m_Conversion;
		private readonly VideoProbeResult m_Probe;
		private readonly RenderTexture m_Target;
		private readonly bool m_Playing;
		private readonly double m_Playhead;
		private readonly float m_Speed;
		private readonly bool m_Loop;
		private bool m_Disposed;

		public string Id { get; }
		public RenderTexture Target => m_Target;

		public LiveProgramFileVideoGraphNode(string id, RenderTexture target, string videoPath, bool playing, double playhead, float speed, bool loop,
			Material videoConversionMaterial, Material hapPremultiplyMaterial, Material hapYCoCgMaterial,
			Material hapAlphaMaterial, ComputeShader hapDecodeShader) {
			if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A live Program video node ID is required.", nameof(id));
			if (target == null) throw new ArgumentNullException(nameof(target));
			if (string.IsNullOrWhiteSpace(videoPath)) throw new ArgumentException("A live Program video file is required.", nameof(videoPath));
			if (double.IsNaN(playhead) || double.IsInfinity(playhead) || playhead < 0d) throw new ArgumentOutOfRangeException(nameof(playhead));
			if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f || speed > 4f) throw new ArgumentOutOfRangeException(nameof(speed));

			Id = id.Trim();
			m_Target = target;
			m_Playing = playing;
			m_Playhead = playhead;
			m_Speed = speed;
			m_Loop = loop;

			var sourcePath = ResolveSourcePath(videoPath);
			var probe = new FileVideoMetadataProbe().Probe(sourcePath);
			if (probe.IsFailure) throw new InvalidOperationException("The live Program video probe failed: " + probe.Error.Message);
			if (!probe.Value.Supported) throw new InvalidOperationException("The live Program video is unsupported: " + probe.Value.DiagnosticMessage);

			var hapGraphics = new UnityHapGraphicsCapabilityProbe();
			var graphics = new VideoGraphicsCapabilities(hapGraphics.SupportsDirectCompressed, hapGraphics.SupportsCompute, hapGraphics.SupportsCpu);
			var selected = VideoBackendSelector.Select(probe.Value, graphics);
			if (selected.IsFailure) throw new InvalidOperationException("The live Program video backend could not be selected: " + selected.Error.Message);

			IVideoBackendHandle backend = null;
			HapUnityGraphicsBridge hapBridge = null;
			IVideoFrameConversionPass conversion = null;
			try {
				if (selected.Value == VideoBackendKind.HapVideoBackend) {
					hapBridge = new HapUnityGraphicsBridge(hapGraphics, hapDecodeShader, hapPremultiplyMaterial, hapYCoCgMaterial, hapAlphaMaterial);
					backend = new HapVideoBackend(NodeInstanceId.New(), 1UL,
						new HapNativeDecoder(new PInvokeHapNativeApi(hapBridge)));
				}
				else {
					backend = new UnityVideoBackend(NodeInstanceId.New(), 1UL);
					conversion = new UnityVideoFrameConversionPass(videoConversionMaterial);
				}

				var prepared = backend.Prepare(new VideoPrepareRequest(sourcePath, probe.Value));
				if (prepared.IsFailure) throw new InvalidOperationException("The live Program video could not be prepared: " + prepared.Error.Message);
				var setSpeed = backend.SetSpeed(speed);
				if (setSpeed.IsFailure) throw new InvalidOperationException("The live Program video speed could not be applied: " + setSpeed.Error.Message);
				var setLoop = backend.SetLoop(loop);
				if (setLoop.IsFailure) throw new InvalidOperationException("The live Program video loop mode could not be applied: " + setLoop.Error.Message);

				m_Backend = backend;
				m_HapBridge = hapBridge;
				m_Conversion = conversion;
				m_Probe = probe.Value;
			}
			catch {
				conversion?.Dispose();
				backend?.Dispose();
				hapBridge?.Dispose();
				throw;
			}
		}

		public void Evaluate(double deltaSeconds, BeatClockFrame bpmFrame) { }

		public void SceneUpdate(double deltaSeconds) { }

		public void Render(IReadOnlyDictionary<string, Texture> outputs, double graphTime, ulong frameNumber) {
			if (m_Disposed) throw new ObjectDisposedException(nameof(LiveProgramFileVideoGraphNode));
			var sync = m_Backend.SyncToGraphClock(LogicalPosition(graphTime), demanded: true);
			if (sync.IsFailure) throw new InvalidOperationException(sync.Error.Message);

			if (IsReady() || m_Backend.BackendKind == VideoBackendKind.HapVideoBackend) {
				if (m_Playing && m_Backend.State != VideoBackendState.Playing) {
					var play = m_Backend.Play();
					if (play.IsFailure) throw new InvalidOperationException(play.Error.Message);
				}
				else if (!m_Playing && m_Backend.State == VideoBackendState.Playing) {
					var pause = m_Backend.Pause();
					if (pause.IsFailure) throw new InvalidOperationException(pause.Error.Message);
				}
			}

			var source = m_Backend.BorrowedTexture as Texture;
			if (source == null) {
				ClearTexture(m_Target);
				return;
			}
			if (m_Backend.BackendKind == VideoBackendKind.HapVideoBackend) {
				Graphics.Blit(source, m_Target);
				return;
			}

			var converted = m_Conversion.Convert(source, m_Target, m_Probe.ConversionMetadata);
			if (converted.IsFailure) throw new InvalidOperationException(converted.Error.Message);
		}

		public bool TrySetParameter(string parameterId, ParameterValue value, out string rejectionReason) {
			rejectionReason = "Video player parameters cannot be changed by a live patch parameter.";
			return false;
		}

		public void Dispose() {
			if (m_Disposed) return;
			m_Disposed = true;
			m_Backend.Dispose();
			m_Conversion?.Dispose();
			m_HapBridge?.Dispose();
			ReleaseTexture(m_Target);
		}

		private bool IsReady() {
			return m_Backend.State == VideoBackendState.Ready
				|| m_Backend.State == VideoBackendState.Playing
				|| m_Backend.State == VideoBackendState.Paused;
		}

		private double LogicalPosition(double graphTime) {
			var position = m_Playing ? m_Playhead + Math.Max(0d, graphTime) * m_Speed : m_Playhead;
			if (m_Probe.DurationSeconds <= 0d) return position;
			if (m_Loop) return position % m_Probe.DurationSeconds;
			return Math.Min(position, m_Probe.DurationSeconds);
		}

		private static string ResolveSourcePath(string storedPath) {
			var normalized = (storedPath ?? string.Empty).Trim().Replace('\\', '/');
			if (Path.IsPathRooted(normalized)) return Path.GetFullPath(normalized);
			const string streamingAssetsPrefix = "Assets/StreamingAssets";
			if (normalized.Equals(streamingAssetsPrefix, StringComparison.OrdinalIgnoreCase)
				|| normalized.StartsWith(streamingAssetsPrefix + "/", StringComparison.OrdinalIgnoreCase)) {
				var suffix = normalized.Substring(streamingAssetsPrefix.Length).TrimStart('/');
				return Path.GetFullPath(Path.Combine(UnityEngine.Application.streamingAssetsPath, suffix.Replace('/', Path.DirectorySeparatorChar)));
			}
			var projectRoot = Directory.GetParent(Path.GetFullPath(UnityEngine.Application.dataPath)).FullName;
			return Path.GetFullPath(Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
		}

		private static void ClearTexture(RenderTexture texture) {
			var previous = RenderTexture.active;
			try {
				RenderTexture.active = texture;
				GL.Clear(true, true, Color.black);
			}
			finally { RenderTexture.active = previous; }
		}

		private static void ReleaseTexture(RenderTexture texture) {
			if (texture == null) return;
			texture.Release();
			if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(texture);
			else UnityEngine.Object.DestroyImmediate(texture);
		}
	}

	internal sealed class LiveProgramVideoGraphNode : ILiveProgramGraphNode {
		private readonly GameObject _host;
		private readonly VideoPlayer _player;
		private readonly RenderTexture _target;
		private readonly bool _playing;
		private readonly double _playhead;
		private readonly float _speed;
		private readonly bool _loop;
		private bool _playheadApplied;
		private bool _disposed;

		public string Id { get; }
		public RenderTexture Target => _target;

		public LiveProgramVideoGraphNode(string id, RenderTexture target, VideoClip clip, bool playing, double playhead, float speed, bool loop) {
			if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A live Program video node ID is required.", nameof(id));
			if (target == null) throw new ArgumentNullException(nameof(target));
			if (clip == null) throw new ArgumentNullException(nameof(clip));
			if (double.IsNaN(playhead) || double.IsInfinity(playhead) || playhead < 0d) throw new ArgumentOutOfRangeException(nameof(playhead));
			if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f || speed > 4f) throw new ArgumentOutOfRangeException(nameof(speed));

			Id = id.Trim();
			_target = target;
			_playing = playing;
			_playhead = playhead;
			_speed = speed;
			_loop = loop;
			_host = new GameObject("ShitDesigner.Main.Video." + Id);
			try {
				_player = _host.AddComponent<VideoPlayer>();
				_player.playOnAwake = false;
				_player.waitForFirstFrame = true;
				_player.renderMode = VideoRenderMode.APIOnly;
				_player.audioOutputMode = VideoAudioOutputMode.None;
				_player.sendFrameReadyEvents = false;
				_player.source = UnityEngine.Video.VideoSource.VideoClip;
				_player.clip = clip;
				_player.isLooping = _loop;
				_player.playbackSpeed = _speed;
				_player.Prepare();
			}
			catch {
				DestroyObject(_host);
				throw;
			}
		}

		public void Evaluate(double deltaSeconds, BeatClockFrame bpmFrame) { }

		public void SceneUpdate(double deltaSeconds) { }

		public void Render(IReadOnlyDictionary<string, Texture> outputs, double graphTime, ulong frameNumber) {
			if (_disposed) throw new ObjectDisposedException(nameof(LiveProgramVideoGraphNode));
			if (_player.isPrepared) {
				if (!_playheadApplied) {
					_player.time = _playhead;
					_playheadApplied = true;
				}
				if (_playing) {
					_player.timeReference = VideoTimeReference.ExternalTime;
					_player.externalReferenceTime = _playhead + graphTime;
					if (!_player.isPlaying) _player.Play();
				}
				else if (_player.isPlaying) _player.Pause();
			}

			var source = _player.texture;
			if (source == null) ClearTexture(_target);
			else Graphics.Blit(source, _target);
		}

		public bool TrySetParameter(string parameterId, ParameterValue value, out string rejectionReason) {
			rejectionReason = "Video player parameters cannot be changed by a live patch parameter.";
			return false;
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			if (_player != null) {
				try { _player.Stop(); } catch { }
			}
			DestroyObject(_host);
			ReleaseTexture(_target);
		}

		private static void ClearTexture(RenderTexture texture) {
			var previous = RenderTexture.active;
			try {
				RenderTexture.active = texture;
				GL.Clear(true, true, Color.black);
			}
			finally { RenderTexture.active = previous; }
		}

		private static void ReleaseTexture(RenderTexture texture) {
			if (texture == null) return;
			texture.Release();
			DestroyObject(texture);
		}

		private static void DestroyObject(UnityEngine.Object value) {
			if (value == null) return;
			if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(value);
			else UnityEngine.Object.DestroyImmediate(value);
		}
	}

	internal sealed class LiveProgramGraph : IDisposable {
		private readonly string _outputNodeId;
		private readonly IReadOnlyList<ILiveProgramGraphNode> _nodes;

		internal LiveProgramGraph(GraphDefinition definition, IEnumerable<ILiveProgramGraphNode> nodes) {
			if (definition == null) throw new ArgumentNullException(nameof(definition));
			_outputNodeId = definition.OutputNodeId;
			_nodes = (nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray();
			if (_nodes.Count != definition.EvaluationOrder.Count) throw new ArgumentException("Every live Program graph node must be constructed.", nameof(nodes));
		}

		public void Evaluate(double deltaSeconds, BeatClockFrame bpmFrame) {
			foreach (var node in _nodes) node.Evaluate(deltaSeconds, bpmFrame);
		}

		public void SceneUpdate(double deltaSeconds) {
			foreach (var node in _nodes) node.SceneUpdate(deltaSeconds);
		}

		public void Render(RenderTexture destination, double graphTime, ulong frameNumber) {
			if (destination == null || !destination.IsCreated())
				throw new ArgumentException("Live Program graph rendering requires a created destination texture.");
			var outputs = new Dictionary<string, Texture>(StringComparer.Ordinal);
			foreach (var node in _nodes) {
				node.Render(outputs, graphTime, frameNumber);
				outputs.Add(node.Id, node.Target);
			}
			if (!outputs.TryGetValue(_outputNodeId, out var output)) throw new InvalidOperationException("The live Program graph did not produce its output.");
			Graphics.Blit(output, destination);
		}

		public bool TrySetParameter(string nodeId, string parameterId, ParameterValue value, out string rejectionReason) {
			var node = _nodes.FirstOrDefault(candidate => string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
			if (node == null) {
				rejectionReason = "The Program graph node is not available.";
				return false;
			}
			return node.TrySetParameter(parameterId, value, out rejectionReason);
		}

		public bool TryGetSceneParameter(string nodeId, string parameterId, out LiveSceneRoot root, out LiveParameterDefinition definition) {
			var node = _nodes.OfType<LiveProgramSceneGraphNode>().FirstOrDefault(candidate => string.Equals(candidate.Id, nodeId, StringComparison.Ordinal));
			if (node == null) {
				root = null;
				definition = default(LiveParameterDefinition);
				return false;
			}
			root = node.Root;
			return node.TryGetParameter(parameterId, out definition);
		}

		public void Dispose() {
			for (var index = _nodes.Count - 1; index >= 0; index--) _nodes[index].Dispose();
		}

	}

	/// <summary>Evaluates a Bootstrap-created graph and manages its live patch outputs.</summary>
	public sealed class LiveGraphRuntime : IDisposable {
		public const int ProgramWidth = 1920;
		public const int ProgramHeight = 1080;
		// Slot previews are sampled independently so the full-resolution Program path remains unaffected.
		public const int SlotPreviewWidth = 160;
		public const int SlotPreviewHeight = 90;
		public const int SlotPreviewFrameRate = 10;

		private const double SlotPreviewIntervalSeconds = 1d / SlotPreviewFrameRate;
		private static readonly LiveRenderSize ProgramRenderSize = new LiveRenderSize(ProgramWidth, ProgramHeight);
		private static readonly LiveRenderSize SlotPreviewRenderSize = new LiveRenderSize(SlotPreviewWidth, SlotPreviewHeight);

		private readonly LiveGraph _graph;
		private readonly Dictionary<string, PatchDefinition> _patchDefinitionsById;
		private readonly LiveBpmClock _bpmClock = new LiveBpmClock();
		private readonly List<LivePatch> _createdPatches = new List<LivePatch>();
		private readonly Dictionary<string, LiveSlotPreview> m_SlotPreviewPatches = new Dictionary<string, LiveSlotPreview>(StringComparer.Ordinal);
		private readonly Dictionary<string, RenderTexture> m_SlotPreviewTextures = new Dictionary<string, RenderTexture>(StringComparer.Ordinal);
		private readonly HashSet<string> m_SlotPreviewPatchFailures = new HashSet<string>(StringComparer.Ordinal);
		private readonly HashSet<string> m_SlotPreviewTextureFailures = new HashSet<string>(StringComparer.Ordinal);
		private readonly RenderTexture[] m_SlotPreviewFrames = new RenderTexture[LivePatchSlots.Capacity];
		private LivePatch _loadedPatch;
		private LivePatch _preloadedPatch;
		private ulong _frameNumber;
		private ulong m_SlotPreviewFrameNumber;
		private double _graphTime;
		private double _lastDeltaSeconds;
		private double m_SlotPreviewElapsedSeconds;
		private bool _disposed;

		public string LoadedPatchId => _loadedPatch?.Definition.Id ?? string.Empty;
		public string PreloadedPatchId => _preloadedPatch?.Definition.Id ?? string.Empty;
		public IReadOnlyList<PatchDefinition> Patches => _graph.PatchDefinitions;
		public LiveProgramFrame CurrentFrame { get; private set; }
		public LiveProgramFrames CurrentFrames { get; private set; }
		public IReadOnlyList<RenderTexture> SlotPreviewTextures => m_SlotPreviewFrames;
		public LiveParameterDefinition BpmDefinition => _bpmClock.Definition;
		public BeatClockFrame BpmFrame => _bpmClock.Frame;

		internal LiveGraphRuntime(LiveGraph graph) {
			_graph = graph ?? throw new ArgumentNullException(nameof(graph));
			_patchDefinitionsById = graph.PatchDefinitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);
			_loadedPatch = CreatePatch(graph.PatchDefinitions[0], ProgramRenderSize);
			_preloadedPatch = _loadedPatch;
			CurrentFrames = new LiveProgramFrames(_loadedPatch.Outputs.Select(output => new LiveProgramFrame(output.ProgramTexture, 0)));
			CurrentFrame = CurrentFrames.Primary;
		}

		public LiveParameterApplicationResult Apply(LiveParameterRequest request) {
			if (request.Kind == LiveParameterRequestKind.SetBpm)
				return _bpmClock.TrySetBpm(request.Value, out var bpmRejection) ? Accept(request) : Reject(request, bpmRejection);
			if (request.Kind == LiveParameterRequestKind.AlignBeat)
				return _bpmClock.TryAlignToNearestBeat(out var alignmentRejection) ? Accept(request) : Reject(request, alignmentRejection);
			if (!_patchDefinitionsById.TryGetValue(request.PatchId, out var definition)) return Reject(request, "The requested patch does not exist.");
			if (request.Kind == LiveParameterRequestKind.PreloadPatch) {
				if (_preloadedPatch?.Definition == definition) return Accept(request);
				var nextPreloadedPatch = CreatePatch(definition, ProgramRenderSize);
				if (_preloadedPatch != _loadedPatch) DisposePatch(_preloadedPatch);
				_preloadedPatch = nextPreloadedPatch;
				return Accept(request);
			}
			if (request.Kind == LiveParameterRequestKind.LoadPatch) {
				if (_preloadedPatch?.Definition != definition) return Reject(request, "The requested patch has not been preloaded.");
				LoadPreloadedPatch();
				return Accept(request);
			}
			if (request.Kind == LiveParameterRequestKind.LaunchPatch) {
				if (_preloadedPatch?.Definition != definition) {
					var nextPreloadedPatch = CreatePatch(definition, ProgramRenderSize);
					if (_preloadedPatch != _loadedPatch) DisposePatch(_preloadedPatch);
					_preloadedPatch = nextPreloadedPatch;
				}
				LoadPreloadedPatch();
				return Accept(request);
			}
			var patch = _preloadedPatch?.Definition == definition ? _preloadedPatch : _loadedPatch?.Definition == definition ? _loadedPatch : null;
			if (patch == null) return Reject(request, "The requested patch is not loaded.");
			if (request.Kind == LiveParameterRequestKind.TriggerFlash) {
				patch.TriggerFlash(_graphTime);
				return Accept(request);
			}
			return patch.TrySetParameter(request.ParameterId, request.ParameterValue, out var reason) ? Accept(request) : Reject(request, reason);
		}

		public void Evaluate(double deltaSeconds) {
			EnsureUsable();
			_lastDeltaSeconds = Math.Max(0d, deltaSeconds);
			_graphTime += _lastDeltaSeconds;
			_bpmClock.Advance(_lastDeltaSeconds);
			_loadedPatch.ApplyResolvedParameters(_bpmClock.Frame);
			foreach (var output in _loadedPatch.Outputs) output.Evaluate(_lastDeltaSeconds, _bpmClock.Frame);
		}

		public void SceneUpdate(double deltaSeconds) {
			EnsureUsable();
			foreach (var output in _loadedPatch.Outputs) output.SceneUpdate(Math.Max(0d, deltaSeconds));
		}

		public LiveProgramFrames Render() {
			EnsureUsable();
			var nextFrame = _frameNumber + 1;
			if (nextFrame == 0) nextFrame = 1;
			foreach (var output in _loadedPatch.Outputs) output.Render(_graphTime, nextFrame);
			_frameNumber = nextFrame;
			CurrentFrames = new LiveProgramFrames(_loadedPatch.Outputs.Select(output => new LiveProgramFrame(output.ProgramTexture, _frameNumber)));
			CurrentFrame = CurrentFrames.Primary;
			return CurrentFrames;
		}

		public IReadOnlyList<RenderTexture> RenderSlotPreviews(IReadOnlyList<LivePatchSlotReadModel> slots, double deltaSeconds) {
			EnsureUsable();
			var activePatchIds = CollectActiveSlotPatchIds(slots);
			ReconcileSlotPreviews(activePatchIds);
			if (activePatchIds.Count == 0) {
				m_SlotPreviewElapsedSeconds = 0d;
				m_SlotPreviewFrameNumber = 0;
			}
			else {
				m_SlotPreviewElapsedSeconds += Math.Max(0d, deltaSeconds);
				if (m_SlotPreviewFrameNumber == 0 || m_SlotPreviewElapsedSeconds >= SlotPreviewIntervalSeconds) {
					m_SlotPreviewElapsedSeconds %= SlotPreviewIntervalSeconds;
					var nextFrame = m_SlotPreviewFrameNumber + 1;
					if (nextFrame == 0) nextFrame = 1;
					RenderSlotPreviewPatches(nextFrame);
					RenderLoadedSlotPreview();
					m_SlotPreviewFrameNumber = nextFrame;
				}
			}

			Array.Clear(m_SlotPreviewFrames, 0, m_SlotPreviewFrames.Length);
			if (slots != null) {
				foreach (var slot in slots) {
					if (!LivePatchSlots.IsValidSlotIndex(slot.Index) || string.IsNullOrEmpty(slot.PatchId)) continue;
					if (slot.PatchId == LoadedPatchId)
						m_SlotPreviewTextures.TryGetValue(slot.PatchId, out m_SlotPreviewFrames[slot.Index]);
					else if (m_SlotPreviewPatches.TryGetValue(slot.PatchId, out var preview))
						m_SlotPreviewFrames[slot.Index] = preview.Texture;
				}
			}
			return m_SlotPreviewFrames;
		}

		public LiveParameterDefinition[] GetLoadedPatchParameterDefinitions() => _loadedPatch?.GetParameterDefinitions() ?? Array.Empty<LiveParameterDefinition>();

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			foreach (var texture in m_SlotPreviewTextures.Values) ReleaseTexture(texture);
			m_SlotPreviewTextures.Clear();
			m_SlotPreviewPatches.Clear();
			m_SlotPreviewPatchFailures.Clear();
			m_SlotPreviewTextureFailures.Clear();
			for (var index = _createdPatches.Count - 1; index >= 0; index--) _createdPatches[index].Dispose();
			_createdPatches.Clear();
			_graph.Dispose();
		}

		private LivePatch CreatePatch(PatchDefinition definition, LiveRenderSize renderSize) {
			var patch = new LivePatch(definition, _graph.CreateOutput, renderSize);
			try {
				patch.ApplyResolvedParameters(_bpmClock.Frame);
				_createdPatches.Add(patch);
				return patch;
			}
			catch {
				patch.Dispose();
				throw;
			}
		}

		private void DisposePatch(LivePatch patch) {
			if (patch == null || !_createdPatches.Remove(patch)) return;
			patch.Dispose();
		}

		private HashSet<string> CollectActiveSlotPatchIds(IReadOnlyList<LivePatchSlotReadModel> slots) {
			var activePatchIds = new HashSet<string>(StringComparer.Ordinal);
			if (slots == null) return activePatchIds;
			foreach (var slot in slots)
				if (!string.IsNullOrEmpty(slot.PatchId)) activePatchIds.Add(slot.PatchId);
			return activePatchIds;
		}

		private void ReconcileSlotPreviews(ISet<string> activePatchIds) {
			foreach (var patchId in m_SlotPreviewPatches.Keys.Where(patchId => !activePatchIds.Contains(patchId) || patchId == LoadedPatchId).ToArray())
				DisposeSlotPreviewPatch(patchId);
			foreach (var patchId in m_SlotPreviewTextures.Keys.Where(patchId => !activePatchIds.Contains(patchId) || patchId != LoadedPatchId).ToArray())
				DisposeSlotPreviewTexture(patchId);
			foreach (var patchId in m_SlotPreviewPatchFailures.Where(patchId => !activePatchIds.Contains(patchId)).ToArray())
				m_SlotPreviewPatchFailures.Remove(patchId);
			foreach (var patchId in m_SlotPreviewTextureFailures.Where(patchId => !activePatchIds.Contains(patchId)).ToArray())
				m_SlotPreviewTextureFailures.Remove(patchId);

			foreach (var patchId in activePatchIds) {
				if (!_patchDefinitionsById.ContainsKey(patchId)) {
					m_SlotPreviewPatchFailures.Add(patchId);
					continue;
				}
				if (patchId == LoadedPatchId) {
					m_SlotPreviewPatchFailures.Remove(patchId);
					if (m_SlotPreviewTextures.ContainsKey(patchId) || m_SlotPreviewTextureFailures.Contains(patchId)) continue;
					try {
						m_SlotPreviewTextures.Add(patchId, CreateSlotPreviewTexture(patchId));
					}
					catch {
						m_SlotPreviewTextureFailures.Add(patchId);
					}
					continue;
				}

				m_SlotPreviewTextureFailures.Remove(patchId);
				if (m_SlotPreviewPatches.ContainsKey(patchId) || m_SlotPreviewPatchFailures.Contains(patchId)) continue;
				try {
					var previewPatch = CreatePatch(_patchDefinitionsById[patchId], SlotPreviewRenderSize);
					m_SlotPreviewPatches.Add(patchId, new LiveSlotPreview(previewPatch));
				}
				catch {
					m_SlotPreviewPatchFailures.Add(patchId);
				}
			}
		}

		private void RenderSlotPreviewPatches(ulong frameNumber) {
			foreach (var pair in m_SlotPreviewPatches.ToArray()) {
				try {
					pair.Value.Render(_graphTime, _bpmClock.Frame, frameNumber);
				}
				catch {
					DisposeSlotPreviewPatch(pair.Key);
					m_SlotPreviewPatchFailures.Add(pair.Key);
				}
			}
		}

		private void RenderLoadedSlotPreview() {
			if (!m_SlotPreviewTextures.TryGetValue(LoadedPatchId, out var target)) return;
			try {
				var source = _loadedPatch.Outputs.Count == 0 ? null : _loadedPatch.Outputs[0].ProgramTexture;
				if (source == null || !source.IsCreated()) ClearTexture(target);
				else Graphics.Blit(source, target);
			}
			catch {
				DisposeSlotPreviewTexture(LoadedPatchId);
				m_SlotPreviewTextureFailures.Add(LoadedPatchId);
			}
		}

		private void DisposeSlotPreviewPatch(string patchId) {
			if (!m_SlotPreviewPatches.TryGetValue(patchId, out var preview)) return;
			m_SlotPreviewPatches.Remove(patchId);
			DisposePatch(preview.Patch);
		}

		private void DisposeSlotPreviewTexture(string patchId) {
			if (!m_SlotPreviewTextures.TryGetValue(patchId, out var texture)) return;
			m_SlotPreviewTextures.Remove(patchId);
			ReleaseTexture(texture);
		}

		private static RenderTexture CreateSlotPreviewTexture(string patchId) {
			var texture = new RenderTexture(SlotPreviewWidth, SlotPreviewHeight, 0, RenderTextureFormat.ARGB32) {
				name = "ShitDesigner.Main.PatchSlotPreview." + patchId,
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp,
				useMipMap = false,
				autoGenerateMips = false
			};
			if (!texture.Create()) {
				ReleaseTexture(texture);
				throw new InvalidOperationException("A patch slot preview texture could not be created.");
			}
			ClearTexture(texture);
			return texture;
		}

		private static void ClearTexture(RenderTexture texture) {
			var previous = RenderTexture.active;
			try {
				RenderTexture.active = texture;
				GL.Clear(true, true, Color.black);
			}
			finally {
				RenderTexture.active = previous;
			}
		}

		private static void ReleaseTexture(RenderTexture texture) {
			if (texture == null) return;
			texture.Release();
			if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(texture);
			else UnityEngine.Object.DestroyImmediate(texture);
		}

		private void LoadPreloadedPatch() {
			var previousLoadedPatch = _loadedPatch;
			_loadedPatch = _preloadedPatch;
			if (previousLoadedPatch != _loadedPatch) DisposePatch(previousLoadedPatch);
		}

		private void EnsureUsable() {
			if (_disposed) throw new ObjectDisposedException(nameof(LiveGraphRuntime));
			if (_loadedPatch == null) throw new InvalidOperationException("A patch is not loaded.");
		}

		private static LiveParameterApplicationResult Accept(LiveParameterRequest request) => new LiveParameterApplicationResult(request.SequenceNumber, true, string.Empty);
		private static LiveParameterApplicationResult Reject(LiveParameterRequest request, string reason) => new LiveParameterApplicationResult(request.SequenceNumber, false, reason);
	}

	internal sealed class LiveSlotPreview {
		private readonly LivePatch m_Patch;
		private double m_LastGraphTime;
		private bool m_HasRendered;

		public LivePatch Patch => m_Patch;
		public RenderTexture Texture => m_Patch.Outputs.Count == 0 ? null : m_Patch.Outputs[0].ProgramTexture;

		public LiveSlotPreview(LivePatch patch) {
			m_Patch = patch ?? throw new ArgumentNullException(nameof(patch));
		}

		public void Render(double graphTime, BeatClockFrame bpmFrame, ulong frameNumber) {
			var deltaSeconds = m_HasRendered ? Math.Max(0d, graphTime - m_LastGraphTime) : Math.Max(0d, graphTime);
			m_Patch.ApplyResolvedParameters(bpmFrame);
			foreach (var output in m_Patch.Outputs) {
				output.Evaluate(deltaSeconds, bpmFrame);
				output.SceneUpdate(deltaSeconds);
				output.Render(graphTime, frameNumber);
			}
			m_LastGraphTime = graphTime;
			m_HasRendered = true;
		}
	}

	internal sealed class LivePatch : IDisposable {
		private readonly Dictionary<string, ILivePublishedParameter> _parameters;
		public PatchDefinition Definition { get; }
		public IReadOnlyList<LiveProgramOutput> Outputs { get; }

		public LivePatch(PatchDefinition definition,
			Func<PatchDefinition, PatchFlashDefinition, LiveRenderSize, LiveProgramOutput> createOutput,
			LiveRenderSize renderSize) {
			Definition = definition ?? throw new ArgumentNullException(nameof(definition));
			if (createOutput == null) throw new ArgumentNullException(nameof(createOutput));
			LiveProgramOutput output = null;
			try {
				output = createOutput(definition, definition.Flash, renderSize);
				Outputs = new[] { output };
				_parameters = definition.Parameters.ToDictionary(parameter => parameter.Id, parameter => {
					var graphNode = definition.ProgramGraph.Nodes.FirstOrDefault(node => string.Equals(node.Id, parameter.NodeId, StringComparison.Ordinal));
					if (graphNode == null) throw new InvalidOperationException("A published parameter references an unknown patch graph node: " + parameter.Id + ".");
					if (graphNode.IsSceneNode) {
						if (!output.TryGetSceneParameter(parameter.NodeId, parameter.ParameterId, out var root, out var source)
							|| string.IsNullOrWhiteSpace(source.Id))
							throw new InvalidOperationException("A published parameter is not provided by its scene graph node: " + parameter.Id + ".");
						return (ILivePublishedParameter)new LivePublishedParameter(parameter, root, source);
					}
					var graphSource = graphNode.FindParameter(parameter.ParameterId);
					if (graphSource == null || !PatchGraphParameter.IsLiveControllable(graphSource.Type))
						throw new InvalidOperationException("A published graph parameter must reference a configured parameter supported by the live renderer: " + parameter.Id + ".");
					return (ILivePublishedParameter)new LivePublishedGraphParameter(parameter, new[] { output }, graphSource.Value);
				}, StringComparer.Ordinal);
			}
			catch {
				output?.Dispose();
				throw;
			}
		}

		public LiveParameterDefinition[] GetParameterDefinitions() => _parameters.Values.Select(parameter => parameter.ToDefinition()).ToArray();

		public bool TrySetParameter(string id, ParameterValue value, out string rejectionReason) {
			if (!_parameters.TryGetValue(id, out var parameter)) {
				rejectionReason = "The parameter is not published by this patch.";
				return false;
			}
			return parameter.TrySetParameter(value, out rejectionReason);
		}

		public void ApplyResolvedParameters(BeatClockFrame frame) {
			foreach (var parameter in _parameters.Values)
				if (!parameter.TryApplyResolvedValue(frame, out var rejectionReason))
					throw new InvalidOperationException("The resolved patch parameter could not be applied: " + rejectionReason);
		}

		public void TriggerFlash(double graphTime) {
			foreach (var output in Outputs) output.TriggerFlash(graphTime);
		}

		public void Dispose() {
			for (var index = Outputs.Count - 1; index >= 0; index--) Outputs[index].Dispose();
		}
	}

	internal interface ILivePublishedParameter {
		LiveParameterDefinition ToDefinition();
		bool TrySetParameter(ParameterValue value, out string rejectionReason);
		bool TryApplyResolvedValue(BeatClockFrame frame, out string rejectionReason);
	}

	internal sealed class LivePublishedParameter : ILivePublishedParameter {
		private readonly PatchParameter _definition;
		private readonly bool _isTriggerParameter;
		private float _baseValue;
		private bool _isDirty;
		private bool _hasResolvedValue;
		private float _lastResolvedValue;
		public LiveSceneRoot Root { get; }
		public LiveParameterDefinition Source { get; }

		public LivePublishedParameter(PatchParameter definition, LiveSceneRoot root, LiveParameterDefinition source) {
			_definition = definition ?? throw new ArgumentNullException(nameof(definition));
			Root = root ?? throw new ArgumentNullException(nameof(root));
			Source = source;
			_isTriggerParameter = Root.IsTriggerParameter(source.Id);
			_baseValue = source.Value;
			_lastResolvedValue = source.Value;
			_hasResolvedValue = true;
		}

		public LiveParameterDefinition ToDefinition() {
			return new LiveParameterDefinition(_definition.Id, _definition.DisplayName, Source.Minimum, Source.Maximum, _baseValue);
		}

		public bool TrySetParameter(ParameterValue value, out string rejectionReason) {
			if (value.Type != ParameterType.Float) {
				rejectionReason = "The published scene parameter requires a float value.";
				return false;
			}
			_baseValue = value.AsFloat();
			_isDirty = true;
			rejectionReason = string.Empty;
			return true;
		}

		public bool TryApplyResolvedValue(BeatClockFrame frame, out string rejectionReason) {
			var resolvedValue = _definition.BeatModulation?.Resolve(_baseValue, frame) ?? _baseValue;
			// A Scene parameter can represent a one-shot action, so only an explicit input may re-dispatch an equal value.
			var triggerActivated = false;
			if (!_isDirty && _isTriggerParameter) {
				var wasActive = _hasResolvedValue && IsTriggerActive(_lastResolvedValue);
				_lastResolvedValue = resolvedValue;
				_hasResolvedValue = true;
				if (wasActive || !IsTriggerActive(resolvedValue)) {
					rejectionReason = string.Empty;
					return true;
				}
				triggerActivated = true;
			}
			if (!triggerActivated && !_isDirty && _hasResolvedValue && Mathf.Approximately(_lastResolvedValue, resolvedValue)) {
				rejectionReason = string.Empty;
				return true;
			}
			if (!Root.TrySetParameter(Source.Id, resolvedValue, out rejectionReason)) return false;
			_lastResolvedValue = resolvedValue;
			_hasResolvedValue = true;
			_isDirty = false;
			return true;
		}

		private bool IsTriggerActive(float value) => value > Source.Minimum + Mathf.Epsilon;
	}

	internal sealed class LivePublishedGraphParameter : ILivePublishedParameter {
		private readonly PatchParameter _definition;
		private readonly IReadOnlyCollection<LiveProgramOutput> _outputs;
		private ParameterValue _baseValue;
		private bool _isDirty;
		private bool _hasResolvedValue;
		private ParameterValue _lastResolvedValue;

		public LivePublishedGraphParameter(PatchParameter definition, IReadOnlyCollection<LiveProgramOutput> outputs, ParameterValue value) {
			_definition = definition ?? throw new ArgumentNullException(nameof(definition));
			_outputs = outputs ?? throw new ArgumentNullException(nameof(outputs));
			_baseValue = value;
			_lastResolvedValue = value;
			_hasResolvedValue = true;
		}

		public LiveParameterDefinition ToDefinition()
			=> new LiveParameterDefinition(_definition.Id, _definition.DisplayName, _baseValue);

		public bool TrySetParameter(ParameterValue value, out string rejectionReason) {
			if (value.Type != _baseValue.Type) {
				rejectionReason = "The published graph parameter type does not match.";
				return false;
			}
			_baseValue = value;
			_isDirty = true;
			rejectionReason = string.Empty;
			return true;
		}

		public bool TryApplyResolvedValue(BeatClockFrame frame, out string rejectionReason) {
			var resolved = _baseValue;
			if (_definition.BeatModulation != null && _definition.BeatModulation.IsEnabled) {
				if (resolved.Type != ParameterType.Float) {
					rejectionReason = "Beat-modulated graph parameters require a float value.";
					return false;
				}
				resolved = ParameterValue.FromFloat(_definition.BeatModulation.Resolve(resolved.AsFloat(), frame));
			}
			if (!_isDirty && _hasResolvedValue && _lastResolvedValue == resolved) {
				rejectionReason = string.Empty;
				return true;
			}
			foreach (var output in _outputs)
				if (!output.TrySetGraphParameter(_definition.NodeId, _definition.ParameterId, resolved, out rejectionReason)) return false;
			_lastResolvedValue = resolved;
			_hasResolvedValue = true;
			_isDirty = false;
			rejectionReason = string.Empty;
			return true;
		}
	}
}
