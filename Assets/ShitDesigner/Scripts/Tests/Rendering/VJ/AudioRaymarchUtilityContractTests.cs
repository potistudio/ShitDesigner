using System;
using System.IO;
using NUnit.Framework;
using ShitDesigner.Rendering.VJ.Audio;
using ShitDesigner.Rendering.VJ.Raymarch;
using ShitDesigner.Rendering.VJ.Utility;
using UnityEngine;

namespace ShitDesigner.Rendering.Tests.VJ {
	public sealed class AudioRaymarchUtilityContractTests {
		[Serializable]
		private sealed class ManifestDto {
			public ManifestVariantDto[] variants;
		}

		[Serializable]
		private sealed class ManifestVariantDto {
			public string id;
			public string family;
			public int variant;
			public string formalPriority;
			public bool phase1Support;
			public bool stateful;
			public string[] inputs;
			public string testStrategy;
		}

		[Test, Category("VJShaderManifest"), Category("Audio"), Category("Raymarch"), Category("Utility")]
		public void AudioRaymarchUtilityManifestContainsExactly89StableVariants() {
			var path = Path.Combine(Application.dataPath, "ShitDesigner/Shaders/Manifests/audio-raymarch-utility-variants.json");
			Assert.That(File.Exists(path), Is.True, path);
			var manifest = JsonUtility.FromJson<ManifestDto>(File.ReadAllText(path));
			Assert.That(manifest, Is.Not.Null);
			Assert.That(manifest.variants, Is.Not.Null);
			Assert.That(manifest.variants.Length, Is.EqualTo(89));
			var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
			var audioCount = 0;
			var raymarchCount = 0;
			var utilityCount = 0;
			ManifestVariantDto wireframeCubeFractal = null;
			foreach (var entry in manifest.variants) {
				Assert.That(ids.Add(entry.id), Is.True, "Duplicate variant id: " + entry.id);
				if (entry.family == "Audio") audioCount++;
				else if (entry.family == "Raymarch") raymarchCount++;
				else if (entry.family == "Utility") utilityCount++;
				else Assert.Fail("Unexpected analysis family: " + entry.family);
				Assert.That(entry.formalPriority, Is.EqualTo("unclassified"), entry.id);
				Assert.That(entry.phase1Support, Is.EqualTo(entry.family == "Utility" && entry.variant < 12), entry.id);
				Assert.That(entry.stateful, Is.False, entry.id);
				if (entry.id == "audio.wireframe_cube_fractal") wireframeCubeFractal = entry;
			}
			Assert.That(audioCount, Is.EqualTo(AudioVariantCatalog.Count));
			Assert.That(raymarchCount, Is.EqualTo(RaymarchVariantCatalog.Count));
			Assert.That(utilityCount, Is.EqualTo(UtilityVariantCatalog.Count));
			for (var i = 0; i < AudioVariantCatalog.Count; i++) Assert.That(ids.Contains("audio." + AudioVariantCatalog.Names[i]), Is.True);
			for (var i = 0; i < RaymarchVariantCatalog.Count; i++) Assert.That(ids.Contains("raymarch." + RaymarchVariantCatalog.Names[i]), Is.True);
			for (var i = 0; i < UtilityVariantCatalog.Count; i++) Assert.That(ids.Contains("utility." + UtilityVariantCatalog.Names[i]), Is.True);
			Assert.That(wireframeCubeFractal, Is.Not.Null);
			Assert.That(wireframeCubeFractal.inputs, Is.Empty, "The BPM-reactive patch must not require audio textures.");
			Assert.That(wireframeCubeFractal.testStrategy, Is.EqualTo("bpm_clock_fixture"));
		}

		[Test, Category("VJShaderContract"), Category("Audio")]
		public void AudioSyntheticFixturesAreDeterministicFiniteAndInputReactive() {
			const int sampleRate = 48000;
			var silence = AudioAnalysisFixture.Analyze(new float[512], sampleRate, 0.25d, 120f);
			Assert.That(silence.IsFinite(), Is.True);
			Assert.That(silence.Rms, Is.EqualTo(0f).Within(1e-6f));
			Assert.That(silence.Peak, Is.EqualTo(0f).Within(1e-6f));
			Assert.That(silence.Beat, Is.EqualTo(0f).Within(1e-6f));

			var toneSamples = AudioAnalysisFixture.Sine(512, sampleRate, 440f, 0.8f);
			var tone = AudioAnalysisFixture.Analyze(toneSamples, sampleRate, 0.25d, 120f);
			var repeat = AudioAnalysisFixture.Analyze(toneSamples, sampleRate, 0.25d, 120f);
			Assert.That(tone.IsFinite(), Is.True);
			Assert.That(tone.Rms, Is.GreaterThan(0.2f));
			Assert.That(tone.Peak, Is.GreaterThan(0.7f));
			Assert.That(tone.Beat, Is.GreaterThan(0f));
			Assert.That(MaxAbs(tone.Waveform), Is.GreaterThan(0.1f));
			Assert.That(Sum(tone.MelBands), Is.GreaterThan(0f));
			Assert.That(MaxIndex(tone.Fft64), Is.LessThanOrEqualTo(2));
			Assert.That(MaxIndex(tone.Fft128), Is.LessThanOrEqualTo(3));
			Assert.That(MaxIndex(tone.Fft512), Is.InRange(3, 7));
			AssertArrayEqual(tone.Fft512, repeat.Fft512, 1e-6f);
			AssertArrayEqual(tone.Waveform, repeat.Waveform, 1e-6f);
			Assert.That(tone.BpmPhase, Is.EqualTo(0.5f).Within(1e-5f));

			var impulse = AudioAnalysisFixture.Analyze(AudioAnalysisFixture.Impulse(512), sampleRate, 0.25d, 120f);
			var sweep = AudioAnalysisFixture.Analyze(AudioAnalysisFixture.Sweep(512, sampleRate, 80f, 4000f), sampleRate, 0.25d, 120f);
			var noise = AudioAnalysisFixture.Analyze(AudioAnalysisFixture.Noise(512, 1234u), sampleRate, 0.25d, 120f);
			Assert.That(impulse.IsFinite() && sweep.IsFinite() && noise.IsFinite(), Is.True);
			Assert.That(impulse.Peak, Is.GreaterThan(0.9f));
			Assert.That(noise.Rms, Is.GreaterThan(0.1f));
			Assert.That(sweep.Fft512[1] + sweep.Fft512[2] + sweep.Fft512[3], Is.GreaterThan(0f));
		}

		[Test, Category("VJShaderContract"), Category("Raymarch")]
		public void RaymarchFixturesCoverAllVariantsWithFiniteDepthNormalAndStepCap() {
			var settings = new RaymarchSettings(24, 0.002f, 12f);
			for (var variant = 0; variant < RaymarchVariantCatalog.Count; variant++) {
				var distance = RaymarchReference.SignedDistance(variant, new Vector3(0.13f, -0.21f, 0.37f), 0.4f);
				Assert.That(float.IsNaN(distance) || float.IsInfinity(distance), Is.False, "distance " + variant);
				var result = RaymarchReference.Trace(variant, new Vector3(0f, 0f, 3f), Vector3.back, settings, 0.4f);
				Assert.That(result.IsFinite, Is.True, "result " + variant);
				Assert.That(result.Steps, Is.LessThanOrEqualTo(settings.MaxSteps), "step cap " + variant);
			}

			var sphereHit = RaymarchReference.Trace(0, new Vector3(0f, 0f, 3f), Vector3.back, settings);
			Assert.That(sphereHit.Hit, Is.True);
			Assert.That(sphereHit.Distance, Is.EqualTo(2.25f).Within(0.05f));
			var miss = RaymarchReference.Trace(0, new Vector3(0f, 0f, 3f), Vector3.up, settings);
			Assert.That(miss.Hit, Is.False);
			Assert.That(miss.IsFinite, Is.True);
		}

		[Test, Category("VJShaderContract"), Category("Utility")]
		public void UtilityReferencesProduceFiniteAnalysisAndColorTransforms() {
			var pixels = new[]
			{
				new Color(0f, 0f, 0f, 1f), new Color(0.25f, 0.5f, 0.75f, 0.5f),
				new Color(1f, 1f, 1f, 1f), new Color(0.5f, 0.25f, 0.1f, 0.25f)
			};
			var histogram = UtilityReference.Histogram(pixels, 16);
			var total = 0f;
			for (var i = 0; i < histogram.Length; i++) {
				Assert.That(float.IsNaN(histogram[i]) || float.IsInfinity(histogram[i]), Is.False);
				total += histogram[i];
			}
			Assert.That(total, Is.EqualTo(1f).Within(1e-5f));
			Assert.That(UtilityReference.Luma(pixels[2]), Is.EqualTo(1f).Within(1e-5f));
			Assert.That(UtilityReference.IsFinite(UtilityReference.ConvertRec709To2020(pixels[1])), Is.True);
			Assert.That(UtilityReference.ToLinear(UtilityReference.ToSrgb(0.37f)), Is.EqualTo(0.37f).Within(1e-4f));
			var scope = UtilityReference.Vectorscope(pixels[1]);
			Assert.That(float.IsNaN(scope.x) || float.IsInfinity(scope.x) || float.IsNaN(scope.y) || float.IsInfinity(scope.y), Is.False);
		}

		[Test, Category("VJShaderImport"), Category("D3D12"), Category("Vulkan")]
		public void AnalysisFamilyShadersAreImportedWithAUsablePass() {
			foreach (var shaderName in new[]
					 {
						 "Hidden/ShitDesigner/VJ/AudioFamily",
						 "Hidden/ShitDesigner/VJ/RaymarchFamily",
						 "Hidden/ShitDesigner/VJ/UtilityFamily"
					 }) {
				var shader = Shader.Find(shaderName);
				Assert.That(shader, Is.Not.Null, shaderName + " was not imported.");
				Assert.That(shader.passCount, Is.GreaterThan(0), shaderName + " has no pass.");
				var material = new Material(shader);
				Assert.That(material.shader, Is.SameAs(shader));
				UnityEngine.Object.DestroyImmediate(material);
			}
		}

		private static int MaxIndex(float[] values) {
			var index = 0;
			for (var i = 1; i < values.Length / 2; i++) if (values[i] > values[index]) index = i;
			return index;
		}

		private static float MaxAbs(float[] values) {
			var maximum = 0f;
			for (var i = 0; i < values.Length; i++) maximum = Mathf.Max(maximum, Mathf.Abs(values[i]));
			return maximum;
		}

		private static float Sum(float[] values) {
			var total = 0f;
			for (var i = 0; i < values.Length; i++) total += values[i];
			return total;
		}

		private static void AssertArrayEqual(float[] expected, float[] actual, float tolerance) {
			Assert.That(actual, Is.Not.Null);
			Assert.That(actual.Length, Is.EqualTo(expected.Length));
			for (var i = 0; i < expected.Length; i++) Assert.That(actual[i], Is.EqualTo(expected[i]).Within(tolerance), "index " + i);
		}
	}
}
