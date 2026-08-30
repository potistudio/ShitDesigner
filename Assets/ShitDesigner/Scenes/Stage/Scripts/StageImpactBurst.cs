using System;
using UnityEngine;
using UnityEngine.VFX;

namespace ShitDesigner.Stage {
	[DisallowMultipleComponent]
	[RequireComponent(typeof(VisualEffect))]
	public sealed class StageImpactBurst : MonoBehaviour {
		private static readonly int PlayEventId = Shader.PropertyToID("OnPlay");
		private static readonly int ImpactTextureId = Shader.PropertyToID("Impact Texture");

		[SerializeField] private Texture2D[] m_Textures = Array.Empty<Texture2D>();

		private VisualEffect m_VisualEffect;
		private Texture2D m_CurrentTexture;

		private void Awake() {
			m_VisualEffect = GetComponent<VisualEffect>();
		}

		public void Fire() {
			if (m_VisualEffect == null) m_VisualEffect = GetComponent<VisualEffect>();
			if (m_VisualEffect == null || !m_VisualEffect.enabled || m_VisualEffect.visualEffectAsset == null) return;

			m_VisualEffect.Reinit();
			var texture = SelectTexture();
			if (texture != null) {
				m_CurrentTexture = texture;
				m_VisualEffect.SetTexture(ImpactTextureId, texture);
			}

			m_VisualEffect.SendEvent(PlayEventId);
		}

		private Texture2D SelectTexture() {
			var candidateCount = 0;
			foreach (var texture in m_Textures) {
				if (texture != null && texture != m_CurrentTexture) candidateCount++;
			}

			if (candidateCount == 0) {
				return m_CurrentTexture == null ? FirstTexture() : m_CurrentTexture;
			}

			var candidateIndex = UnityEngine.Random.Range(0, candidateCount);
			foreach (var texture in m_Textures) {
				if (texture == null || texture == m_CurrentTexture) continue;
				if (candidateIndex-- == 0) return texture;
			}

			return null;
		}

		private Texture2D FirstTexture() {
			foreach (var texture in m_Textures) {
				if (texture != null) return texture;
			}

			return null;
		}
	}
}
