using System;
using System.Text.RegularExpressions;

namespace ShitDesigner.Core {
	/// <summary>
	/// A device-neutral keyboard identity crossing the Input/Application
	/// boundary.  The physical key name is persisted; ControlPath is retained
	/// as the Input System binding path for diagnostics and re-binding.
	/// </summary>
	public readonly struct PhysicalKey : IEquatable<PhysicalKey> {
		public string PhysicalId { get; }
		public string ControlPath { get; }
		public bool IsModifierOnly { get; }

		public PhysicalKey(string physicalId, string controlPath = null, bool isModifierOnly = false) {
			if (string.IsNullOrWhiteSpace(physicalId)) throw new ArgumentException("A physical key ID is required.", nameof(physicalId));
			PhysicalId = physicalId.Trim();
			ControlPath = string.IsNullOrWhiteSpace(controlPath) ? "<Keyboard>/" + PhysicalId.ToLowerInvariant() : controlPath.Trim();
			IsModifierOnly = isModifierOnly;
		}

		public static PhysicalKey From(string physicalId, string controlPath = null, bool isModifierOnly = false) => new PhysicalKey(physicalId, controlPath, isModifierOnly);
		public bool Equals(PhysicalKey other) => string.Equals(PhysicalId, other.PhysicalId, StringComparison.Ordinal) && string.Equals(ControlPath, other.ControlPath, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is PhysicalKey && Equals((PhysicalKey)obj);
		public override int GetHashCode() => HashCode.Combine(PhysicalId, ControlPath);
		public override string ToString() => PhysicalId;
		public static bool operator ==(PhysicalKey left, PhysicalKey right) => left.Equals(right);
		public static bool operator !=(PhysicalKey left, PhysicalKey right) => !left.Equals(right);
	}

	internal static class StableIdRules {
		// Shader ledger IDs use stable lower-ascii hyphenated variant names
		// (for example shader.generator.solid-color).  Keep the existing
		// lowercase/vendor/category contract while allowing that persisted
		// spelling instead of silently rewriting IDs on import.
		private static readonly Regex NodeTypePattern = new Regex("^[a-z0-9_-]+(?:\\.[a-z0-9_-]+){2,}$", RegexOptions.CultureInvariant);
		private static readonly Regex ParameterPattern = new Regex("^[a-z0-9]+(?:_[a-z0-9]+)*(?:\\.[a-z0-9]+(?:_[a-z0-9]+)*)*$", RegexOptions.CultureInvariant);
		private static readonly Regex PortPattern = new Regex("^[a-z0-9]+(?:_[a-z0-9]+)*$", RegexOptions.CultureInvariant);
		public static bool IsUuidV4(string value) {
			if (!Guid.TryParseExact(value, "D", out _)) return false;
			var version = char.ToLowerInvariant(value[14]);
			var variant = char.ToLowerInvariant(value[19]);
			return version == '4' && (variant == '8' || variant == '9' || variant == 'a' || variant == 'b');
		}
		public static string Normalize(string value, string name) {
			if (value == null)
				throw new ArgumentNullException(name);

			var normalized = value.Trim();
			if (normalized.Length == 0)
				throw new ArgumentException("Stable IDs cannot be empty.", name);
			if (normalized.IndexOf('\0') >= 0)
				throw new ArgumentException("Stable IDs cannot contain NUL.", name);
			return normalized;
		}
		public static string NormalizeNodeType(string value, string name) {
			var normalized = Normalize(value, name);
			if (!IsSystemNodeType(normalized) && !NodeTypePattern.IsMatch(normalized)) throw new ArgumentException("NodeTypeId must be lower ASCII vendor.category.name, or a specified system node type.", name);
			return normalized;
		}

		private static bool IsSystemNodeType(string value) {
			switch (value) {
				case "system.program_output":
				case "system.preview":
				case "system.feedback":
				case "system.unknown_node":
					return true;
				default:
					return false;
			}
		}
		public static string NormalizeParameter(string value, string name) {
			var normalized = Normalize(value, name);
			if (!ParameterPattern.IsMatch(normalized)) throw new ArgumentException("ParameterId must use lower snake case segments separated by dots.", name);
			return normalized;
		}
		public static string NormalizePort(string value, string name) {
			var normalized = Normalize(value, name);
			if (!PortPattern.IsMatch(normalized)) throw new ArgumentException("PortId must use lower snake case.", name);
			return normalized;
		}
	}

	public readonly struct NodeInstanceId : IEquatable<NodeInstanceId>, IComparable<NodeInstanceId> {
		private readonly string _value;
		public string Value => _value ?? string.Empty;
		public bool IsEmpty => string.IsNullOrEmpty(_value);
		public NodeInstanceId(string value) => _value = StableIdRules.Normalize(value, nameof(value));
		public static NodeInstanceId New() => new NodeInstanceId(Guid.NewGuid().ToString("D"));
		public bool IsUuidV4 => StableIdRules.IsUuidV4(Value);
		public static bool TryParseUuidV4(string value, out NodeInstanceId id) { if (!TryParse(value, out id) || !id.IsUuidV4) { id = default; return false; } return true; }
		public static bool TryParse(string value, out NodeInstanceId id) {
			try { id = new NodeInstanceId(value); return true; }
			catch { id = default; return false; }
		}
		public bool Equals(NodeInstanceId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is NodeInstanceId other && Equals(other);
		public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
		public int CompareTo(NodeInstanceId other) => string.CompareOrdinal(Value, other.Value);
		public override string ToString() => Value;
		public static bool operator ==(NodeInstanceId left, NodeInstanceId right) => left.Equals(right);
		public static bool operator !=(NodeInstanceId left, NodeInstanceId right) => !left.Equals(right);
	}

	public readonly struct MediaAssetId : IEquatable<MediaAssetId>, IComparable<MediaAssetId> {
		private readonly string _value;
		public string Value => _value ?? string.Empty;
		public bool IsEmpty => string.IsNullOrEmpty(_value);
		public MediaAssetId(string value) => _value = StableIdRules.Normalize(value, nameof(value));
		public static MediaAssetId New() => new MediaAssetId(Guid.NewGuid().ToString("D"));
		public bool IsUuidV4 => StableIdRules.IsUuidV4(Value);
		public static bool TryParseUuidV4(string value, out MediaAssetId id) { if (!TryParse(value, out id) || !id.IsUuidV4) { id = default; return false; } return true; }
		public static bool TryParse(string value, out MediaAssetId id) {
			try { id = new MediaAssetId(value); return true; }
			catch { id = default; return false; }
		}
		public bool Equals(MediaAssetId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is MediaAssetId other && Equals(other);
		public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
		public int CompareTo(MediaAssetId other) => string.CompareOrdinal(Value, other.Value);
		public override string ToString() => Value;
		public static bool operator ==(MediaAssetId left, MediaAssetId right) => left.Equals(right);
		public static bool operator !=(MediaAssetId left, MediaAssetId right) => !left.Equals(right);
	}

	public readonly struct PresetId : IEquatable<PresetId>, IComparable<PresetId> {
		private readonly string _value;
		public string Value => _value ?? string.Empty;
		public bool IsEmpty => string.IsNullOrEmpty(_value);
		public PresetId(string value) => _value = StableIdRules.Normalize(value, nameof(value));
		public static PresetId New() => new PresetId(Guid.NewGuid().ToString("D"));
		public bool IsUuidV4 => StableIdRules.IsUuidV4(Value);
		public static bool TryParseUuidV4(string value, out PresetId id) { if (!TryParse(value, out id) || !id.IsUuidV4) { id = default; return false; } return true; }
		public static bool TryParse(string value, out PresetId id) {
			try { id = new PresetId(value); return true; }
			catch { id = default; return false; }
		}
		public bool Equals(PresetId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is PresetId other && Equals(other);
		public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
		public int CompareTo(PresetId other) => string.CompareOrdinal(Value, other.Value);
		public override string ToString() => Value;
		public static bool operator ==(PresetId left, PresetId right) => left.Equals(right);
		public static bool operator !=(PresetId left, PresetId right) => !left.Equals(right);
	}

	public readonly struct LogicalControlId : IEquatable<LogicalControlId>, IComparable<LogicalControlId> {
		private readonly string _value;
		public string Value => _value ?? string.Empty;
		public bool IsEmpty => string.IsNullOrEmpty(_value);
		public LogicalControlId(string value) => _value = StableIdRules.Normalize(value, nameof(value));
		public static LogicalControlId New() => new LogicalControlId(Guid.NewGuid().ToString("D"));
		public bool IsUuidV4 => StableIdRules.IsUuidV4(Value);
		public static bool TryParseUuidV4(string value, out LogicalControlId id) { if (!TryParse(value, out id) || !id.IsUuidV4) { id = default; return false; } return true; }
		public static bool TryParse(string value, out LogicalControlId id) {
			try { id = new LogicalControlId(value); return true; }
			catch { id = default; return false; }
		}
		public bool Equals(LogicalControlId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is LogicalControlId other && Equals(other);
		public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
		public int CompareTo(LogicalControlId other) => string.CompareOrdinal(Value, other.Value);
		public override string ToString() => Value;
		public static bool operator ==(LogicalControlId left, LogicalControlId right) => left.Equals(right);
		public static bool operator !=(LogicalControlId left, LogicalControlId right) => !left.Equals(right);
	}

	public readonly struct NodeTypeId : IEquatable<NodeTypeId>, IComparable<NodeTypeId> {
		private readonly string _value;
		public string Value => _value ?? string.Empty;
		public bool IsEmpty => string.IsNullOrEmpty(_value);
		public NodeTypeId(string value) => _value = StableIdRules.NormalizeNodeType(value, nameof(value));
		public static bool TryParse(string value, out NodeTypeId id) {
			try { id = new NodeTypeId(value); return true; }
			catch { id = default; return false; }
		}
		public bool Equals(NodeTypeId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is NodeTypeId other && Equals(other);
		public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
		public int CompareTo(NodeTypeId other) => string.CompareOrdinal(Value, other.Value);
		public override string ToString() => Value;
		public static bool operator ==(NodeTypeId left, NodeTypeId right) => left.Equals(right);
		public static bool operator !=(NodeTypeId left, NodeTypeId right) => !left.Equals(right);
	}

	public readonly struct ParameterId : IEquatable<ParameterId>, IComparable<ParameterId> {
		private readonly string _value;
		public string Value => _value ?? string.Empty;
		public bool IsEmpty => string.IsNullOrEmpty(_value);
		public ParameterId(string value) => _value = StableIdRules.NormalizeParameter(value, nameof(value));
		public static bool TryParse(string value, out ParameterId id) {
			try { id = new ParameterId(value); return true; }
			catch { id = default; return false; }
		}
		public bool Equals(ParameterId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is ParameterId other && Equals(other);
		public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
		public int CompareTo(ParameterId other) => string.CompareOrdinal(Value, other.Value);
		public override string ToString() => Value;
		public static bool operator ==(ParameterId left, ParameterId right) => left.Equals(right);
		public static bool operator !=(ParameterId left, ParameterId right) => !left.Equals(right);
	}

	public readonly struct PortId : IEquatable<PortId>, IComparable<PortId> {
		private readonly string _value;
		public string Value => _value ?? string.Empty;
		public bool IsEmpty => string.IsNullOrEmpty(_value);
		public PortId(string value) => _value = StableIdRules.NormalizePort(value, nameof(value));
		public static bool TryParse(string value, out PortId id) {
			try { id = new PortId(value); return true; }
			catch { id = default; return false; }
		}
		public bool Equals(PortId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is PortId other && Equals(other);
		public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
		public int CompareTo(PortId other) => string.CompareOrdinal(Value, other.Value);
		public override string ToString() => Value;
		public static bool operator ==(PortId left, PortId right) => left.Equals(right);
		public static bool operator !=(PortId left, PortId right) => !left.Equals(right);
	}

	public readonly struct ConnectionId : IEquatable<ConnectionId>, IComparable<ConnectionId> {
		private readonly string _value;
		public string Value => _value ?? string.Empty;
		public bool IsEmpty => string.IsNullOrEmpty(_value);
		public ConnectionId(string value) => _value = StableIdRules.Normalize(value, nameof(value));
		public static ConnectionId New() => new ConnectionId(Guid.NewGuid().ToString("D"));
		public static bool TryParse(string value, out ConnectionId id) {
			try { id = new ConnectionId(value); return true; }
			catch { id = default; return false; }
		}
		public bool Equals(ConnectionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is ConnectionId other && Equals(other);
		public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
		public int CompareTo(ConnectionId other) => string.CompareOrdinal(Value, other.Value);
		public override string ToString() => Value;
		public static bool operator ==(ConnectionId left, ConnectionId right) => left.Equals(right);
		public static bool operator !=(ConnectionId left, ConnectionId right) => !left.Equals(right);
	}

	public readonly struct ProjectSessionId : IEquatable<ProjectSessionId>, IComparable<ProjectSessionId> {
		private readonly string _value;
		public string Value => _value ?? string.Empty;
		public bool IsEmpty => string.IsNullOrEmpty(_value);
		public ProjectSessionId(string value) => _value = StableIdRules.Normalize(value, nameof(value));
		public static ProjectSessionId New() => new ProjectSessionId(Guid.NewGuid().ToString("D"));
		public bool Equals(ProjectSessionId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is ProjectSessionId other && Equals(other);
		public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
		public int CompareTo(ProjectSessionId other) => string.CompareOrdinal(Value, other.Value);
		public override string ToString() => Value;
		public static bool operator ==(ProjectSessionId left, ProjectSessionId right) => left.Equals(right);
		public static bool operator !=(ProjectSessionId left, ProjectSessionId right) => !left.Equals(right);
	}
}
