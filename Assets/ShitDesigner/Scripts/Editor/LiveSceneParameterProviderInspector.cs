using System;
using System.Collections.Generic;
using ShitDesigner.Main;
using UnityEditor;

namespace ShitDesigner.Editor {
	[InitializeOnLoad]
	internal static class LiveSceneParameterProviderInspector {
		private const string EmptyParametersLabel = "<None>";

		static LiveSceneParameterProviderInspector() {
			UnityEditor.Editor.finishedDefaultHeaderGUI += DrawLiveParameters;
		}

		private static void DrawLiveParameters(UnityEditor.Editor editor) {
			if (editor.target is not ILiveSceneParameterProvider provider) return;

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
}
