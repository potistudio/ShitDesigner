using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace ShitDesigner.Presentation.Tests.PlayMode {
	/// <summary>
	/// Runtime UI Toolkit acceptance lane.  The model tests cover pure state
	/// rules; this fixture drives the actual visual tree and its controls so
	/// a passing model test cannot masquerade as a working panel.
	/// </summary>
	public sealed class PresentationUiAcceptanceTests {
		private static readonly MethodInfo ClickableSimulateSingleClick = typeof(Clickable).GetMethod(
			"SimulateSingleClick", BindingFlags.Instance | BindingFlags.NonPublic, null,
			new[] { typeof(EventBase), typeof(int) }, null);
		private readonly List<GameObject> _objects = new List<GameObject>();
		private readonly List<PanelSettings> _panels = new List<PanelSettings>();
		private readonly List<RenderTexture> _textures = new List<RenderTexture>();

		[TearDown]
		public void TearDownRuntimeHost() {
			foreach (var panel in _panels) if (panel != null) UnityEngine.Object.DestroyImmediate(panel);
			foreach (var gameObject in _objects) if (gameObject != null) UnityEngine.Object.DestroyImmediate(gameObject);
			foreach (var texture in _textures) if (texture != null) UnityEngine.Object.DestroyImmediate(texture);
			_panels.Clear();
			_objects.Clear();
			_textures.Clear();
		}
		[UnityTest, Category("GUI_01_Workspace_Viewport")] public IEnumerator WorkspaceViewport() { var h = Host(); yield return null; Assert.That(h.worldBound.width, Is.GreaterThanOrEqualTo(1280)); Assert.That(h.worldBound.height, Is.GreaterThanOrEqualTo(720)); var panels = new[] { h.Q("node-library"), h.Q("node-graph-panel"), h.Q("inspector-panel"), h.Q("dashboard-panel"), h.Q("outputs-row") }.Where(x => x != null && x.resolvedStyle.display != DisplayStyle.None).ToList(); Assert.That(panels.All(x => x.worldBound.width > 0 && x.worldBound.height > 0), Is.True); for (var i = 0; i < panels.Count; i++) for (var j = i + 1; j < panels.Count; j++) Assert.That(panels[i].worldBound.Overlaps(panels[j].worldBound), Is.False, panels[i].name + " overlaps " + panels[j].name); }
		[UnityTest, Category("GUI_02_Workspace_DockEdit")] public IEnumerator DockEdit() { var h = Host(); yield return null; var graph = h.Q("node-graph-panel"); var before = graph.worldBound.width; Click(h.Q<Button>("dock-split-horizontal")); yield return null; var after = graph.worldBound.width; Assert.That(h.Q<Label>("layout-dirty-state").text, Does.Contain("Horizontal")); Assert.That(after, Is.Not.EqualTo(before)); Assert.That(after, Is.GreaterThan(0)); Click(h.Q<Button>("dock-resize")); yield return null; Assert.That(graph.worldBound.width, Is.Not.EqualTo(after)); }
		[UnityTest, Category("GUI_03_Workspace_NoExternalWindow")] public IEnumerator NoExternalWindow() { var h = Host(); yield return null; var documentRoot = h; var canvas = h.Q<GraphCanvasElement>("node-graph-canvas"); canvas.SendEvent(PointerDownEvent.GetPooled()); h.SendEvent(PointerUpEvent.GetPooled()); yield return null; Assert.That(h, Is.SameAs(documentRoot)); Assert.That(h.Q("external-window"), Is.Null); }
		[UnityTest, Category("GUI_04_Workspace_DirtySeparation")] public IEnumerator DirtySeparation() { var h = Host(); Click(h.Q<Button>("dock-tabify")); yield return null; Assert.That(h.Q<Label>("layout-dirty-state").text, Does.Contain("Layout Dirty")); Assert.That(h.Q("project-dirty-state"), Is.Null); }
		[UnityTest, Category("GUI_05_Workspace_DiscardCandidate")] public IEnumerator DiscardCandidate() { var h = Host(); Click(h.Q<Button>("dock-close-panel")); Assert.That(h.Q("inspector-panel").style.display.value, Is.EqualTo(DisplayStyle.None)); Click(h.Q<Button>("dock-reopen-panel")); yield return null; Assert.That(h.Q<Label>("layout-dirty-state").text, Does.Contain("Reopened")); Assert.That(h.Q("inspector-panel").style.display.value, Is.EqualTo(DisplayStyle.Flex)); }
		[UnityTest, Category("GUI_06_Workspace_Presets")] public IEnumerator LayoutPresets() { var h = HostWithCatalog(out var commands); Click(h.Q<Button>("layout-preset-create")); Click(h.Q<Button>("layout-preset-overwrite")); Click(h.Q<Button>("layout-preset-rename")); Click(h.Q<Button>("layout-preset-duplicate")); Click(h.Q<Button>("layout-preset-delete")); Click(h.Q<Button>("layout-preset-delete-confirm")); yield return null; Assert.That(h.Q("dock-layout-controls").childCount, Is.GreaterThanOrEqualTo(11)); Assert.That(commands.Count, Is.EqualTo(5)); Assert.That(commands.Select(x => x.Payload["operation"]), Is.EquivalentTo(new[] { "create", "overwrite", "rename", "duplicate", "delete" })); }
		[UnityTest, Category("GUI_07_Workspace_EditPermission")] public IEnumerator LayoutDoesNotOwnGraph() { var h = Host(); var graph = h.Q("node-graph-panel"); Click(h.Q<Button>("dock-tabify")); yield return null; Assert.That(h.Q("node-graph-panel"), Is.SameAs(graph)); Assert.That(graph.ClassListContains("is-active-tab"), Is.True); }
		[UnityTest, Category("GUI_08_Graph_SearchEntry")] public IEnumerator GraphSearchEntry() { var h = HostWithCatalog(out _); h.Q<TextField>("node-search").value = "not-a-node"; yield return null; Assert.That(h.Q<Button>("node-library-fx.blur").resolvedStyle.display, Is.EqualTo(DisplayStyle.None)); }
		[UnityTest, Category("GUI_09_Graph_LibraryAdd")] public IEnumerator GraphLibraryAdd() { var h = HostWithCatalog(out var commands); Click(h.Q<Button>("node-library-fx.blur")); yield return null; Assert.That(commands.Count, Is.EqualTo(1)); Assert.That(commands[0].CommandId, Is.EqualTo("graph.add_node")); Assert.That(commands[0].Payload["nodeTypeId"], Is.EqualTo("fx.blur")); }
		[UnityTest, Category("GUI_10_Graph_UndoRedoBatch")] public IEnumerator GraphCommandSurface() { var h = HostWithCatalog(out var commands); var canvas = h.Q<GraphCanvasElement>("node-graph-canvas"); canvas.AddNode("delete-me", new PresentationPoint(10, 10), "Delete Me"); canvas.Selection.Replace(new[] { "delete-me" }, "delete-me"); canvas.RemoveSelectedNodes(); canvas.SetGraph(new GraphReadModel(nodes: new[] { new GraphNodeReadModel("a", "a", "A", 0, 0), new GraphNodeReadModel("b", "b", "B", 120, 0) }, ports: new[] { new GraphPortReadModel("a", "out", "Out", "Color", PresentationPortDirection.Output, PresentationPortRequirement.Optional), new GraphPortReadModel("b", "in", "In", "Vector4", PresentationPortDirection.Input, PresentationPortRequirement.Required) })); yield return null; SendPointer(canvas.Q("port-a-out"), "down"); SendPointer(canvas.Q("port-b-in"), "move"); SendPointer(canvas.Q("port-b-in"), "up"); yield return null; Assert.That(commands.Any(x => x.CommandId == "graph.delete_node"), Is.True); Assert.That(commands.Any(x => x.CommandId == "graph.connect"), Is.True); }
		[UnityTest, Category("GUI_11_Graph_ConnectionReject")] public IEnumerator GraphConnectionRejectSurface() { var h = HostWithCatalog(out _); var canvas = h.Q<GraphCanvasElement>("node-graph-canvas"); canvas.SetGraph(new GraphReadModel(nodes: new[] { new GraphNodeReadModel("a", "a", "A", 0, 0), new GraphNodeReadModel("b", "b", "B", 120, 0) }, ports: new[] { new GraphPortReadModel("a", "out", "Out", "Float", PresentationPortDirection.Output, PresentationPortRequirement.Required), new GraphPortReadModel("b", "in", "In", "Texture", PresentationPortDirection.Input, PresentationPortRequirement.Required) })); yield return null; Assert.That(h.Q<Label>("port-b-in").tooltip, Does.Contain("Drag")); Assert.That(h.Q("graph-drop-status"), Is.Not.Null); }
		[UnityTest, Category("GUI_12_Graph_ImplicitConversion")] public IEnumerator GraphImplicitConversionSurface() { var h = HostWithCatalog(out _); var canvas = h.Q<GraphCanvasElement>("node-graph-canvas"); canvas.SetGraph(new GraphReadModel(nodes: new[] { new GraphNodeReadModel("a", "a", "A", 0, 0), new GraphNodeReadModel("b", "b", "B", 120, 0) }, ports: new[] { new GraphPortReadModel("a", "out", "Out", "Color", PresentationPortDirection.Output, PresentationPortRequirement.Optional), new GraphPortReadModel("b", "in", "In", "Vector4", PresentationPortDirection.Input, PresentationPortRequirement.Required) }, connections: new[] { new GraphConnectionReadModel("c", "a", "out", "b", "in", true, "Color → Vector4") })); yield return null; Assert.That(h.Q<Label>("connection-c").text, Does.Contain("Conversion")); Assert.That(h.Q<Label>("connection-c").ClassListContains("sd-connection-dashed"), Is.True); Assert.That(h.Q("connection-c-conversion-badge"), Is.Not.Null); }
		[UnityTest, Category("GUI_13_Graph_PortRequirement")] public IEnumerator GraphPortRequirementSurface() { var h = HostWithCatalog(out _); var canvas = h.Q<GraphCanvasElement>("node-graph-canvas"); canvas.SetGraph(new GraphReadModel(nodes: new[] { new GraphNodeReadModel("n", "n", "N", 0, 0) }, ports: new[] { new GraphPortReadModel("n", "required", "Required", "Float", PresentationPortDirection.Input, PresentationPortRequirement.Required), new GraphPortReadModel("n", "optional", "Optional", "Float", PresentationPortDirection.Input, PresentationPortRequirement.Optional) })); yield return null; Assert.That(h.Q<Label>("port-n-required").text, Does.Contain("Required")); Assert.That(h.Q<Label>("port-n-optional").text, Does.Contain("Optional")); }
		[UnityTest, Category("GUI_14_Graph_NodeStatus")] public IEnumerator GraphNodeStatus() { var h = Host(); h.Q<GraphCanvasElement>("node-graph-canvas").SetGraph(new GraphReadModel(nodes: new[] { new GraphNodeReadModel("n", "unknown", "Unknown", 4, 4, PresentationNodeStatus.UnknownNode) })); yield return null; Assert.That(h.Q<Button>("node-n").text, Does.Contain("UnknownNode")); }
		[UnityTest, Category("GUI_14_Graph_NodeStatus")] public IEnumerator GraphCanvasSkipsSameGraphInstanceAndUpdatesNewStatusInstance() { var h = Host(); var canvas = h.Q<GraphCanvasElement>("node-graph-canvas"); var stable = new GraphReadModel(nodes: new[] { new GraphNodeReadModel("n", "test", "Node", 4, 4, PresentationNodeStatus.Ready) }); canvas.SetGraph(stable); yield return null; var node = h.Q<Button>("node-n"); var text = node.text; Assert.That(canvas.TryUpdateGraphState(stable), Is.True); Assert.That(h.Q<Button>("node-n"), Is.SameAs(node)); Assert.That(node.text, Is.EqualTo(text), "A retained graph slice must not reapply node text or status."); var statusChanged = new GraphReadModel(nodes: new[] { new GraphNodeReadModel("n", "test", "Node", 4, 4, PresentationNodeStatus.Blocked) }); Assert.That(canvas.TryUpdateGraphState(statusChanged), Is.True); Assert.That(h.Q<Button>("node-n"), Is.SameAs(node)); Assert.That(node.text, Does.Contain("Blocked")); }
		[UnityTest, Category("GUI_15_Parameters_StandardControl")] public IEnumerator InspectorStandardControl() { var h = HostWithParameter(new ParameterReadModel("node", "gain", "Gain", "0.5", "0.5", false, false, false, null, "Float"), out var commands); yield return null; Assert.That(h.Q("parameter-control-numeric"), Is.Not.Null); Assert.That(h.Q<FloatField>("parameter-float-field"), Is.Not.Null); h.Q<FloatField>("parameter-float-field").value = 0.75f; Assert.That(commands.Count, Is.EqualTo(1)); Assert.That(commands[0].CommandId, Is.EqualTo("parameter.set_base")); }
		[UnityTest, Category("GUI_16_Parameters_BaseEffective")] public IEnumerator InspectorBaseEffective() { var h = HostWithParameter(new ParameterReadModel("node", "gain", "Gain", "2", "3", false, false, false, null, "Float"), out _); h.Q<TextField>("inspector-base-value").value = "2"; yield return null; Assert.That(h.Q<TextField>("inspector-effective-value").isReadOnly, Is.True); Assert.That(h.Q<TextField>("inspector-base-value").value, Is.EqualTo("2")); }
		[UnityTest, Category("GUI_17_Parameters_DashboardPlace")] public IEnumerator DashboardPlace() { var h = Host(); yield return null; Assert.That(h.Q("dashboard-grid-12-columns").childCount, Is.EqualTo(12)); Assert.That(h.Q("dashboard-grid-12-columns").worldBound.width, Is.GreaterThan(0)); }
		[UnityTest, Category("GUI_18_Parameters_DashboardPersist")] public IEnumerator DashboardPersistSurface() { var h = HostWithCatalog(out var commands); Click(h.Q<Button>("dashboard-add-page")); yield return null; Assert.That(commands.Count, Is.EqualTo(1)); Assert.That(commands[0].CommandId, Is.EqualTo("dashboard.add_page")); }
		[UnityTest, Category("GUI_19_Parameters_BrokenWidget")] public IEnumerator DashboardBrokenWidgetSurface() { var h = HostWithDashboard(new DashboardPageReadModel("p", "Page", new[] { new DashboardWidgetReadModel("w", "gain", 0, 0, 2, 1, "Broken", true) }), out _); yield return null; Assert.That(h.Q<Button>("dashboard-widget-w"), Is.Not.Null); Assert.That(h.Q<Button>("dashboard-widget-w").tooltip, Does.Contain("Remove or rebind")); }
		[UnityTest, Category("GUI_20_Parameters_LearnKey")] public IEnumerator LearnKeyCommandBoundary() { var h = HostWithControls(new LogicalControlReadModel("c", "Control", "Value"), out var commands); Click(h.Q<Button>("control-learn-c")); yield return null; Assert.That(commands.Count, Is.EqualTo(1)); Assert.That(commands[0].CommandId, Is.EqualTo("control.learn.begin")); }
		[UnityTest, Category("GUI_20_Parameters_LiveControlState")] public IEnumerator LiveControlStateUpdatesKeyedElementWithoutStructuralRebuild() { var initial = new PresentationReadModel(output: new OutputReadModel(), controls: new[] { new LogicalControlReadModel("value", "Value", "Value", currentValue: 0f), new LogicalControlReadModel("trigger", "Trigger", "PresetTrigger", isFiring: false) }); var root = RootWithModel(initial, new RecordingCommandPort(new List<PresentationCommandRequest>()), out _); yield return null; var valueItem = root.RootVisualElement.Q<VisualElement>("control-value"); var triggerItem = root.RootVisualElement.Q<VisualElement>("control-trigger"); Assert.That(PresentationUiComposition.ApplyDynamicReadModel(root.RootVisualElement, new PresentationReadModel(output: new OutputReadModel(), controls: new[] { new LogicalControlReadModel("value", "Value", "Value", currentValue: 1f), new LogicalControlReadModel("trigger", "Trigger", "PresetTrigger", isFiring: true) })), Is.True); Assert.That(root.RootVisualElement.Q<VisualElement>("control-value"), Is.SameAs(valueItem)); Assert.That(root.RootVisualElement.Q<VisualElement>("control-trigger"), Is.SameAs(triggerItem)); Assert.That(valueItem.Q<Label>("control-label-value").text, Does.Contain("1")); Assert.That(triggerItem.ClassListContains("is-firing"), Is.True); Assert.That(PresentationUiComposition.ApplyDynamicReadModel(root.RootVisualElement, new PresentationReadModel(output: new OutputReadModel(), controls: new[] { new LogicalControlReadModel("value", "Value", "Value", currentValue: 1f), new LogicalControlReadModel("trigger", "Trigger", "PresetTrigger", isFiring: false) })), Is.True); Assert.That(triggerItem.ClassListContains("is-firing"), Is.False); }
		[UnityTest, Category("GUI_21_Parameters_DraftAtomic")] public IEnumerator ExpressionDraftSurface() { var h = HostWithParameter(new ParameterReadModel("node", "gain", "Gain", "0.5", "0.5", false, false, false, null, "Float"), out var commands); h.Q<TextField>("inspector-expression-control").value = "c"; Click(h.Q<Button>("inspector-apply-control-expression")); yield return null; Assert.That(commands.Count, Is.EqualTo(1)); Assert.That(commands[0].CommandId, Is.EqualTo("parameter.apply_expression")); }
		[UnityTest, Category("GUI_22_Program_MonitorClose")] public IEnumerator ProgramMonitorClose() { var h = Host(); Click(h.Q<Button>("program-close")); yield return null; Assert.That(h.Q("program-monitor").ClassListContains("is-closed"), Is.True); }
		[UnityTest, Category("GUI_23_Program_NoOverlay")] public IEnumerator ProgramNoOverlay() { var h = Host(); yield return null; Assert.That(h.Q("program-monitor").Q("diagnostics-overlay"), Is.Null); Assert.That(h.Q("program-image"), Is.Not.Null); }
		[UnityTest, Category("GUI_24_Preview_DoubleClickTab")] public IEnumerator PreviewTabSurface() { var h = HostWithModel(new PresentationReadModel(output: new OutputReadModel(previews: new[] { new PreviewReadModel("n", "tab", true, PresentationOutputFit.Fit, PresentationOutputBackground.Black, PresentationQualityStage.Full, "Ready") })), out _); yield return null; Assert.That(h.Q<TabView>("preview-tabs"), Is.Not.Null); Assert.That(h.Q("preview-tab-tab"), Is.Not.Null); }
		[UnityTest, Category("GUI_25_Preview_MaxEight")] public IEnumerator PreviewMaxEightSurface() { var previews = new List<PreviewReadModel>(); for (var i = 0; i < 9; i++) previews.Add(new PreviewReadModel("n" + i, "tab" + i, true, PresentationOutputFit.Fit, PresentationOutputBackground.Black, PresentationQualityStage.Full, "Ready")); var h = HostWithModel(new PresentationReadModel(output: new OutputReadModel(previews: previews)), out _); yield return null; Assert.That(h.Q<TabView>("preview-tabs").Query<Label>(className: "sd-preview-tab").ToList().Count, Is.EqualTo(8)); Assert.That(h.Q<Label>("preview-rejection").text, Does.Contain("eight")); Assert.That(h.Q<Label>("preview-host-title").text, Does.Contain("/8")); }
		[UnityTest, Category("GUI_26_Preview_HideDemand")] public IEnumerator PreviewHide() { var h = HostWithModel(new PresentationReadModel(output: new OutputReadModel(previews: new[] { new PreviewReadModel("n", "tab", true, PresentationOutputFit.Fit, PresentationOutputBackground.Black, PresentationQualityStage.Full, "Ready") })), out var commands); Click(h.Q<Button>("preview-hide")); Click(h.Q<Button>("preview-show")); yield return null; Assert.That(h.Q("preview-viewer-host").ClassListContains("is-hidden"), Is.False); Assert.That(h.Q("preview-tab-tab"), Is.Not.Null); Assert.That(commands.Select(x => x.CommandId), Is.EqualTo(new[] { "preview.host.visible", "preview.host.visible" })); }
		[UnityTest, Category("GUI_27_Preview_ViewSettings")] public IEnumerator PreviewFitFillStretch() { var h = Host(); Click(h.Q<Button>("preview-fill")); Assert.That(h.Q("preview-viewer-host").ClassListContains("is-fill"), Is.True); Click(h.Q<Button>("preview-stretch")); yield return null; Assert.That(h.Q("preview-viewer-host").ClassListContains("is-stretch"), Is.True); Assert.That(h.Q("preview-viewer-host").ClassListContains("is-fill"), Is.False); }
		[UnityTest, Category("GUI_28_Preview_StateQuality")] public IEnumerator PreviewStateQuality() { var h = HostWithModel(new PresentationReadModel(output: new OutputReadModel(previews: new[] { new PreviewReadModel("n", "tab", true, PresentationOutputFit.Fit, PresentationOutputBackground.Black, PresentationQualityStage.Reduced, "Reduced · 30 fps") })), out _); yield return null; Assert.That(h.Q<Label>("preview-host-title").text, Does.Contain("/8")); Assert.That(h.Q<Label>("preview-tab-tab").text, Does.Contain("Reduced")); }
		[UnityTest, Category("GUI_29_Presets_Trigger")] public IEnumerator PresetTrigger() { var h = HostWithCatalog(out var commands); Click(h.Q<Button>("preset-button-preset-1")); yield return null; Assert.That(commands.Count, Is.EqualTo(1)); Assert.That(commands[0].CommandId, Is.EqualTo("preset.apply")); }
		[UnityTest, Category("GUI_30_Presets_BrokenAtomic")] public IEnumerator PresetBrokenSurface() { var h = HostWithModel(new PresentationReadModel(presets: new[] { new PresetListItemReadModel("bad", "Broken", true, "Missing media") }), out _); yield return null; Assert.That(h.Q<Button>("preset-button-bad").enabledSelf, Is.False); Assert.That(h.Q<Button>("preset-button-bad").tooltip, Does.Contain("Missing media")); }
		[UnityTest, Category("GUI_31_Media_ImportProgress")] public IEnumerator MediaImportProgress() { var h = HostWithPlatform(new RecordingPlatformFiles("C:/media/a.png", "C:/media/b.png"), out var commands, out var platform); Click(h.Q<Button>("media-import-button")); yield return null; Assert.That(platform.LastRequest, Is.Not.Null); Assert.That(platform.LastRequest.Kind, Is.EqualTo(PlatformPathRequestKind.MultiFile)); Assert.That(commands.Count, Is.EqualTo(1)); Assert.That(commands[0].CommandId, Is.EqualTo("media.import.batch")); Assert.That(commands[0].Payload["paths"], Does.Contain("a.png")); Assert.That(commands[0].Payload["paths"], Does.Contain("b.png")); Assert.That(h.Q<Label>("media-import-progress").text, Does.Contain("Importing 2")); }
		[UnityTest, Category("GUI_32_Media_DeleteReferences")] public IEnumerator MediaDeleteSurface() { var h = HostWithModel(new PresentationReadModel(media: new[] { new MediaListItemReadModel("m", "media/a.png", "Referenced", "Used by node") }), out _); yield return null; Assert.That(h.Q<VisualElement>("media-item-m").tooltip, Does.Contain("Used by node")); }
		[UnityTest, Category("GUI_33_Media_BrokenReference")] public IEnumerator MediaBrokenSurface() { var h = HostWithModel(new PresentationReadModel(media: new[] { new MediaListItemReadModel("m", "media/missing.png", "Broken", "File missing") }), out _); yield return null; Assert.That(h.Q<VisualElement>("media-item-m").Q<Label>("media-label-m").text, Does.Contain("missing")); Assert.That(h.Q<VisualElement>("media-item-m").tooltip, Does.Contain("File missing")); }
		[UnityTest, Category("GUI_34_Diagnostics_Filter")] public IEnumerator DiagnosticsFilterSurface() { var h = HostWithModel(new PresentationReadModel(diagnostics: new[] { new DiagnosticReadModel("d1", PresentationSeverity.Error, "graph.bad", "Bad edge"), new DiagnosticReadModel("d2", PresentationSeverity.Warning, "media.missing", "Missing file") }), out _); h.Q<TextField>("diagnostics-filter").value = "graph"; yield return null; Assert.That(h.Q("diagnostic-d1").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex)); Assert.That(h.Q("diagnostic-d2").resolvedStyle.display, Is.EqualTo(DisplayStyle.None)); }
		[UnityTest, Category("GUI_35_Diagnostics_Aggregate")] public IEnumerator DiagnosticsAggregate() { var h = HostWithModel(new PresentationReadModel(diagnostics: new[] { new DiagnosticReadModel("d1", PresentationSeverity.Warning, "fault", "Repeated", count: 3) }), out _); yield return null; Assert.That(h.Q<Button>("diagnostic-d1").text, Does.Contain("Repeated")); Assert.That(h.Q<Button>("diagnostic-d1").tooltip, Does.Contain("fault")); }
		[UnityTest, Category("GUI_35_Diagnostics_BindingReset")] public IEnumerator DiagnosticsBindingSurvivesStructuralAndDynamicCyclesWithoutDuplicates() { var initial = new PresentationReadModel(output: new OutputReadModel(), diagnostics: new[] { new DiagnosticReadModel("d", PresentationSeverity.Warning, "code", "Initial", nodeId: "n") }); var root = RootWithModel(initial, new RecordingCommandPort(new List<PresentationCommandRequest>()), out var coordinator); yield return null; var list = root.RootVisualElement.Q("diagnostics-list"); PresentationUiComposition.ApplyReadModel(root.RootVisualElement, initial, coordinator); var structural = list.Q<Button>("diagnostic-d"); Assert.That(PresentationUiComposition.ApplyDynamicReadModel(root.RootVisualElement, new PresentationReadModel(output: new OutputReadModel(), diagnostics: new[] { new DiagnosticReadModel("d", PresentationSeverity.Error, "code", "Dynamic", nodeId: "n") }), coordinator), Is.True); Assert.That(list.Query<Button>(name: "diagnostic-d").ToList(), Has.Count.EqualTo(1)); Assert.That(list.Q<Button>("diagnostic-d"), Is.SameAs(structural)); Assert.That(structural.text, Does.Contain("Dynamic")); var rebuilt = new PresentationReadModel(output: new OutputReadModel(), diagnostics: new[] { new DiagnosticReadModel("d", PresentationSeverity.Warning, "code", "Rebuilt", nodeId: "next") }); PresentationUiComposition.ApplyReadModel(root.RootVisualElement, rebuilt, coordinator); var afterRebuild = list.Q<Button>("diagnostic-d"); Assert.That(afterRebuild, Is.Not.SameAs(structural)); Assert.That(PresentationUiComposition.ApplyDynamicReadModel(root.RootVisualElement, new PresentationReadModel(output: new OutputReadModel(), diagnostics: new[] { new DiagnosticReadModel("d", PresentationSeverity.Error, "code", "Latest", nodeId: "last") }), coordinator), Is.True); Assert.That(list.Query<Button>(name: "diagnostic-d").ToList(), Has.Count.EqualTo(1)); Assert.That(list.Q<Button>("diagnostic-d"), Is.SameAs(afterRebuild)); Assert.That(afterRebuild.text, Does.Contain("Latest")); }
		[UnityTest, Category("GUI_36_Diagnostics_Export")] public IEnumerator DiagnosticsExport() { var h = HostWithPlatform(new RecordingPlatformFiles("C:/diagnostics/export.json"), out var commands, out _); Click(h.Q<Button>("diagnostics-export-button")); yield return null; Assert.That(h.Q("diagnostics-list"), Is.Not.Null); Assert.That(commands.Count, Is.EqualTo(1)); Assert.That(commands[0].CommandId, Is.EqualTo("diagnostics.export")); }
		[UnityTest, Category("GUI_37_Project_CloseDecision")] public IEnumerator ProjectCloseSurface() { var commands = new List<PresentationCommandRequest>(); var root = RootWithModel(new PresentationReadModel(shell: new ShellReadModel(PresentationProjectState.Ready, "Dirty", true, false, false, false, "Unsaved")), new RecordingCommandPort(commands), out _); yield return null; Click(root.RootVisualElement.Q<Button>("app-menu")); Click(root.RootVisualElement.Q<Button>("project-close")); yield return null; var dialog = root.RootVisualElement.Q("unsaved-changes-dialog"); Assert.That(dialog, Is.Not.Null); var count = commands.Count; Click(dialog.Q<Button>("unsaved-cancel")); Assert.That(commands.Count, Is.EqualTo(count)); Click(root.RootVisualElement.Q<Button>("project-close")); yield return null; Click(root.RootVisualElement.Q<Button>("unsaved-save")); Assert.That(commands.Last().CommandId, Is.EqualTo("project.close")); Assert.That(commands.Last().Payload["decision"], Is.EqualTo("Save")); Click(root.RootVisualElement.Q<Button>("project-close")); yield return null; Click(root.RootVisualElement.Q<Button>("unsaved-discard")); Assert.That(commands.Last().Payload["decision"], Is.EqualTo("Discard")); }
		[UnityTest, Category("GUI_38_Project_SaveFailure")] public IEnumerator ProjectSaveSurface() { var commands = new List<PresentationCommandRequest>(); var root = RootWithModel(new PresentationReadModel(shell: new ShellReadModel(PresentationProjectState.Ready, "Dirty", true, false, false, false, "Unsaved")), new RecordingCommandPort(commands, request => request.CommandId == "project.save" ? new CommandReadModel(request.CommandRequestId, request.InteractionId, PresentationCommandStatus.Rejected, "Disk full") : new CommandReadModel(request.CommandRequestId, request.InteractionId, PresentationCommandStatus.Accepted)), out _); yield return null; var graphBefore = root.RootVisualElement.Q("node-graph-panel"); Click(root.RootVisualElement.Q<Button>("project-save")); yield return null; Assert.That(commands.Count, Is.EqualTo(1), "A keyboard NavigationSubmit must invoke the Save Button callback exactly once."); Assert.That(commands.Last().CommandId, Is.EqualTo("project.save")); Assert.That(root.RootVisualElement.Q<Label>("banner-layer").ClassListContains("is-visible"), Is.True); Assert.That(root.RootVisualElement.Q<Label>("banner-layer").text, Does.Contain("Disk full")); Assert.That(root.RootVisualElement.Q("node-graph-panel"), Is.SameAs(graphBefore)); Assert.That(root.RootVisualElement.Q<Label>("dirty-state").text, Does.Contain("Dirty")); }
		[UnityTest, Category("GUI_38_Project_SavePointer")] public IEnumerator ProjectSavePointerClickUsesTheLivePanelPickTarget() { var commands = new List<PresentationCommandRequest>(); var root = RootWithModel(new PresentationReadModel(shell: new ShellReadModel(PresentationProjectState.Ready, "Dirty", true, false, false, false, "Unsaved")), new RecordingCommandPort(commands), out _); yield return null; var save = root.RootVisualElement.Q<Button>("project-save"); AssertPointerPicksTargetOrChild(save); Assert.That(FocusAndSubmitPickVerifiedSave(save), Is.Null); yield return null; Assert.That(commands.Count, Is.EqualTo(1), "A Pick-verified focused Save NavigationSubmit must submit project.save exactly once."); Assert.That(commands.Single().CommandId, Is.EqualTo("project.save")); }

		[UnityTest, Category("GUI_38_Project_SavePointer")]
		public IEnumerator ProjectAndGraphToolbarPointersRemainIndependentlyPickableAtAcceptanceViewport() {
			var commands = new List<PresentationCommandRequest>();
			var workspace = new WorkspaceReadModel("Edit", false, null, LayoutPresetStore.CreateDefaults().Presets,
				currentTree: LayoutPresetStore.EditDefaultTree());
			var model = new PresentationReadModel(
				shell: new ShellReadModel(PresentationProjectState.Ready, "Complex", true, false, false, false, "Unsaved"),
				workspace: workspace,
				graph: new GraphReadModel(new[] { new GraphNodeReadModel("source", "fx.blur", "Source", 16, 16) }),
				nodeCatalog: new[] { new NodeCatalogItem("fx.blur", "Blur", true, null, true, "FX") },
				diagnostics: new[] { new DiagnosticReadModel("warning", PresentationSeverity.Warning, "graph.warning", "Complex fixture") });
			var root = RootWithModel(model, new RecordingCommandPort(commands), out _);
			Screen.SetResolution(1280, 720, false);
			root.RootVisualElement.style.width = 1280;
			root.RootVisualElement.style.height = 720;
			yield return null;

			var save = root.RootVisualElement.Q<Button>("project-save");
			var graphToolbar = root.RootVisualElement.Q("graph-toolbar");
			var grid = root.RootVisualElement.Q<Button>("graph-toggle-grid");
			var canvas = root.RootVisualElement.Q<GraphCanvasElement>("node-graph-canvas");
			Assert.That(save, Is.Not.Null);
			Assert.That(graphToolbar, Is.Not.Null);
			Assert.That(grid, Is.Not.Null);
			Assert.That(canvas, Is.Not.Null);
			Assert.That(save.worldBound.Overlaps(graphToolbar.worldBound), Is.False, "Top-bar Save and graph toolbar must not overlap.");
			AssertPointerPicksTargetOrChild(save);
			AssertPointerPicksTargetOrChild(grid);

			Assert.That(FocusAndSubmitPickVerifiedSave(save), Is.Null);
			var gridBefore = canvas.IsGridSnapEnabled;
			Assert.That(SimulatePickVerifiedButtonClick(grid), Is.Null);
			yield return null;

			Assert.That(commands.Count, Is.EqualTo(1), "A Pick-verified focused Save NavigationSubmit must submit project.save exactly once.");
			Assert.That(commands.Single().CommandId, Is.EqualTo("project.save"));
			Assert.That(canvas.IsGridSnapEnabled, Is.Not.EqualTo(gridBefore));
		}
		[UnityTest, Category("GUI_39_Project_RecoveredBanner")] public IEnumerator RecoveredBannerSurface() { var commands = new List<PresentationCommandRequest>(); var root = RootWithModel(new PresentationReadModel(shell: new ShellReadModel(PresentationProjectState.Ready, "Recovered", false, true, false, false, "Recovered project")), new RecordingCommandPort(commands), out _); yield return null; var banner = root.RootVisualElement.Q<Label>("banner-layer"); Assert.That(banner.ClassListContains("is-visible"), Is.True); Assert.That(banner.text, Does.Contain("Recovered")); Assert.That(root.RootVisualElement.Q<Label>("status").text, Does.Contain("Recovered")); }
		[UnityTest, Category("GUI_40_Project_OpenFailurePreserves")] public IEnumerator ProjectOpenFailureSurface() { var graphModel = new GraphReadModel(nodes: new[] { new GraphNodeReadModel("keep", "fx.blur", "Keep", 10, 10) }); var commands = new List<PresentationCommandRequest>(); var root = RootWithModel(new PresentationReadModel(shell: new ShellReadModel(PresentationProjectState.Ready, "Current", false, false, false, false, "Ready"), graph: graphModel), new RecordingCommandPort(commands, request => request.CommandId == "project.open" ? new CommandReadModel(request.CommandRequestId, request.InteractionId, PresentationCommandStatus.Rejected, "File is invalid") : new CommandReadModel(request.CommandRequestId, request.InteractionId, PresentationCommandStatus.Accepted)), out _, new RecordingPlatformFiles("C:/invalid-project")); yield return null; var graphBefore = root.RootVisualElement.Q("node-graph-panel"); Assert.That(root.RootVisualElement.Q("node-keep"), Is.Not.Null); Click(root.RootVisualElement.Q<Button>("app-menu")); Click(root.RootVisualElement.Q<Button>("project-open")); yield return null; Assert.That(commands.Last().CommandId, Is.EqualTo("project.open")); Assert.That(root.RootVisualElement.Q<Label>("banner-layer").text, Does.Contain("File is invalid")); Assert.That(root.RootVisualElement.Q("node-graph-panel"), Is.SameAs(graphBefore)); Assert.That(root.RootVisualElement.Q("node-keep"), Is.Not.Null); }
		[UnityTest, Category("GUI_41_Visibility_StateNotColor")] public IEnumerator VisibilityTextSurface() { var h = HostWithModel(new PresentationReadModel(output: new OutputReadModel(programState: "HoldingLastFrame", programDisplay: 2)), out _); yield return null; Assert.That(h.Q<Label>("program-monitor-title").text, Does.Contain("HoldingLastFrame")); Assert.That(h.Q<Label>("program-monitor-title").text, Does.Contain("Display 2")); }
		[UnityTest, Category("GUI_42_Visibility_Focus")] public IEnumerator VisibilityFocusSurface() { var h = Host(); var search = h.Q<TextField>("node-search"); var graph = h.Q<GraphCanvasElement>("node-graph-canvas"); search.Focus(); yield return null; Assert.That(search.ClassListContains("is-focused"), Is.True); graph.Focus(); yield return null; Assert.That(graph.focusable, Is.True); Assert.That(graph.ClassListContains("is-focused"), Is.True); Assert.That(search.ClassListContains("is-focused"), Is.False); }
		[UnityTest, Category("GUI_43_Visibility_Scale")] public IEnumerator VisibilityScaleSurface() { foreach (var scale in new[] { 1f, 1.25f, 1.5f }) { var h = HostAtScale(scale); yield return null; Assert.That(h.worldBound.width, Is.GreaterThan(0)); Assert.That(h.worldBound.height, Is.GreaterThan(0)); var viewport = h.worldBound; foreach (var panel in h.Children().Where(x => x.resolvedStyle.display != DisplayStyle.None)) { Assert.That(panel.worldBound.xMin, Is.GreaterThanOrEqualTo(viewport.xMin - 1f), "scale " + scale + " left " + panel.name); Assert.That(panel.worldBound.xMax, Is.LessThanOrEqualTo(viewport.xMax + 1f), "scale " + scale + " right " + panel.name); Assert.That(panel.worldBound.yMin, Is.GreaterThanOrEqualTo(viewport.yMin - 1f), "scale " + scale + " top " + panel.name); Assert.That(panel.worldBound.yMax, Is.LessThanOrEqualTo(viewport.yMax + 1f), "scale " + scale + " bottom " + panel.name); } } }
		[UnityTest, Category("GUI_44_Visibility_ReduceMotion")]
		public IEnumerator VisibilityReduceMotionSurface() {
			var workspace = new WorkspaceReadModel("Edit", false, null, LayoutPresetStore.CreateDefaults().Presets,
				reduceMotion: true, currentTree: LayoutPresetStore.EditDefaultTree());
			var presentation = RootWithModel(new PresentationReadModel(workspace: workspace), new RecordingCommandPort(new List<PresentationCommandRequest>()), out _);
			var deadline = Time.realtimeSinceStartupAsDouble + 5d;
			while (!presentation.RootVisualElement.ClassListContains("reduce-motion") && Time.realtimeSinceStartupAsDouble < deadline)
				yield return null;

			var root = presentation.RootVisualElement;
			var save = root.Q<Button>("project-save");
			Assert.That(root.ClassListContains("reduce-motion"), Is.True, DescribeTransitionStyles(root));
			Assert.That(save, Is.Not.Null, "The Reduce Motion regression must inspect a real descendant control.");
			AssertZeroMotion(root);
			AssertZeroMotion(save);
		}
		[UnityTest, Category("GUI_45_Visibility_TextInputSuppression")] public IEnumerator TextInputSurface() { var h = HostWithCatalog(out var commands); var search = h.Q<TextField>("node-search"); search.Focus(); yield return null; search.SendEvent(KeyDownEvent.GetPooled('g', KeyCode.G, EventModifiers.None)); yield return null; Assert.That(commands, Is.Empty); Assert.That(search.ClassListContains("is-focused"), Is.True); }

		[UnityTest, Category("GUI_06_Workspace_Presets")]
		public IEnumerator RenamedLayoutSelectorAndSaveUseStableLayoutIds() {
			var renamed = new LayoutPreset("layout-stable-id", "Renamed Layout", LayoutPresetStore.EditDefaultTree());
			var other = new LayoutPreset("other-id", "Other Layout", LayoutPresetStore.LiveDefaultTree());
			var model = new PresentationReadModel(workspace: new WorkspaceReadModel("layout-stable-id", false, null, new[] { renamed, other }, currentTree: renamed.Tree));
			var commands = new List<PresentationCommandRequest>();
			var root = RootWithModel(model, new RecordingCommandPort(commands), out _);
			yield return null;

			var selector = root.RootVisualElement.Q<PopupField<string>>("top-layout-selector");
			Assert.That(selector.value, Is.EqualTo("Renamed Layout"));
			selector.value = "Other Layout";
			selector.value = "Renamed Layout";
			var select = commands.Last(x => x.CommandId == "workspace.layout");
			Assert.That(select.Payload["operation"], Is.EqualTo("select"));
			Assert.That(select.Payload["layoutId"], Is.EqualTo("layout-stable-id"));

			Click(root.RootVisualElement.Q<Button>("top-layout-save"));
			var save = commands.Last();
			Assert.That(save.CommandId, Is.EqualTo("workspace.layout"));
			Assert.That(save.Payload["operation"], Is.EqualTo("overwrite"));
			Assert.That(save.Payload["layoutId"], Is.EqualTo("layout-stable-id"));
		}

		[UnityTest, Category("GUI_Settings")]
		public IEnumerator SettingsUiReflectsReadModelAndRoutesEveryUserPreferencePayload() {
			const string originalFolder = "C:/Diagnostics/Original";
			const string selectedFolder = "C:/Diagnostics/Selected";
			var workspace = new WorkspaceReadModel("Edit", false, null, LayoutPresetStore.CreateDefaults().Presets,
				1.5f, true, LayoutPresetStore.EditDefaultTree(), "Dark", 1f, "List", originalFolder);
			var commands = new List<PresentationCommandRequest>();
			var root = RootWithModel(new PresentationReadModel(workspace: workspace), new RecordingCommandPort(commands), out _, new RecordingPlatformFiles(selectedFolder));
			yield return null;
			Click(root.RootVisualElement.Q<Button>("app-menu"));
			Click(root.RootVisualElement.Q<Button>("top-settings"));

			var scale = root.RootVisualElement.Q<PopupField<string>>("settings-ui-scale");
			var theme = root.RootVisualElement.Q<PopupField<string>>("settings-theme");
			var reduce = root.RootVisualElement.Q<Toggle>("settings-reduce-motion");
			var tooltip = root.RootVisualElement.Q<PopupField<string>>("settings-tooltip-delay");
			var media = root.RootVisualElement.Q<PopupField<string>>("settings-media-view");
			var folder = root.RootVisualElement.Q<TextField>("settings-diagnostics-folder");
			Assert.That(scale.value, Is.EqualTo("150%"));
			Assert.That(theme.value, Is.EqualTo("Dark"));
			Assert.That(reduce.value, Is.True);
			Assert.That(tooltip.value, Is.EqualTo("1000 ms"));
			Assert.That(media.value, Is.EqualTo("List"));
			Assert.That(folder.value, Is.EqualTo(originalFolder));

			scale.value = "125%";
			theme.SetValueWithoutNotify(string.Empty);
			theme.value = "Dark";
			reduce.value = false;
			tooltip.value = "250 ms";
			media.value = "Grid";
			Click(root.RootVisualElement.Q<Button>("settings-diagnostics-folder-choose"));

			Assert.That(commands.Single(x => x.CommandId == "workspace.ui_scale").Payload["value"], Is.EqualTo("1.25"));
			Assert.That(commands.Single(x => x.CommandId == "workspace.theme").Payload["value"], Is.EqualTo("Dark"));
			Assert.That(commands.Single(x => x.CommandId == "workspace.reduce_motion").Payload["value"], Is.EqualTo("false"));
			Assert.That(commands.Single(x => x.CommandId == "workspace.tooltip_delay").Payload["value"], Is.EqualTo("0.25"));
			Assert.That(commands.Single(x => x.CommandId == "workspace.media_view").Payload["value"], Is.EqualTo("Grid"));
			Assert.That(commands.Single(x => x.CommandId == "workspace.diagnostics_folder").Payload["path"], Is.EqualTo(selectedFolder));
		}

		[UnityTest, Category("GUI_DiagnosticsProject")]
		public IEnumerator MissingRecentRequiresConfirmationCancelKeepsAndRemoveHidesImmediately() {
			var first = "C:/ShitDesigner-Missing-Recent-A-" + Guid.NewGuid().ToString("N");
			var second = "C:/ShitDesigner-Missing-Recent-B-" + Guid.NewGuid().ToString("N");
			var commands = new List<PresentationCommandRequest>();
			var model = new PresentationReadModel(recentProjectRoots: new[] { "C:/ShitDesigner-Missing-Recent-Initial", first, second });
			var root = RootWithModel(model, new RecordingCommandPort(commands), out _);
			yield return null;
			Click(root.RootVisualElement.Q<Button>("app-menu"));
			var recent = root.RootVisualElement.Q<PopupField<string>>("project-open-recent");

			recent.value = first;
			var dialog = root.RootVisualElement.Q("missing-recent-dialog");
			Assert.That(dialog, Is.Not.Null);
			Click(dialog.Q<Button>("missing-recent-cancel"));
			Assert.That(recent.choices, Does.Contain(first));
			Assert.That(commands, Is.Empty);

			recent.value = second;
			dialog = root.RootVisualElement.Q("missing-recent-dialog");
			Assert.That(dialog, Is.Not.Null);
			Click(dialog.Q<Button>("missing-recent-remove"));
			var remove = commands.Single();
			Assert.That(remove.CommandId, Is.EqualTo("workspace.recent.remove"));
			Assert.That(remove.Payload["root"], Is.EqualTo(second));
			Assert.That(recent.choices, Does.Not.Contain(second));
			Assert.That(recent.choices, Does.Contain(first));
		}

		[UnityTest, Category("GUI_ProgramMetrics")]
		public IEnumerator InvalidProgramMetricsAreUnavailableInStatusBar() {
			var output = new OutputReadModel(measuredFramesPerSecond: double.NaN,
				cpuFrameTimeMilliseconds: double.PositiveInfinity, gpuFrameTimeMilliseconds: 0d);
			var root = RootWithModel(new PresentationReadModel(output: output), new RecordingCommandPort(new List<PresentationCommandRequest>()), out _);
			yield return null;
			Assert.That(root.RootVisualElement.Q<Label>("program-fps").text, Is.EqualTo("Program fps Unavailable"));
			Assert.That(root.RootVisualElement.Q<Label>("cpu-frame-time").text, Is.EqualTo("CPU Frame Time Unavailable"));
			Assert.That(root.RootVisualElement.Q<Label>("gpu-frame-time").text, Is.EqualTo("GPU Frame Time Unavailable"));
			Assert.That(root.RootVisualElement.Q<Label>("program-monitor-metrics").text, Does.Contain("CPU Frame Time Unavailable"));
			Assert.That(root.RootVisualElement.Q<Label>("program-monitor-metrics").text, Does.Contain("GPU Frame Time Unavailable"));
			Assert.That(root.RootVisualElement.Q<Label>("program-monitor-metrics").text, Does.Contain("fps Unavailable"));
		}

		private VisualElement Host() {
			var host = RuntimeRoot();
			PresentationUiComposition.ComposeWorkspace(host, null);
			return host;
		}

		private static void Click(Button button) {
			Assert.That(button, Is.Not.Null, "Expected an interactive Button in the composed UI.");
			button.Focus();
			button.SendEvent(NavigationSubmitEvent.GetPooled());
		}

		private static void AssertZeroMotion(VisualElement element) {
			Assert.That(element, Is.Not.Null);
			var durations = element.resolvedStyle.transitionDuration;
			var delays = element.resolvedStyle.transitionDelay;
			Assert.That(durations == null || durations.All(value => Mathf.Approximately(value.value, 0f)), Is.True,
				"Reduce Motion must set every transition duration to zero. " + DescribeTransitionStyles(element));
			Assert.That(delays == null || delays.All(value => Mathf.Approximately(value.value, 0f)), Is.True,
				"Reduce Motion must set every transition delay to zero. " + DescribeTransitionStyles(element));
		}

		private static string DescribeTransitionStyles(VisualElement element) {
			if (element == null) return "element=<null>";
			var root = element.panel?.visualTree;
			var styleSheets = root == null
				? string.Empty
				: string.Join(",", Enumerable.Range(0, root.styleSheets.count).Select(index => root.styleSheets[index] == null ? "null" : root.styleSheets[index].name));
			return "element=" + (string.IsNullOrEmpty(element.name) ? element.GetType().Name : element.name) +
				   ":classes=" + string.Join(",", element.GetClasses()) +
				   ":durations=" + DescribeTimeValues(element.resolvedStyle.transitionDuration) +
				   ":delays=" + DescribeTimeValues(element.resolvedStyle.transitionDelay) +
				   ":styleSheets=" + styleSheets;
		}

		private static string DescribeTimeValues(IEnumerable<TimeValue> values) => values == null
			? "<null>"
			: "[" + string.Join(",", values.Select(value => value.value + value.unit.ToString())) + "]";

		private static void SendPointer(VisualElement target, string kind) {
			Assert.That(target, Is.Not.Null);
			var position = target.worldBound.center;
			// Dispatch through the live panel so UI Toolkit performs normal
			// picking and propagation. Sending directly to a leaf does not
			// establish a panel event path in Unity 6 batch mode.
			var dispatchRoot = target.panel == null ? target : target.panel.visualTree;
			if (kind == "down") {
				var systemEvent = new Event { type = EventType.MouseDown, button = 0, mousePosition = position };
				dispatchRoot.SendEvent(PointerDownEvent.GetPooled(systemEvent));
			}
			else if (kind == "move") {
				var systemEvent = new Event { type = EventType.MouseDrag, button = 0, mousePosition = position };
				dispatchRoot.SendEvent(PointerMoveEvent.GetPooled(systemEvent));
			}
			else {
				var systemEvent = new Event { type = EventType.MouseUp, button = 0, mousePosition = position };
				dispatchRoot.SendEvent(PointerUpEvent.GetPooled(systemEvent));
			}
		}

		private static string SimulatePickVerifiedButtonClick(Button target) {
			Assert.That(target, Is.Not.Null);
			// AssertPointerPicksTargetOrChild proves that a real pointer at this
			// coordinate reaches this Button or its child. Synthetic pointer
			// dispatch has context-dependent pressed/capture behavior, so use
			// the Button's own Clickable simulation only after that Pick proof.
			if (target.clickable == null || ClickableSimulateSingleClick == null)
				return "Unity 6000.5 UI Toolkit Clickable.SimulateSingleClick(EventBase, int) is unavailable.";
			try {
				var systemEvent = new Event { type = EventType.MouseUp, button = 0, mousePosition = target.worldBound.center };
				var pointerUp = PointerUpEvent.GetPooled(systemEvent);
				ClickableSimulateSingleClick.Invoke(target.clickable, new object[] { pointerUp, 0 });
				return null;
			}
			catch (TargetInvocationException exception) {
				return exception.InnerException?.Message ?? exception.Message;
			}
			catch (Exception exception) {
				return exception.Message;
			}
		}

		private static string FocusAndSubmitPickVerifiedSave(Button target) {
			Assert.That(target, Is.Not.Null);
			// The Pick assertion above proves the physical coordinate routes to
			// Save. Focus the proven Button, verify the panel focus target, then
			// send the public keyboard activation event. Button.Clickable owns
			// the production callback; this does not call the coordinator or
			// application directly.
			try {
				target.Focus();
				var focused = target.panel?.focusController?.focusedElement as VisualElement;
				if (focused == null || (!ReferenceEquals(focused, target) && !target.Contains(focused)))
					return "Panel focus did not resolve to project-save or its child; focused=" + DescribeElement(focused) + ".";
				target.SendEvent(NavigationSubmitEvent.GetPooled());
				return null;
			}
			catch (Exception exception) {
				return exception.Message;
			}
		}

		private static string DescribeElement(VisualElement element) => element == null
			? "none"
			: string.IsNullOrWhiteSpace(element.name) ? element.GetType().Name : element.name + " (" + element.GetType().Name + ")";

		private static void AssertPointerPicksTargetOrChild(VisualElement target) {
			Assert.That(target, Is.Not.Null);
			Assert.That(target.panel, Is.Not.Null);
			Assert.That(target.enabledInHierarchy, Is.True, target.name + " must be enabled for pointer input.");
			Assert.That(target.worldBound.width, Is.GreaterThan(0f), target.name + " must have a usable pointer width.");
			Assert.That(target.worldBound.height, Is.GreaterThan(0f), target.name + " must have a usable pointer height.");
			var picked = target.panel.Pick(target.worldBound.center);
			Assert.That(picked, Is.Not.Null);
			Assert.That(ReferenceEquals(picked, target) || target.Contains(picked), Is.True,
				target.name + " center must pick the target or a child, not an overlapping surface.");
		}

		private VisualElement HostWithCatalog(out List<PresentationCommandRequest> commands) {
			commands = new List<PresentationCommandRequest>();
			var read = new FixedReadPort();
			var port = new RecordingCommandPort(commands);
			var coordinator = new PresentationCoordinator(read, port);
			coordinator.ApplyLatestReadModels(1);
			var host = RuntimeRoot();
			PresentationUiComposition.ComposeWorkspace(host, coordinator);
			return host;
		}

		private VisualElement HostWithPlatform(IPlatformFileInteractionAdapter platform, out List<PresentationCommandRequest> commands, out RecordingPlatformFiles recording) {
			commands = new List<PresentationCommandRequest>();
			recording = platform as RecordingPlatformFiles;
			var coordinator = new PresentationCoordinator(new FixedReadPort(), new RecordingCommandPort(commands), platformFiles: platform);
			coordinator.ApplyLatestReadModels(1);
			var host = RuntimeRoot();
			PresentationUiComposition.ComposeWorkspace(host, coordinator);
			return host;
		}

		private VisualElement HostAtScale(float scale) {
			var host = RuntimeRoot(scale);
			PresentationUiComposition.ComposeWorkspace(host, null);
			return host;
		}

		private PresentationRoot RootWithModel(PresentationReadModel model, IPresentationCommandPort commands, out PresentationCoordinator coordinator, IPlatformFileInteractionAdapter platformFiles = null) {
			var gameObject = new GameObject("PresentationAcceptanceRoot");
			var panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			var document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			_objects.Add(gameObject);
			_panels.Add(panel);
			coordinator = new PresentationCoordinator(new FixedReadPort(model), commands, platformFiles: platformFiles);
			var root = gameObject.AddComponent<PresentationRoot>();
			root.Configure(coordinator);
			coordinator.ApplyLatestReadModels(1);
			return root;
		}

		private VisualElement HostWithModel(PresentationReadModel model, out List<PresentationCommandRequest> commands) {
			commands = new List<PresentationCommandRequest>();
			var coordinator = new PresentationCoordinator(new FixedReadPort(model), new RecordingCommandPort(commands));
			coordinator.ApplyLatestReadModels(1);
			var host = RuntimeRoot();
			PresentationUiComposition.ComposeWorkspace(host, coordinator);
			PresentationUiComposition.ApplyReadModel(host, coordinator.Current, coordinator);
			return host;
		}

		private VisualElement HostWithParameter(ParameterReadModel parameter, out List<PresentationCommandRequest> commands) {
			return HostWithModel(new PresentationReadModel(parameters: new[] { parameter }), out commands);
		}

		private VisualElement HostWithDashboard(DashboardPageReadModel dashboard, out List<PresentationCommandRequest> commands) {
			return HostWithModel(new PresentationReadModel(dashboardPages: new[] { dashboard }), out commands);
		}

		private VisualElement HostWithControls(LogicalControlReadModel control, out List<PresentationCommandRequest> commands) {
			return HostWithModel(new PresentationReadModel(controls: new[] { control }), out commands);
		}

		private VisualElement RuntimeRoot(float scale = 1f) {
			var gameObject = new GameObject("PresentationAcceptanceHost");
			var panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			panel.scale = scale;
			Screen.SetResolution(1280, 720, false);
			var targetTexture = new RenderTexture(1280, 720, 0) { name = "PresentationAcceptanceViewport" };
			targetTexture.Create();
			panel.targetTexture = targetTexture;
			var document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			_objects.Add(gameObject);
			_panels.Add(panel);
			_textures.Add(targetTexture);
			var root = document.rootVisualElement;
			// A real PlayMode acceptance host is explicitly 1280×720; this
			// avoids inheriting the batch runner's 640×480 default window.
			root.style.width = 1280;
			root.style.height = 720;
			var theme = Resources.Load<StyleSheet>("PresentationTheme");
			if (theme != null && !root.styleSheets.Contains(theme)) root.styleSheets.Add(theme);
			return root;
		}

		private sealed class FixedReadPort : IPresentationReadPort {
			private readonly Guid _session = Guid.NewGuid();
			private readonly PresentationReadModel _model;
			public FixedReadPort() : this(new PresentationReadModel(nodeCatalog: new[] { new NodeCatalogItem("fx.blur", "Blur") }, presets: new[] { new PresetListItemReadModel("preset-1", "Preset from Application") })) { }
			public FixedReadPort(PresentationReadModel model) { _model = model ?? new PresentationReadModel(); }
			public PresentationEnvelope<PresentationReadModel> ReadLatest(bool fullSnapshot) => new PresentationEnvelope<PresentationReadModel>(_session, 1, 1, 1, 1, true, _model);
		}

		private sealed class RecordingCommandPort : IPresentationCommandPort {
			private readonly List<PresentationCommandRequest> _requests;
			private readonly Func<PresentationCommandRequest, CommandReadModel> _handler;
			public RecordingCommandPort(List<PresentationCommandRequest> requests, Func<PresentationCommandRequest, CommandReadModel> handler = null) { _requests = requests; _handler = handler; }
			public CommandReadModel Submit(PresentationCommandRequest request) { _requests.Add(request); return _handler == null ? new CommandReadModel(request.CommandRequestId, request.InteractionId, PresentationCommandStatus.Accepted) : _handler(request); }
		}

		private sealed class RecordingPlatformFiles : IPlatformFileInteractionAdapter {
			private readonly IReadOnlyList<string> _paths;
			public PlatformPathRequest LastRequest { get; private set; }
			public RecordingPlatformFiles(params string[] paths) { _paths = paths; }
			public void PickPath(PlatformPathRequest request, Action<PlatformPathResult> completed) { LastRequest = request; completed?.Invoke(new PlatformPathResult(request.RequestId, request.ProjectSessionId, true, _paths)); }
			public void Cancel(Guid requestId) { }
		}
	}
}
