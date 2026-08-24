using System;
using System.Globalization;

namespace ShitDesigner.Core {
	public enum ParameterType {
		Float,
		Int,
		Bool,
		Vector2,
		Vector3,
		Vector4,
		Color,
		String,
		Enum,
		MediaAssetReference
	}

	public readonly struct Vector2Value : IEquatable<Vector2Value> {
		public float X { get; }
		public float Y { get; }
		public Vector2Value(float x, float y) { X = EnsureFinite(x, nameof(x)); Y = EnsureFinite(y, nameof(y)); }
		public bool Equals(Vector2Value other) => X.Equals(other.X) && Y.Equals(other.Y);
		public override bool Equals(object obj) => obj is Vector2Value other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(X, Y);
		public override string ToString() => $"({X.ToString(CultureInfo.InvariantCulture)}, {Y.ToString(CultureInfo.InvariantCulture)})";
		internal static float EnsureFinite(float value, string name) { if (float.IsNaN(value) || float.IsInfinity(value)) throw new ArgumentOutOfRangeException(name, "Value must be finite."); return value; }
	}

	public readonly struct Vector3Value : IEquatable<Vector3Value> {
		public float X { get; }
		public float Y { get; }
		public float Z { get; }
		public Vector3Value(float x, float y, float z) { X = Vector2Value.EnsureFinite(x, nameof(x)); Y = Vector2Value.EnsureFinite(y, nameof(y)); Z = Vector2Value.EnsureFinite(z, nameof(z)); }
		public bool Equals(Vector3Value other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
		public override bool Equals(object obj) => obj is Vector3Value other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(X, Y, Z);
		public override string ToString() => $"({X.ToString(CultureInfo.InvariantCulture)}, {Y.ToString(CultureInfo.InvariantCulture)}, {Z.ToString(CultureInfo.InvariantCulture)})";
	}

	public readonly struct Vector4Value : IEquatable<Vector4Value> {
		public float X { get; }
		public float Y { get; }
		public float Z { get; }
		public float W { get; }
		public Vector4Value(float x, float y, float z, float w) { X = Vector2Value.EnsureFinite(x, nameof(x)); Y = Vector2Value.EnsureFinite(y, nameof(y)); Z = Vector2Value.EnsureFinite(z, nameof(z)); W = Vector2Value.EnsureFinite(w, nameof(w)); }
		public bool Equals(Vector4Value other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z) && W.Equals(other.W);
		public override bool Equals(object obj) => obj is Vector4Value other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(X, Y, Z, W);
		public override string ToString() => $"({X.ToString(CultureInfo.InvariantCulture)}, {Y.ToString(CultureInfo.InvariantCulture)}, {Z.ToString(CultureInfo.InvariantCulture)}, {W.ToString(CultureInfo.InvariantCulture)})";
	}

	public readonly struct ColorValue : IEquatable<ColorValue> {
		public float R { get; }
		public float G { get; }
		public float B { get; }
		public float A { get; }
		public ColorValue(float r, float g, float b, float a) { R = Vector2Value.EnsureFinite(r, nameof(r)); G = Vector2Value.EnsureFinite(g, nameof(g)); B = Vector2Value.EnsureFinite(b, nameof(b)); A = Vector2Value.EnsureFinite(a, nameof(a)); }
		public bool Equals(ColorValue other) => R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);
		public override bool Equals(object obj) => obj is ColorValue other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(R, G, B, A);
		public override string ToString() => $"({R.ToString(CultureInfo.InvariantCulture)}, {G.ToString(CultureInfo.InvariantCulture)}, {B.ToString(CultureInfo.InvariantCulture)}, {A.ToString(CultureInfo.InvariantCulture)})";
	}

	public readonly struct ParameterValue : IEquatable<ParameterValue> {
		private readonly ParameterType _type;
		private readonly float _float;
		private readonly int _int;
		private readonly bool _bool;
		private readonly Vector2Value _vector2;
		private readonly Vector3Value _vector3;
		private readonly Vector4Value _vector4;
		private readonly ColorValue _color;
		private readonly string _text;
		public ParameterType Type => _type;
		private ParameterValue(ParameterType type, float floatValue = default, int intValue = default, bool boolValue = default, Vector2Value vector2 = default, Vector3Value vector3 = default, Vector4Value vector4 = default, ColorValue color = default, string text = null) {
			_type = type;
			_float = floatValue;
			_int = intValue;
			_bool = boolValue;
			_vector2 = vector2;
			_vector3 = vector3;
			_vector4 = vector4;
			_color = color;
			_text = text;
		}
		public static ParameterValue Float(float value) => FromFloat(value);
		public static ParameterValue FromFloat(float value) => new ParameterValue(ParameterType.Float, Vector2Value.EnsureFinite(value, nameof(value)));
		public static ParameterValue FromInt(int value) => new ParameterValue(ParameterType.Int, intValue: value);
		public static ParameterValue FromBool(bool value) => new ParameterValue(ParameterType.Bool, boolValue: value);
		public static ParameterValue FromVector2(Vector2Value value) => new ParameterValue(ParameterType.Vector2, vector2: value);
		public static ParameterValue FromVector3(Vector3Value value) => new ParameterValue(ParameterType.Vector3, vector3: value);
		public static ParameterValue FromVector4(Vector4Value value) => new ParameterValue(ParameterType.Vector4, vector4: value);
		public static ParameterValue FromColor(ColorValue value) => new ParameterValue(ParameterType.Color, color: value);
		public static ParameterValue FromString(string value) {
			if (value == null) throw new ArgumentNullException(nameof(value));
			if (value.Length > 4096) throw new ArgumentException("String parameter values are limited to 4096 UTF-16 characters.", nameof(value));
			if (value.IndexOf('\0') >= 0) throw new ArgumentException("String parameter values cannot contain NUL.", nameof(value));
			return new ParameterValue(ParameterType.String, text: value);
		}
		public static ParameterValue FromEnum(string optionId) {
			if (optionId == null) throw new ArgumentNullException(nameof(optionId));
			var normalized = optionId.Trim();
			if (normalized.Length > 0) StableIdRules.NormalizeParameter(normalized, nameof(optionId));
			return new ParameterValue(ParameterType.Enum, text: normalized);
		}
		public static ParameterValue FromMediaAsset(MediaAssetId? assetId) => new ParameterValue(ParameterType.MediaAssetReference, text: assetId?.Value);
		public float AsFloat() => _type == ParameterType.Float ? _float : throw TypeError(ParameterType.Float);
		public int AsInt() => _type == ParameterType.Int ? _int : throw TypeError(ParameterType.Int);
		public bool AsBool() => _type == ParameterType.Bool ? _bool : throw TypeError(ParameterType.Bool);
		public Vector2Value AsVector2() => _type == ParameterType.Vector2 ? _vector2 : throw TypeError(ParameterType.Vector2);
		public Vector3Value AsVector3() => _type == ParameterType.Vector3 ? _vector3 : throw TypeError(ParameterType.Vector3);
		public Vector4Value AsVector4() => _type == ParameterType.Vector4 ? _vector4 : throw TypeError(ParameterType.Vector4);
		public ColorValue AsColor() => _type == ParameterType.Color ? _color : throw TypeError(ParameterType.Color);
		public string AsString() => (_type == ParameterType.String || _type == ParameterType.Enum || _type == ParameterType.MediaAssetReference) ? _text : throw TypeError(_type);
		public bool IsMediaAssetSelected => _type == ParameterType.MediaAssetReference && !string.IsNullOrEmpty(_text);
		public MediaAssetId? AsMediaAsset() => _type == ParameterType.MediaAssetReference && !string.IsNullOrEmpty(_text) ? new MediaAssetId(_text) : (MediaAssetId?)null;
		public static ParameterValue Default(ParameterType type) => type == ParameterType.Float ? FromFloat(0) : type == ParameterType.Int ? FromInt(0) : type == ParameterType.Bool ? FromBool(false) : type == ParameterType.Vector2 ? FromVector2(new Vector2Value(0, 0)) : type == ParameterType.Vector3 ? FromVector3(new Vector3Value(0, 0, 0)) : type == ParameterType.Vector4 ? FromVector4(new Vector4Value(0, 0, 0, 0)) : type == ParameterType.Color ? FromColor(new ColorValue(0, 0, 0, 0)) : type == ParameterType.String ? FromString(string.Empty) : type == ParameterType.Enum ? FromEnum(string.Empty) : FromMediaAsset(null);
		public static bool IsLogicalControlTargetType(ParameterType type) => type != ParameterType.String && type != ParameterType.Enum && type != ParameterType.MediaAssetReference;
		public bool Equals(ParameterValue other) => _type == other._type && _float.Equals(other._float) && _int == other._int && _bool == other._bool && _vector2.Equals(other._vector2) && _vector3.Equals(other._vector3) && _vector4.Equals(other._vector4) && _color.Equals(other._color) && string.Equals(_text, other._text, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is ParameterValue other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(HashCode.Combine(_type, _float, _int, _bool), HashCode.Combine(_vector2, _vector3, _vector4, _color), _text);
		public override string ToString() => _type == ParameterType.Float ? _float.ToString(CultureInfo.InvariantCulture) : _type == ParameterType.Int ? _int.ToString(CultureInfo.InvariantCulture) : _type == ParameterType.Bool ? _bool.ToString() : _type == ParameterType.Vector2 ? _vector2.ToString() : _type == ParameterType.Vector3 ? _vector3.ToString() : _type == ParameterType.Vector4 ? _vector4.ToString() : _type == ParameterType.Color ? _color.ToString() : _text ?? string.Empty;
		private InvalidOperationException TypeError(ParameterType expected) => new InvalidOperationException($"Parameter value is {_type}; expected {expected}.");
		public static bool operator ==(ParameterValue left, ParameterValue right) => left.Equals(right);
		public static bool operator !=(ParameterValue left, ParameterValue right) => !left.Equals(right);

		public static Result<ParameterValue> Clamp(ParameterValue value, ParameterValue min, ParameterValue max) {
			if (value.Type != min.Type || value.Type != max.Type || !IsLogicalControlTargetType(value.Type))
				return Result<ParameterValue>.Failure(new Diagnostic(new DiagnosticCode("core.parameter.type_mismatch"), Severity.Error, "Parameter range type mismatch."));
			if (!IsOrdered(min, max))
				return Result<ParameterValue>.Failure(new Diagnostic(new DiagnosticCode("core.parameter.invalid_range"), Severity.Error, "Parameter range minimum cannot exceed maximum."));
			switch (value.Type) {
				case ParameterType.Float: return Result<ParameterValue>.Success(FromFloat(Math.Min(Math.Max(value._float, min._float), max._float)));
				case ParameterType.Int: return Result<ParameterValue>.Success(FromInt(Math.Min(Math.Max(value._int, min._int), max._int)));
				case ParameterType.Bool: return Result<ParameterValue>.Success(value);
				case ParameterType.Vector2: return Result<ParameterValue>.Success(FromVector2(new Vector2Value(Clamp(value._vector2.X, min._vector2.X, max._vector2.X), Clamp(value._vector2.Y, min._vector2.Y, max._vector2.Y))));
				case ParameterType.Vector3: return Result<ParameterValue>.Success(FromVector3(new Vector3Value(Clamp(value._vector3.X, min._vector3.X, max._vector3.X), Clamp(value._vector3.Y, min._vector3.Y, max._vector3.Y), Clamp(value._vector3.Z, min._vector3.Z, max._vector3.Z))));
				case ParameterType.Vector4: return Result<ParameterValue>.Success(FromVector4(new Vector4Value(Clamp(value._vector4.X, min._vector4.X, max._vector4.X), Clamp(value._vector4.Y, min._vector4.Y, max._vector4.Y), Clamp(value._vector4.Z, min._vector4.Z, max._vector4.Z), Clamp(value._vector4.W, min._vector4.W, max._vector4.W))));
				case ParameterType.Color: return Result<ParameterValue>.Success(FromColor(new ColorValue(Clamp(value._color.R, min._color.R, max._color.R), Clamp(value._color.G, min._color.G, max._color.G), Clamp(value._color.B, min._color.B, max._color.B), Clamp(value._color.A, min._color.A, max._color.A))));
				default: throw new InvalidOperationException();
			}
		}
		private static float Clamp(float value, float min, float max) => Math.Min(Math.Max(value, min), max);
		public static Result<ParameterValue> Lerp(ParameterValue min, ParameterValue max, float t) {
			if (min.Type != max.Type || !IsLogicalControlTargetType(min.Type) || float.IsNaN(t) || float.IsInfinity(t))
				return Result<ParameterValue>.Failure(new Diagnostic(new DiagnosticCode("core.parameter.invalid_mapping"), Severity.Error, "Parameter mapping is invalid."));
			if (!IsOrdered(min, max))
				return Result<ParameterValue>.Failure(new Diagnostic(new DiagnosticCode("core.parameter.invalid_range"), Severity.Error, "Parameter mapping minimum cannot exceed maximum."));
			t = Clamp(t, 0f, 1f);
			switch (min.Type) {
				case ParameterType.Float: return Result<ParameterValue>.Success(FromFloat(min._float + (max._float - min._float) * t));
				case ParameterType.Int: return Result<ParameterValue>.Success(FromInt(RoundAwayFromZero(min._int + ((float)max._int - min._int) * t)));
				case ParameterType.Bool: return Result<ParameterValue>.Success(FromBool(t >= 0.5f));
				case ParameterType.Vector2: return Result<ParameterValue>.Success(FromVector2(new Vector2Value(Lerp(min._vector2.X, max._vector2.X, t), Lerp(min._vector2.Y, max._vector2.Y, t))));
				case ParameterType.Vector3: return Result<ParameterValue>.Success(FromVector3(new Vector3Value(Lerp(min._vector3.X, max._vector3.X, t), Lerp(min._vector3.Y, max._vector3.Y, t), Lerp(min._vector3.Z, max._vector3.Z, t))));
				case ParameterType.Vector4: return Result<ParameterValue>.Success(FromVector4(new Vector4Value(Lerp(min._vector4.X, max._vector4.X, t), Lerp(min._vector4.Y, max._vector4.Y, t), Lerp(min._vector4.Z, max._vector4.Z, t), Lerp(min._vector4.W, max._vector4.W, t))));
				case ParameterType.Color: return Result<ParameterValue>.Success(FromColor(new ColorValue(Lerp(min._color.R, max._color.R, t), Lerp(min._color.G, max._color.G, t), Lerp(min._color.B, max._color.B, t), Lerp(min._color.A, max._color.A, t))));
				default: throw new InvalidOperationException();
			}
		}
		private static float Lerp(float min, float max, float t) => min + (max - min) * t;
		private static int RoundAwayFromZero(float value) {
			if (value >= int.MaxValue) return int.MaxValue;
			if (value <= int.MinValue) return int.MinValue;
			return value >= 0 ? (int)Math.Floor(value + 0.5f) : (int)Math.Ceiling(value - 0.5f);
		}
		private static bool IsOrdered(ParameterValue min, ParameterValue max) {
			switch (min.Type) {
				case ParameterType.Bool: return true;
				case ParameterType.Float: return min._float <= max._float;
				case ParameterType.Int: return min._int <= max._int;
				case ParameterType.Vector2: return min._vector2.X <= max._vector2.X && min._vector2.Y <= max._vector2.Y;
				case ParameterType.Vector3: return min._vector3.X <= max._vector3.X && min._vector3.Y <= max._vector3.Y && min._vector3.Z <= max._vector3.Z;
				case ParameterType.Vector4: return min._vector4.X <= max._vector4.X && min._vector4.Y <= max._vector4.Y && min._vector4.Z <= max._vector4.Z && min._vector4.W <= max._vector4.W;
				case ParameterType.Color: return min._color.R <= max._color.R && min._color.G <= max._color.G && min._color.B <= max._color.B && min._color.A <= max._color.A;
				default: return false;
			}
		}
		public static Result<ParameterValue> Min(ParameterValue left, ParameterValue right) => Combine(left, right, false);
		public static Result<ParameterValue> Max(ParameterValue left, ParameterValue right) => Combine(left, right, true);
		private static Result<ParameterValue> Combine(ParameterValue left, ParameterValue right, bool max) {
			if (left.Type != right.Type || !IsLogicalControlTargetType(left.Type)) return Result<ParameterValue>.Failure(new Diagnostic(new DiagnosticCode("core.parameter.type_mismatch"), Severity.Error, "Parameter operation type mismatch."));
			float Pick(float a, float b) => max ? Math.Max(a, b) : Math.Min(a, b);
			switch (left.Type) {
				case ParameterType.Bool: return Result<ParameterValue>.Success(FromBool(max ? left._bool || right._bool : left._bool && right._bool));
				case ParameterType.Float: return Result<ParameterValue>.Success(FromFloat(Pick(left._float, right._float)));
				case ParameterType.Int: return Result<ParameterValue>.Success(FromInt(max ? Math.Max(left._int, right._int) : Math.Min(left._int, right._int)));
				case ParameterType.Vector2: return Result<ParameterValue>.Success(FromVector2(new Vector2Value(Pick(left._vector2.X, right._vector2.X), Pick(left._vector2.Y, right._vector2.Y))));
				case ParameterType.Vector3: return Result<ParameterValue>.Success(FromVector3(new Vector3Value(Pick(left._vector3.X, right._vector3.X), Pick(left._vector3.Y, right._vector3.Y), Pick(left._vector3.Z, right._vector3.Z))));
				case ParameterType.Vector4: return Result<ParameterValue>.Success(FromVector4(new Vector4Value(Pick(left._vector4.X, right._vector4.X), Pick(left._vector4.Y, right._vector4.Y), Pick(left._vector4.Z, right._vector4.Z), Pick(left._vector4.W, right._vector4.W))));
				case ParameterType.Color: return Result<ParameterValue>.Success(FromColor(new ColorValue(Pick(left._color.R, right._color.R), Pick(left._color.G, right._color.G), Pick(left._color.B, right._color.B), Pick(left._color.A, right._color.A))));
				default: return Result<ParameterValue>.Failure(new Diagnostic(new DiagnosticCode("core.parameter.type_not_supported"), Severity.Error, "Parameter type is not supported for Min/Max."));
			}
		}
	}
}
