using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace ShitDesigner.Editor {
	public static class TestAssemblyCompilation {
		private const string TestAssemblyDefine = "SHITDESIGNER_INCLUDE_TESTS";

		[MenuItem("ShitDesigner/Development/Enable Test Assemblies")]
		private static void Enable() => SetEnabled(true);

		[MenuItem("ShitDesigner/Development/Enable Test Assemblies", true)]
		private static bool ValidateEnable() => !IsEnabled();

		[MenuItem("ShitDesigner/Development/Disable Test Assemblies")]
		private static void Disable() => SetEnabled(false);

		[MenuItem("ShitDesigner/Development/Disable Test Assemblies", true)]
		private static bool ValidateDisable() => IsEnabled();

		private static bool IsEnabled() {
			var defines = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone) ?? string.Empty;
			return defines.Split(';').Contains(TestAssemblyDefine);
		}

		private static void SetEnabled(bool enabled) {
			var defines = new HashSet<string>(
				(PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone) ?? string.Empty)
				.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
				StringComparer.Ordinal);
			if (enabled) defines.Add(TestAssemblyDefine);
			else defines.Remove(TestAssemblyDefine);
			PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, string.Join(";", defines.OrderBy(x => x, StringComparer.Ordinal)));
		}
	}
}
