using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Nodes;
using ShitDesigner.Runtime;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace ShitDesigner.Rendering.Tests.VJ {
	public sealed class BitonicPixelSortRuntimeNodeTests {
		private const string ComputeShaderPath = "Assets/ShitDesigner/Scripts/Modules/Media/Shaders/BitonicPixelSorter.compute";

		[Test]
		public void HorizontalSort_OrdersFullThresholdSpanAndReleasesWorkLease() {
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || !SystemInfo.supportsComputeShaders)
				Assert.Ignore("A compute-capable graphics device is required for the Pixel Sort probe.");
			if (!SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormatUsage.LoadStore))
				Assert.Ignore("The graphics device does not support R8G8B8A8 random writes.");

			var shader = AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeShaderPath);
			Assert.That(shader, Is.Not.Null);
			var source = new Texture2D(4, 1, TextureFormat.RGBA32, false, true);
			var target = new RenderTexture(new RenderTextureDescriptor(4, 1, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None));
			target.Create();
			using (var pool = new RenderTexturePool(16L * 1024L * 1024L)) {
				var record = new RuntimeNodeCreateInfo(new NodeInstanceId("pixel-sort-gpu-test"),
					new NodeTypeId(BitonicPixelSortContract.NodeTypeId), 1, "Pixel Sort", true, 0, 0);
				var node = new BitonicPixelSortRuntimeNode(record, 1, shader, pool, "pixel-sort-test-session");
				try {
					source.SetPixels(new[] { Gray(.8f), Gray(.2f), Gray(.6f), Gray(.4f) });
					source.Apply(false, false);
					var result = node.Render(source, target, 1, horizontal: true, ascending: true,
						thresholdMin: 0f, thresholdMax: 1f);
					Assert.That(result.IsSuccess, Is.True, result.IsFailure ? result.Error.Message : string.Empty);
					Assert.That(Read(target), Is.EqualTo(new[] { .2f, .4f, .6f, .8f }).Within(.01f));
					Assert.That(pool.LeasedBytes, Is.EqualTo(0));
				}
				finally {
					node.Dispose();
				}
			}
			if (RenderTexture.active == target) RenderTexture.active = null;
			target.Release();
			Object.DestroyImmediate(target);
			Object.DestroyImmediate(source);
		}

		private static Color Gray(float value) => new Color(value, value, value, 1f);

		private static float[] Read(RenderTexture texture) {
			var previous = RenderTexture.active;
			var readback = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false, true);
			try {
				RenderTexture.active = texture;
				readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
				readback.Apply(false, false);
				var pixels = readback.GetPixels();
				var result = new float[pixels.Length];
				for (var index = 0; index < pixels.Length; index++) result[index] = pixels[index].r;
				return result;
			}
			finally {
				RenderTexture.active = previous;
				Object.DestroyImmediate(readback);
			}
		}
	}
}
