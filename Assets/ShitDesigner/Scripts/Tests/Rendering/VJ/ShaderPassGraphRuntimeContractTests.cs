using System;
using System.Collections;
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
	public sealed class ShaderPassGraphRuntimeContractTests {
		private const string ManifestPath = "Assets/ShitDesigner/Scripts/Modules/Nodes/ShaderNodeManifest.asset";

		[Test]
		public void GeneratedGraphMatrix_DeclaresRequiredPassCountsAndHistoryPolicies() {
			var asset = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(ManifestPath);
			Assert.That(asset, Is.Not.Null);
			var entries = asset.BuildRuntimeManifest().Entries.ToList();
			AssertPass(entries, "shitdesigner.shader.generator.gray-scott-simulation", 2, true, 2);
			AssertPass(entries, "shitdesigner.shader.generator.game-of-life", 2, true, 2);
			AssertPass(entries, "shitdesigner.shader.geometry.optical-flow-warp", 2, true, 3);
			AssertPass(entries, "shitdesigner.shader.geometry.datamosh-motion-warp", 2, true, 3);
			AssertPass(entries, "shitdesigner.shader.geometry.fluid-advection-warp", 2, true, 3);
			AssertPass(entries, "shitdesigner.shader.blur.gaussian-blur", 2, false, 0);
			AssertPass(entries, "shitdesigner.shader.blur.bloom", 4, false, 0);
			AssertPass(entries, "shitdesigner.shader.blur.kawase-blur", 4, false, 0);
			AssertPass(entries, "shitdesigner.shader.blur.custom-kernel-3x3-5x5", 2, false, 0);
			AssertPass(entries, "shitdesigner.shader.temporal.optical_flow_visualizer", 2, true, 3);
			AssertPass(entries, "shitdesigner.shader.temporal.frame_interpolation", 3, true, 3);
			AssertPass(entries, "shitdesigner.shader.temporal.multi_buffer_cellular_simulation", 2, true, 3);
			AssertPass(entries, "shitdesigner.shader.audio.audio_fluid", 2, true, 2);
			Assert.That(entries.Where(x => x.Passes.Count > 1).All(x => x.Passes.Any(p => p.Id.StartsWith("G_", StringComparison.Ordinal))), Is.True);
		}

		[Test]
		public void EveryGeneratedGraphPassIndexFitsItsDirectFamilyShader() {
			var asset = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(ManifestPath);
			Assert.That(asset, Is.Not.Null);
			var graphs = asset.BuildRuntimeManifest().Entries.Where(x => x.Passes.Count > 1).ToList();
			Assert.That(graphs.Count, Is.EqualTo(75), "72 hard graph entries plus three conditional graph entries are expected.");
			foreach (var entry in graphs) {
				var record = asset.Find(entry.TypeId.Value);
				Assert.That(record, Is.Not.Null, entry.TypeId.Value);
				Assert.That(record.Shader, Is.Not.Null, entry.TypeId.Value);
				Assert.That(entry.Passes.Max(x => x.Index), Is.LessThan(record.Shader.passCount), entry.TypeId.Value);
				Assert.That(entry.Passes.Select(x => x.Index).Distinct().Count(), Is.EqualTo(entry.Passes.Count), entry.TypeId.Value);
			}
		}

		[UnityTest]
		public IEnumerator StatefulGraph_UsesRealHistoryRing_PauseResetResizeAndDisposeReleaseAllLeases() {
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("A graphics device is required for the RenderTexture history probe.");
			var asset = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(ManifestPath);
			var entry = asset.BuildRuntimeManifest().Find("shitdesigner.shader.temporal.optical_flow_visualizer");
			Assert.That(entry, Is.Not.Null);
			var binding = new ShaderMaterialBinding(entry.ShaderKey, asset.Find(entry.TypeId.Value).Shader, descriptor: entry.ToShaderBinding());
			using (var pool = new RenderTexturePool(32L * 1024L * 1024L)) {
				var record = new RuntimeNodeCreateInfo(new NodeInstanceId("shader-pass-graph-test"), entry.TypeId, 1,
					entry.DisplayName, true, 0f, 0f);
				var node = new ShaderPassGraphRuntimeNode(record, 1, binding, pool, "shader-pass-graph-session");
				var source = NewTexture(16, 16);
				var target = NewTexture(16, 16);
				try {
					var first = node.Render(source, target, 1, .25d, false);
					Assert.That(first.IsSuccess, Is.True, first.IsFailure ? first.Error.Message : string.Empty);
					Assert.That(node.LastPassCount, Is.EqualTo(2));
					Assert.That(node.ActiveTemporaryLeaseCount, Is.EqualTo(0));
					Assert.That(node.LastExecutedPassIndices, Is.EqualTo(new[] { 0, 1 }));
					Assert.That(node.LastPassInputTextures.Count, Is.EqualTo(2));
					Assert.That(node.LastPassInputTextures.Distinct().Count(), Is.EqualTo(2));
					Assert.That(node.HistoryService.PoolLeaseCount, Is.EqualTo(3));
					Assert.That(node.HistoryService.TryGetSnapshot(record.Id.Value + "." + entry.TypeId.Value, out var beforePause), Is.True);
					Assert.That(beforePause.IsValid, Is.True);
					Assert.That(beforePause.HistoryTextures.Count, Is.EqualTo(3));

					for (var frame = 0; frame < 100; frame++) {
						var paused = node.Render(source, target, 2, .25d, true);
						Assert.That(paused.IsSuccess, Is.True);
					}
					Assert.That(node.HistoryService.TryGetSnapshot(record.Id.Value + "." + entry.TypeId.Value, out var afterPause), Is.True);
					Assert.That(afterPause.LastFrame, Is.EqualTo(beforePause.LastFrame));
					Assert.That(afterPause.ReadSlot, Is.EqualTo(beforePause.ReadSlot));
					Assert.That(afterPause.GraphTime, Is.EqualTo(beforePause.GraphTime).Within(.000001));

					Assert.That(node.ResetHistory(3).IsSuccess, Is.True);
					Assert.That(node.HistoryService.TryGetSnapshot(record.Id.Value + "." + entry.TypeId.Value, out var reset), Is.True);
					Assert.That(reset.IsValid, Is.False);
					Assert.That(node.Render(source, target, 3, .5d, false).IsSuccess, Is.True);
					Assert.That(node.HistoryService.TryGetSnapshot(record.Id.Value + "." + entry.TypeId.Value, out var afterReset), Is.True);
					Assert.That(afterReset.IsValid, Is.True);

					var resized = NewTexture(8, 8);
					try {
						Assert.That(node.Render(source, resized, 4, .75d, false).IsSuccess, Is.True);
						Assert.That(node.HistoryService.TryGetSnapshot(record.Id.Value + "." + entry.TypeId.Value, out var afterResize), Is.True);
						Assert.That(afterResize.Descriptor.Width, Is.EqualTo(8));
						Assert.That(afterResize.Descriptor.Height, Is.EqualTo(8));
						Assert.That(afterResize.IsValid, Is.True);
					}
					finally { DestroyTexture(resized); }
				}
				finally {
					node.Dispose();
					DestroyTexture(source);
					DestroyTexture(target);
				}
				Assert.That(pool.LeasedBytes, Is.EqualTo(0), "History and temporary leases must be zero after node disposal.");
			}
			yield return null;
		}

		[UnityTest]
		public IEnumerator MultiPassGraph_ExecutesPingPongAndReleasesTemporaryLeases() {
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("A graphics device is required for the RenderTexture pass probe.");
			var asset = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(ManifestPath);
			var entry = asset.BuildRuntimeManifest().Find("shitdesigner.shader.blur.bloom");
			Assert.That(entry, Is.Not.Null);
			var binding = new ShaderMaterialBinding(entry.ShaderKey, asset.Find(entry.TypeId.Value).Shader, descriptor: entry.ToShaderBinding());
			using (var pool = new RenderTexturePool(16L * 1024L * 1024L)) {
				var record = new RuntimeNodeCreateInfo(new NodeInstanceId("shader-multipass-test"), entry.TypeId, 1,
					entry.DisplayName, true, 0f, 0f);
				var node = new ShaderPassGraphRuntimeNode(record, 1, binding, pool, "shader-multipass-session");
				var source = NewTexture(16, 16);
				var target = NewTexture(16, 16);
				try {
					var result = node.Render(source, target, 1);
					Assert.That(result.IsSuccess, Is.True, result.IsFailure ? result.Error.Message : string.Empty);
					Assert.That(node.LastPassCount, Is.EqualTo(4));
					Assert.That(node.LastTemporaryLeaseCount, Is.EqualTo(3));
					Assert.That(node.ActiveTemporaryLeaseCount, Is.EqualTo(0));
					Assert.That(node.LastExecutedPassIndices, Is.EqualTo(new[] { 0, 1, 2, 3 }));
					Assert.That(node.LastPassInputTextures.Count, Is.EqualTo(4));
					Assert.That(node.LastPassInputTextures.Distinct().Count(), Is.EqualTo(4),
						"Each pass must consume the previous ping-pong surface.");
				}
				finally {
					node.Dispose();
					DestroyTexture(source);
					DestroyTexture(target);
				}
				Assert.That(pool.LeasedBytes, Is.EqualTo(0));
			}
			yield return null;
		}

		[UnityTest]
		public IEnumerator GraphFamilies_ExecuteEveryExplicitPassAndReleaseLeases() {
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) Assert.Ignore("A graphics device is required for the family pass probe.");
			var asset = AssetDatabase.LoadAssetAtPath<ShaderNodeManifestAsset>(ManifestPath);
			var entries = asset.BuildRuntimeManifest().Entries;
			var targets = new[]
			{
				entries.Single(x => x.Family == ShaderNodeFamily.Generator && x.SourceVariant == 45),
				entries.Single(x => x.Family == ShaderNodeFamily.Geometry && x.SourceVariant == 39),
				entries.Single(x => x.Family == ShaderNodeFamily.Convolution && x.SourceVariant == 8),
				entries.Single(x => x.Family == ShaderNodeFamily.Stylize && x.SourceVariant == 16),
				entries.Single(x => x.Family == ShaderNodeFamily.Key && x.SourceVariant == 9),
				entries.Single(x => x.Family == ShaderNodeFamily.Temporal && x.SourceVariant == 28),
				entries.Single(x => x.Family == ShaderNodeFamily.Audio && x.SourceVariant == 27),
				entries.Single(x => x.Family == ShaderNodeFamily.Utility && x.SourceVariant == 13)
			};
			Assert.That(targets.Length, Is.EqualTo(8));
			using (var pool = new RenderTexturePool(64L * 1024L * 1024L)) {
				for (var index = 0; index < targets.Length; index++) {
					var entry = targets[index];
					var record = asset.Find(entry.TypeId.Value);
					var binding = new ShaderMaterialBinding(entry.ShaderKey, record.Shader, descriptor: entry.ToShaderBinding());
					var nodeRecord = new RuntimeNodeCreateInfo(new NodeInstanceId("shader-family-pass-" + index), entry.TypeId,
						1, entry.DisplayName, true, 0f, 0f);
					var node = new ShaderPassGraphRuntimeNode(nodeRecord, 1, binding, pool,
						"shader-family-pass-session");
					var source = NewTexture(16, 16);
					var target = NewTexture(16, 16);
					try {
						var result = node.Render(source, target, (ulong)(10 + index), .25d + index * .1d, false);
						Assert.That(result.IsSuccess, Is.True, entry.TypeId.Value);
						Assert.That(node.LastExecutedPassIndices, Is.EqualTo(Enumerable.Range(0, entry.Passes.Count).ToArray()), entry.TypeId.Value);
						Assert.That(node.LastPassInputTextures.Count, Is.EqualTo(entry.Passes.Count), entry.TypeId.Value);
						Assert.That(node.LastPassInputTextures.Distinct().Count(), Is.EqualTo(entry.Passes.Count), entry.TypeId.Value);
						Assert.That(node.ActiveTemporaryLeaseCount, Is.EqualTo(0), entry.TypeId.Value);
					}
					finally {
						node.Dispose();
						DestroyTexture(source);
						DestroyTexture(target);
					}
					Assert.That(pool.LeasedBytes, Is.EqualTo(0), entry.TypeId.Value);
				}
			}
			yield return null;
		}

		private static void AssertPass(System.Collections.Generic.IReadOnlyList<ShaderNodeManifestEntry> entries,
			string typeId, int passCount, bool stateful, int historySlots) {
			var entry = entries.Single(x => x.TypeId.Value == typeId);
			Assert.That(entry.Passes.Count, Is.EqualTo(passCount), typeId);
			Assert.That(entry.Stateful, Is.EqualTo(stateful), typeId);
			Assert.That(entry.HistorySlots, Is.EqualTo(historySlots), typeId);
			Assert.That(entry.Passes.Max(x => x.Index), Is.EqualTo(entry.OutputPass), typeId);
		}

		private static RenderTexture NewTexture(int width, int height) {
			var descriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None) {
				msaaSamples = 1,
				useMipMap = false,
				autoGenerateMips = false,
				sRGB = false
			};
			var texture = new RenderTexture(descriptor) { name = "ShitDesigner.PassGraphTestTexture" };
			texture.Create();
			Graphics.Blit(Texture2D.whiteTexture, texture);
			return texture;
		}

		private static void DestroyTexture(RenderTexture texture) {
			if (RenderTexture.active == texture) RenderTexture.active = null;
			UnityEngine.Object.DestroyImmediate(texture);
		}
	}
}
