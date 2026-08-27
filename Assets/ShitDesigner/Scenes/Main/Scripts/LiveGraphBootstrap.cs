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

namespace ShitDesigner.Main {
	/// <summary>Holds the authored patches and their Unity scene nodes.</summary>
	[DisallowMultipleComponent]
	public sealed class LiveGraphBootstrap : MonoBehaviour {
		private static readonly LiveProgramGraphDefinition ProgramGraph = new LiveProgramGraphDefinition(
			"scene",
			"echo",
			new[] {
				new LiveProgramGraphNodeDefinition("echo", "shitdesigner.shader.temporal.echo")
			},
			new[] {
				new LiveProgramGraphConnection("scene", "echo", "input")
			});

		[SerializeField] private PatchDefinition[] _patches = Array.Empty<PatchDefinition>();
		[SerializeField] private ShaderNodeManifestAsset _shaderManifest;
		[SerializeField] private AssetFlashComponent _assetFlash;

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
			var shaderDefinitions = BuildProgramShaderDefinitions();
			var flashShader = Resources.Load<Shader>("LiveProgramFlash");
			if (flashShader == null) throw new InvalidOperationException("The live Program flash shader is missing from Resources.");
			var sceneManager = new SceneIsolationManager(renderSource: new UnityCameraRenderSource());
			var renderPool = new RenderTexturePool();
			var nodeIndices = definitions.SelectMany(definition => definition.Nodes).Select((node, index) => new { node.Id, Index = index })
				.ToDictionary(node => node.Id, node => node.Index, StringComparer.Ordinal);
			try {
				return new LiveGraph(sceneManager, renderPool, definitions, node =>
					BuildOutput(sceneManager, renderPool, node, nodeIndices[node.Id], shaderDefinitions, flashShader, _assetFlash));
			}
			catch {
				sceneManager.Dispose();
				renderPool.Dispose();
				throw;
			}
		}

		private IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> BuildProgramShaderDefinitions() {
			if (_shaderManifest == null) throw new InvalidOperationException("The live Program graph requires a ShaderNodeManifest.");
			var manifest = _shaderManifest.BuildRuntimeManifest();
			var definitions = new Dictionary<NodeTypeId, LiveProgramShaderDefinition>();
			foreach (var node in ProgramGraph.Nodes) {
				if (definitions.ContainsKey(node.TypeId)) continue;
				var entry = manifest.Find(node.TypeId.Value);
				var assetEntry = _shaderManifest.Find(node.TypeId.Value);
				if (entry == null || assetEntry == null || assetEntry.Shader == null)
					throw new InvalidOperationException("The live Program graph shader is missing a direct Shader reference: " + node.TypeId.Value + ".");
				definitions.Add(node.TypeId, new LiveProgramShaderDefinition(entry, assetEntry.Shader));
			}
			return definitions;
		}

		private static LiveProgramOutput BuildOutput(SceneIsolationManager sceneManager, RenderTexturePool renderPool,
			Scene3DDefinition definition, int index, IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> shaderDefinitions,
			Shader flashShader, AssetFlashComponent assetFlash) {
			var created = sceneManager.Create(new SceneCreateRequest(NodeInstanceId.New(), SceneNodeKind.ThreeD,
				"ShitDesigner.Main.LiveScene." + index, 1, definition.Prefab));
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
				shaderGraph = BuildProgramShaderGraph(renderPool, shaderDefinitions, definition.Id);
				flash = new LiveProgramFlash(flashShader);
				return new LiveProgramOutput(definition, created.Value, root, programTexture, renderTexture, shaderGraphTexture, shaderGraph, flash, assetFlash);
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

		private static LiveProgramShaderGraph BuildProgramShaderGraph(RenderTexturePool renderPool,
			IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> shaderDefinitions, string outputId) {
			var connections = ProgramGraph.Connections.GroupBy(connection => connection.TargetNodeId)
				.ToDictionary(group => group.Key, group => (IReadOnlyDictionary<PortId, string>)group.ToDictionary(connection => connection.TargetPortId, connection => connection.SourceNodeId), StringComparer.Ordinal);
			var nodes = new List<LiveProgramShaderGraphNode>(ProgramGraph.EvaluationOrder.Count);
			try {
				foreach (var node in ProgramGraph.EvaluationOrder) {
					if (!shaderDefinitions.TryGetValue(node.TypeId, out var shader))
						throw new InvalidOperationException("The live Program graph shader is unavailable: " + node.TypeId.Value + ".");
					ShaderPassGraphRuntimeNode runtime = null;
					RenderTexture target = null;
					try {
						var binding = shader.Entry.ToShaderBinding();
						var inputs = connections.TryGetValue(node.Id, out var mapped) ? mapped : new Dictionary<PortId, string>();
						foreach (var input in binding.Inputs.Where(input => input.Type == NodePortType.ImageFrame && input.Required && input.Role != ShaderInputRole.History))
							if (!inputs.ContainsKey(input.PortId)) throw new InvalidOperationException("The live Program graph is missing a required input: " + node.Id + "." + input.PortId.Value + ".");
						var record = new RuntimeNodeCreateInfo(NodeInstanceId.New(), node.TypeId, shader.Entry.SchemaVersion,
							shader.Entry.DisplayName, true, 0f, 0f, shader.Entry.Parameters.Select(parameter =>
								new RuntimeParameterSnapshot(parameter.Definition.Id, parameter.Definition.Type, parameter.Definition.DefaultValue, parameter.Definition.RuntimeStateful)));
						runtime = new ShaderPassGraphRuntimeNode(record, 1UL,
							new ShaderMaterialBinding(binding.ShaderKey, shader.Shader, outputPass: binding.OutputPass, descriptor: binding), renderPool,
							"shitdesigner.main." + outputId + "." + node.Id, binding.Family == ShaderNodeFamily.Generator, binding.Family == ShaderNodeFamily.Composite);
						target = CreateTexture("ShitDesigner.Main.ProgramGraph." + outputId + "." + node.Id, 0, RenderTextureFormat.ARGBHalf);
						nodes.Add(new LiveProgramShaderGraphNode(node.Id, runtime, target, inputs));
					}
					catch {
						runtime?.Dispose();
						ReleaseTexture(target);
						throw;
					}
				}
				return new LiveProgramShaderGraph(ProgramGraph, nodes);
			}
			catch {
				for (var index = nodes.Count - 1; index >= 0; index--) nodes[index].Dispose();
				throw;
			}
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
