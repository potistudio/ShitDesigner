using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShitDesigner.Presentation.Demo
{
	/// <summary>Interaction layer for the UXML-only demo layout.</summary>
	[RequireComponent(typeof(UIDocument))]
	public sealed class DemoUiController : MonoBehaviour
	{
		private static readonly string[] FaderNames =
		{
			"master-fader",
			"speed-fader",
			"feedback-fader",
			"shift-fader",
			"hue-fader",
			"opacity-fader"
		};

		[SerializeField] private UIDocument _document;

		private readonly List<Slider> _faders = new List<Slider>();
		private Button _gridButton;
		private Button _liveButton;
		private Label _clockValue;
		private Label _previewStatus;
		private VisualElement _gridPanel;
		private VisualElement _outputPreview;
		private bool _gridVisible = true;
		private bool _running = true;
		private double _elapsedSeconds = 2536.284;

		private void OnEnable()
		{
			if (_document == null) _document = GetComponent<UIDocument>();
			if (_document == null) return;

			var root = _document.rootVisualElement;
			_gridButton = root.Q<Button>("grid-button");
			_liveButton = root.Q<Button>("live-button");
			_clockValue = root.Q<Label>("clock-value");
			_previewStatus = root.Q<Label>("preview-status");
			_gridPanel = root.Q("grid-panel");
			_outputPreview = root.Q("output-preview");

			if (_gridButton != null) _gridButton.clicked += ToggleGrid;
			if (_liveButton != null) _liveButton.clicked += ToggleRunning;

			_faders.Clear();
			foreach (var faderName in FaderNames)
			{
				var fader = root.Q<Slider>(faderName);
				if (fader == null) continue;
				fader.RegisterValueChangedCallback(OnFaderChanged);
				_faders.Add(fader);
				ApplyFaderValue(fader);
			}

			ApplyGridState();
			ApplyRunningState();
		}

		private void OnDisable()
		{
			if (_gridButton != null) _gridButton.clicked -= ToggleGrid;
			if (_liveButton != null) _liveButton.clicked -= ToggleRunning;
			foreach (var fader in _faders) fader.UnregisterValueChangedCallback(OnFaderChanged);
			_faders.Clear();
		}

		private void Update()
		{
			if (!_running) return;
			_elapsedSeconds += Time.unscaledDeltaTime;
			if (_clockValue != null) _clockValue.text = "GRAPH  " + FormatClock(_elapsedSeconds);
		}

		private void ToggleGrid()
		{
			_gridVisible = !_gridVisible;
			ApplyGridState();
		}

		private void ApplyGridState()
		{
			if (_gridButton != null) _gridButton.text = _gridVisible ? "GRID  ON" : "GRID  OFF";
			_gridPanel?.EnableInClassList("grid-disabled", !_gridVisible);
		}

		private void ToggleRunning()
		{
			_running = !_running;
			ApplyRunningState();
		}

		private void ApplyRunningState()
		{
			if (_liveButton != null)
			{
				_liveButton.text = _running ? "●  LIVE" : "▶  START";
				_liveButton.EnableInClassList("is-paused", !_running);
			}

			if (_previewStatus != null)
			{
				_previewStatus.text = _running ? "●  LIVE" : "Ⅱ  PAUSED";
				_previewStatus.EnableInClassList("is-paused", !_running);
			}

			_outputPreview?.EnableInClassList("preview-paused", !_running);
		}

		private void OnFaderChanged(ChangeEvent<float> evt)
		{
			if (evt.target is Slider fader) ApplyFaderValue(fader);
		}

		private void ApplyFaderValue(Slider fader)
		{
			var label = _document.rootVisualElement.Q<Label>(fader.name + "-value");
			if (label != null) label.text = fader.value.ToString("0.00");
		}

		private static string FormatClock(double seconds)
		{
			var value = TimeSpan.FromSeconds(seconds);
			return string.Format("{0:00}:{1:00}:{2:00}.{3:000}",
				(int)value.TotalHours, value.Minutes, value.Seconds, value.Milliseconds);
		}
	}
}
