using ShitDesigner.Core;
using ShitDesigner.Input;
using ShitDesigner.Main;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	[CustomEditor(typeof(ApplicationLiveHost))]
	public sealed class ApplicationLiveHostEditor : UnityEditor.Editor {
		private SerializedProperty m_MidiInputManager;
		private SerializedProperty m_InstantEffectMidiBindings;
		private SerializedProperty m_InstantOverlayMidiBindings;
		private double m_NextRepaint;

		private void OnEnable() {
			m_MidiInputManager = serializedObject.FindProperty("_midiInputManager");
			m_InstantEffectMidiBindings = serializedObject.FindProperty("m_InstantEffectMidiBindings");
			m_InstantOverlayMidiBindings = serializedObject.FindProperty("m_InstantOverlayMidiBindings");
			EditorApplication.update += RepaintWhilePlaying;
		}

		private void OnDisable() => EditorApplication.update -= RepaintWhilePlaying;

		public override void OnInspectorGUI() {
			if (targets.Length != 1) {
				DrawDefaultInspector();
				return;
			}

			serializedObject.Update();
			DrawPropertiesExcluding(serializedObject, "m_Script", "m_InstantEffectMidiBindings", "m_InstantOverlayMidiBindings");
			var midiInputManager = m_MidiInputManager.objectReferenceValue as MidiInputManager;
			DrawMidiAssignments("Instant Effect", "Cue", m_InstantEffectMidiBindings, midiInputManager);
			DrawMidiAssignments("Instant Overlay", "Lane", m_InstantOverlayMidiBindings, midiInputManager);
			serializedObject.ApplyModifiedProperties();
		}

		private static void DrawMidiAssignments(string title, string slotName, SerializedProperty bindings, MidiInputManager midiInputManager) {
			EditorGUILayout.Space();
			EditorGUILayout.LabelField(title + " MIDI Assignments", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox(title == "Instant Effect"
				? "A pressed control triggers its Cue on the next beat."
				: "A Lane remains active while its assigned control is held.", MessageType.None);

			if (midiInputManager == null) EditorGUILayout.HelpBox("Assign a MIDI Input Manager to learn MIDI messages.", MessageType.Warning);
			else if (!UnityEngine.Application.isPlaying) EditorGUILayout.HelpBox("Enter Play Mode and send a MIDI message to learn it.", MessageType.Info);
			else if (midiInputManager.HasLastEvent) EditorGUILayout.LabelField("Last Message", FormatControl(midiInputManager.LastEvent.Control), EditorStyles.miniLabel);
			else EditorGUILayout.LabelField("Last Message", "Waiting for MIDI input...", EditorStyles.miniLabel);

			for (var index = 0; index < bindings.arraySize; index++)
				DrawBinding(slotName, index, bindings.GetArrayElementAtIndex(index), midiInputManager);
		}

		private static void DrawBinding(string slotName, int index, SerializedProperty binding, MidiInputManager midiInputManager) {
			var isAssigned = binding.FindPropertyRelative("m_IsAssigned");
			var messageType = binding.FindPropertyRelative("m_MessageType");
			var channel = binding.FindPropertyRelative("m_Channel");
			var number = binding.FindPropertyRelative("m_Number");

			using (new EditorGUILayout.HorizontalScope()) {
				EditorGUILayout.LabelField(slotName + " " + (index + 1).ToString("00"), GUILayout.Width(62f));
				EditorGUILayout.PropertyField(isAssigned, GUIContent.none, GUILayout.Width(18f));
				using (new EditorGUI.DisabledScope(!isAssigned.boolValue)) {
					EditorGUILayout.PropertyField(messageType, GUIContent.none, GUILayout.MinWidth(105f));
					EditorGUILayout.PropertyField(channel, GUIContent.none, GUILayout.Width(42f));
					EditorGUILayout.PropertyField(number, GUIContent.none, GUILayout.Width(48f));
				}
				using (new EditorGUI.DisabledScope(midiInputManager == null || !UnityEngine.Application.isPlaying || !midiInputManager.HasLastEvent)) {
					if (GUILayout.Button("Learn", GUILayout.Width(48f))) AssignLastMessage(isAssigned, messageType, channel, number, midiInputManager.LastEvent);
				}
				using (new EditorGUI.DisabledScope(!isAssigned.boolValue)) {
					if (GUILayout.Button("Clear", GUILayout.Width(48f))) isAssigned.boolValue = false;
				}
			}
		}

		private static void AssignLastMessage(SerializedProperty isAssigned, SerializedProperty messageType, SerializedProperty channel, SerializedProperty number, MidiInputEvent inputEvent) {
			isAssigned.boolValue = true;
			messageType.enumValueIndex = (int)inputEvent.Control.Kind;
			channel.intValue = inputEvent.Control.Channel;
			number.intValue = inputEvent.Control.Number;
		}

		private static string FormatControl(MidiControl control) => control.Kind + "  Ch " + control.Channel + "  #" + control.Number;

		private void RepaintWhilePlaying() {
			if (!UnityEngine.Application.isPlaying || EditorApplication.timeSinceStartup < m_NextRepaint) return;
			m_NextRepaint = EditorApplication.timeSinceStartup + .1d;
			Repaint();
		}
	}
}
