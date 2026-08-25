using System;
using ShitDesigner.Runtime;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Owns the persistent Main render target and publishes its latest completed frame.</summary>
	[DisallowMultipleComponent]
	public sealed class MainLiveOutput : MonoBehaviour, IDisposable {
		[SerializeField, Min(1)] private int m_Width = 1920;
		[SerializeField, Min(1)] private int m_Height = 1080;

		private RenderTexture m_RenderTarget;
		private ulong _leaseId;

		public RenderTexture Target => m_RenderTarget;
		public IRuntimeImageFrameSurface CurrentFrame { get; private set; }
		public int Width => m_Width;
		public int Height => m_Height;

		private bool m_Initialized = false;

		public bool Initialize() {
			DisposeTarget();
			m_Initialized = false;
			m_RenderTarget = new RenderTexture(m_Width, m_Height, 24, RenderTextureFormat.ARGBHalf) {
				name = "ShitDesigner.Main.LiveOutput",
				useMipMap = false,
				autoGenerateMips = false
			};
			if (!m_RenderTarget.Create()) {
				DisposeTarget();
				return false;
			}
			unchecked { _leaseId++; }
			if (_leaseId == 0) _leaseId = 1;

			m_Initialized = true;
			return true;
		}

		public void Present(ulong frameNumber) {
			if (!m_Initialized || m_RenderTarget == null || !m_RenderTarget.IsCreated() || frameNumber == 0)
				return;

			CurrentFrame = new MainLiveImageFrame(m_RenderTarget, _leaseId, frameNumber);
		}

		public void Dispose() {
			CurrentFrame = null;
			DisposeTarget();
			m_Initialized = false;
		}

		private void OnDestroy() => Dispose();

		private void DisposeTarget() {
			if (m_RenderTarget == null) return;

			m_RenderTarget.Release();
			if (Application.isPlaying) Destroy(m_RenderTarget); else DestroyImmediate(m_RenderTarget);
			m_RenderTarget = null;
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
