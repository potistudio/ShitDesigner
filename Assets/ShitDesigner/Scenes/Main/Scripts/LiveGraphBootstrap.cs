using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Nodes;
using ShitDesigner.Rendering;
using ShitDesigner.Runtime;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Main {
	/// <summary>
	/// Holds the authored patches and their Unity scene nodes.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class LiveGraphBootstrap : MonoBehaviour {
		private const string VideoPlayerTypeId = "shitdesigner.video.player";
		private const string PlayingParameterId = "transport.playing";
		private const string PlayheadParameterId = "transport.playhead_seconds";
		private const string SpeedParameterId = "transport.speed";
		private const string LoopParameterId = "transport.loop";
		[SerializeField] private PatchDefinition[] _patches = Array.Empty<PatchDefinition>();
		[SerializeField] private ShaderNodeManifestAsset _shaderManifest;

		public PatchDefinition[] Patches => _patches ?? Array.Empty<PatchDefinition>();
		public int ProgramOutputCount => Patches.Sum(patch => patch == null ? 0 : patch.Nodes.Count());

		public LiveGraphRuntime CreateRuntime() {
			var graph = BuildGraph();
			try { return new LiveGraphRuntime(graph); }
			catch {
				graph.Dispose();
				throw;
			}
		}

		private LiveGraph BuildGraph() {
			var definitions = Patches;
			ValidateDefinitions(definitions);
			var programGraphs = definitions.ToDictionary(definition => definition.Id, BuildProgramGraph, StringComparer.Ordinal);
			var shaderDefinitions = BuildProgramShaderDefinitions(programGraphs.Values);
			var flashShader = Resources.Load<Shader>("LiveProgramFlash");
			if (flashShader == null) throw new InvalidOperationException("The live Program flash shader is missing from Resources.");
			var sceneManager = new SceneIsolationManager(renderSource: new UnityCameraRenderSource());
			var renderPool = new RenderTexturePool();
			var nodeIndices = definitions.SelectMany(definition => definition.Nodes).Select((node, index) => new { node.Id, Index = index })
				.ToDictionary(node => node.Id, node => node.Index, StringComparer.Ordinal);
			try {
				return new LiveGraph(sceneManager, renderPool, definitions, (patch, node, flashPatch) =>
					BuildOutput(sceneManager, renderPool, patch, node, nodeIndices[node.Id], programGraphs[patch.Id], shaderDefinitions, flashShader, flashPatch));
			}
			catch {
				sceneManager.Dispose();
				renderPool.Dispose();
				throw;
			}
		}

		private IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> BuildProgramShaderDefinitions(IEnumerable<GraphDefinition> programGraphs) {
			if (_shaderManifest == null) throw new InvalidOperationException("The live Program graph requires a ShaderNodeManifest.");
			var manifest = _shaderManifest.BuildRuntimeManifest();
			var definitions = new Dictionary<NodeTypeId, LiveProgramShaderDefinition>();
			foreach (var programGraph in programGraphs) {
				foreach (var node in programGraph.Nodes) {
					if (node.TypeId.Value == VideoPlayerTypeId) continue;
					if (definitions.ContainsKey(node.TypeId)) continue;
					var entry = manifest.Find(node.TypeId.Value);
					var assetEntry = _shaderManifest.Find(node.TypeId.Value);
					if (entry == null || assetEntry == null || assetEntry.Shader == null)
						throw new InvalidOperationException("The live Program graph shader is missing a direct Shader reference: " + node.TypeId.Value + ".");
					definitions.Add(node.TypeId, new LiveProgramShaderDefinition(entry, assetEntry.Shader));
				}
			}
			return definitions;
		}

		private static LiveProgramOutput BuildOutput(SceneIsolationManager sceneManager, RenderTexturePool renderPool,
			PatchDefinition patch, Scene3DDefinition definition, int index, GraphDefinition programGraph,
			IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> shaderDefinitions,
			Shader flashShader, PatchFlashDefinition flashPatch) {
			var created = sceneManager.Create(new SceneCreateRequest(NodeInstanceId.New(), SceneNodeKind.ThreeD,
				"ShitDesigner.Main.LiveScene." + index, 1, definition.Prefab, transparentBackground: true));
			if (created.IsFailure) throw new InvalidOperationException(created.Error.Message);
			var root = created.Value.Root.GetComponent<LiveSceneRoot>();
			if (root == null) {
				created.Value.Dispose();
				throw new InvalidOperationException("Every live scene prefab root requires a LiveSceneRoot.");
			}
			root.Initialize(definition.Id);
			created.Value.BindGraphClock();
			var programTexture = CreateTexture("ShitDesigner.Main.ProgramOutput." + index, 0, RenderTextureFormat.ARGBHalf);
			RenderTexture renderTexture = null;
			RenderTexture shaderGraphTexture = null;
			LiveProgramShaderGraph shaderGraph = null;
			LiveProgramFlash flash = null;
			try {
				ClearTexture(programTexture);
				var renderFormat = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ? RenderTextureFormat.ARGB32 : RenderTextureFormat.ARGBHalf;
				renderTexture = CreateTexture("ShitDesigner.Main.ProgramRender." + index, 24, renderFormat);
				shaderGraphTexture = CreateTexture("ShitDesigner.Main.ProgramGraphOutput." + index, 0, RenderTextureFormat.ARGBHalf);
				shaderGraph = BuildProgramShaderGraph(renderPool, shaderDefinitions, programGraph, patch.ProgramGraph, definition.Id);
				flash = new LiveProgramFlash(flashShader);
				return new LiveProgramOutput(definition, created.Value, root, programTexture, renderTexture, shaderGraphTexture, shaderGraph, flash, flashPatch);
			}
			catch {
				flash?.Dispose();
				shaderGraph?.Dispose();
				ReleaseTexture(shaderGraphTexture);
				ReleaseTexture(programTexture);
				ReleaseTexture(renderTexture);
				created.Value.Dispose();
				throw;
			}
		}

		private static RuntimeParameterSnapshot[] BuildRuntimeParameters(ShaderNodeManifestEntry entry, PatchGraphNode authoredNode) {
			if (authoredNode != null)
				foreach (var configured in authoredNode.Parameters)
					if (configured != null && entry.Parameters.All(parameter => !string.Equals(parameter.Id.Value, configured.Id, StringComparison.Ordinal)))
						throw new InvalidOperationException("The patch graph parameter is not provided by the shader node: " + authoredNode.Id + "." + configured.Id + ".");

			return entry.Parameters.Select(parameter => {
				var configured = authoredNode?.FindParameter(parameter.Id.Value);
				var value = parameter.DefaultValue;
				if (configured != null) {
					value = configured.Value;
					if (value.Type != parameter.Type)
						throw new InvalidOperationException("The patch graph parameter type does not match the shader node: " + authoredNode.Id + "." + parameter.Id.Value + ".");
					if (parameter.Type == ParameterType.Enum && parameter.Definition.EnumOptions.Count > 0
						&& !parameter.Definition.EnumOptions.Contains(value.AsString(), StringComparer.Ordinal))
						throw new InvalidOperationException("The patch graph enum value is not defined by the shader node: " + authoredNode.Id + "." + parameter.Id.Value + ".");
					if (parameter.Minimum.HasValue && parameter.Maximum.HasValue && ParameterValue.IsLogicalControlTargetType(parameter.Type)) {
						var clamped = ParameterValue.Clamp(value, parameter.Minimum.Value, parameter.Maximum.Value);
						if (clamped.IsFailure) throw new InvalidOperationException("The patch graph parameter range is invalid: " + authoredNode.Id + "." + parameter.Id.Value + ".");
						value = clamped.Value;
					}
				}
				return new RuntimeParameterSnapshot(parameter.Definition.Id, parameter.Definition.Type, value, parameter.Definition.RuntimeStateful);
			}).ToArray();
		}

		private static GraphDefinition BuildProgramGraph(PatchDefinition definition) {
			var authored = definition.ProgramGraph;
			return new GraphDefinition(authored.SourceNodeId, authored.OutputNodeId,
				authored.Nodes.Select(node => new NodeDefinition(node.Id, node.TypeId)),
				authored.Connections.Select(connection => new NodeConnection(connection.SourceNodeId, connection.SourcePortId,
					connection.TargetNodeId, connection.TargetPortId)));
		}

		private static LiveProgramShaderGraph BuildProgramShaderGraph(RenderTexturePool renderPool,
			IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> shaderDefinitions, GraphDefinition programGraph,
			PatchProgramGraph authoredGraph, string outputId) {
			var connections = programGraph.Connections.GroupBy(connection => connection.TargetNodeId)
				.ToDictionary(group => group.Key, group => (IReadOnlyDictionary<PortId, string>)group.ToDictionary(connection => connection.TargetPortId, connection => connection.SourceNodeId), StringComparer.Ordinal);
			var nodes = new List<ILiveProgramGraphNode>(programGraph.EvaluationOrder.Count);
			try {
				foreach (var node in programGraph.EvaluationOrder) {
					var authoredNode = authoredGraph.Nodes.FirstOrDefault(candidate => string.Equals(candidate.Id, node.Id, StringComparison.Ordinal));
					var inputs = connections.TryGetValue(node.Id, out var mapped) ? mapped : new Dictionary<PortId, string>();
					ShaderPassGraphRuntimeNode runtime = null;
					RenderTexture target = null;
					try {
						target = CreateTexture("ShitDesigner.Main.ProgramGraph." + outputId + "." + node.Id, 0, RenderTextureFormat.ARGBHalf);
						if (node.TypeId.Value == VideoPlayerTypeId) {
							if (authoredNode == null || authoredNode.VideoClip == null)
								throw new InvalidOperationException("The live Program VideoPlayer node requires a Video Clip: " + node.Id + ".");
							if (inputs.Count > 0)
								throw new InvalidOperationException("The live Program VideoPlayer node does not accept image inputs: " + node.Id + ".");
							nodes.Add(new LiveProgramVideoGraphNode(node.Id, target, authoredNode.VideoClip,
								ReadBoolParameter(authoredNode, PlayingParameterId, true),
								ReadFloatParameter(authoredNode, PlayheadParameterId, 0f),
								ReadFloatParameter(authoredNode, SpeedParameterId, 1f),
								ReadBoolParameter(authoredNode, LoopParameterId, true)));
							continue;
						}
						if (!shaderDefinitions.TryGetValue(node.TypeId, out var shader))
							throw new InvalidOperationException("The live Program graph shader is unavailable: " + node.TypeId.Value + ".");
						var binding = shader.Entry.ToShaderBinding();
						foreach (var input in inputs.Keys)
							if (!binding.Inputs.Any(candidate => candidate.PortId == input))
								throw new InvalidOperationException("The live Program graph references an unknown input: " + node.Id + "." + input.Value + ".");
						foreach (var input in binding.Inputs.Where(input => input.Type == NodePortType.ImageFrame && input.Required && input.Role != ShaderInputRole.History))
							if (!inputs.ContainsKey(input.PortId)) throw new InvalidOperationException("The live Program graph is missing a required input: " + node.Id + "." + input.PortId.Value + ".");
						var parameters = BuildRuntimeParameters(shader.Entry, authoredNode);
						var record = new RuntimeNodeCreateInfo(NodeInstanceId.New(), node.TypeId, shader.Entry.SchemaVersion,
							shader.Entry.DisplayName, true, 0f, 0f, parameters);
						runtime = new ShaderPassGraphRuntimeNode(record, 1UL,
							new ShaderMaterialBinding(binding.ShaderKey, shader.Shader, outputPass: binding.OutputPass, descriptor: binding), renderPool,
							"shitdesigner.main." + outputId + "." + node.Id, binding.Family == ShaderNodeFamily.Generator, binding.Family == ShaderNodeFamily.Composite);
						nodes.Add(new LiveProgramShaderGraphNode(node.Id, runtime, target, inputs));
					}
					catch {
						runtime?.Dispose();
						ReleaseTexture(target);
						throw;
					}
				}
				return new LiveProgramShaderGraph(programGraph, nodes);
			}
			catch {
				for (var index = nodes.Count - 1; index >= 0; index--) nodes[index].Dispose();
				throw;
			}
		}

		private static bool ReadBoolParameter(PatchGraphNode node, string parameterId, bool fallback) {
			var parameter = node?.FindParameter(parameterId);
			if (parameter == null || parameter.Type != ParameterType.Bool) return fallback;
			return parameter.Value.AsBool();
		}

		private static float ReadFloatParameter(PatchGraphNode node, string parameterId, float fallback) {
			var parameter = node?.FindParameter(parameterId);
			if (parameter == null || parameter.Type != ParameterType.Float) return fallback;
			return parameter.Value.AsFloat();
		}

		private static void ValidateDefinitions(IReadOnlyList<PatchDefinition> definitions) {
			if (definitions.Count == 0) throw new InvalidOperationException("At least one patch is required.");
			if (definitions.Any(definition => definition == null || definition.Validate().IsFailure))
				throw new InvalidOperationException("Every patch requires a valid definition.");
			if (definitions.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
				throw new InvalidOperationException("Patch IDs must be unique.");
			var nodeIds = definitions.SelectMany(definition => definition.Nodes).Select(node => node.Id).ToArray();
			if (nodeIds.Distinct(StringComparer.Ordinal).Count() != nodeIds.Length)
				throw new InvalidOperationException("Unity scene nodes cannot be shared by patches.");
		}

		private static RenderTexture CreateTexture(string name, int depth, RenderTextureFormat format) {
			var texture = new RenderTexture(LiveGraphRuntime.ProgramWidth, LiveGraphRuntime.ProgramHeight, depth, format) {
				name = name,
				useMipMap = false,
				autoGenerateMips = false
			};
			if (!texture.Create()) {
				ReleaseTexture(texture);
				throw new InvalidOperationException("A ProgramOutput texture could not be created.");
			}
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
			if (UnityEngine.Application.isPlaying) Destroy(texture);
			else DestroyImmediate(texture);
		}
	}
}
