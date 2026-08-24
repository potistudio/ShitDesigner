using System;
using System.Collections.Generic;

namespace ShitDesigner.Tests.Shared {
	public readonly struct DiagnosticRecord {
		public DiagnosticRecord(string severity, string code, string message) {
			Severity = severity ?? string.Empty;
			Code = code ?? string.Empty;
			Message = message ?? string.Empty;
		}

		public string Severity { get; }
		public string Code { get; }
		public string Message { get; }
	}

	public sealed class RecordingDiagnosticSink {
		private readonly List<DiagnosticRecord> _records = new List<DiagnosticRecord>();

		public IReadOnlyList<DiagnosticRecord> Records => _records;

		public void Record(string severity, string code, string message) {
			_records.Add(new DiagnosticRecord(severity, code, message));
		}

		public void Clear() {
			_records.Clear();
		}
	}
}
