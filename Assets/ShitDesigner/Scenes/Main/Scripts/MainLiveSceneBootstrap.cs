using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Coordinates Main input, immutable live parameters, isolated scene rendering, and output once per frame.</summary>
	[DisallowMultipleComponent]
	[DefaultExecutionOrder(1000)]
	public sealed class MainLiveSceneBootstrap : MonoBehaviour {
		[SerializeField] private Scene3DDefinition[] _scenes = Array.Empty<Scene3DDefinition>();
		[SerializeField] private MainLiveInput _input;
		[SerializeField] private MainLiveMidiInput _midiInput;
		[SerializeField] private MainLiveOutput _output;
		[SerializeField] private bool _startOnEnable = true;
		private readonly MainLiveParameterBuffer _parameters = new MainLiveParameterBuffer();
		private SceneIsolationManager _sceneManager;
		private SceneNodeRuntime _activeScene;
		private Vector3 _activeBaseScale = Vector3.one;
		private float _activeBaseFieldOfView = 60f;
		private int _activeSceneIndex = -1;
		private ulong _frameNumber;
		private bool _running;

		public bool IsRunning => _running;
		public int ActiveSceneIndex => _activeSceneIndex;
		public MainLiveParameterFrame CurrentParameters { get; private set; }
		public string LastError { get; private set; } = string.Empty;
		public IReadOnlyList<Scene3DDefinition> Scenes => _scenes;

		private void Start() {
			if (_startOnEnable) StartLive();
		}

		public bool StartLive() {
			if (_running) return true;
			var definitions = (_scenes ?? Array.Empty<Scene3DDefinition>()).Where(scene => scene != null).ToArray();
			if (definitions.Length == 0 || definitions.Any(scene => scene.Validate().IsFailure)) return Fail("Every Main scene requires a valid Scene3DDefinition.");
			if (_input == null || _midiInput == null || _output == null) return Fail("Main input and output components are required.");
			_scenes = definitions;
			_input.Bind(_parameters);
			if (!_midiInput.Initialize(_input, _scenes.Length)) return Fail(_midiInput.LastError);
			if (!_output.Initialize()) {
				_midiInput.Stop();
				return Fail(_output.LastError);
			}

			_sceneManager = new SceneIsolationManager(renderSource: new UnityCameraRenderSource());
			_frameNumber = 1;
			CurrentParameters = _parameters.Commit(_frameNumber, _scenes.Length);
			if (!SwitchScene(CurrentParameters.SceneIndex)) {
				StopLive();
				return false;
			}
			_running = true;
			LastError = string.Empty;
			return true;
		}

		public void StopLive() {
			_running = false;
			_activeScene?.Dispose();
			_activeScene = null;
			_activeSceneIndex = -1;
			_sceneManager?.Dispose();
			_sceneManager = null;
			_midiInput?.Stop();
			_output?.Dispose();
		}

		private void LateUpdate() {
			if (!_running) return;
			unchecked { _frameNumber++; }
			if (_frameNumber == 0) _frameNumber = 1;
			_input.Capture(_scenes.Length);
			var frame = _parameters.Commit(_frameNumber, _scenes.Length);
			if (frame.SceneIndex != _activeSceneIndex && !SwitchScene(frame.SceneIndex)) return;

			CurrentParameters = frame;
			ApplySceneParameters(frame);
			var delta = Math.Max(0d, Time.unscaledDeltaTime * Mathf.Lerp(0f, 2f, frame.Motion));
			var animation = _activeScene.AdvanceGraphClock(delta);
			if (animation.IsFailure) { Fail(animation.Error.Message); return; }
			var physics = _activeScene.AdvancePhysics(delta);
			if (physics.IsFailure) { Fail(physics.Error.Message); return; }
			var rendered = _activeScene.Render(_output.Target, _output.Width, _output.Height, frame.FrameNumber);
			if (rendered.IsFailure || rendered.Value == null || !rendered.Value.Rendered) {
				Fail(rendered.IsFailure ? rendered.Error.Message : "The active Main scene did not render.");
				return;
			}
			if (!_output.Present(frame.FrameNumber)) Fail(_output.LastError);
		}

		private bool SwitchScene(int sceneIndex) {
			if (_sceneManager == null || sceneIndex < 0 || sceneIndex >= _scenes.Length) return Fail("The selected Main scene is unavailable.");
			var definition = _scenes[sceneIndex];
			var nodeId = NodeInstanceId.New();
			var created = _sceneManager.Create(new SceneCreateRequest(nodeId, SceneNodeKind.ThreeD,
				"ShitDesigner.Main.LiveScene." + sceneIndex, _frameNumber, definition.Prefab));
			if (created.IsFailure) return Fail(created.Error.Message);

			var previous = _activeScene;
			_activeScene = created.Value;
			_activeScene.BindGraphClock();
			_activeSceneIndex = sceneIndex;
			_activeBaseScale = _activeScene.Root.transform.localScale;
			_activeBaseFieldOfView = _activeScene.Camera.fieldOfView;
			previous?.Dispose();
			return true;
		}

		private void ApplySceneParameters(MainLiveParameterFrame frame) {
			var scale = Mathf.Lerp(0.75f, 1.25f, frame.Scale);
			_activeScene.Root.transform.localScale = _activeBaseScale * scale;
			_activeScene.Camera.fieldOfView = Mathf.Clamp(_activeBaseFieldOfView * Mathf.Lerp(0.75f, 1.25f, frame.Scale), 20f, 120f);
		}

		private bool Fail(string message) {
			LastError = string.IsNullOrWhiteSpace(message) ? "Main live scene failed without a diagnostic." : message;
			Debug.LogError("[MainLiveScene] " + LastError, this);
			return false;
		}

		private void OnDestroy() => StopLive();
	}
}
