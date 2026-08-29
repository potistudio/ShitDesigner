using System;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Advances child PerlinNoiseLine components from the scene graph clock.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class PerlinNoiseLineClockController : MonoBehaviour, ISceneGraphClockReceiver, IBpmClockReceiver {
		[Header("Clock")]
		[Tooltip("Base multiplier applied to the graph clock delta before it is sent to the child lines.")]
		[Min(0f)][SerializeField] private float m_ClockSpeedMultiplier = 1f;
		[Header("Beat Pulse")]
		[Tooltip("Additional speed multiplier applied at the start of each beat.")]
		[Min(0f)][SerializeField] private float m_BeatPulseAmount = 1f;

		private PerlinNoiseLine[] m_Lines = Array.Empty<PerlinNoiseLine>();
		private bool m_GraphClockDriven;
		private float m_BeatPulse;

		private void OnEnable() {
			m_GraphClockDriven = false;
			m_BeatPulse = 0f;
			RefreshLines();
		}

		private void Update() {
			if (UnityEngine.Application.isPlaying && !m_GraphClockDriven)
				Advance(Time.deltaTime);
		}

		private void OnDisable() {
			m_GraphClockDriven = false;
			SetLinesGraphClockDriven(false);
			m_Lines = Array.Empty<PerlinNoiseLine>();
		}

		private void OnValidate() {
			if (float.IsNaN(m_ClockSpeedMultiplier) || float.IsInfinity(m_ClockSpeedMultiplier))
				m_ClockSpeedMultiplier = 0f;
			else
				m_ClockSpeedMultiplier = Mathf.Max(0f, m_ClockSpeedMultiplier);
			if (float.IsNaN(m_BeatPulseAmount) || float.IsInfinity(m_BeatPulseAmount))
				m_BeatPulseAmount = 0f;
			else
				m_BeatPulseAmount = Mathf.Max(0f, m_BeatPulseAmount);

			if (isActiveAndEnabled)
				RefreshLines();
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			m_GraphClockDriven = graphClockDriven;
			RefreshLines();
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!m_GraphClockDriven || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds <= 0d)
				return;

			Advance(deltaSeconds);
		}

		public void SetBpmClock(BeatClockFrame frame) {
			m_BeatPulse = frame.IsAvailable && !float.IsNaN(frame.BeatPulse) && !float.IsInfinity(frame.BeatPulse)
				? Mathf.Clamp01(frame.BeatPulse)
				: 0f;
		}

		[ContextMenu("Refresh Perlin Noise Lines")]
		public void RefreshLines() {
			m_Lines = GetComponentsInChildren<PerlinNoiseLine>(true);
			if (isActiveAndEnabled)
				SetLinesGraphClockDriven(true);
		}

		private void Advance(float deltaSeconds) {
			Advance((double)deltaSeconds);
		}

		private void Advance(double deltaSeconds) {
			var speedMultiplier = m_ClockSpeedMultiplier * (1f + m_BeatPulseAmount * m_BeatPulse);
			var scaledDelta = deltaSeconds * speedMultiplier;
			if (double.IsNaN(scaledDelta) || double.IsInfinity(scaledDelta) || scaledDelta <= 0d)
				return;

			var lineDelta = (float)Math.Min(scaledDelta, float.MaxValue);
			foreach (var line in m_Lines) {
				if (line == null || !line.isActiveAndEnabled)
					continue;

				line.AdvanceGraphClock(lineDelta);
			}
		}

		private void SetLinesGraphClockDriven(bool graphClockDriven) {
			foreach (var line in m_Lines)
				if (line != null) line.SetGraphClockDriven(graphClockDriven);
		}
	}
}
