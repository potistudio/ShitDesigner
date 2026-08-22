using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ShitDesigner.Application;
using ShitDesigner.Persistence;
using ShitDesigner.Presentation;
using UnityEngine;

namespace ShitDesigner.Bootstrap
{
    /// <summary>
    /// User-wide persistence is separate from project persistence. Settings
    /// and layouts are independent JSON documents with independent atomic
    /// promotion, so a failed layout write cannot dirty or replace a project.
    /// </summary>
    public sealed class ProjectUserSettingsStorage : IUserSettingsStorage, IRecentProjectStore
    {
        public const int CurrentFormatVersion = 1;

        private readonly IProjectFileSystem _fileSystem;
        private readonly string _root;
        private readonly string _settingsPath;
        private readonly string _layoutsPath;
        private readonly string _legacyPath;

        public string RootPath => _root;
        public string SettingsPath => _settingsPath;
        public string LayoutsPath => _layoutsPath;

        /// <param name="path">A directory containing settings.json and layouts.json. A legacy .dat path is accepted for old callers.</param>
        public ProjectUserSettingsStorage(IProjectFileSystem fileSystem, string path = null)
        {
            _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
            if (string.IsNullOrWhiteSpace(path))
            {
                _root = Path.Combine(UnityEngine.Application.persistentDataPath, "ShitDesigner");
            }
            else if (string.Equals(Path.GetExtension(path), ".dat", StringComparison.OrdinalIgnoreCase))
            {
                _legacyPath = path;
                _root = Path.GetDirectoryName(path) ?? Path.GetTempPath();
            }
            else
            {
                _root = path;
            }
            _settingsPath = Path.Combine(_root, "settings.json");
            _layoutsPath = Path.Combine(_root, "layouts.json");
        }

        // Compatibility with the old presentation storage contract. New
        // production composition uses the typed methods below and never
        // combines settings and layouts into one payload.
        public string Load()
        {
            var path = _legacyPath ?? _settingsPath;
            try { return _fileSystem.Exists(path) ? Encoding.UTF8.GetString(_fileSystem.ReadAllBytes(path)) : string.Empty; }
            catch { return string.Empty; }
        }

        public void Save(string payload)
        {
            if (!string.IsNullOrWhiteSpace(_legacyPath))
            {
                var directory = Path.GetDirectoryName(_legacyPath);
                if (!string.IsNullOrEmpty(directory)) _fileSystem.EnsureDirectory(directory);
                _fileSystem.WriteAllBytes(_legacyPath, new UTF8Encoding(false, true).GetBytes(payload ?? string.Empty));
                return;
            }
            SaveSettingsJson(payload ?? string.Empty);
        }

        public ProjectUserSettingsData ReadSettings() => ReadSettings(out _);

        public ProjectUserSettingsData ReadSettings(out bool recovered)
            => ProjectUserSettingsCodec.DecodeSettings(ReadText(_settingsPath), out recovered);

        public void SaveSettings(ProjectUserSettingsData settings) => SaveSettingsJson(ProjectUserSettingsCodec.EncodeSettings(settings));

        public LayoutPresetStore ReadLayouts(out string currentLayoutId)
            => ReadLayouts(out currentLayoutId, out _);

        public LayoutPresetStore ReadLayouts(out string currentLayoutId, out bool recovered)
            => ProjectUserSettingsCodec.DecodeLayouts(ReadText(_layoutsPath), out currentLayoutId, out recovered);

        public void SaveLayouts(LayoutPresetStore store, string currentLayoutId)
            => SaveLayoutsJson(ProjectUserSettingsCodec.EncodeLayouts(store, currentLayoutId));

        public IReadOnlyList<string> ReadRecentProjectRoots() => ReadSettings().RecentProjectRoots;

        public void WriteRecentProjectRoots(IEnumerable<string> projectRoots)
        {
            var settings = ReadSettings();
            settings.RecentProjectRoots = ProjectUserSettingsCodec.NormalizeRecent(projectRoots);
            SaveSettings(settings);
        }

        public void SaveSettingsJson(string payload) => AtomicWrite(_settingsPath, payload ?? string.Empty);
        public void SaveLayoutsJson(string payload) => AtomicWrite(_layoutsPath, payload ?? string.Empty);

        private string ReadText(string path)
        {
            try
            {
                if (!_fileSystem.Exists(path)) return string.Empty;
                var bytes = _fileSystem.ReadAllBytes(path);
                var offset = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;
                return new UTF8Encoding(false, true).GetString(bytes, offset, bytes.Length - offset);
            }
            catch { return string.Empty; }
        }

        private void AtomicWrite(string path, string payload)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory)) _fileSystem.EnsureDirectory(directory);
            var temporary = path + ".tmp";
            var bytes = new UTF8Encoding(false, true).GetBytes(payload ?? string.Empty);
            _fileSystem.WriteAllBytes(temporary, bytes);
            var durable = _fileSystem as IProjectDurableFileSystem;
            if (durable == null) throw new IOException("The file system does not provide a durable user-settings flush port.");
            durable.Flush(temporary);
            var roundTrip = _fileSystem.ReadAllBytes(temporary);
            if (!bytes.SequenceEqual(roundTrip)) throw new IOException("User settings temporary file read-back did not match.");
            var writer = _fileSystem as IAtomicManifestWriter;
            if (writer == null) throw new IOException("The file system does not provide an atomic user-settings writer.");
            writer.Replace(_fileSystem, temporary, path, null, false);
        }
    }

    /// <summary>All user-wide preferences represented in settings.json.</summary>
    public sealed class ProjectUserSettingsData
    {
        public float UiScale { get; set; } = 1f;
        public string Theme { get; set; } = "Dark";
        public bool ReduceMotion { get; set; }
        public float TooltipDelaySeconds { get; set; } = .5f;
        public string MediaLibraryView { get; set; } = "Grid";
        public string DiagnosticsExportFolder { get; set; } = string.Empty;
        public List<string> RecentProjectRoots { get; set; } = new List<string>();

        public ProjectUserSettingsData Copy() => new ProjectUserSettingsData
        {
            UiScale = UiScale,
            Theme = Theme,
            ReduceMotion = ReduceMotion,
            TooltipDelaySeconds = TooltipDelaySeconds,
            MediaLibraryView = MediaLibraryView,
            DiagnosticsExportFolder = DiagnosticsExportFolder,
            RecentProjectRoots = new List<string>(RecentProjectRoots ?? new List<string>())
        };
    }

    /// <summary>Presentation-facing port backed by the split JSON store.</summary>
    public sealed class ProjectUserSettingsPort : IUserSettingsPort
    {
        private readonly ProjectUserSettingsStorage _storage;
        private ProjectUserSettingsData _settings;
        private LayoutPresetStore _layouts;
        private string _activeLayout;
        private DockTree _draftTree;
        private bool _dirty;
        private WorkspaceSettingsSnapshot _snapshot;

        public ProjectUserSettingsPort(ProjectUserSettingsStorage storage)
        {
            _storage = storage ?? throw new ArgumentNullException(nameof(storage));
            var settingsRecovered = false;
            var layoutsRecovered = false;
            _settings = storage.ReadSettings(out settingsRecovered);
            _layouts = storage.ReadLayouts(out _activeLayout, out layoutsRecovered);
            if (!_layouts.TryGet(_activeLayout, out var activePreset))
            {
                activePreset = _layouts.Presets.FirstOrDefault();
                _activeLayout = activePreset?.Id ?? string.Empty;
            }
            _draftTree = (activePreset?.Tree ?? LayoutPresetStore.DefaultTree()).Copy();
            _snapshot = CreateSnapshot();
            // Initial defaults and recovery are persisted independently. A
            // failure here is non-fatal: the in-memory preferences remain
            // usable and the next explicit save gets another chance.
            try { if (settingsRecovered) storage.SaveSettings(_settings); } catch { }
            try { if (layoutsRecovered) storage.SaveLayouts(_layouts, _activeLayout); } catch { }
        }

        public ProjectUserSettingsData Settings => _settings.Copy();

        public WorkspaceSettingsSnapshot Read() => _snapshot ?? (_snapshot = CreateSnapshot());

        public WorkspaceSettingsCommandResult Apply(WorkspaceSettingsCommand command)
        {
            if (command == null) return new WorkspaceSettingsCommandResult(false, Read(), "A workspace settings command is required.");
            var operation = (command.Operation ?? string.Empty).Trim().ToLowerInvariant();
            var beforeLayouts = CloneStore(_layouts);
            var beforeSettings = _settings.Copy();
            var beforeActive = _activeLayout;
            var beforeDraft = _draftTree?.Copy();
            var beforeDirty = _dirty;
            var persistLayouts = false;
            var persistSettings = false;
            var success = true;
            var error = string.Empty;
            try
            {
                // A successful dock edit updates only the in-memory draft. It
                // becomes durable only through the explicit Layout Save.
                if (operation == "edit" || (operation.Length == 0 && (command.Tree != null || command.IsDirty.HasValue)))
                {
                    if (command.Tree != null && !command.Tree.Validate().IsValid)
                    {
                        success = false;
                        error = "The layout tree is invalid.";
                    }
                    else
                    {
                        if (command.Tree != null) _draftTree = command.Tree.Copy();
                        _dirty = command.IsDirty ?? true;
                    }
                }
                else
                {
                    switch (operation)
                    {
                        case "":
                        case "select":
                            if (!_layouts.TryGet(command.LayoutId, out var selected)) { success = false; error = "The requested layout does not exist."; }
                            else
                            {
                                _activeLayout = selected.Id;
                                _draftTree = selected.Tree.Copy();
                                _dirty = false;
                                // The selected layout is a user-global current
                                // selection, so only this metadata is saved.
                                persistLayouts = true;
                            }
                            break;
                        case "overwrite":
                            if (!_layouts.TryGet(command.LayoutId, out var overwritten)) { success = false; error = "The requested layout does not exist."; }
                            else
                            {
                                var overwriteTree = (command.Tree ?? _draftTree ?? overwritten.Tree);
                                if (!overwriteTree.Validate().IsValid) { success = false; error = "The layout tree is invalid."; }
                                else
                                {
                                    _layouts.Upsert(new LayoutPreset(overwritten.Id, overwritten.Name, overwriteTree.Copy()));
                                    _activeLayout = overwritten.Id;
                                    _draftTree = overwriteTree.Copy();
                                    _dirty = false;
                                    persistLayouts = true;
                                }
                            }
                            break;
                        case "create":
                            var createId = string.IsNullOrWhiteSpace(command.NewLayoutId) ? command.LayoutId : command.NewLayoutId;
                            if (string.IsNullOrWhiteSpace(createId) || _layouts.TryGet(createId, out _)) { success = false; error = "A layout with that ID already exists."; }
                            else
                            {
                                var createTree = (command.Tree ?? _draftTree ?? LayoutPresetStore.DefaultTree());
                                if (!createTree.Validate().IsValid) { success = false; error = "The layout tree is invalid."; }
                                else
                                {
                                    _layouts.Upsert(new LayoutPreset(createId, string.IsNullOrWhiteSpace(command.Name) ? createId : command.Name, createTree.Copy()));
                                    _activeLayout = createId;
                                    _draftTree = createTree.Copy();
                                    _dirty = false;
                                    persistLayouts = true;
                                }
                            }
                            break;
                        case "rename":
                            success = _layouts.TryRename(command.LayoutId, command.Name);
                            error = success ? string.Empty : "The layout name is invalid or unavailable.";
                            persistLayouts = success;
                            break;
                        case "duplicate":
                            success = _layouts.TryDuplicate(command.LayoutId, command.NewLayoutId, command.Name);
                            error = success ? string.Empty : "The duplicate ID already exists or the source is missing.";
                            if (success)
                            {
                                _activeLayout = command.NewLayoutId;
                                _layouts.TryGet(_activeLayout, out var duplicate);
                                _draftTree = duplicate.Tree.Copy();
                                _dirty = false;
                                persistLayouts = true;
                            }
                            break;
                        case "delete":
                            success = _layouts.Remove(command.LayoutId);
                            error = success ? string.Empty : "The last layout cannot be deleted.";
                            if (success)
                            {
                                if (string.Equals(_activeLayout, command.LayoutId, StringComparison.Ordinal))
                                {
                                    _activeLayout = _layouts.Presets.First().Id;
                                    _layouts.TryGet(_activeLayout, out var replacement);
                                    _draftTree = replacement.Tree.Copy();
                                    _dirty = false;
                                }
                                persistLayouts = true;
                            }
                            break;
                        case "defaults":
                            var defaults = LayoutPresetStore.CreateDefaults();
                            foreach (var preset in defaults.Presets)
                            {
                                var id = preset.Id; var name = preset.Name;
                                if (_layouts.TryGet(id, out _))
                                {
                                    var suffix = " (Default)"; id = preset.Id + suffix; var number = 2;
                                    while (_layouts.TryGet(id, out _)) id = preset.Id + suffix + " " + number++;
                                    name = preset.Name + suffix;
                                }
                                _layouts.Upsert(new LayoutPreset(id, name, preset.Tree.Copy()));
                            }
                            if (!_layouts.TryGet(_activeLayout, out var defaultActive))
                            {
                                _activeLayout = _layouts.Presets.First().Id;
                                _draftTree = defaultActive?.Tree.Copy() ?? _layouts.Presets.First().Tree.Copy();
                                _dirty = false;
                            }
                            persistLayouts = true;
                            break;
                        case "ui-scale":
                            if (!command.UiScale.HasValue || !ProjectUserSettingsCodec.IsAllowedUiScale(command.UiScale.Value)) { success = false; error = "UI scale must be 100%, 125%, or 150%."; }
                            else { _settings.UiScale = command.UiScale.Value; persistSettings = true; }
                            break;
                        case "reduce-motion":
                            if (!command.ReduceMotion.HasValue) { success = false; error = "Reduce Motion requires a value."; }
                            else { _settings.ReduceMotion = command.ReduceMotion.Value; persistSettings = true; }
                            break;
                        case "theme":
                            if (!string.Equals(command.Theme, "Dark", StringComparison.OrdinalIgnoreCase)) { success = false; error = "Only the Dark theme is available."; }
                            else { _settings.Theme = "Dark"; persistSettings = true; }
                            break;
                        case "tooltip-delay":
                        case "tooltip_delay":
                            if (!command.TooltipDelaySeconds.HasValue || !IsAllowedTooltipDelay(command.TooltipDelaySeconds.Value)) { success = false; error = "Tooltip delay must be 250 ms, 500 ms, or 1000 ms."; }
                            else { _settings.TooltipDelaySeconds = command.TooltipDelaySeconds.Value; persistSettings = true; }
                            break;
                        case "media-view":
                        case "media_view":
                            if (!string.Equals(command.MediaLibraryView, "Grid", StringComparison.OrdinalIgnoreCase) && !string.Equals(command.MediaLibraryView, "List", StringComparison.OrdinalIgnoreCase)) { success = false; error = "Media Library view must be Grid or List."; }
                            else { _settings.MediaLibraryView = string.Equals(command.MediaLibraryView, "List", StringComparison.OrdinalIgnoreCase) ? "List" : "Grid"; persistSettings = true; }
                            break;
                        case "diagnostics-folder":
                        case "diagnostics_folder":
                            if (string.IsNullOrWhiteSpace(command.DiagnosticsExportFolder)) { success = false; error = "Diagnostics export folder is required."; }
                            else { _settings.DiagnosticsExportFolder = command.DiagnosticsExportFolder.Trim(); persistSettings = true; }
                            break;
                        case "recent-remove":
                        case "recent_remove":
                            if (string.IsNullOrWhiteSpace(command.RecentProjectRoot)) { success = false; error = "Recent project root is required."; }
                            else
                            {
                                var normalized = Path.GetFullPath(command.RecentProjectRoot);
                                _settings.RecentProjectRoots = ProjectUserSettingsCodec.NormalizeRecent((_settings.RecentProjectRoots ?? new List<string>()).Where(x => !string.Equals(x, normalized, StringComparison.OrdinalIgnoreCase)));
                                persistSettings = true;
                            }
                            break;
                        default:
                            success = false; error = "Unknown workspace settings operation: " + command.Operation; break;
                    }
                }

                if (success)
                {
                    if (persistLayouts) _storage.SaveLayouts(_layouts, _activeLayout);
                    if (persistSettings) _storage.SaveSettings(_settings);
                }
            }
            catch (Exception exception)
            {
                success = false;
                error = "User settings could not be saved: " + exception.Message;
            }
            if (!success)
            {
                _layouts = beforeLayouts;
                _settings = beforeSettings;
                _activeLayout = beforeActive;
                _draftTree = beforeDraft;
                _dirty = beforeDirty;
            }
            if (success) _snapshot = CreateSnapshot();
            return new WorkspaceSettingsCommandResult(success, Read(), error);
        }

        private WorkspaceSettingsSnapshot CreateSnapshot() => new WorkspaceSettingsSnapshot(_layouts.Presets, _activeLayout, _settings.UiScale, _settings.ReduceMotion, _dirty, _draftTree,
            _settings.Theme, _settings.TooltipDelaySeconds, _settings.MediaLibraryView, _settings.DiagnosticsExportFolder);

        private static LayoutPresetStore CloneStore(LayoutPresetStore source)
        {
            var clone = new LayoutPresetStore();
            foreach (var preset in source.Presets)
                clone.Upsert(new LayoutPreset(preset.Id, preset.Name, preset.Tree.Copy()));
            return clone;
        }

        private static bool IsAllowedTooltipDelay(float seconds)
        {
            return !float.IsNaN(seconds) && !float.IsInfinity(seconds) &&
                (Math.Abs(seconds - .25f) < .0001f || Math.Abs(seconds - .5f) < .0001f || Math.Abs(seconds - 1f) < .0001f);
        }
    }

    internal static class ProjectUserSettingsCodec
    {
        public static ProjectUserSettingsData DecodeSettings(string payload, out bool recovered)
        {
            recovered = false;
            var defaults = new ProjectUserSettingsData();
            if (string.IsNullOrWhiteSpace(payload)) { recovered = true; return defaults; }
            try
            {
                using (var document = JsonDocument.Parse(payload, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 }))
                {
                    var root = document.RootElement; RequireObject(root); RejectUnknown(root, "formatVersion", "uiScale", "theme", "reduceMotion", "tooltipDelaySeconds", "mediaLibraryView", "diagnosticsExportFolder", "recentProjectRoots");
                    if (!root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != ProjectUserSettingsStorage.CurrentFormatVersion) throw new JsonException("Unsupported user settings format version.");
                    if (root.TryGetProperty("uiScale", out var uiScale)) defaults.UiScale = ReadUiScale(uiScale);
                    if (root.TryGetProperty("theme", out var theme)) defaults.Theme = ReadString(theme, defaults.Theme);
                    if (root.TryGetProperty("reduceMotion", out var motion) && (motion.ValueKind == JsonValueKind.True || motion.ValueKind == JsonValueKind.False)) defaults.ReduceMotion = motion.GetBoolean();
                    if (root.TryGetProperty("tooltipDelaySeconds", out var tooltip)) defaults.TooltipDelaySeconds = ReadFiniteFloat(tooltip, 0f, 60f, defaults.TooltipDelaySeconds);
                    if (root.TryGetProperty("mediaLibraryView", out var view)) defaults.MediaLibraryView = string.Equals(ReadString(view, defaults.MediaLibraryView), "List", StringComparison.OrdinalIgnoreCase) ? "List" : "Grid";
                    if (root.TryGetProperty("diagnosticsExportFolder", out var exportFolder)) defaults.DiagnosticsExportFolder = ReadString(exportFolder, string.Empty);
                    if (root.TryGetProperty("recentProjectRoots", out var recent))
                    {
                        if (recent.ValueKind != JsonValueKind.Array) throw new JsonException("recentProjectRoots must be an array.");
                        defaults.RecentProjectRoots = NormalizeRecent(recent.EnumerateArray().Select(x => ReadString(x, string.Empty)));
                    }
                    return defaults;
                }
            }
            catch { recovered = true; return new ProjectUserSettingsData(); }
        }

        public static string EncodeSettings(ProjectUserSettingsData value)
        {
            value = value?.Copy() ?? new ProjectUserSettingsData();
            value.UiScale = NormalizeUiScale(value.UiScale); value.TooltipDelaySeconds = Clamp(value.TooltipDelaySeconds, 0f, 60f, .5f);
            value.Theme = string.IsNullOrWhiteSpace(value.Theme) ? "Dark" : value.Theme.Trim(); value.MediaLibraryView = string.Equals(value.MediaLibraryView, "List", StringComparison.OrdinalIgnoreCase) ? "List" : "Grid"; value.RecentProjectRoots = NormalizeRecent(value.RecentProjectRoots);
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject(); writer.WriteNumber("formatVersion", ProjectUserSettingsStorage.CurrentFormatVersion); writer.WriteNumber("uiScale", value.UiScale); writer.WriteString("theme", value.Theme); writer.WriteBoolean("reduceMotion", value.ReduceMotion); writer.WriteNumber("tooltipDelaySeconds", value.TooltipDelaySeconds); writer.WriteString("mediaLibraryView", value.MediaLibraryView); writer.WriteString("diagnosticsExportFolder", value.DiagnosticsExportFolder ?? string.Empty); writer.WritePropertyName("recentProjectRoots"); writer.WriteStartArray(); foreach (var root in value.RecentProjectRoots) writer.WriteStringValue(root); writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
                }
                return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
            }
        }

        public static LayoutPresetStore DecodeLayouts(string payload, out string currentLayoutId, out bool recovered)
        {
            currentLayoutId = string.Empty; recovered = false;
            try
            {
                if (string.IsNullOrWhiteSpace(payload)) throw new JsonException("Missing layouts document.");
                using (var document = JsonDocument.Parse(payload, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 }))
                {
                    var root = document.RootElement; RequireObject(root); RejectUnknown(root, "formatVersion", "currentLayoutId", "presets");
                    if (!root.TryGetProperty("formatVersion", out var version) || version.ValueKind != JsonValueKind.Number || version.GetInt32() != ProjectUserSettingsStorage.CurrentFormatVersion) throw new JsonException("Unsupported layout format version.");
                    currentLayoutId = root.TryGetProperty("currentLayoutId", out var active) ? ReadString(active, string.Empty) : string.Empty;
                    if (!root.TryGetProperty("presets", out var presets) || presets.ValueKind != JsonValueKind.Array) throw new JsonException("presets must be an array.");
                    var store = new LayoutPresetStore();
                    foreach (var preset in presets.EnumerateArray())
                    {
                        RequireObject(preset); RejectUnknown(preset, "id", "name", "tree"); var id = preset.TryGetProperty("id", out var idElement) ? ReadString(idElement, string.Empty) : string.Empty;
                        if (string.IsNullOrWhiteSpace(id) || !preset.TryGetProperty("tree", out var tree)) throw new JsonException("Invalid layout preset.");
                        var name = preset.TryGetProperty("name", out var nameElement) ? ReadString(nameElement, id) : id;
                        var decodedTree = new DockTree(DecodeNode(tree));
                        if (!decodedTree.Validate().IsValid) throw new JsonException("Invalid layout tree.");
                        if (store.TryGet(id, out _)) throw new JsonException("Duplicate layout preset.");
                        store.Upsert(new LayoutPreset(id, name, decodedTree));
                    }
                    if (store.Presets.Count == 0) throw new JsonException("No layout presets were stored."); if (!store.TryGet(currentLayoutId, out _)) currentLayoutId = store.Presets.First().Id; return store;
                }
            }
            catch { recovered = true; currentLayoutId = "Edit"; return LayoutPresetStore.CreateDefaults(); }
        }

        public static string EncodeLayouts(LayoutPresetStore store, string currentLayoutId)
        {
            store = store ?? LayoutPresetStore.CreateDefaults(); if (!store.TryGet(currentLayoutId, out _)) currentLayoutId = store.Presets.FirstOrDefault()?.Id ?? string.Empty;
            using (var stream = new MemoryStream())
            {
                using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
                {
                    writer.WriteStartObject(); writer.WriteNumber("formatVersion", ProjectUserSettingsStorage.CurrentFormatVersion); writer.WriteString("currentLayoutId", currentLayoutId ?? string.Empty); writer.WritePropertyName("presets"); writer.WriteStartArray();
                    foreach (var preset in store.Presets.OrderBy(x => x.Id, StringComparer.Ordinal)) { writer.WriteStartObject(); writer.WriteString("id", preset.Id); writer.WriteString("name", preset.Name); writer.WritePropertyName("tree"); EncodeNode(writer, preset.Tree.Root); writer.WriteEndObject(); }
                    writer.WriteEndArray(); writer.WriteEndObject(); writer.Flush();
                }
                return Encoding.UTF8.GetString(stream.ToArray()) + "\n";
            }
        }

        public static List<string> NormalizeRecent(IEnumerable<string> roots)
        {
            var result = new List<string>();
            foreach (var root in roots ?? Enumerable.Empty<string>()) { if (string.IsNullOrWhiteSpace(root)) continue; var value = root.Trim(); if (result.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))) continue; result.Add(value); if (result.Count == 10) break; }
            return result;
        }

        public static bool IsAllowedUiScale(float value)
            => !float.IsNaN(value) && !float.IsInfinity(value) && (Math.Abs(value - 1f) < .0001f || Math.Abs(value - 1.25f) < .0001f || Math.Abs(value - 1.5f) < .0001f);

        private static float ReadUiScale(JsonElement element)
        {
            var value = ReadFiniteFloat(element, .8f, 2f, 1f);
            if (!IsAllowedUiScale(value)) throw new JsonException("UI scale is not one of the supported values.");
            return value;
        }

        private static float NormalizeUiScale(float value) => IsAllowedUiScale(value) ? (Math.Abs(value - 1.25f) < .0001f ? 1.25f : Math.Abs(value - 1.5f) < .0001f ? 1.5f : 1f) : 1f;

        private static void EncodeNode(Utf8JsonWriter writer, DockNode node)
        {
            if (node is DockSplit split) { writer.WriteStartObject(); writer.WriteString("kind", "split"); writer.WriteString("axis", split.Axis == DockAxis.Horizontal ? "horizontal" : "vertical"); writer.WriteNumber("ratio", split.Ratio); writer.WritePropertyName("first"); EncodeNode(writer, split.First); writer.WritePropertyName("second"); EncodeNode(writer, split.Second); writer.WriteEndObject(); return; }
            if (node is DockTabGroup tabs)
            {
                writer.WriteStartObject(); writer.WriteString("kind", "tabs"); writer.WritePropertyName("panels"); writer.WriteStartArray();
                foreach (var panel in tabs.PanelInstanceIds)
                {
                    var unknown = tabs.UnknownPanels.FirstOrDefault(x => string.Equals(x.PanelInstanceId, panel, StringComparison.Ordinal));
                    if (unknown == null) writer.WriteStringValue(panel);
                    else
                    {
                        writer.WriteStartObject(); writer.WriteString("panelTypeId", unknown.PanelTypeId); writer.WriteString("panelInstanceId", unknown.PanelInstanceId); writer.WritePropertyName("rawPayload"); WriteRawPayload(writer, unknown.RawPayload); writer.WriteString("originalLocation", unknown.OriginalLocation); writer.WriteEndObject();
                    }
                }
                writer.WriteEndArray(); writer.WriteString("activePanel", tabs.ActivePanelInstanceId); writer.WriteEndObject(); return;
            }
            writer.WriteStartObject(); writer.WriteString("kind", "empty"); writer.WriteEndObject();
        }

        private static DockNode DecodeNode(JsonElement element)
        {
            RequireObject(element); var kind = element.TryGetProperty("kind", out var kindElement) ? ReadString(kindElement, string.Empty) : string.Empty;
            if (string.Equals(kind, "empty", StringComparison.OrdinalIgnoreCase)) { RejectUnknown(element, "kind"); return new DockEmpty(); }
            if (string.Equals(kind, "tabs", StringComparison.OrdinalIgnoreCase))
            {
                RejectUnknown(element, "kind", "panels", "activePanel"); if (!element.TryGetProperty("panels", out var panels) || panels.ValueKind != JsonValueKind.Array) throw new JsonException("Tab panels must be an array.");
                var panelIds = new List<string>(); var unknownPanels = new List<UnknownPanelPlaceholder>();
                foreach (var panel in panels.EnumerateArray())
                {
                    if (panel.ValueKind == JsonValueKind.String)
                    {
                        var id = ReadString(panel, string.Empty); if (!string.IsNullOrWhiteSpace(id)) panelIds.Add(id);
                    }
                    else if (panel.ValueKind == JsonValueKind.Object)
                    {
                        RejectUnknown(panel, "panelTypeId", "panelInstanceId", "rawPayload", "originalLocation");
                        var typeId = panel.TryGetProperty("panelTypeId", out var typeElement) ? ReadString(typeElement, string.Empty) : string.Empty;
                        var instanceId = panel.TryGetProperty("panelInstanceId", out var instanceElement) ? ReadString(instanceElement, string.Empty) : string.Empty;
                        var rawPayload = panel.TryGetProperty("rawPayload", out var rawElement)
                            ? (rawElement.ValueKind == JsonValueKind.String ? rawElement.GetString() ?? string.Empty : rawElement.GetRawText())
                            : string.Empty;
                        var location = panel.TryGetProperty("originalLocation", out var locationElement) ? ReadString(locationElement, string.Empty) : string.Empty;
                        if (string.IsNullOrWhiteSpace(instanceId) || string.IsNullOrWhiteSpace(typeId)) throw new JsonException("Unknown panel identity is required.");
                        panelIds.Add(instanceId); unknownPanels.Add(new UnknownPanelPlaceholder(typeId, instanceId, rawPayload, location));
                    }
                    else throw new JsonException("Panel entries must be strings or unknown-panel objects.");
                }
                var active = element.TryGetProperty("activePanel", out var activeElement) ? ReadString(activeElement, string.Empty) : string.Empty;
                if (panelIds.Count == 0 || string.IsNullOrWhiteSpace(active) || !panelIds.Contains(active, StringComparer.Ordinal)) throw new JsonException("Tab layout is invalid."); return new DockTabGroup(panelIds, active, unknownPanels);
            }
            if (string.Equals(kind, "split", StringComparison.OrdinalIgnoreCase))
            {
                RejectUnknown(element, "kind", "axis", "ratio", "first", "second"); var axis = element.TryGetProperty("axis", out var axisElement) && string.Equals(ReadString(axisElement, string.Empty), "vertical", StringComparison.OrdinalIgnoreCase) ? DockAxis.Vertical : DockAxis.Horizontal; var ratio = element.TryGetProperty("ratio", out var ratioElement) ? ReadFiniteFloat(ratioElement, .0001f, .9999f, .5f) : .5f;
                if (!element.TryGetProperty("first", out var first) || !element.TryGetProperty("second", out var second)) throw new JsonException("Split children are required."); return new DockSplit(axis, ratio, DecodeNode(first), DecodeNode(second));
            }
            throw new JsonException("Unknown layout node kind.");
        }

        private static void RequireObject(JsonElement element) { if (element.ValueKind != JsonValueKind.Object) throw new JsonException("Expected JSON object."); }
        private static void RejectUnknown(JsonElement element, params string[] allowed) { var names = new HashSet<string>(allowed ?? Array.Empty<string>(), StringComparer.Ordinal); var seen = new HashSet<string>(StringComparer.Ordinal); foreach (var property in element.EnumerateObject()) if (!names.Contains(property.Name) || !seen.Add(property.Name)) throw new JsonException("Unknown or duplicate user-settings property: " + property.Name); }
        private static string ReadString(JsonElement element, string fallback) => element.ValueKind == JsonValueKind.String ? element.GetString() ?? fallback : fallback;
        private static float ReadFiniteFloat(JsonElement element, float min, float max, float fallback) { if (element.ValueKind != JsonValueKind.Number || !element.TryGetSingle(out var value) || float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max) throw new JsonException("User-settings number is outside its allowed range."); return value; }
        private static float Clamp(float value, float min, float max, float fallback) => float.IsNaN(value) || float.IsInfinity(value) ? fallback : Math.Max(min, Math.Min(max, value));
        private static void WriteRawPayload(Utf8JsonWriter writer, string raw)
        {
            raw = raw ?? string.Empty;
            try
            {
                using (JsonDocument.Parse(raw, new JsonDocumentOptions { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = 64 }))
                {
                    writer.WriteRawValue(raw, false);
                    return;
                }
            }
            catch { }
            writer.WriteStringValue(raw);
        }
    }
}
