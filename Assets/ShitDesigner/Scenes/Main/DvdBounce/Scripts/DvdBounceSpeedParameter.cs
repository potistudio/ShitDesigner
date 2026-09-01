using ShitDesigner.Main;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Publishes the DVD bounce movement speed.</summary>
	[DisallowMultipleComponent]
	public sealed class DvdBounceSpeedParameter : LiveSceneParameter {
		public const string ParameterId = "speed";
		public const float MaximumSpeed = 30f;

		[SerializeField] private string m_Id = ParameterId;
		[SerializeField] private string m_DisplayName = "Speed";
		[SerializeField] private DvdBounceScene m_Scene;

		public override LiveParameterDefinition Definition => new LiveParameterDefinition(
			m_Id, m_DisplayName, DvdBounceScene.MinimumSpeed, MaximumSpeed,
			m_Scene == null ? DvdBounceScene.MinimumSpeed : m_Scene.Speed);

		public override void InitializeParameter() {
			if (m_Scene == null)
				m_Scene = GetComponentInChildren<DvdBounceScene>(true);
		}

		public override bool TrySetValue(float value, out string rejectionReason) {
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				rejectionReason = "The parameter value must be finite.";
				return false;
			}
			if (m_Scene == null) {
				rejectionReason = "The DVD bounce scene is missing.";
				return false;
			}

			m_Scene.SetSpeed(Mathf.Clamp(value, DvdBounceScene.MinimumSpeed, MaximumSpeed));
			rejectionReason = string.Empty;
			return true;
		}
	}
}
