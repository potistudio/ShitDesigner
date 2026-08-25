using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace ShitDesigner.Bootstrap {
	/// <summary>Builds the Main scene's initial project from Unity-authored node components.</summary>
	[DisallowMultipleComponent]
	public sealed class StartupSceneGraphBootstrap : MonoBehaviour {
		[SerializeField] private ApplicationHost _host;
		[SerializeField] private bool _createOnStart = true;
		[SerializeField] private string _projectName = "Cylinder Flythrough";
		[SerializeField] private UnityGraphNode[] _nodes = Array.Empty<UnityGraphNode>();
		[SerializeField] private UnityGraphNode _programSource;

		public bool IsReady { get; private set; }
		public string LastError { get; private set; } = string.Empty;
		public string SceneNodeId { get; private set; } = string.Empty;
		public string ProjectRoot { get; private set; } = string.Empty;

		private IEnumerator Start() {
			if (!_createOnStart) yield break;
			if (_host == null || _host.Composition?.Application == null) {
				Fail("The startup graph requires an active ApplicationHost composition.");
				yield break;
			}

			if (_host.Assets?.NodeTypeCatalog == null) {
				Fail("The startup graph requires the production node catalog.");
				yield break;
			}
			var catalog = _host.Assets.NodeTypeCatalog.BuildRuntimeCatalog();
			if (catalog.IsFailure) {
				Fail(catalog.Error?.Message ?? "The startup graph requires the production node catalog.");
				yield break;
			}

			var projectName = string.IsNullOrWhiteSpace(_projectName) ? "Cylinder Flythrough" : _projectName.Trim();
			var authored = UnityGraphProjectBuilder.Build(projectName, catalog.Value, _nodes, _programSource);
			if (authored.IsFailure) {
				Fail(authored.Error?.Message ?? "The startup graph could not be built from its Unity components.");
				yield break;
			}

			ProjectRoot = Path.Combine(UnityEngine.Application.temporaryCachePath, "ShitDesigner.MainStartup", Guid.NewGuid().ToString("N"));
			var created = _host.Composition.CreateAuthoredProject(authored.Value, ProjectRoot);
			if (!created.IsSuccess) {
				Fail(created.Error?.Message ?? "The startup project could not be created.");
				yield break;
			}

			SceneNodeId = _programSource.NodeId;
			IsReady = true;
			Debug.Log("[StartupGraph] Unity-authored graph -> ProgramOutput", this);
		}

		private void OnDestroy() {
			if (string.IsNullOrEmpty(ProjectRoot)) return;
			try {
				var allowedRoot = Path.GetFullPath(Path.Combine(UnityEngine.Application.temporaryCachePath, "ShitDesigner.MainStartup"));
				var projectRoot = Path.GetFullPath(ProjectRoot);
				var allowedPrefix = allowedRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (projectRoot.StartsWith(allowedPrefix, StringComparison.OrdinalIgnoreCase) && Directory.Exists(projectRoot))
					Directory.Delete(projectRoot, true);
			}
			catch (Exception exception) {
				Debug.LogWarning("[StartupGraph] Temporary project cleanup failed: " + exception.Message, this);
			}
		}

		private void Fail(string message) {
			LastError = message ?? "The startup graph failed without a diagnostic.";
			Debug.LogError("[StartupGraph] " + LastError, this);
		}
	}
}
