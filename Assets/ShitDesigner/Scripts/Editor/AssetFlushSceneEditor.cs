using ShitDesigner.AssetFlush;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	[CustomEditor(typeof(AssetFlushScene))]
	public sealed class AssetFlushSceneEditor : UnityEditor.Editor {
		private SerializedProperty m_FullScreen;
		private SerializedProperty m_Size;

		private void OnEnable() {
			m_FullScreen = serializedObject.FindProperty("m_FullScreen");
			m_Size = serializedObject.FindProperty("m_Size");
		}

		public override void OnInspectorGUI() {
			serializedObject.Update();
			DrawPropertiesExcluding(serializedObject, "m_Size");
			if (!m_FullScreen.boolValue) EditorGUILayout.PropertyField(m_Size);
			serializedObject.ApplyModifiedProperties();

			using (new EditorGUI.DisabledScope(!UnityEngine.Application.isPlaying)) {
				if (GUILayout.Button("Trigger Random")) ((AssetFlushScene)target).TryTriggerRandom();
			}
		}
	}
}
