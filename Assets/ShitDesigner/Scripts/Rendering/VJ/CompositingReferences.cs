using System;
using UnityEngine;

namespace ShitDesigner.Rendering.VJ {
	/// <summary>
	/// Small, deterministic CPU references for the compositing families.
	/// They intentionally use the same straight-alpha convention as the
	/// fullscreen shaders: sampled premultiplied colors are unpremultiplied,
	/// the operation is evaluated, and the result is premultiplied again.
	/// </summary>
	public static class VJBlendReference {
		private const float Epsilon = 1e-5f;

		public static Vector4 Evaluate(int variant, Vector4 a, Vector4 b, float amount = 1f, float externalMask = 1f, float depthA = 0f, float depthB = 1f) {
			if (variant < 0 || variant >= VJVariantCatalog.BlendCount) throw new ArgumentOutOfRangeException(nameof(variant));
			a = Finite(a);
			b = Finite(b);
			amount = Clamp01(amount);
			if (amount <= 0f) return a;

			Vector4 result;
			switch (variant) {
				case 0: result = AlphaOver(a, b); break;
				case 1: result = AlphaOver(a, b); break;
				case 2: result = AlphaOver(b, a); break;
				case 33: result = Lerp(a, b, b.w); break;
				case 34: result = Lerp(a, b, externalMask); break;
				case 35: result = depthB < depthA ? b : a; break;
				default:
					result = Compose(BlendRgb(variant, Clamp01(a), Clamp01(b)), CombinedAlpha(a.w, b.w));
					break;
			}

			if (amount >= 1f) return Finite(result);
			return Finite(Lerp(a, result, amount));
		}

		private static Vector4 AlphaOver(Vector4 foreground, Vector4 background) {
			var alpha = foreground.w + background.w * (1f - foreground.w);
			if (alpha <= Epsilon) return Vector4.zero;
			var rgb = (new Vector3(foreground.x, foreground.y, foreground.z) * foreground.w
				+ new Vector3(background.x, background.y, background.z) * background.w * (1f - foreground.w)) / alpha;
			return new Vector4(rgb.x, rgb.y, rgb.z, Clamp01(alpha));
		}

		private static Vector4 Compose(Vector3 rgb, float alpha) =>
			new Vector4(rgb.x, rgb.y, rgb.z, Clamp01(alpha));

		private static Vector3 BlendRgb(int variant, Vector3 a, Vector3 b) {
			var lumaA = Luma(a);
			var lumaB = Luma(b);
			switch (variant) {
				case 3: return a + b;
				case 4: return Clamp01(a + b);
				case 5: return a - b;
				case 6: return b - a;
				case 7: return Vector3.Scale(a, b);
				case 8: return Vector3.one - Vector3.Scale(Vector3.one - a, Vector3.one - b);
				case 9: return Overlay(a, b);
				case 10: return Overlay(b, a);
				case 11: return SoftLight(a, b);
				case 12: return VividLight(a, b);
				case 13: return b + 2f * a - Vector3.one;
				case 14: return PinLight(a, b);
				case 15: return Step(0.5f, a + b - Vector3.one);
				case 16: return Abs(a - b);
				case 17: return a + b - 2f * Vector3.Scale(a, b);
				case 18: return Vector3.Min(a, b);
				case 19: return Vector3.Max(a, b);
				case 20: return ColorDodge(a, b);
				case 21: return ColorBurn(a, b);
				case 22: return a + b - Vector3.one;
				case 23: return Divide(a, b);
				case 24: return (a + b) * 0.5f;
				case 25: return Vector3.one - Abs(Vector3.one - a - b);
				case 26: return Vector3.one - Abs(a - b);
				case 27: return Reflect(a, b);
				case 28: return Glow(a, b);
				case 29: return SetHue(a, b);
				case 30: return SetSaturation(a, b);
				case 31: return SetColor(a, b);
				case 32: return SetLuminosity(a, b);
				default: return lumaA >= lumaB ? a : b;
			}
		}

		private static Vector3 Overlay(Vector3 a, Vector3 b) => new Vector3(
			a.x < 0.5f ? 2f * a.x * b.x : 1f - 2f * (1f - a.x) * (1f - b.x),
			a.y < 0.5f ? 2f * a.y * b.y : 1f - 2f * (1f - a.y) * (1f - b.y),
			a.z < 0.5f ? 2f * a.z * b.z : 1f - 2f * (1f - a.z) * (1f - b.z));

		private static Vector3 SoftLight(Vector3 a, Vector3 b) {
			return new Vector3(SoftLight(a.x, b.x), SoftLight(a.y, b.y), SoftLight(a.z, b.z));
		}

		private static float SoftLight(float a, float b) {
			var d = b <= 0.25f ? ((16f * b - 12f) * b + 4f) * b : Mathf.Sqrt(b);
			return a <= 0.5f ? b - (1f - 2f * a) * b * (1f - b) : b + (2f * a - 1f) * (d - b);
		}

		private static Vector3 VividLight(Vector3 a, Vector3 b) => new Vector3(
			a.x < 0.5f ? ColorBurn(a.x * 2f, b.x) : ColorDodge(a.x * 2f - 1f, b.x),
			a.y < 0.5f ? ColorBurn(a.y * 2f, b.y) : ColorDodge(a.y * 2f - 1f, b.y),
			a.z < 0.5f ? ColorBurn(a.z * 2f, b.z) : ColorDodge(a.z * 2f - 1f, b.z));

		private static Vector3 PinLight(Vector3 a, Vector3 b) => new Vector3(
			a.x < 0.5f ? Mathf.Min(b.x, 2f * a.x) : Mathf.Max(b.x, 2f * a.x - 1f),
			a.y < 0.5f ? Mathf.Min(b.y, 2f * a.y) : Mathf.Max(b.y, 2f * a.y - 1f),
			a.z < 0.5f ? Mathf.Min(b.z, 2f * a.z) : Mathf.Max(b.z, 2f * a.z - 1f));

		private static Vector3 ColorDodge(Vector3 a, Vector3 b) => new Vector3(ColorDodge(a.x, b.x), ColorDodge(a.y, b.y), ColorDodge(a.z, b.z));
		private static float ColorDodge(float a, float b) => a / Mathf.Max(1f - b, Epsilon);
		private static Vector3 ColorBurn(Vector3 a, Vector3 b) => new Vector3(ColorBurn(a.x, b.x), ColorBurn(a.y, b.y), ColorBurn(a.z, b.z));
		private static float ColorBurn(float a, float b) => 1f - (1f - a) / Mathf.Max(b, Epsilon);
		private static Vector3 Divide(Vector3 a, Vector3 b) => new Vector3(Divide(a.x, b.x), Divide(a.y, b.y), Divide(a.z, b.z));
		private static float Divide(float a, float b) => a / Mathf.Max(b, Epsilon);
		private static Vector3 Reflect(Vector3 a, Vector3 b) => new Vector3(Reflect(a.x, b.x), Reflect(a.y, b.y), Reflect(a.z, b.z));
		private static float Reflect(float a, float b) => b * b / Mathf.Max(1f - a, Epsilon);
		private static Vector3 Glow(Vector3 a, Vector3 b) => new Vector3(Glow(a.x, b.x), Glow(a.y, b.y), Glow(a.z, b.z));
		private static float Glow(float a, float b) => a * a / Mathf.Max(1f - b, Epsilon);

		private static Vector3 SetHue(Vector3 a, Vector3 b) {
			var ah = RgbToHsv(a);
			var bh = RgbToHsv(b);
			return HsvToRgb(new Vector3(ah.x, bh.y, bh.z));
		}

		private static Vector3 SetSaturation(Vector3 a, Vector3 b) {
			var ah = RgbToHsv(a);
			var bh = RgbToHsv(b);
			return HsvToRgb(new Vector3(bh.x, ah.y, bh.z));
		}

		private static Vector3 SetColor(Vector3 a, Vector3 b) {
			var ah = RgbToHsv(a);
			var bh = RgbToHsv(b);
			return HsvToRgb(new Vector3(ah.x, ah.y, bh.z));
		}

		private static Vector3 SetLuminosity(Vector3 a, Vector3 b) {
			var ah = RgbToHsv(a);
			var bh = RgbToHsv(b);
			return HsvToRgb(new Vector3(bh.x, bh.y, ah.z));
		}

		private static Vector3 RgbToHsv(Vector3 c) {
			var max = Mathf.Max(c.x, Mathf.Max(c.y, c.z));
			var min = Mathf.Min(c.x, Mathf.Min(c.y, c.z));
			var delta = max - min;
			var hue = 0f;
			if (delta > Epsilon) {
				if (max == c.x) hue = (c.y - c.z) / delta;
				else if (max == c.y) hue = 2f + (c.z - c.x) / delta;
				else hue = 4f + (c.x - c.y) / delta;
				hue = (hue / 6f) - Mathf.Floor(hue / 6f);
			}
			return new Vector3(hue, max <= Epsilon ? 0f : delta / max, max);
		}

		private static Vector3 HsvToRgb(Vector3 hsv) {
			var h = hsv.x - Mathf.Floor(hsv.x);
			var s = Clamp01(hsv.y);
			var v = Mathf.Max(hsv.z, 0f);
			var sector = h * 6f;
			var i = Mathf.FloorToInt(sector);
			var f = sector - i;
			var p = v * (1f - s);
			var q = v * (1f - s * f);
			var t = v * (1f - s * (1f - f));
			switch (i % 6) {
				case 0: return new Vector3(v, t, p);
				case 1: return new Vector3(q, v, p);
				case 2: return new Vector3(p, v, t);
				case 3: return new Vector3(p, q, v);
				case 4: return new Vector3(t, p, v);
				default: return new Vector3(v, p, q);
			}
		}

		private static float CombinedAlpha(float a, float b) => Clamp01(a + b - a * b);
		private static float Luma(Vector3 value) => Vector3.Dot(value, new Vector3(0.2126f, 0.7152f, 0.0722f));
		private static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));
		private static Vector3 Step(float edge, Vector3 v) => new Vector3(v.x >= edge ? 1f : 0f, v.y >= edge ? 1f : 0f, v.z >= edge ? 1f : 0f);
		private static Vector4 Lerp(Vector4 a, Vector4 b, float t) => Vector4.LerpUnclamped(a, b, Clamp01(t));
		private static float Clamp01(float value) => Mathf.Clamp01(float.IsNaN(value) || float.IsInfinity(value) ? 0f : value);
		private static Vector3 Clamp01(Vector3 value) => new Vector3(Clamp01(value.x), Clamp01(value.y), Clamp01(value.z));
		private static Vector4 Finite(Vector4 value) => new Vector4(Finite(value.x), Finite(value.y), Finite(value.z), Clamp01(value.w));
		private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : Mathf.Clamp(value, -1e20f, 1e20f);
	}

	public static class VJTransitionReference {
		public static Vector4 Evaluate(int variant, Vector4 a, Vector4 b, float progress, float softness = 0.02f, Vector2 uv = default(Vector2), int seed = 0, Color dipColor = default(Color)) {
			if (variant < 0 || variant >= VJVariantCatalog.TransitionCount) throw new ArgumentOutOfRangeException(nameof(variant));
			progress = Mathf.Clamp01(progress);
			if (progress <= 0f) return a;
			if (progress >= 1f) return b;
			if (variant == 1) {
				var color = dipColor == default(Color) ? Color.black : dipColor;
				var premultipliedColor = new Vector4(color.r * color.a, color.g * color.a, color.b * color.a, color.a);
				return progress < 0.5f ? Vector4.LerpUnclamped(a, premultipliedColor, progress * 2f) : Vector4.LerpUnclamped(premultipliedColor, b, progress * 2f - 1f);
			}
			var mask = Mask(variant, uv, progress, softness, seed);
			return Vector4.LerpUnclamped(a, b, Mathf.Clamp01(mask));
		}

		private static float Mask(int variant, Vector2 uv, float p, float softness, int seed) {
			var center = uv - new Vector2(0.5f, 0.5f);
			var x = uv.x;
			var y = uv.y;
			var radial = Mathf.Atan2(center.y, center.x) / (Mathf.PI * 2f) + 0.5f;
			var radius = center.magnitude * 1.41421356f;
			var width = Mathf.Max(Mathf.Abs(softness), 1e-4f);
			var noise = Hash(new Vector2(Mathf.Floor(uv.x * 32f), Mathf.Floor(uv.y * 32f)) + new Vector2(seed * 0.17f, seed * 0.37f));
			switch (variant) {
				case 0: return p;
				case 2: return Smooth(p, x, width);
				case 3: return Smooth(p, y, width);
				case 4: return Smooth(p, 1f - radial, width);
				case 5: return Smooth(p, 1f - radius, width);
				case 6: return Smooth(p, 1f - Mathf.Max(Mathf.Abs(center.x), Mathf.Abs(center.y)) * 2f, width);
				case 7: return Smooth(p, 1f - radial, width);
				case 8: return Smooth(p, 1f - x, width);
				case 9: return Smooth(p, noise, width);
				case 10: return Smooth(p, Hash(new Vector2(Mathf.Floor(uv.x * 16f), Mathf.Floor(uv.y * 16f)) + new Vector2(seed, seed)), width);
				case 11: return Smooth(p, 0.5f + 0.5f * Mathf.Sin((x + y) * Mathf.PI), width);
				case 12: return Smooth(p, x, width);
				case 13: return Smooth(p, y, width);
				case 14: return Smooth(p, Mathf.Abs(center.x) * 2f, width);
				case 15: return Smooth(p, Mathf.Max(Mathf.Abs(center.x), Mathf.Abs(center.y)) * 2f, width);
				case 16: return Smooth(p, (Mathf.Floor(y * 8f) % 2f) * 0.5f + x, width);
				case 17: return Smooth(p, ((Mathf.Floor(x * 8f) + Mathf.Floor(y * 8f)) % 2f), width);
				case 18: return Smooth(p, Mathf.Abs(Mathf.Sin(x * Mathf.PI * 4f)), width);
				case 19: return Smooth(p, 1f - radius, width);
				case 20: return Smooth(p, p + 0.2f * Mathf.Sin(radial * Mathf.PI * 2f), width);
				case 21: return Smooth(p, radius, width);
				case 22: return Smooth(p, p, width);
				case 23: return Smooth(p, 0.5f + 0.5f * Mathf.Sin(radius * 12f), width);
				case 24: return Smooth(p, 0.5f + 0.5f * Mathf.Sin((x + y) * 20f), width);
				case 25: return Smooth(p, 1f - radius, width);
				case 26: return Smooth(p, 0.5f + 0.5f * Mathf.Sin(radial * 10f + radius * 8f), width);
				case 27: return Smooth(p, 1f - Mathf.Abs(Mathf.Sin(radial * Mathf.PI * 4f)), width);
				case 28: return Smooth(p, 1f - radius + 0.1f * Mathf.Sin(radius * 30f), width);
				case 29: return Smooth(p, noise, width);
				case 30: return Smooth(p, Mathf.Abs(Mathf.Sin((x * 17f + y * 11f + seed) * Mathf.PI)), width);
				case 31: return Smooth(p, Mathf.Abs(Mathf.Sin((x + y) * Mathf.PI * 8f)), width);
				case 32: return Smooth(p, Mathf.Pow(Mathf.Clamp01(x), 0.75f), width);
				case 33: return Smooth(p, 1f - radius, width);
				case 34: return Smooth(p, 0.5f + 0.5f * Mathf.Sin((x * 13f + y * 7f) * Mathf.PI), width);
				default: return Smooth(p, noise, width);
			}
		}

		private static float Smooth(float p, float value, float width) => Mathf.SmoothStep(p - width, p + width, value);
		private static float Hash(Vector2 value) {
			var dot = value.x * 12.9898f + value.y * 78.233f;
			return Mathf.Repeat(Mathf.Sin(dot) * 43758.5453f, 1f);
		}
	}
}
