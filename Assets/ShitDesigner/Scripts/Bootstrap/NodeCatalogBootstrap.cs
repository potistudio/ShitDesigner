using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Media;
using ShitDesigner.Nodes;
using ShitDesigner.Project;
using ShitDesigner.Rendering;
using ShitDesigner.Runtime;
using ShitDesigner.Scene;

namespace ShitDesigner.Bootstrap {
	/// <summary>
	/// Composition-root adapter. Nodes stays Core+Runtime-only; this boundary
	/// translates its immutable descriptors to the persisted Graph/Project
	/// definitions and injects the explicit Runtime factories.
	/// </summary>
	public static class NodeCatalogBootstrap {
		/// <summary>Translates persisted Project output settings at the
		/// composition boundary. Runtime/Rendering never reference Project
		/// directly, so a project reload creates a new session policy.</summary>
		public static IRuntimeOutputFormatPolicy CreateOutputFormatPolicy(ProjectOutputSettings settings) {
			if (settings == null) throw new ArgumentNullException(nameof(settings));
			return new RuntimeOutputFormatPolicy(settings.DynamicRange == ProjectDynamicRange.Hdr
				? RuntimeDynamicRange.Hdr
				: RuntimeDynamicRange.Ldr);
		}

		public static SceneIsolationManager CreateUnitySceneIsolation(SceneLayerPool layers = null, IScenePhysicsStepper physicsStepper = null,
			float instantiateIntegrationTimeMilliseconds = SceneIsolationManager.DefaultInstantiateIntegrationTimeMilliseconds)
			=> new SceneIsolationManager(layers, new UnityCameraRenderSource(), physicsStepper, instantiateIntegrationTimeMilliseconds);

		public static IVideoBackendFactory CreateVideoBackendFactory(IVideoBackendFactory unity, IVideoBackendFactory hap)
			=> new CompositeVideoBackendFactory(unity, hap);

		/// <summary>Builds the Hap factory with an injected native API. The
		/// API may be the P/Invoke implementation or an unsupported-platform
		/// implementation; either way availability is diagnosed by the
		/// backend at prepare time instead of being hidden by composition.</summary>
		public static IVideoBackendFactory CreateVideoBackendFactory(IVideoBackendFactory unity, IHapNativeApi hapApi) {
			var hap = hapApi == null ? null : (IVideoBackendFactory)new HapVideoBackendFactory(() => new HapNativeDecoder(hapApi));
			return new CompositeVideoBackendFactory(unity, hap);
		}

		/// <summary>
		/// Builds the production factory table from the seven base visual
		/// bindings.  This is intentionally a small composition helper: it
		/// does not create a RuntimeSession, PlayerLoop or presentation
		/// objects.  A missing or unavailable binding fails before any Graph
		/// registration can take place.
		/// </summary>
		public static Result<NodeFactoryBindings, Diagnostic> BuildProductionBindings(
			IRuntimeVisualNodeBinding scene3d,
			IRuntimeVisualNodeBinding scene2d,
			IRuntimeVisualNodeBinding shaderGenerator,
			IRuntimeVisualNodeBinding shaderEffect,
			IRuntimeVisualNodeBinding shaderBlend2,
			IRuntimeVisualNodeBinding videoPlayer,
			IRuntimeVisualNodeBinding feedback) {
			var required = new[] { scene3d, scene2d, shaderGenerator, shaderEffect, shaderBlend2, videoPlayer, feedback };
			if (required.Any(x => x == null)) return Result.Failure<NodeFactoryBindings, Diagnostic>(Failure("bootstrap.nodes.binding_missing", "All seven specialized visual node bindings are required.").Error);
			var bindings = new NodeFactoryBindings();
			foreach (var binding in required) {
				if (!binding.IsAvailable)
					return Result.Failure<NodeFactoryBindings, Diagnostic>(binding.AvailabilityDiagnostic ?? Failure("bootstrap.nodes.binding_unavailable", "A specialized visual node binding is unavailable.").Error);
				var registered = bindings.Register(binding);
				if (registered.IsFailure) return Result.Failure<NodeFactoryBindings, Diagnostic>(registered.Error);
			}
			return Result.Success<NodeFactoryBindings, Diagnostic>(bindings);
		}

		public static Result<NodeFactoryBindings, Diagnostic> BuildProductionBindings(IEnumerable<IRuntimeVisualNodeBinding> bindings) {
			var list = (bindings ?? Enumerable.Empty<IRuntimeVisualNodeBinding>()).ToList();
			var byType = list.Where(x => x != null).GroupBy(x => x.TypeId).ToDictionary(x => x.Key, x => x.First());
			IRuntimeVisualNodeBinding Find(string typeId) => byType.TryGetValue(new NodeTypeId(typeId), out var value) ? value : null;
			var baseResult = BuildProductionBindings(
				Find("shitdesigner.scene.3d"), Find("shitdesigner.scene.2d"),
				Find("shitdesigner.shader.generator"), Find("shitdesigner.shader.effect"),
				Find("shitdesigner.shader.blend2"), Find("shitdesigner.video.player"), Find("system.feedback"));
			if (baseResult.IsFailure) return baseResult;
			var result = baseResult.Value;
			foreach (var binding in list.Where(x => x != null && !result.Contains(x.TypeId))) {
				if (!binding.IsAvailable)
					return Result.Failure<NodeFactoryBindings, Diagnostic>(binding.AvailabilityDiagnostic ?? Failure("bootstrap.nodes.binding_unavailable", "A specialized visual node binding is unavailable.").Error);
				var registered = result.Register(binding);
				if (registered.IsFailure) return Result.Failure<NodeFactoryBindings, Diagnostic>(registered.Error);
			}
			if (!result.Availability.IsComplete)
				return Result.Failure<NodeFactoryBindings, Diagnostic>(Failure("bootstrap.nodes.binding_incomplete", "The production visual binding table is incomplete.").Error);
			return Result.Success<NodeFactoryBindings, Diagnostic>(result);
		}

		public static UnitResult<Diagnostic> RegisterProduction(NodeDefinitionCatalog catalog, NodeTypeRegistry registry, RuntimeSession session) {
			if (catalog == null || registry == null || session == null) return Failure("bootstrap.nodes.arguments", "Node catalog, registry, and runtime session are required.");
			if (!ReferenceEquals(registry, session.Registry)) return Failure("bootstrap.nodes.registry_mismatch", "Graph registry and RuntimeSession registry must be the same instance.");
			var valid = catalog.Validate();
			if (valid.IsFailure) return valid;
			if (catalog.Entries.Any(entry => catalog.SpecializedNodeTypeIdsForCatalog.Contains(entry.TypeId.Value, StringComparer.Ordinal) && entry.Factory is CatalogNodeFactory factory && factory.IsPlaceholder))
				return Failure("bootstrap.nodes.binding_missing", "Production node service bindings must be injected before graph registration.");
			foreach (var entry in catalog.Entries) {
				var adapted = Adapt(entry);
				if (adapted.IsFailure) return UnitResult.Failure<Diagnostic>(adapted.Error);
				if (registry.TryGet(adapted.Value.TypeId, out var existing)) {
					if (existing.SchemaVersion != adapted.Value.SchemaVersion || !string.Equals(existing.DisplayName, adapted.Value.DisplayName, StringComparison.Ordinal)
						|| existing.Ports.Count != adapted.Value.Ports.Count || existing.Parameters.Count != adapted.Value.Parameters.Count)
						return Failure("bootstrap.nodes.registry_mismatch", "The application registry does not match the production catalog.");
				}
				else {
					var registered = registry.Register(adapted.Value);
					if (registered.IsFailure) return registered;
				}
			}
			return catalog.RegisterFactories(session);
		}

		/// <summary>Preloads the same catalog definitions into the Application
		/// registry before a ProjectDocument is opened. Runtime registration
		/// remains a separate operation because the RuntimeSession owns the
		/// factory lifetime.</summary>
		public static UnitResult<Diagnostic> EnsureDefinitions(NodeDefinitionCatalog catalog, NodeTypeRegistry registry) {
			if (catalog == null || registry == null) return Failure("bootstrap.nodes.arguments", "Node catalog and registry are required.");
			var valid = catalog.Validate();
			if (valid.IsFailure) return valid;
			foreach (var entry in catalog.Entries) {
				var adapted = Adapt(entry);
				if (adapted.IsFailure) return UnitResult.Failure<Diagnostic>(adapted.Error);
				if (registry.TryGet(adapted.Value.TypeId, out var existing)) {
					if (existing.SchemaVersion != adapted.Value.SchemaVersion || existing.Ports.Count != adapted.Value.Ports.Count || existing.Parameters.Count != adapted.Value.Parameters.Count)
						return Failure("bootstrap.nodes.registry_mismatch", "The Application registry does not match the production catalog.");
					continue;
				}
				var registered = registry.Register(adapted.Value);
				if (registered.IsFailure) return registered;
			}
			return UnitResult.Success<Diagnostic>();
		}

		/// <summary>Production overload that also wires the explicit
		/// Rendering-owned Feedback committer into Runtime. No generic node
		/// reference is used for temporal history.</summary>
		public static UnitResult<Diagnostic> RegisterProduction(NodeDefinitionCatalog catalog, NodeTypeRegistry registry, RuntimeSession session, NodeFactoryBindings bindings) {
			if (bindings == null) return Failure("bootstrap.nodes.bindings_missing", "Production node bindings are required.");
			if (catalog == null) return Failure("bootstrap.nodes.catalog_missing", "Node catalog is required.");
			var complete = catalog.ValidateProductionBindings(bindings);
			if (complete.IsFailure) return complete;
			if (bindings.TryGetVisualBinding(new NodeTypeId("system.feedback"), out var feedback) && feedback is IFeedbackCommitter committer)
				session.FeedbackCommitter = committer;
			return RegisterProduction(catalog, registry, session);
		}

		/// <summary>Attaches the phase-owned Rendering output service and any
		/// binding-owned preparation (Feedback history) to one Runtime
		/// session. This is the only composition point that knows both
		/// services; visual modules remain independent.</summary>
		public static UnitResult<Diagnostic> AttachRuntimeVisualServices(RuntimeSession session, RuntimeOutputSurfaceService outputs, IRuntimeResourcePreparation additionalPreparation = null) {
			if (session == null || outputs == null) return Failure("bootstrap.runtime.services", "Runtime session and output surface service are required.");
			session.OutputSurfaces = outputs;
			session.ResourcePreparation = additionalPreparation == null
				? (IRuntimeResourcePreparation)outputs
				: new CompositeResourcePreparation(outputs, additionalPreparation);
			session.ResourceFinalization = outputs;
			return UnitResult.Success<Diagnostic>();
		}

		public static UnitResult<Diagnostic> RegisterProduction(NodeDefinitionCatalog catalog, NodeTypeRegistry registry, RuntimeSession session, NodeFactoryBindings bindings, RuntimeOutputSurfaceService outputs) {
			var registered = RegisterProduction(catalog, registry, session, bindings);
			if (registered.IsFailure) return registered;
			IRuntimeResourcePreparation feedbackPreparation = null;
			if (bindings.TryGetVisualBinding(new NodeTypeId("system.feedback"), out var feedback) && feedback is IRuntimeResourcePreparation preparation)
				feedbackPreparation = preparation;
			return AttachRuntimeVisualServices(session, outputs, feedbackPreparation);
		}

		private sealed class CompositeResourcePreparation : IRuntimeResourcePreparationWithPlan, IDisposable {
			private readonly IRuntimeResourcePreparation[] _services;
			public CompositeResourcePreparation(params IRuntimeResourcePreparation[] services) { _services = services ?? Array.Empty<IRuntimeResourcePreparation>(); }
			public UnitResult<Diagnostic> Prepare(FrameSnapshot snapshot) {
				foreach (var service in _services) { var result = service.Prepare(snapshot); if (result.IsFailure) return result; }
				return UnitResult.Success<Diagnostic>();
			}
			public UnitResult<Diagnostic> Prepare(FrameSnapshot snapshot, FrameEvaluationContext evaluation) {
				foreach (var service in _services) {
					var result = service is IRuntimeResourcePreparationWithPlan planAware ? planAware.Prepare(snapshot, evaluation) : service.Prepare(snapshot);
					if (result.IsFailure) return result;
				}
				return UnitResult.Success<Diagnostic>();
			}
			public void Dispose() { foreach (var service in _services.OfType<IDisposable>()) service.Dispose(); }
		}

		public static Result<NodeTypeDefinition, Diagnostic> Adapt(NodeCatalogEntry entry) {
			if (entry == null) return Result.Failure<NodeTypeDefinition, Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.nodes.entry"), Severity.Error, "Node catalog entry is required.", module: "bootstrap"));
			var ports = entry.Ports.Select(port => new PortDefinition(new PortId(port.Id.Value), port.DisplayName, port.Direction == NodePortDirection.Input ? PortDirection.Input : PortDirection.Output, ToProjectType(port.Type), port.Required, port.DefaultImage.HasValue ? (DefaultImageKind?)ToProjectDefault(port.DefaultImage.Value) : null));
			var parameters = entry.Parameters.Select(parameter => {
				ParameterRange? range = null;
				if (parameter.Minimum.HasValue && parameter.Maximum.HasValue) range = new ParameterRange(parameter.Minimum.Value, parameter.Maximum.Value);
				var options = parameter.EnumOptions.Select(option => new EnumOptionDefinition(new ParameterId(option), option));
				return parameter.Type == ParameterType.Enum
					? new ParameterDefinition(new ParameterId(parameter.Id.Value), parameter.DisplayName, parameter.Type, parameter.DefaultValue, range, parameter.RuntimeStateful, options,
						parameter.Group, parameter.DisplayOrder, parameter.Description, parameter.Unit, parameter.Step,
						parameter.IsHidden ? ParameterVisibility.Hidden : parameter.IsReadOnly ? ParameterVisibility.ReadOnly : ParameterVisibility.Editable)
					: new ParameterDefinition(new ParameterId(parameter.Id.Value), parameter.DisplayName, parameter.Type, parameter.DefaultValue, range, parameter.RuntimeStateful, Enumerable.Empty<ParameterId>(),
						parameter.Group, parameter.DisplayOrder, parameter.Description, parameter.Unit, parameter.Step,
						parameter.IsHidden ? ParameterVisibility.Hidden : parameter.IsReadOnly ? ParameterVisibility.ReadOnly : ParameterVisibility.Editable);
			});
			try { return Result.Success<NodeTypeDefinition, Diagnostic>(new NodeTypeDefinition(new NodeTypeId(entry.TypeId.Value), entry.SchemaVersion, entry.DisplayName, entry.Category, ports, parameters, entry.SystemOwned, entry.UserAddable)); }
			catch (Exception exception) { return Result.Failure<NodeTypeDefinition, Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.nodes.definition"), Severity.Error, "Node catalog definition could not be adapted.", nodeTypeId: entry.TypeId, module: "bootstrap", exception: DiagnosticExceptionInfo.FromException(exception))); }
		}

		private static PortType ToProjectType(NodePortType type) => (PortType)Enum.Parse(typeof(PortType), type.ToString(), true);
		private static DefaultImageKind ToProjectDefault(RuntimeDefaultImageKind kind) => kind == RuntimeDefaultImageKind.OpaqueWhite ? DefaultImageKind.OpaqueWhite : kind == RuntimeDefaultImageKind.OpaqueBlack ? DefaultImageKind.OpaqueBlack : DefaultImageKind.TransparentBlack;
		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "bootstrap"));
	}
}
