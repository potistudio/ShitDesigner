using ShitDesigner.Main;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	[CustomEditor(typeof(ShowerSequenceParameter))]
	public sealed class ShowerSequenceParameterEditor : UnityEditor.Editor {
		public override void OnInspectorGUI() {
			DrawDefaultInspector();
			EditorGUILayout.Space();

			using (new EditorGUI.DisabledScope(!UnityEngine.Application.isPlaying))
				if (GUILayout.Button("Trigger Sequence")) TriggerSelectedParameters();

			if (!UnityEngine.Application.isPlaying)
				EditorGUILayout.HelpBox("Enter Play Mode to trigger the sequence.", MessageType.Info);
		}

		private void TriggerSelectedParameters() {
			foreach (var selectedTarget in targets) {
				var parameter = (ShowerSequenceParameter)selectedTarget;
				if (!parameter.TrySetValue(1f, out var rejectionReason))
					Debug.LogWarning(rejectionReason, parameter);
			}
		}
	}
}
