using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text;

namespace ShitDesigner.Presentation
{
    public enum DockAxis { Horizontal, Vertical }
    public enum DockDropPosition { Left, Right, Top, Bottom, Center }

    public abstract class DockNode
    {
        public abstract string Kind { get; }
        internal abstract void CollectPanels(ISet<string> panels, IList<string> errors, ISet<string> seen);
        internal abstract void CollectUnknownPanels(ISet<string> panels);
        internal abstract DockNode Copy();
    }

    public sealed class DockEmpty : DockNode
    {
        public override string Kind => "Empty";
        internal override void CollectPanels(ISet<string> panels, IList<string> errors, ISet<string> seen) { }
        internal override void CollectUnknownPanels(ISet<string> panels) { }
        internal override DockNode Copy() => new DockEmpty();
    }

    public sealed class DockTabGroup : DockNode
    {
        public IReadOnlyList<string> PanelInstanceIds { get; }
        public string ActivePanelInstanceId { get; }
        public IReadOnlyList<UnknownPanelPlaceholder> UnknownPanels { get; }
        public override string Kind => "TabGroup";

        public DockTabGroup(IEnumerable<string> panelInstanceIds, string activePanelInstanceId, IEnumerable<UnknownPanelPlaceholder> unknownPanels = null)
        {
            var ids = (panelInstanceIds ?? Enumerable.Empty<string>()).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            PanelInstanceIds = new ReadOnlyCollection<string>(ids);
            ActivePanelInstanceId = activePanelInstanceId ?? string.Empty;
            UnknownPanels = new ReadOnlyCollection<UnknownPanelPlaceholder>((unknownPanels ?? Enumerable.Empty<UnknownPanelPlaceholder>()).Where(x => x != null).Select(x => new UnknownPanelPlaceholder(x.PanelTypeId, x.PanelInstanceId, x.RawPayload, x.OriginalLocation)).ToList());
        }

        internal override void CollectPanels(ISet<string> panels, IList<string> errors, ISet<string> seen)
        {
            if (PanelInstanceIds.Count == 0) errors.Add("A TabGroup must contain a panel.");
            if (string.IsNullOrWhiteSpace(ActivePanelInstanceId) || !PanelInstanceIds.Contains(ActivePanelInstanceId))
                errors.Add("TabGroup active panel is missing.");
            foreach (var id in PanelInstanceIds)
            {
                if (!seen.Add(id)) errors.Add("Panel instance is duplicated: " + id);
                panels.Add(id);
            }
            var unknownIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var unknown in UnknownPanels)
            {
                if (string.IsNullOrWhiteSpace(unknown.PanelInstanceId) || !PanelInstanceIds.Contains(unknown.PanelInstanceId)) errors.Add("Unknown panel record is not attached to this tab group.");
                if (!unknownIds.Add(unknown.PanelInstanceId)) errors.Add("Unknown panel record is duplicated: " + unknown.PanelInstanceId);
            }
        }

        internal override void CollectUnknownPanels(ISet<string> panels)
        {
            foreach (var unknown in UnknownPanels) panels.Add(unknown.PanelInstanceId);
        }

        internal override DockNode Copy() => new DockTabGroup(PanelInstanceIds, ActivePanelInstanceId, UnknownPanels);
    }

    public sealed class DockSplit : DockNode
    {
        public DockAxis Axis { get; }
        public float Ratio { get; }
        public DockNode First { get; }
        public DockNode Second { get; }
        public override string Kind => "Split";

        public DockSplit(DockAxis axis, float ratio, DockNode first, DockNode second)
        {
            Axis = axis;
            Ratio = ratio;
            First = first ?? new DockEmpty();
            Second = second ?? new DockEmpty();
        }

        internal override void CollectPanels(ISet<string> panels, IList<string> errors, ISet<string> seen)
        {
            if (float.IsNaN(Ratio) || float.IsInfinity(Ratio) || Ratio <= 0f || Ratio >= 1f)
                errors.Add("Split ratio must be finite and between zero and one.");
            First.CollectPanels(panels, errors, seen);
            Second.CollectPanels(panels, errors, seen);
        }

        internal override void CollectUnknownPanels(ISet<string> panels)
        {
            First.CollectUnknownPanels(panels);
            Second.CollectUnknownPanels(panels);
        }

        internal override DockNode Copy() => new DockSplit(Axis, Ratio, First.Copy(), Second.Copy());
    }

    public sealed class DockTree
    {
        public DockNode Root { get; }
        public DockTree(DockNode root) { Root = root ?? new DockEmpty(); }
        public DockTree Copy() => new DockTree(Root.Copy());
        public DockLayoutValidation Validate(ISet<string> knownPanelInstances = null)
        {
            var errors = new List<string>();
            var panels = new HashSet<string>(StringComparer.Ordinal);
            Root.CollectPanels(panels, errors, new HashSet<string>(StringComparer.Ordinal));
            var unknownPanels = new HashSet<string>(StringComparer.Ordinal);
            Root.CollectUnknownPanels(unknownPanels);
            if (knownPanelInstances != null)
                foreach (var panel in panels.Where(x => !knownPanelInstances.Contains(x)))
                    if (!unknownPanels.Contains(panel)) errors.Add("Unknown panel instance: " + panel);
            return new DockLayoutValidation(errors.Count == 0, panels, errors);
        }
    }

    /// <summary>
    /// Stable presentation-boundary representation for a draft DockTree.
    /// The Application adapter carries this opaque string in a command; the
    /// user-settings port decodes it before validation and never receives UI
    /// objects.  It intentionally preserves every panel instance id,
    /// including ids unknown to the current panel catalog.
    /// </summary>
    public static class DockTreeCodec
    {
        public static string Encode(DockTree tree) => tree == null ? string.Empty : EncodeNode(tree.Root);

        public static bool TryDecode(string payload, out DockTree tree)
        {
            tree = null;
            try
            {
                var node = DecodeNode(payload ?? string.Empty);
                if (node == null) return false;
                var candidate = new DockTree(node);
                if (!candidate.Validate().IsValid) return false;
                tree = candidate;
                return true;
            }
            catch { return false; }
        }

        private static string EncodeNode(DockNode node)
        {
            if (node is DockEmpty) return "E";
            if (node is DockTabGroup tabs)
                return "T|" + B64(tabs.ActivePanelInstanceId) + "|" + B64(string.Join("\n", tabs.PanelInstanceIds)) + "|" + B64(string.Join("\n", tabs.UnknownPanels.Select(x => string.Join("\t", B64(x.PanelInstanceId), B64(x.PanelTypeId), B64(x.RawPayload), B64(x.OriginalLocation)))));
            if (node is DockSplit split)
                return "S|" + (split.Axis == DockAxis.Horizontal ? "H" : "V") + "|" + split.Ratio.ToString(CultureInfo.InvariantCulture) + "|" + B64(EncodeNode(split.First)) + "|" + B64(EncodeNode(split.Second));
            return string.Empty;
        }

        private static DockNode DecodeNode(string text)
        {
            if (string.Equals(text, "E", StringComparison.Ordinal)) return new DockEmpty();
            var fields = (text ?? string.Empty).Split('|');
            if ((fields.Length == 3 || fields.Length == 4) && fields[0] == "T")
            {
                var panels = UB64(fields[2]).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
                var unknown = fields.Length == 4 ? DecodeUnknown(UB64(fields[3])) : Enumerable.Empty<UnknownPanelPlaceholder>();
                return new DockTabGroup(panels, UB64(fields[1]), unknown);
            }
            if (fields.Length == 5 && fields[0] == "S" && (fields[1] == "H" || fields[1] == "V") && float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio))
                return new DockSplit(fields[1] == "H" ? DockAxis.Horizontal : DockAxis.Vertical, ratio, DecodeNode(UB64(fields[3])), DecodeNode(UB64(fields[4])));
            return null;
        }

        private static string B64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        private static string UB64(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
        private static IEnumerable<UnknownPanelPlaceholder> DecodeUnknown(string payload)
        {
            foreach (var line in (payload ?? string.Empty).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var fields = line.Split('\t');
                if (fields.Length != 4) throw new FormatException("Invalid unknown panel payload.");
                yield return new UnknownPanelPlaceholder(UB64(fields[1]), UB64(fields[0]), UB64(fields[2]), UB64(fields[3]));
            }
        }
    }

    public sealed class DockLayoutValidation
    {
        public bool IsValid { get; }
        public IReadOnlyCollection<string> PanelInstanceIds { get; }
        public IReadOnlyList<string> Errors { get; }
        internal DockLayoutValidation(bool valid, IEnumerable<string> panels, IEnumerable<string> errors)
        {
            IsValid = valid;
            PanelInstanceIds = new ReadOnlyCollection<string>((panels ?? Enumerable.Empty<string>()).ToList());
            Errors = new ReadOnlyCollection<string>((errors ?? Enumerable.Empty<string>()).ToList());
        }
    }

    public sealed class UnknownPanelPlaceholder
    {
        public string PanelTypeId { get; }
        public string PanelInstanceId { get; }
        public string RawPayload { get; }
        public string OriginalLocation { get; }
        public UnknownPanelPlaceholder(string panelTypeId, string panelInstanceId, string rawPayload, string originalLocation)
        {
            PanelTypeId = panelTypeId ?? string.Empty;
            PanelInstanceId = panelInstanceId ?? string.Empty;
            RawPayload = rawPayload ?? string.Empty;
            OriginalLocation = originalLocation ?? string.Empty;
        }
    }

    /// <summary>Candidate layout edits are isolated until a validated drop.</summary>
    public sealed class DockLayoutSession
    {
        private DockTree _current;
        private DockTree _candidate;
        public DockTree Current => _current;
        public DockTree Candidate => _candidate;
        public bool IsDragging { get; private set; }
        public bool IsDirty { get; private set; }
        public string CurrentPresetId { get; private set; }

        public DockLayoutSession(DockTree initial, string presetId = "Edit")
        {
            _current = initial ?? throw new ArgumentNullException(nameof(initial));
            CurrentPresetId = presetId ?? string.Empty;
        }

        public void BeginDrag() { IsDragging = true; _candidate = _current.Copy(); }
        public void SetCandidate(DockTree candidate)
        {
            if (!IsDragging) throw new InvalidOperationException("A candidate exists only during a drag.");
            _candidate = candidate ?? throw new ArgumentNullException(nameof(candidate));
        }

        public bool TryCommitCandidate(ISet<string> knownPanels, out DockLayoutValidation validation)
        {
            validation = (_candidate ?? _current).Validate(knownPanels);
            if (!IsDragging || !validation.IsValid) { CancelDrag(); return false; }
            _current = _candidate.Copy();
            _candidate = null;
            IsDragging = false;
            IsDirty = true;
            return true;
        }

        public void CancelDrag() { IsDragging = false; _candidate = null; }
        public void SelectPreset(string presetId, DockTree savedLayout)
        {
            if (string.IsNullOrWhiteSpace(presetId) || savedLayout == null) throw new ArgumentException("A saved layout is required.");
            _current = savedLayout.Copy();
            CurrentPresetId = presetId;
            IsDirty = false;
            CancelDrag();
        }

        public void BindReadModel(string presetId, DockTree tree, bool dirty)
        {
            if (tree == null) return;
            _current = tree.Copy();
            CurrentPresetId = presetId ?? string.Empty;
            IsDirty = dirty;
            CancelDrag();
        }

        public void MarkSaved() { IsDirty = false; }
    }

    public sealed class LayoutPreset
    {
        public string Id { get; }
        public string Name { get; }
        public DockTree Tree { get; }
        public LayoutPreset(string id, string name, DockTree tree)
        {
            Id = id ?? string.Empty;
            Name = name ?? id ?? string.Empty;
            Tree = tree ?? throw new ArgumentNullException(nameof(tree));
        }
    }

    public sealed class LayoutPresetStore
    {
        private readonly Dictionary<string, LayoutPreset> _presets = new Dictionary<string, LayoutPreset>(StringComparer.Ordinal);
        public IReadOnlyCollection<LayoutPreset> Presets => new ReadOnlyCollection<LayoutPreset>(_presets.Values.ToList());
        public void Upsert(LayoutPreset preset) { if (preset == null || string.IsNullOrWhiteSpace(preset.Id)) throw new ArgumentException("Preset identity is required."); _presets[preset.Id] = preset; }
        /// <summary>Removing the final layout would leave the shell without a
        /// recoverable workspace, so it is deliberately rejected.</summary>
        public bool Remove(string id) => _presets.Count > 1 && _presets.Remove(id ?? string.Empty);
        public bool TryGet(string id, out LayoutPreset preset) => _presets.TryGetValue(id ?? string.Empty, out preset);
        public bool TryRename(string id, string name)
        {
            if (!_presets.TryGetValue(id ?? string.Empty, out var existing) || string.IsNullOrWhiteSpace(name)) return false;
            _presets[id] = new LayoutPreset(existing.Id, name.Trim(), existing.Tree.Copy());
            return true;
        }
        public bool TryDuplicate(string id, string newId, string name)
        {
            if (!_presets.TryGetValue(id ?? string.Empty, out var existing) || string.IsNullOrWhiteSpace(newId) || _presets.ContainsKey(newId)) return false;
            _presets[newId] = new LayoutPreset(newId, string.IsNullOrWhiteSpace(name) ? existing.Name + " Copy" : name.Trim(), existing.Tree.Copy());
            return true;
        }
        public static LayoutPresetStore CreateDefaults()
        {
            var store = new LayoutPresetStore();
            // Edit and Live are intentionally different workspaces.  Keeping
            // them as two real trees (rather than one tree with a different
            // label) makes switching a user setting observable and keeps the
            // built-in presets aligned with Workspace.md.
            store.Upsert(new LayoutPreset("Edit", "Edit", EditDefaultTree()));
            store.Upsert(new LayoutPreset("Live", "Live", LiveDefaultTree()));
            return store;
        }
        public static DockTree DefaultTree() => EditDefaultTree();
        public static DockTree EditDefaultTree() => new DockTree(new DockSplit(
            DockAxis.Horizontal, .7f,
            new DockTabGroup(new[] { "node-library", "node-graph-panel", "inspector-panel" }, "node-graph-panel"),
            new DockTabGroup(new[] { "dashboard-panel", "outputs-row", "presets-panel", "media-panel", "diagnostics-panel" }, "outputs-row")));
        public static DockTree LiveDefaultTree() => new DockTree(new DockSplit(
            DockAxis.Vertical, .55f,
            new DockTabGroup(new[] { "outputs-row", "dashboard-panel" }, "outputs-row"),
            new DockTabGroup(new[] { "node-library", "node-graph-panel", "inspector-panel", "presets-panel", "media-panel", "diagnostics-panel" }, "node-graph-panel")));
    }

    public sealed class WorkspaceSettingsSnapshot
    {
        public IReadOnlyCollection<LayoutPreset> Presets { get; }
        public string ActivePresetId { get; }
        public float UiScale { get; }
        public bool ReduceMotion { get; }
        public string Theme { get; }
        public float TooltipDelaySeconds { get; }
        public string MediaLibraryView { get; }
        public string DiagnosticsExportFolder { get; }
        public bool IsDirty { get; }
        public DockTree CurrentTree { get; }
        public WorkspaceSettingsSnapshot(IEnumerable<LayoutPreset> presets, string activePresetId, float uiScale = 1f, bool reduceMotion = false, bool isDirty = false,
            string theme = "Dark", float tooltipDelaySeconds = .5f, string mediaLibraryView = "Grid", string diagnosticsExportFolder = null)
        {
            Presets = new ReadOnlyCollection<LayoutPreset>((presets ?? Enumerable.Empty<LayoutPreset>()).Select(x => new LayoutPreset(x.Id, x.Name, x.Tree.Copy())).ToList());
            ActivePresetId = activePresetId ?? string.Empty;
            UiScale = Math.Max(.8f, Math.Min(2f, uiScale));
            ReduceMotion = reduceMotion;
            Theme = string.IsNullOrWhiteSpace(theme) ? "Dark" : theme;
            TooltipDelaySeconds = tooltipDelaySeconds;
            MediaLibraryView = string.Equals(mediaLibraryView, "List", StringComparison.OrdinalIgnoreCase) ? "List" : "Grid";
            DiagnosticsExportFolder = diagnosticsExportFolder ?? string.Empty;
            IsDirty = isDirty;
            CurrentTree = Presets.FirstOrDefault(x => string.Equals(x.Id, ActivePresetId, StringComparison.Ordinal))?.Tree.Copy() ?? LayoutPresetStore.DefaultTree();
        }

        public WorkspaceSettingsSnapshot(IEnumerable<LayoutPreset> presets, string activePresetId, float uiScale, bool reduceMotion, bool isDirty, DockTree currentTree,
            string theme = "Dark", float tooltipDelaySeconds = .5f, string mediaLibraryView = "Grid", string diagnosticsExportFolder = null)
        {
            Presets = new ReadOnlyCollection<LayoutPreset>((presets ?? Enumerable.Empty<LayoutPreset>()).Select(x => new LayoutPreset(x.Id, x.Name, x.Tree.Copy())).ToList());
            ActivePresetId = activePresetId ?? string.Empty;
            UiScale = Math.Max(.8f, Math.Min(2f, uiScale));
            ReduceMotion = reduceMotion;
            Theme = string.IsNullOrWhiteSpace(theme) ? "Dark" : theme;
            TooltipDelaySeconds = tooltipDelaySeconds;
            MediaLibraryView = string.Equals(mediaLibraryView, "List", StringComparison.OrdinalIgnoreCase) ? "List" : "Grid";
            DiagnosticsExportFolder = diagnosticsExportFolder ?? string.Empty;
            IsDirty = isDirty;
            CurrentTree = (currentTree ?? Presets.FirstOrDefault(x => string.Equals(x.Id, ActivePresetId, StringComparison.Ordinal))?.Tree ?? LayoutPresetStore.DefaultTree()).Copy();
        }
    }

    public sealed class WorkspaceSettingsCommand
    {
        public string Operation { get; }
        public string LayoutId { get; }
        public string Name { get; }
        public string NewLayoutId { get; }
        public DockTree Tree { get; }
        public float? UiScale { get; }
        public bool? ReduceMotion { get; }
        public string Theme { get; }
        public float? TooltipDelaySeconds { get; }
        public string MediaLibraryView { get; }
        public string DiagnosticsExportFolder { get; }
        public string RecentProjectRoot { get; }
        public bool? IsDirty { get; }
        public WorkspaceSettingsCommand(string operation, string layoutId = null, string name = null, string newLayoutId = null,
            DockTree tree = null, float? uiScale = null, bool? reduceMotion = null, bool? isDirty = null,
            string theme = null, float? tooltipDelaySeconds = null, string mediaLibraryView = null, string diagnosticsExportFolder = null, string recentProjectRoot = null)
        { Operation = operation ?? string.Empty; LayoutId = layoutId ?? string.Empty; Name = name ?? string.Empty; NewLayoutId = newLayoutId ?? string.Empty; Tree = tree; UiScale = uiScale; ReduceMotion = reduceMotion; IsDirty = isDirty; Theme = theme; TooltipDelaySeconds = tooltipDelaySeconds; MediaLibraryView = mediaLibraryView; DiagnosticsExportFolder = diagnosticsExportFolder; RecentProjectRoot = recentProjectRoot; }
    }

    public sealed class WorkspaceSettingsCommandResult
    {
        public bool IsSuccess { get; }
        public string Error { get; }
        public WorkspaceSettingsSnapshot Snapshot { get; }
        public WorkspaceSettingsCommandResult(bool isSuccess, WorkspaceSettingsSnapshot snapshot, string error = null)
        { IsSuccess = isSuccess; Snapshot = snapshot; Error = error ?? string.Empty; }
    }

    /// <summary>Presentation-owned user settings port.  Layout and UI scale
    /// are user preferences, not project mutations, so this port never calls
    /// an Application project command.</summary>
    public interface IUserSettingsPort
    {
        WorkspaceSettingsSnapshot Read();
        WorkspaceSettingsCommandResult Apply(WorkspaceSettingsCommand command);
    }

    public interface IUserSettingsStorage
    {
        string Load();
        void Save(string payload);
    }

    public sealed class MemoryUserSettingsStorage : IUserSettingsStorage
    {
        public string Payload { get; private set; }
        public string Load() => Payload;
        public void Save(string payload) { Payload = payload ?? string.Empty; }
    }

    /// <summary>Small dependency-free codec used by Player and EditMode
    /// tests.  It is intentionally line based so the Presentation assembly
    /// does not acquire a JSON or persistence-module dependency.</summary>
    public class PersistentUserSettingsPort : IUserSettingsPort
    {
        private readonly IUserSettingsStorage _storage;
        private LayoutPresetStore _store;
        private string _active = "Edit";
        private float _uiScale = 1f;
        private bool _reduceMotion;
        private bool _dirty;
        private WorkspaceSettingsSnapshot _snapshot;

        public PersistentUserSettingsPort(IUserSettingsStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            var payload = storage.Load();
            _store = Decode(payload);
            var persistedActive = ReadLineValue(payload, "active=");
            if (!string.IsNullOrWhiteSpace(persistedActive)) _active = persistedActive;
            if (float.TryParse(ReadLineValue(payload, "scale="), NumberStyles.Float, CultureInfo.InvariantCulture, out var persistedScale)) _uiScale = Math.Max(.8f, Math.Min(2f, persistedScale));
            _reduceMotion = string.Equals(ReadLineValue(payload, "motion="), "1", StringComparison.Ordinal);
            if (!_store.TryGet(_active, out _)) _active = _store.Presets.FirstOrDefault()?.Id ?? string.Empty;
            _snapshot = Snapshot();
        }

        public WorkspaceSettingsSnapshot Read() => _snapshot ?? (_snapshot = Snapshot());

        public WorkspaceSettingsCommandResult Apply(WorkspaceSettingsCommand command)
        {
            if (command == null) return new WorkspaceSettingsCommandResult(false, Read(), "A workspace settings command is required.");
            var beforeStore = CloneStore(_store);
            var beforeActive = _active;
            var beforeUiScale = _uiScale;
            var beforeReduceMotion = _reduceMotion;
            var beforeDirty = _dirty;
            var operation = (command.Operation ?? string.Empty).Trim().ToLowerInvariant();
            var success = true;
            var error = string.Empty;
            switch (operation)
            {
                case "":
                case "select":
                case "overwrite":
                    if (!_store.TryGet(command.LayoutId, out var selected)) { success = false; error = "The requested layout does not exist."; }
                    else { _active = selected.Id; if (operation == "overwrite" && command.Tree != null) _store.Upsert(new LayoutPreset(selected.Id, selected.Name, command.Tree.Copy())); }
                    break;
                case "create":
                    var createId = string.IsNullOrWhiteSpace(command.NewLayoutId) ? command.LayoutId : command.NewLayoutId;
                    if (string.IsNullOrWhiteSpace(createId) || _store.TryGet(createId, out _)) { success = false; error = "A layout with that ID already exists."; }
                    else { _store.Upsert(new LayoutPreset(createId, string.IsNullOrWhiteSpace(command.Name) ? createId : command.Name, (command.Tree ?? LayoutPresetStore.DefaultTree()).Copy())); _active = createId; }
                    break;
                case "rename":
                    success = _store.TryRename(command.LayoutId, command.Name);
                    error = success ? string.Empty : "The layout name is invalid or already unavailable.";
                    break;
                case "duplicate":
                    success = _store.TryDuplicate(command.LayoutId, command.NewLayoutId, command.Name);
                    error = success ? string.Empty : "The duplicate ID already exists or the source is missing.";
                    if (success) _active = command.NewLayoutId;
                    break;
                case "delete":
                    success = _store.Remove(command.LayoutId);
                    error = success ? string.Empty : "The last layout cannot be deleted.";
                    if (success && string.Equals(_active, command.LayoutId, StringComparison.Ordinal)) _active = _store.Presets.First().Id;
                    break;
                case "defaults":
                    // Recreate Defaults is additive: a user layout named Edit
                    // or Live must survive.  The generated IDs are stable for
                    // the first collision and then remain deterministic.
                    var defaults = LayoutPresetStore.CreateDefaults();
                    foreach (var preset in defaults.Presets)
                    {
                        var id = preset.Id;
                        var name = preset.Name;
                        if (_store.TryGet(id, out _))
                        {
                            var suffix = " (Default)";
                            id = preset.Id + suffix;
                            var number = 2;
                            while (_store.TryGet(id, out _)) id = preset.Id + suffix + " " + number++;
                            name = preset.Name + suffix;
                        }
                        _store.Upsert(new LayoutPreset(id, name, preset.Tree.Copy()));
                    }
                    if (!_store.TryGet(_active, out _)) _active = "Edit";
                    break;
                case "ui-scale":
                    if (!command.UiScale.HasValue || command.UiScale.Value < .8f || command.UiScale.Value > 2f) { success = false; error = "UI scale must be between 80% and 200%."; }
                    else _uiScale = command.UiScale.Value;
                    break;
                case "reduce-motion":
                    if (!command.ReduceMotion.HasValue) { success = false; error = "Reduce Motion requires a value."; }
                    else _reduceMotion = command.ReduceMotion.Value;
                    break;
                default: success = false; error = "Unknown workspace settings operation: " + command.Operation; break;
            }
            if (success)
            {
                _dirty = true;
                try { Save(); }
                catch (Exception exception)
                {
                    success = false;
                    error = "Workspace settings could not be saved: " + exception.Message;
                }
            }
            if (!success)
            {
                _store = beforeStore;
                _active = beforeActive;
                _uiScale = beforeUiScale;
                _reduceMotion = beforeReduceMotion;
                _dirty = beforeDirty;
                return new WorkspaceSettingsCommandResult(false, Read(), error);
            }
            _snapshot = Snapshot();
            return new WorkspaceSettingsCommandResult(true, _snapshot, error);
        }

        private WorkspaceSettingsSnapshot Snapshot() => new WorkspaceSettingsSnapshot(_store.Presets, _active, _uiScale, _reduceMotion, _dirty);
        private void Save() => _storage.Save(Encode(_store, _active, _uiScale, _reduceMotion));

        private static LayoutPresetStore CloneStore(LayoutPresetStore source)
        {
            var clone = new LayoutPresetStore();
            foreach (var preset in source?.Presets ?? Enumerable.Empty<LayoutPreset>())
                clone.Upsert(new LayoutPreset(preset.Id, preset.Name, preset.Tree.Copy()));
            return clone;
        }

        private static string Encode(LayoutPresetStore store, string active, float scale, bool reduceMotion)
        {
            var lines = new List<string> { "v1", "active=" + B64(active), "scale=" + scale.ToString(CultureInfo.InvariantCulture), "motion=" + (reduceMotion ? "1" : "0") };
            foreach (var preset in store.Presets.OrderBy(x => x.Id, StringComparer.Ordinal)) lines.Add("preset=" + B64(preset.Id) + "|" + B64(preset.Name) + "|" + B64(EncodeNode(preset.Tree.Root)));
            return string.Join("\n", lines);
        }

        private static LayoutPresetStore Decode(string payload)
        {
            var store = new LayoutPresetStore();
            try
            {
                foreach (var line in (payload ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!line.StartsWith("preset=", StringComparison.Ordinal)) continue;
                    var fields = line.Substring(7).Split('|');
                    if (fields.Length != 3) continue;
                    var id = UB64(fields[0]); var name = UB64(fields[1]); var node = DecodeNode(UB64(fields[2]));
                    if (!string.IsNullOrWhiteSpace(id) && node != null) store.Upsert(new LayoutPreset(id, name, new DockTree(node)));
                }
            }
            catch { store = new LayoutPresetStore(); }
            if (store.Presets.Count == 0) store = LayoutPresetStore.CreateDefaults();
            return store;
        }

        private static string EncodeNode(DockNode node)
        {
            if (node is DockTabGroup tabs) return "T|" + B64(tabs.ActivePanelInstanceId) + "|" + B64(string.Join("\n", tabs.PanelInstanceIds));
            if (node is DockSplit split) return "S|" + (split.Axis == DockAxis.Horizontal ? "H" : "V") + "|" + split.Ratio.ToString(CultureInfo.InvariantCulture) + "|" + B64(EncodeNode(split.First)) + "|" + B64(EncodeNode(split.Second));
            return "E";
        }

        private static DockNode DecodeNode(string text)
        {
            if (string.Equals(text, "E", StringComparison.Ordinal)) return new DockEmpty();
            var fields = (text ?? string.Empty).Split('|');
            if (fields.Length >= 3 && fields[0] == "T") return new DockTabGroup(UB64(fields[2]).Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries), UB64(fields[1]));
            if (fields.Length >= 5 && fields[0] == "S" && float.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio)) return new DockSplit(fields[1] == "H" ? DockAxis.Horizontal : DockAxis.Vertical, ratio, DecodeNode(UB64(fields[3])), DecodeNode(UB64(fields[4])));
            return null;
        }
        private static string B64(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value ?? string.Empty));
        private static string UB64(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value ?? string.Empty));
        private static string ReadLineValue(string payload, string prefix)
        {
            var line = (payload ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(x => x.StartsWith(prefix, StringComparison.Ordinal));
            if (line == null) return string.Empty;
            var value = line.Substring(prefix.Length);
            try { return prefix == "active=" ? UB64(value) : value; }
            catch { return string.Empty; }
        }
    }

    public sealed class InMemoryUserSettingsPort : PersistentUserSettingsPort
    {
        public InMemoryUserSettingsPort() : base(new MemoryUserSettingsStorage()) { }
    }
}
