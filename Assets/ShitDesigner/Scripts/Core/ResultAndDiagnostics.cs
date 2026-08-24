using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text.RegularExpressions;
using CSharpFunctionalExtensions;

namespace ShitDesigner.Core {
	public readonly struct Result {
		private readonly UnitResult<Diagnostic> _result;
		private readonly bool _initialized;
		public bool IsSuccess => _initialized && _result.IsSuccess;
		public bool IsFailure => !IsSuccess;
		public Diagnostic Diagnostic => IsFailure && _initialized ? _result.Error : null;
		private Result(UnitResult<Diagnostic> result) { _result = result; _initialized = true; }
		public static Result Success() => new Result(CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>());
		public static Result Failure(Diagnostic diagnostic) => new Result(CSharpFunctionalExtensions.UnitResult.Failure<Diagnostic>(diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))));
	}

	public readonly struct Result<T> {
		private readonly CSharpFunctionalExtensions.Result<T, Diagnostic> _result;
		private readonly bool _initialized;
		public bool IsSuccess => _initialized && _result.IsSuccess;
		public bool IsFailure => !IsSuccess;
		public T Value => IsSuccess ? _result.Value : throw new InvalidOperationException("Result has no value.");
		public Diagnostic Diagnostic => IsFailure && _initialized ? _result.Error : null;
		private Result(CSharpFunctionalExtensions.Result<T, Diagnostic> result) { _result = result; _initialized = true; }
		public static Result<T> Success(T value) => new Result<T>(CSharpFunctionalExtensions.Result.Success<T, Diagnostic>(value));
		public static Result<T> Failure(Diagnostic diagnostic) => new Result<T>(CSharpFunctionalExtensions.Result.Failure<T, Diagnostic>(diagnostic ?? throw new ArgumentNullException(nameof(diagnostic))));
	}

	public enum Severity {
		Info,
		Warning,
		Error,
		Fatal
	}

	public readonly struct DiagnosticCode : IEquatable<DiagnosticCode>, IComparable<DiagnosticCode> {
		private static readonly Regex Pattern = new Regex("^[a-z0-9_]+(?:\\.[a-z0-9_]+)+$", RegexOptions.CultureInvariant);
		private readonly string _value;
		public string Value => _value ?? string.Empty;
		public DiagnosticCode(string value) {
			if (string.IsNullOrWhiteSpace(value) || !Pattern.IsMatch(value))
				throw new ArgumentException("DiagnosticCode must use lower ASCII module.category.reason form.", nameof(value));
			_value = value;
		}
		public bool Equals(DiagnosticCode other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is DiagnosticCode other && Equals(other);
		public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value);
		public int CompareTo(DiagnosticCode other) => string.CompareOrdinal(Value, other.Value);
		public override string ToString() => Value;
		public static bool operator ==(DiagnosticCode left, DiagnosticCode right) => left.Equals(right);
		public static bool operator !=(DiagnosticCode left, DiagnosticCode right) => !left.Equals(right);
	}

	public sealed class DiagnosticDetail {
		private readonly IReadOnlyDictionary<string, string> _fields;
		public IReadOnlyDictionary<string, string> Fields => _fields;
		public DiagnosticDetail(IEnumerable<KeyValuePair<string, string>> fields = null) {
			var source = fields ?? Enumerable.Empty<KeyValuePair<string, string>>();
			var copy = new SortedDictionary<string, string>(StringComparer.Ordinal);
			foreach (var field in source) {
				if (string.IsNullOrWhiteSpace(field.Key))
					throw new ArgumentException("Diagnostic detail field names cannot be empty.", nameof(fields));
				copy[field.Key] = field.Value ?? string.Empty;
			}
			_fields = new ReadOnlyDictionary<string, string>(copy);
		}
		public string Get(string key) => _fields.TryGetValue(key, out var value) ? value : null;
	}

	public sealed class DiagnosticExceptionInfo {
		public string TypeName { get; }
		public string Message { get; }
		public string StackTrace { get; }
		public IReadOnlyList<DiagnosticExceptionInfo> InnerExceptions { get; }
		public DiagnosticExceptionInfo(string typeName, string message, string stackTrace, IEnumerable<DiagnosticExceptionInfo> innerExceptions = null) {
			TypeName = typeName ?? string.Empty;
			Message = message ?? string.Empty;
			StackTrace = stackTrace ?? string.Empty;
			InnerExceptions = new ReadOnlyCollection<DiagnosticExceptionInfo>((innerExceptions ?? Enumerable.Empty<DiagnosticExceptionInfo>()).ToList());
		}
		public static DiagnosticExceptionInfo FromException(Exception exception) {
			if (exception == null) throw new ArgumentNullException(nameof(exception));
			var inner = exception.InnerException == null ? null : new[] { FromException(exception.InnerException) };
			return new DiagnosticExceptionInfo(exception.GetType().FullName, exception.Message, exception.StackTrace, inner);
		}
	}

	public sealed class Diagnostic {
		public DiagnosticCode Code { get; }
		public Severity Severity { get; }
		public string ScopeId { get; }
		public NodeInstanceId? NodeId { get; }
		public NodeTypeId? NodeTypeId { get; }
		public ulong? GenerationId { get; }
		public PortId? PortId { get; }
		public ParameterId? ParameterId { get; }
		public string Message { get; }
		public DiagnosticDetail Detail { get; }
		public long FrameNumber { get; }
		public double GraphClockTime { get; }
		public string Module { get; }
		public DiagnosticExceptionInfo Exception { get; }
		public IReadOnlyList<DiagnosticCode> RelatedCodes { get; }

		public Diagnostic(
			DiagnosticCode code,
			Severity severity,
			string message,
			string scopeId = null,
			NodeInstanceId? nodeId = null,
			NodeTypeId? nodeTypeId = null,
			ulong? generationId = null,
			PortId? portId = null,
			ParameterId? parameterId = null,
			DiagnosticDetail detail = null,
			long frameNumber = 0,
			double graphClockTime = 0,
			string module = null,
			DiagnosticExceptionInfo exception = null,
			IEnumerable<DiagnosticCode> relatedCodes = null) {
			Code = code;
			Severity = severity;
			Message = message ?? string.Empty;
			ScopeId = scopeId;
			NodeId = nodeId;
			NodeTypeId = nodeTypeId;
			GenerationId = generationId;
			PortId = portId;
			ParameterId = parameterId;
			Detail = detail ?? new DiagnosticDetail();
			FrameNumber = frameNumber;
			GraphClockTime = graphClockTime;
			Module = module ?? code.Value.Split('.')[0];
			Exception = exception;
			RelatedCodes = new ReadOnlyCollection<DiagnosticCode>((relatedCodes ?? Enumerable.Empty<DiagnosticCode>()).ToList());
		}
		public Diagnostic WithFrame(long frame, double clock) => new Diagnostic(Code, Severity, Message, ScopeId, NodeId, NodeTypeId, GenerationId, PortId, ParameterId, Detail, frame, clock, Module, Exception, RelatedCodes);
	}

	public readonly struct CurrentConditionKey : IEquatable<CurrentConditionKey> {
		public string ScopeId { get; }
		public string SubjectKind { get; }
		public string SubjectId { get; }
		public ulong? GenerationId { get; }
		public DiagnosticCode Code { get; }
		public string PortOrParameterId { get; }
		public CurrentConditionKey(string scopeId, string subjectKind, string subjectId, DiagnosticCode code, ulong? generationId = null, string portOrParameterId = null) {
			ScopeId = scopeId ?? string.Empty;
			SubjectKind = subjectKind ?? string.Empty;
			SubjectId = subjectId ?? string.Empty;
			GenerationId = generationId;
			Code = code;
			PortOrParameterId = portOrParameterId;
		}
		public bool Equals(CurrentConditionKey other) => string.Equals(ScopeId, other.ScopeId, StringComparison.Ordinal) && string.Equals(SubjectKind, other.SubjectKind, StringComparison.Ordinal) && string.Equals(SubjectId, other.SubjectId, StringComparison.Ordinal) && GenerationId == other.GenerationId && Code == other.Code && string.Equals(PortOrParameterId, other.PortOrParameterId, StringComparison.Ordinal);
		public override bool Equals(object obj) => obj is CurrentConditionKey other && Equals(other);
		public override int GetHashCode() => HashCode.Combine(ScopeId, SubjectKind, SubjectId, GenerationId, Code, PortOrParameterId);
	}
}
