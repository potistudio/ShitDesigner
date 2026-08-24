using System;

namespace ShitDesigner.Presentation {
	public sealed class PresentationApplyReport {
		public bool Applied { get; }
		public bool RequestedFullSnapshot { get; }
		public Guid ProjectSessionId { get; }
		public long ReadModelVersion { get; }
		public PresentationApplyReport(bool applied, bool requestedFullSnapshot, Guid projectSessionId, long readModelVersion) { Applied = applied; RequestedFullSnapshot = requestedFullSnapshot; ProjectSessionId = projectSessionId; ReadModelVersion = readModelVersion; }
	}

	/// <summary>Frame-boundary coordinator.  It never applies partial or old
	/// snapshots and does not rebuild the root visual tree on project switch.</summary>
	public sealed class PresentationCoordinator {
		private readonly IPresentationReadPort _read;
		private readonly IPresentationCommandPort _commands;
		private readonly PresentationSessionState _session;
		public IOutputSurfacePort OutputSurfacePort { get; }
		public IProgramPresenterPort ProgramPresenter { get; }
		public IPlatformFileInteractionAdapter PlatformFiles { get; }
		public IDisplayIdentifyPort DisplayIdentifyPort { get; }
		private PresentationEnvelope<PresentationReadModel> _latest;
		private ulong _lastAppliedFrame;
		private long _lastAppliedVersion;

		public PresentationReadModel Current => _latest?.Model;
		public PresentationEnvelope<PresentationReadModel> CurrentEnvelope => _latest;
		public PresentationSessionState Session => _session;

		public event Action<PresentationReadModel> ShellApplied;
		public event Action<WorkspaceReadModel> WorkspaceApplied;
		public event Action<PresentationReadModel> PanelsApplied;
		public event Action<PresentationReadModel> NotificationsApplied;

		public PresentationCoordinator(IPresentationReadPort read, IPresentationCommandPort commands, PresentationSessionState session = null, IOutputSurfacePort outputSurfacePort = null, IProgramPresenterPort programPresenter = null, IPlatformFileInteractionAdapter platformFiles = null, IDisplayIdentifyPort displayIdentifyPort = null) {
			_read = read ?? throw new ArgumentNullException(nameof(read));
			_commands = commands ?? throw new ArgumentNullException(nameof(commands));
			_session = session ?? new PresentationSessionState();
			OutputSurfacePort = outputSurfacePort;
			ProgramPresenter = programPresenter;
			PlatformFiles = platformFiles;
			DisplayIdentifyPort = displayIdentifyPort;
		}

		public PresentationApplyReport ApplyLatestReadModels(ulong frameNumber) {
			if (_lastAppliedFrame == frameNumber) return new PresentationApplyReport(false, false, _latest?.ProjectSessionId ?? Guid.Empty, _lastAppliedVersion);
			var candidate = _read.ReadLatest(false);
			if (candidate == null || candidate.Model == null) return new PresentationApplyReport(false, false, Guid.Empty, _lastAppliedVersion);
			var sessionChanged = _latest != null && candidate.ProjectSessionId != _latest.ProjectSessionId;
			if (sessionChanged) {
				_session.ClearProjectScope();
				_latest = null;
				_lastAppliedVersion = 0;
			}
			var gap = _latest != null && candidate.ReadModelVersion > _lastAppliedVersion + 1;
			if (gap) {
				candidate = _read.ReadLatest(true);
				if (candidate == null || candidate.Model == null || candidate.ProjectSessionId != (_latest?.ProjectSessionId ?? candidate.ProjectSessionId))
					return new PresentationApplyReport(false, true, candidate?.ProjectSessionId ?? Guid.Empty, _lastAppliedVersion);
			}
			if (_latest != null && candidate.ProjectSessionId == _latest.ProjectSessionId && candidate.ReadModelVersion <= _lastAppliedVersion)
				return new PresentationApplyReport(false, gap, candidate.ProjectSessionId, _lastAppliedVersion);
			if (_latest == null || sessionChanged) _session.Bind(candidate.ProjectSessionId);
			var previous = _latest?.Model;
			_latest = candidate;
			_lastAppliedVersion = candidate.ReadModelVersion;
			_lastAppliedFrame = frameNumber;
			var forceAllRoutes = sessionChanged || gap || candidate.IsFullSnapshot;
			if (forceAllRoutes || previous == null || !ReferenceEquals(previous.Shell, candidate.Model.Shell))
				ShellApplied?.Invoke(candidate.Model);
			if (forceAllRoutes || previous == null || !ReferenceEquals(previous.Workspace, candidate.Model.Workspace))
				WorkspaceApplied?.Invoke(candidate.Model.Workspace);
			PanelsApplied?.Invoke(candidate.Model);
			if (forceAllRoutes || previous == null || !ReferenceEquals(previous.Diagnostics, candidate.Model.Diagnostics) || !ReferenceEquals(previous.Commands, candidate.Model.Commands) || !ReferenceEquals(previous.Task, candidate.Model.Task))
				NotificationsApplied?.Invoke(candidate.Model);
			return new PresentationApplyReport(true, gap, candidate.ProjectSessionId, candidate.ReadModelVersion);
		}

		public CommandReadModel Submit(string commandId, string targetId = null, Guid interactionId = default(Guid), params KeyValuePairValue[] payload) {
			if (_latest == null) return new CommandReadModel(Guid.Empty, interactionId, PresentationCommandStatus.Rejected, "No project snapshot is bound.");
			var values = new System.Collections.Generic.List<System.Collections.Generic.KeyValuePair<string, string>>();
			foreach (var item in payload ?? new KeyValuePairValue[0]) values.Add(new System.Collections.Generic.KeyValuePair<string, string>(item.Key, item.Value));
			var request = new PresentationCommandRequest(_latest.ProjectSessionId, Guid.NewGuid(), interactionId,
				_latest.DocumentRevision, targetId, commandId, values);
			return _commands.Submit(request);
		}

		public CommandReadModel Submit(string commandId, string targetId, params KeyValuePairValue[] payload)
			=> Submit(commandId, targetId, Guid.Empty, payload);
	}

	public readonly struct KeyValuePairValue {
		public string Key { get; }
		public string Value { get; }
		public KeyValuePairValue(string key, string value) { Key = key ?? string.Empty; Value = value ?? string.Empty; }
	}
}
