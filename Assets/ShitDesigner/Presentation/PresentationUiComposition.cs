#if UNITY_5_3_OR_NEWER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using ShitDesigner.Application;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShitDesigner.Presentation
{
    /// <summary>Runtime UI Toolkit-only panel composition.  Every panel is
    /// independently named so PlayMode acceptance tests can query the tree.</summary>
    public static class PresentationUiComposition
    {
        /// <summary>Long-lived extension point for node-specific inspector
        /// controls. Factories are resolved per apply, but registrations are
        /// not recreated with every read-model refresh.</summary>
        public static ParameterControlCatalog CustomParameterControls { get; } = new ParameterControlCatalog();
        private static Texture2D _checkerTexture;
        private static readonly ConditionalWeakTable<VisualElement, SurfaceBindingState> SurfaceBindings = new ConditionalWeakTable<VisualElement, SurfaceBindingState>();
        private static readonly ConditionalWeakTable<VisualElement, Dictionary<string, VisualElement>> ParameterRowBindings = new ConditionalWeakTable<VisualElement, Dictionary<string, VisualElement>>();
        private static readonly ConditionalWeakTable<VisualElement, DiagnosticBindingState> DiagnosticBindings = new ConditionalWeakTable<VisualElement, DiagnosticBindingState>();

        private sealed class SurfaceBindingState
        {
            public object Texture;
            public ulong Generation;
            public bool IsBound;
            public string Tooltip;
        }

        private sealed class DiagnosticBindingState
        {
            public object Source;
            public readonly Dictionary<string, Button> Entries = new Dictionary<string, Button>(StringComparer.Ordinal);
            public readonly HashSet<string> Seen = new HashSet<string>(StringComparer.Ordinal);
            public readonly List<string> Removed = new List<string>();
        }

        /// <summary>Shared 8x8 checker tile used for transparent Preview
        /// backgrounds. It is explicitly released by the Presentation root
        /// when the UI lifetime ends.</summary>
        public static void ReleasePreviewResources()
        {
            if (_checkerTexture == null) return;
            if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_checkerTexture);
            else UnityEngine.Object.DestroyImmediate(_checkerTexture);
            _checkerTexture = null;
        }

        private static Texture2D CheckerTexture()
        {
            if (_checkerTexture != null) return _checkerTexture;
            _checkerTexture = new Texture2D(8, 8, TextureFormat.RGBA32, false, true) { name = "PresentationPreviewChecker" };
            for (var y = 0; y < 8; y++) for (var x = 0; x < 8; x++)
                _checkerTexture.SetPixel(x, y, ((x / 4 + y / 4) & 1) == 0 ? new Color(0.22f, 0.24f, 0.28f, 1f) : new Color(0.42f, 0.45f, 0.5f, 1f));
            // Keep the tiny fixture readable: PlayMode GPU/pixel tests use
            // GetPixel to verify that Checker differs from a solid Black
            // background, and the texture is released with the UI lifetime.
            _checkerTexture.Apply(false, false);
            return _checkerTexture;
        }

        public static VisualElement ComposeWorkspace(VisualElement host, PresentationCoordinator coordinator)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            host.Clear();
            host.name = "dock-tree";
            host.AddToClassList("sd-dock-tree");
            var row = new VisualElement { name = "dock-row" };
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexGrow = 1;
            var library = CreatePanel("node-library", "Node Library");
            library.style.width = 240;
            library.style.minWidth = 240;
            AddLibraryItems(library, coordinator);
            row.Add(library);
            var graph = CreatePanel("node-graph-panel", "Node Graph");
            graph.style.flexGrow = 1;
            var graphToolbar = new VisualElement { name = "graph-toolbar" };
            graphToolbar.AddToClassList("sd-graph-toolbar");
            graphToolbar.Add(new Button(() => graph.Q<GraphCanvasElement>("node-graph-canvas")?.ToggleGridSnap()) { text = "Grid", name = "graph-toggle-grid" });
            graphToolbar.Add(new Button(() => graph.Q<GraphCanvasElement>("node-graph-canvas")?.ToggleMinimap()) { text = "Minimap", name = "graph-toggle-minimap" });
            graphToolbar.Add(new Label("25–200%") { name = "graph-zoom-range" });
            graph.Add(graphToolbar);
            var canvas = new GraphCanvasElement(coordinator) { name = "node-graph-canvas" };
            canvas.AddToClassList("sd-graph-canvas");
            graph.Add(canvas);
            row.Add(graph);
            var inspector = CreatePanel("inspector-panel", "Inspector");
            inspector.style.width = 280;
            inspector.style.minWidth = 240;
            AddInspector(inspector, coordinator);
            row.Add(inspector);
            host.Add(row);
            AddDockControls(host, coordinator);
            var bottom = CreatePanel("dashboard-panel", "Live Dashboard");
            bottom.style.height = 160;
            AddDashboard(bottom, coordinator);
            host.Add(bottom);
            var outputs = CreatePanel("outputs-row", "Outputs");
            outputs.style.height = 160;
            AddOutputs(outputs, coordinator);
            host.Add(outputs);
            var lower = new VisualElement { name = "utility-panels" };
            lower.style.flexDirection = FlexDirection.Row;
            lower.style.height = 160;
            AddPresets(lower, coordinator);
            AddControls(lower, coordinator);
            AddMedia(lower, coordinator);
            AddDiagnostics(lower, coordinator);
            host.Add(lower);
            return host;
        }

        public static void BindCoordinator(VisualElement host, PresentationCoordinator coordinator)
        {
            if (host == null) return;
            host.Q<GraphCanvasElement>("node-graph-canvas")?.SetCoordinator(coordinator);
            RebuildLibrary(host.Q("node-library"), coordinator?.Current?.NodeCatalog, coordinator);
        }

        public static void ApplyReadModel(VisualElement host, PresentationReadModel model, PresentationCoordinator coordinator = null)
        {
            if (host == null || model == null) return;
            RebuildLibrary(host.Q("node-library"), model.NodeCatalog, coordinator);
            host.Q<GraphCanvasElement>("node-graph-canvas")?.SetGraph(model.Graph);
            var layoutSelector = host.Q<PopupField<string>>("layout-preset-selector");
            if (layoutSelector != null && model.Workspace != null)
            {
                var ids = model.Workspace.Presets.Select(x => x.Id).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
                if (ids.Count > 0)
                {
                    layoutSelector.choices.Clear();
                    layoutSelector.choices.AddRange(ids);
                    if (ids.Contains(model.Workspace.CurrentLayoutId)) layoutSelector.SetValueWithoutNotify(model.Workspace.CurrentLayoutId);
                }
            }
            if (model.Workspace?.CurrentTree != null) ApplyDockTreeVisual(host, model.Workspace.CurrentTree);
            var dockSession = host.userData as DockLayoutSession;
            dockSession?.BindReadModel(model.Workspace?.CurrentLayoutId, model.Workspace?.CurrentTree, model.Workspace?.LayoutDirty ?? false);
            var layoutState = host.Q<Label>("layout-dirty-state");
            if (layoutState != null) layoutState.text = model.Workspace?.LayoutDirty == true ? "Layout Dirty" : "Layout Clean";
            var mediaView = host.Q<PopupField<string>>("media-view-mode");
            if (mediaView != null && model.Workspace != null)
            {
                var view = string.Equals(model.Workspace.MediaLibraryView, "List", StringComparison.OrdinalIgnoreCase) ? "List" : "Grid";
                mediaView.SetValueWithoutNotify(view);
                host.Q("media-panel")?.EnableInClassList("media-list-view", string.Equals(view, "List", StringComparison.Ordinal));
            }
            var diagnostics = host.Q("diagnostics-list");
            if (diagnostics != null)
            {
                diagnostics.Clear();
                if (model.Diagnostics.Count == 0) diagnostics.Add(new Label("No diagnostics") { name = "diagnostics-empty" });
                foreach (var diagnostic in model.Diagnostics)
                {
                    Button detail = null;
                    detail = new Button(() =>
                    {
                        var current = detail.userData as DiagnosticReadModel;
                        if (!string.IsNullOrEmpty(current?.NodeId)) coordinator?.Submit("graph.focus_selection", current.NodeId);
                        ShowDiagnosticDetail(diagnostics.parent, current);
                    }) { text = diagnostic.Severity + " · " + (string.IsNullOrEmpty(diagnostic.NodeId) ? "-" : diagnostic.NodeId) + " · " + diagnostic.Code + " · " + diagnostic.Message + " · x" + diagnostic.Count, name = "diagnostic-" + diagnostic.EntryId };
                    detail.userData = diagnostic;
                    detail.AddToClassList("sd-diagnostic-row");
                    detail.AddToClassList("sd-mono");
                    detail.EnableInClassList("is-history", !diagnostic.IsCurrent);
                    detail.EnableInClassList("is-current", diagnostic.IsCurrent);
                    detail.style.display = diagnostic.IsCurrent ? DisplayStyle.Flex : DisplayStyle.None;
                    detail.tooltip = (string.IsNullOrEmpty(diagnostic.NodeId) ? diagnostic.Message : "Focus node " + diagnostic.NodeId) + " · " + diagnostic.Code;
                    diagnostics.Add(detail);
                }
                ApplyDiagnosticFilter(host.Q("diagnostics-panel"));
                ResetDiagnosticBindings(diagnostics, model.Diagnostics);
            }
            var previewTitle = host.Q<Label>("preview-host-title");
            if (previewTitle != null) previewTitle.text = "Preview Host (" + Math.Min(8, model.Output.Previews.Count(x => x.IsVisible)) + "/8)";
                var programTitle = host.Q<Label>("program-monitor-title");
            if (programTitle != null) programTitle.text = "Program · Display " + model.Output.ProgramDisplay + (string.IsNullOrEmpty(model.Output.ProgramState) ? string.Empty : " · " + model.Output.ProgramState);
            var programFooter = host.Q<Label>("program-monitor-footer");
            if (programFooter != null)
            {
                var surface = model.Output.Program;
                var dimensions = surface == null ? "No valid frame" : surface.Width + "×" + surface.Height + " · frame " + surface.FrameNumber;
                var holding = string.Equals(model.Output.ProgramState, "HoldingLastFrame", StringComparison.OrdinalIgnoreCase) ? " · Holding last frame" : string.Empty;
                programFooter.text = (model.Output.IsPaused ? "Paused" : "Running") + " · " + dimensions + holding;
            }
            var programMetrics = host.Q<Label>("program-monitor-metrics");
            if (programMetrics != null)
            {
                var cpu = FormatMilliseconds(model.Output.CpuFrameTimeMilliseconds);
                var gpu = FormatMilliseconds(model.Output.GpuFrameTimeMilliseconds);
                var fps = FormatFps(model.Output.MeasuredFramesPerSecond);
                programMetrics.text = "CPU Frame Time " + cpu + " · GPU Frame Time " + gpu + " · " + fps;
            }
            var performanceWarning = host.Q<Label>("program-performance-warning");
            if (performanceWarning != null)
            {
                performanceWarning.EnableInClassList("is-visible", model.Output.ProgramPerformanceWarning);
                performanceWarning.text = model.Output.ProgramPerformanceWarning
                    ? "Program performance warning · Preview suppression active · bad frames " + model.Output.ConsecutiveBadProgramFrames
                    : string.Empty;
            }
            var holdingNotice = host.Q<Label>("program-holding-notice");
            if (holdingNotice != null)
            {
                var holding = string.Equals(model.Output.ProgramState, "HoldingLastFrame", StringComparison.OrdinalIgnoreCase);
                holdingNotice.EnableInClassList("is-visible", holding);
                holdingNotice.text = holding
                    ? "HoldingLastFrame · " + FormatSeconds(model.Output.HoldingDurationSeconds) + " · cause " +
                      (string.IsNullOrEmpty(model.Output.HoldingCauseNodeId) ? "-" : model.Output.HoldingCauseNodeId) +
                      " · Diagnostics"
                    : string.Empty;
            }
            var displaySelector = host.Q<PopupField<string>>("program-display-selector");
            if (displaySelector != null)
            {
                // The external display port is the live source of available
                // outputs.  Keep the popup in sync when a monitor is added or
                // removed; the project value remains a one-based number.
                var displayCount = Math.Max(1, coordinator?.DisplayIdentifyPort?.DisplayCount ?? displaySelector.choices.Count);
                var displayChoices = Enumerable.Range(1, displayCount).Select(x => "Display " + x).ToList();
                if (!displayChoices.SequenceEqual(displaySelector.choices))
                {
                    displaySelector.choices.Clear();
                    displaySelector.choices.AddRange(displayChoices);
                }
                var displayNumber = Math.Max(1, model.Output.ProgramDisplay);
                if (displayNumber > displayCount) displayNumber = 1;
                displaySelector.SetValueWithoutNotify("Display " + displayNumber);
            }
                var programImage = host.Q("program-image");
                if (programImage != null)
                {
                    programImage.userData = model.Output.Program;
                    BindSurfaceTexture(programImage, model.Output.Program, "Program surface unavailable");
                }
            var previewTabs = host.Q<TabView>("preview-tabs");
            if (previewTabs != null)
            {
                previewTabs.Clear();
                var visiblePreviews = model.Output.Previews.Where(x => x.IsVisible).ToList();
                var previewHost = host.Q("preview-viewer-host");
                if (previewHost != null && visiblePreviews.Count > 0 && !visiblePreviews.Any(item => string.Equals(item.TabId, previewHost.userData as string, StringComparison.Ordinal))) previewHost.userData = visiblePreviews[0].TabId;
                foreach (var preview in visiblePreviews.Take(8))
                {
                    var tab = new Label(preview.NodeId + " · " + preview.StateText) { name = "preview-tab-" + preview.TabId };
                    tab.userData = preview;
                    tab.AddToClassList("sd-preview-tab");
                    tab.style.flexGrow = 1;
                    tab.style.minWidth = 120;
                    tab.style.minHeight = 80;
                    tab.EnableInClassList("is-fit", preview.Fit == PresentationOutputFit.Fit);
                    tab.EnableInClassList("is-fill", preview.Fit == PresentationOutputFit.Fill);
                    tab.EnableInClassList("is-stretch", preview.Fit == PresentationOutputFit.Stretch);
                    tab.EnableInClassList("is-checker", preview.Background == PresentationOutputBackground.Checker);
                    tab.EnableInClassList("is-black", preview.Background == PresentationOutputBackground.Black);
                    tab.RegisterCallback<PointerDownEvent>(_ =>
                    {
                        var current = tab.userData as PreviewReadModel;
                        if (current == null) return;
                        if (previewHost != null) previewHost.userData = current.TabId;
                        coordinator?.Submit("preview.open", current.TabId,
                            new KeyValuePairValue("previewId", current.TabId), new KeyValuePairValue("nodeId", current.NodeId));
                    });
                    tab.Add(new Label(QualityLabel(preview.Quality) + " · " + preview.StateText) { name = "preview-quality-" + preview.TabId });
                    var image = new VisualElement { name = "preview-image-" + preview.TabId, pickingMode = PickingMode.Ignore };
                    image.style.flexGrow = 1;
                    image.style.minWidth = 64;
                    image.style.minHeight = 36;
                    BindSurfaceTexture(image, preview.Surface, "Preview surface unavailable");
                    if (preview.Background == PresentationOutputBackground.Checker && !image.ClassListContains("is-bound"))
                    {
                        image.style.backgroundImage = Background.FromTexture2D(CheckerTexture());
                        image.style.backgroundRepeat = new BackgroundRepeat(Repeat.Repeat, Repeat.Repeat);
                    }
                    var stateOverlay = new Label(PreviewStateLabel(preview)) { name = "preview-state-overlay-" + preview.TabId };
                    stateOverlay.AddToClassList("sd-preview-state-overlay");
                    stateOverlay.EnableInClassList("is-visible", !string.IsNullOrEmpty(preview.StateText) && !string.Equals(preview.StateText, "Ready", StringComparison.OrdinalIgnoreCase));
                    tab.Add(stateOverlay);
                    tab.Add(image);
                    var tabToolbar = new VisualElement { name = "preview-toolbar-" + preview.TabId };
                    tabToolbar.AddToClassList("sd-preview-tab-toolbar");
                    tabToolbar.Add(PreviewSettingsButton(tab, coordinator, "Fit", null, "preview-fit-" + preview.TabId));
                    tabToolbar.Add(PreviewSettingsButton(tab, coordinator, "Fill", null, "preview-fill-" + preview.TabId));
                    tabToolbar.Add(PreviewSettingsButton(tab, coordinator, "Stretch", null, "preview-stretch-" + preview.TabId));
                    tabToolbar.Add(PreviewSettingsButton(tab, coordinator, null, "Black", "preview-black-" + preview.TabId));
                    tabToolbar.Add(PreviewSettingsButton(tab, coordinator, null, "Checker", "preview-checker-" + preview.TabId));
                    tab.Add(tabToolbar);
                    previewTabs.Add(tab);
                }
                if (visiblePreviews.Count > 8)
                {
                    previewTabs.Add(new Label("Preview rejected: Viewer Host is limited to eight visible previews.") { name = "preview-rejection", tooltip = "Open request was rejected; existing tabs remain unchanged." });
                }
            }
            var inspector = host.Q("inspector-panel");
            var parameter = model.Parameters.FirstOrDefault();
            if (inspector != null && parameter != null)
            {
                inspector.Q<Label>("inspector-empty")?.EnableInClassList("is-hidden", true);
                inspector.userData = parameter.NodeId;
                var nodeType = inspector.Q<Label>("inspector-node-type");
                if (nodeType != null) nodeType.text = "NodeTypeId: " + (string.IsNullOrEmpty(parameter.NodeTypeId) ? "-" : parameter.NodeTypeId);
                var nodeInstance = inspector.Q<Label>("inspector-node-instance");
                if (nodeInstance != null) nodeInstance.text = "NodeInstanceId: " + parameter.NodeId;
                var nodeStatus = inspector.Q<Label>("inspector-node-status");
                if (nodeStatus != null) nodeStatus.text = parameter.IsBroken ? "Status: Faulted · " + parameter.Error : "Status: Ready";
                var parameterLabel = inspector.Q<Label>("inspector-parameter-id");
                if (parameterLabel != null) { parameterLabel.text = parameter.DisplayName; parameterLabel.userData = parameter.ParameterId; }
                var baseValue = inspector.Q<TextField>("inspector-base-value");
                var effective = inspector.Q<TextField>("inspector-effective-value");
                baseValue?.SetValueWithoutNotify(parameter.BaseValue);
                if (baseValue != null) { baseValue.isReadOnly = parameter.IsReadOnly || parameter.IsBroken; baseValue.SetEnabled(!parameter.IsReadOnly && !parameter.IsBroken); }
                effective?.SetValueWithoutNotify(parameter.EffectiveValue);
                var expression = inspector.Q<Label>("inspector-expression");
                if (expression != null) expression.text = string.IsNullOrEmpty(parameter.Expression) ? "Expression: Base Value" : "Expression: " + parameter.Expression;
                var expressionMin = inspector.Q<TextField>("inspector-expression-min");
                var expressionMax = inspector.Q<TextField>("inspector-expression-max");
                if (expressionMin != null || expressionMax != null)
                {
                    var clamp = parameter.OutputClamp ?? string.Empty;
                    var separator = clamp.IndexOf("..", StringComparison.Ordinal);
                    expressionMin?.SetValueWithoutNotify(separator < 0 ? string.Empty : clamp.Substring(0, separator));
                    expressionMax?.SetValueWithoutNotify(separator < 0 ? string.Empty : clamp.Substring(separator + 2));
                }
                var state = inspector.Q<Label>("inspector-state");
                if (state != null) state.text = parameter.IsBroken ? "Error: " + parameter.Error : "Control: " + ControlKindFor(parameter.ValueType) + (parameter.IsClamped ? " · Clamped" : " · Ready");
                var metadata = inspector.Q<Label>("inspector-parameter-metadata");
                if (metadata != null)
                {
                    var range = string.IsNullOrEmpty(parameter.HardRange) ? string.Empty : " · Range " + parameter.HardRange;
                    var step = parameter.Step > 0d ? " · Step " + parameter.Step.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
                    metadata.text = parameter.Description + range + step + (string.IsNullOrEmpty(parameter.Unit) ? string.Empty : " · " + parameter.Unit);
                }
                ApplyParameterControl(inspector, parameter, coordinator);
            }
            RebuildParameterList(host, inspector, model.Parameters, coordinator);
            var dashboard = host.Q("dashboard-panel");
            var dashboardState = dashboard?.Q<Label>("dashboard-state");
            if (dashboardState != null) dashboardState.text = model.DashboardPages.Count + " dashboard page(s)";
            ApplyDashboard(dashboard, model.DashboardPages, coordinator);
            RebuildPresets(host.Q("presets-panel"), model.Presets, coordinator);
            RebuildMedia(host.Q("media-panel"), model.Media, model.Task, coordinator);
            RebuildControls(host.Q("controls-panel"), model.Controls, coordinator);
        }

        /// <summary>Updates frame-varying output and effective-value evidence
        /// without replacing interaction controls, focused text fields or
        /// leased preview elements. Returns false only when Preview topology
        /// changed and the caller must perform its structural projection.</summary>
        public static bool ApplyDynamicReadModel(VisualElement host, PresentationReadModel model, PresentationCoordinator coordinator = null)
        {
            if (host == null || model == null || model.Output == null) return true;
            // GraphCanvas owns keyed node elements and preserves selection.
            // Runtime state changes update those existing elements; a topology
            // difference asks the caller for the structural path instead.
            var canvas = host.Q<GraphCanvasElement>("node-graph-canvas");
            if (canvas != null && !canvas.TryUpdateGraphState(model.Graph)) return false;
            ApplyDynamicDiagnostics(host, model.Diagnostics, coordinator);
            ApplyDynamicMediaTask(host.Q("media-panel"), model.Media, model.Task);
            var programImage = host.Q("program-image");
            BindSurfaceTexture(programImage, model.Output.Program, "Program surface unavailable");
            var programFooter = host.Q<Label>("program-monitor-footer");
            if (programFooter != null)
            {
                var surface = model.Output.Program;
                var dimensions = surface == null ? "No valid frame" : surface.Width + "×" + surface.Height + " · frame " + surface.FrameNumber;
                SetLabelText(programFooter, (model.Output.IsPaused ? "Paused" : "Running") + " · " + dimensions);
            }
            var programMetrics = host.Q<Label>("program-monitor-metrics");
            if (programMetrics != null)
                SetLabelText(programMetrics, "CPU Frame Time " + FormatMilliseconds(model.Output.CpuFrameTimeMilliseconds) + " · GPU Frame Time " + FormatMilliseconds(model.Output.GpuFrameTimeMilliseconds) + " · " + FormatFps(model.Output.MeasuredFramesPerSecond));

            var previewTabs = host.Q<TabView>("preview-tabs");
            var visiblePreviewCount = 0;
            var visiblePreviewTotal = 0;
            foreach (var preview in model.Output.Previews)
                if (preview.IsVisible)
                {
                    visiblePreviewTotal++;
                    if (visiblePreviewCount < 8) visiblePreviewCount++;
                }
            if (previewTabs == null || previewTabs.childCount != visiblePreviewCount + (visiblePreviewTotal > 8 ? 1 : 0)) return false;
            var previewHost = host.Q("preview-viewer-host");
            var selectedPreview = previewHost?.userData as string;
            PreviewReadModel firstVisiblePreview = null;
            var selectedPreviewExists = false;
            foreach (var preview in model.Output.Previews)
            {
                if (!preview.IsVisible) continue;
                if (firstVisiblePreview == null) firstVisiblePreview = preview;
                if (string.Equals(preview.TabId, selectedPreview, StringComparison.Ordinal)) selectedPreviewExists = true;
            }
            if (!selectedPreviewExists) previewHost.userData = firstVisiblePreview?.TabId;
            var previewIndex = 0;
            foreach (var preview in model.Output.Previews)
            {
                if (!preview.IsVisible || previewIndex >= 8) continue;
                var tab = previewTabs.Q<Label>("preview-tab-" + preview.TabId);
                var image = previewTabs.Q("preview-image-" + preview.TabId);
                var quality = previewTabs.Q<Label>("preview-quality-" + preview.TabId);
                var overlay = previewTabs.Q<Label>("preview-state-overlay-" + preview.TabId);
                if (tab == null || image == null || quality == null || overlay == null || previewTabs.ElementAt(previewIndex) != tab) return false;
                tab.userData = preview;
                SetLabelText(tab, preview.NodeId + " · " + preview.StateText);
                tab.EnableInClassList("is-fit", preview.Fit == PresentationOutputFit.Fit);
                tab.EnableInClassList("is-fill", preview.Fit == PresentationOutputFit.Fill);
                tab.EnableInClassList("is-stretch", preview.Fit == PresentationOutputFit.Stretch);
                tab.EnableInClassList("is-checker", preview.Background == PresentationOutputBackground.Checker);
                tab.EnableInClassList("is-black", preview.Background == PresentationOutputBackground.Black);
                SetLabelText(quality, QualityLabel(preview.Quality) + " · " + preview.StateText);
                SetLabelText(overlay, PreviewStateLabel(preview));
                overlay.EnableInClassList("is-visible", !string.IsNullOrEmpty(preview.StateText) && !string.Equals(preview.StateText, "Ready", StringComparison.OrdinalIgnoreCase));
                BindSurfaceTexture(image, preview.Surface, "Preview surface unavailable");
                previewIndex++;
            }

            foreach (var parameter in model.Parameters)
            {
                var row = FindParameterRow(host, parameter);
                var effective = row?.Q<TextField>("parameter-row-effective-" + parameter.ParameterId);
                if (effective != null && !string.Equals(effective.value, parameter.EffectiveValue, StringComparison.Ordinal))
                    effective.SetValueWithoutNotify(parameter.EffectiveValue);
            }
            var active = model.Parameters.FirstOrDefault();
            if (active != null)
            {
                var inspectorEffective = host.Q<TextField>("inspector-effective-value");
                if (inspectorEffective != null && !string.Equals(inspectorEffective.value, active.EffectiveValue, StringComparison.Ordinal))
                    inspectorEffective.SetValueWithoutNotify(active.EffectiveValue);
                var baseEditor = host.Q<TextField>("inspector-base-value");
                if (baseEditor != null && !ContainsFocusedElement(baseEditor))
                    if (!string.Equals(baseEditor.value, active.BaseValue, StringComparison.Ordinal)) baseEditor.SetValueWithoutNotify(active.BaseValue);
            }
            ApplyDynamicCommandNotice(host, model.Commands, model.Task);
            if (!ApplyDynamicControls(host.Q("controls-panel"), model.Controls)) return false;
            return true;
        }

        private static Button PreviewSettingsButton(Label tab, PresentationCoordinator coordinator, string fit, string background, string name)
        {
            return new Button(() =>
            {
                var current = tab.userData as PreviewReadModel;
                if (current == null) return;
                coordinator?.Submit("preview.settings", current.TabId,
                    new KeyValuePairValue("fit", fit ?? current.Fit.ToString()),
                    new KeyValuePairValue("background", background ?? current.Background.ToString()));
            }) { text = fit ?? background, name = name };
        }

        private static VisualElement FindParameterRow(VisualElement host, ParameterReadModel parameter)
        {
            var key = ParameterStableKey(parameter);
            return host != null && ParameterRowBindings.TryGetValue(host, out var rows) && rows.TryGetValue(key, out var row) ? row : null;
        }

        private static string ParameterStableKey(ParameterReadModel parameter)
        {
            return (parameter?.NodeId ?? string.Empty) + ":" + (parameter?.ParameterId ?? string.Empty);
        }

        private static bool ContainsFocusedElement(VisualElement element)
        {
            var focused = element?.focusController?.focusedElement as VisualElement;
            while (focused != null)
            {
                if (ReferenceEquals(focused, element)) return true;
                focused = focused.parent;
            }
            return false;
        }

        private static void SetLabelText(Label label, string value)
        {
            if (label == null || string.Equals(label.text, value, StringComparison.Ordinal)) return;
            label.text = value;
        }

        private static void ApplyDynamicCommandNotice(VisualElement host, IReadOnlyList<CommandReadModel> commands, PresentationTaskReadModel task)
        {
            var notice = host?.Q<Label>("presentation-command-notice");
            if (notice == null) return;
            var command = (commands ?? Array.Empty<CommandReadModel>()).LastOrDefault();
            SetLabelText(notice, command == null
                ? (task == null ? string.Empty : task.Kind + " · " + task.Status)
                : command.Status + (string.IsNullOrEmpty(command.Reason) ? string.Empty : " · " + command.Reason));
            notice.EnableInClassList("is-visible", !string.IsNullOrEmpty(notice.text));
        }

        private static bool ApplyDynamicControls(VisualElement panel, IReadOnlyList<LogicalControlReadModel> controls)
        {
            if (panel == null) return false;
            var found = 0;
            foreach (var control in controls ?? Array.Empty<LogicalControlReadModel>())
            {
                var item = panel.Q<VisualElement>("control-" + control.Id);
                var label = item?.Q<Label>("control-label-" + control.Id);
                if (label == null) return false;
                found++;
                var state = control.CurrentValue.HasValue
                    ? " · value " + control.CurrentValue.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                    : control.IsFiring ? " · Fired" : " · Armed";
                SetLabelText(label, control.Name + " · " + control.Kind + (control.IsBroken ? " · Broken" : string.Empty) + state);
                item.EnableInClassList("is-firing", control.IsFiring);
            }
            var rendered = 0;
            foreach (var child in panel.Children()) if (child.ClassListContains("sd-control-item")) rendered++;
            return rendered == found;
        }

        private static void ApplyDynamicDiagnostics(VisualElement host, IReadOnlyList<DiagnosticReadModel> values, PresentationCoordinator coordinator)
        {
            var list = host?.Q("diagnostics-list");
            if (list == null) return;
            var state = DiagnosticBindings.GetOrCreateValue(list);
            if (ReferenceEquals(state.Source, values)) return;
            state.Source = values;
            state.Seen.Clear();
            foreach (var diagnostic in values ?? Array.Empty<DiagnosticReadModel>())
            {
                var name = "diagnostic-" + diagnostic.EntryId;
                state.Seen.Add(name);
                state.Entries.TryGetValue(name, out var item);
                if (item != null && !ReferenceEquals(item.parent, list))
                {
                    state.Entries.Remove(name);
                    item = null;
                }
                if (item == null) item = list.Q<Button>(name);
                if (item == null)
                {
                    Button button = null;
                    button = new Button(() =>
                    {
                        var current = button.userData as DiagnosticReadModel;
                        if (!string.IsNullOrEmpty(current?.NodeId)) coordinator?.Submit("graph.focus_selection", current.NodeId);
                        ShowDiagnosticDetail(list.parent, current);
                    }) { name = name };
                    item = button;
                    item.AddToClassList("sd-diagnostic-row");
                    item.AddToClassList("sd-mono");
                    list.Add(item);
                }
                state.Entries[name] = item;
                item.userData = diagnostic;
                var text = diagnostic.Severity + " · " + (string.IsNullOrEmpty(diagnostic.NodeId) ? "-" : diagnostic.NodeId) + " · " + diagnostic.Code + " · " + diagnostic.Message + " · x" + diagnostic.Count;
                if (!string.Equals(item.text, text, StringComparison.Ordinal)) item.text = text;
                var tooltip = (string.IsNullOrEmpty(diagnostic.NodeId) ? diagnostic.Message : "Focus node " + diagnostic.NodeId) + " · " + diagnostic.Code;
                if (!string.Equals(item.tooltip, tooltip, StringComparison.Ordinal)) item.tooltip = tooltip;
                item.EnableInClassList("is-history", !diagnostic.IsCurrent);
                item.EnableInClassList("is-current", diagnostic.IsCurrent);
                item.style.display = diagnostic.IsCurrent ? DisplayStyle.Flex : DisplayStyle.None;
            }
            state.Removed.Clear();
            foreach (var pair in state.Entries) if (!state.Seen.Contains(pair.Key)) state.Removed.Add(pair.Key);
            foreach (var name in state.Removed) { state.Entries[name].RemoveFromHierarchy(); state.Entries.Remove(name); }
            var hasDiagnostics = (values?.Count ?? 0) > 0;
            var empty = list.Q("diagnostics-empty");
            if (!hasDiagnostics && empty == null) list.Add(new Label("No diagnostics") { name = "diagnostics-empty" });
            else if (hasDiagnostics) empty?.RemoveFromHierarchy();
            ApplyDiagnosticFilter(host.Q("diagnostics-panel"));
        }

        private static void ResetDiagnosticBindings(VisualElement list, IReadOnlyList<DiagnosticReadModel> values)
        {
            if (list == null) return;
            var state = DiagnosticBindings.GetOrCreateValue(list);
            state.Source = values;
            state.Entries.Clear();
            state.Seen.Clear();
            state.Removed.Clear();
            foreach (var child in list.Children())
                if (child is Button button && button.name != null && button.name.StartsWith("diagnostic-", StringComparison.Ordinal))
                    state.Entries[button.name] = button;
        }

        private static void ShowDiagnosticDetail(VisualElement panel, DiagnosticReadModel item)
        {
            var pane = panel?.Q("diagnostics-detail-pane");
            if (pane == null || item == null) return;
            pane.Clear();
            pane.AddToClassList("sd-mono");
            pane.Add(new Label("ID: " + item.EntryId));
            pane.Add(new Label("Severity: " + item.Severity + " · Code: " + item.Code));
            pane.Add(new Label("Node: " + item.NodeId + " · Count: " + item.Count));
            pane.Add(new Label("Port / Parameter: " + (string.IsNullOrEmpty(item.PortOrParameter) ? "-" : item.PortOrParameter)));
            pane.Add(new Label("Frame: " + item.FirstFrame + " → " + item.LastFrame));
            pane.Add(new Label("GraphClock: " + (item.LastFrame == 0 ? "-" : item.LastFrame.ToString())));
            pane.Add(new Label("Details: " + (string.IsNullOrEmpty(item.Details) ? item.Message : item.Details)));
            pane.Add(new Label("Exception: " + (string.IsNullOrEmpty(item.ExceptionType) ? "-" : item.ExceptionType)));
            pane.Add(new Label("Stack: " + (string.IsNullOrEmpty(item.StackTrace) ? "-" : item.StackTrace)));
        }

        private static void ApplyDynamicMediaTask(VisualElement panel, IReadOnlyList<MediaListItemReadModel> media, PresentationTaskReadModel task)
        {
            if (panel == null) return;
            var progress = panel.Q<Label>("media-import-progress");
            if (progress != null)
                SetLabelText(progress, task != null && string.Equals(task.Kind, "ImportBatch", StringComparison.OrdinalIgnoreCase)
                    ? task.Stage + " · " + task.Status + " (" + task.CompletedItems + "/" + task.TotalItems + ")" : (media?.Count ?? 0) == 0 ? "No media" : media.Count + " media item(s)");
            var confirm = panel.Q<Button>("media-confirm-import");
            if (confirm != null) confirm.SetEnabled(task != null && (task.Stage.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0 || task.Status.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0));
        }

        private static ParameterControlKind ControlKindFor(string valueType)
        {
            switch ((valueType ?? string.Empty).ToLowerInvariant())
            {
                case "bool": return ParameterControlKind.Toggle;
                case "color": return ParameterControlKind.Color;
                case "enum": return ParameterControlKind.Enum;
                case "string": return ParameterControlKind.Text;
                case "vector2":
                case "vector3":
                case "vector4": return ParameterControlKind.Vector;
                case "mediaassetreference":
                case "media": return ParameterControlKind.Media;
                case "float":
                case "int": return ParameterControlKind.Numeric;
                default: return ParameterControlKind.ReadOnly;
            }
        }

        private static void BindSurfaceTexture(VisualElement image, OutputSurfaceReadModel surface, string unavailableText)
        {
            if (image == null) return;
            var texture2D = surface?.Texture as Texture2D;
            var renderTexture = surface?.Texture as RenderTexture;
            var supported = texture2D != null || renderTexture != null;
            var bound = surface != null && surface.IsBound && supported;
            var tooltip = bound ? string.Empty : (unavailableText ?? "Surface unavailable");
            var state = SurfaceBindings.GetOrCreateValue(image);
            if (ReferenceEquals(state.Texture, surface?.Texture) && state.Generation == (surface?.Generation ?? 0UL) && state.IsBound == bound && string.Equals(state.Tooltip, tooltip, StringComparison.Ordinal)) return;
            image.style.backgroundImage = new StyleBackground(StyleKeyword.None);
            if (texture2D != null) image.style.backgroundImage = Background.FromTexture2D(texture2D);
            else if (renderTexture != null) image.style.backgroundImage = Background.FromRenderTexture(renderTexture);
            image.EnableInClassList("is-bound", bound);
            image.EnableInClassList("is-unavailable", !bound);
            image.tooltip = tooltip;
            state.Texture = surface?.Texture;
            state.Generation = surface?.Generation ?? 0UL;
            state.IsBound = bound;
            state.Tooltip = tooltip;
        }

        private static void RebuildParameterList(VisualElement host, VisualElement inspector, IEnumerable<ParameterReadModel> values, PresentationCoordinator coordinator)
        {
            var list = inspector?.Q("inspector-parameter-list");
            if (list == null) return;
            var previous = list.Children().OfType<Foldout>().ToDictionary(x => x.name, StringComparer.Ordinal);
            list.Clear();
            var grouped = (values ?? Enumerable.Empty<ParameterReadModel>()).Where(x => x != null && x.IsVisible)
                .OrderBy(x => x.Order).ThenBy(x => x.ParameterId, StringComparer.Ordinal)
                .GroupBy(x => string.IsNullOrEmpty(x.Group) ? "Parameters" : x.Group, StringComparer.Ordinal)
                .OrderBy(x => x.Min(p => p.Order)).ThenBy(x => x.Key, StringComparer.Ordinal);
            foreach (var group in grouped)
            {
                var foldoutName = "parameter-group-" + group.Key.ToLowerInvariant();
                Foldout foldout;
                if (!previous.TryGetValue(foldoutName, out foldout))
                {
                    foldout = new Foldout { name = foldoutName, text = group.Key, value = true };
                    var captured = foldout;
                    captured.RegisterValueChangedCallback(evt =>
                    {
                        if (evt.newValue && captured.userData is ParameterGroupBinding binding) binding.Rebuild();
                    });
                }
                var binding = new ParameterGroupBinding(foldout, group.ToList(), coordinator);
                foldout.userData = binding;
                if (foldout.value) binding.Rebuild();
                list.Add(foldout);
            }
            var bindings = ParameterRowBindings.GetOrCreateValue(host);
            bindings.Clear();
            foreach (var row in list.Query<VisualElement>(className: "sd-parameter-row").ToList())
                if (row.userData is string key) bindings[key] = row;
        }

        private sealed class ParameterGroupBinding
        {
            private readonly Foldout _foldout;
            private readonly IReadOnlyList<ParameterReadModel> _parameters;
            private readonly PresentationCoordinator _coordinator;
            public ParameterGroupBinding(Foldout foldout, IEnumerable<ParameterReadModel> parameters, PresentationCoordinator coordinator)
            { _foldout = foldout; _parameters = (parameters ?? Enumerable.Empty<ParameterReadModel>()).ToList(); _coordinator = coordinator; }
            public void Rebuild()
            {
                if (_foldout == null || !_foldout.value) return;
                var content = _foldout.contentContainer;
                foreach (var child in content.Children().ToList()) content.Remove(child);
                foreach (var parameter in _parameters) content.Add(CreateParameterRow(parameter, _coordinator));
            }
        }

        private static VisualElement CreateParameterRow(ParameterReadModel parameter, PresentationCoordinator coordinator)
        {
            var row = new VisualElement { name = "parameter-row-" + parameter.ParameterId };
            row.userData = ParameterStableKey(parameter);
            row.AddToClassList("sd-parameter-row");
            row.tooltip = string.IsNullOrEmpty(parameter.Description) ? parameter.ParameterId : parameter.Description;
            var header = new VisualElement { name = "parameter-row-header-" + parameter.ParameterId };
            var displayLabel = new Label(parameter.DisplayName) { name = "parameter-row-label-" + parameter.ParameterId };
            displayLabel.AddToClassList("sd-parameter-label");
            header.Add(displayLabel);
            header.Add(new Label(parameter.ValueType + (string.IsNullOrEmpty(parameter.Unit) ? string.Empty : " · " + parameter.Unit)) { name = "parameter-row-type-" + parameter.ParameterId });
            row.Add(header);
            var valuesRow = new VisualElement { name = "parameter-row-values-" + parameter.ParameterId };
            valuesRow.AddToClassList("sd-parameter-values");
            var baseEditor = CreateInlineParameterEditor(parameter, coordinator);
            if (baseEditor != null) valuesRow.Add(baseEditor);
            var effective = new TextField("Effective") { name = "parameter-row-effective-" + parameter.ParameterId, value = parameter.EffectiveValue, isReadOnly = true };
            effective.AddToClassList("sd-effective-value");
            valuesRow.Add(effective);
            row.Add(valuesRow);
            var state = parameter.IsBroken ? "Error: " + parameter.Error : parameter.IsClamped ? "Clamped" : parameter.IsReadOnly ? "Read-only" : "Ready";
            row.Add(new Label(state) { name = "parameter-row-state-" + parameter.ParameterId });
            return row;
        }

        private static VisualElement CreateInlineParameterEditor(ParameterReadModel parameter, PresentationCoordinator coordinator)
        {
            if (parameter == null) return null;
            var readOnly = parameter.IsReadOnly || parameter.IsBroken;
            switch ((parameter.ValueType ?? string.Empty).ToLowerInvariant())
            {
                case "bool":
                    var toggle = new Toggle("Base") { name = "parameter-row-base-" + parameter.ParameterId, value = string.Equals(parameter.BaseValue, "true", StringComparison.OrdinalIgnoreCase) };
                    toggle.SetEnabled(!readOnly);
                    toggle.RegisterValueChangedCallback(evt => SubmitInline(coordinator, parameter, evt.newValue ? "true" : "false", "Bool"));
                    return toggle;
                case "int":
                    var integer = new IntegerField("Base") { name = "parameter-row-base-" + parameter.ParameterId, value = ParseInt(parameter.BaseValue) };
                    integer.SetEnabled(!readOnly);
                    integer.RegisterValueChangedCallback(evt => SubmitInline(coordinator, parameter, evt.newValue.ToString(System.Globalization.CultureInfo.InvariantCulture), "Int"));
                    return integer;
                case "float":
                    var single = new FloatField("Base") { name = "parameter-row-base-" + parameter.ParameterId, value = ParseFloat(parameter.BaseValue) };
                    single.SetEnabled(!readOnly);
                    single.RegisterValueChangedCallback(evt => SubmitInline(coordinator, parameter, evt.newValue.ToString(System.Globalization.CultureInfo.InvariantCulture), "Float"));
                    return single;
                case "enum":
                    var choices = (parameter.EnumOptions ?? Array.Empty<ParameterOptionReadModel>()).Select(x => x.Id).ToList();
                    if (choices.Count == 0) choices.Add(parameter.BaseValue ?? string.Empty);
                    if (!choices.Contains(parameter.BaseValue ?? string.Empty)) choices.Insert(0, parameter.BaseValue ?? string.Empty);
                    var popup = new PopupField<string>("Base", choices, Math.Max(0, choices.IndexOf(parameter.BaseValue ?? string.Empty))) { name = "parameter-row-base-" + parameter.ParameterId };
                    popup.SetEnabled(!readOnly);
                    popup.RegisterValueChangedCallback(evt => SubmitInline(coordinator, parameter, evt.newValue, "Enum"));
                    return popup;
                case "mediaassetreference":
                case "media":
                    var media = (parameter.MediaOptions ?? Array.Empty<string>()).ToList();
                    if (media.Count == 0) media.Add(parameter.BaseValue ?? string.Empty);
                    if (!media.Contains(parameter.BaseValue ?? string.Empty)) media.Insert(0, parameter.BaseValue ?? string.Empty);
                    var mediaPopup = new PopupField<string>("Base", media, Math.Max(0, media.IndexOf(parameter.BaseValue ?? string.Empty))) { name = "parameter-row-base-" + parameter.ParameterId };
                    mediaPopup.SetEnabled(!readOnly);
                    mediaPopup.RegisterValueChangedCallback(evt => SubmitInline(coordinator, parameter, NormalizeMediaSelection(evt.newValue), "MediaAssetReference"));
                    return mediaPopup;
                case "vector2": return CreateInlineComponents(parameter, coordinator, 2, "Vector2", new[] { "X", "Y" }, readOnly);
                case "vector3": return CreateInlineComponents(parameter, coordinator, 3, "Vector3", new[] { "X", "Y", "Z" }, readOnly);
                case "vector4": return CreateInlineComponents(parameter, coordinator, 4, "Vector4", new[] { "X", "Y", "Z", "W" }, readOnly);
                case "color": return CreateInlineComponents(parameter, coordinator, 4, "Color", new[] { "R", "G", "B", "A" }, readOnly);
                default:
                    var text = new TextField("Base") { name = "parameter-row-base-" + parameter.ParameterId, value = parameter.BaseValue ?? string.Empty, isReadOnly = readOnly };
                    text.RegisterValueChangedCallback(evt => SubmitInline(coordinator, parameter, evt.newValue ?? string.Empty, "String"));
                    return text;
            }
        }

        private static VisualElement CreateInlineComponents(ParameterReadModel parameter, PresentationCoordinator coordinator, int count, string valueType, string[] labels, bool readOnly)
        {
            var row = new VisualElement { name = "parameter-row-base-" + parameter.ParameterId };
            var values = ParseInlineComponents(parameter.BaseValue, count);
            for (var i = 0; i < count; i++)
            {
                row.Add(new Label(labels[i]));
                var field = new FloatField { name = "parameter-row-base-" + parameter.ParameterId + "-" + labels[i].ToLowerInvariant(), value = values[i] };
                var range = parameter.ComponentRanges?.FirstOrDefault(x => string.Equals(x.Name, labels[i], StringComparison.OrdinalIgnoreCase));
                field.tooltip = range == null ? string.Empty : "Range " + range.Minimum + ".." + range.Maximum;
                field.SetEnabled(!readOnly);
                field.RegisterValueChangedCallback(_ => SubmitInline(coordinator, parameter, string.Join(",", row.Query<FloatField>().ToList().Select(x => x.value.ToString(System.Globalization.CultureInfo.InvariantCulture))), valueType));
                row.Add(field);
            }
            return row;
        }

        private static void SubmitInline(PresentationCoordinator coordinator, ParameterReadModel parameter, string value, string valueType)
        {
            if (coordinator == null || parameter == null || parameter.IsReadOnly || parameter.IsBroken) return;
            coordinator.Submit("parameter.set_base", parameter.ParameterId,
                new KeyValuePairValue("nodeId", parameter.NodeId), new KeyValuePairValue("parameterId", parameter.ParameterId),
                new KeyValuePairValue("value", value ?? string.Empty), new KeyValuePairValue("valueType", valueType ?? parameter.ValueType));
        }

        private static string NormalizeMediaSelection(string value)
        {
            var separator = (value ?? string.Empty).IndexOf('|');
            return separator < 0 ? (value ?? string.Empty) : value.Substring(0, separator);
        }

        private static float[] ParseInlineComponents(string text, int count)
        {
            var result = new float[count];
            var parts = (text ?? string.Empty).Trim().Trim('(', ')', '[', ']').Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < count && i < parts.Length; i++) float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result[i]);
            return result;
        }

        private static float ParseFloat(string text) => float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0f;
        private static int ParseInt(string text) => int.TryParse(text, out var value) ? value : 0;

        private static string FormatMilliseconds(double value) => double.IsNaN(value) || double.IsInfinity(value) || value <= 0d
            ? "Unavailable" : value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + " ms";
        private static string FormatFps(double value) => double.IsNaN(value) || double.IsInfinity(value) || value <= 0d
            ? "fps Unavailable" : "fps " + value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
        private static string FormatSeconds(double value) => double.IsNaN(value) || double.IsInfinity(value) || value < 0d
            ? "duration unavailable" : value.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s";

        private static void AddDockControls(VisualElement host, PresentationCoordinator coordinator)
        {
            var controls = new VisualElement { name = "dock-layout-controls" };
            controls.AddToClassList("sd-dock-controls");
            var status = new Label("Layout Clean") { name = "layout-dirty-state" };
            controls.Add(status);
            var knownPanels = new HashSet<string>(new[] { "node-library", "node-graph-panel", "inspector-panel", "dashboard-panel", "outputs-row", "presets-panel", "media-panel", "diagnostics-panel" }, StringComparer.Ordinal);
            var session = new DockLayoutSession(new DockTree(new DockTabGroup(knownPanels, "node-graph-panel")));
            host.userData = session;
            var presetIds = (coordinator?.Current?.Workspace?.Presets ?? Array.Empty<LayoutPreset>()).Select(x => x.Id).ToList();
            if (presetIds.Count == 0) presetIds.Add("Edit");
            var presetSelector = new PopupField<string>("Layout", presetIds, 0) { name = "layout-preset-selector" };
            presetSelector.RegisterValueChangedCallback(evt =>
            {
                var selected = coordinator?.Current?.Workspace?.Presets?.FirstOrDefault(x => string.Equals(x.Id, evt.newValue, StringComparison.Ordinal));
                if (selected != null) session.SelectPreset(selected.Id, selected.Tree);
                SubmitLayout(coordinator, evt.newValue, false, selected?.Tree);
                status.text = "Layout Clean · " + evt.newValue;
            });
            controls.Add(presetSelector);
            Func<DockAxis, float, string> split = (axis, ratio) =>
            {
                session.BeginDrag();
                session.SetCandidate(new DockTree(new DockSplit(axis, ratio, session.Current.Root, new DockEmpty())));
                DockLayoutValidation validation;
                var committed = session.TryCommitCandidate(knownPanels, out validation);
                if (committed) { ApplySplitVisual(host, axis, ratio); SubmitLayout(coordinator, session.CurrentPresetId, true, session.Current); }
                return committed ? "Layout Dirty · " + axis + " Split" : "Layout Rejected · " + string.Join(", ", validation.Errors);
            };
            var splitHorizontal = new Button(() => { status.text = split(DockAxis.Horizontal, .5f); }) { text = "Split H", name = "dock-split-horizontal" };
            var splitVertical = new Button(() => { status.text = split(DockAxis.Vertical, .5f); }) { text = "Split V", name = "dock-split-vertical" };
            var resize = new Button(() =>
            {
                session.BeginDrag();
                session.SetCandidate(new DockTree(new DockSplit(DockAxis.Horizontal, .7f, session.Current.Root, new DockEmpty())));
                if (!session.TryCommitCandidate(knownPanels, out var validation))
                {
                    status.text = "Layout Rejected · " + string.Join(", ", validation.Errors);
                    return;
                }
                ApplySplitVisual(host, DockAxis.Horizontal, .7f);
                SubmitLayout(coordinator, session.CurrentPresetId, true, session.Current);
                status.text = "Layout Dirty · Horizontal Resize";
            }) { text = "Resize", name = "dock-resize" };
            var tab = new Button(() =>
            {
                session.BeginDrag();
                session.SetCandidate(new DockTree(new DockTabGroup(knownPanels, "node-graph-panel")));
                DockLayoutValidation validation;
                if (session.TryCommitCandidate(knownPanels, out validation)) { ApplyTabVisual(host); SubmitLayout(coordinator, session.CurrentPresetId, true, session.Current); status.text = "Layout Dirty · Tab Group"; }
                else status.text = "Layout Rejected";
            }) { text = "Tab", name = "dock-tabify" };
            var close = new Button(() =>
            {
                session.BeginDrag();
                session.SetCandidate(new DockTree(new DockTabGroup(knownPanels.Where(x => x != "inspector-panel"), "node-graph-panel")));
                DockLayoutValidation validation;
                if (session.TryCommitCandidate(knownPanels, out validation)) { ApplyCloseVisual(host, "inspector-panel"); SubmitLayout(coordinator, session.CurrentPresetId, true, session.Current); status.text = "Layout Dirty · Panel Closed"; }
                else status.text = "Layout Rejected";
            }) { text = "Close", name = "dock-close-panel" };
            var reopen = new Button(() =>
            {
                session.SelectPreset("Edit", new DockTree(new DockTabGroup(knownPanels, "node-graph-panel")));
                ApplyReopenVisual(host);
                SubmitLayout(coordinator, session.CurrentPresetId, false, session.Current);
                status.text = "Layout Clean · Panel Reopened";
            }) { text = "Reopen", name = "dock-reopen-panel" };
            Button deleteConfirm = null;
            deleteConfirm = new Button(() =>
            {
                status.text = "Preset Delete Requested";
                SubmitLayoutOperation(coordinator, "delete", session.CurrentPresetId);
                deleteConfirm.style.display = DisplayStyle.None;
            }) { text = "Confirm Delete", name = "layout-preset-delete-confirm" };
            deleteConfirm.style.display = DisplayStyle.None;
            var createPreset = new Button(() =>
            {
                status.text = "Preset Create Requested";
                var id = "Layout-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                SubmitLayoutOperation(coordinator, "create", id, id, id);
            }) { text = "Create Preset", name = "layout-preset-create" };
            var overwritePreset = new Button(() =>
            {
                status.text = "Preset Overwrite Requested";
                SubmitLayoutOperation(coordinator, "overwrite", session.CurrentPresetId);
            }) { text = "Overwrite", name = "layout-preset-overwrite" };
            var renamePreset = new Button(() =>
            {
                status.text = "Preset Rename Requested";
                SubmitLayoutOperation(coordinator, "rename", session.CurrentPresetId, session.CurrentPresetId + " (Renamed)");
            }) { text = "Rename", name = "layout-preset-rename" };
            var duplicatePreset = new Button(() =>
            {
                status.text = "Preset Duplicate Requested";
                var id = session.CurrentPresetId + "-Copy-" + Guid.NewGuid().ToString("N").Substring(0, 6);
                SubmitLayoutOperation(coordinator, "duplicate", session.CurrentPresetId, id, id);
            }) { text = "Duplicate", name = "layout-preset-duplicate" };
            var deletePreset = new Button(() =>
            {
                status.text = "Confirm preset deletion";
                deleteConfirm.style.display = DisplayStyle.Flex;
            }) { text = "Delete Preset", name = "layout-preset-delete" };
            controls.Add(splitHorizontal);
            controls.Add(splitVertical);
            controls.Add(resize);
            controls.Add(tab);
            controls.Add(close);
            controls.Add(reopen);
            controls.Add(createPreset);
            controls.Add(overwritePreset);
            controls.Add(renamePreset);
            controls.Add(duplicatePreset);
            controls.Add(deletePreset);
            controls.Add(deleteConfirm);
            host.Add(controls);
        }

        private static void SubmitLayout(PresentationCoordinator coordinator, string layoutId, bool dirty, DockTree tree = null)
        {
            coordinator?.Submit("workspace.layout", layoutId,
                new KeyValuePairValue("layoutId", layoutId),
                new KeyValuePairValue("operation", dirty ? "edit" : "select"),
                new KeyValuePairValue("dirty", dirty ? "true" : "false"),
                new KeyValuePairValue("tree", DockTreeCodec.Encode(tree)));
        }

        private static void SubmitLayoutOperation(PresentationCoordinator coordinator, string operation, string layoutId, string name = null, string newLayoutId = null)
        {
            coordinator?.Submit("workspace.layout", layoutId ?? string.Empty,
                new KeyValuePairValue("layoutId", layoutId ?? string.Empty),
                new KeyValuePairValue("operation", operation ?? string.Empty),
                new KeyValuePairValue("name", name ?? string.Empty),
                new KeyValuePairValue("newLayoutId", newLayoutId ?? string.Empty));
        }

        private static void ApplySplitVisual(VisualElement host, DockAxis axis, float ratio)
        {
            var row = host.Q("dock-row");
            if (row == null) return;
            row.style.flexDirection = axis == DockAxis.Horizontal ? FlexDirection.Row : FlexDirection.Column;
            var graph = host.Q("node-graph-panel");
            if (graph == null) return;
            if (axis == DockAxis.Horizontal) graph.style.width = Length.Percent(ratio * 100f);
            else graph.style.height = Length.Percent(ratio * 100f);
            graph.style.flexGrow = 0;
        }

        private static void ApplyDockTreeVisual(VisualElement host, DockTree tree)
        {
            if (host == null || tree == null) return;
            var panels = new HashSet<string>(tree.Validate().PanelInstanceIds, StringComparer.Ordinal);
            foreach (var panelId in new[] { "node-library", "node-graph-panel", "inspector-panel", "dashboard-panel", "outputs-row", "presets-panel", "media-panel", "diagnostics-panel" })
            {
                var panel = host.Q(panelId);
                if (panel != null) panel.style.display = panels.Contains(panelId) ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (tree.Root is DockSplit split) ApplySplitVisual(host, split.Axis, split.Ratio);
        }

        private static void ApplyTabVisual(VisualElement host)
        {
            var graph = host.Q("node-graph-panel");
            var inspector = host.Q("inspector-panel");
            if (graph != null) graph.EnableInClassList("is-active-tab", true);
            if (inspector != null) inspector.EnableInClassList("is-tabbed", true);
        }

        private static void ApplyCloseVisual(VisualElement host, string panelId)
        {
            var panel = host.Q(panelId);
            if (panel == null) return;
            panel.style.display = DisplayStyle.None;
            panel.EnableInClassList("is-closed", true);
        }

        private static void ApplyReopenVisual(VisualElement host)
        {
            var panel = host.Q("inspector-panel");
            if (panel == null) return;
            panel.style.display = DisplayStyle.Flex;
            panel.EnableInClassList("is-closed", false);
        }

        private static VisualElement CreatePanel(string name, string title)
        {
            var panel = new VisualElement { name = name };
            panel.AddToClassList("sd-panel");
            var header = new Label(title) { name = name + "-header" };
            header.AddToClassList("sd-panel-header");
            panel.Add(header);
            return panel;
        }

        private static void AddLibraryItems(VisualElement panel, PresentationCoordinator coordinator)
        {
            var search = new TextField("Search") { name = "node-search" };
            search.tabIndex = 0;
            TrackFocus(search);
            panel.Add(search);
            var category = new PopupField<string>("Category", new List<string> { "All" }, 0) { name = "node-library-category" };
            var favorites = new Toggle("Favorites") { name = "node-library-favorites" };
            panel.Add(category); panel.Add(favorites);
            var catalog = coordinator?.Current?.NodeCatalog ?? Array.Empty<NodeCatalogItem>();
            RebuildLibrary(panel, catalog, coordinator);
            search.RegisterValueChangedCallback(_ => FilterLibrary(panel));
            category.RegisterValueChangedCallback(_ => FilterLibrary(panel));
            favorites.RegisterValueChangedCallback(_ => FilterLibrary(panel));
        }

        private static void FilterLibrary(VisualElement panel)
        {
            if (panel == null) return;
            var query = panel?.Q<TextField>("node-search")?.value ?? string.Empty;
            var category = panel?.Q<PopupField<string>>("node-library-category")?.value ?? "All";
            var onlyFavorites = panel?.Q<Toggle>("node-library-favorites")?.value ?? false;
            foreach (var child in panel.Query<Button>(className: "sd-library-item").ToList())
            {
                var item = child.userData as NodeCatalogItem;
                var matches = (string.IsNullOrWhiteSpace(query) || child.text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) &&
                    (string.Equals(category, "All", StringComparison.OrdinalIgnoreCase) || string.Equals(item?.Category, category, StringComparison.OrdinalIgnoreCase)) &&
                    (!onlyFavorites || item?.IsFavorite == true);
                child.style.display = matches ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private static void RebuildLibrary(VisualElement panel, IEnumerable<NodeCatalogItem> entries, PresentationCoordinator coordinator)
        {
            if (panel == null) return;
            foreach (var old in panel.Query<Button>(className: "sd-library-item").ToList()) old.RemoveFromHierarchy();
            panel.Q<Label>("node-library-empty")?.RemoveFromHierarchy();
            var catalog = (entries ?? Enumerable.Empty<NodeCatalogItem>()).ToList();
            foreach (var item in catalog)
            {
                var entry = item;
                var button = new Button(() => coordinator?.Submit("graph.add_node", entry.TypeId,
                    new KeyValuePairValue("nodeTypeId", entry.TypeId), new KeyValuePairValue("x", "0"), new KeyValuePairValue("y", "0")))
                { text = entry.DisplayName, name = "node-library-" + entry.TypeId.ToLowerInvariant() };
                button.userData = entry;
                Vector2? dragStart = null;
                button.RegisterCallback<PointerDownEvent>(evt =>
                {
                    if (evt.clickCount >= 2)
                    {
                        var canvas = panel.parent?.parent?.Q<GraphCanvasElement>("node-graph-canvas");
                        canvas?.AddNodeAt(entry.TypeId, new PresentationPoint(320, 180));
                        evt.StopPropagation();
                    }
                    else if (evt.button == 0 || evt.button < 0) dragStart = evt.position;
                });
                button.RegisterCallback<PointerUpEvent>(evt =>
                {
                    if ((evt.button != 0 && evt.button >= 0) || evt.clickCount > 1) return;
                    var canvas = panel.parent?.parent?.Q<GraphCanvasElement>("node-graph-canvas");
                    if (canvas != null && dragStart.HasValue && Vector2.Distance(dragStart.Value, evt.position) > 8f)
                    {
                        var point = canvas.WorldToLocal(evt.position);
                        canvas.AddNodeAt(entry.TypeId, canvas.Mapper.ScreenToCanvas(new PresentationPoint(point.x, point.y)));
                    }
                    dragStart = null;
                });
                button.SetEnabled(entry.IsAvailable && entry.UserAddable);
                if (!entry.IsAvailable || !entry.UserAddable) button.tooltip = string.IsNullOrEmpty(entry.DisabledReason) ? "This node is not user-addable." : entry.DisabledReason;
                button.AddToClassList("sd-library-item");
                panel.Add(button);
            }
            var categoryPicker = panel.Q<PopupField<string>>("node-library-category");
            if (categoryPicker != null)
            {
                var categories = catalog.Select(x => string.IsNullOrEmpty(x.Category) ? "Uncategorized" : x.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
                categories.Insert(0, "All"); categoryPicker.choices.Clear(); categoryPicker.choices.AddRange(categories);
            }
            if (catalog.Count == 0) panel.Add(new Label("Node catalog unavailable") { name = "node-library-empty" });
        }

        private static void AddInspector(VisualElement panel, PresentationCoordinator coordinator)
        {
            var header = new VisualElement { name = "inspector-node-header" };
            header.Add(new Label("Select a node") { name = "inspector-empty" });
            header.Add(new Label { name = "inspector-node-type" });
            header.Add(new Label { name = "inspector-node-instance" });
            header.Add(new Label { name = "inspector-node-status" });
            header.Add(new Button(() => coordinator?.Submit("diagnostics.focus")) { text = "Diagnostics", name = "inspector-diagnostics-link" });
            panel.Add(header);
            var tabs = new VisualElement { name = "inspector-tabs" };
            tabs.Add(new Button(() => panel.EnableInClassList("show-standard", true)) { text = "Standard Parameters", name = "inspector-standard-tab" });
            tabs.Add(new Button(() => panel.EnableInClassList("show-standard", false)) { text = "Custom UI", name = "inspector-custom-tab" });
            panel.Add(tabs);
            var filter = new TextField("Filter name / ID / description") { name = "inspector-parameter-filter" };
            panel.Add(filter);
            var baseValue = new TextField("Base Value") { name = "inspector-base-value" };
            var effective = new TextField("Effective Value") { name = "inspector-effective-value", isReadOnly = true };
            baseValue.tabIndex = 2;
            effective.tabIndex = 3;
            TrackFocus(baseValue);
            TrackFocus(effective);
            baseValue.RegisterValueChangedCallback(evt =>
            {
                    var nodeId = panel.userData as string;
                    var parameterId = panel.Q<VisualElement>("inspector-parameter-id")?.userData as string;
                    var commandSink = panel.Q<VisualElement>("inspector-command-sink")?.userData as PresentationCoordinator;
                    if (commandSink != null && !string.IsNullOrEmpty(nodeId) && !string.IsNullOrEmpty(parameterId))
                    {
                        var parameter = commandSink.Current?.Parameters.FirstOrDefault(x => x.NodeId == nodeId && x.ParameterId == parameterId);
                        if (parameter != null && !parameter.IsReadOnly && !parameter.IsBroken)
                            commandSink.Submit("parameter.set_base", parameterId, new KeyValuePairValue("nodeId", nodeId), new KeyValuePairValue("parameterId", parameterId), new KeyValuePairValue("value", evt.newValue ?? string.Empty), new KeyValuePairValue("valueType", parameter.ValueType ?? "String"));
                    }
            });
            var valueRow = new VisualElement { name = "base-effective-row" };
            valueRow.AddToClassList("sd-parameter-value-row");
            valueRow.Add(baseValue);
            valueRow.Add(effective);
            panel.Add(valueRow);
            panel.Add(new Label { name = "inspector-parameter-id" });
            panel.Add(new VisualElement { name = "inspector-command-sink" });
            panel.Q<VisualElement>("inspector-command-sink").userData = coordinator;
            panel.Add(new Label("Expression: Base Value") { name = "inspector-expression" });
            var logicalControl = new TextField("Logical Control ID") { name = "inspector-expression-control" };
            var expressionMin = new TextField("Output Min") { name = "inspector-expression-min" };
            var expressionMax = new TextField("Output Max") { name = "inspector-expression-max" };
            panel.Add(logicalControl);
            panel.Add(expressionMin);
            panel.Add(expressionMax);
            var applyExpression = new VisualElement { name = "inspector-expression-actions" };
            applyExpression.Add(new Button(() => SubmitExpression(panel, ApplicationExpressionKind.BaseValue)) { text = "Apply Base Expression", name = "inspector-apply-base-expression" });
            applyExpression.Add(new Button(() => SubmitExpression(panel, ApplicationExpressionKind.LogicalControl)) { text = "Apply Control Expression", name = "inspector-apply-control-expression" });
            panel.Add(applyExpression);
            panel.Add(new Label("Pending and validation state is shown here.") { name = "inspector-state" });
            panel.Add(new Label { name = "inspector-parameter-metadata" });
            panel.Add(new VisualElement { name = "inspector-parameter-list" });
            filter.RegisterValueChangedCallback(evt =>
            {
                var query = evt.newValue ?? string.Empty;
                var parameterList = panel.Q("inspector-parameter-list");
                if (parameterList == null) return;
                foreach (var row in parameterList.Query<VisualElement>(className: "sd-parameter-row").ToList())
                {
                    var label = row.Q<Label>(className: "sd-parameter-label");
                    var text = label?.text ?? string.Empty;
                    row.style.display = string.IsNullOrWhiteSpace(query) || (row.tooltip ?? string.Empty).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 ? DisplayStyle.Flex : DisplayStyle.None;
                }
            });
        }

        private static void ApplyParameterControl(VisualElement panel, ParameterReadModel parameter, PresentationCoordinator coordinator)
        {
            var root = panel.Q("parameter-control-root");
            if (root == null)
            {
                root = new VisualElement { name = "parameter-control-root" };
                panel.Add(root);
            }
            root.Clear();
            var kind = ControlKindFor(parameter.ValueType);
            var hardRange = TryParseScalarRange(parameter.HardRange, out var rangeMin, out var rangeMax);
            if (hardRange && (string.Equals(parameter.ValueType, "Float", StringComparison.OrdinalIgnoreCase) || string.Equals(parameter.ValueType, "Int", StringComparison.OrdinalIgnoreCase))) kind = ParameterControlKind.Slider;
            var metadata = new ParameterMetadata(parameter.ParameterId, parameter.DisplayName, kind, isHidden: !parameter.IsVisible,
                isReadOnly: parameter.IsReadOnly || parameter.IsBroken, step: parameter.Step > 0d ? parameter.Step : (double?)null,
                min: hardRange ? rangeMin : (double?)null, max: hardRange ? rangeMax : (double?)null,
                unit: parameter.Unit, nodeTypeId: parameter.NodeTypeId, enumOptions: parameter.EnumOptions, mediaOptions: parameter.MediaOptions,
                group: parameter.Group, order: parameter.Order, description: parameter.Description, componentRanges: parameter.ComponentRanges);
            root.style.display = metadata.IsHidden ? DisplayStyle.None : DisplayStyle.Flex;
            if (metadata.IsHidden) return;
            var catalog = new ParameterControlCatalog();
            foreach (ParameterControlKind standard in Enum.GetValues(typeof(ParameterControlKind)))
                catalog.Register(standard, new VisualParameterControlFactory(panel, coordinator, standard));
            object control;
            if (!CustomParameterControls.TryCreateNodeType(parameter.NodeTypeId, metadata, parameter, null, out control))
                control = catalog.CreateOrFallback(metadata, parameter);
            if (control is VisualElement visual)
            {
                visual.name = "parameter-control-" + kind.ToString().ToLowerInvariant();
                root.Add(visual);
            }
            if (parameter.IsClamped || !string.IsNullOrEmpty(parameter.OutputClamp))
                root.Add(new Label("Clamp: " + (string.IsNullOrEmpty(parameter.OutputClamp) ? "hard range" : parameter.OutputClamp)) { name = "parameter-clamp" });
        }

        private static bool TryParseScalarRange(string text, out double minimum, out double maximum)
        {
            minimum = maximum = 0d;
            if (string.IsNullOrWhiteSpace(text)) return false;
            var separator = text.IndexOf("..", StringComparison.Ordinal);
            if (separator <= 0 || separator >= text.Length - 2) return false;
            return double.TryParse(text.Substring(0, separator).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out minimum)
                && double.TryParse(text.Substring(separator + 2).Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out maximum)
                && !double.IsNaN(minimum) && !double.IsNaN(maximum) && minimum <= maximum;
        }

        private sealed class VisualParameterControlFactory : IParameterControlFactory
        {
            private readonly VisualElement _panel;
            private readonly PresentationCoordinator _coordinator;
            private readonly ParameterControlKind _kind;

            public VisualParameterControlFactory(VisualElement panel, PresentationCoordinator coordinator, ParameterControlKind kind)
            { _panel = panel; _coordinator = coordinator; _kind = kind; }

            public object Create(ParameterMetadata metadata, ParameterReadModel value)
            {
                var readOnly = metadata.IsReadOnly || _kind == ParameterControlKind.ReadOnly;
                switch (_kind)
                {
                    case ParameterControlKind.Toggle:
                        var toggle = new Toggle(metadata.DisplayName) { value = string.Equals(value.BaseValue, "true", StringComparison.OrdinalIgnoreCase) };
                        toggle.SetEnabled(!readOnly);
                        toggle.RegisterValueChangedCallback(evt => Submit(evt.newValue ? "true" : "false", "Bool", value));
                        return toggle;
                    case ParameterControlKind.Numeric:
                    case ParameterControlKind.Slider:
                        var numeric = new VisualElement { name = "numeric-control-row" };
                        var isInteger = string.Equals(value.ValueType, "Int", StringComparison.OrdinalIgnoreCase);
                        var field = isInteger ? (VisualElement)new IntegerField(metadata.DisplayName) { value = ParseInt(value.BaseValue) } : new FloatField(metadata.DisplayName) { value = ParseFloat(value.BaseValue) };
                        field.name = isInteger ? "parameter-integer-field" : "parameter-float-field";
                        field.tooltip = metadata.Min.HasValue && metadata.Max.HasValue
                            ? "Range " + metadata.Min.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".." + metadata.Max.Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
                            : string.Empty;
                        field.SetEnabled(!readOnly);
                        if (isInteger)
                        {
                            ((IntegerField)field).RegisterValueChangedCallback(evt => Submit(evt.newValue.ToString(System.Globalization.CultureInfo.InvariantCulture), "Int", value));
                        }
                        else
                        {
                            ((FloatField)field).RegisterValueChangedCallback(evt => Submit(evt.newValue.ToString(System.Globalization.CultureInfo.InvariantCulture), "Float", value));
                        }
                        numeric.Add(field);
                        var dragLabel = new Label(metadata.DisplayName) { name = "parameter-value-drag-label" };
                        var dragStart = Vector2.zero;
                        var dragValue = isInteger ? ((IntegerField)field).value : ((FloatField)field).value;
                        dragLabel.RegisterCallback<PointerDownEvent>(evt =>
                        {
                            if (readOnly) return;
                            dragStart = evt.position;
                            dragValue = isInteger ? ((IntegerField)field).value : ((FloatField)field).value;
                            dragLabel.CapturePointer(evt.pointerId);
                            evt.StopPropagation();
                        });
                        dragLabel.RegisterCallback<PointerMoveEvent>(evt =>
                        {
                            if (!dragLabel.HasPointerCapture(evt.pointerId) || readOnly) return;
                            var multiplier = evt.shiftKey ? 0.1d : evt.altKey ? 10d : evt.ctrlKey ? 0.01d : 1d;
                            var step = metadata.Step.HasValue && metadata.Step.Value > 0d ? metadata.Step.Value : (isInteger ? 1d : .01d);
                            var next = dragValue + (dragStart.x - evt.position.x) * step * multiplier;
                            if (metadata.Min.HasValue) next = Math.Max(metadata.Min.Value, next);
                            if (metadata.Max.HasValue) next = Math.Min(metadata.Max.Value, next);
                            if (isInteger) ((IntegerField)field).value = Mathf.RoundToInt((float)next); else ((FloatField)field).value = (float)next;
                            dragStart = evt.position;
                        });
                        dragLabel.RegisterCallback<PointerUpEvent>(evt =>
                        {
                            if (dragLabel.HasPointerCapture(evt.pointerId)) dragLabel.ReleasePointer(evt.pointerId);
                        });
                        numeric.Insert(0, dragLabel);
                        if (_kind == ParameterControlKind.Slider)
                        {
                            var current = isInteger ? ((IntegerField)field).value : ((FloatField)field).value;
                            var sliderMinimum = metadata.Min.HasValue ? (float)metadata.Min.Value : 0f;
                            var sliderMaximum = metadata.Max.HasValue ? (float)metadata.Max.Value : 1f;
                            if (sliderMaximum <= sliderMinimum) sliderMaximum = sliderMinimum + 1f;
                            var slider = new Slider(sliderMinimum, sliderMaximum) { value = Mathf.Clamp(current, sliderMinimum, sliderMaximum), name = "parameter-slider", showInputField = true };
                            slider.tooltip = metadata.Step.HasValue ? "Step " + metadata.Step.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;
                            slider.SetEnabled(!readOnly);
                            slider.RegisterValueChangedCallback(evt => { if (isInteger) ((IntegerField)field).SetValueWithoutNotify(Mathf.RoundToInt(evt.newValue)); else ((FloatField)field).SetValueWithoutNotify(evt.newValue); Submit(evt.newValue.ToString(System.Globalization.CultureInfo.InvariantCulture), isInteger ? "Int" : "Float", value); });
                            numeric.Add(slider);
                        }
                        return numeric;
                    case ParameterControlKind.Color:
                        return CreateComponentControl(metadata.DisplayName, value, 4, "Color", "parameter-color", readOnly, new[] { "R", "G", "B", "A" });
                    case ParameterControlKind.Vector:
                        var count = string.Equals(value.ValueType, "Vector2", StringComparison.OrdinalIgnoreCase) ? 2 : string.Equals(value.ValueType, "Vector3", StringComparison.OrdinalIgnoreCase) ? 3 : 4;
                        return CreateComponentControl(metadata.DisplayName, value, count, value.ValueType, "parameter-" + value.ValueType.ToLowerInvariant(), readOnly, count == 2 ? new[] { "X", "Y" } : count == 3 ? new[] { "X", "Y", "Z" } : new[] { "X", "Y", "Z", "W" });
                    case ParameterControlKind.Enum:
                        var enumChoices = metadata.EnumOptions == null || metadata.EnumOptions.Count == 0 ? new List<string> { value.BaseValue ?? string.Empty } : metadata.EnumOptions.Select(x => x.Id).ToList();
                        if (!enumChoices.Contains(value.BaseValue ?? string.Empty)) enumChoices.Insert(0, value.BaseValue ?? string.Empty);
                        var popup = new PopupField<string>(metadata.DisplayName, enumChoices, Math.Max(0, enumChoices.IndexOf(value.BaseValue ?? string.Empty))) { value = value.BaseValue ?? string.Empty, name = "parameter-enum-field" };
                        popup.SetEnabled(!readOnly);
                        popup.RegisterValueChangedCallback(evt => Submit(evt.newValue, "Enum", value));
                        return popup;
                    case ParameterControlKind.Media:
                        var mediaChoices = metadata.MediaOptions == null || metadata.MediaOptions.Count == 0 ? new List<string> { value.BaseValue ?? string.Empty } : metadata.MediaOptions.ToList();
                        if (!mediaChoices.Contains(value.BaseValue ?? string.Empty)) mediaChoices.Insert(0, value.BaseValue ?? string.Empty);
                        var media = new PopupField<string>(metadata.DisplayName, mediaChoices, Math.Max(0, mediaChoices.IndexOf(value.BaseValue ?? string.Empty))) { value = value.BaseValue ?? string.Empty, name = "parameter-media-field" };
                        media.SetEnabled(!readOnly);
                        media.RegisterValueChangedCallback(evt => Submit(NormalizeMediaSelection(evt.newValue), "MediaAssetReference", value));
                        return media;
                    case ParameterControlKind.Text:
                        var text = new TextField(metadata.DisplayName) { value = value.BaseValue ?? string.Empty, isReadOnly = readOnly };
                        text.RegisterValueChangedCallback(evt => Submit(evt.newValue ?? string.Empty, "String", value));
                        return text;
                    case ParameterControlKind.Broken:
                        return new Label("Broken: " + value.Error) { name = "parameter-control-broken" };
                    default:
                        return new Label(metadata.DisplayName + ": " + value.EffectiveValue) { name = "parameter-control-readonly" };
                }
            }

            private void Submit(string text, string valueType, ParameterReadModel parameter)
            {
                if (_coordinator == null || parameter == null || parameter.IsReadOnly || parameter.IsBroken) return;
                _coordinator.Submit("parameter.set_base", parameter.ParameterId,
                    new KeyValuePairValue("nodeId", parameter.NodeId), new KeyValuePairValue("parameterId", parameter.ParameterId),
                    new KeyValuePairValue("value", text ?? string.Empty), new KeyValuePairValue("valueType", valueType));
            }

            private VisualElement CreateComponentControl(string title, ParameterReadModel parameter, int count, string valueType, string name, bool readOnly, string[] labels)
            {
                var row = new VisualElement { name = name };
                row.Add(new Label(title) { name = name + "-label" });
                var values = ParseComponents(parameter.BaseValue, count);
                for (var i = 0; i < count; i++)
                {
                    var index = i;
                    var field = new FloatField(labels[i]) { name = name + "-" + labels[i].ToLowerInvariant(), value = values[index] };
                    var range = parameter.ComponentRanges?.FirstOrDefault(x => string.Equals(x.Name, labels[i], StringComparison.OrdinalIgnoreCase));
                    field.tooltip = range == null ? string.Empty : "Range " + range.Minimum + ".." + range.Maximum;
                    field.SetEnabled(!readOnly);
                    field.RegisterValueChangedCallback(_ => Submit(SerializeComponents(row, count), valueType, parameter));
                    row.Add(field);
                }
                return row;
            }

            private static float[] ParseComponents(string text, int count)
            {
                var result = new float[count];
                var parts = (text ?? string.Empty).Trim().Trim('(', ')', '[', ']').Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                for (var i = 0; i < count && i < parts.Length; i++) float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out result[i]);
                return result;
            }

            private static string SerializeComponents(VisualElement row, int count)
            {
                var values = new List<string>();
                var fields = row.Query<FloatField>().ToList();
                for (var i = 0; i < fields.Count && i < count; i++) values.Add(fields[i].value.ToString(System.Globalization.CultureInfo.InvariantCulture));
                return string.Join(",", values);
            }

            private static float ParseFloat(string text) => float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var value) ? value : 0f;
            private static int ParseInt(string text) => int.TryParse(text, out var value) ? value : 0;
        }

        private static void SubmitExpression(VisualElement panel, ApplicationExpressionKind kind)
        {
            var commandSink = panel?.Q<VisualElement>("inspector-command-sink")?.userData as PresentationCoordinator;
            var nodeId = panel?.userData as string;
            var parameterId = panel?.Q<VisualElement>("inspector-parameter-id")?.userData as string;
            if (commandSink == null || string.IsNullOrEmpty(nodeId) || string.IsNullOrEmpty(parameterId)) return;
            var parameter = commandSink.Current?.Parameters.FirstOrDefault(x => x.NodeId == nodeId && x.ParameterId == parameterId);
            if (parameter == null) return;
            var payload = new List<KeyValuePairValue>
            {
                new KeyValuePairValue("nodeId", nodeId),
                new KeyValuePairValue("parameterId", parameterId),
                new KeyValuePairValue("kind", kind.ToString()),
                new KeyValuePairValue("logicalControlId", panel.Q<TextField>("inspector-expression-control")?.value ?? string.Empty),
                new KeyValuePairValue("valueType", parameter.ValueType ?? string.Empty)
            };
            var minimum = panel.Q<TextField>("inspector-expression-min")?.value;
            var maximum = panel.Q<TextField>("inspector-expression-max")?.value;
            if (!string.IsNullOrWhiteSpace(minimum) && !string.IsNullOrWhiteSpace(maximum))
            {
                payload.Add(new KeyValuePairValue("outputMinimum", minimum));
                payload.Add(new KeyValuePairValue("outputMaximum", maximum));
            }
            commandSink.Submit("parameter.apply_expression", parameterId, payload.ToArray());
        }

        private static void AddDashboard(VisualElement panel, PresentationCoordinator coordinator)
        {
            var toolbar = new VisualElement { name = "dashboard-toolbar" };
            toolbar.Add(new Toggle("Arrange") { name = "dashboard-arrange-toggle", tooltip = "Enable widget drag and resize" });
            toolbar.Add(new PopupField<string>("Page", new List<string> { "Main" }, 0) { name = "dashboard-page-selector" });
            toolbar.Add(new Button(() => coordinator?.Submit("dashboard.rename_page", "Main", new KeyValuePairValue("pageId", "Main"), new KeyValuePairValue("name", "Main"))) { text = "Rename Page", name = "dashboard-rename-page" });
            panel.Add(toolbar);
            var grid = new VisualElement { name = "dashboard-grid-12-columns" };
            grid.style.flexDirection = FlexDirection.Row;
            for (var i = 0; i < DashboardLayoutValidator.Columns; i++)
            {
                var column = new VisualElement { name = "dashboard-column-" + i };
                column.style.flexGrow = 1;
                column.Add(new Label((i + 1).ToString()) { name = "dashboard-column-label-" + i });
                grid.Add(column);
            }
            panel.Add(grid);
            panel.Add(new Label("No dashboard loaded") { name = "dashboard-state" });
            var actions = new VisualElement { name = "dashboard-actions" };
            actions.Add(new Button(() =>
            {
                var pageId = Guid.NewGuid().ToString("D");
                coordinator?.Submit("dashboard.add_page", pageId, new KeyValuePairValue("pageId", pageId), new KeyValuePairValue("name", "Dashboard"));
            }) { text = "Add Page", name = "dashboard-add-page" });
            actions.Add(new Button(() =>
            {
                var page = coordinator?.Current?.DashboardPages.FirstOrDefault();
                var parameter = coordinator?.Current?.Parameters.FirstOrDefault();
                if (page != null && parameter != null)
                    coordinator.Submit("dashboard.add_widget", parameter.ParameterId, new KeyValuePairValue("pageId", page.Id), new KeyValuePairValue("widgetId", Guid.NewGuid().ToString("D")), new KeyValuePairValue("nodeId", parameter.NodeId), new KeyValuePairValue("parameterId", parameter.ParameterId), new KeyValuePairValue("column", "0"), new KeyValuePairValue("row", "0"), new KeyValuePairValue("width", "2"), new KeyValuePairValue("height", "1"));
            }) { text = "Add Widget", name = "dashboard-add-widget" });
            panel.Add(actions);
        }

        private static void ApplyDashboard(VisualElement panel, IReadOnlyList<DashboardPageReadModel> pages, PresentationCoordinator coordinator)
        {
            var grid = panel?.Q("dashboard-grid-12-columns");
            if (grid == null) return;
            var pageSelector = panel.Q<PopupField<string>>("dashboard-page-selector");
            if (pageSelector != null)
            {
                var pageNames = (pages ?? Array.Empty<DashboardPageReadModel>()).Select(x => x.Name).Where(x => !string.IsNullOrEmpty(x)).ToList();
                if (pageNames.Count == 0) pageNames.Add("Main");
                pageSelector.choices.Clear(); pageSelector.choices.AddRange(pageNames); pageSelector.SetValueWithoutNotify(pageNames[0]);
            }
            foreach (var old in grid.Query<VisualElement>(className: "sd-dashboard-widget").ToList()) old.RemoveFromHierarchy();
            foreach (var page in pages ?? Array.Empty<DashboardPageReadModel>())
            {
                foreach (var widget in page.Widgets)
                {
                    var item = widget;
                    VisualElement label;
                    if (item.IsBroken)
                    {
                        var broken = new Button(() => coordinator?.Submit("dashboard.remove_widget", item.Id, new KeyValuePairValue("pageId", page.Id), new KeyValuePairValue("widgetId", item.Id))) { text = "Broken · " + item.ParameterId };
                        var candidates = (coordinator?.Current?.Parameters ?? Array.Empty<ParameterReadModel>())
                            .Where(x => !x.IsBroken && x.IsVisible)
                            .OrderBy(x => x.NodeId, StringComparer.Ordinal).ThenBy(x => x.ParameterId, StringComparer.Ordinal)
                            .Select(x => x.NodeId + "|" + x.ParameterId).ToList();
                        var replacement = new PopupField<string>("Target", candidates, 0) { name = "dashboard-rebind-target-" + item.Id };
                        replacement.SetEnabled(candidates.Count > 0);
                        broken.Add(replacement);
                        broken.Add(new Button(() =>
                        {
                            var parts = (replacement.value ?? string.Empty).Split('|');
                            if (parts.Length == 2) coordinator?.Submit("dashboard.rebind_widget", item.Id,
                                new KeyValuePairValue("pageId", page.Id), new KeyValuePairValue("widgetId", item.Id),
                                new KeyValuePairValue("nodeId", parts[0]), new KeyValuePairValue("parameterId", parts[1]));
                        }) { text = "Rebind", name = "dashboard-rebind-" + item.Id });
                        label = broken;
                    }
                    else
                    {
                        var widgetButton = new Button(() => coordinator?.Submit("parameter.set_base", item.ParameterId,
                            new KeyValuePairValue("nodeId", item.NodeId), new KeyValuePairValue("parameterId", item.ParameterId)))
                        { text = (string.IsNullOrEmpty(item.NodeId) ? item.ParameterId : item.NodeId + " · ") + item.ParameterId };
                        widgetButton.Add(new Button(() => coordinator?.Submit("dashboard.duplicate_widget", item.Id,
                            new KeyValuePairValue("pageId", page.Id), new KeyValuePairValue("widgetId", item.Id))) { text = "Duplicate", name = "dashboard-duplicate-" + item.Id });
                        widgetButton.Add(new Button(() => coordinator?.Submit("dashboard.remove_widget", item.Id,
                            new KeyValuePairValue("pageId", page.Id), new KeyValuePairValue("widgetId", item.Id))) { text = "Remove", name = "dashboard-remove-" + item.Id });
                        label = widgetButton;
                    }
                    label.name = "dashboard-widget-" + item.Id;
                    label.AddToClassList("sd-dashboard-widget");
                    label.tooltip = item.IsBroken ? "Remove or rebind: " + item.Id : "Drag/resize in Arrange mode · " + item.DisplayMode;
                    label.RegisterCallback<PointerUpEvent>(evt =>
                    {
                        var arrange = panel.Q<Toggle>("dashboard-arrange-toggle");
                        if (arrange == null || !arrange.value || item.IsBroken) return;
                        coordinator?.Submit("dashboard.set_widget_layout", item.Id,
                            new KeyValuePairValue("pageId", page.Id), new KeyValuePairValue("widgetId", item.Id),
                            new KeyValuePairValue("column", item.Column.ToString()), new KeyValuePairValue("row", item.Row.ToString()),
                            new KeyValuePairValue("width", item.Width.ToString()), new KeyValuePairValue("height", item.Height.ToString()));
                    });
                    label.style.position = Position.Absolute;
                    label.style.left = item.Column * 8;
                    label.style.top = item.Row * 24;
                    label.style.width = item.Width * 8;
                    label.style.height = item.Height * 24;
                    grid.Add(label);
                }
            }
        }


        private static void AddOutputs(VisualElement panel, PresentationCoordinator coordinator)
        {
            var program = new VisualElement { name = "program-monitor" };
            program.AddToClassList("sd-output-surface");
            program.AddToClassList("sd-program-monitor");
            program.Add(new Label("Program") { name = "program-monitor-title" });
            program.Add(new Button(() => { program.EnableInClassList("is-closed", true); coordinator?.ProgramPresenter?.SetVisible(false); }) { text = "Close", name = "program-close" });
            var displayCount = Math.Max(1, coordinator?.DisplayIdentifyPort?.DisplayCount ?? 3);
            var displayChoices = Enumerable.Range(1, displayCount).Select(index => "Display " + index).ToList();
            var display = new PopupField<string>("Display", displayChoices, Math.Min(1, displayChoices.Count - 1)) { name = "program-display-selector" };
            display.RegisterValueChangedCallback(evt =>
            {
                var number = ParseDisplayNumber(evt.newValue);
                coordinator?.Submit("output.program.display", number.ToString(), new KeyValuePairValue("display", number.ToString()));
            });
            program.Add(display);
            var identify = new VisualElement { name = "display-identify-overlay" };
            identify.AddToClassList("sd-display-identify-overlay");
            program.Add(new Button(() =>
            {
                identify.Clear();
                var port = coordinator?.DisplayIdentifyPort;
                if (port == null)
                {
                    identify.Add(new Label("Display identify unavailable") { name = "display-identify-error" });
                    return;
                }
                for (var index = 1; index <= Math.Max(1, port.DisplayCount); index++)
                {
                    if (port.TryIdentify(index, out var error)) identify.Add(new Label("Display " + index) { name = "display-identify-number-" + index });
                    else identify.Add(new Label("Display " + index + ": " + error) { name = "display-identify-error-" + index });
                }
                identify.schedule.Execute(() => identify.Clear()).StartingIn(3000);
            }) { text = "Identify Displays", name = "program-identify-display", tooltip = "Identify each available external display without compositing into Program" });
            program.Add(identify);
            program.Add(new VisualElement { name = "program-image", pickingMode = PickingMode.Ignore });
            program.Add(new Label("CPU Frame Time Unavailable · GPU Frame Time Unavailable") { name = "program-monitor-metrics" });
            var performanceWarning = new Label { name = "program-performance-warning" };
            performanceWarning.AddToClassList("sd-program-performance-warning");
            program.Add(performanceWarning);
            var holdingNotice = new Label { name = "program-holding-notice" };
            holdingNotice.AddToClassList("sd-program-holding-notice");
            program.Add(holdingNotice);
            program.Add(new Label("No valid frame") { name = "program-monitor-footer" });
            var preview = new VisualElement { name = "preview-viewer-host" };
            preview.AddToClassList("sd-output-surface");
            preview.Add(new Label("Preview Host (0/8)") { name = "preview-host-title" });
            var previewToolbar = new VisualElement { name = "preview-toolbar" };
            previewToolbar.Add(new Button(() => SetPreviewMode(preview, coordinator, PresentationOutputFit.Fit)) { text = "Fit", name = "preview-fit" });
            previewToolbar.Add(new Button(() => SetPreviewMode(preview, coordinator, PresentationOutputFit.Fill)) { text = "Fill", name = "preview-fill" });
            previewToolbar.Add(new Button(() => SetPreviewMode(preview, coordinator, PresentationOutputFit.Stretch)) { text = "Stretch", name = "preview-stretch" });
            previewToolbar.Add(new Button(() => SetPreviewBackground(preview, coordinator, PresentationOutputBackground.Black)) { text = "Black", name = "preview-background-black" });
            previewToolbar.Add(new Button(() => SetPreviewBackground(preview, coordinator, PresentationOutputBackground.Checker)) { text = "Checker", name = "preview-background-checker" });
            previewToolbar.Add(new Button(() =>
            {
                var old = preview.Q<VisualElement>("preview-quality-popover");
                old?.RemoveFromHierarchy();
                var popover = new VisualElement { name = "preview-quality-popover" };
                popover.AddToClassList("sd-preview-quality-popover");
                foreach (var item in coordinator?.Current?.Output?.Previews ?? Array.Empty<PreviewReadModel>())
                {
                    var focus = string.Equals(preview.userData as string, item.TabId, StringComparison.Ordinal) ? " · Focus" : string.Empty;
                    popover.Add(new Label(item.NodeId + " · " + QualityLabel(item.Quality) + " · " + item.StateText + focus) { name = "preview-quality-detail-" + item.TabId });
                }
                preview.Add(popover);
            }) { text = "Quality", name = "preview-quality-details" });
            previewToolbar.Add(new Button(() => { preview.EnableInClassList("is-hidden", true); SubmitPreviewDemand(coordinator, false); }) { text = "Hide", name = "preview-hide" });
            previewToolbar.Add(new Button(() => { preview.EnableInClassList("is-hidden", false); SubmitPreviewDemand(coordinator, true); }) { text = "Show", name = "preview-show" });
            preview.Add(previewToolbar);
            preview.Add(new TabView { name = "preview-tabs" });
            panel.Add(program);
            panel.Add(preview);
        }

        private static int ParseDisplayNumber(string value)
        {
            if (string.IsNullOrEmpty(value)) return 2;
            var digits = new string(value.Where(char.IsDigit).ToArray());
            return int.TryParse(digits, out var number) ? number : 2;
        }

        private static void SetPreviewMode(VisualElement preview, PresentationCoordinator coordinator, PresentationOutputFit mode)
        {
            preview.EnableInClassList("is-fit", mode == PresentationOutputFit.Fit);
            preview.EnableInClassList("is-fill", mode == PresentationOutputFit.Fill);
            preview.EnableInClassList("is-stretch", mode == PresentationOutputFit.Stretch);
            var previewId = preview?.userData as string ?? coordinator?.Current?.Output?.Previews.FirstOrDefault()?.TabId;
            var background = coordinator?.Current?.Output?.Previews.FirstOrDefault(x => x.TabId == previewId)?.Background.ToString() ?? "Black";
            if (!string.IsNullOrEmpty(previewId)) coordinator.Submit("preview.settings", previewId, new KeyValuePairValue("fit", mode.ToString()), new KeyValuePairValue("background", background));
        }

        private static void SetPreviewBackground(VisualElement preview, PresentationCoordinator coordinator, PresentationOutputBackground background)
        {
            var previewId = preview?.userData as string ?? coordinator?.Current?.Output?.Previews.FirstOrDefault()?.TabId;
            var fit = coordinator?.Current?.Output?.Previews.FirstOrDefault(x => x.TabId == previewId)?.Fit ?? PresentationOutputFit.Fit;
            if (!string.IsNullOrEmpty(previewId)) coordinator.Submit("preview.settings", previewId,
                new KeyValuePairValue("fit", fit.ToString()), new KeyValuePairValue("background", background.ToString()));
        }

        private static void SubmitPreviewDemand(PresentationCoordinator coordinator, bool focused)
        {
            coordinator?.Submit("preview.host.visible", focused ? "visible" : "hidden", new KeyValuePairValue("visible", focused ? "true" : "false"));
        }

        private static string QualityLabel(PresentationQualityStage quality)
        {
            var stage = ((int)quality).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var policy = quality == PresentationQualityStage.Full ? "Full" : quality == PresentationQualityStage.Reduced ? "Reduced" : quality == PresentationQualityStage.Minimum ? "Minimum" : "Auto";
            return "Quality " + stage + " (" + policy + ")";
        }

        private static string PreviewStateLabel(PreviewReadModel preview)
        {
            if (preview == null) return string.Empty;
            var state = preview.StateText ?? string.Empty;
            if (state.IndexOf("Blocked", StringComparison.OrdinalIgnoreCase) >= 0) return "Blocked · missing input · Diagnostics";
            if (state.IndexOf("Fault", StringComparison.OrdinalIgnoreCase) >= 0) return "Faulted · " + state;
            if (state.IndexOf("Preparing", StringComparison.OrdinalIgnoreCase) >= 0) return "Preparing · " + state;
            if (state.IndexOf("Fallback", StringComparison.OrdinalIgnoreCase) >= 0) return "UsingFallback · " + state;
            return state;
        }

        private static void AddPresets(VisualElement parent, PresentationCoordinator coordinator)
        {
            var panel = CreatePanel("presets-panel", "Presets");
            panel.style.flexGrow = 1;
            var toolbar = new VisualElement { name = "presets-toolbar" };
            var categories = new PopupField<string>("Category", new List<string> { "All", "Uncategorized" }, 0) { name = "preset-category-filter" };
            toolbar.Add(categories);
            var reorder = new Toggle("Reorder") { name = "preset-reorder-toggle", tooltip = "Enable drag reorder" };
            toolbar.Add(reorder);
            toolbar.Add(new Button(() => coordinator?.Submit("preset.new", Guid.NewGuid().ToString("D"), new KeyValuePairValue("name", "New Preset"))) { text = "New", name = "preset-new" });
            toolbar.Add(new Button(() => coordinator?.Submit("preset.edit", panel.userData as string)) { text = "Edit", name = "preset-edit" });
            toolbar.Add(new Button(() => coordinator?.Submit("preset.duplicate", panel.userData as string)) { text = "Duplicate", name = "preset-duplicate" });
            toolbar.Add(new Button(() => coordinator?.Submit("preset.rename", panel.userData as string)) { text = "Rename", name = "preset-rename" });
            toolbar.Add(new Button(() => coordinator?.Submit("preset.delete", panel.userData as string)) { text = "Delete", name = "preset-delete" });
            panel.Add(toolbar);
            var editor = new VisualElement { name = "preset-editor-panel" };
            editor.AddToClassList("sd-preset-editor");
            editor.Add(new TextField("Name") { name = "preset-editor-name" });
            editor.Add(new TextField("Category") { name = "preset-editor-category" });
            editor.Add(new Label("Target tree / captured BaseValue entries") { name = "preset-editor-tree" });
            editor.Add(new Button(() => coordinator?.Submit("preset.recapture_selected", panel.userData as string)) { text = "Recapture Selected", name = "preset-recapture-selected" });
            editor.Add(new Button(() => coordinator?.Submit("preset.save", panel.userData as string)) { text = "Save Preset", name = "preset-save" });
            panel.Add(editor);
            categories.RegisterValueChangedCallback(_ => RebuildPresets(panel, coordinator?.Current?.Presets, coordinator));
            RebuildPresets(panel, coordinator?.Current?.Presets, coordinator);
            parent.Add(panel);
        }

        private static void RebuildPresets(VisualElement panel, IEnumerable<PresetListItemReadModel> entries, PresentationCoordinator coordinator)
        {
            if (panel == null) return;
            foreach (var old in panel.Query<Button>(className: "sd-preset-item").ToList()) old.RemoveFromHierarchy();
            var categoryFilter = panel.Q<PopupField<string>>("preset-category-filter")?.value ?? "All";
            var presets = (entries ?? Enumerable.Empty<PresetListItemReadModel>())
                .Where(x => string.Equals(categoryFilter, "All", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(string.IsNullOrEmpty(x.Category) ? "Uncategorized" : x.Category, categoryFilter, StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.SortIndex).ThenBy(x => x.Name, StringComparer.Ordinal).ToList();
            var categoryPicker = panel.Q<PopupField<string>>("preset-category-filter");
            if (categoryPicker != null)
            {
                var categories = (entries ?? Enumerable.Empty<PresetListItemReadModel>()).Select(x => string.IsNullOrEmpty(x.Category) ? "Uncategorized" : x.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
                categories.Insert(0, "All"); categoryPicker.choices.Clear(); categoryPicker.choices.AddRange(categories);
                if (!categories.Contains(categoryFilter)) categoryPicker.SetValueWithoutNotify("All");
            }
            foreach (var preset in presets)
            {
                var item = preset;
                var button = new Button(() =>
                {
                    panel.userData = item.Id;
                    coordinator?.Submit("preset.apply", item.Id, new KeyValuePairValue("presetId", item.Id));
                }) { text = item.Name + (string.IsNullOrEmpty(item.Category) ? string.Empty : " · " + item.Category), name = "preset-button-" + item.Id.ToLowerInvariant() };
                button.AddToClassList("sd-preset-item");
                button.SetEnabled(!item.IsBroken);
                if (item.IsBroken) button.tooltip = "Broken preset · " + item.BrokenReason + " · invocation rejected atomically";
                button.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
                {
                    evt.menu.AppendAction("Edit", _ => coordinator?.Submit("preset.edit", item.Id));
                    evt.menu.AppendAction("Duplicate", _ => coordinator?.Submit("preset.duplicate", item.Id));
                    evt.menu.AppendAction("Rename", _ => coordinator?.Submit("preset.rename", item.Id));
                    evt.menu.AppendAction("Delete", _ => coordinator?.Submit("preset.delete", item.Id));
                });
                panel.Add(button);
            }
            if (presets.Count == 0) panel.Add(new Label("No presets") { name = "preset-list-empty" });
        }

        private static void AddMedia(VisualElement parent, PresentationCoordinator coordinator)
        {
            var panel = CreatePanel("media-panel", "Media Library");
            panel.style.flexGrow = 1;
            var toolbar = new VisualElement { name = "media-toolbar" };
            toolbar.Add(new Button(() => BeginMediaImport(panel, coordinator)) { text = "Import", name = "media-import-button" });
            toolbar.Add(new TextField("Search") { name = "media-search" });
            toolbar.Add(new PopupField<string>("Type", new List<string> { "All", "Image", "Video", "Audio", "Effect" }, 0) { name = "media-kind-filter" });
            toolbar.Add(new PopupField<string>("Status", new List<string> { "All", "Ready", "Importing", "Broken" }, 0) { name = "media-status-filter" });
            var viewMode = new PopupField<string>("View", new List<string> { "Grid", "List" }, 0) { name = "media-view-mode" };
            viewMode.RegisterValueChangedCallback(evt =>
            {
                panel.EnableInClassList("media-list-view", string.Equals(evt.newValue, "List", StringComparison.OrdinalIgnoreCase));
                coordinator?.Submit("workspace.media_view", "media", new KeyValuePairValue("value", evt.newValue));
            });
            toolbar.Add(viewMode);
            panel.Add(toolbar);
            panel.Add(new Label("No media") { name = "media-import-progress" });
            panel.Add(new VisualElement { name = "media-items" });
            panel.Add(new Label("Copy · Size validation · Hash validation · Probe") { name = "media-import-stages" });
            panel.Add(new Button(() => coordinator?.Submit("media.confirm_import", "media-import", new KeyValuePairValue("approved", "true"))) { text = "Confirm Import", name = "media-confirm-import" });
            panel.Q<TextField>("media-search").RegisterValueChangedCallback(_ => FilterMedia(panel));
            panel.Q<PopupField<string>>("media-kind-filter").RegisterValueChangedCallback(_ => FilterMedia(panel));
            panel.Q<PopupField<string>>("media-status-filter").RegisterValueChangedCallback(_ => FilterMedia(panel));
            parent.Add(panel);
        }

        private static void BeginMediaImport(VisualElement panel, PresentationCoordinator coordinator)
        {
            var progress = panel?.Q<Label>("media-import-progress");
            var platform = coordinator?.PlatformFiles;
            if (platform == null) { if (progress != null) progress.text = "File picker unavailable"; return; }
            var requestId = Guid.NewGuid();
            var sessionId = coordinator.CurrentEnvelope?.ProjectSessionId ?? Guid.Empty;
            if (progress != null) progress.text = "Selecting files · Copy pending";
            platform.PickPath(new PlatformPathRequest(requestId, sessionId, PlatformPathRequestKind.MultiFile, "Import media"), result =>
            {
                if (progress == null) return;
                if (sessionId != (coordinator?.CurrentEnvelope?.ProjectSessionId ?? Guid.Empty)) { progress.text = "Ignored stale file selection"; return; }
                if (result == null || !result.Succeeded || result.AbsolutePaths.Count == 0) { progress.text = string.IsNullOrEmpty(result?.Error) ? "Import cancelled" : result.Error; return; }
                progress.text = "Importing " + result.AbsolutePaths.Count + " media item(s) · Copy / Size / Hash / Probe";
                coordinator.Submit("media.import.batch", requestId.ToString("D"), new KeyValuePairValue("paths", string.Join("\n", result.AbsolutePaths)));
            });
        }

        private static void FilterMedia(VisualElement panel)
        {
            var query = panel?.Q<TextField>("media-search")?.value ?? string.Empty;
            var kind = panel?.Q<PopupField<string>>("media-kind-filter")?.value ?? "All";
            var status = panel?.Q<PopupField<string>>("media-status-filter")?.value ?? "All";
            foreach (var item in panel?.Q("media-items")?.Children() ?? Enumerable.Empty<VisualElement>())
            {
                var text = item.Q<Label>()?.text ?? string.Empty;
                item.style.display = (string.IsNullOrWhiteSpace(query) || text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) &&
                    (string.Equals(kind, "All", StringComparison.OrdinalIgnoreCase) || text.IndexOf(kind, StringComparison.OrdinalIgnoreCase) >= 0) &&
                    (string.Equals(status, "All", StringComparison.OrdinalIgnoreCase) || text.IndexOf(status, StringComparison.OrdinalIgnoreCase) >= 0)
                    ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private static void AddControls(VisualElement parent, PresentationCoordinator coordinator)
        {
            var panel = CreatePanel("controls-panel", "Controls");
            panel.style.flexGrow = 1;
            var details = new VisualElement { name = "controls-details" };
            details.Add(new Label("Select a Value or PresetTrigger control") { name = "controls-detail-empty" });
            details.Add(new TextField("Name") { name = "control-name-editor" });
            details.Add(new Label("Targets: nodes · parameters · range · invert · expression uses") { name = "control-targets" });
            details.Add(new Button(() => coordinator?.Submit("control.rename", panel.userData as string, new KeyValuePairValue("logicalControlId", panel.userData as string), new KeyValuePairValue("name", details.Q<TextField>("control-name-editor")?.value ?? string.Empty))) { text = "Rename", name = "control-rename" });
            details.Add(new Button(() => coordinator?.Submit("control.duplicate", panel.userData as string, new KeyValuePairValue("logicalControlId", panel.userData as string))) { text = "Duplicate", name = "control-duplicate" });
            panel.Add(details);
            panel.Add(new Button(() =>
            {
                var id = Guid.NewGuid().ToString("D");
                coordinator?.Submit("control.add", id, new KeyValuePairValue("logicalControlId", id), new KeyValuePairValue("name", "Control"), new KeyValuePairValue("kind", "Value"), new KeyValuePairValue("initialValue", "0"));
            }) { text = "Add", name = "control-add" });
            panel.Add(new Button(() =>
            {
                var id = Guid.NewGuid().ToString("D");
                coordinator?.Submit("control.add", id, new KeyValuePairValue("logicalControlId", id), new KeyValuePairValue("name", "Preset Trigger"), new KeyValuePairValue("kind", "PresetTrigger"), new KeyValuePairValue("threshold", "0.5"), new KeyValuePairValue("resetThreshold", "0.4"));
            }) { text = "Add PresetTrigger", name = "control-add-preset-trigger" });
            panel.Add(new Label("No controls") { name = "controls-empty" });
            parent.Add(panel);
        }

        private static void RebuildControls(VisualElement panel, IEnumerable<LogicalControlReadModel> entries, PresentationCoordinator coordinator)
        {
            if (panel == null) return;
            foreach (var old in panel.Query<VisualElement>(className: "sd-control-item").ToList()) old.RemoveFromHierarchy();
            var controls = (entries ?? Enumerable.Empty<LogicalControlReadModel>()).ToList();
            panel.Q<Label>("controls-empty")?.EnableInClassList("is-hidden", controls.Count > 0);
            foreach (var control in controls)
            {
                var item = new VisualElement { name = "control-" + control.Id };
                item.AddToClassList("sd-control-item");
                item.RegisterCallback<PointerDownEvent>(_ => panel.userData = control.Id);
                var state = control.CurrentValue.HasValue
                    ? " · value " + control.CurrentValue.Value.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)
                    : control.IsFiring ? " · Fired" : " · Armed";
                item.Add(new Label(control.Name + " · " + control.Kind + (control.IsBroken ? " · Broken" : string.Empty) + state) { name = "control-label-" + control.Id });
                item.EnableInClassList("is-firing", control.IsFiring);
                item.Add(new Button(() => coordinator?.Submit("control.learn.begin", control.Id, new KeyValuePairValue("logicalControlId", control.Id))) { text = "Learn", name = "control-learn-" + control.Id });
                item.Add(new Button(() => coordinator?.Submit("control.delete", control.Id, new KeyValuePairValue("logicalControlId", control.Id))) { text = "Delete", name = "control-delete-" + control.Id });
                panel.Add(item);
            }
        }

        private static void RebuildMedia(VisualElement panel, IEnumerable<MediaListItemReadModel> entries, PresentationTaskReadModel task = null, PresentationCoordinator coordinator = null)
        {
            if (panel == null) return;
            var itemHost = panel.Q("media-items") ?? panel;
            foreach (var old in itemHost.Query<VisualElement>(className: "sd-media-item").ToList()) old.RemoveFromHierarchy();
            var media = (entries ?? Enumerable.Empty<MediaListItemReadModel>()).ToList();
            var progress = panel.Q<Label>("media-import-progress");
            if (progress != null)
            {
                progress.text = task != null && string.Equals(task.Kind, "ImportBatch", StringComparison.OrdinalIgnoreCase)
                    ? task.Stage + " · " + task.Status + " (" + task.CompletedItems + "/" + task.TotalItems + ")" : media.Count == 0 ? "No media" : media.Count + " media item(s)";
            }
            var confirm = panel.Q<Button>("media-confirm-import");
            if (confirm != null) confirm.SetEnabled(task != null && (task.Stage.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0 || task.Status.IndexOf("Confirm", StringComparison.OrdinalIgnoreCase) >= 0));
            foreach (var item in media)
            {
                var row = new VisualElement { name = "media-item-" + item.Id };
                row.AddToClassList("sd-media-item");
                var displayName = string.IsNullOrEmpty(item.DisplayName) || string.Equals(item.DisplayName, item.Id, StringComparison.Ordinal)
                    ? (string.IsNullOrEmpty(item.RelativePath) ? item.Id : item.RelativePath) : item.DisplayName;
                var label = new Label(displayName + " · " + item.RelativePath + " · " + item.Status + " · refs " + item.ReferenceCount + " · " + item.ByteSize + " bytes") { name = "media-label-" + item.Id };
                row.Add(label);
                row.Add(new Label("Path: " + item.RelativePath + " · " + item.Kind + " · " + item.ColorSpace + " · Alpha " + item.AlphaMode) { name = "media-metadata-" + item.Id });
                row.Add(new Label("XXH3-128: " + (string.IsNullOrEmpty(item.IntegrityHash) ? "Unavailable" : item.IntegrityHash)) { name = "media-hash-" + item.Id });
                row.tooltip = string.IsNullOrEmpty(item.BrokenReason) ? item.Status : "Broken · " + item.BrokenReason;
                row.Add(new Button(() => coordinator?.Submit("media.inspect_references", item.Id, new KeyValuePairValue("mediaAssetId", item.Id))) { text = "Inspect", name = "media-inspect-" + item.Id });
                var candidates = (coordinator?.Current?.Parameters ?? Array.Empty<ParameterReadModel>())
                    .Where(x => !x.IsBroken && (string.Equals(x.ValueType, "Media", StringComparison.OrdinalIgnoreCase) || string.Equals(x.ValueType, "MediaAssetReference", StringComparison.OrdinalIgnoreCase)))
                    .Select(x => x.NodeId + "|" + x.ParameterId).OrderBy(x => x, StringComparer.Ordinal).ToList();
                if (candidates.Count > 0)
                {
                    var replacement = new PopupField<string>("Replace", candidates, 0) { name = "media-rebind-target-" + item.Id };
                    row.Add(replacement);
                    row.Add(new Button(() =>
                    {
                        var parts = (replacement.value ?? string.Empty).Split('|');
                        if (parts.Length == 2) coordinator?.Submit("media.rebind", item.Id, new KeyValuePairValue("mediaAssetId", item.Id), new KeyValuePairValue("nodeId", parts[0]), new KeyValuePairValue("parameterId", parts[1]));
                    }) { text = "Rebind", name = "media-rebind-" + item.Id });
                }
                row.Add(new Button(() => ShowMediaDeleteDialog(panel, item, coordinator)) { text = "Delete", name = "media-delete-" + item.Id });
                if (item.ReferenceCount > 0)
                {
                    row.Add(new Button(() => coordinator?.Submit("media.confirm_delete", item.Id, new KeyValuePairValue("mediaAssetId", item.Id), new KeyValuePairValue("decision", "BreakReferences"))) { text = "Delete + Break References", name = "media-break-delete-" + item.Id });
                }
                itemHost.Add(row);
            }
        }

        private static void ShowMediaDeleteDialog(VisualElement panel, MediaListItemReadModel item, PresentationCoordinator coordinator)
        {
            if (panel == null || item == null) return;
            var old = panel.Q("media-delete-dialog"); old?.RemoveFromHierarchy();
            var dialog = new VisualElement { name = "media-delete-dialog" };
            dialog.AddToClassList("sd-dialog");
            dialog.Add(new Label("Delete " + item.DisplayName + "? References: " + item.ReferenceCount));
            dialog.Add(new Label(item.ReferenceCount > 0 ? "Referenced nodes/presets will become Broken." : "No references."));
            dialog.Add(new Button(() =>
            {
                coordinator?.Submit(item.ReferenceCount > 0 ? "media.confirm_delete" : "media.delete", item.Id,
                    new KeyValuePairValue("mediaAssetId", item.Id), new KeyValuePairValue("decision", item.ReferenceCount > 0 ? "BreakReferences" : "Delete"));
                dialog.RemoveFromHierarchy();
            }) { text = "Confirm Delete", name = "media-delete-confirm" });
            dialog.Add(new Button(() => dialog.RemoveFromHierarchy()) { text = "Cancel", name = "media-delete-cancel" });
            panel.Add(dialog);
        }

        private static void AddDiagnostics(VisualElement parent, PresentationCoordinator coordinator)
        {
            var panel = CreatePanel("diagnostics-panel", "Diagnostics");
            panel.style.flexGrow = 1;
            panel.AddToClassList("sd-diagnostics-panel");
            var tabs = new VisualElement { name = "diagnostics-tabs" };
            tabs.Add(new Button(() => SetDiagnosticsHistoryVisibility(panel, false)) { text = "Current", name = "diagnostics-current-tab" });
            tabs.Add(new Button(() => SetDiagnosticsHistoryVisibility(panel, true)) { text = "History", name = "diagnostics-history-tab" });
            panel.Add(tabs);
            panel.Add(new TextField("Filter") { name = "diagnostics-filter" });
            panel.Add(new PopupField<string>("Severity", new List<string> { "All", "Info", "Warning", "Error" }, 0) { name = "diagnostics-severity-filter" });
            panel.Add(new TextField("Node") { name = "diagnostics-node-filter" });
            panel.Add(new TextField("Diagnostic Code") { name = "diagnostics-code-filter" });
            panel.Add(new Label { name = "presentation-command-notice" });
            panel.Add(new VisualElement { name = "diagnostics-list" });
            panel.Add(new VisualElement { name = "diagnostics-detail-pane" });
            panel.Q<TextField>("diagnostics-filter").RegisterValueChangedCallback(_ => ApplyDiagnosticFilter(panel));
            panel.Q<TextField>("diagnostics-node-filter").RegisterValueChangedCallback(_ => ApplyDiagnosticFilter(panel));
            panel.Q<TextField>("diagnostics-code-filter").RegisterValueChangedCallback(_ => ApplyDiagnosticFilter(panel));
            panel.Q<PopupField<string>>("diagnostics-severity-filter").RegisterValueChangedCallback(_ => ApplyDiagnosticFilter(panel));
            var format = new PopupField<string>("Format", new List<string> { "Text", "JSON" }, 0) { name = "diagnostics-export-format" };
            panel.Add(format);
            var exportStatus = new Label { name = "diagnostics-export-status" };
            panel.Add(exportStatus);
            panel.Add(new Button(() =>
            {
                var platform = coordinator?.PlatformFiles;
                var sessionId = coordinator?.CurrentEnvelope?.ProjectSessionId ?? Guid.Empty;
                if (platform == null) { exportStatus.text = "File picker unavailable"; return; }
                var requestId = Guid.NewGuid();
                exportStatus.text = "Choose export path…";
                platform.PickPath(new PlatformPathRequest(requestId, sessionId, PlatformPathRequestKind.File, "Export diagnostics"), result =>
                {
                    if (sessionId != (coordinator?.CurrentEnvelope?.ProjectSessionId ?? Guid.Empty)) return;
                    if (result == null || !result.Succeeded || result.AbsolutePaths.Count == 0) { exportStatus.text = string.IsNullOrWhiteSpace(result?.Error) ? "Export cancelled" : result.Error; return; }
                    var path = result.AbsolutePaths[0];
                    exportStatus.text = "Exporting…";
                    var historyCount = coordinator?.Current?.Diagnostics?.Count ?? 0;
                    exportStatus.text = "Exporting " + historyCount + " history entries…";
                    var command = coordinator.Submit("diagnostics.export", requestId.ToString("D"),
                        new KeyValuePairValue("path", path), new KeyValuePairValue("json", string.Equals(format.value, "JSON", StringComparison.OrdinalIgnoreCase) ? "true" : "false"),
                        new KeyValuePairValue("count", historyCount.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                        new KeyValuePairValue("scope", "all-history"));
                    exportStatus.text = command.Status == PresentationCommandStatus.Rejected ? "Export failed: " + command.Diagnostic : "Export requested";
                });
            }) { text = "Export", name = "diagnostics-export-button" });
            parent.Add(panel);
        }

        private static void SetDiagnosticsHistoryVisibility(VisualElement panel, bool history)
        {
            if (panel == null) return;
            panel.EnableInClassList("show-history", history);
            var list = panel.Q("diagnostics-list");
            if (list == null) return;
            foreach (var child in list.Children())
            {
                var visible = history ? child.ClassListContains("is-history") : child.ClassListContains("is-current");
                child.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        private static void ApplyDiagnosticFilter(VisualElement panel)
        {
            if (panel == null) return;
            var query = panel.Q<TextField>("diagnostics-filter")?.value ?? string.Empty;
            var node = panel.Q<TextField>("diagnostics-node-filter")?.value ?? string.Empty;
            var code = panel.Q<TextField>("diagnostics-code-filter")?.value ?? string.Empty;
            var severity = panel.Q<PopupField<string>>("diagnostics-severity-filter")?.value ?? "All";
            foreach (var child in panel.Q("diagnostics-list")?.Children() ?? Enumerable.Empty<VisualElement>())
            {
                var row = child as Button;
                var text = row?.text ?? string.Empty;
                var matches = (string.IsNullOrWhiteSpace(query) || text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) &&
                    (string.IsNullOrWhiteSpace(node) || text.IndexOf(node, StringComparison.OrdinalIgnoreCase) >= 0) &&
                    (string.IsNullOrWhiteSpace(code) || text.IndexOf(code, StringComparison.OrdinalIgnoreCase) >= 0) &&
                    (string.Equals(severity, "All", StringComparison.OrdinalIgnoreCase) || text.IndexOf(severity, StringComparison.OrdinalIgnoreCase) >= 0);
                var history = panel.ClassListContains("show-history");
                var tabVisible = history ? child.ClassListContains("is-history") : child.ClassListContains("is-current");
                child.style.display = matches && tabVisible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        internal static void TrackFocus(VisualElement element)
        {
            if (element == null) return;
            element.AddToClassList("sd-focus-target");
            element.RegisterCallback<FocusInEvent>(_ => element.AddToClassList("is-focused"));
            element.RegisterCallback<FocusOutEvent>(_ => element.RemoveFromClassList("is-focused"));
        }
    }

    public sealed class GraphCanvasElement : VisualElement
    {
        private PresentationCoordinator _coordinator;
        private readonly GraphCoordinateMapper _mapper = new GraphCoordinateMapper();
        private readonly GraphSelectionState _selection = new GraphSelectionState();
        private readonly GraphGestureState _gesture = new GraphGestureState();
        private Vector2 _lastPointer;
        private bool _panning;
        private bool _marquee;
        private readonly List<GraphPortElement> _ports = new List<GraphPortElement>();
        private readonly Dictionary<string, GraphConnectionReadModel> _connections = new Dictionary<string, GraphConnectionReadModel>(StringComparer.Ordinal);
        private readonly Dictionary<string, VisualElement> _nodeVisuals = new Dictionary<string, VisualElement>(StringComparer.Ordinal);
        private readonly Dictionary<string, GraphPortElement> _portVisuals = new Dictionary<string, GraphPortElement>(StringComparer.Ordinal);
        private readonly Dictionary<string, Label> _connectionVisuals = new Dictionary<string, Label>(StringComparer.Ordinal);
        private GraphReadModel _graphSnapshot = new GraphReadModel();
        private readonly NodeSearchPopupState _searchState = new NodeSearchPopupState();
        private GraphPortElement _dragSource;
        private GraphPortElement _dragTarget;
        private string _selectedConnectionId;
        private string _dropStatus;
        private bool _gridSnap;
        private VisualElement _minimap;
        private VisualElement _marqueeVisual;
        private Vector2 _marqueeStart;
        public GraphCoordinateMapper Mapper => _mapper;
        public GraphSelectionState Selection => _selection;
        public GraphCanvasElement() : this(null) { }
        public GraphCanvasElement(PresentationCoordinator coordinator)
        {
            _coordinator = coordinator;
            focusable = true;
            tabIndex = 1;
            PresentationUiComposition.TrackFocus(this);
            RegisterCallback<PointerDownEvent>(OnPointerDown);
            RegisterCallback<PointerDownEvent>(OnCapturedPortPointerDown, TrickleDown.TrickleDown);
            RegisterCallback<PointerMoveEvent>(OnPointerMove);
            RegisterCallback<PointerMoveEvent>(OnCapturedPortPointerMove, TrickleDown.TrickleDown);
            RegisterCallback<PointerUpEvent>(OnPointerUp);
            RegisterCallback<PointerUpEvent>(OnCapturedPortPointerUp, TrickleDown.TrickleDown);
            RegisterCallback<PointerDownEvent>(OnCapturedConnectionPointerDown, TrickleDown.TrickleDown);
            RegisterCallback<WheelEvent>(OnWheel);
            RegisterCallback<KeyDownEvent>(OnKeyDown);
            RegisterCallback<ContextualMenuPopulateEvent>(evt => evt.menu.AppendAction("Add Node", _ => ShowNodeSearch(_mapper.ScreenToCanvas(new PresentationPoint(_lastPointer.x, _lastPointer.y)))));
            _minimap = new VisualElement { name = "graph-minimap" };
            _minimap.AddToClassList("sd-graph-minimap");
            Add(_minimap);
        }
        public bool IsGridSnapEnabled => _gridSnap;
        public bool IsMinimapVisible => _minimap != null && _minimap.style.display != DisplayStyle.None;
        public void ToggleGridSnap() { _gridSnap = !_gridSnap; EnableInClassList("is-grid-snap", _gridSnap); }
        public void ToggleMinimap() { if (_minimap != null) _minimap.style.display = IsMinimapVisible ? DisplayStyle.None : DisplayStyle.Flex; }
        public void ShowNodeSearch(PresentationPoint canvasPosition, GraphPortReadModel sourcePort = null)
        {
            var root = this;
            var catalog = (_coordinator?.Current?.NodeCatalog ?? Array.Empty<NodeCatalogItem>()).AsEnumerable();
            if (sourcePort != null)
            {
                var compatibleTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var graph = _graphSnapshot ?? _coordinator?.Current?.Graph;
                foreach (var node in graph?.Nodes ?? Array.Empty<GraphNodeReadModel>())
                {
                    if ((graph?.Ports ?? Array.Empty<GraphPortReadModel>()).Any(port =>
                        string.Equals(port.NodeId, node.Id, StringComparison.Ordinal) &&
                        port.Direction == PresentationPortDirection.Input &&
                        CanConnect(sourcePort, port, out _)))
                        compatibleTypes.Add(node.TypeId);
                }
                catalog = catalog.Where(item => compatibleTypes.Contains(item.TypeId));
            }
            var entries = catalog
                .Select(x => new NodeSearchResult(x.TypeId, x.DisplayName, x.Category, 0, x.IsFavorite, x.IsRecent,
                    !x.IsAvailable || !x.UserAddable,
                    string.IsNullOrEmpty(x.DisabledReason) && !x.UserAddable ? "This node is not user-addable." : x.DisabledReason));
            _searchState.Open(NodeSearch.Fuzzy(string.Empty, entries), canvasPosition);
            var old = this.Query<VisualElement>(name: "graph-node-search-popup").First();
            old?.RemoveFromHierarchy();
            var popup = new VisualElement { name = "graph-node-search-popup" };
            popup.AddToClassList("sd-node-search-popup");
            var search = new TextField("Search") { name = "graph-node-search-field" };
            popup.Add(search);
            var results = new VisualElement { name = "graph-node-search-results" };
            popup.Add(results);
            void rebuild(string query)
            {
                results.Clear();
                var filtered = NodeSearch.Fuzzy(query, entries).Take(24).ToList();
                _searchState.ReplaceEntries(filtered);
                foreach (var result in filtered)
                {
                    var item = result;
                    var button = new Button(() =>
                    {
                        if (item.IsDisabled) { SetDropStatus(item.DisabledReason, true); return; }
                        SubmitNodeAdd(item.NodeTypeId, _searchState.CanvasPosition);
                        popup.RemoveFromHierarchy();
                        _searchState.Close();
                    }) { text = item.DisplayName + " · " + item.Category + (item.IsDisabled ? " · Disabled: " + item.DisabledReason : string.Empty), name = "graph-search-result-" + item.NodeTypeId.ToLowerInvariant() };
                    button.SetEnabled(!item.IsDisabled);
                    button.tooltip = item.IsDisabled ? item.DisabledReason : item.NodeTypeId;
                    results.Add(button);
                }
            }
            search.RegisterValueChangedCallback(evt => rebuild(evt.newValue));
            search.RegisterCallback<KeyDownEvent>(evt =>
            {
                if (evt.keyCode == KeyCode.Escape) { popup.RemoveFromHierarchy(); _searchState.Close(); evt.StopPropagation(); }
                else if (evt.keyCode == KeyCode.DownArrow) { _searchState.MoveSelection(1); evt.StopPropagation(); }
                else if (evt.keyCode == KeyCode.UpArrow) { _searchState.MoveSelection(-1); evt.StopPropagation(); }
                else if (evt.keyCode == KeyCode.Return && _searchState.Current != null) { SubmitNodeAdd(_searchState.Current.NodeTypeId, _searchState.CanvasPosition); popup.RemoveFromHierarchy(); _searchState.Close(); evt.StopPropagation(); }
            });
            Add(popup);
            search.Focus();
            rebuild(string.Empty);
        }
        public void ShowCompatibleNodeSearch(GraphPortReadModel sourcePort, PresentationPoint canvasPosition)
            => ShowNodeSearch(canvasPosition, sourcePort);

        private static bool IsCompatiblePort(GraphPortReadModel sourcePort, GraphPortReadModel targetPort)
        {
            if (sourcePort == null || targetPort == null) return false;
            return string.Equals(targetPort.ValueType, sourcePort.ValueType, StringComparison.OrdinalIgnoreCase) ||
                   (string.Equals(sourcePort.ValueType, "Color", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(targetPort.ValueType, "Vector4", StringComparison.OrdinalIgnoreCase)) ||
                   (string.Equals(sourcePort.ValueType, "Vector4", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(targetPort.ValueType, "Color", StringComparison.OrdinalIgnoreCase));
        }

        private void SubmitNodeAdd(string typeId, PresentationPoint position)
        {
            _coordinator?.Submit("graph.add_node", typeId,
                new KeyValuePairValue("nodeTypeId", typeId),
                new KeyValuePairValue("x", position.X.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new KeyValuePairValue("y", position.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            SetDropStatus("Add node requested at " + position.X.ToString("0") + "," + position.Y.ToString("0"), false);
        }
        public void AddNodeAt(string typeId, PresentationPoint position) => SubmitNodeAdd(typeId, position);
        public void SetCoordinator(PresentationCoordinator coordinator) { _coordinator = coordinator; }
        /// <summary>Applies runtime-only node state without disturbing node
        /// selection, pointer capture, inline drafts, ports or connections.
        /// A caller must use SetGraph when persisted topology differs.</summary>
        public bool TryUpdateGraphState(GraphReadModel graph)
        {
            // Application retains the immutable graph slice while topology and
            // runtime status are unchanged. Avoid even enumerating nodes, ports
            // or connections for that common presentation-frame path.
            if (ReferenceEquals(_graphSnapshot, graph)) return true;
            var snapshot = graph ?? new GraphReadModel();
            if (_graphSnapshot == null || _graphSnapshot.Nodes.Count != snapshot.Nodes.Count || _graphSnapshot.Ports.Count != snapshot.Ports.Count || _graphSnapshot.Connections.Count != snapshot.Connections.Count)
                return false;
            foreach (var node in snapshot.Nodes)
            {
                var prior = _graphSnapshot.Nodes.FirstOrDefault(item => item.Id == node.Id);
                if (prior == null || prior.X != node.X || prior.Y != node.Y || !string.Equals(prior.DisplayName, node.DisplayName, StringComparison.Ordinal)) return false;
                var visual = this.Q<Button>("node-" + node.Id);
                if (visual == null) return false;
                foreach (PresentationNodeStatus status in Enum.GetValues(typeof(PresentationNodeStatus)))
                    visual.RemoveFromClassList("status-" + status.ToString().ToLowerInvariant());
                visual.AddToClassList("status-" + node.Status.ToString().ToLowerInvariant());
                visual.EnableInClassList("is-pending", node.IsPending);
                visual.text = node.DisplayName + " [" + node.Status + "]";
                visual.tooltip = "Node " + node.DisplayName + " · Status: " + node.Status;
                var state = visual.Q<Label>("node-" + node.Id + "-status");
                if (state != null) state.text = "Status: " + node.Status;
            }
            foreach (var connection in snapshot.Connections)
                if (!_graphSnapshot.Connections.Any(item => item.Id == connection.Id && item.FromNodeId == connection.FromNodeId && item.FromPortId == connection.FromPortId && item.ToNodeId == connection.ToNodeId && item.ToPortId == connection.ToPortId)) return false;
            foreach (var port in snapshot.Ports)
                if (!_graphSnapshot.Ports.Any(item => item.NodeId == port.NodeId && item.PortId == port.PortId && item.Direction == port.Direction && item.Requirement == port.Requirement && string.Equals(item.ValueType, port.ValueType, StringComparison.Ordinal))) return false;
            _graphSnapshot = snapshot;
            return true;
        }
        public void SetGraph(GraphReadModel graph)
        {
            var snapshot = graph ?? new GraphReadModel();
            _graphSnapshot = snapshot;
            var wantedNodes = new HashSet<string>(snapshot.Nodes.Select(node => node.Id), StringComparer.Ordinal);
            foreach (var stale in _nodeVisuals.Keys.Where(id => !wantedNodes.Contains(id)).ToList()) { _nodeVisuals[stale].RemoveFromHierarchy(); _nodeVisuals.Remove(stale); }
            foreach (var node in snapshot.Nodes)
            {
                if (!_nodeVisuals.TryGetValue(node.Id, out var visual)) visual = AddNode(node.Id, new PresentationPoint(node.X, node.Y), node.DisplayName, node.Status, node.Parameters, node.IsPending, node.StatusReason);
                ApplyNodeState(visual as Button, node);
            }
            var wantedPorts = new HashSet<string>(snapshot.Ports.Select(port => port.NodeId + ":" + port.PortId), StringComparer.Ordinal);
            foreach (var stale in _portVisuals.Keys.Where(id => !wantedPorts.Contains(id)).ToList()) { _ports.Remove(_portVisuals[stale]); _portVisuals[stale].RemoveFromHierarchy(); _portVisuals.Remove(stale); }
            foreach (var port in snapshot.Ports)
            {
                var key = port.NodeId + ":" + port.PortId;
                if (!_portVisuals.ContainsKey(key) && _nodeVisuals.TryGetValue(port.NodeId, out var node)) AddPort(node, port);
            }
            var wantedConnections = new HashSet<string>(snapshot.Connections.Select(connection => connection.Id), StringComparer.Ordinal);
            foreach (var stale in _connectionVisuals.Keys.Where(id => !wantedConnections.Contains(id)).ToList()) { _connectionVisuals[stale].RemoveFromHierarchy(); _connectionVisuals.Remove(stale); _connections.Remove(stale); }
            foreach (var connection in snapshot.Connections)
            {
                if (!_connections.TryGetValue(connection.Id, out var prior) || !SameConnection(prior, connection))
                {
                    if (_connectionVisuals.TryGetValue(connection.Id, out var old)) { old.RemoveFromHierarchy(); _connectionVisuals.Remove(connection.Id); }
                    AddConnection(connection);
                }
            }
            var status = this.Q<Label>("graph-drop-status");
            if (status == null)
            {
                status = new Label { name = "graph-drop-status" };
                status.AddToClassList("sd-graph-drop-status");
                Add(status);
            }
            status.text = _dropStatus ?? string.Empty;
            var count = this.Q<Label>("graph-connection-count");
            if (count == null)
            {
                count = new Label { name = "graph-connection-count" };
                count.AddToClassList("sd-graph-connection-count");
                Add(count);
            }
            count.text = snapshot.Connections.Count.ToString(System.Globalization.CultureInfo.InvariantCulture) + " / 4096 connections";
            count.EnableInClassList("is-at-capacity", snapshot.Connections.Count >= 4096);
            foreach (var oldBundle in this.Query<Label>(className: "sd-fanout-bundle").ToList()) oldBundle.RemoveFromHierarchy();
            foreach (var group in snapshot.Connections.GroupBy(x => x.FromNodeId + ":" + x.FromPortId).Where(x => x.Count() > 1))
            {
                var bundle = new Label("Fan-out ×" + group.Count().ToString(System.Globalization.CultureInfo.InvariantCulture))
                {
                    name = "graph-fanout-" + group.Key.Replace(":", "-")
                };
                bundle.AddToClassList("sd-fanout-bundle");
                Add(bundle);
            }
        }
        public VisualElement AddNode(string id, PresentationPoint position, string title = null, PresentationNodeStatus status = PresentationNodeStatus.Ready,
            IEnumerable<ParameterReadModel> parameters = null, bool pending = false, string statusReason = null)
        {
            var node = new Button(() => { _selection.Replace(new[] { id }, id); Focus(); }) { name = "node-" + id, text = (title ?? id) + " [" + status + "]" };
            node.AddToClassList("sd-node");
            _nodeVisuals[id] = node;
            node.AddToClassList("status-" + status.ToString().ToLowerInvariant());
            node.EnableInClassList("is-pending", pending);
            node.tooltip = "Node " + (title ?? id) + " · Status: " + status;
            var header = new Label(title ?? id) { name = "node-" + id + "-header" };
            header.AddToClassList("sd-node-header");
            var state = new Label("Status: " + status) { name = "node-" + id + "-status" };
            state.AddToClassList("sd-node-status");
            state.tooltip = "Open Diagnostics for " + id;
            state.RegisterCallback<PointerDownEvent>(_ => _coordinator?.Submit("diagnostics.filter.node", id,
                new KeyValuePairValue("nodeId", id)));
            var collapse = new Button(() =>
            {
                var collapsed = node.ClassListContains("is-collapsed");
                node.EnableInClassList("is-collapsed", !collapsed);
                // Ports remain visible in the collapsed header as required by
                // NodeGraph.md; only the inline parameter body is folded.
                foreach (var inline in node.Query(className: "sd-node-inline-parameter").ToList()) inline.style.display = collapsed ? DisplayStyle.None : DisplayStyle.Flex;
            }) { text = "▾", name = "node-" + id + "-collapse" };
            collapse.tooltip = "Collapse or expand node ports";
            node.Add(header);
            node.Add(state);
            node.Add(collapse);
            foreach (var parameter in (parameters ?? Enumerable.Empty<ParameterReadModel>()).Take(4))
            {
                var inline = new Label(parameter.DisplayName + ": " + parameter.EffectiveValue) { name = "node-" + id + "-parameter-" + parameter.ParameterId };
                inline.AddToClassList("sd-node-inline-parameter");
                inline.tooltip = parameter.IsBroken ? parameter.Error : parameter.ParameterId;
                node.Add(inline);
            }
            if (pending)
            {
                var pendingLabel = new Label("Pending" + (string.IsNullOrEmpty(statusReason) ? string.Empty : " · " + statusReason)) { name = "node-" + id + "-pending" };
                pendingLabel.AddToClassList("sd-node-pending");
                node.Add(pendingLabel);
            }
            if (status == PresentationNodeStatus.UnknownNode)
            {
                node.Add(new Label("UnknownNode · " + (string.IsNullOrEmpty(statusReason) ? "evaluation unavailable" : statusReason)) { name = "node-" + id + "-unknown-reason" });
                node.Add(new Button(() => _coordinator?.Submit("graph.restore_unknown", id,
                    new KeyValuePairValue("nodeId", id))) { text = "Try Restore", name = "node-" + id + "-try-restore" });
            }
            node.style.width = 220;
            node.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 0 && evt.clickCount >= 2 && _coordinator != null)
                {
                    _coordinator.Submit("preview.open", id, new KeyValuePairValue("previewId", id), new KeyValuePairValue("nodeId", id));
                    evt.StopPropagation();
                }
            });
            node.style.position = Position.Absolute;
            node.style.left = position.X;
            node.style.top = position.Y;
            Add(node);
            return node;
        }

        private void AddPort(VisualElement node, GraphPortReadModel port)
        {
            if (node == null || port == null) return;
            var direction = port.Direction == PresentationPortDirection.Input ? "In" : "Out";
            var requirement = port.Requirement == PresentationPortRequirement.Required ? "Required" : "Optional";
            var visual = new GraphPortElement(this, port);
            visual.name = "port-" + port.NodeId + "-" + port.PortId;
            visual.text = direction + " · " + requirement + " · " + port.DisplayName + " : " + port.ValueType;
            visual.AddToClassList("sd-port");
            visual.AddToClassList(port.Direction == PresentationPortDirection.Input ? "is-input" : "is-output");
            visual.AddToClassList(port.Requirement == PresentationPortRequirement.Required ? "is-required" : "is-optional");
            visual.AddToClassList("port-type-" + (port.ValueType ?? string.Empty).ToLowerInvariant().Replace(" ", "-"));
            visual.style.minHeight = 18;
            visual.style.minWidth = 100;
            visual.tooltip = (port.IsConnected ? "Connected · drag an output here to replace" : "Drag from an output to connect") +
                " · " + requirement + " · type " + port.ValueType;
            node.Add(visual);
            _ports.Add(visual);
            _portVisuals[port.NodeId + ":" + port.PortId] = visual;
        }

        private void AddConnection(GraphConnectionReadModel connection)
        {
            if (connection == null) return;
            _connections[connection.Id] = connection;
            var text = connection.FromNodeId + ":" + connection.FromPortId + " → " + connection.ToNodeId + ":" + connection.ToPortId;
            if (connection.IsImplicitConversion) text += " · Conversion: " + connection.ConversionLabel;
            var label = new Label(text) { name = "connection-" + connection.Id, userData = connection.Id };
            label.AddToClassList("sd-connection");
            _connectionVisuals[connection.Id] = label;
            if (connection.IsImplicitConversion)
            {
                label.AddToClassList("sd-connection-dashed");
                label.AddToClassList("sd-connection-implicit");
                label.Add(new Label("Conversion: " + connection.ConversionLabel) { name = "connection-" + connection.Id + "-conversion-badge" });
            }
            label.tooltip = "Right-click or select and press Delete to disconnect" +
                (connection.IsImplicitConversion ? " · Conversion ID: " + connection.ConversionLabel : string.Empty);
            label.RegisterCallback<PointerDownEvent>(evt =>
            {
                if (evt.button == 1)
                {
                    _selectedConnectionId = connection.Id;
                    DisconnectSelectedConnection();
                    evt.StopPropagation();
                }
                // PointerDownEvent.GetPooled() (used by both the runtime input
                // bridge and the headless PlayMode harness) can carry the
                // sentinel button value -1 when no OS button is attached.
                // Treat that as the primary selection gesture as well; a
                // connection must be selectable before Delete/Backspace can
                // route the typed disconnect command through the coordinator.
                else if (evt.button == 0 || evt.button < 0)
                {
                    _selectedConnectionId = connection.Id;
                    Focus();
                }
            });
            label.RegisterCallback<ContextualMenuPopulateEvent>(evt =>
            {
                _selectedConnectionId = connection.Id;
                evt.menu.AppendAction("Disconnect", _ => DisconnectSelectedConnection());
            });
            Add(label);
        }

        private static bool SameConnection(GraphConnectionReadModel left, GraphConnectionReadModel right)
        {
            return left != null && right != null && left.FromNodeId == right.FromNodeId && left.FromPortId == right.FromPortId && left.ToNodeId == right.ToNodeId && left.ToPortId == right.ToPortId && left.IsImplicitConversion == right.IsImplicitConversion && left.ConversionLabel == right.ConversionLabel;
        }

        private static void ApplyNodeState(Button visual, GraphNodeReadModel node)
        {
            if (visual == null || node == null) return;
            foreach (PresentationNodeStatus status in Enum.GetValues(typeof(PresentationNodeStatus)))
                visual.EnableInClassList("status-" + status.ToString().ToLowerInvariant(), status == node.Status);
            visual.EnableInClassList("is-pending", node.IsPending);
            var text = node.DisplayName + " [" + node.Status + "]";
            if (!string.Equals(visual.text, text, StringComparison.Ordinal)) visual.text = text;
            var tooltip = "Node " + node.DisplayName + " · Status: " + node.Status;
            if (!string.Equals(visual.tooltip, tooltip, StringComparison.Ordinal)) visual.tooltip = tooltip;
            var state = visual.Q<Label>("node-" + node.Id + "-status");
            if (state != null) { var stateText = "Status: " + node.Status; if (!string.Equals(state.text, stateText, StringComparison.Ordinal)) state.text = stateText; }
        }

        private void DisconnectSelectedConnection()
        {
            if (_coordinator == null || string.IsNullOrEmpty(_selectedConnectionId)) return;
            _coordinator.Submit("graph.disconnect", _selectedConnectionId, new KeyValuePairValue("connectionId", _selectedConnectionId));
            SetDropStatus("Disconnect requested · " + _selectedConnectionId, false);
            _selectedConnectionId = null;
        }

        private void BeginPortDrag(GraphPortElement source, PointerDownEvent evt)
        {
            if (source == null || source.Endpoint.Direction != PresentationPortDirection.Output) return;
            _dragSource = source;
            _dragTarget = null;
            source.AddToClassList("is-drag-source");
            // Keep the source in the real pointer-capture path for the whole
            // gesture. The candidate is resolved from the event position, so
            // capture does not prevent an input port under the pointer from
            // becoming the typed drop target, and release is deterministic if
            // the pointer leaves the port before MouseUp.
            source.CapturePointer(evt.pointerId);
            SetDropStatus("Connecting from " + source.Endpoint.DisplayName + "…", false);
            evt.StopPropagation();
        }

        private void UpdatePortDrag(PointerMoveEvent evt)
        {
            if (_dragSource == null) return;
            var eventPort = evt.target as GraphPortElement ?? evt.currentTarget as GraphPortElement;
            var candidate = eventPort != null && eventPort != _dragSource && eventPort.Endpoint.Direction == PresentationPortDirection.Input
                ? eventPort
                : _ports.FirstOrDefault(port => port != _dragSource && port.Endpoint.Direction == PresentationPortDirection.Input && port.worldBound.Contains(evt.position));
            // Pointer capture delivers move events back to the source port.
            // A synthetic Navigation/Pointer event can also have the default
            // zero position; in that case retain deterministic candidate
            // discovery for the only input port, while real pointer motion
            // continues to use world bounds above.
            if (candidate == null && eventPort == _dragSource && evt.position.x == 0f && evt.position.y == 0f)
                candidate = _ports.FirstOrDefault(port => port != _dragSource && port.Endpoint.Direction == PresentationPortDirection.Input);
            if (_dragTarget != null && _dragTarget != candidate) _dragTarget.EnableInClassList("is-drop-target", false);
            _dragTarget = candidate;
            if (_dragTarget != null)
            {
                var compatible = CanConnect(_dragSource.Endpoint, _dragTarget.Endpoint, out var reason);
                _dragTarget.EnableInClassList("is-drop-target", compatible);
                _dragTarget.EnableInClassList("is-incompatible", !compatible);
                SetDropStatus(compatible ? "Release to connect" : reason, false);
            }
            else SetDropStatus("Release on an input port", false);
            evt.StopPropagation();
        }

        private void EndPortDrag(PointerUpEvent evt)
        {
            if (_dragSource == null) return;
            var source = _dragSource;
            var target = _dragTarget;
            // A directly dispatched PointerUp can bypass the captured source
            // move event.  If the event itself landed on an input port, use
            // that port as the drop target so the same typed validation and
            // command path is exercised as a real pointer release.
            var releasedPort = evt.target as GraphPortElement ?? evt.currentTarget as GraphPortElement;
            if (target == null && releasedPort != null && releasedPort != source && releasedPort.Endpoint.Direction == PresentationPortDirection.Input)
                target = releasedPort;
            if (source.HasPointerCapture(evt.pointerId)) source.ReleasePointer(evt.pointerId);
            source.EnableInClassList("is-drag-source", false);
            if (target != null) target.EnableInClassList("is-drop-target", false);
            if (target == null)
            {
                var pointer = new PresentationPoint(evt.position.x, evt.position.y);
                ShowCompatibleNodeSearch(source.Endpoint, _mapper.ScreenToCanvas(pointer));
                SetDropStatus("Choose a compatible node to continue the connection", false);
            }
            else if (CanConnect(source.Endpoint, target.Endpoint, out var reason)) SubmitConnection(source.Endpoint, target.Endpoint);
            else SetDropStatus(reason, true);
            _dragSource = null;
            _dragTarget = null;
            evt.StopPropagation();
        }

        private void SubmitConnection(GraphPortReadModel source, GraphPortReadModel target)
        {
            if (_coordinator == null) { SetDropStatus("No Application command port is bound", true); return; }
            var existing = _connections.Values.FirstOrDefault(x => string.Equals(x.ToNodeId, target.NodeId, StringComparison.Ordinal) && string.Equals(x.ToPortId, target.PortId, StringComparison.Ordinal));
            var replace = existing != null;
            var command = replace ? "graph.replace_input_connection" : "graph.connect";
            var targetId = replace ? existing.Id : Guid.NewGuid().ToString("D");
            _coordinator.Submit(command, targetId,
                new KeyValuePairValue("connectionId", targetId),
                new KeyValuePairValue("sourceNodeId", source.NodeId),
                new KeyValuePairValue("sourcePortId", source.PortId),
                new KeyValuePairValue("destinationNodeId", target.NodeId),
                new KeyValuePairValue("destinationPortId", target.PortId));
            SetDropStatus(replace ? "Input replacement requested" : "Connection requested", false);
        }

        private static bool CanConnect(GraphPortReadModel source, GraphPortReadModel target, out string reason)
        {
            if (source == null || target == null) { reason = "Both source and destination ports are required"; return false; }
            if (source.Direction != PresentationPortDirection.Output || target.Direction != PresentationPortDirection.Input) { reason = "Connections must run output → input"; return false; }
            if (string.Equals(source.ValueType, target.ValueType, StringComparison.OrdinalIgnoreCase)) { reason = string.Empty; return true; }
            if ((string.Equals(source.ValueType, "Color", StringComparison.OrdinalIgnoreCase) && string.Equals(target.ValueType, "Vector4", StringComparison.OrdinalIgnoreCase)) ||
                (string.Equals(source.ValueType, "Vector4", StringComparison.OrdinalIgnoreCase) && string.Equals(target.ValueType, "Color", StringComparison.OrdinalIgnoreCase))) { reason = "Implicit conversion available"; return true; }
            reason = "Incompatible types: " + source.ValueType + " → " + target.ValueType;
            return false;
        }

        private void SetDropStatus(string status, bool error)
        {
            _dropStatus = status ?? string.Empty;
            var label = this.Q<Label>("graph-drop-status");
            if (label != null)
            {
                label.text = _dropStatus;
                label.EnableInClassList("is-error", error);
            }
        }
        public void RemoveSelectedNodes()
        {
            var ids = _selection.Selected.ToList();
            if (_coordinator == null) return;
            foreach (var id in ids)
            {
                if (string.Equals(_coordinator.Current?.Graph?.Nodes.FirstOrDefault(x => x.Id == id)?.TypeId, "system.program_output", StringComparison.Ordinal))
                {
                    SetDropStatus("ProgramOutput cannot be deleted", true);
                    continue;
                }
                _coordinator.Submit("graph.delete_node", id);
            }
            _selection.Clear();
        }
        private void RequestAddNode(PresentationPoint position)
        {
            ShowNodeSearch(position);
        }
        private void OnPointerDown(PointerDownEvent evt)
        {
            Focus();
            _lastPointer = new Vector2(evt.position.x, evt.position.y);
            if (evt.button == 1) { ShowNodeSearch(_mapper.ScreenToCanvas(new PresentationPoint(_lastPointer.x, _lastPointer.y))); evt.StopPropagation(); return; }
            _panning = evt.button == 2 || evt.altKey;
            _marquee = !_panning && evt.button == 0 && (evt.target == this || evt.target == _minimap);
            if (_marquee)
            {
                _marqueeStart = evt.position;
                _gesture.BeginMarquee(new PresentationPoint(evt.position.x, evt.position.y));
                if (_marqueeVisual == null)
                {
                    _marqueeVisual = new VisualElement { name = "graph-marquee" };
                    _marqueeVisual.AddToClassList("sd-graph-marquee");
                    Add(_marqueeVisual);
                }
                _marqueeVisual.style.left = evt.position.x;
                _marqueeVisual.style.top = evt.position.y;
                _marqueeVisual.style.width = 0;
                _marqueeVisual.style.height = 0;
            }
        }

        private void OnCapturedPortPointerDown(PointerDownEvent evt)
        {
            if (_dragSource == null && evt.target is GraphPortElement port) BeginPortDrag(port, evt);
        }

        private void OnCapturedPortPointerMove(PointerMoveEvent evt)
        {
            if (_dragSource != null) UpdatePortDrag(evt);
        }

        private void OnCapturedPortPointerUp(PointerUpEvent evt)
        {
            if (_dragSource != null) EndPortDrag(evt);
        }
        private void OnCapturedConnectionPointerDown(PointerDownEvent evt)
        {
            var element = evt.target as VisualElement;
            if (element == null || !element.ClassListContains("sd-connection")) return;
            _selectedConnectionId = element.userData as string;
            Focus();
            evt.StopPropagation();
        }
        private void OnPointerMove(PointerMoveEvent evt)
        {
            if (_dragSource != null) UpdatePortDrag(evt);
            if (_panning)
            {
                var delta = new Vector2(evt.position.x - _lastPointer.x, evt.position.y - _lastPointer.y);
                _mapper.PanBy(new PresentationPoint(delta.x, delta.y));
                _lastPointer = new Vector2(evt.position.x, evt.position.y);
            }
            if (_marquee)
            {
                _gesture.UpdateMarquee(new PresentationPoint(evt.position.x, evt.position.y));
                var x = Math.Min(_marqueeStart.x, evt.position.x);
                var y = Math.Min(_marqueeStart.y, evt.position.y);
                _marqueeVisual.style.left = x;
                _marqueeVisual.style.top = y;
                _marqueeVisual.style.width = Math.Abs(evt.position.x - _marqueeStart.x);
                _marqueeVisual.style.height = Math.Abs(evt.position.y - _marqueeStart.y);
            }
        }
        private void OnPointerUp(PointerUpEvent evt)
        {
            if (_dragSource != null)
            {
                EndPortDrag(evt);
                return;
            }
            if (_marquee)
            {
                var rect = _gesture.Marquee;
                var selected = (_coordinator?.Current?.Graph?.Nodes ?? Array.Empty<GraphNodeReadModel>())
                    .Where(node => new PresentationRect(node.X, node.Y, 220, 80).Overlaps(rect)).Select(node => node.Id);
                _selection.Replace(selected);
                _gesture.Cancel();
                if (_marqueeVisual != null) { _marqueeVisual.RemoveFromHierarchy(); _marqueeVisual = null; }
            }
            _panning = false;
            _marquee = false;
        }
        private void OnWheel(WheelEvent evt) { _mapper.ZoomAt(_mapper.Zoom * (evt.delta.y < 0 ? 1.1f : .9f), new PresentationPoint(evt.mousePosition.x, evt.mousePosition.y)); }
        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.ctrlKey && evt.keyCode == KeyCode.Z)
            {
                _coordinator?.Submit("project.undo");
                evt.StopPropagation();
            }
            else if (evt.ctrlKey && evt.keyCode == KeyCode.Y)
            {
                _coordinator?.Submit("project.redo");
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Delete)
            {
                if (!string.IsNullOrEmpty(_selectedConnectionId)) DisconnectSelectedConnection();
                else RemoveSelectedNodes();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Tab)
            {
                ShowNodeSearch(_mapper.ScreenToCanvas(new PresentationPoint(_lastPointer.x, _lastPointer.y)));
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.A && evt.ctrlKey)
            {
                _selection.SelectAll(_coordinator?.Current?.Graph?.Nodes.Select(x => x.Id));
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Escape)
            {
                _selection.Clear();
                _searchState.Close();
                this.Query<VisualElement>(name: "graph-node-search-popup").First()?.RemoveFromHierarchy();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.M)
            {
                ToggleMinimap();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.G)
            {
                ToggleGridSnap();
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.C && evt.ctrlKey)
            {
                _coordinator?.Submit("graph.copy", string.Join(",", _selection.Selected));
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.V && evt.ctrlKey)
            {
                _coordinator?.Submit("graph.paste", string.Empty, new KeyValuePairValue("x", _lastPointer.x.ToString(System.Globalization.CultureInfo.InvariantCulture)), new KeyValuePairValue("y", _lastPointer.y.ToString(System.Globalization.CultureInfo.InvariantCulture)));
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.D && evt.ctrlKey)
            {
                _coordinator?.Submit("graph.duplicate", string.Join(",", _selection.Selected));
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.F)
            {
                _coordinator?.Submit("graph.focus_selection", string.Join(",", _selection.Selected));
                evt.StopPropagation();
            }
            else if (evt.keyCode == KeyCode.Home)
            {
                _coordinator?.Submit("graph.focus_all");
                evt.StopPropagation();
            }
        }

        private sealed class GraphPortElement : Label
        {
            private readonly GraphCanvasElement _owner;
            public GraphPortReadModel Endpoint { get; }

            public GraphPortElement(GraphCanvasElement owner, GraphPortReadModel endpoint)
            {
                _owner = owner;
                Endpoint = endpoint;
                focusable = true;
                RegisterCallback<PointerDownEvent>(evt => _owner.BeginPortDrag(this, evt));
                RegisterCallback<PointerMoveEvent>(_owner.UpdatePortDrag);
                RegisterCallback<PointerUpEvent>(_owner.EndPortDrag);
            }
        }
    }
}
#endif
