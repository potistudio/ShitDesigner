using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Main {
	public readonly struct LiveProgramFrame {
		public RenderTexture Texture { get; }
		public ulong FrameNumber { get; }
		public int Width { get; }
		public int Height { get; }
		public RenderTextureFormat Format { get; }

		internal LiveProgramFrame(RenderTexture texture, ulong frameNumber) {
			Texture = texture;
			FrameNumber = frameNumber;
			Width = texture != null ? texture.width : 0;
			Height = texture != null ? texture.height : 0;
			Format = texture != null ? texture.format : default(RenderTextureFormat);
		}
	}

	public readonly struct LiveParameterApplicationResult {
		public ulong SequenceNumber { get; }
		public bool Applied { get; }
		public string RejectionReason { get; }

		internal LiveParameterApplicationResult(ulong sequenceNumber, bool applied, string rejectionReason) {
			SequenceNumber = sequenceNumber;
			Applied = applied;
			RejectionReason = rejectionReason;
		}
	}

	/// <summary>Owns the fixed live graph, all configured scene runtimes, and the Program texture.</summary>
	public sealed class LiveGraphRuntime : IDisposable {
		public const int ProgramWidth = 1920;
		public const int ProgramHeight = 1080;

		private sealed class LiveScene : IDisposable {
			public Scene3DDefinition Definition { get; }
			public SceneNodeRuntime Runtime { get; }
			public LiveSceneRoot Root { get; }

			public LiveScene(Scene3DDefinition definition, SceneNodeRuntime runtime, LiveSceneRoot root) {
				Definition = definition;
				Runtime = runtime;
				Root = root;
			}

			public void Dispose() => Runtime.Dispose();
		}

		private readonly SceneIsolationManager _sceneManager;
		private readonly List<LiveScene> _scenes = new List<LiveScene>();
		private readonly Dictionary<string, LiveScene> _scenesById = new Dictionary<string, LiveScene>(StringComparer.Ordinal);
		private readonly RenderTexture _programTexture;
		private LiveScene _selectedScene;
		private ulong _frameNumber;
		private bool _disposed;

		public string SelectedSceneId => _selectedScene?.Definition.Id ?? string.Empty;
		public IReadOnlyList<Scene3DDefinition> Scenes => _scenes.Select(scene => scene.Definition).ToArray();
		public LiveProgramFrame CurrentFrame { get; private set; }

		public LiveGraphRuntime(IEnumerable<Scene3DDefinition> definitions) {
			var configured = (definitions ?? Array.Empty<Scene3DDefinition>()).ToArray();
			if (configured.Length == 0) throw new InvalidOperationException("At least one live scene is required.");
			if (configured.Any(definition => definition == null || definition.Validate().IsFailure))
				throw new InvalidOperationException("Every live scene requires a valid Scene3DDefinition.");
			if (configured.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count() != configured.Length)
				throw new InvalidOperationException("Live scene IDs must be unique.");

			_programTexture = new RenderTexture(ProgramWidth, ProgramHeight, 24, RenderTextureFormat.ARGBHalf) {
				name = "ShitDesigner.Main.ProgramOutput",
				useMipMap = false,
				autoGenerateMips = false
			};
			if (!_programTexture.Create()) throw new InvalidOperationException("The Program texture could not be created.");
			ClearProgramTexture();
			CurrentFrame = new LiveProgramFrame(_programTexture, 0);

			_sceneManager = new SceneIsolationManager(renderSource: new UnityCameraRenderSource());
			try {
				for (var index = 0; index < configured.Length; index++) CreateScene(configured[index], index);
				_selectedScene = _scenes[0];
			}
			catch {
				Dispose();
				throw;
			}
		}

		public LiveParameterApplicationResult Apply(LiveParameterRequest request) {
			if (!_scenesById.TryGetValue(request.SceneId, out var scene))
				return Reject(request, "The requested live scene does not exist.");

			if (request.Kind == LiveParameterRequestKind.SelectScene) {
				_selectedScene = scene;
				return Accept(request);
			}

			return scene.Root.TrySetParameter(request.ParameterId, request.Value, out var reason)
				? Accept(request)
				: Reject(request, reason);
		}

		public void Evaluate(double deltaSeconds) {
			EnsureUsable();
			var result = _selectedScene.Runtime.AdvanceGraphClock(deltaSeconds * Mathf.Lerp(0f, 2f, _selectedScene.Root.Motion));
			if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
		}

		public void SceneUpdate(double deltaSeconds) {
			EnsureUsable();
			var result = _selectedScene.Runtime.AdvancePhysics(deltaSeconds * Mathf.Lerp(0f, 2f, _selectedScene.Root.Motion));
			if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
		}

		public LiveProgramFrame Render() {
			EnsureUsable();
			var nextFrame = _frameNumber + 1;
			if (nextFrame == 0) nextFrame = 1;
			var result = _selectedScene.Runtime.Render(_programTexture, ProgramWidth, ProgramHeight, nextFrame);
			if (result.IsFailure || result.Value == null || !result.Value.Rendered)
				throw new InvalidOperationException(result.IsFailure ? result.Error.Message : "The selected live scene did not render.");

			_frameNumber = nextFrame;
			CurrentFrame = new LiveProgramFrame(_programTexture, _frameNumber);
			return CurrentFrame;
		}

		public LiveParameterDefinition[] GetSelectedParameterDefinitions()
			=> _selectedScene?.Root.GetParameterDefinitions() ?? Array.Empty<LiveParameterDefinition>();

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			for (var index = _scenes.Count - 1; index >= 0; index--) _scenes[index].Dispose();
			_scenes.Clear();
			_scenesById.Clear();
			_sceneManager?.Dispose();
			if (_programTexture != null) {
				_programTexture.Release();
				if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(_programTexture);
				else UnityEngine.Object.DestroyImmediate(_programTexture);
			}
		}

		private void CreateScene(Scene3DDefinition definition, int index) {
			var created = _sceneManager.Create(new SceneCreateRequest(NodeInstanceId.New(), SceneNodeKind.ThreeD,
				"ShitDesigner.Main.LiveScene." + index, 1, definition.Prefab));
			if (created.IsFailure) throw new InvalidOperationException(created.Error.Message);
			var root = created.Value.Root.GetComponent<LiveSceneRoot>();
			if (root == null) {
				created.Value.Dispose();
				throw new InvalidOperationException("Every live scene prefab root requires a LiveSceneRoot.");
			}
			root.Initialize(definition.Id);
			created.Value.BindGraphClock();
			var scene = new LiveScene(definition, created.Value, root);
			_scenes.Add(scene);
			_scenesById.Add(definition.Id, scene);
		}

		private void ClearProgramTexture() {
			var previous = RenderTexture.active;
			RenderTexture.active = _programTexture;
			GL.Clear(true, true, Color.black);
			RenderTexture.active = previous;
		}

		private void EnsureUsable() {
			if (_disposed) throw new ObjectDisposedException(nameof(LiveGraphRuntime));
			if (_selectedScene == null) throw new InvalidOperationException("A live scene is not selected.");
		}

		private static LiveParameterApplicationResult Accept(LiveParameterRequest request)
			=> new LiveParameterApplicationResult(request.SequenceNumber, true, string.Empty);

		private static LiveParameterApplicationResult Reject(LiveParameterRequest request, string reason)
			=> new LiveParameterApplicationResult(request.SequenceNumber, false, reason);
	}
}
