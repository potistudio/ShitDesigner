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
	/// <summary>Holds the graph sources for independently rendered Main ProgramOutputs.</summary>
	[DisallowMultipleComponent]
	public sealed class LiveGraphBootstrap : MonoBehaviour {
		private static readonly LiveProgramGraphDefinition ProgramGraph = new LiveProgramGraphDefinition(
			"scene",
			"contrast",
			new[] {
				new LiveProgramGraphNodeDefinition("invert", "shitdesigner.shader.color.invert"),
				new LiveProgramGraphNodeDefinition("contrast", "shitdesigner.shader.color.contrast")
			},
			new[] {
				new LiveProgramGraphConnection("scene", "invert", "input"),
				new LiveProgramGraphConnection("invert", "contrast", "input")
			});

		[SerializeField] private Scene3DDefinition[] _programOutputs = Array.Empty<Scene3DDefinition>();
		[SerializeField] private ShaderNodeManifestAsset _shaderManifest;

		public Scene3DDefinition[] ProgramOutputs => _programOutputs ?? Array.Empty<Scene3DDefinition>();
		public Scene3DDefinition[] Scenes => ProgramOutputs;
		public int ProgramOutputCount => ProgramOutputs.Length;

		public LiveGraphRuntime CreateRuntime() => new LiveGraphRuntime(BuildGraph());

		private LiveGraph BuildGraph() {
			var definitions = ProgramOutputs;
			ValidateDefinitions(definitions);
			var shaderDefinitions = BuildProgramShaderDefinitions();
			var sceneManager = new SceneIsolationManager(renderSource: new UnityCameraRenderSource());
			var renderPool = new RenderTexturePool();
			var outputs = new List<LiveProgramOutput>(definitions.Length);
			try {
				for (var index = 0; index < definitions.Length; index++)
					outputs.Add(BuildOutput(sceneManager, renderPool, definitions[index], index, shaderDefinitions));
				return new LiveGraph(sceneManager, renderPool, outputs);
			}
			catch {
				for (var index = outputs.Count - 1; index >= 0; index--) outputs[index].Dispose();
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
			Scene3DDefinition definition, int index, IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> shaderDefinitions) {
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
			LiveProgramShaderGraph shaderGraph = null;
			try {
				ClearTexture(programTexture);
				var renderFormat = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null ? RenderTextureFormat.ARGB32 : RenderTextureFormat.ARGBHalf;
				renderTexture = CreateTexture("ShitDesigner.Main.ProgramRender." + index, 24, renderFormat);
				shaderGraph = BuildProgramShaderGraph(renderPool, shaderDefinitions, definition.Id);
				return new LiveProgramOutput(definition, created.Value, root, programTexture, renderTexture, shaderGraph);
			}
			catch {
				shaderGraph?.Dispose();
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

		private static void ValidateDefinitions(IReadOnlyList<Scene3DDefinition> definitions) {
			if (definitions.Count == 0) throw new InvalidOperationException("At least one live scene is required.");
			if (definitions.Any(definition => definition == null || definition.Validate().IsFailure))
				throw new InvalidOperationException("Every live scene requires a valid Scene3DDefinition.");
			if (definitions.Any(definition => string.IsNullOrWhiteSpace(definition.Id)))
				throw new InvalidOperationException("Every live scene requires an ID.");
			if (definitions.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count() != definitions.Count)
				throw new InvalidOperationException("Live scene IDs must be unique.");
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
			if (Application.isPlaying) Destroy(texture);
			else DestroyImmediate(texture);
		}
	}
}
