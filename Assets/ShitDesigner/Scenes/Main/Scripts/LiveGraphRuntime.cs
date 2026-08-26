using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Nodes;
using ShitDesigner.Rendering;
using ShitDesigner.Runtime;
using ShitDesigner.Scene;
using UnityEngine;

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
		public IReadOnlyList<LiveProgramOutput> ProgramOutputs { get; }

		public LiveGraph(SceneIsolationManager sceneManager, RenderTexturePool renderPool, IEnumerable<LiveProgramOutput> programOutputs) {
			_sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
			_renderPool = renderPool ?? throw new ArgumentNullException(nameof(renderPool));
			ProgramOutputs = (programOutputs ?? throw new ArgumentNullException(nameof(programOutputs))).ToArray();
			if (ProgramOutputs.Count == 0) throw new ArgumentException("A live graph requires at least one Program output.", nameof(programOutputs));
		}

		public void Dispose() {
			for (var index = ProgramOutputs.Count - 1; index >= 0; index--) ProgramOutputs[index].Dispose();
			_sceneManager.Dispose();
			_renderPool.Dispose();
		}
	}

	internal sealed class LiveProgramGraphDefinition {
		public string SourceNodeId { get; }
		public string OutputNodeId { get; }
		public IReadOnlyList<LiveProgramGraphNodeDefinition> Nodes { get; }
		public IReadOnlyList<LiveProgramGraphConnection> Connections { get; }
		public IReadOnlyList<LiveProgramGraphNodeDefinition> EvaluationOrder { get; }

		public LiveProgramGraphDefinition(string sourceNodeId, string outputNodeId,
			IEnumerable<LiveProgramGraphNodeDefinition> nodes, IEnumerable<LiveProgramGraphConnection> connections) {
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

		private IReadOnlyList<LiveProgramGraphNodeDefinition> BuildEvaluationOrder() {
			var remaining = Nodes.ToList();
			var resolved = new HashSet<string>(StringComparer.Ordinal) { SourceNodeId };
			var ordered = new List<LiveProgramGraphNodeDefinition>(Nodes.Count);
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

	internal sealed class LiveProgramGraphNodeDefinition {
		public string Id { get; }
		public NodeTypeId TypeId { get; }

		public LiveProgramGraphNodeDefinition(string id, string typeId) {
			if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A live Program graph node ID is required.", nameof(id));
			Id = id.Trim();
			TypeId = new NodeTypeId(typeId);
		}
	}

	internal sealed class LiveProgramGraphConnection {
		public string SourceNodeId { get; }
		public string TargetNodeId { get; }
		public PortId TargetPortId { get; }

		public LiveProgramGraphConnection(string sourceNodeId, string targetNodeId, string targetPortId) {
			if (string.IsNullOrWhiteSpace(sourceNodeId) || string.IsNullOrWhiteSpace(targetNodeId))
				throw new ArgumentException("Live Program graph connection node IDs are required.");
			SourceNodeId = sourceNodeId.Trim();
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
		private readonly LiveProgramShaderGraph _programGraph;

		public LiveProgramOutput(Scene3DDefinition definition, SceneNodeRuntime runtime, LiveSceneRoot root,
			RenderTexture programTexture, RenderTexture renderTexture, LiveProgramShaderGraph programGraph) {
			Definition = definition ?? throw new ArgumentNullException(nameof(definition));
			Runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
			Root = root ?? throw new ArgumentNullException(nameof(root));
			ProgramTexture = programTexture ?? throw new ArgumentNullException(nameof(programTexture));
			RenderTexture = renderTexture ?? throw new ArgumentNullException(nameof(renderTexture));
			_programGraph = programGraph ?? throw new ArgumentNullException(nameof(programGraph));
		}

		public void Render(double graphTime, double deltaSeconds, ulong frameNumber) {
			var result = Runtime.Render(RenderTexture, LiveGraphRuntime.ProgramWidth, LiveGraphRuntime.ProgramHeight, frameNumber);
			if (result.IsFailure || result.Value == null || !result.Value.Rendered)
				throw new InvalidOperationException(result.IsFailure ? result.Error.Message : "A live ProgramOutput did not render.");
			_programGraph.Render(RenderTexture, ProgramTexture, graphTime, frameNumber);
		}

		public void Dispose() {
			_programGraph.Dispose();
			Runtime.Dispose();
			ReleaseTexture(ProgramTexture);
			ReleaseTexture(RenderTexture);
		}

		private static void ReleaseTexture(RenderTexture texture) {
			if (texture == null) return;
			texture.Release();
			if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
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

	internal sealed class LiveProgramShaderGraphNode : IDisposable {
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

		public void Dispose() {
			_runtime.Dispose();
			ReleaseTexture(Target);
		}

		private static void ReleaseTexture(RenderTexture texture) {
			if (texture == null) return;
			texture.Release();
			if (Application.isPlaying) UnityEngine.Object.Destroy(texture);
			else UnityEngine.Object.DestroyImmediate(texture);
		}
	}

	internal sealed class LiveProgramShaderGraph : IDisposable {
		private readonly string _sourceNodeId;
		private readonly string _outputNodeId;
		private readonly IReadOnlyList<LiveProgramShaderGraphNode> _nodes;

		internal LiveProgramShaderGraph(LiveProgramGraphDefinition definition, IEnumerable<LiveProgramShaderGraphNode> nodes) {
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

		public void Dispose() {
			for (var index = _nodes.Count - 1; index >= 0; index--) _nodes[index].Dispose();
		}

	}

	/// <summary>Evaluates a Bootstrap-created graph without constructing nodes or rendering resources.</summary>
	public sealed class LiveGraphRuntime : IDisposable {
		public const int ProgramWidth = 1920;
		public const int ProgramHeight = 1080;

		private readonly LiveGraph _graph;
		private readonly IReadOnlyList<LiveProgramOutput> _programOutputs;
		private readonly Dictionary<string, LiveProgramOutput> _programOutputsBySceneId;
		private LiveProgramOutput _selectedProgramOutput;
		private ulong _frameNumber;
		private double _graphTime;
		private double _lastDeltaSeconds;
		private bool _disposed;

		public string SelectedSceneId => _selectedProgramOutput?.Definition.Id ?? string.Empty;
		public IReadOnlyList<Scene3DDefinition> Scenes => _programOutputs.Select(output => output.Definition).ToArray();
		public LiveProgramFrame CurrentFrame { get; private set; }
		public LiveProgramFrames CurrentFrames { get; private set; }

		internal LiveGraphRuntime(LiveGraph graph) {
			_graph = graph ?? throw new ArgumentNullException(nameof(graph));
			_programOutputs = graph.ProgramOutputs;
			_programOutputsBySceneId = _programOutputs.ToDictionary(output => output.Definition.Id, StringComparer.Ordinal);
			_selectedProgramOutput = _programOutputs[0];
			CurrentFrames = new LiveProgramFrames(_programOutputs.Select(output => new LiveProgramFrame(output.ProgramTexture, 0)));
			CurrentFrame = CurrentFrames.Primary;
		}

		public LiveParameterApplicationResult Apply(LiveParameterRequest request) {
			if (!_programOutputsBySceneId.TryGetValue(request.SceneId, out var scene)) return Reject(request, "The requested live scene does not exist.");
			if (request.Kind == LiveParameterRequestKind.SelectScene) {
				_selectedProgramOutput = scene;
				return Accept(request);
			}
			return scene.Root.TrySetParameter(request.ParameterId, request.Value, out var reason) ? Accept(request) : Reject(request, reason);
		}

		public void Evaluate(double deltaSeconds) {
			EnsureUsable();
			_lastDeltaSeconds = Math.Max(0d, deltaSeconds);
			_graphTime += _lastDeltaSeconds;
			foreach (var scene in _programOutputs) {
				var result = scene.Runtime.AdvanceGraphClock(_lastDeltaSeconds * Mathf.Lerp(0f, 2f, scene.Root.Motion));
				if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
			}
		}

		public void SceneUpdate(double deltaSeconds) {
			EnsureUsable();
			foreach (var scene in _programOutputs) {
				var result = scene.Runtime.AdvancePhysics(deltaSeconds * Mathf.Lerp(0f, 2f, scene.Root.Motion));
				if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
			}
		}

		public LiveProgramFrames Render() {
			EnsureUsable();
			var nextFrame = _frameNumber + 1;
			if (nextFrame == 0) nextFrame = 1;
			foreach (var scene in _programOutputs) scene.Render(_graphTime, _lastDeltaSeconds, nextFrame);
			_frameNumber = nextFrame;
			CurrentFrames = new LiveProgramFrames(_programOutputs.Select(output => new LiveProgramFrame(output.ProgramTexture, _frameNumber)));
			CurrentFrame = CurrentFrames.Primary;
			return CurrentFrames;
		}

		public LiveParameterDefinition[] GetSelectedParameterDefinitions() => _selectedProgramOutput?.Root.GetParameterDefinitions() ?? Array.Empty<LiveParameterDefinition>();

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			_graph.Dispose();
		}

		private void EnsureUsable() {
			if (_disposed) throw new ObjectDisposedException(nameof(LiveGraphRuntime));
			if (_selectedProgramOutput == null) throw new InvalidOperationException("A live ProgramOutput is not selected.");
		}

		private static LiveParameterApplicationResult Accept(LiveParameterRequest request) => new LiveParameterApplicationResult(request.SequenceNumber, true, string.Empty);
		private static LiveParameterApplicationResult Reject(LiveParameterRequest request, string reason) => new LiveParameterApplicationResult(request.SequenceNumber, false, reason);
	}
}
