using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace ShitDesigner.Presentation {
	/// <summary>
	/// Presentation's stable, read-only view projection of Application state.
	/// Application owns the canonical split read models; these records are the
	/// UI-facing projection used by presenters and visual fixtures. The project
	/// domain is deliberately not exposed here.
	/// </summary>
	public sealed class PresentationEnvelope<T> {
		public Guid ProjectSessionId { get; }
		public long ReadModelVersion { get; }
		public ulong FrameNumber { get; }
		public long DocumentRevision { get; }
		public long GraphRevision { get; }
		public bool IsFullSnapshot { get; }
		public T Model { get; }

		public PresentationEnvelope(Guid projectSessionId, long readModelVersion, ulong frameNumber,
			long documentRevision, long graphRevision, bool isFullSnapshot, T model) {
			ProjectSessionId = projectSessionId;
			ReadModelVersion = readModelVersion;
			FrameNumber = frameNumber;
			DocumentRevision = documentRevision;
			GraphRevision = graphRevision;
			IsFullSnapshot = isFullSnapshot;
			Model = model;
		}
	}

	public enum PresentationProjectState { Empty, Ready, Loading, Saving, SaveAs, Closing, Exited }
	public enum PresentationSeverity { Info, Warning, Error, Fatal }
	public enum PresentationNodeStatus { Ready, Blocked, Faulted, Preparing, UsingFallback, UnknownNode }
	public enum PresentationPortRequirement { Required, Optional }
	public enum PresentationPortDirection { Input, Output }
	public enum PresentationOutputFit { Fit, Fill, Stretch }
	public enum PresentationOutputBackground { Checker, Black }
	// The Application may publish a named quality policy (Full/Reduced/
	// Minimum), while the GUI specification exposes the stable numeric
	// ladder 0..4. Keep both spellings as aliases so read-model projections
	// and the visible status line never lose the policy's numeric stage.
	public enum PresentationQualityStage {
		Stage0 = 0,
		Stage1 = 1,
		Stage2 = 2,
		Stage3 = 3,
		Stage4 = 4,
		Minimum = Stage0,
		Reduced = Stage2,
		Full = Stage4
	}
	public enum PresentationCommandStatus { Accepted, Applied, Rejected, Superseded, Cancelled }

	public sealed class ShellReadModel {
		public PresentationProjectState State { get; }
		public string ProjectName { get; }
		public bool ProjectDirty { get; }
		public bool Recovered { get; }
		public bool CanUndo { get; }
		public bool CanRedo { get; }
		public string StatusText { get; }
		public ulong GraphClockFrame { get; }
		public bool GraphClockPaused { get; }

		public ShellReadModel(PresentationProjectState state, string projectName, bool projectDirty,
			bool recovered, bool canUndo, bool canRedo, string statusText = null,
			ulong graphClockFrame = 0, bool graphClockPaused = false) {
			State = state;
			ProjectName = projectName ?? string.Empty;
			ProjectDirty = projectDirty;
			Recovered = recovered;
			CanUndo = canUndo;
			CanRedo = canRedo;
			StatusText = statusText ?? string.Empty;
			GraphClockFrame = graphClockFrame;
			GraphClockPaused = graphClockPaused;
		}
	}

	public sealed class WorkspaceReadModel {
		public string CurrentLayoutId { get; }
		public bool LayoutDirty { get; }
		public IReadOnlyList<string> VisiblePanelInstanceIds { get; }
		public IReadOnlyList<LayoutPreset> Presets { get; }
		public float UiScale { get; }
		public bool ReduceMotion { get; }
		public string Theme { get; }
		public float TooltipDelaySeconds { get; }
		public string MediaLibraryView { get; }
		public string DiagnosticsExportFolder { get; }
		public DockTree CurrentTree { get; }

		public WorkspaceReadModel(string currentLayoutId, bool layoutDirty, IEnumerable<string> visiblePanelInstanceIds,
			IEnumerable<LayoutPreset> presets = null, float uiScale = 1f, bool reduceMotion = false, DockTree currentTree = null,
			string theme = "Dark", float tooltipDelaySeconds = .5f, string mediaLibraryView = "Grid", string diagnosticsExportFolder = null) {
			CurrentLayoutId = currentLayoutId ?? string.Empty;
			LayoutDirty = layoutDirty;
			VisiblePanelInstanceIds = PresentationCollections.Copy(visiblePanelInstanceIds);
			Presets = new ReadOnlyCollection<LayoutPreset>((presets ?? Enumerable.Empty<LayoutPreset>()).Select(x => new LayoutPreset(x.Id, x.Name, x.Tree.Copy())).ToList());
			UiScale = uiScale;
			ReduceMotion = reduceMotion;
			Theme = string.IsNullOrWhiteSpace(theme) ? "Dark" : theme;
			TooltipDelaySeconds = tooltipDelaySeconds;
			MediaLibraryView = string.Equals(mediaLibraryView, "List", StringComparison.OrdinalIgnoreCase) ? "List" : "Grid";
			DiagnosticsExportFolder = diagnosticsExportFolder ?? string.Empty;
			CurrentTree = (currentTree ?? Presets.FirstOrDefault(x => string.Equals(x.Id, CurrentLayoutId, StringComparison.Ordinal))?.Tree ?? LayoutPresetStore.DefaultTree()).Copy();
		}
	}

	public sealed class NodeCatalogItem {
		public string TypeId { get; }
		public string DisplayName { get; }
		public string Category { get; }
		public bool IsAvailable { get; }
		public bool UserAddable { get; }
		public bool IsFavorite { get; }
		public bool IsRecent { get; }
		public string DisabledReason { get; }
		public NodeCatalogItem(string typeId, string displayName, bool isAvailable = true, string disabledReason = null, bool userAddable = true,
			string category = null, bool isFavorite = false, bool isRecent = false) { TypeId = typeId ?? string.Empty; DisplayName = displayName ?? typeId ?? string.Empty; Category = category ?? string.Empty; IsAvailable = isAvailable; UserAddable = userAddable; IsFavorite = isFavorite; IsRecent = isRecent; DisabledReason = disabledReason ?? string.Empty; }
	}

	public sealed class LogicalControlReadModel {
		public string Id { get; }
		public string Name { get; }
		public string Kind { get; }
		public string PresetId { get; }
		public bool IsBroken { get; }
		public float? CurrentValue { get; }
		public bool IsFiring { get; }
		public LogicalControlReadModel(string id, string name, string kind, string presetId = null, bool isBroken = false, float? currentValue = null, bool isFiring = false) { Id = id ?? string.Empty; Name = name ?? id ?? string.Empty; Kind = kind ?? string.Empty; PresetId = presetId ?? string.Empty; IsBroken = isBroken; CurrentValue = currentValue; IsFiring = isFiring; }
	}

	public sealed class GraphNodeReadModel {
		public string Id { get; }
		public string TypeId { get; }
		public string DisplayName { get; }
		public float X { get; }
		public float Y { get; }
		public PresentationNodeStatus Status { get; }
		public bool IsPending { get; }
		public string StatusReason { get; }
		public IReadOnlyList<ParameterReadModel> Parameters { get; }

		public GraphNodeReadModel(string id, string typeId, string displayName, float x, float y,
			PresentationNodeStatus status = PresentationNodeStatus.Ready, bool isPending = false, string statusReason = null,
			IEnumerable<ParameterReadModel> parameters = null) {
			Id = id ?? string.Empty;
			TypeId = typeId ?? string.Empty;
			DisplayName = displayName ?? typeId ?? string.Empty;
			X = x;
			Y = y;
			Status = status;
			IsPending = isPending;
			StatusReason = statusReason ?? string.Empty;
			Parameters = PresentationCollections.Copy(parameters);
		}
	}

	public sealed class GraphPortReadModel {
		public string NodeId { get; }
		public string PortId { get; }
		public string DisplayName { get; }
		public string ValueType { get; }
		public PresentationPortDirection Direction { get; }
		public PresentationPortRequirement Requirement { get; }
		public bool IsConnected { get; }

		public GraphPortReadModel(string nodeId, string portId, string displayName, string valueType,
			PresentationPortDirection direction, PresentationPortRequirement requirement, bool isConnected = false) {
			NodeId = nodeId ?? string.Empty;
			PortId = portId ?? string.Empty;
			DisplayName = displayName ?? portId ?? string.Empty;
			ValueType = valueType ?? string.Empty;
			Direction = direction;
			Requirement = requirement;
			IsConnected = isConnected;
		}
	}

	public sealed class GraphConnectionReadModel {
		public string Id { get; }
		public string FromNodeId { get; }
		public string FromPortId { get; }
		public string ToNodeId { get; }
		public string ToPortId { get; }
		public bool IsImplicitConversion { get; }
		public string ConversionLabel { get; }

		public GraphConnectionReadModel(string id, string fromNodeId, string fromPortId, string toNodeId,
			string toPortId, bool isImplicitConversion = false, string conversionLabel = null) {
			Id = id ?? string.Empty;
			FromNodeId = fromNodeId ?? string.Empty;
			FromPortId = fromPortId ?? string.Empty;
			ToNodeId = toNodeId ?? string.Empty;
			ToPortId = toPortId ?? string.Empty;
			IsImplicitConversion = isImplicitConversion;
			ConversionLabel = conversionLabel ?? string.Empty;
		}
	}

	public sealed class GraphReadModel {
		public IReadOnlyList<GraphNodeReadModel> Nodes { get; }
		public IReadOnlyList<GraphPortReadModel> Ports { get; }
		public IReadOnlyList<GraphConnectionReadModel> Connections { get; }

		public GraphReadModel(IEnumerable<GraphNodeReadModel> nodes = null, IEnumerable<GraphPortReadModel> ports = null,
			IEnumerable<GraphConnectionReadModel> connections = null) {
			Nodes = PresentationCollections.Copy(nodes);
			Ports = PresentationCollections.Copy(ports);
			Connections = PresentationCollections.Copy(connections);
		}
	}

	public sealed class ParameterReadModel {
		public string NodeId { get; }
		public string ParameterId { get; }
		public string DisplayName { get; }
		public string BaseValue { get; }
		public string EffectiveValue { get; }
		public string ValueType { get; }
		public string Expression { get; }
		public string OutputClamp { get; }
		public bool IsReadOnly { get; }
		public bool IsBroken { get; }
		public bool IsClamped { get; }
		public string Error { get; }
		public string Group { get; }
		public int Order { get; }
		public string Description { get; }
		public string Unit { get; }
		public double Step { get; }
		public string HardRange { get; }
		public IReadOnlyList<ParameterComponentRangeReadModel> ComponentRanges { get; }
		public IReadOnlyList<ParameterOptionReadModel> EnumOptions { get; }
		public IReadOnlyList<string> MediaOptions { get; }
		public string MediaKind { get; }
		public string NodeTypeId { get; }
		public bool IsVisible { get; }

		public ParameterReadModel(string nodeId, string parameterId, string displayName, string baseValue,
			string effectiveValue, bool isReadOnly = false, bool isBroken = false, bool isClamped = false, string error = null, string valueType = null,
			string expression = null, string outputClamp = null, string group = null, int order = 0, string description = null,
			string unit = null, double step = 0d, string hardRange = null, IEnumerable<ParameterComponentRangeReadModel> componentRanges = null,
			IEnumerable<ParameterOptionReadModel> enumOptions = null, IEnumerable<string> mediaOptions = null, string mediaKind = null,
			string nodeTypeId = null, bool isVisible = true) {
			NodeId = nodeId ?? string.Empty;
			ParameterId = parameterId ?? string.Empty;
			DisplayName = displayName ?? parameterId ?? string.Empty;
			BaseValue = baseValue ?? string.Empty;
			EffectiveValue = effectiveValue ?? string.Empty;
			ValueType = valueType ?? string.Empty;
			Expression = expression ?? string.Empty;
			OutputClamp = outputClamp ?? string.Empty;
			IsReadOnly = isReadOnly;
			IsBroken = isBroken;
			IsClamped = isClamped;
			Error = error ?? string.Empty;
			Group = group ?? string.Empty;
			Order = order;
			Description = description ?? string.Empty;
			Unit = unit ?? string.Empty;
			Step = step;
			HardRange = hardRange ?? string.Empty;
			ComponentRanges = PresentationCollections.Copy(componentRanges);
			EnumOptions = PresentationCollections.Copy(enumOptions);
			MediaOptions = PresentationCollections.Copy(mediaOptions);
			MediaKind = mediaKind ?? string.Empty;
			NodeTypeId = nodeTypeId ?? string.Empty;
			IsVisible = isVisible;
		}
	}

	public sealed class ParameterComponentRangeReadModel {
		public string Name { get; }
		public string Minimum { get; }
		public string Maximum { get; }
		public ParameterComponentRangeReadModel(string name, string minimum, string maximum) { Name = name ?? string.Empty; Minimum = minimum ?? string.Empty; Maximum = maximum ?? string.Empty; }
	}

	public sealed class ParameterOptionReadModel {
		public string Id { get; }
		public string DisplayName { get; }
		public ParameterOptionReadModel(string id, string displayName) { Id = id ?? string.Empty; DisplayName = displayName ?? id ?? string.Empty; }
	}

	public sealed class DashboardWidgetReadModel {
		public string Id { get; }
		public string NodeId { get; }
		public string ParameterId { get; }
		public int Column { get; }
		public int Row { get; }
		public int Width { get; }
		public int Height { get; }
		public string DisplayMode { get; }
		public bool IsBroken { get; }

		public DashboardWidgetReadModel(string id, string parameterId, int column, int row, int width, int height,
			string displayMode, bool isBroken = false, string nodeId = null) {
			Id = id ?? string.Empty;
			NodeId = nodeId ?? string.Empty;
			ParameterId = parameterId ?? string.Empty;
			Column = column;
			Row = row;
			Width = width;
			Height = height;
			DisplayMode = displayMode ?? string.Empty;
			IsBroken = isBroken;
		}
	}

	public sealed class DashboardPageReadModel {
		public string Id { get; }
		public string Name { get; }
		public IReadOnlyList<DashboardWidgetReadModel> Widgets { get; }

		public DashboardPageReadModel(string id, string name, IEnumerable<DashboardWidgetReadModel> widgets = null) {
			Id = id ?? string.Empty;
			Name = name ?? id ?? string.Empty;
			Widgets = PresentationCollections.Copy(widgets);
		}
	}

	public sealed class PresetListItemReadModel {
		public string Id { get; }
		public string Name { get; }
		public bool IsBroken { get; }
		public string BrokenReason { get; }
		public string Category { get; }
		public int SortIndex { get; }
		public IReadOnlyList<PresetEntryReadModel> Entries { get; }
		public PresetListItemReadModel(string id, string name, bool isBroken = false, string brokenReason = null,
			string category = null, int sortIndex = 0, IEnumerable<PresetEntryReadModel> entries = null) {
			Id = id ?? string.Empty; Name = name ?? id ?? string.Empty; IsBroken = isBroken; BrokenReason = brokenReason ?? string.Empty;
			Category = category ?? string.Empty; SortIndex = sortIndex; Entries = PresentationCollections.Copy(entries);
		}
	}

	public sealed class PresetEntryReadModel {
		public string NodeId { get; }
		public string ParameterId { get; }
		public string ValueType { get; }
		public string Value { get; }
		public bool IsBroken { get; }
		public string BrokenReason { get; }
		public PresetEntryReadModel(string nodeId, string parameterId, string valueType, string value,
			bool isBroken = false, string brokenReason = null) {
			NodeId = nodeId ?? string.Empty; ParameterId = parameterId ?? string.Empty;
			ValueType = valueType ?? string.Empty; Value = value ?? string.Empty;
			IsBroken = isBroken; BrokenReason = brokenReason ?? string.Empty;
		}
	}

	public sealed class MediaListItemReadModel {
		public string Id { get; }
		public string RelativePath { get; }
		public string Status { get; }
		public string BrokenReason { get; }
		public int ReferenceCount { get; }
		public string DisplayName { get; }
		public long ByteSize { get; }
		public string IntegrityHash { get; }
		public string Kind { get; }
		public string ColorSpace { get; }
		public string AlphaMode { get; }
		public MediaListItemReadModel(string id, string relativePath, string status, string brokenReason = null, int referenceCount = 0,
			string displayName = null, long byteSize = 0, string integrityHash = null, string kind = null,
			string colorSpace = null, string alphaMode = null) {
			Id = id ?? string.Empty; RelativePath = relativePath ?? string.Empty; Status = status ?? string.Empty;
			BrokenReason = brokenReason ?? string.Empty; ReferenceCount = Math.Max(0, referenceCount);
			DisplayName = displayName ?? id ?? string.Empty; ByteSize = Math.Max(0, byteSize); IntegrityHash = integrityHash ?? string.Empty;
			Kind = kind ?? string.Empty; ColorSpace = colorSpace ?? string.Empty; AlphaMode = alphaMode ?? string.Empty;
		}
	}

	public sealed class PresentationTaskReadModel {
		public Guid TaskId { get; }
		public string Kind { get; }
		public string Stage { get; }
		public string Status { get; }
		public int CompletedItems { get; }
		public int TotalItems { get; }
		public string CurrentItem { get; }
		public string Error { get; }
		public PresentationTaskReadModel(Guid taskId, string kind, string stage, string status, int completedItems = 0, int totalItems = 0, string currentItem = null, string error = null) { TaskId = taskId; Kind = kind ?? string.Empty; Stage = stage ?? string.Empty; Status = status ?? string.Empty; CompletedItems = completedItems; TotalItems = totalItems; CurrentItem = currentItem ?? string.Empty; Error = error ?? string.Empty; }
	}

	public sealed class OutputSurfaceReadModel {
		public string SurfaceId { get; }
		public ulong Generation { get; }
		public int Width { get; }
		public int Height { get; }
		public ulong FrameNumber { get; }
		public object Texture { get; }
		public bool IsProgram { get; }
		public bool IsBound { get; }

		public OutputSurfaceReadModel(string surfaceId, ulong generation, int width, int height, ulong frameNumber,
			object texture = null, bool isProgram = false, bool isBound = false) {
			SurfaceId = surfaceId ?? string.Empty;
			Generation = generation;
			Width = width;
			Height = height;
			FrameNumber = frameNumber;
			Texture = texture;
			IsProgram = isProgram;
			IsBound = isBound;
		}
	}

	public sealed class PreviewReadModel {
		public string NodeId { get; }
		public string TabId { get; }
		public bool IsVisible { get; }
		public PresentationOutputFit Fit { get; }
		public PresentationOutputBackground Background { get; }
		public PresentationQualityStage Quality { get; }
		public string StateText { get; }
		public OutputSurfaceReadModel Surface { get; }

		public PreviewReadModel(string nodeId, string tabId, bool isVisible, PresentationOutputFit fit,
			PresentationOutputBackground background, PresentationQualityStage quality, string stateText,
			OutputSurfaceReadModel surface = null) {
			NodeId = nodeId ?? string.Empty;
			TabId = tabId ?? string.Empty;
			IsVisible = isVisible;
			Fit = fit;
			Background = background;
			Quality = quality;
			StateText = stateText ?? string.Empty;
			Surface = surface;
		}
	}

	public sealed class OutputReadModel {
		public OutputSurfaceReadModel Program { get; }
		public string ProgramState { get; }
		public bool IsPaused { get; }
		public int ProgramDisplay { get; }
		public IReadOnlyList<PreviewReadModel> Previews { get; }
		public bool ExternalDisplayActive { get; }
		public double CpuFrameTimeMilliseconds { get; }
		public double GpuFrameTimeMilliseconds { get; }
		public double MeasuredFramesPerSecond { get; }
		public double HoldingDurationSeconds { get; }
		public string HoldingCauseNodeId { get; }
		public string HoldingDiagnosticCode { get; }
		public bool ProgramPerformanceWarning { get; }
		public int ConsecutiveBadProgramFrames { get; }

		public OutputReadModel(OutputSurfaceReadModel program = null, IEnumerable<PreviewReadModel> previews = null,
			bool externalDisplayActive = false, string programState = null, bool isPaused = false, int programDisplay = 2,
			double cpuFrameTimeMilliseconds = double.NaN, double gpuFrameTimeMilliseconds = double.NaN,
			double measuredFramesPerSecond = double.NaN, double holdingDurationSeconds = double.NaN,
			string holdingCauseNodeId = null, string holdingDiagnosticCode = null,
			bool programPerformanceWarning = false, int consecutiveBadProgramFrames = 0) {
			Program = program;
			ProgramState = programState ?? string.Empty;
			IsPaused = isPaused;
			ProgramDisplay = programDisplay;
			Previews = PresentationCollections.Copy(previews);
			ExternalDisplayActive = externalDisplayActive;
			CpuFrameTimeMilliseconds = cpuFrameTimeMilliseconds; GpuFrameTimeMilliseconds = gpuFrameTimeMilliseconds;
			MeasuredFramesPerSecond = measuredFramesPerSecond; HoldingDurationSeconds = holdingDurationSeconds;
			HoldingCauseNodeId = holdingCauseNodeId ?? string.Empty; HoldingDiagnosticCode = holdingDiagnosticCode ?? string.Empty;
			ProgramPerformanceWarning = programPerformanceWarning; ConsecutiveBadProgramFrames = Math.Max(0, consecutiveBadProgramFrames);
		}
	}

	public sealed class DiagnosticReadModel {
		public string EntryId { get; }
		public PresentationSeverity Severity { get; }
		public string Code { get; }
		public string Message { get; }
		public string NodeId { get; }
		public int Count { get; }
		public bool IsCurrent { get; }
		public ulong FirstFrame { get; }
		public ulong LastFrame { get; }
		public string PortOrParameter { get; }
		public string Details { get; }
		public string ExceptionType { get; }
		public string StackTrace { get; }

		public DiagnosticReadModel(string entryId, PresentationSeverity severity, string code, string message,
			string nodeId = null, int count = 1, bool isCurrent = true, ulong firstFrame = 0, ulong lastFrame = 0,
			string portOrParameter = null, string details = null, string exceptionType = null, string stackTrace = null) {
			EntryId = entryId ?? string.Empty;
			Severity = severity;
			Code = code ?? string.Empty;
			Message = message ?? string.Empty;
			NodeId = nodeId ?? string.Empty;
			Count = Math.Max(1, count);
			IsCurrent = isCurrent;
			FirstFrame = firstFrame; LastFrame = lastFrame; PortOrParameter = portOrParameter ?? string.Empty;
			Details = details ?? string.Empty; ExceptionType = exceptionType ?? string.Empty; StackTrace = stackTrace ?? string.Empty;
		}
	}

	public sealed class CommandReadModel {
		public Guid CommandRequestId { get; }
		public Guid InteractionId { get; }
		public PresentationCommandStatus Status { get; }
		public string Reason { get; }
		// Compatibility alias for the shell banner and command-result
		// adapter.  Application remains the source of the diagnostic text;
		// Presentation exposes the same immutable value under both names.
		public string Diagnostic => Reason;
		public bool IsTerminal => Status != PresentationCommandStatus.Accepted;

		public CommandReadModel(Guid commandRequestId, Guid interactionId, PresentationCommandStatus status, string reason = null) {
			CommandRequestId = commandRequestId;
			InteractionId = interactionId;
			Status = status;
			Reason = reason ?? string.Empty;
		}
	}

	public sealed class PresentationReadModel {
		public ShellReadModel Shell { get; }
		public WorkspaceReadModel Workspace { get; }
		public IReadOnlyList<NodeCatalogItem> NodeCatalog { get; }
		public IReadOnlyList<LogicalControlReadModel> Controls { get; }
		public GraphReadModel Graph { get; }
		public IReadOnlyList<ParameterReadModel> Parameters { get; }
		public IReadOnlyList<DashboardPageReadModel> DashboardPages { get; }
		public IReadOnlyList<PresetListItemReadModel> Presets { get; }
		public IReadOnlyList<MediaListItemReadModel> Media { get; }
		public PresentationTaskReadModel Task { get; }
		public OutputReadModel Output { get; }
		public IReadOnlyList<DiagnosticReadModel> Diagnostics { get; }
		public IReadOnlyList<CommandReadModel> Commands { get; }
		public IReadOnlyList<string> RecentProjectRoots { get; }

		public PresentationReadModel(ShellReadModel shell = null, WorkspaceReadModel workspace = null,
			GraphReadModel graph = null, IEnumerable<ParameterReadModel> parameters = null,
			IEnumerable<DashboardPageReadModel> dashboardPages = null, OutputReadModel output = null,
			IEnumerable<DiagnosticReadModel> diagnostics = null, IEnumerable<CommandReadModel> commands = null,
			IEnumerable<NodeCatalogItem> nodeCatalog = null, IEnumerable<PresetListItemReadModel> presets = null,
			IEnumerable<MediaListItemReadModel> media = null, PresentationTaskReadModel task = null,
			IEnumerable<LogicalControlReadModel> controls = null, IEnumerable<string> recentProjectRoots = null) {
			Shell = shell ?? new ShellReadModel(PresentationProjectState.Empty, string.Empty, false, false, false, false);
			Workspace = workspace ?? new WorkspaceReadModel(string.Empty, false, null);
			NodeCatalog = PresentationCollections.Copy(nodeCatalog);
			Graph = graph ?? new GraphReadModel();
			Parameters = Copy(parameters);
			DashboardPages = Copy(dashboardPages);
			Presets = Copy(presets);
			Media = Copy(media);
			Task = task;
			Controls = Copy(controls);
			Output = output ?? new OutputReadModel();
			Diagnostics = Copy(diagnostics);
			Commands = Copy(commands);
			RecentProjectRoots = CopyRecentRoots(recentProjectRoots);
		}

		private static IReadOnlyList<T> Copy<T>(IEnumerable<T> source) {
			if (source == null) return PresentationCollections.Empty<T>();
			if (source is ReadOnlyCollection<T> immutable) return immutable;
			return new ReadOnlyCollection<T>(source.ToList());
		}

		private static IReadOnlyList<string> CopyRecentRoots(IEnumerable<string> source) {
			if (source == null) return PresentationCollections.Empty<string>();
			if (source is ReadOnlyCollection<string> immutable && immutable.Count <= 10) return immutable;
			return new ReadOnlyCollection<string>(source.Take(10).ToList());
		}
	}

	public sealed class PresentationCommandRequest {
		public Guid ProjectSessionId { get; }
		public Guid CommandRequestId { get; }
		public Guid InteractionId { get; }
		public long RequestedDocumentRevision { get; }
		public string TargetId { get; }
		public string CommandId { get; }
		public IReadOnlyDictionary<string, string> Payload { get; }

		public PresentationCommandRequest(Guid projectSessionId, Guid commandRequestId, Guid interactionId,
			long requestedDocumentRevision, string targetId, string commandId,
			IEnumerable<KeyValuePair<string, string>> payload = null) {
			ProjectSessionId = projectSessionId;
			CommandRequestId = commandRequestId;
			InteractionId = interactionId;
			RequestedDocumentRevision = requestedDocumentRevision;
			TargetId = targetId ?? string.Empty;
			CommandId = commandId ?? string.Empty;
			Payload = new ReadOnlyDictionary<string, string>((payload ?? Enumerable.Empty<KeyValuePair<string, string>>())
				.ToDictionary(x => x.Key ?? string.Empty, x => x.Value ?? string.Empty, StringComparer.Ordinal));
		}
	}

	public interface IPresentationReadPort {
		PresentationEnvelope<PresentationReadModel> ReadLatest(bool fullSnapshot);
	}

	public interface IPresentationCommandPort {
		CommandReadModel Submit(PresentationCommandRequest request);
	}

	public interface IPresentationNoticeSink {
		void Record(PresentationSeverity severity, string code, string message, string panelId = null);
	}

	public static class PresentationCollections {
		private static class EmptyList<T> {
			internal static readonly IReadOnlyList<T> Value = new ReadOnlyCollection<T>(new List<T>());
		}

		public static IReadOnlyList<T> Empty<T>() => EmptyList<T>.Value;

		public static IReadOnlyList<T> Copy<T>(IEnumerable<T> source) {
			if (source == null) return Empty<T>();
			if (source is ReadOnlyCollection<T> immutable) return immutable;
			return new ReadOnlyCollection<T>(source.ToList());
		}
	}
}
