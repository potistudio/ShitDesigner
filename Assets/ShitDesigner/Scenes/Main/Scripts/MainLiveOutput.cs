using System;
using ShitDesigner.Bootstrap;
using ShitDesigner.Runtime;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Owns the Main render target and submits completed frames to Program.</summary>
	[DisallowMultipleComponent]
	public sealed class MainLiveOutput : MonoBehaviour, IDisposable {
		[SerializeField, Min(1)] private int m_Width = 1920;
		[SerializeField, Min(1)] private int m_Height = 1080;
		[SerializeField] private ApplicationHost m_Host;

		private RenderTexture m_RenderTarget;
		private OutputSurfaceBridge m_Output;
		private IRuntimeImageFrameSurface m_SubmittedFrame;
		private ulong _leaseId;

		public RenderTexture Target => m_RenderTarget;
		public IRuntimeImageFrameSurface CurrentFrame { get; private set; }
		public int Width => m_Width;
		public int Height => m_Height;
		public bool IsBound => m_Output != null;
		public ulong SubmittedFrameNumber => m_SubmittedFrame?.FrameNumber ?? 0;
		public ulong ConsumedFrameNumber => m_Output?.LastProgramOverrideConsumedFrameNumber ?? 0;
		public string LastError { get; private set; } = string.Empty;

		private bool m_Initialized = false;

		public bool Initialize() {
			Dispose();
			m_Output = m_Host?.Composition?.OutputSurfaces;
			if (m_Output == null) return Fail("ApplicationHost Program output is unavailable.");
			m_Initialized = false;
			m_RenderTarget = new RenderTexture(m_Width, m_Height, 24, RenderTextureFormat.ARGBHalf) {
				name = "ShitDesigner.Main.LiveOutput",
				useMipMap = false,
				autoGenerateMips = false
			};
			if (!m_RenderTarget.Create()) {
				Dispose();
				return Fail("Main live output could not create its render target.");
			}
			unchecked { _leaseId++; }
			if (_leaseId == 0) _leaseId = 1;

			m_Initialized = true;
			LastError = string.Empty;
			return true;
		}

		public bool Present(ulong frameNumber) {
			if (!m_Initialized || m_RenderTarget == null || !m_RenderTarget.IsCreated() || frameNumber == 0)
				return Fail("Main live output is not initialized.");

			CurrentFrame = new MainLiveImageFrame(m_RenderTarget, _leaseId, frameNumber);
			var submitted = m_Output.SetProgramSourceOverride(CurrentFrame);
			if (submitted.IsFailure) return Fail(submitted.Error.Message);
			m_SubmittedFrame = CurrentFrame;
			LastError = string.Empty;
			return true;
		}

		public void Dispose() {
			m_Output?.ClearProgramSourceOverride(m_SubmittedFrame);
			m_Output = null;
			m_SubmittedFrame = null;
			CurrentFrame = null;
			DisposeTarget();
			m_Initialized = false;
		}

		private void OnDestroy() => Dispose();

		private void DisposeTarget() {
			if (m_RenderTarget == null) return;

			m_RenderTarget.Release();
			if (UnityEngine.Application.isPlaying) Destroy(m_RenderTarget); else DestroyImmediate(m_RenderTarget);
			m_RenderTarget = null;
		}

		private bool Fail(string message) {
			LastError = string.IsNullOrWhiteSpace(message) ? "Main live output failed without a diagnostic." : message;
			return false;
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
