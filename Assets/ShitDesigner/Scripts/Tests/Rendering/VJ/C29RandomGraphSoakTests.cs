using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Nodes;
using ShitDesigner.Rendering;
using ShitDesigner.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace ShitDesigner.Rendering.Tests.VJ {
	/// <summary>
	/// C29 long-running graph lifetime probe. A deterministic PRNG chooses
	/// short chains from the authoritative 438-entry VJ manifest while the
	/// real ShaderPassGraphRuntimeNode owns history and temporary leases.
	/// The test intentionally runs for the full 30 minutes when invoked by
	/// the integration command; it is not a shortened fixture loop.
	/// </summary>
	public sealed class C29RandomGraphSoakTests {
		private const string ManifestPath = "Assets/ShitDesigner/Scripts/Nodes/ShaderNodeManifest.asset";
		private const double SoakSeconds = 1800d;
		private const int Seed = 0xC290438;

		[UnityTest]
		[Category("C29Soak")]
		[Timeout(1900000)]
		public IEnumerator RandomManifestGraphs_Run30MinutesWithoutNaNOrLeaseLeaks() {
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
				Assert.Ignore("C29 requires a real graphics device.");

			var asset = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(ManifestPath);
			Assert.That(asset, Is.Not.Null, "The generated shader manifest asset is required.");
			var entries = asset.BuildRuntimeManifest().Entries
				.Where(x => x != null && x.ShaderKey.StartsWith("Hidden/ShitDesigner/VJ/", StringComparison.Ordinal))
				.ToArray();
			Assert.That(entries.Length, Is.EqualTo(438), "C29 must draw from every generated VJ variant.");
			Assert.That(entries.All(x => asset.Find(x.TypeId.Value)?.Shader != null), Is.True,
				"Every soak entry must have a direct family Shader asset.");

			var random = new System.Random(Seed);
			var stopwatch = Stopwatch.StartNew();
			var frame = 1UL;
			var graphCount = 0;
			var renderCount = 0;
			var finiteProbeCount = 0;
			var lastProgress = -1;

			using (var pool = new RenderTexturePool(256L * 1024L * 1024L)) {
				var readback = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
				try {
					while (stopwatch.Elapsed.TotalSeconds < SoakSeconds) {
						var width = random.NextDouble() < 0.08 ? 8 : 16;
						var height = random.NextDouble() < 0.08 ? 12 : 16;
						var chainLength = 1 + random.Next(4);
						var nodes = new List<ShaderPassGraphRuntimeNode>(chainLength);
						var sources = CreateSurfaces(width, height);
						try {
							for (var index = 0; index < chainLength; index++) {
								var entry = entries[random.Next(entries.Length)];
								var record = asset.Find(entry.TypeId.Value);
								var binding = new ShaderMaterialBinding(entry.ShaderKey, record.Shader,
									descriptor: entry.ToShaderBinding());
								var nodeRecord = new RuntimeNodeCreateInfo(
									new NodeInstanceId("c29-" + graphCount + "-" + index), entry.TypeId,
									1, entry.DisplayName, true, 0f, 0f);
								nodes.Add(new ShaderPassGraphRuntimeNode(nodeRecord, 1, binding, pool,
									"c29-random-graph-soak", generator: entry.Family == ShaderNodeFamily.Generator,
									blend: entry.Family == ShaderNodeFamily.Composite));
							}

							var graphFrames = 4 + random.Next(48);
							for (var localFrame = 0; localFrame < graphFrames && stopwatch.Elapsed.TotalSeconds < SoakSeconds; localFrame++) {
								var current = sources[0];
								for (var index = 0; index < nodes.Count; index++) {
									var target = sources[1 + ((localFrame + index) & 1)];
									var reset = random.NextDouble() < 0.004;
									var paused = random.NextDouble() < 0.025;
									var result = nodes[index].Render(current, target, frame,
										frame * 0.0166666667d, paused, reset);
									Assert.That(result.IsSuccess, Is.True,
										"C29 render failed for " + nodes[index].TypeId.Value + ".");
									Assert.That(nodes[index].ActiveTemporaryLeaseCount, Is.EqualTo(0),
										"Temporary lease remained active after " + nodes[index].TypeId.Value + ".");
									current = target;
									renderCount++;
								}

								if ((renderCount & 31) == 0) {
									var pixel = ReadPixel(current, readback);
									AssertFinite(pixel, "C29 frame " + frame);
									finiteProbeCount++;
								}

								frame++;
								yield return null;
							}
						}
						finally {
							for (var index = nodes.Count - 1; index >= 0; index--) nodes[index].Dispose();
							DestroySurfaces(sources);
						}

						Assert.That(pool.LeasedBytes, Is.EqualTo(0), "C29 graph teardown leaked pool leases.");
						graphCount++;
						var progress = Mathf.FloorToInt((float)(stopwatch.Elapsed.TotalSeconds / 60d));
						if (progress > lastProgress) {
							lastProgress = progress;
							UnityEngine.Debug.Log("C29 soak progress: minutes=" + progress + ", graphs=" + graphCount +
								", renders=" + renderCount + ", finiteProbes=" + finiteProbeCount +
								", poolLeasedBytes=" + pool.LeasedBytes + ".");
						}
					}
				}
				finally { UnityEngine.Object.DestroyImmediate(readback); }
			}

			Assert.That(stopwatch.Elapsed.TotalSeconds, Is.GreaterThanOrEqualTo(SoakSeconds));
			Assert.That(graphCount, Is.GreaterThan(0));
			Assert.That(renderCount, Is.GreaterThan(0));
			Assert.That(finiteProbeCount, Is.GreaterThan(0));
			yield return null;
		}

		private static RenderTexture[] CreateSurfaces(int width, int height) {
			var descriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.R16G16B16A16_SFloat, 0) {
				msaaSamples = 1,
				useMipMap = false,
				autoGenerateMips = false,
				sRGB = false
			};
			var source = new RenderTexture(descriptor) { name = "ShitDesigner.C29.Source" };
			var ping = new RenderTexture(descriptor) { name = "ShitDesigner.C29.Ping" };
			var pong = new RenderTexture(descriptor) { name = "ShitDesigner.C29.Pong" };
			source.Create();
			ping.Create();
			pong.Create();
			Graphics.Blit(Texture2D.whiteTexture, source);
			Graphics.Blit(Texture2D.blackTexture, ping);
			Graphics.Blit(Texture2D.grayTexture, pong);
			return new[] { source, ping, pong };
		}

		private static Vector4 ReadPixel(RenderTexture target, Texture2D readback) {
			var previous = RenderTexture.active;
			try {
				RenderTexture.active = target;
				readback.ReadPixels(new Rect(target.width / 2, target.height / 2, 1, 1), 0, 0);
				readback.Apply(false, false);
				var pixel = readback.GetPixel(0, 0);
				return new Vector4(pixel.r, pixel.g, pixel.b, pixel.a);
			}
			finally { RenderTexture.active = previous; }
		}

		private static void AssertFinite(Vector4 value, string label) {
			Assert.That(float.IsNaN(value.x) || float.IsInfinity(value.x), Is.False, label + " x");
			Assert.That(float.IsNaN(value.y) || float.IsInfinity(value.y), Is.False, label + " y");
			Assert.That(float.IsNaN(value.z) || float.IsInfinity(value.z), Is.False, label + " z");
			Assert.That(float.IsNaN(value.w) || float.IsInfinity(value.w), Is.False, label + " w");
		}

		private static void DestroySurfaces(IReadOnlyList<RenderTexture> surfaces) {
			for (var index = 0; index < surfaces.Count; index++) {
				if (RenderTexture.active == surfaces[index]) RenderTexture.active = null;
				UnityEngine.Object.DestroyImmediate(surfaces[index]);
			}
		}
	}
}
