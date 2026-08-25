using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShitDesigner.Presentation {
	/// <summary>Runtime UI Toolkit composition root. No panel owns a project
	/// object; all changes flow through PresentationCoordinator.</summary>
	[RequireComponent(typeof(UIDocument))]
	public sealed class PresentationRoot : MonoBehaviour {
		[SerializeField] private UIDocument _document;
		[SerializeField] private StyleSheet _theme;
		private PresentationCoordinator _coordinator;
		private VisualElement _root;
		private Label _projectLabel;
		private Label _statusLabel;
		private Label _dirtyLabel;
		private Label _layoutLabel;
		private Label _bannerLabel;
		private VisualElement _workspace;
		private VisualElement _commandPalette;
		private PopupField<string> _recentProjects;
		private PopupField<string> _topProgramDisplay;
		private readonly Dictionary<string, string> _layoutChoiceIds = new Dictionary<string, string>(StringComparer.Ordinal);
		private readonly HashSet<string> _hiddenRecentRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		private string[] _appliedRecentRoots = Array.Empty<string>();
		private bool _recentProjectsInitialized;
		private string _activeLayoutId = string.Empty;
		private Guid _panelProjectionSessionId;
		private bool _panelProjectionInitialized;
		private IReadOnlyList<NodeCatalogItem> _panelCatalogProjection;
		private IReadOnlyList<LogicalControlReadModel> _panelControlsProjection;
		private IReadOnlyList<DashboardPageReadModel> _panelDashboardProjection;
		private IReadOnlyList<PresetListItemReadModel> _panelPresetProjection;
		private IReadOnlyList<MediaListItemReadModel> _panelMediaProjection;
		private WorkspaceReadModel _workspaceProjection;
		private VisualElement _settingsPopover;
		private PopupField<string> _settingsScale;
		private Toggle _settingsReduceMotion;
		private PopupField<string> _settingsTooltipDelay;
		private PopupField<string> _settingsMediaView;
		private PopupField<string> _settingsTheme;
		private TextField _settingsDiagnosticsFolder;
		private int _pendingRecentIndex = -1;
		private string _pendingRecentRoot = string.Empty;
		private readonly List<int> _lastPreviewQualityStages = new List<int>();
		private int _lastSuppressedPreviewCount = -1;
		private int _lastWarningCount = -1;
		private int _lastErrorCount = -1;
		private Guid _activePathRequestId;
		private Guid _activePathSessionId;
		private IPlatformFileInteractionAdapter _activePathPlatform;
		private bool _built;
		private PanelSettings _uiScalePanelSettings;
		private float _uiScaleBasePanelScale = 1f;
		private float _appliedUiScale = float.NaN;

		public UIDocument Document => _document;
		public VisualElement RootVisualElement => _root;
		public void ConfigureDocument(UIDocument document) {
			if (document == null) throw new ArgumentNullException(nameof(document));
			// The serialized production root builds during Awake before
			// ApplicationHost supplies that same UIDocument.
			// Keep a stable document idempotent, while BuildOnce below still
			// detects if UIDocument replaced its visual root during attachment.
			if (!ReferenceEquals(_document, document)) {
				_document = document;
				_built = false;
			}
			BuildOnce();
		}
		public void Configure(PresentationCoordinator coordinator) {
			if (_coordinator != null) _coordinator.ShellApplied -= ApplyShell;
			if (_coordinator != null) _coordinator.PanelsApplied -= ApplyPanels;
			if (_coordinator != null) _coordinator.WorkspaceApplied -= ApplyWorkspace;
			_coordinator = coordinator;
			BuildOnce();
			PresentationUiComposition.BindCoordinator(_workspace, _coordinator);
			if (_coordinator != null) { _coordinator.ShellApplied += ApplyShell; _coordinator.PanelsApplied += ApplyPanels; _coordinator.WorkspaceApplied += ApplyWorkspace; }
		}

		private void Awake() {
			if (_document == null) _document = GetComponent<UIDocument>();
			BuildOnce();
		}

		private void OnEnable() { BuildOnce(); }
		private void OnDisable() { if (_coordinator != null) { _coordinator.ShellApplied -= ApplyShell; _coordinator.PanelsApplied -= ApplyPanels; _coordinator.WorkspaceApplied -= ApplyWorkspace; } _coordinator = null; PresentationUiComposition.ReleasePreviewResources(); }

		public void ApplyLatestReadModels(ulong frameNumber) {
			if (_coordinator == null) return;
			_coordinator.ApplyLatestReadModels(frameNumber);
		}

		/// <summary>Presentation stage hook for the Bootstrap frame driver.
		/// Coordinator state is applied exactly once during the Application
		/// Apply stage; Present only ensures the host is alive and must never
		/// read or apply the envelope a second time.</summary>
		public void PresentCurrent() {
			BuildOnce();
		}

		public void SetReduceMotion(bool enabled) {
			BuildOnce();
			_root?.EnableInClassList("reduce-motion", enabled);
		}

		private void BuildOnce() {
			if (_document == null) return;
			var documentRoot = _document.rootVisualElement;
			if (documentRoot == null) return;
			if (_built && ReferenceEquals(_root, documentRoot)) return;

			// UIDocument can replace its root while attaching its serialized
			// PanelSettings. The Bootstrap then supplies the same UIDocument
			// after PresentationRoot.Awake. Build against the current visual
			// root, not merely the same UIDocument reference; otherwise the
			// stylesheet and controls stay on a detached tree while the live
			// panel lays out an unstyled replacement.
			_root = documentRoot;
			_built = true;
			_root.focusable = true;
			if (_theme == null) _theme = Resources.Load<StyleSheet>("PresentationTheme");
			if (_theme != null && !_root.styleSheets.Contains(_theme)) _root.styleSheets.Add(_theme);
			_root.AddToClassList("sd-root");
			var topBar = new VisualElement { name = "top-bar" };
			topBar.AddToClassList("sd-top-bar");
			_projectLabel = new Label("Untitled") { name = "project-name" };
			_projectLabel.AddToClassList("sd-project-name");
			_dirtyLabel = new Label { name = "dirty-state" };
			_statusLabel = new Label("Ready") { name = "status" };
			topBar.Add(_projectLabel);
			topBar.Add(_dirtyLabel);
			var appMenu = new Button(() => ToggleCommandPalette()) { text = "App", name = "app-menu", tooltip = "Application menu · Ctrl+K" };
			topBar.Insert(0, appMenu);
			AddProjectActions(topBar);
			var shellActions = new VisualElement { name = "shell-actions" };
			shellActions.AddToClassList("sd-shell-actions");
			shellActions.Add(new Button(() => _coordinator?.Submit("project.undo")) { text = "Undo", name = "shell-undo", tooltip = "Undo · Ctrl+Z" });
			shellActions.Add(new Button(() => _coordinator?.Submit("project.redo")) { text = "Redo", name = "shell-redo", tooltip = "Redo · Ctrl+Shift+Z" });
			shellActions.Add(new Button(() => _coordinator?.Submit("graph.clock.pause")) { text = "Pause", name = "graph-clock-pause", tooltip = "GraphClock Pause/Resume · Ctrl+Space" });
			var topLayout = new PopupField<string>("Layout", new List<string> { "Edit", "Live" }, 0) { name = "top-layout-selector" };
			topLayout.AddToClassList("sd-top-layout-selector");
			topLayout.RegisterValueChangedCallback(evt => _coordinator?.Submit("workspace.layout", evt.newValue,
				new KeyValuePairValue("layoutId", ResolveLayoutId(evt.newValue)), new KeyValuePairValue("operation", "select")));
			shellActions.Add(topLayout);
			_layoutLabel = new Label("Layout") { name = "top-layout-state" };
			shellActions.Add(_layoutLabel);
			shellActions.Add(new Button(() => _coordinator?.Submit("workspace.layout", _activeLayoutId,
				new KeyValuePairValue("layoutId", _activeLayoutId), new KeyValuePairValue("operation", "overwrite"))) { text = "Layout Save", name = "top-layout-save" });
			shellActions.Add(new Button(SaveLayoutAs) { text = "Layout Save As", name = "top-layout-save-as" });
			shellActions.Add(new Button(ToggleLayoutManagementPopover) { text = "Layout Manage", name = "top-layout-manage" });
			var displayCount = Math.Max(1, _coordinator?.DisplayIdentifyPort?.DisplayCount ?? 3);
			var displayChoices = Enumerable.Range(1, displayCount).Select(index => "Display " + index).ToList();
			_topProgramDisplay = new PopupField<string>("Display", displayChoices, Math.Min(1, displayChoices.Count - 1)) { name = "top-program-display-selector" };
			_topProgramDisplay.AddToClassList("sd-top-program-display-selector");
			_topProgramDisplay.RegisterValueChangedCallback(evt => {
				var display = ParseDisplayNumber(evt.newValue);
				_coordinator?.Submit("output.program.display", display.ToString(), new KeyValuePairValue("display", display.ToString()));
			});
			shellActions.Add(_topProgramDisplay);
			shellActions.Add(new Button(() => _coordinator?.Submit("diagnostics.focus")) { text = "Diagnostics", name = "top-diagnostics", tooltip = "Focus Diagnostics · Ctrl+Shift+D" });
			topBar.Add(shellActions);
			_root.Add(topBar);
			_bannerLabel = new Label { name = "banner-layer" };
			_bannerLabel.AddToClassList("sd-banner");
			_root.Add(_bannerLabel);
			_workspace = new VisualElement { name = "dock-workspace" };
			_workspace.AddToClassList("sd-dock-workspace");
			PresentationUiComposition.ComposeWorkspace(_workspace, _coordinator);
			_root.Add(_workspace);
			var statusBar = new VisualElement { name = "status-bar" };
			statusBar.AddToClassList("sd-status-bar");
			statusBar.Add(MonoStatusLabel("GraphClock 0 · Running", "graph-clock-status"));
			statusBar.Add(MonoStatusLabel("Program fps Unavailable", "program-fps"));
			statusBar.Add(MonoStatusLabel("CPU Frame Time Unavailable", "cpu-frame-time"));
			statusBar.Add(MonoStatusLabel("GPU Frame Time Unavailable", "gpu-frame-time"));
			statusBar.Add(MonoStatusLabel("Preview Quality 4 · suppressed 0", "preview-quality-status"));
			statusBar.Add(MonoStatusLabel("Warnings 0 · Errors 0", "diagnostics-count"));
			statusBar.Add(_statusLabel);
			// Kept as a compatibility/readability alias for existing Player
			// fixtures that query the old shell status element.
			statusBar.Add(new Label("Graph ready") { name = "graph-status" });
			_root.Add(statusBar);
			var modal = new VisualElement { name = "modal-layer", pickingMode = PickingMode.Ignore };
			modal.AddToClassList("sd-layer");
			_root.Add(modal);
			var popover = new VisualElement { name = "popover-layer", pickingMode = PickingMode.Ignore };
			popover.AddToClassList("sd-layer");
			_root.Add(popover);
			var drag = new VisualElement { name = "drag-layer", pickingMode = PickingMode.Ignore };
			drag.AddToClassList("sd-layer");
			_root.Add(drag);
			var toast = new VisualElement { name = "toast-layer", pickingMode = PickingMode.Ignore };
			toast.AddToClassList("sd-layer");
			_root.Add(toast);
			_root.RegisterCallback<KeyDownEvent>(OnRootKeyDown);
		}

		private static Label MonoStatusLabel(string text, string name) {
			var label = new Label(text) { name = name };
			label.AddToClassList("sd-mono");
			return label;
		}

		private string ResolveLayoutId(string displayValue) {
			return _layoutChoiceIds.TryGetValue(displayValue ?? string.Empty, out var id) ? id : displayValue ?? string.Empty;
		}

		private void OnRootKeyDown(KeyDownEvent evt) {
			// Primary is Control on Windows/Linux and Command on macOS. Do
			// not steal text-entry keystrokes from a focused field.
			if (evt.target is TextField) return;
			var primary = (evt.modifiers & (EventModifiers.Control | EventModifiers.Command)) != 0;
			if (primary && evt.keyCode == KeyCode.K) {
				ToggleCommandPalette();
				evt.StopPropagation();
			}
			else if (evt.keyCode == KeyCode.Escape && _commandPalette != null) {
				HideCommandPalette();
				evt.StopPropagation();
			}
		}

		private void ToggleCommandPalette() {
			if (_commandPalette != null) { HideCommandPalette(); return; }
			var layer = _root?.Q("popover-layer");
			if (layer == null) return;
			_commandPalette = new VisualElement { name = "command-palette" };
			_commandPalette.AddToClassList("sd-command-palette");
			_commandPalette.Add(new TextField("Search commands") { name = "command-palette-search" });
			var commands = new[]
			{
				new[] { "Save", "project.save" },
				new[] { "Undo", "project.undo" },
				new[] { "Redo", "project.redo" },
				new[] { "Focus Graph Selection", "graph.focus_selection" },
				new[] { "Focus All Graph Nodes", "graph.focus_all" },
				new[] { "Hide Preview Host", "preview.host.visible" }
			};
			var list = new VisualElement { name = "command-palette-results" };
			foreach (var entry in commands) {
				var commandId = entry[1];
				var button = new Button(() => {
					var result = _coordinator?.Submit(commandId, commandId);
					if (result != null && result.Status == PresentationCommandStatus.Rejected) {
						_bannerLabel.text = "Command rejected: " + result.Diagnostic;
						_bannerLabel.EnableInClassList("is-visible", true);
					}
					HideCommandPalette();
				}) { text = entry[0], name = "command-palette-" + commandId.Replace('.', '-') };
				button.tooltip = "Run " + entry[0];
				list.Add(button);
			}
			_commandPalette.Add(list);
			AddProjectMenuActions(_commandPalette);
			layer.Add(_commandPalette);
			_commandPalette.Q<TextField>("command-palette-search")?.Focus();
		}

		private void HideCommandPalette() {
			if (_commandPalette == null) return;
			_commandPalette.RemoveFromHierarchy();
			_commandPalette = null;
		}

		private void ToggleSettingsPopover() {
			if (_settingsPopover != null) {
				_settingsPopover.RemoveFromHierarchy();
				_settingsPopover = null;
				return;
			}
			var layer = _root?.Q("popover-layer");
			if (layer == null) return;
			_settingsPopover = new VisualElement { name = "settings-popover" };
			_settingsPopover.AddToClassList("sd-command-palette");
			_settingsPopover.Add(new Label("Settings") { name = "settings-title" });

			_settingsScale = new PopupField<string>("UI Scale", new List<string> { "100%", "125%", "150%" }, 0) { name = "settings-ui-scale" };
			_settingsScale.RegisterValueChangedCallback(evt => {
				var text = (evt.newValue ?? string.Empty).TrimEnd('%');
				if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var percent))
					_coordinator?.Submit("workspace.ui_scale", "settings", new KeyValuePairValue("value", (percent / 100f).ToString(System.Globalization.CultureInfo.InvariantCulture)));
			});
			_settingsPopover.Add(_settingsScale);

			_settingsTheme = new PopupField<string>("Theme", new List<string> { "Dark" }, 0) { name = "settings-theme" };
			_settingsTheme.tooltip = "Dark Theme is the only initial theme.";
			_settingsTheme.RegisterValueChangedCallback(evt => _coordinator?.Submit("workspace.theme", "settings", new KeyValuePairValue("value", evt.newValue)));
			_settingsPopover.Add(_settingsTheme);

			_settingsReduceMotion = new Toggle("Reduce Motion") { name = "settings-reduce-motion" };
			_settingsReduceMotion.RegisterValueChangedCallback(evt => _coordinator?.Submit("workspace.reduce_motion", "settings", new KeyValuePairValue("value", evt.newValue ? "true" : "false")));
			_settingsPopover.Add(_settingsReduceMotion);

			_settingsTooltipDelay = new PopupField<string>("Tooltip Delay", new List<string> { "250 ms", "500 ms", "1000 ms" }, 1) { name = "settings-tooltip-delay" };
			_settingsTooltipDelay.RegisterValueChangedCallback(evt => {
				var value = evt.newValue != null && evt.newValue.StartsWith("250", StringComparison.Ordinal) ? .25f : evt.newValue != null && evt.newValue.StartsWith("1000", StringComparison.Ordinal) ? 1f : .5f;
				_coordinator?.Submit("workspace.tooltip_delay", "settings", new KeyValuePairValue("value", value.ToString(System.Globalization.CultureInfo.InvariantCulture)));
			});
			_settingsPopover.Add(_settingsTooltipDelay);

			_settingsMediaView = new PopupField<string>("Media Library", new List<string> { "Grid", "List" }, 0) { name = "settings-media-view" };
			_settingsMediaView.RegisterValueChangedCallback(evt => _coordinator?.Submit("workspace.media_view", "settings", new KeyValuePairValue("value", evt.newValue)));
			_settingsPopover.Add(_settingsMediaView);

			var folderRow = new VisualElement { name = "settings-diagnostics-folder-row" };
			_settingsDiagnosticsFolder = new TextField("Diagnostics Export Folder") { name = "settings-diagnostics-folder", isReadOnly = true };
			folderRow.Add(_settingsDiagnosticsFolder);
			folderRow.Add(new Button(BeginDiagnosticsFolderSelection) { text = "Choose", name = "settings-diagnostics-folder-choose" });
			_settingsPopover.Add(folderRow);
			layer.Add(_settingsPopover);
			ApplyWorkspace(_coordinator?.Current?.Workspace);
		}

		private void BeginDiagnosticsFolderSelection() {
			var platform = _coordinator?.PlatformFiles;
			if (platform == null) {
				if (_settingsDiagnosticsFolder != null) _settingsDiagnosticsFolder.value = "File/folder selection unavailable";
				return;
			}
			var sessionId = _coordinator.CurrentEnvelope?.ProjectSessionId ?? Guid.Empty;
			var requestId = Guid.NewGuid();
			platform.PickPath(new PlatformPathRequest(requestId, sessionId, PlatformPathRequestKind.Folder, "Diagnostics export folder"), result => {
				if (result == null || !result.Succeeded || result.AbsolutePaths.Count == 0 || sessionId != (_coordinator?.CurrentEnvelope?.ProjectSessionId ?? Guid.Empty)) return;
				var path = result.AbsolutePaths[0];
				_coordinator?.Submit("workspace.diagnostics_folder", "settings", new KeyValuePairValue("path", path));
			});
		}

		private void AddProjectActions(VisualElement topBar) {
			var actions = new VisualElement { name = "project-actions" };
			actions.AddToClassList("sd-project-actions");
			var save = new Button(() => SubmitProjectAction("project.save", false)) { text = "Save", name = "project-save" };
			actions.Add(save);
			topBar.Add(actions);
		}

		private void AddProjectMenuActions(VisualElement host) {
			var actions = new VisualElement { name = "project-menu-actions" };
			actions.AddToClassList("sd-project-menu-actions");
			actions.Add(new Button(() => SubmitProjectAction("project.new", true)) { text = "New", name = "project-new" });
			var newProjectName = new TextField { value = "Untitled", name = "new-project-name", tooltip = "Project name used by New" };
			actions.Add(newProjectName);
			actions.Add(new Button(CancelProjectPathSelection) { text = "Cancel Picker", name = "project-path-cancel" });
			actions.Add(new Button(() => SubmitProjectAction("project.open", true)) { text = "Open", name = "project-open" });
			_recentProjects = new PopupField<string>("Open Recent", new List<string> { "No recent projects" }, 0) { name = "project-open-recent" };
			_recentProjects.RegisterValueChangedCallback(evt => {
				var index = _recentProjects.choices.IndexOf(evt.newValue);
				if (index >= 0 && index < 10 && !string.Equals(evt.newValue, "No recent projects", StringComparison.Ordinal)) {
					if (!Directory.Exists(evt.newValue) && !File.Exists(evt.newValue)) ShowMissingRecentDialog(index, evt.newValue);
					else _coordinator?.Submit("project.open_recent", evt.newValue, new KeyValuePairValue("index", index.ToString()), new KeyValuePairValue("decision", "Cancel"));
				}
			});
			actions.Add(_recentProjects);
			actions.Add(new Button(() => SubmitProjectAction("project.save_as", false)) { text = "Save As", name = "project-save-as" });
			actions.Add(new Button(() => SubmitProjectAction("project.close", true)) { text = "Close", name = "project-close" });
			actions.Add(new Button(() => SubmitProjectAction("project.exit", true)) { text = "Exit", name = "project-exit" });
			actions.Add(new Button(ToggleSettingsPopover) { text = "Settings", name = "top-settings", tooltip = "User settings" });
			host.Add(actions);
			UpdateRecentProjects(_coordinator?.Current?.RecentProjectRoots);
		}

		private void SaveLayoutAs() {
			var sourceId = string.IsNullOrWhiteSpace(_activeLayoutId) ? "layout" : _activeLayoutId;
			var copyId = sourceId + "-Copy-" + Guid.NewGuid().ToString("N").Substring(0, 6);
			_coordinator?.Submit("workspace.layout", sourceId,
				new KeyValuePairValue("layoutId", sourceId),
				new KeyValuePairValue("operation", "duplicate"),
				new KeyValuePairValue("name", copyId),
				new KeyValuePairValue("newLayoutId", copyId));
		}

		private void ToggleLayoutManagementPopover() {
			var layer = _root?.Q("popover-layer");
			if (layer == null) return;
			var existing = layer.Q("layout-management-popover");
			if (existing != null) { existing.RemoveFromHierarchy(); return; }
			var popover = new VisualElement { name = "layout-management-popover" };
			popover.AddToClassList("sd-command-palette");
			popover.Add(new Label("Layout Management") { name = "layout-management-title" });
			popover.Add(new Button(SaveLayoutAs) { text = "Duplicate Layout", name = "layout-manage-duplicate" });
			var confirmDelete = new Button(() => {
				_coordinator?.Submit("workspace.layout", _activeLayoutId,
					new KeyValuePairValue("layoutId", _activeLayoutId), new KeyValuePairValue("operation", "delete"));
				popover.RemoveFromHierarchy();
			}) { text = "Confirm Delete Layout", name = "layout-manage-delete-confirm" };
			confirmDelete.style.display = DisplayStyle.None;
			popover.Add(new Button(() => confirmDelete.style.display = DisplayStyle.Flex) { text = "Delete Layout", name = "layout-manage-delete" });
			popover.Add(confirmDelete);
			layer.Add(popover);
		}

		private void ShowMissingRecentDialog(int index, string root) {
			_pendingRecentIndex = index;
			_pendingRecentRoot = root ?? string.Empty;
			var modal = _root?.Q("modal-layer");
			if (modal == null) return;
			modal.Clear();
			modal.pickingMode = PickingMode.Position;
			modal.style.display = DisplayStyle.Flex;
			modal.AddToClassList("is-visible");
			var dialog = new VisualElement { name = "missing-recent-dialog" };
			dialog.AddToClassList("sd-dialog");
			dialog.Add(new Label("Recent project is missing") { name = "missing-recent-title" });
			dialog.Add(new Label(_pendingRecentRoot + "\nRemove it from Open Recent?") { name = "missing-recent-message" });
			var buttons = new VisualElement { name = "missing-recent-actions" };
			buttons.Add(new Button(() => {
				var result = _coordinator?.Submit("workspace.recent.remove", _pendingRecentRoot, new KeyValuePairValue("root", _pendingRecentRoot));
				if (result == null || result.Status != PresentationCommandStatus.Rejected) {
					_hiddenRecentRoots.Add(_pendingRecentRoot);
					if (_recentProjects != null) {
						_recentProjects.choices.RemoveAll(root => string.Equals(root, _pendingRecentRoot, StringComparison.OrdinalIgnoreCase));
						if (_recentProjects.choices.Count == 0) _recentProjects.choices.Add("No recent projects");
						_recentProjects.SetValueWithoutNotify(_recentProjects.choices[0]);
					}
				}
				HideModal(modal);
			}) { text = "Remove", name = "missing-recent-remove" });
			buttons.Add(new Button(() => HideModal(modal)) { text = "Cancel", name = "missing-recent-cancel" });
			dialog.Add(buttons);
			modal.Add(dialog);
		}

		private void SubmitProjectAction(string commandId, bool requiresDecision) {
			if (requiresDecision && _coordinator?.Current?.Shell?.ProjectDirty == true) {
				ShowUnsavedChangesDialog(commandId);
				return;
			}
			ContinueProjectAction(commandId, "Cancel");
		}

		private void ContinueProjectAction(string commandId, string decision) {
			if (commandId == "project.new" || commandId == "project.open" || commandId == "project.save_as") {
				BeginProjectPathSelection(commandId, decision);
				return;
			}
			SubmitProjectCommand(commandId, decision);
		}

		private void SubmitProjectCommand(string commandId, string decision, string root = null, string name = null) {
			var payload = new List<KeyValuePairValue> { new KeyValuePairValue("decision", decision) };
			if (!string.IsNullOrWhiteSpace(root)) payload.Add(new KeyValuePairValue("root", root));
			if (!string.IsNullOrWhiteSpace(name)) payload.Add(new KeyValuePairValue("name", name));
			var result = _coordinator?.Submit(commandId, commandId, payload.ToArray());
			if (result == null) return;
			_bannerLabel.text = result.Status == PresentationCommandStatus.Rejected ? "Command rejected: " + result.Diagnostic : result.Status.ToString();
			_bannerLabel.EnableInClassList("is-visible", result.Status == PresentationCommandStatus.Rejected);
		}

		private void BeginProjectPathSelection(string commandId, string decision) {
			var platform = _coordinator?.PlatformFiles;
			var sessionId = _coordinator?.CurrentEnvelope?.ProjectSessionId ?? Guid.Empty;
			if (platform == null) {
				_bannerLabel.text = "File/folder selection unavailable";
				_bannerLabel.EnableInClassList("is-visible", true);
				return;
			}
			var requestId = Guid.NewGuid();
			_activePathRequestId = requestId;
			_activePathSessionId = sessionId;
			_activePathPlatform = platform;
			platform.PickPath(new PlatformPathRequest(requestId, sessionId, PlatformPathRequestKind.Folder, commandId == "project.open" ? "Open project folder" : "Choose project folder"), result => {
				if (requestId != _activePathRequestId || sessionId != (_coordinator?.CurrentEnvelope?.ProjectSessionId ?? Guid.Empty)) return;
				_activePathRequestId = Guid.Empty;
				_activePathPlatform = null;
				if (result == null || !result.Succeeded || result.AbsolutePaths.Count == 0) {
					_bannerLabel.text = string.IsNullOrWhiteSpace(result?.Error) ? "Selection cancelled" : result.Error;
					_bannerLabel.EnableInClassList("is-visible", !string.IsNullOrWhiteSpace(_bannerLabel.text));
					return;
				}
				var root = result.AbsolutePaths[0];
				var name = _root.Q<TextField>("new-project-name")?.value ?? "Untitled";
				SubmitProjectCommand(commandId, decision, root, name);
			});
		}

		private void CancelProjectPathSelection() {
			if (_activePathRequestId == Guid.Empty || _activePathPlatform == null) return;
			_activePathPlatform.Cancel(_activePathRequestId);
			_activePathRequestId = Guid.Empty;
			_activePathPlatform = null;
			_bannerLabel.text = "Selection cancelled";
			_bannerLabel.EnableInClassList("is-visible", true);
		}

		private void ShowUnsavedChangesDialog(string commandId) {
			var modal = _root?.Q("modal-layer");
			if (modal == null) return;
			modal.Clear();
			modal.pickingMode = PickingMode.Position;
			modal.style.display = DisplayStyle.Flex;
			modal.AddToClassList("is-visible");
			var dialog = new VisualElement { name = "unsaved-changes-dialog" };
			dialog.AddToClassList("sd-dialog");
			dialog.Add(new Label("Unsaved project changes") { name = "unsaved-changes-title" });
			dialog.Add(new Label("Save before continuing?") { name = "unsaved-changes-message" });
			var buttons = new VisualElement { name = "unsaved-changes-actions" };
			buttons.Add(new Button(() => { HideModal(modal); ContinueProjectAction(commandId, "Save"); }) { text = "Save", name = "unsaved-save" });
			buttons.Add(new Button(() => { HideModal(modal); ContinueProjectAction(commandId, "Discard"); }) { text = "Discard", name = "unsaved-discard" });
			buttons.Add(new Button(() => HideModal(modal)) { text = "Cancel", name = "unsaved-cancel" });
			dialog.Add(buttons);
			modal.Add(dialog);
		}

		private static void HideModal(VisualElement modal) {
			modal.Clear();
			modal.RemoveFromClassList("is-visible");
			modal.pickingMode = PickingMode.Ignore;
			modal.style.display = DisplayStyle.None;
		}

		private void ApplyShell(PresentationReadModel model) {
			if (model?.Shell == null) return;
			var projectName = string.IsNullOrEmpty(model.Shell.ProjectName) ? "Untitled" : model.Shell.ProjectName;
			SetLabelText(_projectLabel, projectName + (model.Shell.ProjectDirty ? " *" : string.Empty));
			SetLabelText(_dirtyLabel, model.Shell.ProjectDirty ? "Project Dirty" : "Project Saved");
			SetLabelText(_statusLabel, model.Shell.StatusText);
			var graphClock = _root?.Q<Label>("graph-clock-status");
			if (graphClock != null)
				SetLabelText(graphClock, "GraphClock " + model.Shell.GraphClockFrame + " · " + (model.Shell.GraphClockPaused ? "Paused" : "Running"));
			UpdateRecentProjects(model.RecentProjectRoots);
			SetLabelText(_bannerLabel, model.Shell.Recovered ? "Recovered project: save to keep changes" : string.Empty);
			_bannerLabel.EnableInClassList("is-visible", model.Shell.Recovered);
		}

		private void ApplyPanels(PresentationReadModel model) {
			var envelope = _coordinator?.CurrentEnvelope;
			var structuralProjection = !_panelProjectionInitialized
				|| envelope == null
				|| envelope.IsFullSnapshot
				|| envelope.ProjectSessionId != _panelProjectionSessionId
				|| !ReferenceEquals(_panelCatalogProjection, model?.NodeCatalog)
				|| !ReferenceEquals(_panelDashboardProjection, model?.DashboardPages)
				|| !ReferenceEquals(_panelPresetProjection, model?.Presets)
				|| !ReferenceEquals(_panelMediaProjection, model?.Media);
			if (structuralProjection || !PresentationUiComposition.ApplyDynamicReadModel(_workspace, model, _coordinator)) {
				PresentationUiComposition.ApplyReadModel(_workspace, model, _coordinator);
				_panelProjectionInitialized = true;
				_panelProjectionSessionId = envelope?.ProjectSessionId ?? Guid.Empty;
				_panelCatalogProjection = model?.NodeCatalog;
				_panelDashboardProjection = model?.DashboardPages;
				_panelPresetProjection = model?.Presets;
				_panelMediaProjection = model?.Media;
			}
			if (model == null) return;
			var output = model.Output;
			// Shell is a source-keyed static slice. The fresh envelope owns
			// the frame clock, so refresh this one dynamic label from the
			// Panels route without waking the rest of the shell tree.
			var graphClock = _root?.Q<Label>("graph-clock-status");
			if (graphClock != null)
				SetLabelText(graphClock, "GraphClock " + (envelope?.FrameNumber ?? model.Shell?.GraphClockFrame ?? 0UL) +
					" · " + ((output?.IsPaused ?? model.Shell?.GraphClockPaused ?? false) ? "Paused" : "Running"));
			var fps = FormatStatusFps(output?.MeasuredFramesPerSecond ?? double.NaN);
			var cpu = FormatStatusMilliseconds(output?.CpuFrameTimeMilliseconds ?? double.NaN);
			var gpu = FormatStatusMilliseconds(output?.GpuFrameTimeMilliseconds ?? double.NaN);
			var fpsLabel = _root?.Q<Label>("program-fps"); if (fpsLabel != null) SetLabelText(fpsLabel, "Program fps " + fps);
			var cpuLabel = _root?.Q<Label>("cpu-frame-time"); if (cpuLabel != null) SetLabelText(cpuLabel, "CPU Frame Time " + cpu);
			var gpuLabel = _root?.Q<Label>("gpu-frame-time"); if (gpuLabel != null) SetLabelText(gpuLabel, "GPU Frame Time " + gpu);
			var previews = output?.Previews;
			var suppressed = 0;
			var qualityChanged = previews == null ? _lastPreviewQualityStages.Count != 0 : _lastPreviewQualityStages.Count != previews.Count;
			if (previews != null) for (var index = 0; index < previews.Count; index++) {
				var stage = (int)previews[index].Quality;
				if (stage != (int)PresentationQualityStage.Full) suppressed++;
				if (!qualityChanged && _lastPreviewQualityStages[index] != stage) qualityChanged = true;
			}
			if (qualityChanged || _lastSuppressedPreviewCount != suppressed) {
				_lastPreviewQualityStages.Clear();
				if (previews != null) for (var index = 0; index < previews.Count; index++) _lastPreviewQualityStages.Add((int)previews[index].Quality);
				_lastSuppressedPreviewCount = suppressed;
				var quality = previews == null || previews.Count == 0 ? "4" : string.Join(",", _lastPreviewQualityStages);
				var qualityLabel = _root?.Q<Label>("preview-quality-status"); if (qualityLabel != null) SetLabelText(qualityLabel, "Preview Quality " + quality + " · suppressed " + suppressed);
			}
			var warningCount = 0; var errorCount = 0;
			foreach (var diagnostic in model.Diagnostics ?? Array.Empty<DiagnosticReadModel>()) {
				if (diagnostic.Severity == PresentationSeverity.Warning) warningCount++;
				else if (diagnostic.Severity == PresentationSeverity.Error || diagnostic.Severity == PresentationSeverity.Fatal) errorCount++;
			}
			if (_lastWarningCount != warningCount || _lastErrorCount != errorCount) {
				_lastWarningCount = warningCount; _lastErrorCount = errorCount;
				var diagnosticsLabel = _root?.Q<Label>("diagnostics-count"); if (diagnosticsLabel != null) SetLabelText(diagnosticsLabel, "Warnings " + warningCount + " · Errors " + errorCount);
			}
			if (_topProgramDisplay != null && output != null) {
				var selected = "Display " + Math.Max(1, output.ProgramDisplay);
				if (!_topProgramDisplay.choices.Contains(selected)) _topProgramDisplay.choices.Add(selected);
				_topProgramDisplay.SetValueWithoutNotify(selected);
			}
		}

		private void UpdateRecentProjects(IEnumerable<string> roots) {
			if (_recentProjects == null) return;
			if (_recentProjectsInitialized && RecentRootsMatch(roots)) return;
			_recentProjects.choices.Clear();
			var visibleRoots = (roots ?? Array.Empty<string>()).Where(root => !_hiddenRecentRoots.Contains(root)).Take(10).ToArray();
			_appliedRecentRoots = visibleRoots;
			_recentProjectsInitialized = true;
			_recentProjects.choices.AddRange(visibleRoots.Length == 0 ? new[] { "No recent projects" } : visibleRoots);
			_recentProjects.SetValueWithoutNotify(_recentProjects.choices[0]);
		}

		private bool RecentRootsMatch(IEnumerable<string> roots) {
			var index = 0;
			foreach (var root in roots ?? Array.Empty<string>()) {
				if (_hiddenRecentRoots.Contains(root)) continue;
				if (index >= 10) break;
				if (index >= _appliedRecentRoots.Length || !string.Equals(_appliedRecentRoots[index], root, StringComparison.Ordinal)) return false;
				index++;
			}
			return index == _appliedRecentRoots.Length;
		}

		private static int ParseDisplayNumber(string value) {
			var digits = new string((value ?? string.Empty).Where(char.IsDigit).ToArray());
			return int.TryParse(digits, out var display) ? Math.Max(1, display) : 2;
		}

		private void ApplyWorkspace(WorkspaceReadModel workspace) {
			if (workspace == null) return;
			if (ReferenceEquals(_workspaceProjection, workspace)) { ApplyWorkspaceSettings(workspace); return; }
			_workspaceProjection = workspace;
			ApplyUiScale(workspace.UiScale);
			SetReduceMotion(workspace.ReduceMotion);
			_activeLayoutId = workspace.CurrentLayoutId ?? string.Empty;
			var activePreset = (workspace.Presets ?? Array.Empty<LayoutPreset>()).FirstOrDefault(x => string.Equals(x.Id, _activeLayoutId, StringComparison.Ordinal));
			if (_layoutLabel != null)
				_layoutLabel.text = (string.IsNullOrEmpty(activePreset?.Name) ? (string.IsNullOrEmpty(workspace.CurrentLayoutId) ? "Layout" : workspace.CurrentLayoutId) : activePreset.Name) + (workspace.LayoutDirty ? " *" : string.Empty);
			var selector = _root?.Q<PopupField<string>>("top-layout-selector");
			if (selector != null) {
				_layoutChoiceIds.Clear();
				var choices = new List<string>();
				foreach (var preset in workspace.Presets ?? Array.Empty<LayoutPreset>()) {
					if (string.IsNullOrWhiteSpace(preset?.Id)) continue;
					var display = string.IsNullOrWhiteSpace(preset.Name) ? preset.Id : preset.Name;
					if (_layoutChoiceIds.ContainsKey(display)) display = display + " (" + preset.Id + ")";
					_layoutChoiceIds[display] = preset.Id;
					choices.Add(display);
				}
				if (choices.Count == 0) { choices.Add("Edit"); _layoutChoiceIds["Edit"] = "Edit"; }
				selector.choices.Clear(); selector.choices.AddRange(choices);
				var selected = _layoutChoiceIds.FirstOrDefault(x => string.Equals(x.Value, workspace.CurrentLayoutId, StringComparison.Ordinal)).Key;
				selector.SetValueWithoutNotify(string.IsNullOrEmpty(selected) ? choices[0] : selected);
			}
			ApplyWorkspaceSettings(workspace);
		}

		private void ApplyWorkspaceSettings(WorkspaceReadModel workspace) {
			if (workspace == null) return;
			if (_settingsScale != null) _settingsScale.SetValueWithoutNotify(workspace.UiScale >= 1.49f ? "150%" : workspace.UiScale >= 1.24f ? "125%" : "100%");
			if (_settingsTheme != null) _settingsTheme.SetValueWithoutNotify(string.IsNullOrWhiteSpace(workspace.Theme) ? "Dark" : workspace.Theme);
			if (_settingsReduceMotion != null) _settingsReduceMotion.SetValueWithoutNotify(workspace.ReduceMotion);
			if (_settingsTooltipDelay != null) _settingsTooltipDelay.SetValueWithoutNotify(workspace.TooltipDelaySeconds <= .26f ? "250 ms" : workspace.TooltipDelaySeconds >= .9f ? "1000 ms" : "500 ms");
			if (_settingsMediaView != null) _settingsMediaView.SetValueWithoutNotify(string.Equals(workspace.MediaLibraryView, "List", StringComparison.OrdinalIgnoreCase) ? "List" : "Grid");
			if (_settingsDiagnosticsFolder != null) _settingsDiagnosticsFolder.SetValueWithoutNotify(workspace.DiagnosticsExportFolder ?? string.Empty);
		}

		/// <summary>Projects the persisted application UI scale onto the
		/// Player-owned PanelSettings copy.  Panel scale performs the visual
		/// transform; the root token class is updated in the same operation
		/// so USS design tokens can stay aligned without multiplying every
		/// individual control size.</summary>
		private void ApplyUiScale(float requestedScale) {
			var scale = NormalizeUiScale(requestedScale);
			var panelSettings = _document?.panelSettings;
			if (!ReferenceEquals(_uiScalePanelSettings, panelSettings)) {
				_uiScalePanelSettings = panelSettings;
				_uiScaleBasePanelScale = panelSettings == null || panelSettings.scale <= 0f ? 1f : panelSettings.scale;
				_appliedUiScale = float.NaN;
			}

			if (panelSettings != null && !Mathf.Approximately(_appliedUiScale, scale)) {
				panelSettings.scale = _uiScaleBasePanelScale * scale;
				_appliedUiScale = scale;
			}

			if (_root == null) return;
			_root.EnableInClassList("sd-ui-scale-100", Mathf.Approximately(scale, 1f));
			_root.EnableInClassList("sd-ui-scale-125", Mathf.Approximately(scale, 1.25f));
			_root.EnableInClassList("sd-ui-scale-150", Mathf.Approximately(scale, 1.5f));
		}

		private static float NormalizeUiScale(float value) {
			if (value >= 1.375f) return 1.5f;
			if (value >= 1.125f) return 1.25f;
			return 1f;
		}

		private static void SetLabelText(Label label, string value) {
			if (label == null || string.Equals(label.text, value, StringComparison.Ordinal)) return;
			label.text = value;
		}

		private static string FormatStatusMilliseconds(double value) {
			return double.IsNaN(value) || double.IsInfinity(value) || value <= 0d ? "Unavailable" : value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms";
		}

		private static string FormatStatusFps(double value) {
			return double.IsNaN(value) || double.IsInfinity(value) || value <= 0d ? "Unavailable" : value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
		}
	}
}
