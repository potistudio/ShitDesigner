using System;
using System.Collections;
using System.IO;
using System.Linq;
using ShitDesigner.Application;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using UnityEngine;

namespace ShitDesigner.Bootstrap {
	/// <summary>Creates the Main scene's authored startup graph through the public application command boundary.</summary>
	[DisallowMultipleComponent]
	public sealed class StartupSceneGraphBootstrap : MonoBehaviour {
		private const string Scene3dTypeId = "shitdesigner.scene.3d";
		private const double StartupTimeoutSeconds = 10d;

		[SerializeField] private ApplicationHost _host;
		[SerializeField] private bool _createOnStart = true;
		[SerializeField] private string _projectName = "Cylinder Flythrough";

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

			var application = _host.Composition.Application;
			if (application.State != ApplicationProjectState.Empty) {
				Fail("The startup graph can only be created while the application has no open project.");
				yield break;
			}

			ProjectRoot = Path.Combine(UnityEngine.Application.temporaryCachePath, "ShitDesigner.MainStartup", Guid.NewGuid().ToString("N"));
			var created = application.NewProject(string.IsNullOrWhiteSpace(_projectName) ? "Cylinder Flythrough" : _projectName.Trim(),
				ProjectRoot, UnsavedChangesDecision.Discard);
			if (!created.IsSuccess) {
				Fail(created.Diagnostic?.Message ?? "The startup project could not be created.");
				yield break;
			}

			SceneNodeId = NodeInstanceId.New().Value;
			var added = application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode,
				SceneNodeId, nodeTypeId: Scene3dTypeId, nodeDisplayName: "Cylinder Flythrough", positionX: 120f, positionY: 180f));
			if (added.Status != ApplicationCommandStatus.Accepted) {
				Fail(added.Diagnostic?.Message ?? "The startup 3D node could not be queued.");
				yield break;
			}

			var deadline = Time.realtimeSinceStartupAsDouble + StartupTimeoutSeconds;
			while (!HasNode(application, SceneNodeId) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
			if (!HasNode(application, SceneNodeId)) {
				Fail("The startup 3D node was not applied before the timeout.");
				yield break;
			}

			var programNodeId = application.ReadModel.Graph?.Model?.Nodes
				.FirstOrDefault(node => node.TypeId == GraphConstants.ProgramOutputTypeId)?.Id;
			if (string.IsNullOrEmpty(programNodeId)) {
				Fail("The startup project does not contain ProgramOutput.");
				yield break;
			}

			var connectionId = Guid.NewGuid().ToString("D");
			var connected = application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect,
				connectionId, SceneNodeId, GraphConstants.ImagePortId, programNodeId, GraphConstants.ImagePortId));
			if (connected.Status != ApplicationCommandStatus.Accepted) {
				Fail(connected.Diagnostic?.Message ?? "The startup ProgramOutput connection could not be queued.");
				yield break;
			}

			while (!HasConnection(application, connectionId) && Time.realtimeSinceStartupAsDouble < deadline) yield return null;
			if (!HasConnection(application, connectionId)) {
				Fail("The startup ProgramOutput connection was not applied before the timeout.");
				yield break;
			}

			IsReady = true;
			Debug.Log("[StartupGraph] Cylinder Flythrough -> ProgramOutput", this);
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

		private static bool HasNode(ProjectApplication application, string nodeId) =>
			application.ReadModel.Graph?.Model?.Nodes.Any(node => node.Id == nodeId) == true;

		private static bool HasConnection(ProjectApplication application, string connectionId) =>
			application.ReadModel.Graph?.Model?.Connections.Any(connection => connection.Id == connectionId) == true;

		private void Fail(string message) {
			LastError = message ?? "The startup graph failed without a diagnostic.";
			Debug.LogError("[StartupGraph] " + LastError, this);
		}
	}
}
