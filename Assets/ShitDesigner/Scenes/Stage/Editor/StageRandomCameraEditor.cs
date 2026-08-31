using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Stage.Editor {
	[CustomEditor(typeof(StageRandomCamera))]
	public sealed class StageRandomCameraEditor : UnityEditor.Editor {
		public override void OnInspectorGUI() {
			DrawDefaultInspector();

			using (new EditorGUI.DisabledScope(!Application.isPlaying)) {
				if (GUILayout.Button("飛び")) ((StageRandomCamera)target).JumpToNextShot();
			}
		}
	}
}
