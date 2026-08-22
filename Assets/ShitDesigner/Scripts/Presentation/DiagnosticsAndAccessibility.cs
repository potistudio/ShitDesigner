using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ShitDesigner.Presentation
{
    public sealed class DiagnosticFilter
    {
        public PresentationSeverity? Severity { get; set; }
        public string NodeId { get; set; }
        public string Code { get; set; }
        public bool Matches(DiagnosticReadModel diagnostic)
        {
            if (diagnostic == null) return false;
            return (!Severity.HasValue || diagnostic.Severity == Severity.Value)
                && (string.IsNullOrEmpty(NodeId) || string.Equals(NodeId, diagnostic.NodeId, StringComparison.Ordinal))
                && (string.IsNullOrEmpty(Code) || string.Equals(Code, diagnostic.Code, StringComparison.Ordinal));
        }
    }

    public sealed class DiagnosticPresenter
    {
        public IReadOnlyList<DiagnosticReadModel> Filter(IEnumerable<DiagnosticReadModel> diagnostics, DiagnosticFilter filter)
        {
            return (diagnostics ?? Enumerable.Empty<DiagnosticReadModel>()).Where(x => filter == null || filter.Matches(x)).ToList();
        }
        public IReadOnlyList<DiagnosticReadModel> Aggregate(IEnumerable<DiagnosticReadModel> diagnostics)
        {
            return (diagnostics ?? Enumerable.Empty<DiagnosticReadModel>()).GroupBy(x => (x.Code ?? string.Empty) + "\u001f" + (x.NodeId ?? string.Empty))
                .Select(group =>
                {
                    var first = group.First();
                    return new DiagnosticReadModel(first.EntryId, first.Severity, first.Code, first.Message, first.NodeId, group.Sum(x => x.Count), group.Any(x => x.IsCurrent));
                }).ToList();
        }
        public string ExportText(IEnumerable<DiagnosticReadModel> diagnostics)
        {
            var builder = new StringBuilder();
            foreach (var diagnostic in diagnostics ?? Enumerable.Empty<DiagnosticReadModel>())
                builder.Append(diagnostic.Severity).Append('\t').Append(diagnostic.Code).Append('\t').Append(diagnostic.Count).Append('\t').AppendLine(diagnostic.Message.Replace("\r", " ").Replace("\n", " "));
            return builder.ToString();
        }
        public string ExportJson(IEnumerable<DiagnosticReadModel> diagnostics)
        {
            var values = (diagnostics ?? Enumerable.Empty<DiagnosticReadModel>()).Select(x => "{\"id\":\"" + Escape(x.EntryId) + "\",\"severity\":\"" + Escape(x.Severity.ToString()) + "\",\"code\":\"" + Escape(x.Code) + "\",\"count\":" + x.Count + ",\"message\":\"" + Escape(x.Message) + "\"}");
            return "[" + string.Join(",", values) + "]";
        }
        private static string Escape(string value) => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n");
    }

    public enum DialogKind { None, NewProject, OpenProject, RecentProjects, SaveAs, CloseProject, Exit, Recovered, ReadFailure, Settings }
    public enum UnsavedDecision { Cancel, Save, Discard }
    public sealed class DialogState
    {
        public DialogKind Kind { get; private set; }
        public string Reason { get; private set; }
        public bool IsOpen => Kind != DialogKind.None;
        public void Open(DialogKind kind, string reason = null) { Kind = kind; Reason = reason ?? string.Empty; }
        public void Close() { Kind = DialogKind.None; Reason = string.Empty; }
    }

    public enum PrimaryModifier { Control, Command }
    public enum TooltipDelay { Short = 250, Medium = 500, Long = 1000 }

    public sealed class AccessibilitySettings
    {
        public float TextScale { get; private set; } = 1f;
        public float UiScale { get; private set; } = 1f;
        public bool ReduceMotion { get; private set; }
        public TooltipDelay TooltipDelay { get; private set; } = TooltipDelay.Medium;
        public void SetTextScale(float scale) { TextScale = Clamp(scale, .8f, 2f); }
        public void SetUiScale(float scale) { UiScale = Clamp(scale, .8f, 2f); }
        public void SetReduceMotion(bool enabled) { ReduceMotion = enabled; }
        public void SetTooltipDelay(TooltipDelay delay) { TooltipDelay = delay; }
        private static float Clamp(float value, float min, float max) => float.IsNaN(value) || float.IsInfinity(value) ? 1f : value < min ? min : value > max ? max : value;
    }

    public enum PresentationKey { Escape, Tab, Delete, F, G, A, Z, Y, S, O, N, P }
    public readonly struct ShortcutBinding
    {
        public PresentationKey Key { get; }
        public bool Primary { get; }
        public bool Shift { get; }
        public bool Alt { get; }
        public string CommandId { get; }
        public ShortcutBinding(PresentationKey key, string commandId, bool primary = false, bool shift = false, bool alt = false)
        { Key = key; CommandId = commandId ?? string.Empty; Primary = primary; Shift = shift; Alt = alt; }
    }

    public sealed class ShortcutRouter
    {
        private readonly List<ShortcutBinding> _bindings = new List<ShortcutBinding>();
        public void Register(ShortcutBinding binding) { _bindings.Add(binding); }
        public string Resolve(PresentationKey key, bool primary, bool shift, bool alt, bool textInputFocused, bool graphFocused, bool modalOpen)
        {
            if (modalOpen && key != PresentationKey.Escape) return null;
            if (textInputFocused && !IsEditingSafe(key)) return null;
            var binding = _bindings.FirstOrDefault(x => x.Key == key && x.Primary == primary && x.Shift == shift && x.Alt == alt);
            if (binding.CommandId.Length == 0) return null;
            if (!graphFocused && !binding.Primary && !binding.Shift && !binding.Alt && (key == PresentationKey.Delete || key == PresentationKey.G || key == PresentationKey.A || key == PresentationKey.F)) return null;
            return binding.CommandId;
        }
        private static bool IsEditingSafe(PresentationKey key) => key == PresentationKey.Escape || key == PresentationKey.Tab;
    }

    public sealed class PresentationSessionState
    {
        public Guid ProjectSessionId { get; private set; }
        public string SelectedNodeId { get; private set; }
        public string FocusedPanelInstanceId { get; private set; }
        public string SearchText { get; private set; } = string.Empty;
        public bool IsProjectScopeActive => ProjectSessionId != Guid.Empty;
        public void Bind(Guid projectSessionId) { ProjectSessionId = projectSessionId; SelectedNodeId = null; SearchText = string.Empty; }
        public void ClearProjectScope() { ProjectSessionId = Guid.Empty; SelectedNodeId = null; SearchText = string.Empty; }
        public void SelectNode(string nodeId) { SelectedNodeId = nodeId; }
        public void FocusPanel(string panelId) { FocusedPanelInstanceId = panelId; }
        public void SetSearch(string text) { SearchText = text ?? string.Empty; }
    }
}
