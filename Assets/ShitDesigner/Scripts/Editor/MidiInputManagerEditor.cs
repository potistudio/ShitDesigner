using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using ShitDesigner.Input;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	[CustomEditor(typeof(MidiInputManager))]
	public sealed class MidiInputManagerEditor : UnityEditor.Editor {
		private SerializedProperty _deviceId;
		private SerializedProperty _openOnConfigure;
		private SerializedProperty _bindings;
		private IReadOnlyList<MidiInputDeviceInfo> _devices = Array.Empty<MidiInputDeviceInfo>();
		private string _deviceScanError = string.Empty;
		private double _nextRepaint;

		private void OnEnable() {
			_deviceId = serializedObject.FindProperty("_deviceId");
			_openOnConfigure = serializedObject.FindProperty("_openOnConfigure");
			_bindings = serializedObject.FindProperty("_bindings");
			RefreshDevices();
			EditorApplication.update += RepaintWhilePlaying;
		}

		private void OnDisable() => EditorApplication.update -= RepaintWhilePlaying;

		public override void OnInspectorGUI() {
			var manager = (MidiInputManager)target;
			serializedObject.Update();

			EditorGUILayout.LabelField("MIDI Device", EditorStyles.boldLabel);
			var previousDeviceId = _deviceId.intValue;
			var previousOpen = _openOnConfigure.boolValue;
			DrawDeviceSelector();
			EditorGUILayout.PropertyField(_openOnConfigure, new GUIContent("Open On Play"));

			var deviceChanged = serializedObject.ApplyModifiedProperties();
			if (deviceChanged && UnityEngine.Application.isPlaying) {
				var reopenDevice = previousDeviceId != manager.DeviceId || previousOpen != _openOnConfigure.boolValue;
				manager.ApplyInspectorConfiguration(reopenDevice);
			}

			EditorGUILayout.Space();
			DrawInputActivity(manager);

			EditorGUILayout.Space();
			EditorGUILayout.LabelField("MIDI Bindings", EditorStyles.boldLabel);
			EditorGUILayout.PropertyField(_bindings, true);

			var bindingsChanged = serializedObject.ApplyModifiedProperties();
			if (bindingsChanged && UnityEngine.Application.isPlaying) manager.ApplyInspectorConfiguration(false);

			EditorGUILayout.Space();
			DrawBindingMonitor(manager);
		}

		private void DrawDeviceSelector() {
			using (new EditorGUILayout.HorizontalScope()) {
				if (_devices.Count == 0) {
					EditorGUILayout.PropertyField(_deviceId, new GUIContent("Device ID"));
				}
				else {
					var names = new string[_devices.Count];
					var selected = 0;
					for (var index = 0; index < _devices.Count; index++) {
						names[index] = "[" + _devices[index].Id + "] " + _devices[index].Name;
						if (_devices[index].Id == (uint)Math.Max(0, _deviceId.intValue)) selected = index;
					}
					selected = EditorGUILayout.Popup("Device", selected, names);
					_deviceId.intValue = (int)_devices[selected].Id;
				}

				if (GUILayout.Button("Refresh", GUILayout.Width(64f))) RefreshDevices();
			}

			if (!string.IsNullOrEmpty(_deviceScanError)) EditorGUILayout.HelpBox(_deviceScanError, MessageType.Warning);
			else if (_devices.Count == 0) EditorGUILayout.HelpBox("No MIDI input devices found.", MessageType.Info);
		}

		private void DrawInputActivity(MidiInputManager manager) {
			EditorGUILayout.LabelField("MIDI Input Activity", EditorStyles.boldLabel);
			if (!UnityEngine.Application.isPlaying) {
				EditorGUILayout.HelpBox("Enter Play Mode to monitor all MIDI input, including messages without a binding.", MessageType.Info);
				return;
			}

			var status = manager.IsOpen ? "Connected: " + manager.DeviceName : "Disconnected";
			EditorGUILayout.HelpBox(status, manager.IsOpen ? MessageType.Info : MessageType.Warning);
			if (!string.IsNullOrEmpty(manager.LastError)) EditorGUILayout.HelpBox(manager.LastError, MessageType.Error);
			if (manager.IsOpen && !manager.IsConfigured)
				EditorGUILayout.HelpBox("Raw MIDI monitoring is active, but Live Control routing is not connected to Production Bootstrap.", MessageType.Warning);

			using (new EditorGUI.DisabledScope(true)) {
				EditorGUILayout.LongField("Received", manager.ReceivedEventCount);
				EditorGUILayout.LongField("Binding Matches", manager.MatchedBindingCount);
				EditorGUILayout.LongField("Sent To MIDI Learn", manager.ForwardedEventCount);
			}

			if (manager.HasLastEvent) {
				var input = manager.LastEvent;
				EditorGUILayout.LabelField("Last Message", FormatControl(input.Control) + "  Value " + input.RawValue);
			}
			else {
				EditorGUILayout.LabelField("Last Message", "Waiting for MIDI input...");
			}

			var activity = manager.RecentActivity;
			if (activity.Count > 0) {
				EditorGUILayout.Space(2f);
				EditorGUILayout.LabelField("Recent Messages", EditorStyles.miniBoldLabel);
				for (var index = 0; index < activity.Count; index++) {
					var item = activity[index];
					var route = !item.ApplicationConnected
						? "Monitor only"
						: item.ForwardedToMidiLearn ? "MIDI Learn" : "Bindings x" + item.MatchedBindings;
					EditorGUILayout.LabelField(FormatControl(item.InputEvent.Control) + "  Value " + item.InputEvent.RawValue, route, EditorStyles.miniLabel);
				}
			}

			using (new EditorGUILayout.HorizontalScope()) {
				if (GUILayout.Button("Reset Monitor")) manager.ResetMonitor();
				if (GUILayout.Button("Reopen Device")) manager.ApplyInspectorConfiguration(true);
			}
		}

		private void DrawBindingMonitor(MidiInputManager manager) {
			EditorGUILayout.LabelField("Binding Output Monitor", EditorStyles.boldLabel);
			if (!UnityEngine.Application.isPlaying) {
				EditorGUILayout.HelpBox("Binding output values appear here in Play Mode.", MessageType.Info);
				return;
			}
			var states = manager.BindingStates;
			for (var index = 0; index < _bindings.arraySize; index++) DrawBindingMonitor(manager, states, index);
		}

		private void DrawBindingMonitor(MidiInputManager manager, IReadOnlyList<MidiLiveControlBindingState> states, int index) {
			var state = index < states.Count ? states[index] : null;
			EditorGUILayout.Space(3f);
			using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox)) {
				EditorGUILayout.LabelField("Binding " + index, EditorStyles.boldLabel);
				DrawLiveControlSelector(manager, index);
				if (state == null || !state.IsValid) {
					EditorGUILayout.HelpBox(state?.Error ?? "Binding state is unavailable.", MessageType.Warning);
				}
				else if (state.HasValue) {
					var rect = GUILayoutUtility.GetRect(18f, 18f);
					EditorGUI.ProgressBar(rect, state.LastNormalizedValue, state.LastNormalizedValue.ToString("0.000"));
					EditorGUILayout.LabelField("Raw " + state.LastRawValue + "    Matches " + state.MatchCount);
				}
				else {
					EditorGUILayout.LabelField("Waiting for a matching message...");
				}

				using (new EditorGUI.DisabledScope(!manager.HasLastEvent)) {
					if (GUILayout.Button("Assign Last Message")) AssignLastMessage(manager, index);
				}
			}
		}

		private void DrawLiveControlSelector(MidiInputManager manager, int index) {
			var controls = manager.AvailableLiveControls;
			var binding = _bindings.GetArrayElementAtIndex(index);
			var id = binding.FindPropertyRelative("_liveControlId");
			if (controls.Count == 0) {
				EditorGUILayout.HelpBox("Open a project containing at least one Live Control.", MessageType.Info);
				return;
			}

			var names = new string[controls.Count + 1];
			names[0] = "<Select Live Control>";
			var selected = 0;
			for (var controlIndex = 0; controlIndex < controls.Count; controlIndex++) {
				var control = controls[controlIndex];
				names[controlIndex + 1] = control.Name + "  [" + ShortId(control.Id) + "]";
				if (string.Equals(control.Id, id.stringValue, StringComparison.Ordinal)) selected = controlIndex + 1;
			}

			EditorGUI.BeginChangeCheck();
			selected = EditorGUILayout.Popup("Live Control", selected, names);
			if (!EditorGUI.EndChangeCheck()) return;
			id.stringValue = selected == 0 ? string.Empty : controls[selected - 1].Id;
			serializedObject.ApplyModifiedProperties();
			manager.ApplyInspectorConfiguration(false);
		}

		private void AssignLastMessage(MidiInputManager manager, int index) {
			if (!manager.HasLastEvent || index < 0 || index >= _bindings.arraySize) return;
			var input = manager.LastEvent;
			var binding = _bindings.GetArrayElementAtIndex(index);
			binding.FindPropertyRelative("_messageType").enumValueIndex = (int)input.Control.Kind;
			binding.FindPropertyRelative("_channel").intValue = input.Control.Channel;
			binding.FindPropertyRelative("_number").intValue = input.Control.Number;
			binding.FindPropertyRelative("_rawMinimum").intValue = input.Control.RawMinimum;
			binding.FindPropertyRelative("_rawMaximum").intValue = input.Control.RawMaximum;
			serializedObject.ApplyModifiedProperties();
			manager.ApplyInspectorConfiguration(false);
		}

		private static string FormatControl(MidiControl control) => control.Kind + "  Ch " + control.Channel + "  #" + control.Number;

		private static string ShortId(string id) => string.IsNullOrEmpty(id) || id.Length <= 8 ? id ?? string.Empty : id.Substring(0, 8);

		private void RefreshDevices() {
			try {
				_devices = WindowsMidiInputSource.GetDevices();
				_deviceScanError = string.Empty;
			}
			catch (Exception exception) {
				_devices = Array.Empty<MidiInputDeviceInfo>();
				_deviceScanError = exception.Message;
			}
		}

		private void RepaintWhilePlaying() {
			if (!UnityEngine.Application.isPlaying || EditorApplication.timeSinceStartup < _nextRepaint) return;
			_nextRepaint = EditorApplication.timeSinceStartup + 0.1d;
			Repaint();
		}
	}
}
