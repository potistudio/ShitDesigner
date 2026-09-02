using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Input;
using UnityEngine;
using UnityEngine.InputSystem;

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
		public const int MainCueCount = LiveGraphRuntime.MainCueCount;
		private const int FollowOverlaySequencer = -1;
		private const int NoPianoOverlayTake = -2;
		private const float MaximumUnityTimeScale = 100f;
		private static readonly string[] m_EmptyMainCuePatchIds = new string[MainCueCount];

		[SerializeField] private LiveGraphBootstrap _graphBootstrap;
		[SerializeField] private MidiInputManager _midiInputManager;
		[SerializeField] private LiveCapabilityMonitor _capabilityMonitor;
		[SerializeField] private LiveExternalDisplayOutput _externalDisplay;
		[SerializeField] private LiveUiController _uiController;
		[SerializeField] private bool _bootOnAwake = true;

		[Header("Main Cue MIDI")]
		[SerializeField, Range(1, 16)] public int m_MainCueFaderChannel = 16;
		[SerializeField, Range(0, 127)] public int m_MainCueFaderControlNumber = 5;
		[SerializeField, Tooltip("Maps latched Main Cue fader travel to alternate Cue opacity.")]
		public AnimationCurve m_MainCueFaderCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
		[SerializeField, Range(1, 16)] private int m_SceneTimeEncoderChannel = 16;
		[SerializeField, Range(0, 127)] private int m_SceneTimeEncoderControlNumber = 77;
		[SerializeField, Min(.01f)] private float m_SceneTimeJogSpeedPerStep = 1f;
		[SerializeField, Range(.01f, 8f)] private float m_SceneTimeJogMaximumSpeedOffset = 4f;
		[SerializeField, Min(0f)] private float m_ThumbnailTimeOffsetSeconds = .05f;

		[Header("Instant Effect Keyboard")]
		[SerializeField, Tooltip("Maps each Instant Effect slot to a keyboard key. None leaves the slot available through its on-screen control.")]
		private Key[] m_InstantEffectKeys = new Key[ShitDesigner.Runtime.InstantEffectTriggerContract.TriggerCount];

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
		private LiveEffectNodeReadModel[] m_EffectNodes = Array.Empty<LiveEffectNodeReadModel>();
		private string[] m_MainPatchIds = Array.Empty<string>();
		private string[] m_OverlayPatchIds = Array.Empty<string>();
		private string[] m_EffectNodeTypeIds = Array.Empty<string>();
		private ulong _tickFrameNumber;
		private LiveCatalogRole m_SelectedCatalogRole;
		private int m_SelectedMainPatchIndex;
		private int m_SelectedOverlayPatchIndex;
		private int m_SelectedEffectNodeIndex;
		private readonly string[] m_InstantEffectTypeIds = new string[ShitDesigner.Runtime.InstantEffectTriggerContract.TriggerCount];
		private readonly LiveBeatQuantizedTriggerQueue m_InstantEffectTriggerQueue = new LiveBeatQuantizedTriggerQueue();
		private readonly LiveBeatEffectGate m_InstantEffectGate = new LiveBeatEffectGate();
		private IReadOnlyList<int> m_FiredInstantEffectTriggers = Array.Empty<int>();
		private int m_LiveParameterCueIndex = -1;
		private bool m_IsEditMode;
		private string m_OpenEffectCategory = string.Empty;
		private bool m_IsEffectCategorySelected;
		private string m_SelectedEffectCategory = string.Empty;
		private string m_PianoReturnMainPatchId = string.Empty;
		private readonly int[] m_OverlayTakeOverrides = new int[LiveStepSequencer.OverlayLaneCount];
		private readonly int[] m_PianoReturnOverlayTakeOverrides = new int[LiveStepSequencer.OverlayLaneCount];
		private float m_BaseUnityTimeScale = 1f;
		private bool m_OwnsUnityTimeScale;
		private bool m_RebuildRuntimeForProgramWidth;

		public ApplicationLiveHostState State { get; private set; } = ApplicationLiveHostState.Cold;
		public LiveUiReadModel ReadModel { get; private set; }
		public LiveParameterQueue ParameterQueue => _parameterQueue;
		public IReadOnlyList<string> MainCuePatchIds => _runtime?.MainCuePatchIds ?? m_EmptyMainCuePatchIds;
		public int ActiveMainCueIndex => _runtime?.ActiveMainCueIndex ?? -1;
		public string LastDiagnostic { get; private set; } = string.Empty;
		public IReadOnlyList<LiveStepSequencer> Sequencers => m_Sequencers;
		public IReadOnlyList<Key> InstantEffectKeys => m_InstantEffectKeys;
		public bool IsEditMode => m_IsEditMode;
		public event Action<IReadOnlyList<int>> InstantEffectTriggersFired;

		private void Awake() {
			if (_bootOnAwake) Boot();
		}

		private void OnValidate() {
			if (m_InstantEffectKeys == null || m_InstantEffectKeys.Length != ShitDesigner.Runtime.InstantEffectTriggerContract.TriggerCount)
				Array.Resize(ref m_InstantEffectKeys, ShitDesigner.Runtime.InstantEffectTriggerContract.TriggerCount);
		}

		public bool Boot() {
			if (State == ApplicationLiveHostState.Running) return true;
			if (State != ApplicationLiveHostState.Cold && State != ApplicationLiveHostState.Offline) return false;
			_shutdown.Clear();
			LastDiagnostic = string.Empty;
			try {
				if (_graphBootstrap == null || _midiInputManager == null || _capabilityMonitor == null || _externalDisplay == null || _uiController == null)
					throw new InvalidOperationException("ApplicationLiveHost requires graph, MIDI, capability, Display, and UI components.");

				m_BaseUnityTimeScale = Time.timeScale;
				m_OwnsUnityTimeScale = true;
				_shutdown.Add(RestoreUnityTimeScale);
				_runtime = _graphBootstrap.CreateRuntime();
				_runtime.ConfigureMainCueFaderCurve(m_MainCueFaderCurve);
				_runtime.ConfigureSceneTimeJog(m_SceneTimeJogMaximumSpeedOffset);
				_shutdown.Add(() => { _runtime?.Dispose(); _runtime = null; });
				_patchIds = _runtime.Patches.Select(patch => patch.Id).ToArray();
				var mainPatchIds = new HashSet<string>(_graphBootstrap.MainPatches.Where(patch => patch != null).Select(patch => patch.Id), StringComparer.Ordinal);
				_patches = _runtime.Patches.Select(patch => new LivePatchReadModel(patch.Id, patch.DisplayName,
					mainPatchIds.Contains(patch.Id) ? LivePatchRole.Main : LivePatchRole.Overlay)).ToArray();
				m_MainPatchIds = _patches.Where(patch => patch.Role == LivePatchRole.Main).Select(patch => patch.Id).ToArray();
				m_OverlayPatchIds = _patches.Where(patch => patch.Role == LivePatchRole.Overlay).Select(patch => patch.Id).ToArray();
				m_EffectNodes = _graphBootstrap.EffectNodes.Select(entry => new LiveEffectNodeReadModel(
					entry.TypeId.Value, entry.DisplayName, entry.Category)).ToArray();
				m_EffectNodeTypeIds = m_EffectNodes.Select(node => node.TypeId).ToArray();
				m_SelectedCatalogRole = m_MainPatchIds.Length > 0 ? LiveCatalogRole.Main
					: m_OverlayPatchIds.Length > 0 ? LiveCatalogRole.Overlay : LiveCatalogRole.Effect;
				m_SelectedMainPatchIndex = 0;
				m_SelectedOverlayPatchIndex = 0;
				m_SelectedEffectNodeIndex = 0;
				m_OpenEffectCategory = m_EffectNodes.Length > 0 ? m_EffectNodes[0].Category : string.Empty;
				m_SelectedEffectCategory = m_OpenEffectCategory;
				m_IsEffectCategorySelected = false;
				for (var laneIndex = 0; laneIndex < LiveStepSequencer.OverlayLaneCount; laneIndex++) {
					m_OverlayTakeOverrides[laneIndex] = FollowOverlaySequencer;
					m_PianoReturnOverlayTakeOverrides[laneIndex] = NoPianoOverlayTake;
				}
				UpdateOverlayComposition(_runtime.BpmFrame.AdjustedTotalBeats);
				m_IsEditMode = false;
				m_PianoReturnMainPatchId = string.Empty;
				ShitDesigner.Runtime.InstantEffectInputMode.SetEditing(false);
				_keyboard = new LiveKeyboardInput(_parameterQueue, _runtime.Patches, BeginPianoOverlayTake, MoveCatalogSelection, () => { LaunchSelectedCatalogPatch(); }, TapBpm,
					ToggleEditMode, cueIndex => { AssignSelectedEffectToCue(cueIndex); }, () => m_IsEditMode, QueueInstantEffectTrigger,
					cueIndex => { FocusInstantEffectParameters(cueIndex); }, ToggleSelectedEffectCategory, BeginPianoMainCueSwitch,
					EndPianoMainCueSwitch, CompleteMainCueSwitch, EndPianoOverlayTake, TurnOnOverlaySequencerStep,
					delta => {
						LiveGraphRuntime.AdjustProgramWidth(delta);
						m_RebuildRuntimeForProgramWidth = true;
					}, m_InstantEffectKeys);
				_midiInputManager.InitializeForHostPolling();
				_midiInputManager.ConfigureLaunchControlXl3RelativeEncoder(m_SceneTimeEncoderChannel, m_SceneTimeEncoderControlNumber);
				_shutdown.Add(_midiInputManager.Shutdown);
				_midi = new LiveMidiInput(_midiInputManager, _parameterQueue, _runtime.Patches,
					m_MainCueFaderChannel, m_MainCueFaderControlNumber, m_SceneTimeEncoderChannel,
					m_SceneTimeEncoderControlNumber, m_SceneTimeJogSpeedPerStep);
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
			if (m_RebuildRuntimeForProgramWidth) {
				RebuildRuntimeForProgramWidth();
				return;
			}
			_midi.SetSelectedPatch(_runtime.LoadedPatchId);
			_midiInputManager.Poll();
			try {
				var deltaSeconds = Math.Max(0d, Time.unscaledDeltaTime);
				ApplyRequests();
				var projectedBeatPosition = _runtime.BpmFrame.AdjustedTotalBeats + deltaSeconds * _runtime.BpmFrame.Bpm / 60d;
				var firedInstantEffectTriggers = m_InstantEffectTriggerQueue.DrainDue(projectedBeatPosition);
				if (firedInstantEffectTriggers.Count > 0) InstantEffectTriggersFired?.Invoke(firedInstantEffectTriggers);
				m_InstantEffectGate.Activate(firedInstantEffectTriggers, projectedBeatPosition);
				m_FiredInstantEffectTriggers = m_InstantEffectGate.GetActive(projectedBeatPosition);
				var overlayComposition = UpdateOverlayComposition(projectedBeatPosition);
				_runtime.Evaluate(deltaSeconds);
				ApplyUnityTimeScale(_runtime.GraphTimeScale);
				_runtime.SceneUpdate();
				var frames = _runtime.Render(m_FiredInstantEffectTriggers);
				_runtime.RenderPreviews(overlayComposition.LanePatchIds, _runtime.MainCuePatchIds, deltaSeconds,
					m_ThumbnailTimeOffsetSeconds);
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
			m_IsEditMode = false;
			ShitDesigner.Runtime.InstantEffectInputMode.SetEditing(false);
			State = ApplicationLiveHostState.Offline;
		}

		private void RebuildRuntimeForProgramWidth() {
			m_RebuildRuntimeForProgramWidth = false;
			var replacement = _graphBootstrap.CreateRuntime();
			replacement.ConfigureMainCueFaderCurve(m_MainCueFaderCurve);
			replacement.ConfigureSceneTimeJog(m_SceneTimeJogMaximumSpeedOffset);
			var previous = _runtime;
			_runtime = replacement;
			previous.Dispose();
			UpdateOverlayComposition(_runtime.BpmFrame.AdjustedTotalBeats);
			PublishReadModel(string.Empty);
		}

		public void ToggleEditMode() {
			m_IsEditMode = !m_IsEditMode;
			ShitDesigner.Runtime.InstantEffectInputMode.SetEditing(m_IsEditMode);
			if (m_IsEditMode) {
				m_SelectedCatalogRole = LiveCatalogRole.Effect;
				OpenSelectedEffectCategory();
				m_SelectedEffectCategory = m_OpenEffectCategory;
				m_IsEffectCategorySelected = !string.IsNullOrEmpty(m_SelectedEffectCategory);
			}
			else {
				m_IsEffectCategorySelected = false;
			}
		}

		public bool AssignSelectedEffectToCue(int cueIndex) {
			if (!m_IsEditMode || cueIndex < 0 || cueIndex >= m_InstantEffectTypeIds.Length || m_SelectedCatalogRole != LiveCatalogRole.Effect || m_IsEffectCategorySelected)
				return false;
			var typeId = SelectedCatalogItemId;
			if (string.IsNullOrEmpty(typeId)) return false;
			if (!_runtime.TryAssignInstantEffect(cueIndex, typeId, out var rejectionReason)) {
				LastDiagnostic = rejectionReason;
				return false;
			}
			m_InstantEffectTypeIds[cueIndex] = typeId;
			m_LiveParameterCueIndex = cueIndex;
			return true;
		}

		public void QueueInstantEffectTrigger(int triggerNumber) {
			if (State != ApplicationLiveHostState.Running || m_IsEditMode || _runtime == null) return;
			ShitDesigner.Runtime.InstantEffectTriggerContract.Validate(triggerNumber);
			if (string.IsNullOrEmpty(m_InstantEffectTypeIds[triggerNumber - 1])) return;
			m_InstantEffectTriggerQueue.Enqueue(triggerNumber, _runtime.BpmFrame.AdjustedTotalBeats);
		}

		public bool FocusInstantEffectParameters(int cueIndex) {
			if (cueIndex < 0 || cueIndex >= m_InstantEffectTypeIds.Length || string.IsNullOrEmpty(m_InstantEffectTypeIds[cueIndex])) return false;
			m_LiveParameterCueIndex = cueIndex;
			return true;
		}

		public void MoveCatalogSelection(int horizontalDirection, int verticalDirection) {
			if (_patches.Length == 0 || (horizontalDirection == 0 && verticalDirection == 0)) return;
			if (m_IsEditMode) {
				m_SelectedCatalogRole = LiveCatalogRole.Effect;
				horizontalDirection = 0;
				if (verticalDirection != 0) MoveEffectTreeSelection(verticalDirection);
				return;
			}
			if (horizontalDirection != 0) {
				var nextRoleIndex = Mathf.Clamp((int)m_SelectedCatalogRole + Math.Sign(horizontalDirection), (int)LiveCatalogRole.Main, (int)LiveCatalogRole.Effect);
				m_SelectedCatalogRole = (LiveCatalogRole)nextRoleIndex;
			}
			if (verticalDirection == 0) return;

			SetSelectedCatalogIndex(m_SelectedCatalogRole,
				MoveWithinList(GetSelectedCatalogIndex(m_SelectedCatalogRole), verticalDirection, GetCatalogItemIds(m_SelectedCatalogRole).Length));
			if (m_SelectedCatalogRole == LiveCatalogRole.Effect) OpenSelectedEffectCategory();
		}

		public void SelectCatalogRole(LiveCatalogRole role) {
			if (role < LiveCatalogRole.Main || role > LiveCatalogRole.Effect) throw new ArgumentOutOfRangeException(nameof(role));
			if (m_IsEditMode && role != LiveCatalogRole.Effect) return;
			m_SelectedCatalogRole = role;
			if (role == LiveCatalogRole.Effect) {
				m_IsEffectCategorySelected = false;
				OpenSelectedEffectCategory();
			}
		}

		public LiveParameterEnqueueResult LaunchSelectedCatalogPatch() {
			if (m_SelectedCatalogRole == LiveCatalogRole.Effect)
				return LiveParameterEnqueueResult.Reject("FX nodes must be wired into a graph before they can be triggered.");
			var patchId = SelectedCatalogItemId;
			return string.IsNullOrEmpty(patchId)
				? LiveParameterEnqueueResult.Reject("The selected patch catalog is empty.")
				: _parameterQueue.EnqueueLaunchPatch(patchId);
		}

		public LiveParameterEnqueueResult LaunchCatalogPatch(string patchId) {
			if (!IsKnownPatch(patchId)) return LiveParameterEnqueueResult.Reject("The requested patch does not exist.");
			SelectCatalogPatch(patchId);
			return _parameterQueue.EnqueueLaunchPatch(patchId);
		}

		public bool SelectCatalogPatch(string patchId) {
			var mainIndex = Array.IndexOf(m_MainPatchIds, patchId);
			if (mainIndex >= 0) {
				m_SelectedCatalogRole = LiveCatalogRole.Main;
				m_SelectedMainPatchIndex = mainIndex;
				return true;
			}

			var overlayIndex = Array.IndexOf(m_OverlayPatchIds, patchId);
			if (overlayIndex < 0) return false;
			m_SelectedCatalogRole = LiveCatalogRole.Overlay;
			m_SelectedOverlayPatchIndex = overlayIndex;
			return true;
		}

		public bool AssignMainPatchToCue(int cueIndex, string patchId) {
			if (_runtime == null || cueIndex < 0 || cueIndex >= MainCueCount || cueIndex == _runtime.ActiveMainCueIndex || !m_MainPatchIds.Contains(patchId))
				return false;
			return _parameterQueue.EnqueuePreloadPatch(patchId).Accepted;
		}

		private void BeginPianoMainCueSwitch() {
			if (_runtime == null || !string.IsNullOrEmpty(m_PianoReturnMainPatchId)) return;
			var targetPatchId = AlternateMainCuePatchId(_runtime.LoadedPatchId);
			if (string.IsNullOrEmpty(targetPatchId)) return;
			m_PianoReturnMainPatchId = _runtime.LoadedPatchId;
			_parameterQueue.EnqueueLoadPatch(targetPatchId);
		}

		private void EndPianoMainCueSwitch() {
			var returnPatchId = m_PianoReturnMainPatchId;
			m_PianoReturnMainPatchId = string.Empty;
			if (!string.IsNullOrEmpty(returnPatchId)) _parameterQueue.EnqueueLoadPatch(returnPatchId);
		}

		private void CompleteMainCueSwitch() {
			m_PianoReturnMainPatchId = string.Empty;
			_parameterQueue.EnqueueToggleMainCue();
		}

		private void BeginPianoOverlayTake(int laneIndex) {
			if (!CanTakeOverlayLane(laneIndex) || m_PianoReturnOverlayTakeOverrides[laneIndex] != NoPianoOverlayTake) return;
			m_PianoReturnOverlayTakeOverrides[laneIndex] = m_OverlayTakeOverrides[laneIndex];
			m_OverlayTakeOverrides[laneIndex] = IsOverlayLaneTaken(laneIndex) ? 0 : 1;
		}

		private void EndPianoOverlayTake(int laneIndex) {
			if (laneIndex < 0 || laneIndex >= m_PianoReturnOverlayTakeOverrides.Length) return;
			var returnOverride = m_PianoReturnOverlayTakeOverrides[laneIndex];
			if (returnOverride == NoPianoOverlayTake) return;
			m_PianoReturnOverlayTakeOverrides[laneIndex] = NoPianoOverlayTake;
			m_OverlayTakeOverrides[laneIndex] = returnOverride;
		}

		private void TurnOnOverlaySequencerStep(int laneIndex) {
			if (!CanTakeOverlayLane(laneIndex)) return;
			var adjustedTotalBeats = _runtime?.BpmFrame.AdjustedTotalBeats ?? 0d;
			var overlay = m_Sequencers.First(sequencer => sequencer.Kind == LiveSequencerKind.Overlay);
			var currentStep = overlay.CreateReadModel(adjustedTotalBeats).CurrentStep;
			overlay.TurnOnCell(laneIndex, currentStep);
		}

		private bool CanTakeOverlayLane(int laneIndex) {
			if (laneIndex < 0 || laneIndex >= LiveStepSequencer.OverlayLaneCount) return false;
			var overlay = m_Sequencers.First(sequencer => sequencer.Kind == LiveSequencerKind.Overlay).CreateReadModel(0d);
			return overlay.LanePatchIds.Count > laneIndex && !string.IsNullOrEmpty(overlay.LanePatchIds[laneIndex]);
		}

		private bool IsOverlayLaneTaken(int laneIndex) {
			var adjustedTotalBeats = _runtime?.BpmFrame.AdjustedTotalBeats ?? 0d;
			var overlay = m_Sequencers.First(sequencer => sequencer.Kind == LiveSequencerKind.Overlay)
				.CreateReadModel(adjustedTotalBeats, m_OverlayTakeOverrides);
			return overlay.IsActive(laneIndex, overlay.CurrentStep);
		}

		private string AlternateMainCuePatchId(string activePatchId)
			=> MainCuePatchIds.FirstOrDefault(patchId => !string.IsNullOrEmpty(patchId) && patchId != activePatchId) ?? string.Empty;

		public void SelectEffectNode(string typeId) {
			var effectIndex = Array.IndexOf(m_EffectNodeTypeIds, typeId);
			if (effectIndex < 0) return;
			m_SelectedCatalogRole = LiveCatalogRole.Effect;
			m_SelectedEffectNodeIndex = effectIndex;
			m_SelectedEffectCategory = m_EffectNodes[effectIndex].Category;
			m_IsEffectCategorySelected = false;
			OpenSelectedEffectCategory();
		}

		public void ToggleEffectCategory(string category) {
			if (string.IsNullOrEmpty(category)) return;
			var effectIndex = Array.FindIndex(m_EffectNodes, effect => string.Equals(effect.Category, category, StringComparison.Ordinal));
			if (effectIndex < 0) return;
			m_SelectedCatalogRole = LiveCatalogRole.Effect;
			m_SelectedEffectCategory = category;
			m_IsEffectCategorySelected = true;
			if (string.Equals(m_OpenEffectCategory, category, StringComparison.Ordinal)) {
				m_OpenEffectCategory = string.Empty;
				return;
			}
			m_OpenEffectCategory = category;
		}

		public void ToggleSelectedEffectCategory() {
			if (!m_IsEditMode || !m_IsEffectCategorySelected || string.IsNullOrEmpty(m_SelectedEffectCategory)) return;
			ToggleEffectCategory(m_SelectedEffectCategory);
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

		public LiveSequencerOperationResult ToggleOverlayLaneOutput2Copy(int laneIndex) {
			return m_Sequencers.First(sequencer => sequencer.Kind == LiveSequencerKind.Overlay).ToggleOutput2Copy(laneIndex);
		}

		public bool IsSelectingSequencerLane => m_Sequencers.Any(sequencer => sequencer.SelectedLaneIndex >= 0);

		public LiveSequencerOperationResult SelectSequencerLane(LiveSequencerKind kind, int laneIndex) {
			if (kind != LiveSequencerKind.Overlay) return LiveSequencerOperationResult.Reject("Scene assignment is available for the overlay sequencer.");
			var result = m_Sequencers.First(sequencer => sequencer.Kind == kind).SelectLane(laneIndex);
			if (result.Accepted) m_SelectedCatalogRole = LiveCatalogRole.Overlay;
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
			var patchId = m_SelectedCatalogRole == LiveCatalogRole.Overlay ? SelectedCatalogItemId : string.Empty;
			return string.IsNullOrEmpty(patchId)
				? LiveSequencerOperationResult.Reject("Select an overlay scene first.")
				: AssignOverlayPatchToLane(laneIndex, patchId);
		}

		public LiveSequencerOperationResult AssignOverlayPatchToLane(int laneIndex, string patchId) {
			return !m_OverlayPatchIds.Contains(patchId)
				? LiveSequencerOperationResult.Reject("Only overlay scenes can be assigned to the overlay sequencer.")
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
				.CreateReadModel(adjustedTotalBeats, m_OverlayTakeOverrides);
			_runtime.SetOverlayComposition(overlay);
			return overlay;
		}

		private void PublishReadModel(string diagnostic) {
			ReadModel = new LiveUiReadModel(_tickFrameNumber, _patches, m_EffectNodes, m_SelectedCatalogRole, SelectedCatalogItemId, _runtime?.LoadedPatchId, _runtime?.OverlayPreviewFrames, _runtime?.MainCuePreviewFrames,
				_runtime?.BpmDefinition ?? default, _runtime?.IsTimeEasingEnabled ?? false, CreateLiveParameterDefinitions(), CreateSequencerReadModels(), _runtime?.CurrentFrames ?? default(LiveProgramFrames), _externalDisplay,
				_capabilityMonitor != null ? _capabilityMonitor.Snapshot : default(LiveCapabilitySnapshot), diagnostic,
				_requestResults.ToArray(), m_IsEditMode, m_InstantEffectTypeIds, m_FiredInstantEffectTriggers, m_LiveParameterCueIndex, m_OpenEffectCategory,
				m_IsEffectCategorySelected, m_SelectedEffectCategory);
		}

		private LiveParameterDefinition[] CreateLiveParameterDefinitions() {
			if (_runtime == null) return Array.Empty<LiveParameterDefinition>();
			var patchParameters = _runtime.GetLoadedPatchParameterDefinitions();
			if (m_LiveParameterCueIndex < 0) return patchParameters;
			return patchParameters.Concat(_runtime.GetInstantEffectParameterDefinitions(m_LiveParameterCueIndex)).ToArray();
		}

		private LiveSequencerReadModel[] CreateSequencerReadModels() {
			var adjustedTotalBeats = _runtime?.BpmFrame.AdjustedTotalBeats ?? 0d;
			return m_Sequencers.Select(sequencer => sequencer.CreateReadModel(adjustedTotalBeats,
				sequencer.Kind == LiveSequencerKind.Overlay ? m_OverlayTakeOverrides : null)).ToArray();
		}

		private bool IsKnownPatch(string patchId) => !string.IsNullOrWhiteSpace(patchId) && _patchIds.Contains(patchId);

		private string SelectedCatalogItemId {
			get {
				var itemIds = GetCatalogItemIds(m_SelectedCatalogRole);
				var selectedIndex = GetSelectedCatalogIndex(m_SelectedCatalogRole);
				return selectedIndex >= 0 && selectedIndex < itemIds.Length ? itemIds[selectedIndex] : string.Empty;
			}
		}

		private string[] GetCatalogItemIds(LiveCatalogRole role) {
			switch (role) {
				case LiveCatalogRole.Main: return m_MainPatchIds;
				case LiveCatalogRole.Overlay: return m_OverlayPatchIds;
				case LiveCatalogRole.Effect: return m_EffectNodeTypeIds;
				default: throw new ArgumentOutOfRangeException(nameof(role), role, null);
			}
		}

		private int GetSelectedCatalogIndex(LiveCatalogRole role) {
			switch (role) {
				case LiveCatalogRole.Main: return m_SelectedMainPatchIndex;
				case LiveCatalogRole.Overlay: return m_SelectedOverlayPatchIndex;
				case LiveCatalogRole.Effect: return m_SelectedEffectNodeIndex;
				default: throw new ArgumentOutOfRangeException(nameof(role), role, null);
			}
		}

		private void OpenSelectedEffectCategory() {
			if (m_SelectedEffectNodeIndex < 0 || m_SelectedEffectNodeIndex >= m_EffectNodes.Length) return;
			m_OpenEffectCategory = m_EffectNodes[m_SelectedEffectNodeIndex].Category;
		}

		private void MoveEffectTreeSelection(int direction) {
			var rows = CreateVisibleEffectTreeRows();
			if (rows.Count == 0) return;
			var selectedRowIndex = rows.FindIndex(row => m_IsEffectCategorySelected
				? row.IsCategory && string.Equals(row.Category, m_SelectedEffectCategory, StringComparison.Ordinal)
				: !row.IsCategory && row.EffectIndex == m_SelectedEffectNodeIndex);
			if (selectedRowIndex < 0) selectedRowIndex = 0;
			var row = rows[Mathf.Clamp(selectedRowIndex + Math.Sign(direction), 0, rows.Count - 1)];
			m_SelectedEffectCategory = row.Category;
			m_IsEffectCategorySelected = row.IsCategory;
			if (!row.IsCategory) m_SelectedEffectNodeIndex = row.EffectIndex;
		}

		private List<EffectTreeRow> CreateVisibleEffectTreeRows() {
			var rows = new List<EffectTreeRow>();
			foreach (var category in m_EffectNodes.Select(effect => effect.Category).Distinct(StringComparer.Ordinal)) {
				rows.Add(new EffectTreeRow(category, -1));
				if (!string.Equals(category, m_OpenEffectCategory, StringComparison.Ordinal)) continue;
				for (var effectIndex = 0; effectIndex < m_EffectNodes.Length; effectIndex++) {
					if (string.Equals(m_EffectNodes[effectIndex].Category, category, StringComparison.Ordinal))
						rows.Add(new EffectTreeRow(category, effectIndex));
				}
			}
			return rows;
		}

		private readonly struct EffectTreeRow {
			public string Category { get; }
			public int EffectIndex { get; }
			public bool IsCategory => EffectIndex < 0;

			public EffectTreeRow(string category, int effectIndex) {
				Category = category;
				EffectIndex = effectIndex;
			}
		}

		private void SetSelectedCatalogIndex(LiveCatalogRole role, int index) {
			switch (role) {
				case LiveCatalogRole.Main: m_SelectedMainPatchIndex = index; break;
				case LiveCatalogRole.Overlay: m_SelectedOverlayPatchIndex = index; break;
				case LiveCatalogRole.Effect:
					m_SelectedEffectNodeIndex = index;
					m_IsEffectCategorySelected = false;
					if (index >= 0 && index < m_EffectNodes.Length) m_SelectedEffectCategory = m_EffectNodes[index].Category;
					break;
				default: throw new ArgumentOutOfRangeException(nameof(role), role, null);
			}
		}

		private static int MoveWithinList(int selectedIndex, int direction, int patchCount) {
			if (patchCount <= 0) return 0;
			return Mathf.Clamp(selectedIndex + Math.Sign(direction), 0, patchCount - 1);
		}

		private void ApplyUnityTimeScale(double graphTimeScale) {
			if (!m_OwnsUnityTimeScale) return;
			var finiteScale = double.IsNaN(graphTimeScale) || double.IsInfinity(graphTimeScale) ? 1d : graphTimeScale;
			var requestedScale = m_BaseUnityTimeScale * finiteScale;
			Time.timeScale = (float)Math.Min(MaximumUnityTimeScale, Math.Max(0d, requestedScale));
		}

		private void RestoreUnityTimeScale() {
			if (!m_OwnsUnityTimeScale) return;
			Time.timeScale = m_BaseUnityTimeScale;
			m_OwnsUnityTimeScale = false;
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
