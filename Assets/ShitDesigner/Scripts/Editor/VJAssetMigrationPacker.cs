using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	public static class VJAssetMigrationPacker {
		private const string MenuPath = "ShitDesigner/Migration/Pack Git-Ignored VJ Assets";
		private const int CopyBufferSize = 1024 * 1024;
		private static readonly string[] AssetRoots = {
			"Assets/External",
			"Assets/ShitDesigner/Shared/Textures",
			"Assets/ShitDesigner/Shared/Videos",
			"Assets/ShitDesigner/Scenes/Win32Spin/Images",
			"Assets/ShitDesigner/Scenes/MontagemHikari/Videos"
		};

		[MenuItem(MenuPath)]
		private static void Pack() {
			var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
			var files = CollectFiles(projectRoot);
			if (files.Count == 0) {
				EditorUtility.DisplayDialog("VJ Asset Migration", "No git-ignored VJ assets were found.", "OK");
				return;
			}

			var totalBytes = files.Sum(file => new FileInfo(file).Length);
			if (!EditorUtility.DisplayDialog(
					"VJ Asset Migration",
					$"Pack {files.Count:N0} files ({EditorUtility.FormatBytes(totalBytes)}) for migration?\n\nThe archive preserves Unity .meta files and must be extracted into the destination project root.",
					"Pack",
					"Cancel")) return;

			var projectName = new DirectoryInfo(projectRoot).Name;
			var archiveName = $"{projectName}-VJ-Assets-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
			var defaultDirectory = Directory.GetParent(projectRoot)?.FullName ?? projectRoot;
			var destination = EditorUtility.SaveFilePanel("Pack Git-Ignored VJ Assets", defaultDirectory, archiveName, "zip");
			if (string.IsNullOrEmpty(destination)) return;
			if (IsInsideProject(destination, projectRoot)) {
				EditorUtility.DisplayDialog("VJ Asset Migration", "Save the migration archive outside the Unity project.", "OK");
				return;
			}

			var temporaryPath = destination + ".tmp";
			try {
				WriteArchive(temporaryPath, projectRoot, files, totalBytes);
				if (File.Exists(destination)) File.Delete(destination);
				File.Move(temporaryPath, destination);
				Debug.Log($"Packed {files.Count:N0} git-ignored VJ asset files ({EditorUtility.FormatBytes(totalBytes)}) to {destination}");
				EditorUtility.RevealInFinder(destination);
			}
			catch (OperationCanceledException) {
				Debug.Log("VJ asset migration packing was canceled.");
			}
			catch (Exception exception) {
				Debug.LogException(exception);
				EditorUtility.DisplayDialog("VJ Asset Migration", $"The archive could not be created.\n\n{exception.Message}", "OK");
			}
			finally {
				EditorUtility.ClearProgressBar();
				if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
			}
		}

		private static IReadOnlyList<string> CollectFiles(string projectRoot) {
			var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var assetRoot in AssetRoots) {
				var fullRoot = Path.Combine(projectRoot, assetRoot.Replace('/', Path.DirectorySeparatorChar));
				if (!Directory.Exists(fullRoot)) continue;

				foreach (var file in Directory.GetFiles(fullRoot, "*", SearchOption.AllDirectories))
					files.Add(Path.GetFullPath(file));

				if (!string.Equals(assetRoot, "Assets/External", StringComparison.Ordinal)) continue;
				var rootMeta = fullRoot + ".meta";
				if (File.Exists(rootMeta)) files.Add(Path.GetFullPath(rootMeta));
			}

			return files.OrderBy(file => file, StringComparer.OrdinalIgnoreCase).ToArray();
		}

		private static void WriteArchive(string archivePath, string projectRoot, IReadOnlyList<string> files, long totalBytes) {
			var completedBytes = 0L;
			var buffer = new byte[CopyBufferSize];
			using var archiveStream = new FileStream(archivePath, FileMode.Create, FileAccess.Write, FileShare.None);
			using var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create);

			foreach (var file in files) {
				var entryPath = GetRelativePath(projectRoot, file).Replace(Path.DirectorySeparatorChar, '/');
				var entry = archive.CreateEntry(entryPath, System.IO.Compression.CompressionLevel.NoCompression);
				using var source = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
				using var target = entry.Open();
				while (true) {
					var read = source.Read(buffer, 0, buffer.Length);
					if (read == 0) break;
					target.Write(buffer, 0, read);
					completedBytes += read;
					var progress = totalBytes == 0 ? 1f : (float)completedBytes / totalBytes;
					if (EditorUtility.DisplayCancelableProgressBar("Packing VJ Assets", entryPath, progress))
						throw new OperationCanceledException();
				}
			}
		}

		private static string GetRelativePath(string root, string path) {
			var rootUri = new Uri(AppendDirectorySeparator(Path.GetFullPath(root)));
			var pathUri = new Uri(Path.GetFullPath(path));
			return Uri.UnescapeDataString(rootUri.MakeRelativeUri(pathUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
		}

		private static bool IsInsideProject(string path, string projectRoot) {
			var fullPath = Path.GetFullPath(path);
			var rootWithSeparator = AppendDirectorySeparator(Path.GetFullPath(projectRoot));
			return fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
		}

		private static string AppendDirectorySeparator(string path) {
			return path.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
				? path
				: path + Path.DirectorySeparatorChar;
		}
	}
}
