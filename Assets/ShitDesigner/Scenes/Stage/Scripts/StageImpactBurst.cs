using System;
using ShitDesigner.Core;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.VFX;

namespace ShitDesigner.Stage {
	[DisallowMultipleComponent]
	[RequireComponent(typeof(VisualEffect))]
	public sealed class StageImpactBurst : MonoBehaviour, IBpmClockReceiver {
		[SerializeField] private String m_PlayEventName = "OnPlay";
		[SerializeField] private String m_ImpactTextureName = "Impact Texture";

		[SerializeField] private VisualEffect m_VisualEffect;
		[SerializeField] private Texture2D[] m_Textures = Array.Empty<Texture2D>();

		private int PlayEventId;
		private int ImpactTextureId;
		private Texture2D m_CurrentTexture;
		private bool m_Initialized;
		private double m_LastBeatIndex = double.NaN;

		private void Awake() {
			if (m_VisualEffect == null)
				throw new InvalidOperationException($"VisualEffect component is not assigned in {nameof(StageImpactBurst)} on {gameObject.name}.");

			PlayEventId = Shader.PropertyToID(m_PlayEventName);
			ImpactTextureId = Shader.PropertyToID(m_ImpactTextureName);

			m_Initialized = true;
		}

		private void OnEnable() {
			m_LastBeatIndex = double.NaN;
		}

		public void SetBpmClock(BeatClockFrame frame) {
			if (!frame.IsAvailable || double.IsNaN(frame.AdjustedTotalBeats) || double.IsInfinity(frame.AdjustedTotalBeats))
				return;

			var beatIndex = Math.Floor(frame.AdjustedTotalBeats + 1e-9d);
			if (double.IsNaN(m_LastBeatIndex) || beatIndex < m_LastBeatIndex) {
				m_LastBeatIndex = beatIndex;
				return;
			}

			if (beatIndex <= m_LastBeatIndex)
				return;

			m_LastBeatIndex = beatIndex;
			Fire();
		}

		public void Fire() {
			if (!m_Initialized)
				return;

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
