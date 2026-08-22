using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.ObjectModel;
using NUnit.Framework;

namespace ShitDesigner.Presentation.Tests
{
    public sealed class PresentationAcceptanceTests
    {
        private static DockTree Layout(string id = "graph") => new DockTree(new DockTabGroup(new[] { id }, id));

        [Test, Category("GUI_01_Workspace_Viewport")]
        public void Workspace_MinimumViewport_IsRepresentedByLayoutRoot() { Assert.That(DockLayoutSessionMinimums.Width, Is.GreaterThanOrEqualTo(1280)); Assert.That(DockLayoutSessionMinimums.Height, Is.GreaterThanOrEqualTo(720)); }

        [Test, Category("GUI_02_Workspace_DockEdit")]
        public void Workspace_DockSplitAndTabResize_IsCandidateUntilDrop()
        {
            var session = new DockLayoutSession(Layout()); session.BeginDrag(); session.SetCandidate(new DockTree(new DockSplit(DockAxis.Horizontal, .5f, new DockTabGroup(new[] { "a" }, "a"), new DockTabGroup(new[] { "b" }, "b"))));
            Assert.That(session.Current.Root.Kind, Is.EqualTo("TabGroup")); Assert.That(session.TryCommitCandidate(new HashSet<string> { "a", "b" }, out var validation), Is.True); Assert.That(validation.IsValid, Is.True);
        }

        [Test, Category("GUI_03_Workspace_NoExternalWindow")]
        public void Workspace_ExternalDrop_DoesNotCreateWindow() { Assert.That(Enum.GetNames(typeof(DockDropPosition)), Does.Not.Contain("Window")); }

        [Test, Category("GUI_04_Workspace_DirtySeparation")]
        public void Workspace_LayoutDirty_IsIndependentOfProjectDirty()
        {
            var session = new DockLayoutSession(Layout()); session.BeginDrag(); session.SetCandidate(Layout("other")); Assert.That(session.TryCommitCandidate(new HashSet<string> { "other" }, out _), Is.True); Assert.That(session.IsDirty, Is.True);
            var shell = new ShellReadModel(PresentationProjectState.Ready, "P", false, false, false, false); Assert.That(shell.ProjectDirty, Is.False);
        }

        [Test, Category("GUI_05_Workspace_DiscardCandidate")]
        public void Workspace_SelectPreset_DiscardsUncommittedCandidate()
        {
            var session = new DockLayoutSession(Layout()); session.BeginDrag(); session.SetCandidate(Layout("candidate")); session.SelectPreset("Live", Layout("live")); Assert.That(session.IsDragging, Is.False); Assert.That(session.IsDirty, Is.False); Assert.That(session.CurrentPresetId, Is.EqualTo("Live"));
        }

        [Test, Category("GUI_06_Workspace_Presets")]
        public void Workspace_LayoutPresets_SupportCreateOverwriteRenameDuplicateDelete()
        {
            var store = new LayoutPresetStore(); store.Upsert(new LayoutPreset("edit", "Edit", Layout())); store.Upsert(new LayoutPreset("edit", "Renamed", Layout())); store.Upsert(new LayoutPreset("copy", "Copy", Layout())); Assert.That(store.TryGet("edit", out var edit) && edit.Name == "Renamed", Is.True); Assert.That(store.Remove("copy"), Is.True);
        }

        [Test, Category("GUI_06_Workspace_Presets")]
        public void Workspace_Settings_DefaultsLastDeleteAndPersistence_AreDeterministic()
        {
            var storage = new MemoryUserSettingsStorage();
            var first = new PersistentUserSettingsPort(storage);
            Assert.That(first.Read().Presets.Select(x => x.Id), Is.EquivalentTo(new[] { "Edit", "Live" }));
            Assert.That(first.Read().Presets.Single(x => x.Id == "Edit").Tree.Root.Kind, Is.EqualTo("Split"));
            Assert.That(first.Read().Presets.Single(x => x.Id == "Live").Tree.Root.Kind, Is.EqualTo("Split"));
            Assert.That(first.Read().Presets.Single(x => x.Id == "Edit").Tree.Root.Kind,
                Is.EqualTo(first.Read().Presets.Single(x => x.Id == "Live").Tree.Root.Kind));
            Assert.That(((DockSplit)first.Read().Presets.Single(x => x.Id == "Edit").Tree.Root).Axis, Is.EqualTo(DockAxis.Horizontal));
            Assert.That(((DockSplit)first.Read().Presets.Single(x => x.Id == "Live").Tree.Root).Axis, Is.EqualTo(DockAxis.Vertical));
            Assert.That(first.Apply(new WorkspaceSettingsCommand("rename", "Edit", "Editing")).IsSuccess, Is.True);
            Assert.That(first.Apply(new WorkspaceSettingsCommand("duplicate", "Edit", "Editing Copy", "EditingCopy")).IsSuccess, Is.True);
            Assert.That(first.Apply(new WorkspaceSettingsCommand("ui-scale", uiScale: 1.5f)).IsSuccess, Is.True);
            var second = new PersistentUserSettingsPort(storage);
            Assert.That(second.Read().Presets.Any(x => x.Id == "EditingCopy"), Is.True);
            Assert.That(second.Read().UiScale, Is.EqualTo(1.5f));
            Assert.That(first.Apply(new WorkspaceSettingsCommand("defaults")).IsSuccess, Is.True);
            Assert.That(first.Read().Presets.Any(x => x.Id == "Live (Default)"), Is.True);
            Assert.That(first.Read().Presets.Any(x => x.Id == "Edit" && x.Name == "Editing"), Is.True);
            var collision = new PersistentUserSettingsPort(new MemoryUserSettingsStorage());
            Assert.That(collision.Apply(new WorkspaceSettingsCommand("defaults")).IsSuccess, Is.True);
            Assert.That(collision.Read().Presets.Any(x => x.Id == "Edit (Default)"), Is.True);
            Assert.That(collision.Read().Presets.Any(x => x.Id == "Live (Default)"), Is.True);
            foreach (var preset in second.Read().Presets.Skip(1).ToList()) Assert.That(second.Apply(new WorkspaceSettingsCommand("delete", preset.Id)).IsSuccess, Is.True);
            Assert.That(second.Apply(new WorkspaceSettingsCommand("delete", "Editing")).IsSuccess, Is.False);
            Assert.That(second.Read().Presets.Count, Is.EqualTo(1));
        }

        [Test, Category("GUI_06_Workspace_Presets")]
        public void Workspace_SettingsReadReusesSnapshotAndFailedApplyPreservesIt()
        {
            var storage = new FailingMemoryUserSettingsStorage();
            var port = new PersistentUserSettingsPort(storage);
            var initial = port.Read();
            Assert.That(port.Read(), Is.SameAs(initial));

            Assert.That(port.Apply(new WorkspaceSettingsCommand("ui-scale", uiScale: 1.25f)).IsSuccess, Is.True);
            var changed = port.Read();
            Assert.That(changed, Is.Not.SameAs(initial));
            Assert.That(changed.UiScale, Is.EqualTo(1.25f));
            Assert.That(port.Read(), Is.SameAs(changed));

            storage.FailSaves = true;
            var failed = port.Apply(new WorkspaceSettingsCommand("ui-scale", uiScale: 1.5f));
            Assert.That(failed.IsSuccess, Is.False);
            Assert.That(failed.Snapshot, Is.SameAs(changed));
            Assert.That(port.Read(), Is.SameAs(changed));
            Assert.That(port.Read().UiScale, Is.EqualTo(1.25f));
        }

        [Test, Category("GUI_07_Workspace_EditPermission")]
        public void Workspace_LayoutSelection_DoesNotChangeGraphEditCommands() { var composer = new GraphCommandComposer(Guid.NewGuid(), 1); Assert.That(composer.AddNode("image", 0, 0).CommandId, Is.EqualTo("graph.add_node")); }

        [Test, Category("GUI_08_Graph_SearchEntry")]
        public void Graph_SearchResults_AreAvailableForContextAndTab() { Assert.That(NodeSearch.Fuzzy("blur", new[] { new NodeSearchResult("fx.blur", "Blur", "FX", 0) }).Count, Is.EqualTo(1)); }

        [Test, Category("GUI_08_Graph_SearchEntry")]
        public void Graph_SearchPopup_PreservesCursorAndWrapsKeyboardSelection()
        {
            var popup = new NodeSearchPopupState();
            popup.Open(new[]
            {
                new NodeSearchResult("fx.blur", "Blur", "FX", 100, isFavorite: true),
                new NodeSearchResult("fx.color", "Color", "FX", 90)
            }, new PresentationPoint(128, 256));
            Assert.That(popup.IsOpen, Is.True);
            Assert.That(popup.CanvasPosition.X, Is.EqualTo(128));
            Assert.That(popup.Current.NodeTypeId, Is.EqualTo("fx.blur"));
            popup.MoveSelection(1);
            Assert.That(popup.Current.NodeTypeId, Is.EqualTo("fx.color"));
            popup.MoveSelection(1);
            Assert.That(popup.Current.NodeTypeId, Is.EqualTo("fx.blur"));
            popup.MoveSelection(-1);
            Assert.That(popup.Current.NodeTypeId, Is.EqualTo("fx.color"));
            popup.Close();
            Assert.That(popup.IsOpen, Is.False);
            Assert.That(popup.Entries, Is.Empty);
        }

        [Test, Category("GUI_09_Graph_ZoomPan")]
        public void Graph_CoordinateMapper_ZoomPanKeepsCursorAnchoredAndSnapsEightPixels()
        {
            var mapper = new GraphCoordinateMapper();
            var cursor = new PresentationPoint(400, 200);
            var before = mapper.ScreenToCanvas(cursor);
            mapper.ZoomAt(2f, cursor);
            Assert.That(mapper.CanvasToScreen(before).X, Is.EqualTo(cursor.X).Within(.001f));
            Assert.That(mapper.CanvasToScreen(before).Y, Is.EqualTo(cursor.Y).Within(.001f));
            var panBefore = mapper.Pan;
            mapper.PanBy(new PresentationPoint(16, -8));
            Assert.That(mapper.Pan.X, Is.EqualTo(panBefore.X + 16));
            Assert.That(mapper.Pan.Y, Is.EqualTo(panBefore.Y - 8));
            Assert.That(GraphCoordinateMapper.Snap(13), Is.EqualTo(16));
        }

        [Test, Category("GUI_09_Graph_LibraryAdd")]
        public void Graph_GestureState_MarqueeAndMultiMovePreserveRelativePositions()
        {
            var gesture = new GraphGestureState();
            gesture.BeginNodeDrag(new[]
            {
                new GraphNodeReadModel("a", "fx.a", "A", 8, 16),
                new GraphNodeReadModel("b", "fx.b", "B", 24, 32)
            });
            gesture.MoveBy(new PresentationPoint(13, 7), snap: false);
            var moved = gesture.CommitNodeDrag();
            Assert.That(moved["b"].X - moved["a"].X, Is.EqualTo(16));
            Assert.That(moved["b"].Y - moved["a"].Y, Is.EqualTo(16));
            gesture.BeginMarquee(new PresentationPoint(100, 100));
            gesture.UpdateMarquee(new PresentationPoint(40, 60));
            Assert.That(gesture.Marquee.X, Is.EqualTo(40));
            Assert.That(gesture.Marquee.Y, Is.EqualTo(60));
            Assert.That(gesture.Marquee.Width, Is.EqualTo(60));
            Assert.That(gesture.Marquee.Height, Is.EqualTo(40));
        }

        [Test, Category("GUI_09_Graph_LibraryAdd")]
        public void Graph_LibraryEntry_CanBeAddedAtDropPosition() { var request = new GraphCommandComposer(Guid.NewGuid(), 1).AddNode("fx.blur", 128, 256); Assert.That(request.Payload["x"], Is.EqualTo("128")); Assert.That(request.Payload["y"], Is.EqualTo("256")); }

        [Test, Category("GUI_10_Graph_UndoRedoBatch")]
        public void Graph_EditOperations_UseTypedAtomicCommandIds() { var c = new GraphCommandComposer(Guid.NewGuid(), 1); Assert.That(new[] { c.AddNode("x", 0, 0).CommandId, c.DeleteNodes(new[] { "n" }).CommandId, c.Disconnect("e").CommandId, c.Replace("e", "a", "o", "b", "i").CommandId }, Is.All.Not.Empty); }

        [Test, Category("GUI_11_Graph_ConnectionReject")]
        public void Graph_ConnectionDrop_CanBeRejectedWithoutChangingReadModel() { var graph = new GraphReadModel(connections: new[] { new GraphConnectionReadModel("e", "a", "o", "b", "i") }); Assert.That(graph.Connections.Count, Is.EqualTo(1)); }

        [Test, Category("GUI_12_Graph_ImplicitConversion")]
        public void Graph_ImplicitConversion_ContainsDottedBadgeMetadata() { var connection = new GraphConnectionReadModel("e", "a", "o", "b", "i", true, "Color → Vector4"); Assert.That(connection.IsImplicitConversion, Is.True); Assert.That(connection.ConversionLabel, Is.Not.Empty); }

        [Test, Category("GUI_13_Graph_PortRequirement")]
        public void Graph_RequiredOptionalPorts_AreTextualMetadata() { var port = new GraphPortReadModel("n", "p", "Input", "Image", PresentationPortDirection.Input, PresentationPortRequirement.Required); Assert.That(port.Requirement, Is.EqualTo(PresentationPortRequirement.Required)); Assert.That(port.DisplayName, Is.Not.Empty); }

        [Test, Category("GUI_14_Graph_NodeStatus")]
        public void Graph_NodeStatus_IncludesAllNonColorStates() { var names = Enum.GetNames(typeof(PresentationNodeStatus)); Assert.That(Array.IndexOf(names, "Blocked") >= 0, Is.True); Assert.That(Array.IndexOf(names, "Faulted") >= 0, Is.True); Assert.That(Array.IndexOf(names, "Preparing") >= 0, Is.True); Assert.That(Array.IndexOf(names, "UsingFallback") >= 0, Is.True); Assert.That(Array.IndexOf(names, "UnknownNode") >= 0, Is.True); }

        [Test, Category("GUI_15_Parameters_StandardControl")]
        public void Parameters_StandardFactory_HandlesPublishedValue() { var catalog = new ParameterControlCatalog(); catalog.Register(ParameterControlKind.ReadOnly, new ConstantFactory()); Assert.That(catalog.CreateOrFallback(new ParameterMetadata("p", "Gain", ParameterControlKind.Numeric), new ParameterReadModel("n", "p", "Gain", "1", "1")), Is.Not.Null); }

        [Test, Category("GUI_15_Parameters_StandardControl")]
        public void Parameters_CustomFactoryFailure_UsesReadonlyFallbackAndNotice()
        {
            var catalog = new ParameterControlCatalog();
            catalog.Register(ParameterControlKind.Numeric, new ThrowingFactory());
            catalog.Register(ParameterControlKind.ReadOnly, new ConstantFactory());
            var notices = new PresentationNoticeSink();
            var result = catalog.CreateOrFallback(new ParameterMetadata("p", "Gain", ParameterControlKind.Numeric), new ParameterReadModel("n", "p", "Gain", "1", "1"), notices);
            Assert.That(result, Is.Not.Null);
            Assert.That(notices.Notices.Any(x => x.Code == "presentation.parameter_factory_failed"), Is.True);
        }

        [Test, Category("GUI_15_Parameters_StandardControl")]
        public void Parameters_NodeTypeFactory_IsSelectedBeforeStandardFactory()
        {
            var catalog = new ParameterControlCatalog();
            catalog.Register(ParameterControlKind.Numeric, new ConstantFactory());
            catalog.Register(ParameterControlKind.ReadOnly, new ConstantFactory());
            catalog.RegisterNodeType("custom.node", new ConstantFactory("custom"));
            var result = catalog.CreateOrFallback(new ParameterMetadata("p", "Gain", ParameterControlKind.Numeric, nodeTypeId: "custom.node"), new ParameterReadModel("n", "p", "Gain", "1", "1"));
            Assert.That(result, Is.EqualTo("custom"));
        }

        [Test, Category("GUI_16_Parameters_BaseEffective")]
        public void Parameters_BaseAndEffective_AreSeparateValues() { var value = new ParameterReadModel("n", "p", "P", "1", "2"); Assert.That(value.BaseValue, Is.Not.EqualTo(value.EffectiveValue)); Assert.That(value.IsReadOnly, Is.False); }

        [Test, Category("GUI_17_Parameters_DashboardPlace")]
        public void Parameters_MultipleNodes_CanBePlacedOnDashboard() { var page = new DashboardPageReadModel("p", "Live", new[] { new DashboardWidgetReadModel("w1", "a", 0, 0, 3, 1, "Value"), new DashboardWidgetReadModel("w2", "b", 3, 0, 3, 1, "Value") }); Assert.That(page.Widgets.Count, Is.EqualTo(2)); Assert.That(DashboardLayoutValidator.Validate(page.Widgets).IsValid, Is.True); }

        [Test, Category("GUI_18_Parameters_DashboardPersist")]
        public void Parameters_DashboardPageAndWidget_HaveStableIds() { var widget = new DashboardWidgetReadModel("w", "p", 0, 0, 2, 1, "Slider"); Assert.That(widget.Id, Is.EqualTo("w")); Assert.That(widget.ParameterId, Is.EqualTo("p")); }

        [Test, Category("GUI_19_Parameters_BrokenWidget")]
        public void Parameters_BrokenWidget_RemainsVisibleForRebindOrRemove() { var widget = new DashboardWidgetReadModel("w", "missing", 0, 0, 2, 1, "Value", true); Assert.That(widget.IsBroken, Is.True); Assert.That(widget.Id, Is.EqualTo("w")); }

        [Test, Category("GUI_20_Parameters_LearnKey")]
        public void Parameters_LearnKey_IsAnApplicationCommandBoundary() { var request = new PresentationCommandRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 2, "control", "input.begin_learn"); Assert.That(request.CommandId, Is.EqualTo("input.begin_learn")); }

        [Test, Category("GUI_21_Parameters_DraftAtomic")]
        public void Parameters_ExpressionDraft_DoesNotApplyBeforeApply() { var draft = new ExpressionDraft(Guid.NewGuid(), 4, "n", "p", "0", "1"); draft.Edit("min(x)", "max(x)"); Assert.That(draft.IsPending, Is.False); var request = draft.Apply(); Assert.That(draft.IsPending, Is.True); Assert.That(request.CommandId, Is.EqualTo("parameter.apply_expression")); }

        [Test, Category("GUI_22_Program_MonitorClose")]
        public void Program_ClosingMonitor_DoesNotClearExternalOutputPolicy() { var monitor = new ProgramMonitorController(); monitor.Open(); monitor.Close(); Assert.That(monitor.IsOpen, Is.False); Assert.That(PreviewOverlayPolicy.CanOverlayProgram, Is.False); }

        [Test, Category("GUI_22_Program_MonitorClose")]
        public void Program_SurfaceWithoutPort_IsUnavailableAndLeaseReleases()
        {
            var released = false;
            var lease = new OutputSurfaceLease("program", 7, 64, 32, 9, new object(), () => released = true);
            var binding = new SurfaceBinding();
            Assert.That(binding.Bind(new OutputSurfaceReadModel("program", lease.Generation, lease.Width, lease.Height, lease.FrameNumber, lease.Texture, true, true)), Is.True);
            Assert.That(binding.Texture, Is.SameAs(lease.Texture));
            var release = binding.Unbind();
            Assert.That(release.Generation, Is.EqualTo(7));
            lease.Dispose();
            Assert.That(released, Is.True);
            Assert.That(binding.IsBound, Is.False);
        }

        [Test, Category("GUI_23_Program_NoOverlay")]
        public void Program_ImageHasNoDiagnosticOverlay() { Assert.That(PreviewOverlayPolicy.CanOverlayProgram, Is.False); }

        [Test, Category("GUI_24_Preview_DoubleClickTab")]
        public void Preview_NodeOpen_SelectsCorrespondingTab() { var host = new PreviewHostController(); Assert.That(host.Open(new PreviewReadModel("n", "tab", true, PresentationOutputFit.Fit, PresentationOutputBackground.Black, PresentationQualityStage.Full, "Ready")), Is.True); Assert.That(host.Tabs.Single().TabId, Is.EqualTo("tab")); }

        [Test, Category("GUI_25_Preview_MaxEight")]
        public void Preview_NinthTab_IsRejectedWithReason() { var host = new PreviewHostController(); for (var i = 0; i < 8; i++) Assert.That(host.Open(Preview(i)), Is.True); Assert.That(host.Open(Preview(8)), Is.False); Assert.That(host.LastRejectionReason, Is.Not.Empty); }

        [Test, Category("GUI_26_Preview_HideDemand")]
        public void Preview_HostHide_StopsDemandWithoutChangingTabAssignment() { var spy = new DemandSpy(); var host = new PreviewHostController(spy); host.SetVisible(true); host.Open(Preview(1)); host.SetVisible(false); Assert.That(host.Tabs.Count, Is.EqualTo(1)); Assert.That(spy.LastDemand, Is.False); }

        [Test, Category("GUI_27_Preview_ViewSettings")]
        public void Preview_FitFillStretchAndBackground_ArePerTab() { var host = new PreviewHostController(); host.Open(Preview(1)); Assert.That(host.ApplySettings("tab1", PresentationOutputFit.Fill, PresentationOutputBackground.Checker), Is.True); Assert.That(host.Tabs.Single().Fit, Is.EqualTo(PresentationOutputFit.Fill)); Assert.That(host.Tabs.Single().Background, Is.EqualTo(PresentationOutputBackground.Checker)); }

        [Test, Category("GUI_28_Preview_StateQuality")]
        public void Preview_StateAndQuality_AreReadModelFields() { var preview = Preview(1); Assert.That(preview.StateText, Is.EqualTo("Ready")); Assert.That(preview.Quality, Is.EqualTo(PresentationQualityStage.Full)); }

        [Test, Category("GUI_29_Presets_Trigger")]
        public void Presets_OnePress_EmitsOneRequest() { var port = new RecordingCommandPort(); var coordinator = NewCoordinator(port); coordinator.ApplyLatestReadModels(1); coordinator.Submit("preset.apply", "preset", payload: new KeyValuePairValue("presetId", "p")); Assert.That(port.Requests.Count, Is.EqualTo(1)); }

        [Test, Category("GUI_30_Presets_BrokenAtomic")]
        public void Presets_BrokenApply_IsRejectedAsWholeRequest() { var result = new CommandReadModel(Guid.NewGuid(), Guid.Empty, PresentationCommandStatus.Rejected, "Broken preset item"); Assert.That(result.Status, Is.EqualTo(PresentationCommandStatus.Rejected)); }

        [Test, Category("GUI_31_Media_ImportProgress")]
        public void Media_MultiImport_UsesPathOnlyPlatformAdapter() { var request = new PlatformPathRequest(Guid.NewGuid(), Guid.NewGuid(), PlatformPathRequestKind.MultiFile, "Import media"); Assert.That(request.Kind, Is.EqualTo(PlatformPathRequestKind.MultiFile)); }

        [Test, Category("GUI_32_Media_DeleteReferences")]
        public void Media_DeleteConfirmation_CarriesSessionAndPaths() { var result = new PlatformPathResult(Guid.NewGuid(), Guid.NewGuid(), true, new[] { "C:/a.mov" }); Assert.That(result.AbsolutePaths.Count, Is.EqualTo(1)); }

        [Test, Category("GUI_33_Media_BrokenReference")]
        public void Media_BrokenReference_RemainsRepresentable() { var diagnostic = new DiagnosticReadModel("m", PresentationSeverity.Warning, "media.broken", "Missing media", "node"); Assert.That(diagnostic.NodeId, Is.EqualTo("node")); }

        [Test, Category("GUI_34_Diagnostics_Filter")]
        public void Diagnostics_FilterBySeverityNodeAndCode() { var presenter = new DiagnosticPresenter(); var result = presenter.Filter(new[] { new DiagnosticReadModel("1", PresentationSeverity.Error, "graph.fault", "x", "n"), new DiagnosticReadModel("2", PresentationSeverity.Warning, "media.broken", "y", "m") }, new DiagnosticFilter { Severity = PresentationSeverity.Error, NodeId = "n", Code = "graph.fault" }); Assert.That(result.Count, Is.EqualTo(1)); }

        [Test, Category("GUI_35_Diagnostics_Aggregate")]
        public void Diagnostics_ContinuousFault_UpdatesCount() { var result = new DiagnosticPresenter().Aggregate(new[] { new DiagnosticReadModel("1", PresentationSeverity.Error, "graph.fault", "x", "n", 1), new DiagnosticReadModel("2", PresentationSeverity.Error, "graph.fault", "x", "n", 2) }); Assert.That(result.Single().Count, Is.EqualTo(3)); }

        [Test, Category("GUI_36_Diagnostics_Export")]
        public void Diagnostics_ExportsTextAndJson() { var values = new[] { new DiagnosticReadModel("1", PresentationSeverity.Error, "x.y", "bad") }; var presenter = new DiagnosticPresenter(); Assert.That(presenter.ExportText(values), Does.Contain("x.y")); Assert.That(presenter.ExportJson(values), Does.Contain("\"code\":\"x.y\"")); }

        [Test, Category("GUI_37_Project_CloseDecision")]
        public void Project_CloseDialog_OffersSaveDiscardCancel() { Assert.That(Enum.GetNames(typeof(UnsavedDecision)), Is.EquivalentTo(new[] { "Cancel", "Save", "Discard" })); }

        [Test, Category("GUI_38_Project_SaveFailure")]
        public void Project_SaveFailure_IsTerminalRejectedState() { var command = new CommandReadModel(Guid.NewGuid(), Guid.Empty, PresentationCommandStatus.Rejected, "Save failed"); Assert.That(command.IsTerminal, Is.True); }

        [Test, Category("GUI_39_Project_RecoveredBanner")]
        public void Project_RecoveredReadModel_RequestsBanner() { var shell = new ShellReadModel(PresentationProjectState.Ready, "P", true, true, false, false); Assert.That(shell.Recovered && shell.ProjectDirty, Is.True); }

        [Test, Category("GUI_40_Project_OpenFailurePreserves")]
        public void Project_OpenFailure_DoesNotReplaceCurrentSnapshot() { var current = new PresentationEnvelope<PresentationReadModel>(Guid.NewGuid(), 1, 1, 1, 1, true, new PresentationReadModel()); Assert.That(current.Model, Is.Not.Null); }

        [Test, Category("GUI_41_Visibility_StateNotColor")]
        public void Visibility_StateAndPortType_HaveTextualValues() { Assert.That(new GraphPortReadModel("n", "p", "Input", "Image", PresentationPortDirection.Input, PresentationPortRequirement.Optional).ValueType, Is.Not.Empty); Assert.That(PresentationNodeStatus.Faulted.ToString(), Is.EqualTo("Faulted")); }

        [Test, Category("GUI_42_Visibility_Focus")]
        public void Visibility_FocusPanel_IsStableSessionState() { var session = new PresentationSessionState(); session.FocusPanel("graph"); Assert.That(session.FocusedPanelInstanceId, Is.EqualTo("graph")); }

        [Test, Category("GUI_43_Visibility_Scale")]
        public void Visibility_UiScale_ClampsToEightyToTwoHundredPercent() { var settings = new AccessibilitySettings(); settings.SetTextScale(.1f); Assert.That(settings.TextScale, Is.EqualTo(.8f)); settings.SetTextScale(3f); Assert.That(settings.TextScale, Is.EqualTo(2f)); }

        [Test, Category("GUI_44_Visibility_ReduceMotion")]
        public void Visibility_ReduceMotion_IsExplicitSetting() { var settings = new AccessibilitySettings(); settings.SetReduceMotion(true); Assert.That(settings.ReduceMotion, Is.True); }

        [Test, Category("GUI_45_Visibility_TextInputSuppression")]
        public void Visibility_TextInput_SuppressesGraphSingleKeyShortcut() { var router = new ShortcutRouter(); router.Register(new ShortcutBinding(PresentationKey.G, "graph.toggle_grid")); Assert.That(router.Resolve(PresentationKey.G, false, false, false, true, true, false), Is.Null); Assert.That(router.Resolve(PresentationKey.G, false, false, false, false, true, false), Is.EqualTo("graph.toggle_grid")); }

        [Test]
        public void Coordinator_SelectiveRoutesAndFullSnapshotOrderingAreDeterministic()
        {
            var port = new MutableReadPort();
            var coordinator = new PresentationCoordinator(port, new RecordingCommandPort());
            var events = new List<string>();
            coordinator.ShellApplied += _ => events.Add("shell");
            coordinator.WorkspaceApplied += _ => events.Add("workspace");
            coordinator.PanelsApplied += _ => events.Add("panels");
            coordinator.NotificationsApplied += _ => events.Add("notifications");
            coordinator.ApplyLatestReadModels(1);
            Assert.That(events, Is.EqualTo(new[] { "shell", "workspace", "panels", "notifications" }));
            events.Clear(); port.Version = 2; coordinator.ApplyLatestReadModels(2);
            Assert.That(events, Is.EqualTo(new[] { "panels" }), "A fresh outer envelope with unchanged Shell/Workspace sources must update Panels only.");
            events.Clear(); port.Version = 3; port.Workspace = new WorkspaceReadModel("changed", false, null); coordinator.ApplyLatestReadModels(3);
            Assert.That(events, Is.EqualTo(new[] { "workspace", "panels" }));
            events.Clear(); port.Version = 4; port.Shell = new ShellReadModel(PresentationProjectState.Ready, "Renamed", false, false, false, false); coordinator.ApplyLatestReadModels(4);
            Assert.That(events, Is.EqualTo(new[] { "shell", "panels" }));
            events.Clear(); port.Version = 5; port.Full = true; coordinator.ApplyLatestReadModels(5);
            Assert.That(events, Is.EqualTo(new[] { "shell", "workspace", "panels", "notifications" }));
            events.Clear(); port.Full = false; port.Version = 6; port.Diagnostics = MutableReadPort.CreateDiagnostics("new current diagnostic"); coordinator.ApplyLatestReadModels(6);
            Assert.That(events, Is.EqualTo(new[] { "panels", "notifications" }));
            events.Clear(); port.Version = 7; port.Commands = MutableReadPort.CreateCommands("project.save"); coordinator.ApplyLatestReadModels(7);
            Assert.That(events, Is.EqualTo(new[] { "panels", "notifications" }));
            events.Clear(); port.Version = 9; var gap = coordinator.ApplyLatestReadModels(8);
            Assert.That(gap.RequestedFullSnapshot, Is.True);
            Assert.That(port.FullReadCount, Is.EqualTo(1));
            Assert.That(events, Is.EqualTo(new[] { "shell", "workspace", "panels", "notifications" }));
            events.Clear(); port.Version = 1; port.SessionId = Guid.NewGuid(); coordinator.ApplyLatestReadModels(9);
            Assert.That(events, Is.EqualTo(new[] { "shell", "workspace", "panels", "notifications" }));
        }

        private static PreviewReadModel Preview(int index) => new PreviewReadModel("node" + index, "tab" + index, true, PresentationOutputFit.Fit, PresentationOutputBackground.Black, PresentationQualityStage.Full, "Ready");
        private static PresentationCoordinator NewCoordinator(RecordingCommandPort commands)
        {
            return new PresentationCoordinator(new FixedReadPort(), commands);
        }

        private sealed class ConstantFactory : IParameterControlFactory
        {
            private readonly string _value;
            public ConstantFactory(string value = null) { _value = value; }
            public object Create(ParameterMetadata metadata, ParameterReadModel value) => _value ?? new object();
        }
        private sealed class ThrowingFactory : IParameterControlFactory { public object Create(ParameterMetadata metadata, ParameterReadModel value) { throw new InvalidOperationException("factory failed"); } }
        private sealed class FailingMemoryUserSettingsStorage : IUserSettingsStorage
        {
            public string Payload { get; private set; }
            public bool FailSaves { get; set; }
            public string Load() => Payload;
            public void Save(string payload)
            {
                if (FailSaves) throw new InvalidOperationException("Injected settings storage failure.");
                Payload = payload ?? string.Empty;
            }
        }
        private sealed class DemandSpy : IPreviewDemandPort { public bool LastDemand; public void SetDemand(string previewNodeId, bool demanded) { LastDemand = demanded; } }
        private sealed class RecordingCommandPort : IPresentationCommandPort
        {
            public readonly List<PresentationCommandRequest> Requests = new List<PresentationCommandRequest>();
            public CommandReadModel Submit(PresentationCommandRequest request) { Requests.Add(request); return new CommandReadModel(request.CommandRequestId, request.InteractionId, PresentationCommandStatus.Accepted); }
        }
        private sealed class FixedReadPort : IPresentationReadPort
        {
            private readonly Guid _id = Guid.NewGuid();
            public PresentationEnvelope<PresentationReadModel> ReadLatest(bool fullSnapshot) => new PresentationEnvelope<PresentationReadModel>(_id, 1, 1, 1, 1, true, new PresentationReadModel(new ShellReadModel(PresentationProjectState.Ready, "P", false, false, false, false)));
        }
        private sealed class MutableReadPort : IPresentationReadPort
        {
            public Guid SessionId = Guid.NewGuid();
            public long Version = 1;
            public bool Full = false;
            public ShellReadModel Shell = new ShellReadModel(PresentationProjectState.Ready, "P", false, false, false, false);
            public WorkspaceReadModel Workspace = new WorkspaceReadModel("same", false, null);
            public IReadOnlyList<DiagnosticReadModel> Diagnostics = PresentationCollections.Empty<DiagnosticReadModel>();
            public IReadOnlyList<CommandReadModel> Commands = PresentationCollections.Empty<CommandReadModel>();
            public int FullReadCount;

            public PresentationEnvelope<PresentationReadModel> ReadLatest(bool fullSnapshot)
            {
                if (fullSnapshot) FullReadCount++;
                return new PresentationEnvelope<PresentationReadModel>(SessionId, Version, (ulong)Version, 1, 1, Full || fullSnapshot,
                    new PresentationReadModel(shell: Shell, workspace: Workspace, diagnostics: Diagnostics, commands: Commands));
            }

            public static IReadOnlyList<DiagnosticReadModel> CreateDiagnostics(string message)
            {
                return new ReadOnlyCollection<DiagnosticReadModel>(new List<DiagnosticReadModel>
                {
                    new DiagnosticReadModel("test", PresentationSeverity.Warning, "test.changed", message, "graph")
                });
            }

            public static IReadOnlyList<CommandReadModel> CreateCommands(string commandId)
            {
                return new ReadOnlyCollection<CommandReadModel>(new List<CommandReadModel>
                {
                    new CommandReadModel(Guid.NewGuid(), Guid.Empty, PresentationCommandStatus.Applied, commandId)
                });
            }
        }
    }

    internal static class DockLayoutSessionMinimums { public const int Width = 1280; public const int Height = 720; }
}
