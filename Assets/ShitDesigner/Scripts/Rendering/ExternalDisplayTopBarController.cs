using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShitDesigner.Rendering {
	[DisallowMultipleComponent]
	[RequireComponent(typeof(PanelRenderer))]
	public sealed class ExternalDisplayTopBarController : MonoBehaviour {
		[SerializeField] private PanelRenderer _panelRenderer;
		[SerializeField] private SimpleExternalDisplayOutput _externalDisplayOutput;

		private Button _liveButton;
		private Label _liveButtonLabel;
		private VisualElement _liveStatus;
		private Label _liveStatusLabel;
		private Button _cancelButton;
		private Button _confirmButton;
		private DropdownField _displaySelector;
		private Label _confirmationTitle;
		private Label _confirmationMessage;
		private VisualElement _confirmationOverlay;
		private bool _pendingActive;
		private int _pendingDisplayNumber;
		private bool _showingError;
		private Coroutine _bindRoutine;
		private readonly List<int> _displayNumbers = new List<int>();

		private void OnEnable() {
			if (_panelRenderer == null) _panelRenderer = GetComponent<PanelRenderer>();
			if (_externalDisplayOutput == null) _externalDisplayOutput = FindAnyObjectByType<SimpleExternalDisplayOutput>();

			if (_panelRenderer != null) {
				_panelRenderer.RegisterUIReloadCallback(OnUiReloaded);
				_bindRoutine = StartCoroutine(ReloadUiAfterPanelInitialization());
			}

			if (_externalDisplayOutput != null)
				_externalDisplayOutput.OutputActiveChanged += OnOutputActiveChanged;

			RefreshLiveButton();
		}

		private void OnDisable() {
			if (_bindRoutine != null) {
				StopCoroutine(_bindRoutine);
				_bindRoutine = null;
			}
			if (_panelRenderer != null) _panelRenderer.UnregisterUIReloadCallback(OnUiReloaded);
			if (_externalDisplayOutput != null)
				_externalDisplayOutput.OutputActiveChanged -= OnOutputActiveChanged;
			Unbind();
		}

		private IEnumerator ReloadUiAfterPanelInitialization() {
			yield return null;
			_bindRoutine = null;
			if (_panelRenderer == null) yield break;

			// PanelRenderer intentionally keeps its root internal. Reloading its
			// public asset invokes the supported callback with the live root.
			var asset = _panelRenderer.visualTreeAsset;
			_panelRenderer.visualTreeAsset = null;
			_panelRenderer.visualTreeAsset = asset;
		}

		private void OnUiReloaded(PanelRenderer renderer, VisualElement root) {
			Bind(root);
		}

		private void Bind(VisualElement root) {
			Unbind();
			if (root == null) return;

			_liveButton = root.Q<Button>("live-button");
			_liveButtonLabel = root.Q<Label>("live-button-label");
			_liveStatus = root.Q("live-status");
			_liveStatusLabel = root.Q<Label>("live-status-label");
			_cancelButton = root.Q<Button>("live-confirm-cancel");
			_confirmButton = root.Q<Button>("live-confirm-accept");
			_displaySelector = root.Q<DropdownField>("live-display-selector");
			_confirmationTitle = root.Q<Label>("live-confirm-title");
			_confirmationMessage = root.Q<Label>("live-confirm-message");
			_confirmationOverlay = root.Q("live-confirm-overlay");

			if (_liveButton != null) _liveButton.clicked += RequestToggle;
			if (_cancelButton != null) _cancelButton.clicked += CancelToggle;
			if (_confirmButton != null) _confirmButton.clicked += ConfirmToggle;
			if (_displaySelector != null) _displaySelector.RegisterValueChangedCallback(OnDisplaySelectionChanged);

			HideConfirmation();
			RefreshLiveButton();
		}

		private void Unbind() {
			if (_liveButton != null) _liveButton.clicked -= RequestToggle;
			if (_cancelButton != null) _cancelButton.clicked -= CancelToggle;
			if (_confirmButton != null) _confirmButton.clicked -= ConfirmToggle;
			if (_displaySelector != null) _displaySelector.UnregisterValueChangedCallback(OnDisplaySelectionChanged);

			_liveButton = null;
			_liveButtonLabel = null;
			_liveStatus = null;
			_liveStatusLabel = null;
			_cancelButton = null;
			_confirmButton = null;
			_displaySelector = null;
			_confirmationTitle = null;
			_confirmationMessage = null;
			_confirmationOverlay = null;
		}

		private void RequestToggle() {
			if (_externalDisplayOutput == null) return;

			_showingError = false;
			_cancelButton?.RemoveFromClassList("is-hidden");
			_pendingActive = !_externalDisplayOutput.IsOutputActive;
			PrepareDisplaySelector();
			if (_pendingActive && !_externalDisplayOutput.CanActivate(_pendingDisplayNumber, out var activationError)) {
				ShowUnavailable(activationError);
				return;
			}

			if (_confirmationTitle != null)
				_confirmationTitle.text = _pendingActive ? "START LIVE OUTPUT?" : "STOP LIVE OUTPUT?";
			if (_confirmationMessage != null)
				_confirmationMessage.text = _pendingActive
					? $"Send camera output to Display {_pendingDisplayNumber}."
					: $"Stop camera output on Display {_externalDisplayOutput.DisplayNumber}.";
			if (_confirmButton != null) {
				_confirmButton.text = _pendingActive ? "START" : "STOP";
				_confirmButton.EnableInClassList("is-stop", !_pendingActive);
			}

			_confirmationOverlay?.RemoveFromClassList("is-hidden");
		}

		private void PrepareDisplaySelector() {
			_displayNumbers.Clear();
			var labels = new List<string>();
			for (var displayNumber = 2; displayNumber <= _externalDisplayOutput.ConnectedDisplayCount; displayNumber++) {
				_displayNumbers.Add(displayNumber);
				labels.Add($"Display {displayNumber}");
			}

			if (_displayNumbers.Count == 0) {
				_displayNumbers.Add(_externalDisplayOutput.DisplayNumber);
				labels.Add($"Display {_externalDisplayOutput.DisplayNumber}");
			}

			var selectedIndex = _displayNumbers.IndexOf(_externalDisplayOutput.DisplayNumber);
			if (selectedIndex < 0) selectedIndex = 0;
			_pendingDisplayNumber = _displayNumbers[selectedIndex];

			if (_displaySelector == null) return;
			_displaySelector.choices = labels;
			_displaySelector.SetValueWithoutNotify(labels[selectedIndex]);
			_displaySelector.SetEnabled(
				_pendingActive && !UnityEngine.Application.isEditor && _externalDisplayOutput.ConnectedDisplayCount > 1);
		}

		private void OnDisplaySelectionChanged(ChangeEvent<string> changeEvent) {
			if (_displaySelector == null) return;
			var selectedIndex = _displaySelector.choices.IndexOf(changeEvent.newValue);
			if (selectedIndex < 0 || selectedIndex >= _displayNumbers.Count) return;

			_pendingDisplayNumber = _displayNumbers[selectedIndex];
			if (_confirmationMessage != null && _pendingActive)
				_confirmationMessage.text = $"Send camera output to Display {_pendingDisplayNumber}.";
		}

		private void ShowUnavailable(string message) {
			_showingError = true;
			_cancelButton?.AddToClassList("is-hidden");
			if (_confirmationTitle != null) _confirmationTitle.text = "OUTPUT UNAVAILABLE";
			if (_confirmationMessage != null) _confirmationMessage.text = message;
			if (_confirmButton != null) {
				_confirmButton.text = "CLOSE";
				_confirmButton.RemoveFromClassList("is-stop");
			}
			_confirmationOverlay?.RemoveFromClassList("is-hidden");
		}

		private void CancelToggle() {
			_showingError = false;
			HideConfirmation();
		}

		private void ConfirmToggle() {
			if (_externalDisplayOutput == null) return;
			if (_showingError) {
				_showingError = false;
				HideConfirmation();
				return;
			}

			if (_pendingActive && !_externalDisplayOutput.SelectDisplay(_pendingDisplayNumber)) {
				ShowUnavailable("The output display cannot be changed while LIVE output is active.");
				return;
			}

			var succeeded = _externalDisplayOutput.SetOutputActive(_pendingActive);
			if (succeeded) {
				HideConfirmation();
			}
			else {
				ShowUnavailable(_externalDisplayOutput.LastError);
			}
			RefreshLiveButton();
		}

		private void OnOutputActiveChanged(bool active) {
			RefreshLiveButton();
		}

		private void RefreshLiveButton() {
			if (_liveButton == null) return;
			var active = _externalDisplayOutput != null && _externalDisplayOutput.IsOutputActive;
			if (_liveButtonLabel != null) _liveButtonLabel.text = active ? "STOP OUTPUT" : "START OUTPUT";
			_liveButton.EnableInClassList("is-off", !active);
			_liveButton.EnableInClassList("is-stop", active);
			_liveStatus?.EnableInClassList("is-idle", !active);
			_liveStatus?.EnableInClassList("is-live", active);
			if (_liveStatusLabel != null)
				_liveStatusLabel.text = active
					? $"LIVE / D{_externalDisplayOutput.DisplayNumber}"
					: "IDLE";
		}

		private void HideConfirmation() {
			_confirmationOverlay?.AddToClassList("is-hidden");
		}
	}
}
