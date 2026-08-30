using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ShitDesigner.Presentation {
	public enum ParameterControlKind { Slider, Numeric, Toggle, Text, Color, Vector, Enum, Media, ReadOnly, Broken }

	public sealed class ParameterMetadata {
		public string ParameterId { get; }
		public string DisplayName { get; }
		public ParameterControlKind ControlKind { get; }
		public bool IsHidden { get; }
		public bool IsReadOnly { get; }
		public double? Min { get; }
		public double? Max { get; }
		public double? Step { get; }
		public string Unit { get; }
		public string Group { get; }
		public int Order { get; }
		public string Description { get; }
		public string NodeTypeId { get; }
		public IReadOnlyList<ParameterComponentRangeReadModel> ComponentRanges { get; }
		public IReadOnlyList<ParameterOptionReadModel> EnumOptions { get; }
		public IReadOnlyList<string> MediaOptions { get; }
		public ParameterMetadata(string parameterId, string displayName, ParameterControlKind controlKind,
			bool isHidden = false, bool isReadOnly = false, double? min = null, double? max = null, double? step = null, string unit = null,
			string nodeTypeId = null, IEnumerable<ParameterOptionReadModel> enumOptions = null, IEnumerable<string> mediaOptions = null,
			string group = null, int order = 0, string description = null, IEnumerable<ParameterComponentRangeReadModel> componentRanges = null) {
			ParameterId = parameterId ?? string.Empty;
			DisplayName = displayName ?? parameterId ?? string.Empty;
			ControlKind = controlKind;
			IsHidden = isHidden;
			IsReadOnly = isReadOnly;
			Min = min;
			Max = max;
			Step = step;
			Unit = unit ?? string.Empty;
			Group = group ?? string.Empty;
			Order = order;
			Description = description ?? string.Empty;
			NodeTypeId = nodeTypeId ?? string.Empty;
			ComponentRanges = new ReadOnlyCollection<ParameterComponentRangeReadModel>((componentRanges ?? Enumerable.Empty<ParameterComponentRangeReadModel>()).ToList());
			EnumOptions = new ReadOnlyCollection<ParameterOptionReadModel>((enumOptions ?? Enumerable.Empty<ParameterOptionReadModel>()).ToList());
			MediaOptions = new ReadOnlyCollection<string>((mediaOptions ?? Enumerable.Empty<string>()).ToList());
		}
	}

	public interface IParameterControlFactory {
		object Create(ParameterMetadata metadata, ParameterReadModel value);
	}

	public sealed class ParameterControlCatalog {
		private readonly Dictionary<ParameterControlKind, IParameterControlFactory> _factories = new Dictionary<ParameterControlKind, IParameterControlFactory>();
		private readonly Dictionary<string, IParameterControlFactory> _nodeFactories = new Dictionary<string, IParameterControlFactory>(StringComparer.Ordinal);
		public void Register(ParameterControlKind kind, IParameterControlFactory factory) {
			if (factory == null) throw new ArgumentNullException(nameof(factory));
			if (_factories.ContainsKey(kind)) throw new InvalidOperationException("Parameter factory is already registered: " + kind);
			_factories.Add(kind, factory);
		}
		public void RegisterNodeType(string nodeTypeId, IParameterControlFactory factory) {
			if (string.IsNullOrWhiteSpace(nodeTypeId) || factory == null) throw new ArgumentException("Node type and factory are required.");
			if (_nodeFactories.ContainsKey(nodeTypeId)) throw new InvalidOperationException("Parameter factory is already registered for node type: " + nodeTypeId);
			_nodeFactories.Add(nodeTypeId, factory);
		}
		/// <summary>Resolves only an explicitly registered node factory.  The
		/// standard visual factories are owned by the composition instance,
		/// so this keeps custom registrations long-lived without capturing a
		/// stale panel or coordinator.</summary>
		public bool TryCreateNodeType(string nodeTypeId, ParameterMetadata metadata, ParameterReadModel value, IPresentationNoticeSink notices, out object result) {
			result = null;
			if (string.IsNullOrWhiteSpace(nodeTypeId) || !_nodeFactories.TryGetValue(nodeTypeId, out var factory)) return false;
			try {
				result = factory.Create(metadata, value);
				return result != null;
			}
			catch (Exception exception) {
				notices?.Record(PresentationSeverity.Error, "presentation.parameter_factory_failed", exception.Message, metadata?.ParameterId);
				return false;
			}
		}
		public object CreateOrFallback(ParameterMetadata metadata, ParameterReadModel value, IPresentationNoticeSink notices = null) {
			if (metadata == null) throw new ArgumentNullException(nameof(metadata));
			if (metadata.IsHidden) return null;
			var kind = metadata.IsReadOnly ? ParameterControlKind.ReadOnly : metadata.ControlKind;
			IParameterControlFactory factory = null;
			if (!metadata.IsReadOnly) _nodeFactories.TryGetValue(metadata.NodeTypeId ?? string.Empty, out factory);
			if (factory == null) _factories.TryGetValue(kind, out factory);
			if (factory == null)
				return _factories.TryGetValue(ParameterControlKind.ReadOnly, out var standard) ? standard.Create(metadata, value) : null;
			try { return factory.Create(metadata, value); }
			catch (Exception exception) {
				notices?.Record(PresentationSeverity.Error, "presentation.parameter_factory_failed", exception.Message, metadata.ParameterId);
				if (_factories.TryGetValue(ParameterControlKind.ReadOnly, out var fallback)) return fallback.Create(metadata, value);
				return null;
			}
		}
	}

	public sealed class ExpressionDraft {
		public Guid ProjectSessionId { get; }
		public long DocumentRevision { get; }
		public string NodeId { get; }
		public string ParameterId { get; }
		public string MinExpression { get; private set; }
		public string MaxExpression { get; private set; }
		public bool IsPending { get; private set; }
		public string Error { get; private set; }
		public ExpressionDraft(Guid projectSessionId, long documentRevision, string nodeId, string parameterId, string minExpression, string maxExpression) {
			ProjectSessionId = projectSessionId;
			DocumentRevision = documentRevision;
			NodeId = nodeId ?? string.Empty;
			ParameterId = parameterId ?? string.Empty;
			MinExpression = minExpression ?? string.Empty;
			MaxExpression = maxExpression ?? string.Empty;
		}
		public void Edit(string minExpression, string maxExpression) { MinExpression = minExpression ?? string.Empty; MaxExpression = maxExpression ?? string.Empty; Error = string.Empty; }
		public PresentationCommandRequest Apply() {
			IsPending = true;
			return new PresentationCommandRequest(ProjectSessionId, Guid.NewGuid(), Guid.NewGuid(), DocumentRevision, NodeId + ":" + ParameterId,
				"parameter.apply_expression", new[] { new KeyValuePair<string, string>("min", MinExpression), new KeyValuePair<string, string>("max", MaxExpression) });
		}
		public void Reject(string error) { IsPending = false; Error = error ?? "Expression was rejected."; }
		public void Applied() { IsPending = false; Error = string.Empty; }
	}

	public sealed class DashboardValidation {
		public bool IsValid { get; }
		public IReadOnlyList<string> Errors { get; }
		internal DashboardValidation(bool valid, IEnumerable<string> errors) { IsValid = valid; Errors = new ReadOnlyCollection<string>((errors ?? Enumerable.Empty<string>()).ToList()); }
	}

	public static class DashboardLayoutValidator {
		public const int Columns = 12;
		public static DashboardValidation Validate(IEnumerable<DashboardWidgetReadModel> widgets) {
			var errors = new List<string>();
			var occupied = new List<DashboardWidgetReadModel>();
			foreach (var widget in widgets ?? Enumerable.Empty<DashboardWidgetReadModel>()) {
				if (widget.Column < 0 || widget.Column + widget.Width > Columns) errors.Add(widget.Id + ": outside 12-column grid");
				if (widget.Row < 0 || widget.Width <= 0 || widget.Height <= 0) errors.Add(widget.Id + ": invalid size");
				if (occupied.Any(x => x.Id != widget.Id && x.Column < widget.Column + widget.Width && x.Column + x.Width > widget.Column && x.Row < widget.Row + widget.Height && x.Row + x.Height > widget.Row))
					errors.Add(widget.Id + ": overlaps another widget");
				occupied.Add(widget);
			}
			return new DashboardValidation(errors.Count == 0, errors);
		}
	}

	public sealed class DashboardSession {
		private readonly Dictionary<string, DashboardWidgetReadModel> _candidate = new Dictionary<string, DashboardWidgetReadModel>(StringComparer.Ordinal);
		public bool IsArranging { get; private set; }
		public IReadOnlyCollection<DashboardWidgetReadModel> Candidate => new ReadOnlyCollection<DashboardWidgetReadModel>(_candidate.Values.ToList());
		public void Begin(IEnumerable<DashboardWidgetReadModel> widgets) { IsArranging = true; _candidate.Clear(); foreach (var widget in widgets ?? Enumerable.Empty<DashboardWidgetReadModel>()) _candidate[widget.Id] = widget; }
		public void Set(DashboardWidgetReadModel widget) { if (!IsArranging) throw new InvalidOperationException("Dashboard arrangement is not active."); _candidate[widget.Id] = widget; }
		public bool TryCommit(out DashboardValidation validation) {
			validation = DashboardLayoutValidator.Validate(_candidate.Values);
			if (!validation.IsValid) { Cancel(); return false; }
			IsArranging = false;
			return true;
		}
		public void Cancel() { IsArranging = false; _candidate.Clear(); }
	}
}
