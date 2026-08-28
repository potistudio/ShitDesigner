using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace ShitDesigner.Rendering.Tests.VJ {
	public sealed class RecursiveRectanglesShaderContractTests {
		private const string ShaderPath = "Assets/ShitDesigner/Scripts/Media/Shaders/RecursiveRectangles.shader";

		[UnityTest]
		public IEnumerator RecursiveRectangles_RevealZeroShowsOnlyTheRootRegion() {
			RequireGraphicsDevice();
			var material = CreateMaterial();
			try {
				Configure(material);
				material.SetFloat("_RevealProgress", 0f);
				material.SetFloat("_Gutter", 0f);

				Color32[] pixels = null;
				yield return Render(material, 32, 32, result => pixels = result);

				Assert.That(pixels.Distinct().Count(), Is.EqualTo(1));
			}
			finally { UnityEngine.Object.DestroyImmediate(material); }
		}

		[UnityTest]
		public IEnumerator RecursiveRectangles_ZeroProbabilityMatchesZeroDepth() {
			RequireGraphicsDevice();
			var material = CreateMaterial();
			try {
				Configure(material);
				material.SetFloat("_SplitProbability", 0f);
				Color32[] zeroProbability = null;
				yield return Render(material, 32, 32, result => zeroProbability = result);

				material.SetFloat("_SplitProbability", 1f);
				material.SetInt("_MaxDepth", 0);
				Color32[] zeroDepth = null;
				yield return Render(material, 32, 32, result => zeroDepth = result);

				Assert.That(zeroProbability, Is.EqualTo(zeroDepth));
			}
			finally { UnityEngine.Object.DestroyImmediate(material); }
		}

		[UnityTest]
		public IEnumerator RecursiveRectangles_SameSeedIsDeterministicAndCompletedEasingDoesNotChangeStructure() {
			RequireGraphicsDevice();
			var material = CreateMaterial();
			try {
				Configure(material);
				material.SetInt("_Easing", 0);
				Color32[] first = null;
				Color32[] second = null;
				yield return Render(material, 48, 32, result => first = result);
				yield return Render(material, 48, 32, result => second = result);

				material.SetInt("_Easing", 4);
				Color32[] alternateEasing = null;
				yield return Render(material, 48, 32, result => alternateEasing = result);

				Assert.That(second, Is.EqualTo(first));
				Assert.That(alternateEasing, Is.EqualTo(first));
			}
			finally { UnityEngine.Object.DestroyImmediate(material); }
		}

		[UnityTest]
		public IEnumerator RecursiveRectangles_OutputIsPremultiplied() {
			RequireGraphicsDevice();
			var material = CreateMaterial();
			try {
				Configure(material);
				material.SetVector("_ColorA", new Vector4(1f, 0f, 0f, .5f));
				material.SetVector("_ColorB", new Vector4(0f, 1f, 0f, .5f));
				material.SetVector("_LineColor", new Vector4(0f, 0f, 1f, .5f));

				Color32[] pixels = null;
				yield return Render(material, 32, 32, result => pixels = result);
				foreach (var pixel in pixels) {
					Assert.That(pixel.r, Is.LessThanOrEqualTo(pixel.a + 1));
					Assert.That(pixel.g, Is.LessThanOrEqualTo(pixel.a + 1));
					Assert.That(pixel.b, Is.LessThanOrEqualTo(pixel.a + 1));
				}
			}
			finally { UnityEngine.Object.DestroyImmediate(material); }
		}

		private static void RequireGraphicsDevice() {
			if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
				Assert.Ignore("A GPU graphics device is required for the recursive rectangle render probe.");
		}

		private static Material CreateMaterial() {
			var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
			Assert.That(shader, Is.Not.Null);
			Assert.That(shader.isSupported, Is.True);
			return new Material(shader);
		}

		private static void Configure(Material material) {
			material.SetInt("_MaxDepth", 5);
			material.SetFloat("_MinLeafSize", .04f);
			material.SetFloat("_SplitProbability", 1f);
			material.SetInt("_AxisMode", 3);
			material.SetFloat("_RatioMin", .25f);
			material.SetFloat("_RatioMax", .75f);
			material.SetInt("_StructureSeed", 193);
			material.SetFloat("_RevealProgress", 1f);
			material.SetFloat("_SplitDuration", .15f);
			material.SetFloat("_SplitStagger", .04f);
			material.SetInt("_Easing", 1);
			material.SetVector("_ColorA", new Vector4(.05f, .12f, .22f, 1f));
			material.SetVector("_ColorB", new Vector4(.95f, .32f, .14f, 1f));
			material.SetFloat("_Gutter", .004f);
			material.SetVector("_LineColor", new Vector4(.01f, .01f, .01f, 1f));
		}

		private static IEnumerator Render(Material material, int width, int height, Action<Color32[]> completed) {
			var descriptor = new RenderTextureDescriptor(width, height, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.None) {
				msaaSamples = 1,
				sRGB = false,
				useMipMap = false,
				autoGenerateMips = false
			};
			var target = new RenderTexture(descriptor);
			var readback = new Texture2D(width, height, TextureFormat.RGBA32, false, true);
			var previous = RenderTexture.active;
			try {
				target.Create();
				Graphics.Blit(Texture2D.blackTexture, target, material);
				yield return null;
				RenderTexture.active = target;
				readback.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				readback.Apply(false, false);
				completed(readback.GetPixels32());
			}
			finally {
				RenderTexture.active = previous;
				UnityEngine.Object.DestroyImmediate(readback);
				UnityEngine.Object.DestroyImmediate(target);
			}
		}
	}
}
