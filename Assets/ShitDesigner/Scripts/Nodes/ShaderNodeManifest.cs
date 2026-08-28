using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Runtime;

namespace ShitDesigner.Nodes {
	/// <summary>
	/// The implementation family used by a shader node.  The enum is part of
	/// the neutral manifest contract, so the editor, catalog and renderer can
	/// agree on a family without loading a Unity Shader asset.
	/// </summary>
	public enum ShaderNodeFamily {
		Generator,
		Color,
		Geometry,
		Glitch,
		Convolution,
		Stylize,
		Key,
		Composite,
		Transition,
		Temporal,
		Audio,
		Raymarch,
		Utility,
		Custom
	}

	[Flags]
	public enum ShaderFeatureFlags {
		None = 0,
		ShaderModel45 = 1 << 0,
		Derivatives = 1 << 1,
		RandomWrite = 1 << 2,
		MultipleRenderTargets = 1 << 3,
		TextureArray = 1 << 4,
		AudioTexture = 1 << 5,
		History = 1 << 6,
		Compute = 1 << 7
	}

	public enum ShaderInputRole {
		Primary,
		Secondary,
		Mask,
		Displacement,
		History,
		Audio,
		Analysis,
		Custom
	}

	public enum ShaderPassKind {
		Draw,
		Compute,
		HistoryCopy,
		Reduction
	}

	/// <summary>One shader input mapping in the manifest.</summary>
	public sealed class ShaderNodeManifestInput {
		public PortId Id { get; }
		/// <summary>The source ledger token before the graph's lower-snake
		/// port normalization.  This keeps the JSON contract auditable while
		/// the runtime graph continues to use its existing PortId rules.</summary>
		public string SourceId { get; }
		public string DisplayName { get; }
		public NodePortType Type { get; }
		public bool Required { get; }
		public RuntimeDefaultImageKind? DefaultImage { get; }
		public string Property { get; }
		public ShaderInputRole Role { get; }

		public ShaderNodeManifestInput(PortId id, string displayName, string property,
			ShaderInputRole role = ShaderInputRole.Primary, NodePortType type = NodePortType.ImageFrame,
			bool required = true, RuntimeDefaultImageKind? defaultImage = null, string sourceId = null) {
			if (id.IsEmpty || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(property))
				throw new ArgumentException("Shader input identity and property are required.");
			if (type != NodePortType.ImageFrame && defaultImage.HasValue)
				throw new ArgumentException("Only optional ImageFrame inputs may have a default image.");
			if (defaultImage.HasValue && required)
				throw new ArgumentException("A required shader input cannot have a default image.");
			Id = id;
			SourceId = string.IsNullOrWhiteSpace(sourceId) ? id.Value : sourceId.Trim();
			DisplayName = displayName.Trim();
			Type = type;
			Required = required;
			DefaultImage = defaultImage;
			Property = property.Trim();
			Role = role;
		}
	}

	/// <summary>One typed shader parameter and its material property mapping.</summary>
	public sealed class ShaderNodeManifestParameter {
		// Keep the ID sourced from the neutral definition.  The overload that
		// accepts NodeParameterDefinition used to leave an auto-property at
		// default(ParameterId), which only surfaced when a generated asset was
		// decoded back into a Standalone-safe runtime manifest.
		public ParameterId Id => Definition.Id;
		/// <summary>Original ledger parameter token, retained for deterministic
		/// manifest↔ledger audits when a token is normalized for ParameterId.</summary>
		public string SourceId => _sourceId;
		public string Property { get; }
		public NodeParameterDefinition Definition { get; }
		public IReadOnlyDictionary<string, int> EnumMapping { get; }
		private readonly string _sourceId;

		public string DisplayName => Definition.DisplayName;
		public ParameterType Type => Definition.Type;
		public ParameterValue DefaultValue => Definition.DefaultValue;
		public ParameterValue? Minimum => Definition.Minimum;
		public ParameterValue? Maximum => Definition.Maximum;

		public ShaderNodeManifestParameter(ParameterId id, string displayName, ParameterType type,
			ParameterValue defaultValue, string property, ParameterValue? minimum = null,
			ParameterValue? maximum = null, bool runtimeStateful = false,
			IEnumerable<string> enumOptions = null, IDictionary<string, int> enumMapping = null,
			string group = null, int displayOrder = 0, string description = null,
			string unit = null, double step = 0d, bool isReadOnly = false, bool isHidden = false, string sourceId = null) {
			if (id.IsEmpty || string.IsNullOrWhiteSpace(property))
				throw new ArgumentException("Shader parameter identity and property are required.");
			Definition = new NodeParameterDefinition(id, displayName, type, defaultValue, minimum, maximum,
				runtimeStateful, enumOptions, group, displayOrder, description, unit, step, isReadOnly, isHidden);
			Property = property.Trim();
			_sourceId = string.IsNullOrWhiteSpace(sourceId) ? id.Value : sourceId.Trim();
			EnumMapping = BuildEnumMapping(Definition.EnumOptions, enumMapping);
		}

		public ShaderNodeManifestParameter(NodeParameterDefinition definition, string property,
			IDictionary<string, int> enumMapping = null, string sourceId = null) {
			Definition = definition ?? throw new ArgumentNullException(nameof(definition));
			if (string.IsNullOrWhiteSpace(property)) throw new ArgumentException("Shader parameter property is required.", nameof(property));
			Property = property.Trim();
			_sourceId = string.IsNullOrWhiteSpace(sourceId) ? definition.Id.Value : sourceId.Trim();
			EnumMapping = BuildEnumMapping(Definition.EnumOptions, enumMapping);
		}

		private static IReadOnlyDictionary<string, int> BuildEnumMapping(IReadOnlyList<string> options, IDictionary<string, int> mapping) {
			var result = new Dictionary<string, int>(StringComparer.Ordinal);
			if (mapping != null)
				foreach (var pair in mapping) result[pair.Key ?? string.Empty] = pair.Value;
			if (options != null)
				for (var index = 0; index < options.Count; index++)
					if (!result.ContainsKey(options[index] ?? string.Empty)) result[options[index] ?? string.Empty] = index;
			return new ReadOnlyDictionary<string, int>(result);
		}
	}

	/// <summary>One fixed shader pass/variant in a manifest entry.</summary>
	public sealed class ShaderNodeManifestPass {
		public string Id { get; }
		public int Index { get; }
		public ShaderPassKind Kind { get; }
		public string VariantId { get; }
		public string OutputRole { get; }
		public ShaderFeatureFlags RequiredFeatures { get; }

		public ShaderNodeManifestPass(string id, int index, string variantId = "default",
			ShaderPassKind kind = ShaderPassKind.Draw, string outputRole = "image",
			ShaderFeatureFlags requiredFeatures = ShaderFeatureFlags.None) {
			if (string.IsNullOrWhiteSpace(id) || index < 0 || string.IsNullOrWhiteSpace(variantId) || string.IsNullOrWhiteSpace(outputRole))
				throw new ArgumentException("Shader pass metadata is invalid.");
			Id = id.Trim();
			Index = index;
			Kind = kind;
			VariantId = variantId.Trim();
			OutputRole = outputRole.Trim();
			RequiredFeatures = requiredFeatures;
		}
	}

	/// <summary>Neutral declaration for one searchable shader node.</summary>
	public sealed class ShaderNodeManifestEntry {
		private readonly IReadOnlyList<ShaderNodeManifestInput> _inputs;
		private readonly IReadOnlyList<ShaderNodeManifestParameter> _parameters;
		private readonly IReadOnlyList<ShaderNodeManifestPass> _passes;
		private readonly IReadOnlyList<string> _aliases;

		public NodeTypeId TypeId { get; }
		public int SchemaVersion { get; }
		public string DisplayName { get; }
		public string Category { get; }
		public ShaderNodeFamily Family { get; }
		public string ShaderKey { get; }
		public string VariantId { get; }
		public ShaderFeatureFlags RequiredFeatures { get; }
		public bool Stateful { get; }
		public int HistorySlots { get; }
		public bool UserAddable { get; }
		public string Description { get; }
		public IReadOnlyList<ShaderNodeManifestInput> Inputs => _inputs;
		public IReadOnlyList<ShaderNodeManifestParameter> Parameters => _parameters;
		public IReadOnlyList<ShaderNodeManifestPass> Passes => _passes;
		public IReadOnlyList<string> Aliases => _aliases;
		public int OutputPass { get; }
		public string Priority { get; }
		public int WarmupFrames { get; }
		public string SourceLedger { get; }
		public int SourceVariant { get; }

		public ShaderNodeManifestEntry(NodeTypeId typeId, string displayName, string category,
			ShaderNodeFamily family, string shaderKey, string variantId = "default",
			IEnumerable<ShaderNodeManifestInput> inputs = null,
			IEnumerable<ShaderNodeManifestParameter> parameters = null,
			IEnumerable<ShaderNodeManifestPass> passes = null,
			int outputPass = 0, ShaderFeatureFlags requiredFeatures = ShaderFeatureFlags.None,
			bool stateful = false, int historySlots = 0, IEnumerable<string> aliases = null,
			string description = null, int schemaVersion = 1, bool userAddable = true) {
			if (typeId.IsEmpty || string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(category)
				|| string.IsNullOrWhiteSpace(shaderKey) || string.IsNullOrWhiteSpace(variantId))
				throw new ArgumentException("Shader manifest entry metadata is invalid.");
			if (schemaVersion < 1 || outputPass < 0 || historySlots < 0)
				throw new ArgumentOutOfRangeException(nameof(schemaVersion));
			TypeId = typeId;
			SchemaVersion = schemaVersion;
			DisplayName = displayName.Trim();
			Category = category.Trim();
			Family = family;
			ShaderKey = shaderKey.Trim();
			VariantId = variantId.Trim();
			RequiredFeatures = requiredFeatures;
			Stateful = stateful;
			HistorySlots = historySlots;
			UserAddable = userAddable;
			Description = description ?? string.Empty;
			OutputPass = outputPass;
			Priority = string.Empty;
			WarmupFrames = 0;
			SourceLedger = string.Empty;
			SourceVariant = 0;
			_inputs = ReadOnlyList(inputs);
			_parameters = ReadOnlyList(parameters);
			_passes = ReadOnlyList(passes ?? new[] { new ShaderNodeManifestPass("main", outputPass, VariantId) });
			_aliases = new ReadOnlyCollection<string>((aliases ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x.Trim()).Distinct(StringComparer.Ordinal).ToList());
		}

		public ShaderNodeManifestEntry(NodeTypeId typeId, string displayName, string category,
			ShaderNodeFamily family, string shaderKey, string variantId,
			IEnumerable<ShaderNodeManifestInput> inputs, IEnumerable<ShaderNodeManifestParameter> parameters,
			IEnumerable<ShaderNodeManifestPass> passes, int outputPass, ShaderFeatureFlags requiredFeatures,
			bool stateful, int historySlots, IEnumerable<string> aliases, string description,
			int schemaVersion, bool userAddable, string priority, int warmupFrames,
			string sourceLedger, int sourceVariant)
			: this(typeId, displayName, category, family, shaderKey, variantId, inputs, parameters, passes,
				outputPass, requiredFeatures, stateful, historySlots, aliases, description, schemaVersion, userAddable) {
			if (warmupFrames < 0 || sourceVariant < 0) throw new ArgumentOutOfRangeException(nameof(warmupFrames));
			Priority = priority ?? string.Empty;
			WarmupFrames = warmupFrames;
			SourceLedger = sourceLedger ?? string.Empty;
			SourceVariant = sourceVariant;
		}

		public NodeDefinition ToNodeDefinition() {
			var ports = Inputs.Select(x => new NodePortDefinition(x.Id, x.DisplayName, NodePortDirection.Input, x.Type, x.Required, x.DefaultImage)).ToList();
			ports.Add(new NodePortDefinition(new PortId("image"), "Image", NodePortDirection.Output, NodePortType.ImageFrame, false));
			var parameters = Parameters.Select(x => x.Definition).ToList();
			return new NodeDefinition(TypeId, SchemaVersion, DisplayName, Category, ports, parameters, userAddable: UserAddable);
		}

		public ShaderNodeBinding ToShaderBinding() {
			var inputProperties = Inputs.ToDictionary(x => x.Id, x => x.Property);
			var parameterProperties = Parameters.ToDictionary(x => x.Definition.Id, x => x.Property);
			var inputBindings = Inputs.Select(x => new ShaderInputBinding(x.Id, x.Property, x.Role, x.Required, x.DefaultImage, x.Type));
			var parameterBindings = Parameters.Select(x => new ShaderParameterBinding(x.Definition.Id, x.Property, x.Type, x.EnumMapping));
			var passes = Passes.Select(x => new ShaderPassBinding(x.Id, x.Index, x.Kind, x.VariantId, x.OutputRole, x.RequiredFeatures));
			return new ShaderNodeBinding(ShaderKey, inputProperties, parameterProperties, OutputPass,
				TypeId, Family, VariantId, passes, inputBindings, parameterBindings, RequiredFeatures,
				Stateful, HistorySlots, Aliases, SourceVariant, WarmupFrames);
		}

		private static IReadOnlyList<T> ReadOnlyList<T>(IEnumerable<T> source) {
			var values = (source ?? Enumerable.Empty<T>()).ToList();
			if (values.Any(x => object.ReferenceEquals(x, null))) throw new ArgumentException("Manifest collections cannot contain null entries.");
			return new ReadOnlyCollection<T>(values);
		}
	}

	/// <summary>Immutable manifest containing all shader node declarations.</summary>
	public sealed class ShaderNodeManifest {
		public const int CurrentSchemaVersion = 1;
		private readonly IReadOnlyList<ShaderNodeManifestEntry> _entries;
		public int SchemaVersion { get; }
		public IReadOnlyList<ShaderNodeManifestEntry> Entries => _entries;

		public ShaderNodeManifest(IEnumerable<ShaderNodeManifestEntry> entries, int schemaVersion = CurrentSchemaVersion) {
			if (schemaVersion < 1) throw new ArgumentOutOfRangeException(nameof(schemaVersion));
			SchemaVersion = schemaVersion;
			_entries = new ReadOnlyCollection<ShaderNodeManifestEntry>((entries ?? Enumerable.Empty<ShaderNodeManifestEntry>()).ToList());
		}

		public ShaderNodeManifestEntry Find(NodeTypeId typeId) => _entries.FirstOrDefault(x => x != null && x.TypeId == typeId);
		public ShaderNodeManifestEntry Find(string typeId) => NodeTypeId.TryParse(typeId, out var parsed) ? Find(parsed) : null;

		/// <summary>
		/// Built-in declarations are intentionally expressed through
		/// the manifest. This keeps their stable IDs and properties intact
		/// while allowing new families to be added without a second catalog.
		/// </summary>
		public static ShaderNodeManifest CreateBuiltIn() {
			var generator = new ShaderNodeManifestEntry(
				new NodeTypeId("shitdesigner.shader.generator"), "Shader Generator", "Shader/Generator",
				ShaderNodeFamily.Generator, "builtin.shader.generator", "default",
				parameters: new[] { new ShaderNodeManifestParameter(
					new NodeParameterDefinition(new ParameterId("color"), "Color", ParameterType.Color,
						ParameterValue.FromColor(new ColorValue(0, 0, 0, 1)), group: "Shader", displayOrder: 0), "_Color") },
				aliases: new[] { "solid color", "generator" }, description: "Legacy HDR solid-color generator.");
			var effect = new ShaderNodeManifestEntry(
				new NodeTypeId("shitdesigner.shader.effect"), "Shader Effect", "Shader/Effect",
				ShaderNodeFamily.Custom, "builtin.shader.effect", "default",
				inputs: new[] { new ShaderNodeManifestInput(new PortId("input"), "Input", "_MainTex") },
				aliases: new[] { "effect", "passthrough" }, description: "Legacy single-input shader effect.");
			var blend = new ShaderNodeManifestEntry(
				new NodeTypeId("shitdesigner.shader.blend2"), "Shader 2-input Blend", "Shader/Effect",
				ShaderNodeFamily.Composite, "builtin.shader.blend2", "default",
				inputs: new[]
				{
					new ShaderNodeManifestInput(new PortId("a"), "A", "_TexA", ShaderInputRole.Primary),
					new ShaderNodeManifestInput(new PortId("b"), "B", "_TexB", ShaderInputRole.Secondary)
				}, aliases: new[] { "blend2", "alpha blend" }, description: "Legacy two-input blend role.");
			var recursiveRectangles = CreateRecursiveRectangles();
			return new ShaderNodeManifest(new[] { generator, effect, blend, recursiveRectangles });
		}

		private static ShaderNodeManifestEntry CreateRecursiveRectangles() {
			var parameters = new[]
			{
				Parameter("max_depth", "Max Depth", ParameterType.Int, ParameterValue.FromInt(5), "_MaxDepth", ParameterValue.FromInt(0), ParameterValue.FromInt(8), group: "Structure", order: 0),
				Parameter("min_leaf_size", "Min Leaf Size", ParameterType.Float, ParameterValue.FromFloat(.08f), "_MinLeafSize", ParameterValue.FromFloat(.001f), ParameterValue.FromFloat(.5f), group: "Structure", order: 1),
				Parameter("split_probability", "Split Probability", ParameterType.Float, ParameterValue.FromFloat(.9f), "_SplitProbability", ParameterValue.FromFloat(0f), ParameterValue.FromFloat(1f), group: "Structure", order: 2),
				EnumParameter("axis_mode", "Axis Mode", "_AxisMode", "longer_side", new[] { "longer_side", "horizontal", "vertical", "random" }, "Structure", 3),
				Parameter("ratio_min", "Ratio Min", ParameterType.Float, ParameterValue.FromFloat(.25f), "_RatioMin", ParameterValue.FromFloat(0f), ParameterValue.FromFloat(1f), group: "Structure", order: 4),
				Parameter("ratio_max", "Ratio Max", ParameterType.Float, ParameterValue.FromFloat(.75f), "_RatioMax", ParameterValue.FromFloat(0f), ParameterValue.FromFloat(1f), group: "Structure", order: 5),
				Parameter("seed", "Seed", ParameterType.Int, ParameterValue.FromInt(1), "_StructureSeed", ParameterValue.FromInt(-1000000), ParameterValue.FromInt(1000000), group: "Structure", order: 6),
				Parameter("reveal_progress", "Reveal Progress", ParameterType.Float, ParameterValue.FromFloat(1f), "_RevealProgress", ParameterValue.FromFloat(0f), ParameterValue.FromFloat(1f), group: "Animation", order: 10),
				Parameter("split_duration", "Split Duration", ParameterType.Float, ParameterValue.FromFloat(.15f), "_SplitDuration", ParameterValue.FromFloat(.001f), ParameterValue.FromFloat(1f), group: "Animation", order: 11),
				Parameter("split_stagger", "Split Stagger", ParameterType.Float, ParameterValue.FromFloat(.04f), "_SplitStagger", ParameterValue.FromFloat(0f), ParameterValue.FromFloat(1f), group: "Animation", order: 12),
				EnumParameter("easing", "Easing", "_Easing", "smooth_step", new[] { "linear", "smooth_step", "ease_in", "ease_out", "ease_in_out" }, "Animation", 13),
				Parameter("color_a", "Color A", ParameterType.Color, ParameterValue.FromColor(new ColorValue(.05f, .12f, .22f, 1f)), "_ColorA", group: "Appearance", order: 20),
				Parameter("color_b", "Color B", ParameterType.Color, ParameterValue.FromColor(new ColorValue(.95f, .32f, .14f, 1f)), "_ColorB", group: "Appearance", order: 21),
				Parameter("gutter", "Gutter", ParameterType.Float, ParameterValue.FromFloat(.004f), "_Gutter", ParameterValue.FromFloat(0f), ParameterValue.FromFloat(.1f), group: "Appearance", order: 22),
				Parameter("line_color", "Line Color", ParameterType.Color, ParameterValue.FromColor(new ColorValue(.01f, .01f, .01f, 1f)), "_LineColor", group: "Appearance", order: 23)
			};
			return new ShaderNodeManifestEntry(
				new NodeTypeId("shitdesigner.shader.generator.recursive-rectangles"), "Recursive Rectangles", "Shader/Generator",
				ShaderNodeFamily.Generator, "builtin.shader.generator.recursive-rectangles", "default",
				parameters: parameters, aliases: new[] { "recursive rectangles", "bsp rectangles" },
				description: "Deterministic BSP-based recursive rectangle generator.");
		}

		private static ShaderNodeManifestParameter Parameter(string id, string displayName, ParameterType type,
			ParameterValue defaultValue, string property, ParameterValue? minimum = null, ParameterValue? maximum = null,
			string group = null, int order = 0) {
			return new ShaderNodeManifestParameter(new ParameterId(id), displayName, type, defaultValue, property,
				minimum, maximum, group: group, displayOrder: order);
		}

		private static ShaderNodeManifestParameter EnumParameter(string id, string displayName, string property,
			string defaultValue, string[] options, string group, int order) {
			return new ShaderNodeManifestParameter(new ParameterId(id), displayName, ParameterType.Enum,
				ParameterValue.FromEnum(defaultValue), property, enumOptions: options, group: group, displayOrder: order);
		}
	}

	/// <summary>Manifest validation is deliberately independent of Unity so
	/// EditMode and external catalog tooling can run it deterministically.</summary>
	public static class ShaderNodeManifestValidator {
		public static UnitResult<Diagnostic> Validate(ShaderNodeManifest manifest) {
			if (manifest == null) return Failure("nodes.shader_manifest_missing", "Shader node manifest is required.");
			if (manifest.SchemaVersion != ShaderNodeManifest.CurrentSchemaVersion) return Failure("nodes.shader_manifest_schema", "Unsupported shader node manifest schema.");
			if (manifest.Entries == null || manifest.Entries.Count == 0) return Failure("nodes.shader_manifest_empty", "Shader node manifest contains no entries.");
			if (manifest.Entries.Any(x => x == null)) return Failure("nodes.shader_manifest_null", "Shader node manifest contains a null entry.");
			if (manifest.Entries.GroupBy(x => x.TypeId).Any(x => x.Count() > 1)) return Failure("nodes.shader_manifest_duplicate", "Shader node manifest TypeIds must be unique.");
			foreach (var entry in manifest.Entries) {
				var result = ValidateEntry(entry);
				if (result.IsFailure) return result;
			}
			return UnitResult.Success<Diagnostic>();
		}

		public static UnitResult<Diagnostic> ValidateEntry(ShaderNodeManifestEntry entry) {
			if (entry == null) return Failure("nodes.shader_manifest_entry", "Shader manifest entry is required.");
			if (entry.SchemaVersion < 1 || entry.TypeId.IsEmpty || string.IsNullOrWhiteSpace(entry.DisplayName)
				|| string.IsNullOrWhiteSpace(entry.Category) || string.IsNullOrWhiteSpace(entry.ShaderKey)
				|| string.IsNullOrWhiteSpace(entry.VariantId)) return Failure("nodes.shader_manifest_metadata", "Shader manifest entry metadata is incomplete.");
			if (entry.Stateful && entry.HistorySlots <= 0) return Failure("nodes.shader_manifest_history", "A stateful shader entry must declare at least one history slot.");
			if (!entry.Stateful && entry.HistorySlots != 0) return Failure("nodes.shader_manifest_history", "A stateless shader entry cannot declare history slots.");
			if (entry.Passes.Count == 0) return Failure("nodes.shader_manifest_pass_missing", "Shader manifest entry must declare at least one pass.");
			if (entry.Passes.GroupBy(x => x.Index).Any(x => x.Count() > 1) || entry.Passes.GroupBy(x => x.Id, StringComparer.Ordinal).Any(x => x.Count() > 1)) return Failure("nodes.shader_manifest_pass_duplicate", "Shader manifest pass IDs and indexes must be unique.");
			if (entry.OutputPass < 0 || !entry.Passes.Any(x => x.Index == entry.OutputPass)) return Failure("nodes.shader_manifest_pass_range", "Shader manifest output pass is not declared.");
			if (entry.Inputs.GroupBy(x => x.Id).Any(x => x.Count() > 1)) return Failure("nodes.shader_manifest_input_duplicate", "Shader manifest input IDs must be unique.");
			if (entry.Parameters.GroupBy(x => x.Definition.Id).Any(x => x.Count() > 1)) return Failure("nodes.shader_manifest_parameter_duplicate", "Shader manifest parameter IDs must be unique.");
			var properties = entry.Inputs.Select(x => x.Property).Concat(entry.Parameters.Select(x => x.Property)).ToList();
			if (properties.Any(string.IsNullOrWhiteSpace) || properties.GroupBy(x => x, StringComparer.Ordinal).Any(x => x.Count() > 1)) return Failure("nodes.shader_manifest_property_duplicate", "Shader manifest material properties must be non-empty and unique.");
			foreach (var parameter in entry.Parameters) {
				if (parameter == null) return Failure("nodes.shader_manifest_parameter", "Shader manifest parameter is null.");
				if (parameter.Type == ParameterType.Enum) {
					if (parameter.Definition.EnumOptions.Count == 0) return Failure("nodes.shader_manifest_enum_options", "Enum shader parameters must declare options.");
					if (parameter.Definition.EnumOptions.Any(x => !parameter.EnumMapping.ContainsKey(x))) return Failure("nodes.shader_manifest_enum_mapping", "Enum shader parameter mapping is incomplete.");
					if (!parameter.EnumMapping.ContainsKey(parameter.DefaultValue.AsString())) return Failure("nodes.shader_manifest_enum_default", "Enum shader parameter default is not mapped.");
				}
			}
			try {
				entry.ToNodeDefinition();
				entry.ToShaderBinding();
			}
			catch (Exception exception) {
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("nodes.shader_manifest_contract"), Severity.Error,
					"Manifest entry " + entry.TypeId.Value + " failed runtime binding conversion: " + exception.ToString(),
					module: "nodes", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
			return UnitResult.Success<Diagnostic>();
		}

		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "nodes"));
	}

	/// <summary>Small generator seam used by the editor validator and tests.
	/// It creates the same runtime catalog path as production bootstrap.</summary>
	public static class ShaderNodeManifestGenerator {
		public static Result<IReadOnlyList<NodeDefinition>, Diagnostic> GenerateDefinitions(ShaderNodeManifest manifest) {
			var valid = ShaderNodeManifestValidator.Validate(manifest);
			if (valid.IsFailure) return Result.Failure<IReadOnlyList<NodeDefinition>, Diagnostic>(valid.Error);
			return Result.Success<IReadOnlyList<NodeDefinition>, Diagnostic>(new ReadOnlyCollection<NodeDefinition>(manifest.Entries.Select(x => x.ToNodeDefinition()).ToList()));
		}

		public static Result<NodeDefinitionCatalog, Diagnostic> GenerateCatalog(ShaderNodeManifest manifest, NodeFactoryBindings bindings = null) {
			var valid = ShaderNodeManifestValidator.Validate(manifest);
			if (valid.IsFailure) return Result.Failure<NodeDefinitionCatalog, Diagnostic>(valid.Error);
			var catalog = NodeDefinitionCatalog.CreateInitial(manifest, bindings);
			var catalogValid = catalog.Validate();
			return catalogValid.IsFailure ? Result.Failure<NodeDefinitionCatalog, Diagnostic>(catalogValid.Error) : Result.Success<NodeDefinitionCatalog, Diagnostic>(catalog);
		}
	}
}
