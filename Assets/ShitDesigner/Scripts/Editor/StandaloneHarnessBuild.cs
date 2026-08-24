#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityScene = UnityEngine.SceneManagement.Scene;

namespace ShitDesigner.Editor {
	/// <summary>Build entry point for the test-only Player assembly. The
	/// custom symbol is scoped to this build and restored even when Unity
	/// reports a failure, keeping SHITDESIGNER_TEST_HARNESS out of product
	/// builds.</summary>
	public static class StandaloneHarnessBuild {
		public static void EnableHarnessDefine() {
			var symbols = new HashSet<string>((PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone) ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
			symbols.Add("SHITDESIGNER_TEST_HARNESS");
			PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, string.Join(";", symbols.OrderBy(x => x, StringComparer.Ordinal)));
			AssetDatabase.Refresh();
			Debug.Log("SHITDESIGNER_TEST_HARNESS enabled for the active Standalone compilation.");
		}

		public static void DisableHarnessDefine() {
			var symbols = new HashSet<string>((PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone) ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
			symbols.Remove("SHITDESIGNER_TEST_HARNESS");
			PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, string.Join(";", symbols.OrderBy(x => x, StringComparer.Ordinal)));
			AssetDatabase.Refresh();
			Debug.Log("SHITDESIGNER_TEST_HARNESS disabled.");
		}

		// Retain the historical entry point for existing external callers. A
		// generic harness build is an Acceptance/production Player; the
		// performance runner must use its explicit Development entry point.
		public static void BuildStandaloneHarness() => BuildStandaloneAcceptanceHarness();

		public static void BuildStandaloneAcceptanceHarness() =>
			BuildStandaloneHarness(BuildOptions.None, "acceptance");

		public static void BuildStandalonePerformanceHarness() =>
			BuildStandaloneHarness(BuildOptions.Development, "performance");

		private static void BuildStandaloneHarness(BuildOptions buildOptions, string buildProfile) {
			var target = ParseBuildTarget(Argument("-sdHarnessBuildTarget"));
			var output = Argument("-sdHarnessBuildOutput");
			if (string.IsNullOrWhiteSpace(output))
				output = target == BuildTarget.StandaloneOSX
					? Path.Combine("Builds", "ShitDesignerHarness", "StandaloneOSX", "ShitDesignerHarness.app")
					: Path.Combine("Builds", "ShitDesignerHarness", "StandaloneWindows64", "ShitDesignerHarness.exe");
			var oldSymbols = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
			var oldUseDefault = PlayerSettings.GetUseDefaultGraphicsAPIs(target);
			var oldApis = PlayerSettings.GetGraphicsAPIs(target);
			var oldScriptingBackend = PlayerSettings.GetScriptingBackend(NamedBuildTarget.Standalone);
			var oldArchitecture = PlayerSettings.GetArchitecture(NamedBuildTarget.Standalone);
			var oldFrameTimingStats = PlayerSettings.enableFrameTimingStats;
			var originalActiveScene = SceneManager.GetActiveScene();
			var restoreErrors = new List<Exception>();
			Exception primaryFailure = null;

			try {
				try {
					var symbols = new HashSet<string>((oldSymbols ?? string.Empty).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);
					symbols.Add("SHITDESIGNER_TEST_HARNESS");
					PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, string.Join(";", symbols.OrderBy(x => x, StringComparer.Ordinal)));
					PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, ScriptingImplementation.IL2CPP);
					ConfigureGraphicsApis(target);
					PlayerSettings.enableFrameTimingStats = true;
					if (target == BuildTarget.StandaloneOSX)
						PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, (int)OSArchitecture.ARM64);
					ShitDesigner.Bootstrap.Editor.BootstrapSceneAuthoring.Ensure();
					var configuredScenes = EditorBuildSettings.scenes.Where(x => x != null && x.enabled).Select(x => x.path).Where(File.Exists).ToArray();
					if (configuredScenes.Length == 0) throw new InvalidOperationException("No enabled scenes are configured for the Standalone Harness build.");

					var harnessScene = PrepareHarnessScene(configuredScenes[0]);
					var sceneCleanupErrors = new List<Exception>();
					try {
						Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)) ?? ".");
						var report = BuildPipeline.BuildPlayer(new[] { harnessScene }, output, target, buildOptions);
						if (report.summary.result != BuildResult.Succeeded)
							throw new InvalidOperationException("Standalone Harness build failed: " + report.summary.result + " (" + report.summary.totalErrors + " errors).");
					}
					catch (Exception exception) {
						primaryFailure = exception;
					}
					finally {
						TryDeleteTemporaryScene(harnessScene, sceneCleanupErrors, true);
						TryRestoreActiveScene(originalActiveScene, sceneCleanupErrors);
					}
					if (sceneCleanupErrors.Count != 0)
						primaryFailure = CombineFailures("Standalone Harness scene cleanup failed.", primaryFailure, sceneCleanupErrors);
					if (primaryFailure == null)
						Debug.Log("ShitDesigner " + buildProfile + " Harness build succeeded: " + output + " (BuildOptions=" + buildOptions + ").");
				}
				catch (Exception exception) {
					primaryFailure = CombineFailures("Standalone Harness build setup failed.", primaryFailure, new[] { exception });
				}
			}
			catch (Exception exception) {
				primaryFailure = CombineFailures("Standalone Harness build failed.", primaryFailure, new[] { exception });
			}
			finally {
				TryRestore("scripting define symbols", () => PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Standalone, oldSymbols ?? string.Empty), restoreErrors);
				if (oldUseDefault) {
					TryRestore("graphics API default selection", () => PlayerSettings.SetUseDefaultGraphicsAPIs(target, true), restoreErrors);
				}
				else {
					// Restore an empty explicit API list too. An empty list is
					// still a distinct pre-build setting from Unity defaults.
					TryRestore("graphics API list", () => PlayerSettings.SetGraphicsAPIs(target, oldApis ?? Array.Empty<GraphicsDeviceType>()), restoreErrors);
					TryRestore("graphics API default selection", () => PlayerSettings.SetUseDefaultGraphicsAPIs(target, false), restoreErrors);
				}
				TryRestore("Standalone scripting backend", () => PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone, oldScriptingBackend), restoreErrors);
				TryRestore("Standalone architecture", () => PlayerSettings.SetArchitecture(NamedBuildTarget.Standalone, oldArchitecture), restoreErrors);
				TryRestore("frame timing statistics", () => PlayerSettings.enableFrameTimingStats = oldFrameTimingStats, restoreErrors);
				TryRestoreActiveScene(originalActiveScene, restoreErrors);
			}

			if (primaryFailure != null || restoreErrors.Count != 0) {
				if (primaryFailure != null && restoreErrors.Count != 0)
					throw CombineFailures("Standalone Harness build failed and one or more cleanup operations also failed.", primaryFailure, restoreErrors);
				if (primaryFailure != null) throw primaryFailure;
				throw new AggregateException("Standalone Harness settings cleanup failed.", restoreErrors);
			}
		}

		private static string PrepareHarnessScene(string sourcePath) {
			// Unity 6 rejects a leading-dot asset name in SaveScene. Keep the
			// build-only scene explicit and ordinary-looking; it is still
			// removed in every success and failure path below.
			const string temporaryPath = "Assets/Scenes/ShitDesignerHarnessBuildTemp.unity";
			var originalScene = SceneManager.GetActiveScene();
			try {
				if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath)) throw new FileNotFoundException("Harness source scene is missing.", sourcePath);
				if (AssetExists(temporaryPath)) {
					var staleCleanupErrors = new List<Exception>();
					TryDeleteTemporaryScene(temporaryPath, staleCleanupErrors, true);
					if (staleCleanupErrors.Count != 0) throw CombineFailures("Could not remove the previous temporary Harness scene.", null, staleCleanupErrors);
				}

				var sourceScene = EditorSceneManager.OpenScene(sourcePath, OpenSceneMode.Single);
				var root = sourceScene.GetRootGameObjects().FirstOrDefault(x => x.GetComponent<ShitDesigner.Bootstrap.ApplicationHost>() != null);
				if (root == null) throw new InvalidOperationException("The Harness source scene does not contain ApplicationHost.");
				var harnessType = AppDomain.CurrentDomain.GetAssemblies().Select(x => x.GetType("ShitDesigner.TestHarness.StandalonePerformanceHarness", false)).FirstOrDefault(x => x != null);
				if (harnessType == null) throw new InvalidOperationException("ShitDesigner.TestHarness is not compiled. Run EnableHarnessDefine once before building.");
				if (root.GetComponent(harnessType) == null) root.AddComponent(harnessType);
				var temporaryDirectory = Path.GetDirectoryName(temporaryPath);
				if (!string.IsNullOrEmpty(temporaryDirectory)) Directory.CreateDirectory(temporaryDirectory);
				if (!EditorSceneManager.SaveScene(sourceScene, temporaryPath)) throw new InvalidOperationException("Could not save the temporary Harness scene.");
				RestoreActiveSceneOrThrow(originalScene);
				return temporaryPath;
			}
			catch (Exception exception) {
				var cleanupErrors = new List<Exception> { exception };
				TryRestoreActiveScene(originalScene, cleanupErrors);
				TryDeleteTemporaryScene(temporaryPath, cleanupErrors, false);
				throw CombineFailures("Preparing the temporary Harness scene failed.", null, cleanupErrors);
			}
		}

		private static bool AssetExists(string path) {
			return File.Exists(path) || AssetDatabase.LoadAssetAtPath<SceneAsset>(path) != null;
		}

		private static void TryDeleteTemporaryScene(string path, List<Exception> errors, bool requireExists) {
			if (string.IsNullOrWhiteSpace(path)) return;
			try {
				if (!AssetExists(path)) {
					if (requireExists) errors.Add(new FileNotFoundException("Temporary Harness scene was not present during cleanup.", path));
					return;
				}
				if (!AssetDatabase.DeleteAsset(path)) throw new InvalidOperationException("AssetDatabase.DeleteAsset returned false for the temporary Harness scene: " + path);
				if (AssetExists(path)) throw new IOException("Temporary Harness scene still exists after AssetDatabase.DeleteAsset: " + path);
			}
			catch (Exception exception) {
				errors.Add(exception);
			}
		}

		private static void RestoreActiveSceneOrThrow(UnityScene originalScene) {
			if (!originalScene.IsValid() || string.IsNullOrWhiteSpace(originalScene.path)) return;
			if (string.Equals(SceneManager.GetActiveScene().path, originalScene.path, StringComparison.OrdinalIgnoreCase)) return;
			var restored = EditorSceneManager.OpenScene(originalScene.path, OpenSceneMode.Single);
			if (!restored.IsValid()) throw new InvalidOperationException("Could not restore the original active scene: " + originalScene.path);
		}

		private static void TryRestoreActiveScene(UnityScene originalScene, List<Exception> errors) {
			try { RestoreActiveSceneOrThrow(originalScene); }
			catch (Exception exception) { errors.Add(exception); }
		}

		private static void TryRestore(string label, Action action, List<Exception> errors) {
			try { action(); }
			catch (Exception exception) {
				var wrapped = new InvalidOperationException("Failed to restore " + label + ".", exception);
				errors.Add(wrapped);
				Debug.LogError(wrapped);
			}
		}

		private static Exception CombineFailures(string message, Exception primary, IEnumerable<Exception> additional) {
			var failures = new List<Exception>();
			if (primary != null) failures.Add(primary);
			if (additional != null) failures.AddRange(additional.Where(x => x != null));
			if (failures.Count == 1) return failures[0];
			return new AggregateException(message, failures);
		}

		private static void ConfigureGraphicsApis(BuildTarget target) {
			GraphicsDeviceType[] apis;
			if (target == BuildTarget.StandaloneOSX) apis = new[] { GraphicsDeviceType.Metal };
			else if (target == BuildTarget.StandaloneWindows64) apis = new[] { GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Vulkan };
			else throw new ArgumentException("Unsupported Standalone Harness build target: " + target);
			PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
			PlayerSettings.SetGraphicsAPIs(target, apis);
		}

		private static BuildTarget ParseBuildTarget(string value) {
			if (string.IsNullOrWhiteSpace(value) || string.Equals(value, "StandaloneWindows64", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "windows", StringComparison.OrdinalIgnoreCase))
				return BuildTarget.StandaloneWindows64;
			if (string.Equals(value, "StandaloneOSX", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "macos", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "osx", StringComparison.OrdinalIgnoreCase))
				return BuildTarget.StandaloneOSX;
			throw new ArgumentException("Unsupported Standalone Harness build target: " + value);
		}

		private static string Argument(string key) {
			var args = Environment.GetCommandLineArgs();
			for (var i = 0; i + 1 < args.Length; i++) if (string.Equals(args[i], key, StringComparison.OrdinalIgnoreCase)) return args[i + 1];
			return null;
		}
	}
}
#endif
