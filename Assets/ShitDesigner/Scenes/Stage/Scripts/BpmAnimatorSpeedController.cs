using System;
using System.Collections.Generic;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Stage {
	/// <summary>
	/// Synchronizes the selected Animator controller with the shared BPM clock and
	/// optionally evaluates it at a fixed frame rate.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class BpmAnimatorSpeedController : MonoBehaviour, IBpmClockReceiver, ISceneGraphClockReceiver {
		private const float DefaultReferenceBpm = 145f;

		[SerializeField] private RuntimeAnimatorController m_Animator;
		[SerializeField, Min(1.0f)] private float m_ReferenceBpm = DefaultReferenceBpm;
		[SerializeField, Min(0.0f)] private float m_PosterizeFrameRate;

		private readonly List<Animator> m_Animators = new List<Animator>();
		private float m_AnimatorSpeed = 1f;
		private double m_PosterizeAccumulator;
		private bool m_GraphClockDriven;

		private void Awake() {
			FindAnimators();
		}

		private void OnEnable() {
			m_GraphClockDriven = false;
			m_PosterizeAccumulator = 0d;
			FindAnimators();
			SetAnimatorSpeed(1f);
		}

		public void SetBpmClock(BpmClockState clock) {
			SetAnimatorSpeed(clock.BeatsPerMinute / m_ReferenceBpm);
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			m_GraphClockDriven = graphClockDriven && IsPosterizeTimeEnabled;
			m_PosterizeAccumulator = 0d;
			ApplyAnimatorSpeed();
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!m_GraphClockDriven || !IsPosterizeTimeEnabled || m_AnimatorSpeed <= 0f
				|| float.IsNaN(m_AnimatorSpeed) || float.IsInfinity(m_AnimatorSpeed)
				|| deltaSeconds <= 0d || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds))
				return;

			var frameDuration = 1d / m_PosterizeFrameRate;
			m_PosterizeAccumulator += deltaSeconds;
			if (double.IsNaN(m_PosterizeAccumulator) || double.IsInfinity(m_PosterizeAccumulator)) {
				m_PosterizeAccumulator = 0d;
				return;
			}

			var frameCount = Math.Floor(m_PosterizeAccumulator / frameDuration);
			if (frameCount < 1d)
				return;

			m_PosterizeAccumulator -= frameCount * frameDuration;
			var updateSeconds = (float)Math.Min(frameCount * frameDuration * m_AnimatorSpeed, float.MaxValue);
			foreach (var animator in m_Animators) {
				if (animator == null)
					continue;

				animator.speed = 1f;
				try {
					animator.Update(updateSeconds);
				}
				finally {
					animator.speed = 0f;
				}
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

		private bool IsPosterizeTimeEnabled => m_PosterizeFrameRate > 0f
			&& !float.IsNaN(m_PosterizeFrameRate) && !float.IsInfinity(m_PosterizeFrameRate);
	}
}
