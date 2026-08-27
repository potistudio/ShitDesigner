using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ShitDesigner.Editor {
	public static class MissingScriptRemover {
		private const string MenuPath = "ShitDesigner/Tools/Remove Missing Scripts (Selection)";

		[MenuItem(MenuPath)]
		private static void RemoveFromSelection() {
			var roots = Selection.gameObjects
				.Where(gameObject => gameObject != null && gameObject.scene.IsValid())
				.ToArray();
			var targets = roots
				.SelectMany(GetHierarchy)
				.Distinct()
				.ToArray();
			var missingScriptCount = targets.Sum(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount);

			if (missingScriptCount == 0) {
				EditorUtility.DisplayDialog(
					"Remove Missing Scripts",
					"No missing scripts were found in the selected hierarchy.",
					"OK");
				return;
			}

			if (!EditorUtility.DisplayDialog(
					"Remove Missing Scripts",
					$"Remove {missingScriptCount} missing script(s) from the selected hierarchy?",
					"Remove",
					"Cancel")) return;

			var undoGroup = Undo.GetCurrentGroup();
			Undo.SetCurrentGroupName("Remove Missing Scripts");
			var removedScriptCount = 0;
			foreach (var gameObject in targets) {
				var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
				if (count == 0) continue;

				Undo.RegisterCompleteObjectUndo(gameObject, "Remove Missing Scripts");
				GameObjectUtility.RemoveMonoBehavioursWithMissingScript(gameObject);
				EditorUtility.SetDirty(gameObject);
				removedScriptCount += count;
			}
			Undo.CollapseUndoOperations(undoGroup);

			foreach (var scene in targets.Select(gameObject => gameObject.scene).Distinct())
				if (scene.IsValid()) EditorSceneManager.MarkSceneDirty(scene);

			Debug.Log($"Removed {removedScriptCount} missing script(s) from the selected hierarchy.");
		}

		[MenuItem(MenuPath, true)]
		private static bool ValidateRemoveFromSelection() {
			return Selection.gameObjects.Any(gameObject => gameObject != null && gameObject.scene.IsValid());
		}

		private static GameObject[] GetHierarchy(GameObject root) {
			return root.GetComponentsInChildren<Transform>(true)
				.Select(transform => transform.gameObject)
				.ToArray();
		}
	}
}
