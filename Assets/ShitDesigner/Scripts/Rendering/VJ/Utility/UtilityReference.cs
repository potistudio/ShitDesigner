using System;
using UnityEngine;

namespace ShitDesigner.Rendering.VJ.Utility
{
    /// <summary>CPU probes used by Utility contract tests and diagnostics.</summary>
    public static class UtilityReference
    {
        public static float Luma(Color color) => Finite(color.r) * 0.2126f + Finite(color.g) * 0.7152f + Finite(color.b) * 0.0722f;

        public static float[] Histogram(Color[] pixels, int bins = 256)
        {
            if (bins < 2) throw new ArgumentOutOfRangeException(nameof(bins));
            var output = new float[bins];
            if (pixels == null || pixels.Length == 0) return output;
            for (var i = 0; i < pixels.Length; i++)
            {
                var index = Mathf.Clamp(Mathf.FloorToInt(Mathf.Clamp01(Luma(pixels[i])) * (bins - 1)), 0, bins - 1);
                output[index]++;
            }
            for (var i = 0; i < output.Length; i++) output[i] /= pixels.Length;
            return output;
        }

        public static Vector2 Vectorscope(Color color)
        {
            var red = Finite(color.r);
            var green = Finite(color.g);
            var blue = Finite(color.b);
            var chromaX = red - 0.5f * (green + blue);
            var chromaY = (green - blue) * 0.8660254f;
            return new Vector2(chromaX, chromaY);
        }

        public static Color Difference(Color first, Color second)
        {
            return new Color(Mathf.Abs(Finite(first.r) - Finite(second.r)), Mathf.Abs(Finite(first.g) - Finite(second.g)),
                Mathf.Abs(Finite(first.b) - Finite(second.b)), Mathf.Abs(Finite(first.a) - Finite(second.a)));
        }

        public static Color ConvertRec709To2020(Color color)
        {
            var input = new Vector3(Finite(color.r), Finite(color.g), Finite(color.b));
            var output = new Vector3(
                Vector3.Dot(input, new Vector3(0.6274f, 0.3293f, 0.0433f)),
                Vector3.Dot(input, new Vector3(0.0691f, 0.9195f, 0.0114f)),
                Vector3.Dot(input, new Vector3(0.0164f, 0.0880f, 0.8956f)));
            return new Color(Finite(output.x), Finite(output.y), Finite(output.z), Mathf.Clamp01(Finite(color.a)));
        }

        public static float ToSrgb(float linear)
        {
            linear = Mathf.Max(0f, Finite(linear));
            return linear <= 0.0031308f ? linear * 12.92f : 1.055f * Mathf.Pow(linear, 1f / 2.4f) - 0.055f;
        }

        public static float ToLinear(float srgb)
        {
            srgb = Mathf.Max(0f, Finite(srgb));
            return srgb <= 0.04045f ? srgb / 12.92f : Mathf.Pow((srgb + 0.055f) / 1.055f, 2.4f);
        }

        public static bool IsFinite(Color color) => IsFiniteValue(color.r) && IsFiniteValue(color.g) && IsFiniteValue(color.b) && IsFiniteValue(color.a);
        private static float Finite(float value) => IsFiniteValue(value) ? Mathf.Clamp(value, -1e20f, 1e20f) : 0f;
        private static bool IsFiniteValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
    }
}
