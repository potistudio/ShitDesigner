using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ShitDesigner.Presentation {
	public enum PlatformPathRequestKind { File, Folder, MultiFile }
	public sealed class PlatformPathRequest {
		public Guid RequestId { get; }
		public Guid ProjectSessionId { get; }
		public PlatformPathRequestKind Kind { get; }
		public string Title { get; }
		public PlatformPathRequest(Guid requestId, Guid projectSessionId, PlatformPathRequestKind kind, string title) { RequestId = requestId; ProjectSessionId = projectSessionId; Kind = kind; Title = title ?? string.Empty; }
	}

	public interface IPlatformFileInteractionAdapter {
		void PickPath(PlatformPathRequest request, Action<PlatformPathResult> completed);
		void Cancel(Guid requestId);
	}

	public sealed class PlatformPathResult {
		public Guid RequestId { get; }
		public Guid ProjectSessionId { get; }
		public bool Succeeded { get; }
		public IReadOnlyList<string> AbsolutePaths { get; }
		public string Error { get; }
		public PlatformPathResult(Guid requestId, Guid projectSessionId, bool succeeded, IEnumerable<string> absolutePaths = null, string error = null) {
			RequestId = requestId;
			ProjectSessionId = projectSessionId;
			Succeeded = succeeded;
			AbsolutePaths = new ReadOnlyCollection<string>((absolutePaths ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList());
			Error = error ?? string.Empty;
		}
	}

	public sealed class PlatformDropResult {
		public Guid ProjectSessionId { get; }
		public IReadOnlyList<string> AbsolutePaths { get; }
		public PlatformDropResult(Guid projectSessionId, IEnumerable<string> absolutePaths) { ProjectSessionId = projectSessionId; AbsolutePaths = new ReadOnlyCollection<string>((absolutePaths ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList()); }
	}

	public sealed class PanelDescriptor {
		public string PanelTypeId { get; }
		public string PanelInstanceId { get; }
		public int MinWidth { get; }
		public int MinHeight { get; }
		public PanelDescriptor(string panelTypeId, string panelInstanceId, int minWidth, int minHeight) {
			PanelTypeId = panelTypeId ?? string.Empty;
			PanelInstanceId = panelInstanceId ?? string.Empty;
			MinWidth = Math.Max(1, minWidth);
			MinHeight = Math.Max(1, minHeight);
		}
	}

	public interface IPanelFactory {
		object Create(string panelInstanceId);
	}

	public sealed class PanelErrorPlaceholder {
		public string PanelInstanceId { get; }
		public string Message { get; }
		public PanelErrorPlaceholder(string panelInstanceId, string message) { PanelInstanceId = panelInstanceId ?? string.Empty; Message = message ?? string.Empty; }
	}

	public sealed class PanelCatalog {
		private sealed class Entry {
			public PanelDescriptor Descriptor;
			public IPanelFactory Factory;
		}
		private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
		public IReadOnlyCollection<PanelDescriptor> Descriptors => new ReadOnlyCollection<PanelDescriptor>(_entries.Values.Select(x => x.Descriptor).ToList());
		public void Register(PanelDescriptor descriptor, IPanelFactory factory) {
			if (descriptor == null || string.IsNullOrWhiteSpace(descriptor.PanelInstanceId) || factory == null) throw new ArgumentException("Panel descriptor and factory are required.");
			if (_entries.ContainsKey(descriptor.PanelInstanceId)) throw new InvalidOperationException("Panel instance is already registered: " + descriptor.PanelInstanceId);
			_entries.Add(descriptor.PanelInstanceId, new Entry { Descriptor = descriptor, Factory = factory });
		}
		public object CreateOrPlaceholder(string panelInstanceId, IPresentationNoticeSink notices = null) {
			if (!_entries.TryGetValue(panelInstanceId ?? string.Empty, out var entry)) return new PanelErrorPlaceholder(panelInstanceId, "Unknown panel type is retained as an opaque placeholder.");
			try { return entry.Factory.Create(panelInstanceId); }
			catch (Exception exception) {
				notices?.Record(PresentationSeverity.Error, "presentation.panel_factory_failed", exception.Message, panelInstanceId);
				return new PanelErrorPlaceholder(panelInstanceId, exception.Message);
			}
		}
	}

	public sealed class PresentationNoticeSink : IPresentationNoticeSink {
		private readonly List<PresentationNotice> _notices = new List<PresentationNotice>();
		public IReadOnlyList<PresentationNotice> Notices => new ReadOnlyCollection<PresentationNotice>(_notices);
		public void Record(PresentationSeverity severity, string code, string message, string panelId = null) { _notices.Add(new PresentationNotice(Guid.NewGuid(), severity, code, message, panelId)); }
		public void Clear() => _notices.Clear();
	}

	public sealed class PresentationNotice {
		public Guid Id { get; }
		public PresentationSeverity Severity { get; }
		public string Code { get; }
		public string Message { get; }
		public string PanelId { get; }
		public PresentationNotice(Guid id, PresentationSeverity severity, string code, string message, string panelId) { Id = id; Severity = severity; Code = code ?? string.Empty; Message = message ?? string.Empty; PanelId = panelId ?? string.Empty; }
	}
}
