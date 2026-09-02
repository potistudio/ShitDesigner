using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace ShitDesigner.Main {
	/// <summary>Reflects the latest completed live frame and queues patch and parameter requests.</summary>
	[DisallowMultipleComponent]
	[DefaultExecutionOrder(1100)]
	public sealed class LiveUiController : MonoBehaviour {
		private static readonly string[] m_InstantEffectCueKeys = { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" };

		[SerializeField] private PanelRenderer m_PanelRenderer;

		private VisualElement m_Root;
		private VisualElement m_MainUi;
		private ApplicationLiveHost _host;
		private LiveExternalDisplayOutput _output;
		private VisualElement _programMonitor;
		private VisualElement m_Output2Content;
		private Label m_Output2Label;
		private VisualElement m_PatchControls;
		private readonly RenderTexture[] m_OverlayLanePreviewTextures = new RenderTexture[LiveStepSequencer.OverlayLaneCount];
		private readonly RenderTexture[] m_MainCuePreviewTextures = new RenderTexture[ApplicationLiveHost.MainCueCount];
		private ScrollView m_MainPatchControls;
		private ScrollView m_OverlayPatchControls;
		private ScrollView m_EffectNodeControls;
		private VisualElement m_SequencerControls;
		private VisualElement m_InstantEffectCueControls;
		private VisualElement _parameterControls;
		private VisualElement _tempoControls;
		private VisualElement[] m_MainCueSlots = Array.Empty<VisualElement>();
		private VisualElement m_PatchDragStroke;
		private Button[] m_SidebarTabButtons = Array.Empty<Button>();
		private Button[] m_InstantEffectCueButtons = Array.Empty<Button>();
		private VisualElement[] m_SidebarTabContents = Array.Empty<VisualElement>();
		private TextField _bpmField;
		private Button _bpmTapButton;
		private Button m_BeatAlignmentButton;
		private Button m_TimeEasingButton;
		private Label _capabilityLabel;
		private Label _diagnosticLabel;
		private LiveOutputMenuController m_OutputMenu;
		private Coroutine m_ReloadRoutine;
		private string _renderedPatchId = string.Empty;
		private string m_RenderedParameterIds = string.Empty;
		private string m_CenteredCatalogItemId = string.Empty;
		private string m_PendingCenteredCatalogItemId = string.Empty;
		private int m_RenderedPatchCount = -1;
		private int m_RenderedEffectNodeCount = -1;
		private bool _initialized;
		private bool _updating;
		private bool _editingBpm;
		private Button m_PatchDragSource;
		private string m_DraggedPatchId = string.Empty;
		private LivePatchRole m_DraggedPatchRole;
		private int m_PatchDragPointerId = -1;
		private Vector2 m_PatchDragStart;
		private Vector2 m_PatchDragCurrent;
		private bool m_IsPatchDragging;
		private VisualElement m_PatchDropTarget;
		private int m_LastPatchDropFrame = -1;

		private readonly struct SequencerCellAddress {
			public LiveSequencerKind Kind { get; }
			public int LaneIndex { get; }
			public int StepIndex { get; }

			public SequencerCellAddress(LiveSequencerKind kind, int laneIndex, int stepIndex) {
				Kind = kind;
				LaneIndex = laneIndex;
				StepIndex = stepIndex;
			}
		}

		private readonly struct SequencerStepAddress {
			public LiveSequencerKind Kind { get; }
			public int StepIndex { get; }

			public SequencerStepAddress(LiveSequencerKind kind, int stepIndex) {
				Kind = kind;
				StepIndex = stepIndex;
			}
		}

		private readonly struct SequencerLaneAddress {
			public LiveSequencerKind Kind { get; }
			public int LaneIndex { get; }

			public SequencerLaneAddress(LiveSequencerKind kind, int laneIndex) {
				Kind = kind;
				LaneIndex = laneIndex;
			}
		}

		public void Initialize(ApplicationLiveHost host, LiveExternalDisplayOutput output) {
			_host = host ?? throw new ArgumentNullException(nameof(host));
			_output = output ?? throw new ArgumentNullException(nameof(output));
			if (m_PanelRenderer == null) throw new InvalidOperationException("A dedicated live PanelRenderer is required.");
			m_OutputMenu?.Dispose();
			m_OutputMenu = new LiveOutputMenuController(output);
			m_PanelRenderer.RegisterUIReloadCallback(OnUiReload);
			m_ReloadRoutine = StartCoroutine(ReloadUiAfterPanelInitialization());
		}

		private IEnumerator ReloadUiAfterPanelInitialization() {
			yield return null;
			m_ReloadRoutine = null;
			if (m_PanelRenderer == null) yield break;

			var asset = m_PanelRenderer.visualTreeAsset;
			m_PanelRenderer.visualTreeAsset = null;
			m_PanelRenderer.visualTreeAsset = asset;
		}

		private void OnUiReload(PanelRenderer panelRenderer, VisualElement root) {
			if (root == null || root.childCount == 0 || _host == null || _output == null) return;
			UnbindVisualTree();
			m_Root = root;
			m_MainUi = Required<VisualElement>(root, "main-ui");

			_programMonitor = Required<VisualElement>(root, "program-monitor");
			m_Output2Content = Required<VisualElement>(root, "output-2-content");
			m_Output2Label = Required<Label>(root, "output-2-label");
			m_PatchControls = Required<VisualElement>(root, "patch-controls");
			m_MainPatchControls = Required<ScrollView>(root, "main-patch-controls");
			m_OverlayPatchControls = Required<ScrollView>(root, "overlay-patch-controls");
			m_EffectNodeControls = Required<ScrollView>(root, "effect-node-controls");
			m_SequencerControls = Required<VisualElement>(root, "sequencer-controls");
			m_InstantEffectCueControls = Required<VisualElement>(root, "instant-effect-cues");
			_parameterControls = Required<VisualElement>(root, "parameter-controls");
			_tempoControls = Required<VisualElement>(root, "tempo-controls");
			m_MainCueSlots = Enumerable.Range(1, ApplicationLiveHost.MainCueCount)
				.Select(index => Required<VisualElement>(root, "cue-slot-" + index))
				.ToArray();
			m_PatchDragStroke = new VisualElement {
				name = "patch-drag-stroke",
				pickingMode = PickingMode.Ignore
			};
			m_PatchDragStroke.AddToClassList("patch-drag-stroke");
			m_PatchDragStroke.generateVisualContent += DrawPatchDragStroke;
			m_MainUi.Add(m_PatchDragStroke);
			m_SidebarTabButtons = new[] {
				Required<Button>(root, "main-tab"),
				Required<Button>(root, "overlay-tab"),
				Required<Button>(root, "effect-tab")
			};
			m_SidebarTabContents = new[] {
				m_MainPatchControls,
				m_OverlayPatchControls,
				m_EffectNodeControls
			};
			m_InstantEffectCueButtons = Enumerable.Range(1, InstantEffectTriggerContract.TriggerCount)
				.Select(index => Required<Button>(root, "instant-effect-cue-" + index))
				.ToArray();
			for (var index = 0; index < m_InstantEffectCueButtons.Length; index++) m_InstantEffectCueButtons[index].userData = index + 1;
			for (var tabIndex = 0; tabIndex < m_SidebarTabButtons.Length; tabIndex++) {
				m_SidebarTabButtons[tabIndex].userData = (LiveCatalogRole)tabIndex;
				m_SidebarTabButtons[tabIndex].RegisterCallback<ClickEvent>(OnSidebarTabClicked);
			}
			SelectSidebarTab(LiveCatalogRole.Main);
			_bpmField = Required<TextField>(root, "bpm-field");
			_bpmTapButton = Required<Button>(root, "bpm-tap");
			m_BeatAlignmentButton = Required<Button>(root, "beat-alignment-button");
			m_TimeEasingButton = Required<Button>(root, "time-easing-button");
			_capabilityLabel = Required<Label>(root, "capability-status");
			_diagnosticLabel = Required<Label>(root, "diagnostic-status");
			m_Root.RegisterCallback<PointerDownEvent>(OnPatchPointerDown, TrickleDown.TrickleDown);
			m_Root.RegisterCallback<PointerMoveEvent>(OnPatchPointerMove, TrickleDown.TrickleDown);
			m_Root.RegisterCallback<PointerUpEvent>(OnPatchPointerUp, TrickleDown.TrickleDown);
			m_Root.RegisterCallback<PointerCancelEvent>(OnPatchPointerCancel, TrickleDown.TrickleDown);
			m_MainPatchControls.RegisterCallback<PointerDownEvent>(OnPatchPointerDown, TrickleDown.TrickleDown);
			m_OverlayPatchControls.RegisterCallback<PointerDownEvent>(OnPatchPointerDown, TrickleDown.TrickleDown);
			m_MainPatchControls.RegisterCallback<MouseDownEvent>(OnPatchMouseDown, TrickleDown.TrickleDown);
			m_OverlayPatchControls.RegisterCallback<MouseDownEvent>(OnPatchMouseDown, TrickleDown.TrickleDown);
			m_Root.RegisterCallback<MouseMoveEvent>(OnPatchMouseMove, TrickleDown.TrickleDown);
			m_Root.RegisterCallback<MouseUpEvent>(OnPatchMouseUp, TrickleDown.TrickleDown);
			m_SequencerControls.RegisterCallback<ClickEvent>(OnSequencerCellClicked);
			m_InstantEffectCueControls.RegisterCallback<ClickEvent>(OnInstantEffectCueClicked);
			BindSequencers(root);
			_bpmField.RegisterValueChangedCallback(OnBpmInputChanged);
			_bpmField.RegisterCallback<FocusInEvent>(OnBpmFocusIn);
			_bpmField.RegisterCallback<FocusOutEvent>(OnBpmFocusOut);
			_bpmTapButton.clicked += TapBpm;
			m_BeatAlignmentButton.clicked += AlignBeat;
			m_TimeEasingButton.clicked += ToggleTimeEasing;
			_initialized = true;
		}

		public void Shutdown() {
			if (m_ReloadRoutine != null) {
				StopCoroutine(m_ReloadRoutine);
				m_ReloadRoutine = null;
			}
			if (m_PanelRenderer != null) m_PanelRenderer.UnregisterUIReloadCallback(OnUiReload);
			UnbindVisualTree();
			m_OutputMenu?.Dispose();
			m_OutputMenu = null;
			_host = null;
			_output = null;
		}

		private void UnbindVisualTree() {
			if (m_Root != null) {
				CancelPatchDrag();
				m_Root.UnregisterCallback<PointerDownEvent>(OnPatchPointerDown, TrickleDown.TrickleDown);
				m_Root.UnregisterCallback<PointerMoveEvent>(OnPatchPointerMove, TrickleDown.TrickleDown);
				m_Root.UnregisterCallback<PointerUpEvent>(OnPatchPointerUp, TrickleDown.TrickleDown);
				m_Root.UnregisterCallback<PointerCancelEvent>(OnPatchPointerCancel, TrickleDown.TrickleDown);
				m_Root.UnregisterCallback<MouseMoveEvent>(OnPatchMouseMove, TrickleDown.TrickleDown);
				m_Root.UnregisterCallback<MouseUpEvent>(OnPatchMouseUp, TrickleDown.TrickleDown);
			}
			if (m_PatchDragStroke != null) {
				m_PatchDragStroke.generateVisualContent -= DrawPatchDragStroke;
				m_PatchDragStroke.RemoveFromHierarchy();
			}
			m_MainPatchControls?.UnregisterCallback<PointerDownEvent>(OnPatchPointerDown, TrickleDown.TrickleDown);
			m_OverlayPatchControls?.UnregisterCallback<PointerDownEvent>(OnPatchPointerDown, TrickleDown.TrickleDown);
			m_MainPatchControls?.UnregisterCallback<MouseDownEvent>(OnPatchMouseDown, TrickleDown.TrickleDown);
			m_OverlayPatchControls?.UnregisterCallback<MouseDownEvent>(OnPatchMouseDown, TrickleDown.TrickleDown);
			m_MainCueSlots = Array.Empty<VisualElement>();
			m_PatchDragStroke = null;
			if (m_SequencerControls != null) m_SequencerControls.UnregisterCallback<ClickEvent>(OnSequencerCellClicked);
			if (m_InstantEffectCueControls != null) m_InstantEffectCueControls.UnregisterCallback<ClickEvent>(OnInstantEffectCueClicked);
			foreach (var button in m_SidebarTabButtons) button.UnregisterCallback<ClickEvent>(OnSidebarTabClicked);
			m_SidebarTabButtons = Array.Empty<Button>();
			m_SidebarTabContents = Array.Empty<VisualElement>();
			m_InstantEffectCueButtons = Array.Empty<Button>();
			m_InstantEffectCueControls = null;
			if (_bpmField != null) {
				_bpmField.UnregisterValueChangedCallback(OnBpmInputChanged);
				_bpmField.UnregisterCallback<FocusInEvent>(OnBpmFocusIn);
				_bpmField.UnregisterCallback<FocusOutEvent>(OnBpmFocusOut);
			}
			if (_bpmTapButton != null) _bpmTapButton.clicked -= TapBpm;
			if (m_BeatAlignmentButton != null) m_BeatAlignmentButton.clicked -= AlignBeat;
			if (m_TimeEasingButton != null) m_TimeEasingButton.clicked -= ToggleTimeEasing;
			_initialized = false;
			m_Root = null;
			m_MainUi = null;
			_renderedPatchId = string.Empty;
			m_RenderedParameterIds = string.Empty;
			m_CenteredCatalogItemId = string.Empty;
			m_PendingCenteredCatalogItemId = string.Empty;
			m_RenderedPatchCount = -1;
			m_RenderedEffectNodeCount = -1;
			Array.Clear(m_OverlayLanePreviewTextures, 0, m_OverlayLanePreviewTextures.Length);
			Array.Clear(m_MainCuePreviewTextures, 0, m_MainCuePreviewTextures.Length);
		}

		private void LateUpdate() {
			m_OutputMenu?.Tick();
			if (!_initialized) return;
			RefreshInstantEffectCueHighlights();
			if (_host.ReadModel == null) return;
			var model = _host.ReadModel;
			_updating = true;
			try {
				RefreshMainCueSlots(model);
				ApplyPreviewTexture(_programMonitor, model.ProgramFrames.Count > 0 ? model.ProgramFrames[0].Texture : null);
				ApplyPreviewTexture(m_Output2Content, model.ProgramFrames.Count > 1 ? model.ProgramFrames[1].Texture : null);
				RefreshOutput2Viewport(model.Output2Viewport);
				RefreshPatchControls(model);
				RefreshEditMode(model);
				RefreshSequencers(model);
				RefreshTempoControls(model);
				_capabilityLabel.text = $"MIDI: {(model.Capabilities.MidiAvailable ? "READY" : "UNAVAILABLE")}  DISPLAY: {(model.Capabilities.ExternalDisplayAvailable ? "READY" : "UNAVAILABLE")}  FRAME: {model.ProgramFrameNumber}";
				_diagnosticLabel.text = ResolveDiagnostic(model);
				if (_renderedPatchId != model.LoadedPatchId || m_RenderedParameterIds != ParameterIds(model)) RebuildParameters(model);
				else RefreshParameterValues(model);
			}
			finally { _updating = false; }
		}

		private void RefreshInstantEffectCueHighlights() {
			var keyboard = Keyboard.current;
			for (var index = 0; index < m_InstantEffectCueButtons.Length; index++)
				m_InstantEffectCueButtons[index].EnableInClassList("is-keyboard-active", !(_host?.IsEditMode ?? false) && IsInstantEffectCueKeyPressed(keyboard, index));
		}

		private void RefreshOutput2Viewport(LiveOutputViewport viewport) {
			if (m_Output2Content == null || m_Output2Label == null) return;
			var rect = viewport.ResolveRect(LiveGraphRuntime.OverlayWidth, LiveGraphRuntime.OverlayHeight);
			m_Output2Content.style.left = Length.Percent(100f * rect.x / LiveGraphRuntime.OverlayWidth);
			m_Output2Content.style.top = Length.Percent(100f * (LiveGraphRuntime.OverlayHeight - rect.yMax) / LiveGraphRuntime.OverlayHeight);
			m_Output2Content.style.width = Length.Percent(100f * rect.width / LiveGraphRuntime.OverlayWidth);
			m_Output2Content.style.height = Length.Percent(100f * rect.height / LiveGraphRuntime.OverlayHeight);
			m_Output2Label.text = $"Output 2  {viewport.Width}×{viewport.Height}  {viewport.OffsetX:+0;-0;0},{viewport.OffsetY:+0;-0;0}";
		}

		private void RefreshEditMode(LiveUiReadModel model) {
			m_MainUi.EnableInClassList("is-edit-mode", model.IsEditMode);
			m_SequencerControls.SetEnabled(!model.IsEditMode);
			_parameterControls.SetEnabled(!model.IsEditMode);
			_tempoControls.SetEnabled(!model.IsEditMode);
			for (var tabIndex = 0; tabIndex < m_SidebarTabButtons.Length; tabIndex++)
				m_SidebarTabButtons[tabIndex].SetEnabled(!model.IsEditMode || tabIndex == (int)LiveCatalogRole.Effect);
			for (var index = 0; index < m_InstantEffectCueButtons.Length; index++) {
				var typeId = index < model.InstantEffectTypeIds.Count ? model.InstantEffectTypeIds[index] : string.Empty;
				var effect = model.EffectNodes.FirstOrDefault(candidate => candidate.TypeId == typeId);
				var effectName = string.IsNullOrEmpty(effect.Name) ? typeId : effect.Name;
				m_InstantEffectCueButtons[index].text = string.IsNullOrEmpty(typeId) ? m_InstantEffectCueKeys[index] : effectName;
				m_InstantEffectCueButtons[index].tooltip = string.IsNullOrEmpty(typeId)
					? m_InstantEffectCueKeys[index] + " · Instant Effect Trigger " + (index + 1)
					: m_InstantEffectCueKeys[index] + " · " + (model.IsEditMode ? "Replace " : string.Empty) + effectName;
				m_InstantEffectCueButtons[index].EnableInClassList("is-assigned", !string.IsNullOrEmpty(typeId));
				m_InstantEffectCueButtons[index].EnableInClassList("is-edit-key-active", model.IsEditMode && IsInstantEffectCueKeyPressed(Keyboard.current, index));
				m_InstantEffectCueButtons[index].EnableInClassList("is-trigger-fired", model.FiredInstantEffectTriggers.Contains(index + 1));
				m_InstantEffectCueButtons[index].EnableInClassList("is-parameter-focused", model.FocusedInstantEffectCueIndex == index);
			}
		}

		private void OnInstantEffectCueClicked(ClickEvent click) {
			var target = click.target as VisualElement;
			var button = target as Button ?? target?.GetFirstAncestorOfType<Button>();
			if (_host == null || button?.userData is not int triggerNumber) return;
			if (_host.IsEditMode) _host.AssignSelectedEffectToCue(triggerNumber - 1);
			else _host.QueueInstantEffectTrigger(triggerNumber);
		}

		private static bool IsInstantEffectCueKeyPressed(Keyboard keyboard, int index) {
			if (keyboard == null) return false;
			switch (index) {
				case 0: return keyboard.qKey.isPressed;
				case 1: return keyboard.wKey.isPressed;
				case 2: return keyboard.eKey.isPressed;
				case 3: return keyboard.rKey.isPressed;
				case 4: return keyboard.tKey.isPressed;
				case 5: return keyboard.yKey.isPressed;
				case 6: return keyboard.uKey.isPressed;
				case 7: return keyboard.iKey.isPressed;
				case 8: return keyboard.oKey.isPressed;
				case 9: return keyboard.pKey.isPressed;
				default: return false;
			}
		}

		private void OnSidebarTabClicked(ClickEvent click) {
			if (click.currentTarget is not Button button || button.userData is not LiveCatalogRole role) return;
			_host?.SelectCatalogRole(role);
			SelectSidebarTab(role);
		}

		private void SelectSidebarTab(LiveCatalogRole selectedRole) {
			for (var tabIndex = 0; tabIndex < m_SidebarTabButtons.Length; tabIndex++) {
				var isSelected = tabIndex == (int)selectedRole;
				m_SidebarTabButtons[tabIndex].EnableInClassList("is-selected", isSelected);
				m_SidebarTabContents[tabIndex].EnableInClassList("is-hidden", !isSelected);
			}
		}

		private void BindSequencers(VisualElement root) {
			foreach (var sequencer in _host.Sequencers) {
				var container = Required<VisualElement>(root, GetSequencerElementName(sequencer.Kind));
				for (var stepIndex = 0; stepIndex < LiveStepSequencer.StepCount; stepIndex++) {
					Required<Button>(container, GetSequencerStepName(sequencer.Kind, stepIndex)).userData =
						new SequencerStepAddress(sequencer.Kind, stepIndex);
				}

				for (var laneIndex = 0; laneIndex < sequencer.LaneCount; laneIndex++) {
					var laneLabel = Required<Button>(container, GetSequencerLaneName(sequencer.Kind, laneIndex));
					var lane = laneLabel.parent;
					if (sequencer.Kind == LiveSequencerKind.Overlay)
						lane.userData = new SequencerLaneAddress(sequencer.Kind, laneIndex);
					laneLabel.userData = new SequencerLaneAddress(sequencer.Kind, laneIndex);
					for (var stepIndex = 0; stepIndex < LiveStepSequencer.StepCount; stepIndex++) {
						Required<Button>(lane, GetSequencerCellName(sequencer.Kind, laneIndex, stepIndex)).userData =
							new SequencerCellAddress(sequencer.Kind, laneIndex, stepIndex);
					}
				}
			}
		}

		private void RefreshSequencers(LiveUiReadModel model) {
			foreach (var sequencer in model.Sequencers) {
				for (var laneIndex = 0; laneIndex < sequencer.LaneCount; laneIndex++) {
					var laneLabel = m_SequencerControls.Q<VisualElement>(GetSequencerLaneName(sequencer.Kind, laneIndex));
					if (laneLabel != null) {
						var patchId = sequencer.LanePatchIds.Count > laneIndex ? sequencer.LanePatchIds[laneIndex] : string.Empty;
						var patch = model.Patches.FirstOrDefault(candidate => candidate.Id == patchId);
						var isCopiedToOutput2 = sequencer.IsCopiedToOutput2(laneIndex);
						if (sequencer.Kind == LiveSequencerKind.Overlay) {
							var preview = laneIndex < model.OverlayLanePreviews.Count ? model.OverlayLanePreviews[laneIndex] : null;
							if (m_OverlayLanePreviewTextures[laneIndex] != preview) {
								m_OverlayLanePreviewTextures[laneIndex] = preview;
								ApplyPreviewTexture(laneLabel, preview);
							}
							laneLabel.EnableInClassList("has-preview", preview != null);
						}
						laneLabel.tooltip = sequencer.Kind == LiveSequencerKind.Effect
							? "LANE " + (laneIndex + 1) + " · CLICK TO TOGGLE ROW"
							: string.IsNullOrEmpty(patchId)
								? "LANE " + (laneIndex + 1) + " · CLICK TO TOGGLE ROW · DROP OVERLAY SCENE"
								: "LANE " + (laneIndex + 1) + " · " + (string.IsNullOrEmpty(patch.Name) ? patchId : patch.Name) + " · CLICK TO TOGGLE ROW · RIGHT-CLICK TO UNASSIGN";
						if (isCopiedToOutput2) laneLabel.tooltip += " · OUTPUT 2 COPY";
						laneLabel.EnableInClassList("is-assigned", !string.IsNullOrEmpty(patchId));
						laneLabel.parent?.EnableInClassList("is-output-2-copy", isCopiedToOutput2);
					}
					for (var stepIndex = 0; stepIndex < LiveStepSequencer.StepCount; stepIndex++) {
						var button = m_SequencerControls.Q<Button>(GetSequencerCellName(sequencer.Kind, laneIndex, stepIndex));
						if (button == null) continue;
						var mode = sequencer.GetCellMode(laneIndex, stepIndex);
						button.text = FormatSequencerCellMode(mode);
						button.tooltip = mode.ToString().ToUpperInvariant();
						button.EnableInClassList("is-set", mode != LiveSequencerCellMode.Off);
						button.EnableInClassList("is-playhead", sequencer.CurrentStep == stepIndex);
					}
				}
			}
		}

		private void OnSequencerCellClicked(ClickEvent change) {
			if (m_LastPatchDropFrame == Time.frameCount) {
				change.StopImmediatePropagation();
				return;
			}
			var target = change.target as VisualElement;
			var button = target as Button ?? target?.GetFirstAncestorOfType<Button>();
			if (change.button != 0) return;
			var lane = target;
			while (lane != null && lane.userData is not SequencerLaneAddress) lane = lane.parent;
			if (_host != null && lane?.userData is SequencerLaneAddress outputLaneAddress
				&& outputLaneAddress.Kind == LiveSequencerKind.Overlay
				&& (change.modifiers & (EventModifiers.Control | EventModifiers.Command)) != 0) {
				ShowSequencerRejection(_host.ToggleOverlayLaneOutput2Copy(outputLaneAddress.LaneIndex));
				change.StopImmediatePropagation();
				return;
			}
			if (_host == null || button == null) return;
			if (button.userData is SequencerStepAddress stepAddress) {
				ShowSequencerRejection(_host.ToggleSequencerStep(stepAddress.Kind, stepAddress.StepIndex));
				return;
			}
			if (button.userData is SequencerCellAddress cellAddress) {
				ShowSequencerRejection(_host.CycleSequencerCellMode(cellAddress.Kind, cellAddress.LaneIndex, cellAddress.StepIndex));
				return;
			}
			if (button.userData is SequencerLaneAddress laneAddress)
				ShowSequencerRejection(_host.ToggleSequencerLane(laneAddress.Kind, laneAddress.LaneIndex));
		}

		private static string GetSequencerElementName(LiveSequencerKind kind) {
			switch (kind) {
				case LiveSequencerKind.Overlay: return "overlay-sequencer";
				case LiveSequencerKind.Effect: return "effect-sequencer";
				default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
			}
		}

		private static string GetSequencerCellName(LiveSequencerKind kind, int laneIndex, int stepIndex) {
			return "sequencer-" + GetSequencerId(kind) + "-lane-" + laneIndex + "-step-" + stepIndex;
		}

		private static string GetSequencerStepName(LiveSequencerKind kind, int stepIndex) {
			return "sequencer-" + GetSequencerId(kind) + "-step-" + stepIndex;
		}

		private static string GetSequencerLaneName(LiveSequencerKind kind, int laneIndex) {
			return "sequencer-" + GetSequencerId(kind) + "-lane-label-" + laneIndex;
		}

		private static string GetSequencerId(LiveSequencerKind kind) {
			switch (kind) {
				case LiveSequencerKind.Overlay: return "overlay";
				case LiveSequencerKind.Effect: return "effect";
				default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
			}
		}

		private static string FormatSequencerCellMode(LiveSequencerCellMode mode) {
			return mode.ToString().ToUpperInvariant();
		}

		private void RebuildParameters(LiveUiReadModel model) {
			_parameterControls.Clear();
			foreach (var parameter in model.Parameters) {
				var channel = new VisualElement { name = "parameter-channel-" + parameter.Id };
				channel.AddToClassList("parameter-fader-channel");
				var valueLabel = new Label(FormatParameterValue(parameter.TypedValue)) { name = "parameter-value-" + parameter.Id };
				valueLabel.AddToClassList("parameter-fader-value");
				var label = new Label(parameter.DisplayName);
				label.AddToClassList("parameter-fader-label");
				channel.Add(valueLabel);
				channel.Add(CreateParameterControl(parameter));
				channel.Add(label);
				_parameterControls.Add(channel);
			}
			_renderedPatchId = model.LoadedPatchId;
			m_RenderedParameterIds = ParameterIds(model);
		}

		private static string ParameterIds(LiveUiReadModel model)
			=> string.Join("\n", model.Parameters.Select(parameter => parameter.Id));

		private void RefreshParameterValues(LiveUiReadModel model) {
			foreach (var parameter in model.Parameters) {
				RefreshParameterControl(parameter);
				var valueLabel = _parameterControls.Q<Label>("parameter-value-" + parameter.Id);
				if (valueLabel != null) valueLabel.text = FormatParameterValue(parameter.TypedValue);
			}
		}

		private VisualElement CreateParameterControl(LiveParameterDefinition parameter) {
			var name = "parameter-" + parameter.Id;
			if (parameter.Type == ParameterType.Float && parameter.HasRange) {
				var slider = new Slider(parameter.Minimum, parameter.Maximum) {
					direction = SliderDirection.Vertical,
					name = name,
					value = parameter.TypedValue.AsFloat()
				};
				slider.AddToClassList("parameter-slider");
				slider.RegisterValueChangedCallback(change => QueueParameter(parameter.Id, ParameterValue.FromFloat(change.newValue)));
				return slider;
			}

			switch (parameter.Type) {
				case ParameterType.Float:
					var single = new FloatField { name = name, value = parameter.TypedValue.AsFloat() };
					single.RegisterValueChangedCallback(change => QueueParameter(parameter.Id, ParameterValue.FromFloat(change.newValue)));
					return single;
				case ParameterType.Int:
					var integer = new IntegerField { name = name, value = parameter.TypedValue.AsInt() };
					integer.RegisterValueChangedCallback(change => QueueParameter(parameter.Id, ParameterValue.FromInt(change.newValue)));
					return integer;
				case ParameterType.Bool:
					var toggle = new Toggle { name = name, value = parameter.TypedValue.AsBool() };
					toggle.RegisterValueChangedCallback(change => QueueParameter(parameter.Id, ParameterValue.FromBool(change.newValue)));
					return toggle;
				case ParameterType.Color:
				case ParameterType.Vector2:
				case ParameterType.Vector3:
				case ParameterType.Vector4:
					return CreateComponentControl(parameter);
				default:
					var text = new TextField { name = name, value = parameter.TypedValue.AsString() };
					text.RegisterValueChangedCallback(change => QueueParameter(parameter.Id, ParameterValue.FromEnum(change.newValue ?? string.Empty)));
					return text;
			}
		}

		private VisualElement CreateComponentControl(LiveParameterDefinition parameter) {
			var values = Components(parameter.TypedValue);
			var control = new VisualElement { name = "parameter-" + parameter.Id };
			for (var index = 0; index < values.Length; index++) {
				var componentIndex = index;
				var field = new FloatField { name = "parameter-" + parameter.Id + "-" + index, value = values[index] };
				field.RegisterValueChangedCallback(change => {
					values[componentIndex] = change.newValue;
					QueueParameter(parameter.Id, ComponentValue(parameter.Type, values));
				});
				control.Add(field);
			}
			return control;
		}

		private void RefreshParameterControl(LiveParameterDefinition parameter) {
			var name = "parameter-" + parameter.Id;
			if (parameter.Type == ParameterType.Float && parameter.HasRange) {
				_parameterControls.Q<Slider>(name)?.SetValueWithoutNotify(parameter.TypedValue.AsFloat());
				return;
			}
			switch (parameter.Type) {
				case ParameterType.Float: _parameterControls.Q<FloatField>(name)?.SetValueWithoutNotify(parameter.TypedValue.AsFloat()); break;
				case ParameterType.Int: _parameterControls.Q<IntegerField>(name)?.SetValueWithoutNotify(parameter.TypedValue.AsInt()); break;
				case ParameterType.Bool: _parameterControls.Q<Toggle>(name)?.SetValueWithoutNotify(parameter.TypedValue.AsBool()); break;
				case ParameterType.Color:
				case ParameterType.Vector2:
				case ParameterType.Vector3:
				case ParameterType.Vector4:
					var values = Components(parameter.TypedValue);
					for (var index = 0; index < values.Length; index++) _parameterControls.Q<FloatField>(name + "-" + index)?.SetValueWithoutNotify(values[index]);
					break;
				default: _parameterControls.Q<TextField>(name)?.SetValueWithoutNotify(parameter.TypedValue.AsString()); break;
			}
		}

		private void QueueParameter(string parameterId, ParameterValue value) {
			if (!_updating && _host?.ReadModel != null)
				ShowEnqueueRejection(_host.ParameterQueue.EnqueueSetParameter(_host.ReadModel.LoadedPatchId, parameterId, value));
		}

		private static float[] Components(ParameterValue value) {
			switch (value.Type) {
				case ParameterType.Vector2: var vector2 = value.AsVector2(); return new[] { vector2.X, vector2.Y };
				case ParameterType.Vector3: var vector3 = value.AsVector3(); return new[] { vector3.X, vector3.Y, vector3.Z };
				case ParameterType.Vector4: var vector4 = value.AsVector4(); return new[] { vector4.X, vector4.Y, vector4.Z, vector4.W };
				case ParameterType.Color: var color = value.AsColor(); return new[] { color.R, color.G, color.B, color.A };
				default: throw new ArgumentOutOfRangeException(nameof(value));
			}
		}

		private static ParameterValue ComponentValue(ParameterType type, IReadOnlyList<float> values) {
			switch (type) {
				case ParameterType.Vector2: return ParameterValue.FromVector2(new Vector2Value(values[0], values[1]));
				case ParameterType.Vector3: return ParameterValue.FromVector3(new Vector3Value(values[0], values[1], values[2]));
				case ParameterType.Vector4: return ParameterValue.FromVector4(new Vector4Value(values[0], values[1], values[2], values[3]));
				case ParameterType.Color: return ParameterValue.FromColor(new ColorValue(values[0], values[1], values[2], values[3]));
				default: throw new ArgumentOutOfRangeException(nameof(type));
			}
		}

		private void RefreshPatchControls(LiveUiReadModel model) {
			if (m_RenderedPatchCount != model.Patches.Count || m_RenderedEffectNodeCount != model.EffectNodes.Count)
				RebuildPatchControls(model);
			SelectSidebarTab(model.SelectedCatalogRole);
			foreach (var patch in model.Patches) {
				var button = m_PatchControls.Q<Button>("patch-" + patch.Id);
				if (button == null) continue;
				button.EnableInClassList("is-selected", patch.Id == model.SelectedCatalogItemId);
			}
			foreach (var effect in model.EffectNodes) {
				var button = m_EffectNodeControls.Q<Button>("effect-node-" + effect.TypeId);
				if (button != null) button.EnableInClassList("is-selected", !model.IsEffectCategorySelected && effect.TypeId == model.SelectedCatalogItemId);
			}
			foreach (var header in m_EffectNodeControls.Query<Button>(className: "effect-category-button").ToList()) {
				var category = header.userData as string ?? string.Empty;
				var isOpen = string.Equals(category, model.OpenEffectCategory, StringComparison.Ordinal);
				header.text = (isOpen ? "▼ " : "▶ ") + category;
				header.EnableInClassList("is-open", isOpen);
				header.EnableInClassList("is-selected", model.SelectedCatalogRole == LiveCatalogRole.Effect && model.IsEffectCategorySelected
					&& string.Equals(model.SelectedEffectCategory, category, StringComparison.Ordinal));
			}
			foreach (var items in m_EffectNodeControls.Query<VisualElement>(className: "effect-category-items").ToList())
				items.EnableInClassList("is-hidden", !string.Equals(items.userData as string, model.OpenEffectCategory, StringComparison.Ordinal));
			var selectedItemId = model.SelectedCatalogRole == LiveCatalogRole.Effect && model.IsEffectCategorySelected
				? "category:" + model.SelectedEffectCategory
				: model.SelectedCatalogItemId;
			if (m_CenteredCatalogItemId != selectedItemId && m_PendingCenteredCatalogItemId != selectedItemId)
				CenterCatalogSelection(model.SelectedCatalogRole, selectedItemId);
		}

		private void RebuildPatchControls(LiveUiReadModel model) {
			m_MainPatchControls.Clear();
			m_OverlayPatchControls.Clear();
			m_EffectNodeControls.Clear();
			AddPatchButtons(m_MainPatchControls, model.Patches.Where(patch => patch.Role == LivePatchRole.Main));
			AddPatchButtons(m_OverlayPatchControls, model.Patches.Where(patch => patch.Role == LivePatchRole.Overlay));
			AddEffectNodeButtons(model.EffectNodes);
			m_MainPatchControls.scrollOffset = Vector2.zero;
			m_OverlayPatchControls.scrollOffset = Vector2.zero;
			m_EffectNodeControls.scrollOffset = Vector2.zero;
			m_CenteredCatalogItemId = model.SelectedCatalogItemId;
			m_PendingCenteredCatalogItemId = string.Empty;
			m_RenderedPatchCount = model.Patches.Count;
			m_RenderedEffectNodeCount = model.EffectNodes.Count;
		}

		private void AddPatchButtons(ScrollView controls, IEnumerable<LivePatchReadModel> patches) {
			foreach (var patch in patches) {
				var patchId = patch.Id;
				var button = new Button(() => ChoosePatch(patchId)) {
					name = "patch-" + patchId,
					text = patch.Name,
					userData = patchId
				};
				button.AddToClassList("catalog-button");
				button.AddToClassList(GetPatchRoleClass(patch.Role));
				controls.Add(button);
			}
		}

		private void AddEffectNodeButtons(IEnumerable<LiveEffectNodeReadModel> effects) {
			var categoryIndex = 0;
			foreach (var categoryGroup in effects.GroupBy(effect => effect.Category, StringComparer.Ordinal)) {
				var category = categoryGroup.Key;
				var header = new Button(() => _host?.ToggleEffectCategory(category)) {
					name = "effect-category-" + categoryIndex,
					text = "▶ " + category,
					userData = category
				};
				header.AddToClassList("effect-category-button");
				m_EffectNodeControls.Add(header);
				var items = new VisualElement {
					name = "effect-category-items-" + categoryIndex,
					userData = category
				};
				items.AddToClassList("effect-category-items");
				foreach (var effect in categoryGroup) {
					var typeId = effect.TypeId;
					var button = new Button(() => _host?.SelectEffectNode(typeId)) {
						name = "effect-node-" + typeId,
						text = effect.Name,
						tooltip = effect.Category + " · " + typeId,
						userData = typeId
					};
					button.AddToClassList("catalog-button");
					button.AddToClassList("effect-node-button");
					items.Add(button);
				}
				m_EffectNodeControls.Add(items);
				categoryIndex++;
			}
		}

		private static string GetPatchRoleClass(LivePatchRole role) {
			switch (role) {
				case LivePatchRole.Main: return "patch-main-button";
				case LivePatchRole.Overlay: return "patch-overlay-button";
				default: throw new ArgumentOutOfRangeException(nameof(role), role, null);
			}
		}

		private void RefreshMainCueSlots(LiveUiReadModel model) {
			for (var index = 0; index < m_MainCueSlots.Length; index++) {
				var patchId = index < _host.MainCuePatchIds.Count ? _host.MainCuePatchIds[index] : string.Empty;
				var patch = model.Patches.FirstOrDefault(candidate => candidate.Role == LivePatchRole.Main && candidate.Id == patchId);
				var assigned = !string.IsNullOrEmpty(patch.Id);
				m_MainCueSlots[index].Q<Label>().text = assigned ? patch.Name : "Cue Slot " + (index + 1);
				m_MainCueSlots[index].EnableInClassList("is-active", assigned && index == _host.ActiveMainCueIndex);
				m_MainCueSlots[index].tooltip = !assigned
					? "CUE SLOT " + (index + 1) + " · ASSIGN MAIN SCENE"
					: patch.Name + " · RIGHT-CLICK TO UNASSIGN";
				var preview = index < model.MainCuePreviews.Count ? model.MainCuePreviews[index] : null;
				if (m_MainCuePreviewTextures[index] == preview) continue;
				m_MainCuePreviewTextures[index] = preview;
				ApplyPreviewTexture(m_MainCueSlots[index], preview);
				m_MainCueSlots[index].EnableInClassList("has-preview", preview != null);
			}
		}

		private void OnPatchPointerDown(PointerDownEvent evt) {
			if (evt.button == 1) {
				var rightClickTarget = evt.target as VisualElement;
				var rightClickButton = rightClickTarget as Button ?? rightClickTarget?.GetFirstAncestorOfType<Button>();
				if (_host != null && rightClickButton?.userData is SequencerLaneAddress laneAddress
					&& laneAddress.Kind == LiveSequencerKind.Overlay) {
					ShowSequencerRejection(_host.UnassignOverlayPatchFromLane(laneAddress.LaneIndex));
					evt.StopImmediatePropagation();
					return;
				}
				var cueSlot = rightClickTarget;
				var cueIndex = Array.IndexOf(m_MainCueSlots, cueSlot);
				while (cueIndex < 0 && cueSlot != null) {
					cueSlot = cueSlot.parent;
					cueIndex = Array.IndexOf(m_MainCueSlots, cueSlot);
				}
				if (_host != null && cueIndex >= 0) {
					ShowEnqueueRejection(_host.UnassignMainPatchFromCue(cueIndex));
					evt.StopImmediatePropagation();
				}
				return;
			}
			if (!TryBeginPatchDrag(evt.target as VisualElement, evt.button, evt.pointerId, evt.position)) return;
			evt.StopImmediatePropagation();
		}

		private void OnPatchMouseDown(MouseDownEvent evt) {
			if (!TryBeginPatchDrag(evt.target as VisualElement, evt.button, PointerId.mousePointerId, evt.mousePosition)) return;
			evt.StopImmediatePropagation();
		}

		private bool TryBeginPatchDrag(VisualElement target, int buttonIndex, int pointerId, Vector2 position) {
			if (buttonIndex != 0 || m_PatchDragPointerId >= 0) return false;
			var button = target as Button ?? target?.GetFirstAncestorOfType<Button>();
			if (button == null || button.userData is not string patchId) return false;
			if (button.ClassListContains("patch-main-button")) m_DraggedPatchRole = LivePatchRole.Main;
			else if (button.ClassListContains("patch-overlay-button")) m_DraggedPatchRole = LivePatchRole.Overlay;
			else return false;
			m_PatchDragSource = button;
			m_DraggedPatchId = patchId;
			m_PatchDragPointerId = pointerId;
			m_PatchDragStart = position;
			m_PatchDragCurrent = position;
			m_IsPatchDragging = true;
			m_PatchDragSource.AddToClassList("is-dragging");
			m_PatchDragStroke.AddToClassList("is-active");
			m_PatchDragStroke.MarkDirtyRepaint();
			m_Root.CapturePointer(pointerId);
			return true;
		}

		private void OnPatchPointerMove(PointerMoveEvent evt) {
			if (evt.pointerId != m_PatchDragPointerId || m_PatchDragSource == null) return;
			m_PatchDragCurrent = evt.position;
			m_PatchDragStroke.MarkDirtyRepaint();
			UpdatePatchDropTarget(evt.position);
			evt.StopImmediatePropagation();
		}

		private void OnPatchMouseMove(MouseMoveEvent evt) {
			if (m_PatchDragPointerId != PointerId.mousePointerId || m_PatchDragSource == null) return;
			m_PatchDragCurrent = evt.mousePosition;
			m_PatchDragStroke.MarkDirtyRepaint();
			UpdatePatchDropTarget(evt.mousePosition);
			evt.StopImmediatePropagation();
		}

		private void OnPatchPointerUp(PointerUpEvent evt) {
			if (evt.pointerId != m_PatchDragPointerId) return;
			CompletePatchDrag(evt.position);
			evt.StopImmediatePropagation();
		}

		private void OnPatchMouseUp(MouseUpEvent evt) {
			if (m_PatchDragPointerId != PointerId.mousePointerId) return;
			CompletePatchDrag(evt.mousePosition);
			evt.StopImmediatePropagation();
		}

		private void CompletePatchDrag(Vector2 pointerPosition) {
			if (m_IsPatchDragging) {
				UpdatePatchDropTarget(pointerPosition);
				if (m_DraggedPatchRole == LivePatchRole.Main) {
					var cueIndex = Array.IndexOf(m_MainCueSlots, m_PatchDropTarget);
					var rejected = m_PatchDropTarget?.ClassListContains("is-active") == true;
					if (cueIndex >= 0 && !rejected && !(_host?.AssignMainPatchToCue(cueIndex, m_DraggedPatchId) ?? false))
						_diagnosticLabel.text = "Only Main scenes can be assigned to a Cue Slot.";
				}
				else if (m_PatchDropTarget?.userData is SequencerLaneAddress laneAddress) {
					m_LastPatchDropFrame = Time.frameCount;
					ShowSequencerRejection(_host.AssignOverlayPatchToLane(laneAddress.LaneIndex, m_DraggedPatchId));
				}
			}
			CancelPatchDrag();
		}

		private void OnPatchPointerCancel(PointerCancelEvent evt) {
			if (evt.pointerId == m_PatchDragPointerId) CancelPatchDrag();
		}

		private void DrawPatchDragStroke(MeshGenerationContext context) {
			if (!m_IsPatchDragging || m_PatchDragStroke == null) return;
			var painter = context.painter2D;
			painter.lineWidth = 2f;
			painter.strokeColor = m_PatchDragStroke.ClassListContains("is-rejected")
				? new Color32(255, 72, 72, 255)
				: new Color32(255, 190, 74, 255);
			painter.BeginPath();
			painter.MoveTo(m_PatchDragStroke.WorldToLocal(m_PatchDragStart));
			painter.LineTo(m_PatchDragStroke.WorldToLocal(m_PatchDragCurrent));
			painter.Stroke();
		}

		private void UpdatePatchDropTarget(Vector2 pointerPosition) {
			var target = m_DraggedPatchRole == LivePatchRole.Main
				? m_MainCueSlots.FirstOrDefault(slot => slot.worldBound.Contains(pointerPosition))
				: m_SequencerControls.Query<VisualElement>(className: "sequencer-lane").ToList()
					.FirstOrDefault(lane => lane.userData is SequencerLaneAddress address && address.Kind == LiveSequencerKind.Overlay
						&& lane.worldBound.Contains(pointerPosition));
			if (target == m_PatchDropTarget) return;
			m_PatchDropTarget?.RemoveFromClassList("is-drop-target");
			m_PatchDropTarget = target;
			var rejected = m_DraggedPatchRole == LivePatchRole.Main && m_PatchDropTarget?.ClassListContains("is-active") == true;
			if (!rejected) m_PatchDropTarget?.AddToClassList("is-drop-target");
			m_PatchDragStroke.EnableInClassList("is-rejected", rejected);
			m_PatchDragStroke.MarkDirtyRepaint();
		}

		private void CancelPatchDrag() {
			var pointerId = m_PatchDragPointerId;
			m_PatchDragSource?.RemoveFromClassList("is-dragging");
			m_PatchDropTarget?.RemoveFromClassList("is-drop-target");
			m_PatchDragStroke?.RemoveFromClassList("is-active");
			m_PatchDragStroke?.RemoveFromClassList("is-rejected");
			m_PatchDragStroke?.MarkDirtyRepaint();
			if (pointerId >= 0 && m_Root?.HasPointerCapture(pointerId) == true) m_Root.ReleasePointer(pointerId);
			m_PatchDragSource = null;
			m_DraggedPatchId = string.Empty;
			m_PatchDragPointerId = -1;
			m_IsPatchDragging = false;
			m_PatchDropTarget = null;
		}

		private void ChoosePatch(string patchId) {
			if (_host == null) return;
			if (!_host.SelectCatalogPatch(patchId)) _diagnosticLabel.text = "The selected scene does not exist.";
		}

		private void CenterCatalogSelection(LiveCatalogRole role, string itemId) {
			if (string.IsNullOrWhiteSpace(itemId)) return;
			m_PendingCenteredCatalogItemId = itemId;
			m_PatchControls.schedule.Execute(() => {
				if (m_PendingCenteredCatalogItemId != itemId) return;
				var selected = role == LiveCatalogRole.Effect
					? itemId.StartsWith("category:", StringComparison.Ordinal)
						? m_EffectNodeControls.Query<Button>(className: "effect-category-button").ToList()
							.FirstOrDefault(header => string.Equals(header.userData as string, itemId.Substring("category:".Length), StringComparison.Ordinal))
						: m_EffectNodeControls.Q<Button>("effect-node-" + itemId)
					: m_PatchControls.Q<Button>("patch-" + itemId);
				if (selected == null) {
					m_PendingCenteredCatalogItemId = string.Empty;
					return;
				}
				var controls = GetCatalogControls(selected);
				var viewportHeight = controls.contentViewport.layout.height;
				if (float.IsNaN(viewportHeight) || viewportHeight <= 0f) {
					m_PendingCenteredCatalogItemId = string.Empty;
					return;
				}
				var selectedCenter = selected.ChangeCoordinatesTo(controls.contentContainer,
					new Vector2(selected.layout.width * 0.5f, selected.layout.height * 0.5f));
				var offset = selectedCenter.y - viewportHeight * 0.5f;
				var maximum = Mathf.Max(0f, controls.verticalScroller.highValue);
				controls.scrollOffset = new Vector2(controls.scrollOffset.x, Mathf.Clamp(offset, 0f, maximum));
				m_CenteredCatalogItemId = itemId;
				m_PendingCenteredCatalogItemId = string.Empty;
			}).StartingIn(0);
		}

		private ScrollView GetCatalogControls(VisualElement button) {
			if (button.ClassListContains("patch-main-button")) return m_MainPatchControls;
			if (button.ClassListContains("patch-overlay-button")) return m_OverlayPatchControls;
			return m_EffectNodeControls;
		}

		private void RefreshTempoControls(LiveUiReadModel model) {
			_tempoControls.RemoveFromClassList("is-hidden");
			if (!_editingBpm) _bpmField.SetValueWithoutNotify(FormatBpm(model.Bpm.Value));
			m_TimeEasingButton.text = model.IsTimeEasingEnabled ? "TIME EASE ON" : "TIME EASE OFF";
			m_TimeEasingButton.EnableInClassList("is-time-easing-enabled", model.IsTimeEasingEnabled);
		}

		private void OnBpmInputChanged(ChangeEvent<string> change) {
			if (_updating) return;
			if (!TryParseBpm(change.newValue, out var bpm)) {
				_diagnosticLabel.text = "BPM must be a positive number.";
				return;
			}
			QueueBpm(bpm);
		}

		private void OnBpmFocusIn(FocusInEvent _) => _editingBpm = true;
		private void OnBpmFocusOut(FocusOutEvent _) => _editingBpm = false;

		private void AlignBeat() {
			if (_host == null) return;
			ShowEnqueueRejection(_host.ParameterQueue.EnqueueAlignBeat());
		}

		private void ToggleTimeEasing() {
			if (_host?.ReadModel == null) return;
			ShowEnqueueRejection(_host.ParameterQueue.EnqueueSetTimeEasingEnabled(!_host.ReadModel.IsTimeEasingEnabled));
		}

		private void TapBpm() {
			_host?.TapBpm(Time.unscaledTimeAsDouble);
		}

		private void QueueBpm(float bpm) {
			if (_host?.ReadModel == null) return;
			var definition = _host.ReadModel.Bpm;
			ShowEnqueueRejection(_host.ParameterQueue.EnqueueSetBpm(Mathf.Clamp(bpm, definition.Minimum, definition.Maximum)));
		}

		private static bool TryParseBpm(string text, out float bpm) {
			if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out bpm) && bpm > 0f && !float.IsInfinity(bpm)) return true;
			return float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out bpm) && bpm > 0f && !float.IsInfinity(bpm);
		}

		private static string FormatBpm(float bpm) => bpm.ToString("0.##", CultureInfo.InvariantCulture);

		private static void ApplyPreviewTexture(VisualElement preview, RenderTexture texture) {
			if (preview == null) return;
			preview.style.backgroundImage = texture == null
				? StyleKeyword.None
				: new StyleBackground(Background.FromRenderTexture(texture));
		}

		private void ShowEnqueueRejection(LiveParameterEnqueueResult result) {
			if (!result.Accepted && _diagnosticLabel != null) _diagnosticLabel.text = result.RejectionReason;
		}

		private void ShowSequencerRejection(LiveSequencerOperationResult result) {
			if (!result.Accepted && _diagnosticLabel != null) _diagnosticLabel.text = result.RejectionReason;
		}

		private static string ResolveDiagnostic(LiveUiReadModel model) {
			if (!string.IsNullOrEmpty(model.Diagnostic)) return model.Diagnostic;
			var rejection = model.RequestResults.LastOrDefault(result => !result.Applied);
			if (!string.IsNullOrEmpty(rejection.RejectionReason)) return rejection.RejectionReason;
			return model.DisplayError;
		}

		private static string FormatParameterValue(ParameterValue value) {
			switch (value.Type) {
				case ParameterType.Float: return value.AsFloat().ToString("0.00", CultureInfo.InvariantCulture);
				case ParameterType.Int: return value.AsInt().ToString(CultureInfo.InvariantCulture);
				case ParameterType.Bool: return value.AsBool() ? "On" : "Off";
				default: return value.ToString();
			}
		}

		private static T Required<T>(VisualElement root, string name) where T : VisualElement {
			var element = root.Q<T>(name);
			return element ?? throw new InvalidOperationException($"The live UXML requires '{name}'.");
		}
	}
}
