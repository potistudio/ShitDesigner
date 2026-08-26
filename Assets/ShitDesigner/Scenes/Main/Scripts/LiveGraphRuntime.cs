using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.Rendering;

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

	public readonly struct LiveProgramFrames {
		private readonly LiveProgramFrame[] _frames;

		internal LiveProgramFrames(IEnumerable<LiveProgramFrame> frames) {
			_frames = (frames ?? Array.Empty<LiveProgramFrame>()).ToArray();
		}

		public int Count => _frames?.Length ?? 0;
		public LiveProgramFrame this[int index] => _frames[index];
		public LiveProgramFrame Primary => Count > 0 ? _frames[0] : default(LiveProgramFrame);
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

	/// <summary>Owns configured live graph outputs and their independent Program textures.</summary>
	public sealed class LiveGraphRuntime : IDisposable {
		public const int ProgramWidth = 1920;
		public const int ProgramHeight = 1080;

		private sealed class LiveProgramOutput : IDisposable {
			public Scene3DDefinition Definition { get; }
			public SceneNodeRuntime Runtime { get; }
			public LiveSceneRoot Root { get; }
			public RenderTexture ProgramTexture { get; }
			public RenderTexture RenderTexture { get; }

			public LiveProgramOutput(Scene3DDefinition definition, SceneNodeRuntime runtime, LiveSceneRoot root,
				RenderTexture programTexture, RenderTexture renderTexture) {
				Definition = definition;
				Runtime = runtime;
				Root = root;
				ProgramTexture = programTexture;
				RenderTexture = renderTexture;
			}

			public void Dispose() {
				Runtime.Dispose();
				ReleaseTexture(ProgramTexture);
				ReleaseTexture(RenderTexture);
			}
		}

		private readonly SceneIsolationManager _sceneManager;
		private readonly List<LiveProgramOutput> _programOutputs = new List<LiveProgramOutput>();
		private readonly Dictionary<string, LiveProgramOutput> _programOutputsBySceneId = new Dictionary<string, LiveProgramOutput>(StringComparer.Ordinal);
		private LiveProgramOutput _selectedProgramOutput;
		private ulong _frameNumber;
		private bool _disposed;

		public string SelectedSceneId => _selectedProgramOutput?.Definition.Id ?? string.Empty;
		public IReadOnlyList<Scene3DDefinition> Scenes => _programOutputs.Select(output => output.Definition).ToArray();
		public LiveProgramFrame CurrentFrame { get; private set; }
		public LiveProgramFrames CurrentFrames { get; private set; }

		public LiveGraphRuntime(IEnumerable<Scene3DDefinition> definitions) {
			var configured = (definitions ?? Array.Empty<Scene3DDefinition>()).ToArray();
			if (configured.Length == 0) throw new InvalidOperationException("At least one live scene is required.");
			if (configured.Any(definition => definition == null || definition.Validate().IsFailure))
				throw new InvalidOperationException("Every live scene requires a valid Scene3DDefinition.");
			if (configured.Any(definition => string.IsNullOrWhiteSpace(definition.Id)))
				throw new InvalidOperationException("Every live scene requires an ID.");
			if (configured.Select(definition => definition.Id).Distinct(StringComparer.Ordinal).Count() != configured.Length)
				throw new InvalidOperationException("Live scene IDs must be unique.");

			_sceneManager = new SceneIsolationManager(renderSource: new UnityCameraRenderSource());
			try {
				for (var index = 0; index < configured.Length; index++) CreateScene(configured[index], index);
				_selectedProgramOutput = _programOutputs[0];
				CurrentFrames = new LiveProgramFrames(_programOutputs.Select(output => new LiveProgramFrame(output.ProgramTexture, 0)));
				CurrentFrame = CurrentFrames.Primary;
			}
			catch {
				Dispose();
				throw;
			}
		}

		public LiveParameterApplicationResult Apply(LiveParameterRequest request) {
			if (!_programOutputsBySceneId.TryGetValue(request.SceneId, out var scene))
				return Reject(request, "The requested live scene does not exist.");

			if (request.Kind == LiveParameterRequestKind.SelectScene) {
				_selectedProgramOutput = scene;
				return Accept(request);
			}

			return scene.Root.TrySetParameter(request.ParameterId, request.Value, out var reason)
				? Accept(request)
				: Reject(request, reason);
		}

		public void Evaluate(double deltaSeconds) {
			EnsureUsable();
			foreach (var scene in _programOutputs) {
				var result = scene.Runtime.AdvanceGraphClock(deltaSeconds * Mathf.Lerp(0f, 2f, scene.Root.Motion));
				if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
			}
		}

		public void SceneUpdate(double deltaSeconds) {
			EnsureUsable();
			foreach (var scene in _programOutputs) {
				var result = scene.Runtime.AdvancePhysics(deltaSeconds * Mathf.Lerp(0f, 2f, scene.Root.Motion));
				if (result.IsFailure) throw new InvalidOperationException(result.Error.Message);
			}
		}

		public LiveProgramFrames Render() {
			EnsureUsable();
			var nextFrame = _frameNumber + 1;
			if (nextFrame == 0) nextFrame = 1;
			foreach (var scene in _programOutputs) {
				var result = scene.Runtime.Render(scene.RenderTexture, ProgramWidth, ProgramHeight, nextFrame);
				if (result.IsFailure || result.Value == null || !result.Value.Rendered)
					throw new InvalidOperationException(result.IsFailure ? result.Error.Message : "A live ProgramOutput did not render.");
				Graphics.Blit(scene.RenderTexture, scene.ProgramTexture);
			}
			_frameNumber = nextFrame;
			CurrentFrames = new LiveProgramFrames(_programOutputs.Select(output => new LiveProgramFrame(output.ProgramTexture, _frameNumber)));
			CurrentFrame = CurrentFrames.Primary;
			return CurrentFrames;
		}

		public LiveParameterDefinition[] GetSelectedParameterDefinitions()
			=> _selectedProgramOutput?.Root.GetParameterDefinitions() ?? Array.Empty<LiveParameterDefinition>();

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			for (var index = _programOutputs.Count - 1; index >= 0; index--) _programOutputs[index].Dispose();
			_programOutputs.Clear();
			_programOutputsBySceneId.Clear();
			_sceneManager?.Dispose();
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
			var programTexture = CreateTexture("ShitDesigner.Main.ProgramOutput." + index, 0, RenderTextureFormat.ARGBHalf);
			RenderTexture renderTexture = null;
			try {
				ClearTexture(programTexture);
				var renderFormat = SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null
					? RenderTextureFormat.ARGB32
					: RenderTextureFormat.ARGBHalf;
				renderTexture = CreateTexture("ShitDesigner.Main.ProgramRender." + index, 24, renderFormat);
				var scene = new LiveProgramOutput(definition, created.Value, root, programTexture, renderTexture);
				_programOutputs.Add(scene);
				_programOutputsBySceneId.Add(definition.Id, scene);
			}
			catch {
				ReleaseTexture(programTexture);
				ReleaseTexture(renderTexture);
				created.Value.Dispose();
				throw;
			}
		}

		private static RenderTexture CreateTexture(string name, int depth, RenderTextureFormat format) {
			var texture = new RenderTexture(ProgramWidth, ProgramHeight, depth, format) {
				name = name,
				useMipMap = false,
				autoGenerateMips = false
			};
			if (!texture.Create()) {
				ReleaseTexture(texture);
				throw new InvalidOperationException("A ProgramOutput texture could not be created.");
			}
			return texture;
		}

		private static void ClearTexture(RenderTexture texture) {
			var previous = RenderTexture.active;
			RenderTexture.active = texture;
			GL.Clear(true, true, Color.black);
			RenderTexture.active = previous;
		}

		private static void ReleaseTexture(RenderTexture texture) {
			if (texture == null) return;
			texture.Release();
			if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(texture);
			else UnityEngine.Object.DestroyImmediate(texture);
		}

		private void EnsureUsable() {
			if (_disposed) throw new ObjectDisposedException(nameof(LiveGraphRuntime));
			if (_selectedProgramOutput == null) throw new InvalidOperationException("A live ProgramOutput is not selected.");
		}

		private static LiveParameterApplicationResult Accept(LiveParameterRequest request)
			=> new LiveParameterApplicationResult(request.SequenceNumber, true, string.Empty);

		private static LiveParameterApplicationResult Reject(LiveParameterRequest request, string reason)
			=> new LiveParameterApplicationResult(request.SequenceNumber, false, reason);
	}
}
