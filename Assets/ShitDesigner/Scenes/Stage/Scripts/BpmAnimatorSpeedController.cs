using System.Collections.Generic;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Stage {
	/// <summary>
	/// Scales the selected Animator controller playback rate from the shared BPM clock.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class BpmAnimatorSpeedController : MonoBehaviour, IBpmClockReceiver {
		private const float DefaultReferenceBpm = 145f;

		[SerializeField] private RuntimeAnimatorController m_Animator;
		[SerializeField, Min(1.0f)] private float m_ReferenceBpm = DefaultReferenceBpm;

		private readonly List<Animator> m_Animators = new List<Animator>();

		private void Awake() {
			FindAnimators();
		}

		private void OnEnable() {
			FindAnimators();
			SetAnimatorSpeed(1f);
		}

		public void SetBpmClock(BpmClockState clock) {
			SetAnimatorSpeed(clock.BeatsPerMinute / m_ReferenceBpm);
		}

		private void FindAnimators() {
			m_Animators.Clear();
			if (m_Animator == null)
				return;

			foreach (var animator in GetComponentsInChildren<Animator>(true))
				if (animator.runtimeAnimatorController == m_Animator) m_Animators.Add(animator);
		}

		private void SetAnimatorSpeed(float speed) {
			foreach (var animator in m_Animators) animator.speed = speed;
		}
	}
}
