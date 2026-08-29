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
		private readonly LivePatchSlots _patchSlots = new LivePatchSlots();
		private readonly LiveBpmTap _bpmTap = new LiveBpmTap();
		private readonly List<LiveParameterRequest> _pendingRequests = new List<LiveParameterRequest>();
		private readonly List<LiveParameterApplicationResult> _requestResults = new List<LiveParameterApplicationResult>();
		private readonly List<Action> _shutdown = new List<Action>();
		private LiveGraphRuntime _runtime;
		private LiveKeyboardInput _keyboard;
		private LiveMidiInput _midi;
		private string[] _patchIds = Array.Empty<string>();
		private LivePatchReadModel[] _patches = Array.Empty<LivePatchReadModel>();
		private ulong _tickFrameNumber;
		private int _selectedPatchSlotIndex;
		private int _selectedCatalogPatchIndex;

		public ApplicationLiveHostState State { get; private set; } = ApplicationLiveHostState.Cold;
		public LiveUiReadModel ReadModel { get; private set; }
		public LiveParameterQueue ParameterQueue => _parameterQueue;
		public LivePatchSlots PatchSlots => _patchSlots;
		public string LastDiagnostic { get; private set; } = string.Empty;

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
				_keyboard = new LiveKeyboardInput(_parameterQueue, _runtime.Patches, slotIndex => { LaunchPatchSlot(slotIndex); }, slotIndex => { ClearPatchSlot(slotIndex); }, MoveCatalogSelection, () => { QueueSelectedCatalogPatch(); }, TapBpm);
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
			ApplyRequests();
			try {
				var deltaSeconds = Math.Max(0d, Time.unscaledDeltaTime);
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

		public LivePatchSlotOperationResult QueuePatch(string patchId) {
			if (!IsKnownPatch(patchId)) return LivePatchSlotOperationResult.Reject("The requested patch does not exist.");
			_selectedCatalogPatchIndex = Array.IndexOf(_patchIds, patchId);
			var result = _patchSlots.Queue(patchId);
			if (result.Accepted) _selectedPatchSlotIndex = result.SlotIndex;
			return result;
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

		public LiveParameterEnqueueResult CueSelectedPatchSlot() {
			return _patchSlots.TryGetPatchId(_selectedPatchSlotIndex, out var patchId)
				? _parameterQueue.EnqueuePreloadPatch(patchId)
				: LiveParameterEnqueueResult.Reject("The selected patch slot is empty.");
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

		public void MoveCatalogSelection(int direction) {
			if (_patchIds.Length == 0 || direction == 0) return;
			_selectedCatalogPatchIndex = Mathf.Clamp(_selectedCatalogPatchIndex + Math.Sign(direction), 0, _patchIds.Length - 1);
		}

		public LivePatchSlotOperationResult QueueSelectedCatalogPatch() {
			return _patchIds.Length == 0
				? LivePatchSlotOperationResult.Reject("The patch catalog is empty.")
				: QueuePatch(_patchIds[_selectedCatalogPatchIndex]);
		}

		public void TapBpm(double time) {
			if (!_bpmTap.TryTap(time, out var bpm) || _runtime == null) return;
			_parameterQueue.EnqueueSetBpm(bpm);
		}

		private void ApplyRequests() {
			_pendingRequests.Clear();
			_requestResults.Clear();
			_parameterQueue.Drain(_pendingRequests);
			foreach (var request in _pendingRequests) {
				try { _requestResults.Add(_runtime.Apply(request)); }
				catch (Exception exception) { _requestResults.Add(new LiveParameterApplicationResult(request.SequenceNumber, false, exception.Message)); }
			}
		}

		private void PublishReadModel(string diagnostic) {
			ReadModel = new LiveUiReadModel(_tickFrameNumber, _patches, _patchSlots.ReadModel, _runtime?.SlotPreviewTextures, _selectedPatchSlotIndex, SelectedCatalogPatchId,
				_runtime?.LoadedPatchId, _runtime?.PreloadedPatchId,
				_runtime?.BpmDefinition ?? default, _runtime?.GetLoadedPatchParameterDefinitions(), _runtime?.CurrentFrames ?? default(LiveProgramFrames), _externalDisplay,
				_capabilityMonitor != null ? _capabilityMonitor.Snapshot : default(LiveCapabilitySnapshot), diagnostic,
				_requestResults.ToArray());
		}

		private bool IsKnownPatch(string patchId) => !string.IsNullOrWhiteSpace(patchId) && _patchIds.Contains(patchId);
		private string SelectedCatalogPatchId => _selectedCatalogPatchIndex >= 0 && _selectedCatalogPatchIndex < _patchIds.Length ? _patchIds[_selectedCatalogPatchIndex] : string.Empty;

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
