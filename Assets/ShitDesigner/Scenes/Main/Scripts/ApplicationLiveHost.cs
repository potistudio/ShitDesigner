using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Input;
using UnityEngine;

namespace ShitDesigner.Main {
	public enum ApplicationLiveHostState {
		Cold,
		Running,
		Faulted,
		Offline
	}

	/// <summary>Owns the Main live lifecycle and its single ordered execution tick.</summary>
	[DisallowMultipleComponent]
	[DefaultExecutionOrder(1000)]
	public sealed class ApplicationLiveHost : MonoBehaviour {
		[SerializeField] private LiveGraphBootstrap _graphBootstrap;
		[SerializeField] private MidiInputManager _midiInputManager;
		[SerializeField] private LiveCapabilityMonitor _capabilityMonitor;
		[SerializeField] private LiveExternalDisplayOutput _externalDisplay;
		[SerializeField] private LiveUiController _uiController;
		[SerializeField] private bool _bootOnAwake = true;

		private readonly LiveParameterQueue _parameterQueue = new LiveParameterQueue();
		private readonly LiveBpmTap _bpmTap = new LiveBpmTap();
		private readonly LiveStepSequencer[] m_Sequencers = {
			new LiveStepSequencer(LiveSequencerKind.Overlay, "OVERLAY"),
			new LiveStepSequencer(LiveSequencerKind.Effect, "EFFECT")
		};
		private readonly List<LiveParameterRequest> _pendingRequests = new List<LiveParameterRequest>();
		private readonly List<LiveParameterApplicationResult> _requestResults = new List<LiveParameterApplicationResult>();
		private readonly List<Action> _shutdown = new List<Action>();
		private LiveGraphRuntime _runtime;
		private LiveKeyboardInput _keyboard;
		private LiveMidiInput _midi;
		private string[] _patchIds = Array.Empty<string>();
		private LivePatchReadModel[] _patches = Array.Empty<LivePatchReadModel>();
		private string[] m_MainPatchIds = Array.Empty<string>();
		private string[] m_OverlayPatchIds = Array.Empty<string>();
		private string[] m_EffectPatchIds = Array.Empty<string>();
		private ulong _tickFrameNumber;
		private LivePatchRole m_SelectedPatchRole;
		private int m_SelectedMainPatchIndex;
		private int m_SelectedOverlayPatchIndex;
		private int m_SelectedEffectPatchIndex;

		public ApplicationLiveHostState State { get; private set; } = ApplicationLiveHostState.Cold;
		public LiveUiReadModel ReadModel { get; private set; }
		public LiveParameterQueue ParameterQueue => _parameterQueue;
		public string LastDiagnostic { get; private set; } = string.Empty;
		public IReadOnlyList<LiveStepSequencer> Sequencers => m_Sequencers;

		private void Awake() {
			if (_bootOnAwake) Boot();
		}

		public bool Boot() {
			if (State == ApplicationLiveHostState.Running) return true;
			if (State != ApplicationLiveHostState.Cold && State != ApplicationLiveHostState.Offline) return false;
			_shutdown.Clear();
			LastDiagnostic = string.Empty;
			try {
				if (_graphBootstrap == null || _midiInputManager == null || _capabilityMonitor == null || _externalDisplay == null || _uiController == null)
					throw new InvalidOperationException("ApplicationLiveHost requires graph, MIDI, capability, Display, and UI components.");

				_runtime = _graphBootstrap.CreateRuntime();
				_shutdown.Add(() => { _runtime?.Dispose(); _runtime = null; });
				_patchIds = _runtime.Patches.Select(patch => patch.Id).ToArray();
				var mainPatchIds = new HashSet<string>(_graphBootstrap.MainPatches.Where(patch => patch != null).Select(patch => patch.Id), StringComparer.Ordinal);
				var overlayPatchIds = new HashSet<string>(_graphBootstrap.OverlayPatches.Where(patch => patch != null).Select(patch => patch.Id), StringComparer.Ordinal);
				_patches = _runtime.Patches.Select(patch => new LivePatchReadModel(patch.Id, patch.DisplayName,
					mainPatchIds.Contains(patch.Id) ? LivePatchRole.Main : overlayPatchIds.Contains(patch.Id) ? LivePatchRole.Overlay : LivePatchRole.Effect)).ToArray();
				m_MainPatchIds = _patches.Where(patch => patch.Role == LivePatchRole.Main).Select(patch => patch.Id).ToArray();
				m_OverlayPatchIds = _patches.Where(patch => patch.Role == LivePatchRole.Overlay).Select(patch => patch.Id).ToArray();
				m_EffectPatchIds = _patches.Where(patch => patch.Role == LivePatchRole.Effect).Select(patch => patch.Id).ToArray();
				m_SelectedPatchRole = m_MainPatchIds.Length > 0 ? LivePatchRole.Main
					: m_OverlayPatchIds.Length > 0 ? LivePatchRole.Overlay : LivePatchRole.Effect;
				m_SelectedMainPatchIndex = 0;
				m_SelectedOverlayPatchIndex = 0;
				m_SelectedEffectPatchIndex = 0;
				UpdateOverlayComposition(_runtime.BpmFrame.AdjustedTotalBeats);
				_keyboard = new LiveKeyboardInput(_parameterQueue, _runtime.Patches, laneIndex => { AssignSelectedOverlayPatchToLane(laneIndex); }, MoveCatalogSelection, () => { LaunchSelectedCatalogPatch(); }, TapBpm);
				_midiInputManager.InitializeForHostPolling();
				_shutdown.Add(_midiInputManager.Shutdown);
				_midi = new LiveMidiInput(_midiInputManager, _parameterQueue, _runtime.Patches);
				_shutdown.Add(() => { _midi?.Dispose(); _midi = null; });
				_externalDisplay.Initialize();
				_shutdown.Add(_externalDisplay.Shutdown);
				_capabilityMonitor.Initialize(_midiInputManager, _externalDisplay);
				_shutdown.Add(_capabilityMonitor.Shutdown);
				_uiController.Initialize(this, _externalDisplay);
				_shutdown.Add(_uiController.Shutdown);
				PublishReadModel(string.Empty);
				State = ApplicationLiveHostState.Running;
				return true;
			}
			catch (Exception exception) {
				LastDiagnostic = exception.Message;
				ShutdownStartedComponents();
				State = ApplicationLiveHostState.Faulted;
				Debug.LogError("[ApplicationLiveHost] " + LastDiagnostic, this);
				return false;
			}
		}

		private void LateUpdate() {
			if (State != ApplicationLiveHostState.Running) return;
			unchecked { _tickFrameNumber++; }
			if (_tickFrameNumber == 0) _tickFrameNumber = 1;

			_keyboard.Poll(_runtime.LoadedPatchId);
			_midi.SetSelectedPatch(_runtime.LoadedPatchId);
			_midiInputManager.Poll();
			try {
				var deltaSeconds = Math.Max(0d, Time.unscaledDeltaTime);
				ApplyRequests();
				var overlayComposition = UpdateOverlayComposition(_runtime.BpmFrame.AdjustedTotalBeats + deltaSeconds * _runtime.BpmFrame.Bpm / 60d);
				_runtime.Evaluate(deltaSeconds);
				_runtime.SceneUpdate(deltaSeconds);
				var frames = _runtime.Render();
				_runtime.RenderOverlayPreviews(overlayComposition.LanePatchIds, deltaSeconds);
				_externalDisplay.Present(frames);
				LastDiagnostic = string.Empty;
				PublishReadModel(string.Empty);
			}
			catch (Exception exception) {
				LastDiagnostic = exception.Message;
				PublishReadModel(LastDiagnostic);
			}
		}

		public void Shutdown() {
			if (State == ApplicationLiveHostState.Cold || State == ApplicationLiveHostState.Offline) return;
			ShutdownStartedComponents();
			State = ApplicationLiveHostState.Offline;
		}

		public void MoveCatalogSelection(int horizontalDirection, int verticalDirection) {
			if (_patches.Length == 0 || (horizontalDirection == 0 && verticalDirection == 0)) return;
			if (horizontalDirection != 0) {
				var nextRoleIndex = Mathf.Clamp((int)m_SelectedPatchRole + Math.Sign(horizontalDirection), (int)LivePatchRole.Main, (int)LivePatchRole.Effect);
				m_SelectedPatchRole = (LivePatchRole)nextRoleIndex;
			}
			if (verticalDirection == 0) return;

			SetSelectedPatchIndex(m_SelectedPatchRole,
				MoveWithinList(GetSelectedPatchIndex(m_SelectedPatchRole), verticalDirection, GetPatchIds(m_SelectedPatchRole).Length));
		}

		public void SelectCatalogRole(LivePatchRole role) {
			if (role < LivePatchRole.Main || role > LivePatchRole.Effect) throw new ArgumentOutOfRangeException(nameof(role));
			m_SelectedPatchRole = role;
		}

		public LiveParameterEnqueueResult LaunchSelectedCatalogPatch() {
			var patchId = SelectedCatalogPatchId;
			return string.IsNullOrEmpty(patchId)
				? LiveParameterEnqueueResult.Reject("The patch catalog is empty.")
				: _parameterQueue.EnqueueLaunchPatch(patchId);
		}

		public LiveParameterEnqueueResult LaunchCatalogPatch(string patchId) {
			if (!IsKnownPatch(patchId)) return LiveParameterEnqueueResult.Reject("The requested patch does not exist.");
			SelectCatalogPatch(patchId);
			return _parameterQueue.EnqueueLaunchPatch(patchId);
		}

		public void TapBpm(double time) {
			if (!_bpmTap.TryTap(time, out var bpm) || _runtime == null) return;
			_parameterQueue.EnqueueSetBpm(bpm);
		}

		public LiveSequencerOperationResult CycleSequencerCellMode(LiveSequencerKind kind, int laneIndex, int stepIndex) {
			var sequencer = m_Sequencers.FirstOrDefault(item => item.Kind == kind);
			return sequencer == null
				? LiveSequencerOperationResult.Reject("The requested sequencer does not exist.")
				: sequencer.CycleCellMode(laneIndex, stepIndex);
		}

		public bool IsSelectingSequencerLane => m_Sequencers.Any(sequencer => sequencer.SelectedLaneIndex >= 0);

		public LiveSequencerOperationResult SelectSequencerLane(LiveSequencerKind kind, int laneIndex) {
			if (kind != LiveSequencerKind.Overlay) return LiveSequencerOperationResult.Reject("Scene assignment is available for the overlay sequencer.");
			var result = m_Sequencers.First(sequencer => sequencer.Kind == kind).SelectLane(laneIndex);
			if (result.Accepted) m_SelectedPatchRole = LivePatchRole.Overlay;
			return result;
		}

		public LiveSequencerOperationResult AssignSelectedSequencerPatch(string patchId) {
			if (!m_OverlayPatchIds.Contains(patchId)) return LiveSequencerOperationResult.Reject("Select an overlay scene for this lane.");
			var sequencer = m_Sequencers.FirstOrDefault(item => item.SelectedLaneIndex >= 0);
			return sequencer == null
				? LiveSequencerOperationResult.Reject("Select a sequencer lane first.")
				: sequencer.AssignSelectedLane(patchId);
		}

		public LiveSequencerOperationResult AssignSelectedOverlayPatchToLane(int laneIndex) {
			var patchId = m_SelectedPatchRole == LivePatchRole.Overlay ? SelectedCatalogPatchId : string.Empty;
			return string.IsNullOrEmpty(patchId)
				? LiveSequencerOperationResult.Reject("Select an overlay scene first.")
				: m_Sequencers.First(sequencer => sequencer.Kind == LiveSequencerKind.Overlay).AssignLane(laneIndex, patchId);
		}

		private void ApplyRequests(bool clearResults = true) {
			_pendingRequests.Clear();
			if (clearResults) _requestResults.Clear();
			_parameterQueue.Drain(_pendingRequests);
			foreach (var request in _pendingRequests) {
				try { _requestResults.Add(_runtime.Apply(request)); }
				catch (Exception exception) { _requestResults.Add(new LiveParameterApplicationResult(request.SequenceNumber, false, exception.Message)); }
			}
		}

		private LiveSequencerReadModel UpdateOverlayComposition(double adjustedTotalBeats) {
			var overlay = m_Sequencers.First(sequencer => sequencer.Kind == LiveSequencerKind.Overlay)
				.CreateReadModel(adjustedTotalBeats);
			_runtime.SetOverlayComposition(overlay);
			return overlay;
		}

		private void PublishReadModel(string diagnostic) {
			ReadModel = new LiveUiReadModel(_tickFrameNumber, _patches, m_SelectedPatchRole, SelectedCatalogPatchId, _runtime?.LoadedPatchId, _runtime?.OverlayPreviewFrames,
				_runtime?.BpmDefinition ?? default, _runtime?.GetLoadedPatchParameterDefinitions(), CreateSequencerReadModels(), _runtime?.CurrentFrames ?? default(LiveProgramFrames), _externalDisplay,
				_capabilityMonitor != null ? _capabilityMonitor.Snapshot : default(LiveCapabilitySnapshot), diagnostic,
				_requestResults.ToArray());
		}

		private LiveSequencerReadModel[] CreateSequencerReadModels() {
			var adjustedTotalBeats = _runtime?.BpmFrame.AdjustedTotalBeats ?? 0d;
			return m_Sequencers.Select(sequencer => sequencer.CreateReadModel(adjustedTotalBeats)).ToArray();
		}

		private bool IsKnownPatch(string patchId) => !string.IsNullOrWhiteSpace(patchId) && _patchIds.Contains(patchId);

		private string SelectedCatalogPatchId {
			get {
				var patchIds = GetPatchIds(m_SelectedPatchRole);
				var selectedIndex = GetSelectedPatchIndex(m_SelectedPatchRole);
				return selectedIndex >= 0 && selectedIndex < patchIds.Length ? patchIds[selectedIndex] : string.Empty;
			}
		}

		private void SelectCatalogPatch(string patchId) {
			var mainIndex = Array.IndexOf(m_MainPatchIds, patchId);
			if (mainIndex >= 0) {
				m_SelectedPatchRole = LivePatchRole.Main;
				m_SelectedMainPatchIndex = mainIndex;
				return;
			}

			var overlayIndex = Array.IndexOf(m_OverlayPatchIds, patchId);
			if (overlayIndex >= 0) {
				m_SelectedPatchRole = LivePatchRole.Overlay;
				m_SelectedOverlayPatchIndex = overlayIndex;
				return;
			}

			var effectIndex = Array.IndexOf(m_EffectPatchIds, patchId);
			if (effectIndex < 0) return;
			m_SelectedPatchRole = LivePatchRole.Effect;
			m_SelectedEffectPatchIndex = effectIndex;
		}

		private string[] GetPatchIds(LivePatchRole role) {
			switch (role) {
				case LivePatchRole.Main: return m_MainPatchIds;
				case LivePatchRole.Overlay: return m_OverlayPatchIds;
				case LivePatchRole.Effect: return m_EffectPatchIds;
				default: throw new ArgumentOutOfRangeException(nameof(role), role, null);
			}
		}

		private int GetSelectedPatchIndex(LivePatchRole role) {
			switch (role) {
				case LivePatchRole.Main: return m_SelectedMainPatchIndex;
				case LivePatchRole.Overlay: return m_SelectedOverlayPatchIndex;
				case LivePatchRole.Effect: return m_SelectedEffectPatchIndex;
				default: throw new ArgumentOutOfRangeException(nameof(role), role, null);
			}
		}

		private void SetSelectedPatchIndex(LivePatchRole role, int index) {
			switch (role) {
				case LivePatchRole.Main: m_SelectedMainPatchIndex = index; break;
				case LivePatchRole.Overlay: m_SelectedOverlayPatchIndex = index; break;
				case LivePatchRole.Effect: m_SelectedEffectPatchIndex = index; break;
				default: throw new ArgumentOutOfRangeException(nameof(role), role, null);
			}
		}

		private static int MoveWithinList(int selectedIndex, int direction, int patchCount) {
			if (patchCount <= 0) return 0;
			return Mathf.Clamp(selectedIndex + Math.Sign(direction), 0, patchCount - 1);
		}

		private void ShutdownStartedComponents() {
			for (var index = _shutdown.Count - 1; index >= 0; index--) {
				try { _shutdown[index](); }
				catch (Exception exception) { Debug.LogException(exception, this); }
			}
			_shutdown.Clear();
			_keyboard = null;
		}

		private void OnDestroy() => Shutdown();
	}
}
