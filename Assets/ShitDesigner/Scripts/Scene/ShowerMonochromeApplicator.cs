using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Marks this hierarchy for monochrome rendering.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class ShowerMonochromeApplicator : MonoBehaviour {
		public const uint RenderingLayerMask = 1u << 8;

		private void OnEnable() {
			Apply();
		}

		private void OnTransformChildrenChanged() {
			Apply();
		}

		private void OnValidate() {
			Apply();
		}

		[ContextMenu("Apply Shower Monochrome")]
		public void Apply() {
			foreach (var renderer in GetComponentsInChildren<Renderer>(true))
				renderer.renderingLayerMask = RenderingLayerMask;
		}
	}
}
