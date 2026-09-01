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

	internal readonly struct LiveOutputRenderSizes {
		public LiveRenderSize Program { get; }
		public LiveRenderSize Overlay { get; }

		public LiveOutputRenderSizes(LiveRenderSize program, LiveRenderSize overlay) {
			Program = program;
			Overlay = overlay;
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
		private readonly Func<PatchDefinition, LiveRenderSize, LiveProgramOutput> _createOutput;
		public IReadOnlyList<PatchDefinition> PatchDefinitions { get; }
		public LiveOverlayCompositor Compositor { get; }
		public LiveOverlayCompositor OverlayOutputCompositor { get; }
		public LiveInstantEffectRenderer InstantEffects { get; }

		public LiveGraph(SceneIsolationManager sceneManager, RenderTexturePool renderPool, IEnumerable<PatchDefinition> patchDefinitions,
			Func<PatchDefinition, LiveRenderSize, LiveProgramOutput> createOutput, LiveOverlayCompositor compositor, LiveOverlayCompositor overlayOutputCompositor,
			LiveInstantEffectRenderer instantEffects) {
			_sceneManager = sceneManager ?? throw new ArgumentNullException(nameof(sceneManager));
			_renderPool = renderPool ?? throw new ArgumentNullException(nameof(renderPool));
			_createOutput = createOutput ?? throw new ArgumentNullException(nameof(createOutput));
			Compositor = compositor ?? throw new ArgumentNullException(nameof(compositor));
			OverlayOutputCompositor = overlayOutputCompositor ?? throw new ArgumentNullException(nameof(overlayOutputCompositor));
			InstantEffects = instantEffects ?? throw new ArgumentNullException(nameof(instantEffects));
			PatchDefinitions = (patchDefinitions ?? throw new ArgumentNullException(nameof(patchDefinitions))).ToArray();
			if (PatchDefinitions.Count == 0) throw new ArgumentException("A live graph requires patches.");
		}

		public LiveProgramOutput CreateOutput(PatchDefinition patch, LiveRenderSize renderSize)
			=> _createOutput(patch, renderSize);

		public void Dispose() {
			InstantEffects.Dispose();
			OverlayOutputCompositor.Dispose();
			Compositor.Dispose();
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

		public LiveProgramOutput(RenderTexture programTexture, RenderTexture shaderGraphTexture,
			LiveProgramGraph programGraph) {
			ProgramTexture = programTexture ?? throw new ArgumentNullException(nameof(programTexture));
			_shaderGraphTexture = shaderGraphTexture ?? throw new ArgumentNullException(nameof(shaderGraphTexture));
			_programGraph = programGraph ?? throw new ArgumentNullException(nameof(programGraph));
		}

		public void Evaluate(double deltaSeconds, BeatClockFrame bpmFrame) => _programGraph.Evaluate(deltaSeconds, bpmFrame);

		public void SceneUpdate(double deltaSeconds) => _programGraph.SceneUpdate(deltaSeconds);

		public void SetSceneActive(bool active) => _programGraph.SetSceneActive(active);

		public void Render(double graphTime, ulong frameNumber) {
			_programGraph.Render(_shaderGraphTexture, graphTime, frameNumber);
			Graphics.Blit(_shaderGraphTexture, ProgramTexture);
		}

		public bool TrySetGraphParameter(string nodeId, string parameterId, ParameterValue value, out string rejectionReason)
			=> _programGraph.TrySetParameter(nodeId, parameterId, value, out rejectionReason);

		public bool TryGetSceneParameter(string nodeId, string parameterId, out LiveSceneRoot root, out LiveParameterDefinition definition)
			=> _programGraph.TryGetSceneParameter(nodeId, parameterId, out root, out definition);

		public void Dispose() {
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

	internal readonly struct LiveOverlayInput {
		public LiveSequencerCellMode Mode { get; }
		public Texture Texture { get; }

		public LiveOverlayInput(LiveSequencerCellMode mode, Texture texture) {
			if (mode == LiveSequencerCellMode.Off) throw new ArgumentException("An overlay input requires an active compositing mode.", nameof(mode));
			Mode = mode;
			Texture = texture ?? throw new ArgumentNullException(nameof(texture));
		}
	}

	internal sealed class LiveMainCueFader {
		private int m_ReferenceCueIndex;
		private AnimationCurve m_ResponseCurve;
		private float m_ReferencePosition;
		private bool m_HasReferencePosition;
		private float m_CurrentPosition;
		private bool m_HasCurrentPosition;

		public int ReferenceCueIndex => m_ReferenceCueIndex;
		public int AlternateCueIndex => 1 - m_ReferenceCueIndex;
		public int DominantCueIndex => AlternateOpacity > .5f ? AlternateCueIndex : ReferenceCueIndex;
		public float AlternateOpacity { get; private set; }

		public LiveMainCueFader(int referenceCueIndex = 0, AnimationCurve responseCurve = null) {
			SetResponseCurve(responseCurve);
			SetReferenceCue(referenceCueIndex);
		}

		public void SetResponseCurve(AnimationCurve responseCurve) {
			m_ResponseCurve = responseCurve ?? AnimationCurve.Linear(0f, 0f, 1f, 1f);
		}

		public void SetPosition(float normalizedPosition) {
			if (float.IsNaN(normalizedPosition) || float.IsInfinity(normalizedPosition))
				throw new ArgumentOutOfRangeException(nameof(normalizedPosition));
			var position = Mathf.Clamp01(normalizedPosition);
			m_CurrentPosition = position;
			m_HasCurrentPosition = true;
			if (!m_HasReferencePosition) {
				m_ReferencePosition = position;
				m_HasReferencePosition = true;
				AlternateOpacity = 0f;
				return;
			}

			var offset = position - m_ReferencePosition;
			var availableDistance = offset > 0f ? 1f - m_ReferencePosition : m_ReferencePosition;
			var normalizedOpacity = availableDistance <= Mathf.Epsilon
				? 0f
				: Mathf.Clamp01(Mathf.Abs(offset) / availableDistance);
			var curvedOpacity = m_ResponseCurve.Evaluate(normalizedOpacity);
			AlternateOpacity = float.IsNaN(curvedOpacity) || float.IsInfinity(curvedOpacity)
				? normalizedOpacity
				: Mathf.Clamp01(curvedOpacity);
		}

		public void ToggleReferenceCue() {
			SetReferenceCue(1 - DominantCueIndex);
		}

		public void SetReferenceCue(int cueIndex) {
			if (cueIndex < 0 || cueIndex >= LiveGraphRuntime.MainCueCount) throw new ArgumentOutOfRangeException(nameof(cueIndex));
			m_ReferenceCueIndex = cueIndex;
			AlternateOpacity = 0f;
			m_ReferencePosition = m_CurrentPosition;
			m_HasReferencePosition = m_HasCurrentPosition;
		}
	}

	internal sealed class LiveOverlayCompositor : IDisposable {
		private const string NormalTypeId = "shitdesigner.shader.blend.normal_alpha_over";
		private const string AddTypeId = "shitdesigner.shader.blend.add";
		private const string MultiplyTypeId = "shitdesigner.shader.blend.multiply";
		private const string SubtractTypeId = "shitdesigner.shader.blend.subtract";
		private const string DifferenceTypeId = "shitdesigner.shader.blend.difference";
		private const string InvertTypeId = "shitdesigner.shader.color.invert";
		private const string CrossfadeTypeId = "shitdesigner.shader.transition.crossfade";

		private static readonly IReadOnlyDictionary<LiveSequencerCellMode, string> BlendTypeIds =
			new Dictionary<LiveSequencerCellMode, string> {
				{ LiveSequencerCellMode.Normal, NormalTypeId },
				{ LiveSequencerCellMode.Add, AddTypeId },
				{ LiveSequencerCellMode.Multiply, MultiplyTypeId },
				{ LiveSequencerCellMode.Subtract, SubtractTypeId },
				{ LiveSequencerCellMode.Difference, DifferenceTypeId }
			};

		public static IReadOnlyList<NodeTypeId> RequiredNodeTypeIds { get; } = BlendTypeIds.Values
			.Concat(new[] { InvertTypeId, CrossfadeTypeId })
			.Select(value => new NodeTypeId(value))
			.ToArray();

		private readonly Dictionary<LiveSequencerCellMode, ShaderPassGraphRuntimeNode> m_BlendNodes =
			new Dictionary<LiveSequencerCellMode, ShaderPassGraphRuntimeNode>();
		private readonly RenderTexture[] m_Scratch = new RenderTexture[2];
		private readonly ShaderPassGraphRuntimeNode m_InvertNode;
		private readonly ShaderPassGraphRuntimeNode m_CrossfadeNode;
		private readonly RenderTexture m_InvertedOverlay;
		private float m_MainCompositeOpacity = .5f;
		private bool m_Disposed;

		public RenderTexture Output { get; }

		public LiveOverlayCompositor(IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> shaderDefinitions,
			RenderTexturePool renderPool, LiveRenderSize renderSize) {
			if (shaderDefinitions == null) throw new ArgumentNullException(nameof(shaderDefinitions));
			if (renderPool == null) throw new ArgumentNullException(nameof(renderPool));
			try {
				Output = CreateTexture("ShitDesigner.Main.Composite.Output", renderSize);
				ClearTexture(Output);
				m_Scratch[0] = CreateTexture("ShitDesigner.Main.Composite.Scratch.0", renderSize);
				m_Scratch[1] = CreateTexture("ShitDesigner.Main.Composite.Scratch.1", renderSize);
				m_InvertedOverlay = CreateTexture("ShitDesigner.Main.Composite.InvertedOverlay", renderSize);
				foreach (var pair in BlendTypeIds)
					m_BlendNodes.Add(pair.Key, CreateNode(pair.Value, shaderDefinitions, renderPool));
				m_InvertNode = CreateNode(InvertTypeId, shaderDefinitions, renderPool);
				m_CrossfadeNode = CreateNode(CrossfadeTypeId, shaderDefinitions, renderPool);
			}
			catch {
				Dispose();
				throw;
			}
		}

		public RenderTexture Render(Texture main, Texture alternateMain, float alternateOpacity, bool compositeMain,
			IReadOnlyList<LiveOverlayInput> overlays, ulong frameNumber, double graphTime) {
			if (m_Disposed) throw new ObjectDisposedException(nameof(LiveOverlayCompositor));
			if (main == null) throw new ArgumentNullException(nameof(main));
			Texture accumulated = main;
			var scratchIndex = 0;
			var mainMix = compositeMain ? m_MainCompositeOpacity : alternateOpacity;
			if (alternateMain != null && mainMix > 0f) {
				if (mainMix >= 1f) accumulated = alternateMain;
				else {
					if (!m_CrossfadeNode.TrySetDirectParameter("progress", ParameterValue.FromFloat(mainMix), out var rejectionReason))
						throw new InvalidOperationException("The Main Cue crossfade could not be configured: " + rejectionReason);
					var inputs = new Dictionary<PortId, Texture> {
						{ new PortId("a"), main },
						{ new PortId("b"), alternateMain }
					};
					var crossfaded = m_CrossfadeNode.Render(inputs, m_Scratch[0], frameNumber, graphTime);
					if (crossfaded.IsFailure) throw new InvalidOperationException(crossfaded.Error.Message);
					accumulated = m_Scratch[0];
					scratchIndex = 1;
				}
			}
			if (overlays == null || overlays.Count == 0) {
				if (accumulated is RenderTexture mainTexture) return mainTexture;
				Graphics.Blit(accumulated, Output);
				return Output;
			}
			foreach (var overlay in overlays) {
				var foreground = overlay.Texture;
				var mode = overlay.Mode;
				if (mode == LiveSequencerCellMode.Invert) {
					var inverted = m_InvertNode.Render(foreground as RenderTexture, m_InvertedOverlay, frameNumber, graphTime);
					if (inverted.IsFailure) throw new InvalidOperationException(inverted.Error.Message);
					foreground = m_InvertedOverlay;
					mode = LiveSequencerCellMode.Normal;
				}
				if (!m_BlendNodes.TryGetValue(mode, out var blendNode))
					throw new InvalidOperationException("The overlay compositing mode is not available: " + mode + ".");
				var inputs = new Dictionary<PortId, Texture> {
					{ new PortId("a"), foreground },
					{ new PortId("b"), accumulated }
				};
				var blended = blendNode.Render(inputs, m_Scratch[scratchIndex], frameNumber, graphTime);
				if (blended.IsFailure) throw new InvalidOperationException(blended.Error.Message);
				accumulated = m_Scratch[scratchIndex];
				scratchIndex = 1 - scratchIndex;
			}
			Graphics.Blit(accumulated, Output);
			return Output;
		}

		public void SetMainCompositeOpacity(float opacity) {
			m_MainCompositeOpacity = Mathf.Clamp01(opacity);
		}

		public void Dispose() {
			if (m_Disposed) return;
			m_Disposed = true;
			foreach (var node in m_BlendNodes.Values) node.Dispose();
			m_BlendNodes.Clear();
			m_InvertNode?.Dispose();
			m_CrossfadeNode?.Dispose();
			ReleaseTexture(m_InvertedOverlay);
			foreach (var texture in m_Scratch) ReleaseTexture(texture);
			ReleaseTexture(Output);
		}

		private static ShaderPassGraphRuntimeNode CreateNode(string typeId,
			IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> shaderDefinitions, RenderTexturePool renderPool) {
			var nodeTypeId = new NodeTypeId(typeId);
			if (!shaderDefinitions.TryGetValue(nodeTypeId, out var definition))
				throw new InvalidOperationException("The overlay compositor shader is unavailable: " + typeId + ".");
			var binding = definition.Entry.ToShaderBinding();
			var record = new RuntimeNodeCreateInfo(NodeInstanceId.New(), nodeTypeId, definition.Entry.SchemaVersion,
				definition.Entry.DisplayName, true, 0f, 0f, LiveGraphBootstrap.BuildRuntimeParameters(definition.Entry, null));
			var node = new ShaderPassGraphRuntimeNode(record, 1UL,
				new ShaderMaterialBinding(binding.ShaderKey, definition.Shader, outputPass: binding.OutputPass, descriptor: binding),
				renderPool, "shitdesigner.main.compositor." + typeId, binding.Family == ShaderNodeFamily.Generator,
				binding.Family == ShaderNodeFamily.Composite);
			if (binding.Parameters.Any(parameter => parameter.ParameterId.Value == "amount")
				&& !node.TrySetDirectParameter("amount", ParameterValue.FromFloat(1f), out var rejectionReason)) {
				node.Dispose();
				throw new InvalidOperationException("The overlay compositor amount could not be configured: " + rejectionReason);
			}
			return node;
		}

		private static RenderTexture CreateTexture(string name, LiveRenderSize renderSize) {
			var texture = new RenderTexture(renderSize.Width, renderSize.Height, 0, RenderTextureFormat.ARGBHalf) {
				name = name,
				useMipMap = false,
				autoGenerateMips = false
			};
			if (texture.Create()) return texture;
			ReleaseTexture(texture);
			throw new InvalidOperationException("An overlay compositor texture could not be created.");
		}

		private static void ClearTexture(RenderTexture texture) {
			var previous = RenderTexture.active;
			RenderTexture.active = texture;
			GL.Clear(true, true, Color.black);
			RenderTexture.active = previous;
		}

		private static void ReleaseTexture(RenderTexture texture) {
			if (texture == null) return;
			texture.Release();
			if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(texture);
			else UnityEngine.Object.DestroyImmediate(texture);
		}
	}

	internal sealed class LiveInstantEffectRenderer : IDisposable {
		internal const int LiveParameterCount = 8;
		private const string ParameterIdPrefix = "instant-fx/";
		private static readonly string[] m_DefaultLiveParameterOrder = { "amount", "mix", "gain", "radius", "scale", "frequency", "detail", "speed" };

		private sealed class Slot : IDisposable {
			public ShaderPassGraphRuntimeNode Node { get; }
			public ShaderNodeBinding Binding { get; }
			public ShaderNodeManifestEntry Entry { get; }
			public Dictionary<string, ParameterValue> ParameterValues { get; }

			public Slot(ShaderPassGraphRuntimeNode node, ShaderNodeBinding binding, ShaderNodeManifestEntry entry,
				IEnumerable<RuntimeParameterSnapshot> parameters) {
				Node = node ?? throw new ArgumentNullException(nameof(node));
				Binding = binding ?? throw new ArgumentNullException(nameof(binding));
				Entry = entry ?? throw new ArgumentNullException(nameof(entry));
				ParameterValues = (parameters ?? throw new ArgumentNullException(nameof(parameters)))
					.ToDictionary(parameter => parameter.Id.Value, parameter => parameter.Value, StringComparer.Ordinal);
			}

			public void Dispose() => Node.Dispose();
		}

		private readonly IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> m_ShaderDefinitions;
		private readonly RenderTexturePool m_RenderPool;
		private readonly Slot[] m_Slots = new Slot[InstantEffectTriggerContract.TriggerCount];
		private readonly RenderTexture[] m_Scratch = new RenderTexture[2];
		private bool m_Disposed;

		public LiveInstantEffectRenderer(IReadOnlyDictionary<NodeTypeId, LiveProgramShaderDefinition> shaderDefinitions,
			RenderTexturePool renderPool, LiveRenderSize renderSize) {
			m_ShaderDefinitions = shaderDefinitions ?? throw new ArgumentNullException(nameof(shaderDefinitions));
			m_RenderPool = renderPool ?? throw new ArgumentNullException(nameof(renderPool));
			try {
				m_Scratch[0] = CreateTexture("ShitDesigner.Main.InstantEffect.Scratch.0", renderSize);
				m_Scratch[1] = CreateTexture("ShitDesigner.Main.InstantEffect.Scratch.1", renderSize);
			}
			catch {
				foreach (var texture in m_Scratch) ReleaseTexture(texture);
				throw;
			}
		}

		public bool TryAssign(int cueIndex, string typeId, out string rejectionReason) {
			if (m_Disposed) throw new ObjectDisposedException(nameof(LiveInstantEffectRenderer));
			if (cueIndex < 0 || cueIndex >= m_Slots.Length) throw new ArgumentOutOfRangeException(nameof(cueIndex));
			if (!NodeTypeId.TryParse(typeId, out var nodeTypeId) || !m_ShaderDefinitions.TryGetValue(nodeTypeId, out var definition)) {
				rejectionReason = "The requested FX node is unavailable.";
				return false;
			}

			ShaderPassGraphRuntimeNode node = null;
			Slot replacement;
			try {
				var binding = definition.Entry.ToShaderBinding();
				var parameters = BuildInstantEffectParameters(definition.Entry, definition.Shader);
				var record = new RuntimeNodeCreateInfo(NodeInstanceId.New(), nodeTypeId, definition.Entry.SchemaVersion,
					definition.Entry.DisplayName, true, 0f, 0f, parameters);
				node = new ShaderPassGraphRuntimeNode(record, 1UL,
					new ShaderMaterialBinding(binding.ShaderKey, definition.Shader, outputPass: binding.OutputPass, descriptor: binding),
					m_RenderPool, "shitdesigner.main.instant_effect." + cueIndex, binding.Family == ShaderNodeFamily.Generator,
					binding.Family == ShaderNodeFamily.Composite);
				replacement = new Slot(node, binding, definition.Entry, parameters);
				ConfigureFullStrength(replacement);
			}
			catch (Exception exception) {
				node?.Dispose();
				rejectionReason = exception.Message;
				return false;
			}

			var previous = m_Slots[cueIndex];
			m_Slots[cueIndex] = replacement;
			previous?.Dispose();
			rejectionReason = string.Empty;
			return true;
		}

		public void Clear(int cueIndex) {
			if (m_Disposed) throw new ObjectDisposedException(nameof(LiveInstantEffectRenderer));
			if (cueIndex < 0 || cueIndex >= m_Slots.Length) throw new ArgumentOutOfRangeException(nameof(cueIndex));
			var previous = m_Slots[cueIndex];
			m_Slots[cueIndex] = null;
			previous?.Dispose();
		}

		public LiveParameterDefinition[] GetParameterDefinitions(int cueIndex) {
			if (m_Disposed) throw new ObjectDisposedException(nameof(LiveInstantEffectRenderer));
			if (cueIndex < 0 || cueIndex >= m_Slots.Length) return Array.Empty<LiveParameterDefinition>();
			var slot = m_Slots[cueIndex];
			if (slot == null) return Array.Empty<LiveParameterDefinition>();
			return LiveParameters(slot).Select(parameter => ToDefinition(cueIndex, parameter, slot.ParameterValues[parameter.Id.Value])).ToArray();
		}

		public bool TrySetParameter(int cueIndex, string parameterId, ParameterValue value, out string rejectionReason) {
			if (m_Disposed) throw new ObjectDisposedException(nameof(LiveInstantEffectRenderer));
			if (cueIndex < 0 || cueIndex >= m_Slots.Length) {
				rejectionReason = "The instant FX Cue does not exist.";
				return false;
			}
			var slot = m_Slots[cueIndex];
			var parameter = slot?.Entry.Parameters.FirstOrDefault(candidate => candidate.Id.Value == parameterId);
			if (parameter == null || !LiveParameters(slot).Contains(parameter)) {
				rejectionReason = "The instant FX parameter is not exposed for live control.";
				return false;
			}
			if (parameter.Type != value.Type) {
				rejectionReason = "The instant FX parameter type does not match.";
				return false;
			}
			if (parameter.Minimum.HasValue && parameter.Maximum.HasValue && ParameterValue.IsLogicalControlTargetType(parameter.Type)) {
				var clamped = ParameterValue.Clamp(value, parameter.Minimum.Value, parameter.Maximum.Value);
				if (clamped.IsFailure) {
					rejectionReason = clamped.Error.Message;
					return false;
				}
				value = clamped.Value;
			}
			if (!slot.Node.TrySetDirectParameter(parameterId, value, out rejectionReason)) return false;
			slot.ParameterValues[parameterId] = value;
			return true;
		}

		internal static string ParameterAddress(int cueIndex, string parameterId)
			=> ParameterIdPrefix + cueIndex + "/" + parameterId;

		internal static bool TryParseParameterAddress(string address, out int cueIndex, out string parameterId) {
			cueIndex = -1;
			parameterId = string.Empty;
			if (string.IsNullOrEmpty(address) || !address.StartsWith(ParameterIdPrefix, StringComparison.Ordinal)) return false;
			var separator = address.IndexOf('/', ParameterIdPrefix.Length);
			if (separator < 0 || !int.TryParse(address.Substring(ParameterIdPrefix.Length, separator - ParameterIdPrefix.Length), out cueIndex)) return false;
			parameterId = address.Substring(separator + 1);
			return cueIndex >= 0 && cueIndex < InstantEffectTriggerContract.TriggerCount && !string.IsNullOrEmpty(parameterId);
		}

		public RenderTexture Render(RenderTexture source, IReadOnlyList<int> triggerNumbers, ulong frameNumber, double graphTime) {
			if (m_Disposed) throw new ObjectDisposedException(nameof(LiveInstantEffectRenderer));
			if (source == null) throw new ArgumentNullException(nameof(source));
			if (triggerNumbers == null || triggerNumbers.Count == 0) return source;
			var output = source;
			var scratchIndex = 0;
			foreach (var triggerNumber in triggerNumbers.Distinct().OrderBy(value => value)) {
				InstantEffectTriggerContract.Validate(triggerNumber);
				var slot = m_Slots[triggerNumber - 1];
				if (slot == null) continue;
				var rendered = slot.Node.Render(BuildInputs(slot.Binding, output), m_Scratch[scratchIndex], frameNumber, graphTime);
				if (rendered.IsFailure) throw new InvalidOperationException(rendered.Error.Message);
				output = m_Scratch[scratchIndex];
				scratchIndex = 1 - scratchIndex;
			}
			return output;
		}

		internal static IReadOnlyDictionary<PortId, Texture> BuildInputs(ShaderNodeBinding binding, Texture source) {
			if (binding == null) throw new ArgumentNullException(nameof(binding));
			if (source == null) throw new ArgumentNullException(nameof(source));
			var imageInputs = binding.Inputs.Where(input => input.Type == NodePortType.ImageFrame && input.Role != ShaderInputRole.History).ToArray();
			var primary = imageInputs.FirstOrDefault(input => input.Role == ShaderInputRole.Primary) ?? imageInputs.FirstOrDefault();
			return imageInputs.Where(input => ReferenceEquals(input, primary) || input.Required)
				.ToDictionary(input => input.PortId, input => source);
		}

		public void Dispose() {
			if (m_Disposed) return;
			m_Disposed = true;
			for (var index = 0; index < m_Slots.Length; index++) {
				m_Slots[index]?.Dispose();
				m_Slots[index] = null;
			}
			foreach (var texture in m_Scratch) ReleaseTexture(texture);
		}

		private static void ConfigureFullStrength(Slot slot) {
			foreach (var parameterId in new[] { "amount", "mix" }) {
				var parameter = slot.Entry.Parameters.FirstOrDefault(candidate => candidate.Id.Value == parameterId && candidate.Type == ParameterType.Float);
				if (parameter == null) continue;
				var value = ParameterValue.FromFloat(1f);
				if (!slot.Node.TrySetDirectParameter(parameterId, value, out var rejectionReason))
					throw new InvalidOperationException("The instant FX strength could not be configured: " + rejectionReason);
				slot.ParameterValues[parameterId] = value;
			}
		}

		private static RuntimeParameterSnapshot[] BuildInstantEffectParameters(ShaderNodeManifestEntry entry, Shader shader) {
			if (entry == null) throw new ArgumentNullException(nameof(entry));
			if (shader == null) throw new ArgumentNullException(nameof(shader));
			var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
			try {
				return entry.Parameters.Select(parameter => new RuntimeParameterSnapshot(parameter.Id, parameter.Type,
					ReadShaderDefault(material, parameter), parameter.Definition.RuntimeStateful)).ToArray();
			}
			finally {
				if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(material);
				else UnityEngine.Object.DestroyImmediate(material);
			}
		}

		private static ParameterValue ReadShaderDefault(Material material, ShaderNodeManifestParameter parameter) {
			if (!material.HasProperty(parameter.Property)) return parameter.DefaultValue;
			switch (parameter.Type) {
				case ParameterType.Float:
					return ParameterValue.FromFloat(material.GetFloat(parameter.Property));
				case ParameterType.Int:
					return ParameterValue.FromInt(Mathf.RoundToInt(material.GetFloat(parameter.Property)));
				case ParameterType.Bool:
					return ParameterValue.FromBool(!Mathf.Approximately(material.GetFloat(parameter.Property), 0f));
				case ParameterType.Vector2:
					var vector2 = material.GetVector(parameter.Property);
					return ParameterValue.FromVector2(new Vector2Value(vector2.x, vector2.y));
				case ParameterType.Vector3:
					var vector3 = material.GetVector(parameter.Property);
					return ParameterValue.FromVector3(new Vector3Value(vector3.x, vector3.y, vector3.z));
				case ParameterType.Vector4:
					var vector4 = material.GetVector(parameter.Property);
					return ParameterValue.FromVector4(new Vector4Value(vector4.x, vector4.y, vector4.z, vector4.w));
				case ParameterType.Color:
					var color = material.GetColor(parameter.Property);
					return ParameterValue.FromColor(new ColorValue(color.r, color.g, color.b, color.a));
				case ParameterType.Enum:
					var enumValue = Mathf.RoundToInt(material.GetFloat(parameter.Property));
					var option = parameter.EnumMapping.FirstOrDefault(item => item.Value == enumValue).Key;
					return string.IsNullOrEmpty(option) ? parameter.DefaultValue : ParameterValue.FromEnum(option);
				default:
					return parameter.DefaultValue;
			}
		}

		private static IEnumerable<ShaderNodeManifestParameter> LiveParameters(Slot slot) {
			var available = slot.Entry.Parameters.Where(parameter => !parameter.Definition.IsHidden && !parameter.Definition.IsReadOnly
				&& IsLiveParameterType(parameter.Type)).ToDictionary(parameter => parameter.Id.Value, StringComparer.Ordinal);
			var selected = new List<ShaderNodeManifestParameter>(LiveParameterCount);
			foreach (var parameterId in LiveParameterOrder(slot.Entry.Family))
				if (available.TryGetValue(parameterId, out var parameter) && !selected.Contains(parameter)) selected.Add(parameter);
			foreach (var parameter in slot.Entry.Parameters)
				if (selected.Count < LiveParameterCount && available.ContainsKey(parameter.Id.Value) && !selected.Contains(parameter)) selected.Add(parameter);
			return selected.Take(LiveParameterCount);
		}

		private static IEnumerable<string> LiveParameterOrder(ShaderNodeFamily family) {
			switch (family) {
				case ShaderNodeFamily.Color:
					return new[] { "amount", "mix", "gain", "exposure", "gamma", "hue", "saturation", "contrast" };
				case ShaderNodeFamily.Geometry:
					return new[] { "amount", "scale", "radius", "angle", "center", "frequency", "detail", "displacement" };
				case ShaderNodeFamily.Glitch:
					return new[] { "amount", "frequency", "detail", "speed", "phase", "seed", "scale", "radius" };
				case ShaderNodeFamily.Convolution:
				case ShaderNodeFamily.Stylize:
					return new[] { "amount", "radius", "gain", "frequency", "detail", "softness", "threshold", "mix" };
				case ShaderNodeFamily.Key:
					return new[] { "amount", "threshold", "softness", "gain", "radius", "frequency", "center", "mix" };
				default:
					return m_DefaultLiveParameterOrder;
			}
		}

		private static bool IsLiveParameterType(ParameterType type) {
			switch (type) {
				case ParameterType.Float:
				case ParameterType.Int:
				case ParameterType.Bool:
				case ParameterType.Vector2:
				case ParameterType.Vector3:
				case ParameterType.Vector4:
				case ParameterType.Color:
				case ParameterType.Enum:
					return true;
				default:
					return false;
			}
		}

		private static LiveParameterDefinition ToDefinition(int cueIndex, ShaderNodeManifestParameter parameter, ParameterValue value) {
			var id = ParameterAddress(cueIndex, parameter.Id.Value);
			var displayName = "FX " + parameter.DisplayName;
			if (parameter.Type == ParameterType.Float && parameter.Minimum.HasValue && parameter.Maximum.HasValue)
				return new LiveParameterDefinition(id, displayName, parameter.Minimum.Value.AsFloat(), parameter.Maximum.Value.AsFloat(), value.AsFloat());
			return new LiveParameterDefinition(id, displayName, value);
		}

		private static RenderTexture CreateTexture(string name, LiveRenderSize renderSize) {
			var texture = new RenderTexture(renderSize.Width, renderSize.Height, 0, RenderTextureFormat.ARGBHalf) {
				name = name,
				useMipMap = false,
				autoGenerateMips = false
			};
			if (texture.Create()) return texture;
			ReleaseTexture(texture);
			throw new InvalidOperationException("An instant FX output texture could not be created.");
		}

		private static void ReleaseTexture(RenderTexture texture) {
			if (texture == null) return;
			texture.Release();
			if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(texture);
			else UnityEngine.Object.DestroyImmediate(texture);
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

	internal static class LiveUnityVideoClock {
		public static void Configure(VideoPlayer player) {
			if (player == null) throw new ArgumentNullException(nameof(player));
			player.timeReference = VideoTimeReference.InternalTime;
			player.timeUpdateMode = VideoTimeUpdateMode.GameTime;
		}
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

		public void SetSceneActive(bool active) {
			var result = active ? m_Runtime.Activate() : m_Runtime.Deactivate();
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
		private double m_DeltaSeconds;
		public string Id { get; }
		public RenderTexture Target { get; }
		public IReadOnlyDictionary<PortId, string> Inputs { get; }

		public LiveProgramShaderGraphNode(string id, ShaderPassGraphRuntimeNode runtime, RenderTexture target, IReadOnlyDictionary<PortId, string> inputs) {
			Id = id;
			_runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
			Target = target ?? throw new ArgumentNullException(nameof(target));
			Inputs = inputs ?? throw new ArgumentNullException(nameof(inputs));
		}

		public void Evaluate(double deltaSeconds, BeatClockFrame bpmFrame) => m_DeltaSeconds = Math.Max(0d, deltaSeconds);

		public void SceneUpdate(double deltaSeconds) { }

		public void Render(IReadOnlyDictionary<string, Texture> outputs, double graphTime, ulong frameNumber) {
			var inputs = Inputs.ToDictionary(input => input.Key, input => outputs.TryGetValue(input.Value, out var texture) ? texture : null);
			var result = _runtime.Render(inputs, Target, frameNumber, graphTime, deltaTime: m_DeltaSeconds);
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

	internal sealed class LiveVideoTransportState {
		private const double PositionTolerance = 1e-9d;
		private double m_AnchorGraphTime;

		public bool Playing { get; private set; }
		public double PlayheadSeconds { get; private set; }
		public float Speed { get; private set; }
		public float PlaybackSpeed => Mathf.Clamp(Speed * m_BpmSpeedMultiplier, 0f, 4f);
		public bool Loop { get; private set; }
		public bool SeekPending { get; private set; }
		public bool SettingsPending { get; private set; }
		private float m_BpmSpeedMultiplier = 1f;

		public LiveVideoTransportState(bool playing, double playheadSeconds, float speed, bool loop) {
			if (double.IsNaN(playheadSeconds) || double.IsInfinity(playheadSeconds) || playheadSeconds < 0d)
				throw new ArgumentOutOfRangeException(nameof(playheadSeconds));
			if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f || speed > 4f)
				throw new ArgumentOutOfRangeException(nameof(speed));
			Playing = playing;
			PlayheadSeconds = playheadSeconds;
			Speed = speed;
			Loop = loop;
			SeekPending = playheadSeconds > PositionTolerance;
		}

		public double LogicalPosition(double graphTime, double durationSeconds) {
			var position = Playing
				? PlayheadSeconds + Math.Max(0d, graphTime - m_AnchorGraphTime) * PlaybackSpeed
				: PlayheadSeconds;
			if (durationSeconds <= 0d) return position;
			return Loop ? position % durationSeconds : Math.Min(position, durationSeconds);
		}

		public bool TrySetParameter(string parameterId, ParameterValue value, double graphTime, double durationSeconds,
			out string rejectionReason) {
			if (parameterId == VideoPlayerContract.PlayingParameterId) {
				if (value.Type != ParameterType.Bool) return Reject("Playing requires a Bool value.", out rejectionReason);
				Reanchor(graphTime, durationSeconds);
				Playing = value.AsBool();
				rejectionReason = string.Empty;
				return true;
			}
			if (parameterId == VideoPlayerContract.PlayheadParameterId) {
				if (value.Type != ParameterType.Float) return Reject("Playhead requires a Float value.", out rejectionReason);
				var playhead = value.AsFloat();
				if (float.IsNaN(playhead) || float.IsInfinity(playhead) || playhead < 0f)
					return Reject("Playhead must be finite and non-negative.", out rejectionReason);
				PlayheadSeconds = durationSeconds > 0d ? Math.Min(playhead, durationSeconds) : playhead;
				m_AnchorGraphTime = graphTime;
				SeekPending = true;
				rejectionReason = string.Empty;
				return true;
			}
			if (parameterId == VideoPlayerContract.SpeedParameterId) {
				if (value.Type != ParameterType.Float) return Reject("Speed requires a Float value.", out rejectionReason);
				var speed = value.AsFloat();
				if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f || speed > 4f)
					return Reject("Speed must be between 0 and 4.", out rejectionReason);
				Reanchor(graphTime, durationSeconds);
				Speed = speed;
				SettingsPending = true;
				rejectionReason = string.Empty;
				return true;
			}
			if (parameterId == VideoPlayerContract.LoopParameterId) {
				if (value.Type != ParameterType.Bool) return Reject("Loop requires a Bool value.", out rejectionReason);
				Reanchor(graphTime, durationSeconds);
				Loop = value.AsBool();
				SettingsPending = true;
				rejectionReason = string.Empty;
				return true;
			}
			return Reject("The video transport parameter is unknown.", out rejectionReason);
		}

		public void MarkSeekApplied() => SeekPending = false;
		public void MarkSettingsApplied() => SettingsPending = false;

		public void SetBpmSpeedMultiplier(float multiplier, double graphTime, double durationSeconds) {
			if (float.IsNaN(multiplier) || float.IsInfinity(multiplier) || multiplier < 0f)
				throw new ArgumentOutOfRangeException(nameof(multiplier));
			if (Mathf.Approximately(m_BpmSpeedMultiplier, multiplier)) return;
			Reanchor(graphTime, durationSeconds);
			m_BpmSpeedMultiplier = multiplier;
			SettingsPending = true;
		}

		private void Reanchor(double graphTime, double durationSeconds) {
			PlayheadSeconds = LogicalPosition(graphTime, durationSeconds);
			m_AnchorGraphTime = graphTime;
		}

		private static bool Reject(string reason, out string rejectionReason) {
			rejectionReason = reason;
			return false;
		}
	}

	internal sealed class LiveProgramFileVideoGraphNode : ILiveProgramGraphNode {
		private readonly IVideoBackendHandle m_Backend;
		private readonly HapUnityGraphicsBridge m_HapBridge;
		private readonly IVideoFrameConversionPass m_Conversion;
		private readonly VideoProbeResult m_Probe;
		private readonly RenderTexture m_Target;
		private readonly LiveVideoTransportState m_Transport;
		private readonly float m_VideoBpm;
		private double m_LastGraphTime;
		private bool m_AwaitingSeekCompletion;
		private bool m_Disposed;

		public string Id { get; }
		public RenderTexture Target => m_Target;

		public LiveProgramFileVideoGraphNode(string id, RenderTexture target, string videoPath, bool playing, double playhead, float speed, bool loop, float videoBpm,
			Material videoConversionMaterial, Material hapPremultiplyMaterial, Material hapYCoCgMaterial,
			Material hapAlphaMaterial, ComputeShader hapDecodeShader) {
			if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A live Program video node ID is required.", nameof(id));
			if (target == null) throw new ArgumentNullException(nameof(target));
			if (string.IsNullOrWhiteSpace(videoPath)) throw new ArgumentException("A live Program video file is required.", nameof(videoPath));
			if (double.IsNaN(playhead) || double.IsInfinity(playhead) || playhead < 0d) throw new ArgumentOutOfRangeException(nameof(playhead));
			if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f || speed > 4f) throw new ArgumentOutOfRangeException(nameof(speed));
			if (float.IsNaN(videoBpm) || float.IsInfinity(videoBpm) || videoBpm < 1f) throw new ArgumentOutOfRangeException(nameof(videoBpm));

			Id = id.Trim();
			m_Target = target;
			m_Transport = new LiveVideoTransportState(playing, playhead, speed, loop);
			m_VideoBpm = videoBpm;

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
					var unityBackend = new UnityVideoBackend(NodeInstanceId.New(), 1UL);
					LiveUnityVideoClock.Configure(unityBackend.Player);
					backend = unityBackend;
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

		public void Evaluate(double deltaSeconds, BeatClockFrame bpmFrame) {
			m_Transport.SetBpmSpeedMultiplier(bpmFrame.IsAvailable ? bpmFrame.Bpm / m_VideoBpm : 1f,
				m_LastGraphTime, m_Probe.DurationSeconds);
		}

		public void SceneUpdate(double deltaSeconds) { }

		public void Render(IReadOnlyDictionary<string, Texture> outputs, double graphTime, ulong frameNumber) {
			if (m_Disposed) throw new ObjectDisposedException(nameof(LiveProgramFileVideoGraphNode));
			m_LastGraphTime = graphTime;
			ApplyPendingTransportSettings();
			var logicalPosition = m_Transport.LogicalPosition(graphTime, m_Probe.DurationSeconds);
			if (m_AwaitingSeekCompletion && IsReady()) m_AwaitingSeekCompletion = false;
			if (m_Transport.SeekPending && IsReady()) {
				var seek = m_Backend.Seek(logicalPosition);
				if (seek.IsFailure) throw new InvalidOperationException(seek.Error.Message);
				m_Transport.MarkSeekApplied();
				m_AwaitingSeekCompletion = true;
			}
			if (m_Backend.BackendKind == VideoBackendKind.HapVideoBackend) {
				var sync = m_Backend.SyncToGraphClock(logicalPosition, demanded: true);
				if (sync.IsFailure) throw new InvalidOperationException(sync.Error.Message);
			}

			if (!m_AwaitingSeekCompletion && (IsReady() || m_Backend.BackendKind == VideoBackendKind.HapVideoBackend)) {
				if (m_Transport.Playing && m_Backend.State != VideoBackendState.Playing) {
					var play = m_Backend.Play();
					if (play.IsFailure) throw new InvalidOperationException(play.Error.Message);
				}
				else if (!m_Transport.Playing && m_Backend.State == VideoBackendState.Playing) {
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
			return m_Transport.TrySetParameter(parameterId, value, m_LastGraphTime, m_Probe.DurationSeconds,
				out rejectionReason);
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

		private void ApplyPendingTransportSettings() {
			if (!m_Transport.SettingsPending) return;
			if (m_Backend.BackendKind == VideoBackendKind.HapVideoBackend && !IsReady()) return;
			var speed = m_Backend.SetSpeed(m_Transport.PlaybackSpeed);
			if (speed.IsFailure) throw new InvalidOperationException(speed.Error.Message);
			var loop = m_Backend.SetLoop(m_Transport.Loop);
			if (loop.IsFailure) throw new InvalidOperationException(loop.Error.Message);
			m_Transport.MarkSettingsApplied();
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
		private readonly LiveVideoTransportState m_Transport;
		private readonly float m_VideoBpm;
		private double m_LastGraphTime;
		private bool _playheadApplied;
		private bool _disposed;

		public string Id { get; }
		public RenderTexture Target => _target;

		public LiveProgramVideoGraphNode(string id, RenderTexture target, VideoClip clip, bool playing, double playhead, float speed, bool loop, float videoBpm) {
			if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A live Program video node ID is required.", nameof(id));
			if (target == null) throw new ArgumentNullException(nameof(target));
			if (clip == null) throw new ArgumentNullException(nameof(clip));
			if (double.IsNaN(playhead) || double.IsInfinity(playhead) || playhead < 0d) throw new ArgumentOutOfRangeException(nameof(playhead));
			if (float.IsNaN(speed) || float.IsInfinity(speed) || speed < 0f || speed > 4f) throw new ArgumentOutOfRangeException(nameof(speed));
			if (float.IsNaN(videoBpm) || float.IsInfinity(videoBpm) || videoBpm < 1f) throw new ArgumentOutOfRangeException(nameof(videoBpm));

			Id = id.Trim();
			_target = target;
			m_Transport = new LiveVideoTransportState(playing, playhead, speed, loop);
			m_VideoBpm = videoBpm;
			_host = new GameObject("ShitDesigner.Main.Video." + Id);
			try {
				_player = _host.AddComponent<VideoPlayer>();
				_player.playOnAwake = false;
				_player.waitForFirstFrame = true;
				_player.renderMode = VideoRenderMode.APIOnly;
				_player.audioOutputMode = VideoAudioOutputMode.None;
				_player.sendFrameReadyEvents = false;
				LiveUnityVideoClock.Configure(_player);
				_player.source = UnityEngine.Video.VideoSource.VideoClip;
				_player.clip = clip;
				_player.isLooping = m_Transport.Loop;
				_player.playbackSpeed = m_Transport.PlaybackSpeed;
				_player.Prepare();
			}
			catch {
				DestroyObject(_host);
				throw;
			}
		}

		public void Evaluate(double deltaSeconds, BeatClockFrame bpmFrame) {
			m_Transport.SetBpmSpeedMultiplier(bpmFrame.IsAvailable ? bpmFrame.Bpm / m_VideoBpm : 1f,
				m_LastGraphTime, _player == null ? 0d : _player.length);
		}

		public void SceneUpdate(double deltaSeconds) { }

		public void Render(IReadOnlyDictionary<string, Texture> outputs, double graphTime, ulong frameNumber) {
			if (_disposed) throw new ObjectDisposedException(nameof(LiveProgramVideoGraphNode));
			m_LastGraphTime = graphTime;
			if (_player.isPrepared) {
				if (m_Transport.SettingsPending) {
					_player.isLooping = m_Transport.Loop;
					_player.playbackSpeed = m_Transport.PlaybackSpeed;
					m_Transport.MarkSettingsApplied();
				}
				var logicalPosition = m_Transport.LogicalPosition(graphTime, _player.length);
				if (!_playheadApplied || m_Transport.SeekPending) {
					_player.time = logicalPosition;
					_playheadApplied = true;
					m_Transport.MarkSeekApplied();
				}
				if (m_Transport.Playing) {
					if (!_player.isPlaying) _player.Play();
				}
				else if (_player.isPlaying) _player.Pause();
			}

			var source = _player.texture;
			if (source == null) ClearTexture(_target);
			else Graphics.Blit(source, _target);
		}

		public bool TrySetParameter(string parameterId, ParameterValue value, out string rejectionReason) {
			return m_Transport.TrySetParameter(parameterId, value, m_LastGraphTime, _player == null ? 0d : _player.length,
				out rejectionReason);
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

		public void SetSceneActive(bool active) {
			foreach (var node in _nodes.OfType<LiveProgramSceneGraphNode>()) node.SetSceneActive(active);
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

	/// <summary>Evaluates Bootstrap-created patch graphs and composes their live Program output.</summary>
	public sealed class LiveGraphRuntime : IDisposable {
		public const int MainCueCount = 2;
		public const int ProgramWidthStep = 10;
		private static int m_ProgramWidth = 1560;
		public const int ProgramHeight = 854;
		public const int OverlayWidth = 3840;
		public const int OverlayHeight = 1536;
		public static int ProgramWidth => m_ProgramWidth;

		internal static void AdjustProgramWidth(int delta) {
			m_ProgramWidth = Math.Max(ProgramWidthStep, m_ProgramWidth + delta);
		}
		public const int PreviewWidth = 160;
		public const int PreviewHeight = 90;
		public const int PreviewFrameRate = 10;

		private const double PreviewIntervalSeconds = 1d / PreviewFrameRate;
		private static readonly LiveRenderSize PreviewRenderSize = new LiveRenderSize(PreviewWidth, PreviewHeight);

		private readonly LiveGraph _graph;
		private readonly LiveRenderSize m_ProgramRenderSize;
		private readonly LiveRenderSize m_OverlayRenderSize;
		private readonly int m_OverlayPatchOutputIndex;
		private readonly Dictionary<string, PatchDefinition> _patchDefinitionsById;
		private readonly LiveBpmClock m_BpmClock;
		private readonly List<LivePatch> _createdPatches = new List<LivePatch>();
		private readonly LivePatch[] m_OverlayPatches = new LivePatch[LiveStepSequencer.OverlayLaneCount];
		private readonly LiveSequencerCellMode[] m_OverlayModes = new LiveSequencerCellMode[LiveStepSequencer.OverlayLaneCount];
		private readonly bool[] m_OverlayOutput2Copies = new bool[LiveStepSequencer.OverlayLaneCount];
		private readonly Dictionary<string, LivePatchPreview> m_Previews = new Dictionary<string, LivePatchPreview>(StringComparer.Ordinal);
		private readonly HashSet<string> m_PreviewFailures = new HashSet<string>(StringComparer.Ordinal);
		private readonly RenderTexture[] m_OverlayPreviewFrames = new RenderTexture[LiveStepSequencer.OverlayLaneCount];
		private readonly RenderTexture[] m_MainCuePreviewFrames = new RenderTexture[MainCueCount];
		private readonly LivePatch[] m_MainCuePatches = new LivePatch[MainCueCount];
		private readonly string[] m_MainCuePatchIds = new string[MainCueCount];
		private readonly LiveMainCueFader m_MainCueFader = new LiveMainCueFader();
		private bool m_IsMainCueCompositeActive;
		private int m_ActiveMainCueIndex;
		private ulong _frameNumber;
		private ulong m_PreviewFrameNumber;
		private double m_GraphTime;
		private double m_SceneTimeJogSpeedOffset;
		private double m_SceneTimeJogMaximumSpeedOffset = 4d;
		private double m_LastGraphDeltaSeconds;
		private double m_GraphTimeScale = 1d;
		private double m_PreviewElapsedSeconds;
		private bool _disposed;

		public string LoadedPatchId => LoadedMainPatch?.Definition.Id ?? string.Empty;
		public string PreloadedPatchId => PreloadedMainPatch?.Definition.Id ?? string.Empty;
		public IReadOnlyList<string> MainCuePatchIds => m_MainCuePatchIds;
		public int ActiveMainCueIndex => m_ActiveMainCueIndex;
		public float MainCueAlternateOpacity => m_MainCueFader.AlternateOpacity;
		public bool IsMainCueCompositeActive => m_IsMainCueCompositeActive;
		public IReadOnlyList<PatchDefinition> Patches => _graph.PatchDefinitions;
		public LiveProgramFrame CurrentFrame { get; private set; }
		public LiveProgramFrames CurrentFrames { get; private set; }
		public IReadOnlyList<RenderTexture> OverlayPreviewFrames => m_OverlayPreviewFrames;
		public IReadOnlyList<RenderTexture> MainCuePreviewFrames => m_MainCuePreviewFrames;
		public LiveParameterDefinition BpmDefinition => m_BpmClock.Definition;
		public BeatClockFrame BpmFrame => m_BpmClock.Frame;
		public double SceneTimePlaybackRate => Math.Max(0d, 1d + m_SceneTimeJogSpeedOffset);
		public double GraphTimeScale => m_GraphTimeScale;
		public bool IsTimeEasingEnabled => m_BpmClock.IsTimeEasingEnabled;

		internal LiveGraphRuntime(LiveGraph graph, AnimationCurve globalTimeEasing,
			LiveRenderSize programRenderSize, LiveRenderSize overlayRenderSize) {
			_graph = graph ?? throw new ArgumentNullException(nameof(graph));
			m_ProgramRenderSize = programRenderSize;
			m_OverlayRenderSize = overlayRenderSize;
			m_OverlayPatchOutputIndex = programRenderSize.Width == overlayRenderSize.Width
				&& programRenderSize.Height == overlayRenderSize.Height ? 0 : 1;
			m_BpmClock = new LiveBpmClock(LiveBpmClock.DefaultBpm, globalTimeEasing);
			_patchDefinitionsById = graph.PatchDefinitions.ToDictionary(definition => definition.Id, StringComparer.Ordinal);
			m_MainCuePatches[0] = CreatePatch(graph.PatchDefinitions[0], m_ProgramRenderSize);
			m_MainCuePatchIds[0] = graph.PatchDefinitions[0].Id;
			m_ActiveMainCueIndex = 0;
			LoadedMainPatch.SetSceneActive(true);
			CurrentFrames = new LiveProgramFrames(new[] {
				new LiveProgramFrame(LoadedMainPatch.Outputs[0].ProgramTexture, 0),
				new LiveProgramFrame(_graph.OverlayOutputCompositor.Output, 0)
			});
			CurrentFrame = CurrentFrames.Primary;
		}

		public void ConfigureSceneTimeJog(float maximumSpeedOffset) {
			if (float.IsNaN(maximumSpeedOffset) || float.IsInfinity(maximumSpeedOffset) || maximumSpeedOffset <= 0f)
				throw new ArgumentOutOfRangeException(nameof(maximumSpeedOffset));
			m_SceneTimeJogMaximumSpeedOffset = maximumSpeedOffset;
			m_SceneTimeJogSpeedOffset = Math.Max(-1d, Math.Min(maximumSpeedOffset, m_SceneTimeJogSpeedOffset));
		}

		public void ConfigureMainCueFaderCurve(AnimationCurve responseCurve) {
			m_MainCueFader.SetResponseCurve(responseCurve);
		}

		public void ConfigureMainCompositeOpacity(float opacity) {
			_graph.Compositor.SetMainCompositeOpacity(opacity);
		}

		public LiveParameterApplicationResult Apply(LiveParameterRequest request) {
			if (request.Kind == LiveParameterRequestKind.SetBpm)
				return m_BpmClock.TrySetBpm(request.Value, out var bpmRejection) ? Accept(request) : Reject(request, bpmRejection);
			if (request.Kind == LiveParameterRequestKind.AlignBeat)
				return m_BpmClock.TryAlignToNearestBeat(out var alignmentRejection) ? Accept(request) : Reject(request, alignmentRejection);
			if (request.Kind == LiveParameterRequestKind.SetTimeEasingEnabled) {
				m_BpmClock.SetTimeEasingEnabled(request.ParameterValue.AsBool());
				return Accept(request);
			}
			if (request.Kind == LiveParameterRequestKind.JogSceneTime) {
				var nextSpeedOffset = m_SceneTimeJogSpeedOffset + request.Value;
				if (double.IsNaN(nextSpeedOffset) || double.IsInfinity(nextSpeedOffset)) return Reject(request, "The scene time jog speed must be finite.");
				m_SceneTimeJogSpeedOffset = Math.Max(-1d,
					Math.Min(m_SceneTimeJogMaximumSpeedOffset, nextSpeedOffset));
				return Accept(request);
			}
			if (request.Kind == LiveParameterRequestKind.RecallHotCue
				|| request.Kind == LiveParameterRequestKind.RecallOppositeHotCue) {
				var targetPatch = request.Kind == LiveParameterRequestKind.RecallOppositeHotCue
					? PreloadedMainPatch : LoadedMainPatch;
				if (targetPatch == null) return Accept(request);
				var hotCueIndex = request.ParameterValue.AsInt();
				return targetPatch.TryRecallHotCue(hotCueIndex, out var hotCueRejection)
					? Accept(request) : Reject(request, hotCueRejection);
			}
			if (request.Kind == LiveParameterRequestKind.SetMainCueFader) {
				m_MainCueFader.SetPosition(request.Value);
				if (m_MainCuePatches[m_MainCueFader.AlternateCueIndex] == null)
					m_MainCueFader.SetReferenceCue(m_MainCueFader.ReferenceCueIndex);
				RefreshMainCueActivation();
				return Accept(request);
			}
			if (request.Kind == LiveParameterRequestKind.ToggleMainCue) {
				if (m_MainCuePatches.Any(patch => patch == null)) return Reject(request, "Both Main Cue slots must be assigned before switching.");
				m_IsMainCueCompositeActive = false;
				m_MainCueFader.ToggleReferenceCue();
				RefreshMainCueActivation();
				return Accept(request);
			}
			if (request.Kind == LiveParameterRequestKind.SetMainCueComposite) {
				if (request.ParameterValue.AsBool() && m_MainCuePatches.Any(patch => patch == null))
					return Reject(request, "Both Main Cue slots must be assigned before compositing.");
				m_IsMainCueCompositeActive = request.ParameterValue.AsBool();
				RefreshMainCueActivation();
				return Accept(request);
			}
			if (request.Kind == LiveParameterRequestKind.ToggleMainCueComposite) {
				if (!m_IsMainCueCompositeActive && m_MainCuePatches.Any(patch => patch == null))
					return Reject(request, "Both Main Cue slots must be assigned before compositing.");
				m_IsMainCueCompositeActive = !m_IsMainCueCompositeActive;
				RefreshMainCueActivation();
				return Accept(request);
			}
			if (request.Kind == LiveParameterRequestKind.SetParameter
				&& LiveInstantEffectRenderer.TryParseParameterAddress(request.ParameterId, out var cueIndex, out var effectParameterId))
				return _graph.InstantEffects.TrySetParameter(cueIndex, effectParameterId, request.ParameterValue, out var effectRejection)
					? Accept(request) : Reject(request, effectRejection);
			if (!_patchDefinitionsById.TryGetValue(request.PatchId, out var definition)) return Reject(request, "The requested patch does not exist.");
			if (request.Kind == LiveParameterRequestKind.PreloadPatch) {
				AssignMainCuePatch(InactiveMainCueIndex, definition);
				return Accept(request);
			}
			if (request.Kind == LiveParameterRequestKind.LoadPatch) {
				if (PreloadedMainPatch?.Definition != definition) return Reject(request, "The requested patch has not been preloaded.");
				ActivateMainCue(InactiveMainCueIndex);
				return Accept(request);
			}
			if (request.Kind == LiveParameterRequestKind.LaunchPatch) {
				var mainCueIndex = Array.FindIndex(m_MainCuePatches, patch => patch?.Definition == definition);
				if (mainCueIndex < 0) {
					mainCueIndex = InactiveMainCueIndex;
					AssignMainCuePatch(mainCueIndex, definition);
				}
				ActivateMainCue(mainCueIndex);
				return Accept(request);
			}
			var patch = m_MainCuePatches.FirstOrDefault(candidate => candidate?.Definition == definition);
			if (patch == null) return Reject(request, "The requested patch is not loaded.");
			return patch.TrySetParameter(request.ParameterId, request.ParameterValue, out var reason) ? Accept(request) : Reject(request, reason);
		}

		public void Evaluate(double deltaSeconds) {
			EnsureUsable();
			var sourceDeltaSeconds = Math.Max(0d, deltaSeconds);
			var playbackRate = SceneTimePlaybackRate;
			m_SceneTimeJogSpeedOffset = 0d;
			var clockDeltaSeconds = m_BpmClock.Advance(sourceDeltaSeconds);
			m_LastGraphDeltaSeconds = clockDeltaSeconds * playbackRate;
			m_GraphTimeScale = sourceDeltaSeconds > 0d ? m_LastGraphDeltaSeconds / sourceDeltaSeconds : 1d;
			m_GraphTime += m_LastGraphDeltaSeconds;
			foreach (var patch in ActiveMainCuePatches()) {
				patch.ApplyResolvedParameters(m_BpmClock.Frame);
				foreach (var output in patch.Outputs) output.Evaluate(m_LastGraphDeltaSeconds, m_BpmClock.Frame);
			}
			foreach (var overlay in ActiveOverlayPatches()) {
				overlay.ApplyResolvedParameters(m_BpmClock.Frame);
				foreach (var output in overlay.Outputs) output.Evaluate(m_LastGraphDeltaSeconds, m_BpmClock.Frame);
			}
		}

		public void SceneUpdate() {
			EnsureUsable();
			foreach (var patch in ActiveMainCuePatches())
				foreach (var output in patch.Outputs) output.SceneUpdate(m_LastGraphDeltaSeconds);
			foreach (var overlay in ActiveOverlayPatches())
				foreach (var output in overlay.Outputs) output.SceneUpdate(m_LastGraphDeltaSeconds);
		}

		public LiveProgramFrames Render(IReadOnlyList<int> instantEffectTriggers = null, bool blackout = false) {
			EnsureUsable();
			var nextFrame = _frameNumber + 1;
			if (nextFrame == 0) nextFrame = 1;
			foreach (var patch in ActiveMainCuePatches())
				foreach (var output in patch.Outputs) output.Render(m_GraphTime, nextFrame);
			foreach (var overlay in ActiveOverlayPatches())
				foreach (var output in overlay.Outputs) output.Render(m_GraphTime, nextFrame);
			var overlayInputs = new List<LiveOverlayInput>();
			var output2Inputs = new List<LiveOverlayInput>();
			for (var laneIndex = 0; laneIndex < m_OverlayPatches.Length; laneIndex++) {
				var overlay = m_OverlayPatches[laneIndex];
				if (overlay == null || m_OverlayModes[laneIndex] == LiveSequencerCellMode.Off || overlay.Outputs.Count == 0) continue;
				overlayInputs.Add(new LiveOverlayInput(m_OverlayModes[laneIndex], overlay.Outputs[0].ProgramTexture));
				if (m_OverlayOutput2Copies[laneIndex])
					output2Inputs.Add(new LiveOverlayInput(m_OverlayModes[laneIndex], overlay.Outputs[m_OverlayPatchOutputIndex].ProgramTexture));
			}
			var referencePatch = m_MainCuePatches[m_MainCueFader.ReferenceCueIndex];
			var alternatePatch = m_MainCuePatches[m_MainCueFader.AlternateCueIndex];
			var mainTexture = referencePatch?.Outputs.Count > 0 ? referencePatch.Outputs[0].ProgramTexture : null;
			var alternateTexture = alternatePatch?.Outputs.Count > 0 ? alternatePatch.Outputs[0].ProgramTexture : null;
			var composite = _graph.Compositor.Render(mainTexture, alternateTexture, m_MainCueFader.AlternateOpacity, m_IsMainCueCompositeActive,
				overlayInputs, nextFrame, m_GraphTime);
			var programOutput = _graph.InstantEffects.Render(composite, instantEffectTriggers, nextFrame, m_GraphTime);
			var overlayOutput = _graph.OverlayOutputCompositor.Render(Texture2D.blackTexture, null, 0f, false,
				output2Inputs, nextFrame, m_GraphTime);
			if (blackout) {
				ClearTexture(programOutput);
				if (overlayOutput != programOutput) ClearTexture(overlayOutput);
			}
			_frameNumber = nextFrame;
			CurrentFrames = new LiveProgramFrames(new[] {
				new LiveProgramFrame(programOutput, _frameNumber),
				new LiveProgramFrame(overlayOutput, _frameNumber)
			});
			CurrentFrame = CurrentFrames.Primary;
			return CurrentFrames;
		}

		private static void ClearTexture(RenderTexture texture) {
			if (texture == null) return;
			var previous = RenderTexture.active;
			RenderTexture.active = texture;
			GL.Clear(true, true, Color.black);
			RenderTexture.active = previous;
		}

		public void SetOverlayComposition(LiveSequencerReadModel composition) {
			EnsureUsable();
			if (composition.Kind != LiveSequencerKind.Overlay)
				throw new ArgumentException("The live overlay compositor requires the overlay sequencer.", nameof(composition));
			var activeModes = composition.GetActiveLayers().ToDictionary(layer => layer.LaneIndex, layer => layer.Mode);
			for (var laneIndex = 0; laneIndex < m_OverlayPatches.Length; laneIndex++) {
				var patchId = composition.LanePatchIds.Count > laneIndex ? composition.LanePatchIds[laneIndex] : string.Empty;
				var current = m_OverlayPatches[laneIndex];
				if (string.IsNullOrEmpty(patchId)) {
					m_OverlayPatches[laneIndex] = null;
					m_OverlayModes[laneIndex] = LiveSequencerCellMode.Off;
					DisposeUnreferencedOverlayPatch(current);
					continue;
				}
				if (!_patchDefinitionsById.TryGetValue(patchId, out var definition))
					throw new InvalidOperationException("The overlay sequencer references an unknown patch: " + patchId + ".");
				if (current == null || current.Definition != definition) {
					var replacement = m_OverlayPatches.FirstOrDefault(patch => patch?.Definition == definition)
						?? CreateOverlayPatch(definition);
					m_OverlayPatches[laneIndex] = replacement;
					DisposeUnreferencedOverlayPatch(current);
				}
				m_OverlayModes[laneIndex] = activeModes.TryGetValue(laneIndex, out var mode) ? mode : LiveSequencerCellMode.Off;
				m_OverlayOutput2Copies[laneIndex] = composition.IsCopiedToOutput2(laneIndex);
			}
			foreach (var patch in m_OverlayPatches.Where(patch => patch != null).Distinct())
				patch.SetSceneActive(IsOverlayPatchActive(patch));
		}

		public void RenderPreviews(IReadOnlyList<string> lanePatchIds, IReadOnlyList<string> mainCuePatchIds, double deltaSeconds,
			double timeOffsetSeconds) {
			EnsureUsable();
			var activePatchIds = CollectAssignedPreviewPatchIds(lanePatchIds, mainCuePatchIds);
			ReconcilePreviews(activePatchIds);
			if (activePatchIds.Count == 0) {
				m_PreviewElapsedSeconds = 0d;
				m_PreviewFrameNumber = 0;
			}
			else {
				m_PreviewElapsedSeconds += Math.Max(0d, deltaSeconds);
				if (m_PreviewFrameNumber == 0 || m_PreviewElapsedSeconds >= PreviewIntervalSeconds) {
					m_PreviewElapsedSeconds %= PreviewIntervalSeconds;
					var nextFrame = m_PreviewFrameNumber + 1;
					if (nextFrame == 0) nextFrame = 1;
					var previewTimeOffset = double.IsNaN(timeOffsetSeconds) || double.IsInfinity(timeOffsetSeconds)
						? 0d : Math.Max(0d, timeOffsetSeconds);
					RenderPreviewPatches(nextFrame, m_GraphTime + m_BpmClock.ProjectGraphDelta(previewTimeOffset),
						OffsetBeatClockFrame(m_BpmClock.Frame, previewTimeOffset));
					m_PreviewFrameNumber = nextFrame;
				}
			}

			PopulatePreviewFrames(lanePatchIds, m_OverlayPreviewFrames);
			PopulatePreviewFrames(mainCuePatchIds, m_MainCuePreviewFrames);
		}

		public LiveParameterDefinition[] GetLoadedPatchParameterDefinitions() => LoadedMainPatch?.GetParameterDefinitions() ?? Array.Empty<LiveParameterDefinition>();

		public LiveParameterDefinition[] GetInstantEffectParameterDefinitions(int cueIndex) {
			EnsureUsable();
			return _graph.InstantEffects.GetParameterDefinitions(cueIndex);
		}

		public bool TryAssignInstantEffect(int cueIndex, string typeId, out string rejectionReason) {
			EnsureUsable();
			return _graph.InstantEffects.TryAssign(cueIndex, typeId, out rejectionReason);
		}

		public void ClearInstantEffect(int cueIndex) {
			EnsureUsable();
			_graph.InstantEffects.Clear(cueIndex);
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			m_Previews.Clear();
			m_PreviewFailures.Clear();
			for (var index = _createdPatches.Count - 1; index >= 0; index--) _createdPatches[index].Dispose();
			_createdPatches.Clear();
			_graph.Dispose();
		}

		private LivePatch CreatePatch(PatchDefinition definition, LiveRenderSize renderSize) {
			return CreatePatch(definition, new[] { renderSize });
		}

		private LivePatch CreateOverlayPatch(PatchDefinition definition) {
			return m_OverlayPatchOutputIndex == 0
				? CreatePatch(definition, m_ProgramRenderSize)
				: CreatePatch(definition, m_ProgramRenderSize, m_OverlayRenderSize);
		}

		private LivePatch CreatePatch(PatchDefinition definition, params LiveRenderSize[] renderSizes) {
			var patch = new LivePatch(definition, _graph.CreateOutput, renderSizes);
			try {
				patch.ApplyResolvedParameters(m_BpmClock.Frame);
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

		private IEnumerable<LivePatch> ActiveOverlayPatches() {
			return m_OverlayPatches.Where(IsOverlayPatchActive).Distinct();
		}

		private bool IsOverlayPatchActive(LivePatch patch) {
			if (patch == null) return false;
			for (var laneIndex = 0; laneIndex < m_OverlayPatches.Length; laneIndex++)
				if (ReferenceEquals(m_OverlayPatches[laneIndex], patch) && m_OverlayModes[laneIndex] != LiveSequencerCellMode.Off)
					return true;
			return false;
		}

		private void DisposeUnreferencedOverlayPatch(LivePatch patch) {
			if (patch != null && !m_OverlayPatches.Contains(patch)) DisposePatch(patch);
		}

		private static HashSet<string> CollectAssignedPreviewPatchIds(IReadOnlyList<string> lanePatchIds, IReadOnlyList<string> mainCuePatchIds) {
			var patchIds = new HashSet<string>(StringComparer.Ordinal);
			CollectAssignedPatchIds(lanePatchIds, patchIds);
			CollectAssignedPatchIds(mainCuePatchIds, patchIds);
			return patchIds;
		}

		private static void CollectAssignedPatchIds(IReadOnlyList<string> source, ISet<string> destination) {
			if (source == null) return;
			foreach (var patchId in source)
				if (!string.IsNullOrEmpty(patchId)) destination.Add(patchId);
		}

		private void PopulatePreviewFrames(IReadOnlyList<string> patchIds, RenderTexture[] frames) {
			Array.Clear(frames, 0, frames.Length);
			if (patchIds == null) return;
			for (var index = 0; index < Math.Min(patchIds.Count, frames.Length); index++) {
				var patchId = patchIds[index];
				if (!string.IsNullOrEmpty(patchId) && m_Previews.TryGetValue(patchId, out var preview)) frames[index] = preview.Texture;
			}
		}

		private void ReconcilePreviews(ISet<string> activePatchIds) {
			foreach (var patchId in m_Previews.Keys.Where(patchId => !activePatchIds.Contains(patchId)).ToArray())
				DisposePreview(patchId);
			foreach (var patchId in m_PreviewFailures.Where(patchId => !activePatchIds.Contains(patchId)).ToArray())
				m_PreviewFailures.Remove(patchId);

			foreach (var patchId in activePatchIds) {
				if (m_Previews.ContainsKey(patchId) || m_PreviewFailures.Contains(patchId)) continue;
				if (!_patchDefinitionsById.TryGetValue(patchId, out var definition)) {
					m_PreviewFailures.Add(patchId);
					continue;
				}
				try {
					var patch = CreatePatch(definition, PreviewRenderSize);
					m_Previews.Add(patchId, new LivePatchPreview(patch));
				}
				catch {
					m_PreviewFailures.Add(patchId);
				}
			}
		}

		private void RenderPreviewPatches(ulong frameNumber, double graphTime, BeatClockFrame bpmFrame) {
			foreach (var pair in m_Previews.ToArray()) {
				try {
					pair.Value.Render(graphTime, bpmFrame, frameNumber);
				}
				catch {
					DisposePreview(pair.Key);
					m_PreviewFailures.Add(pair.Key);
				}
			}
		}

		private static BeatClockFrame OffsetBeatClockFrame(BeatClockFrame frame, double offsetSeconds) {
			if (!frame.IsAvailable || offsetSeconds <= 0d) return frame;
			return new BeatClockFrame(frame.Bpm, frame.TotalBeats + offsetSeconds * frame.Bpm / 60d,
				frame.BeatAlignmentBeats);
		}

		private void DisposePreview(string patchId) {
			if (!m_Previews.TryGetValue(patchId, out var preview)) return;
			m_Previews.Remove(patchId);
			DisposePatch(preview.Patch);
		}

		private LivePatch LoadedMainPatch => m_MainCuePatches[m_ActiveMainCueIndex];
		private LivePatch PreloadedMainPatch => m_MainCuePatches[InactiveMainCueIndex];
		private int InactiveMainCueIndex => 1 - m_ActiveMainCueIndex;

		private void AssignMainCuePatch(int cueIndex, PatchDefinition definition) {
			if (m_MainCuePatches[cueIndex]?.Definition == definition) return;
			var nextPatch = CreatePatch(definition, m_ProgramRenderSize);
			var previousPatch = m_MainCuePatches[cueIndex];
			m_MainCuePatches[cueIndex] = nextPatch;
			m_MainCuePatchIds[cueIndex] = definition.Id;
			DisposePatch(previousPatch);
		}

		private void ActivateMainCue(int cueIndex) {
			var nextPatch = m_MainCuePatches[cueIndex];
			if (nextPatch == null) throw new InvalidOperationException("The requested Cue Slot is empty.");
			m_IsMainCueCompositeActive = false;
			m_MainCueFader.SetReferenceCue(cueIndex);
			RefreshMainCueActivation();
		}

		private IEnumerable<LivePatch> ActiveMainCuePatches() {
			var referencePatch = m_MainCuePatches[m_MainCueFader.ReferenceCueIndex];
			if (referencePatch != null && (m_IsMainCueCompositeActive || m_MainCueFader.AlternateOpacity < 1f)) yield return referencePatch;
			var alternatePatch = m_MainCuePatches[m_MainCueFader.AlternateCueIndex];
			if (alternatePatch != null && (m_IsMainCueCompositeActive || m_MainCueFader.AlternateOpacity > 0f)) yield return alternatePatch;
		}

		private void RefreshMainCueActivation() {
			m_ActiveMainCueIndex = m_MainCueFader.DominantCueIndex;
			for (var cueIndex = 0; cueIndex < m_MainCuePatches.Length; cueIndex++) {
				var patch = m_MainCuePatches[cueIndex];
				if (patch == null) continue;
				var active = m_IsMainCueCompositeActive || (cueIndex == m_MainCueFader.ReferenceCueIndex
					? m_MainCueFader.AlternateOpacity < 1f
					: m_MainCueFader.AlternateOpacity > 0f);
				patch.SetSceneActive(active);
			}
		}

		private void EnsureUsable() {
			if (_disposed) throw new ObjectDisposedException(nameof(LiveGraphRuntime));
			if (LoadedMainPatch == null) throw new InvalidOperationException("A patch is not loaded.");
		}

		private static LiveParameterApplicationResult Accept(LiveParameterRequest request) => new LiveParameterApplicationResult(request.SequenceNumber, true, string.Empty);
		private static LiveParameterApplicationResult Reject(LiveParameterRequest request, string reason) => new LiveParameterApplicationResult(request.SequenceNumber, false, reason);
	}

	internal sealed class LivePatchPreview {
		private readonly LivePatch m_Patch;
		private double m_LastGraphTime;
		private bool m_HasRendered;

		public LivePatch Patch => m_Patch;
		public RenderTexture Texture => m_Patch.Outputs.Count == 0 ? null : m_Patch.Outputs[0].ProgramTexture;

		public LivePatchPreview(LivePatch patch) {
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
			Func<PatchDefinition, LiveRenderSize, LiveProgramOutput> createOutput,
			IEnumerable<LiveRenderSize> renderSizes) {
			Definition = definition ?? throw new ArgumentNullException(nameof(definition));
			if (createOutput == null) throw new ArgumentNullException(nameof(createOutput));
			var outputs = new List<LiveProgramOutput>();
			try {
				foreach (var renderSize in renderSizes ?? throw new ArgumentNullException(nameof(renderSizes)))
					outputs.Add(createOutput(definition, renderSize));
				if (outputs.Count == 0) throw new ArgumentException("A live patch requires at least one output resolution.", nameof(renderSizes));
				Outputs = outputs;
				_parameters = definition.Parameters.ToDictionary(parameter => parameter.Id, parameter => {
					var graphNode = definition.ProgramGraph.Nodes.FirstOrDefault(node => string.Equals(node.Id, parameter.NodeId, StringComparison.Ordinal));
					if (graphNode == null) throw new InvalidOperationException("A published parameter references an unknown patch graph node: " + parameter.Id + ".");
					if (graphNode.IsSceneNode) {
						if (!outputs[0].TryGetSceneParameter(parameter.NodeId, parameter.ParameterId, out var root, out var source)
							|| string.IsNullOrWhiteSpace(source.Id))
							throw new InvalidOperationException("A published parameter is not provided by its scene graph node: " + parameter.Id + ".");
						var roots = new List<LiveSceneRoot> { root };
						foreach (var output in outputs.Skip(1)) {
							if (!output.TryGetSceneParameter(parameter.NodeId, parameter.ParameterId, out var additionalRoot, out var additionalSource)
								|| additionalSource.Id != source.Id)
								throw new InvalidOperationException("A published scene parameter is inconsistent across output resolutions: " + parameter.Id + ".");
							roots.Add(additionalRoot);
						}
						return (ILivePublishedParameter)new LivePublishedParameter(parameter, roots, source);
					}
					var graphSource = graphNode.FindParameter(parameter.ParameterId);
					if (graphSource == null || !PatchGraphParameter.IsLiveControllable(graphSource.Type))
						throw new InvalidOperationException("A published graph parameter must reference a configured parameter supported by the live renderer: " + parameter.Id + ".");
					return (ILivePublishedParameter)new LivePublishedGraphParameter(parameter, outputs, graphSource.Value);
				}, StringComparer.Ordinal);
			}
			catch {
				for (var index = outputs.Count - 1; index >= 0; index--) outputs[index].Dispose();
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

		public bool TryRecallHotCue(int hotCueIndex, out string rejectionReason) {
			if (hotCueIndex < 0 || hotCueIndex >= PatchDefinition.HotCueCount) {
				rejectionReason = "The Hot Cue index must be 0 or 1.";
				return false;
			}
			var hotCue = Definition.GetHotCue(hotCueIndex);
			if (hotCue == null) {
				rejectionReason = string.Empty;
				return true;
			}
			foreach (var value in hotCue.ConfiguredValues) {
				if (!Definition.TryResolveHotCueTarget(value, out var graphNode, out _)) {
					rejectionReason = "A Hot Cue references an unknown or ambiguous Program Graph parameter.";
					return false;
				}
				var published = Definition.Parameters.FirstOrDefault(parameter => string.Equals(parameter.NodeId, graphNode.Id, StringComparison.Ordinal)
					&& string.Equals(parameter.ParameterId, value.Id, StringComparison.Ordinal));
				if (published != null) {
					var parameter = _parameters[published.Id];
					var applied = parameter is LivePublishedParameter sceneParameter
						? sceneParameter.TrySetHotCueParameter(value.Value, out rejectionReason)
						: parameter.TrySetParameter(value.Value, out rejectionReason);
					if (!applied) return false;
					continue;
				}
				foreach (var output in Outputs)
					if (!output.TrySetGraphParameter(graphNode.Id, value.Id, value.Value, out rejectionReason)) return false;
			}
			rejectionReason = string.Empty;
			return true;
		}

		public void SetSceneActive(bool active) {
			foreach (var output in Outputs) output.SetSceneActive(active);
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
		private readonly IReadOnlyList<LiveSceneRoot> m_Roots;
		private readonly bool _isTriggerParameter;
		private float _baseValue;
		private bool _isDirty;
		private bool _hasResolvedValue;
		private float _lastResolvedValue;
		private bool m_ReleaseTriggerAfterApply;
		public LiveSceneRoot Root => m_Roots[0];
		public LiveParameterDefinition Source { get; }

		public LivePublishedParameter(PatchParameter definition, LiveSceneRoot root, LiveParameterDefinition source)
			: this(definition, new[] { root }, source) { }

		public LivePublishedParameter(PatchParameter definition, IEnumerable<LiveSceneRoot> roots, LiveParameterDefinition source) {
			_definition = definition ?? throw new ArgumentNullException(nameof(definition));
			m_Roots = (roots ?? throw new ArgumentNullException(nameof(roots))).ToArray();
			if (m_Roots.Count == 0 || m_Roots.Any(root => root == null))
				throw new ArgumentException("At least one live scene root is required.", nameof(roots));
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
			m_ReleaseTriggerAfterApply = false;
			rejectionReason = string.Empty;
			return true;
		}

		public bool TrySetHotCueParameter(ParameterValue value, out string rejectionReason) {
			if (!TrySetParameter(value, out rejectionReason)) return false;
			m_ReleaseTriggerAfterApply = _isTriggerParameter && IsTriggerActive(value.AsFloat());
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
			foreach (var root in m_Roots)
				if (!root.TrySetParameter(Source.Id, resolvedValue, out rejectionReason)) return false;
			_lastResolvedValue = resolvedValue;
			_hasResolvedValue = true;
			_isDirty = false;
			if (m_ReleaseTriggerAfterApply) {
				foreach (var root in m_Roots)
					if (!root.TrySetParameter(Source.Id, Source.Minimum, out rejectionReason)) return false;
				_baseValue = Source.Minimum;
				_lastResolvedValue = Source.Minimum;
				m_ReleaseTriggerAfterApply = false;
			}
			rejectionReason = string.Empty;
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
