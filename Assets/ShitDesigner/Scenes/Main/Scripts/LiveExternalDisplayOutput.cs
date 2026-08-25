using System;
using ShitDesigner.Rendering;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Owns external Display selection, activation, display transform, and Program frame presentation.</summary>
	[DisallowMultipleComponent]
	public sealed class LiveExternalDisplayOutput : MonoBehaviour {
		[SerializeField, Min(2)] private int _displayNumber = 2;
		[SerializeField] private Shader _displayTransformShader;

		private DisplayTransformPass _displayTransform;
		private RenderTexture _displayTexture;
		private Camera _camera;
		private LiveProgramDisplayCamera _presenter;
		private ulong _presentedFrameNumber;
		private bool _initialized;

		public int DisplayNumber => _displayNumber;
		public int ConnectedDisplayCount => Display.displays?.Length ?? 0;
		public bool IsOutputActive { get; private set; }
		public bool IsAvailable => !UnityEngine.Application.isEditor && ConnectedDisplayCount >= _displayNumber;
		public ulong PresentedFrameNumber => _presentedFrameNumber;
		public string DisplayIdentity => IsAvailable
			? $"Display {_displayNumber} ({Display.displays[_displayNumber - 1].systemWidth}x{Display.displays[_displayNumber - 1].systemHeight})"
			: $"Display {_displayNumber} (Unavailable)";
		public string LastError { get; private set; } = string.Empty;

		public void Initialize() {
			Shutdown();
			if (_displayTransformShader == null) throw new InvalidOperationException("A DisplayTransform shader is required.");
			_displayTransform = new DisplayTransformPass(_displayTransformShader);
			_displayTexture = new RenderTexture(LiveGraphRuntime.ProgramWidth, LiveGraphRuntime.ProgramHeight, 0, RenderTextureFormat.ARGB32) {
				name = "ShitDesigner.Main.ExternalDisplay",
				useMipMap = false,
				autoGenerateMips = false
			};
			if (!_displayTexture.Create()) throw new InvalidOperationException("The external Display texture could not be created.");

			var cameraObject = new GameObject("Live External Display Camera");
			cameraObject.transform.SetParent(transform, false);
			_camera = cameraObject.AddComponent<Camera>();
			_camera.clearFlags = CameraClearFlags.SolidColor;
			_camera.backgroundColor = Color.black;
			_camera.cullingMask = 0;
			_camera.enabled = false;
			_presenter = cameraObject.AddComponent<LiveProgramDisplayCamera>();
			_presenter.Source = _displayTexture;
			_initialized = true;
		}

		public bool SelectDisplay(int displayNumber) {
			if (IsOutputActive || displayNumber < 2) return Fail("Stop external output before selecting another Display.");
			_displayNumber = displayNumber;
			LastError = string.Empty;
			return true;
		}

		public bool SetOutputActive(bool active) {
			if (!_initialized) return Fail("External Display output is not initialized.");
			if (!active) {
				IsOutputActive = false;
				if (_camera != null) _camera.enabled = false;
				LastError = string.Empty;
				return true;
			}
			if (!IsAvailable) return Fail(UnityEngine.Application.isEditor
				? "External Display output requires a standalone Player."
				: $"Display {_displayNumber} is not connected.");

			var display = Display.displays[_displayNumber - 1];
			if (!display.active) display.Activate();
			_camera.targetDisplay = _displayNumber - 1;
			_camera.enabled = true;
			IsOutputActive = true;
			LastError = string.Empty;
			return true;
		}

		public void IdentifyDisplay() => Debug.Log(DisplayIdentity, this);

		public void Present(LiveProgramFrame frame) {
			if (!_initialized || frame.Texture == null || frame.FrameNumber == 0 || frame.FrameNumber == _presentedFrameNumber) return;
			_displayTransform.Blit(frame.Texture, _displayTexture, DisplayTransformMode.HdrAces);
			_presentedFrameNumber = frame.FrameNumber;
		}

		public void Shutdown() {
			IsOutputActive = false;
			_initialized = false;
			_presentedFrameNumber = 0;
			if (_camera != null) DestroyObject(_camera.gameObject);
			_camera = null;
			_presenter = null;
			_displayTransform?.Dispose();
			_displayTransform = null;
			if (_displayTexture != null) {
				_displayTexture.Release();
				DestroyObject(_displayTexture);
				_displayTexture = null;
			}
		}

		private void OnDestroy() => Shutdown();

		private bool Fail(string error) {
			LastError = error;
			IsOutputActive = false;
			return false;
		}

		private static void DestroyObject(UnityEngine.Object value) {
			if (value == null) return;
			if (UnityEngine.Application.isPlaying) Destroy(value); else DestroyImmediate(value);
		}
	}

	[AddComponentMenu("")]
	public sealed class LiveProgramDisplayCamera : MonoBehaviour {
		public RenderTexture Source { private get; set; }

		private void OnRenderImage(RenderTexture source, RenderTexture destination) {
			if (Source != null) Graphics.Blit(Source, destination);
			else Graphics.Blit(source, destination);
		}
	}
}
