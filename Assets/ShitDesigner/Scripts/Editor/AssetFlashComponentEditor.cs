using ShitDesigner.Media;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	[CustomEditor(typeof(AssetFlashComponent))]
	public sealed class AssetFlashComponentEditor : UnityEditor.Editor {
		public override void OnInspectorGUI() {
			DrawDefaultInspector();

			using (new EditorGUI.DisabledScope(!UnityEngine.Application.isPlaying)) {
				if (GUILayout.Button("Trigger Random")) ((AssetFlashComponent)target).TryTriggerRandom();
			}
		}
	}
}
