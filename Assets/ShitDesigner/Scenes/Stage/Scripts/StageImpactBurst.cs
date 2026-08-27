using UnityEngine;
using UnityEngine.VFX;

namespace ShitDesigner.Stage {
	[DisallowMultipleComponent]
	[RequireComponent(typeof(VisualEffect))]
	public sealed class StageImpactBurst : MonoBehaviour {
		private static readonly int PlayEventId = Shader.PropertyToID("OnPlay");

		private VisualEffect _visualEffect;

		private void Awake() {
			_visualEffect = GetComponent<VisualEffect>();
		}

		public void Fire() {
			if (_visualEffect == null) _visualEffect = GetComponent<VisualEffect>();
			if (_visualEffect == null || !_visualEffect.enabled || _visualEffect.visualEffectAsset == null) return;

			_visualEffect.Reinit();
			_visualEffect.SendEvent(PlayEventId);
		}
	}
}
