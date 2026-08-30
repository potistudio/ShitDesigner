using System;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Advances child PerlinNoiseLine components from the scene graph clock.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class PerlinNoiseLineClockController : MonoBehaviour, ISceneGraphClockReceiver {
		private PerlinNoiseLine[] m_Lines = Array.Empty<PerlinNoiseLine>();
		private bool m_GraphClockDriven;
		private float m_ClockSpeedMultiplier = 1f;

		private void OnEnable() {
			m_GraphClockDriven = false;
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

		public void SetGraphClockDriven(bool graphClockDriven) {
			m_GraphClockDriven = graphClockDriven;
			RefreshLines();
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!m_GraphClockDriven || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds <= 0d)
				return;

			Advance(deltaSeconds);
		}

		public void SetClockSpeedMultiplier(float clockSpeedMultiplier) {
			if (float.IsNaN(clockSpeedMultiplier) || float.IsInfinity(clockSpeedMultiplier)) {
				m_ClockSpeedMultiplier = 0f;
				return;
			}

			m_ClockSpeedMultiplier = Mathf.Max(0f, clockSpeedMultiplier);
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
			var scaledDelta = deltaSeconds * m_ClockSpeedMultiplier;
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
