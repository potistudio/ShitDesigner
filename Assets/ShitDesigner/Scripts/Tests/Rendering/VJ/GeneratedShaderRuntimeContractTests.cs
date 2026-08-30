using System;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Nodes;
using ShitDesigner.Rendering;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Rendering.Tests.VJ {
	/// <summary>Cross-checks the generated Standalone artifacts against all
	/// three authoritative ledgers and probes the actual family Material
	/// properties used by the runtime uniform bridge.</summary>
	public sealed class GeneratedShaderRuntimeContractTests {
		private const string ManifestPath = "Assets/ShitDesigner/Scripts/Modules/Nodes/ShaderNodeManifest.asset";
		private const string CatalogPath = "Assets/ShitDesigner/Scripts/Modules/Nodes/NodeTypeCatalog.asset";

		[Test]
		public void GeneratedManifestAsset_ContainsAll438LedgerEntriesAndDirectShaders() {
			var asset = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(ManifestPath);
			Assert.That(asset, Is.Not.Null);
			var valid = asset.ValidateShaderReferences();
			Assert.That(valid.IsSuccess, Is.True, valid.IsFailure ? valid.Error.Message : string.Empty);

			var generated = asset.Entries.Where(x => !string.IsNullOrWhiteSpace(x.SourceLedger)).ToList();
			Assert.That(generated.Count, Is.EqualTo(438));
			Assert.That(generated.Count(x => x.SourceLedger == "spatial-variants.json"), Is.EqualTo(246));
			Assert.That(generated.Count(x => x.SourceLedger == "compositing-temporal-variants.json"), Is.EqualTo(104));
			Assert.That(generated.Count(x => x.SourceLedger == "audio-raymarch-utility-variants.json"), Is.EqualTo(88));
			Assert.That(generated.Select(x => x.TypeId).Distinct(StringComparer.Ordinal).Count(), Is.EqualTo(438));
			Assert.That(generated.All(x => x.Shader != null && x.Passes.Count > 0 && x.SourceVariant >= 0), Is.True);
			Assert.That(generated.All(x => x.Passes.Any(pass => pass.Index == x.OutputPass && pass.VariantId == x.VariantId)), Is.True);
			Assert.That(asset.Entries.Count(x => string.IsNullOrWhiteSpace(x.SourceLedger)), Is.EqualTo(4));
			Assert.That(asset.Entries.Select(x => x.TypeId), Does.Contain("shitdesigner.shader.generator"));
			Assert.That(asset.Entries.Select(x => x.TypeId), Does.Contain("shitdesigner.shader.effect"));
			Assert.That(asset.Entries.Select(x => x.TypeId), Does.Contain("shitdesigner.shader.blend2"));
			Assert.That(asset.Entries.Select(x => x.TypeId), Does.Contain("shitdesigner.shader.generator.recursive-rectangles"));
			var recursive = asset.Find("shitdesigner.shader.generator.recursive-rectangles");
			Assert.That(recursive, Is.Not.Null);
			Assert.That(recursive.Shader, Is.Not.Null);
			Assert.That(recursive.Parameters.Count, Is.EqualTo(15));
			Assert.That(recursive.Parameters.Single(x => x.Id == "color_a").DisplayName, Is.EqualTo("Color"));
			Assert.That(recursive.Parameters.Any(x => x.Id == "color_b"), Is.False);
			Assert.That(recursive.Parameters.Single(x => x.Id == "seed").Property, Is.EqualTo("_StructureSeed"));
		}

		[Test]
		public void GeneratedCatalog_MatchesManifestEntriesAndNumericVariants() {
			var manifest = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(ManifestPath);
			var catalog = AssetDatabase.LoadAssetAtPath<NodeTypeCatalog>(CatalogPath);
			Assert.That(manifest, Is.Not.Null);
			Assert.That(catalog, Is.Not.Null);
			Assert.That(catalog.ValidateManifest().IsSuccess, Is.True);
			var runtime = catalog.BuildRuntimeCatalog();
			Assert.That(runtime.IsSuccess, Is.True, runtime.IsFailure ? runtime.Error.Message : string.Empty);
			Assert.That(catalog.Entries.Count, Is.EqualTo(462));
			Assert.That(catalog.Entries.Count(x => !string.IsNullOrWhiteSpace(x.ShaderKey)), Is.EqualTo(442));
			Assert.That(runtime.Value.Entries.Count, Is.EqualTo(catalog.Entries.Count));

			foreach (var source in manifest.Entries) {
				var catalogEntry = catalog.Entries.Single(x => x.TypeId == source.TypeId);
				var runtimeEntry = runtime.Value.Entries.Single(x => x.TypeId.Value == source.TypeId);
				Assert.That(catalogEntry.ShaderVariantId, Is.EqualTo(source.VariantId), source.TypeId);
				Assert.That(catalogEntry.ShaderSourceVariant, Is.EqualTo(source.SourceVariant), source.TypeId);
				Assert.That(catalogEntry.OutputPass, Is.EqualTo(source.OutputPass), source.TypeId);
				Assert.That(runtimeEntry.ShaderBinding == null || runtimeEntry.ShaderBinding.SourceVariant == source.SourceVariant, Is.True, source.TypeId);
			}
		}

		[Test]
		public void FamilyMaterials_ReceiveLedgerVariantAndStoppedClockAliases() {
			var asset = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(ManifestPath);
			Assert.That(asset, Is.Not.Null);
			var runtime = asset.BuildRuntimeManifest();
			foreach (var entry in runtime.Entries.Where(x => !string.IsNullOrWhiteSpace(x.SourceLedger))) {
				var assetEntry = asset.Find(entry.TypeId.Value);
				Assert.That(assetEntry, Is.Not.Null, entry.TypeId.Value);
				Assert.That(assetEntry.Shader, Is.Not.Null, entry.TypeId.Value);
				var material = new Material(assetEntry.Shader);
				try {
					var binding = new ShaderMaterialBinding(entry.ShaderKey, assetEntry.Shader, descriptor: entry.ToShaderBinding());
					ShaderRuntimeUniformApplier.Apply(material, binding, graphTime: 0d, deltaTime: 0d,
						frameNumber: 37, width: 640, height: 360, seed: .25f);
					var variantProperty = material.HasProperty(ShaderFrameUniformNames.VjVariant)
						? ShaderFrameUniformNames.VjVariant : ShaderFrameUniformNames.Variant;
					Assert.That(material.HasProperty(variantProperty), Is.True, entry.TypeId.Value);
					Assert.That(material.GetFloat(variantProperty), Is.EqualTo(entry.SourceVariant).Within(.001f), entry.TypeId.Value);
					Assert.That(material.GetFloat(ShaderFrameUniformNames.Time), Is.EqualTo(0f).Within(.0001f), entry.TypeId.Value);
					if (material.HasProperty(ShaderFrameUniformNames.GraphTime))
						Assert.That(material.GetFloat(ShaderFrameUniformNames.GraphTime), Is.EqualTo(0f).Within(.0001f), entry.TypeId.Value);
					Assert.That(material.GetFloat(ShaderFrameUniformNames.Frame), Is.EqualTo(37f).Within(.0001f), entry.TypeId.Value);
					if (material.HasProperty(ShaderFrameUniformNames.FrameAlias))
						Assert.That(material.GetFloat(ShaderFrameUniformNames.FrameAlias), Is.EqualTo(37f).Within(.0001f), entry.TypeId.Value);
					Assert.That(material.GetFloat(ShaderFrameUniformNames.Seed), Is.EqualTo(.25f).Within(.0001f), entry.TypeId.Value);
					if (material.HasProperty(ShaderFrameUniformNames.SeedAlias))
						Assert.That(material.GetFloat(ShaderFrameUniformNames.SeedAlias), Is.EqualTo(.25f).Within(.0001f), entry.TypeId.Value);
					Assert.That(material.GetFloat(ShaderFrameUniformNames.BeatPhase), Is.EqualTo(0f).Within(.0001f), entry.TypeId.Value);
				}
				finally {
					UnityEngine.Object.DestroyImmediate(material);
				}
			}
		}

		[Test]
		public void RecursiveRectangles_ReceivesThePublishedBeatClock() {
			var asset = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(ManifestPath);
			var entry = asset.BuildRuntimeManifest().Find("shitdesigner.shader.generator.recursive-rectangles");
			var shader = asset.Find(entry.TypeId.Value).Shader;
			var material = new Material(shader);
			try {
				ShaderBeatClock.Publish(new BeatClockFrame(120f, 3.25d));
				var binding = new ShaderMaterialBinding(entry.ShaderKey, shader, descriptor: entry.ToShaderBinding());
				ShaderRuntimeUniformApplier.Apply(material, binding, graphTime: 0d, deltaTime: 0d,
					frameNumber: 1, width: 64, height: 64, seed: 0f);

				Assert.That(material.GetFloat(ShaderFrameUniformNames.HasBeatClock), Is.EqualTo(1f));
				Assert.That(material.GetFloat(ShaderFrameUniformNames.BeatPhase), Is.EqualTo(.25f).Within(.0001f));
				Assert.That(material.GetFloat(ShaderFrameUniformNames.BeatIndex), Is.EqualTo(3f));
				Assert.That(material.GetFloat(ShaderFrameUniformNames.BeatDuration), Is.EqualTo(.5f).Within(.0001f));
			}
			finally { UnityEngine.Object.DestroyImmediate(material); }
		}
	}
}
