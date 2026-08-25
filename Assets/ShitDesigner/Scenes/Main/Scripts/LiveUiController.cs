using System;
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
		private DropdownField _displaySelector;
		private string _renderedSceneId = string.Empty;
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
			_displaySelector = Required<DropdownField>(root, "display-selector");
			_sceneSelector.RegisterValueChangedCallback(OnSceneSelected);
			_displaySelector.RegisterValueChangedCallback(OnDisplaySelected);
			_outputButton.clicked += ToggleOutput;
			_identifyButton.clicked += _output.IdentifyDisplay;
			_initialized = true;
		}

		public void Shutdown() {
			if (_sceneSelector != null) _sceneSelector.UnregisterValueChangedCallback(OnSceneSelected);
			if (_displaySelector != null) _displaySelector.UnregisterValueChangedCallback(OnDisplaySelected);
			if (_outputButton != null) _outputButton.clicked -= ToggleOutput;
			if (_identifyButton != null && _output != null) _identifyButton.clicked -= _output.IdentifyDisplay;
			_initialized = false;
			_host = null;
			_output = null;
			_renderedSceneId = string.Empty;
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
				_displaySelector.choices = Enumerable.Range(2, Math.Max(0, model.ConnectedDisplayCount - 1)).Select(number => "Display " + number).ToList();
				_displaySelector.SetValueWithoutNotify("Display " + model.SelectedDisplayNumber);
				_outputButton.text = model.IsDisplayOutputActive ? "STOP OUTPUT" : "START OUTPUT";
				_capabilityLabel.text = $"MIDI: {(model.Capabilities.MidiAvailable ? "READY" : "UNAVAILABLE")}  DISPLAY: {(model.Capabilities.ExternalDisplayAvailable ? "READY" : "UNAVAILABLE")}  FRAME: {model.ProgramFrameNumber}";
				_diagnosticLabel.text = ResolveDiagnostic(model);
				if (_renderedSceneId != model.SelectedSceneId) RebuildParameters(model);
				else RefreshParameterValues(model);
			}
			finally { _updating = false; }
		}

		private void RebuildParameters(LiveUiReadModel model) {
			_parameterControls.Clear();
			foreach (var parameter in model.Parameters) {
				var slider = new Slider(parameter.DisplayName, parameter.Minimum, parameter.Maximum) {
					name = "parameter-" + parameter.Id,
					value = parameter.Value,
					userData = parameter.Id
				};
				slider.RegisterValueChangedCallback(change => {
					if (!_updating && _host?.ReadModel != null)
						ShowEnqueueRejection(_host.ParameterQueue.EnqueueSetParameter(_host.ReadModel.SelectedSceneId, (string)slider.userData, change.newValue));
				});
				_parameterControls.Add(slider);
			}
			_renderedSceneId = model.SelectedSceneId;
		}

		private void RefreshParameterValues(LiveUiReadModel model) {
			foreach (var parameter in model.Parameters)
				_parameterControls.Q<Slider>("parameter-" + parameter.Id)?.SetValueWithoutNotify(parameter.Value);
		}

		private void OnSceneSelected(ChangeEvent<string> change) {
			if (_updating || _host?.ReadModel == null) return;
			var scene = _host.ReadModel.Scenes.FirstOrDefault(candidate => candidate.Name == change.newValue);
			if (!string.IsNullOrEmpty(scene.Id)) ShowEnqueueRejection(_host.ParameterQueue.EnqueueSelectScene(scene.Id));
		}

		private void OnDisplaySelected(ChangeEvent<string> change) {
			if (_updating || _output == null || string.IsNullOrEmpty(change.newValue)) return;
			if (int.TryParse(change.newValue.Replace("Display ", string.Empty), out var number)) _output.SelectDisplay(number);
		}

		private void ToggleOutput() => _output?.SetOutputActive(!_output.IsOutputActive);

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
