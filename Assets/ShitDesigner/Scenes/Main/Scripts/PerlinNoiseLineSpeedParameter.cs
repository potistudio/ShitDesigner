using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Publishes the graph-clock speed multiplier for a Perlin noise line controller.</summary>
	[DisallowMultipleComponent]
	public sealed class PerlinNoiseLineSpeedParameter : LiveSceneParameter {
		public const string ParameterId = "line_speed";

		private const float MinimumSpeedMultiplier = 0f;
		private const float MaximumSpeedMultiplier = 2f;

		[SerializeField] private string m_Id = ParameterId;
		[SerializeField] private string m_DisplayName = "Line Speed";
		[SerializeField] private PerlinNoiseLineClockController m_Controller;
		[Range(MinimumSpeedMultiplier, MaximumSpeedMultiplier)][SerializeField] private float m_Value = 1f;

		public override LiveParameterDefinition Definition => new LiveParameterDefinition(
			m_Id, m_DisplayName, MinimumSpeedMultiplier, MaximumSpeedMultiplier, m_Value);

		public override void InitializeParameter() {
			if (m_Controller == null)
				m_Controller = GetComponent<PerlinNoiseLineClockController>();

			ApplyValue();
		}

		public override bool TrySetValue(float value, out string rejectionReason) {
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				rejectionReason = "The parameter value must be finite.";
				return false;
			}
			if (m_Controller == null) {
				rejectionReason = "The Perlin noise line clock controller is missing.";
				return false;
			}

			m_Value = Mathf.Clamp(value, MinimumSpeedMultiplier, MaximumSpeedMultiplier);
			ApplyValue();
			rejectionReason = string.Empty;
			return true;
		}

		private void OnValidate() {
			m_Value = Mathf.Clamp(m_Value, MinimumSpeedMultiplier, MaximumSpeedMultiplier);
		}

		private void ApplyValue() {
			if (m_Controller != null)
				m_Controller.SetClockSpeedMultiplier(m_Value);
		}
	}
}
