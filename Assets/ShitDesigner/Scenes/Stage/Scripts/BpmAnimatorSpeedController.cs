using System;
using System.Collections.Generic;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Stage {
	/// <summary>
	/// Synchronizes the selected Animator controller with the shared BPM clock and
	/// optionally advances it at beat-aligned samples.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class BpmAnimatorSpeedController : MonoBehaviour, IBpmClockReceiver, ISceneGraphClockReceiver {
		private const float DefaultReferenceBpm = 145f;
		private const int DefaultPosterizeFramesPerBeat = 1;

		[SerializeField] private RuntimeAnimatorController m_Animator;
		[SerializeField, Min(1.0f)] private float m_ReferenceBpm = DefaultReferenceBpm;
		[SerializeField, Min(1)] private int m_PosterizeFramesPerBeat = DefaultPosterizeFramesPerBeat;

		private readonly List<Animator> m_Animators = new List<Animator>();
		private float m_AnimatorSpeed = 1f;
		private double m_LastProcessedBeatIndex = double.NaN;
		private bool m_GraphClockDriven;

		private void Awake() {
			FindAnimators();
		}

		private void OnEnable() {
			m_GraphClockDriven = false;
			m_LastProcessedBeatIndex = double.NaN;
			FindAnimators();
			SetAnimatorSpeed(1f);
		}

		public void SetBpmClock(BpmClockState clock) {
			SetAnimatorSpeed(clock.BeatsPerMinute / m_ReferenceBpm);
			AdvanceToBeat(clock);
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			m_GraphClockDriven = graphClockDriven && IsPosterizeTimeEnabled;
			m_LastProcessedBeatIndex = double.NaN;
			ApplyAnimatorSpeed();
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			// Beat-quantized animation is advanced from the absolute BPM clock in SetBpmClock.
		}

		private void AdvanceToBeat(BpmClockState clock) {
			if (!m_GraphClockDriven || !IsPosterizeTimeEnabled || clock.BeatsPerMinute <= 0f
				|| float.IsNaN(clock.BeatsPerMinute) || float.IsInfinity(clock.BeatsPerMinute)
				|| double.IsNaN(clock.TotalBeats) || double.IsInfinity(clock.TotalBeats))
				return;

			var beatPosition = clock.TotalBeats * m_PosterizeFramesPerBeat;
			if (double.IsNaN(beatPosition) || double.IsInfinity(beatPosition))
				return;

			var beatIndex = Math.Floor(beatPosition + 1e-9d);
			if (double.IsNaN(m_LastProcessedBeatIndex))
				m_LastProcessedBeatIndex = 0d;
			if (beatIndex < m_LastProcessedBeatIndex) {
				m_LastProcessedBeatIndex = beatIndex;
				return;
			}

			var frameCount = beatIndex - m_LastProcessedBeatIndex;
			if (frameCount < 1d)
				return;

			m_LastProcessedBeatIndex = beatIndex;
			var secondsPerFrame = 60d / (clock.BeatsPerMinute * m_PosterizeFramesPerBeat);
			var updateSeconds = (float)Math.Min(frameCount * secondsPerFrame, float.MaxValue);
			foreach (var animator in m_Animators) {
				if (animator == null)
					continue;

				animator.speed = 1f;
				try {
					animator.Update(updateSeconds);
					LoopAnimatorStates(animator);
				}
				finally {
					animator.speed = 0f;
				}
			}
		}

		private static void LoopAnimatorStates(Animator animator) {
			for (var layer = 0; layer < animator.layerCount; layer++) {
				if (animator.IsInTransition(layer))
					continue;

				var state = animator.GetCurrentAnimatorStateInfo(layer);
				if (state.loop || state.normalizedTime < 1f || state.fullPathHash == 0)
					continue;

				animator.Play(state.fullPathHash, layer, Mathf.Repeat(state.normalizedTime, 1f));
				animator.Update(0f);
			}
		}

		private void FindAnimators() {
			m_Animators.Clear();
			if (m_Animator == null)
				return;

			foreach (var animator in GetComponentsInChildren<Animator>(true))
				if (animator.runtimeAnimatorController == m_Animator) m_Animators.Add(animator);
		}

		private void SetAnimatorSpeed(float speed) {
			m_AnimatorSpeed = speed;
			ApplyAnimatorSpeed();
		}

		private void ApplyAnimatorSpeed() {
			var speed = m_GraphClockDriven ? 0f : m_AnimatorSpeed;
			foreach (var animator in m_Animators)
				if (animator != null) animator.speed = speed;
		}

		private bool IsPosterizeTimeEnabled => m_PosterizeFramesPerBeat > 0;
	}
}
