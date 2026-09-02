using ShitDesigner.Scene;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	[CustomEditor(typeof(StrokeRectangleScene))]
	public sealed class StrokeRectangleSceneEditor : UnityEditor.Editor {
		public override void OnInspectorGUI() {
			DrawDefaultInspector();
			EditorGUILayout.Space();

			using (new EditorGUI.DisabledScope(!UnityEngine.Application.isPlaying))
				if (GUILayout.Button("Trigger Move Up"))
					TriggerSelectedCuboids();

			if (!UnityEngine.Application.isPlaying)
				EditorGUILayout.HelpBox("Enter Play Mode to trigger the upward motion.", MessageType.Info);
		}

		private void TriggerSelectedCuboids() {
			foreach (var selectedTarget in targets)
				((StrokeRectangleScene)selectedTarget).TriggerMoveUp();
		}
	}
}
