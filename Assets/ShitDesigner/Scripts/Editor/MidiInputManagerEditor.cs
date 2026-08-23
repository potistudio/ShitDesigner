using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using ShitDesigner.Input;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor
{
    [CustomEditor(typeof(MidiInputManager))]
    public sealed class MidiInputManagerEditor : UnityEditor.Editor
    {
        private SerializedProperty _deviceId;
        private SerializedProperty _openOnConfigure;
        private SerializedProperty _bindings;
        private IReadOnlyList<MidiInputDeviceInfo> _devices = Array.Empty<MidiInputDeviceInfo>();
        private string _deviceScanError = string.Empty;
        private double _nextRepaint;

        private void OnEnable()
        {
            _deviceId = serializedObject.FindProperty("_deviceId");
            _openOnConfigure = serializedObject.FindProperty("_openOnConfigure");
            _bindings = serializedObject.FindProperty("_bindings");
            RefreshDevices();
            EditorApplication.update += RepaintWhilePlaying;
        }

        private void OnDisable() => EditorApplication.update -= RepaintWhilePlaying;

        public override void OnInspectorGUI()
        {
            var manager = (MidiInputManager)target;
            serializedObject.Update();

            EditorGUILayout.LabelField("MIDI Device", EditorStyles.boldLabel);
            var previousDeviceId = _deviceId.intValue;
            var previousOpen = _openOnConfigure.boolValue;
            DrawDeviceSelector();
            EditorGUILayout.PropertyField(_openOnConfigure, new GUIContent("Open On Play"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Live Control Bindings", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_bindings, true);

            var changed = serializedObject.ApplyModifiedProperties();
            if (changed && UnityEngine.Application.isPlaying)
            {
                var reopenDevice = previousDeviceId != manager.DeviceId || previousOpen != _openOnConfigure.boolValue;
                manager.ApplyInspectorConfiguration(reopenDevice);
            }

            EditorGUILayout.Space();
            DrawMonitor(manager);
        }

        private void DrawDeviceSelector()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (_devices.Count == 0)
                {
                    EditorGUILayout.PropertyField(_deviceId, new GUIContent("Device ID"));
                }
                else
                {
                    var names = new string[_devices.Count];
                    var selected = 0;
                    for (var index = 0; index < _devices.Count; index++)
                    {
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

        private void DrawMonitor(MidiInputManager manager)
        {
            EditorGUILayout.LabelField("Live Monitor", EditorStyles.boldLabel);
            if (!UnityEngine.Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to monitor MIDI input and Live Control values.", MessageType.Info);
                return;
            }

            var status = manager.IsOpen ? "Connected: " + manager.DeviceName : "Disconnected";
            EditorGUILayout.HelpBox(status, manager.IsOpen ? MessageType.Info : MessageType.Warning);
            if (!string.IsNullOrEmpty(manager.LastError)) EditorGUILayout.HelpBox(manager.LastError, MessageType.Error);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LongField("Received", manager.ReceivedEventCount);
                EditorGUILayout.LongField("Binding Matches", manager.MatchedBindingCount);
                EditorGUILayout.LongField("Sent To MIDI Learn", manager.ForwardedEventCount);
            }

            if (manager.HasLastEvent)
            {
                var input = manager.LastEvent;
                EditorGUILayout.LabelField("Last Message", FormatControl(input.Control) + "  Value " + input.RawValue);
            }
            else
            {
                EditorGUILayout.LabelField("Last Message", "Waiting for MIDI input...");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Reset Monitor")) manager.ResetMonitor();
                if (GUILayout.Button("Reopen Device")) manager.ApplyInspectorConfiguration(true);
            }

            var states = manager.BindingStates;
            for (var index = 0; index < _bindings.arraySize; index++) DrawBindingMonitor(manager, states, index);
        }

        private void DrawBindingMonitor(MidiInputManager manager, IReadOnlyList<MidiLiveControlBindingState> states, int index)
        {
            var state = index < states.Count ? states[index] : null;
            EditorGUILayout.Space(3f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField("Binding " + index, EditorStyles.boldLabel);
                if (state == null || !state.IsValid)
                {
                    EditorGUILayout.HelpBox(state?.Error ?? "Binding state is unavailable.", MessageType.Warning);
                }
                else if (state.HasValue)
                {
                    var rect = GUILayoutUtility.GetRect(18f, 18f);
                    EditorGUI.ProgressBar(rect, state.LastNormalizedValue, state.LastNormalizedValue.ToString("0.000"));
                    EditorGUILayout.LabelField("Raw " + state.LastRawValue + "    Matches " + state.MatchCount);
                }
                else
                {
                    EditorGUILayout.LabelField("Waiting for a matching message...");
                }

                using (new EditorGUI.DisabledScope(!manager.HasLastEvent))
                {
                    if (GUILayout.Button("Assign Last Message")) AssignLastMessage(manager, index);
                }
            }
        }

        private void AssignLastMessage(MidiInputManager manager, int index)
        {
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

        private void RefreshDevices()
        {
            try
            {
                _devices = WindowsMidiInputSource.GetDevices();
                _deviceScanError = string.Empty;
            }
            catch (Exception exception)
            {
                _devices = Array.Empty<MidiInputDeviceInfo>();
                _deviceScanError = exception.Message;
            }
        }

        private void RepaintWhilePlaying()
        {
            if (!UnityEngine.Application.isPlaying || EditorApplication.timeSinceStartup < _nextRepaint) return;
            _nextRepaint = EditorApplication.timeSinceStartup + 0.1d;
            Repaint();
        }
    }
}
