using System;
using UnityEngine;

namespace ShitDesigner.Rendering.VJ.Raymarch {
	public readonly struct RaymarchSettings {
		public int MaxSteps { get; }
		public float Epsilon { get; }
		public float FarDistance { get; }

		public RaymarchSettings(int maxSteps = 96, float epsilon = 0.001f, float farDistance = 30f) {
			MaxSteps = Mathf.Clamp(maxSteps, 1, 256);
			Epsilon = Mathf.Clamp(float.IsNaN(epsilon) || float.IsInfinity(epsilon) ? 0.001f : epsilon, 1e-5f, 0.1f);
			FarDistance = Mathf.Clamp(float.IsNaN(farDistance) || float.IsInfinity(farDistance) ? 30f : farDistance, 0.1f, 1000f);
		}
	}

	public readonly struct RaymarchResult {
		public bool Hit { get; }
		public float Distance { get; }
		public Vector3 Normal { get; }
		public int Steps { get; }

		public bool IsFinite => IsFiniteValue(Distance) && IsFiniteValue(Normal.x) && IsFiniteValue(Normal.y) && IsFiniteValue(Normal.z);

		internal RaymarchResult(bool hit, float distance, Vector3 normal, int steps) {
			Hit = hit;
			Distance = IsFiniteValue(distance) ? Mathf.Max(0f, distance) : 0f;
			Normal = FiniteVector(normal, Vector3.up);
			Steps = Mathf.Max(0, steps);
		}

		private static bool IsFiniteValue(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
		private static Vector3 FiniteVector(Vector3 value, Vector3 fallback) => IsFiniteValue(value.x) && IsFiniteValue(value.y) && IsFiniteValue(value.z) ? value : fallback;
	}

	public static class RaymarchVariantCatalog {
		public static readonly string[] Names =
		{
			"raymarched_sphere", "box", "torus", "capsule", "rounded_box", "sdf_boolean_sculpture",
			"repeated_geometry", "infinite_columns", "infinite_city", "neon_tunnel", "fractal_tunnel",
			"menger_sponge", "mandelbulb", "mandelbox", "kaleidoscopic_ifs", "gyroid", "metaball_sculpture",
			"voxel_landscape", "heightfield_terrain", "ocean", "volumetric_clouds", "nebula", "black_hole",
			"wormhole", "crystal", "glass_sdf", "emissive_wireframe_sdf", "truchet_3d", "audio_reactive_sculpture",
			"signed_distance_text_extrusion"
		};

		public const int Count = 30;
	}

	/// <summary>
	/// CPU fixture for validating the raymarch safety contract.  It mirrors
	/// the finite SDF vocabulary used by the GPU family, but keeps the test
	/// path independent from a graphics device.
	/// </summary>
	public static class RaymarchReference {
		public static RaymarchResult Trace(int variant, Vector3 origin, Vector3 direction, RaymarchSettings settings, float audio = 0f) {
			if (variant < 0 || variant >= RaymarchVariantCatalog.Count) throw new ArgumentOutOfRangeException(nameof(variant));
			var rayDirection = SafeNormalize(direction, Vector3.forward);
			var position = origin;
			var distanceTravelled = 0f;
			for (var step = 0; step < settings.MaxSteps; step++) {
				var distance = SignedDistance(variant, position, audio);
				if (!IsFinite(distance)) return new RaymarchResult(false, distanceTravelled, Vector3.up, step + 1);
				if (distance <= settings.Epsilon)
					return new RaymarchResult(true, distanceTravelled, Normal(variant, position, settings.Epsilon, audio), step + 1);
				distanceTravelled += Mathf.Max(distance, settings.Epsilon * 0.5f);
				if (distanceTravelled > settings.FarDistance)
					return new RaymarchResult(false, settings.FarDistance, Vector3.up, step + 1);
				position = origin + rayDirection * distanceTravelled;
			}
			return new RaymarchResult(false, Mathf.Min(distanceTravelled, settings.FarDistance), Vector3.up, settings.MaxSteps);
		}

		public static float SignedDistance(int variant, Vector3 point, float audio = 0f) {
			if (variant < 0 || variant >= RaymarchVariantCatalog.Count) throw new ArgumentOutOfRangeException(nameof(variant));
			var p = FiniteVector(point);
			var t = Mathf.Clamp(Finite(audio), -4f, 4f);
			switch (variant) {
				case 0: return Sphere(p, 0.75f);
				case 1: return Box(p, new Vector3(0.7f, 0.7f, 0.7f));
				case 2: return Torus(p, 0.72f, 0.18f);
				case 3: return Capsule(p, new Vector3(0f, -0.5f, 0f), new Vector3(0f, 0.5f, 0f), 0.28f);
				case 4: return RoundedBox(p, new Vector3(0.58f, 0.58f, 0.58f), 0.15f);
				case 5: return SmoothUnion(Sphere(p - new Vector3(-0.35f, 0f, 0f), 0.55f), Torus(p - new Vector3(0.35f, 0f, 0f), 0.45f, 0.16f), 0.18f);
				case 6: return Sphere(Repeat(p, new Vector3(1.5f, 1.5f, 1.5f)), 0.42f);
				case 7: return Columns(p);
				case 8: return City(p);
				case 9: return Torus(new Vector3(p.x, p.y, Repeat(p, new Vector3(0f, 0f, 3f)).z), 1.0f, 0.07f);
				case 10: return FractalTunnel(p);
				case 11: return Menger(p);
				case 12: return Mandelbulb(p);
				case 13: return Mandelbox(p);
				case 14: return Kaleidoscope(p);
				case 15: return (Mathf.Sin(p.x * 3f) + Mathf.Sin(p.y * 3f) + Mathf.Sin(p.z * 3f)) * 0.12f - 0.08f;
				case 16: return Metaballs(p);
				case 17: return VoxelLandscape(p);
				case 18: return p.y - (Mathf.Sin(p.x * 1.5f) * 0.2f + Mathf.Cos(p.z * 1.3f) * 0.2f);
				case 19: return p.y + 0.35f + Mathf.Sin(p.x * 2.3f + p.z) * 0.12f + Mathf.Sin(p.z * 3.1f) * 0.08f;
				case 20: return Clouds(p);
				case 21: return Nebula(p);
				case 22: return Mathf.Abs(p.z) + 0.12f - Mathf.Max(0.2f, new Vector2(p.x, p.y).magnitude * 0.2f);
				case 23: return Mathf.Abs(new Vector2(p.x, p.y).magnitude - 0.45f) - 0.08f;
				case 24: return Crystal(p);
				case 25: return Mathf.Abs(Box(p, new Vector3(0.65f, 0.65f, 0.65f))) - 0.03f;
				case 26: return Wireframe(p);
				case 27: return Truchet(p);
				case 28: return Sphere(p, 0.55f + t * 0.08f) + Mathf.Sin(p.y * 12f + t) * 0.04f;
				default: return TextExtrusion(p);
			}
		}

		private static Vector3 Normal(int variant, Vector3 point, float epsilon, float audio) {
			var e = Mathf.Max(epsilon, 1e-4f);
			var x = SignedDistance(variant, point + new Vector3(e, 0f, 0f), audio) - SignedDistance(variant, point - new Vector3(e, 0f, 0f), audio);
			var y = SignedDistance(variant, point + new Vector3(0f, e, 0f), audio) - SignedDistance(variant, point - new Vector3(0f, e, 0f), audio);
			var z = SignedDistance(variant, point + new Vector3(0f, 0f, e), audio) - SignedDistance(variant, point - new Vector3(0f, 0f, e), audio);
			return SafeNormalize(new Vector3(x, y, z), Vector3.up);
		}

		private static float Columns(Vector3 p) => Mathf.Min(Mathf.Abs(new Vector2(p.x, p.z).magnitude - 0.45f) - 0.12f, Mathf.Abs(p.y) - 1.0f);
		private static float City(Vector3 p) {
			var cell = Repeat(p, new Vector3(1.4f, 2f, 1.4f));
			var height = 0.25f + 0.8f * Hash(new Vector2(cell.x, cell.z));
			return Box(cell - new Vector3(0f, height - 1f, 0f), new Vector3(0.38f, height, 0.38f));
		}

		private static float FractalTunnel(Vector3 p) {
			var q = p;
			var distance = 10f;
			for (var i = 0; i < 5; i++) {
				q = new Vector3(Mathf.Abs(q.x), Mathf.Abs(q.y), q.z) - Vector3.one * 0.35f;
				q = Quaternion.Euler(0f, 17f, 23f) * q;
				distance = Mathf.Min(distance, Box(q, new Vector3(0.35f, 0.35f, 1.5f)) / (i + 1f));
			}
			return distance;
		}

		private static float Menger(Vector3 p) {
			var scale = 1f;
			var distance = Box(p, Vector3.one * 0.8f);
			for (var i = 0; i < 4; i++) {
				p = Repeat(p, Vector3.one * 2f) - Vector3.one;
				var cross = Mathf.Max(Mathf.Abs(p.x), Mathf.Abs(p.y));
				cross = Mathf.Max(cross, Mathf.Abs(p.z));
				distance = Mathf.Max(distance, -Mathf.Min(Mathf.Abs(p.x), Mathf.Min(Mathf.Abs(p.y), Mathf.Abs(p.z))) + 0.16f / scale);
				scale *= 3f;
			}
			return distance / scale;
		}

		private static float Mandelbulb(Vector3 p) {
			var z = p;
			var dr = 1f;
			var radius = z.magnitude;
			for (var i = 0; i < 8 && radius < 2f; i++) {
				var theta = Mathf.Acos(Mathf.Clamp(z.z / Mathf.Max(radius, 1e-4f), -1f, 1f));
				var phi = Mathf.Atan2(z.y, z.x);
				var power = 8f;
				dr = Mathf.Pow(radius, power - 1f) * power * dr + 1f;
				var zr = Mathf.Pow(radius, power);
				z = zr * new Vector3(Mathf.Sin(theta * power) * Mathf.Cos(phi * power), Mathf.Sin(phi * power) * Mathf.Sin(theta * power), Mathf.Cos(theta * power)) + p;
				radius = z.magnitude;
			}
			return 0.5f * Mathf.Log(Mathf.Max(radius, 1e-4f)) * radius / Mathf.Max(dr, 1e-4f);
		}

		private static float Mandelbox(Vector3 p) {
			var z = p;
			var scale = 2.2f;
			var minRadius = 0.35f;
			var fixedRadius = 1f;
			var derivative = 1f;
			for (var i = 0; i < 8; i++) {
				z = Vector3.Min(Vector3.one, Vector3.Max(-Vector3.one, z * 2f)) - z;
				var radius = z.magnitude;
				if (radius < minRadius) { z *= fixedRadius / minRadius; derivative *= fixedRadius / minRadius; }
				else if (radius < fixedRadius) { z *= fixedRadius / radius; derivative *= fixedRadius / radius; }
				z = z * scale + p;
				derivative = derivative * Mathf.Abs(scale) + 1f;
			}
			return z.magnitude / Mathf.Max(Mathf.Abs(derivative), 1e-4f);
		}

		private static float Kaleidoscope(Vector3 p) {
			var q = p;
			for (var i = 0; i < 4; i++) {
				q = Vector3.Scale(q, new Vector3(-1f, 1f, 1f));
				q = Vector3.Min(q, new Vector3(q.y, q.z, q.x));
				q = q * 1.35f - Vector3.one * 0.42f;
			}
			return Sphere(q, 0.3f);
		}

		private static float Metaballs(Vector3 p) {
			var field = 0f;
			for (var i = 0; i < 4; i++) {
				var center = new Vector3(Mathf.Sin(i * 2.1f) * 0.5f, Mathf.Cos(i * 1.7f) * 0.4f, Mathf.Sin(i * 1.3f) * 0.45f);
				field += 0.08f / Mathf.Max((p - center).sqrMagnitude, 1e-4f);
			}
			return 0.55f - field;
		}

		private static float VoxelLandscape(Vector3 p) {
			var cell = Repeat(p, Vector3.one * 1.0f);
			var height = 0.2f + Hash(new Vector2(Mathf.Floor(p.x * 1.7f), Mathf.Floor(p.z * 1.7f))) * 0.8f;
			return Mathf.Max(Mathf.Abs(cell.x) - 0.45f, Mathf.Max(Mathf.Abs(cell.z) - 0.45f, p.y - height));
		}

		private static float Clouds(Vector3 p) {
			var density = 0f;
			var q = p * 1.3f;
			for (var i = 0; i < 4; i++) {
				density += VJNoise(q) * 0.5f;
				q = q * 2.02f + Vector3.one * 0.17f;
			}
			return 0.45f - density * 0.42f;
		}

		private static float Nebula(Vector3 p) => 0.3f - VJNoise(p * 2.4f) * 0.45f - VJNoise(p * 6.3f) * 0.12f;
		private static float Crystal(Vector3 p) => Mathf.Max(Box(p, new Vector3(0.42f, 0.9f, 0.42f)), Mathf.Abs(p.y) - 0.9f) + Mathf.Sin(p.y * 8f) * 0.05f;
		private static float Wireframe(Vector3 p) => Mathf.Min(Mathf.Abs(Mathf.Abs(p.x) - 0.6f), Mathf.Min(Mathf.Abs(Mathf.Abs(p.y) - 0.6f), Mathf.Abs(Mathf.Abs(p.z) - 0.6f))) - 0.035f;
		private static float Truchet(Vector3 p) {
			var cell = Repeat(p, Vector3.one * 1.5f);
			var arc = Mathf.Abs(new Vector2(cell.x, cell.z).magnitude - 0.5f) - 0.07f;
			return Mathf.Max(arc, Mathf.Abs(cell.y) - 0.5f);
		}
		private static float TextExtrusion(Vector3 p) {
			var bars = Mathf.Min(Mathf.Abs(p.x) - 0.08f, Mathf.Max(Mathf.Abs(p.y) - 0.55f, Mathf.Abs(p.z) - 0.2f));
			return Mathf.Max(bars, Mathf.Abs(p.z) - 0.25f);
		}

		private static float SmoothUnion(float a, float b, float k) {
			var h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / Mathf.Max(k, 1e-4f));
			return Mathf.Lerp(b, a, h) - k * h * (1f - h);
		}

		private static float Sphere(Vector3 p, float radius) => p.magnitude - radius;
		private static float Box(Vector3 p, Vector3 bounds) {
			var q = new Vector3(Mathf.Abs(p.x), Mathf.Abs(p.y), Mathf.Abs(p.z)) - bounds;
			var outside = new Vector3(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f), Mathf.Max(q.z, 0f)).magnitude;
			var inside = Mathf.Min(Mathf.Max(q.x, Mathf.Max(q.y, q.z)), 0f);
			return outside + inside;
		}
		private static float Torus(Vector3 p, float major, float minor) => new Vector2(new Vector2(p.x, p.z).magnitude - major, p.y).magnitude - minor;
		private static float Capsule(Vector3 p, Vector3 a, Vector3 b, float radius) {
			var ba = b - a;
			var h = Mathf.Clamp01(Vector3.Dot(p - a, ba) / Mathf.Max(Vector3.Dot(ba, ba), 1e-4f));
			return (p - Vector3.Lerp(a, b, h)).magnitude - radius;
		}
		private static float RoundedBox(Vector3 p, Vector3 bounds, float radius) => Box(p, bounds - Vector3.one * radius) - radius;
		private static Vector3 Repeat(Vector3 p, Vector3 cell) => new Vector3(Mod(p.x + cell.x * 0.5f, cell.x) - cell.x * 0.5f, Mod(p.y + cell.y * 0.5f, cell.y) - cell.y * 0.5f, Mod(p.z + cell.z * 0.5f, cell.z) - cell.z * 0.5f);
		private static float Mod(float value, float divisor) => divisor == 0f ? value : value - divisor * Mathf.Floor(value / divisor);
		private static float VJNoise(Vector3 p) => Mathf.Repeat(Mathf.Sin(Vector3.Dot(p, new Vector3(12.9898f, 78.233f, 37.719f))) * 43758.5453f, 1f);
		private static float Hash(float value) => Mathf.Repeat(Mathf.Sin(value * 12.9898f) * 43758.5453f, 1f);
		private static float Hash(Vector2 value) => Mathf.Repeat(Mathf.Sin(Vector2.Dot(value, new Vector2(12.9898f, 78.233f))) * 43758.5453f, 1f);
		private static Vector3 FiniteVector(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) ? value : Vector3.zero;
		private static Vector3 SafeNormalize(Vector3 value, Vector3 fallback) => value.sqrMagnitude > 1e-8f && IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z) ? value.normalized : fallback;
		private static float Finite(float value) => IsFinite(value) ? Mathf.Clamp(value, -1e6f, 1e6f) : 0f;
		private static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
	}
}
