using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Nodes;
using ShitDesigner.Rendering.VJ.Temporal;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace ShitDesigner.Rendering {
	/// <summary>
	/// Rendering-owned execution path for manifest entries with more than one
	/// fixed pass or an explicit history ring. Each non-final pass receives a
	/// temporary RenderTexturePool lease. Leases are released in a finally
	/// block even when a shader/pass throws, so a failed graph cannot leak a
	/// surface into the next frame.
	/// </summary>
	public sealed class ShaderPassGraphRuntimeNode : IRuntimeNode, IRuntimeDemandAwareNode {
		private readonly RuntimeNodeCreateInfo _record;
		private readonly ShaderMaterialBinding _binding;
		private readonly RenderTexturePool _pool;
		private readonly Material _material;
		private readonly bool _generator;
		private readonly bool _blend;
		private readonly string _historyKey;
		private readonly ResourceOwnerKey _owner;
		private readonly TemporalHistoryService _history;
		private TemporalHistoryLease _historyLease;
		private readonly List<TextureLeaseHandle> _temporaryLeases = new List<TextureLeaseHandle>();
		private readonly List<int> _lastExecutedPassIndices = new List<int>();
		private readonly List<Texture> _lastPassInputTextures = new List<Texture>();
		private IRuntimeImageFrame _lastFrame;
		private bool _disposed;
		private double _lastClock;
		private bool _hasClock;

		public NodeInstanceId NodeId => _record.Id;
		public NodeTypeId TypeId => _record.TypeId;
		public ulong GenerationId { get; }
		public RuntimeNodeState State { get; private set; } = RuntimeNodeState.Preparing;
		public ShaderMaterialBinding Binding => _binding;
		public int PassCount => _binding.Passes.Count;
		public int LastPassCount { get; private set; }
		public int LastTemporaryLeaseCount { get; private set; }
		public IReadOnlyList<int> LastExecutedPassIndices => _lastExecutedPassIndices;
		/// <summary>Textures consumed by each executed
		/// pass. A multi-pass graph must feed each temporary result into the
		/// next pass rather than drawing the original input repeatedly.</summary>
		public IReadOnlyList<Texture> LastPassInputTextures => _lastPassInputTextures;
		public int ActiveTemporaryLeaseCount => _temporaryLeases.Count(x => x != null && !x.IsReleased);
		public TemporalHistoryService HistoryService => _history;
		public RenderTexture LastOutputTexture { get; private set; }
		public ulong LastCommittedFrame { get; private set; }

		public ShaderPassGraphRuntimeNode(RuntimeNodeCreateInfo record, ulong generationId,
			ShaderMaterialBinding binding, RenderTexturePool pool, string sessionId,
			bool generator = false, bool blend = false) {
			_record = record ?? throw new ArgumentNullException(nameof(record));
			_binding = binding ?? throw new ArgumentNullException(nameof(binding));
			if (generationId == 0) throw new ArgumentOutOfRangeException(nameof(generationId));
			_pool = pool ?? throw new ArgumentNullException(nameof(pool));
			if (string.IsNullOrWhiteSpace(sessionId)) throw new ArgumentException("A shader graph session ID is required.", nameof(sessionId));
			GenerationId = generationId;
			_generator = generator || binding.Family == ShaderNodeFamily.Generator;
			_blend = blend || binding.Family == ShaderNodeFamily.Composite;
			_material = new Material(binding.Shader) { name = "ShitDesigner.PassGraphMaterial." + record.Id.Value };
			_owner = new ResourceOwnerKey(sessionId, ResourceOwnerKind.RuntimeNode, record.Id.Value,
				generationId, "pass-graph", LeaseRole.Output);
			_historyKey = record.Id.Value + "." + record.TypeId.Value;
			_history = binding.Stateful ? new TemporalHistoryService(pool, _owner) : null;
		}

		public void OnDemandChanged(bool demanded, FrameEvaluationContext context) {
			if (demanded && context != null) {
				_lastClock = context.Snapshot.GraphClockTime;
				_hasClock = true;
			}
		}

		public void Evaluate(NodeExecutionContext context, NodeOutputWriter outputs) {
			var image = new PortId("image");
			if (_disposed) {
				outputs.SetFaulted(image, Failure("rendering.shader_graph.disposed", "Shader pass graph node is disposed.", context));
				return;
			}
			if (!context.RequestedOutputs.Contains(image)) return;
			if (!RuntimeOutputResolutionDemandAccess.TryGet(context, NodeId, image, out var demand)) {
				outputs.SetPreparing(image, Failure("rendering.shader_graph.demand_missing", "Shader pass graph has no output resolution demand.", context));
				State = RuntimeNodeState.Preparing;
				return;
			}
			var prepared = context.OutputSurfaces?.TryGetPrepared(NodeId, image, demand.Width, demand.Height, context.Snapshot.FrameNumber);
			var target = prepared.HasValue && prepared.Value.IsSuccess ? prepared.Value.Value.NativeSurface as RenderTexture : null;
			if (!prepared.HasValue || prepared.Value.IsFailure || target == null) {
				var diagnostic = prepared.HasValue && prepared.Value.IsFailure
					? prepared.Value.Diagnostic
					: Failure("rendering.shader_graph.surface_missing", "Shader pass graph requires a prepared RenderTexture output.", context);
				outputs.SetPreparing(image, diagnostic);
				State = RuntimeNodeState.Preparing;
				return;
			}

			try {
				ApplyParameters(context.Snapshot);
				var reset = ReadBoolParameter(context.Snapshot, "reset");
				var source = BindInputs(context, reset, target);
				var graphTime = FiniteFloat(context.Snapshot.GraphClockTime);
				var delta = _hasClock && !context.Snapshot.IsGraphClockPaused
					? FiniteFloat(context.Snapshot.GraphClockTime - _lastClock) : 0f;
				if (delta < 0f) delta = 0f;
				ShaderRuntimeUniformApplier.Apply(_material, _binding, graphTime, delta,
					context.Snapshot.FrameNumber, target.width, target.height, StableSeed(NodeId.Value));
				_material.SetFloat("_Paused", context.Snapshot.IsGraphClockPaused ? 1f : 0f);
				_material.SetFloat("_Reset", reset ? 1f : 0f);
				var result = RenderCore(source, target, context.Snapshot.FrameNumber, graphTime,
					context.Snapshot.IsGraphClockPaused, reset);
				if (result.IsFailure) {
					WriteLastOrFailure(context, outputs, image, result.Diagnostic);
					State = RuntimeNodeState.Faulted;
					return;
				}
				var preparedSurface = prepared.Value.Value;
				if (preparedSurface is IRuntimeOutputSurfaceCompletion completion) completion.MarkRendered();
				_lastClock = context.Snapshot.GraphClockTime;
				_hasClock = true;
				var frame = new RenderingRuntimeImageFrame(preparedSurface, context.Snapshot.FrameNumber);
				_lastFrame = frame;
				LastOutputTexture = target;
				State = RuntimeNodeState.Ready;
				outputs.SetAvailable(image, PortValue.FromImageFrame(frame));
			}
			catch (Exception exception) {
				State = RuntimeNodeState.Faulted;
				WriteLastOrFailure(context, outputs, image, Failure("rendering.shader_graph.render_failed", exception.Message, context, exception));
			}
		}

		/// <summary>Direct rendering seam used by GPU/runtime contract tests.
		/// It exercises the same pool leases, pass order and history commit as
		/// the graph-bound Evaluate path.</summary>
		public Result<RenderTexture> Render(RenderTexture source, RenderTexture target, ulong frameNumber,
			double graphTime = 0d, bool paused = false, bool reset = false) {
			if (_disposed) return Result<RenderTexture>.Failure(DiagnosticFor("rendering.shader_graph.disposed", "Shader pass graph node is disposed."));
			if (target == null || !target.IsCreated()) return Result<RenderTexture>.Failure(DiagnosticFor("rendering.shader_graph.target", "A created RenderTexture target is required."));
			if (frameNumber == 0) return Result<RenderTexture>.Failure(DiagnosticFor("rendering.shader_graph.frame", "Frame number must be positive."));
			try {
				Texture input = source ?? (Texture)Texture2D.blackTexture;
				ShaderRuntimeUniformApplier.Apply(_material, _binding, graphTime, 0d, frameNumber,
					target.width, target.height, StableSeed(NodeId.Value));
				_material.SetFloat("_Paused", paused ? 1f : 0f);
				_material.SetFloat("_Reset", reset ? 1f : 0f);
				var result = RenderCore(input, target, frameNumber, graphTime, paused, reset);
				if (result.IsFailure) return result;
				LastOutputTexture = target;
				return result;
			}
			catch (Exception exception) {
				return Result<RenderTexture>.Failure(new Diagnostic(new DiagnosticCode("rendering.shader_graph.render_failed"), Severity.Error,
					exception.Message, nodeId: NodeId, nodeTypeId: TypeId, generationId: GenerationId,
					module: "rendering", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		public Result ResetHistory(ulong frameNumber = 1UL) {
			if (_history == null) return Result.Success();
			if (!_history.Reset(_historyKey, frameNumber)) return Result.Failure(DiagnosticFor("rendering.shader_graph.history_reset", "Shader history reset failed."));
			return Result.Success();
		}

		private Result<RenderTexture> RenderCore(Texture source, RenderTexture target, ulong frameNumber,
			double graphTime, bool paused, bool reset) {
			if (_binding.Stateful && !EnsureHistory(target, frameNumber))
				return Result<RenderTexture>.Failure(DiagnosticFor("rendering.shader_graph.history_allocate", "Stateful shader history could not allocate its RenderTexture ring."));
			if (_binding.Stateful && reset && !ResetHistory(frameNumber).IsSuccess)
				return Result<RenderTexture>.Failure(DiagnosticFor("rendering.shader_graph.history_reset", "Stateful shader history reset failed."));

			var validBefore = false;
			if (_history != null && _history.TryGetSnapshot(_historyKey, out var historyBefore)) validBefore = historyBefore.IsValid;
			if (_binding.Stateful && paused && validBefore) {
				if (!TryGetHistoryTexture(0, out var frozen))
					return Result<RenderTexture>.Failure(DiagnosticFor("rendering.shader_graph.history_missing", "Paused shader history has no readable slot."));
				Graphics.Blit(frozen, target);
				LastPassCount = 0;
				LastTemporaryLeaseCount = 0;
				return Result<RenderTexture>.Success(target);
			}

			var effectivePaused = false;
			if (_history != null) {
				if (!_history.BeginFrame(_historyKey, frameNumber, graphTime, effectivePaused))
					return Result<RenderTexture>.Failure(DiagnosticFor("rendering.shader_graph.history_begin", "Stateful shader history could not begin its frame."));
				BindHistoryTextures();
			}

			var passes = _binding.Passes.OrderBy(x => x.Index).ToList();
			if (passes.Count == 0) return Result<RenderTexture>.Failure(DiagnosticFor("rendering.shader_graph.pass_missing", "Shader pass graph has no declared passes."));
			var current = source ?? Texture2D.blackTexture;
			// Preserve the original graph input separately from the current
			// ping-pong surface. Multi-pass family shaders use this to blend
			// extracted/warped/reduced results back into the source at their
			// explicit composite stage.
			_material.SetTexture("_SD_SourceTex", current);
			_lastExecutedPassIndices.Clear();
			_lastPassInputTextures.Clear();
			try {
				for (var index = 0; index < passes.Count; index++) {
					var pass = passes[index];
					var isLast = index == passes.Count - 1;
					var destination = target;
					if (!isLast) {
						var temporary = AcquireTemporary(target, frameNumber, index);
						if (temporary.IsFailure) return Result<RenderTexture>.Failure(temporary.Diagnostic);
						_temporaryLeases.Add(temporary.Value);
						destination = temporary.Value.Texture;
					}
					_material.SetTexture("_MainTex", current);
					if (_binding.InputProperties.TryGetValue(new PortId("input"), out var inputProperty))
						_material.SetTexture(inputProperty, current);
					_material.SetFloat("_SD_PassIndex", pass.Index);
					_material.SetFloat("_SD_PassCount", passes.Count);
					_lastExecutedPassIndices.Add(pass.Index);
					_lastPassInputTextures.Add(current);
					// Pass.Index is a real ShaderLab pass index. The editor
					// validator checks it against the direct Shader asset, so
					// an out-of-range declaration fails before production.
					Graphics.Blit(current, destination, _material, pass.Index);
					current = destination;
				}
				LastPassCount = passes.Count;
				LastTemporaryLeaseCount = _temporaryLeases.Count;
				if (_history != null) {
					if (!_history.Commit(_historyKey, frameNumber, target))
						return Result<RenderTexture>.Failure(DiagnosticFor("rendering.shader_graph.history_commit", "Stateful shader history commit failed."));
					LastCommittedFrame = frameNumber;
				}
				return Result<RenderTexture>.Success(target);
			}
			finally {
				ReleaseTemporaryLeases(frameNumber);
			}
		}

		private bool EnsureHistory(RenderTexture target, ulong frameNumber) {
			if (_history == null) return true;
			var format = target.graphicsFormat;
			if (format == GraphicsFormat.None) format = GraphicsFormat.R8G8B8A8_UNorm;
			var descriptor = new TemporalHistoryDescriptor(target.width, target.height, format);
			if (!_history.Ensure(_historyKey, descriptor, Math.Max(2, _binding.HistorySlots), _binding.WarmupFrames, frameNumber)) return false;
			if (_historyLease == null || _historyLease.IsReleased) {
				if (!_history.TryAcquire(_historyKey, out _historyLease)) return false;
			}
			return true;
		}

		private Texture BindInputs(NodeExecutionContext context, bool reset, RenderTexture target) {
			Texture source = null;
			if (_binding.Inputs.Count > 0) {
				foreach (var input in _binding.Inputs) {
					if (input.Role == ShaderInputRole.History) continue;
					if (input.Type != NodePortType.ImageFrame) {
						if (TryInputValue(context, input.PortId, out var value)) ApplyTypedParameter(input.Property, null, value);
						continue;
					}
					var texture = TryInputTexture(context, input.PortId);
					if (texture == null && input.Required) throw new InvalidOperationException("Required shader graph input is unavailable: " + input.PortId.Value);
					texture = texture ?? DefaultTexture(input.DefaultImage);
					_material.SetTexture(input.Property, texture);
					if (source == null && (input.Role == ShaderInputRole.Primary || _binding.Inputs.Count == 1)) source = texture;
				}
			}
			else if (!_generator) {
				var firstId = _blend ? new PortId("a") : new PortId("input");
				source = TryInputTexture(context, firstId);
				if (source == null) throw new InvalidOperationException("Required shader graph input is unavailable: " + firstId.Value);
				_material.SetTexture(PropertyFor(firstId, "_MainTex"), source);
				if (_blend) {
					var second = TryInputTexture(context, new PortId("b"));
					if (second == null) throw new InvalidOperationException("Required shader graph input is unavailable: b");
					_material.SetTexture(PropertyFor(new PortId("b"), "_TexB"), second);
				}
			}
			BindHistoryTextures();
			return source ?? Texture2D.blackTexture;
		}

		private void BindHistoryTextures() {
			if (_history == null) return;
			foreach (var input in _binding.Inputs.Where(x => x.Role == ShaderInputRole.History)) {
				var offset = input.Property.EndsWith("HistoryTex2", StringComparison.OrdinalIgnoreCase) ? 1
					: input.Property.EndsWith("HistoryTex3", StringComparison.OrdinalIgnoreCase) ? 2 : 0;
				if (TryGetHistoryTexture(offset, out var texture)) _material.SetTexture(input.Property, texture);
				else _material.SetTexture(input.Property, Texture2D.blackTexture);
			}
			// Some generated entries bind history through a minimal input
			// list. Set the canonical properties as well so family shaders
			// and legacy aliases always see the same ring.
			for (var offset = 0; offset < 3; offset++)
				if (TryGetHistoryTexture(offset, out var texture)) _material.SetTexture(offset == 0 ? "_HistoryTex" : offset == 1 ? "_HistoryTex2" : "_HistoryTex3", texture);
		}

		private bool TryGetHistoryTexture(int offset, out RenderTexture texture) {
			texture = null;
			return _history != null && _history.TryGetTexture(_historyKey, offset, out texture);
		}

		private Result<TextureLeaseHandle> AcquireTemporary(RenderTexture target, ulong frameNumber, int passIndex) {
			var format = target.graphicsFormat == GraphicsFormat.None ? GraphicsFormat.R8G8B8A8_UNorm : target.graphicsFormat;
			var descriptor = new TextureDescriptor(target.width, target.height, format);
			return _pool.Acquire(descriptor, TemporaryOwner(passIndex), Math.Max(1UL, frameNumber));
		}

		private void ReleaseTemporaryLeases(ulong frameNumber) {
			var releaseFrame = Math.Max(1UL, frameNumber);
			for (var index = 0; index < _temporaryLeases.Count; index++) {
				var lease = _temporaryLeases[index];
				if (lease != null && !lease.IsReleased) lease.Release(TemporaryOwner(index), releaseFrame);
			}
			_temporaryLeases.Clear();
		}

		private ResourceOwnerKey TemporaryOwner(int index) => new ResourceOwnerKey(_owner.SessionId,
			ResourceOwnerKind.RuntimeNode, _owner.OwnerId, _owner.GenerationId, "temporary." + index, LeaseRole.Output);

		private bool TryInputValue(NodeExecutionContext context, PortId id, out ParameterValue value) {
			value = default(ParameterValue);
			if (!context.Inputs.TryGetValue(id, out var input) || !input.HasValue) return false;
			try { value = input.Value.AsParameterValue(); return true; }
			catch (InvalidOperationException) { return false; }
		}

		private Texture TryInputTexture(NodeExecutionContext context, PortId id) {
			if (!context.Inputs.TryGetValue(id, out var input) || !input.HasValue || !input.Value.IsImageFrame) return null;
			return (input.Value.AsImageFrame() as IRuntimeImageFrameSurface)?.NativeSurface as Texture;
		}

		private void ApplyParameters(FrameSnapshot snapshot) {
			foreach (var parameter in _binding.Parameters)
				if (snapshot.EffectiveValues.TryGetValue(new ParameterKey(NodeId, parameter.ParameterId), out var value)) ApplyTypedParameter(parameter.Property, parameter, value);
		}

		private void ApplyTypedParameter(string property, ShaderParameterBinding binding, ParameterValue value) {
			switch (value.Type) {
				case ParameterType.Float: _material.SetFloat(property, value.AsFloat()); break;
				case ParameterType.Int: _material.SetInt(property, value.AsInt()); break;
				case ParameterType.Bool: _material.SetFloat(property, value.AsBool() ? 1f : 0f); break;
				case ParameterType.Color: var c = value.AsColor(); _material.SetVector(property, new Vector4(c.R, c.G, c.B, c.A)); break;
				case ParameterType.Vector2: var v2 = value.AsVector2(); _material.SetVector(property, new Vector4(v2.X, v2.Y, 0f, 0f)); break;
				case ParameterType.Vector3: var v3 = value.AsVector3(); _material.SetVector(property, new Vector4(v3.X, v3.Y, v3.Z, 0f)); break;
				case ParameterType.Vector4: var v4 = value.AsVector4(); _material.SetVector(property, new Vector4(v4.X, v4.Y, v4.Z, v4.W)); break;
				case ParameterType.Enum:
					var option = value.AsString();
					var mapped = 0;
					if (binding != null && binding.EnumMapping.TryGetValue(option, out var enumValue)) mapped = enumValue;
					_material.SetInt(property, mapped);
					break;
			}
		}

		private bool ReadBoolParameter(FrameSnapshot snapshot, string id) {
			var parameter = _binding.Parameters.FirstOrDefault(x => string.Equals(x.ParameterId.Value, id, StringComparison.Ordinal));
			return parameter != null && snapshot.EffectiveValues.TryGetValue(new ParameterKey(NodeId, parameter.ParameterId), out var value) && value.Type == ParameterType.Bool && value.AsBool();
		}

		private string PropertyFor(PortId id, string fallback) => _binding.InputProperties.TryGetValue(id, out var property) ? property : fallback;
		private static Texture DefaultTexture(RuntimeDefaultImageKind? kind) => kind == RuntimeDefaultImageKind.OpaqueWhite ? Texture2D.whiteTexture : Texture2D.blackTexture;

		private void WriteLastOrFailure(NodeExecutionContext context, NodeOutputWriter outputs, PortId image, Diagnostic diagnostic) {
			if (_lastFrame != null) { outputs.SetAvailable(image, PortValue.FromImageFrame(_lastFrame)); context.Diagnostics.Report(diagnostic); }
			else outputs.SetPreparing(image, diagnostic);
		}

		private Diagnostic Failure(string code, string message, NodeExecutionContext context, Exception exception = null)
			=> new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: NodeId, nodeTypeId: TypeId,
				generationId: GenerationId, frameNumber: context == null ? 0 : unchecked((long)context.Snapshot.FrameNumber),
				graphClockTime: context == null ? 0d : context.Snapshot.GraphClockTime, module: "rendering",
				exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception));

		private Diagnostic DiagnosticFor(string code, string message)
			=> new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: NodeId, nodeTypeId: TypeId,
				generationId: GenerationId, module: "rendering");

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			ReleaseTemporaryLeases(Math.Max(1UL, _pool.CurrentFrame));
			if (_historyLease != null) { _historyLease.Dispose(); _historyLease = null; }
			_history?.Release(_historyKey);
			_history?.Dispose();
			_lastFrame = null;
			LastOutputTexture = null;
			if (_material != null) UnityEngine.Object.DestroyImmediate(_material);
			State = RuntimeNodeState.Disposed;
		}

		private static float StableSeed(string value) {
			unchecked {
				uint hash = 2166136261u;
				foreach (var character in value ?? string.Empty) { hash ^= character; hash *= 16777619u; }
				return (hash % 1000003u) / 1000003f;
			}
		}

		private static float FiniteFloat(double value) {
			if (double.IsNaN(value) || double.IsInfinity(value)) return 0f;
			if (value >= float.MaxValue) return float.MaxValue;
			if (value <= float.MinValue) return float.MinValue;
			return (float)value;
		}
	}
}
