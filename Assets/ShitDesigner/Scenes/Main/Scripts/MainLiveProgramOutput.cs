using ShitDesigner.Bootstrap;
using ShitDesigner.Runtime;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Submits Main Live frames to the existing Program output and OutputDisplay pipeline.</summary>
	[DisallowMultipleComponent]
	public sealed class MainLiveProgramOutput : MonoBehaviour {
		[SerializeField] private ApplicationHost m_Host;
		[SerializeField, Min(1)] private int m_DisplayNumber = 2;
		private OutputSurfaceBridge m_Output;
		private IRuntimeImageFrameSurface m_SubmittedFrame;
		private bool m_ActivatedOutput;

		public bool IsBound => m_Output != null;
		public ulong SubmittedFrameNumber => m_SubmittedFrame?.FrameNumber ?? 0;
		public ulong ConsumedFrameNumber => m_Output?.LastProgramOverrideConsumedFrameNumber ?? 0;
		public string LastError { get; private set; } = string.Empty;

		public bool Initialize() {
			Stop();
			m_Output = m_Host?.Composition?.OutputSurfaces;
			if (m_Output == null) return Fail("ApplicationHost Program output is unavailable.");

			if (!Application.isEditor) ActivateConfiguredDisplay();
			return true;
		}

		public bool Present(IRuntimeImageFrameSurface frame) {
			if (m_Output == null) return Fail("Program output is not initialized.");
			var submitted = m_Output.SetProgramSourceOverride(frame);
			if (submitted.IsFailure) return Fail(submitted.Error.Message);
			m_SubmittedFrame = frame;
			LastError = string.Empty;
			return true;
		}

		public void Stop() {
			if (m_Output != null) {
				m_Output.ClearProgramSourceOverride(m_SubmittedFrame);
				if (m_ActivatedOutput) m_Output.SetOutputActive(false);
			}
			m_Output = null;
			m_SubmittedFrame = null;
			m_ActivatedOutput = false;
		}

		private void ActivateConfiguredDisplay() {
			if (m_Output.IsOutputActive && m_Output.DisplayNumber != m_DisplayNumber)
				m_Output.SetOutputActive(false);
			if (m_Output.IsOutputActive) return;
			if (!m_Output.SelectDisplay(m_DisplayNumber) || !m_Output.SetOutputActive(true)) {
				LastError = m_Output.LastError;
				return;
			}
			m_ActivatedOutput = true;
			LastError = string.Empty;
		}

		private bool Fail(string message) {
			LastError = string.IsNullOrWhiteSpace(message) ? "Program output failed without a diagnostic." : message;
			return false;
		}

		private void OnDestroy() => Stop();
	}
}
