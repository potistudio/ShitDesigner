using UnityEngine;
using UnityEngine.InputSystem;

namespace ShitDesigner.Main {
	/// <summary>Main-scene input boundary for keyboard controls and external normalized live-control sources.</summary>
	[DisallowMultipleComponent]
	public sealed class MainLiveInput : MonoBehaviour {
		[SerializeField, Range(0.001f, 1f)] private float _adjustmentStep = 0.05f;
		private float _scene;
		private float _motion = 0.5f;
		private float _scale = 0.5f;
		private MainLiveParameterBuffer _buffer;

		public void Bind(MainLiveParameterBuffer buffer) => _buffer = buffer;

		public void Capture(int sceneCount) {
			var keyboard = Keyboard.current;
			if (keyboard == null || _buffer == null) return;

			if (keyboard.digit1Key.wasPressedThisFrame) SetSceneIndex(0, sceneCount);
			if (keyboard.digit2Key.wasPressedThisFrame) SetSceneIndex(1, sceneCount);
			if (keyboard.leftArrowKey.wasPressedThisFrame) SetScale(_scale - _adjustmentStep);
			if (keyboard.rightArrowKey.wasPressedThisFrame) SetScale(_scale + _adjustmentStep);
			if (keyboard.downArrowKey.wasPressedThisFrame) SetMotion(_motion - _adjustmentStep);
			if (keyboard.upArrowKey.wasPressedThisFrame) SetMotion(_motion + _adjustmentStep);
		}

		public void SetScene(float normalizedValue) {
			_scene = Mathf.Clamp01(normalizedValue);
			_buffer?.Enqueue(MainLiveParameterId.Scene, _scene);
		}

		public void SetSceneIndex(int sceneIndex, int sceneCount) {
			if (sceneCount <= 1) SetScene(0f);
			else SetScene(Mathf.Clamp(sceneIndex, 0, sceneCount - 1) / (float)(sceneCount - 1));
		}

		public void SetMotion(float normalizedValue) {
			_motion = Mathf.Clamp01(normalizedValue);
			_buffer?.Enqueue(MainLiveParameterId.Motion, _motion);
		}

		public void SetScale(float normalizedValue) {
			_scale = Mathf.Clamp01(normalizedValue);
			_buffer?.Enqueue(MainLiveParameterId.Scale, _scale);
		}
	}
}
