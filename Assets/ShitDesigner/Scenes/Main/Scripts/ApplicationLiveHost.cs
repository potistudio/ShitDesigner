using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Input;
using UnityEngine;
using UnityEngine.Rendering;

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
		private readonly LivePatchSlots _patchSlots = new LivePatchSlots();
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
		private ulong _tickFrameNumber;
		private int _selectedPatchSlotIndex;
		private LivePatchRole m_SelectedPatchRole;
		private int m_SelectedMainPatchIndex;
		private int m_SelectedOverlayPatchIndex;

		public ApplicationLiveHostState State { get; private set; } = ApplicationLiveHostState.Cold;
		public LiveUiReadModel ReadModel { get; private set; }
		public LiveParameterQueue ParameterQueue => _parameterQueue;
		public LivePatchSlots PatchSlots => _patchSlots;
		public string LastDiagnostic { get; private set; } = string.Empty;
		public IReadOnlyList<LiveStepSequencer> Sequencers => m_Sequencers;

		private void OnEnable() {
			RenderPipelineManager.beginContextRendering += UseNativeUiOverlayForMultipleMetalDisplays;
		}

		private void OnDisable() {
			RenderPipelineManager.beginContextRendering -= UseNativeUiOverlayForMultipleMetalDisplays;
		}

		public static bool RequiresNativeUiOverlay(GraphicsDeviceType graphicsDeviceType, int displayCount)
			=> graphicsDeviceType == GraphicsDeviceType.Metal && displayCount > 1;

		private static void UseNativeUiOverlayForMultipleMetalDisplays(ScriptableRenderContext context, List<Camera> cameras) {
			var displayCount = Display.displays?.Length ?? 0;
			if (RequiresNativeUiOverlay(SystemInfo.graphicsDeviceType, displayCount))
				SupportedRenderingFeatures.active.rendersUIOverlay = false;
		}

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
				_patches = _runtime.Patches.Select(patch => new LivePatchReadModel(patch.Id, patch.DisplayName,
					mainPatchIds.Contains(patch.Id) ? LivePatchRole.Main : LivePatchRole.Overlay)).ToArray();
				m_MainPatchIds = _patches.Where(patch => patch.Role == LivePatchRole.Main).Select(patch => patch.Id).ToArray();
				m_OverlayPatchIds = _patches.Where(patch => patch.Role == LivePatchRole.Overlay).Select(patch => patch.Id).ToArray();
				m_SelectedPatchRole = m_MainPatchIds.Length > 0 ? LivePatchRole.Main : LivePatchRole.Overlay;
				m_SelectedMainPatchIndex = 0;
				m_SelectedOverlayPatchIndex = 0;
				UpdateOverlayComposition(_runtime.BpmFrame.AdjustedTotalBeats);
				_keyboard = new LiveKeyboardInput(_parameterQueue, _runtime.Patches, slotIndex => { LaunchPatchSlot(slotIndex); }, slotIndex => { ClearPatchSlot(slotIndex); }, MoveCatalogSelection, () => { AssignSelectedCatalogPatch(); }, TapBpm);
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
				UpdateOverlayComposition(_runtime.BpmFrame.AdjustedTotalBeats + deltaSeconds * _runtime.BpmFrame.Bpm / 60d);
				_runtime.Evaluate(deltaSeconds);
				_runtime.SceneUpdate(deltaSeconds);
				var frames = _runtime.Render();
				_runtime.RenderSlotPreviews(_patchSlots.ReadModel, deltaSeconds);
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

		public LivePatchSlotOperationResult AssignPatchToSelectedSlot(string patchId) {
			if (!IsKnownPatch(patchId)) return LivePatchSlotOperationResult.Reject("The requested patch does not exist.");
			SelectCatalogPatch(patchId);
			return _patchSlots.Assign(_selectedPatchSlotIndex, patchId);
		}

		public LivePatchSlotOperationResult SelectPatchSlot(int slotIndex) {
			if (!LivePatchSlots.IsValidSlotIndex(slotIndex)) return LivePatchSlotOperationResult.Reject("The patch slot does not exist.");
			_selectedPatchSlotIndex = slotIndex;
			return LivePatchSlotOperationResult.Accept(slotIndex);
		}

		public LivePatchSlotOperationResult ClearSelectedPatchSlot() {
			return _patchSlots.Clear(_selectedPatchSlotIndex);
		}

		public LivePatchSlotOperationResult ClearPatchSlot(int slotIndex) {
			var selection = SelectPatchSlot(slotIndex);
			return selection.Accepted ? ClearSelectedPatchSlot() : selection;
		}

		public LiveParameterEnqueueResult LaunchSelectedPatchSlot() {
			if (!_patchSlots.TryGetPatchId(_selectedPatchSlotIndex, out var patchId))
				return LiveParameterEnqueueResult.Reject("The selected patch slot is empty.");
			return _runtime != null && _runtime.PreloadedPatchId == patchId
				? _parameterQueue.EnqueueLoadPatch(patchId)
				: _parameterQueue.EnqueueLaunchPatch(patchId);
		}

		public LiveParameterEnqueueResult LaunchPatchSlot(int slotIndex) {
			var selection = SelectPatchSlot(slotIndex);
			return selection.Accepted ? LaunchSelectedPatchSlot() : LiveParameterEnqueueResult.Reject(selection.RejectionReason);
		}

		public void MoveCatalogSelection(int horizontalDirection, int verticalDirection) {
			if (_patches.Length == 0 || (horizontalDirection == 0 && verticalDirection == 0)) return;
			if (verticalDirection < 0 && m_MainPatchIds.Length > 0) {
				m_SelectedPatchRole = LivePatchRole.Main;
				return;
			}
			if (verticalDirection > 0 && m_OverlayPatchIds.Length > 0) {
				m_SelectedPatchRole = LivePatchRole.Overlay;
				return;
			}
			if (horizontalDirection == 0) return;

			if (m_SelectedPatchRole == LivePatchRole.Main)
				m_SelectedMainPatchIndex = MoveWithinRow(m_SelectedMainPatchIndex, horizontalDirection, m_MainPatchIds.Length);
			else
				m_SelectedOverlayPatchIndex = MoveWithinRow(m_SelectedOverlayPatchIndex, horizontalDirection, m_OverlayPatchIds.Length);
		}

		public LivePatchSlotOperationResult AssignSelectedCatalogPatch() {
			var patchId = SelectedCatalogPatchId;
			return string.IsNullOrEmpty(patchId)
				? LivePatchSlotOperationResult.Reject("The patch catalog is empty.")
				: AssignPatchToSelectedSlot(patchId);
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
			return m_Sequencers.First(sequencer => sequencer.Kind == kind).SelectLane(laneIndex);
		}

		public LiveSequencerOperationResult AssignSelectedSequencerPatch(string patchId) {
			if (!m_OverlayPatchIds.Contains(patchId)) return LiveSequencerOperationResult.Reject("Select an overlay scene for this lane.");
			var sequencer = m_Sequencers.FirstOrDefault(item => item.SelectedLaneIndex >= 0);
			return sequencer == null
				? LiveSequencerOperationResult.Reject("Select a sequencer lane first.")
				: sequencer.AssignSelectedLane(patchId);
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

		private void UpdateOverlayComposition(double adjustedTotalBeats) {
			var overlay = m_Sequencers.First(sequencer => sequencer.Kind == LiveSequencerKind.Overlay)
				.CreateReadModel(adjustedTotalBeats);
			_runtime.SetOverlayComposition(overlay);
		}

		private void PublishReadModel(string diagnostic) {
			ReadModel = new LiveUiReadModel(_tickFrameNumber, _patches, _patchSlots.ReadModel, _runtime?.SlotPreviewTextures, _selectedPatchSlotIndex, SelectedCatalogPatchId,
				_runtime?.LoadedPatchId, _runtime?.PreloadedPatchId,
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
				var patchIds = m_SelectedPatchRole == LivePatchRole.Main ? m_MainPatchIds : m_OverlayPatchIds;
				var selectedIndex = m_SelectedPatchRole == LivePatchRole.Main ? m_SelectedMainPatchIndex : m_SelectedOverlayPatchIndex;
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
			if (overlayIndex < 0) return;
			m_SelectedPatchRole = LivePatchRole.Overlay;
			m_SelectedOverlayPatchIndex = overlayIndex;
		}

		private static int MoveWithinRow(int selectedIndex, int direction, int patchCount) {
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
