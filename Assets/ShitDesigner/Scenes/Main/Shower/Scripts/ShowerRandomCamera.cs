using System;
using ShitDesigner.Main;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Moves the Shower camera through smooth noise, changing its field of view on trigger while keeping it aimed at a target.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class ShowerRandomCamera : MonoBehaviour, ISceneGraphClockReceiver, ILiveParameterTriggerReceiver {
		[Header("References")]
		[SerializeField] private Camera m_Camera;
		[SerializeField] private Transform m_Target;

		[Header("Motion")]
		[SerializeField] private Vector3 m_MovementRange = new Vector3(0.4f, 0.25f, 0.35f);
		[Min(0f)][SerializeField] private float m_MovementSpeed = 0.12f;
		[Min(0.01f)][SerializeField] private float m_TeleportDistance = 2f;
		[SerializeField] private int m_RandomSeed = 2718;
		[SerializeField] private Vector2 m_FieldOfViewRange = new Vector2(45f, 75f);

		private bool m_GraphClockDriven;
		private bool m_Initialized;
		private bool m_HasBasePosition;
		private float m_NoiseSeed;
		private float m_NoiseTime;
		private Vector3 m_BaseLocalPosition;
		private System.Random m_Random;

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
			m_TeleportDistance = Mathf.Max(0.01f, m_TeleportDistance);
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

		public void OnLiveParameterTriggered() {
			Initialize();
			if (m_Camera == null || m_Target == null)
				return;

			m_Random ??= new System.Random(m_RandomSeed);
			m_Camera.fieldOfView = Mathf.Lerp(m_FieldOfViewRange.x, m_FieldOfViewRange.y, (float)m_Random.NextDouble());
			m_Camera.transform.position = m_Target.position + NextRandomDirection() * m_TeleportDistance;
			m_NoiseTime = 0f;
			m_BaseLocalPosition = m_Camera.transform.localPosition - Vector3.Scale(GetNoiseOffset(), m_MovementRange);
			ApplyOrientation();
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
			m_Random = new System.Random(m_RandomSeed);
			m_Initialized = true;
		}

		private void ApplyCamera() {
			Initialize();
			if (m_Camera == null)
				return;

			m_Camera.transform.localPosition = m_BaseLocalPosition + Vector3.Scale(GetNoiseOffset(), m_MovementRange);
			ApplyOrientation();
		}

		private Vector3 GetNoiseOffset() {
			return new Vector3(
				SampleNoise(0f),
				SampleNoise(37.1f),
				SampleNoise(73.7f));
		}

		private void ApplyOrientation() {
			if (m_Target == null)
				return;

			var targetDirection = m_Target.position - m_Camera.transform.position;
			if (targetDirection.sqrMagnitude > 0.000001f)
				m_Camera.transform.rotation = Quaternion.LookRotation(targetDirection, Vector3.up);
		}

		private float SampleNoise(float axisOffset) {
			return Mathf.PerlinNoise(m_NoiseSeed + axisOffset, m_NoiseTime) * 2f - 1f;
		}

		private Vector3 NextRandomDirection() {
			var vertical = Mathf.Lerp(-0.65f, 0.65f, (float)m_Random.NextDouble());
			var horizontalAngle = (float)m_Random.NextDouble() * Mathf.PI * 2f;
			var horizontalLength = Mathf.Sqrt(1f - vertical * vertical);
			return new Vector3(
				Mathf.Cos(horizontalAngle) * horizontalLength,
				vertical,
				Mathf.Sin(horizontalAngle) * horizontalLength);
		}
	}
}
