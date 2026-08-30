using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Persistence;
using ShitDesigner.Project;
using ShitDesigner.Runtime;

namespace ShitDesigner.Application {
	/// <summary>Application-level lifetime state exposed to Presentation.</summary>
	public enum ApplicationProjectState {
		Empty,
		Ready,
		Loading,
		Saving,
		SaveAs,
		Closing,
		Exited
	}

	public enum UnsavedChangesDecision {
		Cancel,
		Save,
		Discard
	}

	public enum ApplicationCommandStatus {
		Accepted,
		Applied,
		Rejected,
		Superseded,
		Cancelled,
		/// <summary>No command was created (for example a modifier-only key-up).</summary>
		Ignored
	}

	/// <summary>
	/// Stable command identity and terminal status.  This is intentionally a
	/// small immutable object so a Presenter cannot mutate Application state.
	/// </summary>
	public sealed class ApplicationCommandResult {
		public Guid CommandRequestId { get; }
		public Guid InteractionId { get; }
		public Guid ProjectSessionId { get; }
		public long RequestedDocumentRevision { get; }
		public ApplicationCommandStatus Status { get; }
		public Diagnostic Diagnostic { get; }
		public bool IsSuccess => Status == ApplicationCommandStatus.Applied;
		public bool IsIgnored => Status == ApplicationCommandStatus.Ignored;
		public bool IsTerminal => Status != ApplicationCommandStatus.Accepted && Status != ApplicationCommandStatus.Ignored;

		internal ApplicationCommandResult(Guid requestId, Guid interactionId, Guid sessionId, long revision, ApplicationCommandStatus status, Diagnostic diagnostic = null) {
			CommandRequestId = requestId;
			InteractionId = interactionId;
			ProjectSessionId = sessionId;
			RequestedDocumentRevision = revision;
			Status = status;
			Diagnostic = diagnostic;
		}

		public static ApplicationCommandResult Ignored(Guid sessionId = default(Guid)) {
			return new ApplicationCommandResult(Guid.Empty, Guid.Empty, sessionId, 0, ApplicationCommandStatus.Ignored);
		}
	}

	public sealed class PendingCommandReadModel {
		public Guid CommandRequestId { get; }
		public Guid InteractionId { get; }
		public Guid ProjectSessionId { get; }
		public ApplicationCommandStatus Status { get; }
		public Diagnostic Diagnostic { get; }
		public bool IsTerminal => Status != ApplicationCommandStatus.Accepted && Status != ApplicationCommandStatus.Ignored;

		internal PendingCommandReadModel(Guid requestId, Guid interactionId, Guid sessionId, ApplicationCommandStatus status, Diagnostic diagnostic) {
			CommandRequestId = requestId;
			InteractionId = interactionId;
			ProjectSessionId = sessionId;
			Status = status;
			Diagnostic = diagnostic;
		}
	}

	public enum ReadModelChangeKind {
		Add,
		Update,
		Remove
	}

	public sealed class ReadModelChange<T> {
		public string StableId { get; }
		public ReadModelChangeKind Kind { get; }
		public T Value { get; }
		internal ReadModelChange(string stableId, ReadModelChangeKind kind, T value) {
			StableId = stableId ?? string.Empty;
			Kind = kind;
			Value = value;
		}
	}

	public sealed class ReadModelChangeSet<T> {
		public long ReadModelVersion { get; }
		public bool IsFullSnapshot { get; }
		public IReadOnlyList<ReadModelChange<T>> Changes { get; }
		internal ReadModelChangeSet(long version, bool fullSnapshot, IEnumerable<ReadModelChange<T>> changes) {
			ReadModelVersion = version;
			IsFullSnapshot = fullSnapshot;
			// Producers keep immutable empty deltas as a shared frozen list;
			// the envelope still carries a fresh version without allocating a
			// throwaway List on every stable presentation frame.
			Changes = changes is ReadOnlyCollection<ReadModelChange<T>> frozen
				? frozen
				: new ReadOnlyCollection<ReadModelChange<T>>((changes ?? Enumerable.Empty<ReadModelChange<T>>()).ToList());
		}

		internal static IReadOnlyList<ReadModelChange<T>> EmptyChanges { get; } =
			new ReadOnlyCollection<ReadModelChange<T>>(new List<ReadModelChange<T>>());
	}

	public sealed class ReadModelEnvelope<T> {
		public Guid ProjectSessionId { get; }
		public long ReadModelVersion { get; }
		public ulong FrameNumber { get; }
		public long DocumentRevision { get; }
		public long GraphRevision { get; }
		public bool IsFullSnapshot { get; }
		public T Model { get; }

		internal ReadModelEnvelope(Guid sessionId, long version, ulong frame, long documentRevision, long graphRevision, bool fullSnapshot, T model) {
			ProjectSessionId = sessionId;
			ReadModelVersion = version;
			FrameNumber = frame;
			DocumentRevision = documentRevision;
			GraphRevision = graphRevision;
			IsFullSnapshot = fullSnapshot;
			Model = model;
		}
	}

	/// <summary>Application-owned physical input kind.  Project's mapping
	/// enum must not cross the Presentation/Input assembly boundary.</summary>
	public enum ApplicationPhysicalInputKind {
		Keyboard,
		Midi
	}

	public sealed class ControlMappingReadModel {
		public ApplicationPhysicalInputKind Kind { get; }
		public string PhysicalId { get; }
		public string ControlPath { get; }
		public float RawMin { get; }
		public float RawMax { get; }
		public bool Invert { get; }
		public bool IsBroken { get; }

		internal ControlMappingReadModel(ControlMappingRecord mapping) {
			Kind = mapping.Kind == PhysicalInputKind.Midi ? ApplicationPhysicalInputKind.Midi : ApplicationPhysicalInputKind.Keyboard;
			PhysicalId = mapping.PhysicalId;
			ControlPath = mapping.ControlPath;
			RawMin = mapping.RawMin;
			RawMax = mapping.RawMax;
			Invert = mapping.Invert;
			IsBroken = mapping.IsBroken;
		}
	}

	public sealed class LogicalControlReadModel {
		public string Id { get; }
		public string Name { get; }
		public ApplicationLogicalControlKind Kind { get; }
		public string PresetId { get; }
		public bool PresetIsBroken { get; }
		public IReadOnlyList<ControlMappingReadModel> Mappings { get; }

		internal LogicalControlReadModel(LogicalControlRecord control) {
			Id = control.Id.Value;
			Name = control.Name;
			Kind = control.Kind == LogicalControlKind.PresetTrigger ? ApplicationLogicalControlKind.PresetTrigger : ApplicationLogicalControlKind.Value;
			PresetId = control.PresetId.HasValue ? control.PresetId.Value.Value : string.Empty;
			PresetIsBroken = control.PresetIsBroken;
			Mappings = new ReadOnlyCollection<ControlMappingReadModel>(control.Mappings.Select(x => new ControlMappingReadModel(x)).ToList());
		}
	}

	/// <summary>Read-only Project projection used by UI and Input adapters.</summary>
	public sealed class ProjectReadModel {
		public string ProjectName { get; }
		public string ProjectRoot { get; }
		public bool IsDirty { get; }
		public bool IsRecovered { get; }
		public int NodeCount { get; }
		public int ConnectionCount { get; }
		public int PresetCount { get; }
		public int MediaAssetCount { get; }
		public IReadOnlyList<LogicalControlReadModel> LogicalControls { get; }

		internal ProjectReadModel(ProjectDocument document, string root, bool recovered) {
			ProjectName = document == null ? string.Empty : document.ProjectName;
			ProjectRoot = root ?? string.Empty;
			IsDirty = document != null && document.IsDirty;
			IsRecovered = recovered;
			NodeCount = document == null ? 0 : document.Nodes.Count;
			ConnectionCount = document == null ? 0 : document.Connections.Count;
			PresetCount = document == null ? 0 : document.Presets.Count;
			MediaAssetCount = document == null ? 0 : document.MediaAssets.Count;
			LogicalControls = new ReadOnlyCollection<LogicalControlReadModel>(document == null
				? new List<LogicalControlReadModel>()
				: document.LogicalControls.Select(x => new LogicalControlReadModel(x)).ToList());
		}
	}

	public sealed class ApplicationReadModel {
		public ApplicationProjectState State { get; }
		public bool IsRecovered { get; }
		public ReadModelEnvelope<ProjectReadModel> Project { get; }
		public IReadOnlyList<string> RecentProjectRoots { get; }
		public IReadOnlyList<PendingCommandReadModel> Commands { get; }
		public IReadOnlyList<Diagnostic> Diagnostics { get; }
		public IReadOnlyList<MediaDeletionReadModel> PendingMediaDeletions { get; }
		public ReadModelChangeSet<ProjectReadModel> ChangeSet { get; }
		public ReadModelEnvelope<ApplicationShellReadModel> Shell { get; }
		public ReadModelEnvelope<ApplicationWorkspaceReadModel> Workspace { get; }
		public ReadModelEnvelope<IReadOnlyList<ApplicationNodeCatalogEntry>> NodeCatalog { get; }
		public ReadModelEnvelope<ApplicationGraphReadModel> Graph { get; }
		public ReadModelEnvelope<IReadOnlyList<ApplicationParameterReadModel>> Parameters { get; }
		public ReadModelEnvelope<IReadOnlyList<ApplicationDashboardReadModel>> Dashboard { get; }
		public ReadModelEnvelope<IReadOnlyList<ApplicationPresetReadModel>> Presets { get; }
		public ReadModelEnvelope<IReadOnlyList<ApplicationMediaReadModel>> Media { get; }
		public ReadModelEnvelope<ApplicationOutputReadModel> Output { get; }
		public ReadModelEnvelope<ApplicationDiagnosticsReadModel> DiagnosticModel { get; }
		public ReadModelEnvelope<IReadOnlyList<PendingCommandReadModel>> CommandResults { get; }
		public ReadModelEnvelope<ApplicationTaskReadModel> Task { get; }
		public ApplicationReadModelChangeSets ChangeSets { get; }
		public IReadOnlyDictionary<string, float> ControlValues { get; }
		public IReadOnlyDictionary<string, ApplicationControlRuntimeReadModel> ControlRuntime { get; }

		internal ApplicationReadModel(ApplicationProjectState state, bool recovered, ReadModelEnvelope<ProjectReadModel> project, IEnumerable<string> recent, IEnumerable<PendingCommandReadModel> commands, IEnumerable<Diagnostic> diagnostics, IEnumerable<MediaDeletionReadModel> deletions, ReadModelChangeSet<ProjectReadModel> changeSet,
			ReadModelEnvelope<ApplicationShellReadModel> shell = null,
			ReadModelEnvelope<ApplicationWorkspaceReadModel> workspace = null,
			ReadModelEnvelope<IReadOnlyList<ApplicationNodeCatalogEntry>> nodeCatalog = null,
			ReadModelEnvelope<ApplicationGraphReadModel> graph = null,
			ReadModelEnvelope<IReadOnlyList<ApplicationParameterReadModel>> parameters = null,
			ReadModelEnvelope<IReadOnlyList<ApplicationDashboardReadModel>> dashboard = null,
			ReadModelEnvelope<IReadOnlyList<ApplicationPresetReadModel>> presets = null,
			ReadModelEnvelope<IReadOnlyList<ApplicationMediaReadModel>> media = null,
			ReadModelEnvelope<ApplicationOutputReadModel> output = null,
			ReadModelEnvelope<ApplicationDiagnosticsReadModel> diagnosticModel = null,
			ReadModelEnvelope<IReadOnlyList<PendingCommandReadModel>> commandResults = null,
			ReadModelEnvelope<ApplicationTaskReadModel> task = null,
			ApplicationReadModelChangeSets changeSets = null,
			IReadOnlyDictionary<string, float> controlValues = null,
			IReadOnlyDictionary<string, ApplicationControlRuntimeReadModel> controlRuntime = null) {
			State = state;
			IsRecovered = recovered;
			Project = project;
			RecentProjectRoots = Freeze(recent);
			Commands = Freeze(commands);
			Diagnostics = Freeze(diagnostics);
			PendingMediaDeletions = Freeze(deletions);
			ChangeSet = changeSet ?? new ReadModelChangeSet<ProjectReadModel>(project == null ? 0 : project.ReadModelVersion, true, Enumerable.Empty<ReadModelChange<ProjectReadModel>>());
			Shell = shell; Workspace = workspace; NodeCatalog = nodeCatalog; Graph = graph; Parameters = parameters;
			Dashboard = dashboard; Presets = presets; Media = media; Output = output; DiagnosticModel = diagnosticModel;
			CommandResults = commandResults; Task = task; ChangeSets = changeSets;
			ControlValues = controlValues is ReadOnlyDictionary<string, float>
				? controlValues
				: new ReadOnlyDictionary<string, float>(new Dictionary<string, float>(controlValues ?? new Dictionary<string, float>(), StringComparer.Ordinal));
			ControlRuntime = controlRuntime is ReadOnlyDictionary<string, ApplicationControlRuntimeReadModel>
				? controlRuntime
				: new ReadOnlyDictionary<string, ApplicationControlRuntimeReadModel>(new Dictionary<string, ApplicationControlRuntimeReadModel>(controlRuntime ?? new Dictionary<string, ApplicationControlRuntimeReadModel>(), StringComparer.Ordinal));
		}

		internal ApplicationReadModel AsFullSnapshot() {
			return new ApplicationReadModel(State, IsRecovered, Full(Project), RecentProjectRoots, Commands, Diagnostics, PendingMediaDeletions, new ReadModelChangeSet<ProjectReadModel>(Project == null ? 0 : Project.ReadModelVersion, true, Enumerable.Empty<ReadModelChange<ProjectReadModel>>()),
				Full(Shell), Full(Workspace), Full(NodeCatalog), Full(Graph), Full(Parameters), Full(Dashboard), Full(Presets), Full(Media), Full(Output), Full(DiagnosticModel), Full(CommandResults), Full(Task), ChangeSets == null ? null : ChangeSets.AsFullSnapshot(Project == null ? 0 : Project.ReadModelVersion), ControlValues, ControlRuntime);
		}

		private static ReadModelEnvelope<T> Full<T>(ReadModelEnvelope<T> envelope) => envelope == null ? null : new ReadModelEnvelope<T>(envelope.ProjectSessionId, envelope.ReadModelVersion, envelope.FrameNumber, envelope.DocumentRevision, envelope.GraphRevision, true, envelope.Model);

		private static IReadOnlyList<T> Freeze<T>(IEnumerable<T> values) {
			if (values is ReadOnlyCollection<T> frozen) return frozen;
			return new ReadOnlyCollection<T>((values ?? Enumerable.Empty<T>()).ToList());
		}
	}

	public sealed class MediaDeletionReadModel {
		public MediaAssetId AssetId { get; }
		public string RelativePath { get; }
		public string ProjectRoot { get; }
		public bool IsOrphan { get; }
		internal MediaDeletionReadModel(PendingMediaDeletion item) {
			AssetId = item.AssetId;
			RelativePath = item.RelativePath;
			ProjectRoot = item.ProjectRoot;
			IsOrphan = item.IsOrphan;
		}
	}

	public interface IProjectApplicationReadPort {
		ApplicationReadModel ReadModel { get; }
		ApplicationReadModel ReadLatest(long lastReadModelVersion = 0);
	}

	/// <summary>
	/// User-settings boundary for the list shown by Open Recent.  Recent
	/// projects are user-wide state and therefore must not be serialized into
	/// a project document.  The application only owns the small, ordered list;
	/// the platform/bootstrap layer owns its file format and atomic I/O.
	/// </summary>
	public interface IRecentProjectStore {
		IReadOnlyList<string> ReadRecentProjectRoots();
		void WriteRecentProjectRoots(IEnumerable<string> projectRoots);
	}

	public interface IProjectApplicationCommandPort {
		ApplicationCommandResult NewProject(string projectName, string targetRoot, UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel);
		ApplicationCommandResult OpenProject(string projectRoot, UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel);
		ApplicationCommandResult OpenRecent(int index, UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel);
		ApplicationCommandResult SaveProject();
		ApplicationCommandResult SaveAs(string targetRoot);
		ApplicationCommandResult CloseProject(UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel);
		ApplicationCommandResult Exit(UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel);
		ApplicationCommandResult AddLogicalControl(LogicalControlRecord control, Guid? interactionId = null);
		ApplicationCommandResult AddMediaAsset(MediaAssetRecord asset, Guid? interactionId = null);
		ApplicationCommandResult SetLogicalControlMappings(LogicalControlId id, IEnumerable<ControlMappingRecord> mappings, Guid? interactionId = null);
		ApplicationCommandResult SetPresetTriggerBinding(LogicalControlId id, PresetId? presetId, Guid? interactionId = null);
		ApplicationCommandResult DeleteMediaAsset(MediaAssetId id, Guid? interactionId = null);
		ApplicationCommandResult Undo(Guid? interactionId = null);
		ApplicationCommandResult ApplyPreset(PresetId presetId);
		ApplicationCommandResult EnqueueBaseValue(BaseValueUpdate update, Guid? interactionId = null);
		ApplicationCommandResult EnqueueGraphEdit(GraphEditCommand command, Guid? interactionId = null);
	}

	/// <summary>
	/// Composition boundary for the runtime graph.  Bootstrap supplies the
	/// Nodes/Rendering/Scene/Media bindings here; Application never discovers
	/// Unity services while switching projects.
	/// </summary>
	public sealed class ApplicationRuntimeComposition : IDisposable {
		public RuntimeSession Session { get; }
		public FrameCoordinator Frames { get; }
		public bool RuntimeAvailable { get; }
		public string UnavailableReason { get; }
		private readonly IReadOnlyList<IDisposable> _ownedResources;
		public ApplicationRuntimeComposition(RuntimeSession session, FrameCoordinator frames, bool runtimeAvailable, string unavailableReason = null)
			: this(session, frames, runtimeAvailable, unavailableReason, null) { }

		public ApplicationRuntimeComposition(RuntimeSession session, FrameCoordinator frames, bool runtimeAvailable, string unavailableReason, IEnumerable<IDisposable> ownedResources) {
			Session = session ?? throw new ArgumentNullException(nameof(session));
			Frames = frames ?? throw new ArgumentNullException(nameof(frames));
			RuntimeAvailable = runtimeAvailable;
			UnavailableReason = unavailableReason ?? string.Empty;
			_ownedResources = new ReadOnlyCollection<IDisposable>((ownedResources ?? Enumerable.Empty<IDisposable>()).Where(x => x != null).ToList());
		}
		public void Dispose() {
			Session.Dispose();
			for (var index = _ownedResources.Count - 1; index >= 0; index--) {
				try { _ownedResources[index].Dispose(); } catch { /* teardown is best effort and remains idempotent */ }
			}
		}
	}

	public interface IApplicationRuntimeSessionFactory {
		Result<ApplicationRuntimeComposition, Diagnostic> Create(ProjectDocument document, NodeTypeRegistry registry);
	}

	/// <summary>Optional Bootstrap seam for project-scoped services.  The
	/// existing factory contract stays source-compatible with headless tests,
	/// while production media resolution receives the validated project root
	/// before a RuntimeSession is created.</summary>
	public interface IProjectRootAwareRuntimeSessionFactory {
		void SetProjectRoot(string projectRoot);
	}

	/// <summary>Core-only fallback used by EditMode tests and headless tools.</summary>
	public sealed class MinimalApplicationRuntimeSessionFactory : IApplicationRuntimeSessionFactory {
		public Result<ApplicationRuntimeComposition, Diagnostic> Create(ProjectDocument document, NodeTypeRegistry registry) {
			if (document == null || registry == null) return Result.Failure<ApplicationRuntimeComposition, Diagnostic>(new Diagnostic(new DiagnosticCode("application.runtime.composition_invalid"), Severity.Error, "Runtime composition requires a document and node registry."));
			var session = new RuntimeSession(document, registry, new DiagnosticHub("application.runtime"));
			return Result.Success<ApplicationRuntimeComposition, Diagnostic>(new ApplicationRuntimeComposition(session, new FrameCoordinator(session), false, "No application runtime bindings were supplied by Bootstrap."));
		}
	}

	/// <summary>
	/// The only mutable Project boundary used by Presentation and Input.  All
	/// project switches use an isolated Persistence candidate and install it
	/// only after the candidate has been fully validated and persisted.
	/// </summary>
	public sealed class ProjectApplication : IProjectApplicationReadPort, IProjectApplicationCommandPort, IApplicationCommandPort, IKeyboardInputApplicationPort, IMidiInputApplicationPort, ILiveControlApplicationPort, IApplicationShortcutCommandPort, IDisposable {
		private static readonly string[] m_InstantEffectPhysicalKeys = { "q", "w", "e", "r", "t", "y", "u", "i", "o", "p" };
		// Terminal command results are UI feedback, not an unbounded audit
		// log. Keep a bounded public history so the completion snapshot is
		// observable without making sustained input grow every frame.
		public const int TerminalCommandHistoryLimit = 256;
		private enum CommandQueueKind { Immediate, Graph, Parameter, Runtime, Task }
		private sealed class CommandLedgerEntry {
			internal readonly Guid RequestId;
			internal readonly Guid InteractionId;
			internal readonly Guid SessionId;
			internal readonly long Revision;
			internal CommandQueueKind Kind;
			internal readonly List<ulong> ParameterSequences = new List<ulong>();
			internal readonly List<string> GraphCommandIds = new List<string>();
			internal readonly HashSet<ulong> ObservedParameterSequences = new HashSet<ulong>();
			internal readonly HashSet<string> ObservedGraphCommandIds = new HashSet<string>(StringComparer.Ordinal);
			internal string RuntimeCommandId;
			internal CommandLedgerEntry(Guid requestId, Guid interactionId, Guid sessionId, long revision, CommandQueueKind kind) { RequestId = requestId; InteractionId = interactionId; SessionId = sessionId; Revision = revision; Kind = kind; }
		}

		private sealed class MediaImportBatchOperation {
			internal readonly Guid RequestId;
			internal readonly Guid SessionId;
			internal readonly IReadOnlyList<ApplicationMediaImportRequest> Requests;
			internal readonly List<MediaAssetRecord> Imported = new List<MediaAssetRecord>();
			internal int Index;
			internal MediaAssetImportTransaction Transaction;
			internal MediaImportBatchOperation(Guid requestId, Guid sessionId, IReadOnlyList<ApplicationMediaImportRequest> requests) { RequestId = requestId; SessionId = sessionId; Requests = requests; }
		}

		private readonly IProjectFileSystem _fileSystem;
		private readonly NodeTypeRegistry _registry;
		private readonly INodeSchemaCatalog _catalog;
		private readonly NodeMigrationRegistry _nodeMigrations;
		private readonly ProjectFormatMigrationRegistry _projectMigrations;
		private readonly MediaDeletionSession _mediaDeletions = new MediaDeletionSession();
		private readonly List<string> _recent = new List<string>();
		private long _recentRevision;
		private long _cachedRecentRevision = long.MinValue;
		private IReadOnlyList<string> _cachedRecentProjection;
		private readonly List<PendingCommandReadModel> _commands = new List<PendingCommandReadModel>();
		private long _commandRevision;
		private long _cachedCommandRevision = long.MinValue;
		private IReadOnlyList<PendingCommandReadModel> _cachedCommandProjection;
		private readonly Dictionary<Guid, int> _commandIndices = new Dictionary<Guid, int>();
		private readonly Dictionary<Guid, CommandLedgerEntry> _ledger = new Dictionary<Guid, CommandLedgerEntry>();
		private readonly Dictionary<ulong, Guid> _parameterRequests = new Dictionary<ulong, Guid>();
		private readonly Dictionary<string, Guid> _graphRequests = new Dictionary<string, Guid>(StringComparer.Ordinal);
		// FrameCoordinator reports runtime outcomes by its caller supplied
		// command-request ID.  Keep that correlation at enqueue time; a
		// value scan of the command ledger here makes sustained shortcuts
		// quadratic as terminal feedback accumulates.
		private readonly Dictionary<string, Guid> _runtimeRequests = new Dictionary<string, Guid>(StringComparer.Ordinal);
		private readonly Dictionary<Guid, Guid> _latestParameterRequestByInteraction = new Dictionary<Guid, Guid>();
		private ProjectDocument _document;
		private ProjectCommandProcessor _projectCommands;
		private RuntimeSession _runtime;
		private FrameCoordinator _frames;
		private string _root;
		private bool _recovered;
		private bool _disposed;
		private Guid _sessionId = Guid.NewGuid();
		private long _readVersion;
		private ulong _sequence;
		private LogicalControlId? _learningControl;
		private ApplicationProjectState _state = ApplicationProjectState.Empty;
		private ApplicationReadModel _publishedReadModel;
		private bool _nextSnapshotFull = true;
		private ApplicationTaskReadModel _task;
		private OutputPresentation _lastPresentation;
		private readonly Dictionary<string, ApplicationPreviewSettingsRequest> _previewSettings = new Dictionary<string, ApplicationPreviewSettingsRequest>(StringComparer.Ordinal);
		private readonly Dictionary<string, ApplicationOutputDemandRequest> _previewDemands = new Dictionary<string, ApplicationOutputDemandRequest>(StringComparer.Ordinal);
		private bool _previewHostVisible = true;
		private bool _programWasHolding;
		private double _programHoldingStartClock = double.NaN;
		private string _programHoldingCauseNodeId = string.Empty;
		private string _programHoldingDiagnosticCode = string.Empty;
		private string _workspaceLayoutId = "default";
		private bool _workspaceLayoutDirty;
		private readonly Dictionary<string, ProjectReadModel> _previousProjects = new Dictionary<string, ProjectReadModel>(StringComparer.Ordinal);
		private readonly Dictionary<string, ApplicationParameterReadModel> _previousParameters = new Dictionary<string, ApplicationParameterReadModel>(StringComparer.Ordinal);
		private readonly Dictionary<string, ApplicationDiagnosticReadModel> _previousDiagnostics = new Dictionary<string, ApplicationDiagnosticReadModel>(StringComparer.Ordinal);
		private readonly Dictionary<string, ApplicationGraphNodeReadModel> _previousGraphNodes = new Dictionary<string, ApplicationGraphNodeReadModel>(StringComparer.Ordinal);
		private readonly Dictionary<string, ApplicationGraphConnectionReadModel> _previousGraphConnections = new Dictionary<string, ApplicationGraphConnectionReadModel>(StringComparer.Ordinal);
		// These projections contain only immutable document/catalog metadata.
		// Runtime output, effective values, controls, diagnostics and command
		// terminal state remain frame-local below.  Caching this structural
		// layer prevents a normal presentation Tick from rehashing imported
		// media or rebuilding catalog/control metadata.
		private ProjectDocument _cachedProjectDocument;
		private long _cachedProjectDocumentRevision = long.MinValue;
		private string _cachedProjectRoot;
		private bool _cachedProjectRecovered;
		private bool _cachedProjectDirty;
		private ProjectReadModel _cachedProjectModel;
		private ProjectDocument _cachedMediaDocument;
		private long _cachedMediaDocumentRevision = long.MinValue;
		private string _cachedMediaRoot;
		private IReadOnlyList<ApplicationMediaReadModel> _cachedMediaProjection;
		private long _cachedMediaDeletionRevision = long.MinValue;
		private IReadOnlyList<MediaDeletionReadModel> _cachedMediaDeletionProjection;
		private ProjectDocument _cachedDocumentListProjectionDocument;
		private long _cachedDocumentListProjectionRevision = long.MinValue;
		private ProjectUiStateRecord _cachedDocumentListProjectionUi;
		private IReadOnlyList<ApplicationDashboardReadModel> _cachedDashboardProjection;
		private IReadOnlyList<ApplicationPresetReadModel> _cachedPresetProjection;
		private IReadOnlyList<string> _cachedWorkspaceVisiblePanelIds;
		private ApplicationShellReadModel _cachedShellModel;
		private ApplicationProjectState _cachedShellState;
		private string _cachedShellProjectName;
		private string _cachedShellRoot;
		private bool _cachedShellDirty;
		private bool _cachedShellRecovered;
		private bool _cachedShellCanUndo;
		private bool _cachedShellCanRedo;
		private string _cachedShellStatus;
		private ApplicationWorkspaceReadModel _cachedWorkspaceModel;
		private string _cachedWorkspaceLayoutId;
		private bool _cachedWorkspaceDirty;
		private IReadOnlyList<string> _cachedWorkspaceVisiblePanelSource;
		private long _cachedCatalogRevision = -1;
		private bool _cachedCatalogRuntimeAvailable;
		private string _cachedCatalogRuntimeUnavailableReason;
		private IReadOnlyList<ApplicationNodeCatalogEntry> _cachedCatalogProjection;
		private DiagnosticHub _cachedDiagnosticHub;
		private long _cachedDiagnosticRevision = long.MinValue;
		private IReadOnlyList<ApplicationDiagnosticReadModel> _cachedDiagnosticHistoryProjection;
		private IReadOnlyList<ApplicationDiagnosticReadModel> _cachedCurrentDiagnosticProjection;
		private IDictionary<string, long> _cachedDiagnosticSummaryProjection;
		private ApplicationDiagnosticsReadModel _cachedDiagnosticModel;
		private ReadModelChangeSet<ApplicationDiagnosticReadModel> _cachedDiagnosticChanges;
		private ProjectDocument _cachedParameterDocument;
		private long _cachedParameterDocumentRevision = long.MinValue;
		private long _cachedParameterEffectiveRevision = long.MinValue;
		private long _cachedParameterControlRevision = long.MinValue;
		private IList<ApplicationParameterReadModel> _cachedParameterProjection;
		private IReadOnlyDictionary<string, float> _cachedControlValueProjection;
		private IReadOnlyDictionary<string, ApplicationControlRuntimeReadModel> _cachedControlRuntimeProjection;
		private readonly Dictionary<string, ApplicationParameterReadModel> _cachedParameterRows = new Dictionary<string, ApplicationParameterReadModel>(StringComparer.Ordinal);
		private readonly List<string> _cachedParameterOrder = new List<string>();
		private ProjectDocument _cachedGraphDocument;
		// GraphState.Revision advances only for structural graph edits.  A
		// ProjectDocument revision also advances for unrelated parameter/media
		// edits, so it must not rebuild graph rows on every frame.
		private long _cachedGraphTopologyRevision = long.MinValue;
		private IReadOnlyList<ApplicationGraphPortReadModel> _cachedGraphPorts;
		private IReadOnlyList<ApplicationGraphConnectionReadModel> _cachedGraphConnections;
		private readonly Dictionary<string, ApplicationGraphNodeReadModel> _cachedGraphNodeRows = new Dictionary<string, ApplicationGraphNodeReadModel>(StringComparer.Ordinal);
		private readonly List<string> _cachedGraphNodeOrder = new List<string>();
		private IReadOnlyList<ApplicationGraphNodeReadModel> _cachedGraphNodeProjection;
		private ApplicationGraphReadModel _cachedGraphModel;
		private ReadModelChangeSet<ApplicationGraphNodeReadModel> _cachedGraphNodeChanges;
		private ReadModelChangeSet<ApplicationGraphConnectionReadModel> _cachedGraphConnectionChanges;
		private ReadModelChangeSet<ApplicationParameterReadModel> _cachedParameterChanges;
		private ApplicationReadModelChangeSets _cachedSplitChanges;
		private ApplicationReadModelChangeSets _cachedEmptySplitChanges;
		private readonly IMediaAssetProbe _mediaProbe;
		private readonly IApplicationRuntimeSessionFactory _runtimeFactory;
		private readonly IRecentProjectStore _recentProjectStore;
		private MediaImportBatchOperation _mediaBatch;
		private ApplicationRuntimeComposition _runtimeComposition;
		private bool _runtimeAvailable;
		private string _runtimeUnavailableReason = "No application runtime bindings were supplied by Bootstrap.";

		public ProjectApplication(IProjectFileSystem fileSystem, NodeTypeRegistry registry = null, INodeSchemaCatalog catalog = null, NodeMigrationRegistry nodeMigrations = null, ProjectFormatMigrationRegistry projectMigrations = null, IMediaAssetProbe mediaProbe = null, IApplicationRuntimeSessionFactory runtimeFactory = null, IRecentProjectStore recentProjectStore = null) {
			_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
			_registry = registry ?? new NodeTypeRegistry();
			_catalog = catalog;
			_nodeMigrations = nodeMigrations;
			_projectMigrations = projectMigrations;
			_mediaProbe = mediaProbe;
			_runtimeFactory = runtimeFactory ?? new MinimalApplicationRuntimeSessionFactory();
			_recentProjectStore = recentProjectStore;
			if (_recentProjectStore != null) {
				try {
					foreach (var root in NormalizeRecent(_recentProjectStore.ReadRecentProjectRoots())) {
						var full = root;
						try { full = _fileSystem.GetFullPath(root); } catch { }
						if (!_recent.Any(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase))) _recent.Add(full);
					}
				}
				catch {
					// User settings are recoverable preferences.  A damaged
					// settings file must never prevent an empty application
					// session from starting.
				}
			}
		}

		public ApplicationProjectState State => _state;
		public Guid ProjectSessionId => _sessionId;
		public string CurrentRoot => _root;
		// Runtime and FrameCoordinator are composition details.  Presentation
		// receives only the immutable split read model; tests and Bootstrap
		// drive frames through this narrow application port.
		public bool IsLearningKeyboard => _learningControl.HasValue;
		public bool IsKeyboardLearnActive => _learningControl.HasValue;
		public LogicalControlId? LearningControlId => _learningControl;
		public ApplicationReadModel ReadModel => _publishedReadModel ?? PublishReadModel(true);
		public IReadOnlyList<ApplicationShortcutMetadata> ShortcutCatalog => ApplicationShortcutCatalog.All;

		/// <summary>Returns the exact canonical Project payload hash without
		/// beginning a Save, publishing a task, or touching the filesystem.
		/// This is the persistence identity used for restart verification,
		/// rather than a projection that may also contain runtime UI state.</summary>
		public Result<string, Diagnostic> CaptureCanonicalProjectFingerprint() {
			if (_document == null) return Result.Failure<string, Diagnostic>(Failure("application.fingerprint.project_missing", "A canonical Project fingerprint requires a current project."));
			var snapshot = _document.TryCreateSaveSnapshot();
			if (snapshot.IsFailure) return Result.Failure<string, Diagnostic>(snapshot.Error);
			var serialized = ProjectSerializer.Serialize(snapshot.Value);
			if (serialized.IsFailure) return Result.Failure<string, Diagnostic>(serialized.Error);
			try { return Result.Success<string, Diagnostic>(AssetIntegrity.Hash(Encoding.UTF8.GetBytes(serialized.Value))); }
			catch (Exception exception) { return Result.Failure<string, Diagnostic>(Failure("application.fingerprint.hash_failed", "Canonical Project fingerprint hashing failed: " + exception.Message)); }
		}

		public ReadModelEnvelope<ProjectReadModel> ReadProject(bool fullSnapshot = true) {
			var current = ReadModel;
			if (current.Project == null) return null;
			if (fullSnapshot == current.Project.IsFullSnapshot) return current.Project;
			var model = current.Project.Model;
			return new ReadModelEnvelope<ProjectReadModel>(current.Project.ProjectSessionId, current.Project.ReadModelVersion, current.Project.FrameNumber, current.Project.DocumentRevision, current.Project.GraphRevision, fullSnapshot, model);
		}

		public ApplicationReadModel ReadLatest(long lastReadModelVersion = 0) => ReadSnapshot(lastReadModelVersion);

		/// <summary>Resets runtime diagnostics for a measurement interval
		/// through the public Application boundary. Active conditions are
		/// intentionally retained and remain visible to the next read model.</summary>
		public UnitResult<Diagnostic> ResetDiagnosticsForMeasurement(ulong measurementFrame = 0) {
			if (_runtime == null) return UnitResult.Success<Diagnostic>();
			_runtime.Diagnostics.ResetMeasurement(measurementFrame);
			_runtime.ResetPerformanceForMeasurement(measurementFrame);
			_previousDiagnostics.Clear();
			_nextSnapshotFull = true;
			return UnitResult.Success<Diagnostic>();
		}

		/// <summary>Returns the last published snapshot without advancing its version.
		/// A caller that missed a version receives a full snapshot marker.</summary>
		public ApplicationReadModel ReadSnapshot(long lastReadModelVersion = 0) {
			var current = ReadModel;
			if (lastReadModelVersion == current.Shell.ReadModelVersion || (lastReadModelVersion > 0 && lastReadModelVersion == current.Shell.ReadModelVersion - 1))
				return current;
			return current.AsFullSnapshot();
		}

		public ApplicationCommandResult NewProject(string projectName, string targetRoot, UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel) {
			return NewProjectCore(() => new NewProjectStager().Create(projectName, targetRoot, _fileSystem), targetRoot, decision);
		}

		/// <summary>Opens a complete composition-authored project without routing
		/// its initial graph through the interactive editor command queue.</summary>
		public ApplicationCommandResult NewProject(ProjectDocument document, string targetRoot, UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel) {
			if (document == null) return Rejected(Guid.Empty, Failure("application.project.document_missing", "An authored project document is required."));
			return NewProjectCore(() => new NewProjectStager().Create(document, targetRoot, _fileSystem), targetRoot, decision);
		}

		private ApplicationCommandResult NewProjectCore(Func<Result<NewProjectResult, Diagnostic>> create, string targetRoot, UnsavedChangesDecision decision) {
			if (!CanReplaceCurrent(decision, out var guard)) return guard;
			var request = BeginRequest(Guid.Empty);
			_state = ApplicationProjectState.Loading;
			BeginTask("New", targetRoot, "Staging", "Pending");
			var result = create();
			if (result.IsFailure) { SetTask("Failed", result.Error); return Complete(request, ApplicationCommandStatus.Rejected, result.Error, _document == null ? ApplicationProjectState.Empty : ApplicationProjectState.Ready); }
			SetTask("Readback", null);
			Install(result.Value.Document, result.Value.ProjectRoot, false, request);
			AddRecent(result.Value.ProjectRoot);
			SetTask("Completed", null);
			return Complete(request, ApplicationCommandStatus.Applied, null, ApplicationProjectState.Ready);
		}

		public ApplicationCommandResult OpenProject(string projectRoot, UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel) {
			if (!CanReplaceCurrent(decision, out var guard)) return guard;
			var request = BeginRequest(Guid.Empty);
			_state = ApplicationProjectState.Loading;
			BeginTask("Open", projectRoot, "Read", "Pending");
			var loaded = new ProjectLoader().Load(projectRoot, _fileSystem, _document, _catalog, _nodeMigrations, _projectMigrations);
			if (loaded.IsFailure) { SetTask("Failed", loaded.Error); return Complete(request, ApplicationCommandStatus.Rejected, loaded.Error, _document == null ? ApplicationProjectState.Empty : ApplicationProjectState.Ready); }
			Install(loaded.Value.Document, projectRoot, loaded.Value.IsRecovered, request);
			AddRecent(projectRoot);
			foreach (var diagnostic in loaded.Value.Diagnostics) _runtime?.Diagnostics.Report(diagnostic);
			SetTask("Completed", null);
			return Complete(request, ApplicationCommandStatus.Applied, null, ApplicationProjectState.Ready);
		}

		public ApplicationCommandResult OpenRecent(int index, UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel) {
			if (index < 0 || index >= _recent.Count)
				return Rejected(Guid.Empty, Failure("application.recent.invalid", "Recent project index is invalid."));
			return OpenProject(_recent[index], decision);
		}

		public ApplicationCommandResult SaveProject() {
			var request = BeginRequest(Guid.Empty);
			if (_document == null || string.IsNullOrWhiteSpace(_root)) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), ApplicationProjectState.Empty);
			_state = ApplicationProjectState.Saving;
			BeginTask("Save", _root, "Serialize", "Pending");
			var saved = new ProjectSaver().Save(_document, _root, _fileSystem);
			if (saved.IsFailure) { SetTask("Failed", saved.Error); return Complete(request, ApplicationCommandStatus.Rejected, saved.Error, ApplicationProjectState.Ready); }
			SetTask("FlushAndReplace", null);
			var deletion = _mediaDeletions.FinalizeAfterSave(_document, _fileSystem);
			if (deletion.IsFailure) SetTask("Failed", deletion.Error); else SetTask("Completed", null);
			return Complete(request, ApplicationCommandStatus.Applied, deletion.IsFailure ? deletion.Error : null, ApplicationProjectState.Ready);
		}

		public ApplicationCommandResult SaveAs(string targetRoot) {
			var request = BeginRequest(Guid.Empty);
			if (_document == null || string.IsNullOrWhiteSpace(_root)) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), ApplicationProjectState.Empty);
			_state = ApplicationProjectState.SaveAs;
			BeginTask("SaveAs", targetRoot, "StageCopy", "Pending");
			var saved = new PortableProjectSaver().SaveAs(_document, _root, targetRoot, _fileSystem);
			if (saved.IsFailure) { SetTask("Failed", saved.Error); return Complete(request, ApplicationCommandStatus.Rejected, saved.Error, ApplicationProjectState.Ready); }
			SetTask("SwitchRoot", null);
			CancelAllMediaDeletions();
			_root = targetRoot;
			AddRecent(targetRoot);
			SetTask("Completed", null);
			return Complete(request, ApplicationCommandStatus.Applied, null, ApplicationProjectState.Ready);
		}

		public ApplicationCommandResult ExecuteShortcut(ApplicationShortcutCommand command) {
			switch (command) {
				case ApplicationShortcutCommand.Save: return SaveProject();
				case ApplicationShortcutCommand.SaveAs: return Rejected(Guid.Empty, Failure("application.shortcut.save_as_target", "Save As requires a target project root."));
				case ApplicationShortcutCommand.CloseProject: return CloseProject();
				case ApplicationShortcutCommand.NewProject: return Rejected(Guid.Empty, Failure("application.shortcut.new_target", "New Project requires a project name and target root."));
				case ApplicationShortcutCommand.OpenProject: return Rejected(Guid.Empty, Failure("application.shortcut.open_target", "Open Project requires a project root."));
				case ApplicationShortcutCommand.Undo: return Undo();
				case ApplicationShortcutCommand.Redo: return Redo();
				case ApplicationShortcutCommand.PauseResume: return QueuePauseResume();
				case ApplicationShortcutCommand.CloseActivePanel:
				case ApplicationShortcutCommand.CommandPalette:
				case ApplicationShortcutCommand.FocusDiagnostics:
				case ApplicationShortcutCommand.FocusProgram:
				case ApplicationShortcutCommand.Dismiss:
					var request = BeginRequest(Guid.Empty);
					return Complete(request, ApplicationCommandStatus.Applied, null, _state);
				default: return Rejected(Guid.Empty, Failure("application.shortcut.unsupported", "Shortcut is not supported by this application session."));
			}
		}

		public ApplicationCommandResult EditParameter(ApplicationParameterEditRequest request) {
			if (request == null || !NodeInstanceId.TryParse(request.NodeId, out var nodeId) || !ParameterId.TryParse(request.ParameterId, out var parameterId))
				return Rejected(Guid.Empty, Failure("application.parameter.invalid", "Parameter edit IDs are invalid."));
			return EnqueueBaseValue(new BaseValueUpdate(nodeId, parameterId, request.Value), request.InteractionId);
		}

		public ApplicationCommandResult ApplyPreset(ApplicationPresetCommandRequest request) {
			if (request == null || !PresetId.TryParse(request.PresetId, out var presetId)) return Rejected(Guid.Empty, Failure("application.preset.invalid", "Preset ID is invalid."));
			return ApplyPreset(presetId);
		}

		public ApplicationCommandResult AddLogicalControl(ApplicationLogicalControlRequest request) {
			if (request == null || !LogicalControlId.TryParse(request.Id, out var id)) return Rejected(Guid.Empty, Failure("application.input.control_invalid", "Logical control ID is invalid."));
			if (!Enum.TryParse<LogicalControlKind>(request.Kind.ToString(), out var kind)) return Rejected(Guid.Empty, Failure("application.input.control_kind", "Logical control kind is invalid."));
			PresetId? preset = null;
			if (!string.IsNullOrWhiteSpace(request.PresetId)) { if (!PresetId.TryParse(request.PresetId, out var parsed)) return Rejected(Guid.Empty, Failure("application.preset.invalid", "Preset ID is invalid.")); preset = parsed; }
			var mappings = request.Mappings.Select(ToControlMapping);
			return AddLogicalControl(new LogicalControlRecord(id, request.Name, kind, request.InitialValue, mappings: mappings, presetId: preset));
		}

		public ApplicationCommandResult RenameLogicalControl(string logicalControlId, string name) {
			if (!LogicalControlId.TryParse(logicalControlId, out var id)) return Rejected(Guid.Empty, Failure("application.input.control_invalid", "Logical control ID is invalid."));
			return CompleteProjectMutation(_projectCommands == null ? UnitResult.Failure<Diagnostic>(Failure("application.project.missing", "There is no current project.")) : _projectCommands.RenameLogicalControl(id, name));
		}

		public ApplicationCommandResult SetLogicalControlTargets(string logicalControlId, IEnumerable<ApplicationLogicalControlTargetRequest> targets) {
			if (!LogicalControlId.TryParse(logicalControlId, out var id)) return Rejected(Guid.Empty, Failure("application.input.control_invalid", "Logical control ID is invalid."));
			try {
				var records = (targets ?? Enumerable.Empty<ApplicationLogicalControlTargetRequest>()).Select(target => {
					if (!NodeInstanceId.TryParse(target.NodeId, out var node) || !ParameterId.TryParse(target.ParameterId, out var parameter)) throw new ArgumentException("Logical control target IDs are invalid.");
					return new LogicalControlTargetRecord(node, parameter, target.TargetMin.Type, target.TargetMin, target.TargetMax, target.Invert);
				});
				return CompleteProjectMutation(_projectCommands == null ? UnitResult.Failure<Diagnostic>(Failure("application.project.missing", "There is no current project.")) : _projectCommands.SetLogicalControlTargets(id, records));
			}
			catch (Exception exception) { return Rejected(Guid.Empty, new Diagnostic(new DiagnosticCode("application.input.target_invalid"), Severity.Error, exception.Message, module: "application")); }
		}

		public ApplicationCommandResult ApplyExpression(ApplicationExpressionDraft request) {
			if (request == null || !NodeInstanceId.TryParse(request.NodeId, out var node) || !ParameterId.TryParse(request.ParameterId, out var parameter)) return Rejected(Guid.Empty, Failure("application.expression.invalid", "Expression target IDs are invalid."));
			try {
				var expression = BuildExpression(request);
				ParameterRange? range = null;
				if (request.OutputMinimum.HasValue != request.OutputMaximum.HasValue) throw new ArgumentException("Expression output clamp requires both bounds.");
				if (request.OutputMinimum.HasValue) range = new ParameterRange(request.OutputMinimum.Value, request.OutputMaximum.Value);
				return CompleteProjectMutation(_projectCommands == null ? UnitResult.Failure<Diagnostic>(Failure("application.project.missing", "There is no current project.")) : _projectCommands.AddExpression(new ParameterExpressionRecord(node, parameter, expression, range)));
			}
			catch (Exception exception) { return Rejected(Guid.Empty, new Diagnostic(new DiagnosticCode("application.expression.invalid"), Severity.Error, exception.Message, module: "application")); }
		}

		public ApplicationCommandResult SetLogicalControlMappings(string logicalControlId, IEnumerable<ApplicationControlMappingRequest> mappings) {
			if (!LogicalControlId.TryParse(logicalControlId, out var id)) return Rejected(Guid.Empty, Failure("application.input.control_invalid", "Logical control ID is invalid."));
			try { return SetLogicalControlMappings(id, (mappings ?? Enumerable.Empty<ApplicationControlMappingRequest>()).Select(ToControlMapping)); }
			catch (Exception exception) { return Rejected(Guid.Empty, new Diagnostic(new DiagnosticCode("application.input.mapping_invalid"), Severity.Error, exception.Message, module: "application")); }
		}

		public ApplicationCommandResult SetPresetTriggerBinding(string logicalControlId, string presetId) {
			if (!LogicalControlId.TryParse(logicalControlId, out var id)) return Rejected(Guid.Empty, Failure("application.input.control_invalid", "Logical control ID is invalid."));
			PresetId? parsed = null;
			if (!string.IsNullOrWhiteSpace(presetId)) { if (!PresetId.TryParse(presetId, out var value)) return Rejected(Guid.Empty, Failure("application.preset.invalid", "Preset ID is invalid.")); parsed = value; }
			return SetPresetTriggerBinding(id, parsed);
		}

		public ApplicationCommandResult DeleteLogicalControl(string logicalControlId) {
			if (!LogicalControlId.TryParse(logicalControlId, out var id)) return Rejected(Guid.Empty, Failure("application.input.control_invalid", "Logical control ID is invalid."));
			var request = BeginRequest(Guid.Empty);
			if (_projectCommands == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var result = _projectCommands.DeleteLogicalControl(id); if (result.IsSuccess) SynchronizeRuntime();
			return Complete(request, result.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, result.IsSuccess ? null : result.Error, _state);
		}

		public ApplicationCommandResult AddMediaAsset(ApplicationMediaAssetRequest request) {
			if (request == null || !MediaAssetId.TryParseUuidV4(request.Id, out var id)) return Rejected(Guid.Empty, Failure("application.media.invalid", "Media asset ID is invalid."));
			if (!Enum.TryParse(request.Kind, true, out MediaAssetKind kind) || !Enum.TryParse(request.ColorSpace, true, out MediaColorSpace colorSpace) || !Enum.TryParse(request.AlphaMode, true, out MediaAlphaMode alphaMode)) return Rejected(Guid.Empty, Failure("application.media.metadata", "Media asset metadata is invalid."));
			try { return AddMediaAsset(new MediaAssetRecord(id, request.DisplayName, request.RelativePath, request.ByteSize, request.IntegrityHash, kind, colorSpace, alphaMode)); }
			catch (Exception exception) { return Rejected(Guid.Empty, new Diagnostic(new DiagnosticCode("application.media.invalid"), Severity.Error, exception.Message, module: "application")); }
		}

		public ApplicationCommandResult ImportMedia(ApplicationMediaImportRequest request) {
			return ImportMediaBatch(request == null ? Enumerable.Empty<ApplicationMediaImportRequest>() : new[] { request });
		}

		public ApplicationCommandResult ImportMediaBatch(IEnumerable<ApplicationMediaImportRequest> requests) {
			var items = (requests ?? Enumerable.Empty<ApplicationMediaImportRequest>()).ToList();
			if (items.Count == 0 || items.Count > 64 || string.IsNullOrWhiteSpace(_root) || _projectCommands == null)
				return Rejected(Guid.Empty, Failure("application.media.import_invalid", "A non-empty media import batch and current project are required."));
			if (_mediaBatch != null)
				return Rejected(Guid.Empty, Failure("application.media.import_busy", "A media import batch is already running."));
			foreach (var item in items)
				if (item == null || string.IsNullOrWhiteSpace(item.SourcePath) || !Enum.TryParse(item.Kind, true, out MediaAssetKind _) || !Enum.TryParse(item.ColorSpace, true, out MediaColorSpace _) || !Enum.TryParse(item.AlphaMode, true, out MediaAlphaMode _))
					return Rejected(Guid.Empty, Failure("application.media.import_invalid", "Media import metadata is invalid."));

			var request = BeginRequest(Guid.Empty);
			_ledger[request].Kind = CommandQueueKind.Task;
			_mediaBatch = new MediaImportBatchOperation(request, _sessionId, new ReadOnlyCollection<ApplicationMediaImportRequest>(items));
			BeginTask("ImportBatch", _root, "Queued", "Pending", 0, items.Count, null);
			PublishReadModel(false);
			return KeepAccepted(request);
		}

		/// <summary>Approves or rejects a probe result that needs user confirmation.</summary>
		public ApplicationCommandResult ConfirmMediaImport(bool approved) {
			if (_mediaBatch == null || _mediaBatch.Transaction == null || _mediaBatch.Transaction.Stage != MediaAssetImportTransactionStage.AwaitingProbeConfirmation)
				return Rejected(Guid.Empty, Failure("application.media.probe_confirmation_invalid", "No media import is awaiting probe confirmation."));
			var result = _mediaBatch.Transaction.ConfirmProbe(approved);
			if (result.IsFailure) {
				var operation = _mediaBatch;
				CleanupImportedFiles(operation.Imported);
				_mediaBatch = null;
				SetTask("Failed", result.Error);
				return Complete(operation.RequestId, ApplicationCommandStatus.Rejected, result.Error, _state);
			}
			SetTaskProgress("Rename", _mediaBatch.Index, _mediaBatch.Requests.Count, _mediaBatch.Requests[_mediaBatch.Index].DisplayName);
			PublishReadModel(false);
			return CompleteImmediate(ApplicationCommandStatus.Applied, null);
		}

		public ApplicationCommandResult CancelMediaImport() {
			var operation = _mediaBatch;
			if (operation == null) return Rejected(Guid.Empty, Failure("application.media.cancel_invalid", "No media import batch is running."));
			try { operation.Transaction?.Cancel(); } catch { }
			CleanupImportedFiles(operation.Imported);
			_mediaBatch = null;
			var diagnostic = Failure("application.media.cancelled", "The media import was cancelled.");
			if (_task != null) _task = new ApplicationTaskReadModel(_task.TaskId, _task.Kind, "Cancelled", "Cancelled", _task.Path, diagnostic, _task.CompletedItems, _task.TotalItems, _task.CurrentItem);
			return Complete(operation.RequestId, ApplicationCommandStatus.Cancelled, diagnostic, _state);
		}

		public ApplicationCommandResult RebindMedia(string mediaAssetId, string nodeId, string parameterId) {
			if (!MediaAssetId.TryParseUuidV4(mediaAssetId, out var media) || !NodeInstanceId.TryParse(nodeId, out var node) || !ParameterId.TryParse(parameterId, out var parameter)) return Rejected(Guid.Empty, Failure("application.media.rebind_invalid", "Media rebind IDs are invalid."));
			var record = _document?.FindNode(node)?.FindParameter(parameter);
			if (record == null || record.Definition.Type != ParameterType.MediaAssetReference) return Rejected(Guid.Empty, Failure("application.media.rebind_target", "The target parameter is not a media reference."));
			return CompleteProjectMutation(_projectCommands == null ? UnitResult.Failure<Diagnostic>(Failure("application.project.missing", "There is no current project.")) : _projectCommands.SetBaseValue(node, parameter, ParameterValue.FromMediaAsset(media)));
		}

		public ApplicationCommandResult ConfirmDeleteMedia(string mediaAssetId, ApplicationMediaDeleteDecision decision) {
			if (decision == ApplicationMediaDeleteDecision.Cancel) return ApplicationCommandResult.Ignored(_sessionId);
			return DeleteMediaAsset(mediaAssetId);
		}

		public ApplicationCommandResult InspectMediaReferences(string mediaAssetId, out IReadOnlyList<ApplicationMediaReferenceReadModel> references) {
			references = new ReadOnlyCollection<ApplicationMediaReferenceReadModel>(new List<ApplicationMediaReferenceReadModel>());
			if (!MediaAssetId.TryParseUuidV4(mediaAssetId, out var id)) return Rejected(Guid.Empty, Failure("application.media.invalid", "Media asset ID is invalid."));
			if (_document == null) return Rejected(Guid.Empty, Failure("application.project.missing", "There is no current project."));
			var list = new List<ApplicationMediaReferenceReadModel>();
			foreach (var node in _document.Nodes)
				foreach (var parameter in node.Parameters.Where(x => x.BaseValue.IsMediaAssetSelected && x.BaseValue.AsMediaAsset().Value == id)) list.Add(new ApplicationMediaReferenceReadModel(id.Value, "Parameter", node.Id.Value, parameter.Definition.Id.Value, parameter.IsBroken));
			foreach (var preset in _document.Presets)
				foreach (var entry in preset.Entries.Where(x => x.Value.IsMediaAssetSelected && x.Value.AsMediaAsset().Value == id)) list.Add(new ApplicationMediaReferenceReadModel(id.Value, "Preset", preset.Id.Value, entry.ParameterId.Value, entry.IsBroken));
			references = new ReadOnlyCollection<ApplicationMediaReferenceReadModel>(list);
			return ApplicationCommandResult.Ignored(_sessionId);
		}

		public ApplicationCommandResult DeleteMediaAsset(string mediaAssetId) {
			if (!MediaAssetId.TryParseUuidV4(mediaAssetId, out var id)) return Rejected(Guid.Empty, Failure("application.media.invalid", "Media asset ID is invalid."));
			return DeleteMediaAsset(id);
		}

		public ApplicationCommandResult AddPreset(ApplicationPresetRequest request) {
			if (request == null || !PresetId.TryParse(request.Id, out var id)) return Rejected(Guid.Empty, Failure("application.preset.invalid", "Preset ID is invalid."));
			try {
				var entries = request.Entries.Select(entry => {
					if (!NodeInstanceId.TryParse(entry.NodeId, out var nodeId) || !ParameterId.TryParse(entry.ParameterId, out var parameterId)) throw new ArgumentException("Preset entry IDs are invalid.");
					return new PresetEntryRecord(nodeId, parameterId, entry.Value.Type, entry.Value);
				});
				var command = BeginRequest(Guid.Empty);
				if (_projectCommands == null) return Complete(command, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
				var result = _projectCommands.AddPreset(new PresetRecord(id, request.Name, request.Category, request.SortIndex, entries)); if (result.IsSuccess) SynchronizeRuntime();
				return Complete(command, result.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, result.IsSuccess ? null : result.Error, _state);
			}
			catch (Exception exception) { return Rejected(Guid.Empty, new Diagnostic(new DiagnosticCode("application.preset.invalid"), Severity.Error, exception.Message, module: "application")); }
		}

		public ApplicationCommandResult RenamePreset(string presetId, string name) {
			if (!PresetId.TryParse(presetId, out var id)) return Rejected(Guid.Empty, Failure("application.preset.invalid", "Preset ID is invalid."));
			return CompleteProjectMutation(_projectCommands == null ? UnitResult.Failure<Diagnostic>(Failure("application.project.missing", "There is no current project.")) : _projectCommands.RenamePreset(id, name));
		}

		public ApplicationCommandResult DuplicatePreset(string presetId, string newPresetId, string name) {
			if (!PresetId.TryParse(presetId, out var source) || !PresetId.TryParse(newPresetId, out var target)) return Rejected(Guid.Empty, Failure("application.preset.invalid", "Preset IDs are invalid."));
			var sourcePreset = _document?.FindPreset(source);
			if (sourcePreset == null) return Rejected(Guid.Empty, Failure("application.preset.missing", "Preset does not exist."));
			return AddPreset(new ApplicationPresetRequest(target.Value, name, sourcePreset.Category, sourcePreset.SortIndex, sourcePreset.Entries.Select(entry => new ApplicationPresetEntryRequest(entry.NodeId.Value, entry.ParameterId.Value, entry.Value))));
		}

		public ApplicationCommandResult CapturePresetEntry(string presetId, ApplicationPresetEntryRequest entry) {
			if (!PresetId.TryParse(presetId, out var id) || entry == null || !NodeInstanceId.TryParse(entry.NodeId, out var node) || !ParameterId.TryParse(entry.ParameterId, out var parameter)) return Rejected(Guid.Empty, Failure("application.preset.entry_invalid", "Preset entry is invalid."));
			var preset = _document?.FindPreset(id); if (preset == null) return Rejected(Guid.Empty, Failure("application.preset.missing", "Preset does not exist."));
			var entries = preset.Entries.Where(x => x.NodeId != node || x.ParameterId != parameter).Concat(new[] { new PresetEntryRecord(node, parameter, entry.Value.Type, entry.Value) });
			return CompleteProjectMutation(_projectCommands == null ? UnitResult.Failure<Diagnostic>(Failure("application.project.missing", "There is no current project.")) : _projectCommands.SetPresetEntries(id, entries));
		}

		public ApplicationCommandResult RemovePresetEntry(string presetId, string nodeId, string parameterId) {
			if (!PresetId.TryParse(presetId, out var id) || !NodeInstanceId.TryParse(nodeId, out var node) || !ParameterId.TryParse(parameterId, out var parameter)) return Rejected(Guid.Empty, Failure("application.preset.entry_invalid", "Preset entry is invalid."));
			var preset = _document?.FindPreset(id); if (preset == null) return Rejected(Guid.Empty, Failure("application.preset.missing", "Preset does not exist."));
			return CompleteProjectMutation(_projectCommands == null ? UnitResult.Failure<Diagnostic>(Failure("application.project.missing", "There is no current project.")) : _projectCommands.SetPresetEntries(id, preset.Entries.Where(x => x.NodeId != node || x.ParameterId != parameter)));
		}

		public ApplicationCommandResult AddDashboardPage(ApplicationDashboardPageRequest request) => UpdateDashboardPage(request, false);
		public ApplicationCommandResult UpdateDashboardPage(ApplicationDashboardPageRequest request) => UpdateDashboardPage(request, true);

		private ApplicationCommandResult UpdateDashboardPage(ApplicationDashboardPageRequest request, bool replace) {
			if (request == null || string.IsNullOrWhiteSpace(request.PageId) || string.IsNullOrWhiteSpace(request.Name)) return Rejected(Guid.Empty, Failure("application.dashboard.invalid", "Dashboard page identity is required."));
			try {
				var widgets = (request.Widgets ?? new List<ApplicationDashboardWidgetRequest>()).Select(widget => {
					if (!NodeInstanceId.TryParse(widget.NodeId, out var node) || !ParameterId.TryParse(widget.ParameterId, out var parameter)) throw new ArgumentException("Dashboard widget target IDs are invalid.");
					return new DashboardWidgetRecord(widget.WidgetId, node, parameter, widget.Column, widget.Row, widget.Width, widget.Height, widget.Label);
				}).ToList();
				var pages = (_document?.Ui?.DashboardPages ?? Enumerable.Empty<DashboardPageRecord>()).ToList();
				var index = pages.FindIndex(x => string.Equals(x.PageId, request.PageId, StringComparison.Ordinal));
				if (index >= 0 && !replace) return Rejected(Guid.Empty, Failure("application.dashboard.exists", "Dashboard page already exists."));
				var page = new DashboardPageRecord(request.PageId, request.Name, widgets);
				if (index >= 0) pages[index] = page; else pages.Add(page);
				return CompleteProjectMutation(_projectCommands == null ? UnitResult.Failure<Diagnostic>(Failure("application.project.missing", "There is no current project.")) : _projectCommands.ReplaceUi(_document.Ui.WithDashboardPages(pages)));
			}
			catch (Exception exception) { return Rejected(Guid.Empty, new Diagnostic(new DiagnosticCode("application.dashboard.invalid"), Severity.Error, exception.Message, module: "application")); }
		}

		public ApplicationCommandResult DeleteDashboardPage(string pageId) {
			if (string.IsNullOrWhiteSpace(pageId)) return Rejected(Guid.Empty, Failure("application.dashboard.invalid", "Dashboard page ID is required."));
			var pages = (_document?.Ui?.DashboardPages ?? Enumerable.Empty<DashboardPageRecord>()).Where(x => !string.Equals(x.PageId, pageId, StringComparison.Ordinal)).ToList();
			if (_document == null) return Rejected(Guid.Empty, Failure("application.project.missing", "There is no current project."));
			if (pages.Count == _document.Ui.DashboardPages.Count) return Rejected(Guid.Empty, Failure("application.dashboard.missing", "Dashboard page does not exist."));
			return CompleteProjectMutation(_projectCommands.ReplaceUi(_document.Ui.WithDashboardPages(pages)));
		}

		public ApplicationCommandResult AddDashboardWidget(string pageId, ApplicationDashboardWidgetRequest request) {
			if (request == null || string.IsNullOrWhiteSpace(pageId)) return Rejected(Guid.Empty, Failure("application.dashboard.invalid", "Dashboard page and widget are required."));
			var page = _document?.Ui?.DashboardPages.FirstOrDefault(x => string.Equals(x.PageId, pageId, StringComparison.Ordinal));
			if (page == null) return Rejected(Guid.Empty, Failure("application.dashboard.missing", "Dashboard page does not exist."));
			var updated = new ApplicationDashboardPageRequest(page.PageId, page.Name, page.Widgets.Select(x => new ApplicationDashboardWidgetRequest(x.WidgetId, x.NodeId.Value, x.ParameterId.Value, x.Column, x.Row, x.Width, x.Height, x.Label)).Concat(new[] { request }));
			return UpdateDashboardPage(updated, true);
		}

		public ApplicationCommandResult RemoveDashboardWidget(string pageId, string widgetId) {
			var page = _document?.Ui?.DashboardPages.FirstOrDefault(x => string.Equals(x.PageId, pageId, StringComparison.Ordinal));
			if (page == null) return Rejected(Guid.Empty, Failure("application.dashboard.missing", "Dashboard page does not exist."));
			var updated = new ApplicationDashboardPageRequest(page.PageId, page.Name, page.Widgets.Where(x => !string.Equals(x.WidgetId, widgetId, StringComparison.Ordinal)).Select(x => new ApplicationDashboardWidgetRequest(x.WidgetId, x.NodeId.Value, x.ParameterId.Value, x.Column, x.Row, x.Width, x.Height, x.Label)));
			return UpdateDashboardPage(updated, true);
		}

		public ApplicationCommandResult RebindDashboardWidget(string pageId, string widgetId, string nodeId, string parameterId) {
			if (_document == null) return Rejected(Guid.Empty, Failure("application.project.missing", "There is no current project."));
			if (!NodeInstanceId.TryParse(nodeId, out var node) || !ParameterId.TryParse(parameterId, out var parameter))
				return Rejected(Guid.Empty, Failure("application.dashboard.rebind_invalid", "A valid node and parameter are required."));
			var targetNode = _document.Nodes.FirstOrDefault(x => x.Id == node);
			if (targetNode == null || targetNode.Parameters.All(x => x.Definition.Id != parameter))
				return Rejected(Guid.Empty, Failure("application.dashboard.rebind_missing", "The selected node parameter does not exist."));
			var page = _document.Ui?.DashboardPages.FirstOrDefault(x => string.Equals(x.PageId, pageId, StringComparison.Ordinal));
			if (page == null) return Rejected(Guid.Empty, Failure("application.dashboard.missing", "Dashboard page does not exist."));
			var widget = page.Widgets.FirstOrDefault(x => string.Equals(x.WidgetId, widgetId, StringComparison.Ordinal));
			if (widget == null) return Rejected(Guid.Empty, Failure("application.dashboard.widget_missing", "Dashboard widget does not exist."));
			var updated = new ApplicationDashboardPageRequest(page.PageId, page.Name, page.Widgets.Select(x =>
				string.Equals(x.WidgetId, widgetId, StringComparison.Ordinal)
					? new ApplicationDashboardWidgetRequest(x.WidgetId, nodeId, parameterId, x.Column, x.Row, x.Width, x.Height, x.Label)
					: new ApplicationDashboardWidgetRequest(x.WidgetId, x.NodeId.Value, x.ParameterId.Value, x.Column, x.Row, x.Width, x.Height, x.Label)));
			return UpdateDashboardPage(updated, true);
		}

		public ApplicationCommandResult OpenPreview(string previewId) {
			if (string.IsNullOrWhiteSpace(previewId) || _document == null) return Rejected(Guid.Empty, Failure("application.preview.invalid", "Preview ID and current project are required."));
			var node = _document.Nodes.FirstOrDefault(x => x.Id.Value == previewId);
			if (node == null || node.TypeId.Value != GraphConstants.PreviewTypeId) return Rejected(Guid.Empty, Failure("application.preview.not_preview_node", "Only a system.preview node can be opened as a Preview tab."));
			var current = _document.Ui.PreviewNodeIds.ToList();
			if (current.Contains(previewId, StringComparer.Ordinal)) {
				// A persisted/legacy tab may not have a runtime demand yet.
				// Opening it through the same command path must establish the
				// normal 640x360/30 demand and select it as the one focused
				// Viewer tab without changing the project.
				FocusPreviewDemand(previewId);
				var existingQueued = QueueVisiblePreviewDemands();
				return CompleteImmediate(existingQueued.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected,
					existingQueued.IsSuccess ? null : existingQueued.Error);
			}
			if (current.Count >= 8) {
				var names = string.Join(", ", current.Select(id => _document.Nodes.FirstOrDefault(x => x.Id.Value == id)?.DisplayName ?? id));
				return Rejected(Guid.Empty, Failure("application.preview.limit", "Preview tab limit (8) reached. Currently visible: " + names));
			}
			var result = CompleteProjectMutation(_projectCommands.ReplaceUi(_document.Ui.WithPreviewNodeIds(current.Concat(new[] { previewId }))));
			if (result.IsSuccess) {
				FocusPreviewDemand(previewId);
				var queued = QueueVisiblePreviewDemands();
				if (queued.IsFailure) return CompleteImmediate(ApplicationCommandStatus.Rejected, queued.Error);
			}
			return result;
		}

		public ApplicationCommandResult ClosePreview(string previewId) {
			if (_document == null) return Rejected(Guid.Empty, Failure("application.project.missing", "There is no current project."));
			var result = CompleteProjectMutation(_projectCommands.ReplaceUi(_document.Ui.WithPreviewNodeIds(_document.Ui.PreviewNodeIds.Where(x => !string.Equals(x, previewId, StringComparison.Ordinal)))));
			if (result.IsSuccess) {
				_previewDemands.Remove(previewId ?? string.Empty);
				if (NodeInstanceId.TryParse(previewId, out var previewNode)) _runtime?.RemovePreview(previewNode);
				var queued = QueueVisiblePreviewDemands();
				if (queued.IsFailure) return CompleteImmediate(ApplicationCommandStatus.Rejected, queued.Error);
			}
			return result;
		}

		public ApplicationCommandResult SetPreviewSettings(ApplicationPreviewSettingsRequest request) {
			if (request == null || string.IsNullOrWhiteSpace(request.PreviewId)) return Rejected(Guid.Empty, Failure("application.preview.invalid", "Preview settings require a preview ID."));
			var node = _document?.Nodes.FirstOrDefault(x => x.Id.Value == request.PreviewId);
			if (node == null || node.TypeId.Value != GraphConstants.PreviewTypeId) return Rejected(Guid.Empty, Failure("application.preview.not_preview_node", "Preview settings require a system.preview node."));
			if (!Enum.IsDefined(typeof(ApplicationOutputFitMode), request.FitMode)) return Rejected(Guid.Empty, Failure("application.preview.fit_invalid", "Preview fit mode must be Fit, Fill or Stretch."));
			if (!string.Equals(request.BackgroundMode, "Checker", StringComparison.OrdinalIgnoreCase) && !string.Equals(request.BackgroundMode, "Black", StringComparison.OrdinalIgnoreCase)) return Rejected(Guid.Empty, Failure("application.preview.background_invalid", "Preview background must be Checker or Black."));
			var raw = MergePreviewState(node.RawState, request);
			var result = CompleteProjectMutation(_projectCommands == null ? UnitResult.Failure<Diagnostic>(Failure("application.project.missing", "There is no current project.")) : _projectCommands.SetNodeRawState(node.Id, raw));
			if (result.IsSuccess) _previewSettings[request.PreviewId] = request;
			return result;
		}

		public ApplicationCommandResult RequestPreviewDemand(ApplicationOutputDemandRequest request) {
			if (request == null || _frames == null || !NodeInstanceId.TryParse(request.PreviewId, out var node)) return Rejected(Guid.Empty, Failure("application.preview.demand_invalid", "Preview demand requires a valid preview node and current project."));
			if (_document == null || !_document.Nodes.Any(item => item.Id == node && item.TypeId.Value == GraphConstants.PreviewTypeId)) return Rejected(Guid.Empty, Failure("application.preview.not_preview_node", "Output demand requires a system.preview node."));
			if (request.Width < 1 || request.Height < 1) return Rejected(Guid.Empty, Failure("application.preview.demand_invalid", "Preview dimensions must be positive."));
			if (!PortId.TryParse(request.PortId, out var portId)) return Rejected(Guid.Empty, Failure("application.preview.demand_invalid", "Preview output port ID is invalid."));
			if (request.Focused) {
				foreach (var pair in _previewDemands.Where(x => !string.Equals(x.Key, request.PreviewId, StringComparison.Ordinal) && x.Value.Focused).ToList())
					_previewDemands[pair.Key] = new ApplicationOutputDemandRequest(pair.Value.PreviewId, pair.Value.PortId, pair.Value.Width, pair.Value.Height, false);
			}
			_previewDemands[request.PreviewId] = request;
			var queued = QueueVisiblePreviewDemands();
			return CompleteImmediate(queued.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, queued.IsSuccess ? null : queued.Error);
		}

		public ApplicationCommandResult SetPreviewHostVisible(bool visible) {
			_previewHostVisible = visible;
			// Host visibility is a presentation/layout state. It does not
			// alter the project document or saved tab assignment. Runtime is
			// explicitly told to hide active demands, while quality state is
			// retained for a later host show.
			var queued = visible ? QueueVisiblePreviewDemands() : (_runtime == null ? UnitResult.Success<Diagnostic>() : _runtime.HideAllPreviews());
			return CompleteImmediate(queued.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, queued.IsSuccess ? null : queued.Error);
		}

		public ApplicationCommandResult SetProgramDisplay(int display) {
			if (display < 1) return Rejected(Guid.Empty, Failure("application.output.display_invalid", "Program display must be positive."));
			return CompleteProjectMutation(_projectCommands == null ? UnitResult.Failure<Diagnostic>(Failure("application.project.missing", "There is no current project.")) : _projectCommands.SetOutputSettings((_document?.Settings ?? ProjectOutputSettings.CreateDefault()).WithProgramDisplay(display)));
		}

		public ApplicationCommandResult ResetFeedback(string nodeId) {
			if (!NodeInstanceId.TryParse(nodeId, out var node)) return Rejected(Guid.Empty, Failure("application.feedback.invalid", "Feedback node ID is invalid."));
			var request = BeginRequest(Guid.Empty);
			if (_frames == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var command = RuntimeCommand.ResetFeedback(node, request.ToString("D"));
			var queued = _frames.EnqueueRuntimeCommand(command);
			if (queued.IsFailure) return Complete(request, ApplicationCommandStatus.Rejected, queued.Error, _state);
			TrackRuntime(request, command.CommandRequestId);
			return KeepAccepted(request);
		}

		public ApplicationCommandResult ExportDiagnostics(string path, bool json) {
			var result = json ? ExportDiagnosticsJson(path) : ExportDiagnostics(path);
			return CompleteImmediate(result.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, result.IsSuccess ? null : result.Error);
		}

		public ApplicationCommandResult SetWorkspaceLayout(string layoutId, bool dirty) {
			if (string.IsNullOrWhiteSpace(layoutId)) return Rejected(Guid.Empty, Failure("application.workspace.invalid", "Workspace layout ID is required."));
			_workspaceLayoutId = layoutId.Trim(); _workspaceLayoutDirty = dirty;
			return CompleteImmediate(ApplicationCommandStatus.Applied, null);
		}

		public ApplicationCommandResult DeletePreset(string presetId) {
			if (!PresetId.TryParse(presetId, out var id)) return Rejected(Guid.Empty, Failure("application.preset.invalid", "Preset ID is invalid."));
			var request = BeginRequest(Guid.Empty);
			if (_projectCommands == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var result = _projectCommands.DeletePreset(id); if (result.IsSuccess) SynchronizeRuntime();
			return Complete(request, result.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, result.IsSuccess ? null : result.Error, _state);
		}

		public ApplicationCommandResult BeginKeyboardLearn(string logicalControlId) {
			return !LogicalControlId.TryParse(logicalControlId, out var id) ? Rejected(Guid.Empty, Failure("application.input.control_invalid", "Logical control ID is invalid.")) : BeginKeyboardLearn(id);
		}

		public ApplicationCommandResult CancelKeyboardLearn() => CancelKeyboardLearn(null);

		public ApplicationCommandResult SubmitGraph(ApplicationGraphEditRequest request) {
			if (request == null) return Rejected(Guid.Empty, Failure("application.graph.invalid", "Graph request is required."));
			var applicationRequest = BeginRequest(Guid.Empty);
			GraphEditCommand command = null;
			try {
				var commandId = applicationRequest.ToString("D");
				switch (request.Kind) {
					case ApplicationGraphEditKind.AddNode:
						if (!NodeInstanceId.TryParse(request.TargetId, out var newNodeId) || !NodeTypeId.TryParse(request.NodeTypeId, out var newTypeId) || !_registry.TryGet(newTypeId, out var definition)) throw new ArgumentException("Node type or node ID is invalid.");
						var parameters = definition.Parameters.Select(parameter => new ParameterRecord(parameter, parameter.DefaultValue));
						var ports = definition.Ports.Select(port => port.ToSnapshot());
						command = new AddNodeEditCommand(new NodeRecord(newNodeId, newTypeId, request.SchemaVersion < 1 ? definition.SchemaVersion : request.SchemaVersion, string.IsNullOrWhiteSpace(request.NodeDisplayName) ? definition.DisplayName : request.NodeDisplayName, request.Enabled, new ProjectPosition(request.PositionX, request.PositionY), parameters, ports, request.RawState, definition.SystemOwned, definition.UserAddable), commandId, request.RequestedDocumentRevision); break;
					case ApplicationGraphEditKind.DeleteNode:
						if (!NodeInstanceId.TryParse(request.TargetId, out var deleteId)) throw new ArgumentException("Node ID is invalid.");
						command = new DeleteNodeEditCommand(deleteId, commandId, request.RequestedDocumentRevision); break;
					case ApplicationGraphEditKind.Disconnect:
						command = new DisconnectEditCommand(new ConnectionId(request.TargetId), commandId, request.RequestedDocumentRevision); break;
					case ApplicationGraphEditKind.SetEnabled:
						if (!NodeInstanceId.TryParse(request.TargetId, out var enabledId)) throw new ArgumentException("Node ID is invalid.");
						command = new SetNodeEnabledEditCommand(enabledId, request.Enabled, commandId, request.RequestedDocumentRevision); break;
					case ApplicationGraphEditKind.Connect:
					case ApplicationGraphEditKind.ReplaceInputConnection:
						if (!NodeInstanceId.TryParse(request.SourceId, out var sourceId) || !NodeInstanceId.TryParse(request.DestinationId, out var destinationId)) throw new ArgumentException("Connection node IDs are invalid.");
						var connection = new ConnectionRecord(new ConnectionId(request.TargetId), sourceId, new PortId(request.SourcePortId), destinationId, new PortId(request.DestinationPortId), request.ConversionId);
						command = request.Kind == ApplicationGraphEditKind.Connect ? (GraphEditCommand)new ConnectEditCommand(connection, commandId, request.RequestedDocumentRevision) : new ReplaceInputConnectionEditCommand(connection, commandId, request.RequestedDocumentRevision); break;
					case ApplicationGraphEditKind.Undo:
						if (_projectCommands == null) return Complete(applicationRequest, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
						var undo = _projectCommands.Undo(); if (undo.IsSuccess) SynchronizeRuntime();
						return Complete(applicationRequest, undo.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, undo.IsSuccess ? null : undo.Error, _state);
					case ApplicationGraphEditKind.Redo:
						if (_projectCommands == null) return Complete(applicationRequest, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
						var redo = _projectCommands.Redo(); if (redo.IsSuccess) SynchronizeRuntime();
						return Complete(applicationRequest, redo.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, redo.IsSuccess ? null : redo.Error, _state);
					case ApplicationGraphEditKind.CopySelection:
					case ApplicationGraphEditKind.PasteSelection:
					case ApplicationGraphEditKind.DuplicateSelection:
					case ApplicationGraphEditKind.FocusSelection:
					case ApplicationGraphEditKind.FocusAll:
						// Selection/focus/clipboard are session state owned by
						// Presentation. Acknowledging them through the public
						// Application port keeps the boundary observable while
						// correctly leaving Project revision/dirty unchanged.
						return Complete(applicationRequest, ApplicationCommandStatus.Applied, null, _state);
					default: return Complete(applicationRequest, ApplicationCommandStatus.Rejected, Failure("application.graph.unsupported", "This graph request requires a node or connection payload."), _state);
				}
			}
			catch (Exception exception) { return Complete(applicationRequest, ApplicationCommandStatus.Rejected, new Diagnostic(new DiagnosticCode("application.graph.invalid"), Severity.Error, exception.Message, module: "application"), _state); }
			if (_frames == null) return Complete(applicationRequest, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var queued = _frames.EnqueueGraphEdit(command);
			if (queued.IsFailure) return Complete(applicationRequest, ApplicationCommandStatus.Rejected, queued.Error, _state);
			TrackGraph(applicationRequest, command.CommandRequestId);
			return KeepAccepted(applicationRequest);
		}

		public ApplicationCommandResult ClearKeyboardMapping(string logicalControlId) {
			if (!LogicalControlId.TryParse(logicalControlId, out var id)) return Rejected(Guid.Empty, Failure("application.input.control_invalid", "Logical control ID is invalid."));
			return ClearKeyboardMapping(id);
		}

		private ApplicationCommandResult QueuePauseResume() {
			var request = BeginRequest(Guid.Empty);
			if (_frames == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var command = _frames.Clock.IsPaused ? RuntimeCommand.ResumeClock(request.ToString("D")) : RuntimeCommand.PauseClock(request.ToString("D"));
			var queued = _frames.EnqueueRuntimeCommand(command);
			if (queued.IsFailure) return Complete(request, ApplicationCommandStatus.Rejected, queued.Error, _state);
			TrackRuntime(request, command.CommandRequestId);
			return KeepAccepted(request);
		}

		public ApplicationCommandResult CloseProject(UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel) {
			if (_document != null && !CanReplaceCurrent(decision, out var guard)) return guard;
			var request = BeginRequest(Guid.Empty);
			_state = ApplicationProjectState.Closing;
			var oldSession = _sessionId;
			CancelAcceptedForSession(oldSession, request);
			DisposeRuntime();
			_document = null;
			_projectCommands = null;
			_root = null;
			_recovered = false;
			_learningControl = null;
			CancelAllMediaDeletions();
			_sessionId = Guid.NewGuid();
			_publishedReadModel = null; _previousProjects.Clear(); _previousGraphNodes.Clear(); _previousGraphConnections.Clear(); _previousParameters.Clear(); _previousDiagnostics.Clear(); ResetSessionShellAndWorkspaceCaches(); _nextSnapshotFull = true;
			_state = ApplicationProjectState.Empty;
			return Complete(request, ApplicationCommandStatus.Applied, null, ApplicationProjectState.Empty);
		}

		public ApplicationCommandResult Exit(UnsavedChangesDecision decision = UnsavedChangesDecision.Cancel) {
			if (_document != null && !CanReplaceCurrent(decision, out var guard)) return guard;
			var request = BeginRequest(Guid.Empty);
			var oldSession = _sessionId;
			CancelAcceptedForSession(oldSession, request);
			DisposeRuntime();
			_document = null;
			_projectCommands = null;
			_root = null;
			_learningControl = null;
			CancelAllMediaDeletions();
			_sessionId = Guid.NewGuid();
			_publishedReadModel = null; _previousProjects.Clear(); _previousGraphNodes.Clear(); _previousGraphConnections.Clear(); _previousParameters.Clear(); _previousDiagnostics.Clear(); ResetSessionShellAndWorkspaceCaches(); _nextSnapshotFull = true;
			_state = ApplicationProjectState.Exited;
			return Complete(request, ApplicationCommandStatus.Applied, null, ApplicationProjectState.Exited);
		}

		public ApplicationCommandResult ApplyPreset(PresetId presetId) {
			var request = BeginRequest(Guid.Empty);
			if (_frames == null || _document == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var sequence = ++_sequence;
			var result = _frames.EnqueueParameterEvent(RuntimeParameterEvent.Preset(sequence, presetId));
			if (result.IsFailure) return Complete(request, ApplicationCommandStatus.Rejected, result.Error, _state);
			TrackParameter(request, sequence);
			return KeepAccepted(request);
		}

		public ApplicationCommandResult EnqueueBaseValue(BaseValueUpdate update, Guid? interactionId = null) {
			var request = BeginRequest(interactionId ?? Guid.Empty);
			if (_frames == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var sequence = ++_sequence;
			var result = _frames.EnqueueParameterEvent(RuntimeParameterEvent.BaseValue(sequence, update.NodeId, update.ParameterId, update.Value));
			if (result.IsSuccess) TrackParameter(request, sequence);
			return result.IsSuccess ? KeepAccepted(request) : Complete(request, ApplicationCommandStatus.Rejected, result.Error, _state);
		}

		public ApplicationCommandResult EnqueueGraphEdit(GraphEditCommand command, Guid? interactionId = null) {
			var request = BeginRequest(interactionId ?? Guid.Empty);
			if (_frames == null || command == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.graph.invalid", "A graph edit and current project are required."), _state);
			var result = _frames.EnqueueGraphEdit(command);
			if (result.IsSuccess) TrackGraph(request, command.CommandRequestId);
			return result.IsSuccess ? KeepAccepted(request) : Complete(request, ApplicationCommandStatus.Rejected, result.Error, _state);
		}

		public ApplicationCommandResult SetLogicalControlMappings(LogicalControlId id, IEnumerable<ControlMappingRecord> mappings, Guid? interactionId = null) {
			var request = BeginRequest(interactionId ?? Guid.Empty);
			if (_projectCommands == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var result = _projectCommands.SetLogicalControlMappings(id, mappings);
			if (result.IsSuccess) SynchronizeRuntime();
			return Complete(request, result.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, result.IsSuccess ? null : result.Error, _state);
		}

		public ApplicationCommandResult AddLogicalControl(LogicalControlRecord control, Guid? interactionId = null) {
			var request = BeginRequest(interactionId ?? Guid.Empty);
			if (_projectCommands == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var result = _projectCommands.AddLogicalControl(control);
			if (result.IsSuccess) SynchronizeRuntime();
			return Complete(request, result.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, result.IsSuccess ? null : result.Error, _state);
		}

		public ApplicationCommandResult DeleteMediaAsset(MediaAssetId id, Guid? interactionId = null) {
			var request = BeginRequest(interactionId ?? Guid.Empty);
			if (_projectCommands == null || _document == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var tracked = _mediaDeletions.RequestDeletion(_document, id, _root, _fileSystem);
			if (tracked.IsFailure) return Complete(request, ApplicationCommandStatus.Rejected, tracked.Error, _state);
			var result = _projectCommands.DeleteMediaAsset(id);
			if (result.IsFailure) {
				_mediaDeletions.Cancel(id);
				return Complete(request, ApplicationCommandStatus.Rejected, result.Error, _state);
			}
			SynchronizeRuntime();
			return Complete(request, ApplicationCommandStatus.Applied, null, _state);
		}

		public ApplicationCommandResult AddMediaAsset(MediaAssetRecord asset, Guid? interactionId = null) {
			var request = BeginRequest(interactionId ?? Guid.Empty);
			if (_projectCommands == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var result = _projectCommands.AddMediaAsset(asset);
			if (result.IsSuccess) SynchronizeRuntime();
			return Complete(request, result.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, result.IsSuccess ? null : result.Error, _state);
		}

		public ApplicationCommandResult Undo(Guid? interactionId = null) {
			var request = BeginRequest(interactionId ?? Guid.Empty);
			if (_projectCommands == null || _document == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var result = _projectCommands.Undo();
			if (result.IsSuccess) {
				foreach (var pending in _mediaDeletions.Pending.ToList()) _mediaDeletions.OnUndo(_document, pending.AssetId);
				SynchronizeRuntime();
			}
			return Complete(request, result.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, result.IsSuccess ? null : result.Error, _state);
		}

		ApplicationCommandResult IApplicationCommandPort.Undo() => Undo((Guid?)null);

		public ApplicationCommandResult Redo(Guid? interactionId = null) {
			var request = BeginRequest(interactionId ?? Guid.Empty);
			if (_projectCommands == null || _document == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var result = _projectCommands.Redo();
			if (result.IsSuccess) SynchronizeRuntime();
			return Complete(request, result.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, result.IsSuccess ? null : result.Error, _state);
		}

		ApplicationCommandResult IApplicationCommandPort.Redo() => Redo((Guid?)null);

		public ApplicationCommandResult SetPresetTriggerBinding(LogicalControlId id, PresetId? presetId, Guid? interactionId = null) {
			var request = BeginRequest(interactionId ?? Guid.Empty);
			if (_projectCommands == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			var result = _projectCommands.SetPresetTriggerBinding(id, presetId);
			if (result.IsSuccess) SynchronizeRuntime();
			return Complete(request, result.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, result.IsSuccess ? null : result.Error, _state);
		}

		public ApplicationCommandResult BeginKeyboardLearn(LogicalControlId id, Guid? interactionId = null) {
			var request = BeginRequest(interactionId ?? Guid.Empty);
			var control = _document?.FindLogicalControl(id);
			if (control == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.input.control_missing", "Logical control does not exist."), _state);
			_learningControl = id;
			return Complete(request, ApplicationCommandStatus.Applied, null, _state);
		}

		public ApplicationCommandResult CancelKeyboardLearn(Guid? interactionId = null) {
			var request = BeginRequest(interactionId ?? Guid.Empty);
			_learningControl = null;
			return Complete(request, ApplicationCommandStatus.Cancelled, null, _state);
		}

		public ApplicationCommandResult ClearKeyboardMapping(LogicalControlId id, Guid? interactionId = null) => SetLogicalControlMappings(id, Enumerable.Empty<ControlMappingRecord>(), interactionId);

		/// <summary>Consumes a key at the Application boundary. Learn mode is
		/// exclusive: a captured key never also drives a logical control.</summary>
		public ApplicationCommandResult HandleKeyboard(PhysicalKey key, bool pressed) {
			if (_learningControl.HasValue) {
				if (string.Equals(key.PhysicalId, "escape", StringComparison.OrdinalIgnoreCase) || string.Equals(key.PhysicalId, "esc", StringComparison.OrdinalIgnoreCase)) return CancelKeyboardLearn();
				if (key.IsModifierOnly || !pressed) return ApplicationCommandResult.Ignored(_sessionId);
				if (TryGetInstantEffectTrigger(key, out _))
					return Complete(BeginRequest(Guid.Empty), ApplicationCommandStatus.Rejected, Failure("application.input.key_reserved", "QWERTYUIOP are reserved for global instant effect triggers."), _state);
				var mapping = new ControlMappingRecord(PhysicalInputKind.Keyboard, key.PhysicalId, key.ControlPath);
				var learned = SetLogicalControlMappings(_learningControl.Value, new[] { mapping });
				if (learned.IsSuccess) _learningControl = null;
				return learned;
			}

			var request = BeginRequest(Guid.Empty);
			if (_frames == null || _document == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			if (TryGetInstantEffectTrigger(key, out var instantEffectTrigger)) {
				if (!pressed) return Complete(request, ApplicationCommandStatus.Applied, null, _state);
				var queued = _frames.EnqueueInstantEffectTrigger(instantEffectTrigger);
				return queued.IsFailure
					? Complete(request, ApplicationCommandStatus.Rejected, queued.Error, _state)
					: Complete(request, ApplicationCommandStatus.Applied, null, _state);
			}
			var matched = _document.LogicalControls.SelectMany(control => control.Mappings
				.Where(mapping => mapping.Kind == PhysicalInputKind.Keyboard && (string.Equals(mapping.PhysicalId, key.PhysicalId, StringComparison.Ordinal) || string.Equals(mapping.ControlPath, key.ControlPath, StringComparison.Ordinal)))
				.Select(mapping => new { Control = control, Mapping = mapping })).ToList();
			foreach (var item in matched) {
				var value = item.Mapping.Normalize(pressed ? 1f : 0f);
				var sequence = ++_sequence;
				var queued = _frames.EnqueueParameterEvent(RuntimeParameterEvent.ControlValue(sequence, item.Control.Id, value));
				if (queued.IsFailure) return Complete(request, ApplicationCommandStatus.Rejected, queued.Error, _state);
				TrackParameter(request, sequence);
			}
			// Keyboard polling can deliver several 120 Hz updates before the
			// next application frame.  Those requests are queued together and
			// become terminal in Tick, so publishing a full snapshot for every
			// accepted input only rebuilds the same UI state repeatedly.  Keep
			// the generic KeepAccepted path for command surfaces that expose a
			// pending state; the high-rate physical-input path publishes its
			// correlated terminal results at the frame boundary instead.
			return matched.Count == 0 ? Complete(request, ApplicationCommandStatus.Applied, null, _state) : AcceptedWithoutPublication(request);
		}

		private static bool TryGetInstantEffectTrigger(PhysicalKey key, out int triggerNumber) {
			var index = Array.FindIndex(m_InstantEffectPhysicalKeys, candidate => string.Equals(candidate, key.PhysicalId, StringComparison.OrdinalIgnoreCase));
			triggerNumber = index + 1;
			return index >= 0;
		}

		/// <summary>Consumes a decoded channel-voice MIDI event. Unmapped MIDI
		/// traffic is ignored so clock-rate controllers cannot create UI work.</summary>
		public ApplicationCommandResult HandleMidi(MidiInputEvent inputEvent) {
			var control = inputEvent.Control;
			if (_learningControl.HasValue) {
				var mapping = new ControlMappingRecord(PhysicalInputKind.Midi, control.PhysicalId, control.ControlPath, control.RawMinimum, control.RawMaximum);
				var learned = SetLogicalControlMappings(_learningControl.Value, new[] { mapping });
				if (learned.IsSuccess) _learningControl = null;
				return learned;
			}

			if (_frames == null || _document == null) return ApplicationCommandResult.Ignored(_sessionId);
			var matched = _document.LogicalControls.SelectMany(logicalControl => logicalControl.Mappings
				.Where(mapping => mapping.Kind == PhysicalInputKind.Midi && (string.Equals(mapping.PhysicalId, control.PhysicalId, StringComparison.Ordinal) || string.Equals(mapping.ControlPath, control.ControlPath, StringComparison.Ordinal)))
				.Select(mapping => new { Control = logicalControl, Mapping = mapping })).ToList();
			if (matched.Count == 0) return ApplicationCommandResult.Ignored(_sessionId);

			var request = BeginRequest(Guid.Empty);
			foreach (var item in matched) {
				var value = item.Mapping.Normalize(inputEvent.RawValue);
				var sequence = ++_sequence;
				var queued = _frames.EnqueueParameterEvent(RuntimeParameterEvent.ControlValue(sequence, item.Control.Id, value));
				if (queued.IsFailure) return Complete(request, ApplicationCommandStatus.Rejected, queued.Error, _state);
				TrackParameter(request, sequence);
			}
			return AcceptedWithoutPublication(request);
		}

		public ApplicationCommandResult SetLiveControlValue(LogicalControlId id, float normalizedValue) {
			var request = BeginRequest(Guid.Empty);
			if (_frames == null || _document == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.project.missing", "There is no current project."), _state);
			if (_document.FindLogicalControl(id) == null) return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.input.control_missing", "Live Control does not exist."), _state);
			if (float.IsNaN(normalizedValue) || float.IsInfinity(normalizedValue) || normalizedValue < 0f || normalizedValue > 1f)
				return Complete(request, ApplicationCommandStatus.Rejected, Failure("application.input.control_value_invalid", "Live Control values must be between 0 and 1."), _state);
			var sequence = ++_sequence;
			var queued = _frames.EnqueueParameterEvent(RuntimeParameterEvent.ControlValue(sequence, id, normalizedValue));
			if (queued.IsFailure) return Complete(request, ApplicationCommandStatus.Rejected, queued.Error, _state);
			TrackParameter(request, sequence);
			return AcceptedWithoutPublication(request);
		}

		private static ControlMappingRecord ToControlMapping(ApplicationControlMappingRequest mapping) =>
			new ControlMappingRecord(mapping.Kind == ApplicationPhysicalInputKind.Midi ? PhysicalInputKind.Midi : PhysicalInputKind.Keyboard,
				mapping.PhysicalId, mapping.ControlPath, mapping.RawMin, mapping.RawMax, mapping.Invert);

		public ApplicationFrameResult Tick(double monotonicTime = double.NaN) {
			if (_frames == null) return null;
			var report = _frames.Tick(monotonicTime);
			_lastPresentation = report.Presentation;
			var frameResults = new List<ApplicationFrameCommandResult>();
			foreach (var item in report.GraphCommandExecutionResults) {
				var entry = _graphRequests.TryGetValue(item.CommandRequestId ?? string.Empty, out var requestId) && _ledger.TryGetValue(requestId, out var found)
					? found : null;
				var diagnostic = item.Result.IsFailure ? item.Result.Error : null;
				if (entry != null) TryCompleteQueued(entry, item.Result.IsSuccess, diagnostic, item.CommandRequestId);
				frameResults.Add(new ApplicationFrameCommandResult(item.CommandRequestId, item.Result.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, diagnostic));
			}
			foreach (var item in report.ParameterEventResults) {
				var entry = _parameterRequests.TryGetValue(item.SequenceNumber, out var requestId) && _ledger.TryGetValue(requestId, out var found)
					? found : null;
				if (entry == null) continue;
				if (item.Status == ParameterEventStatus.Superseded) MarkCommand(entry.RequestId, ApplicationCommandStatus.Superseded, null);
				else if (!item.Applied) MarkCommand(entry.RequestId, ApplicationCommandStatus.Rejected, item.Diagnostic);
				else TryCompleteQueued(entry, true, null, item.SequenceNumber);
				frameResults.Add(new ApplicationFrameCommandResult(entry.RequestId.ToString("D"), item.Status == ParameterEventStatus.Superseded ? ApplicationCommandStatus.Superseded : item.Applied ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, item.Diagnostic));
			}
			foreach (var item in report.RuntimeCommandResults) {
				var entry = _runtimeRequests.TryGetValue(item.CommandRequestId ?? string.Empty, out var requestId) && _ledger.TryGetValue(requestId, out var found)
					? found : null;
				if (entry != null) TryCompleteQueued(entry, item.Applied, item.Diagnostic, null);
				frameResults.Add(new ApplicationFrameCommandResult(item.CommandRequestId, item.Applied ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, item.Diagnostic));
			}
			ProcessMediaImportBatch();
			// Project installation publishes a frame-zero full snapshot. The
			// first frame boundary must carry that same full-snapshot marker
			// so a host that begins consuming at its first Tick cannot miss
			// the required baseline. Subsequent frames are deltas again.
			var firstFrameAfterInstall = _publishedReadModel != null
				&& _publishedReadModel.Project != null
				&& _publishedReadModel.Project.IsFullSnapshot
				&& _publishedReadModel.Project.FrameNumber == 0UL;
			PublishReadModel(_nextSnapshotFull || firstFrameAfterInstall);
			_nextSnapshotFull = false;
			return new ApplicationFrameResult(report.FrameNumber, report.Succeeded, frameResults);
		}

		/// <summary>Records the measured timing of the frame just presented
		/// by the Unity host. This is deliberately separate from Tick so the
		/// Runtime never reads Unity timing APIs. The next application frame
		/// publishes it with all other frame-boundary changes, avoiding a
		/// second complete Read Model projection in the same host frame.</summary>
		public void ObserveFrameTiming(RuntimeFrameTimingSample sample) {
			if (_runtime == null) return;
			_runtime.ObserveFrameTiming(sample);
		}

		public UnitResult<Diagnostic> ExportDiagnostics(string path) {
			if (string.IsNullOrWhiteSpace(path)) return UnitResult.Failure<Diagnostic>(Failure("application.diagnostics.path", "Diagnostic export path is required."));
			try {
				var directory = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(directory)) _fileSystem.EnsureDirectory(directory);
				var lines = new List<string> { "ShitDesigner diagnostics", "projectSessionId=" + _sessionId.ToString("D"), "projectRoot=" + (_root ?? string.Empty) };
				foreach (var diagnostic in (_runtime == null ? Enumerable.Empty<Diagnostic>() : _runtime.Diagnostics.History))
					lines.Add(diagnostic.Code.Value + "\t" + diagnostic.Severity + "\t" + diagnostic.Message.Replace("\r", " ").Replace("\n", " "));
				_fileSystem.WriteAllBytes(path, new UTF8Encoding(false, true).GetBytes(string.Join("\n", lines)));
				return UnitResult.Success<Diagnostic>();
			}
			catch (Exception exception) {
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("application.diagnostics.export_failed"), Severity.Error, "Diagnostic export failed.", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		public UnitResult<Diagnostic> ExportDiagnosticsJson(string path) {
			if (string.IsNullOrWhiteSpace(path)) return UnitResult.Failure<Diagnostic>(Failure("application.diagnostics.path", "Diagnostic export path is required."));
			try {
				var directory = Path.GetDirectoryName(path);
				if (!string.IsNullOrEmpty(directory)) _fileSystem.EnsureDirectory(directory);
				var builder = new StringBuilder();
				builder.Append("{\"projectSessionId\":\"").Append(JsonEscape(_sessionId.ToString("D"))).Append("\",\"history\":[");
				var entries = _runtime == null ? Enumerable.Empty<DiagnosticHistoryEntry>() : _runtime.Diagnostics.HistoryEntries.OrderBy(x => x.EntryId);
				var first = true;
				foreach (var entry in entries) {
					if (!first) builder.Append(','); first = false;
					var diagnostic = entry.Diagnostic;
					builder.Append("{\"entryId\":").Append(entry.EntryId).Append(",\"code\":\"").Append(JsonEscape(diagnostic.Code.Value)).Append("\",\"severity\":\"").Append(diagnostic.Severity).Append("\",\"message\":\"").Append(JsonEscape(diagnostic.Message)).Append("\",\"count\":").Append(entry.Count).Append(",\"firstFrame\":").Append(entry.FirstFrame).Append(",\"lastFrame\":").Append(entry.LastFrame).Append('}');
				}
				builder.Append("]}");
				_fileSystem.WriteAllBytes(path, new UTF8Encoding(false, true).GetBytes(builder.ToString()));
				return UnitResult.Success<Diagnostic>();
			}
			catch (Exception exception) {
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("application.diagnostics.export_failed"), Severity.Error, "Diagnostic JSON export failed.", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		private static string JsonEscape(string value) {
			var builder = new StringBuilder();
			foreach (var character in value ?? string.Empty) {
				switch (character) {
					case '\\': builder.Append("\\\\"); break;
					case '"': builder.Append("\\\""); break;
					case '\r': builder.Append("\\r"); break;
					case '\n': builder.Append("\\n"); break;
					case '\t': builder.Append("\\t"); break;
					default: builder.Append(character); break;
				}
			}
			return builder.ToString();
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			CancelMediaImportBatch();
			DisposeRuntime();
			_state = ApplicationProjectState.Exited;
		}

		private bool CanReplaceCurrent(UnsavedChangesDecision decision, out ApplicationCommandResult rejected) {
			rejected = null;
			if (_document == null || !_document.IsDirty) return true;
			if (decision == UnsavedChangesDecision.Cancel) {
				rejected = Rejected(Guid.Empty, Failure("application.project.dirty_cancelled", "The current project has unsaved changes."));
				return false;
			}
			if (decision == UnsavedChangesDecision.Save) {
				var saved = SaveProject();
				if (!saved.IsSuccess) {
					rejected = saved;
					return false;
				}
			}
			return true;
		}

		private void Install(ProjectDocument document, string root, bool recovered, Guid switchRequestId) {
			var oldSession = _sessionId;
			CancelAcceptedForSession(oldSession, switchRequestId);
			CancelMediaImportBatch();
			DisposeRuntime();
			CancelAllMediaDeletions();
			_document = document;
			_projectCommands = new ProjectCommandProcessor(document);
			_root = root;
			_recovered = recovered;
			_sessionId = Guid.NewGuid();
			_learningControl = null;
			Result<ApplicationRuntimeComposition, Diagnostic> composition;
			try {
				if (_runtimeFactory is IProjectRootAwareRuntimeSessionFactory rootAware) rootAware.SetProjectRoot(root);
				composition = _runtimeFactory.Create(document, _registry);
			}
			catch (Exception exception) {
				composition = Result.Failure<ApplicationRuntimeComposition, Diagnostic>(new Diagnostic(new DiagnosticCode("application.runtime.composition_failed"), Severity.Error, "Runtime composition could not be created.", module: "application", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
			if (composition.IsSuccess && composition.Value != null) {
				_runtimeComposition = composition.Value;
				_runtime = _runtimeComposition.Session;
				_frames = _runtimeComposition.Frames;
				_runtimeAvailable = _runtimeComposition.RuntimeAvailable;
				_runtimeUnavailableReason = _runtimeComposition.UnavailableReason;
			}
			else {
				var fallback = new MinimalApplicationRuntimeSessionFactory().Create(document, _registry);
				_runtimeComposition = fallback.Value;
				_runtime = _runtimeComposition.Session;
				_frames = _runtimeComposition.Frames;
				_runtimeAvailable = false;
				_runtimeUnavailableReason = composition.Error == null ? "Runtime composition could not be created." : composition.Error.Message;
				_runtime.Diagnostics.Report(composition.Error ?? Failure("application.runtime.composition_failed", "Runtime composition could not be created."));
			}
			_publishedReadModel = null;
			_previousProjects.Clear(); _previousGraphNodes.Clear(); _previousGraphConnections.Clear(); _previousParameters.Clear(); _previousDiagnostics.Clear();
			ResetSessionShellAndWorkspaceCaches();
			_nextSnapshotFull = true;
			_previewDemands.Clear();
			_previewHostVisible = true;
			_programWasHolding = false;
			_programHoldingStartClock = double.NaN;
			_programHoldingCauseNodeId = string.Empty;
			_programHoldingDiagnosticCode = string.Empty;
			HydratePreviewSettings();
			foreach (var previewId in (_document?.Ui?.PreviewNodeIds ?? Enumerable.Empty<string>()).Take(8))
				EnsureDefaultPreviewDemand(previewId);
			QueueVisiblePreviewDemands();
		}

		private void SynchronizeRuntime() {
			if (_runtime != null) _runtime.Parameters.Synchronize(_runtime.GraphEditor.State, _document);
		}

		private void DisposeRuntime() {
			if (_runtimeComposition != null) _runtimeComposition.Dispose();
			else if (_runtime != null) _runtime.Dispose();
			_runtimeComposition = null;
			_runtime = null;
			_frames = null;
			_runtimeAvailable = false;
		}

		private void CancelAllMediaDeletions() {
			foreach (var pending in _mediaDeletions.Pending.ToList()) _mediaDeletions.Cancel(pending.AssetId);
			foreach (var orphan in _mediaDeletions.Orphans.ToList()) _mediaDeletions.Cancel(orphan.AssetId);
		}

		private void AddRecent(string root) {
			if (string.IsNullOrWhiteSpace(root)) return;
			var full = root;
			try { full = _fileSystem.GetFullPath(root); } catch { }
			_recent.RemoveAll(x => string.Equals(x, full, StringComparison.OrdinalIgnoreCase));
			_recent.Insert(0, full);
			if (_recent.Count > 10) _recent.RemoveAt(_recent.Count - 1);
			_recentRevision++;
			try { _recentProjectStore?.WriteRecentProjectRoots(_recent); } catch { }
		}

		private IReadOnlyList<string> GetRecentProjection() {
			if (_cachedRecentProjection != null && _cachedRecentRevision == _recentRevision)
				return _cachedRecentProjection;
			_cachedRecentRevision = _recentRevision;
			_cachedRecentProjection = new ReadOnlyCollection<string>(new List<string>(_recent));
			return _cachedRecentProjection;
		}

		private static IEnumerable<string> NormalizeRecent(IEnumerable<string> roots) {
			var normalized = new List<string>();
			foreach (var root in roots ?? Enumerable.Empty<string>()) {
				if (string.IsNullOrWhiteSpace(root)) continue;
				var value = root.Trim();
				if (normalized.Any(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase))) continue;
				normalized.Add(value);
				if (normalized.Count == 10) break;
			}
			return normalized;
		}

		private void BeginTask(string kind, string path, string stage, string status, int completedItems = 0, int totalItems = 0, string currentItem = null) {
			_task = new ApplicationTaskReadModel(Guid.NewGuid(), kind, stage, status, path, null, completedItems, totalItems, currentItem);
		}

		private void SetTaskProgress(string stage, int completed, int total, string current) {
			if (_task == null) return;
			_task = new ApplicationTaskReadModel(_task.TaskId, _task.Kind, stage, "Running", _task.Path, null, completed, total, current);
		}

		private void SetTaskWaiting(string stage, int completed, int total, string current, Diagnostic diagnostic) {
			if (_task == null) return;
			_task = new ApplicationTaskReadModel(_task.TaskId, _task.Kind, stage, "Waiting", _task.Path, diagnostic, completed, total, current);
		}

		private void ProcessMediaImportBatch() {
			var operation = _mediaBatch;
			if (operation == null) return;
			if (operation.SessionId != _sessionId) {
				CancelMediaImportBatch();
				return;
			}
			if (operation.Transaction == null) {
				if (operation.Index >= operation.Requests.Count) {
					SetTaskProgress("Register", operation.Imported.Count, operation.Requests.Count, null);
					var command = _projectCommands == null ? UnitResult.Failure<Diagnostic>(Failure("application.project.missing", "There is no current project.")) : _projectCommands.AddMediaAssets(operation.Imported);
					if (command.IsFailure) {
						FailMediaImportBatch(operation, command.Error);
						return;
					}
					SynchronizeRuntime();
					_mediaBatch = null;
					SetTask("Completed", null);
					Complete(operation.RequestId, ApplicationCommandStatus.Applied, null, _state);
					return;
				}
				var item = operation.Requests[operation.Index];
				if (!Enum.TryParse(item.Kind, true, out MediaAssetKind kind) || !Enum.TryParse(item.ColorSpace, true, out MediaColorSpace colorSpace) || !Enum.TryParse(item.AlphaMode, true, out MediaAlphaMode alphaMode)) {
					FailMediaImportBatch(operation, Failure("application.media.import_invalid", "Media import metadata is invalid."));
					return;
				}
				try {
					operation.Transaction = new MediaAssetImportTransaction(item.SourcePath, _root, _fileSystem, item.DisplayName, kind, colorSpace, alphaMode, _mediaProbe);
				}
				catch (Exception exception) {
					FailMediaImportBatch(operation, new Diagnostic(new DiagnosticCode("application.media.import_failed"), Severity.Error, "Media import transaction could not start.", module: "application", exception: DiagnosticExceptionInfo.FromException(exception)));
					return;
				}
			}

			var progress = operation.Transaction.Step();
			var current = operation.Requests[operation.Index].DisplayName;
			switch (progress.Stage) {
				case MediaAssetImportTransactionStage.Copy:
					SetTaskProgress("Copy", operation.Index, operation.Requests.Count, current); break;
				case MediaAssetImportTransactionStage.Verify:
					SetTaskProgress("SizeHash", operation.Index, operation.Requests.Count, current); break;
				case MediaAssetImportTransactionStage.Probe:
					SetTaskProgress("Probe", operation.Index, operation.Requests.Count, current); break;
				case MediaAssetImportTransactionStage.AwaitingProbeConfirmation:
					SetTaskWaiting("ProbeConfirmation", operation.Index, operation.Requests.Count, current, progress.Diagnostic); break;
				case MediaAssetImportTransactionStage.Completed:
					operation.Imported.Add(progress.Asset);
					operation.Transaction = null;
					operation.Index++;
					SetTaskProgress(operation.Index >= operation.Requests.Count ? "Register" : "Queued", operation.Index, operation.Requests.Count, operation.Index >= operation.Requests.Count ? null : operation.Requests[operation.Index].DisplayName);
					break;
				case MediaAssetImportTransactionStage.Failed:
				case MediaAssetImportTransactionStage.Cancelled:
					FailMediaImportBatch(operation, progress.Diagnostic ?? Failure("application.media.import_failed", "Media import transaction failed."));
					return;
				case MediaAssetImportTransactionStage.Rename:
					SetTaskProgress("Rename", operation.Index, operation.Requests.Count, current); break;
			}
			PublishReadModel(false);
		}

		private void FailMediaImportBatch(MediaImportBatchOperation operation, Diagnostic diagnostic) {
			if (operation == null) return;
			try { operation.Transaction?.Cancel(); } catch { }
			CleanupImportedFiles(operation.Imported);
			_mediaBatch = null;
			SetTask("Failed", diagnostic);
			Complete(operation.RequestId, ApplicationCommandStatus.Rejected, diagnostic, _state);
		}

		private void CancelMediaImportBatch() {
			var operation = _mediaBatch;
			if (operation == null) return;
			try { operation.Transaction?.Cancel(); } catch { }
			CleanupImportedFiles(operation.Imported);
			_mediaBatch = null;
			SetTask("Failed", Failure("application.session.cancelled", "The media import was cancelled because the project session changed."));
			if (_ledger.ContainsKey(operation.RequestId)) MarkCommand(operation.RequestId, ApplicationCommandStatus.Cancelled, Failure("application.session.cancelled", "The media import was cancelled because the project session changed."));
		}

		private void CleanupImportedFiles(IEnumerable<MediaAssetRecord> assets) {
			foreach (var asset in assets ?? Enumerable.Empty<MediaAssetRecord>()) {
				var path = Path.Combine(_root, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar));
				try { _fileSystem.Delete(path); } catch { }
				try { (_fileSystem as IProjectDirectoryCleanup)?.DeleteDirectory(Path.GetDirectoryName(path)); } catch { }
			}
		}

		private UnitResult<Diagnostic> QueueVisiblePreviewDemands() {
			if (_runtime == null) return UnitResult.Failure<Diagnostic>(Failure("application.preview.runtime_missing", "Preview demands require a current runtime session."));
			if (!_previewHostVisible) return _runtime.SetOutputDemands(Enumerable.Empty<OutputDemand>());
			var demands = new List<OutputDemand>();
			foreach (var previewId in (_document?.Ui?.PreviewNodeIds ?? Enumerable.Empty<string>()).Take(8)) {
				if (!_previewDemands.TryGetValue(previewId, out var request)) continue;
				if (!NodeInstanceId.TryParse(previewId, out var node)) continue;
				if (!PortId.TryParse(request.PortId, out var portId)) continue;
				demands.Add(new OutputDemand(OutputTargetKind.Preview, node, portId, request.Width, request.Height, request.Focused));
			}
			return _runtime.SetOutputDemands(demands);
		}

		private void EnsureDefaultPreviewDemand(string previewId) {
			if (string.IsNullOrWhiteSpace(previewId)) return;
			if (!_previewDemands.ContainsKey(previewId))
				_previewDemands[previewId] = new ApplicationOutputDemandRequest(previewId, "image", 640, 360, false);
		}

		private void FocusPreviewDemand(string previewId) {
			if (string.IsNullOrWhiteSpace(previewId)) return;
			foreach (var pair in _previewDemands.Where(x => !string.Equals(x.Key, previewId, StringComparison.Ordinal) && x.Value.Focused).ToList())
				_previewDemands[pair.Key] = new ApplicationOutputDemandRequest(pair.Value.PreviewId, pair.Value.PortId, pair.Value.Width, pair.Value.Height, false);
			if (_previewDemands.TryGetValue(previewId, out var existing))
				_previewDemands[previewId] = new ApplicationOutputDemandRequest(existing.PreviewId, existing.PortId, existing.Width, existing.Height, true);
			else
				_previewDemands[previewId] = new ApplicationOutputDemandRequest(previewId, "image", 640, 360, true);
		}

		private static string MergePreviewState(string original, ApplicationPreviewSettingsRequest request) {
			using (var stream = new MemoryStream()) {
				using (var writer = new Utf8JsonWriter(stream)) {
					writer.WriteStartObject();
					try {
						using (var json = JsonDocument.Parse(string.IsNullOrWhiteSpace(original) ? "{}" : original)) {
							if (json.RootElement.ValueKind == JsonValueKind.Object)
								foreach (var property in json.RootElement.EnumerateObject())
									if (!string.Equals(property.Name, "fitMode", StringComparison.Ordinal) && !string.Equals(property.Name, "backgroundMode", StringComparison.Ordinal) && !string.Equals(property.Name, "holdLastFrame", StringComparison.Ordinal)) {
										writer.WritePropertyName(property.Name); property.Value.WriteTo(writer);
									}
						}
					}
					catch { }
					writer.WriteString("fitMode", request.FitMode.ToString());
					writer.WriteString("backgroundMode", request.BackgroundMode);
					writer.WriteBoolean("holdLastFrame", request.HoldLastFrame);
					writer.WriteEndObject(); writer.Flush();
				}
				return Encoding.UTF8.GetString(stream.ToArray());
			}
		}

		private void HydratePreviewSettings() {
			_previewSettings.Clear();
			if (_document == null) return;
			foreach (var node in _document.Nodes.Where(x => x.TypeId.Value == GraphConstants.PreviewTypeId)) {
				try {
					using (var json = JsonDocument.Parse(node.RawState ?? "{}")) {
						var root = json.RootElement;
						if (!root.TryGetProperty("fitMode", out var fit) || !Enum.TryParse(fit.GetString(), true, out ApplicationOutputFitMode fitMode)) continue;
						var background = root.TryGetProperty("backgroundMode", out var bg) ? bg.GetString() : "Black";
						var hold = !root.TryGetProperty("holdLastFrame", out var holdValue) || holdValue.GetBoolean();
						_previewSettings[node.Id.Value] = new ApplicationPreviewSettingsRequest(node.Id.Value, fitMode, background, "Project", hold);
					}
				}
				catch { }
			}
		}

		private ApplicationCommandResult CompleteImmediate(ApplicationCommandStatus status, Diagnostic diagnostic) {
			var request = BeginRequest(Guid.Empty);
			return Complete(request, status, diagnostic, _state);
		}

		private ApplicationCommandResult CompleteProjectMutation(UnitResult<Diagnostic> result) {
			var request = BeginRequest(Guid.Empty);
			if (result.IsSuccess) SynchronizeRuntime();
			return Complete(request, result.IsSuccess ? ApplicationCommandStatus.Applied : ApplicationCommandStatus.Rejected, result.IsSuccess ? null : result.Error, _state);
		}

		private static LogicalExpressionNode BuildExpression(ApplicationExpressionDraft request) {
			if (request == null) throw new ArgumentNullException(nameof(request));
			switch (request.Kind) {
				case ApplicationExpressionKind.BaseValue: return new BaseValueLeaf();
				case ApplicationExpressionKind.LogicalControl:
					if (!LogicalControlId.TryParse(request.LogicalControlId, out var control)) throw new ArgumentException("Expression control ID is invalid.");
					return new LogicalControlLeaf(control);
				case ApplicationExpressionKind.Min: return new BinaryLogicalExpression(LogicalOperator.Min, BuildExpression(request.Left), BuildExpression(request.Right));
				case ApplicationExpressionKind.Max: return new BinaryLogicalExpression(LogicalOperator.Max, BuildExpression(request.Left), BuildExpression(request.Right));
				default: throw new ArgumentException("Expression kind is invalid.");
			}
		}

		private void SetTask(string stage, Diagnostic diagnostic) {
			if (_task == null) return;
			_task = new ApplicationTaskReadModel(_task.TaskId, _task.Kind, stage, diagnostic == null ? (string.Equals(stage, "Completed", StringComparison.Ordinal) ? "Completed" : "Running") : "Failed", _task.Path, diagnostic, _task.CompletedItems, _task.TotalItems, _task.CurrentItem);
		}

		private Guid BeginRequest(Guid interactionId) {
			var id = Guid.NewGuid();
			var revision = _document == null ? 0 : _document.DocumentRevision;
			_ledger[id] = new CommandLedgerEntry(id, interactionId, _sessionId, revision, CommandQueueKind.Immediate);
			_commands.Add(new PendingCommandReadModel(id, interactionId, _sessionId, ApplicationCommandStatus.Accepted, null));
			_commandRevision++;
			_commandIndices[id] = _commands.Count - 1;
			return id;
		}

		private ApplicationCommandResult KeepAccepted(Guid requestId) {
			var entry = _ledger[requestId];
			PublishReadModel(false);
			return new ApplicationCommandResult(requestId, entry.InteractionId, entry.SessionId, entry.Revision, ApplicationCommandStatus.Accepted);
		}

		private ApplicationCommandResult AcceptedWithoutPublication(Guid requestId) {
			var entry = _ledger[requestId];
			return new ApplicationCommandResult(requestId, entry.InteractionId, entry.SessionId, entry.Revision, ApplicationCommandStatus.Accepted);
		}

		private ApplicationCommandResult Complete(Guid requestId, ApplicationCommandStatus status, Diagnostic diagnostic, ApplicationProjectState state) {
			_state = state;
			MarkCommand(requestId, status, diagnostic);
			var entry = _ledger.TryGetValue(requestId, out var found) ? found : new CommandLedgerEntry(requestId, Guid.Empty, _sessionId, _document == null ? 0 : _document.DocumentRevision, CommandQueueKind.Immediate);
			var full = _nextSnapshotFull;
			PublishReadModel(full);
			_nextSnapshotFull = false;
			return new ApplicationCommandResult(requestId, entry.InteractionId, entry.SessionId, entry.Revision, status, diagnostic);
		}

		private void TrackParameter(Guid requestId, ulong sequence) {
			var entry = _ledger[requestId];
			entry.Kind = CommandQueueKind.Parameter;
			entry.ParameterSequences.Add(sequence);
			_parameterRequests[sequence] = requestId;
			if (entry.InteractionId != Guid.Empty) {
				if (_latestParameterRequestByInteraction.TryGetValue(entry.InteractionId, out var previousRequestId) && previousRequestId != requestId &&
					_commandIndices.TryGetValue(previousRequestId, out var previousIndex) && _commands[previousIndex].Status == ApplicationCommandStatus.Accepted)
					MarkCommand(previousRequestId, ApplicationCommandStatus.Superseded, null);
				_latestParameterRequestByInteraction[entry.InteractionId] = requestId;
			}
		}

		private void TrackGraph(Guid requestId, string graphCommandId) {
			var entry = _ledger[requestId];
			var id = graphCommandId ?? string.Empty;
			entry.Kind = CommandQueueKind.Graph;
			entry.GraphCommandIds.Add(id);
			_graphRequests[id] = requestId;
		}

		private void TrackRuntime(Guid requestId, string runtimeCommandId) {
			var entry = _ledger[requestId];
			var id = runtimeCommandId ?? string.Empty;
			entry.Kind = CommandQueueKind.Runtime;
			entry.RuntimeCommandId = id;
			_runtimeRequests[id] = requestId;
		}

		private void TryCompleteQueued(CommandLedgerEntry entry, bool success, Diagnostic diagnostic, object observedId) {
			if (entry == null) return;
			if (!success) { MarkCommand(entry.RequestId, ApplicationCommandStatus.Rejected, diagnostic); return; }
			if (observedId is ulong) {
				entry.ObservedParameterSequences.Add((ulong)observedId);
				if (entry.ParameterSequences.Any(x => !entry.ObservedParameterSequences.Contains(x))) return;
			}
			else if (observedId is string) {
				entry.ObservedGraphCommandIds.Add((string)observedId);
				if (entry.GraphCommandIds.Any(x => !entry.ObservedGraphCommandIds.Contains(x))) return;
			}
			MarkCommand(entry.RequestId, ApplicationCommandStatus.Applied, null);
		}

		private void MarkCommand(Guid requestId, ApplicationCommandStatus status, Diagnostic diagnostic) {
			if (!_commandIndices.TryGetValue(requestId, out var index)) return;
			var current = _commands[index];
			if (current.Status != ApplicationCommandStatus.Accepted && status != ApplicationCommandStatus.Cancelled) return;
			var entry = _ledger.TryGetValue(requestId, out var found) ? found : null;
			_commands[index] = new PendingCommandReadModel(requestId, current.InteractionId, entry == null ? current.ProjectSessionId : entry.SessionId, status, diagnostic);
			_commandRevision++;
		}

		private void CancelAcceptedForSession(Guid sessionId, Guid exceptRequestId) {
			foreach (var item in _commands.Where(x => x.ProjectSessionId == sessionId && x.Status == ApplicationCommandStatus.Accepted && x.CommandRequestId != exceptRequestId).ToList())
				MarkCommand(item.CommandRequestId, ApplicationCommandStatus.Cancelled, Failure("application.session.cancelled", "The request was cancelled because the project session changed."));
		}

		private ApplicationCommandResult Rejected(Guid interactionId, Diagnostic diagnostic) {
			var request = BeginRequest(interactionId);
			return Complete(request, ApplicationCommandStatus.Rejected, diagnostic, _state);
		}

		private ApplicationReadModel PublishReadModel(bool fullSnapshot) {
			_readVersion++;
			_nextSnapshotFull = fullSnapshot;
			var frame = _frames == null ? 0UL : _frames.FrameNumber;
			var documentRevision = _document == null ? 0 : _document.DocumentRevision;
			var graphRevision = _runtime == null ? 0 : _runtime.GraphEditor.State.Revision;
			var projectModel = GetProjectProjection();
			var project = new ReadModelEnvelope<ProjectReadModel>(_sessionId, _readVersion, frame, documentRevision, graphRevision, fullSnapshot, projectModel);
			var recent = GetRecentProjection();

			var shellModel = GetShellProjection();
			// Dashboard pages own the Workspace-visible panel identities. Keep
			// their frozen projection independent from value-only document
			// mutations before deriving the Workspace slice.
			EnsureDocumentListProjections();
			var workspaceModel = GetWorkspaceProjection();
			var catalog = GetCatalogProjection();
			// RuntimeSession returns defensive copies at its public boundary.
			// Capture each frame's status inputs once, then share the same
			// immutable snapshot across all graph rows and output projection.
			var runtimeOutputResults = _runtime?.OutputResults;
			var runtimeCurrentConditions = _runtime?.Diagnostics?.CurrentConditions;

			// A document revision also changes for parameter values.  GraphState.Revision is the
			// structural stamp, while the loop below compares only runtime status/reason and
			// replaces an individual row when that public state actually changes.
			var graphTopologyChanged = !ReferenceEquals(_cachedGraphDocument, _document) || _cachedGraphTopologyRevision != graphRevision;
			var graphNodeChanged = false;
			if (graphTopologyChanged) {
				_cachedGraphDocument = _document;
				_cachedGraphTopologyRevision = graphRevision;
				_cachedGraphNodeRows.Clear();
				_cachedGraphNodeOrder.Clear();
				if (_document != null) {
					foreach (var node in _document.Nodes) {
						_cachedGraphNodeOrder.Add(node.Id.Value);
						_cachedGraphNodeRows[node.Id.Value] = CreateGraphNodeRow(node, runtimeOutputResults, runtimeCurrentConditions);
					}
				}
				graphNodeChanged = true;
				_cachedGraphNodeProjection = CreateGraphNodeProjection();

				var nextPorts = new List<ApplicationGraphPortReadModel>();
				var nextConnections = new List<ApplicationGraphConnectionReadModel>();
				if (_document != null) {
					foreach (var node in _document.Nodes) {
						foreach (var port in node.Ports) {
							var connected = false;
							foreach (var connection in _document.Connections) {
								if ((connection.SourceNodeId == node.Id && connection.SourcePortId == port.Id) ||
									(connection.DestinationNodeId == node.Id && connection.DestinationPortId == port.Id)) {
									connected = true;
									break;
								}
							}
							nextPorts.Add(new ApplicationGraphPortReadModel(node.Id.Value + ":" + port.Id.Value, node.Id.Value, port.Id.Value,
								port.Type.ToString(), port.Direction.ToString(), port.Required, connected));
						}
					}
					foreach (var connection in _document.Connections)
						nextConnections.Add(new ApplicationGraphConnectionReadModel(connection.Id.Value, connection.SourceNodeId.Value,
							connection.SourcePortId.Value, connection.DestinationNodeId.Value, connection.DestinationPortId.Value,
							!string.IsNullOrEmpty(connection.ConversionId), connection.ConversionId));
				}
				_cachedGraphPorts = new ReadOnlyCollection<ApplicationGraphPortReadModel>(nextPorts);
				_cachedGraphConnections = new ReadOnlyCollection<ApplicationGraphConnectionReadModel>(nextConnections);
			}
			else if (_document != null) {
				// The structural stamp guarantees these IDs remain present.  Keeping this path
				// allocation-free is important because it is hit once per presentation frame.
				foreach (var node in _document.Nodes) {
					if (!_cachedGraphNodeRows.TryGetValue(node.Id.Value, out var prior)) {
						// GraphState owns legal topology edits and advances its revision for
						// each of them. This guard merely keeps an externally supplied document
						// from producing a null row; the next legal graph revision rebuilds the
						// complete port/connection projection.
						_cachedGraphNodeOrder.Add(node.Id.Value);
						_cachedGraphNodeRows[node.Id.Value] = CreateGraphNodeRow(node, runtimeOutputResults, runtimeCurrentConditions);
						graphNodeChanged = true;
						continue;
					}
					var status = ResolveNodeStatus(node, runtimeOutputResults, runtimeCurrentConditions, out var reason);
					var statusReason = node.IsUnknown && node.Unknown != null ? "Unknown node type: " + node.Unknown.OriginalNodeTypeId.Value : reason;
					if (!GraphNodeMatches(prior, node, status, statusReason)) {
						_cachedGraphNodeRows[node.Id.Value] = new ApplicationGraphNodeReadModel(node.Id.Value, node.TypeId.Value, node.DisplayName,
							node.Position.X, node.Position.Y, status, false, statusReason, node.Enabled,
							node.Unknown?.OriginalNodeTypeId.Value, node.Unknown?.OriginalSchemaVersion ?? 0, node.Unknown?.RawJsonValue);
						graphNodeChanged = true;
					}
				}
				if (graphNodeChanged)
					_cachedGraphNodeProjection = CreateGraphNodeProjection();
			}
			var graphNodes = _cachedGraphNodeProjection ?? (IReadOnlyList<ApplicationGraphNodeReadModel>)Array.Empty<ApplicationGraphNodeReadModel>();
			var graphPorts = _cachedGraphPorts ?? (IReadOnlyList<ApplicationGraphPortReadModel>)Array.Empty<ApplicationGraphPortReadModel>();
			var graphConnections = _cachedGraphConnections ?? (IReadOnlyList<ApplicationGraphConnectionReadModel>)Array.Empty<ApplicationGraphConnectionReadModel>();
			if (_cachedGraphModel == null || graphNodeChanged || graphTopologyChanged)
				_cachedGraphModel = new ApplicationGraphReadModel(graphNodes, graphPorts, graphConnections);
			var graphModel = _cachedGraphModel;

			var effectiveRevision = _runtime?.Parameters.EffectiveRevision ?? 0;
			var controlRevision = _runtime?.Parameters.ControlRevision ?? 0;
			var parameterStructureChanged = !ReferenceEquals(_cachedParameterDocument, _document) || _cachedParameterDocumentRevision != documentRevision;
			var parameterProjectionChanged = parameterStructureChanged;
			if (parameterStructureChanged) {
				_cachedParameterDocument = _document;
				_cachedParameterDocumentRevision = documentRevision;
				_cachedParameterEffectiveRevision = effectiveRevision;
				_cachedParameterControlRevision = controlRevision;
				var effective = _runtime == null ? new Dictionary<ParameterKey, ParameterValue>() : _runtime.Parameters.EffectiveValues;
				var parameterItems = new List<ApplicationParameterReadModel>();
				if (_document != null) foreach (var node in _document.Nodes) foreach (var parameter in node.Parameters) {
					var stableId = node.Id.Value + ":" + parameter.Definition.Id.Value;
					var key = new ParameterKey(node.Id, parameter.Definition.Id);
					var effectiveText = effective.TryGetValue(key, out var value) ? value.ToString() : parameter.BaseValue.ToString();
					var changed = !_previousParameters.TryGetValue(stableId, out var previous) || !string.Equals(previous.EffectiveValue, effectiveText, StringComparison.Ordinal);
					var expression = _document.FindExpression(node.Id, parameter.Definition.Id);
					var targets = _document.LogicalControls.SelectMany(control => control.Targets.Where(target => target.NodeId == node.Id && target.ParameterId == parameter.Definition.Id).Select(target => control.Id.Value)).ToList();
					var mediaOptions = parameter.Definition.Type == ParameterType.MediaAssetReference
						? _document.MediaAssets.OrderBy(asset => asset.DisplayName, StringComparer.Ordinal).ThenBy(asset => asset.Id.Value, StringComparer.Ordinal).Select(asset => asset.Id.Value + "|" + asset.DisplayName)
						: Enumerable.Empty<string>();
					var visibility = parameter.Definition.Visibility;
					parameterItems.Add(new ApplicationParameterReadModel(stableId, node.Id.Value, parameter.Definition.Id.Value, parameter.Definition.DisplayName, parameter.BaseValue.ToString(), effectiveText, changed,
						visibility == ParameterVisibility.ReadOnly || parameter.IsBroken, parameter.IsBroken, parameter.IsBroken, parameter.BrokenReason, parameter.Definition.Type.ToString(), DescribeRange(parameter.Definition.HardRange), string.Join(",", targets), expression == null ? string.Empty : DescribeExpression(expression.Expression), expression == null ? string.Empty : DescribeRange(expression.OutputRange),
						group: parameter.Definition.Group, order: parameter.Definition.DisplayOrder, description: parameter.Definition.Description, unit: parameter.Definition.Unit, step: parameter.Definition.Step,
						componentRanges: DescribeComponentRanges(parameter.Definition.HardRange),
						enumOptions: parameter.Definition.EnumOptions.Select(option => new ApplicationParameterOptionReadModel(option.Id.Value, option.DisplayName)),
						mediaOptions: mediaOptions, mediaKind: parameter.Definition.Type == ParameterType.MediaAssetReference ? "Media" : string.Empty,
						nodeTypeId: node.TypeId.Value, isVisible: visibility != ParameterVisibility.Hidden));
				}
				_cachedParameterProjection = new ReadOnlyCollection<ApplicationParameterReadModel>(parameterItems);
				_cachedParameterRows.Clear(); _cachedParameterOrder.Clear();
				foreach (var item in parameterItems) { _cachedParameterRows[item.StableId] = item; _cachedParameterOrder.Add(item.StableId); }
				_cachedControlValueProjection = new ReadOnlyDictionary<string, float>(_runtime == null ? new Dictionary<string, float>(StringComparer.Ordinal) : _runtime.Parameters.ControlValues.ToDictionary(x => x.Key.Value, x => x.Value, StringComparer.Ordinal));
				_cachedControlRuntimeProjection = BuildControlRuntimeProjection();
			}
			else if (_cachedParameterEffectiveRevision != effectiveRevision || _cachedParameterControlRevision != controlRevision) {
				parameterProjectionChanged = true;
				_cachedParameterEffectiveRevision = effectiveRevision;
				_cachedParameterControlRevision = controlRevision;
				var effective = _runtime?.Parameters.EffectiveValues;
				foreach (var key in _runtime?.Parameters.ChangedEffectiveKeys ?? Array.Empty<ParameterKey>()) {
					var stableId = key.NodeId.Value + ":" + key.ParameterId.Value;
					if (!_cachedParameterRows.TryGetValue(stableId, out var existing)) continue;
					var effectiveText = effective != null && effective.TryGetValue(key, out var value) ? value.ToString() : existing.BaseValue;
					_cachedParameterRows[stableId] = CopyParameterWithEffective(existing, effectiveText, !string.Equals(existing.EffectiveValue, effectiveText, StringComparison.Ordinal));
				}
				_cachedParameterProjection = new ReadOnlyCollection<ApplicationParameterReadModel>(_cachedParameterOrder.Select(id => _cachedParameterRows[id]).ToList());
				_cachedControlValueProjection = new ReadOnlyDictionary<string, float>(_runtime == null ? new Dictionary<string, float>(StringComparer.Ordinal) : _runtime.Parameters.ControlValues.ToDictionary(x => x.Key.Value, x => x.Value, StringComparer.Ordinal));
				_cachedControlRuntimeProjection = BuildControlRuntimeProjection();
			}
			var parameters = _cachedParameterProjection ?? (IList<ApplicationParameterReadModel>)new ReadOnlyCollection<ApplicationParameterReadModel>(new List<ApplicationParameterReadModel>());
			var controlValues = _cachedControlValueProjection ?? (IReadOnlyDictionary<string, float>)new ReadOnlyDictionary<string, float>(new Dictionary<string, float>(StringComparer.Ordinal));
			var controlRuntime = _cachedControlRuntimeProjection ?? (IReadOnlyDictionary<string, ApplicationControlRuntimeReadModel>)new ReadOnlyDictionary<string, ApplicationControlRuntimeReadModel>(new Dictionary<string, ApplicationControlRuntimeReadModel>(StringComparer.Ordinal));
			var dashboards = _cachedDashboardProjection;
			var presets = _cachedPresetProjection;
			var media = GetMediaProjection();
			var output = BuildOutputReadModel(frame, runtimeOutputResults);

			var diagnosticHub = _runtime?.Diagnostics;
			var diagnosticRevision = diagnosticHub?.Revision ?? 0;
			var diagnosticProjectionChanged = !ReferenceEquals(_cachedDiagnosticHub, diagnosticHub) || _cachedDiagnosticRevision != diagnosticRevision;
			if (diagnosticProjectionChanged) {
				_cachedDiagnosticHub = diagnosticHub;
				_cachedDiagnosticRevision = diagnosticRevision;
				_cachedDiagnosticHistoryProjection = new ReadOnlyCollection<ApplicationDiagnosticReadModel>((diagnosticHub?.HistoryEntries ?? Array.Empty<DiagnosticHistoryEntry>()).Select(entry => ToDiagnosticReadModel(entry.EntryId.ToString(), entry.Diagnostic, entry.Count, entry.FirstFrame, entry.LastFrame)).ToList());
				_cachedCurrentDiagnosticProjection = new ReadOnlyCollection<ApplicationDiagnosticReadModel>((runtimeCurrentConditions ?? new ReadOnlyDictionary<CurrentConditionKey, Diagnostic>(new Dictionary<CurrentConditionKey, Diagnostic>())).Select(pair => ToDiagnosticReadModel(CurrentDiagnosticId(pair.Key), pair.Value, 1, (ulong)Math.Max(0, pair.Value.FrameNumber), (ulong)Math.Max(0, pair.Value.FrameNumber))).ToList());
				_cachedDiagnosticSummaryProjection = new ReadOnlyDictionary<string, long>(_cachedDiagnosticHistoryProjection.GroupBy(x => x.Severity, StringComparer.Ordinal).ToDictionary(group => group.Key, group => (long)group.Count(), StringComparer.Ordinal));
			}
			var diagnosticHistory = _cachedDiagnosticHistoryProjection ?? (IReadOnlyList<ApplicationDiagnosticReadModel>)Array.Empty<ApplicationDiagnosticReadModel>();
			var currentDiagnostics = _cachedCurrentDiagnosticProjection ?? (IReadOnlyList<ApplicationDiagnosticReadModel>)Array.Empty<ApplicationDiagnosticReadModel>();
			var summary = _cachedDiagnosticSummaryProjection ?? (IDictionary<string, long>)new Dictionary<string, long>();
			if (_cachedDiagnosticChanges == null || fullSnapshot || diagnosticProjectionChanged) {
				_cachedDiagnosticChanges = BuildDiagnosticChanges(_previousDiagnostics, currentDiagnostics, diagnosticHistory, fullSnapshot, _readVersion);
			}
			var diagnosticChanges = _cachedDiagnosticChanges;
			if (_cachedDiagnosticModel == null || fullSnapshot || diagnosticProjectionChanged)
				_cachedDiagnosticModel = new ApplicationDiagnosticsReadModel(currentDiagnostics, diagnosticHistory, summary, diagnosticChanges);
			var diagnosticModel = _cachedDiagnosticModel;
			if (_cachedMediaDeletionProjection == null || _cachedMediaDeletionRevision != _mediaDeletions.Revision) {
				_cachedMediaDeletionRevision = _mediaDeletions.Revision;
				var items = new List<MediaDeletionReadModel>();
				foreach (var pending in _mediaDeletions.Pending) items.Add(new MediaDeletionReadModel(pending));
				foreach (var orphan in _mediaDeletions.Orphans) items.Add(new MediaDeletionReadModel(orphan));
				_cachedMediaDeletionProjection = new ReadOnlyCollection<MediaDeletionReadModel>(items);
			}
			var deletions = _cachedMediaDeletionProjection;
			_previousProjects.TryGetValue("project", out var previousProject);
			var projectChanged = fullSnapshot || previousProject == null || !SameProject(previousProject, projectModel);
			ReadModelChangeSet<ProjectReadModel> changeSet;
			if (projectChanged) {
				changeSet = new ReadModelChangeSet<ProjectReadModel>(_readVersion, fullSnapshot,
					new[] { new ReadModelChange<ProjectReadModel>("project", fullSnapshot || previousProject == null ? ReadModelChangeKind.Add : ReadModelChangeKind.Update, projectModel) });
				_previousProjects["project"] = projectModel;
			}
			else
				changeSet = new ReadModelChangeSet<ProjectReadModel>(_readVersion, false, ReadModelChangeSet<ProjectReadModel>.EmptyChanges);
			if (_cachedGraphNodeChanges == null || fullSnapshot || graphNodeChanged) {
				_cachedGraphNodeChanges = BuildChanges(_previousGraphNodes, graphNodes, x => x.Id, fullSnapshot, _readVersion, SameGraphNode);
				_previousGraphNodes.Clear(); foreach (var item in graphNodes) _previousGraphNodes[item.Id] = item;
			}
			if (_cachedGraphConnectionChanges == null || fullSnapshot || graphTopologyChanged) {
				_cachedGraphConnectionChanges = BuildChanges(_previousGraphConnections, graphConnections, x => x.Id, fullSnapshot, _readVersion, SameGraphConnection);
				_previousGraphConnections.Clear(); foreach (var item in graphConnections) _previousGraphConnections[item.Id] = item;
			}
			if (_cachedParameterChanges == null || fullSnapshot || parameterProjectionChanged) {
				_cachedParameterChanges = BuildChanges(_previousParameters, parameters, x => x.StableId, fullSnapshot, _readVersion, SameParameter);
				_previousParameters.Clear(); foreach (var item in parameters) _previousParameters[item.StableId] = item;
			}
			var hasSliceChanges = _cachedSplitChanges == null || fullSnapshot || graphNodeChanged || graphTopologyChanged || parameterProjectionChanged || diagnosticProjectionChanged;
			if (_cachedSplitChanges == null || hasSliceChanges)
				_cachedSplitChanges = new ApplicationReadModelChangeSets(_cachedGraphNodeChanges, _cachedGraphConnectionChanges, _cachedParameterChanges, diagnosticChanges);
			if (_cachedEmptySplitChanges == null)
				_cachedEmptySplitChanges = new ApplicationReadModelChangeSets(EmptyChanges<ApplicationGraphNodeReadModel>(), EmptyChanges<ApplicationGraphConnectionReadModel>(), EmptyChanges<ApplicationParameterReadModel>(), EmptyChanges<ApplicationDiagnosticReadModel>());
			var splitChanges = hasSliceChanges ? _cachedSplitChanges : _cachedEmptySplitChanges;
			if (_cachedCommandRevision != _commandRevision) {
				_cachedCommandRevision = _commandRevision;
				_cachedCommandProjection = new ReadOnlyCollection<PendingCommandReadModel>(_commands.ToList());
			}
			var commandProjection = _cachedCommandProjection ?? (IReadOnlyList<PendingCommandReadModel>)new ReadOnlyCollection<PendingCommandReadModel>(new List<PendingCommandReadModel>());
			var read = new ApplicationReadModel(_state, _recovered, project, recent, commandProjection, _runtime == null ? Enumerable.Empty<Diagnostic>() : _runtime.Diagnostics.History, deletions, changeSet,
				MakeEnvelope(shellModel, frame, documentRevision, graphRevision, fullSnapshot), MakeEnvelope(workspaceModel, frame, documentRevision, graphRevision, fullSnapshot), MakeEnvelope(catalog, frame, documentRevision, graphRevision, fullSnapshot), MakeEnvelope(graphModel, frame, documentRevision, graphRevision, fullSnapshot), MakeEnvelope((IReadOnlyList<ApplicationParameterReadModel>)parameters, frame, documentRevision, graphRevision, fullSnapshot), MakeEnvelope(dashboards, frame, documentRevision, graphRevision, fullSnapshot), MakeEnvelope(presets, frame, documentRevision, graphRevision, fullSnapshot), MakeEnvelope(media, frame, documentRevision, graphRevision, fullSnapshot), MakeEnvelope(output, frame, documentRevision, graphRevision, fullSnapshot), MakeEnvelope(diagnosticModel, frame, documentRevision, graphRevision, fullSnapshot), MakeEnvelope(commandProjection, frame, documentRevision, graphRevision, fullSnapshot), MakeEnvelope(_task, frame, documentRevision, graphRevision, fullSnapshot), splitChanges,
				controlValues, controlRuntime);
			_publishedReadModel = read;
			PrunePublishedTerminalCommands();
			return read;
		}

		private void PrunePublishedTerminalCommands() {
			var removeCount = _commands.Count(item => item.IsTerminal) - TerminalCommandHistoryLimit;
			if (removeCount <= 0) return;
			for (var index = 0; index < _commands.Count && removeCount > 0;) {
				var command = _commands[index];
				if (!command.IsTerminal) { index++; continue; }
				RemoveCommandTracking(command.CommandRequestId);
				_commands.RemoveAt(index);
				removeCount--;
			}
			_commandIndices.Clear();
			for (var index = 0; index < _commands.Count; index++)
				_commandIndices[_commands[index].CommandRequestId] = index;
			_commandRevision++;
		}

		private static ApplicationParameterReadModel CopyParameterWithEffective(ApplicationParameterReadModel source, string effectiveValue, bool changed) {
			return new ApplicationParameterReadModel(source.StableId, source.NodeId, source.ParameterId, source.DisplayName, source.BaseValue, effectiveValue,
				changed, source.IsReadOnly, source.IsBroken, source.IsClamped, source.Error, source.ValueType, source.HardRange, source.LogicalTargets,
				source.Expression, source.OutputClamp, source.Group, source.Order, source.Description, source.Unit, source.Step,
				source.ComponentRanges, source.EnumOptions, source.MediaOptions, source.MediaKind, source.NodeTypeId, source.IsVisible);
		}

		private IReadOnlyDictionary<string, ApplicationControlRuntimeReadModel> BuildControlRuntimeProjection() {
			var values = new Dictionary<string, ApplicationControlRuntimeReadModel>(StringComparer.Ordinal);
			if (_runtime != null)
				foreach (var pair in _runtime.Parameters.ControlRuntime)
					values[pair.Key.Value] = new ApplicationControlRuntimeReadModel(pair.Value.Value, pair.Value.HasValue, pair.Value.IsFiring);
			return new ReadOnlyDictionary<string, ApplicationControlRuntimeReadModel>(values);
		}

		private void RemoveCommandTracking(Guid requestId) {
			if (!_ledger.TryGetValue(requestId, out var entry)) return;
			foreach (var sequence in entry.ParameterSequences)
				if (_parameterRequests.TryGetValue(sequence, out var mapped) && mapped == requestId) _parameterRequests.Remove(sequence);
			foreach (var graphId in entry.GraphCommandIds)
				if (_graphRequests.TryGetValue(graphId, out var mapped) && mapped == requestId) _graphRequests.Remove(graphId);
			if (!string.IsNullOrEmpty(entry.RuntimeCommandId) && _runtimeRequests.TryGetValue(entry.RuntimeCommandId, out var runtimeRequest) && runtimeRequest == requestId)
				_runtimeRequests.Remove(entry.RuntimeCommandId);
			if (entry.InteractionId != Guid.Empty && _latestParameterRequestByInteraction.TryGetValue(entry.InteractionId, out var latest) && latest == requestId)
				_latestParameterRequestByInteraction.Remove(entry.InteractionId);
			_ledger.Remove(requestId);
		}

		private string ResolveNodeStatus(NodeRecord node,
			IReadOnlyDictionary<NodeInstanceId, IReadOnlyDictionary<PortId, NodeOutputResult>> outputResults,
			IReadOnlyDictionary<CurrentConditionKey, Diagnostic> currentConditions, out string reason) {
			reason = string.Empty;
			if (node == null || node.IsUnknown) { reason = "Node type is unavailable."; return "Unknown"; }
			if (!node.Enabled) { reason = "Node is disabled."; return "Disabled"; }
			if (_runtime == null) { reason = "Runtime session is unavailable."; return "Unavailable"; }
			var handle = _runtime.FindNode(node.Id);
			if (handle == null) { reason = "No runtime factory is registered for this node type."; return "Unavailable"; }
			if (handle.State == RuntimeNodeState.Faulted) { reason = "Runtime node faulted."; return "Faulted"; }
			if (handle.State == RuntimeNodeState.Preparing || handle.State == RuntimeNodeState.Creating) return "Preparing";
			if (handle.State == RuntimeNodeState.Retiring || handle.State == RuntimeNodeState.Disposed) { reason = "Runtime node is retiring."; return "Unavailable"; }
			if (outputResults != null && outputResults.TryGetValue(node.Id, out var outputs)) {
				if (outputs.Values.Any(x => x.Status == NodeOutputStatus.Faulted)) { reason = "Node output faulted."; return "Faulted"; }
				if (outputs.Values.Any(x => x.Status == NodeOutputStatus.Blocked)) { reason = "Node output is blocked."; return "Blocked"; }
				if (outputs.Values.Any(x => x.Status == NodeOutputStatus.Preparing)) return "Preparing";
			}
			if (currentConditions != null && currentConditions.Any(pair => pair.Value != null && pair.Value.NodeId == node.Id && string.Equals(pair.Key.Code.Value, "runtime.input.fallback", StringComparison.Ordinal))) {
				reason = "An optional input is using its fallback value."; return "UsingFallback";
			}
			if (node.TypeId.Value == GraphConstants.ProgramOutputTypeId && _runtime.ProgramState == ProgramRuntimeState.HoldingLastFrame) {
				reason = "The last valid Program frame is being held."; return "HoldingLastFrame";
			}
			return handle.State == RuntimeNodeState.Ready ? "Ready" : "Unavailable";
		}

		private ProjectReadModel GetProjectProjection() {
			var dirty = _document != null && _document.IsDirty;
			if (_cachedProjectModel != null
				&& ReferenceEquals(_cachedProjectDocument, _document)
				&& _cachedProjectDocumentRevision == (_document == null ? 0 : _document.DocumentRevision)
				&& string.Equals(_cachedProjectRoot, _root, StringComparison.Ordinal)
				&& _cachedProjectRecovered == _recovered
				&& _cachedProjectDirty == dirty)
				return _cachedProjectModel;

			_cachedProjectDocument = _document;
			_cachedProjectDocumentRevision = _document == null ? 0 : _document.DocumentRevision;
			_cachedProjectRoot = _root;
			_cachedProjectRecovered = _recovered;
			_cachedProjectDirty = dirty;
			_cachedProjectModel = new ProjectReadModel(_document, _root, _recovered);
			return _cachedProjectModel;
		}

		private IReadOnlyList<ApplicationNodeCatalogEntry> GetCatalogProjection() {
			if (_cachedCatalogProjection != null
				&& _cachedCatalogRevision == _registry.Revision
				&& _cachedCatalogRuntimeAvailable == _runtimeAvailable
				&& string.Equals(_cachedCatalogRuntimeUnavailableReason, _runtimeUnavailableReason, StringComparison.Ordinal))
				return _cachedCatalogProjection;

			var definitions = _registry.Definitions ?? Enumerable.Empty<NodeTypeDefinition>();
			var catalog = definitions.Select(definition => new ApplicationNodeCatalogEntry(definition.TypeId.Value, definition.DisplayName, _runtimeAvailable, _runtimeAvailable ? null : _runtimeUnavailableReason, definition.Category, definition.UserAddable,
				definition.Ports.Select(port => new ApplicationNodeCatalogPortMetadata(port.Id.Value, port.DisplayName, port.Direction.ToString(), port.Type.ToString(), port.Required)),
				definition.Parameters.Select(parameter => new ApplicationNodeCatalogParameterMetadata(parameter.Id.Value, parameter.DisplayName, parameter.Type.ToString(), parameter.DefaultValue.ToString(), DescribeRange(parameter.HardRange), parameter.RuntimeStateful,
					parameter.Group, parameter.DisplayOrder, parameter.Description, parameter.Unit, parameter.Step,
					parameter.Visibility == ParameterVisibility.ReadOnly, parameter.Visibility != ParameterVisibility.Hidden)), _runtimeAvailable, _runtimeAvailable ? null : _runtimeUnavailableReason)).ToList();
			_cachedCatalogRevision = _registry.Revision;
			_cachedCatalogRuntimeAvailable = _runtimeAvailable;
			_cachedCatalogRuntimeUnavailableReason = _runtimeUnavailableReason;
			_cachedCatalogProjection = new ReadOnlyCollection<ApplicationNodeCatalogEntry>(catalog);
			return _cachedCatalogProjection;
		}

		private IReadOnlyList<ApplicationMediaReadModel> GetMediaProjection() {
			var revision = _document == null ? 0 : _document.DocumentRevision;
			if (_cachedMediaProjection != null
				&& ReferenceEquals(_cachedMediaDocument, _document)
				&& _cachedMediaDocumentRevision == revision
				&& string.Equals(_cachedMediaRoot, _root, StringComparison.Ordinal))
				return _cachedMediaProjection;

			var media = _document == null ? new List<ApplicationMediaReadModel>() : _document.MediaAssets.Select(ToMediaReadModel).ToList();
			_cachedMediaDocument = _document;
			_cachedMediaDocumentRevision = revision;
			_cachedMediaRoot = _root;
			_cachedMediaProjection = new ReadOnlyCollection<ApplicationMediaReadModel>(media);
			return _cachedMediaProjection;
		}

		private ApplicationShellReadModel GetShellProjection() {
			var projectName = _document?.ProjectName ?? string.Empty;
			var dirty = _document != null && _document.IsDirty;
			var canUndo = _projectCommands != null && _projectCommands.CanUndo;
			var canRedo = _projectCommands != null && _projectCommands.CanRedo;
			var status = _task?.Status ?? string.Empty;
			if (_cachedShellModel != null && _cachedShellState == _state &&
				string.Equals(_cachedShellProjectName, projectName, StringComparison.Ordinal) &&
				string.Equals(_cachedShellRoot, _root, StringComparison.Ordinal) &&
				_cachedShellDirty == dirty && _cachedShellRecovered == _recovered &&
				_cachedShellCanUndo == canUndo && _cachedShellCanRedo == canRedo &&
				string.Equals(_cachedShellStatus, status, StringComparison.Ordinal))
				return _cachedShellModel;

			_cachedShellState = _state;
			_cachedShellProjectName = projectName;
			_cachedShellRoot = _root;
			_cachedShellDirty = dirty;
			_cachedShellRecovered = _recovered;
			_cachedShellCanUndo = canUndo;
			_cachedShellCanRedo = canRedo;
			_cachedShellStatus = status;
			_cachedShellModel = new ApplicationShellReadModel(_state, projectName, _root, dirty, _recovered, canUndo, canRedo, status);
			return _cachedShellModel;
		}

		private void ResetSessionShellAndWorkspaceCaches() {
			_cachedShellModel = null;
			_cachedWorkspaceModel = null;
			_cachedWorkspaceVisiblePanelSource = null;
			_cachedDashboardProjection = null;
			_cachedPresetProjection = null;
			_cachedWorkspaceVisiblePanelIds = null;
			_cachedDocumentListProjectionDocument = null;
			_cachedDocumentListProjectionRevision = long.MinValue;
			_cachedDocumentListProjectionUi = null;
		}

		private ApplicationWorkspaceReadModel GetWorkspaceProjection() {
			var visible = _cachedWorkspaceVisiblePanelIds ?? (IReadOnlyList<string>)Array.Empty<string>();
			if (_cachedWorkspaceModel != null &&
				string.Equals(_cachedWorkspaceLayoutId, _workspaceLayoutId, StringComparison.Ordinal) &&
				_cachedWorkspaceDirty == _workspaceLayoutDirty &&
				ReferenceEquals(_cachedWorkspaceVisiblePanelSource, visible))
				return _cachedWorkspaceModel;

			_cachedWorkspaceLayoutId = _workspaceLayoutId;
			_cachedWorkspaceDirty = _workspaceLayoutDirty;
			_cachedWorkspaceVisiblePanelSource = visible;
			_cachedWorkspaceModel = new ApplicationWorkspaceReadModel(_workspaceLayoutId, _workspaceLayoutDirty, visible,
				"Unavailable", "Workspace layout availability is owned by the Presentation host.");
			return _cachedWorkspaceModel;
		}

		private void EnsureDocumentListProjections() {
			var revision = _document == null ? 0 : _document.DocumentRevision;
			var ui = _document?.Ui;
			var dashboardChanged = _cachedDashboardProjection == null || !ReferenceEquals(_cachedDocumentListProjectionUi, ui);
			var presetChanged = _cachedPresetProjection == null ||
				!ReferenceEquals(_cachedDocumentListProjectionDocument, _document) ||
				_cachedDocumentListProjectionRevision != revision;
			if (!dashboardChanged && !presetChanged) return;

			if (dashboardChanged) {
				var dashboards = _document == null
					? new List<ApplicationDashboardReadModel>()
					: _document.Ui.DashboardPages.Select(page => new ApplicationDashboardReadModel(page.PageId, page.Name, page.Widgets.Select(widget => widget.WidgetId), page.Widgets.Select(widget => new ApplicationDashboardWidgetReadModel(widget.WidgetId, widget.NodeId.Value, widget.ParameterId.Value, widget.Column, widget.Row, widget.Width, widget.Height, widget.Label, widget.IsBroken, widget.BrokenReason)))).ToList();
				_cachedDashboardProjection = new ReadOnlyCollection<ApplicationDashboardReadModel>(dashboards);
				_cachedWorkspaceVisiblePanelIds = new ReadOnlyCollection<string>(dashboards.Select(page => page.Id).ToList());
				_cachedDocumentListProjectionUi = ui;
			}
			if (presetChanged) {
				var presets = _document == null
					? new List<ApplicationPresetReadModel>()
					: _document.Presets.Select(preset => new ApplicationPresetReadModel(preset.Id.Value, preset.Name, preset.IsBroken, preset.Entries.FirstOrDefault(x => x.IsBroken)?.BrokenReason, preset.Category, preset.SortIndex, preset.Entries.Select(entry => new ApplicationPresetEntryReadModel(entry.NodeId.Value, entry.ParameterId.Value, entry.ParameterType.ToString(), entry.Value.ToString(), entry.IsBroken, entry.BrokenReason)))).ToList();
				_cachedPresetProjection = new ReadOnlyCollection<ApplicationPresetReadModel>(presets);
			}
			_cachedDocumentListProjectionDocument = _document;
			_cachedDocumentListProjectionRevision = revision;
		}

		private ApplicationMediaReadModel ToMediaReadModel(MediaAssetRecord asset) {
			var status = "Unavailable";
			var reason = "Project root is unavailable.";
			if (!string.IsNullOrWhiteSpace(_root)) {
				try {
					var root = _fileSystem.GetFullPath(_root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
					var path = _fileSystem.GetFullPath(Path.Combine(_root, asset.RelativePath.Replace('/', Path.DirectorySeparatorChar)));
					if (path.StartsWith(root, StringComparison.OrdinalIgnoreCase)) {
						status = _fileSystem.Exists(path) ? "Ready" : "Missing";
						reason = status == "Ready" ? string.Empty : "Media file is missing.";
						if (status == "Ready" && _fileSystem is IProjectStreamingFileOperations streaming) {
							using (var stream = streaming.OpenRead(path)) {
								var length = stream.CanSeek ? stream.Length : asset.ByteSize;
								var hash = AssetIntegrity.Hash(stream);
								if (length != asset.ByteSize || !string.Equals(hash, asset.IntegrityHash, StringComparison.Ordinal)) {
									status = "Replaced";
									reason = "Media file integrity does not match the project manifest.";
								}
							}
						}
					}
					else { status = "Broken"; reason = "Media path is outside the project root."; }
				}
				catch { status = "Unavailable"; reason = "Media file could not be probed."; }
			}
			return new ApplicationMediaReadModel(asset.Id.Value, asset.RelativePath, asset.ByteSize, asset.IntegrityHash, status, asset.Kind.ToString(), asset.ColorSpace.ToString(), asset.AlphaMode.ToString(), MediaReferenceCount(asset.Id), reason);
		}

		private ApplicationOutputReadModel BuildOutputReadModel(ulong frame,
			IReadOnlyDictionary<NodeInstanceId, IReadOnlyDictionary<PortId, NodeOutputResult>> outputResults) {
			var programState = _runtime == null ? "Unavailable" : _runtime.ProgramState.ToString();
			var paused = _frames != null && _frames.Clock.IsPaused;
			var programDemand = _runtime?.OutputDemands?.FirstOrDefault(x => x.TargetKind == OutputTargetKind.Program);
			var program = ToSurface("program", "Program", _lastPresentation.Program, programDemand, programState, null);
			var performance = _runtime == null ? RuntimeProgramPerformanceSnapshot.Unavailable : _runtime.ProgramPerformance;
			var holding = string.Equals(programState, "HoldingLastFrame", StringComparison.Ordinal);
			if (holding && !_programWasHolding) {
				_programHoldingStartClock = _runtime?.LastSnapshot == null ? double.NaN : _runtime.LastSnapshot.GraphClockTime;
				var programNode = _runtime?.GraphEditor?.State?.Nodes?.FirstOrDefault(x => x.TypeId.Value == GraphConstants.ProgramOutputTypeId);
				_programHoldingCauseNodeId = programNode?.Id.Value ?? string.Empty;
				_programHoldingDiagnosticCode = string.Empty;
				if (programNode != null && outputResults != null && outputResults.TryGetValue(programNode.Id, out var outputs) && outputs.TryGetValue(new PortId(GraphConstants.ImagePortId), out var output))
					_programHoldingDiagnosticCode = output.Diagnostic?.Code.Value ?? string.Empty;
			}
			if (!holding) { _programHoldingStartClock = double.NaN; _programHoldingCauseNodeId = string.Empty; _programHoldingDiagnosticCode = string.Empty; }
			_programWasHolding = holding;
			var holdingDuration = holding && _runtime?.LastSnapshot != null && !double.IsNaN(_programHoldingStartClock)
				? Math.Max(0d, _runtime.LastSnapshot.GraphClockTime - _programHoldingStartClock) : double.NaN;
			var previews = new List<ApplicationOutputSurfaceReadModel>();
			var quality = (_runtime?.CapturePreviewOutputSnapshots() ?? Array.Empty<RuntimePreviewOutputSnapshot>()).ToDictionary(x => x.PreviewId, StringComparer.Ordinal);
			foreach (var previewId in (_document?.Ui?.PreviewNodeIds ?? Enumerable.Empty<string>()).Take(8)) {
				if (!NodeInstanceId.TryParse(previewId, out var previewNode)) continue;
				var result = _lastPresentation.Previews != null && _lastPresentation.Previews.TryGetValue(previewNode, out var presented)
					? presented
					: NodeOutputResult.Preparing(new Diagnostic(new DiagnosticCode("application.output.waiting"), Severity.Info, "Preview output is waiting for runtime presentation."));
				_previewSettings.TryGetValue(previewId, out var settings);
				_previewDemands.TryGetValue(previewId, out var requestedDemand);
				var demand = _runtime?.OutputDemands?.FirstOrDefault(x => x.TargetKind == OutputTargetKind.Preview && x.NodeId == previewNode);
				quality.TryGetValue(previewId, out var qualitySnapshot);
				if (demand == null && _previewHostVisible && requestedDemand != null) {
					// A Preview whose update interval has not elapsed is not
					// in OutputDemands for this frame, but its read model
					// must still expose the current effective quality rather
					// than reverting to the requested 640x360 dimensions.
					var width = qualitySnapshot == null ? requestedDemand.Width : qualitySnapshot.Width;
					var height = qualitySnapshot == null ? requestedDemand.Height : qualitySnapshot.Height;
					demand = new OutputDemand(OutputTargetKind.Preview, previewNode, new PortId(requestedDemand.PortId), width, height, requestedDemand.Focused);
				}
				var qualityName = qualitySnapshot == null ? null : "Stage" + qualitySnapshot.QualityStage.ToString(System.Globalization.CultureInfo.InvariantCulture);
				previews.Add(ToSurface(previewId, "Preview", result, demand, null, settings, requestedDemand == null ? (bool?)null : requestedDemand.Focused, qualityName));
			}
			return new ApplicationOutputReadModel(frame, programState, paused, program, previews, _document == null ? ProjectOutputSettings.DefaultProgramDisplay : _document.Settings.ProgramDisplay,
				performance.CpuFrameMilliseconds, performance.GpuFrameMilliseconds, performance.FramesPerSecond, performance.WarningActive, performance.ConsecutiveBadFrames,
				holdingDuration, _programHoldingCauseNodeId, _programHoldingDiagnosticCode, performance.FrameNumber);
		}

		private static ApplicationOutputSurfaceReadModel ToSurface(string id, string targetKind, NodeOutputResult result, OutputDemand demand, string runtimeState, ApplicationPreviewSettingsRequest settings, bool? focusedOverride = null, string qualityOverride = null) {
			var state = runtimeState;
			if (string.IsNullOrEmpty(state)) state = result.Status.ToString();
			if (result.Status == NodeOutputStatus.Faulted) state = "Faulted";
			else if (result.Status == NodeOutputStatus.Blocked) state = "Blocked";
			else if (result.Status == NodeOutputStatus.Preparing) state = "Preparing";
			var reason = result.Diagnostic == null ? string.Empty : result.Diagnostic.Message;
			var hold = string.Equals(runtimeState, "HoldingLastFrame", StringComparison.Ordinal) || (settings != null && settings.HoldLastFrame && result.Status == NodeOutputStatus.Available && !result.HasValue);
			return new ApplicationOutputSurfaceReadModel(id, targetKind, state, demand == null ? 0 : demand.Width, demand == null ? 0 : demand.Height, settings == null ? "Fit" : settings.FitMode.ToString(), settings == null ? "Black" : settings.BackgroundMode, qualityOverride ?? (settings == null ? "Project" : settings.Quality), demand != null, hold, reason, demand != null && (focusedOverride ?? demand.Focused));
		}

		private static ApplicationDiagnosticReadModel ToDiagnosticReadModel(string id, Diagnostic diagnostic, long count, ulong firstFrame, ulong lastFrame) {
			return new ApplicationDiagnosticReadModel(id, diagnostic == null ? string.Empty : diagnostic.Severity.ToString(), diagnostic == null ? string.Empty : diagnostic.Code.Value, diagnostic == null ? string.Empty : diagnostic.Message, diagnostic?.NodeId?.Value, count, firstFrame, lastFrame);
		}

		private int MediaReferenceCount(MediaAssetId assetId) {
			if (_document == null) return 0;
			var nodeReferences = _document.Nodes.SelectMany(node => node.Parameters).Count(parameter => parameter.BaseValue.IsMediaAssetSelected && parameter.BaseValue.AsMediaAsset().Value == assetId);
			var presetReferences = _document.Presets.SelectMany(preset => preset.Entries).Count(entry => entry.Value.IsMediaAssetSelected && entry.Value.AsMediaAsset().Value == assetId);
			return nodeReferences + presetReferences;
		}

		private static string DescribeRange(ParameterRange? range) => range.HasValue ? range.Value.Minimum + ".." + range.Value.Maximum : string.Empty;

		private static IEnumerable<ApplicationParameterComponentRangeReadModel> DescribeComponentRanges(ParameterRange? range) {
			if (!range.HasValue) return Enumerable.Empty<ApplicationParameterComponentRangeReadModel>();
			var minimum = range.Value.Minimum;
			var maximum = range.Value.Maximum;
			switch (minimum.Type) {
				case ParameterType.Vector2:
					var min2 = minimum.AsVector2(); var max2 = maximum.AsVector2();
					return new[] { ComponentRange("x", min2.X, max2.X), ComponentRange("y", min2.Y, max2.Y) };
				case ParameterType.Vector3:
					var min3 = minimum.AsVector3(); var max3 = maximum.AsVector3();
					return new[] { ComponentRange("x", min3.X, max3.X), ComponentRange("y", min3.Y, max3.Y), ComponentRange("z", min3.Z, max3.Z) };
				case ParameterType.Vector4:
					var min4 = minimum.AsVector4(); var max4 = maximum.AsVector4();
					return new[] { ComponentRange("x", min4.X, max4.X), ComponentRange("y", min4.Y, max4.Y), ComponentRange("z", min4.Z, max4.Z), ComponentRange("w", min4.W, max4.W) };
				case ParameterType.Color:
					var minColor = minimum.AsColor(); var maxColor = maximum.AsColor();
					return new[] { ComponentRange("r", minColor.R, maxColor.R), ComponentRange("g", minColor.G, maxColor.G), ComponentRange("b", minColor.B, maxColor.B), ComponentRange("a", minColor.A, maxColor.A) };
				default: return Enumerable.Empty<ApplicationParameterComponentRangeReadModel>();
			}
		}

		private static ApplicationParameterComponentRangeReadModel ComponentRange(string name, float minimum, float maximum)
			=> new ApplicationParameterComponentRangeReadModel(name, minimum.ToString(CultureInfo.InvariantCulture), maximum.ToString(CultureInfo.InvariantCulture));

		private static string DescribeExpression(LogicalExpressionNode expression) {
			if (expression == null) return string.Empty;
			if (expression is BaseValueLeaf) return "base";
			if (expression is LogicalControlLeaf control) return "control:" + control.ControlId.Value;
			if (expression is BrokenExpressionLeaf broken) return "broken:" + broken.OriginalControlId.Value;
			if (expression is BinaryLogicalExpression binary) return binary.Operator + "(" + DescribeExpression(binary.Left) + "," + DescribeExpression(binary.Right) + ")";
			return expression.GetType().Name;
		}

		private ReadModelEnvelope<T> MakeEnvelope<T>(T model, ulong frame, long documentRevision, long graphRevision, bool fullSnapshot) {
			return new ReadModelEnvelope<T>(_sessionId, _readVersion, frame, documentRevision, graphRevision, fullSnapshot, model);
		}

		private static string CurrentDiagnosticId(CurrentConditionKey key) => key.ScopeId + ":" + key.SubjectKind + ":" + key.SubjectId + ":" + key.Code.Value + ":" + (key.PortOrParameterId ?? string.Empty);

		private static ReadModelChangeSet<T> BuildChanges<T>(IDictionary<string, T> previous, IEnumerable<T> current, Func<T, string> id, bool full, long version, Func<T, T, bool> same = null) {
			var currentList = (current ?? Enumerable.Empty<T>()).ToList();
			var currentMap = currentList.ToDictionary(id, StringComparer.Ordinal);
			var changes = new List<ReadModelChange<T>>();
			foreach (var item in currentList) {
				var key = id(item);
				if (full || !previous.ContainsKey(key)) changes.Add(new ReadModelChange<T>(key, ReadModelChangeKind.Add, item));
				else if (same == null || !same(previous[key], item)) changes.Add(new ReadModelChange<T>(key, ReadModelChangeKind.Update, item));
			}
			foreach (var old in previous.Keys.Where(x => !currentMap.ContainsKey(x))) changes.Add(new ReadModelChange<T>(old, ReadModelChangeKind.Remove, default(T)));
			return new ReadModelChangeSet<T>(version, full, changes);
		}

		private static ReadModelChangeSet<T> EmptyChanges<T>() => new ReadModelChangeSet<T>(0, false, ReadModelChangeSet<T>.EmptyChanges);

		private static ReadModelChangeSet<ApplicationDiagnosticReadModel> BuildDiagnosticChanges(IDictionary<string, ApplicationDiagnosticReadModel> previous, IEnumerable<ApplicationDiagnosticReadModel> current, IEnumerable<ApplicationDiagnosticReadModel> history, bool full, long version) {
			var now = new Dictionary<string, ApplicationDiagnosticReadModel>(StringComparer.Ordinal);
			foreach (var item in current ?? Array.Empty<ApplicationDiagnosticReadModel>()) now["current:" + item.EntryId] = item;
			foreach (var item in history ?? Array.Empty<ApplicationDiagnosticReadModel>()) now["history:" + item.EntryId] = item;
			var changes = new List<ReadModelChange<ApplicationDiagnosticReadModel>>();
			foreach (var pair in now) {
				if (full || !previous.TryGetValue(pair.Key, out var old)) changes.Add(new ReadModelChange<ApplicationDiagnosticReadModel>(pair.Key, ReadModelChangeKind.Add, pair.Value));
				else if (!SameDiagnostic(old, pair.Value)) changes.Add(new ReadModelChange<ApplicationDiagnosticReadModel>(pair.Key, ReadModelChangeKind.Update, pair.Value));
			}
			foreach (var pair in previous) if (!now.ContainsKey(pair.Key)) changes.Add(new ReadModelChange<ApplicationDiagnosticReadModel>(pair.Key, ReadModelChangeKind.Remove, default(ApplicationDiagnosticReadModel)));
			previous.Clear(); foreach (var pair in now) previous[pair.Key] = pair.Value;
			return new ReadModelChangeSet<ApplicationDiagnosticReadModel>(version, full, changes);
		}

		private ApplicationGraphNodeReadModel CreateGraphNodeRow(NodeRecord node,
			IReadOnlyDictionary<NodeInstanceId, IReadOnlyDictionary<PortId, NodeOutputResult>> outputResults,
			IReadOnlyDictionary<CurrentConditionKey, Diagnostic> currentConditions) {
			var status = ResolveNodeStatus(node, outputResults, currentConditions, out var reason);
			var statusReason = node.IsUnknown && node.Unknown != null ? "Unknown node type: " + node.Unknown.OriginalNodeTypeId.Value : reason;
			return new ApplicationGraphNodeReadModel(node.Id.Value, node.TypeId.Value, node.DisplayName, node.Position.X, node.Position.Y,
				status, false, statusReason, node.Enabled, node.Unknown?.OriginalNodeTypeId.Value,
				node.Unknown?.OriginalSchemaVersion ?? 0, node.Unknown?.RawJsonValue);
		}

		private IReadOnlyList<ApplicationGraphNodeReadModel> CreateGraphNodeProjection() {
			var rows = new List<ApplicationGraphNodeReadModel>(_cachedGraphNodeOrder.Count);
			foreach (var id in _cachedGraphNodeOrder)
				rows.Add(_cachedGraphNodeRows[id]);
			return new ReadOnlyCollection<ApplicationGraphNodeReadModel>(rows);
		}

		private static bool GraphNodeMatches(ApplicationGraphNodeReadModel row, NodeRecord node, string status, string statusReason) {
			return row.Id == node.Id.Value && row.TypeId == node.TypeId.Value && row.DisplayName == node.DisplayName &&
				row.X == node.Position.X && row.Y == node.Position.Y && row.Status == status && !row.IsPending &&
				row.StatusReason == statusReason && row.Enabled == node.Enabled &&
				row.UnknownOriginalTypeId == (node.Unknown?.OriginalNodeTypeId.Value ?? string.Empty) &&
				row.UnknownOriginalSchemaVersion == (node.Unknown?.OriginalSchemaVersion ?? 0) &&
				row.OpaqueRawState == (node.Unknown?.RawJsonValue ?? string.Empty);
		}

		private static bool SameGraphNode(ApplicationGraphNodeReadModel left, ApplicationGraphNodeReadModel right) => left.TypeId == right.TypeId && left.DisplayName == right.DisplayName && left.X == right.X && left.Y == right.Y && left.Status == right.Status && left.IsPending == right.IsPending && left.StatusReason == right.StatusReason && left.Enabled == right.Enabled && left.UnknownOriginalTypeId == right.UnknownOriginalTypeId && left.UnknownOriginalSchemaVersion == right.UnknownOriginalSchemaVersion && left.OpaqueRawState == right.OpaqueRawState;
		private static bool SameGraphConnection(ApplicationGraphConnectionReadModel left, ApplicationGraphConnectionReadModel right) => left.FromNodeId == right.FromNodeId && left.FromPortId == right.FromPortId && left.ToNodeId == right.ToNodeId && left.ToPortId == right.ToPortId && left.IsImplicitConversion == right.IsImplicitConversion && left.ConversionLabel == right.ConversionLabel;
		private static bool SameGraphPorts(IReadOnlyList<ApplicationGraphPortReadModel> left, IReadOnlyList<ApplicationGraphPortReadModel> right) {
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Count != right.Count) return false;
			for (var i = 0; i < left.Count; i++)
				if (left[i].StableId != right[i].StableId || left[i].NodeId != right[i].NodeId || left[i].PortId != right[i].PortId || left[i].ValueType != right[i].ValueType || left[i].Direction != right[i].Direction || left[i].IsRequired != right[i].IsRequired || left[i].IsConnected != right[i].IsConnected) return false;
			return true;
		}

		private static bool SameGraphConnections(IReadOnlyList<ApplicationGraphConnectionReadModel> left, IReadOnlyList<ApplicationGraphConnectionReadModel> right) {
			if (ReferenceEquals(left, right)) return true;
			if (left == null || right == null || left.Count != right.Count) return false;
			for (var i = 0; i < left.Count; i++)
				if (left[i].Id != right[i].Id || !SameGraphConnection(left[i], right[i])) return false;
			return true;
		}
		private static bool SameParameter(ApplicationParameterReadModel left, ApplicationParameterReadModel right) {
			if (left.NodeId != right.NodeId || left.ParameterId != right.ParameterId || left.DisplayName != right.DisplayName || left.BaseValue != right.BaseValue || left.EffectiveValue != right.EffectiveValue || left.IsReadOnly != right.IsReadOnly || left.IsBroken != right.IsBroken || left.IsClamped != right.IsClamped || left.Error != right.Error || left.ValueType != right.ValueType || left.HardRange != right.HardRange || left.LogicalTargets != right.LogicalTargets || left.Expression != right.Expression || left.OutputClamp != right.OutputClamp || left.Group != right.Group || left.Order != right.Order || left.Description != right.Description || left.Unit != right.Unit || left.Step != right.Step || left.MediaKind != right.MediaKind || left.NodeTypeId != right.NodeTypeId || left.IsVisible != right.IsVisible)
				return false;
			if (left.ComponentRanges.Count != right.ComponentRanges.Count || left.EnumOptions.Count != right.EnumOptions.Count || left.MediaOptions.Count != right.MediaOptions.Count) return false;
			for (var i = 0; i < left.ComponentRanges.Count; i++) if (left.ComponentRanges[i].Name != right.ComponentRanges[i].Name || left.ComponentRanges[i].Minimum != right.ComponentRanges[i].Minimum || left.ComponentRanges[i].Maximum != right.ComponentRanges[i].Maximum) return false;
			for (var i = 0; i < left.EnumOptions.Count; i++) if (left.EnumOptions[i].Id != right.EnumOptions[i].Id || left.EnumOptions[i].DisplayName != right.EnumOptions[i].DisplayName) return false;
			for (var i = 0; i < left.MediaOptions.Count; i++) if (left.MediaOptions[i] != right.MediaOptions[i]) return false;
			return true;
		}
		private static bool SameDiagnostic(ApplicationDiagnosticReadModel left, ApplicationDiagnosticReadModel right) => left.EntryId == right.EntryId && left.Severity == right.Severity && left.Code == right.Code && left.Message == right.Message && left.NodeId == right.NodeId && left.Count == right.Count && left.FirstFrame == right.FirstFrame && left.LastFrame == right.LastFrame;
		private static bool SameProject(ProjectReadModel left, ProjectReadModel right) {
			if (left == null || right == null) return left == right;
			if (left.ProjectName != right.ProjectName || left.ProjectRoot != right.ProjectRoot || left.IsDirty != right.IsDirty || left.IsRecovered != right.IsRecovered || left.NodeCount != right.NodeCount || left.ConnectionCount != right.ConnectionCount || left.PresetCount != right.PresetCount || left.MediaAssetCount != right.MediaAssetCount || left.LogicalControls.Count != right.LogicalControls.Count) return false;
			for (var i = 0; i < left.LogicalControls.Count; i++) {
				var a = left.LogicalControls[i]; var b = right.LogicalControls[i];
				if (a.Id != b.Id || a.Name != b.Name || a.Kind != b.Kind || a.PresetId != b.PresetId || a.Mappings.Count != b.Mappings.Count) return false;
			}
			return true;
		}

		private static Diagnostic Failure(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "application");
	}

	/// <summary>Application-side contract implemented by Keyboard adapters.</summary>
	public interface IKeyboardInputApplicationPort {
		bool IsKeyboardLearnActive { get; }
		ApplicationCommandResult HandleKeyboard(PhysicalKey key, bool pressed);
		ApplicationCommandResult BeginKeyboardLearn(LogicalControlId id, Guid? interactionId = null);
		ApplicationCommandResult CancelKeyboardLearn(Guid? interactionId = null);
	}

	public interface IMidiInputApplicationPort {
		ApplicationCommandResult HandleMidi(MidiInputEvent inputEvent);
	}

	public interface ILiveControlApplicationPort {
		ApplicationCommandResult SetLiveControlValue(LogicalControlId id, float normalizedValue);
	}
}
