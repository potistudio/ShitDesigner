using System;
using System.IO;
using NUnit.Framework;
using ShitDesigner.Rendering.VJ;
using ShitDesigner.Rendering.VJ.Temporal;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace ShitDesigner.Rendering.Tests.VJ
{
    public sealed class CompositingTemporalContractTests
    {
        [Serializable]
        private sealed class ManifestDto
        {
            public ManifestVariantDto[] variants;
        }

        [Serializable]
        private sealed class ManifestVariantDto
        {
            public string id;
            public string family;
            public int variant;
            public bool stateful;
        }

        [Test, Category("VJShaderManifest"), Category("Blend"), Category("Transition"), Category("Temporal")]
        public void CompositingTemporalManifestContainsExactly104StableVariants()
        {
            var path = Path.Combine(Application.dataPath, "ShitDesigner/Shaders/Manifests/compositing-temporal-variants.json");
            Assert.That(File.Exists(path), Is.True, path);
            var dto = JsonUtility.FromJson<ManifestDto>(File.ReadAllText(path));
            Assert.That(dto, Is.Not.Null);
            Assert.That(dto.variants, Is.Not.Null);
            Assert.That(dto.variants.Length, Is.EqualTo(VJVariantCatalog.TotalCount));

            var blendCount = 0;
            var transitionCount = 0;
            var temporalCount = 0;
            var ids = new System.Collections.Generic.HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in dto.variants)
            {
                Assert.That(ids.Add(entry.id), Is.True, "Duplicate variant id: " + entry.id);
                if (entry.family == "Blend") blendCount++;
                else if (entry.family == "Transition") transitionCount++;
                else if (entry.family == "Temporal")
                {
                    temporalCount++;
                    Assert.That(entry.stateful, Is.True, entry.id);
                }
                else Assert.Fail("Unknown VJ family: " + entry.family);
            }
            Assert.That(blendCount, Is.EqualTo(VJVariantCatalog.BlendCount));
            Assert.That(transitionCount, Is.EqualTo(VJVariantCatalog.TransitionCount));
            Assert.That(temporalCount, Is.EqualTo(VJVariantCatalog.TemporalCount));
            for (var family = 0; family < 3; family++)
                for (var variant = 0; variant < (family == 0 ? VJVariantCatalog.BlendCount : family == 1 ? VJVariantCatalog.TransitionCount : VJVariantCatalog.TemporalCount); variant++)
                    Assert.That(ids.Contains(VJVariantCatalog.StableId(family, variant)), Is.True, VJVariantCatalog.StableId(family, variant));
        }

        [Test, Category("VJShaderContract"), Category("Blend")]
        public void BlendCpuReferencesAreFiniteAndHaveExactAmountEndpoints()
        {
            var a = new Vector4(0.17f, 0.41f, 0.73f, 0.62f);
            var b = new Vector4(0.81f, 0.23f, 0.37f, 0.54f);
            for (var variant = 0; variant < VJVariantCatalog.BlendCount; variant++)
            {
                var start = VJBlendReference.Evaluate(variant, a, b, 0f);
                var end = VJBlendReference.Evaluate(variant, a, b, 1f);
                AssertVectorClose(a, start, 1f / 1024f, "blend start " + variant);
                AssertFinite(end, "blend end " + variant);
            }

            var multiply = VJBlendReference.Evaluate(7, a, b, 1f);
            Assert.That(multiply.x, Is.EqualTo(a.x * b.x).Within(1f / 1024f));
            Assert.That(multiply.y, Is.EqualTo(a.y * b.y).Within(1f / 1024f));
            Assert.That(multiply.z, Is.EqualTo(a.z * b.z).Within(1f / 1024f));
        }

        [Test, Category("VJShaderContract"), Category("Transition")]
        public void TransitionCpuReferencesReturnExactAAndBEndpointsForEveryVariant()
        {
            var a = new Vector4(0.1f, 0.2f, 0.3f, 1f);
            var b = new Vector4(0.8f, 0.7f, 0.6f, 1f);
            for (var variant = 0; variant < VJVariantCatalog.TransitionCount; variant++)
            {
                AssertVectorClose(a, VJTransitionReference.Evaluate(variant, a, b, 0f), 0f, "transition start " + variant);
                AssertVectorClose(b, VJTransitionReference.Evaluate(variant, a, b, 1f), 0f, "transition end " + variant);
                var first = VJTransitionReference.Evaluate(variant, a, b, 0.37f, 0.03f, new Vector2(0.31f, 0.72f), 11);
                var second = VJTransitionReference.Evaluate(variant, a, b, 0.37f, 0.03f, new Vector2(0.31f, 0.72f), 11);
                AssertVectorClose(first, second, 0f, "transition deterministic " + variant);
                AssertFinite(first, "transition finite " + variant);
            }
        }

        [Test, Category("VJShaderImport"), Category("D3D12"), Category("Vulkan")]
        public void FamilyShadersAreImportedWithAUsablePass()
        {
            foreach (var shaderName in new[]
                     {
                         "Hidden/ShitDesigner/VJ/BlendFamily",
                         "Hidden/ShitDesigner/VJ/TransitionFamily",
                         "Hidden/ShitDesigner/VJ/TemporalFamily"
                     })
            {
                var shader = Shader.Find(shaderName);
                Assert.That(shader, Is.Not.Null, shaderName + " was not imported.");
                Assert.That(shader.passCount, Is.GreaterThan(0), shaderName + " has no pass.");
                var material = new Material(shader);
                Assert.That(material.shader, Is.SameAs(shader));
                UnityEngine.Object.DestroyImmediate(material);
            }
        }

        [Test, Category("VJShaderContract"), Category("Temporal"), Category("History")]
        public void TemporalHistoryLifecycleResetsResizesPausesAndReleasesLeases()
        {
            var descriptor = new TemporalHistoryDescriptor(320, 180, GraphicsFormat.R16G16B16A16_SFloat);
            using (var service = new TemporalHistoryService())
            {
                Assert.That(service.Ensure("vj-temporal-test", descriptor, 3, 1), Is.True);
                Assert.That(service.TryAcquire("vj-temporal-test", out var lease), Is.True);
                Assert.That(service.ActiveLeaseCount, Is.EqualTo(1));
                Assert.That(service.BeginFrame("vj-temporal-test", 1, 0.5d, false), Is.True);
                Assert.That(service.Commit("vj-temporal-test", 1), Is.True);
                Assert.That(service.TryGetSnapshot("vj-temporal-test", out var first), Is.True);
                Assert.That(first.IsValid, Is.True);
                Assert.That(first.HistorySlotCount, Is.EqualTo(3));

                var beforePause = first;
                for (var frame = 2UL; frame < 102UL; frame++)
                {
                    Assert.That(service.BeginFrame("vj-temporal-test", frame, 100d + frame, true), Is.True);
                    Assert.That(service.Commit("vj-temporal-test", frame), Is.True);
                }
                Assert.That(service.TryGetSnapshot("vj-temporal-test", out var afterPause), Is.True);
                Assert.That(afterPause.LastFrame, Is.EqualTo(beforePause.LastFrame));
                Assert.That(afterPause.GraphTime, Is.EqualTo(beforePause.GraphTime));
                Assert.That(afterPause.ReadSlot, Is.EqualTo(beforePause.ReadSlot));
                Assert.That(afterPause.IsPaused, Is.True);

                var generation = afterPause.Generation;
                Assert.That(service.Reset("vj-temporal-test", 102), Is.True);
                Assert.That(service.TryGetSnapshot("vj-temporal-test", out var reset), Is.True);
                Assert.That(reset.IsValid, Is.False);
                Assert.That(reset.Generation, Is.GreaterThan(generation));
                Assert.That(service.Resize("vj-temporal-test", new TemporalHistoryDescriptor(640, 360, GraphicsFormat.R16G16B16A16_SFloat), 2, 103), Is.True);
                Assert.That(service.TryGetSnapshot("vj-temporal-test", out var resized), Is.True);
                Assert.That(resized.Descriptor.Width, Is.EqualTo(640));
                Assert.That(resized.HistorySlotCount, Is.EqualTo(2));
                lease.Dispose();
                Assert.That(service.ActiveLeaseCount, Is.EqualTo(0));
                Assert.That(service.Release("vj-temporal-test"), Is.True);
                Assert.That(service.HistoryCount, Is.EqualTo(0));
            }
        }

        [Test, Category("VJShaderContract"), Category("Temporal"), Category("GraphClock")]
        public void PausedGraphClockDoesNotAdvanceFor100Updates()
        {
            var source = new ManualMonotonicSource();
            var clock = new GraphClock(source);
            clock.Update(0d);
            clock.Update(1d);
            var before = clock.Time;
            clock.Pause();
            for (var i = 0; i < 100; i++) clock.Update(10d + i);
            Assert.That(clock.Time, Is.EqualTo(before));
            Assert.That(clock.LastDelta, Is.EqualTo(0d));
            Assert.That(clock.IsPaused, Is.True);
        }

        private static void AssertVectorClose(Vector4 expected, Vector4 actual, float tolerance, string label)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance), label + " x");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance), label + " y");
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance), label + " z");
            Assert.That(actual.w, Is.EqualTo(expected.w).Within(tolerance), label + " w");
        }

        private static void AssertFinite(Vector4 value, string label)
        {
            Assert.That(float.IsNaN(value.x) || float.IsInfinity(value.x), Is.False, label + " x");
            Assert.That(float.IsNaN(value.y) || float.IsInfinity(value.y), Is.False, label + " y");
            Assert.That(float.IsNaN(value.z) || float.IsInfinity(value.z), Is.False, label + " z");
            Assert.That(float.IsNaN(value.w) || float.IsInfinity(value.w), Is.False, label + " w");
        }

        private sealed class ManualMonotonicSource : IMonotonicClock
        {
            public double Now { get; set; }
        }
    }
}
