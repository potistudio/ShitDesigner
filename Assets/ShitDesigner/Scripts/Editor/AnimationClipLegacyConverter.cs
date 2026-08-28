using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	public static class AnimationClipLegacyConverter {
		private const string MenuPath = "ShitDesigner/Animation/Convert Selected .anim to Animator Clip";

		[MenuItem(MenuPath)]
		private static void ConvertSelectedClip() {
			var clip = Selection.activeObject as AnimationClip;
			if (clip == null) return;

			var assetPath = AssetDatabase.GetAssetPath(clip);
			if (!IsStandaloneAnimationClip(assetPath)) {
				EditorUtility.DisplayDialog(
					"Animation Clip Conversion",
					"Only standalone .anim assets can be converted here. Configure imported clips from the source model's Rig settings.",
					"OK");
				return;
			}

			if (!clip.legacy) {
				Debug.Log($"AnimationClip is already configured for Animator: {assetPath}");
				return;
			}

			Undo.RecordObject(clip, "Convert AnimationClip to Animator Clip");
			clip.legacy = false;
			EditorUtility.SetDirty(clip);
			AssetDatabase.SaveAssets();

			Debug.Log($"Converted AnimationClip to Animator-compatible format without recreating its animation data: {assetPath}");
		}

		[MenuItem(MenuPath, true)]
		private static bool ValidateConvertSelectedClip() {
			return Selection.activeObject is AnimationClip;
		}

		private static bool IsStandaloneAnimationClip(string assetPath) {
			return !string.IsNullOrEmpty(assetPath)
				&& string.Equals(Path.GetExtension(assetPath), ".anim", StringComparison.OrdinalIgnoreCase);
		}
	}
}
