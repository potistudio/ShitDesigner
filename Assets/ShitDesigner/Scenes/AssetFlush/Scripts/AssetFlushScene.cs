using System;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.Video;

namespace ShitDesigner.AssetFlush {
	/// <summary>Selects and flashes media while managing the AssetFlush scene surface.</summary>
	[DisallowMultipleComponent]
	public sealed class AssetFlushScene : MonoBehaviour, ISceneActivationReceiver {
		[SerializeField] private Camera m_Camera;
		[SerializeField] private Transform m_Surface;
		[SerializeField, Min(.01f)] private float m_DurationSeconds = .25f;
		[SerializeField] private bool m_UseUnscaledTime = true;
		[SerializeField] private Renderer m_TargetRenderer;
		[SerializeField] private string m_TextureProperty = "_BaseMap";
		[SerializeField] private bool m_DisableRendererWhenIdle = true;
		[SerializeField] private Texture2D[] m_Images = Array.Empty<Texture2D>();
		[SerializeField] private VideoClip[] m_Videos = Array.Empty<VideoClip>();
		[SerializeField, Tooltip("Size the surface to fill the camera frame.")]
		private bool m_FullScreen = true;
		[SerializeField, HideInInspector, Tooltip("Surface width and height when Full Screen is disabled.")]
		private Vector2 m_Size = Vector2.one;

		private MaterialPropertyBlock m_PropertyBlock;
		private VideoPlayer m_Player;
		private VideoClip m_ActiveVideo;
		private double m_VisibleUntil;
		private Texture m_OutputTexture;
		private bool m_IsActive;
		private float m_LastAspect = -1f;
		private float m_LastOrthographicSize = -1f;

		public int AvailableAssetCount {
			get {
				EnsureAssetCollections();
				return CountAvailableAssets();
			}
		}
		public Texture OutputTexture => m_OutputTexture;
		public float DurationSeconds { get => m_DurationSeconds; set => m_DurationSeconds = Mathf.Max(.01f, value); }

		private void Awake() {
			EnsureAssetCollections();
			ApplyOutput(null);
		}

		private void OnEnable() {
			RefreshLayout();
		}

		private void Start() {
			if (Application.isPlaying) TryTriggerRandom();
		}

		private void Update() {
			if (!m_IsActive) return;
			if (Now >= m_VisibleUntil) { Clear(); return; }
			if (m_ActiveVideo != null) ApplyOutput(m_Player?.texture);
		}

		private void LateUpdate() {
			if (m_Camera == null || !m_Camera.orthographic) return;
			if (Mathf.Approximately(m_LastAspect, m_Camera.aspect)
				&& Mathf.Approximately(m_LastOrthographicSize, m_Camera.orthographicSize)) return;
			RefreshLayout();
		}

		private void OnDisable() { Clear(); }

		private void OnDestroy() { DestroyPlayer(); }

		private void OnValidate() {
			m_DurationSeconds = Mathf.Max(.01f, m_DurationSeconds);
			if (string.IsNullOrWhiteSpace(m_TextureProperty)) m_TextureProperty = "_BaseMap";
			EnsureAssetCollections();
			RefreshLayout();
		}

		public bool TryTriggerRandom() {
			EnsureAssetCollections();
			var availableAssetCount = CountAvailableAssets();
			if (availableAssetCount == 0) {
				Clear();
				return false;
			}

			var selection = UnityEngine.Random.Range(0, availableAssetCount);
			for (var index = 0; index < m_Images.Length; index++) {
				var image = m_Images[index];
				if (image == null) continue;
				if (selection-- == 0) {
					ShowImage(image);
					return true;
				}
			}
			for (var index = 0; index < m_Videos.Length; index++) {
				var video = m_Videos[index];
				if (video == null) continue;
				if (selection-- == 0) {
					ShowVideo(video);
					return true;
				}
			}
			return false;
		}

		public void SetImages(params Texture2D[] images) {
			m_Images = images == null ? Array.Empty<Texture2D>() : (Texture2D[])images.Clone();
		}

		public void SetVideos(params VideoClip[] videos) {
			m_Videos = videos == null ? Array.Empty<VideoClip>() : (VideoClip[])videos.Clone();
		}

		public void Clear() {
			StopPlayer();
			m_ActiveVideo = null;
			m_VisibleUntil = 0d;
			m_IsActive = false;
			ApplyOutput(null);
		}

		[ContextMenu("Refresh Layout")]
		public void RefreshLayout() {
			if (m_Camera == null || m_Surface == null || !m_Camera.orthographic) return;
			var fullScreenHeight = m_Camera.orthographicSize * 2f;
			var size = m_FullScreen
				? new Vector2(fullScreenHeight * m_Camera.aspect, fullScreenHeight)
				: m_Size;
			m_Surface.localScale = new Vector3(size.x, size.y, 1f);
			m_LastAspect = m_Camera.aspect;
			m_LastOrthographicSize = m_Camera.orthographicSize;
		}

		public void ActivateScene() {
			TryTriggerRandom();
		}

		public void DeactivateScene() {
			Clear();
		}

		private double Now => m_UseUnscaledTime ? Time.unscaledTimeAsDouble : Time.timeAsDouble;

		private void ShowImage(Texture2D image) {
			StopPlayer();
			m_ActiveVideo = null;
			BeginFlash();
			ApplyOutput(image);
		}

		private void ShowVideo(VideoClip video) {
			EnsurePlayer();
			m_ActiveVideo = video;
			m_VisibleUntil = 0d;
			m_IsActive = false;
			m_Player.Stop();
			m_Player.clip = video;
			m_Player.Prepare();
			ApplyOutput(null);
		}

		private void BeginFlash() {
			m_VisibleUntil = Now + Math.Max(.01d, m_DurationSeconds);
			m_IsActive = true;
		}

		private int CountAvailableAssets() {
			var count = 0;
			for (var index = 0; index < m_Images.Length; index++)
				if (m_Images[index] != null) count++;
			for (var index = 0; index < m_Videos.Length; index++)
				if (m_Videos[index] != null) count++;
			return count;
		}

		private void EnsureAssetCollections() {
			if (m_Images == null) m_Images = Array.Empty<Texture2D>();
			if (m_Videos == null) m_Videos = Array.Empty<VideoClip>();
		}

		private void EnsurePlayer() {
			if (m_Player != null) return;
			var host = new GameObject("AssetFlush.Video");
			host.hideFlags = HideFlags.HideAndDontSave;
			host.transform.SetParent(transform, false);
			m_Player = host.AddComponent<VideoPlayer>();
			m_Player.playOnAwake = false;
			m_Player.isLooping = false;
			m_Player.waitForFirstFrame = true;
			m_Player.skipOnDrop = true;
			m_Player.renderMode = VideoRenderMode.APIOnly;
			m_Player.audioOutputMode = VideoAudioOutputMode.None;
			m_Player.source = UnityEngine.Video.VideoSource.VideoClip;
			m_Player.prepareCompleted += OnVideoPrepared;
		}

		private void OnVideoPrepared(VideoPlayer player) {
			if (player != m_Player || m_ActiveVideo == null || player.clip != m_ActiveVideo) return;
			BeginFlash();
			player.frame = 0;
			player.Play();
			ApplyOutput(player.texture);
		}

		private void StopPlayer() {
			if (m_Player != null) m_Player.Stop();
		}

		private void DestroyPlayer() {
			if (m_Player == null) return;
			m_Player.prepareCompleted -= OnVideoPrepared;
			var host = m_Player.gameObject;
			m_Player = null;
			if (Application.isPlaying) Destroy(host);
			else DestroyImmediate(host);
		}

		private void ApplyOutput(Texture texture) {
			if (ReferenceEquals(m_OutputTexture, texture) && (m_TargetRenderer == null || m_TargetRenderer.enabled == (texture != null || !m_DisableRendererWhenIdle))) return;
			m_OutputTexture = texture;
			if (m_TargetRenderer == null) return;
			if (m_PropertyBlock == null) m_PropertyBlock = new MaterialPropertyBlock();
			m_PropertyBlock.Clear();
			if (texture != null) m_PropertyBlock.SetTexture(m_TextureProperty, texture);
			m_TargetRenderer.SetPropertyBlock(m_PropertyBlock);
			if (m_DisableRendererWhenIdle) m_TargetRenderer.enabled = texture != null;
		}
	}
}
