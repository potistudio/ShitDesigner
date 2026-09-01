using System;
using System.Collections.Generic;
using ShitDesigner.Main;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.Video;

namespace ShitDesigner.AssetFlush {
	[Serializable]
	public sealed class AssetFlushImageEntry {
		[SerializeField] private string m_Id = string.Empty;
		[SerializeField] private Texture2D m_Image;

		public string Id => (m_Id ?? string.Empty).Trim();
		public Texture2D Image => m_Image;

		public AssetFlushImageEntry() { }

		public AssetFlushImageEntry(string id, Texture2D image) {
			m_Id = id ?? string.Empty;
			m_Image = image;
		}
	}

	[Serializable]
	public sealed class AssetFlushVideoEntry {
		[SerializeField] private string m_Id = string.Empty;
		[SerializeField] private VideoClip m_Video;

		public string Id => (m_Id ?? string.Empty).Trim();
		public VideoClip Video => m_Video;

		public AssetFlushVideoEntry() { }

		public AssetFlushVideoEntry(string id, VideoClip video) {
			m_Id = id ?? string.Empty;
			m_Video = video;
		}
	}

	/// <summary>Selects and flashes media while managing the AssetFlush scene surface.</summary>
	[DisallowMultipleComponent]
	public sealed class AssetFlushScene : MonoBehaviour, ISceneActivationReceiver, ILiveSceneParameterProvider {
		[SerializeField] private Camera m_Camera;
		[SerializeField] private Transform m_Surface;
		[SerializeField, Min(0f)] private float m_FadeOutSeconds = .15f;
		[SerializeField] private bool m_UseUnscaledTime = true;
		[SerializeField] private Renderer m_TargetRenderer;
		[SerializeField] private string m_TextureProperty = "_BaseMap";
		[SerializeField] private bool m_DisableRendererWhenIdle = true;
		[SerializeField] private AssetFlushImageEntry[] m_Images = Array.Empty<AssetFlushImageEntry>();
		[SerializeField] private AssetFlushVideoEntry[] m_Videos = Array.Empty<AssetFlushVideoEntry>();
		[SerializeField, Tooltip("Size the surface to fill the camera frame.")]
		private bool m_FullScreen = true;
		[SerializeField, HideInInspector, Tooltip("Surface width and height when Full Screen is disabled.")]
		private Vector2 m_Size = Vector2.one;

		private MaterialPropertyBlock m_PropertyBlock;
		private readonly List<string> m_HeldIds = new List<string>();
		private VideoPlayer m_Player;
		private VideoClip m_ActiveVideo;
		private string m_ActiveId = string.Empty;
		private double m_FadeStartedAt;
		private Texture m_OutputTexture;
		private float m_Opacity;
		private float m_AppliedOpacity = -1f;
		private bool m_IsFading;
		private float m_LastAspect = -1f;
		private float m_LastOrthographicSize = -1f;

		public int AvailableAssetCount {
			get {
				EnsureAssetCollections();
				return CountAvailableAssets();
			}
		}
		public Texture OutputTexture => m_OutputTexture;
		public float Opacity => m_Opacity;
		public float FadeOutSeconds { get => m_FadeOutSeconds; set => m_FadeOutSeconds = Mathf.Max(0f, value); }
		public IReadOnlyList<ILiveSceneParameter> LiveParameters {
			get {
				EnsureAssetCollections();
				var parameters = new List<ILiveSceneParameter>();
				var parameterIds = new HashSet<string>(StringComparer.Ordinal);
				for (var index = 0; index < m_Images.Length; index++) {
					var entry = m_Images[index];
					if (entry != null && entry.Image != null && !string.IsNullOrWhiteSpace(entry.Id) && parameterIds.Add(entry.Id))
						parameters.Add(new LiveAssetParameter(this, entry.Id));
				}
				for (var index = 0; index < m_Videos.Length; index++) {
					var entry = m_Videos[index];
					if (entry != null && entry.Video != null && !string.IsNullOrWhiteSpace(entry.Id) && parameterIds.Add(entry.Id))
						parameters.Add(new LiveAssetParameter(this, entry.Id));
				}
				return parameters;
			}
		}

		private void Awake() {
			EnsureAssetCollections();
			ApplyOutput(null);
		}

		private void OnEnable() {
			RefreshLayout();
		}

		private void Update() {
			if (m_ActiveVideo != null) ApplyOutput(m_Player?.texture);
			if (!m_IsFading) return;
			var progress = m_FadeOutSeconds <= Mathf.Epsilon
				? 1f
				: Mathf.Clamp01((float)((Now - m_FadeStartedAt) / m_FadeOutSeconds));
			if (progress >= 1f) { Clear(); return; }
			SetOpacity(1f - progress);
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
			m_FadeOutSeconds = Mathf.Max(0f, m_FadeOutSeconds);
			if (string.IsNullOrWhiteSpace(m_TextureProperty)) m_TextureProperty = "_BaseMap";
			EnsureAssetCollections();
			RefreshLayout();
		}

		public bool TryTriggerRandom() {
			EnsureAssetCollections();
			m_HeldIds.Clear();
			var availableAssetCount = CountAvailableAssets();
			if (availableAssetCount == 0) {
				Clear();
				return false;
			}

			var selection = UnityEngine.Random.Range(0, availableAssetCount);
			for (var index = 0; index < m_Images.Length; index++) {
				var image = m_Images[index]?.Image;
				if (image == null) continue;
				if (selection-- == 0) {
					ShowImage(string.Empty, image);
					return true;
				}
			}
			for (var index = 0; index < m_Videos.Length; index++) {
				var video = m_Videos[index]?.Video;
				if (video == null) continue;
				if (selection-- == 0) {
					ShowVideo(string.Empty, video);
					return true;
				}
			}
			return false;
		}

		public bool TryTrigger(string id) => TrySetTrigger(id, true);

		public bool TrySetTrigger(string id, bool isPressed) {
			var normalizedId = (id ?? string.Empty).Trim();
			if (normalizedId.Length == 0) return false;
			if (!isPressed) {
				ReleaseTrigger(normalizedId);
				return true;
			}
			if (m_HeldIds.Contains(normalizedId)) return true;
			if (!TryShowAsset(normalizedId)) return false;
			m_HeldIds.Add(normalizedId);
			return true;
		}

		private bool TryShowAsset(string id) {
			EnsureAssetCollections();
			var matchingAssetCount = 0;
			for (var index = 0; index < m_Images.Length; index++) {
				var entry = m_Images[index];
				if (entry?.Image != null && string.Equals(entry.Id, id, StringComparison.Ordinal)) matchingAssetCount++;
			}
			for (var index = 0; index < m_Videos.Length; index++) {
				var entry = m_Videos[index];
				if (entry?.Video != null && string.Equals(entry.Id, id, StringComparison.Ordinal)) matchingAssetCount++;
			}
			if (matchingAssetCount == 0) return false;

			var selection = UnityEngine.Random.Range(0, matchingAssetCount);
			for (var index = 0; index < m_Images.Length; index++) {
				var entry = m_Images[index];
				if (entry?.Image == null || !string.Equals(entry.Id, id, StringComparison.Ordinal)) continue;
				if (selection-- == 0) {
					ShowImage(id, entry.Image);
					return true;
				}
			}
			for (var index = 0; index < m_Videos.Length; index++) {
				var entry = m_Videos[index];
				if (entry?.Video == null || !string.Equals(entry.Id, id, StringComparison.Ordinal)) continue;
				if (selection-- == 0) {
					ShowVideo(id, entry.Video);
					return true;
				}
			}
			return false;
		}

		private void ReleaseTrigger(string id) {
			if (!m_HeldIds.Remove(id) || !string.Equals(m_ActiveId, id, StringComparison.Ordinal)) return;
			while (m_HeldIds.Count > 0) {
				var fallbackId = m_HeldIds[m_HeldIds.Count - 1];
				if (TryShowAsset(fallbackId)) return;
				m_HeldIds.RemoveAt(m_HeldIds.Count - 1);
			}
			m_ActiveId = string.Empty;
			BeginFadeOut();
		}

		public void SetImages(params Texture2D[] images) {
			if (images == null) {
				m_Images = Array.Empty<AssetFlushImageEntry>();
				return;
			}
			m_Images = new AssetFlushImageEntry[images.Length];
			for (var index = 0; index < images.Length; index++)
				m_Images[index] = new AssetFlushImageEntry(string.Empty, images[index]);
		}

		public void SetImageEntries(params AssetFlushImageEntry[] images) {
			m_Images = images == null ? Array.Empty<AssetFlushImageEntry>() : (AssetFlushImageEntry[])images.Clone();
		}

		public void SetVideos(params VideoClip[] videos) {
			if (videos == null) {
				m_Videos = Array.Empty<AssetFlushVideoEntry>();
				return;
			}
			m_Videos = new AssetFlushVideoEntry[videos.Length];
			for (var index = 0; index < videos.Length; index++)
				m_Videos[index] = new AssetFlushVideoEntry(string.Empty, videos[index]);
		}

		public void SetVideoEntries(params AssetFlushVideoEntry[] videos) {
			m_Videos = videos == null ? Array.Empty<AssetFlushVideoEntry>() : (AssetFlushVideoEntry[])videos.Clone();
		}

		public void Clear() {
			StopPlayer();
			m_HeldIds.Clear();
			m_ActiveVideo = null;
			m_ActiveId = string.Empty;
			m_FadeStartedAt = 0d;
			m_IsFading = false;
			m_Opacity = 0f;
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
			// Main owns trigger timing. AssetFlush stays transparent until an input is pressed.
		}

		public void DeactivateScene() {
			Clear();
		}

		private double Now => m_UseUnscaledTime ? Time.unscaledTimeAsDouble : Time.timeAsDouble;

		private void ShowImage(string id, Texture2D image) {
			StopPlayer();
			m_ActiveVideo = null;
			BeginHold(id);
			ApplyOutput(image);
		}

		private void ShowVideo(string id, VideoClip video) {
			EnsurePlayer();
			m_ActiveVideo = video;
			BeginHold(id);
			m_Player.Stop();
			m_Player.clip = video;
			m_Player.Prepare();
			ApplyOutput(null);
		}

		private void BeginHold(string id) {
			m_ActiveId = id ?? string.Empty;
			m_FadeStartedAt = 0d;
			m_IsFading = false;
			SetOpacity(1f);
		}

		private void BeginFadeOut() {
			if (m_FadeOutSeconds <= Mathf.Epsilon) { Clear(); return; }
			m_FadeStartedAt = Now;
			m_IsFading = true;
		}

		private int CountAvailableAssets() {
			var count = 0;
			for (var index = 0; index < m_Images.Length; index++)
				if (m_Images[index]?.Image != null) count++;
			for (var index = 0; index < m_Videos.Length; index++)
				if (m_Videos[index]?.Video != null) count++;
			return count;
		}

		private void EnsureAssetCollections() {
			if (m_Images == null) m_Images = Array.Empty<AssetFlushImageEntry>();
			if (m_Videos == null) m_Videos = Array.Empty<AssetFlushVideoEntry>();
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
			if (UnityEngine.Application.isPlaying) Destroy(host);
			else DestroyImmediate(host);
		}

		private void ApplyOutput(Texture texture) {
			var rendererEnabled = texture != null || !m_DisableRendererWhenIdle;
			if (ReferenceEquals(m_OutputTexture, texture) && Mathf.Approximately(m_AppliedOpacity, m_Opacity)
				&& (m_TargetRenderer == null || m_TargetRenderer.enabled == rendererEnabled)) return;
			m_OutputTexture = texture;
			if (m_TargetRenderer == null) return;
			if (m_PropertyBlock == null) m_PropertyBlock = new MaterialPropertyBlock();
			m_PropertyBlock.Clear();
			if (texture != null) m_PropertyBlock.SetTexture(m_TextureProperty, texture);
			m_PropertyBlock.SetFloat("_Opacity", m_Opacity);
			m_TargetRenderer.SetPropertyBlock(m_PropertyBlock);
			m_AppliedOpacity = m_Opacity;
			if (m_DisableRendererWhenIdle) m_TargetRenderer.enabled = texture != null;
		}

		private void SetOpacity(float opacity) {
			m_Opacity = Mathf.Clamp01(opacity);
			ApplyOutput(m_OutputTexture);
		}

		private sealed class LiveAssetParameter : ILiveSceneParameter, ILiveSceneTriggerParameter {
			private readonly AssetFlushScene m_Scene;
			private readonly string m_Id;

			public LiveParameterDefinition Definition => new LiveParameterDefinition(m_Id, m_Id, 0f, 1f, 0f);

			public LiveAssetParameter(AssetFlushScene scene, string id) {
				m_Scene = scene;
				m_Id = id;
			}

			public bool TrySetValue(float value, out string rejectionReason) {
				if (float.IsNaN(value) || float.IsInfinity(value)) {
					rejectionReason = "The parameter value must be finite.";
					return false;
				}
				if (!m_Scene.TrySetTrigger(m_Id, value > Mathf.Epsilon)) {
					rejectionReason = "The AssetFlush asset is no longer available: " + m_Id + ".";
					return false;
				}
				rejectionReason = string.Empty;
				return true;
			}
		}
	}
}
