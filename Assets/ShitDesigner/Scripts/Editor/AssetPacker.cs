using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ShitDesigner.Editor {
	public static class AssetPacker {
		private const string PackMenuPath = "ShitDesigner/Tools/Pack Assets";
		private const string RestoreMenuPath = "ShitDesigner/Tools/Restore Assets";
		private const int CopyBufferSize = 1024 * 1024;
		private static readonly string[] AssetRoots = {
			"Assets/External",
			"Assets/ShitDesigner/Shared/Textures",
			"Assets/ShitDesigner/Shared/Videos",
			"Assets/ShitDesigner/Scenes/Win32Spin/Images",
			"Assets/ShitDesigner/Scenes/MontagemHikari/Videos",
			"Assets/ShitDesigner/Scenes/Doraemonn"
		};

		[MenuItem(PackMenuPath)]
		private static void Pack() {
			var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
			var files = CollectFiles(projectRoot);
			if (files.Count == 0) {
				EditorUtility.DisplayDialog("Asset Migration", "No assets were found.", "OK");
				return;
			}

			var totalBytes = files.Sum(file => new FileInfo(file).Length);
			if (!EditorUtility.DisplayDialog(
					"Asset Migration",
					$"Pack {files.Count:N0} files ({EditorUtility.FormatBytes(totalBytes)}) for migration?\n\nThe archive preserves Unity .meta files and must be extracted into the destination project root.",
					"Pack",
					"Cancel")) return;

			var projectName = new DirectoryInfo(projectRoot).Name;
			var archiveName = $"{projectName}-Assets-{DateTime.Now:yyyyMMdd-HHmmss}.zip";
			var defaultDirectory = Directory.GetParent(projectRoot)?.FullName ?? projectRoot;
			var destination = EditorUtility.SaveFilePanel("Pack Assets", defaultDirectory, archiveName, "zip");
			if (string.IsNullOrEmpty(destination)) return;
			if (IsInsideProject(destination, projectRoot)) {
				EditorUtility.DisplayDialog("Asset Migration", "Save the migration archive outside the Unity project.", "OK");
				return;
			}

			var temporaryPath = destination + ".tmp";
			try {
				WriteArchive(temporaryPath, projectRoot, files, totalBytes);
				if (File.Exists(destination)) File.Delete(destination);
				File.Move(temporaryPath, destination);
				Debug.Log($"Packed {files.Count:N0} assets ({EditorUtility.FormatBytes(totalBytes)}) to {destination}");
				EditorUtility.RevealInFinder(destination);
			}
			catch (OperationCanceledException) {
				Debug.Log("Asset migration packing was canceled.");
			}
			catch (Exception exception) {
				Debug.LogException(exception);
				EditorUtility.DisplayDialog("Asset Migration", $"The archive could not be created.\n\n{exception.Message}", "OK");
			}
			finally {
				EditorUtility.ClearProgressBar();
				if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
			}
		}

		[MenuItem(RestoreMenuPath)]
		private static void Restore() {
			var projectRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
			var defaultDirectory = Directory.GetParent(projectRoot)?.FullName ?? projectRoot;
			var archivePath = EditorUtility.OpenFilePanel("Restore Assets", defaultDirectory, "zip");
			if (string.IsNullOrEmpty(archivePath)) return;

			try {
				using var archive = ZipFile.OpenRead(archivePath);
				var entries = ValidateEntries(archive).ToArray();
				if (entries.Length == 0) {
					EditorUtility.DisplayDialog("Asset Migration", "The archive contains no supported asset files.", "OK");
					return;
				}

				var totalBytes = entries.Sum(entry => entry.Length);
				var overwriteCount = entries.Count(entry => File.Exists(GetDestinationPath(projectRoot, entry.FullName)));
				if (!EditorUtility.DisplayDialog(
						"Asset Migration",
						$"Restore {entries.Length:N0} files ({EditorUtility.FormatBytes(totalBytes)}) into this project?\n\n{overwriteCount:N0} existing files will be overwritten. Unity .meta files will restore the original asset GUIDs.",
						"Restore",
						"Cancel")) return;

				RestoreEntries(projectRoot, entries, totalBytes);
				Debug.Log($"Restored {entries.Length:N0} assets ({EditorUtility.FormatBytes(totalBytes)}) from {archivePath}");
			}
			catch (InvalidDataException exception) {
				Debug.LogException(exception);
				EditorUtility.DisplayDialog("Asset Migration", $"The archive is not a valid asset migration package.\n\n{exception.Message}", "OK");
			}
			catch (Exception exception) {
				Debug.LogException(exception);
				EditorUtility.DisplayDialog("Asset Migration", $"The archive could not be restored.\n\n{exception.Message}", "OK");
			}
			finally {
				EditorUtility.ClearProgressBar();
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
					if (EditorUtility.DisplayCancelableProgressBar("Packing Assets", entryPath, progress))
						throw new OperationCanceledException();
				}
			}
		}

		private static IEnumerable<ZipArchiveEntry> ValidateEntries(ZipArchive archive) {
			var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			foreach (var entry in archive.Entries) {
				var path = NormalizeEntryPath(entry.FullName);
				if (!IsAllowedAssetPath(path))
					throw new InvalidDataException($"Archive entry is outside the supported asset paths: {entry.FullName}");
				if (!paths.Add(path))
					throw new InvalidDataException($"Archive contains a duplicate asset path: {entry.FullName}");
				yield return entry;
			}
		}

		private static void RestoreEntries(string projectRoot, IReadOnlyList<ZipArchiveEntry> entries, long totalBytes) {
			var completedBytes = 0L;
			var buffer = new byte[CopyBufferSize];
			AssetDatabase.StartAssetEditing();
			try {
				foreach (var entry in entries) {
					var entryPath = NormalizeEntryPath(entry.FullName);
					var destination = GetDestinationPath(projectRoot, entryPath);
					var directory = Path.GetDirectoryName(destination);
					if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

					var temporaryPath = destination + ".assetrestore.tmp";
					try {
						using (var source = entry.Open())
						using (var target = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None)) {
							while (true) {
								var read = source.Read(buffer, 0, buffer.Length);
								if (read == 0) break;
								target.Write(buffer, 0, read);
								completedBytes += read;
								var progress = totalBytes == 0 ? 1f : (float)completedBytes / totalBytes;
								EditorUtility.DisplayProgressBar("Restoring Assets", entryPath, progress);
							}
						}

						if (File.Exists(destination)) File.Delete(destination);
						File.Move(temporaryPath, destination);
					}
					finally {
						if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
					}
				}
			}
			finally {
				AssetDatabase.StopAssetEditing();
				AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
			}
		}

		private static string NormalizeEntryPath(string path) {
			var normalized = path.Replace('\\', '/');
			if (string.IsNullOrEmpty(normalized) || normalized.StartsWith("/", StringComparison.Ordinal) || normalized.Contains(":"))
				throw new InvalidDataException($"Archive contains an invalid path: {path}");

			var segments = normalized.Split('/');
			if (segments.Any(segment => segment == "." || segment == ".." || segment.Length == 0))
				throw new InvalidDataException($"Archive contains an invalid path: {path}");
			return normalized;
		}

		private static bool IsAllowedAssetPath(string path) {
			if (string.Equals(path, "Assets/External.meta", StringComparison.OrdinalIgnoreCase)) return true;
			return AssetRoots.Any(root => path.StartsWith(root + "/", StringComparison.OrdinalIgnoreCase));
		}

		private static string GetDestinationPath(string projectRoot, string entryPath) {
			var normalized = NormalizeEntryPath(entryPath);
			var destination = Path.GetFullPath(Path.Combine(projectRoot, normalized.Replace('/', Path.DirectorySeparatorChar)));
			var rootWithSeparator = AppendDirectorySeparator(Path.GetFullPath(projectRoot));
			if (!destination.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
				throw new InvalidDataException($"Archive entry escapes the project root: {entryPath}");
			return destination;
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
