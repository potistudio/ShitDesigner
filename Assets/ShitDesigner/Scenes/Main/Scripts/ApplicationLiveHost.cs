using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Input;
using ShitDesigner.Scene;
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
		[SerializeField, Range(0f, 1f), Tooltip("Opacity of the alternate Main during a Composite Take.")]
		public float m_MainCompositeOpacity = .5f;
		[SerializeField, Range(1, 16)] private int m_SceneTimeEncoderChannel = 16;
		[SerializeField, Range(0, 127)] private int m_SceneTimeEncoderControlNumber = 77;
		[SerializeField, Min(.01f)] private float m_SceneTimeJogSpeedPerStep = 1f;
		[SerializeField, Range(.01f, 8f)] private float m_SceneTimeJogMaximumSpeedOffset = 4f;
		[SerializeField, Min(0f)] private float m_ThumbnailTimeOffsetSeconds = .05f;
		[SerializeField, Tooltip("Blacks out the rendered Program and Overlay frames while held. Test patterns are unaffected.")] private Key m_BlackoutKey = Key.Backquote;
		[Header("Global Flash")]
		[SerializeField, Range(0f, 1f)] private float m_GlobalFlashAmount = 1f;
		[SerializeField, Range(1f, 30f)] private float m_GlobalFlashRate = 12f;
		[SerializeField, Range(.05f, .95f)] private float m_GlobalFlashDuty = .35f;

		[Header("Instant Effect MIDI")]
		[SerializeField, Tooltip("Maps each Instant Effect slot to a MIDI message. Disabled slots remain available through their on-screen control.")]
		private InstantEffectMidiBinding[] m_InstantEffectMidiBindings = CreateInstantEffectMidiBindings();
		[Header("Instant Effect")]
		[SerializeField, Tooltip("Maps each Instant Effect Cue to a User Addable Shader Manifest node.")]
		private string[] m_InstantEffectTypeIds = CreateInstantEffectTypeIds();
		[Header("Instant Overlay MIDI")]
		[SerializeField, Tooltip("Maps each of the sixteen Instant Overlay lanes to a MIDI message. The lane remains active while the mapped control is pressed.")]
		private InstantOverlayMidiBinding[] m_InstantOverlayMidiBindings = CreateInstantOverlayMidiBindings();
		[Header("Instant Overlay")]
		[SerializeField, Tooltip("Maps each Instant Overlay Lane to an Overlay Patch.")]
		private PatchDefinition[] m_InstantOverlayPatches = CreateInstantOverlayPatches();

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
		private readonly LiveBeatQuantizedTriggerQueue m_InstantEffectTriggerQueue = new LiveBeatQuantizedTriggerQueue();
		private readonly LiveBeatEffectGate m_InstantEffectGate = new LiveBeatEffectGate();
		private IReadOnlyList<int> m_FiredInstantEffectTriggers = Array.Empty<int>();
		private int m_LiveParameterCueIndex = -1;
		private bool m_IsEditMode;
		private string m_OpenEffectCategory = string.Empty;
		private bool m_IsEffectCategorySelected;
		private string m_SelectedEffectCategory = string.Empty;
		private string m_PianoReturnMainPatchId = string.Empty;
		private bool m_ReturnToPermanentMainComposite;
		private readonly int[] m_OverlayTakeOverrides = new int[LiveStepSequencer.OverlayLaneCount];
		private readonly int[] m_PianoReturnOverlayTakeOverrides = new int[LiveStepSequencer.OverlayLaneCount];
		private float m_BaseUnityTimeScale = 1f;
		private bool m_OwnsUnityTimeScale;
		private bool m_RebuildRuntimeForOutputResolution;
		private bool m_IsBlackoutActive;
		private bool m_IsGlobalFlashActive;

		public ApplicationLiveHostState State { get; private set; } = ApplicationLiveHostState.Cold;
		public LiveUiReadModel ReadModel { get; private set; }
		public LiveParameterQueue ParameterQueue => _parameterQueue;
		public IReadOnlyList<string> MainCuePatchIds => _runtime?.MainCuePatchIds ?? m_EmptyMainCuePatchIds;
		public int ActiveMainCueIndex => _runtime?.ActiveMainCueIndex ?? -1;
		public string LastDiagnostic { get; private set; } = string.Empty;
		public IReadOnlyList<LiveStepSequencer> Sequencers => m_Sequencers;
		public IReadOnlyList<InstantEffectMidiBinding> InstantEffectMidiBindings => m_InstantEffectMidiBindings;
		public IReadOnlyList<InstantOverlayMidiBinding> InstantOverlayMidiBindings => m_InstantOverlayMidiBindings;
		public bool IsEditMode => m_IsEditMode;
		public event Action<IReadOnlyList<int>> InstantEffectTriggersFired;

		private void Awake() {
			if (_bootOnAwake) Boot();
		}

		private void OnValidate() {
			if (m_InstantEffectMidiBindings == null || m_InstantEffectMidiBindings.Length != ShitDesigner.Runtime.InstantEffectTriggerContract.TriggerCount)
				Array.Resize(ref m_InstantEffectMidiBindings, ShitDesigner.Runtime.InstantEffectTriggerContract.TriggerCount);
			for (var index = 0; index < m_InstantEffectMidiBindings.Length; index++)
				m_InstantEffectMidiBindings[index] ??= new InstantEffectMidiBinding();
			if (m_InstantEffectTypeIds == null || m_InstantEffectTypeIds.Length != ShitDesigner.Runtime.InstantEffectTriggerContract.TriggerCount)
				Array.Resize(ref m_InstantEffectTypeIds, ShitDesigner.Runtime.InstantEffectTriggerContract.TriggerCount);
			if (m_InstantOverlayMidiBindings == null || m_InstantOverlayMidiBindings.Length != LiveStepSequencer.OverlayLaneCount)
				Array.Resize(ref m_InstantOverlayMidiBindings, LiveStepSequencer.OverlayLaneCount);
			for (var index = 0; index < m_InstantOverlayMidiBindings.Length; index++)
				m_InstantOverlayMidiBindings[index] ??= new InstantOverlayMidiBinding();
			if (m_InstantOverlayPatches == null || m_InstantOverlayPatches.Length != LiveStepSequencer.OverlayLaneCount)
				Array.Resize(ref m_InstantOverlayPatches, LiveStepSequencer.OverlayLaneCount);
		}

		private static InstantEffectMidiBinding[] CreateInstantEffectMidiBindings() {
			var bindings = new InstantEffectMidiBinding[ShitDesigner.Runtime.InstantEffectTriggerContract.TriggerCount];
			for (var index = 0; index < bindings.Length; index++) bindings[index] = new InstantEffectMidiBinding();
			return bindings;
		}

		private static string[] CreateInstantEffectTypeIds() => new string[ShitDesigner.Runtime.InstantEffectTriggerContract.TriggerCount];

		private static InstantOverlayMidiBinding[] CreateInstantOverlayMidiBindings() {
			var bindings = new InstantOverlayMidiBinding[LiveStepSequencer.OverlayLaneCount];
			for (var index = 0; index < bindings.Length; index++) bindings[index] = new InstantOverlayMidiBinding();
			return bindings;
		}

		private static PatchDefinition[] CreateInstantOverlayPatches() => new PatchDefinition[LiveStepSequencer.OverlayLaneCount];

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
				_runtime.ConfigureMainCompositeOpacity(m_MainCompositeOpacity);
				_runtime.ConfigureSceneTimeJog(m_SceneTimeJogMaximumSpeedOffset);
				_runtime.ConfigureGlobalFlash(m_GlobalFlashAmount, m_GlobalFlashRate, m_GlobalFlashDuty);
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
				ApplyInstantEffectAssignments();
				ApplyInstantOverlayAssignments();
				for (var laneIndex = 0; laneIndex < LiveStepSequencer.OverlayLaneCount; laneIndex++) {
					m_OverlayTakeOverrides[laneIndex] = FollowOverlaySequencer;
					m_PianoReturnOverlayTakeOverrides[laneIndex] = NoPianoOverlayTake;
				}
				UpdateOverlayComposition(_runtime.BpmFrame.AdjustedTotalBeats);
				m_IsEditMode = false;
				m_PianoReturnMainPatchId = string.Empty;
				ShitDesigner.Runtime.InstantEffectInputMode.SetEditing(false);
				_keyboard = new LiveKeyboardInput(_parameterQueue, _runtime.Patches, BeginPianoOverlayTake, MoveCatalogSelection, () => { LaunchSelectedCatalogPatch(); }, TapBpm,
					ToggleEditMode, () => m_IsEditMode, ToggleSelectedEffectCategory, BeginPianoMainCueSwitch,
					EndPianoMainCueSwitch, CompleteMainCueSwitch, EndPianoOverlayTake, TurnOnOverlaySequencerStep,
					(widthDelta, heightDelta) => {
						LiveGraphRuntime.AdjustOverlayResolution(widthDelta, heightDelta);
						m_RebuildRuntimeForOutputResolution = true;
					}, FireLiveParameter, m_BlackoutKey, active => { m_IsBlackoutActive = active; }, BeginMomentaryMainComposite,
					EndMomentaryMainComposite, CompleteMainComposite, SetGlobalFlashActive);
				_midiInputManager.InitializeForHostPolling();
				_midiInputManager.ConfigureLaunchControlXl3RelativeEncoder(m_SceneTimeEncoderChannel, m_SceneTimeEncoderControlNumber);
				_shutdown.Add(_midiInputManager.Shutdown);
				_midi = new LiveMidiInput(_midiInputManager, _parameterQueue, _runtime.Patches,
					m_MainCueFaderChannel, m_MainCueFaderControlNumber, m_SceneTimeEncoderChannel,
					m_SceneTimeEncoderControlNumber, m_SceneTimeJogSpeedPerStep, m_InstantEffectMidiBindings,
					triggerNumber => {
						if (m_IsEditMode) AssignSelectedEffectToCue(triggerNumber - 1);
						else QueueInstantEffectTrigger(triggerNumber);
					}, m_InstantOverlayMidiBindings, BeginPianoOverlayTake, EndPianoOverlayTake);
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
			if (m_RebuildRuntimeForOutputResolution) {
				RebuildRuntimeForOutputResolution();
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
				var frames = _runtime.Render(m_FiredInstantEffectTriggers, m_IsBlackoutActive, m_IsGlobalFlashActive);
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
			m_IsBlackoutActive = false;
			m_IsGlobalFlashActive = false;
			ShitDesigner.Runtime.InstantEffectInputMode.SetEditing(false);
			State = ApplicationLiveHostState.Offline;
		}

		private void RebuildRuntimeForOutputResolution() {
			m_RebuildRuntimeForOutputResolution = false;
			var replacement = _graphBootstrap.CreateRuntime();
			replacement.ConfigureMainCueFaderCurve(m_MainCueFaderCurve);
			replacement.ConfigureMainCompositeOpacity(m_MainCompositeOpacity);
			replacement.ConfigureSceneTimeJog(m_SceneTimeJogMaximumSpeedOffset);
			replacement.ConfigureGlobalFlash(m_GlobalFlashAmount, m_GlobalFlashRate, m_GlobalFlashDuty);
			var previous = _runtime;
			_runtime = replacement;
			previous.Dispose();
			UpdateOverlayComposition(_runtime.BpmFrame.AdjustedTotalBeats);
			PublishReadModel(string.Empty);
		}

		public void ToggleEditMode() {
			m_IsEditMode = !m_IsEditMode;
			if (m_IsEditMode) SetGlobalFlashActive(false);
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

		private void SetGlobalFlashActive(bool active) {
			m_IsGlobalFlashActive = State == ApplicationLiveHostState.Running && !m_IsEditMode && active;
		}

		private void OnApplicationFocus(bool hasFocus) {
			if (!hasFocus) SetGlobalFlashActive(false);
		}

		public bool AssignSelectedEffectToCue(int cueIndex) {
			if (!m_IsEditMode || cueIndex < 0 || cueIndex >= m_InstantEffectTypeIds.Length || m_SelectedCatalogRole != LiveCatalogRole.Effect || m_IsEffectCategorySelected)
				return false;
			var typeId = SelectedCatalogItemId;
			if (string.IsNullOrEmpty(typeId)) return false;
			return AssignInstantEffect(cueIndex, typeId);
		}

		public bool AssignInstantEffect(int cueIndex, string typeId) {
			if (cueIndex < 0 || cueIndex >= m_InstantEffectTypeIds.Length) return false;
			if (_runtime != null) {
				if (string.IsNullOrEmpty(typeId)) _runtime.ClearInstantEffect(cueIndex);
				else if (!_runtime.TryAssignInstantEffect(cueIndex, typeId, out var rejectionReason)) {
					LastDiagnostic = rejectionReason;
					return false;
				}
			}
			m_InstantEffectTypeIds[cueIndex] = typeId ?? string.Empty;
			if (string.IsNullOrEmpty(typeId) && m_LiveParameterCueIndex == cueIndex) m_LiveParameterCueIndex = -1;
			else if (!string.IsNullOrEmpty(typeId)) m_LiveParameterCueIndex = cueIndex;
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

		private void FireLiveParameter(int parameterIndex, bool firing) {
			var parameters = CreateLiveParameterDefinitions();
			if (parameterIndex < 0 || parameterIndex >= parameters.Length) return;
			var parameter = parameters[parameterIndex];
			if (parameter.Type != ParameterType.Float || !parameter.HasRange) return;
			_parameterQueue.EnqueueSetParameter(_runtime.LoadedPatchId, parameter.Id, firing ? parameter.Maximum : parameter.Minimum);
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
			if (_runtime == null || cueIndex < 0 || cueIndex >= MainCueCount || !string.IsNullOrEmpty(_runtime.MainCuePatchIds[cueIndex])
				|| !m_MainPatchIds.Contains(patchId))
				return false;
			return _parameterQueue.EnqueueAssignMainCue(cueIndex, patchId).Accepted;
		}

		public LiveParameterEnqueueResult UnassignMainPatchFromCue(int cueIndex) {
			if (_runtime == null) return LiveParameterEnqueueResult.Reject("The live runtime is unavailable.");
			if (cueIndex < 0 || cueIndex >= MainCueCount) return LiveParameterEnqueueResult.Reject("The Main Cue Slot does not exist.");
			if (string.IsNullOrEmpty(_runtime.MainCuePatchIds[cueIndex])) return LiveParameterEnqueueResult.Reject("The Main Cue Slot is already empty.");
			return _parameterQueue.EnqueueUnassignMainCue(cueIndex);
		}

		private void BeginPianoMainCueSwitch() {
			if (_runtime == null || !string.IsNullOrEmpty(m_PianoReturnMainPatchId)) return;
			var targetPatchId = AlternateMainCuePatchId(_runtime.LoadedPatchId);
			if (string.IsNullOrEmpty(targetPatchId)) return;
			m_ReturnToPermanentMainComposite = _runtime.IsMainCueCompositeActive;
			m_PianoReturnMainPatchId = _runtime.LoadedPatchId;
			_parameterQueue.EnqueueSetMainCueComposite(false);
			_parameterQueue.EnqueueLoadPatch(targetPatchId);
		}

		private void EndPianoMainCueSwitch() {
			var returnPatchId = m_PianoReturnMainPatchId;
			m_PianoReturnMainPatchId = string.Empty;
			if (!string.IsNullOrEmpty(returnPatchId)) {
				_parameterQueue.EnqueueLoadPatch(returnPatchId);
				_parameterQueue.EnqueueSetMainCueComposite(m_ReturnToPermanentMainComposite);
			}
		}

		private void CompleteMainCueSwitch() {
			m_PianoReturnMainPatchId = string.Empty;
			_parameterQueue.EnqueueToggleMainCue();
		}

		private void BeginMomentaryMainComposite() {
			m_ReturnToPermanentMainComposite = _runtime != null && _runtime.IsMainCueCompositeActive;
			_parameterQueue.EnqueueSetMainCueComposite(true);
		}

		private void EndMomentaryMainComposite() {
			_parameterQueue.EnqueueSetMainCueComposite(m_ReturnToPermanentMainComposite);
		}

		private void CompleteMainComposite() {
			_parameterQueue.EnqueueToggleMainCueComposite();
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

		public LiveSequencerOperationResult TurnOffSequencerCell(LiveSequencerKind kind, int laneIndex, int stepIndex) {
			var sequencer = m_Sequencers.FirstOrDefault(item => item.Kind == kind);
			return sequencer == null
				? LiveSequencerOperationResult.Reject("The requested sequencer does not exist.")
				: sequencer.TurnOffCell(laneIndex, stepIndex);
		}

		public LiveSequencerOperationResult ToggleSequencerStep(LiveSequencerKind kind, int stepIndex) {
			var sequencer = m_Sequencers.FirstOrDefault(item => item.Kind == kind);
			return sequencer == null
				? LiveSequencerOperationResult.Reject("The requested sequencer does not exist.")
				: sequencer.ToggleStep(stepIndex);
		}

		public LiveSequencerOperationResult ToggleSequencerLane(LiveSequencerKind kind, int laneIndex) {
			var sequencer = m_Sequencers.FirstOrDefault(item => item.Kind == kind);
			return sequencer == null
				? LiveSequencerOperationResult.Reject("The requested sequencer does not exist.")
				: sequencer.ToggleLane(laneIndex);
		}

		public LiveSequencerOperationResult ToggleOverlayLaneOutput2Copy(int laneIndex) {
			return m_Sequencers.First(sequencer => sequencer.Kind == LiveSequencerKind.Overlay).ToggleOutput2Copy(laneIndex);
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

		public bool AssignInstantOverlayPatch(int laneIndex, PatchDefinition patch) {
			if (laneIndex < 0 || laneIndex >= m_InstantOverlayPatches.Length) return false;
			if (patch != null && (_graphBootstrap == null || !_graphBootstrap.OverlayPatches.Contains(patch))) return false;
			m_InstantOverlayPatches[laneIndex] = patch;
			if (_runtime == null) return true;
			var overlay = m_Sequencers.First(sequencer => sequencer.Kind == LiveSequencerKind.Overlay);
			if (patch == null) {
				overlay.ClearLane(laneIndex);
				m_OverlayTakeOverrides[laneIndex] = FollowOverlaySequencer;
				m_PianoReturnOverlayTakeOverrides[laneIndex] = NoPianoOverlayTake;
				return true;
			}
			return overlay.AssignLane(laneIndex, patch.Id).Accepted;
		}

		public LiveSequencerOperationResult UnassignOverlayPatchFromLane(int laneIndex) {
			return AssignInstantOverlayPatch(laneIndex, null)
				? LiveSequencerOperationResult.Accept()
				: LiveSequencerOperationResult.Reject("The sequencer lane does not exist.");
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

		private void ApplyInstantEffectAssignments() {
			for (var cueIndex = 0; cueIndex < m_InstantEffectTypeIds.Length; cueIndex++) {
				var typeId = m_InstantEffectTypeIds[cueIndex];
				if (string.IsNullOrEmpty(typeId)) continue;
				if (_runtime.TryAssignInstantEffect(cueIndex, typeId, out var rejectionReason)) continue;
				Debug.LogWarning("[ApplicationLiveHost] Instant Effect Cue " + (cueIndex + 1) + " could not be assigned: " + rejectionReason, this);
			}
		}

		private void ApplyInstantOverlayAssignments() {
			var overlay = m_Sequencers.First(sequencer => sequencer.Kind == LiveSequencerKind.Overlay);
			for (var laneIndex = 0; laneIndex < m_InstantOverlayPatches.Length; laneIndex++) {
				var patch = m_InstantOverlayPatches[laneIndex];
				if (patch == null || !m_OverlayPatchIds.Contains(patch.Id)) continue;
				overlay.AssignLane(laneIndex, patch.Id);
			}
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
