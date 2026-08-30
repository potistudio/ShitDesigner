using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Media;
using ShitDesigner.Nodes;
using ShitDesigner.Rendering;
using ShitDesigner.Runtime;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

namespace ShitDesigner.Main {
	/// <summary>
	/// Holds the authored patches and their graph node dependencies.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class LiveGraphBootstrap : MonoBehaviour {
		private const string VideoPlayerTypeId = "shitdesigner.video.player";
		private const string PlayingParameterId = "transport.playing";
		private const string PlayheadParameterId = "transport.playhead_seconds";
		private const string SpeedParameterId = "transport.speed";
		private const string LoopParameterId = "transport.loop";
		[Header("Main")]
		[FormerlySerializedAs("_patches")]
		[SerializeField] private PatchDefinition[] m_MainPatches = Array.Empty<PatchDefinition>();
		[Header("Overlay")]
		[SerializeField] private PatchDefinition[] m_OverlayPatches = Array.Empty<PatchDefinition>();
		[SerializeField] private ShaderNodeManifestAsset _shaderManifest;
		[Header("Video decoding")]
		[SerializeField] private Material m_VideoConversionMaterial;
		[SerializeField] private Material m_HapPremultiplyMaterial;
		[SerializeField] private Material m_HapYCoCgMaterial;
		[SerializeField] private Material m_HapAlphaMaterial;
		[SerializeField] private ComputeShader m_HapDecodeShader;

		public PatchDefinition[] MainPatches => m_MainPatches ?? Array.Empty<PatchDefinition>();
		public PatchDefinition[] OverlayPatches => m_OverlayPatches ?? Array.Empty<PatchDefinition>();
		public PatchDefinition[] Patches => MainPatches.Concat(OverlayPatches).ToArray();
		public int ProgramOutputCount => Patches.Count(patch => patch != null);

		public LiveGraphRuntime CreateRuntime() {
			var graph = BuildGraph(new LiveRenderSize(LiveGraphRuntime.ProgramWidth, LiveGraphRuntime.ProgramHeight));
			try { return new LiveGraphRuntime(graph); }
			catch {
				graph.Dispose();
				throw;
			}
		}

		private LiveGraph BuildGraph(LiveRenderSize renderSize) {
			var definitions = Patches;
			ValidateDefinitions(definitions);
			var programGraphs = definitions.ToDictionary(definition => definition.Id, BuildProgramGraph, StringComparer.Ordinal);
			var shaderDefinitions = BuildProgramShaderDefinitions(programGraphs.Values);
			var sceneManager = new SceneIsolationManager(renderSource: new UnityCameraRenderSource());
			var renderPool = new RenderTexturePool();
			LiveOverlayCompositor compositor = null;
			try {
				compositor = new LiveOverlayCompositor(shaderDefinitions, renderPool, renderSize);
				return new LiveGraph(sceneManager, renderPool, definitions, (patch, outputSize) =>
					BuildOutput(sceneManager, renderPool, patch, programGraphs[patch.Id], shaderDefinitions, outputSize), compositor);
			}
			catch {
				compositor?.Dispose();
				sceneManager.Dispose();
				renderPool.Dispose();
				throw;
			}
		}

		private IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> BuildProgramShaderDefinitions(IEnumerable<GraphDefinition> programGraphs) {
			if (_shaderManifest == null) throw new InvalidOperationException("The live Program graph requires a ShaderNodeManifest.");
			var manifest = _shaderManifest.BuildRuntimeManifest();
			var definitions = new Dictionary<NodeTypeId, LiveProgramShaderDefinition>();
			var typeIds = programGraphs.SelectMany(programGraph => programGraph.Nodes.Select(node => node.TypeId))
				.Concat(LiveOverlayCompositor.RequiredNodeTypeIds)
				.Distinct()
				.Where(typeId => typeId.Value != PatchGraphNode.Scene3DTypeId && typeId.Value != VideoPlayerTypeId);
			foreach (var typeId in typeIds) {
				var entry = manifest.Find(typeId.Value);
				var assetEntry = _shaderManifest.Find(typeId.Value);
				if (entry == null || assetEntry == null || assetEntry.Shader == null)
					throw new InvalidOperationException("The live Program graph shader is missing a direct Shader reference: " + typeId.Value + ".");
				definitions.Add(typeId, new LiveProgramShaderDefinition(entry, assetEntry.Shader));
			}
			return definitions;
		}

		private LiveProgramOutput BuildOutput(SceneIsolationManager sceneManager, RenderTexturePool renderPool,
			PatchDefinition patch, GraphDefinition programGraph,
			IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> shaderDefinitions,
			LiveRenderSize renderSize) {
			var resourceSuffix = patch.Id + "." + renderSize.Width + "x" + renderSize.Height;
			var programTexture = CreateTexture("ShitDesigner.Main.ProgramOutput." + resourceSuffix, renderSize, 0, RenderTextureFormat.ARGBHalf);
			RenderTexture shaderGraphTexture = null;
			LiveProgramGraph programGraphRuntime = null;
			try {
				ClearTexture(programTexture);
				shaderGraphTexture = CreateTexture("ShitDesigner.Main.ProgramGraphOutput." + resourceSuffix, renderSize, 0, RenderTextureFormat.ARGBHalf);
				programGraphRuntime = BuildLiveProgramGraph(sceneManager, renderPool, shaderDefinitions, programGraph, patch.ProgramGraph,
					resourceSuffix, renderSize);
				return new LiveProgramOutput(programTexture, shaderGraphTexture, programGraphRuntime);
			}
			catch {
				programGraphRuntime?.Dispose();
				ReleaseTexture(shaderGraphTexture);
				ReleaseTexture(programTexture);
				throw;
			}
		}

		internal static RuntimeParameterSnapshot[] BuildRuntimeParameters(ShaderNodeManifestEntry entry, PatchGraphNode authoredNode) {
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
			return new GraphDefinition(authored.OutputNodeId,
				authored.Nodes.Select(node => new NodeDefinition(node.Id, node.TypeId)),
				authored.Connections.Select(connection => new NodeConnection(connection.SourceNodeId, connection.SourcePortId,
					connection.TargetNodeId, connection.TargetPortId)));
		}

		private LiveProgramGraph BuildLiveProgramGraph(SceneIsolationManager sceneManager, RenderTexturePool renderPool,
			IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> shaderDefinitions, GraphDefinition programGraph,
			PatchProgramGraph authoredGraph, string outputId, LiveRenderSize renderSize) {
			var connections = programGraph.Connections.GroupBy(connection => connection.TargetNodeId)
				.ToDictionary(group => group.Key, group => (IReadOnlyDictionary<PortId, string>)group.ToDictionary(connection => connection.TargetPortId, connection => connection.SourceNodeId), StringComparer.Ordinal);
			var nodes = new List<ILiveProgramGraphNode>(programGraph.EvaluationOrder.Count);
			try {
				foreach (var node in programGraph.EvaluationOrder) {
					var authoredNode = authoredGraph.Nodes.FirstOrDefault(candidate => string.Equals(candidate.Id, node.Id, StringComparison.Ordinal));
					var inputs = connections.TryGetValue(node.Id, out var mapped) ? mapped : new Dictionary<PortId, string>();
					ShaderPassGraphRuntimeNode runtime = null;
					SceneNodeRuntime sceneRuntime = null;
					RenderTexture target = null;
					try {
						var renderFormat = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ? RenderTextureFormat.ARGB32 : RenderTextureFormat.ARGBHalf;
						target = CreateTexture("ShitDesigner.Main.ProgramGraph." + outputId + "." + node.Id, renderSize,
							node.TypeId.Value == PatchGraphNode.Scene3DTypeId ? 24 : 0, renderFormat);
						if (node.TypeId.Value == PatchGraphNode.Scene3DTypeId) {
							if (authoredNode == null || authoredNode.SceneDefinition == null)
								throw new InvalidOperationException("The live Program scene node requires a Scene3DDefinition: " + node.Id + ".");
							if (inputs.Count > 0)
								throw new InvalidOperationException("The live Program scene node does not accept image inputs: " + node.Id + ".");
							var created = sceneManager.Create(new SceneCreateRequest(NodeInstanceId.New(), SceneNodeKind.ThreeD,
								"ShitDesigner.Main.LiveScene." + outputId + "." + node.Id,
								1, authoredNode.SceneDefinition.Prefab, transparentBackground: true));
							if (created.IsFailure) throw new InvalidOperationException(created.Error.Message);
							sceneRuntime = created.Value;
							var root = sceneRuntime.Root.GetComponent<LiveSceneRoot>();
							if (root == null) throw new InvalidOperationException("Every live scene prefab root requires a LiveSceneRoot.");
							root.Initialize(authoredNode.SceneDefinition.Id);
							sceneRuntime.BindGraphClock();
							nodes.Add(new LiveProgramSceneGraphNode(node.Id, authoredNode.SceneDefinition, sceneRuntime, root, target, renderSize));
							sceneRuntime = null;
							target = null;
							continue;
						}
						if (node.TypeId.Value == VideoPlayerTypeId) {
							if (authoredNode == null)
								throw new InvalidOperationException("The live Program VideoPlayer node requires a video source: " + node.Id + ".");
							if (inputs.Count > 0)
								throw new InvalidOperationException("The live Program VideoPlayer node does not accept image inputs: " + node.Id + ".");
							if (!string.IsNullOrWhiteSpace(authoredNode.VideoPath)) {
								nodes.Add(new LiveProgramFileVideoGraphNode(node.Id, target, authoredNode.VideoPath,
									ReadBoolParameter(authoredNode, PlayingParameterId, true),
									ReadFloatParameter(authoredNode, PlayheadParameterId, 0f),
									ReadFloatParameter(authoredNode, SpeedParameterId, 1f),
									ReadBoolParameter(authoredNode, LoopParameterId, true),
									m_VideoConversionMaterial, m_HapPremultiplyMaterial, m_HapYCoCgMaterial,
									m_HapAlphaMaterial, m_HapDecodeShader));
								continue;
							}
							if (authoredNode.VideoClip == null)
								throw new InvalidOperationException("The live Program VideoPlayer node requires a video source: " + node.Id + ".");
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
						sceneRuntime?.Dispose();
						ReleaseTexture(target);
						throw;
					}
				}
				return new LiveProgramGraph(programGraph, nodes);
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
			foreach (var definition in definitions) {
				if (definition != null && definition.Validate().IsSuccess) continue;
				var patchName = definition == null ? "<missing>" : definition.DisplayName;
				if (string.IsNullOrWhiteSpace(patchName) && definition != null) patchName = definition.name;
				if (string.IsNullOrWhiteSpace(patchName)) patchName = "<unnamed>";
				throw new InvalidOperationException("Every patch requires a valid definition: " + patchName + ".");
			}
			if (definitions.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
				throw new InvalidOperationException("Patch IDs must be unique.");
		}

		private static RenderTexture CreateTexture(string name, LiveRenderSize renderSize, int depth, RenderTextureFormat format) {
			var texture = new RenderTexture(renderSize.Width, renderSize.Height, depth, format) {
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
