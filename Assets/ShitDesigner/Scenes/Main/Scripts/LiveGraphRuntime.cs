using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
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
		private readonly Func<PatchDefinition, Scene3DDefinition, PatchFlashDefinition, LiveProgramOutput> _createOutput;
		public IReadOnlyList<PatchDefinition> PatchDefinitions { get; }

		public LiveGraph(SceneIsolationManager sceneManager, RenderTexturePool renderPool, IEnumerable<PatchDefinition> patchDefinitions,
			Func<PatchDefinition, Scene3DDefinition, PatchFlashDefinition, LiveProgramOutput> createOutput) {
			_sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
			_renderPool = renderPool ?? throw new ArgumentNullException(nameof(renderPool));
			_createOutput = createOutput ?? throw new ArgumentNullException(nameof(createOutput));
			PatchDefinitions = (patchDefinitions ?? throw new ArgumentNullException(nameof(patchDefinitions))).ToArray();
			if (PatchDefinitions.Count == 0) throw new ArgumentException("A live graph requires patches.");
		}

		public LiveProgramOutput CreateOutput(PatchDefinition patch, Scene3DDefinition definition, PatchFlashDefinition flashPatch) => _createOutput(patch, definition, flashPatch);

		public void Dispose() {
			_sceneManager.Dispose();
			_renderPool.Dispose();
		}
	}

	internal sealed class GraphDefinition {
		public string SourceNodeId { get; }
		public string OutputNodeId { get; }
		public IReadOnlyList<NodeDefinition> Nodes { get; }
		public IReadOnlyList<NodeConnection> Connections { get; }
		public IReadOnlyList<NodeDefinition> EvaluationOrder { get; }

		public GraphDefinition(string sourceNodeId, string outputNodeId,
			IEnumerable<NodeDefinition> nodes, IEnumerable<NodeConnection> connections) {
			SourceNodeId = RequireId(sourceNodeId, nameof(sourceNodeId));
			OutputNodeId = RequireId(outputNodeId, nameof(outputNodeId));
			Nodes = (nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray();
			Connections = (connections ?? throw new ArgumentNullException(nameof(connections))).ToArray();
			if (Nodes.Count == 0) throw new ArgumentException("A live Program graph requires at least one shader node.", nameof(nodes));
			if (Nodes.Any(node => node == null) || Nodes.Select(node => node.Id).Distinct(StringComparer.Ordinal).Count() != Nodes.Count)
				throw new ArgumentException("Live Program graph node IDs must be unique.", nameof(nodes));
			if (!Nodes.Any(node => node.Id == OutputNodeId)) throw new ArgumentException("The live Program graph output must be a shader node.", nameof(outputNodeId));
			if (Nodes.Any(node => node.Id == SourceNodeId)) throw new ArgumentException("The live Program graph source cannot be a shader node.", nameof(sourceNodeId));
			if (Connections.Any(connection => connection == null)) throw new ArgumentException("Live Program graph connections cannot be null.", nameof(connections));
			if (Connections.Any(connection => !Nodes.Any(node => node.Id == connection.TargetNodeId)))
				throw new ArgumentException("Every live Program graph connection target must be a shader node.", nameof(connections));
			if (Connections.Any(connection => connection.SourceNodeId != SourceNodeId && !Nodes.Any(node => node.Id == connection.SourceNodeId)))
				throw new ArgumentException("Every live Program graph connection source must exist.", nameof(connections));
			if (Connections.GroupBy(connection => new { connection.TargetNodeId, connection.TargetPortId }).Any(group => group.Count() > 1))
				throw new ArgumentException("A live Program graph input port can have only one connection.", nameof(connections));
			EvaluationOrder = BuildEvaluationOrder();
		}

		private IReadOnlyList<NodeDefinition> BuildEvaluationOrder() {
			var remaining = Nodes.ToList();
			var resolved = new HashSet<string>(StringComparer.Ordinal) { SourceNodeId };
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
		public Scene3DDefinition Definition { get; }
		public SceneNodeRuntime Runtime { get; }
		public LiveSceneRoot Root { get; }
		public RenderTexture ProgramTexture { get; }
		public RenderTexture RenderTexture { get; }
		private readonly RenderTexture _shaderGraphTexture;
		private readonly LiveProgramShaderGraph _programGraph;
		private readonly LiveProgramFlash _flash;
		private readonly PatchFlashDefinition _flashPatch;

		public LiveProgramOutput(Scene3DDefinition definition, SceneNodeRuntime runtime, LiveSceneRoot root,
			RenderTexture programTexture, RenderTexture renderTexture, RenderTexture shaderGraphTexture,
			LiveProgramShaderGraph programGraph, LiveProgramFlash flash, PatchFlashDefinition flashPatch) {
			Definition = definition ?? throw new ArgumentNullException(nameof(definition));
			Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
			Root = root ?? throw new ArgumentNullException(nameof(root));
			ProgramTexture = programTexture ?? throw new ArgumentNullException(nameof(programTexture));
			RenderTexture = renderTexture ?? throw new ArgumentNullException(nameof(renderTexture));
			_shaderGraphTexture = shaderGraphTexture ?? throw new ArgumentNullException(nameof(shaderGraphTexture));
			_programGraph = programGraph ?? throw new ArgumentNullException(nameof(programGraph));
			_flash = flash ?? throw new ArgumentNullException(nameof(flash));
			_flashPatch = flashPatch;
		}

		public void Render(double graphTime, double deltaSeconds, ulong frameNumber) {
			var result = Runtime.Render(RenderTexture, LiveGraphRuntime.ProgramWidth, LiveGraphRuntime.ProgramHeight, frameNumber);
			if (result.IsFailure || result.Value == null || !result.Value.Rendered)
				throw new InvalidOperationException(result.IsFailure ? result.Error.Message : "A live ProgramOutput did not render.");
			_programGraph.Render(RenderTexture, _shaderGraphTexture, graphTime, frameNumber);
			_flash.Render(_shaderGraphTexture, ProgramTexture, graphTime);
		}

		public void TriggerFlash(double graphTime) {
			_flash.Trigger(graphTime);
			if (_flashPatch?.Image == null) return;
			_flash.TriggerAsset(graphTime, _flashPatch.Image, _flashPatch.DurationSeconds);
		}

		public bool TrySetGraphParameter(string nodeId, string parameterId, ParameterValue value, out string rejectionReason)
			=> _programGraph.TrySetParameter(nodeId, parameterId, value, out rejectionReason);

		public void Dispose() {
			_flash.Dispose();
			_programGraph.Dispose();
			ReleaseTexture(_shaderGraphTexture);
			Runtime.Dispose();
			ReleaseTexture(ProgramTexture);
			ReleaseTexture(RenderTexture);
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
		void Render(IReadOnlyDictionary<string, Texture> outputs, double graphTime, ulong frameNumber);
		bool TrySetParameter(string parameterId, ParameterValue value, out string rejectionReason);
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
				_player.source = VideoSource.VideoClip;
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

	internal sealed class LiveProgramShaderGraph : IDisposable {
		private readonly string _sourceNodeId;
		private readonly string _outputNodeId;
		private readonly IReadOnlyList<ILiveProgramGraphNode> _nodes;

		internal LiveProgramShaderGraph(GraphDefinition definition, IEnumerable<ILiveProgramGraphNode> nodes) {
			if (definition == null) throw new ArgumentNullException(nameof(definition));
			_sourceNodeId = definition.SourceNodeId;
			_outputNodeId = definition.OutputNodeId;
			_nodes = (nodes ?? throw new ArgumentNullException(nameof(nodes))).ToArray();
			if (_nodes.Count != definition.EvaluationOrder.Count) throw new ArgumentException("Every live Program graph node must be constructed.", nameof(nodes));
		}

		public void Render(RenderTexture source, RenderTexture destination, double graphTime, ulong frameNumber) {
			if (source == null || destination == null || !source.IsCreated() || !destination.IsCreated())
				throw new ArgumentException("Live Program graph rendering requires created source and destination textures.");
			var outputs = new Dictionary<string, Texture>(StringComparer.Ordinal) { [_sourceNodeId] = source };
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

		public void Dispose() {
			for (var index = _nodes.Count - 1; index >= 0; index--) _nodes[index].Dispose();
		}

	}

	/// <summary>Evaluates a Bootstrap-created graph without constructing nodes or rendering resources.</summary>
	public sealed class LiveGraphRuntime : IDisposable {
		public const int ProgramWidth = 1920;
		public const int ProgramHeight = 1080;

		private readonly LiveGraph _graph;
		private readonly Dictionary<string, PatchDefinition> _patchDefinitionsById;
		private readonly LiveBpmClock _bpmClock = new LiveBpmClock();
		private readonly List<LivePatch> _createdPatches = new List<LivePatch>();
		private LivePatch _loadedPatch;
		private LivePatch _preloadedPatch;
		private ulong _frameNumber;
		private double _graphTime;
		private double _lastDeltaSeconds;
		private bool _disposed;

		public string LoadedPatchId => _loadedPatch?.Definition.Id ?? string.Empty;
		public string PreloadedPatchId => _preloadedPatch?.Definition.Id ?? string.Empty;
		public IReadOnlyList<PatchDefinition> Patches => _graph.PatchDefinitions;
		public LiveProgramFrame CurrentFrame { get; private set; }
		public LiveProgramFrames CurrentFrames { get; private set; }
		public LiveParameterDefinition BpmDefinition => _bpmClock.Definition;

		internal LiveGraphRuntime(LiveGraph graph) {
			_graph = graph ?? throw new ArgumentNullException(nameof(graph));
			_patchDefinitionsById = graph.PatchDefinitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);
			_loadedPatch = CreatePatch(graph.PatchDefinitions[0]);
			_preloadedPatch = _loadedPatch;
			CurrentFrames = new LiveProgramFrames(_loadedPatch.Outputs.Select(output => new LiveProgramFrame(output.ProgramTexture, 0)));
			CurrentFrame = CurrentFrames.Primary;
		}

		public LiveParameterApplicationResult Apply(LiveParameterRequest request) {
			if (request.Kind == LiveParameterRequestKind.SetBpm)
				return _bpmClock.TrySetBpm(request.Value, out var bpmRejection) ? Accept(request) : Reject(request, bpmRejection);
			if (!_patchDefinitionsById.TryGetValue(request.PatchId, out var definition)) return Reject(request, "The requested patch does not exist.");
			if (request.Kind == LiveParameterRequestKind.PreloadPatch) {
				if (_preloadedPatch?.Definition == definition) return Accept(request);
				var nextPreloadedPatch = CreatePatch(definition);
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
					var nextPreloadedPatch = CreatePatch(definition);
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
			foreach (var scene in _loadedPatch.Outputs) {
				var result = scene.Runtime.AdvanceGraphClock(_lastDeltaSeconds * scene.Root.TimeScale);
				if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
				var bpmResult = scene.Runtime.ApplyBpmClock(_bpmClock.Frame);
				if (bpmResult.IsFailure) throw new InvalidOperationException(bpmResult.Error.Message);
			}
		}

		public void SceneUpdate(double deltaSeconds) {
			EnsureUsable();
			foreach (var scene in _loadedPatch.Outputs) {
				var result = scene.Runtime.AdvancePhysics(deltaSeconds * scene.Root.TimeScale);
				if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
			}
		}

		public LiveProgramFrames Render() {
			EnsureUsable();
			var nextFrame = _frameNumber + 1;
			if (nextFrame == 0) nextFrame = 1;
			foreach (var scene in _loadedPatch.Outputs) scene.Render(_graphTime, _lastDeltaSeconds, nextFrame);
			_frameNumber = nextFrame;
			CurrentFrames = new LiveProgramFrames(_loadedPatch.Outputs.Select(output => new LiveProgramFrame(output.ProgramTexture, _frameNumber)));
			CurrentFrame = CurrentFrames.Primary;
			return CurrentFrames;
		}

		public LiveParameterDefinition[] GetLoadedPatchParameterDefinitions() => _loadedPatch?.GetParameterDefinitions() ?? Array.Empty<LiveParameterDefinition>();

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			for (var index = _createdPatches.Count - 1; index >= 0; index--) _createdPatches[index].Dispose();
			_createdPatches.Clear();
			_graph.Dispose();
		}

		private LivePatch CreatePatch(PatchDefinition definition) {
			var patch = new LivePatch(definition, _graph.CreateOutput);
			patch.ApplyResolvedParameters(_bpmClock.Frame);
			_createdPatches.Add(patch);
			return patch;
		}

		private void DisposePatch(LivePatch patch) {
			if (patch == null || !_createdPatches.Remove(patch)) return;
			patch.Dispose();
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

	internal sealed class LivePatch : IDisposable {
		private readonly Dictionary<string, ILivePublishedParameter> _parameters;
		public PatchDefinition Definition { get; }
		public IReadOnlyList<LiveProgramOutput> Outputs { get; }

		public LivePatch(PatchDefinition definition, Func<PatchDefinition, Scene3DDefinition, PatchFlashDefinition, LiveProgramOutput> createOutput) {
			Definition = definition ?? throw new ArgumentNullException(nameof(definition));
			if (createOutput == null) throw new ArgumentNullException(nameof(createOutput));
			var outputsByNodeId = new Dictionary<string, LiveProgramOutput>(StringComparer.Ordinal);
			try {
				foreach (var node in definition.Nodes) outputsByNodeId.Add(node.Id, createOutput(definition, node, definition.Flash));
				Outputs = outputsByNodeId.Values.ToArray();
				_parameters = definition.Parameters.ToDictionary(parameter => parameter.Id, parameter => {
					if (parameter.Source == PatchParameterSource.ProgramGraphNode) {
						var graphNode = definition.ProgramGraph.Nodes.FirstOrDefault(node => string.Equals(node.Id, parameter.NodeId, StringComparison.Ordinal));
						var source = graphNode?.FindParameter(parameter.ParameterId);
						if (source == null || !PatchGraphParameter.IsLiveControllable(source.Type))
							throw new InvalidOperationException("A published graph parameter must reference a configured parameter supported by the live renderer: " + parameter.Id + ".");
						return (ILivePublishedParameter)new LivePublishedGraphParameter(parameter, outputsByNodeId.Values, source.Value);
					}
					var output = outputsByNodeId[parameter.NodeId];
					var sceneSource = output.Root.GetParameterDefinitions().FirstOrDefault(candidate => candidate.Id == parameter.ParameterId);
					if (string.IsNullOrWhiteSpace(sceneSource.Id)) throw new InvalidOperationException("A published scene parameter is not provided by its Unity scene node: " + parameter.Id + ".");
					return (ILivePublishedParameter)new LivePublishedParameter(parameter, output.Root, sceneSource);
				}, StringComparer.Ordinal);
			}
			catch {
				for (var index = outputsByNodeId.Count - 1; index >= 0; index--) outputsByNodeId.Values.ElementAt(index).Dispose();
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
		private float _baseValue;
		private bool _hasResolvedValue;
		private float _lastResolvedValue;
		public LiveSceneRoot Root { get; }
		public LiveParameterDefinition Source { get; }

		public LivePublishedParameter(PatchParameter definition, LiveSceneRoot root, LiveParameterDefinition source) {
			_definition = definition ?? throw new ArgumentNullException(nameof(definition));
			Root = root ?? throw new ArgumentNullException(nameof(root));
			Source = source;
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
			rejectionReason = string.Empty;
			return true;
		}

		public bool TryApplyResolvedValue(BeatClockFrame frame, out string rejectionReason) {
			var resolvedValue = _definition.BeatModulation?.Resolve(_baseValue, frame) ?? _baseValue;
			// A Scene parameter can represent a one-shot action, so equal frame values must not be dispatched again.
			if (_hasResolvedValue && Mathf.Approximately(_lastResolvedValue, resolvedValue)) {
				rejectionReason = string.Empty;
				return true;
			}
			if (!Root.TrySetParameter(Source.Id, resolvedValue, out rejectionReason)) return false;
			_lastResolvedValue = resolvedValue;
			_hasResolvedValue = true;
			return true;
		}
	}

	internal sealed class LivePublishedGraphParameter : ILivePublishedParameter {
		private readonly PatchParameter _definition;
		private readonly IReadOnlyCollection<LiveProgramOutput> _outputs;
		private ParameterValue _baseValue;
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
			if (_hasResolvedValue && _lastResolvedValue == resolved) {
				rejectionReason = string.Empty;
				return true;
			}
			foreach (var output in _outputs)
				if (!output.TrySetGraphParameter(_definition.NodeId, _definition.ParameterId, resolved, out rejectionReason)) return false;
			_lastResolvedValue = resolved;
			_hasResolvedValue = true;
			rejectionReason = string.Empty;
			return true;
		}
	}
}
