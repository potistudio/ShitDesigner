using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;

namespace ShitDesigner.Project {
	public readonly struct StateToken : IEquatable<StateToken> {
		private readonly Guid _value;
		public bool IsEmpty => _value == Guid.Empty;
		private StateToken(Guid value) { _value = value; }
		public static StateToken New() => new StateToken(Guid.NewGuid());
		public bool Equals(StateToken other) => _value == other._value;
		public override bool Equals(object obj) => obj is StateToken other && Equals(other);
		public override int GetHashCode() => _value.GetHashCode();
		public override string ToString() => _value.ToString("D");
		public static bool operator ==(StateToken left, StateToken right) => left.Equals(right);
		public static bool operator !=(StateToken left, StateToken right) => !left.Equals(right);
	}

	public sealed class ProjectDocument {
		private readonly List<NodeRecord> _nodes = new List<NodeRecord>();
		private readonly List<ConnectionRecord> _connections = new List<ConnectionRecord>();
		private readonly List<LogicalControlRecord> _logicalControls = new List<LogicalControlRecord>();
		private readonly List<ParameterExpressionRecord> _expressions = new List<ParameterExpressionRecord>();
		private readonly List<PresetRecord> _presets = new List<PresetRecord>();
		private readonly List<MediaAssetRecord> _mediaAssets = new List<MediaAssetRecord>();
		private ProjectUiStateRecord _ui;
		private IReadOnlyList<NodeRecord> _nodesView;
		private IReadOnlyList<ConnectionRecord> _connectionsView;
		private IReadOnlyList<LogicalControlRecord> _logicalControlsView;
		private IReadOnlyList<ParameterExpressionRecord> _expressionsView;
		private IReadOnlyList<PresetRecord> _presetsView;
		private IReadOnlyList<MediaAssetRecord> _mediaAssetsView;

		public string ProjectName { get; private set; }
		public int ProjectFormatVersion { get; }
		public long DocumentRevision { get; private set; }
		public StateToken CurrentToken { get; private set; }
		public StateToken SavedToken { get; private set; }
		public StateToken SavingToken { get; private set; }
		public bool IsDirty => CurrentToken != SavedToken;
		public IReadOnlyList<NodeRecord> Nodes => _nodesView ?? (_nodesView = new ReadOnlyCollection<NodeRecord>(_nodes));
		public IReadOnlyList<ConnectionRecord> Connections => _connectionsView ?? (_connectionsView = new ReadOnlyCollection<ConnectionRecord>(_connections));
		public IReadOnlyList<LogicalControlRecord> LogicalControls => _logicalControlsView ?? (_logicalControlsView = new ReadOnlyCollection<LogicalControlRecord>(_logicalControls));
		public IReadOnlyList<ParameterExpressionRecord> Expressions => _expressionsView ?? (_expressionsView = new ReadOnlyCollection<ParameterExpressionRecord>(_expressions));
		public IReadOnlyList<PresetRecord> Presets => _presetsView ?? (_presetsView = new ReadOnlyCollection<PresetRecord>(_presets));
		public IReadOnlyList<MediaAssetRecord> MediaAssets => _mediaAssetsView ?? (_mediaAssetsView = new ReadOnlyCollection<MediaAssetRecord>(_mediaAssets));
		public ProjectUiStateRecord Ui => _ui;
		public ProjectOutputSettings Settings { get; private set; }

		/// <summary>
		/// Persistence's canonical top-level ControlMapping projection.  The
		/// domain keeps mappings on LogicalControl, while this read-only view
		/// gives serializers an explicit, deterministic boundary.
		/// </summary>
		public IReadOnlyList<ControlMappingRecord> ControlMappings =>
			new ReadOnlyCollection<ControlMappingRecord>(_logicalControls.SelectMany(x => x.Mappings).ToList());

		public ProjectDocument(string projectName, int projectFormatVersion = 1, ProjectUiStateRecord ui = null, ProjectOutputSettings settings = null) {
			if (string.IsNullOrWhiteSpace(projectName)) throw new ArgumentException("Project name is required.", nameof(projectName));
			if (projectFormatVersion < 1) throw new ArgumentOutOfRangeException(nameof(projectFormatVersion));
			ProjectName = projectName.Trim(); ProjectFormatVersion = projectFormatVersion; CurrentToken = StateToken.New(); SavedToken = CurrentToken; SavingToken = CurrentToken; _ui = ui ?? new ProjectUiStateRecord(); Settings = settings ?? ProjectOutputSettings.CreateDefault();
		}

		public NodeRecord FindNode(NodeInstanceId id) => _nodes.FirstOrDefault(x => x.Id == id);
		public LogicalControlRecord FindLogicalControl(LogicalControlId id) => _logicalControls.FirstOrDefault(x => x.Id == id);
		public PresetRecord FindPreset(PresetId id) => _presets.FirstOrDefault(x => x.Id == id);
		public MediaAssetRecord FindMediaAsset(MediaAssetId id) => _mediaAssets.FirstOrDefault(x => x.Id == id);
		public ParameterExpressionRecord FindExpression(NodeInstanceId nodeId, ParameterId parameterId) => _expressions.FirstOrDefault(x => x.NodeId == nodeId && x.ParameterId == parameterId);

		public StateToken BeginSave() { SavingToken = CurrentToken; return SavingToken; }
		public bool TryMarkSaved(StateToken completedSavingToken) {
			if (completedSavingToken != SavingToken) return false;
			SavedToken = completedSavingToken;
			return true;
		}
		public void MarkSaved() => TryMarkSaved(SavingToken);
		public bool MarkSaved(StateToken completedSavingToken) => TryMarkSaved(completedSavingToken);
		public void MarkSavedAtCurrentToken() { SavingToken = CurrentToken; SavedToken = CurrentToken; }

		public CSharpFunctionalExtensions.Result<ProjectSaveSnapshot, Diagnostic> TryCreateSaveSnapshot() {
			if (HasDuplicateIds()) return CSharpFunctionalExtensions.Result.Failure<ProjectSaveSnapshot, Diagnostic>(ProjectDiagnostics.Rejected("project.snapshot.duplicate_id", "Project snapshot contains duplicate stable IDs."));
			return CSharpFunctionalExtensions.Result.Success<ProjectSaveSnapshot, Diagnostic>(new ProjectSaveSnapshot(ProjectName, ProjectFormatVersion, DocumentRevision, _nodes, _connections, _logicalControls, _expressions, _presets, _mediaAssets, _ui, Settings, SavingToken));
		}

		internal DocumentSnapshot CaptureSnapshot() {
			return new DocumentSnapshot(ProjectName, _nodes.ToList(), _connections.ToList(), _logicalControls.ToList(), _expressions.ToList(), _presets.ToList(), _mediaAssets.ToList(), _ui, Settings, CurrentToken, SavedToken, SavingToken);
		}
		internal void RestoreSnapshot(DocumentSnapshot snapshot, bool restoreToken, long revision) {
			ProjectName = snapshot.ProjectName;
			_nodes.Clear(); _nodes.AddRange(snapshot.Nodes);
			_connections.Clear(); _connections.AddRange(snapshot.Connections);
			_logicalControls.Clear(); _logicalControls.AddRange(snapshot.LogicalControls);
			_expressions.Clear(); _expressions.AddRange(snapshot.Expressions);
			_presets.Clear(); _presets.AddRange(snapshot.Presets);
			_mediaAssets.Clear(); _mediaAssets.AddRange(snapshot.MediaAssets);
			_ui = snapshot.Ui;
			Settings = snapshot.Settings;
			if (restoreToken) { CurrentToken = snapshot.CurrentToken; SavedToken = snapshot.SavedToken; SavingToken = snapshot.SavingToken; }
			DocumentRevision = revision;
		}
		internal void RestoreHistorySnapshot(DocumentSnapshot snapshot, long revision) {
			var savedToken = SavedToken;
			var savingToken = SavingToken;
			RestoreSnapshot(snapshot, true, revision);
			SavedToken = savedToken;
			SavingToken = savingToken;
		}
		internal void CommitMutation() {
			DocumentRevision++;
			CurrentToken = StateToken.New();
		}
		internal void ReplaceToken(StateToken token) => CurrentToken = token;
		internal void AddNode(NodeRecord node) { _nodes.Add(node); }
		internal void ReplaceNode(NodeRecord node) { var index = _nodes.FindIndex(x => x.Id == node.Id); if (index < 0) throw new InvalidOperationException("Node not found."); _nodes[index] = node; }
		/// <summary>
		/// Atomically replaces the graph portion after an external Graph
		/// workspace has validated its patch. Broken persistence references
		/// for deleted nodes are kept so a later stable-ID rebind can repair
		/// them; the graph's active edges come from the validated snapshot.
		/// </summary>
		internal void ReplaceGraph(IEnumerable<NodeRecord> nodes, IEnumerable<ConnectionRecord> connections) {
			var replacementNodes = (nodes ?? Enumerable.Empty<NodeRecord>()).ToList();
			var replacementConnections = (connections ?? Enumerable.Empty<ConnectionRecord>()).ToList();
			var incomingConnectionIds = new HashSet<ConnectionId>(replacementConnections.Select(x => x.Id));
			var removedNodeIds = new HashSet<NodeInstanceId>(_nodes.Select(x => x.Id).Except(replacementNodes.Select(x => x.Id)));
			foreach (var removedNodeId in removedNodeIds) RemoveNode(removedNodeId);
			var preservedBroken = _connections.Where(x => x.IsBroken && !incomingConnectionIds.Contains(x.Id)).ToList();
			_nodes.Clear();
			_nodes.AddRange(replacementNodes);
			_connections.Clear();
			_connections.AddRange(preservedBroken);
			_connections.AddRange(replacementConnections);
			// The incoming graph is already validated/normalized by the
			// GraphEditor candidate. Do not run the registry-blind repair pass
			// here: it could incorrectly re-enable a Broken edge whose saved
			// conversion ID is no longer registered. Load-time repair remains
			// explicit in ProjectDocumentFactory.
		}
		internal void RemoveNode(NodeInstanceId id) {
			_nodes.RemoveAll(x => x.Id == id);
			for (var i = 0; i < _connections.Count; i++) if (_connections[i].SourceNodeId == id || _connections[i].DestinationNodeId == id) _connections[i] = _connections[i].AsBroken("Referenced node was deleted.");
			for (var i = 0; i < _logicalControls.Count; i++) {
				var control = _logicalControls[i];
				var targets = control.Targets.Select(x => x.NodeId == id ? x.AsBroken("Referenced node was deleted.") : x);
				_logicalControls[i] = control.WithTargets(targets);
			}
			for (var i = 0; i < _expressions.Count; i++) if (_expressions[i].NodeId == id) _expressions[i] = _expressions[i].AsBroken("Referenced node was deleted.");
			for (var i = 0; i < _presets.Count; i++) _presets[i] = _presets[i].WithEntries(_presets[i].Entries.Select(x => x.NodeId == id ? x.AsBroken("Referenced node was deleted.") : x));
			_ui = _ui.WithDashboardPages(_ui.DashboardPages.Select(page => new DashboardPageRecord(page.PageId, page.Name, page.Widgets.Select(x => x.NodeId == id ? x.AsBroken("Referenced node was deleted.") : x))));
		}
		internal void AddConnection(ConnectionRecord connection) { _connections.RemoveAll(x => x.DestinationNodeId == connection.DestinationNodeId && x.DestinationPortId == connection.DestinationPortId); _connections.Add(connection); }
		internal void RemoveConnection(ConnectionId id) { _connections.RemoveAll(x => x.Id == id); }
		internal void AddLogicalControl(LogicalControlRecord control) { _logicalControls.Add(control); }
		internal void ReplaceLogicalControl(LogicalControlRecord control) {
			if (control == null) throw new ArgumentNullException(nameof(control));
			var index = _logicalControls.FindIndex(x => x.Id == control.Id);
			if (index < 0) throw new InvalidOperationException("Logical control not found.");
			_logicalControls[index] = control;
		}
		internal void RemoveLogicalControl(LogicalControlId id) {
			_logicalControls.RemoveAll(x => x.Id == id);
			for (var i = 0; i < _expressions.Count; i++) _expressions[i] = _expressions[i].Revalidate(controlId => controlId != id && FindLogicalControl(controlId) != null);
		}
		internal void AddExpression(ParameterExpressionRecord expression) { _expressions.RemoveAll(x => x.NodeId == expression.NodeId && x.ParameterId == expression.ParameterId); _expressions.Add(expression); }
		internal void RemoveExpression(NodeInstanceId nodeId, ParameterId parameterId) { _expressions.RemoveAll(x => x.NodeId == nodeId && x.ParameterId == parameterId); }
		internal void AddPreset(PresetRecord preset) { _presets.Add(preset); }
		internal void ReplacePreset(PresetRecord preset) { var index = _presets.FindIndex(x => x.Id == preset.Id); if (index < 0) throw new InvalidOperationException("Preset not found."); _presets[index] = preset; }
		internal void RemovePreset(PresetId id) {
			_presets.RemoveAll(x => x.Id == id);
			for (var i = 0; i < _logicalControls.Count; i++) if (_logicalControls[i].PresetId == id) _logicalControls[i] = _logicalControls[i].AsBrokenPreset("Referenced preset was deleted.");
		}
		internal void AddMediaAsset(MediaAssetRecord asset) { _mediaAssets.Add(asset); }
		internal void RemoveMediaAsset(MediaAssetId id) {
			_mediaAssets.RemoveAll(x => x.Id == id);
			for (var i = 0; i < _nodes.Count; i++) {
				var node = _nodes[i];
				foreach (var parameter in node.Parameters) if (parameter.Definition.Type == ParameterType.MediaAssetReference && parameter.BaseValue.IsMediaAssetSelected && parameter.BaseValue.AsMediaAsset().Value == id) node = node.WithParameterRecord(parameter.Definition.Id, parameter.AsBroken("Referenced media asset was deleted."));
				_nodes[i] = node;
			}
			for (var i = 0; i < _presets.Count; i++) _presets[i] = _presets[i].WithEntries(_presets[i].Entries.Select(x => x.Value.Type == ParameterType.MediaAssetReference && x.Value.IsMediaAssetSelected && x.Value.AsMediaAsset().Value == id ? x.AsBroken("Referenced media asset was deleted.") : x));
		}
		internal void ReplaceUi(ProjectUiStateRecord ui) { _ui = ui ?? throw new ArgumentNullException(nameof(ui)); }
		internal void ReplaceSettings(ProjectOutputSettings settings) { Settings = settings ?? throw new ArgumentNullException(nameof(settings)); }
		internal void RevalidateBrokenReferences() {
			for (var i = 0; i < _nodes.Count; i++) {
				var node = _nodes[i];
				foreach (var parameter in node.Parameters) {
					if (!parameter.IsBroken) continue;
					if (parameter.Definition.Type != ParameterType.MediaAssetReference || !parameter.BaseValue.IsMediaAssetSelected || FindMediaAsset(parameter.BaseValue.AsMediaAsset().Value) == null) continue;
					node = node.WithParameterRecord(parameter.Definition.Id, parameter.AsRepaired());
				}
				_nodes[i] = node;
			}
			for (var i = 0; i < _connections.Count; i++) if (_connections[i].IsBroken && CanRepairConnection(_connections[i])) _connections[i] = _connections[i].AsRepaired();
			for (var i = 0; i < _logicalControls.Count; i++) {
				var control = _logicalControls[i];
				var targets = control.Targets.Select(target => target.IsBroken && FindNode(target.NodeId)?.FindParameter(target.ParameterId)?.Definition.Type == target.ParameterType ? target.AsRepaired() : target);
				if (control.PresetId.HasValue && FindPreset(control.PresetId.Value) == null && !control.PresetIsBroken) control = control.AsBrokenPreset("Referenced preset is missing.");
				else if (control.PresetIsBroken && control.PresetId.HasValue && FindPreset(control.PresetId.Value) != null) control = control.AsRepairedPreset();
				_logicalControls[i] = control.WithTargets(targets);
			}
			for (var i = 0; i < _expressions.Count; i++) {
				var expression = _expressions[i];
				if (FindNode(expression.NodeId)?.FindParameter(expression.ParameterId) != null) expression = expression.Revalidate(controlId => FindLogicalControl(controlId) != null && FindLogicalControl(controlId).Kind == LogicalControlKind.Value);
				_expressions[i] = expression;
			}
			for (var i = 0; i < _presets.Count; i++) {
				var preset = _presets[i];
				_presets[i] = preset.WithEntries(preset.Entries.Select(entry => {
					var node = FindNode(entry.NodeId); var parameter = node?.FindParameter(entry.ParameterId);
					var valid = node != null && parameter != null && parameter.Definition.Type == entry.ParameterType && (!entry.Value.IsMediaAssetSelected || FindMediaAsset(entry.Value.AsMediaAsset().Value) != null);
					return entry.IsBroken && valid ? entry.AsRepaired() : entry;
				}));
			}
			_ui = _ui.WithDashboardPages(_ui.DashboardPages.Select(page => new DashboardPageRecord(page.PageId, page.Name, page.Widgets.Select(widget => widget.IsBroken && FindNode(widget.NodeId)?.FindParameter(widget.ParameterId) != null ? widget.AsRepaired() : widget))));
		}

		/// <summary>
		/// Marks persisted references to a missing or replaced media file as
		/// broken without dropping their stable ID.  Persistence calls this
		/// while building an isolated load candidate; the current document is
		/// never touched by a failed load.
		/// </summary>
		public void MarkMediaAssetBroken(MediaAssetId id, string reason) {
			for (var i = 0; i < _nodes.Count; i++) {
				var node = _nodes[i];
				foreach (var parameter in node.Parameters)
					if (parameter.Definition.Type == ParameterType.MediaAssetReference && parameter.BaseValue.IsMediaAssetSelected && parameter.BaseValue.AsMediaAsset().Value == id)
						node = node.WithParameterRecord(parameter.Definition.Id, parameter.AsBroken(reason));
				_nodes[i] = node;
			}
			for (var i = 0; i < _presets.Count; i++)
				_presets[i] = _presets[i].WithEntries(_presets[i].Entries.Select(entry => entry.Value.Type == ParameterType.MediaAssetReference && entry.Value.IsMediaAssetSelected && entry.Value.AsMediaAsset().Value == id ? entry.AsBroken(reason) : entry));
			CommitMutation();
		}
		private bool CanRepairConnection(ConnectionRecord connection) {
			var sourceNode = FindNode(connection.SourceNodeId); var destinationNode = FindNode(connection.DestinationNodeId);
			var source = sourceNode?.FindPort(connection.SourcePortId); var destination = destinationNode?.FindPort(connection.DestinationPortId);
			if (source == null || destination == null || source.Direction != PortDirection.Output || destination.Direction != PortDirection.Input) return false;
			return source.Type == destination.Type ? string.IsNullOrEmpty(connection.ConversionId) : !string.IsNullOrEmpty(connection.ConversionId);
		}
		internal bool HasDuplicateIds() {
			return _nodes.GroupBy(x => x.Id).Any(g => g.Count() > 1) || _connections.GroupBy(x => x.Id).Any(g => g.Count() > 1) || _logicalControls.GroupBy(x => x.Id).Any(g => g.Count() > 1) || _presets.GroupBy(x => x.Id).Any(g => g.Count() > 1) || _mediaAssets.GroupBy(x => x.Id).Any(g => g.Count() > 1);
		}
	}

	internal sealed class DocumentSnapshot {
		public string ProjectName { get; }
		public IReadOnlyList<NodeRecord> Nodes { get; }
		public IReadOnlyList<ConnectionRecord> Connections { get; }
		public IReadOnlyList<LogicalControlRecord> LogicalControls { get; }
		public IReadOnlyList<ParameterExpressionRecord> Expressions { get; }
		public IReadOnlyList<PresetRecord> Presets { get; }
		public IReadOnlyList<MediaAssetRecord> MediaAssets { get; }
		public ProjectUiStateRecord Ui { get; }
		public ProjectOutputSettings Settings { get; }
		public StateToken CurrentToken { get; }
		public StateToken SavedToken { get; }
		public StateToken SavingToken { get; }
		public DocumentSnapshot(string projectName, IEnumerable<NodeRecord> nodes, IEnumerable<ConnectionRecord> connections, IEnumerable<LogicalControlRecord> controls, IEnumerable<ParameterExpressionRecord> expressions, IEnumerable<PresetRecord> presets, IEnumerable<MediaAssetRecord> assets, ProjectUiStateRecord ui, ProjectOutputSettings settings, StateToken current, StateToken saved, StateToken saving) {
			ProjectName = projectName; Nodes = new ReadOnlyCollection<NodeRecord>(nodes.ToList()); Connections = new ReadOnlyCollection<ConnectionRecord>(connections.ToList()); LogicalControls = new ReadOnlyCollection<LogicalControlRecord>(controls.ToList()); Expressions = new ReadOnlyCollection<ParameterExpressionRecord>(expressions.ToList()); Presets = new ReadOnlyCollection<PresetRecord>(presets.ToList()); MediaAssets = new ReadOnlyCollection<MediaAssetRecord>(assets.ToList()); Ui = ui; Settings = settings ?? ProjectOutputSettings.CreateDefault(); CurrentToken = current; SavedToken = saved; SavingToken = saving;
		}
	}

	/// <summary>Validated, immutable save boundary data. It contains no I/O or Unity runtime objects.</summary>
	public sealed class ProjectSaveSnapshot {
		public string ProjectName { get; }
		public int ProjectFormatVersion { get; }
		public long DocumentRevision { get; }
		public IReadOnlyList<NodeRecord> Nodes { get; }
		public IReadOnlyList<ConnectionRecord> Connections { get; }
		public IReadOnlyList<LogicalControlRecord> LogicalControls { get; }
		public IReadOnlyList<ParameterExpressionRecord> Expressions { get; }
		public IReadOnlyList<PresetRecord> Presets { get; }
		public IReadOnlyList<MediaAssetRecord> MediaAssets { get; }
		public ProjectUiStateRecord Ui { get; }
		public ProjectOutputSettings Settings { get; }
		public IReadOnlyList<ControlMappingRecord> ControlMappings =>
			new ReadOnlyCollection<ControlMappingRecord>(LogicalControls.SelectMany(x => x.Mappings).ToList());
		public StateToken SavingToken { get; }
		internal ProjectSaveSnapshot(string projectName, int projectFormatVersion, long revision, IEnumerable<NodeRecord> nodes, IEnumerable<ConnectionRecord> connections, IEnumerable<LogicalControlRecord> controls, IEnumerable<ParameterExpressionRecord> expressions, IEnumerable<PresetRecord> presets, IEnumerable<MediaAssetRecord> assets, ProjectUiStateRecord ui, ProjectOutputSettings settings, StateToken savingToken) {
			ProjectName = projectName; ProjectFormatVersion = projectFormatVersion; DocumentRevision = revision;
			Nodes = new ReadOnlyCollection<NodeRecord>(nodes.ToList()); Connections = new ReadOnlyCollection<ConnectionRecord>(connections.ToList()); LogicalControls = new ReadOnlyCollection<LogicalControlRecord>(controls.ToList()); Expressions = new ReadOnlyCollection<ParameterExpressionRecord>(expressions.ToList()); Presets = new ReadOnlyCollection<PresetRecord>(presets.ToList()); MediaAssets = new ReadOnlyCollection<MediaAssetRecord>(assets.ToList()); Ui = ui; Settings = settings ?? ProjectOutputSettings.CreateDefault(); SavingToken = savingToken;
		}
	}
}
