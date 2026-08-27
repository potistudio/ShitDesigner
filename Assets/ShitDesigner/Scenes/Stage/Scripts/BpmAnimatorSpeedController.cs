using System.Collections.Generic;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Stage {
	/// <summary>Scales the selected Animator controller playback rate from the shared BPM clock.</summary>
	[DisallowMultipleComponent]
	public sealed class BpmAnimatorSpeedController : MonoBehaviour, IBpmClockReceiver {
		private const float DefaultReferenceBpm = 145f;

		[SerializeField] private RuntimeAnimatorController _controller;
		[SerializeField, Min(0.01f)] private float _referenceBpm = DefaultReferenceBpm;

		private readonly List<Animator> _animators = new List<Animator>();

		private void Awake() {
			FindAnimators();
		}

		private void OnEnable() {
			FindAnimators();
			SetAnimatorSpeed(1f);
		}

		private void OnValidate() {
			_referenceBpm = Mathf.Max(0.01f, _referenceBpm);
		}

		public void SetBpmClock(BpmClockState clock) {
			SetAnimatorSpeed(clock.BeatsPerMinute / _referenceBpm);
		}

		private void FindAnimators() {
			_animators.Clear();
			if (_controller == null) return;

			foreach (var animator in GetComponentsInChildren<Animator>(true))
				if (animator.runtimeAnimatorController == _controller) _animators.Add(animator);
		}

		private void SetAnimatorSpeed(float speed) {
			foreach (var animator in _animators) animator.speed = speed;
		}
	}
}
