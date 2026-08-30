using ShitDesigner.Stage;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	[CustomEditor(typeof(StageImpactBurst))]
	public sealed class StageImpactBurstEditor : UnityEditor.Editor {
		public override void OnInspectorGUI() {
			DrawDefaultInspector();

			using (new EditorGUI.DisabledScope(!Application.isPlaying)) {
				if (GUILayout.Button("Fire")) ((StageImpactBurst)target).Fire();
			}
		}
	}
}
