using System;
using System.Collections.Generic;
using ShitDesigner.Main;
using ShitDesigner.Scene;
using UnityEditor;

namespace ShitDesigner.Editor {
	internal static class LiveSceneParameterProviderInspector {
		private const string EmptyParametersLabel = "<None>";

		public static void Draw(ILiveSceneParameterProvider provider) {
			using (new EditorGUI.DisabledScope(true))
				EditorGUILayout.TextField("Live Parameters", FormatParameters(provider.LiveParameters));
		}

		private static string FormatParameters(IReadOnlyList<ILiveSceneParameter> parameters) {
			if (parameters == null || parameters.Count == 0) return EmptyParametersLabel;

			var labels = new List<string>(parameters.Count);
			for (var index = 0; index < parameters.Count; index++) {
				var parameter = parameters[index];
				if (parameter == null) continue;
				var definition = parameter.Definition;
				labels.Add(string.IsNullOrWhiteSpace(definition.DisplayName)
					? definition.Id
					: definition.DisplayName + " (" + definition.Id + ")");
			}

			return labels.Count == 0 ? EmptyParametersLabel : string.Join(", ", labels);
		}
	}

	[CustomEditor(typeof(ChitoseCandyCutScene))]
	public sealed class ChitoseCandyCutSceneEditor : UnityEditor.Editor {
		public override void OnInspectorGUI() {
			LiveSceneParameterProviderInspector.Draw((ChitoseCandyCutScene)target);
			DrawDefaultInspector();
		}
	}
}
