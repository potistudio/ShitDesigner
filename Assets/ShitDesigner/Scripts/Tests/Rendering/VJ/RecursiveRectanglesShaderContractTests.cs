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
		public IEnumerator RecursiveRectangles_RevealZeroIsTransparent() {
			RequireGraphicsDevice();
			var material = CreateMaterial();
			try {
				Configure(material);
				material.SetFloat("_RevealProgress", 0f);
				material.SetFloat("_Gutter", 0f);

				Color32[] pixels = null;
				yield return Render(material, 32, 32, result => pixels = result);

				Assert.That(pixels.All(pixel => pixel.a == 0), Is.True);
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

		[UnityTest]
		public IEnumerator RecursiveRectangles_UsesSpecifiedFillColorOrTransparency() {
			RequireGraphicsDevice();
			var material = CreateMaterial();
			try {
				Configure(material);
				material.SetVector("_ColorA", new Vector4(.8f, .4f, .2f, .5f));
				material.SetFloat("_Gutter", 0f);

				Color32[] pixels = null;
				yield return Render(material, 48, 32, result => pixels = result);

				Assert.That(pixels.Any(pixel => pixel.a == 0), Is.True);
				Assert.That(pixels.Any(pixel => pixel.a > 0), Is.True);
				foreach (var pixel in pixels) {
					if (pixel.a == 0) {
						Assert.That(pixel, Is.EqualTo(new Color32(0, 0, 0, 0)));
						continue;
					}
					Assert.That(pixel.r, Is.EqualTo(102).Within(1));
					Assert.That(pixel.g, Is.EqualTo(51).Within(1));
					Assert.That(pixel.b, Is.EqualTo(26).Within(1));
					Assert.That(pixel.a, Is.EqualTo(128).Within(1));
				}
			}
			finally { UnityEngine.Object.DestroyImmediate(material); }
		}

		[UnityTest]
		public IEnumerator RecursiveRectangles_BeatPhaseControlsRevealWhenClockIsAvailable() {
			RequireGraphicsDevice();
			var material = CreateMaterial();
			try {
				Configure(material);
				material.SetFloat("_BeatSync", 1f);
				material.SetFloat("_SD_HasBeatClock", 1f);
				material.SetFloat("_Gutter", 0f);
				material.SetFloat("_SD_BeatPhase", 0f);
				Color32[] beatStart = null;
				yield return Render(material, 48, 32, result => beatStart = result);

				material.SetFloat("_SD_BeatPhase", .999f);
				Color32[] beatEnd = null;
				yield return Render(material, 48, 32, result => beatEnd = result);

				Assert.That(beatStart.Distinct().Count(), Is.EqualTo(1));
				Assert.That(beatEnd.Distinct().Count(), Is.GreaterThan(1));
			}
			finally { UnityEngine.Object.DestroyImmediate(material); }
		}

		[UnityTest]
		public IEnumerator RecursiveRectangles_SplitDurationControlsBeatSynchronizedSpeed() {
			RequireGraphicsDevice();
			var material = CreateMaterial();
			try {
				Configure(material);
				material.SetInt("_StructureSeed", 5);
				material.SetInt("_MaxDepth", 1);
				material.SetInt("_AxisMode", 2);
				material.SetFloat("_RatioMin", .5f);
				material.SetFloat("_RatioMax", .5f);
				material.SetFloat("_Gutter", 0f);
				material.SetFloat("_BeatSync", 1f);
				material.SetFloat("_SD_HasBeatClock", 1f);
				material.SetFloat("_SD_BeatDuration", 1f);
				material.SetFloat("_SD_BeatPhase", .5f);

				material.SetFloat("_SplitDuration", .25f);
				Color32[] fast = null;
				yield return Render(material, 64, 8, result => fast = result);

				material.SetFloat("_SplitDuration", 1f);
				Color32[] slow = null;
				yield return Render(material, 64, 8, result => slow = result);

				Assert.That(fast.Count(pixel => pixel.a > 0), Is.GreaterThan(slow.Count(pixel => pixel.a > 0)));
			}
			finally { UnityEngine.Object.DestroyImmediate(material); }
		}

		[UnityTest]
		public IEnumerator RecursiveRectangles_ZeroStaggerStartsDepthsTogether() {
			RequireGraphicsDevice();
			var material = CreateMaterial();
			try {
				Configure(material);
				material.SetInt("_StructureSeed", 5);
				material.SetInt("_MaxDepth", 2);
				material.SetInt("_AxisMode", 2);
				material.SetFloat("_RatioMin", .5f);
				material.SetFloat("_RatioMax", .5f);
				material.SetFloat("_Gutter", .04f);
				material.SetColor("_ColorA", Color.clear);
				material.SetColor("_LineColor", Color.white);
				material.SetFloat("_Easing", 0f);
				material.SetFloat("_SplitDuration", 1f);
				material.SetFloat("_SplitStagger", 0f);
				material.SetFloat("_RevealProgress", .5f);

				Color32[] pixels = null;
				yield return Render(material, 64, 8, result => pixels = result);

				var row = 4 * 64;
				Assert.That(pixels[row + 15].a, Is.GreaterThan(0));
				Assert.That(pixels[row + 47].a, Is.GreaterThan(0));
			}
			finally { UnityEngine.Object.DestroyImmediate(material); }
		}

		[UnityTest]
		public IEnumerator RecursiveRectangles_SquareChildrenAlwaysRevealLeftToRight() {
			RequireGraphicsDevice();
			var material = CreateMaterial();
			try {
				Configure(material);
				material.SetInt("_StructureSeed", 5);
				material.SetInt("_MaxDepth", 1);
				material.SetFloat("_Gutter", 0f);
				material.SetFloat("_RevealProgress", 0f);
				Color32[] root = null;
				yield return Render(material, 64, 8, result => root = result);

				material.SetFloat("_RevealProgress", .5f);
				Color32[] halfway = null;
				yield return Render(material, 64, 8, result => halfway = result);

				var row = 4 * 64;
				Assert.That(halfway[row + 8], Is.Not.EqualTo(root[row + 8]));
				Assert.That(halfway[row + 24], Is.EqualTo(root[row + 24]));

				material.SetFloat("_RevealProgress", 1f);
				Color32[] completed = null;
				yield return Render(material, 64, 8, result => completed = result);
				Assert.That(completed.Count(pixel => pixel.a > 0), Is.EqualTo(completed.Length / 4));
			}
			finally { UnityEngine.Object.DestroyImmediate(material); }
		}

		[UnityTest]
		public IEnumerator RecursiveRectangles_SplitProbabilityProducesMixedSquareSizes() {
			RequireGraphicsDevice();
			var material = CreateMaterial();
			try {
				Configure(material);
				material.SetInt("_StructureSeed", 193);
				material.SetInt("_MaxDepth", 2);
				material.SetFloat("_SplitProbability", .5f);
				material.SetFloat("_Gutter", .03f);
				material.SetColor("_ColorA", Color.clear);
				material.SetColor("_LineColor", Color.white);
				material.SetFloat("_SplitStagger", 0f);
				material.SetFloat("_RevealProgress", 1f);

				Color32[] pixels = null;
				yield return Render(material, 64, 64, result => pixels = result);

				Assert.That(pixels[8 * 64 + 15].a, Is.GreaterThan(0));
				Assert.That(pixels[56 * 64 + 47].a, Is.EqualTo(0));
			}
			finally { UnityEngine.Object.DestroyImmediate(material); }
		}

		[UnityTest]
		public IEnumerator RecursiveRectangles_BeatIndexChangesOnlySynchronizedStructure() {
			RequireGraphicsDevice();
			var material = CreateMaterial();
			try {
				Configure(material);
				material.SetFloat("_BeatSync", 1f);
				material.SetFloat("_SD_HasBeatClock", 1f);
				material.SetFloat("_SD_BeatPhase", .999f);
				material.SetFloat("_SD_BeatIndex", 3f);
				Color32[] firstBeat = null;
				yield return Render(material, 48, 32, result => firstBeat = result);
				Color32[] repeatedBeat = null;
				yield return Render(material, 48, 32, result => repeatedBeat = result);

				material.SetFloat("_SD_BeatIndex", 4f);
				Color32[] nextBeat = null;
				yield return Render(material, 48, 32, result => nextBeat = result);

				Assert.That(repeatedBeat, Is.EqualTo(firstBeat));
				Assert.That(nextBeat, Is.Not.EqualTo(firstBeat));

				material.SetFloat("_BeatSync", 0f);
				Color32[] manualAtFourthBeat = null;
				yield return Render(material, 48, 32, result => manualAtFourthBeat = result);
				material.SetFloat("_SD_BeatIndex", 5f);
				Color32[] manualAtFifthBeat = null;
				yield return Render(material, 48, 32, result => manualAtFifthBeat = result);

				Assert.That(manualAtFifthBeat, Is.EqualTo(manualAtFourthBeat));
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
			material.SetFloat("_BeatSync", 0f);
			material.SetFloat("_RevealProgress", 1f);
			material.SetFloat("_SplitDuration", .15f);
			material.SetFloat("_SplitStagger", .04f);
			material.SetInt("_Easing", 1);
			material.SetVector("_ColorA", new Vector4(.05f, .12f, .22f, 1f));
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
