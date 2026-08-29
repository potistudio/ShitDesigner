using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ShitDesigner.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShitDesigner.Main {
	/// <summary>Reflects the latest completed live frame and queues patch and parameter requests.</summary>
	[DisallowMultipleComponent]
	[DefaultExecutionOrder(1100)]
	public sealed class PanelRenderer : MonoBehaviour {
		[SerializeField] private UIDocument _document;

		private VisualElement m_Root;
		private ApplicationLiveHost _host;
		private LiveExternalDisplayOutput _output;
		private VisualElement _programMonitor;
		private VisualElement m_Output2Preview;
		private VisualElement _patchSlotControls;
		private readonly RenderTexture[] m_PatchSlotPreviewTextures = new RenderTexture[LivePatchSlots.Capacity];
		private VisualElement _patchControls;
		private ScrollView m_MainPatchControls;
		private ScrollView m_OverlayPatchControls;
		private VisualElement m_SequencerControls;
		private VisualElement _parameterControls;
		private VisualElement _tempoControls;
		private TextField _bpmField;
		private Button _bpmTapButton;
		private Button m_BeatAlignmentButton;
		private Label _capabilityLabel;
		private Label _diagnosticLabel;
		private Button _outputButton;
		private Button _identifyButton;
		private Button _confirmationCancelButton;
		private Button _confirmationConfirmButton;
		private Label _confirmationDisplaySelector;
		private Label _confirmationTitle;
		private Label _confirmationMessage;
		private VisualElement _confirmationOverlay;
		private string _renderedPatchId = string.Empty;
		private string _centeredPatchId = string.Empty;
		private int m_RenderedPatchCount = -1;
		private bool _pendingOutputActive;
		private bool _showingOutputError;
		private bool _initialized;
		private bool _updating;
		private bool _editingBpm;

		private const float PatchScrollWheelUnits = 48f;

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
			if (_document == null) throw new InvalidOperationException("A dedicated live UIDocument is required.");
			m_Root = _document.rootVisualElement;
			if (m_Root == null) throw new InvalidOperationException("The live UIDocument has no visual tree.");
			var root = m_Root;

			_programMonitor = Required<VisualElement>(root, "program-monitor");
			m_Output2Preview = Required<VisualElement>(root, "output-2-preview");
			_patchSlotControls = Required<VisualElement>(root, "patch-slot-controls");
			for (var slotIndex = 0; slotIndex < LivePatchSlots.Capacity; slotIndex++) {
				var button = Required<Button>(root, "patch-slot-" + slotIndex);
				button.userData = slotIndex;
			}
			_patchSlotControls.RegisterCallback<ClickEvent>(OnPatchSlotClicked);
			_patchControls = Required<VisualElement>(root, "patch-controls");
			m_MainPatchControls = Required<ScrollView>(root, "main-patch-controls");
			m_OverlayPatchControls = Required<ScrollView>(root, "overlay-patch-controls");
			m_SequencerControls = Required<VisualElement>(root, "sequencer-controls");
			_parameterControls = Required<VisualElement>(root, "parameter-controls");
			_tempoControls = Required<VisualElement>(root, "tempo-controls");
			_bpmField = Required<TextField>(root, "bpm-field");
			_bpmTapButton = Required<Button>(root, "bpm-tap");
			m_BeatAlignmentButton = Required<Button>(root, "beat-alignment-button");
			_capabilityLabel = Required<Label>(root, "capability-status");
			_diagnosticLabel = Required<Label>(root, "diagnostic-status");
			_outputButton = Required<Button>(root, "output-toggle");
			_identifyButton = Required<Button>(root, "identify-display");
			_confirmationCancelButton = Required<Button>(root, "output-confirm-cancel");
			_confirmationConfirmButton = Required<Button>(root, "output-confirm-accept");
			_confirmationDisplaySelector = Required<Label>(root, "output-confirm-display-selector");
			_confirmationTitle = Required<Label>(root, "output-confirm-title");
			_confirmationMessage = Required<Label>(root, "output-confirm-message");
			_confirmationOverlay = Required<VisualElement>(root, "output-confirm-overlay");
			m_Root.RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit, TrickleDown.TrickleDown);
			m_MainPatchControls.RegisterCallback<WheelEvent>(OnMainPatchSelectionWheel, TrickleDown.TrickleDown);
			m_OverlayPatchControls.RegisterCallback<WheelEvent>(OnOverlayPatchSelectionWheel, TrickleDown.TrickleDown);
			m_SequencerControls.RegisterCallback<ClickEvent>(OnSequencerCellClicked);
			BuildSequencers(root);
			_bpmField.RegisterValueChangedCallback(OnBpmInputChanged);
			_bpmField.RegisterCallback<FocusInEvent>(OnBpmFocusIn);
			_bpmField.RegisterCallback<FocusOutEvent>(OnBpmFocusOut);
			_bpmTapButton.clicked += TapBpm;
			m_BeatAlignmentButton.clicked += AlignBeat;
			_outputButton.clicked += RequestOutputToggle;
			_identifyButton.clicked += _output.IdentifyDisplay;
			_confirmationCancelButton.clicked += HideOutputConfirmation;
			_confirmationConfirmButton.clicked += ConfirmOutputToggle;
			HideOutputConfirmation();
			_initialized = true;
		}

		public void Shutdown() {
			if (m_Root != null) m_Root.UnregisterCallback<NavigationSubmitEvent>(OnNavigationSubmit, TrickleDown.TrickleDown);
			if (_patchSlotControls != null) _patchSlotControls.UnregisterCallback<ClickEvent>(OnPatchSlotClicked);
			if (m_MainPatchControls != null) m_MainPatchControls.UnregisterCallback<WheelEvent>(OnMainPatchSelectionWheel, TrickleDown.TrickleDown);
			if (m_OverlayPatchControls != null) m_OverlayPatchControls.UnregisterCallback<WheelEvent>(OnOverlayPatchSelectionWheel, TrickleDown.TrickleDown);
			if (m_SequencerControls != null) m_SequencerControls.UnregisterCallback<ClickEvent>(OnSequencerCellClicked);
			if (_bpmField != null) {
				_bpmField.UnregisterValueChangedCallback(OnBpmInputChanged);
				_bpmField.UnregisterCallback<FocusInEvent>(OnBpmFocusIn);
				_bpmField.UnregisterCallback<FocusOutEvent>(OnBpmFocusOut);
			}
			if (_bpmTapButton != null) _bpmTapButton.clicked -= TapBpm;
			if (m_BeatAlignmentButton != null) m_BeatAlignmentButton.clicked -= AlignBeat;
			if (_outputButton != null) _outputButton.clicked -= RequestOutputToggle;
			if (_identifyButton != null && _output != null) _identifyButton.clicked -= _output.IdentifyDisplay;
			if (_confirmationCancelButton != null) _confirmationCancelButton.clicked -= HideOutputConfirmation;
			if (_confirmationConfirmButton != null) _confirmationConfirmButton.clicked -= ConfirmOutputToggle;
			_initialized = false;
			m_Root = null;
			_host = null;
			_output = null;
			_renderedPatchId = string.Empty;
			_centeredPatchId = string.Empty;
			m_RenderedPatchCount = -1;
			Array.Clear(m_PatchSlotPreviewTextures, 0, m_PatchSlotPreviewTextures.Length);
		}

		private void LateUpdate() {
			if (!_initialized || _host.ReadModel == null) return;
			var model = _host.ReadModel;
			_updating = true;
			try {
				ApplyPreviewTexture(_programMonitor, model.ProgramFrames.Count > 0 ? model.ProgramFrames[0].Texture : null);
				ApplyPreviewTexture(m_Output2Preview, model.ProgramFrames.Count > 1 ? model.ProgramFrames[1].Texture : null);
				RefreshPatchSlotControls(model);
				RefreshPatchControls(model);
				RefreshSequencers(model);
				RefreshTempoControls(model);
				_outputButton.text = model.IsDisplayOutputActive ? "STOP OUTPUT" : "START OUTPUT";
				_capabilityLabel.text = $"MIDI: {(model.Capabilities.MidiAvailable ? "READY" : "UNAVAILABLE")}  DISPLAY: {(model.Capabilities.ExternalDisplayAvailable ? "READY" : "UNAVAILABLE")}  FRAME: {model.ProgramFrameNumber}";
				_diagnosticLabel.text = ResolveDiagnostic(model);
				if (_renderedPatchId != model.LoadedPatchId) RebuildParameters(model);
				else RefreshParameterValues(model);
			}
			finally { _updating = false; }
		}

		private void BuildSequencers(VisualElement root) {
			foreach (var sequencer in _host.Sequencers) {
				var container = Required<VisualElement>(root, GetSequencerElementName(sequencer.Kind));
				container.Clear();
				container.Add(new Label(sequencer.DisplayName) { name = "sequencer-title-" + GetSequencerId(sequencer.Kind) });
				container[0].AddToClassList("sequencer-title");

				var header = new VisualElement();
				header.AddToClassList("sequencer-beat-header");
				var corner = new Label("LANE");
				corner.AddToClassList("sequencer-corner");
				header.Add(corner);
				for (var stepIndex = 0; stepIndex < LiveStepSequencer.StepCount; stepIndex++) {
					var beat = new Label((stepIndex + 1).ToString(CultureInfo.InvariantCulture));
					beat.AddToClassList("sequencer-beat-label");
					header.Add(beat);
				}
				container.Add(header);

				for (var laneIndex = 0; laneIndex < LiveStepSequencer.LaneCount; laneIndex++) {
					var lane = new VisualElement();
					lane.AddToClassList("sequencer-lane");
					VisualElement laneLabel;
					if (sequencer.Kind == LiveSequencerKind.Overlay) {
						laneLabel = new Button {
							name = GetSequencerLaneName(sequencer.Kind, laneIndex),
							text = (laneIndex + 1).ToString(CultureInfo.InvariantCulture),
							userData = new SequencerLaneAddress(sequencer.Kind, laneIndex)
						};
						laneLabel.AddToClassList("is-clickable");
					}
					else {
						laneLabel = new Label((laneIndex + 1).ToString(CultureInfo.InvariantCulture)) {
							name = GetSequencerLaneName(sequencer.Kind, laneIndex)
						};
					}
					laneLabel.AddToClassList("sequencer-lane-label");
					lane.Add(laneLabel);
					for (var stepIndex = 0; stepIndex < LiveStepSequencer.StepCount; stepIndex++) {
						var button = new Button {
							name = GetSequencerCellName(sequencer.Kind, laneIndex, stepIndex),
							userData = new SequencerCellAddress(sequencer.Kind, laneIndex, stepIndex)
						};
						button.AddToClassList("sequencer-step");
						lane.Add(button);
					}
					container.Add(lane);
				}
			}
		}

		private void RefreshSequencers(LiveUiReadModel model) {
			foreach (var sequencer in model.Sequencers) {
				for (var laneIndex = 0; laneIndex < LiveStepSequencer.LaneCount; laneIndex++) {
					var laneLabel = m_SequencerControls.Q<VisualElement>(GetSequencerLaneName(sequencer.Kind, laneIndex));
					if (laneLabel != null) {
						var patchId = sequencer.LanePatchIds.Count > laneIndex ? sequencer.LanePatchIds[laneIndex] : string.Empty;
						var patch = model.Patches.FirstOrDefault(candidate => candidate.Id == patchId);
						laneLabel.tooltip = string.IsNullOrEmpty(patchId)
							? "LANE " + (laneIndex + 1) + " · SELECT OVERLAY SCENE"
							: "LANE " + (laneIndex + 1) + " · " + (string.IsNullOrEmpty(patch.Name) ? patchId : patch.Name);
						laneLabel.EnableInClassList("is-assigned", !string.IsNullOrEmpty(patchId));
						laneLabel.EnableInClassList("is-selecting", sequencer.SelectedLaneIndex == laneIndex);
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
			m_OverlayPatchControls.EnableInClassList("is-scene-selecting", model.Sequencers.Any(sequencer => sequencer.Kind == LiveSequencerKind.Overlay && sequencer.SelectedLaneIndex >= 0));
		}

		private void OnSequencerCellClicked(ClickEvent change) {
			var target = change.target as VisualElement;
			var button = target as Button ?? target?.GetFirstAncestorOfType<Button>();
			if (_host == null || button == null) return;
			if (button.userData is SequencerCellAddress cellAddress) {
				ShowSequencerRejection(_host.CycleSequencerCellMode(cellAddress.Kind, cellAddress.LaneIndex, cellAddress.StepIndex));
				return;
			}
			if (button.userData is SequencerLaneAddress laneAddress)
				ShowSequencerRejection(_host.SelectSequencerLane(laneAddress.Kind, laneAddress.LaneIndex));
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
		}

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
			if (m_RenderedPatchCount != model.Patches.Count) {
				RebuildPatchControls(model);
				_centeredPatchId = string.Empty;
			}
			foreach (var patch in model.Patches) {
				var button = _patchControls.Q<Button>("patch-" + patch.Id);
				if (button == null) continue;
				button.EnableInClassList("is-loaded", patch.Id == model.LoadedPatchId);
				button.EnableInClassList("is-preloaded", patch.Id == model.PreloadedPatchId);
				button.EnableInClassList("is-queued", model.PatchSlots.Any(slot => slot.PatchId == patch.Id));
				button.EnableInClassList("is-selected", patch.Id == model.SelectedCatalogPatchId);
				button.EnableInClassList("is-assignment-option", _host.IsSelectingSequencerLane && patch.Role == LivePatchRole.Overlay);
			}
			var selectedPatchId = string.IsNullOrEmpty(model.SelectedCatalogPatchId) ? model.LoadedPatchId : model.SelectedCatalogPatchId;
			if (_centeredPatchId != selectedPatchId) {
				_centeredPatchId = selectedPatchId;
				CenterPatchSelection(selectedPatchId);
			}
		}

		private void RebuildPatchControls(LiveUiReadModel model) {
			m_MainPatchControls.Clear();
			m_OverlayPatchControls.Clear();
			AddPatchButtons(m_MainPatchControls, model.Patches.Where(patch => patch.Role == LivePatchRole.Main));
			AddPatchButtons(m_OverlayPatchControls, model.Patches.Where(patch => patch.Role == LivePatchRole.Overlay));
			m_RenderedPatchCount = model.Patches.Count;
		}

		private void AddPatchButtons(ScrollView controls, IEnumerable<LivePatchReadModel> patches) {
			foreach (var patch in patches) {
				var patchId = patch.Id;
				var button = new Button(() => ChoosePatch(patchId)) {
					name = "patch-" + patchId,
					text = patch.Name,
					userData = patchId
				};
				button.AddToClassList("patch-button");
				button.AddToClassList(patch.Role == LivePatchRole.Main ? "patch-main-button" : "patch-overlay-button");
				controls.Add(button);
			}
		}

		private void ChoosePatch(string patchId) {
			if (_host == null) return;
			if (_host.IsSelectingSequencerLane) {
				ShowSequencerRejection(_host.AssignSelectedSequencerPatch(patchId));
				return;
			}
			AssignPatchToSelectedSlot(patchId);
		}

		private void RefreshPatchSlotControls(LiveUiReadModel model) {
			foreach (var slot in model.PatchSlots) {
				if (!LivePatchSlots.IsValidSlotIndex(slot.Index)) continue;
				var button = _patchSlotControls.Q<Button>("patch-slot-" + slot.Index);
				if (button == null) continue;
				var preview = slot.Index >= 0 && slot.Index < model.PatchSlotPreviews.Count ? model.PatchSlotPreviews[slot.Index] : null;
				if (m_PatchSlotPreviewTextures[slot.Index] != preview) {
					m_PatchSlotPreviewTextures[slot.Index] = preview;
					ApplyPreviewTexture(button, preview);
				}
				button.tooltip = FormatPatchSlot(slot, model.Patches);
				button.EnableInClassList("is-selected", slot.Index == model.SelectedPatchSlotIndex);
				button.EnableInClassList("is-cued", !slot.IsEmpty && slot.PatchId == model.PreloadedPatchId);
				button.EnableInClassList("is-playing", !slot.IsEmpty && slot.PatchId == model.LoadedPatchId);
			}

		}

		private void OnPatchSlotClicked(ClickEvent change) {
			if (!(change.target is Button button) || !(button.userData is int slotIndex)) return;
			SelectPatchSlot(slotIndex);
		}

		private void SelectPatchSlot(int slotIndex) {
			if (_host == null) return;
			ShowSlotRejection(_host.SelectPatchSlot(slotIndex));
		}

		private void AssignPatchToSelectedSlot(string patchId) {
			if (_host == null) return;
			ShowSlotRejection(_host.AssignPatchToSelectedSlot(patchId));
		}

		private static string FormatPatchSlot(LivePatchSlotReadModel slot, IReadOnlyList<LivePatchReadModel> patches) {
			var patch = patches.FirstOrDefault(candidate => candidate.Id == slot.PatchId);
			var patchName = slot.IsEmpty ? "EMPTY" : (string.IsNullOrEmpty(patch.Name) ? "UNKNOWN" : patch.Name);
			return "SLOT " + (slot.Index + 1) + " · " + patchName;
		}

		private void OnMainPatchSelectionWheel(WheelEvent change) => ScrollPatchRow(m_MainPatchControls, change);

		private void OnOverlayPatchSelectionWheel(WheelEvent change) => ScrollPatchRow(m_OverlayPatchControls, change);

		private static void ScrollPatchRow(ScrollView controls, WheelEvent change) {
			var maximum = Mathf.Max(0f, controls.horizontalScroller.highValue);
			if (maximum <= 0f) return;
			var delta = Mathf.Abs(change.delta.x) > Mathf.Epsilon ? change.delta.x : change.delta.y;
			if (Mathf.Abs(delta) <= Mathf.Epsilon) return;
			controls.scrollOffset = new Vector2(Mathf.Clamp(controls.scrollOffset.x + delta * PatchScrollWheelUnits, 0f, maximum), controls.scrollOffset.y);
			change.StopPropagation();
		}

		private void CenterPatchSelection(string patchId) {
			if (string.IsNullOrWhiteSpace(patchId)) return;
			_patchControls.schedule.Execute(() => {
				var selected = _patchControls.Q<Button>("patch-" + patchId);
				if (selected == null) return;
				var controls = selected.ClassListContains("patch-main-button") ? m_MainPatchControls : m_OverlayPatchControls;
				var viewportWidth = controls.contentViewport.layout.width;
				if (viewportWidth <= 0f) return;
				var selectedCenter = selected.ChangeCoordinatesTo(controls.contentContainer,
					new Vector2(selected.layout.width * 0.5f, selected.layout.height * 0.5f));
				var offset = selectedCenter.x - viewportWidth * 0.5f;
				var maximum = Mathf.Max(0f, controls.horizontalScroller.highValue);
				controls.scrollOffset = new Vector2(Mathf.Clamp(offset, 0f, maximum), controls.scrollOffset.y);
			}).StartingIn(0);
		}

		private void RefreshTempoControls(LiveUiReadModel model) {
			_tempoControls.RemoveFromClassList("is-hidden");
			if (!_editingBpm) _bpmField.SetValueWithoutNotify(FormatBpm(model.Bpm.Value));
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

		private void RequestOutputToggle() {
			if (_output == null) return;
			_showingOutputError = false;
			_confirmationCancelButton.RemoveFromClassList("is-hidden");
			_pendingOutputActive = !_output.IsOutputActive;
			PrepareConfirmationDisplaySelector();
			if (_pendingOutputActive && !_output.IsAvailable) {
				ShowOutputError(UnityEngine.Application.isEditor
					? "External Display output requires a standalone Player."
					: "No external Display is connected.");
				return;
			}

			_confirmationTitle.text = _pendingOutputActive ? "START LIVE OUTPUT?" : "STOP LIVE OUTPUT?";
			_confirmationMessage.text = _pendingOutputActive
				? $"Send Program output to {_output.DisplayIdentity}."
				: "Stop Program output on all external Displays.";
			_confirmationConfirmButton.text = _pendingOutputActive ? "START" : "STOP";
			_confirmationConfirmButton.EnableInClassList("is-stop", !_pendingOutputActive);
			_confirmationOverlay.RemoveFromClassList("is-hidden");
		}

		private void OnNavigationSubmit(NavigationSubmitEvent change) {
			if (!ReferenceEquals(change.target, _outputButton)) return;
			change.StopImmediatePropagation();
		}

		private void PrepareConfirmationDisplaySelector() {
			var labels = Enumerable.Range(2, Math.Max(0, _output.ConnectedDisplayCount - 1)).Select(number => "Display " + number).ToList();
			if (labels.Count == 0) labels.Add("No external Display");
			_confirmationDisplaySelector.text = "OUTPUT DISPLAYS: " + string.Join(", ", labels);
		}

		private void ConfirmOutputToggle() {
			if (_output == null) return;
			if (_showingOutputError) {
				HideOutputConfirmation();
				return;
			}
			if (_output.SetOutputActive(_pendingOutputActive)) HideOutputConfirmation();
			else ShowOutputError(_output.LastError);
		}

		private void ShowOutputError(string message) {
			_showingOutputError = true;
			_confirmationCancelButton.AddToClassList("is-hidden");
			_confirmationTitle.text = "OUTPUT UNAVAILABLE";
			_confirmationMessage.text = message;
			_confirmationConfirmButton.text = "CLOSE";
			_confirmationConfirmButton.RemoveFromClassList("is-stop");
			_confirmationOverlay.RemoveFromClassList("is-hidden");
		}

		private void HideOutputConfirmation() => _confirmationOverlay?.AddToClassList("is-hidden");

		private void ShowEnqueueRejection(LiveParameterEnqueueResult result) {
			if (!result.Accepted && _diagnosticLabel != null) _diagnosticLabel.text = result.RejectionReason;
		}

		private void ShowSlotRejection(LivePatchSlotOperationResult result) {
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
