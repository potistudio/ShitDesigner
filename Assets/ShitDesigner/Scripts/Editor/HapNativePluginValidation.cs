using System.IO;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Media.Editor {
	/// <summary>Editor-side guard for optional Hap native artifacts. Source
	/// and CMake are real decoder inputs, but only an installed platform binary
	/// can be used by a player.</summary>
	[InitializeOnLoad]
	public static class HapNativePluginValidation {
		private const string WindowsSource = "Assets/Plugins/x86_64/ShitDesignerHapNative/CMakeLists.txt";
		private const string MacSource = "Assets/Plugins/macOS/ShitDesignerHapNative/CMakeLists.txt";

		static HapNativePluginValidation() {
			if (!UnityEngine.Application.isBatchMode) ValidateSources(logWarning: true);
		}

		[MenuItem("ShitDesigner/Media/Validate Hap Native Plugin")]
		public static void ValidateFromMenu() => ValidateSources(logWarning: false);

		public static bool ValidateSources(bool logWarning) {
			var windowsPresent = File.Exists(WindowsSource);
			var macPresent = File.Exists(MacSource);
			if (!windowsPresent || !macPresent) {
				Debug.LogError("ShitDesigner Hap native source/build configuration is incomplete.");
				return false;
			}

			var windowsBinary = FindPlugin("Assets/Plugins/x86_64/ShitDesignerHapNative", ".dll");
			var macBundle = FindPlugin("Assets/Plugins/macOS/ShitDesignerHapNative", ".bundle");
			var macDylib = FindPlugin("Assets/Plugins/macOS/ShitDesignerHapNative", ".dylib");
			var macBinary = macBundle ?? macDylib;
			var windowsEditor = UnityEngine.Application.platform == RuntimePlatform.WindowsEditor || UnityEngine.Application.platform == RuntimePlatform.WindowsPlayer;
			var macEditor = UnityEngine.Application.platform == RuntimePlatform.OSXEditor || UnityEngine.Application.platform == RuntimePlatform.OSXPlayer;
			var currentBinary = windowsEditor ? windowsBinary : macEditor ? macBinary : windowsBinary ?? macBinary;
			if (currentBinary == null) {
				if (logWarning) Debug.LogWarning("Hap native source is present, but no binary for the current platform is installed; Hap decode remains unsupported.");
				return false;
			}
			if (logWarning && (windowsBinary == null || macBinary == null))
				Debug.LogWarning("Hap native binary is installed only for the current platform; the other platform remains unsupported.");

			// A checked-in DLL/dylib is not evidence that the player can load
			// it. Query the exported ABI/capability symbols now, so source-only
			// or stale binaries never become a production capability.
			var probe = new PInvokeHapNativeApi().ProbeInstalledBinary();
			if (!probe.IsAvailable) {
				var message = "Hap native binary is present but failed load/ABI probe (" + probe.DiagnosticCode + "): " + probe.Message;
				if (logWarning) Debug.LogWarning(message);
				else Debug.LogError(message);
				return false;
			}
			if (logWarning) Debug.Log("Hap native plugin verified: ABI " + probe.AbiVersion + ", capabilities 0x" + probe.Capabilities.ToString("X8") + ".");
			return true;
		}

		private static string FindPlugin(string directory, string extension) {
			if (!Directory.Exists(directory)) return null;
			foreach (var path in Directory.GetFiles(directory, "*" + extension, SearchOption.AllDirectories)) return path;
			return null;
		}
	}

	/// <summary>Checks platform compatibility when a native artifact is
	/// imported. C/CMake sources are not marked as plugins.</summary>
	public sealed class HapNativePluginPostprocessor : AssetPostprocessor {
		private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom) {
			foreach (var path in imported) {
				var extension = Path.GetExtension(path).ToLowerInvariant();
				if (extension != ".dll" && extension != ".dylib" && extension != ".bundle") continue;
				var importer = AssetImporter.GetAtPath(path) as PluginImporter;
				if (importer == null || !path.Contains("ShitDesignerHapNative")) continue;
				var windows = path.Contains("x86_64");
				var mac = path.Contains("macOS");
				if (windows && !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneWindows64))
					Debug.LogWarning("Hap Windows x64 plugin is imported but disabled for StandaloneWindows64: " + path);
				if (mac && !importer.GetCompatibleWithPlatform(BuildTarget.StandaloneOSX))
					Debug.LogWarning("Hap macOS plugin is imported but disabled for StandaloneOSX: " + path);
			}
		}
	}
}
