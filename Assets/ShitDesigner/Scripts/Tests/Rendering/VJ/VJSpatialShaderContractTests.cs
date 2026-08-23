using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ShitDesigner.Rendering.Tests.VJ
{
    public sealed class VJSpatialShaderContractTests
    {
        private const int ExpectedVariantCount = 246;
        private static readonly string[] FamilyShaderNames =
        {
            "Hidden/ShitDesigner/VJ/Generator",
            "Hidden/ShitDesigner/VJ/Color",
            "Hidden/ShitDesigner/VJ/Geometry",
            "Hidden/ShitDesigner/VJ/Glitch",
            "Hidden/ShitDesigner/VJ/Convolution",
            "Hidden/ShitDesigner/VJ/Edge",
            "Hidden/ShitDesigner/VJ/Key"
        };

        [Test]
        [Category("VJSpatial")]
        [Category("Manifest")]
        public void SpatialLedger_ContainsAll246VariantsWithContiguousFamilyIds()
        {
            var path = Path.Combine(Application.dataPath, "ShitDesigner/Shaders/Manifests/spatial-variants.json");
            Assert.That(File.Exists(path), Is.True, "Spatial variant ledger is missing: " + path);

            var ledger = JsonUtility.FromJson<SpatialLedger>(File.ReadAllText(path));
            Assert.That(ledger, Is.Not.Null);
            Assert.That(ledger.schemaVersion, Is.EqualTo(1));
            Assert.That(ledger.variantCount, Is.EqualTo(ExpectedVariantCount));
            Assert.That(ledger.variants, Is.Not.Null);
            Assert.That(ledger.variants.Length, Is.EqualTo(ExpectedVariantCount));

            var ids = new HashSet<string>();
            var shaderNames = new HashSet<string>(FamilyShaderNames);
            foreach (var variant in ledger.variants)
            {
                Assert.That(variant, Is.Not.Null);
                Assert.That(ids.Add(variant.nodeTypeId), Is.True, "Duplicate nodeTypeId: " + variant.nodeTypeId);
                Assert.That(variant.variantId, Is.Not.Empty);
                Assert.That(variant.name, Is.Not.Empty);
                Assert.That(variant.family, Is.Not.Empty);
                Assert.That(shaderNames.Contains(variant.shader), Is.True, variant.name + " has an unknown family shader.");
                Assert.That(variant.variant, Is.GreaterThanOrEqualTo(0));
                Assert.That(variant.inputs, Is.Not.Null);
                Assert.That(variant.outputs, Is.Not.Null);
                Assert.That(variant.parameters, Is.Not.Null);
                Assert.That(variant.testStrategy, Is.Not.Empty);
            }

            foreach (var group in ledger.variants.GroupBy(x => x.family))
            {
                var ordered = group.OrderBy(x => x.variant).ToArray();
                Assert.That(ordered.Select(x => x.variant), Is.EqualTo(Enumerable.Range(0, ordered.Length).ToArray()), group.Key + " variant IDs must be contiguous.");
            }

            var expectedCounts = new Dictionary<string, int>
            {
                { "VJGenerator", 48 },
                { "VJColor", 34 },
                { "VJGeometry", 42 },
                { "VJGlitch", 32 },
                { "VJConvolution", 28 },
                { "VJEdge", 38 },
                { "VJKey", 24 }
            };
            foreach (var expected in expectedCounts)
                Assert.That(ledger.variants.Count(x => x.family == expected.Key), Is.EqualTo(expected.Value), expected.Key);
        }

        [UnityTest]
        [Category("VJSpatial")]
        [Category("Finite")]
        [Category("Deterministic")]
        public IEnumerator SpatialFamilyShaders_RenderFiniteAndDeterministically()
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
                Assert.Ignore("A GPU graphics device is required for the spatial shader render probe.");

            var shaders = new Dictionary<string, Shader>();
            foreach (var shaderName in FamilyShaderNames)
            {
                var shader = Shader.Find(shaderName);
                Assert.That(shader, Is.Not.Null, "Family shader was not imported: " + shaderName);
                Assert.That(shader.isSupported, Is.True, "Family shader is unsupported: " + shaderName);
                Assert.That(shader.passCount, Is.GreaterThan(0), "Family shader has no pass: " + shaderName);
                shaders.Add(shaderName, shader);
            }

            var path = Path.Combine(Application.dataPath, "ShitDesigner/Shaders/Manifests/spatial-variants.json");
            var ledger = JsonUtility.FromJson<SpatialLedger>(File.ReadAllText(path));
            var source = CreateSourceTexture(16, 16);
            var displacement = CreateSourceTexture(16, 16);
            var target = new RenderTexture(16, 16, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
            {
                name = "VJSpatialContractTarget",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            target.Create();

            var materials = new List<Material>();
            try
            {
                foreach (var shader in shaders.Values)
                    materials.Add(new Material(shader) { name = "VJSpatialContractMaterial" });

                foreach (var variant in ledger.variants)
                {
                    var material = materials.First(x => x.shader.name == variant.shader);
                    material.SetTexture("_VJDisplacementTex", displacement);
                    ConfigureMaterial(material, variant.variant);
                    Graphics.Blit(source, target, material);
                    yield return null;
                    var first = ReadPixel(target);
                    AssertFinite(first, variant);
                    if (variant.family == "VJGenerator" && variant.variant == 0)
                    {
                        Assert.That(first.r, Is.EqualTo(1.0f).Within(1.0f / 64.0f), "Solid Color reference R");
                        Assert.That(first.g, Is.EqualTo(0.2f).Within(1.0f / 64.0f), "Solid Color reference G");
                        Assert.That(first.b, Is.EqualTo(0.05f).Within(1.0f / 64.0f), "Solid Color reference B");
                        Assert.That(first.a, Is.EqualTo(1.0f).Within(1.0f / 64.0f), "Solid Color reference A");
                    }

                    Graphics.Blit(source, target, material);
                    yield return null;
                    var second = ReadPixel(target);
                    AssertFinite(second, variant);
                    Assert.That(Vector4.Distance(first, second), Is.LessThan(1.0e-4f), "Non-deterministic output for " + variant.nodeTypeId);
                }
            }
            finally
            {
                foreach (var material in materials)
                    UnityEngine.Object.DestroyImmediate(material);
                UnityEngine.Object.DestroyImmediate(source);
                UnityEngine.Object.DestroyImmediate(displacement);
                if (RenderTexture.active == target) RenderTexture.active = null;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        private static void ConfigureMaterial(Material material, int variant)
        {
            material.SetFloat("_VJVariant", variant);
            material.SetFloat("_VJAmount", 0.45f);
            material.SetFloat("_VJFrequency", 4.0f);
            material.SetFloat("_VJDetail", 4.0f);
            material.SetFloat("_VJSoftness", 0.05f);
            material.SetFloat("_VJThreshold", 0.5f);
            material.SetFloat("_VJGain", 1.0f);
            material.SetFloat("_VJMix", 0.5f);
            material.SetFloat("_VJSpeed", 0.7f);
            material.SetFloat("_VJPhase", 0.13f);
            material.SetFloat("_VJDirection", 1.0f);
            material.SetFloat("_VJAspect", 1.0f);
            material.SetFloat("_VJSeed", 17.0f);
            material.SetFloat("_VJScale", 1.0f);
            material.SetFloat("_VJRadius", 1.0f);
            material.SetFloat("_VJFalloff", 1.0f);
            material.SetFloat("_VJHue", 0.1f);
            material.SetFloat("_VJSaturation", 1.1f);
            material.SetFloat("_VJContrast", 1.1f);
            material.SetFloat("_VJTemperature", 0.1f);
            material.SetFloat("_VJTile", 3.0f);
            material.SetFloat("_VJAngle", 0.37f);
            material.SetVector("_VJCenter", new Vector4(0.5f, 0.5f, 0f, 0f));
            material.SetVector("_VJColorA", new Vector4(1f, 0.2f, 0.05f, 1f));
            material.SetVector("_VJColorB", new Vector4(0.05f, 0.1f, 1f, 1f));
            material.SetVector("_VJColorC", new Vector4(0.05f, 1f, 0.2f, 1f));
            material.SetVector("_VJPivot", new Vector4(0.5f, 0.5f, 0f, 0f));
            material.SetVector("_VJDisplacement", new Vector4(0.02f, 0.015f, 0f, 0f));
            material.SetVector("_SD_Resolution", new Vector4(16f, 16f, 1f / 16f, 1f / 16f));
            material.SetFloat("_SD_Time", 1.25f);
            material.SetFloat("_SD_DeltaTime", 1f / 60f);
            material.SetFloat("_SD_Frame", 42f);
            material.SetFloat("_SD_Seed", 9.0f);
            material.SetFloat("_SD_BeatPhase", 0.25f);
            material.SetFloat("_SD_BeatPulse", 0.75f);
            material.SetFloat("_SD_BarPhase", 0.5f);
            material.SetVector("_SD_Pointer", new Vector4(0.5f, 0.5f, 0f, 0f));
        }

        private static Texture2D CreateSourceTexture(int width, int height)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
            {
                name = "VJSpatialContractSource",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            var pixels = new Color[width * height];
            for (var y = 0; y < height; y++)
                for (var x = 0; x < width; x++)
                {
                    var u = x / (float)(width - 1);
                    var v = y / (float)(height - 1);
                    pixels[y * width + x] = new Color(u, v, 1f - u * 0.5f, 0.35f + v * 0.6f);
                }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            return texture;
        }

        private static Color ReadPixel(RenderTexture target)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = target;
            var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
            readback.ReadPixels(new Rect(7, 7, 1, 1), 0, 0);
            readback.Apply(false, false);
            var pixel = readback.GetPixel(0, 0);
            UnityEngine.Object.DestroyImmediate(readback);
            RenderTexture.active = previous;
            return pixel;
        }

        private static void AssertFinite(Color pixel, SpatialVariant variant)
        {
            Assert.That(float.IsNaN(pixel.r) || float.IsInfinity(pixel.r), Is.False, variant.nodeTypeId);
            Assert.That(float.IsNaN(pixel.g) || float.IsInfinity(pixel.g), Is.False, variant.nodeTypeId);
            Assert.That(float.IsNaN(pixel.b) || float.IsInfinity(pixel.b), Is.False, variant.nodeTypeId);
            Assert.That(float.IsNaN(pixel.a) || float.IsInfinity(pixel.a), Is.False, variant.nodeTypeId);
        }

        [Serializable]
        private sealed class SpatialLedger
        {
            public int schemaVersion;
            public int variantCount;
            public SpatialFamily[] families;
            public SpatialVariant[] variants;
        }

        [Serializable]
        private sealed class SpatialFamily
        {
            public string family;
            public string shader;
            public int count;
        }

        [Serializable]
        private sealed class SpatialVariant
        {
            public string nodeTypeId;
            public string variantId;
            public string name;
            public string category;
            public string family;
            public string shader;
            public int variant;
            public string role;
            public string[] inputs;
            public string[] outputs;
            public string[] parameters;
            public bool stateful;
            public string priority;
            public string testStrategy;
        }
    }
}
