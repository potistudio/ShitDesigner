using System;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Publishes a one-shot live control that assigns a random field of view to the scene camera.</summary>
	[DisallowMultipleComponent]
	public sealed class LiveRandomFieldOfViewTrigger : LiveSceneParameter, ILiveSceneTriggerParameter {
		public const string ParameterId = "random_fov";

		[Header("Live Parameter")]
		[SerializeField] private string m_Id = ParameterId;
		[SerializeField] private string m_DisplayName = "Random Field of View";

		[Header("Random Field of View")]
		[SerializeField] private Camera m_Camera;
		[SerializeField] private Vector2 m_FieldOfViewRange = new Vector2(35f, 85f);
		[SerializeField] private int m_RandomSeed = 9202;

		private System.Random m_Random;

		public override LiveParameterDefinition Definition => new LiveParameterDefinition(m_Id, m_DisplayName, 0f, 1f, 0f);

		private void OnValidate() {
			m_FieldOfViewRange.x = Mathf.Clamp(m_FieldOfViewRange.x, 1f, 179f);
			m_FieldOfViewRange.y = Mathf.Clamp(m_FieldOfViewRange.y, m_FieldOfViewRange.x, 179f);
		}

		public override void InitializeParameter() {
			if (m_Camera == null)
				m_Camera = GetComponentInChildren<Camera>(true);
			m_Random = new System.Random(m_RandomSeed);
		}

		public override bool TrySetValue(float value, out string rejectionReason) {
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				rejectionReason = "The random field-of-view trigger value must be finite.";
				return false;
			}

			if (value <= Mathf.Epsilon) {
				rejectionReason = string.Empty;
				return true;
			}

			if (m_Camera == null)
				InitializeParameter();
			if (m_Camera == null) {
				rejectionReason = "The random field-of-view trigger requires a scene camera.";
				return false;
			}

			m_Random ??= new System.Random(m_RandomSeed);
			m_Camera.fieldOfView = Mathf.Lerp(m_FieldOfViewRange.x, m_FieldOfViewRange.y, (float)m_Random.NextDouble());
			rejectionReason = string.Empty;
			return true;
		}
	}
}
