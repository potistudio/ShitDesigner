using System;
using CSharpFunctionalExtensions;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Nodes;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace ShitDesigner.Rendering {
	public sealed class BitonicPixelSortVisualNodeBinding : IRuntimeVisualNodeBinding {
		private readonly ComputeShader m_Shader;
		private readonly RenderTexturePool m_Pool;
		private readonly string m_SessionId;

		public NodeTypeId TypeId { get; } = new NodeTypeId(BitonicPixelSortContract.NodeTypeId);
		public bool IsAvailable => m_Shader != null && m_Pool != null && !string.IsNullOrWhiteSpace(m_SessionId);
		public Diagnostic AvailabilityDiagnostic => IsAvailable ? null : new Diagnostic(
			new DiagnosticCode("rendering.pixel_sort.binding_missing"), Severity.Error,
			"Pixel Sort requires an explicit ComputeShader, RenderTexturePool, and session ID.",
			nodeTypeId: TypeId, module: "rendering");

		public BitonicPixelSortVisualNodeBinding(ComputeShader shader, RenderTexturePool pool, string sessionId) {
			m_Shader = shader;
			m_Pool = pool;
			m_SessionId = sessionId;
		}

		public Result<IRuntimeNode, Diagnostic> Create(RuntimeNodeCreateInfo node, ulong generationId) {
			if (!IsAvailable) return Result.Failure<IRuntimeNode, Diagnostic>(AvailabilityDiagnostic);
			if (node == null || node.TypeId != TypeId || generationId == 0)
				return Result.Failure<IRuntimeNode, Diagnostic>(new Diagnostic(
					new DiagnosticCode("rendering.pixel_sort.node"), Severity.Error,
					"Pixel Sort factory input does not match its binding.", nodeId: node?.Id ?? default,
					nodeTypeId: TypeId, generationId: generationId, module: "rendering"));
			try {
				return Result.Success<IRuntimeNode, Diagnostic>(new BitonicPixelSortRuntimeNode(
					node, generationId, m_Shader, m_Pool, m_SessionId));
			}
			catch (Exception exception) {
				return Result.Failure<IRuntimeNode, Diagnostic>(new Diagnostic(
					new DiagnosticCode("rendering.pixel_sort.create"), Severity.Error, exception.Message,
					nodeId: node.Id, nodeTypeId: TypeId, generationId: generationId, module: "rendering",
					exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}
	}

	public sealed class BitonicPixelSortRuntimeNode : IRuntimeNode, IRuntimeDemandAwareNode {
		private const string Size4096Keyword = "BPS_SIZE_4096";
		private const int MaximumLineLength = 4096;

		private static readonly int m_DirectionId = Shader.PropertyToID("direction");
		private static readonly int m_OrderingId = Shader.PropertyToID("ordering");
		private static readonly int m_SortTextureId = Shader.PropertyToID("sortTex");
		private static readonly int m_SourceTextureId = Shader.PropertyToID("srcTex");
		private static readonly int m_ThresholdMaxId = Shader.PropertyToID("thresholdMax");
		private static readonly int m_ThresholdMinId = Shader.PropertyToID("thresholdMin");

		private readonly RuntimeNodeCreateInfo m_Record;
		private readonly ComputeShader m_Shader;
		private readonly RenderTexturePool m_Pool;
		private readonly ResourceOwnerKey m_WorkOwner;
		private readonly int m_SortPassIndex;
		private IRuntimeImageFrame m_LastFrame;
		private bool m_Disposed;

		public NodeInstanceId NodeId => m_Record.Id;
		public NodeTypeId TypeId => m_Record.TypeId;
		public ulong GenerationId { get; }
		public RuntimeNodeState State { get; private set; } = RuntimeNodeState.Preparing;

		public BitonicPixelSortRuntimeNode(RuntimeNodeCreateInfo record, ulong generationId,
			ComputeShader shader, RenderTexturePool pool, string sessionId) {
			m_Record = record ?? throw new ArgumentNullException(nameof(record));
			m_Shader = shader != null ? shader : throw new ArgumentNullException(nameof(shader));
			m_Pool = pool ?? throw new ArgumentNullException(nameof(pool));
			if (generationId == 0) throw new ArgumentOutOfRangeException(nameof(generationId));
			if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("A Pixel Sort session ID is required.", nameof(sessionId));
			GenerationId = generationId;
			m_SortPassIndex = shader.FindKernel("SortPass");
			m_WorkOwner = new ResourceOwnerKey(sessionId, ResourceOwnerKind.RuntimeNode,
				record.Id.Value, generationId, "pixel-sort", LeaseRole.Output);
		}

		public void OnDemandChanged(bool demanded, FrameEvaluationContext context) { }

		public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs) {
			var outputId = new PortId(BitonicPixelSortContract.OutputPortId);
			if (m_Disposed) {
				outputs.SetFaulted(outputId, Failure("rendering.pixel_sort.disposed", "Pixel Sort node is disposed.", context));
				return;
			}
			if (!context.RequestedOutputs.Contains(outputId)) return;
			if (!RuntimeOutputResolutionDemandAccess.TryGet(context, NodeId, outputId, out var demand)) {
				WriteLastOrFailure(context, outputs, outputId,
					Failure("rendering.pixel_sort.demand_missing", "Pixel Sort output has no resolution demand.", context));
				State = RuntimeNodeState.Preparing;
				return;
			}

			var preparedResult = context.OutputSurfaces?.TryGetPrepared(
				NodeId, outputId, demand.Width, demand.Height, context.Snapshot.FrameNumber);
			var prepared = preparedResult.HasValue && preparedResult.Value.IsSuccess ? preparedResult.Value.Value : null;
			var target = prepared?.NativeSurface as RenderTexture;
			if (target == null || prepared.LeaseId == 0) {
				var diagnostic = preparedResult.HasValue && preparedResult.Value.IsFailure
					? preparedResult.Value.Error
					: Failure("rendering.pixel_sort.surface_missing", "Pixel Sort requires a prepared RenderTexture output.", context);
				WriteLastOrFailure(context, outputs, outputId, diagnostic);
				State = RuntimeNodeState.Preparing;
				return;
			}

			var source = InputTexture(context, new PortId(BitonicPixelSortContract.InputPortId));
			if (source == null) {
				WriteLastOrFailure(context, outputs, outputId,
					Failure("rendering.pixel_sort.input_missing", "Pixel Sort input is unavailable.", context));
				State = RuntimeNodeState.Preparing;
				return;
			}

			var direction = ParameterEnum(context.Snapshot, BitonicPixelSortContract.DirectionParameterId,
				BitonicPixelSortContract.HorizontalDirection);
			var horizontal = string.Equals(direction, BitonicPixelSortContract.HorizontalDirection, StringComparison.Ordinal);
			var ascending = ParameterBool(context.Snapshot, BitonicPixelSortContract.AscendingParameterId, true);
			var thresholdMin = Mathf.Clamp01(ParameterFloat(context.Snapshot, BitonicPixelSortContract.ThresholdMinParameterId, .4f));
			var thresholdMax = Mathf.Clamp01(ParameterFloat(context.Snapshot, BitonicPixelSortContract.ThresholdMaxParameterId, .6f));
			var rendered = Render(source, target, context.Snapshot.FrameNumber, horizontal, ascending, thresholdMin, thresholdMax);
			if (rendered.IsFailure) {
				WriteLastOrFailure(context, outputs, outputId, rendered.Error);
				State = RuntimeNodeState.Faulted;
				return;
			}

			if (prepared is IRuntimeOutputSurfaceCompletion completion) completion.MarkRendered();
			var frame = new RenderingRuntimeImageFrame(prepared, context.Snapshot.FrameNumber);
			m_LastFrame = frame;
			State = RuntimeNodeState.Ready;
			outputs.SetAvailable(outputId, PortValue.FromImageFrame(frame));
		}

		public Result<RenderTexture, Diagnostic> Render(Texture source, RenderTexture target, ulong frameNumber,
			bool horizontal, bool ascending, float thresholdMin, float thresholdMax) {
			if (m_Disposed) return Result.Failure<RenderTexture, Diagnostic>(Failure("rendering.pixel_sort.disposed", "Pixel Sort node is disposed."));
			if (source == null || target == null || !target.IsCreated())
				return Result.Failure<RenderTexture, Diagnostic>(Failure("rendering.pixel_sort.target", "Created source and target textures are required."));
			if (frameNumber == 0) return Result.Failure<RenderTexture, Diagnostic>(Failure("rendering.pixel_sort.frame", "Frame number must be positive."));
			if (!SystemInfo.supportsComputeShaders)
				return Result.Failure<RenderTexture, Diagnostic>(Failure("rendering.pixel_sort.compute_unsupported", "The active graphics device does not support compute shaders."));

			var lineLength = horizontal ? source.width : source.height;
			if (lineLength > MaximumLineLength) {
				Graphics.Blit(source, target);
				return Result.Success<RenderTexture, Diagnostic>(target);
			}

			var format = target.graphicsFormat == GraphicsFormat.None ? GraphicsFormat.R8G8B8A8_UNorm : target.graphicsFormat;
			if (!SystemInfo.IsFormatSupported(format, GraphicsFormatUsage.LoadStore))
				return Result.Failure<RenderTexture, Diagnostic>(Failure("rendering.pixel_sort.format_unsupported", "Pixel Sort output format does not support random writes."));
			var descriptor = new TextureDescriptor(source.width, source.height, format, randomWrite: true);
			var acquired = m_Pool.Acquire(descriptor, m_WorkOwner, frameNumber);
			if (acquired.IsFailure) return Result.Failure<RenderTexture, Diagnostic>(acquired.Error);

			var work = acquired.Value;
			try {
				if (lineLength > 2048) m_Shader.EnableKeyword(Size4096Keyword);
				else m_Shader.DisableKeyword(Size4096Keyword);
				m_Shader.SetBool(m_DirectionId, horizontal);
				m_Shader.SetBool(m_OrderingId, ascending);
				m_Shader.SetFloat(m_ThresholdMinId, Mathf.Clamp01(thresholdMin));
				m_Shader.SetFloat(m_ThresholdMaxId, Mathf.Clamp01(thresholdMax));
				m_Shader.SetTexture(m_SortPassIndex, m_SourceTextureId, source);
				m_Shader.SetTexture(m_SortPassIndex, m_SortTextureId, work.Texture);
				m_Shader.Dispatch(m_SortPassIndex, horizontal ? source.height : source.width, 1, 1);
				Graphics.Blit(work.Texture, target);
				return Result.Success<RenderTexture, Diagnostic>(target);
			}
			catch (Exception exception) {
				return Result.Failure<RenderTexture, Diagnostic>(new Diagnostic(
					new DiagnosticCode("rendering.pixel_sort.render_failed"), Severity.Error, exception.Message,
					nodeId: NodeId, nodeTypeId: TypeId, generationId: GenerationId, module: "rendering",
					exception: DiagnosticExceptionInfo.FromException(exception)));
			}
			finally {
				if (!work.IsReleased) work.Release(m_WorkOwner, frameNumber);
			}
		}

		private Texture InputTexture(NodeExecutionContext context, PortId id) {
			if (!context.Inputs.TryGetValue(id, out var input) || !input.HasValue || !input.Value.IsImageFrame) return null;
			return (input.Value.AsImageFrame() as IRuntimeImageFrameSurface)?.NativeSurface as Texture;
		}

		private ParameterValue? Parameter(FrameSnapshot snapshot, string parameterId) {
			if (snapshot.EffectiveValues.TryGetValue(new ParameterKey(NodeId, new ParameterId(parameterId)), out var effective)) return effective;
			foreach (var parameter in m_Record.Parameters)
				if (string.Equals(parameter.Id.Value, parameterId, StringComparison.Ordinal)) return parameter.Value;
			return null;
		}

		private float ParameterFloat(FrameSnapshot snapshot, string id, float fallback) {
			try { return Parameter(snapshot, id)?.AsFloat() ?? fallback; }
			catch (InvalidOperationException) { return fallback; }
		}

		private bool ParameterBool(FrameSnapshot snapshot, string id, bool fallback) {
			try { return Parameter(snapshot, id)?.AsBool() ?? fallback; }
			catch (InvalidOperationException) { return fallback; }
		}

		private string ParameterEnum(FrameSnapshot snapshot, string id, string fallback) {
			try { return Parameter(snapshot, id)?.AsString() ?? fallback; }
			catch (InvalidOperationException) { return fallback; }
		}

		private void WriteLastOrFailure(NodeExecutionContext context, NodeOutputWriter outputs, PortId outputId, Diagnostic diagnostic) {
			if (m_LastFrame != null) {
				outputs.SetAvailable(outputId, PortValue.FromImageFrame(m_LastFrame));
				context.Diagnostics.Report(diagnostic);
			}
			else outputs.SetPreparing(outputId, diagnostic);
		}

		private Diagnostic Failure(string code, string message, NodeExecutionContext context = null) => new Diagnostic(
			new DiagnosticCode(code), Severity.Error, message, nodeId: NodeId, nodeTypeId: TypeId,
			generationId: GenerationId, frameNumber: context == null ? 0 : unchecked((long)context.Snapshot.FrameNumber), module: "rendering");

		public void Dispose() {
			if (m_Disposed) return;
			m_Disposed = true;
			m_LastFrame = null;
			State = RuntimeNodeState.Disposed;
		}
	}
}
