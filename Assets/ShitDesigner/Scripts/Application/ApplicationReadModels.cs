using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;

namespace ShitDesigner.Application
{
    using static ApplicationReadModelCollections;

    public sealed class ApplicationControlRuntimeReadModel
    {
        public float Value { get; }
        public bool HasValue { get; }
        public bool IsFiring { get; }
        public ApplicationControlRuntimeReadModel(float value, bool hasValue, bool isFiring)
        { Value = value; HasValue = hasValue; IsFiring = isFiring; }
    }

    /// <summary>
    /// Application-owned read contracts.  These records deliberately use
    /// scalar stable IDs instead of Project, Graph, or Runtime objects so a
    /// Presenter can never mutate domain state through a read model.
    /// </summary>
    public sealed class ApplicationShellReadModel
    {
        public ApplicationProjectState State { get; }
        public string ProjectName { get; }
        public string ProjectRoot { get; }
        public bool IsDirty { get; }
        public bool IsRecovered { get; }
        public bool CanUndo { get; }
        public bool CanRedo { get; }
        public string StatusText { get; }
        public ApplicationShellReadModel(ApplicationProjectState state, string projectName, string projectRoot,
            bool isDirty, bool isRecovered, bool canUndo, bool canRedo, string statusText = null)
        {
            State = state; ProjectName = projectName ?? string.Empty; ProjectRoot = projectRoot ?? string.Empty;
            IsDirty = isDirty; IsRecovered = isRecovered; CanUndo = canUndo; CanRedo = canRedo;
            StatusText = statusText ?? string.Empty;
        }
    }

    public sealed class ApplicationWorkspaceReadModel
    {
        public string LayoutId { get; }
        public bool IsDirty { get; }
        public string AvailabilityStatus { get; }
        public string UnavailableReason { get; }
        public IReadOnlyList<string> VisiblePanelIds { get; }
        public ApplicationWorkspaceReadModel(string layoutId, bool isDirty, IEnumerable<string> visiblePanelIds = null, string availabilityStatus = "Available", string unavailableReason = null)
        {
            LayoutId = layoutId ?? string.Empty; IsDirty = isDirty; AvailabilityStatus = availabilityStatus ?? string.Empty; UnavailableReason = unavailableReason ?? string.Empty;
            VisiblePanelIds = Copy(visiblePanelIds);
        }
    }

    public sealed class ApplicationNodeCatalogPortMetadata
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string Direction { get; }
        public string ValueType { get; }
        public bool IsRequired { get; }
        public ApplicationNodeCatalogPortMetadata(string id, string displayName, string direction, string valueType, bool isRequired)
        { Id = id ?? string.Empty; DisplayName = displayName ?? string.Empty; Direction = direction ?? string.Empty; ValueType = valueType ?? string.Empty; IsRequired = isRequired; }
    }

    public sealed class ApplicationNodeCatalogParameterMetadata
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string ValueType { get; }
        public string DefaultValue { get; }
        public string HardRange { get; }
        public bool RuntimeStateful { get; }
        public string Group { get; }
        public int Order { get; }
        public string Description { get; }
        public string Unit { get; }
        public double Step { get; }
        public bool IsReadOnly { get; }
        public bool IsVisible { get; }
        public ApplicationNodeCatalogParameterMetadata(string id, string displayName, string valueType, string defaultValue, string hardRange, bool runtimeStateful,
            string group = null, int order = 0, string description = null, string unit = null, double step = 0d, bool isReadOnly = false, bool isVisible = true)
        { Id = id ?? string.Empty; DisplayName = displayName ?? string.Empty; ValueType = valueType ?? string.Empty; DefaultValue = defaultValue ?? string.Empty; HardRange = hardRange ?? string.Empty; RuntimeStateful = runtimeStateful; Group = group ?? string.Empty; Order = order; Description = description ?? string.Empty; Unit = unit ?? string.Empty; Step = step; IsReadOnly = isReadOnly; IsVisible = isVisible; }
    }

    public sealed class ApplicationNodeCatalogEntry
    {
        public string TypeId { get; }
        public string DisplayName { get; }
        public string Category { get; }
        public bool UserAddable { get; }
        public bool IsAvailable { get; }
        public bool RuntimeAvailable { get; }
        public string RuntimeUnavailableReason { get; }
        public string DisabledReason { get; }
        public IReadOnlyList<ApplicationNodeCatalogPortMetadata> Ports { get; }
        public IReadOnlyList<ApplicationNodeCatalogParameterMetadata> Parameters { get; }
        public ApplicationNodeCatalogEntry(string typeId, string displayName, bool isAvailable = true, string disabledReason = null, string category = null, bool userAddable = true, IEnumerable<ApplicationNodeCatalogPortMetadata> ports = null, IEnumerable<ApplicationNodeCatalogParameterMetadata> parameters = null, bool runtimeAvailable = false, string runtimeUnavailableReason = null)
        { TypeId = typeId ?? string.Empty; DisplayName = displayName ?? typeId ?? string.Empty; Category = category ?? string.Empty; UserAddable = userAddable; IsAvailable = isAvailable; RuntimeAvailable = runtimeAvailable; RuntimeUnavailableReason = runtimeUnavailableReason ?? string.Empty; DisabledReason = disabledReason ?? string.Empty; Ports = Copy(ports); Parameters = Copy(parameters); }
    }

    public sealed class ApplicationGraphNodeReadModel
    {
        public string Id { get; }
        public string TypeId { get; }
        public string DisplayName { get; }
        public float X { get; }
        public float Y { get; }
        public string Status { get; }
        public bool IsPending { get; }
        public string StatusReason { get; }
        public bool Enabled { get; }
        public string UnknownOriginalTypeId { get; }
        public int UnknownOriginalSchemaVersion { get; }
        public string OpaqueRawState { get; }
        public ApplicationGraphNodeReadModel(string id, string typeId, string displayName, float x, float y,
            string status = "Ready", bool isPending = false, string statusReason = null, bool enabled = true, string unknownOriginalTypeId = null, int unknownOriginalSchemaVersion = 0, string opaqueRawState = null)
        { Id = id ?? string.Empty; TypeId = typeId ?? string.Empty; DisplayName = displayName ?? typeId ?? string.Empty; X = x; Y = y; Status = status ?? string.Empty; IsPending = isPending; StatusReason = statusReason ?? string.Empty; Enabled = enabled; UnknownOriginalTypeId = unknownOriginalTypeId ?? string.Empty; UnknownOriginalSchemaVersion = unknownOriginalSchemaVersion; OpaqueRawState = opaqueRawState ?? string.Empty; }
    }

    public sealed class ApplicationGraphPortReadModel
    {
        public string StableId { get; }
        public string NodeId { get; }
        public string PortId { get; }
        public string ValueType { get; }
        public string Direction { get; }
        public bool IsRequired { get; }
        public bool IsConnected { get; }
        public ApplicationGraphPortReadModel(string stableId, string nodeId, string portId, string valueType, string direction, bool isRequired, bool isConnected)
        { StableId = stableId ?? string.Empty; NodeId = nodeId ?? string.Empty; PortId = portId ?? string.Empty; ValueType = valueType ?? string.Empty; Direction = direction ?? string.Empty; IsRequired = isRequired; IsConnected = isConnected; }
    }

    public sealed class ApplicationGraphConnectionReadModel
    {
        public string Id { get; }
        public string FromNodeId { get; }
        public string FromPortId { get; }
        public string ToNodeId { get; }
        public string ToPortId { get; }
        public bool IsImplicitConversion { get; }
        public string ConversionLabel { get; }
        public ApplicationGraphConnectionReadModel(string id, string fromNodeId, string fromPortId, string toNodeId, string toPortId, bool isImplicitConversion = false, string conversionLabel = null)
        { Id = id ?? string.Empty; FromNodeId = fromNodeId ?? string.Empty; FromPortId = fromPortId ?? string.Empty; ToNodeId = toNodeId ?? string.Empty; ToPortId = toPortId ?? string.Empty; IsImplicitConversion = isImplicitConversion; ConversionLabel = conversionLabel ?? string.Empty; }
    }

    public sealed class ApplicationGraphReadModel
    {
        public IReadOnlyList<ApplicationGraphNodeReadModel> Nodes { get; }
        public IReadOnlyList<ApplicationGraphPortReadModel> Ports { get; }
        public IReadOnlyList<ApplicationGraphConnectionReadModel> Connections { get; }
        public ApplicationGraphReadModel(IEnumerable<ApplicationGraphNodeReadModel> nodes = null, IEnumerable<ApplicationGraphPortReadModel> ports = null, IEnumerable<ApplicationGraphConnectionReadModel> connections = null)
        { Nodes = Copy(nodes); Ports = Copy(ports); Connections = Copy(connections); }
    }

    public sealed class ApplicationParameterComponentRangeReadModel
    {
        public string Name { get; }
        public string Minimum { get; }
        public string Maximum { get; }
        public ApplicationParameterComponentRangeReadModel(string name, string minimum, string maximum)
        { Name = name ?? string.Empty; Minimum = minimum ?? string.Empty; Maximum = maximum ?? string.Empty; }
    }

    public sealed class ApplicationParameterOptionReadModel
    {
        public string Id { get; }
        public string DisplayName { get; }
        public ApplicationParameterOptionReadModel(string id, string displayName)
        { Id = id ?? string.Empty; DisplayName = displayName ?? id ?? string.Empty; }
    }

    public sealed class ApplicationParameterReadModel
    {
        public string StableId { get; }
        public string NodeId { get; }
        public string ParameterId { get; }
        public string DisplayName { get; }
        public string BaseValue { get; }
        public string EffectiveValue { get; }
        public bool EffectiveValueChanged { get; }
        public bool IsReadOnly { get; }
        public bool IsBroken { get; }
        public bool IsClamped { get; }
        public string Error { get; }
        public string ValueType { get; }
        public string HardRange { get; }
        public string LogicalTargets { get; }
        public string Expression { get; }
        public string OutputClamp { get; }
        public string Group { get; }
        public int Order { get; }
        public string Description { get; }
        public string Unit { get; }
        public double Step { get; }
        public IReadOnlyList<ApplicationParameterComponentRangeReadModel> ComponentRanges { get; }
        public IReadOnlyList<ApplicationParameterOptionReadModel> EnumOptions { get; }
        public IReadOnlyList<string> MediaOptions { get; }
        public string MediaKind { get; }
        public string NodeTypeId { get; }
        public bool IsVisible { get; }
        public ApplicationParameterReadModel(string stableId, string nodeId, string parameterId, string displayName, string baseValue, string effectiveValue, bool effectiveValueChanged = false, bool isReadOnly = false, bool isBroken = false, bool isClamped = false, string error = null, string valueType = null, string hardRange = null, string logicalTargets = null, string expression = null, string outputClamp = null)
        {
            StableId = stableId ?? string.Empty; NodeId = nodeId ?? string.Empty; ParameterId = parameterId ?? string.Empty; DisplayName = displayName ?? parameterId ?? string.Empty; BaseValue = baseValue ?? string.Empty; EffectiveValue = effectiveValue ?? string.Empty; EffectiveValueChanged = effectiveValueChanged; IsReadOnly = isReadOnly; IsBroken = isBroken; IsClamped = isClamped; Error = error ?? string.Empty; ValueType = valueType ?? string.Empty; HardRange = hardRange ?? string.Empty; LogicalTargets = logicalTargets ?? string.Empty; Expression = expression ?? string.Empty; OutputClamp = outputClamp ?? string.Empty;
            Group = string.Empty; Order = 0; Description = string.Empty; Unit = string.Empty; Step = 0d; ComponentRanges = Copy<ApplicationParameterComponentRangeReadModel>(null); EnumOptions = Copy<ApplicationParameterOptionReadModel>(null); MediaOptions = Copy<string>(null); MediaKind = string.Empty; NodeTypeId = string.Empty; IsVisible = true;
        }

        public ApplicationParameterReadModel(string stableId, string nodeId, string parameterId, string displayName, string baseValue, string effectiveValue,
            bool effectiveValueChanged, bool isReadOnly, bool isBroken, bool isClamped, string error, string valueType, string hardRange, string logicalTargets,
            string expression, string outputClamp, string group, int order, string description, string unit, double step,
            IEnumerable<ApplicationParameterComponentRangeReadModel> componentRanges, IEnumerable<ApplicationParameterOptionReadModel> enumOptions,
            IEnumerable<string> mediaOptions, string mediaKind, string nodeTypeId, bool isVisible = true)
        {
            StableId = stableId ?? string.Empty; NodeId = nodeId ?? string.Empty; ParameterId = parameterId ?? string.Empty; DisplayName = displayName ?? parameterId ?? string.Empty; BaseValue = baseValue ?? string.Empty; EffectiveValue = effectiveValue ?? string.Empty; EffectiveValueChanged = effectiveValueChanged; IsReadOnly = isReadOnly; IsBroken = isBroken; IsClamped = isClamped; Error = error ?? string.Empty; ValueType = valueType ?? string.Empty; HardRange = hardRange ?? string.Empty; LogicalTargets = logicalTargets ?? string.Empty; Expression = expression ?? string.Empty; OutputClamp = outputClamp ?? string.Empty;
            Group = group ?? string.Empty; Order = order; Description = description ?? string.Empty; Unit = unit ?? string.Empty; Step = step; ComponentRanges = Copy(componentRanges); EnumOptions = Copy(enumOptions); MediaOptions = Copy(mediaOptions); MediaKind = mediaKind ?? string.Empty; NodeTypeId = nodeTypeId ?? string.Empty; IsVisible = isVisible;
        }

        private static IReadOnlyList<T> Copy<T>(IEnumerable<T> values) => new ReadOnlyCollection<T>((values ?? Enumerable.Empty<T>()).ToList());
    }

    public sealed class ApplicationDashboardWidgetReadModel
    {
        public string Id { get; }
        public string NodeId { get; }
        public string ParameterId { get; }
        public int Column { get; }
        public int Row { get; }
        public int Width { get; }
        public int Height { get; }
        public string Label { get; }
        public bool IsBroken { get; }
        public string BrokenReason { get; }
        public ApplicationDashboardWidgetReadModel(string id, string nodeId, string parameterId, int column, int row, int width, int height, string label, bool isBroken, string brokenReason)
        { Id = id ?? string.Empty; NodeId = nodeId ?? string.Empty; ParameterId = parameterId ?? string.Empty; Column = column; Row = row; Width = width; Height = height; Label = label ?? string.Empty; IsBroken = isBroken; BrokenReason = brokenReason ?? string.Empty; }
    }

    public sealed class ApplicationDashboardReadModel
    {
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<string> WidgetIds { get; }
        public IReadOnlyList<ApplicationDashboardWidgetReadModel> Widgets { get; }
        public ApplicationDashboardReadModel(string id, string name, IEnumerable<string> widgetIds = null, IEnumerable<ApplicationDashboardWidgetReadModel> widgets = null)
        { Id = id ?? string.Empty; Name = name ?? id ?? string.Empty; WidgetIds = Copy(widgetIds); Widgets = Copy(widgets); }
    }

    public sealed class ApplicationPresetReadModel
    {
        public string Id { get; }
        public string Name { get; }
        public bool IsBroken { get; }
        public string BrokenReason { get; }
        public string Category { get; }
        public int SortIndex { get; }
        public IReadOnlyList<ApplicationPresetEntryReadModel> Entries { get; }
        public ApplicationPresetReadModel(string id, string name, bool isBroken = false, string brokenReason = null, string category = null, int sortIndex = 0, IEnumerable<ApplicationPresetEntryReadModel> entries = null)
        { Id = id ?? string.Empty; Name = name ?? id ?? string.Empty; IsBroken = isBroken; BrokenReason = brokenReason ?? string.Empty; Category = category ?? string.Empty; SortIndex = sortIndex; Entries = Copy(entries); }
    }

    public sealed class ApplicationPresetEntryReadModel
    {
        public string NodeId { get; }
        public string ParameterId { get; }
        public string ValueType { get; }
        public string Value { get; }
        public bool IsBroken { get; }
        public string BrokenReason { get; }
        public ApplicationPresetEntryReadModel(string nodeId, string parameterId, string valueType, string value, bool isBroken, string brokenReason)
        { NodeId = nodeId ?? string.Empty; ParameterId = parameterId ?? string.Empty; ValueType = valueType ?? string.Empty; Value = value ?? string.Empty; IsBroken = isBroken; BrokenReason = brokenReason ?? string.Empty; }
    }

    public sealed class ApplicationMediaReadModel
    {
        public string Id { get; }
        public string RelativePath { get; }
        public long Size { get; }
        public string IntegrityHash { get; }
        public string Status { get; }
        public string Kind { get; }
        public string ColorSpace { get; }
        public string AlphaMode { get; }
        public int ReferenceCount { get; }
        public string BrokenReason { get; }
        public bool IsBroken { get; }
        public ApplicationMediaReadModel(string id, string relativePath, long size, string integrityHash, string status = "Ready", string kind = null, string colorSpace = null, string alphaMode = null, int referenceCount = 0, string brokenReason = null)
        { Id = id ?? string.Empty; RelativePath = relativePath ?? string.Empty; Size = size; IntegrityHash = integrityHash ?? string.Empty; Status = status ?? string.Empty; Kind = kind ?? string.Empty; ColorSpace = colorSpace ?? string.Empty; AlphaMode = alphaMode ?? string.Empty; ReferenceCount = referenceCount; BrokenReason = brokenReason ?? string.Empty; IsBroken = !string.Equals(Status, "Ready", StringComparison.OrdinalIgnoreCase); }
    }

    public sealed class ApplicationOutputReadModel
    {
        public ulong FrameNumber { get; }
        public string ProgramState { get; }
        public bool IsPaused { get; }
        public int ProgramDisplay { get; }
        public ApplicationOutputSurfaceReadModel Program { get; }
        public IReadOnlyList<ApplicationOutputSurfaceReadModel> Previews { get; }
        public ulong PerformanceFrameNumber { get; }
        public double CpuFrameTimeMilliseconds { get; }
        public double GpuFrameTimeMilliseconds { get; }
        public double MeasuredFramesPerSecond { get; }
        public bool ProgramPerformanceWarning { get; }
        public int ConsecutiveBadProgramFrames { get; }
        public double HoldingDurationSeconds { get; }
        public string HoldingCauseNodeId { get; }
        public string HoldingDiagnosticCode { get; }
        public ApplicationOutputReadModel(ulong frameNumber, string programState, bool isPaused, ApplicationOutputSurfaceReadModel program = null, IEnumerable<ApplicationOutputSurfaceReadModel> previews = null, int programDisplay = 2,
            double cpuFrameTimeMilliseconds = double.NaN, double gpuFrameTimeMilliseconds = double.NaN, double measuredFramesPerSecond = double.NaN,
            bool programPerformanceWarning = false, int consecutiveBadProgramFrames = 0, double holdingDurationSeconds = double.NaN,
            string holdingCauseNodeId = null, string holdingDiagnosticCode = null, ulong performanceFrameNumber = 0)
        {
            FrameNumber = frameNumber; ProgramState = programState ?? string.Empty; IsPaused = isPaused; ProgramDisplay = programDisplay;
            Program = program ?? new ApplicationOutputSurfaceReadModel("program", "Program", programState, 0, 0, "Fit", "Black", "Project", false, programState == "HoldingLastFrame", programState);
            Previews = Copy(previews);
            CpuFrameTimeMilliseconds = cpuFrameTimeMilliseconds; GpuFrameTimeMilliseconds = gpuFrameTimeMilliseconds; MeasuredFramesPerSecond = measuredFramesPerSecond;
            PerformanceFrameNumber = performanceFrameNumber;
            ProgramPerformanceWarning = programPerformanceWarning; ConsecutiveBadProgramFrames = Math.Max(0, consecutiveBadProgramFrames);
            HoldingDurationSeconds = holdingDurationSeconds; HoldingCauseNodeId = holdingCauseNodeId ?? string.Empty; HoldingDiagnosticCode = holdingDiagnosticCode ?? string.Empty;
        }
    }

    public sealed class ApplicationOutputSurfaceReadModel
    {
        public string Id { get; }
        public string TargetKind { get; }
        public string State { get; }
        public int Width { get; }
        public int Height { get; }
        public string FitMode { get; }
        public string BackgroundMode { get; }
        public string Quality { get; }
        public bool IsDemanded { get; }
        public bool IsFocused { get; }
        public bool IsHoldingLastFrame { get; }
        public string StatusReason { get; }
        public ApplicationOutputSurfaceReadModel(string id, string targetKind, string state, int width, int height, string fitMode, string backgroundMode, string quality, bool isDemanded, bool isHoldingLastFrame, string statusReason = null, bool isFocused = false)
        { Id = id ?? string.Empty; TargetKind = targetKind ?? string.Empty; State = state ?? string.Empty; Width = width; Height = height; FitMode = fitMode ?? string.Empty; BackgroundMode = backgroundMode ?? string.Empty; Quality = quality ?? string.Empty; IsDemanded = isDemanded; IsFocused = isFocused; IsHoldingLastFrame = isHoldingLastFrame; StatusReason = statusReason ?? string.Empty; }
    }

    public sealed class ApplicationDiagnosticReadModel
    {
        public string EntryId { get; }
        public string Severity { get; }
        public string Code { get; }
        public string Message { get; }
        public string NodeId { get; }
        public long Count { get; }
        public ulong FirstFrame { get; }
        public ulong LastFrame { get; }
        public ApplicationDiagnosticReadModel(string entryId, string severity, string code, string message, string nodeId = null, long count = 1, ulong firstFrame = 0, ulong lastFrame = 0)
        { EntryId = entryId ?? string.Empty; Severity = severity ?? string.Empty; Code = code ?? string.Empty; Message = message ?? string.Empty; NodeId = nodeId ?? string.Empty; Count = Math.Max(1, count); FirstFrame = firstFrame; LastFrame = lastFrame; }
    }

    public sealed class ApplicationDiagnosticsReadModel
    {
        public IReadOnlyList<ApplicationDiagnosticReadModel> Current { get; }
        public IReadOnlyList<ApplicationDiagnosticReadModel> History { get; }
        public IReadOnlyDictionary<string, long> Summary { get; }
        public ReadModelChangeSet<ApplicationDiagnosticReadModel> ChangeSet { get; }
        public ApplicationDiagnosticsReadModel(IEnumerable<ApplicationDiagnosticReadModel> current, IEnumerable<ApplicationDiagnosticReadModel> history, IDictionary<string, long> summary, ReadModelChangeSet<ApplicationDiagnosticReadModel> changeSet)
        { Current = Copy(current); History = Copy(history); Summary = new ReadOnlyDictionary<string, long>(new Dictionary<string, long>(summary ?? new Dictionary<string, long>(), StringComparer.Ordinal)); ChangeSet = changeSet; }
    }

    public sealed class ApplicationTaskReadModel
    {
        public Guid TaskId { get; }
        public string Kind { get; }
        public string Stage { get; }
        public string Status { get; }
        public string Path { get; }
        public int CompletedItems { get; }
        public int TotalItems { get; }
        public string CurrentItem { get; }
        public Diagnostic Diagnostic { get; }
        public ApplicationTaskReadModel(Guid taskId, string kind, string stage, string status, string path = null, Diagnostic diagnostic = null, int completedItems = 0, int totalItems = 0, string currentItem = null)
        { TaskId = taskId; Kind = kind ?? string.Empty; Stage = stage ?? string.Empty; Status = status ?? string.Empty; Path = path ?? string.Empty; CompletedItems = completedItems; TotalItems = totalItems; CurrentItem = currentItem ?? string.Empty; Diagnostic = diagnostic; }
    }

    public sealed class ApplicationFrameCommandResult
    {
        public string RequestId { get; }
        public ApplicationCommandStatus Status { get; }
        public Diagnostic Diagnostic { get; }
        public ApplicationFrameCommandResult(string requestId, ApplicationCommandStatus status, Diagnostic diagnostic = null)
        { RequestId = requestId ?? string.Empty; Status = status; Diagnostic = diagnostic; }
    }

    public sealed class ApplicationFrameResult
    {
        public ulong FrameNumber { get; }
        public bool Succeeded { get; }
        public IReadOnlyList<ApplicationFrameCommandResult> CommandResults { get; }
        public ApplicationFrameResult(ulong frameNumber, bool succeeded, IEnumerable<ApplicationFrameCommandResult> commandResults = null)
        { FrameNumber = frameNumber; Succeeded = succeeded; CommandResults = Copy(commandResults); }
    }

    public enum ApplicationShortcutCommand
    {
        NewProject, OpenProject, Save, SaveAs, CloseProject, Undo, Redo, CommandPalette,
        PauseResume, CloseActivePanel, FocusDiagnostics, FocusProgram, Dismiss
    }

    public sealed class ApplicationShortcutMetadata
    {
        public ApplicationShortcutCommand Command { get; }
        public string DisplayName { get; }
        public string Tooltip { get; }
        public string Chord { get; }
        public bool Global { get; }
        public ApplicationShortcutMetadata(ApplicationShortcutCommand command, string displayName, string tooltip, string chord, bool global = true)
        { Command = command; DisplayName = displayName ?? string.Empty; Tooltip = tooltip ?? string.Empty; Chord = chord ?? string.Empty; Global = global; }
    }

    public static class ApplicationShortcutCatalog
    {
        private static readonly IReadOnlyList<ApplicationShortcutMetadata> Items = new ReadOnlyCollection<ApplicationShortcutMetadata>(new[]
        {
            new ApplicationShortcutMetadata(ApplicationShortcutCommand.NewProject, "New Project", "Create a project", "Primary+N"),
            new ApplicationShortcutMetadata(ApplicationShortcutCommand.OpenProject, "Open Project", "Open a project", "Primary+O"),
            new ApplicationShortcutMetadata(ApplicationShortcutCommand.Save, "Save", "Save the current project", "Primary+S"),
            new ApplicationShortcutMetadata(ApplicationShortcutCommand.SaveAs, "Save As", "Save a portable copy", "Primary+Shift+S"),
            new ApplicationShortcutMetadata(ApplicationShortcutCommand.CloseProject, "Close Project", "Close the current project", "Menu", true),
            new ApplicationShortcutMetadata(ApplicationShortcutCommand.Undo, "Undo", "Undo the last edit", "Primary+Z"),
            new ApplicationShortcutMetadata(ApplicationShortcutCommand.Redo, "Redo", "Redo the last edit", "Primary+Shift+Z"),
            new ApplicationShortcutMetadata(ApplicationShortcutCommand.CommandPalette, "Command Palette", "Show commands", "Primary+K"),
            new ApplicationShortcutMetadata(ApplicationShortcutCommand.PauseResume, "Pause/Resume", "Pause or resume evaluation", "Primary+Space"),
            new ApplicationShortcutMetadata(ApplicationShortcutCommand.CloseActivePanel, "Close Panel", "Close the active panel", "Primary+W"),
            new ApplicationShortcutMetadata(ApplicationShortcutCommand.FocusDiagnostics, "Diagnostics", "Focus diagnostics", "Primary+Shift+D"),
            new ApplicationShortcutMetadata(ApplicationShortcutCommand.FocusProgram, "Program", "Focus program monitor", "Primary+Shift+P")
        });
        public static IReadOnlyList<ApplicationShortcutMetadata> All => Items;
    }

    public interface IApplicationShortcutCommandPort
    {
        ApplicationCommandResult ExecuteShortcut(ApplicationShortcutCommand command);
        IReadOnlyList<ApplicationShortcutMetadata> ShortcutCatalog { get; }
    }

    public enum ApplicationGraphEditKind
    {
        AddNode, DeleteNode, Connect, Disconnect, ReplaceInputConnection, SetEnabled, Undo, Redo,
        // These are graph-session commands. They intentionally do not dirty
        // the project, but still cross the Application command boundary so a
        // host can acknowledge/reject them consistently with graph edits.
        CopySelection, PasteSelection, DuplicateSelection, FocusSelection, FocusAll
    }
    public sealed class ApplicationGraphEditRequest
    {
        public Guid CommandRequestId { get; }
        public ApplicationGraphEditKind Kind { get; }
        public string TargetId { get; }
        public string SourceId { get; }
        public string SourcePortId { get; }
        public string DestinationId { get; }
        public string DestinationPortId { get; }
        public string NodeTypeId { get; }
        public string NodeDisplayName { get; }
        public string RawState { get; }
        public float PositionX { get; }
        public float PositionY { get; }
        public int SchemaVersion { get; }
        public string ConversionId { get; }
        public bool Enabled { get; }
        public long RequestedDocumentRevision { get; }
        public ApplicationGraphEditRequest(ApplicationGraphEditKind kind, string targetId = null, string sourceId = null, string sourcePortId = null, string destinationId = null, string destinationPortId = null, bool enabled = true, long requestedDocumentRevision = -1, Guid? commandRequestId = null, string nodeTypeId = null, string nodeDisplayName = null, float positionX = 0, float positionY = 0, int schemaVersion = 1, string rawState = "{}", string conversionId = null)
        { CommandRequestId = commandRequestId ?? Guid.NewGuid(); Kind = kind; TargetId = targetId ?? string.Empty; SourceId = sourceId ?? string.Empty; SourcePortId = sourcePortId ?? string.Empty; DestinationId = destinationId ?? string.Empty; DestinationPortId = destinationPortId ?? string.Empty; Enabled = enabled; RequestedDocumentRevision = requestedDocumentRevision; NodeTypeId = nodeTypeId ?? string.Empty; NodeDisplayName = nodeDisplayName ?? string.Empty; PositionX = positionX; PositionY = positionY; SchemaVersion = schemaVersion; RawState = rawState ?? "{}"; ConversionId = conversionId ?? string.Empty; }
    }

    public sealed class ApplicationParameterEditRequest
    {
        public string NodeId { get; }
        public string ParameterId { get; }
        public ParameterValue Value { get; }
        public Guid InteractionId { get; }
        public ApplicationParameterEditRequest(string nodeId, string parameterId, ParameterValue value, Guid? interactionId = null)
        { NodeId = nodeId ?? string.Empty; ParameterId = parameterId ?? string.Empty; Value = value; InteractionId = interactionId ?? Guid.Empty; }
    }

    public enum ApplicationLogicalControlKind { Value, PresetTrigger }
    public sealed class ApplicationControlMappingRequest
    {
        public string PhysicalId { get; }
        public string ControlPath { get; }
        public float RawMin { get; }
        public float RawMax { get; }
        public bool Invert { get; }
        public ApplicationControlMappingRequest(string physicalId, string controlPath, float rawMin = 0, float rawMax = 1, bool invert = false)
        { PhysicalId = physicalId ?? string.Empty; ControlPath = controlPath ?? string.Empty; RawMin = rawMin; RawMax = rawMax; Invert = invert; }
    }

    public sealed class ApplicationLogicalControlRequest
    {
        public string Id { get; }
        public string Name { get; }
        public ApplicationLogicalControlKind Kind { get; }
        public float InitialValue { get; }
        public string PresetId { get; }
        public IReadOnlyList<ApplicationControlMappingRequest> Mappings { get; }
        public ApplicationLogicalControlRequest(string id, string name, ApplicationLogicalControlKind kind, float initialValue = 0, string presetId = null, IEnumerable<ApplicationControlMappingRequest> mappings = null)
        { Id = id ?? string.Empty; Name = name ?? string.Empty; Kind = kind; InitialValue = initialValue; PresetId = presetId ?? string.Empty; Mappings = Copy(mappings); }
    }

    public sealed class ApplicationMediaAssetRequest
    {
        public string Id { get; }
        public string DisplayName { get; }
        public string RelativePath { get; }
        public long ByteSize { get; }
        public string IntegrityHash { get; }
        public string Kind { get; }
        public string ColorSpace { get; }
        public string AlphaMode { get; }
        public ApplicationMediaAssetRequest(string id, string displayName, string relativePath, long byteSize, string integrityHash, string kind = "Experimental", string colorSpace = "SRgb", string alphaMode = "Opaque")
        { Id = id ?? string.Empty; DisplayName = displayName ?? string.Empty; RelativePath = relativePath ?? string.Empty; ByteSize = byteSize; IntegrityHash = integrityHash ?? string.Empty; Kind = kind ?? string.Empty; ColorSpace = colorSpace ?? string.Empty; AlphaMode = alphaMode ?? string.Empty; }
    }

    public sealed class ApplicationPresetEntryRequest
    {
        public string NodeId { get; }
        public string ParameterId { get; }
        public ParameterValue Value { get; }
        public ApplicationPresetEntryRequest(string nodeId, string parameterId, ParameterValue value)
        { NodeId = nodeId ?? string.Empty; ParameterId = parameterId ?? string.Empty; Value = value; }
    }

    public sealed class ApplicationPresetRequest
    {
        public string Id { get; }
        public string Name { get; }
        public string Category { get; }
        public int SortIndex { get; }
        public IReadOnlyList<ApplicationPresetEntryRequest> Entries { get; }
        public ApplicationPresetRequest(string id, string name, string category = null, int sortIndex = 0, IEnumerable<ApplicationPresetEntryRequest> entries = null)
        { Id = id ?? string.Empty; Name = name ?? string.Empty; Category = category ?? string.Empty; SortIndex = sortIndex; Entries = Copy(entries); }
    }

    public sealed class ApplicationPresetCommandRequest
    {
        public string PresetId { get; }
        public ApplicationPresetCommandRequest(string presetId) { PresetId = presetId ?? string.Empty; }
    }

    public sealed class ApplicationLogicalControlTargetRequest
    {
        public string NodeId { get; }
        public string ParameterId { get; }
        public ParameterValue TargetMin { get; }
        public ParameterValue TargetMax { get; }
        public bool Invert { get; }
        public ApplicationLogicalControlTargetRequest(string nodeId, string parameterId, ParameterValue targetMin, ParameterValue targetMax, bool invert = false)
        { NodeId = nodeId ?? string.Empty; ParameterId = parameterId ?? string.Empty; TargetMin = targetMin; TargetMax = targetMax; Invert = invert; }
    }

    public enum ApplicationExpressionKind { BaseValue, LogicalControl, Min, Max }
    public sealed class ApplicationExpressionDraft
    {
        public string NodeId { get; }
        public string ParameterId { get; }
        public ApplicationExpressionKind Kind { get; }
        public string LogicalControlId { get; }
        public ApplicationExpressionDraft Left { get; }
        public ApplicationExpressionDraft Right { get; }
        public ParameterValue? OutputMinimum { get; }
        public ParameterValue? OutputMaximum { get; }
        public ApplicationExpressionDraft(string nodeId, string parameterId, ApplicationExpressionKind kind, string logicalControlId = null, ApplicationExpressionDraft left = null, ApplicationExpressionDraft right = null, ParameterValue? outputMinimum = null, ParameterValue? outputMaximum = null)
        { NodeId = nodeId ?? string.Empty; ParameterId = parameterId ?? string.Empty; Kind = kind; LogicalControlId = logicalControlId ?? string.Empty; Left = left; Right = right; OutputMinimum = outputMinimum; OutputMaximum = outputMaximum; }
    }

    public sealed class ApplicationMediaImportRequest
    {
        public string SourcePath { get; }
        public string DisplayName { get; }
        public string Kind { get; }
        public string ColorSpace { get; }
        public string AlphaMode { get; }
        public ApplicationMediaImportRequest(string sourcePath, string displayName, string kind = "Experimental", string colorSpace = "SRgb", string alphaMode = "Opaque")
        { SourcePath = sourcePath ?? string.Empty; DisplayName = displayName ?? string.Empty; Kind = kind ?? string.Empty; ColorSpace = colorSpace ?? string.Empty; AlphaMode = alphaMode ?? string.Empty; }
    }

    public sealed class ApplicationMediaReferenceReadModel
    {
        public string AssetId { get; }
        public string OwnerKind { get; }
        public string OwnerId { get; }
        public string ParameterId { get; }
        public bool IsBroken { get; }
        public ApplicationMediaReferenceReadModel(string assetId, string ownerKind, string ownerId, string parameterId, bool isBroken)
        { AssetId = assetId ?? string.Empty; OwnerKind = ownerKind ?? string.Empty; OwnerId = ownerId ?? string.Empty; ParameterId = parameterId ?? string.Empty; IsBroken = isBroken; }
    }

    public enum ApplicationMediaDeleteDecision { Cancel, Confirm }
    public enum ApplicationOutputFitMode { Fit, Fill, Stretch }
    public sealed class ApplicationDashboardWidgetRequest
    {
        public string WidgetId { get; }
        public string NodeId { get; }
        public string ParameterId { get; }
        public int Column { get; }
        public int Row { get; }
        public int Width { get; }
        public int Height { get; }
        public string Label { get; }
        public ApplicationDashboardWidgetRequest(string widgetId, string nodeId, string parameterId, int column = 0, int row = 0, int width = 1, int height = 1, string label = null)
        { WidgetId = widgetId ?? string.Empty; NodeId = nodeId ?? string.Empty; ParameterId = parameterId ?? string.Empty; Column = column; Row = row; Width = width; Height = height; Label = label ?? string.Empty; }
    }
    public sealed class ApplicationDashboardPageRequest
    {
        public string PageId { get; }
        public string Name { get; }
        public IReadOnlyList<ApplicationDashboardWidgetRequest> Widgets { get; }
        public ApplicationDashboardPageRequest(string pageId, string name, IEnumerable<ApplicationDashboardWidgetRequest> widgets = null)
        { PageId = pageId ?? string.Empty; Name = name ?? string.Empty; Widgets = Copy(widgets); }
    }
    public sealed class ApplicationPreviewSettingsRequest
    {
        public string PreviewId { get; }
        public ApplicationOutputFitMode FitMode { get; }
        public string BackgroundMode { get; }
        public string Quality { get; }
        public bool HoldLastFrame { get; }
        public ApplicationPreviewSettingsRequest(string previewId, ApplicationOutputFitMode fitMode = ApplicationOutputFitMode.Fit, string backgroundMode = "Black", string quality = "Project", bool holdLastFrame = true)
        { PreviewId = previewId ?? string.Empty; FitMode = fitMode; BackgroundMode = backgroundMode ?? string.Empty; Quality = quality ?? string.Empty; HoldLastFrame = holdLastFrame; }
    }
    public sealed class ApplicationOutputDemandRequest
    {
        public string PreviewId { get; }
        public string PortId { get; }
        public int Width { get; }
        public int Height { get; }
        public bool Focused { get; }
        public ApplicationOutputDemandRequest(string previewId, string portId = "image", int width = 640, int height = 360, bool focused = false)
        { PreviewId = previewId ?? string.Empty; PortId = portId ?? string.Empty; Width = width; Height = height; Focused = focused; }
    }

    public interface IApplicationCommandPort
    {
        ApplicationCommandResult NewProject(string projectName, string targetRoot, UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel);
        ApplicationCommandResult OpenProject(string projectRoot, UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel);
        ApplicationCommandResult SaveProject();
        ApplicationCommandResult SaveAs(string targetRoot);
        ApplicationCommandResult CloseProject(UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel);
        ApplicationCommandResult Exit(UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel);
        ApplicationCommandResult EditParameter(ApplicationParameterEditRequest request);
        ApplicationCommandResult ApplyPreset(ApplicationPresetCommandRequest request);
        ApplicationCommandResult AddLogicalControl(ApplicationLogicalControlRequest request);
        ApplicationCommandResult RenameLogicalControl(string logicalControlId, string name);
        ApplicationCommandResult SetLogicalControlTargets(string logicalControlId, IEnumerable<ApplicationLogicalControlTargetRequest> targets);
        ApplicationCommandResult SetLogicalControlMappings(string logicalControlId, IEnumerable<ApplicationControlMappingRequest> mappings);
        ApplicationCommandResult ApplyExpression(ApplicationExpressionDraft request);
        ApplicationCommandResult SetPresetTriggerBinding(string logicalControlId, string presetId);
        ApplicationCommandResult DeleteLogicalControl(string logicalControlId);
        ApplicationCommandResult AddMediaAsset(ApplicationMediaAssetRequest request);
        ApplicationCommandResult ImportMedia(ApplicationMediaImportRequest request);
        ApplicationCommandResult ImportMediaBatch(IEnumerable<ApplicationMediaImportRequest> requests);
        ApplicationCommandResult ConfirmMediaImport(bool approved);
        ApplicationCommandResult CancelMediaImport();
        ApplicationCommandResult RebindMedia(string mediaAssetId, string nodeId, string parameterId);
        ApplicationCommandResult ConfirmDeleteMedia(string mediaAssetId, ApplicationMediaDeleteDecision decision);
        ApplicationCommandResult InspectMediaReferences(string mediaAssetId, out IReadOnlyList<ApplicationMediaReferenceReadModel> references);
        ApplicationCommandResult DeleteMediaAsset(string mediaAssetId);
        ApplicationCommandResult AddPreset(ApplicationPresetRequest request);
        ApplicationCommandResult RenamePreset(string presetId, string name);
        ApplicationCommandResult DuplicatePreset(string presetId, string newPresetId, string name);
        ApplicationCommandResult CapturePresetEntry(string presetId, ApplicationPresetEntryRequest entry);
        ApplicationCommandResult RemovePresetEntry(string presetId, string nodeId, string parameterId);
        ApplicationCommandResult DeletePreset(string presetId);
        ApplicationCommandResult AddDashboardPage(ApplicationDashboardPageRequest request);
        ApplicationCommandResult UpdateDashboardPage(ApplicationDashboardPageRequest request);
        ApplicationCommandResult DeleteDashboardPage(string pageId);
        ApplicationCommandResult AddDashboardWidget(string pageId, ApplicationDashboardWidgetRequest request);
        ApplicationCommandResult RemoveDashboardWidget(string pageId, string widgetId);
        ApplicationCommandResult RebindDashboardWidget(string pageId, string widgetId, string nodeId, string parameterId);
        ApplicationCommandResult OpenPreview(string previewId);
        ApplicationCommandResult ClosePreview(string previewId);
        ApplicationCommandResult SetPreviewSettings(ApplicationPreviewSettingsRequest request);
        ApplicationCommandResult RequestPreviewDemand(ApplicationOutputDemandRequest request);
        ApplicationCommandResult SetPreviewHostVisible(bool visible);
        ApplicationCommandResult SetProgramDisplay(int display);
        ApplicationCommandResult ResetFeedback(string nodeId);
        ApplicationCommandResult ExportDiagnostics(string path, bool json);
        ApplicationCommandResult SetWorkspaceLayout(string layoutId, bool dirty);
        ApplicationCommandResult Undo();
        ApplicationCommandResult Redo();
        ApplicationCommandResult BeginKeyboardLearn(string logicalControlId);
        ApplicationCommandResult CancelKeyboardLearn();
        ApplicationCommandResult SubmitGraph(ApplicationGraphEditRequest request);
        ApplicationCommandResult ClearKeyboardMapping(string logicalControlId);
    }

    public sealed class ApplicationReadModelChangeSets
    {
        public ReadModelChangeSet<ApplicationGraphNodeReadModel> GraphNodes { get; }
        public ReadModelChangeSet<ApplicationGraphConnectionReadModel> GraphConnections { get; }
        public ReadModelChangeSet<ApplicationParameterReadModel> Parameters { get; }
        public ReadModelChangeSet<ApplicationDiagnosticReadModel> Diagnostics { get; }
        public ApplicationReadModelChangeSets(ReadModelChangeSet<ApplicationGraphNodeReadModel> graphNodes, ReadModelChangeSet<ApplicationGraphConnectionReadModel> graphConnections, ReadModelChangeSet<ApplicationParameterReadModel> parameters, ReadModelChangeSet<ApplicationDiagnosticReadModel> diagnostics)
        { GraphNodes = graphNodes; GraphConnections = graphConnections; Parameters = parameters; Diagnostics = diagnostics; }
        internal ApplicationReadModelChangeSets AsFullSnapshot(long version)
        {
            return new ApplicationReadModelChangeSets(Full(GraphNodes, version), Full(GraphConnections, version), Full(Parameters, version), Full(Diagnostics, version));
        }
        private static ReadModelChangeSet<T> Full<T>(ReadModelChangeSet<T> source, long version) => source == null ? null : new ReadModelChangeSet<T>(version, true, source.Changes);
    }

    internal static class ApplicationReadModelCollections
    {
        internal static IReadOnlyList<T> Copy<T>(IEnumerable<T> source) => new ReadOnlyCollection<T>((source ?? Enumerable.Empty<T>()).ToList());
    }
}
