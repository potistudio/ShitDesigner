using System.Collections.Generic;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Marks this hierarchy for monochrome rendering.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class ShowerMonochromeApplicator : MonoBehaviour {
		public const uint RenderingLayerMask = 1u << 8;
		private readonly Dictionary<Renderer, uint> _originalRenderingLayerMasks = new Dictionary<Renderer, uint>();
		private bool _monochromeEnabled;

		private void OnEnable() {
			ApplyState();
		}

		private void OnTransformChildrenChanged() {
			ApplyState();
		}

		private void OnValidate() {
			ApplyState();
		}

		[ContextMenu("Apply Shower Monochrome")]
		public void Apply() => SetMonochromeEnabled(true);

		public void SetMonochromeEnabled(bool enabled) {
			_monochromeEnabled = enabled;
			ApplyState();
		}

		private void ApplyState() {
			foreach (var renderer in GetComponentsInChildren<Renderer>(true)) {
				if (!_originalRenderingLayerMasks.TryGetValue(renderer, out var originalRenderingLayerMask)) {
					originalRenderingLayerMask = renderer.renderingLayerMask;
					_originalRenderingLayerMasks.Add(renderer, originalRenderingLayerMask);
				}

				renderer.renderingLayerMask = _monochromeEnabled ? RenderingLayerMask : originalRenderingLayerMask;
			}
		}
	}
}
