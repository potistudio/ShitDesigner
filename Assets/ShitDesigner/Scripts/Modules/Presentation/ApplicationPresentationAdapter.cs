using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Application;
using ShitDesigner.Core;

namespace ShitDesigner.Presentation {
	/// <summary>
	/// The only Application-aware type in Presentation.  It translates the
	/// Application facade's immutable read port into UI projections; it never
	/// exposes Project, Graph or Runtime objects to a View.
	/// </summary>
	public sealed class ApplicationPresentationAdapter : IPresentationReadPort, IPresentationCommandPort, IDisposable {
		private readonly IProjectApplicationReadPort _read;
		private readonly IApplicationCommandPort _commands;
		private readonly IOutputSurfacePort _outputSurfaces;
		private readonly IUserSettingsPort _userSettings;
		private OutputSurfaceLease _programLease;
		private readonly Dictionary<string, OutputSurfaceLease> _previewLeases = new Dictionary<string, OutputSurfaceLease>(StringComparer.Ordinal);
		private object _shellSource;
		private bool _shellPaused;
		private ShellReadModel _cachedShell;
		private bool _shellCached;
		private object _catalogSource;
		private IReadOnlyList<NodeCatalogItem> _cachedCatalog;
		private object _controlsSource;
		private object _controlRuntimeSource;
		private IReadOnlyList<LogicalControlReadModel> _cachedControls;
		private object _dashboardSource;
		private IReadOnlyList<DashboardPageReadModel> _cachedDashboards;
		private object _presetSource;
		private IReadOnlyList<PresetListItemReadModel> _cachedPresets;
		private object _mediaSource;
		private IReadOnlyList<MediaListItemReadModel> _cachedMedia;
		private object _workspaceSource;
		private object _workspaceSettingsSource;
		private WorkspaceReadModel _cachedWorkspace;
		private bool _workspaceCached;
		private object _graphSource;
		private object _parameterSource;
		private GraphReadModel _cachedGraph;
		private IReadOnlyList<ParameterReadModel> _cachedParameters;
		private bool _graphCached;
		private object _taskSource;
		private PresentationTaskReadModel _cachedTask;
		private bool _taskCached;
		private object _commandSource;
		private IReadOnlyList<CommandReadModel> _cachedCommands;
		private bool _commandCached;
		private object _diagnosticSource;
		private IReadOnlyList<DiagnosticReadModel> _cachedDiagnostics;
		private bool _diagnosticCached;
		private long _version;

		public ApplicationPresentationAdapter(IProjectApplicationReadPort read, IApplicationCommandPort commands, IOutputSurfacePort outputSurfaces = null, IUserSettingsPort userSettings = null) {
			_read = read ?? throw new ArgumentNullException(nameof(read));
			_commands = commands ?? throw new ArgumentNullException(nameof(commands));
			_outputSurfaces = outputSurfaces;
			_userSettings = userSettings ?? new InMemoryUserSettingsPort();
		}

		public PresentationEnvelope<PresentationReadModel> ReadLatest(bool fullSnapshot) {
			var application = _read.ReadModel;
			var shellEnvelope = application?.Shell;
			var shellModel = shellEnvelope?.Model;
			var paused = application?.Output?.Model?.IsPaused ?? false;
			if (!_shellCached || !ReferenceEquals(_shellSource, shellModel) || _shellPaused != paused) {
				_shellCached = true;
				_shellSource = shellModel;
				_shellPaused = paused;
				var state = MapState(shellModel == null ? ApplicationProjectState.Empty : shellModel.State);
				// Graph-clock frame identity is carried by the fresh outer
				// envelope and applied from the dynamic Panels route. Keeping
				// this static slice source-keyed prevents a 60 Hz shell tree
				// update when only a frame counter advanced.
				_cachedShell = new ShellReadModel(state, shellModel?.ProjectName, shellModel?.IsDirty ?? false,
					shellModel?.IsRecovered ?? application?.IsRecovered ?? false, shellModel?.CanUndo ?? false, shellModel?.CanRedo ?? false,
					shellModel?.StatusText, shellEnvelope?.FrameNumber ?? 0UL, paused);
			}
			var shell = _cachedShell ?? new ShellReadModel(PresentationProjectState.Empty, string.Empty, false, false, false, false);
			var workspaceModel = application?.Workspace?.Model;
			var settings = _userSettings.Read();
			var currentTree = settings?.CurrentTree;
			var visiblePanelIds = currentTree?.Validate().PanelInstanceIds ?? workspaceModel?.VisiblePanelIds;
			if (!_workspaceCached || !ReferenceEquals(_workspaceSource, workspaceModel) || !ReferenceEquals(_workspaceSettingsSource, settings)) {
				_workspaceCached = true;
				_workspaceSource = workspaceModel;
				_workspaceSettingsSource = settings;
				_cachedWorkspace = new WorkspaceReadModel(settings?.ActivePresetId ?? workspaceModel?.LayoutId, settings?.IsDirty ?? workspaceModel?.IsDirty ?? false,
					visiblePanelIds, settings?.Presets, settings?.UiScale ?? 1f, settings?.ReduceMotion ?? false, currentTree,
					settings?.Theme ?? "Dark", settings?.TooltipDelaySeconds ?? .5f, settings?.MediaLibraryView ?? "Grid", settings?.DiagnosticsExportFolder);
			}
			var workspace = _cachedWorkspace ?? new WorkspaceReadModel(string.Empty, false, null);
			var catalogSource = application?.NodeCatalog?.Model;
			if (!ReferenceEquals(_catalogSource, catalogSource)) {
				_catalogSource = catalogSource;
				_cachedCatalog = PresentationCollections.Copy(catalogSource?.Select(x => new NodeCatalogItem(x.TypeId, x.DisplayName, x.IsAvailable, x.DisabledReason, x.UserAddable, x.Category)));
			}
			var controlsSource = application?.Project?.Model?.LogicalControls;
			var controlRuntimeSource = application?.ControlRuntime;
			if (!ReferenceEquals(_controlsSource, controlsSource) || !ReferenceEquals(_controlRuntimeSource, controlRuntimeSource)) {
				_controlsSource = controlsSource;
				_controlRuntimeSource = controlRuntimeSource;
				_cachedControls = PresentationCollections.Copy(controlsSource?.Select(x => {
					ApplicationControlRuntimeReadModel runtime = null;
					controlRuntimeSource?.TryGetValue(x.Id, out runtime);
					return new LogicalControlReadModel(x.Id, x.Name, x.Kind.ToString(), x.PresetId, x.PresetIsBroken,
						runtime != null && runtime.HasValue ? runtime.Value : (float?)null, runtime != null && runtime.IsFiring);
				}));
			}
			var nodeCatalog = _cachedCatalog;
			var controls = _cachedControls;
			var graphModel = application?.Graph?.Model;
			var parameterSource = application?.Parameters?.Model;
			if (!_graphCached || !ReferenceEquals(_graphSource, graphModel) || !ReferenceEquals(_parameterSource, parameterSource)) {
				_graphCached = true;
				_graphSource = graphModel;
				_parameterSource = parameterSource;
				_cachedParameters = PresentationCollections.Copy(parameterSource?.Select(x => new ParameterReadModel(x.NodeId, x.ParameterId, x.DisplayName, x.BaseValue, x.EffectiveValue, x.IsReadOnly, x.IsBroken, x.IsClamped, x.Error, x.ValueType, x.Expression, x.OutputClamp,
					x.Group, x.Order, x.Description, x.Unit, x.Step, x.HardRange, x.ComponentRanges?.Select(range => new ParameterComponentRangeReadModel(range.Name, range.Minimum, range.Maximum)),
					x.EnumOptions?.Select(option => new ParameterOptionReadModel(option.Id, option.DisplayName)), x.MediaOptions, x.MediaKind, x.NodeTypeId, x.IsVisible)));
				var parametersForGraph = _cachedParameters;
				_cachedGraph = graphModel == null ? new GraphReadModel() : new GraphReadModel(
					graphModel.Nodes?.Select(x => new GraphNodeReadModel(x.Id, x.TypeId, x.DisplayName, x.X, x.Y, MapNodeStatus(x.Status), x.IsPending, x.StatusReason,
						parametersForGraph.Where(parameter => string.Equals(parameter.NodeId, x.Id, StringComparison.Ordinal)).Take(4))),
					graphModel.Ports?.Select(x => new GraphPortReadModel(x.NodeId, x.PortId, x.PortId, x.ValueType, MapPortDirection(x.Direction), x.IsRequired ? PresentationPortRequirement.Required : PresentationPortRequirement.Optional, x.IsConnected)),
					graphModel.Connections?.Select(x => new GraphConnectionReadModel(x.Id, x.FromNodeId, x.FromPortId, x.ToNodeId, x.ToPortId, x.IsImplicitConversion, x.ConversionLabel)));
			}
			var graph = _cachedGraph ?? new GraphReadModel();
			var parameters = _cachedParameters;
			var dashboardSource = application?.Dashboard?.Model;
			if (!ReferenceEquals(_dashboardSource, dashboardSource)) {
				_dashboardSource = dashboardSource;
				_cachedDashboards = PresentationCollections.Copy(dashboardSource?.Select(page => new DashboardPageReadModel(page.Id, page.Name, page.Widgets?.Select(widget => new DashboardWidgetReadModel(widget.Id, widget.ParameterId, widget.Column, widget.Row, widget.Width, widget.Height, widget.Label, widget.IsBroken, widget.NodeId)))));
			}
			var presetSource = application?.Presets?.Model;
			if (!ReferenceEquals(_presetSource, presetSource)) {
				_presetSource = presetSource;
				_cachedPresets = PresentationCollections.Copy(presetSource?.Select(x => new PresetListItemReadModel(x.Id, x.Name, x.IsBroken, x.BrokenReason,
					x.Category, x.SortIndex, x.Entries?.Select(entry => new PresetEntryReadModel(entry.NodeId, entry.ParameterId, entry.ValueType, entry.Value, entry.IsBroken, entry.BrokenReason)))));
			}
			var mediaSource = application?.Media?.Model;
			if (!ReferenceEquals(_mediaSource, mediaSource)) {
				_mediaSource = mediaSource;
				_cachedMedia = PresentationCollections.Copy(mediaSource?.Select(x => new MediaListItemReadModel(x.Id, x.RelativePath, x.Status, x.BrokenReason, x.ReferenceCount,
					displayName: x.Id, byteSize: x.Size, integrityHash: x.IntegrityHash, kind: x.Kind, colorSpace: x.ColorSpace, alphaMode: x.AlphaMode)));
			}
			var dashboards = _cachedDashboards;
			var presets = _cachedPresets;
			var media = _cachedMedia;
			var taskModel = application?.Task?.Model;
			if (!_taskCached || !ReferenceEquals(_taskSource, taskModel)) {
				_taskCached = true;
				_taskSource = taskModel;
				_cachedTask = taskModel == null ? null : new PresentationTaskReadModel(taskModel.TaskId, taskModel.Kind, taskModel.Stage, taskModel.Status, taskModel.CompletedItems, taskModel.TotalItems, taskModel.CurrentItem, taskModel.Diagnostic?.Message);
			}
			var task = _cachedTask;
			var output = application?.Output?.Model == null ? null : BuildOutput(application.Output.Model);
			IEnumerable<PendingCommandReadModel> commandModels = application?.CommandResults?.Model ?? application?.Commands;
			if (!_commandCached || !ReferenceEquals(_commandSource, commandModels)) {
				_commandCached = true;
				_commandSource = commandModels;
				_cachedCommands = PresentationCollections.Copy(commandModels?.Select(command => new CommandReadModel(command.CommandRequestId, command.InteractionId, MapStatus(command.Status), command.Diagnostic?.Message)));
			}
			var commands = _cachedCommands;
			var diagnosticReadModel = application?.DiagnosticModel?.Model;
			if (!_diagnosticCached || !ReferenceEquals(_diagnosticSource, diagnosticReadModel)) {
				_diagnosticCached = true;
				_diagnosticSource = diagnosticReadModel;
				var diagnosticItems = new List<DiagnosticReadModel>();
				if (diagnosticReadModel != null) {
					foreach (var diagnostic in diagnosticReadModel.Current ?? Array.Empty<ApplicationDiagnosticReadModel>())
						diagnosticItems.Add(new DiagnosticReadModel(diagnostic.EntryId, MapSeverity(diagnostic.Severity), diagnostic.Code, diagnostic.Message,
							diagnostic.NodeId, (int)diagnostic.Count, isCurrent: true, firstFrame: diagnostic.FirstFrame, lastFrame: diagnostic.LastFrame));
					foreach (var diagnostic in diagnosticReadModel.History ?? Array.Empty<ApplicationDiagnosticReadModel>())
						if (!diagnosticItems.Any(x => string.Equals(x.EntryId, diagnostic.EntryId, StringComparison.Ordinal)))
							diagnosticItems.Add(new DiagnosticReadModel(diagnostic.EntryId, MapSeverity(diagnostic.Severity), diagnostic.Code, diagnostic.Message,
								diagnostic.NodeId, (int)diagnostic.Count, isCurrent: false, firstFrame: diagnostic.FirstFrame, lastFrame: diagnostic.LastFrame));
				}
				_cachedDiagnostics = PresentationCollections.Copy(diagnosticItems);
			}
			var diagnostics = _cachedDiagnostics;
			var projectSessionId = shellEnvelope?.ProjectSessionId ?? Guid.Empty;
			var envelopeVersion = shellEnvelope?.ReadModelVersion ?? ++_version;
			if (envelopeVersion > _version) _version = envelopeVersion;
			return new PresentationEnvelope<PresentationReadModel>(projectSessionId, envelopeVersion,
				shellEnvelope?.FrameNumber ?? 0, shellEnvelope?.DocumentRevision ?? 0, shellEnvelope?.GraphRevision ?? 0,
				fullSnapshot || shellEnvelope?.IsFullSnapshot != false,
				new PresentationReadModel(shell, workspace, graph, parameters, dashboardPages: dashboards, output: output, diagnostics: diagnostics, commands: commands, nodeCatalog: nodeCatalog, presets: presets, media: media, task: task, controls: controls, recentProjectRoots: application?.RecentProjectRoots));
		}

		public CommandReadModel Submit(PresentationCommandRequest request) {
			if (request == null) throw new ArgumentNullException(nameof(request));
			ApplicationCommandResult result;
			switch (request.CommandId) {
				case "project.new":
					result = _commands.NewProject(request.Payload.TryGetValue("name", out var name) ? name : "Untitled",
						request.Payload.TryGetValue("root", out var root) ? root : string.Empty, ParseUnsavedDecision(request.Payload));
					break;
				case "project.open":
					result = _commands.OpenProject(request.Payload.TryGetValue("root", out var openRoot) ? openRoot : string.Empty, ParseUnsavedDecision(request.Payload));
					break;
				case "project.open_recent":
					var recentIndex = ParseIndex(request.Payload);
					var recentRoots = _read.ReadModel?.RecentProjectRoots;
					if (recentRoots == null || recentIndex < 0 || recentIndex >= recentRoots.Count)
						return new CommandReadModel(request.CommandRequestId, request.InteractionId, PresentationCommandStatus.Rejected, "A valid recent project index is required.");
					result = _commands.OpenProject(recentRoots[recentIndex], ParseUnsavedDecision(request.Payload));
					break;
				case "project.save": result = _commands.SaveProject(); break;
				case "project.save_as": result = _commands.SaveAs(request.Payload.TryGetValue("root", out var saveRoot) ? saveRoot : string.Empty); break;
				case "project.close": result = _commands.CloseProject(ParseUnsavedDecision(request.Payload)); break;
				case "project.exit": result = _commands.Exit(ParseUnsavedDecision(request.Payload)); break;
				case "project.undo": result = _commands.Undo(); break;
				case "project.redo": result = _commands.Redo(); break;
				case "graph.add_node":
					var nodeTypeId = request.PayloadValue("nodeTypeId", request.PayloadValue("type", request.TargetId));
					var nodeId = request.PayloadValue("nodeId");
					if (string.IsNullOrWhiteSpace(nodeId)) nodeId = Guid.NewGuid().ToString("D");
					result = _commands.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode,
						targetId: nodeId, nodeTypeId: nodeTypeId, nodeDisplayName: request.PayloadValue("displayName"),
						positionX: ParseFloat(request.PayloadValue("x")), positionY: ParseFloat(request.PayloadValue("y")),
						requestedDocumentRevision: request.RequestedDocumentRevision, commandRequestId: request.CommandRequestId));
					break;
				case "graph.delete_node":
				case "graph.delete_nodes":
					result = _commands.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.DeleteNode,
						targetId: request.TargetId, requestedDocumentRevision: request.RequestedDocumentRevision, commandRequestId: request.CommandRequestId));
					break;
				case "graph.disconnect":
					result = _commands.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.Disconnect,
						targetId: request.TargetId, requestedDocumentRevision: request.RequestedDocumentRevision, commandRequestId: request.CommandRequestId));
					break;
				case "graph.connect":
					result = _commands.SubmitGraph(ParseConnectionRequest(request, ApplicationGraphEditKind.Connect));
					break;
				case "graph.replace_connection":
				case "graph.replace_input_connection":
					result = _commands.SubmitGraph(ParseConnectionRequest(request, ApplicationGraphEditKind.ReplaceInputConnection));
					break;
				case "graph.set_enabled":
					result = _commands.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.SetEnabled,
						targetId: request.TargetId, enabled: ParseBool(request.PayloadValue("enabled"), true),
						requestedDocumentRevision: request.RequestedDocumentRevision, commandRequestId: request.CommandRequestId));
					break;
				case "graph.undo":
					result = _commands.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.Undo, commandRequestId: request.CommandRequestId));
					break;
				case "graph.redo":
					result = _commands.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.Redo, commandRequestId: request.CommandRequestId));
					break;
				case "graph.copy":
					result = _commands.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.CopySelection,
						targetId: request.TargetId, commandRequestId: request.CommandRequestId));
					break;
				case "graph.paste":
					result = _commands.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.PasteSelection,
						targetId: request.TargetId, commandRequestId: request.CommandRequestId));
					break;
				case "graph.duplicate":
					result = _commands.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.DuplicateSelection,
						targetId: request.TargetId, commandRequestId: request.CommandRequestId));
					break;
				case "graph.focus_selection":
					result = _commands.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.FocusSelection,
						targetId: request.TargetId, commandRequestId: request.CommandRequestId));
					break;
				case "graph.focus_all":
					result = _commands.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.FocusAll,
						commandRequestId: request.CommandRequestId));
					break;
				case "parameter.set_base":
				case "parameter.edit_base":
				case "parameter.apply_base":
					if (!TryParseParameterValue(request.Payload, out var value, out var valueError))
						return new CommandReadModel(request.CommandRequestId, request.InteractionId, PresentationCommandStatus.Rejected, valueError);
					result = _commands.EditParameter(new ApplicationParameterEditRequest(request.PayloadValue("nodeId", request.TargetId), request.PayloadValue("parameterId"), value, request.InteractionId));
					break;
				case "parameter.apply_expression":
					result = _commands.ApplyExpression(ParseExpressionDraft(request));
					break;
				case "control.add":
				case "logical_control.add":
					result = _commands.AddLogicalControl(ParseLogicalControlRequest(request));
					break;
				case "control.mapping.set":
				case "logical_control.mapping.set":
					result = _commands.SetLogicalControlMappings(request.PayloadValue("logicalControlId", request.TargetId), ParseMappings(request.Payload));
					break;
				case "control.rename":
					result = _commands.RenameLogicalControl(request.PayloadValue("logicalControlId", request.TargetId), request.PayloadValue("name"));
					break;
				case "control.targets.set":
					result = _commands.SetLogicalControlTargets(request.PayloadValue("logicalControlId", request.TargetId), ParseTargets(request.Payload));
					break;
				case "control.preset.bind":
				case "logical_control.preset.bind":
					result = _commands.SetPresetTriggerBinding(request.PayloadValue("logicalControlId", request.TargetId), request.PayloadValue("presetId"));
					break;
				case "control.delete":
				case "logical_control.delete":
					result = _commands.DeleteLogicalControl(request.PayloadValue("logicalControlId", request.TargetId));
					break;
				case "control.learn.begin":
				case "logical_control.learn.begin":
					result = _commands.BeginKeyboardLearn(request.PayloadValue("logicalControlId", request.TargetId));
					break;
				case "control.learn.cancel":
				case "logical_control.learn.cancel":
					result = _commands.CancelKeyboardLearn();
					break;
				case "media.import":
					var importPaths = ParsePaths(request.PayloadValue("paths"));
					result = importPaths.Count > 0
						? _commands.ImportMediaBatch(importPaths.Select(path => new ApplicationMediaImportRequest(path, System.IO.Path.GetFileName(path), request.PayloadValue("kind", "Experimental"), request.PayloadValue("colorSpace", "SRgb"), request.PayloadValue("alphaMode", "Opaque"))))
						: _commands.ImportMedia(new ApplicationMediaImportRequest(request.PayloadValue("sourcePath"), request.PayloadValue("displayName"), request.PayloadValue("kind", "Experimental"), request.PayloadValue("colorSpace", "SRgb"), request.PayloadValue("alphaMode", "Opaque")));
					break;
				case "media.import.batch":
					var batchPaths = ParsePaths(request.PayloadValue("paths"));
					result = _commands.ImportMediaBatch(batchPaths.Select(path => new ApplicationMediaImportRequest(path, System.IO.Path.GetFileName(path), request.PayloadValue("kind", "Experimental"), request.PayloadValue("colorSpace", "SRgb"), request.PayloadValue("alphaMode", "Opaque"))));
					break;
				case "media.confirm_import":
					result = _commands.ConfirmMediaImport(ParseBool(request.PayloadValue("approved"), false));
					break;
				case "media.add":
					result = _commands.AddMediaAsset(ParseMediaAssetRequest(request));
					break;
				case "media.rebind":
					result = _commands.RebindMedia(request.PayloadValue("mediaAssetId", request.TargetId), request.PayloadValue("nodeId"), request.PayloadValue("parameterId"));
					break;
				case "media.confirm_delete":
					result = _commands.ConfirmDeleteMedia(request.PayloadValue("mediaAssetId", request.TargetId), Enum.TryParse<ApplicationMediaDeleteDecision>(request.PayloadValue("decision"), true, out var mediaDecision) ? mediaDecision : ApplicationMediaDeleteDecision.Cancel);
					break;
				case "media.inspect_references":
					IReadOnlyList<ApplicationMediaReferenceReadModel> references;
					result = _commands.InspectMediaReferences(request.PayloadValue("mediaAssetId", request.TargetId), out references);
					break;
				case "media.delete":
					result = _commands.DeleteMediaAsset(request.PayloadValue("mediaAssetId", request.TargetId));
					break;
				case "preset.create":
					result = _commands.AddPreset(new ApplicationPresetRequest(request.PayloadValue("presetId", request.TargetId), request.PayloadValue("name", request.TargetId), request.PayloadValue("category"), ParseInt(request.PayloadValue("sortIndex"))));
					break;
				case "preset.rename":
					result = _commands.RenamePreset(request.PayloadValue("presetId", request.TargetId), request.PayloadValue("name"));
					break;
				case "preset.duplicate":
					result = _commands.DuplicatePreset(request.PayloadValue("presetId", request.TargetId), request.PayloadValue("newPresetId"), request.PayloadValue("name"));
					break;
				case "preset.capture_entry":
					if (!TryParseParameterValue(request.Payload, out var presetValue, out var presetError)) return new CommandReadModel(request.CommandRequestId, request.InteractionId, PresentationCommandStatus.Rejected, presetError);
					result = _commands.CapturePresetEntry(request.PayloadValue("presetId", request.TargetId), new ApplicationPresetEntryRequest(request.PayloadValue("nodeId"), request.PayloadValue("parameterId"), presetValue));
					break;
				case "preset.remove_entry":
					result = _commands.RemovePresetEntry(request.PayloadValue("presetId", request.TargetId), request.PayloadValue("nodeId"), request.PayloadValue("parameterId"));
					break;
				case "preset.delete":
					result = _commands.DeletePreset(request.PayloadValue("presetId", request.TargetId));
					break;
				case "dashboard.add_page":
					result = _commands.AddDashboardPage(new ApplicationDashboardPageRequest(request.PayloadValue("pageId", request.TargetId), request.PayloadValue("name", "Dashboard")));
					break;
				case "dashboard.update_page":
					result = _commands.UpdateDashboardPage(new ApplicationDashboardPageRequest(request.PayloadValue("pageId", request.TargetId), request.PayloadValue("name", "Dashboard")));
					break;
				case "dashboard.delete_page":
					result = _commands.DeleteDashboardPage(request.PayloadValue("pageId", request.TargetId));
					break;
				case "dashboard.add_widget":
					result = _commands.AddDashboardWidget(request.PayloadValue("pageId"), new ApplicationDashboardWidgetRequest(request.PayloadValue("widgetId"), request.PayloadValue("nodeId"), request.PayloadValue("parameterId"), ParseInt(request.PayloadValue("column")), ParseInt(request.PayloadValue("row")), ParseInt(request.PayloadValue("width"), 1), ParseInt(request.PayloadValue("height"), 1), request.PayloadValue("label")));
					break;
				case "dashboard.remove_widget":
					result = _commands.RemoveDashboardWidget(request.PayloadValue("pageId"), request.PayloadValue("widgetId", request.TargetId));
					break;
				case "dashboard.rebind_widget":
					result = _commands.RebindDashboardWidget(request.PayloadValue("pageId"), request.PayloadValue("widgetId", request.TargetId), request.PayloadValue("nodeId"), request.PayloadValue("parameterId"));
					break;
				case "preview.open":
					result = _commands.OpenPreview(request.PayloadValue("previewId", request.TargetId));
					break;
				case "preview.close":
					result = _commands.ClosePreview(request.PayloadValue("previewId", request.TargetId));
					break;
				case "output.program.display":
					result = _commands.SetProgramDisplay(ParseInt(request.PayloadValue("display"), 2));
					break;
				case "preview.settings":
					result = _commands.SetPreviewSettings(ParsePreviewSettings(request));
					break;
				case "preview.demand":
					result = _commands.RequestPreviewDemand(new ApplicationOutputDemandRequest(request.PayloadValue("previewId", request.TargetId), request.PayloadValue("portId", "image"), ParseInt(request.PayloadValue("width"), 640), ParseInt(request.PayloadValue("height"), 360), ParseBool(request.PayloadValue("focused"), false)));
					break;
				case "preview.host.visible":
					result = _commands.SetPreviewHostVisible(ParseBool(request.PayloadValue("visible"), true));
					break;
				case "feedback.reset":
					result = _commands.ResetFeedback(request.PayloadValue("nodeId", request.TargetId));
					break;
				case "diagnostics.export":
					result = _commands.ExportDiagnostics(request.PayloadValue("path"), ParseBool(request.PayloadValue("json"), false));
					break;
				case "workspace.layout":
					var layoutId = request.PayloadValue("layoutId", request.TargetId);
					var operation = request.PayloadValue("operation", string.Empty);
					var tree = DockTreeCodec.TryDecode(request.PayloadValue("tree"), out var decodedTree) ? decodedTree : null;
					var settingsResult = _userSettings.Apply(new WorkspaceSettingsCommand(operation, layoutId,
						request.PayloadValue("name"), request.PayloadValue("newLayoutId"), tree,
						ParseNullableFloat(request.PayloadValue("uiScale")), ParseNullableBool(request.PayloadValue("reduceMotion")),
						ParseNullableBool(request.PayloadValue("dirty"))));
					return new CommandReadModel(request.CommandRequestId, request.InteractionId,
						settingsResult.IsSuccess ? PresentationCommandStatus.Applied : PresentationCommandStatus.Rejected, settingsResult.Error);
				case "workspace.ui_scale":
					var scaleResult = _userSettings.Apply(new WorkspaceSettingsCommand("ui-scale", uiScale: ParseNullableFloat(request.PayloadValue("value"))));
					return new CommandReadModel(request.CommandRequestId, request.InteractionId,
						scaleResult.IsSuccess ? PresentationCommandStatus.Applied : PresentationCommandStatus.Rejected, scaleResult.Error);
				case "workspace.reduce_motion":
					var motionResult = _userSettings.Apply(new WorkspaceSettingsCommand("reduce-motion", reduceMotion: ParseNullableBool(request.PayloadValue("value"))));
					return new CommandReadModel(request.CommandRequestId, request.InteractionId,
						motionResult.IsSuccess ? PresentationCommandStatus.Applied : PresentationCommandStatus.Rejected, motionResult.Error);
				case "workspace.theme":
					var themeResult = _userSettings.Apply(new WorkspaceSettingsCommand("theme", theme: request.PayloadValue("value", request.PayloadValue("theme", "Dark"))));
					return new CommandReadModel(request.CommandRequestId, request.InteractionId,
						themeResult.IsSuccess ? PresentationCommandStatus.Applied : PresentationCommandStatus.Rejected, themeResult.Error);
				case "workspace.tooltip_delay":
				case "workspace.tooltip-delay":
					var tooltipResult = _userSettings.Apply(new WorkspaceSettingsCommand("tooltip-delay", tooltipDelaySeconds: ParseNullableFloat(request.PayloadValue("value", request.PayloadValue("seconds")))));
					return new CommandReadModel(request.CommandRequestId, request.InteractionId,
						tooltipResult.IsSuccess ? PresentationCommandStatus.Applied : PresentationCommandStatus.Rejected, tooltipResult.Error);
				case "workspace.media_view":
				case "workspace.media-view":
					var mediaViewResult = _userSettings.Apply(new WorkspaceSettingsCommand("media-view", mediaLibraryView: request.PayloadValue("value", request.PayloadValue("view", "Grid"))));
					return new CommandReadModel(request.CommandRequestId, request.InteractionId,
						mediaViewResult.IsSuccess ? PresentationCommandStatus.Applied : PresentationCommandStatus.Rejected, mediaViewResult.Error);
				case "workspace.diagnostics_folder":
				case "workspace.diagnostics-folder":
					var diagnosticsFolderResult = _userSettings.Apply(new WorkspaceSettingsCommand("diagnostics-folder", diagnosticsExportFolder: request.PayloadValue("path", request.PayloadValue("value"))));
					return new CommandReadModel(request.CommandRequestId, request.InteractionId,
						diagnosticsFolderResult.IsSuccess ? PresentationCommandStatus.Applied : PresentationCommandStatus.Rejected, diagnosticsFolderResult.Error);
				case "workspace.recent.remove":
					var recentRemoveResult = _userSettings.Apply(new WorkspaceSettingsCommand("recent-remove", recentProjectRoot: request.PayloadValue("root", request.TargetId)));
					return new CommandReadModel(request.CommandRequestId, request.InteractionId,
						recentRemoveResult.IsSuccess ? PresentationCommandStatus.Applied : PresentationCommandStatus.Rejected, recentRemoveResult.Error);
				case "preset.apply":
					if (!request.Payload.TryGetValue("presetId", out var presetId) || !PresetId.TryParse(presetId, out var parsedPreset))
						return new CommandReadModel(request.CommandRequestId, request.InteractionId, PresentationCommandStatus.Rejected, "A valid preset ID is required.");
					result = _commands.ApplyPreset(new ApplicationPresetCommandRequest(parsedPreset.Value));
					break;
				default:
					return new CommandReadModel(request.CommandRequestId, request.InteractionId, PresentationCommandStatus.Rejected, "Unsupported Application command: " + request.CommandId);
			}
			return new CommandReadModel(result.CommandRequestId, result.InteractionId, MapStatus(result.Status), result.Diagnostic?.Message);
		}

		public void Dispose() {
			_programLease?.Dispose();
			_programLease = null;
			foreach (var lease in _previewLeases.Values) lease.Dispose();
			_previewLeases.Clear();
		}

		private OutputReadModel BuildOutput(ApplicationOutputReadModel applicationOutput) {
			OutputSurfaceReadModel program;
			if (TryResolveSurface("program", ref _programLease, out var programDescriptor)) {
				program = ToSurfaceReadModel(programDescriptor, true);
			}
			else {
				var appProgram = applicationOutput.Program;
				program = new OutputSurfaceReadModel(appProgram?.Id ?? "program", 0, appProgram?.Width ?? 0, appProgram?.Height ?? 0, applicationOutput.FrameNumber, isProgram: true, isBound: false);
			}
			var previews = new List<PreviewReadModel>();
			foreach (var item in applicationOutput.Previews ?? Array.Empty<ApplicationOutputSurfaceReadModel>()) {
				OutputSurfaceReadModel surface;
				_previewLeases.TryGetValue(item.Id, out var previewLease);
				if (TryResolveSurface(item.Id, ref previewLease, out var previewDescriptor)) {
					_previewLeases[item.Id] = previewLease;
					surface = ToSurfaceReadModel(previewDescriptor, false);
				}
				else {
					_previewLeases.Remove(item.Id);
					surface = new OutputSurfaceReadModel(item.Id, 0, item.Width, item.Height, applicationOutput.FrameNumber, isProgram: false, isBound: false);
				}
				previews.Add(new PreviewReadModel(item.Id, item.Id, item.IsDemanded || item.IsHoldingLastFrame, ParseFit(item.FitMode), ParseBackground(item.BackgroundMode), ParseQuality(item.Quality), item.State, surface));
			}
			var activeIds = new HashSet<string>(previews.Select(x => x.TabId), StringComparer.Ordinal);
			foreach (var old in _previewLeases.Keys.Where(x => !activeIds.Contains(x)).ToList()) { _previewLeases[old].Dispose(); _previewLeases.Remove(old); }
			return new OutputReadModel(program, previews, externalDisplayActive: applicationOutput.ProgramDisplay > 0, programState: applicationOutput.ProgramState, isPaused: applicationOutput.IsPaused, programDisplay: applicationOutput.ProgramDisplay,
				cpuFrameTimeMilliseconds: applicationOutput.CpuFrameTimeMilliseconds,
				gpuFrameTimeMilliseconds: applicationOutput.GpuFrameTimeMilliseconds,
				measuredFramesPerSecond: applicationOutput.MeasuredFramesPerSecond,
				holdingDurationSeconds: applicationOutput.HoldingDurationSeconds,
				holdingCauseNodeId: applicationOutput.HoldingCauseNodeId,
				holdingDiagnosticCode: applicationOutput.HoldingDiagnosticCode,
				programPerformanceWarning: applicationOutput.ProgramPerformanceWarning,
				consecutiveBadProgramFrames: applicationOutput.ConsecutiveBadProgramFrames);
		}

		private bool TryResolveSurface(string surfaceId, ref OutputSurfaceLease current, out OutputSurfaceDescriptor descriptor) {
			descriptor = default(OutputSurfaceDescriptor);
			if (_outputSurfaces == null) {
				current?.Dispose();
				current = null;
				return false;
			}

			if (_outputSurfaces is IOutputSurfaceDescriptorPort described) {
				if (!described.TryDescribe(surfaceId, out descriptor) || !descriptor.IsBound) {
					current?.Dispose();
					current = null;
					return false;
				}
				if (Matches(current, descriptor)) return true;
				if (!described.TryAcquire(surfaceId, out var replacement)) {
					current?.Dispose();
					current = null;
					descriptor = default(OutputSurfaceDescriptor);
					return false;
				}
				var previous = current;
				current = replacement;
				previous?.Dispose();
				descriptor = new OutputSurfaceDescriptor(replacement.SurfaceId, replacement.Generation, replacement.Width,
					replacement.Height, replacement.FrameNumber, replacement.Texture, true);
				return true;
			}

			// Compatibility ports predate descriptor probing. They retain the
			// original acquire-and-release behavior until upgraded.
			if (!_outputSurfaces.TryAcquire(surfaceId, out var acquired)) {
				current?.Dispose();
				current = null;
				return false;
			}
			// The compatibility port has no non-borrowing probe.  Preserve the
			// just-acquired frame metadata before releasing an equal-generation
			// transient lease; the retained lease can be one frame older.
			var acquiredDescriptor = new OutputSurfaceDescriptor(acquired.SurfaceId, acquired.Generation, acquired.Width, acquired.Height,
				acquired.FrameNumber, acquired.Texture, true);
			if (current != null && current.Generation == acquired.Generation && string.Equals(current.SurfaceId, acquired.SurfaceId, StringComparison.Ordinal)) {
				acquired.Dispose();
			}
			else {
				var previous = current;
				current = acquired;
				previous?.Dispose();
			}
			descriptor = acquiredDescriptor;
			return true;
		}

		private static bool Matches(OutputSurfaceLease lease, OutputSurfaceDescriptor descriptor) {
			return lease != null && descriptor.IsBound &&
				string.Equals(lease.SurfaceId, descriptor.SurfaceId, StringComparison.Ordinal) &&
				lease.Generation == descriptor.Generation && lease.Width == descriptor.Width && lease.Height == descriptor.Height &&
				ReferenceEquals(lease.Texture, descriptor.Texture);
		}

		private static OutputSurfaceReadModel ToSurfaceReadModel(OutputSurfaceDescriptor descriptor, bool isProgram)
			=> new OutputSurfaceReadModel(descriptor.SurfaceId, descriptor.Generation, descriptor.Width, descriptor.Height,
				descriptor.FrameNumber, descriptor.Texture, isProgram, true);

		private static PresentationOutputFit ParseFit(string value) => Enum.TryParse(value, true, out PresentationOutputFit result) ? result : PresentationOutputFit.Fit;
		private static PresentationOutputBackground ParseBackground(string value) => string.Equals(value, "Checker", StringComparison.OrdinalIgnoreCase) ? PresentationOutputBackground.Checker : PresentationOutputBackground.Black;
		private static PresentationQualityStage ParseQuality(string value) => Enum.TryParse(value, true, out PresentationQualityStage result) ? result : PresentationQualityStage.Full;

		private static int ParseIndex(IReadOnlyDictionary<string, string> payload) => payload.TryGetValue("index", out var value) && int.TryParse(value, out var index) ? index : -1;
		private static IReadOnlyList<string> ParsePaths(string value) => (value ?? string.Empty).Split(new[] { '\n', '|', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(path => path.Trim()).Where(path => path.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		private static UnsavedChangesDecision ParseUnsavedDecision(IReadOnlyDictionary<string, string> payload)
			=> Enum.TryParse(payloadValue(payload, "decision"), true, out UnsavedChangesDecision decision) ? decision : UnsavedChangesDecision.Cancel;
		private static bool ParseBool(string value, bool fallback) => bool.TryParse(value, out var parsed) ? parsed : fallback;
		private static bool? ParseNullableBool(string value) => bool.TryParse(value, out var parsed) ? parsed : (bool?)null;
		private static int ParseInt(string value, int fallback = 0) => int.TryParse(value, out var parsed) ? parsed : fallback;
		private static float ParseFloat(string value, float fallback = 0f) => float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
		private static float? ParseNullableFloat(string value) => float.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? parsed : (float?)null;
		private static ApplicationLogicalControlRequest ParseLogicalControlRequest(PresentationCommandRequest request) {
			var kind = Enum.TryParse<ApplicationLogicalControlKind>(request.PayloadValue("kind"), true, out var parsedKind) ? parsedKind : ApplicationLogicalControlKind.Value;
			return new ApplicationLogicalControlRequest(request.PayloadValue("logicalControlId", request.TargetId), request.PayloadValue("name", request.TargetId), kind,
				ParseFloat(request.PayloadValue("initialValue")), request.PayloadValue("presetId"), ParseMappings(request.Payload));
		}

		private static IEnumerable<ApplicationLogicalControlTargetRequest> ParseTargets(IReadOnlyDictionary<string, string> payload) {
			if (!TryParseParameterValue(payload, "targetMin", out var min, out var minError) || !TryParseParameterValue(payload, "targetMax", out var max, out var maxError)) return Enumerable.Empty<ApplicationLogicalControlTargetRequest>();
			return new[] { new ApplicationLogicalControlTargetRequest(payloadValue(payload, "nodeId"), payloadValue(payload, "parameterId"), min, max, ParseBool(payloadValue(payload, "invert"), false)) };
		}

		private static ApplicationExpressionDraft ParseExpressionDraft(PresentationCommandRequest request) {
			var kind = Enum.TryParse<ApplicationExpressionKind>(request.PayloadValue("kind"), true, out var parsed) ? parsed : ApplicationExpressionKind.BaseValue;
			ParameterValue? minimum = null;
			ParameterValue? maximum = null;
			if (request.Payload.TryGetValue("outputMinimum", out var minimumText) && !string.IsNullOrWhiteSpace(minimumText) &&
				TryParseParameterValue(request.Payload, "outputMinimum", out var parsedMinimum, out _)) minimum = parsedMinimum;
			if (request.Payload.TryGetValue("outputMaximum", out var maximumText) && !string.IsNullOrWhiteSpace(maximumText) &&
				TryParseParameterValue(request.Payload, "outputMaximum", out var parsedMaximum, out _)) maximum = parsedMaximum;
			return new ApplicationExpressionDraft(request.PayloadValue("nodeId", request.TargetId), request.PayloadValue("parameterId"), kind, request.PayloadValue("logicalControlId"), outputMinimum: minimum, outputMaximum: maximum);
		}

		private static ApplicationPreviewSettingsRequest ParsePreviewSettings(PresentationCommandRequest request) {
			var fit = Enum.TryParse<ApplicationOutputFitMode>(request.PayloadValue("fit"), true, out var parsed) ? parsed : ApplicationOutputFitMode.Fit;
			return new ApplicationPreviewSettingsRequest(request.PayloadValue("previewId", request.TargetId), fit, request.PayloadValue("background", "Black"), request.PayloadValue("quality", "Project"), ParseBool(request.PayloadValue("holdLastFrame"), true));
		}

		private static IEnumerable<ApplicationControlMappingRequest> ParseMappings(IReadOnlyDictionary<string, string> payload) {
			var physical = payloadValue(payload, "physicalId");
			var path = payloadValue(payload, "controlPath");
			if (string.IsNullOrEmpty(physical) && string.IsNullOrEmpty(path)) return Enumerable.Empty<ApplicationControlMappingRequest>();
			var kindText = payloadValue(payload, "kind");
			var kind = Enum.TryParse(kindText, true, out ApplicationPhysicalInputKind parsedKind) ? parsedKind : ApplicationPhysicalInputKind.Keyboard;
			return new[] { new ApplicationControlMappingRequest(physical, path, ParseFloat(payloadValue(payload, "rawMin")), ParseFloat(payloadValue(payload, "rawMax"), 1f), ParseBool(payloadValue(payload, "invert"), false), kind) };
		}

		private static ApplicationMediaAssetRequest ParseMediaAssetRequest(PresentationCommandRequest request) {
			return new ApplicationMediaAssetRequest(request.PayloadValue("mediaAssetId", request.TargetId), request.PayloadValue("displayName", request.TargetId),
				request.PayloadValue("relativePath"), long.TryParse(request.PayloadValue("byteSize"), out var size) ? size : 0L,
				request.PayloadValue("integrityHash"), request.PayloadValue("kind", "Experimental"), request.PayloadValue("colorSpace", "SRgb"), request.PayloadValue("alphaMode", "Opaque"));
		}
		private static ApplicationGraphEditRequest ParseConnectionRequest(PresentationCommandRequest request, ApplicationGraphEditKind kind) {
			var source = request.PayloadValue("sourceNodeId");
			var sourcePort = request.PayloadValue("sourcePortId");
			var destination = request.PayloadValue("destinationNodeId");
			var destinationPort = request.PayloadValue("destinationPortId");
			if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(sourcePort) || string.IsNullOrEmpty(destination) || string.IsNullOrEmpty(destinationPort))
				ParseConnectionText(request.TargetId, out source, out sourcePort, out destination, out destinationPort);
			return new ApplicationGraphEditRequest(kind, targetId: request.TargetId, sourceId: source, sourcePortId: sourcePort,
				destinationId: destination, destinationPortId: destinationPort, requestedDocumentRevision: request.RequestedDocumentRevision,
				commandRequestId: request.CommandRequestId);
		}

		private static void ParseConnectionText(string text, out string source, out string sourcePort, out string destination, out string destinationPort) {
			source = sourcePort = destination = destinationPort = string.Empty;
			if (string.IsNullOrEmpty(text)) return;
			var arrow = text.IndexOf("->", StringComparison.Ordinal);
			if (arrow < 0) return;
			ParseEndpoint(text.Substring(0, arrow), out source, out sourcePort);
			ParseEndpoint(text.Substring(arrow + 2), out destination, out destinationPort);
		}

		private static void ParseEndpoint(string text, out string node, out string port) {
			var split = (text ?? string.Empty).LastIndexOf(':');
			if (split < 0) { node = text ?? string.Empty; port = string.Empty; return; }
			node = text.Substring(0, split); port = text.Substring(split + 1);
		}

		private static bool TryParseParameterValue(IReadOnlyDictionary<string, string> payload, out ParameterValue value, out string error) {
			return TryParseParameterValue(payload, "value", out value, out error);
		}

		private static bool TryParseParameterValue(IReadOnlyDictionary<string, string> payload, string valueKey, out ParameterValue value, out string error) {
			value = default(ParameterValue);
			var text = payloadValue(payload, valueKey);
			var type = payloadValue(payload, "valueType");
			if (string.IsNullOrEmpty(type)) type = "String";
			try {
				switch (type.Trim().ToLowerInvariant()) {
					case "float": if (float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f)) { value = ParameterValue.FromFloat(f); error = null; return true; } break;
					case "int": if (int.TryParse(text, out var i)) { value = ParameterValue.FromInt(i); error = null; return true; } break;
					case "bool": if (bool.TryParse(text, out var b)) { value = ParameterValue.FromBool(b); error = null; return true; } break;
					case "vector2": if (TryParseComponents(text, 2, out var v2)) { value = ParameterValue.FromVector2(new Vector2Value(v2[0], v2[1])); error = null; return true; } break;
					case "vector3": if (TryParseComponents(text, 3, out var v3)) { value = ParameterValue.FromVector3(new Vector3Value(v3[0], v3[1], v3[2])); error = null; return true; } break;
					case "vector4": if (TryParseComponents(text, 4, out var v4)) { value = ParameterValue.FromVector4(new Vector4Value(v4[0], v4[1], v4[2], v4[3])); error = null; return true; } break;
					case "color": if (TryParseComponents(text, 4, out var color)) { value = ParameterValue.FromColor(new ColorValue(color[0], color[1], color[2], color[3])); error = null; return true; } break;
					case "enum": value = ParameterValue.FromEnum(text); error = null; return true;
					case "mediaassetreference": value = ParameterValue.FromMediaAsset(string.IsNullOrEmpty(text) ? (MediaAssetId?)null : new MediaAssetId(text)); error = null; return true;
					case "string": value = ParameterValue.FromString(text); error = null; return true;
				}
			}
			catch (Exception exception) { error = exception.Message; return false; }
			error = "The parameter value does not match its declared type.";
			return false;
		}

		private static bool TryParseComponents(string text, int count, out float[] values) {
			values = null;
			var normalized = (text ?? string.Empty).Trim().Trim('(', ')', '[', ']');
			var parts = normalized.Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length != count) return false;
			var parsed = new float[count];
			for (var i = 0; i < count; i++)
				if (!float.TryParse(parts[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed[i]) || float.IsNaN(parsed[i]) || float.IsInfinity(parsed[i])) return false;
			values = parsed;
			return true;
		}

		private static string payloadValue(IReadOnlyDictionary<string, string> payload, string key, string fallback = "") => payload != null && payload.TryGetValue(key, out var value) ? value ?? fallback : fallback;
		private static PresentationProjectState MapState(ApplicationProjectState state) => (PresentationProjectState)Enum.Parse(typeof(PresentationProjectState), state.ToString(), true);
		private static PresentationCommandStatus MapStatus(ApplicationCommandStatus status) => (PresentationCommandStatus)Enum.Parse(typeof(PresentationCommandStatus), status.ToString(), true);
		private static PresentationSeverity MapSeverity(string severity) => Enum.TryParse(severity, true, out PresentationSeverity result) ? result : PresentationSeverity.Info;
		private static PresentationNodeStatus MapNodeStatus(string status) {
			if (string.Equals(status, "Unknown", StringComparison.OrdinalIgnoreCase)) return PresentationNodeStatus.UnknownNode;
			if (string.Equals(status, "Broken", StringComparison.OrdinalIgnoreCase)) return PresentationNodeStatus.Blocked;
			return Enum.TryParse(status, true, out PresentationNodeStatus result) ? result : PresentationNodeStatus.Ready;
		}
		private static PresentationPortDirection MapPortDirection(string direction) => string.Equals(direction, "Output", StringComparison.OrdinalIgnoreCase) ? PresentationPortDirection.Output : PresentationPortDirection.Input;
	}

	internal static class PresentationCommandRequestExtensions {
		internal static string PayloadValue(this PresentationCommandRequest request, string key, string fallback = "") => request?.Payload != null && request.Payload.TryGetValue(key, out var value) ? value ?? fallback : fallback;
	}
}
