using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace ShitDesigner.Documentation.Tests {
	public sealed class VJShaderDocumentationContractTests {
		private const string SpatialPath = "ShitDesigner/Shaders/Manifests/spatial-variants.json";
		private const string CompositingPath = "ShitDesigner/Shaders/Manifests/compositing-temporal-variants.json";
		private const string AudioPath = "ShitDesigner/Shaders/Manifests/audio-raymarch-utility-variants.json";
		private const string ReferencePath = "docs/vj-shader-reference.md";
		private const string CompatibilityPath = "docs/shader-shader-reference.md";
		private const string PresetPath = "ShitDesigner/Presets/vj-presets.json";
		private const string P0ReferenceDirectory = "ShitDesigner/Shaders/References/P0";
		private const string P0ContactSheetPath = "ShitDesigner/Shaders/References/P0/contact-sheet.png";

		[Test]
		[Category("VJDocumentation")]
		[Category("Manifest")]
		public void AllThreeLedgersAndReferenceDocumentContain438StableVariants() {
			var audio = AbsoluteAssetPath(AudioPath);
			if (!File.Exists(audio))
				Assert.Ignore("audio-raymarch-utility ledger is still being produced.");

			var records = LoadRecords();
			Assert.That(records.Count, Is.EqualTo(438));
			var ids = new HashSet<string>(StringComparer.Ordinal);
			foreach (var record in records) {
				Assert.That(record.Id, Is.Not.Empty);
				Assert.That(ids.Add(record.Id), Is.True, "Duplicate variant ID: " + record.Id);
				Assert.That(record.Family, Is.Not.Empty);
				Assert.That(record.Shader, Is.Not.Empty);
				Assert.That(record.Inputs, Is.Not.Null);
				Assert.That(record.Parameters, Is.Not.Null);
				Assert.That(record.Pass, Is.GreaterThanOrEqualTo(0));
				Assert.That(record.HistorySlots, Is.GreaterThanOrEqualTo(0));
			}

			var p0 = records.Count(x => Priority(x) == "P0");
			Assert.That(p0, Is.EqualTo(162), "Formal P0 is spatial 102 + Blend 36 + Transition 12 + Temporal 12.");
			Assert.That(records.Count(x => x.Family == "Utility" && Priority(x) == "SUPPORT"), Is.EqualTo(12));

			var reference = File.ReadAllText(AbsoluteProjectPath(ReferencePath));
			var compatibility = File.ReadAllText(AbsoluteProjectPath(CompatibilityPath));
			Assert.That(reference, Does.Contain("VJ Shader Reference"));
			Assert.That(reference, Does.Contain("formal P0 total is 162"));
			Assert.That(compatibility, Is.EqualTo(reference));
			foreach (var record in records)
				Assert.That(reference, Does.Contain(record.Id), "Reference is missing " + record.Id);
		}

		[Test]
		[Category("VJDocumentation")]
		[Category("Preset")]
		public void RepresentativePresetDefinitionUsesStableLedgerIds() {
			var audio = AbsoluteAssetPath(AudioPath);
			if (!File.Exists(audio))
				Assert.Ignore("audio-raymarch-utility ledger is still being produced.");

			var records = LoadRecords();
			var ids = new HashSet<string>(records.Select(x => x.Id), StringComparer.Ordinal);
			var presetPath = AbsoluteAssetPath(PresetPath);
			Assert.That(File.Exists(presetPath), Is.True, presetPath);

			var presets = JsonUtility.FromJson<PresetWrapper>(File.ReadAllText(presetPath));
			Assert.That(presets, Is.Not.Null);
			Assert.That(presets.schemaVersion, Is.EqualTo(1));
			Assert.That(presets.presets, Is.Not.Null);
			Assert.That(presets.presets.Length, Is.GreaterThanOrEqualTo(10));

			var presetIds = new HashSet<string>(StringComparer.Ordinal);
			foreach (var preset in presets.presets) {
				Assert.That(presetIds.Add(preset.id), Is.True, "Duplicate preset ID: " + preset.id);
				Assert.That(ids.Contains(preset.variantId), Is.True, "Preset points to unknown variant: " + preset.variantId);
				Assert.That(preset.family, Is.Not.Empty);
				Assert.That(preset.shader, Is.Not.Empty);
				Assert.That(preset.values, Is.Not.Null);
				Assert.That(preset.values.Length, Is.GreaterThan(0));
				Assert.That(preset.tags, Does.Contain("p0"));
			}
		}

		[Test]
		[Category("VJDocumentation")]
		[Category("ReferenceImages")]
		public void P0ReferenceImagesAndContactSheetCoverFormalP0() {
			var audio = AbsoluteAssetPath(AudioPath);
			if (!File.Exists(audio))
				Assert.Ignore("audio-raymarch-utility ledger is still being produced.");

			var p0 = LoadRecords().Where(x => Priority(x) == "P0").ToArray();
			Assert.That(p0.Length, Is.EqualTo(162));

			var directory = AbsoluteAssetPath(P0ReferenceDirectory);
			Assert.That(Directory.Exists(directory), Is.True, directory);
			var pngs = Directory.GetFiles(directory, "*.png", SearchOption.TopDirectoryOnly)
				.Where(x => !string.Equals(Path.GetFileName(x), "contact-sheet.png", StringComparison.OrdinalIgnoreCase))
				.ToArray();
			Assert.That(pngs.Length, Is.EqualTo(162));

			foreach (var record in p0) {
				var path = Path.Combine(directory, Slug(record.Id) + ".png");
				Assert.That(File.Exists(path), Is.True, "Missing P0 reference image: " + record.Id);
				var image = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
				try {
					Assert.That(image.LoadImage(File.ReadAllBytes(path), false), Is.True, path);
					Assert.That(image.width, Is.EqualTo(192), path);
					Assert.That(image.height, Is.EqualTo(108), path);
				}
				finally {
					UnityEngine.Object.DestroyImmediate(image);
				}
			}

			var contactSheet = AbsoluteAssetPath(P0ContactSheetPath);
			Assert.That(File.Exists(contactSheet), Is.True, contactSheet);
			var sheet = new Texture2D(2, 2, TextureFormat.RGBA32, false, true);
			try {
				Assert.That(sheet.LoadImage(File.ReadAllBytes(contactSheet), false), Is.True, contactSheet);
				Assert.That(sheet.width, Is.EqualTo(1536), contactSheet);
				Assert.That(sheet.height, Is.EqualTo(2688), contactSheet);
			}
			finally {
				UnityEngine.Object.DestroyImmediate(sheet);
			}
		}

		private static List<VariantRecord> LoadRecords() {
			var records = new List<VariantRecord>();
			LoadLedger(SpatialPath, records);
			LoadLedger(CompositingPath, records);
			LoadLedger(AudioPath, records);
			return records;
		}

		private static void LoadLedger(string relativeAssetPath, List<VariantRecord> records) {
			var path = AbsoluteAssetPath(relativeAssetPath);
			Assert.That(File.Exists(path), Is.True, path);
			var dto = JsonUtility.FromJson<ManifestDto>(File.ReadAllText(path));
			Assert.That(dto, Is.Not.Null, path);
			Assert.That(dto.variants, Is.Not.Null, path);
			foreach (var source in dto.variants) {
				Assert.That(source, Is.Not.Null);
				records.Add(new VariantRecord {
					Id = First(source.nodeTypeId, source.id, source.variantId),
					Family = source.family ?? string.Empty,
					Shader = source.shader ?? string.Empty,
					Variant = source.variant,
					Pass = source.pass,
					Inputs = source.inputs ?? Array.Empty<string>(),
					Parameters = source.parameters ?? Array.Empty<string>(),
					Stateful = source.stateful,
					HistorySlots = source.historySlots,
					PriorityValue = source.priority
				});
			}
		}

		private static string Priority(VariantRecord record) {
			if (!string.IsNullOrWhiteSpace(record.PriorityValue))
				return record.PriorityValue.ToUpperInvariant();
			if (record.Family.Equals("Blend", StringComparison.OrdinalIgnoreCase))
				return "P0";
			if (record.Family.Equals("Transition", StringComparison.OrdinalIgnoreCase))
				return record.Variant < 12 ? "P0" : "P1";
			if (record.Family.Equals("Temporal", StringComparison.OrdinalIgnoreCase))
				return record.Variant < 12 ? "P0" : record.Variant < 26 ? "P1" : "P2";
			if (record.Family.Equals("Utility", StringComparison.OrdinalIgnoreCase))
				return record.Variant < 12 ? "SUPPORT" : "P1";
			if (record.Family.Equals("Audio", StringComparison.OrdinalIgnoreCase) ||
				record.Family.Equals("Raymarch", StringComparison.OrdinalIgnoreCase))
				return "UNCLASSIFIED";
			return "P1";
		}

		private static string First(params string[] values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? string.Empty;
		private static string Slug(string value) {
			var builder = new System.Text.StringBuilder();
			foreach (var character in value.ToLowerInvariant())
				builder.Append(char.IsLetterOrDigit(character) ? character : '-');
			return builder.ToString().Trim('-');
		}
		private static string AbsoluteAssetPath(string path) => Path.Combine(UnityEngine.Application.dataPath, path);
		private static string AbsoluteProjectPath(string path) => Path.GetFullPath(Path.Combine(Directory.GetParent(UnityEngine.Application.dataPath).FullName, path));

		[Serializable]
		private sealed class ManifestDto {
			public VariantDto[] variants;
		}

		[Serializable]
		private sealed class VariantDto {
			public string id;
			public string nodeTypeId;
			public string variantId;
			public string family;
			public string shader;
			public int variant;
			public int pass;
			public string[] inputs;
			public string[] parameters;
			public bool stateful;
			public int historySlots;
			public string priority;
		}

		private sealed class VariantRecord {
			public string Id;
			public string Family;
			public string Shader;
			public int Variant;
			public int Pass;
			public string[] Inputs;
			public string[] Parameters;
			public bool Stateful;
			public int HistorySlots;
			public string PriorityValue;
		}

		[Serializable]
		private sealed class PresetWrapper {
			public int schemaVersion;
			public PresetRecord[] presets;
		}

		[Serializable]
		private sealed class PresetRecord {
			public string id;
			public string variantId;
			public string family;
			public string shader;
			public PresetValue[] values;
			public string[] tags;
		}

		[Serializable]
		private sealed class PresetValue {
			public string id;
			public float value;
		}
	}
}
