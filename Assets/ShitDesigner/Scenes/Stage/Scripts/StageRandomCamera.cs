using System;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Stage {
	/// <summary>Moves the Stage camera in straight lines between deterministic random shots while keeping it aimed at its target.</summary>
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

		private bool m_GraphClockDriven;
		private bool m_Initialized;
		private float m_ShotDurationSeconds;
		private float m_ShotElapsedSeconds;
		private float m_FromFieldOfView;
		private float m_ToFieldOfView;
		private Vector3 m_FromLocalPosition;
		private Vector3 m_ToLocalPosition;
		private System.Random m_Random;

		private void OnEnable() {
			m_GraphClockDriven = false;
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
			while (remainingDelta >= m_ShotDurationSeconds - m_ShotElapsedSeconds) {
				remainingDelta -= m_ShotDurationSeconds - m_ShotElapsedSeconds;
				m_FromLocalPosition = m_ToLocalPosition;
				m_FromFieldOfView = m_ToFieldOfView;
				SelectNextShot();
			}

			m_ShotElapsedSeconds += remainingDelta;
		}

		private void Initialize() {
			if (m_Camera == null)
				m_Camera = GetComponentInChildren<Camera>(true);
			if (m_Target == null)
				m_Target = transform.Find("Camera Target");
			if (m_Camera == null)
				return;

			if (m_Initialized)
				return;

			m_Random = new System.Random(m_RandomSeed);
			m_FromLocalPosition = m_Camera.transform.localPosition;
			m_FromFieldOfView = m_Camera.fieldOfView;
			SelectNextShot();
			m_Initialized = true;
		}

		private void SelectNextShot() {
			m_ToLocalPosition = m_ShotPositionCenter + new Vector3(
				NextFloat(-m_ShotPositionExtents.x, m_ShotPositionExtents.x),
				NextFloat(-m_ShotPositionExtents.y, m_ShotPositionExtents.y),
				NextFloat(-m_ShotPositionExtents.z, m_ShotPositionExtents.z));
			m_ToFieldOfView = NextFloat(m_FieldOfViewRange.x, m_FieldOfViewRange.y);
			m_ShotDurationSeconds = NextFloat(m_ShotDurationRange.x, m_ShotDurationRange.y);
			m_ShotElapsedSeconds = 0f;
		}

		private float NextFloat(float minimum, float maximum) {
			return Mathf.Lerp(minimum, maximum, (float)m_Random.NextDouble());
		}

		private void ApplyCamera() {
			Initialize();
			if (m_Camera == null)
				return;

			var progress = Mathf.Clamp01(m_ShotElapsedSeconds / m_ShotDurationSeconds);
			m_Camera.transform.localPosition = Vector3.Lerp(m_FromLocalPosition, m_ToLocalPosition, progress);
			m_Camera.fieldOfView = Mathf.Lerp(m_FromFieldOfView, m_ToFieldOfView, progress);
			if (m_Target == null)
				return;

			var targetDirection = m_Target.position - m_Camera.transform.position;
			if (targetDirection.sqrMagnitude > 0.000001f)
				m_Camera.transform.rotation = Quaternion.LookRotation(targetDirection, Vector3.up);
		}
	}
}
