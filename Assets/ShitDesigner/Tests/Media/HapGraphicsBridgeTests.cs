using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using ShitDesigner.Media;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.TestTools;

namespace ShitDesigner.Tests.Media
{
    public sealed class HapGraphicsBridgeTests
    {
        [Test]
        public void PathSelectionRequiresEveryPlaneAndUsesCpuWhenAPlaneIsUnsupported()
        {
            var frame = Frame(isYCoCg: true, new HapDecodedPlane(2, new byte[16]), new HapDecodedPlane(3, new byte[8]));
            var probe = new FakeProbe(direct: true, compute: false, cpu: true, format => format == 2);
            using (var bridge = new HapUnityGraphicsBridge(probe, null, null, null, null))
                Assert.That(bridge.SelectDecodePath(frame), Is.EqualTo(HapDecodePath.Cpu));
        }

        [Test]
        public void PathSelectionReportsUnsupportedWhenNoFramePathIsAvailable()
        {
            var frame = Frame(isYCoCg: false, new HapDecodedPlane(1, new byte[8]));
            var probe = new FakeProbe(direct: false, compute: false, cpu: false, _ => false);
            using (var bridge = new HapUnityGraphicsBridge(probe, null, null, null, null))
                Assert.That(bridge.SelectDecodePath(frame), Is.EqualTo(HapDecodePath.Unsupported));
        }

        [Test]
        public void DirectCompressedPathDoesNotRequireACpuRgbaCopy()
        {
            var frame = new HapDecodedFrame(4, 4, 1, 0, Array.Empty<byte>(), new[] { new HapDecodedPlane(1, new byte[8]) }, usesCpuFallback: false);
            var probe = new FakeProbe(direct: true, compute: false, cpu: true, _ => true);
            using (var bridge = new HapUnityGraphicsBridge(probe, null, ShaderMaterial("Hidden/ShitDesigner/HapPremultiply"), null, null))
                Assert.That(bridge.SelectDecodePath(frame), Is.EqualTo(HapDecodePath.DirectCompressed));
        }

        [UnityTest, Category("RequiresDirectCompressedGraphics")]
        public IEnumerator NativeDirectUploadReusesTheTextureLeaseAcrossFrames()
        {
            var path = Path.Combine(FixtureRoot(), "hap1.mov");
            var probeResult = new FileVideoMetadataProbe().Probe(path);
            Assert.That(probeResult.IsSuccess && probeResult.Value.Supported, Is.True);
            using (var bridge = new HapUnityGraphicsBridge())
            {
                var api = new PInvokeHapNativeApi(bridge);
                var opened = api.Open(new VideoPrepareRequest(path, probeResult.Value));
                Assert.That(opened.IsSuccess, Is.True, opened.IsFailure ? opened.Diagnostic.Message : string.Empty);
                try
                {
                    var first = api.GetBorrowedTexture(opened.Value) as Texture;
                    Assert.That(first, Is.Not.Null);
                    Assert.That(api.Seek(opened.Value, 1d / 60d).IsSuccess, Is.True);
                    var second = api.GetBorrowedTexture(opened.Value) as Texture;
                    Assert.That(second, Is.SameAs(first), "The direct compressed path updates the existing GPU lease instead of allocating a texture per frame.");
                }
                finally { api.Close(opened.Value); }
            }
            yield return null;
        }

        [Test]
        public void ComputePathIsSelectableOnlyWithTheRealComputeAsset()
        {
            var shader = Resources.Load<ComputeShader>("HapDecode");
            Assert.That(shader, Is.Not.Null, "The checked-in HapDecode.compute fixture must be imported; silently skipping is not allowed.");
            var frame = Frame(isYCoCg: true, new HapDecodedPlane(2, new byte[16]));
            var probe = new FakeProbe(direct: false, compute: true, cpu: true, _ => false);
            using (var bridge = new HapUnityGraphicsBridge(probe, shader, ShaderMaterial("Hidden/ShitDesigner/HapPremultiply"), ShaderMaterial("Hidden/ShitDesigner/HapYToLinear"), ShaderMaterial("Hidden/ShitDesigner/HapAlphaCompose")))
                Assert.That(bridge.SelectDecodePath(frame), Is.EqualTo(HapDecodePath.Compute));
        }

        [Test, Category("RequiresDirectCompressedGraphics")]
        public void DirectCompressedPathReadsAllFourCodecFixturesWithPremultipliedPixels()
        {
            var probe = new FakeProbe(direct: true, compute: false, cpu: false, _ => true);
            using (var bridge = new HapUnityGraphicsBridge(probe, null, ShaderMaterial("Hidden/ShitDesigner/HapPremultiply"), ShaderMaterial("Hidden/ShitDesigner/HapYToLinear"), ShaderMaterial("Hidden/ShitDesigner/HapAlphaCompose")))
            {
                foreach (var codec in new[] { VideoCodec.Hap1, VideoCodec.Hap5, VideoCodec.HapY, VideoCodec.HapM })
                    AssertFixturePixel(bridge, Fixture(codec), tolerance: 8, codec: codec.ToString());
            }
        }

        [Test, Category("RequiresComputeGraphics")]
        public void ComputePathExpandsAllFourCodecFixturesWithPremultipliedPixels()
        {
            Assert.That(SystemInfo.supportsComputeShaders, Is.True, "This test is a required compute-capable Player lane, not a successful skip.");
            var shader = Resources.Load<ComputeShader>("HapDecode");
            Assert.That(shader, Is.Not.Null, "The checked-in HapDecode.compute fixture must be imported.");
            var probe = new FakeProbe(direct: false, compute: true, cpu: false, _ => false);
            using (var bridge = new HapUnityGraphicsBridge(probe, shader, null, null, null))
            {
                foreach (var codec in new[] { VideoCodec.Hap1, VideoCodec.Hap5, VideoCodec.HapY, VideoCodec.HapM })
                    AssertFixturePixel(bridge, Fixture(codec), tolerance: 8, codec: codec.ToString());
            }
        }

        [UnityTest]
        public System.Collections.IEnumerator CpuPathUploadsLinearPremultipliedRgba16FAndReleasesLease()
        {
            var probe = new FakeProbe(direct: false, compute: false, cpu: true, _ => false);
            var frame = new HapDecodedFrame(1, 1, 0, 0, new byte[] { 128, 40, 128, 128 }, Array.Empty<HapDecodedPlane>());
            using (var bridge = new HapUnityGraphicsBridge(probe, null, null, null, null))
            {
                var uploaded = bridge.Upload(frame);
                Assert.That(uploaded.IsSuccess, Is.True, uploaded.IsFailure ? uploaded.Diagnostic.Message : string.Empty);
                using (var lease = uploaded.Value)
                {
                    Assert.That(lease.Path, Is.EqualTo(HapDecodePath.Cpu));
                    Assert.That(lease.Texture.graphicsFormat, Is.EqualTo(GraphicsFormat.R16G16B16A16_SFloat));
                    RenderTexture.active = lease.Texture;
                    var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                    try
                    {
                        readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                        readback.Apply(false, false);
                        var color = readback.GetPixel(0, 0);
                        var pixel = new Color32((byte)Mathf.RoundToInt(color.r * 255f), (byte)Mathf.RoundToInt(color.g * 255f), (byte)Mathf.RoundToInt(color.b * 255f), (byte)Mathf.RoundToInt(color.a * 255f));
                        Assert.That(pixel.a, Is.EqualTo(128).Within(3));
                        Assert.That(pixel.r, Is.LessThanOrEqualTo(pixel.a));
                        Assert.That(pixel.b, Is.LessThanOrEqualTo(pixel.a));
                    }
                    finally
                    {
                        RenderTexture.active = null;
                        UnityEngine.Object.DestroyImmediate(readback);
                    }
                }
                Assert.That(uploaded.Value.IsReleased, Is.True);
            }
            yield return null;
        }

        [UnityTest, Category("RequiresGraphicsAPI")]
        public System.Collections.IEnumerator CpuPathMatchesAllFourCodecFixturePixels()
        {
            var probe = new FakeProbe(direct: false, compute: false, cpu: true, _ => false);
            using (var bridge = new HapUnityGraphicsBridge(probe, null, null, null, null))
            {
                foreach (var codec in new[] { VideoCodec.Hap1, VideoCodec.Hap5, VideoCodec.HapY, VideoCodec.HapM })
                    AssertFixturePixel(bridge, Fixture(codec), tolerance: 3, codec: codec.ToString());
            }
            yield return null;
        }

        private static HapDecodedFrame Frame(bool isYCoCg, params HapDecodedPlane[] planes) => new HapDecodedFrame(4, 4, 0, 0, new byte[4 * 4 * 4], planes, isYCoCg: isYCoCg);

        private static string FixtureRoot() => Path.Combine(Application.dataPath, "ShitDesigner", "Tests", "Media", "Fixtures");

        private static HapDecodedFrame Fixture(VideoCodec codec)
        {
            var path = Path.Combine("Assets", "ShitDesigner", "Tests", "Media", "Fixtures", codec == VideoCodec.Hap1 ? "hap1.mov" : codec == VideoCodec.Hap5 ? "hap5.mov" : codec == VideoCodec.HapY ? "hapy.mov" : "hapm.mov");
            Assert.That(File.Exists(path), Is.True, path + " is a required checked-in fixture.");
            Assert.That(HapMovie.TryOpen(path, out var movie, out var movieError), Is.True, movieError);
            var sample = movie.ReadSample(1);
            Assert.That(HapFrameDecoder.TryDecode(sample.Data, codec, movie.Width, movie.Height, out var decoded, out var decodeError), Is.True, decodeError);
            var planes = new List<HapDecodedPlane> { Plane(decoded.Color) };
            if (decoded.Alpha != null) planes.Add(Plane(decoded.Alpha));
            return new HapDecodedFrame((uint)decoded.Width, (uint)decoded.Height, 1, sample.PresentationTicks, HapColorConversion.ToLinearPremultipliedRgba8(decoded.Rgba8), planes.ToArray(), isYCoCg: codec == VideoCodec.HapY || codec == VideoCodec.HapM);
        }

        private static HapDecodedPlane Plane(HapPlane plane) => new HapDecodedPlane(plane.Format == HapPlaneFormat.Bc1 ? 1u : plane.Format == HapPlaneFormat.Bc3 ? 2u : 3u, plane.CompressedBlocks);

        private static void AssertFixturePixel(HapUnityGraphicsBridge bridge, HapDecodedFrame expected, int tolerance, string codec = "unknown")
        {
            var uploaded = bridge.Upload(expected);
            Assert.That(uploaded.IsSuccess, Is.True, uploaded.IsFailure ? uploaded.Diagnostic.Message : string.Empty);
            using (var lease = uploaded.Value)
            {
                RenderTexture.active = lease.Texture;
                var readback = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
                try
                {
                    readback.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    readback.Apply(false, false);
                    var color = readback.GetPixel(0, 0);
                    var actual = new Color32((byte)Mathf.RoundToInt(color.r * 255f), (byte)Mathf.RoundToInt(color.g * 255f), (byte)Mathf.RoundToInt(color.b * 255f), (byte)Mathf.RoundToInt(color.a * 255f));
                    var rgba = expected.Rgba8PremultipliedLinear;
                    Assert.That(Math.Abs(actual.r - rgba[0]), Is.LessThanOrEqualTo(tolerance), $"{codec} actual={actual.r},{actual.g},{actual.b},{actual.a} expected={rgba[0]},{rgba[1]},{rgba[2]},{rgba[3]}");
                    Assert.That(Math.Abs(actual.g - rgba[1]), Is.LessThanOrEqualTo(tolerance), $"{codec} actual={actual.r},{actual.g},{actual.b},{actual.a} expected={rgba[0]},{rgba[1]},{rgba[2]},{rgba[3]}");
                    Assert.That(Math.Abs(actual.b - rgba[2]), Is.LessThanOrEqualTo(tolerance), $"{codec} actual={actual.r},{actual.g},{actual.b},{actual.a} expected={rgba[0]},{rgba[1]},{rgba[2]},{rgba[3]}");
                    Assert.That(Math.Abs(actual.a - rgba[3]), Is.LessThanOrEqualTo(tolerance), $"{codec} actual={actual.r},{actual.g},{actual.b},{actual.a} expected={rgba[0]},{rgba[1]},{rgba[2]},{rgba[3]}");
                    Assert.That(actual.r, Is.LessThanOrEqualTo(actual.a + tolerance));
                    Assert.That(actual.g, Is.LessThanOrEqualTo(actual.a + tolerance));
                    Assert.That(actual.b, Is.LessThanOrEqualTo(actual.a + tolerance));
                }
                finally
                {
                    RenderTexture.active = null;
                    UnityEngine.Object.DestroyImmediate(readback);
                }
            }
        }
        private static Material ShaderMaterial(string name)
        {
            var shader = Shader.Find(name);
            Assert.That(shader, Is.Not.Null, name + " shader is required for the GPU contract.");
            return new Material(shader);
        }

        private sealed class FakeProbe : IHapGraphicsCapabilityProbe, IHapPlaneGraphicsCapabilityProbe
        {
            private readonly Func<uint, bool> _formats;
            public FakeProbe(bool direct, bool compute, bool cpu, Func<uint, bool> formats) { SupportsDirectCompressed = direct; SupportsCompute = compute; SupportsCpu = cpu; _formats = formats; }
            public bool SupportsDirectCompressed { get; }
            public bool SupportsCompute { get; }
            public bool SupportsCpu { get; }
            public string Diagnostic => "fake-hap-probe";
            public bool SupportsPlane(uint format) => _formats(format);
        }
    }
}
