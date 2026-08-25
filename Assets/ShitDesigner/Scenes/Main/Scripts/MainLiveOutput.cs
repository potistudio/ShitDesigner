using System;
using ShitDesigner.Runtime;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Owns the persistent Main render target and presents it on an explicitly assigned renderer.</summary>
	[DisallowMultipleComponent]
	public sealed class MainLiveOutput : MonoBehaviour, IDisposable {
		[SerializeField] private Renderer _targetRenderer;
		[SerializeField] private string _textureProperty = "_BaseMap";
		[SerializeField, Min(1)] private int _width = 1920;
		[SerializeField, Min(1)] private int _height = 1080;
		private MaterialPropertyBlock _properties;
		private RenderTexture _target;
		private ulong _leaseId;

		public RenderTexture Target => _target;
		public IRuntimeImageFrameSurface CurrentFrame { get; private set; }
		public int Width => _width;
		public int Height => _height;

		public bool Initialize() {
			if (_targetRenderer == null || string.IsNullOrWhiteSpace(_textureProperty)) return false;
			_properties ??= new MaterialPropertyBlock();
			DisposeTarget();
			_target = new RenderTexture(_width, _height, 24, RenderTextureFormat.ARGBHalf) {
				name = "ShitDesigner.Main.LiveOutput",
				useMipMap = false,
				autoGenerateMips = false
			};
			if (!_target.Create()) {
				DisposeTarget();
				return false;
			}
			unchecked { _leaseId++; }
			if (_leaseId == 0) _leaseId = 1;
			_targetRenderer.enabled = true;
			PresentTexture();
			return true;
		}

		public void Present(ulong frameNumber) {
			if (_target == null || !_target.IsCreated() || frameNumber == 0) return;
			CurrentFrame = new MainLiveImageFrame(_target, _leaseId, frameNumber);
			_targetRenderer.enabled = true;
			PresentTexture();
		}

		public void Dispose() {
			CurrentFrame = null;
			if (_targetRenderer != null && _properties != null) {
				_properties.Clear();
				_targetRenderer.SetPropertyBlock(_properties);
			}
			DisposeTarget();
		}

		private void OnDestroy() => Dispose();

		private void PresentTexture() {
			_targetRenderer.GetPropertyBlock(_properties);
			_properties.SetTexture(_textureProperty, _target);
			_targetRenderer.SetPropertyBlock(_properties);
		}

		private void DisposeTarget() {
			if (_target == null) return;
			_target.Release();
			if (Application.isPlaying) Destroy(_target); else DestroyImmediate(_target);
			_target = null;
		}

		private sealed class MainLiveImageFrame : IRuntimeImageFrameSurface {
			public int Width { get; }
			public int Height { get; }
			public string ColorFormat => RenderTexture.graphicsFormat.ToString();
			public ulong FrameNumber { get; }
			public ulong LeaseId { get; }
			public object NativeSurface => RenderTexture;
			private RenderTexture RenderTexture { get; }

			public MainLiveImageFrame(RenderTexture renderTexture, ulong leaseId, ulong frameNumber) {
				RenderTexture = renderTexture ?? throw new ArgumentNullException(nameof(renderTexture));
				Width = renderTexture.width;
				Height = renderTexture.height;
				LeaseId = leaseId;
				FrameNumber = frameNumber;
			}
		}
	}
}
