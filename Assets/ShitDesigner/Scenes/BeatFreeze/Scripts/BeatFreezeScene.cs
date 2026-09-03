using System;
using ShitDesigner.Core;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.Video;

namespace ShitDesigner.BeatFreeze {
	/// <summary>Holds a video frame at the start of each shared-clock beat.</summary>
	[DisallowMultipleComponent]
	public sealed class BeatFreezeScene : MonoBehaviour, IBpmClockReceiver, ISceneActivationReceiver {
		[SerializeField] private VideoClip m_Video;
		[SerializeField] private Renderer m_TargetRenderer;
		[SerializeField] private string m_TextureProperty = "_BaseMap";
		[Range(0f, 1f)][SerializeField] private float m_FreezeBeats = .5f;
		[SerializeField] private bool m_Loop = true;
		[Range(30f, 300f)][SerializeField] private float m_PreviewBpm = 120f;

		private VideoPlayer m_Player;
		private MaterialPropertyBlock m_PropertyBlock;
		private double m_PreviewTotalBeats;
		private float m_BeatPhase;
		private bool m_HasExternalClock;
		private bool m_IsActive;
		private bool m_IsFrozen;

		private void Awake() {
			EnsurePlayer();
			ApplyOutput(null);
		}

		private void OnEnable() {
			EnsurePlayer();
			Prepare();
		}

		private void Update() {
			if (!Application.isPlaying || m_HasExternalClock)
				return;

			m_PreviewTotalBeats += Math.Max(0d, Time.unscaledDeltaTime) * m_PreviewBpm / 60d;
			m_BeatPhase = (float)(m_PreviewTotalBeats - Math.Floor(m_PreviewTotalBeats));
			SyncPlayback();
		}

		private void OnDisable() => Stop();

		private void OnDestroy() => DestroyPlayer();

		private void OnValidate() {
			m_FreezeBeats = Mathf.Clamp01(m_FreezeBeats);
			m_PreviewBpm = Mathf.Clamp(m_PreviewBpm, 30f, 300f);
			if (string.IsNullOrWhiteSpace(m_TextureProperty))
				m_TextureProperty = "_BaseMap";
		}

		public void SetBpmClock(BeatClockFrame frame) {
			if (!frame.IsAvailable || double.IsNaN(frame.AdjustedTotalBeats) || double.IsInfinity(frame.AdjustedTotalBeats))
				return;

			m_HasExternalClock = true;
			m_BeatPhase = frame.BeatPhase;
			SyncPlayback();
		}

		public void ActivateScene() {
			m_IsActive = true;
			Prepare();
			SyncPlayback();
		}

		public void DeactivateScene() {
			m_IsActive = false;
			Stop();
		}

		private void EnsurePlayer() {
			if (m_Player != null)
				return;

			var host = new GameObject("BeatFreeze.Video") {
				hideFlags = HideFlags.HideAndDontSave
			};
			host.transform.SetParent(transform, false);
			m_Player = host.AddComponent<VideoPlayer>();
			m_Player.playOnAwake = false;
			m_Player.waitForFirstFrame = true;
			m_Player.skipOnDrop = true;
			m_Player.renderMode = VideoRenderMode.APIOnly;
			m_Player.audioOutputMode = VideoAudioOutputMode.None;
			m_Player.prepareCompleted += OnPrepared;
		}

		private void Prepare() {
			if (m_Player == null || m_Video == null || (m_Player.isPrepared && m_Player.clip == m_Video))
				return;

			m_Player.Stop();
			m_Player.source = VideoSource.VideoClip;
			m_Player.clip = m_Video;
			m_Player.isLooping = m_Loop;
			m_Player.Prepare();
		}

		private void OnPrepared(VideoPlayer player) {
			if (player != m_Player || player.clip != m_Video)
				return;

			ApplyOutput(player.texture);
			SyncPlayback();
		}

		private void SyncPlayback() {
			if (m_Player == null || !m_Player.isPrepared || !m_IsActive)
				return;

			var shouldFreeze = m_BeatPhase < m_FreezeBeats;
			if (shouldFreeze) {
				if (!m_IsFrozen || m_Player.isPlaying)
					m_Player.Pause();
			}
			else if (m_IsFrozen || !m_Player.isPlaying) {
				m_Player.Play();
			}
			m_IsFrozen = shouldFreeze;
		}

		private void Stop() {
			m_IsFrozen = false;
			if (m_Player != null)
				m_Player.Stop();
			ApplyOutput(null);
		}

		private void DestroyPlayer() {
			if (m_Player == null)
				return;

			m_Player.prepareCompleted -= OnPrepared;
			var host = m_Player.gameObject;
			m_Player = null;
			if (Application.isPlaying)
				Destroy(host);
			else
				DestroyImmediate(host);
		}

		private void ApplyOutput(Texture texture) {
			if (m_TargetRenderer == null)
				return;

			if (m_PropertyBlock == null)
				m_PropertyBlock = new MaterialPropertyBlock();
			m_PropertyBlock.Clear();
			if (texture != null)
				m_PropertyBlock.SetTexture(m_TextureProperty, texture);
			m_TargetRenderer.SetPropertyBlock(m_PropertyBlock);
			m_TargetRenderer.enabled = texture != null;
		}
	}
}
