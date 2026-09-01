using System;
using ShitDesigner.Main;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Stage {
	/// <summary>Moves the Stage camera indefinitely in one direction until the next manual jump.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class StageRandomCamera : MonoBehaviour, ISceneGraphClockReceiver, ILiveSceneParameter, ILiveSceneTriggerParameter {
		public const string JumpParameterId = "camera_jump";

		[Header("Live Parameter")]
		[SerializeField] private string m_JumpParameterId = JumpParameterId;
		[SerializeField] private string m_JumpParameterDisplayName = "Camera Jump";

		[Header("References")]
		[SerializeField] private Camera m_Camera;
		[SerializeField] private Transform m_Target;
		[SerializeField] private Transform m_AudienceReference;

		[Header("Jump")]
		[SerializeField] private Vector3 m_JumpPositionCenter = new Vector3(0f, 3f, -11f);
		[SerializeField] private Vector3 m_JumpPositionExtents = new Vector3(5f, 1.5f, 2f);
		[SerializeField] private Vector2 m_FieldOfViewRange = new Vector2(30f, 65f);
		[Min(0f)][SerializeField] private float m_MinimumAudienceSideDistance = 4f;
		[SerializeField] private int m_RandomSeed = 2718;

		[Header("Motion")]
		[SerializeField] private Vector2 m_MovementSpeedRange = new Vector2(0.15f, 0.5f);

		private bool m_GraphClockDriven;
		private bool m_Initialized;
		private bool m_Suspended;
		private float m_MovementSpeed;
		private Vector3 m_MovementDirection;
		private System.Random m_Random;

		public LiveParameterDefinition Definition => new LiveParameterDefinition(
			m_JumpParameterId, m_JumpParameterDisplayName, 0f, 1f, 0f);

		private void OnEnable() {
			m_GraphClockDriven = false;
			m_Initialized = false;
			m_Suspended = false;
			Initialize();
			ApplyCamera();
		}

		private void Update() {
			if (Application.isPlaying && !m_GraphClockDriven && !m_Suspended)
				Advance(Time.deltaTime);
		}

		private void OnValidate() {
			m_JumpPositionExtents.x = Mathf.Max(0f, m_JumpPositionExtents.x);
			m_JumpPositionExtents.y = Mathf.Max(0f, m_JumpPositionExtents.y);
			m_JumpPositionExtents.z = Mathf.Max(0f, m_JumpPositionExtents.z);
			m_FieldOfViewRange.x = Mathf.Clamp(m_FieldOfViewRange.x, 1f, 179f);
			m_FieldOfViewRange.y = Mathf.Clamp(m_FieldOfViewRange.y, m_FieldOfViewRange.x, 179f);
			m_MinimumAudienceSideDistance = Mathf.Max(0f, m_MinimumAudienceSideDistance);
			m_MovementSpeedRange.x = Mathf.Max(0f, m_MovementSpeedRange.x);
			m_MovementSpeedRange.y = Mathf.Max(m_MovementSpeedRange.x, m_MovementSpeedRange.y);
			if (!Application.isPlaying)
				m_Initialized = false;
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			m_GraphClockDriven = graphClockDriven;
			if (graphClockDriven)
				ApplyCamera();
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!m_GraphClockDriven || m_Suspended || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
				return;

			Advance((float)Math.Min(deltaSeconds, float.MaxValue));
		}

		public void SetSuspended(bool suspended) {
			m_Suspended = suspended;
			if (!suspended)
				ApplyCamera();
		}

		public bool TrySetValue(float value, out string rejectionReason) {
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				rejectionReason = "The camera jump parameter value must be finite.";
				return false;
			}

			if (value > Mathf.Epsilon)
				Jump();
			rejectionReason = string.Empty;
			return true;
		}

		public void Jump() {
			Initialize();
			if (m_Camera == null)
				return;

			m_Camera.transform.localPosition = SelectJumpPosition();
			m_Camera.fieldOfView = NextFloat(m_FieldOfViewRange.x, m_FieldOfViewRange.y);
			SelectMovement();
			ApplyCamera();
		}

		private void Advance(float deltaSeconds) {
			Initialize();
			if (m_Camera == null)
				return;

			if (deltaSeconds > 0f && !float.IsNaN(deltaSeconds) && !float.IsInfinity(deltaSeconds)) {
				var localPosition = m_Camera.transform.localPosition;
				localPosition += m_MovementDirection * (m_MovementSpeed * deltaSeconds);
				m_Camera.transform.localPosition = ConstrainToAudienceSide(localPosition);
			}
			ApplyCamera();
		}

		private void Initialize() {
			if (m_Camera == null)
				m_Camera = GetComponentInChildren<Camera>(true);
			if (m_Target == null)
				m_Target = transform.Find("Camera Target");
			if (m_AudienceReference == null)
				m_AudienceReference = transform.Find("Penlight");
			if (m_Camera == null)
				return;

			if (m_Initialized)
				return;

			m_Random = new System.Random(m_RandomSeed);
			m_Camera.transform.localPosition = ConstrainToAudienceSide(m_Camera.transform.localPosition);
			SelectMovement();
			m_Initialized = true;
		}

		private Vector3 SelectJumpPosition() {
			return ConstrainToAudienceSide(m_JumpPositionCenter + new Vector3(
				NextFloat(-m_JumpPositionExtents.x, m_JumpPositionExtents.x),
				NextFloat(-m_JumpPositionExtents.y, m_JumpPositionExtents.y),
				NextFloat(-m_JumpPositionExtents.z, m_JumpPositionExtents.z)));
		}

		private void SelectMovement() {
			m_MovementSpeed = NextFloat(m_MovementSpeedRange.x, m_MovementSpeedRange.y);
			if (!TryGetAudienceDirection(out _, out var audienceDirection)) {
				m_MovementDirection = Vector3.right;
				return;
			}

			var audienceTangent = Vector3.Cross(Vector3.up, audienceDirection).normalized;
			var angleRadians = NextFloat(-Mathf.PI * 0.5f, Mathf.PI * 0.5f);
			var audienceWeight = Mathf.Max(0f, Mathf.Cos(angleRadians));
			m_MovementDirection = audienceDirection * audienceWeight + audienceTangent * Mathf.Sin(angleRadians);
			m_MovementDirection.Normalize();
		}

		private Vector3 ConstrainToAudienceSide(Vector3 localPosition) {
			if (!TryGetAudienceDirection(out var targetLocalPosition, out var audienceDirection))
				return localPosition;

			var audienceSideDistance = Vector3.Dot(localPosition - targetLocalPosition, audienceDirection);
			if (audienceSideDistance >= m_MinimumAudienceSideDistance)
				return localPosition;

			return localPosition + audienceDirection * (m_MinimumAudienceSideDistance - audienceSideDistance);
		}

		private bool TryGetAudienceDirection(out Vector3 targetLocalPosition, out Vector3 audienceDirection) {
			targetLocalPosition = default;
			audienceDirection = default;
			if (m_Camera == null || m_Target == null || m_AudienceReference == null || m_Camera.transform.parent == null)
				return false;

			var cameraParent = m_Camera.transform.parent;
			targetLocalPosition = cameraParent.InverseTransformPoint(m_Target.position);
			var audienceLocalPosition = cameraParent.InverseTransformPoint(m_AudienceReference.position);
			audienceDirection = audienceLocalPosition - targetLocalPosition;
			audienceDirection.y = 0f;
			if (audienceDirection.sqrMagnitude <= 0.000001f)
				return false;

			audienceDirection.Normalize();
			return true;
		}

		private float NextFloat(float minimum, float maximum) {
			return Mathf.Lerp(minimum, maximum, (float)m_Random.NextDouble());
		}

		private void ApplyCamera() {
			Initialize();
			if (m_Camera == null)
				return;

			m_Camera.transform.localPosition = ConstrainToAudienceSide(m_Camera.transform.localPosition);
			if (m_Target == null)
				return;

			var targetDirection = m_Target.position - m_Camera.transform.position;
			if (targetDirection.sqrMagnitude > 0.000001f)
				m_Camera.transform.rotation = Quaternion.LookRotation(targetDirection, Vector3.up);
		}
	}
}
