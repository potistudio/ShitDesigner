using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor.VJ {
	/// <summary>
	/// Reads the three VJ variant ledgers and emits the human-facing reference,
	/// compatibility alias, representative preset data, and GPU reference-image
	/// tools. Generated text is deterministic and contains no timestamps.
	/// </summary>
	public static class VJShaderPackDocumentationGenerator {
		private const string SpatialLedgerPath = "Assets/ShitDesigner/Shaders/Manifests/spatial-variants.json";
		private const string CompositingLedgerPath = "Assets/ShitDesigner/Shaders/Manifests/compositing-temporal-variants.json";
		private const string AudioLedgerPath = "Assets/ShitDesigner/Shaders/Manifests/audio-raymarch-utility-variants.json";
		private const string MainReferencePath = "docs/vj-shader-reference.md";
		private const string CompatibilityReferencePath = "docs/shader-shader-reference.md";
		private const string PresetPath = "Assets/ShitDesigner/Presets/vj-presets.json";
		private const string ReferenceDirectory = "Assets/ShitDesigner/Shaders/References/P0";

		[MenuItem("ShitDesigner/VJ Shader Pack/Generate Documentation and Presets")]
		public static void GenerateDocumentationAndPresets() {
			var result = GenerateDocumentationAndPresetsInternal();
			foreach (var warning in result.Warnings)
				Debug.LogWarning("[VJ] " + warning);
			foreach (var error in result.Errors)
				Debug.LogError("[VJ] " + error);
			if (result.Errors.Count > 0)
				throw new InvalidOperationException(string.Join(Environment.NewLine, result.Errors));
			Debug.Log("[VJ] Generated " + result.VariantCount + " variant rows and " + result.PresetCount + " representative presets.");
			AssetDatabase.Refresh();
		}

		[MenuItem("ShitDesigner/VJ Shader Pack/Generate P0 Reference Images")]
		public static void GenerateP0ReferenceImages() {
			var records = LoadAllRecords(out var warnings);
			var p0 = records.Where(x => ResolvePriority(x) == "P0").ToArray();
			if (p0.Length == 0)
				throw new InvalidOperationException("No P0 variants were found in the available ledgers.");
			if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
				throw new InvalidOperationException("A GPU graphics device is required to render P0 reference images.");

			Directory.CreateDirectory(ToAbsoluteProjectPath(ReferenceDirectory));
			foreach (var record in p0)
				RenderReference(record);
			AssetDatabase.Refresh();
			Debug.Log("[VJ] Generated " + p0.Length + " P0 reference images under " + ReferenceDirectory + ".");
			foreach (var warning in warnings)
				Debug.LogWarning("[VJ] " + warning);
		}

		[MenuItem("ShitDesigner/VJ Shader Pack/Generate P0 Contact Sheet")]
		public static void GenerateP0ContactSheet() {
			var records = LoadAllRecords(out var warnings);
			var p0 = records.Where(x => ResolvePriority(x) == "P0").ToArray();
			if (p0.Length == 0)
				throw new InvalidOperationException("No P0 variants were found in the available ledgers.");

			var files = p0
				.Select(x => ToAbsoluteProjectPath(Path.Combine(ReferenceDirectory, Slug(x) + ".png")))
				.Where(File.Exists)
				.ToArray();
			if (files.Length == 0)
				throw new InvalidOperationException("No P0 reference images exist. Run Generate P0 Reference Images first.");

			const int cellWidth = 192;
			const int cellHeight = 128;
			const int columns = 8;
			var rows = Mathf.CeilToInt(files.Length / (float)columns);
			var sheet = new Texture2D(columns * cellWidth, rows * cellHeight, TextureFormat.RGBA32, false, true);
			sheet.SetPixels(Enumerable.Repeat(new Color(0.02f, 0.02f, 0.02f, 1f), sheet.width * sheet.height).ToArray());

			for (var index = 0; index < files.Length; index++) {
				var tile = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
				if (!tile.LoadImage(File.ReadAllBytes(files[index]), false)) {
					UnityEngine.Object.DestroyImmediate(tile);
					continue;
				}

				var resized = Resize(tile, cellWidth, cellHeight - 24);
				var x0 = (index % columns) * cellWidth;
				var y0 = (rows - 1 - index / columns) * cellHeight + 24;
				sheet.SetPixels(x0, y0, cellWidth, cellHeight - 24, resized.GetPixels());
				UnityEngine.Object.DestroyImmediate(resized);
				UnityEngine.Object.DestroyImmediate(tile);
			}

			sheet.Apply(false, false);
			var sheetPath = ToAbsoluteProjectPath(Path.Combine(ReferenceDirectory, "contact-sheet.png"));
			File.WriteAllBytes(sheetPath, sheet.EncodeToPNG());
			UnityEngine.Object.DestroyImmediate(sheet);
			AssetDatabase.Refresh();
			Debug.Log("[VJ] Generated P0 contact sheet: " + sheetPath);
			foreach (var warning in warnings)
				Debug.LogWarning("[VJ] " + warning);
		}

		public static GenerationResult GenerateDocumentationAndPresetsInternal() {
			var result = new GenerationResult();
			var records = LoadAllRecords(result.Warnings, result.Errors);
			if (records.Count == 0) {
				result.Errors.Add("No VJ variant ledger could be read.");
				return result;
			}

			var documentation = BuildDocumentation(records, result.Warnings);
			WriteProjectText(MainReferencePath, documentation);
			WriteProjectText(CompatibilityReferencePath, documentation);
			WriteProjectText(PresetPath, BuildPresetJson(records));
			result.VariantCount = records.Count;
			result.PresetCount = records.Count(x => ResolvePriority(x) == "P0");
			return result;
		}

		private static List<VariantRecord> LoadAllRecords(out List<string> warnings) {
			var errors = new List<string>();
			var records = LoadAllRecords(warnings = new List<string>(), errors);
			foreach (var error in errors)
				Debug.LogError("[VJ] " + error);
			return records;
		}

		private static List<VariantRecord> LoadAllRecords(List<string> warnings, List<string> errors = null) {
			var records = new List<VariantRecord>();
			LoadLedger(SpatialLedgerPath, records, warnings, errors);
			LoadLedger(CompositingLedgerPath, records, warnings, errors);
			LoadLedger(AudioLedgerPath, records, warnings, errors);
			return records;
		}

		private static void LoadLedger(string path, List<VariantRecord> records, List<string> warnings, List<string> errors) {
			var absolutePath = ToAbsoluteProjectPath(path);
			if (!File.Exists(absolutePath)) {
				warnings.Add("Ledger is not available yet: " + path);
				return;
			}

			try {
				var dto = JsonUtility.FromJson<ManifestDto>(File.ReadAllText(absolutePath));
				if (dto == null || dto.variants == null) {
					AddError(errors, "Ledger has no variants array: " + path);
					return;
				}

				foreach (var source in dto.variants)
					if (source != null)
						records.Add(Normalize(source, path));
			}
			catch (Exception exception) {
				AddError(errors, "Unable to read " + path + ": " + exception.Message);
			}
		}

		private static VariantRecord Normalize(VariantDto source, string ledgerPath) {
			var family = First(source.family, "Unknown");
			var name = First(source.name, source.displayName, source.id, source.variantId, "Unnamed Variant");
			var id = First(source.nodeTypeId, source.id, source.variantId, family.ToLowerInvariant() + "." + source.variant);
			var features = source.features ?? source.featureFlags ?? Array.Empty<string>();
			if (features.Length == 0)
				features = DefaultFeatures(family, source.stateful, (source.inputs?.Length ?? 0) > 0);
			return new VariantRecord {
				Id = id,
				Name = name,
				Category = First(source.category, family),
				Family = family,
				Shader = First(source.shader, "unknown"),
				Variant = source.variant,
				Pass = source.pass,
				Priority = ResolvePriority(source),
				Inputs = source.inputs ?? Array.Empty<string>(),
				Parameters = source.parameters ?? Array.Empty<string>(),
				Stateful = source.stateful,
				HistorySlots = source.historySlots,
				WarmupFrames = source.warmupFrames,
				Features = features,
				TestStrategy = First(source.testStrategy, "finite-deterministic-reference"),
				LedgerPath = ledgerPath
			};
		}

		private static string[] DefaultFeatures(string family, bool stateful, bool hasInputs) {
			var features = new List<string> { "linear-hdr", "premultiplied-alpha", "finite-guard", "deterministic-seed" };
			if (family.IndexOf("Generator", StringComparison.OrdinalIgnoreCase) >= 0)
				features.Add("generator-3-resolution");
			if (stateful)
				features.Add("history-reset-resize-pause");
			if (family.IndexOf("Audio", StringComparison.OrdinalIgnoreCase) >= 0 && hasInputs)
				features.Add("audio-fixture-input");
			if (family.IndexOf("Raymarch", StringComparison.OrdinalIgnoreCase) >= 0)
				features.Add("step-cap");
			return features.ToArray();
		}

		private static string ResolvePriority(VariantDto source) {
			if (!string.IsNullOrWhiteSpace(source.priority))
				return source.priority.ToUpperInvariant();

			var family = First(source.family, string.Empty);
			if (family.Equals("Blend", StringComparison.OrdinalIgnoreCase))
				return "P0";
			if (family.Equals("Transition", StringComparison.OrdinalIgnoreCase))
				return source.variant < 12 ? "P0" : "P1";
			if (family.Equals("Temporal", StringComparison.OrdinalIgnoreCase))
				return source.variant < 12 ? "P0" : source.variant < 26 ? "P1" : "P2";
			if (family.Equals("Utility", StringComparison.OrdinalIgnoreCase))
				return source.variant < 12 ? "SUPPORT" : "P1";
			// Audio and Raymarch are implemented and documented, but their
			// priority classification is intentionally deferred.  They must
			// not inflate the formal P0 arithmetic until their reference
			// fixtures are accepted by the pack owner.
			if (family.Equals("Audio", StringComparison.OrdinalIgnoreCase) ||
				family.Equals("Raymarch", StringComparison.OrdinalIgnoreCase))
				return "UNCLASSIFIED";
			return "P1";
		}

		private static string BuildDocumentation(List<VariantRecord> records, List<string> warnings) {
			var builder = new StringBuilder();
			builder.AppendLine("# VJ Shader Reference");
			builder.AppendLine();
			builder.AppendLine("Generated from the three machine-readable ledgers. Run the ShitDesigner VJ Shader Pack documentation menu after changing a ledger.");
			builder.AppendLine();
			builder.AppendLine("## Contract");
			builder.AppendLine();
			builder.AppendLine("- Internal color is linear HDR with premultiplied alpha.");
			builder.AppendLine("- _SD_Time, _SD_DeltaTime, _SD_Frame, _SD_Resolution, _SD_Seed, beat/bar phase, and pointer values are explicit graph inputs.");
			builder.AppendLine("- Same input, seed, frame, resolution, and parameters must produce finite deterministic pixels.");
			builder.AppendLine("- Generator nodes output one image and are checked at 1920x1080, 1080x1920, and 1024x1024.");
			builder.AppendLine("- Effect nodes accept an image input; displacement, mask, audio, and history inputs are listed per variant.");
			builder.AppendLine("- Blend and Transition endpoint references use a maximum error of 1/1024; Temporal variants document history ownership.");
			builder.AppendLine();
			builder.AppendLine("## Priority arithmetic");
			builder.AppendLine();
			builder.AppendLine("The formal P0 total is 162: spatial P0 102 + Blend 36 + Transition 12 + Temporal 12. Utility's first 12 entries are Phase 1 support fixtures, not additional P0 nodes.");
			builder.AppendLine("Audio and Raymarch entries are implemented but remain UNCLASSIFIED for priority accounting until their accepted reference fixtures are signed off.");
			builder.AppendLine("The old three-node project format remains compatible: shitdesigner.shader.generator, shitdesigner.shader.effect, and shitdesigner.shader.blend2 keep their original shader keys.");
			builder.AppendLine();
			builder.AppendLine("## Ledger status");
			builder.AppendLine();
			foreach (var warning in warnings.Distinct())
				builder.AppendLine("- " + warning);
			if (warnings.Count == 0)
				builder.AppendLine("- All expected ledgers were found.");
			builder.AppendLine();
			builder.AppendLine("## Family summary");
			builder.AppendLine();
			builder.AppendLine("| Family | Variants | P0 | P1 | P2/Support | Shader |");
			builder.AppendLine("|---|---:|---:|---:|---:|---|");
			foreach (var group in records.GroupBy(x => x.Family).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) {
				var shader = group.Select(x => x.Shader).FirstOrDefault() ?? "unknown";
				builder.AppendLine("| " + Escape(group.Key) + " | " + group.Count() + " | " +
					group.Count(x => ResolvePriority(x) == "P0") + " | " +
					group.Count(x => ResolvePriority(x) == "P1") + " | " +
					group.Count(x => ResolvePriority(x) != "P0" && ResolvePriority(x) != "P1") + " | " +
					Escape(shader) + " |");
			}
			builder.AppendLine();
			builder.AppendLine("## Variant index");
			builder.AppendLine();
			foreach (var group in records.GroupBy(x => x.Category).OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)) {
				builder.AppendLine("### " + group.Key);
				builder.AppendLine();
				builder.AppendLine("| Priority | Name | NodeTypeId | Family | Variant | Shader | Pass | Inputs | Parameters | History | Features | Test |");
				builder.AppendLine("|---|---|---|---|---:|---|---:|---|---|---:|---|---|");
				foreach (var record in group.OrderBy(x => x.Variant).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase)) {
					builder.Append("| ").Append(Escape(ResolvePriority(record))).Append(" | ")
						.Append(Escape(record.Name)).Append(" | ").Append(Escape(record.Id)).Append(" | ")
						.Append(Escape(record.Family)).Append(" | ").Append(record.Variant).Append(" | ")
						.Append(Escape(record.Shader)).Append(" | ").Append(record.Pass).Append(" | ")
						.Append(Escape(Join(record.Inputs))).Append(" | ").Append(Escape(Join(record.Parameters))).Append(" | ")
						.Append(record.HistorySlots).Append(record.Stateful ? " slots, " + record.WarmupFrames + " warmup" : "")
						.Append(" | ").Append(Escape(Join(record.Features))).Append(" | ")
						.Append(Escape(record.TestStrategy)).AppendLine(" |");
				}
				builder.AppendLine();
			}
			return builder.ToString();
		}

		private static string BuildPresetJson(List<VariantRecord> records) {
			var presets = new List<PresetRecord>();
			foreach (var record in records.Where(x => ResolvePriority(x) == "P0").GroupBy(x => x.Family).Select(x => x.First())) {
				presets.Add(new PresetRecord {
					id = "preset." + Slug(record.Family),
					name = record.Family + " P0 Starter",
					variantId = record.Id,
					family = record.Family,
					shader = record.Shader,
					variant = record.Variant,
					values = new[]
					{
						new PresetValue { id = "amount", value = 0.5f },
						new PresetValue { id = "frequency", value = 4f },
						new PresetValue { id = "detail", value = 4f },
						new PresetValue { id = "softness", value = 0.05f },
						new PresetValue { id = "threshold", value = 0.5f },
						new PresetValue { id = "gain", value = 1f },
						new PresetValue { id = "mix", value = 0.5f },
						new PresetValue { id = "speed", value = 1f },
						new PresetValue { id = "scale", value = 1f },
						new PresetValue { id = "radius", value = 1f }
					},
					colors = new[] { "#FF3311", "#1133FF", "#22FF66" },
					tags = new[] { "starter", "p0", "linear-hdr", "premultiplied-alpha" }
				});
			}
			return JsonUtility.ToJson(new PresetWrapper { schemaVersion = 1, presets = presets.ToArray() }, true) + Environment.NewLine;
		}

		private static void RenderReference(VariantRecord record) {
			var shader = Shader.Find(record.Shader);
			if (shader == null) {
				Debug.LogWarning("[VJ] Shader not found for " + record.Id + ": " + record.Shader);
				return;
			}

			const int width = 192;
			const int height = 108;
			var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
			var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
				hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp
			};
			target.Create();
			var source = CreateSourceTexture(width, height);
			try {
				ConfigureMaterial(material, record, source);
				Graphics.Blit(source, target, material);
				var previous = RenderTexture.active;
				RenderTexture.active = target;
				var output = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
				output.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				output.Apply(false, false);
				File.WriteAllBytes(ToAbsoluteProjectPath(Path.Combine(ReferenceDirectory, Slug(record) + ".png")), output.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(output);
				RenderTexture.active = previous;
			}
			finally {
				UnityEngine.Object.DestroyImmediate(source);
				target.Release();
				UnityEngine.Object.DestroyImmediate(target);
				UnityEngine.Object.DestroyImmediate(material);
			}
		}

		private static void ConfigureMaterial(Material material, VariantRecord record, Texture source) {
			SetFloatIfPresent(material, "_VJVariant", record.Variant);
			SetFloatIfPresent(material, "_SD_Frame", 17f);
			SetFloatIfPresent(material, "_SD_Time", 0.75f);
			SetFloatIfPresent(material, "_SD_DeltaTime", 1f / 60f);
			SetFloatIfPresent(material, "_SD_Seed", 19f);
			SetFloatIfPresent(material, "_VJSeed", 19f);
			SetFloatIfPresent(material, "_VJAmount", 0.5f);
			SetFloatIfPresent(material, "_VJFrequency", 4f);
			SetFloatIfPresent(material, "_VJDetail", 4f);
			SetFloatIfPresent(material, "_VJSoftness", 0.05f);
			SetFloatIfPresent(material, "_VJThreshold", 0.5f);
			SetFloatIfPresent(material, "_VJGain", 1f);
			SetFloatIfPresent(material, "_VJMix", 0.5f);
			SetFloatIfPresent(material, "_VJSpeed", 1f);
			SetFloatIfPresent(material, "_VJScale", 1f);
			SetFloatIfPresent(material, "_VJRadius", 1f);
			SetFloatIfPresent(material, "_VJFalloff", 1f);
			SetVectorIfPresent(material, "_SD_Resolution", new Vector4(192f, 108f, 1f / 192f, 1f / 108f));
			SetVectorIfPresent(material, "_VJCenter", new Vector4(0.5f, 0.5f, 0f, 0f));
			SetVectorIfPresent(material, "_VJColorA", new Vector4(1f, 0.2f, 0.05f, 1f));
			SetVectorIfPresent(material, "_VJColorB", new Vector4(0.05f, 0.1f, 1f, 1f));
			SetVectorIfPresent(material, "_VJColorC", new Vector4(0.05f, 1f, 0.2f, 1f));
			SetVectorIfPresent(material, "_VJDisplacement", new Vector4(0.02f, 0.02f, 0f, 0f));
			if (record.Family.Equals("Blend", StringComparison.OrdinalIgnoreCase) ||
				record.Family.Equals("Transition", StringComparison.OrdinalIgnoreCase)) {
				SetTextureIfPresent(material, "_TexA", source);
				SetTextureIfPresent(material, "_TexB", source);
			}
			else {
				SetTextureIfPresent(material, "_MainTex", source);
			}
		}

		private static Texture2D CreateSourceTexture(int width, int height) {
			var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true) {
				hideFlags = HideFlags.HideAndDontSave,
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp
			};
			var pixels = new Color[width * height];
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++) {
					var u = x / (float)Math.Max(width - 1, 1);
					var v = y / (float)Math.Max(height - 1, 1);
					pixels[y * width + x] = new Color(u, v, 1f - 0.5f * u, 0.5f + 0.5f * v);
				}
			texture.SetPixels(pixels);
			texture.Apply(false, false);
			return texture;
		}

		private static Texture2D Resize(Texture2D source, int width, int height) {
			var result = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
			var sourcePixels = source.GetPixels();
			var pixels = new Color[width * height];
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++) {
					var sx = Mathf.Clamp(Mathf.FloorToInt(x * source.width / (float)width), 0, source.width - 1);
					var sy = Mathf.Clamp(Mathf.FloorToInt(y * source.height / (float)height), 0, source.height - 1);
					pixels[y * width + x] = sourcePixels[sy * source.width + sx];
				}
			result.SetPixels(pixels);
			result.Apply(false, false);
			return result;
		}

		private static void SetFloatIfPresent(Material material, string property, float value) {
			if (material.HasProperty(property))
				material.SetFloat(property, value);
		}

		private static void SetVectorIfPresent(Material material, string property, Vector4 value) {
			if (material.HasProperty(property))
				material.SetVector(property, value);
		}

		private static void SetTextureIfPresent(Material material, string property, Texture value) {
			if (material.HasProperty(property))
				material.SetTexture(property, value);
		}

		private static string ResolvePriority(VariantRecord record) {
			return string.IsNullOrWhiteSpace(record.Priority) ? "P1" : record.Priority;
		}

		private static string Slug(VariantRecord record) => Slug(record.Id);
		private static string Slug(string value) {
			var builder = new StringBuilder();
			foreach (var character in value.ToLowerInvariant())
				builder.Append(char.IsLetterOrDigit(character) ? character : '-');
			return builder.ToString().Trim('-');
		}

		private static string First(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
		private static string Join(string[] values) => values == null || values.Length == 0 ? "—" : string.Join(", ", values);
		private static string Escape(string value) => (value ?? string.Empty).Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
		private static string ToAbsoluteProjectPath(string relativePath) => Path.GetFullPath(Path.Combine(Directory.GetParent(UnityEngine.Application.dataPath).FullName, relativePath));

		private static void WriteProjectText(string relativePath, string content) {
			var path = ToAbsoluteProjectPath(relativePath);
			Directory.CreateDirectory(Path.GetDirectoryName(path));
			File.WriteAllText(path, content, new UTF8Encoding(false));
		}

		private static void AddError(List<string> errors, string value) {
			if (errors != null)
				errors.Add(value);
			else
				Debug.LogError("[VJ] " + value);
		}

		public sealed class GenerationResult {
			public int VariantCount;
			public int PresetCount;
			public readonly List<string> Warnings = new List<string>();
			public readonly List<string> Errors = new List<string>();
		}

		[Serializable]
		private sealed class ManifestDto {
			public VariantDto[] variants;
		}

		[Serializable]
		private sealed class VariantDto {
			public string id;
			public string nodeTypeId;
			public string variantId;
			public string name;
			public string displayName;
			public string category;
			public string family;
			public string shader;
			public int variant;
			public int pass;
			public string[] inputs;
			public string[] outputs;
			public string[] parameters;
			public string[] features;
			public string[] featureFlags;
			public bool stateful;
			public int historySlots;
			public int warmupFrames;
			public string priority;
			public string role;
			public string testStrategy;
		}

		private sealed class VariantRecord {
			public string Id;
			public string Name;
			public string Category;
			public string Family;
			public string Shader;
			public int Variant;
			public int Pass;
			public string Priority;
			public string[] Inputs;
			public string[] Parameters;
			public bool Stateful;
			public int HistorySlots;
			public int WarmupFrames;
			public string[] Features;
			public string TestStrategy;
			public string LedgerPath;
		}

		[Serializable]
		private sealed class PresetWrapper {
			public int schemaVersion;
			public PresetRecord[] presets;
		}

		[Serializable]
		private sealed class PresetRecord {
			public string id;
			public string name;
			public string variantId;
			public string family;
			public string shader;
			public int variant;
			public PresetValue[] values;
			public string[] colors;
			public string[] tags;
		}

		[Serializable]
		private sealed class PresetValue {
			public string id;
			public float value;
		}
	}
}
