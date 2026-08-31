using ShitDesigner.Main;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Publishes the number of DVD bounce visuals.</summary>
	[DisallowMultipleComponent]
	public sealed class DvdBounceInstanceCountParameter : LiveSceneParameter {
		public const string ParameterId = "instance_count";

		[SerializeField] private string m_Id = ParameterId;
		[SerializeField] private string m_DisplayName = "Instance Count";
		[SerializeField] private DvdBounceScene m_Scene;

		public override LiveParameterDefinition Definition => new LiveParameterDefinition(
			m_Id, m_DisplayName, DvdBounceScene.MinimumInstanceCount, DvdBounceScene.MaximumInstanceCount,
			m_Scene == null ? DvdBounceScene.MinimumInstanceCount : m_Scene.InstanceCount);

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

			m_Scene.SetInstanceCount(Mathf.RoundToInt(value));
			rejectionReason = string.Empty;
			return true;
		}
	}
}
