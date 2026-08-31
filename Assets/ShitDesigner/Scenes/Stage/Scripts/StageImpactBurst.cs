using System;
using System.Collections.Generic;
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

		private readonly Dictionary<Texture2D, VisualEffect> m_TextureEffects = new();
		private readonly List<GameObject> m_CreatedEffectObjects = new();
		private int m_PlayEventId;
		private int m_ImpactTextureId;
		private Texture2D m_CurrentTexture;
		private bool m_Initialized;
		private double m_LastBeatIndex = double.NaN;

		private void Awake() {
			if (m_VisualEffect == null)
				throw new InvalidOperationException($"VisualEffect component is not assigned in {nameof(StageImpactBurst)} on {gameObject.name}.");

			m_PlayEventId = Shader.PropertyToID(m_PlayEventName);
			m_ImpactTextureId = Shader.PropertyToID(m_ImpactTextureName);
			InitializeTextureEffects();

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
			if (texture == null) {
				m_VisualEffect.SendEvent(m_PlayEventId);
				return;
			}

			m_CurrentTexture = texture;
			m_TextureEffects[texture].SendEvent(m_PlayEventId);
		}

		private void InitializeTextureEffects() {
			foreach (var texture in m_Textures) {
				if (texture == null || m_TextureEffects.ContainsKey(texture)) continue;

				var visualEffect = m_TextureEffects.Count == 0 ? m_VisualEffect : CreateVisualEffect(texture);
				visualEffect.SetTexture(m_ImpactTextureId, texture);
				m_TextureEffects.Add(texture, visualEffect);
			}
		}

		private VisualEffect CreateVisualEffect(Texture2D texture) {
			var effectObject = new GameObject($"{m_VisualEffect.gameObject.name} ({texture.name})") {
				hideFlags = HideFlags.DontSave,
				layer = m_VisualEffect.gameObject.layer
			};
			effectObject.transform.SetParent(m_VisualEffect.transform, false);

			var visualEffect = effectObject.AddComponent<VisualEffect>();
			visualEffect.enabled = false;
			visualEffect.visualEffectAsset = m_VisualEffect.visualEffectAsset;
			visualEffect.initialEventName = m_VisualEffect.initialEventName;
			visualEffect.startSeed = m_VisualEffect.startSeed;
			visualEffect.resetSeedOnPlay = m_VisualEffect.resetSeedOnPlay;
			visualEffect.allowInstancing = m_VisualEffect.allowInstancing;
			visualEffect.releaseInstanceWhenDisabled = m_VisualEffect.releaseInstanceWhenDisabled;
			visualEffect.playRate = m_VisualEffect.playRate;
			visualEffect.pause = m_VisualEffect.pause;
			visualEffect.enabled = m_VisualEffect.enabled;

			m_CreatedEffectObjects.Add(effectObject);
			return visualEffect;
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

		private void OnDestroy() {
			foreach (var effectObject in m_CreatedEffectObjects) {
				if (effectObject != null) Destroy(effectObject);
			}
		}
	}
}
