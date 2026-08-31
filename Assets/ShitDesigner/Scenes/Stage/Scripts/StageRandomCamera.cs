using System;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Stage {
	/// <summary>Keeps the Stage camera aimed at its target while alternating between deterministic random shots.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class StageRandomCamera : MonoBehaviour, ISceneGraphClockReceiver {
		[Header("References")]
		[SerializeField] private Camera m_Camera;
		[SerializeField] private Transform m_Target;

		[Header("Shot Selection")]
		[SerializeField] private Vector3 m_ShotPositionCenter = new Vector3(0f, 3f, -11f);
		[SerializeField] private Vector3 m_ShotPositionExtents = new Vector3(5f, 1.5f, 2f);
		[SerializeField] private Vector2 m_ShotDurationRange = new Vector2(2.5f, 6f);
		[SerializeField] private Vector2 m_FieldOfViewRange = new Vector2(30f, 65f);
		[SerializeField] private int m_RandomSeed = 2718;

		[Header("Motion")]
		[SerializeField] private Vector3 m_MovementRange = new Vector3(0.35f, 0.2f, 0.35f);
		[Min(0f)][SerializeField] private float m_MovementSpeed = 0.18f;

		private bool m_GraphClockDriven;
		private bool m_HasBasePosition;
		private bool m_Initialized;
		private float m_NoiseSeed;
		private float m_NoiseTime;
		private float m_RemainingShotSeconds;
		private Vector3 m_BaseLocalPosition;
		private System.Random m_Random;

		private void OnEnable() {
			m_GraphClockDriven = false;
			m_HasBasePosition = false;
			m_Initialized = false;
			Initialize();
			ApplyCamera();
		}

		private void Update() {
			if (Application.isPlaying && !m_GraphClockDriven)
				Advance(Time.deltaTime);
		}

		private void OnValidate() {
			m_ShotPositionExtents.x = Mathf.Max(0f, m_ShotPositionExtents.x);
			m_ShotPositionExtents.y = Mathf.Max(0f, m_ShotPositionExtents.y);
			m_ShotPositionExtents.z = Mathf.Max(0f, m_ShotPositionExtents.z);
			m_ShotDurationRange.x = Mathf.Max(0.01f, m_ShotDurationRange.x);
			m_ShotDurationRange.y = Mathf.Max(m_ShotDurationRange.x, m_ShotDurationRange.y);
			m_FieldOfViewRange.x = Mathf.Clamp(m_FieldOfViewRange.x, 1f, 179f);
			m_FieldOfViewRange.y = Mathf.Clamp(m_FieldOfViewRange.y, m_FieldOfViewRange.x, 179f);
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
				AdvanceShot(deltaSeconds);
			ApplyCamera();
		}

		private void AdvanceShot(float deltaSeconds) {
			var remainingDelta = deltaSeconds;
			while (remainingDelta >= m_RemainingShotSeconds) {
				remainingDelta -= m_RemainingShotSeconds;
				SelectRandomShot();
				m_RemainingShotSeconds = NextShotDuration();
			}

			m_RemainingShotSeconds -= remainingDelta;
			m_NoiseTime += remainingDelta * m_MovementSpeed;
		}

		private void Initialize() {
			if (m_Camera == null)
				m_Camera = GetComponentInChildren<Camera>(true);
			if (m_Target == null)
				m_Target = transform.Find("Camera Target");
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
			m_Random = new System.Random(m_RandomSeed);
			m_RemainingShotSeconds = NextShotDuration();
			m_Initialized = true;
		}

		private void SelectRandomShot() {
			m_BaseLocalPosition = m_ShotPositionCenter + new Vector3(
				NextFloat(-m_ShotPositionExtents.x, m_ShotPositionExtents.x),
				NextFloat(-m_ShotPositionExtents.y, m_ShotPositionExtents.y),
				NextFloat(-m_ShotPositionExtents.z, m_ShotPositionExtents.z));
			m_Camera.fieldOfView = NextFloat(m_FieldOfViewRange.x, m_FieldOfViewRange.y);
			m_NoiseTime = 0f;
		}

		private float NextShotDuration() {
			return NextFloat(m_ShotDurationRange.x, m_ShotDurationRange.y);
		}

		private float NextFloat(float minimum, float maximum) {
			return Mathf.Lerp(minimum, maximum, (float)m_Random.NextDouble());
		}

		private void ApplyCamera() {
			Initialize();
			if (m_Camera == null)
				return;

			m_Camera.transform.localPosition = m_BaseLocalPosition + Vector3.Scale(GetNoiseOffset(), m_MovementRange);
			if (m_Target == null)
				return;

			var targetDirection = m_Target.position - m_Camera.transform.position;
			if (targetDirection.sqrMagnitude > 0.000001f)
				m_Camera.transform.rotation = Quaternion.LookRotation(targetDirection, Vector3.up);
		}

		private Vector3 GetNoiseOffset() {
			return new Vector3(
				SampleNoise(0f),
				SampleNoise(37.1f),
				SampleNoise(73.7f));
		}

		private float SampleNoise(float axisOffset) {
			return Mathf.PerlinNoise(m_NoiseSeed + axisOffset, m_NoiseTime) * 2f - 1f;
		}
	}
}
