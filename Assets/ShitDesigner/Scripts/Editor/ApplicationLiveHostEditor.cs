using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Input;
using ShitDesigner.Main;
using ShitDesigner.Nodes;
using ShitDesigner.Scene;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace ShitDesigner.Editor {
	[CustomEditor(typeof(ApplicationLiveHost))]
	public sealed class ApplicationLiveHostEditor : UnityEditor.Editor {
		private SerializedProperty m_GraphBootstrap;
		private SerializedProperty m_MidiInputManager;
		private SerializedProperty m_InstantEffectTypeIds;
		private SerializedProperty m_InstantEffectMidiBindings;
		private SerializedProperty m_InstantOverlayVideos;
		private SerializedProperty m_InstantOverlayMidiBindings;
		private double m_NextRepaint;

		private void OnEnable() {
			m_GraphBootstrap = serializedObject.FindProperty("_graphBootstrap");
			m_MidiInputManager = serializedObject.FindProperty("_midiInputManager");
			m_InstantEffectTypeIds = serializedObject.FindProperty("m_InstantEffectTypeIds");
			m_InstantEffectMidiBindings = serializedObject.FindProperty("m_InstantEffectMidiBindings");
			m_InstantOverlayVideos = serializedObject.FindProperty("m_InstantOverlayVideos");
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
			DrawPropertiesExcluding(serializedObject, "m_Script", "m_InstantEffectTypeIds", "m_InstantEffectMidiBindings", "m_InstantOverlayVideos", "m_InstantOverlayMidiBindings");
			var host = (ApplicationLiveHost)target;
			var graphBootstrap = m_GraphBootstrap.objectReferenceValue as LiveGraphBootstrap;
			var midiInputManager = m_MidiInputManager.objectReferenceValue as MidiInputManager;
			DrawInstantEffectAssignments(host, graphBootstrap);
			DrawInstantOverlayAssignments(host);
			DrawMidiAssignments("Instant Effect", "Cue", m_InstantEffectMidiBindings, midiInputManager);
			DrawMidiAssignments("Instant Overlay", "Lane", m_InstantOverlayMidiBindings, midiInputManager);
			serializedObject.ApplyModifiedProperties();
		}

		private void DrawInstantEffectAssignments(ApplicationLiveHost host, LiveGraphBootstrap graphBootstrap) {
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Instant Effect Assignments", EditorStyles.boldLabel);
			var effects = graphBootstrap == null ? Array.Empty<ShaderNodeManifestEntry>() : graphBootstrap.EffectNodes.ToArray();
			if (graphBootstrap == null) EditorGUILayout.HelpBox("Assign a Live Graph Bootstrap to choose Instant Effects.", MessageType.Warning);
			else if (effects.Length == 0) EditorGUILayout.HelpBox("No User Addable image effects are available from the Shader Manifest.", MessageType.Warning);
			for (var index = 0; index < m_InstantEffectTypeIds.arraySize; index++)
				DrawInstantEffectAssignment(host, index, m_InstantEffectTypeIds.GetArrayElementAtIndex(index), effects);
		}

		private static void DrawInstantEffectAssignment(ApplicationLiveHost host, int index, SerializedProperty typeId, IReadOnlyList<ShaderNodeManifestEntry> effects) {
			var currentTypeId = typeId.stringValue;
			var options = new List<string> { "<Unassigned>" };
			var typeIds = new List<string> { string.Empty };
			var selected = 0;
			for (var effectIndex = 0; effectIndex < effects.Count; effectIndex++) {
				var effect = effects[effectIndex];
				options.Add(effect.Category + " / " + effect.DisplayName);
				typeIds.Add(effect.TypeId.Value);
				if (string.Equals(currentTypeId, effect.TypeId.Value, StringComparison.Ordinal)) selected = effectIndex + 1;
			}
			if (!string.IsNullOrEmpty(currentTypeId) && selected == 0) {
				options.Add("<Unavailable> " + currentTypeId);
				typeIds.Add(currentTypeId);
				selected = options.Count - 1;
			}

			EditorGUI.BeginChangeCheck();
			selected = EditorGUILayout.Popup("Cue " + (index + 1).ToString("00"), selected, options.ToArray());
			if (!EditorGUI.EndChangeCheck()) return;
			typeId.stringValue = typeIds[selected];
			if (UnityEngine.Application.isPlaying) host.AssignInstantEffect(index, typeId.stringValue);
		}

		private void DrawInstantOverlayAssignments(ApplicationLiveHost host) {
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Instant Overlay Assignments", EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Assign a video to each lane. Instant Overlay videos are composed with Unmult alpha.", MessageType.None);
			for (var index = 0; index < m_InstantOverlayVideos.arraySize; index++)
				DrawInstantOverlayAssignment(host, index, m_InstantOverlayVideos.GetArrayElementAtIndex(index));
		}

		private static void DrawInstantOverlayAssignment(ApplicationLiveHost host, int index, SerializedProperty videoProperty) {
			EditorGUI.BeginChangeCheck();
			var video = (VideoClip)EditorGUILayout.ObjectField("Lane " + (index + 1).ToString("00"),
				videoProperty.objectReferenceValue, typeof(VideoClip), false);
			if (!EditorGUI.EndChangeCheck()) return;
			videoProperty.objectReferenceValue = video;
			if (UnityEngine.Application.isPlaying) host.AssignInstantOverlayVideo(index, video);
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
			var deviceName = binding.FindPropertyRelative("m_DeviceName");
			var messageType = binding.FindPropertyRelative("m_MessageType");
			var channel = binding.FindPropertyRelative("m_Channel");
			var number = binding.FindPropertyRelative("m_Number");

			using (new EditorGUILayout.HorizontalScope()) {
				EditorGUILayout.LabelField(slotName + " " + (index + 1).ToString("00"), GUILayout.Width(82f));
				isAssigned.boolValue = EditorGUILayout.ToggleLeft("Enabled", isAssigned.boolValue, GUILayout.Width(72f));
				using (new EditorGUI.DisabledScope(midiInputManager == null || !UnityEngine.Application.isPlaying || !midiInputManager.HasLastEvent)) {
					if (GUILayout.Button("Learn", GUILayout.Width(48f))) AssignLastMessage(isAssigned, deviceName, messageType, channel, number, midiInputManager.LastEvent);
				}
				using (new EditorGUI.DisabledScope(!isAssigned.boolValue)) {
					if (GUILayout.Button("Clear", GUILayout.Width(48f))) isAssigned.boolValue = false;
				}
			}
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.PropertyField(deviceName, new GUIContent("MIDI Device"));
			EditorGUILayout.PropertyField(messageType, new GUIContent("Message Type"));
			channel.intValue = Mathf.Clamp(EditorGUILayout.IntField("Channel", channel.intValue), 1, 16);
			var numberLabel = messageType.enumValueIndex == (int)MidiControlKind.ControlChange ? "CC Number" : "Number";
			number.intValue = Mathf.Clamp(EditorGUILayout.IntField(numberLabel, number.intValue), 0, 127);
			if (EditorGUI.EndChangeCheck()) isAssigned.boolValue = true;
		}

		private static void AssignLastMessage(SerializedProperty isAssigned, SerializedProperty deviceName, SerializedProperty messageType, SerializedProperty channel, SerializedProperty number, MidiInputEvent inputEvent) {
			isAssigned.boolValue = true;
			deviceName.stringValue = inputEvent.Control.DeviceName;
			messageType.enumValueIndex = (int)inputEvent.Control.Kind;
			channel.intValue = inputEvent.Control.Channel;
			number.intValue = inputEvent.Control.Number;
		}

		private static string FormatControl(MidiControl control)
			=> control.DeviceName + "  " + control.Kind + "  Ch " + control.Channel + "  #" + control.Number;

		private void RepaintWhilePlaying() {
			if (!UnityEngine.Application.isPlaying || EditorApplication.timeSinceStartup < m_NextRepaint) return;
			m_NextRepaint = EditorApplication.timeSinceStartup + .1d;
			Repaint();
		}
	}
}
