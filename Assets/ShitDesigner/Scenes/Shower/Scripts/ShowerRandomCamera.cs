using System;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Moves the Shower camera through smooth noise while keeping it aimed at a target.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class ShowerRandomCamera : MonoBehaviour, ISceneGraphClockReceiver {
		[Header("References")]
		[SerializeField] private Camera m_Camera;
		[SerializeField] private Transform m_Target;

		[Header("Motion")]
		[SerializeField] private Vector3 m_MovementRange = new Vector3(0.4f, 0.25f, 0.35f);
		[Min(0f)][SerializeField] private float m_MovementSpeed = 0.12f;
		[SerializeField] private int m_RandomSeed = 2718;

		private bool m_GraphClockDriven;
		private bool m_Initialized;
		private bool m_HasBasePosition;
		private float m_NoiseSeed;
		private float m_NoiseTime;
		private Vector3 m_BaseLocalPosition;

		private void OnEnable() {
			m_GraphClockDriven = false;
			m_Initialized = false;
			m_HasBasePosition = false;
			Initialize();
			ApplyCamera();
		}

		private void Update() {
			if (UnityEngine.Application.isPlaying && !m_GraphClockDriven)
				Advance(Time.deltaTime);
		}

		private void OnValidate() {
			m_MovementRange.x = Mathf.Max(0f, m_MovementRange.x);
			m_MovementRange.y = Mathf.Max(0f, m_MovementRange.y);
			m_MovementRange.z = Mathf.Max(0f, m_MovementRange.z);
			m_MovementSpeed = Mathf.Max(0f, m_MovementSpeed);
			m_Initialized = false;
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			m_GraphClockDriven = graphClockDriven;
			if (graphClockDriven)
				ApplyCamera();
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!m_GraphClockDriven || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
				return;

			Advance((float)Math.Min(deltaSeconds, float.MaxValue));
		}

		private void Advance(float deltaSeconds) {
			Initialize();
			if (m_Camera == null)
				return;

			if (deltaSeconds > 0f && !float.IsNaN(deltaSeconds) && !float.IsInfinity(deltaSeconds))
				m_NoiseTime += deltaSeconds * m_MovementSpeed;
			ApplyCamera();
		}

		private void Initialize() {
			if (m_Camera == null)
				m_Camera = GetComponentInChildren<Camera>(true);
			if (m_Target == null)
				m_Target = transform.Find("Target");
			if (m_Camera == null)
				return;

			if (!m_HasBasePosition) {
				m_BaseLocalPosition = m_Camera.transform.localPosition;
				m_HasBasePosition = true;
			}
			if (m_Initialized)
				return;

			m_NoiseSeed = Mathf.Repeat(m_RandomSeed * 0.61803399f, 1000f);
			m_NoiseTime = 0f;
			m_Initialized = true;
		}

		private void ApplyCamera() {
			Initialize();
			if (m_Camera == null)
				return;

			var noiseOffset = new Vector3(
				SampleNoise(0f),
				SampleNoise(37.1f),
				SampleNoise(73.7f));
			m_Camera.transform.localPosition = m_BaseLocalPosition + Vector3.Scale(noiseOffset, m_MovementRange);

			if (m_Target == null)
				return;

			var targetDirection = m_Target.position - m_Camera.transform.position;
			if (targetDirection.sqrMagnitude > 0.000001f)
				m_Camera.transform.rotation = Quaternion.LookRotation(targetDirection, Vector3.up);
		}

		private float SampleNoise(float axisOffset) {
			return Mathf.PerlinNoise(m_NoiseSeed + axisOffset, m_NoiseTime) * 2f - 1f;
		}
	}
}
