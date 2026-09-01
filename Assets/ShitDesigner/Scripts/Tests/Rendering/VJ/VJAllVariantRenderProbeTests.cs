using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ShitDesigner.Rendering.Tests.VJ {
	/// <summary>
	/// GPU acceptance probe for the complete VJ ledger set.  The existing
	/// family tests are intentionally narrow; this probe keeps the 449-entry
	/// contract honest by exercising every variant branch on the imported
	/// family shader and comparing two readbacks from the same deterministic
	/// fixture.
	/// </summary>
	public sealed class VJAllVariantRenderProbeTests {
		private const int ExpectedVariantCount = 449;
		private static readonly (int width, int height)[] P0Resolutions =
		{
			(8, 8),
			(16, 16),
			(32, 18)
		};

		[UnityTest]
		[Category("VJAllVariants")]
		[Category("GPU")]
		[Category("Finite")]
		[Category("Deterministic")]
		public IEnumerator All449Variants_RenderFiniteAndDeterministically() {
			if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
				Assert.Ignore("A GPU graphics device is required for the all-variant render probe.");

			var ledgers = LoadAllLedgers();
			var variants = ledgers.Spatial.Select(x => new VariantRecord(x.shader, x.variant, x.nodeTypeId))
				.Concat(ledgers.Compositing.Select(x => new VariantRecord(x.shader, x.variant, x.id)))
				.Concat(ledgers.Analysis.Select(x => new VariantRecord(x.shader, x.variant, x.id)))
				.ToArray();
			Assert.That(variants.Length, Is.EqualTo(ExpectedVariantCount));

			var shaderNames = variants.Select(x => x.Shader).Distinct(StringComparer.Ordinal).ToArray();
			var materials = new Dictionary<string, Material>(StringComparer.Ordinal);
			var source = CreateSourceTexture(16, 16, 0.0f);
			var secondary = CreateSourceTexture(16, 16, 0.37f);
			var history = CreateSourceTexture(16, 16, 0.71f);
			var target = CreateTarget(16, 16, "VJAllVariantTarget");
			var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
			try {
				foreach (var shaderName in shaderNames) {
					var shader = Shader.Find(shaderName);
					Assert.That(shader, Is.Not.Null, "Family shader was not imported: " + shaderName);
					Assert.That(shader.isSupported, Is.True, "Family shader is unsupported: " + shaderName);
					Assert.That(shader.passCount, Is.GreaterThan(0), "Family shader has no pass: " + shaderName);
					materials.Add(shaderName, new Material(shader) { name = "VJAllVariant." + shaderName });
				}

				foreach (var variant in variants) {
					var material = materials[variant.Shader];
					ConfigureMaterial(material, variant.Variant, source, secondary, history);
					Graphics.Blit(source, target, material, 0);
					yield return null;
					var first = ReadPixel(target, readback);
					AssertFinite(first, variant.Id);

					Graphics.Blit(source, target, material, 0);
					yield return null;
					var second = ReadPixel(target, readback);
					AssertFinite(second, variant.Id);
					Assert.That(Vector4.Distance(first, second), Is.LessThan(1.0e-3f),
						"Non-deterministic output for " + variant.Id);
				}
			}
			finally {
				foreach (var material in materials.Values)
					UnityEngine.Object.DestroyImmediate(material);
				DestroyTexture(source);
				DestroyTexture(secondary);
				DestroyTexture(history);
				DestroyTexture(target);
				UnityEngine.Object.DestroyImmediate(readback);
			}
		}

		[UnityTest]
		[Category("VJAllVariants")]
		[Category("GPU")]
		[Category("P0")]
		public IEnumerator P0SpatialVariants_RenderAtThreeRequiredResolutions() {
			if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
				Assert.Ignore("A GPU graphics device is required for the P0 resolution probe.");

			var ledger = JsonUtility.FromJson<SpatialLedger>(ReadLedger("spatial-variants.json"));
			var p0 = ledger.variants.Where(x => string.Equals(x.priority, "P0", StringComparison.OrdinalIgnoreCase)).ToArray();
			Assert.That(p0.Length, Is.EqualTo(102), "Spatial P0 ledger count changed.");
			var shaders = new Dictionary<string, Material>(StringComparer.Ordinal);
			var textures = new List<UnityEngine.Object>();
			var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
			try {
				foreach (var variant in p0) {
					if (!shaders.ContainsKey(variant.shader)) {
						var shader = Shader.Find(variant.shader);
						Assert.That(shader, Is.Not.Null, variant.shader);
						Assert.That(shader.isSupported, Is.True, variant.shader);
						shaders.Add(variant.shader, new Material(shader) { name = "VJP0Resolution." + variant.shader });
					}
				}

				foreach (var resolution in P0Resolutions) {
					var source = CreateSourceTexture(resolution.width, resolution.height, resolution.width * 0.013f);
					var secondary = CreateSourceTexture(resolution.width, resolution.height, 0.41f);
					var history = CreateSourceTexture(resolution.width, resolution.height, 0.79f);
					var target = CreateTarget(resolution.width, resolution.height, "VJP0Target");
					textures.Add(source);
					textures.Add(secondary);
					textures.Add(history);
					textures.Add(target);
					foreach (var variant in p0) {
						var material = shaders[variant.shader];
						ConfigureMaterial(material, variant.variant, source, secondary, history);
						material.SetVector("_SD_Resolution", new Vector4(resolution.width, resolution.height,
							1f / resolution.width, 1f / resolution.height));
						Graphics.Blit(source, target, material, 0);
						yield return null;
						var first = ReadPixel(target, readback);
						AssertFinite(first, variant.nodeTypeId + " @ " + resolution.width + "x" + resolution.height);
					}
				}
			}
			finally {
				foreach (var material in shaders.Values)
					UnityEngine.Object.DestroyImmediate(material);
				foreach (var texture in textures)
					DestroyTexture(texture);
				UnityEngine.Object.DestroyImmediate(readback);
			}
		}

		private static LedgerBundle LoadAllLedgers() {
			var spatial = JsonUtility.FromJson<SpatialLedger>(ReadLedger("spatial-variants.json"));
			var compositing = JsonUtility.FromJson<CompositingLedger>(ReadLedger("compositing-temporal-variants.json"));
			var analysis = JsonUtility.FromJson<AnalysisLedger>(ReadLedger("audio-raymarch-utility-variants.json"));
			Assert.That(spatial.variants, Is.Not.Null);
			Assert.That(compositing.variants, Is.Not.Null);
			Assert.That(analysis.variants, Is.Not.Null);
			Assert.That(spatial.variants.Length, Is.EqualTo(256));
			Assert.That(compositing.variants.Length, Is.EqualTo(104));
			Assert.That(analysis.variants.Length, Is.EqualTo(89));
			return new LedgerBundle(spatial.variants, compositing.variants, analysis.variants);
		}

		private static string ReadLedger(string fileName) {
			var path = Path.Combine(Application.dataPath, "ShitDesigner/Shaders/Manifests", fileName);
			Assert.That(File.Exists(path), Is.True, "Missing ledger: " + path);
			return File.ReadAllText(path);
		}

		private static void ConfigureMaterial(Material material, int variant, Texture source, Texture secondary, Texture history) {
			SetFloat(material, "_VJVariant", variant);
			SetFloat(material, "_Variant", variant);
			SetFloat(material, "_Amount", 0.45f);
			SetFloat(material, "_VJAmount", 0.45f);
			SetFloat(material, "_Progress", 0.37f);
			SetFloat(material, "_Softness", 0.05f);
			SetFloat(material, "_Mix", 0.5f);
			SetFloat(material, "_ExternalMask", 0.8f);
			SetFloat(material, "_Gain", 1.1f);
			SetFloat(material, "_Rms", 0.42f);
			SetFloat(material, "_Peak", 0.75f);
			SetFloat(material, "_Beat", 0.6f);
			SetFloat(material, "_BpmPhase", 0.25f);
			SetFloat(material, "_AudioRms", 0.42f);
			SetFloat(material, "_GraphTime", 1.25f);
			SetFloat(material, "_Frame", 42f);
			SetFloat(material, "_SD_Frame", 42f);
			SetFloat(material, "_SD_Time", 1.25f);
			SetFloat(material, "_SD_GraphTime", 1.25f);
			SetFloat(material, "_SD_DeltaTime", 1f / 60f);
			SetFloat(material, "_SD_Seed", 9f);
			SetFloat(material, "_Seed", 9f);
			SetFloat(material, "_Steps", 96f);
			SetFloat(material, "_Epsilon", 0.001f);
			SetFloat(material, "_FarDistance", 30f);
			SetVector(material, "_Resolution", new Vector4(16f, 16f, 1f / 16f, 1f / 16f));
			SetVector(material, "_SD_Resolution", new Vector4(16f, 16f, 1f / 16f, 1f / 16f));
			SetVector(material, "_VJCenter", new Vector4(0.5f, 0.5f, 0f, 0f));
			SetVector(material, "_VJColorA", new Vector4(1f, 0.2f, 0.05f, 1f));
			SetVector(material, "_VJColorB", new Vector4(0.05f, 0.1f, 1f, 1f));
			SetVector(material, "_VJColorC", new Vector4(0.05f, 1f, 0.2f, 1f));
			SetVector(material, "_CameraPosition", new Vector4(0f, 0f, 3f, 0f));
			SetVector(material, "_CameraTarget", new Vector4(0f, 0f, 0f, 0f));
			SetVector(material, "_LightDirection", new Vector4(0.4f, 0.7f, 0.6f, 0f));
			SetVector(material, "_SD_Pointer", new Vector4(0.5f, 0.5f, 0f, 0f));
			SetTexture(material, "_MainTex", source);
			SetTexture(material, "_TexA", source);
			SetTexture(material, "_TexB", secondary);
			SetTexture(material, "_CompareTex", secondary);
			SetTexture(material, "_MaskTex", secondary);
			SetTexture(material, "_DisplacementTex", secondary);
			SetTexture(material, "_VJDisplacementTex", secondary);
			SetTexture(material, "_WaveformTex", source);
			SetTexture(material, "_SpectrumTex", secondary);
			SetTexture(material, "_MelTex", history);
			SetTexture(material, "_OnsetTex", secondary);
			SetTexture(material, "_HistoryTex", history);
			SetTexture(material, "_HistoryTex2", secondary);
			SetTexture(material, "_HistoryTex3", source);
			SetTexture(material, "_SD_SourceTex", source);
		}

		private static void SetFloat(Material material, string property, float value) {
			if (material.HasProperty(property)) material.SetFloat(property, value);
		}

		private static void SetVector(Material material, string property, Vector4 value) {
			if (material.HasProperty(property)) material.SetVector(property, value);
		}

		private static void SetTexture(Material material, string property, Texture texture) {
			if (material.HasProperty(property)) material.SetTexture(property, texture);
		}

		private static Texture2D CreateSourceTexture(int width, int height, float phase) {
			var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true) {
				name = "VJAllVariantSource",
				filterMode = FilterMode.Bilinear,
				wrapMode = TextureWrapMode.Clamp
			};
			var pixels = new Color[width * height];
			for (var y = 0; y < height; y++)
				for (var x = 0; x < width; x++) {
					var u = x / (float)Math.Max(1, width - 1);
					var v = y / (float)Math.Max(1, height - 1);
					pixels[y * width + x] = new Color(Mathf.Repeat(u + phase, 1f), v,
						1f - u * 0.5f, 0.35f + v * 0.6f);
				}
			texture.SetPixels(pixels);
			texture.Apply(false, false);
			return texture;
		}

		private static RenderTexture CreateTarget(int width, int height, string name) {
			var target = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear) {
				name = name,
				filterMode = FilterMode.Point,
				wrapMode = TextureWrapMode.Clamp
			};
			target.Create();
			return target;
		}

		private static Vector4 ReadPixel(RenderTexture target, Texture2D readback) {
			var previous = RenderTexture.active;
			RenderTexture.active = target;
			readback.ReadPixels(new Rect(Mathf.Max(0, target.width / 2), Mathf.Max(0, target.height / 2), 1, 1), 0, 0);
			readback.Apply(false, false);
			var pixel = readback.GetPixel(0, 0);
			RenderTexture.active = previous;
			return new Vector4(pixel.r, pixel.g, pixel.b, pixel.a);
		}

		private static void AssertFinite(Vector4 pixel, string id) {
			Assert.That(float.IsNaN(pixel.x) || float.IsInfinity(pixel.x), Is.False, id + " R");
			Assert.That(float.IsNaN(pixel.y) || float.IsInfinity(pixel.y), Is.False, id + " G");
			Assert.That(float.IsNaN(pixel.z) || float.IsInfinity(pixel.z), Is.False, id + " B");
			Assert.That(float.IsNaN(pixel.w) || float.IsInfinity(pixel.w), Is.False, id + " A");
		}

		private static void DestroyTexture(UnityEngine.Object texture) {
			if (texture is RenderTexture renderTexture) {
				if (RenderTexture.active == renderTexture) RenderTexture.active = null;
				if (renderTexture.IsCreated()) renderTexture.Release();
			}
			UnityEngine.Object.DestroyImmediate(texture);
		}

		private readonly struct VariantRecord {
			public readonly string Shader;
			public readonly int Variant;
			public readonly string Id;
			public VariantRecord(string shader, int variant, string id) {
				Shader = shader;
				Variant = variant;
				Id = id;
			}
		}

		private readonly struct LedgerBundle {
			public readonly SpatialVariant[] Spatial;
			public readonly CompositingVariant[] Compositing;
			public readonly AnalysisVariant[] Analysis;
			public LedgerBundle(SpatialVariant[] spatial, CompositingVariant[] compositing, AnalysisVariant[] analysis) {
				Spatial = spatial;
				Compositing = compositing;
				Analysis = analysis;
			}
		}

		[Serializable] private sealed class SpatialLedger { public SpatialVariant[] variants; }
		[Serializable] private sealed class CompositingLedger { public CompositingVariant[] variants; }
		[Serializable] private sealed class AnalysisLedger { public AnalysisVariant[] variants; }
		[Serializable] private sealed class SpatialVariant { public string nodeTypeId; public string shader; public int variant; public string priority; }
		[Serializable] private sealed class CompositingVariant { public string id; public string shader; public int variant; }
		[Serializable] private sealed class AnalysisVariant { public string id; public string shader; public int variant; }
	}
}
