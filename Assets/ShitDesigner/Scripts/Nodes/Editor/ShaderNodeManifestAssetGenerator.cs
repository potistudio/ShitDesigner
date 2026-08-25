using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Nodes.Editor {
	/// <summary>
	/// Imports the three machine-readable VJ ledgers into the runtime
	/// manifest and catalog assets.  The ledgers are authoritative: this
	/// importer never edits them and fails when an entry cannot be represented
	/// by the neutral node schema or its family Shader.
	/// </summary>
	public static class ShaderNodeManifestAssetGenerator {
		public const string ManifestAssetPath = "Assets/ShitDesigner/Scripts/Nodes/ShaderNodeManifest.asset";
		public const string CatalogAssetPath = "Assets/ShitDesigner/Scripts/Nodes/NodeTypeCatalog.asset";
		private const string SpatialLedgerPath = "Assets/ShitDesigner/Shaders/Manifests/spatial-variants.json";
		private const string CompositingLedgerPath = "Assets/ShitDesigner/Shaders/Manifests/compositing-temporal-variants.json";
		private const string AudioLedgerPath = "Assets/ShitDesigner/Shaders/Manifests/audio-raymarch-utility-variants.json";

		private static readonly IReadOnlyDictionary<string, string> FamilyShaderPaths =
			new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
			{
				{ "VJGenerator", "Assets/ShitDesigner/Shaders/Families/VJGenerator.shader" },
				{ "VJColor", "Assets/ShitDesigner/Shaders/Families/VJColor.shader" },
				{ "VJGeometry", "Assets/ShitDesigner/Shaders/Families/VJGeometry.shader" },
				{ "VJEdge", "Assets/ShitDesigner/Shaders/Families/VJEdge.shader" },
				{ "VJGlitch", "Assets/ShitDesigner/Shaders/Families/VJGlitch.shader" },
				{ "VJConvolution", "Assets/ShitDesigner/Shaders/Families/VJConvolution.shader" },
				{ "VJKey", "Assets/ShitDesigner/Shaders/Families/VJKey.shader" },
				{ "Blend", "Assets/ShitDesigner/Shaders/Families/VJBlendFamily.shader" },
				{ "Transition", "Assets/ShitDesigner/Shaders/Families/VJTransitionFamily.shader" },
				{ "Temporal", "Assets/ShitDesigner/Shaders/Families/VJTemporalFamily.shader" },
				{ "Audio", "Assets/ShitDesigner/Shaders/Families/VJAudioFamily.shader" },
				{ "Raymarch", "Assets/ShitDesigner/Shaders/Families/VJRaymarchFamily.shader" },
				{ "Utility", "Assets/ShitDesigner/Shaders/Families/VJUtilityFamily.shader" }
			};

		private static readonly IReadOnlyDictionary<string, string> LegacyShaderPaths =
			new Dictionary<string, string>(StringComparer.Ordinal)
			{
				{ "builtin.shader.generator", "Assets/ShitDesigner/Scripts/Media/Shaders/BuiltinGenerator.shader" },
				{ "builtin.shader.effect", "Assets/ShitDesigner/Scripts/Media/Shaders/BuiltinEffect.shader" },
				{ "builtin.shader.blend2", "Assets/ShitDesigner/Scripts/Media/Shaders/BuiltinBlend2.shader" }
			};

		[MenuItem("ShitDesigner/VJ Shader Pack/Generate Manifest and Node Catalog")]
		public static void GenerateMenu() {
			var result = GenerateAndValidate();
			if (result.IsFailure) {
				var diagnostic = result.Error;
				Debug.LogError("Manifest generation failed [" + diagnostic.Code.Value + "]: " + diagnostic.Message
					+ (diagnostic.Exception == null ? string.Empty : "\n" + diagnostic.Exception.StackTrace));
				throw new InvalidOperationException(diagnostic.Message);
			}
			Debug.Log("ShitDesigner VJ shader manifest and node catalog generated: " + result.Value + " shader entries.");
		}

		public static Result<int, Diagnostic> GenerateAndValidate() {
			var loaded = LoadManifest();
			if (loaded.IsFailure) return Result.Failure<int, Diagnostic>(loaded.Error);
			var manifest = loaded.Value.Manifest;
			var shaderByType = loaded.Value.Shaders;
			var valid = ShaderNodeManifestValidator.Validate(manifest);
			if (valid.IsFailure) return Result.Failure<int, Diagnostic>(valid.Error);

			var manifestAsset = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(ManifestAssetPath);
			if (manifestAsset == null) {
				manifestAsset = ScriptableObject.CreateInstance<ShaderNodeManifestAsset>();
				manifestAsset.name = "ShaderNodeManifest";
				AssetDatabase.CreateAsset(manifestAsset, ManifestAssetPath);
			}
			manifestAsset.ReplaceManifest(manifest, loaded.Value.Fingerprint);
			foreach (var pair in shaderByType) {
				var attached = manifestAsset.SetShaderReference(pair.Key, pair.Value);
				if (attached.IsFailure) return Result.Failure<int, Diagnostic>(attached.Error);
			}
			var assetValid = manifestAsset.ValidateManifest();
			if (assetValid.IsFailure) return Result.Failure<int, Diagnostic>(assetValid.Error);
			var shadersValid = manifestAsset.ValidateShaderReferences();
			if (shadersValid.IsFailure) return Result.Failure<int, Diagnostic>(shadersValid.Error);

			var runtime = NodeDefinitionCatalog.CreateInitial(manifest);
			var runtimeValid = runtime.Validate();
			if (runtimeValid.IsFailure) return Result.Failure<int, Diagnostic>(runtimeValid.Error);
			var catalog = AssetDatabase.LoadAssetAtPath<NodeTypeCatalog>(CatalogAssetPath);
			if (catalog == null) {
				catalog = ScriptableObject.CreateInstance<NodeTypeCatalog>();
				catalog.name = "NodeTypeCatalog";
				AssetDatabase.CreateAsset(catalog, CatalogAssetPath);
			}
			catalog.SetShaderManifest(manifestAsset);
			catalog.ReplaceManifest(runtime.Entries);
			foreach (var pair in shaderByType) {
				var configured = catalog.ConfigureShaderReference(pair.Key, pair.Value);
				if (configured.IsFailure) return Result.Failure<int, Diagnostic>(configured.Error);
			}
			var sceneReferences = ConfigureLegacySceneReferences(catalog);
			if (sceneReferences.IsFailure) return Result.Failure<int, Diagnostic>(sceneReferences.Error);
			var catalogValid = catalog.ValidateManifest();
			if (catalogValid.IsFailure) return Result.Failure<int, Diagnostic>(catalogValid.Error);
			var exact = catalog.ValidateAgainst(runtime);
			if (exact.IsFailure) return Result.Failure<int, Diagnostic>(exact.Error);
			var count = manifest.Entries.Count;
			if (count != 441) return Result.Failure<int, Diagnostic>(Failure("nodes.shader_manifest_count", "Expected 438 VJ entries plus 3 legacy shader entries, found " + count + ".").Error);
			EditorUtility.SetDirty(manifestAsset);
			EditorUtility.SetDirty(catalog);
			AssetDatabase.SaveAssets();
			AssetDatabase.Refresh();
			return Result.Success<int, Diagnostic>(count);
		}

		public static Result<ShaderNodeManifest, Diagnostic> LoadAuthoritativeManifest() {
			var result = LoadManifest();
			return result.IsFailure ? Result.Failure<ShaderNodeManifest, Diagnostic>(result.Error) : Result.Success<ShaderNodeManifest, Diagnostic>(result.Value.Manifest);
		}

		private static UnitResult<Diagnostic> ConfigureLegacySceneReferences(NodeTypeCatalog catalog) {
			var scene3d = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ShitDesigner/Scenes/Cylinder Flythrough.prefab");
			var scene2d = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ShitDesigner/Scripts/Bootstrap/Scene2D.prefab");
			if (scene3d == null || scene2d == null) return Failure("nodes.catalog.prefab_missing", "Required Scene prefabs are missing from the project.");
			var configured3d = catalog.ConfigureReference("shitdesigner.scene.3d", prefab: scene3d);
			if (configured3d.IsFailure) return configured3d;
			return catalog.ConfigureReference("shitdesigner.scene.2d", prefab: scene2d);
		}

		private static Result<LoadedManifest, Diagnostic> LoadManifest() {
			try {
				var entries = new List<ShaderNodeManifestEntry>();
				var shaders = new Dictionary<string, Shader>(StringComparer.Ordinal);
				var spatial = Read<SpatialLedger>(SpatialLedgerPath);
				var compositing = Read<CompositingLedger>(CompositingLedgerPath);
				var audio = Read<AudioLedger>(AudioLedgerPath);
				if (spatial == null || spatial.variants == null || spatial.variants.Length != 246)
					return Result.Failure<LoadedManifest, Diagnostic>(Failure("nodes.shader_ledger_spatial", "Spatial ledger must contain exactly 246 variants.").Error);
				if (compositing == null || compositing.variants == null || compositing.variants.Length != 104)
					return Result.Failure<LoadedManifest, Diagnostic>(Failure("nodes.shader_ledger_compositing", "Compositing/temporal ledger must contain exactly 104 variants.").Error);
				if (audio == null || audio.variants == null || audio.variants.Length != 88)
					return Result.Failure<LoadedManifest, Diagnostic>(Failure("nodes.shader_ledger_audio", "Audio/raymarch/utility ledger must contain exactly 88 variants.").Error);

				entries.AddRange(ShaderNodeManifest.CreateBuiltIn().Entries);
				foreach (var row in spatial.variants) {
					var entry = FromSpatial(row);
					entries.Add(entry);
					shaders[row.nodeTypeId] = LoadFamilyShader(row.family, row.shader);
				}
				foreach (var row in compositing.variants) {
					var entry = FromCompositing(row);
					entries.Add(entry);
					shaders["shitdesigner.shader." + row.id] = LoadFamilyShader(row.family, row.shader);
				}
				foreach (var row in audio.variants) {
					var entry = FromAnalysis(row);
					entries.Add(entry);
					shaders["shitdesigner.shader." + row.id] = LoadFamilyShader(row.family, row.shader);
				}
				foreach (var legacy in ShaderNodeManifest.CreateBuiltIn().Entries)
					shaders[legacy.TypeId.Value] = LoadLegacyShader(legacy.ShaderKey);
				var duplicate = entries.GroupBy(x => x.TypeId.Value, StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
				if (duplicate != null) return Result.Failure<LoadedManifest, Diagnostic>(Failure("nodes.shader_ledger_duplicate", "Duplicate shader manifest TypeId: " + duplicate.Key + ".").Error);
				foreach (var pair in shaders)
					if (pair.Value == null) return Result.Failure<LoadedManifest, Diagnostic>(Failure("nodes.shader_family_missing", "Family Shader is missing for " + pair.Key + ".").Error);
				return Result.Success<LoadedManifest, Diagnostic>(new LoadedManifest(new ShaderNodeManifest(entries), shaders, Fingerprint()));
			}
			catch (Exception exception) {
				// Preserve the offending constructor/ledger row in the editor
				// diagnostic.  A bare message such as "Stable IDs cannot be
				// empty" is otherwise impossible to action when one of the
				// three authoritative ledgers changes.
				return Result.Failure<LoadedManifest, Diagnostic>(Failure("nodes.shader_ledger_import", exception.ToString(), exception).Error);
			}
		}

		private static ShaderNodeManifestEntry FromSpatial(SpatialVariant row) {
			var family = ParseFamily(row.family);
			// The ledger remains authoritative for the variant, property and
			// input tokens.  Stateful simulation nodes are an explicit
			// runtime capability of those ledger entries: the source ledger
			// predates the pool-backed history contract and therefore does
			// not repeat the derived slot policy on every row.
			var stateful = IsStatefulSpatial(row);
			var historySlots = stateful ? HistorySlotsForSpatial(row.nodeTypeId) : 0;
			var warmupFrames = stateful ? 1 : 0;
			var inputs = BuildSpatialInputs(row, stateful);
			var parameters = row.parameters.Select(x => BuildParameter(x, spatial: true)).ToArray();
			var passes = BuildPasses(row.nodeTypeId, row.variantId, family, row.variant, 0, stateful);
			var outputPass = passes.Max(x => x.Index);
			return new ShaderNodeManifestEntry(new NodeTypeId(row.nodeTypeId), row.name, row.category, family, row.shader,
				row.variantId, inputs, parameters, passes, outputPass, Features(family, stateful), stateful, historySlots,
				new[] { Slug(row.name), row.variantId }, row.testStrategy, 1, true, row.priority, 0,
				Path.GetFileName(SpatialLedgerPath), row.variant);
		}

		private static bool IsStatefulSpatial(SpatialVariant row) {
			var id = (row?.nodeTypeId ?? string.Empty).ToLowerInvariant();
			// These are simulation/velocity effects whose authored family
			// shader consumes a history ring.  Keep the policy explicit so a
			// ledger row cannot accidentally become stateful because a name
			// contains a generic word such as "motion".
			return id.Contains("gray-scott") || id.Contains("game-of-life") ||
				id.Contains("elementary-cellular-automata") || id.Contains("reaction-diffusion") ||
				id.Contains("geometry.optical-flow") || id.Contains("geometry.datamosh") ||
				id.Contains("geometry.fluid-advection") || id.Contains("glitch.databend-simulation");
		}

		private static int HistorySlotsForSpatial(string typeId) {
			var id = (typeId ?? string.Empty).ToLowerInvariant();
			return id.Contains("gray-scott") || id.Contains("game-of-life") || id.Contains("cellular") ? 2 : 3;
		}

		private static IReadOnlyList<ShaderNodeManifestPass> BuildPasses(string typeId, string variantId,
			ShaderNodeFamily family, int sourceVariant, int firstPass, bool stateful) {
			var graph = ResolveGraph(typeId, family, sourceVariant, stateful);
			var count = graph.PassCount;
			var features = Features(family, stateful);
			var result = new List<ShaderNodeManifestPass>(count);
			for (var index = 0; index < count; index++) {
				var passIndex = firstPass + index;
				var passId = count == 1 ? "main" : graph.Id + "_pass" + (index + 1);
				result.Add(new ShaderNodeManifestPass(passId, passIndex, variantId, ShaderPassKind.Draw, "image", features));
			}
			return result;
		}

		private readonly struct GraphPlan {
			public string Id { get; }
			public int PassCount { get; }
			public GraphPlan(string id, int passCount) { Id = id; PassCount = passCount; }
		}

		private static GraphPlan ResolveGraph(string typeId, ShaderNodeFamily family, int sourceVariant, bool stateful) {
			var id = (typeId ?? string.Empty).ToLowerInvariant();
			if (family == ShaderNodeFamily.Generator) {
				if (sourceVariant == 44 || sourceVariant >= 45 && sourceVariant <= 47) return new GraphPlan("G_SIM2", 2);
			}
			if (family == ShaderNodeFamily.Geometry) {
				if (sourceVariant == 36) return new GraphPlan("G_BEZIER2", 2);
				if (sourceVariant == 37) return new GraphPlan("G_TPS2", 2);
				if (sourceVariant == 38) return new GraphPlan("G_BEZIER2", 2);
				if (sourceVariant == 39) return new GraphPlan("G_FLOW2", 2);
				if (sourceVariant == 40) return new GraphPlan("G_DATAMOSH2", 2);
				if (sourceVariant == 41) return new GraphPlan("G_FLUID2", 2);
			}
			if (family == ShaderNodeFamily.Convolution) {
				if (sourceVariant >= 0 && sourceVariant <= 5) return new GraphPlan("G_SEP2", 2);
				if (sourceVariant == 8) return new GraphPlan("G_BLOOM4", 4);
				if (sourceVariant == 9 || sourceVariant == 10) return new GraphPlan("G_LIGHT3", 3);
				if (sourceVariant == 11) return new GraphPlan("G_STREAK3", 3);
				if (sourceVariant == 12) return new GraphPlan("G_KAWASE4", 4);
				if (sourceVariant == 13) return new GraphPlan("G_DUAL3", 3);
				if (sourceVariant == 14) return new GraphPlan("G_BOKEH2", 2);
				if (sourceVariant == 15) return new GraphPlan("G_TILT3", 3);
				if (sourceVariant == 16) return new GraphPlan("G_IRIS2", 2);
				if (sourceVariant == 18 || sourceVariant == 19) return new GraphPlan("G_SEP2", 2);
				if (sourceVariant == 20) return new GraphPlan("G_FLARE3", 3);
				if (sourceVariant == 21) return new GraphPlan("G_ANAMORPHIC2", 2);
				if (sourceVariant == 22) return new GraphPlan("G_STARBURST2", 2);
				if (sourceVariant == 23) return new GraphPlan("G_GHOST3", 3);
				if (sourceVariant == 24) return new GraphPlan("G_DOF4", 4);
				if (sourceVariant == 25) return new GraphPlan("G_TMB2", 2);
				if (sourceVariant == 26) return new GraphPlan("G_FFT3", 3);
				if (sourceVariant == 27) return new GraphPlan("G_CUSTOM2", 2);
			}
			if (family == ShaderNodeFamily.Stylize) {
				if (sourceVariant == 16) return new GraphPlan("G_DOG3", 3);
				if (sourceVariant == 17) return new GraphPlan("G_CANNY3", 3);
			}
			if (family == ShaderNodeFamily.Key && sourceVariant == 9) return new GraphPlan("G_MATTE2", 2);
			if (family == ShaderNodeFamily.Temporal) {
				if (sourceVariant <= 10) return new GraphPlan("G_TEMPORAL2", 2);
				if (sourceVariant <= 25) return new GraphPlan("G_TEMPORAL3", 3);
				if (sourceVariant <= 27) return new GraphPlan("G_TFLOW2", 2);
				if (sourceVariant == 28) return new GraphPlan("G_INTERP3", 3);
				if (sourceVariant == 29) return new GraphPlan("G_TFLUID2", 2);
				if (sourceVariant == 30) return new GraphPlan("G_TRD2", 2);
				if (sourceVariant == 31) return new GraphPlan("G_TCELL2", 2);
			}
			if (family == ShaderNodeFamily.Audio && sourceVariant == 27) return new GraphPlan("G_AUDIO_FLUID2", 2);
			if (family == ShaderNodeFamily.Audio && sourceVariant == 29) return new GraphPlan("G_AUDIO_HISTORY2", 2);
			if (family == ShaderNodeFamily.Utility && sourceVariant >= 13 && sourceVariant <= 15) return new GraphPlan("G_REDUCE2", 2);
			return new GraphPlan("G_SINGLE", 1);
		}

		private static ShaderNodeManifestEntry FromCompositing(CompositingVariant row) {
			var family = ParseFamily(row.family);
			var inputs = BuildCompositingInputs(row.inputs, family);
			var parameters = row.parameters.Select(x => BuildParameter(x, spatial: false)).ToArray();
			var passes = BuildPasses("shitdesigner.shader." + row.id, row.id, family, row.variant, row.pass, row.stateful);
			var outputPass = passes.Max(x => x.Index);
			var category = "Composite/" + row.family;
			return new ShaderNodeManifestEntry(new NodeTypeId("shitdesigner.shader." + row.id), row.displayName, category, family, row.shader,
				row.id, inputs, parameters, passes, outputPass, Features(family, row.stateful), row.stateful, row.historySlots,
				new[] { Slug(row.displayName), row.id }, row.testStrategy, 1, true, Priority(row.family, row.variant), row.warmupFrames,
				Path.GetFileName(CompositingLedgerPath), row.variant);
		}

		private static ShaderNodeManifestEntry FromAnalysis(AnalysisVariant row) {
			var family = ParseFamily(row.family);
			var stateful = row.stateful || IsStatefulAnalysis(row);
			var historySlots = stateful ? (row.variant == 29 ? 1 : 2) : row.historySlots;
			var inputs = BuildAnalysisInputs(row.inputs, family, stateful);
			var parameters = row.parameters.Select(x => BuildParameter(x, spatial: false)).ToArray();
			var passes = BuildPasses("shitdesigner.shader." + row.id, row.id, family, row.variant, row.pass, stateful);
			var outputPass = passes.Max(x => x.Index);
			var category = "Shader/" + row.family;
			var priority = string.Equals(row.formalPriority, "unclassified", StringComparison.OrdinalIgnoreCase)
				? "UNCLASSIFIED" : (row.formalPriority ?? string.Empty).ToUpperInvariant();
			if (string.IsNullOrWhiteSpace(priority)) priority = row.phase1Support ? "SUPPORT" : "P2";
			return new ShaderNodeManifestEntry(new NodeTypeId("shitdesigner.shader." + row.id), row.displayName, category, family, row.shader,
				row.id, inputs, parameters, passes, outputPass, Features(family, stateful), stateful, historySlots,
				new[] { Slug(row.displayName), row.id }, row.testStrategy, 1, true, priority, row.warmupFrames,
				Path.GetFileName(AudioLedgerPath), row.variant);
		}

		private static bool IsStatefulAnalysis(AnalysisVariant row) {
			return string.Equals(row?.family, "Audio", StringComparison.OrdinalIgnoreCase) && (row.variant == 27 || row.variant == 29);
		}

		private static IEnumerable<ShaderNodeManifestInput> BuildSpatialInputs(SpatialVariant row, bool stateful) {
			var result = new List<ShaderNodeManifestInput>();
			if (row.inputs != null && row.inputs.Length > 0) {
				// Spatial ledgers currently expose one primary input token.
				// Preserve that token while assigning stable graph ports for
				// any future multi-input ledger row.
				for (var index = 0; index < row.inputs.Length; index++) {
					var raw = row.inputs[index];
					var port = index == 0 ? "input" : NormalizePort(raw) + "_input";
					result.Add(new ShaderNodeManifestInput(new PortId(port), raw, index == 0 ? "_MainTex" : "_" + raw,
						index == 0 ? ShaderInputRole.Primary : ShaderInputRole.Custom,
						NodePortType.ImageFrame, true, null, raw));
				}
			}
			if (stateful) {
				result.Add(new ShaderNodeManifestInput(new PortId("history_tex"), "HistoryTex", "_HistoryTex",
					ShaderInputRole.History, NodePortType.ImageFrame, false, RuntimeDefaultImageKind.OpaqueBlack, "HistoryTex"));
				result.Add(new ShaderNodeManifestInput(new PortId("history_tex2"), "HistoryTex2", "_HistoryTex2",
					ShaderInputRole.History, NodePortType.ImageFrame, false, RuntimeDefaultImageKind.OpaqueBlack, "HistoryTex2"));
				result.Add(new ShaderNodeManifestInput(new PortId("history_tex3"), "HistoryTex3", "_HistoryTex3",
					ShaderInputRole.History, NodePortType.ImageFrame, false, RuntimeDefaultImageKind.OpaqueBlack, "HistoryTex3"));
			}
			return result;
		}

		private static IEnumerable<ShaderNodeManifestInput> BuildCompositingInputs(string[] rawInputs, ShaderNodeFamily family) {
			var list = new List<ShaderNodeManifestInput>();
			foreach (var raw in rawInputs ?? Array.Empty<string>()) {
				var normalized = NormalizePort(raw);
				var role = raw.Equals("TexA", StringComparison.OrdinalIgnoreCase) || raw.Equals("MainTex", StringComparison.OrdinalIgnoreCase)
					? ShaderInputRole.Primary
					: raw.Equals("TexB", StringComparison.OrdinalIgnoreCase) ? ShaderInputRole.Secondary
					: raw.IndexOf("History", StringComparison.OrdinalIgnoreCase) >= 0 ? ShaderInputRole.History
					: raw.IndexOf("Displacement", StringComparison.OrdinalIgnoreCase) >= 0 ? ShaderInputRole.Displacement
					: ShaderInputRole.Custom;
				var required = role == ShaderInputRole.Primary && !raw.Equals("MainTex", StringComparison.OrdinalIgnoreCase)
					? true : role == ShaderInputRole.Primary;
				if (role == ShaderInputRole.History || role == ShaderInputRole.Displacement) required = false;
				list.Add(new ShaderNodeManifestInput(new PortId(normalized), raw, "_" + raw, role,
					NodePortType.ImageFrame, required, required ? (RuntimeDefaultImageKind?)null : RuntimeDefaultImageKind.OpaqueBlack, raw));
			}
			return list;
		}

		private static IEnumerable<ShaderNodeManifestInput> BuildAnalysisInputs(string[] rawInputs, ShaderNodeFamily family, bool stateful) {
			var list = new List<ShaderNodeManifestInput>();
			foreach (var raw in rawInputs ?? Array.Empty<string>()) {
				var role = raw.Equals("MainTex", StringComparison.OrdinalIgnoreCase) ? ShaderInputRole.Primary : ShaderInputRole.Audio;
				var required = role == ShaderInputRole.Primary && family == ShaderNodeFamily.Utility;
				list.Add(new ShaderNodeManifestInput(new PortId(NormalizePort(raw)), raw, "_" + raw, role,
					NodePortType.ImageFrame, required, required ? (RuntimeDefaultImageKind?)null : RuntimeDefaultImageKind.OpaqueBlack, raw));
			}
			if (stateful) {
				list.Add(new ShaderNodeManifestInput(new PortId("history_tex"), "HistoryTex", "_HistoryTex",
					ShaderInputRole.History, NodePortType.ImageFrame, false, RuntimeDefaultImageKind.OpaqueBlack, "HistoryTex"));
				list.Add(new ShaderNodeManifestInput(new PortId("history_tex2"), "HistoryTex2", "_HistoryTex2",
					ShaderInputRole.History, NodePortType.ImageFrame, false, RuntimeDefaultImageKind.OpaqueBlack, "HistoryTex2"));
			}
			return list;
		}

		private static ShaderNodeManifestParameter BuildParameter(string raw, bool spatial) {
			var id = NormalizeParameter(raw);
			var property = spatial ? "_VJ" + Pascal(raw) : "_" + raw;
			var type = ParameterType.Float;
			var options = (IEnumerable<string>)null;
			var mapping = (IDictionary<string, int>)null;
			ParameterValue value = ParameterValue.FromFloat(DefaultFloat(id));
			ParameterValue? minimum = null;
			ParameterValue? maximum = null;
			if (id == "color_a") { type = ParameterType.Vector4; value = ParameterValue.FromVector4(new Vector4Value(1, 0, 0, 1)); }
			else if (id == "color_b") { type = ParameterType.Vector4; value = ParameterValue.FromVector4(new Vector4Value(0, 0, 1, 1)); }
			else if (id == "color_c") { type = ParameterType.Vector4; value = ParameterValue.FromVector4(new Vector4Value(0, 1, 0, 1)); }
			else if (id == "center" || id == "pivot" || id == "displacement" || id == "camera_position" || id == "camera_target" || id == "light_direction" || id == "resolution") {
				type = ParameterType.Vector4; value = ParameterValue.FromVector4(new Vector4Value(.5f, .5f, 0, 0));
			}
			else if (id == "steps") {
				type = ParameterType.Int; value = ParameterValue.FromInt(96); minimum = ParameterValue.FromInt(1); maximum = ParameterValue.FromInt(256);
			}
			else if (id == "channel") {
				type = ParameterType.Enum; options = new[] { "red", "green", "blue", "alpha" }; mapping = new Dictionary<string, int> { { "red", 0 }, { "green", 1 }, { "blue", 2 }, { "alpha", 3 } }; value = ParameterValue.FromEnum("red");
			}
			else if (id == "range_mode") {
				type = ParameterType.Enum; options = new[] { "absolute", "normalized", "log" }; mapping = new Dictionary<string, int> { { "absolute", 0 }, { "normalized", 1 }, { "log", 2 } }; value = ParameterValue.FromEnum("absolute");
			}
			else if (id == "paused" || id == "reset" || id == "reverse") {
				type = ParameterType.Bool; value = ParameterValue.FromBool(false);
			}
			else if (id == "amount" || id == "external_mask" || id == "progress" || id == "softness" || id == "threshold" || id == "mix" || id == "rms" || id == "peak" || id == "beat" || id == "bpm_phase" || id == "audio_rms") {
				minimum = ParameterValue.FromFloat(0f); maximum = ParameterValue.FromFloat(1f);
			}
			return new ShaderNodeManifestParameter(new ParameterId(id), Humanize(raw), type, value, property, minimum, maximum,
				false, options, mapping, "Shader", 0, string.Empty, string.Empty, 0d, false, false, raw);
		}

		private static ShaderNodeFamily ParseFamily(string family) {
			switch (family ?? string.Empty) {
				case "VJGenerator": case "Generator": return ShaderNodeFamily.Generator;
				case "VJColor": case "Color": return ShaderNodeFamily.Color;
				case "VJGeometry": case "Geometry": return ShaderNodeFamily.Geometry;
				case "VJGlitch": case "Glitch": return ShaderNodeFamily.Glitch;
				case "VJConvolution": case "Convolution": return ShaderNodeFamily.Convolution;
				case "VJEdge": case "Edge": return ShaderNodeFamily.Stylize;
				case "VJKey": case "Key": return ShaderNodeFamily.Key;
				case "Blend": return ShaderNodeFamily.Composite;
				case "Transition": return ShaderNodeFamily.Transition;
				case "Temporal": return ShaderNodeFamily.Temporal;
				case "Audio": return ShaderNodeFamily.Audio;
				case "Raymarch": return ShaderNodeFamily.Raymarch;
				case "Utility": return ShaderNodeFamily.Utility;
				default: throw new InvalidOperationException("Unknown shader family: " + family);
			}
		}

		private static ShaderFeatureFlags Features(ShaderNodeFamily family, bool stateful) {
			var value = ShaderFeatureFlags.None;
			if (stateful) value |= ShaderFeatureFlags.History;
			if (family == ShaderNodeFamily.Audio) value |= ShaderFeatureFlags.AudioTexture;
			if (family == ShaderNodeFamily.Raymarch) value |= ShaderFeatureFlags.ShaderModel45 | ShaderFeatureFlags.Derivatives;
			if (family == ShaderNodeFamily.Geometry || family == ShaderNodeFamily.Glitch || family == ShaderNodeFamily.Convolution || family == ShaderNodeFamily.Stylize || family == ShaderNodeFamily.Key)
				value |= ShaderFeatureFlags.Derivatives;
			return value;
		}

		private static string Priority(string family, int variant) {
			if (string.Equals(family, "Blend", StringComparison.OrdinalIgnoreCase)) return "P0";
			if (string.Equals(family, "Transition", StringComparison.OrdinalIgnoreCase)) return variant < 12 ? "P0" : "P1";
			if (string.Equals(family, "Temporal", StringComparison.OrdinalIgnoreCase)) return variant < 12 ? "P0" : variant < 26 ? "P1" : "P2";
			return "P1";
		}

		private static float DefaultFloat(string id) {
			if (id == "frequency") return 4f;
			if (id == "detail") return 4f;
			if (id == "speed" || id == "gain") return 1f;
			if (id == "scale" || id == "radius" || id == "falloff" || id == "feedback") return 1f;
			if (id == "softness") return .05f;
			if (id == "threshold") return .5f;
			if (id == "fov") return 55f;
			if (id == "far_distance") return 30f;
			if (id == "epsilon") return .001f;
			if (id == "fog") return .15f;
			if (id == "ambient_occlusion") return .35f;
			return 0f;
		}

		private static string NormalizePort(string raw) {
			if (string.Equals(raw, "TexA", StringComparison.OrdinalIgnoreCase)) return "a";
			if (string.Equals(raw, "TexB", StringComparison.OrdinalIgnoreCase)) return "b";
			if (string.Equals(raw, "MainTex", StringComparison.OrdinalIgnoreCase) || string.Equals(raw, "image", StringComparison.OrdinalIgnoreCase)) return "input";
			var value = NormalizeToken(raw);
			return value == "input" || value == "image" ? value + "_input" : value;
		}

		private static string NormalizeParameter(string raw) => NormalizeToken(raw);

		private static string NormalizeToken(string raw) {
			if (string.IsNullOrWhiteSpace(raw)) throw new ArgumentException("Ledger token is empty.");
			var builder = new StringBuilder();
			var previousSeparator = false;
			foreach (var character in raw.Trim()) {
				if (char.IsLetterOrDigit(character)) {
					if (char.IsUpper(character) && builder.Length > 0 && !previousSeparator) builder.Append('_');
					builder.Append(char.ToLowerInvariant(character));
					previousSeparator = false;
				}
				else if (!previousSeparator) {
					builder.Append('_');
					previousSeparator = true;
				}
			}
			var result = builder.ToString().Trim('_');
			if (result.Length == 0) throw new ArgumentException("Ledger token is empty.");
			return result;
		}

		private static string Pascal(string raw) {
			var normalized = NormalizeToken(raw);
			return string.Concat(normalized.Split('_').Where(x => x.Length > 0).Select(x => char.ToUpperInvariant(x[0]) + x.Substring(1)));
		}

		private static string Humanize(string raw)
			=> string.Join(" ", NormalizeToken(raw).Split('_').Where(x => x.Length > 0).Select(x => char.ToUpperInvariant(x[0]) + x.Substring(1)));

		private static string Slug(string raw) => NormalizeToken(raw);

		private static T Read<T>(string assetPath) where T : class {
			var absolute = Path.Combine(Directory.GetParent(Application.dataPath).FullName, assetPath.Replace('/', Path.DirectorySeparatorChar));
			return JsonUtility.FromJson<T>(File.ReadAllText(absolute));
		}

		private static Shader LoadFamilyShader(string family, string shaderKey) {
			if (!FamilyShaderPaths.TryGetValue(family ?? string.Empty, out var path))
				throw new InvalidOperationException("No family shader path is registered for " + family + ".");
			var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
			if (shader == null || !string.Equals(shader.name, shaderKey, StringComparison.Ordinal))
				throw new InvalidOperationException("Family shader asset does not match ledger shader key: " + shaderKey + ".");
			return shader;
		}

		private static Shader LoadLegacyShader(string key)
			=> LegacyShaderPaths.TryGetValue(key, out var path) ? AssetDatabase.LoadAssetAtPath<Shader>(path) : null;

		private static string Fingerprint() {
			using (var sha = SHA256.Create()) {
				var bytes = Encoding.UTF8.GetBytes(string.Join("\n", new[] { SpatialLedgerPath, CompositingLedgerPath, AudioLedgerPath }.Select(path => File.ReadAllText(Path.Combine(Directory.GetParent(Application.dataPath).FullName, path.Replace('/', Path.DirectorySeparatorChar))))));
				return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", string.Empty).ToLowerInvariant();
			}
		}

		[Serializable] private sealed class SpatialLedger { public SpatialVariant[] variants; }
		[Serializable] private sealed class CompositingLedger { public CompositingVariant[] variants; }
		[Serializable] private sealed class AudioLedger { public AnalysisVariant[] variants; }
		[Serializable]
		private sealed class SpatialVariant {
			public string nodeTypeId; public string variantId; public string name; public string category; public string family; public string shader; public int variant; public string role; public string[] inputs; public string[] outputs; public string[] parameters; public bool stateful; public string priority; public string testStrategy;
		}
		[Serializable]
		private sealed class CompositingVariant {
			public string id; public string displayName; public string family; public string shader; public int variant; public int pass; public string[] inputs; public string[] parameters; public bool stateful; public int historySlots; public int warmupFrames; public string testStrategy;
		}
		[Serializable]
		private sealed class AnalysisVariant {
			public string id; public string displayName; public string family; public string shader; public int variant; public int pass; public string formalPriority; public bool phase1Support; public string[] inputs; public string[] parameters; public bool stateful; public int historySlots; public int warmupFrames; public string testStrategy;
		}
		private sealed class LoadedManifest {
			public ShaderNodeManifest Manifest { get; }
			public IReadOnlyDictionary<string, Shader> Shaders { get; }
			public string Fingerprint { get; }
			public LoadedManifest(ShaderNodeManifest manifest, IReadOnlyDictionary<string, Shader> shaders, string fingerprint) { Manifest = manifest; Shaders = shaders; Fingerprint = fingerprint; }
		}

		private static UnitResult<Diagnostic> Failure(string code, string message, Exception exception = null)
			=> UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "nodes", exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception)));
	}
}
