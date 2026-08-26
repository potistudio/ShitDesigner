using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShitDesigner.Main {
	/// <summary>Reflects the latest completed live frame and queues only scene and public-parameter requests.</summary>
	[DisallowMultipleComponent]
	[DefaultExecutionOrder(1100)]
	public sealed class LiveUiController : MonoBehaviour {
		[SerializeField] private UIDocument _document;

		private ApplicationLiveHost _host;
		private LiveExternalDisplayOutput _output;
		private VisualElement _programMonitor;
		private DropdownField _sceneSelector;
		private VisualElement _parameterControls;
		private Label _capabilityLabel;
		private Label _diagnosticLabel;
		private Button _outputButton;
		private Button _identifyButton;
		private Label _displaySelector;
		private Button _confirmationCancelButton;
		private Button _confirmationConfirmButton;
		private Label _confirmationDisplaySelector;
		private Label _confirmationTitle;
		private Label _confirmationMessage;
		private VisualElement _confirmationOverlay;
		private string _renderedSceneId = string.Empty;
		private readonly HashSet<Slider> _activeSliderInteractions = new HashSet<Slider>();
		private bool _pendingOutputActive;
		private bool _showingOutputError;
		private bool _initialized;
		private bool _updating;

		public void Initialize(ApplicationLiveHost host, LiveExternalDisplayOutput output) {
			_host = host ?? throw new ArgumentNullException(nameof(host));
			_output = output ?? throw new ArgumentNullException(nameof(output));
			if (_document == null) throw new InvalidOperationException("A dedicated live UIDocument is required.");
			var root = _document.rootVisualElement;
			if (root == null) throw new InvalidOperationException("The live UIDocument has no visual tree.");

			_programMonitor = Required<VisualElement>(root, "program-monitor");
			_sceneSelector = Required<DropdownField>(root, "scene-selector");
			_parameterControls = Required<VisualElement>(root, "parameter-controls");
			_capabilityLabel = Required<Label>(root, "capability-status");
			_diagnosticLabel = Required<Label>(root, "diagnostic-status");
			_outputButton = Required<Button>(root, "output-toggle");
			_identifyButton = Required<Button>(root, "identify-display");
			_displaySelector = Required<Label>(root, "display-selector");
			_confirmationCancelButton = Required<Button>(root, "output-confirm-cancel");
			_confirmationConfirmButton = Required<Button>(root, "output-confirm-accept");
			_confirmationDisplaySelector = Required<Label>(root, "output-confirm-display-selector");
			_confirmationTitle = Required<Label>(root, "output-confirm-title");
			_confirmationMessage = Required<Label>(root, "output-confirm-message");
			_confirmationOverlay = Required<VisualElement>(root, "output-confirm-overlay");
			_sceneSelector.RegisterValueChangedCallback(OnSceneSelected);
			_outputButton.clicked += RequestOutputToggle;
			_identifyButton.clicked += _output.IdentifyDisplay;
			_confirmationCancelButton.clicked += HideOutputConfirmation;
			_confirmationConfirmButton.clicked += ConfirmOutputToggle;
			HideOutputConfirmation();
			_initialized = true;
		}

		public void Shutdown() {
			if (_sceneSelector != null) _sceneSelector.UnregisterValueChangedCallback(OnSceneSelected);
			if (_outputButton != null) _outputButton.clicked -= RequestOutputToggle;
			if (_identifyButton != null && _output != null) _identifyButton.clicked -= _output.IdentifyDisplay;
			if (_confirmationCancelButton != null) _confirmationCancelButton.clicked -= HideOutputConfirmation;
			if (_confirmationConfirmButton != null) _confirmationConfirmButton.clicked -= ConfirmOutputToggle;
			_initialized = false;
			_host = null;
			_output = null;
			_renderedSceneId = string.Empty;
			_activeSliderInteractions.Clear();
		}

		private void LateUpdate() {
			if (!_initialized || _host.ReadModel == null) return;
			var model = _host.ReadModel;
			_updating = true;
			try {
				_programMonitor.style.backgroundImage = model.ProgramTexture == null
					? StyleKeyword.None
					: new StyleBackground(Background.FromRenderTexture(model.ProgramTexture));
				_sceneSelector.choices = model.Scenes.Select(scene => scene.Name).ToList();
				var selected = model.Scenes.FirstOrDefault(scene => scene.Id == model.SelectedSceneId);
				if (!string.IsNullOrEmpty(selected.Id)) _sceneSelector.SetValueWithoutNotify(selected.Name);
				var connectedDisplayLabels = Enumerable.Range(2, Math.Max(0, model.ConnectedDisplayCount - 1)).Select(number => "Display " + number).ToList();
				if (connectedDisplayLabels.Count == 0) connectedDisplayLabels.Add("No external Display");
				_displaySelector.text = "OUTPUT: " + string.Join(", ", connectedDisplayLabels);
				_outputButton.text = model.IsDisplayOutputActive ? "STOP OUTPUT" : "START OUTPUT";
				_capabilityLabel.text = $"MIDI: {(model.Capabilities.MidiAvailable ? "READY" : "UNAVAILABLE")}  DISPLAY: {(model.Capabilities.ExternalDisplayAvailable ? "READY" : "UNAVAILABLE")}  FRAME: {model.ProgramFrameNumber}";
				_diagnosticLabel.text = ResolveDiagnostic(model);
				if (_renderedSceneId != model.SelectedSceneId) RebuildParameters(model);
				else RefreshParameterValues(model);
			}
			finally { _updating = false; }
		}

		private void RebuildParameters(LiveUiReadModel model) {
			_activeSliderInteractions.Clear();
			_parameterControls.Clear();
			foreach (var parameter in model.Parameters) {
				var slider = new Slider(parameter.DisplayName, parameter.Minimum, parameter.Maximum) {
					name = "parameter-" + parameter.Id,
					value = parameter.Value,
					userData = parameter.Id
				};
				slider.AddToClassList("parameter-slider");
				slider.RegisterValueChangedCallback(change => {
					if (!_updating && _host?.ReadModel != null)
						ShowEnqueueRejection(_host.ParameterQueue.EnqueueSetParameter(_host.ReadModel.SelectedSceneId, (string)slider.userData, change.newValue));
				});
				slider.RegisterCallback<PointerDownEvent>(_ => _activeSliderInteractions.Add(slider), TrickleDown.TrickleDown);
				slider.RegisterCallback<PointerUpEvent>(_ => _activeSliderInteractions.Remove(slider), TrickleDown.TrickleDown);
				slider.RegisterCallback<PointerCaptureOutEvent>(_ => _activeSliderInteractions.Remove(slider), TrickleDown.TrickleDown);
				_parameterControls.Add(slider);
			}
			_renderedSceneId = model.SelectedSceneId;
		}

		private void RefreshParameterValues(LiveUiReadModel model) {
			foreach (var parameter in model.Parameters) {
				var slider = _parameterControls.Q<Slider>("parameter-" + parameter.Id);
				if (slider != null && !_activeSliderInteractions.Contains(slider)) slider.SetValueWithoutNotify(parameter.Value);
			}
		}

		private void OnSceneSelected(ChangeEvent<string> change) {
			if (_updating || _host?.ReadModel == null) return;
			var scene = _host.ReadModel.Scenes.FirstOrDefault(candidate => candidate.Name == change.newValue);
			if (!string.IsNullOrEmpty(scene.Id)) ShowEnqueueRejection(_host.ParameterQueue.EnqueueSelectScene(scene.Id));
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

		private static string ResolveDiagnostic(LiveUiReadModel model) {
			if (!string.IsNullOrEmpty(model.Diagnostic)) return model.Diagnostic;
			var rejection = model.RequestResults.LastOrDefault(result => !result.Applied);
			if (!string.IsNullOrEmpty(rejection.RejectionReason)) return rejection.RejectionReason;
			return model.DisplayError;
		}

		private static T Required<T>(VisualElement root, string name) where T : VisualElement {
			var element = root.Q<T>(name);
			return element ?? throw new InvalidOperationException($"The live UXML requires '{name}'.");
		}
	}
}
