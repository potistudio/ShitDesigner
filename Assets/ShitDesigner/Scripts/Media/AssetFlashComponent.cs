using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Video;

namespace ShitDesigner.Media {
	public enum AssetFlashComponentMediaKind { None, Image, Video }

	[Serializable]
	public sealed class AssetFlashComponentSlot {
		[SerializeField] private AssetFlashComponentMediaKind m_Kind;
		[SerializeField] private Texture2D m_Image;
		[SerializeField] private VideoClip m_Video;
		[SerializeField, Tooltip("Key that triggers this slot. Set None to disable keyboard triggering.")] private Key m_KeyboardKey;
		[SerializeField, HideInInspector] private bool m_KeyboardKeyConfigured;

		public AssetFlashComponentMediaKind Kind => m_Kind;
		public Texture2D Image => m_Image;
		public VideoClip Video => m_Video;
		public Key KeyboardKey => m_KeyboardKey;

		internal void EnsureKeyboardKey(Key defaultKey) {
			if (m_KeyboardKeyConfigured) return;
			m_KeyboardKey = defaultKey;
			m_KeyboardKeyConfigured = true;
		}

		public void SetImage(Texture2D image) {
			m_Kind = image == null ? AssetFlashComponentMediaKind.None : AssetFlashComponentMediaKind.Image;
			m_Image = image;
			m_Video = null;
		}

		public void SetVideo(VideoClip video) {
			m_Kind = video == null ? AssetFlashComponentMediaKind.None : AssetFlashComponentMediaKind.Video;
			m_Image = null;
			m_Video = video;
		}
	}

	/// <summary>
	/// Standalone Unity component facade for an eight-slot image/video flash.
	/// Press the configured keyboard key, call Trigger(1..8), or wire
	/// FireSlot1..FireSlot8 directly to UnityEvents.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class AssetFlashComponent : MonoBehaviour {
		private const int SlotCount = 8;
		private static readonly Key[] DefaultKeyboardKeys = {
			Key.Digit1,
			Key.Digit2,
			Key.Digit3,
			Key.Digit4,
			Key.Digit5,
			Key.Digit6,
			Key.Digit7,
			Key.Digit8
		};

		[SerializeField, Min(.01f)] private float m_DurationSeconds = .25f;
		[SerializeField] private bool m_UseUnscaledTime = true;
		[SerializeField] private bool m_UseKeyboardInput = true;
		[SerializeField] private Renderer m_TargetRenderer;
		[SerializeField] private string m_TextureProperty = "_BaseMap";
		[SerializeField] private bool m_DisableRendererWhenIdle = true;
		[SerializeField] private AssetFlashComponentSlot[] m_Slots = CreateSlots();

		private readonly VideoPlayer[] m_Players = new VideoPlayer[SlotCount];
		private readonly Dictionary<VideoPlayer, int> m_PlayerSlots = new Dictionary<VideoPlayer, int>();
		private MaterialPropertyBlock m_PropertyBlock;
		private int m_ActiveSlot = -1;
		private double m_VisibleUntil;
		private Texture m_OutputTexture;
		private bool m_Initialized;

		public int ActiveSlot => m_ActiveSlot < 0 ? 0 : m_ActiveSlot + 1;
		public Texture OutputTexture => m_OutputTexture;
		public float DurationSeconds { get => m_DurationSeconds; set => m_DurationSeconds = Mathf.Max(.01f, value); }
		public bool UseKeyboardInput { get => m_UseKeyboardInput; set => m_UseKeyboardInput = value; }
		public event Action<Texture> OutputChanged;

		private static AssetFlashComponentSlot[] CreateSlots() {
			var slots = new AssetFlashComponentSlot[SlotCount];
			for (var index = 0; index < slots.Length; index++) slots[index] = new AssetFlashComponentSlot();
			return slots;
		}

		private void Awake() {
			EnsureSlots();
			InitializePlayers();
			ApplyOutput(null);
		}

		private void Update() {
			PollKeyboard();
			if (m_ActiveSlot < 0) return;
			if (Now >= m_VisibleUntil) { Clear(); return; }
			var slot = m_Slots[m_ActiveSlot];
			if (slot.Kind == AssetFlashComponentMediaKind.Video) ApplyOutput(m_Players[m_ActiveSlot]?.texture);
		}

		private void PollKeyboard() {
			if (!m_UseKeyboardInput) return;
			var keyboard = Keyboard.current;
			if (keyboard == null) return;
			for (var index = 0; index < m_Slots.Length; index++) {
				var key = m_Slots[index].KeyboardKey;
				if (key != Key.None && keyboard[key].wasPressedThisFrame) TryTrigger(index + 1);
			}
		}

		private void OnDisable() { Clear(); }

		private void OnDestroy() {
			for (var index = 0; index < m_Players.Length; index++) DestroyPlayer(index);
			m_PlayerSlots.Clear();
		}

		private void OnValidate() {
			m_DurationSeconds = Mathf.Max(.01f, m_DurationSeconds);
			if (string.IsNullOrWhiteSpace(m_TextureProperty)) m_TextureProperty = "_BaseMap";
			EnsureSlots();
		}

		public void Trigger(int slotNumber) {
			if (!TryTrigger(slotNumber)) throw new ArgumentOutOfRangeException(nameof(slotNumber), "Slot number must be between 1 and 8.");
		}

		public bool TryTrigger(int slotNumber) {
			if (slotNumber < 1 || slotNumber > SlotCount) return false;
			EnsureInitialized();
			var index = slotNumber - 1;
			StopVideosExcept(index);
			m_ActiveSlot = index;
			m_VisibleUntil = Now + Math.Max(.01d, m_DurationSeconds);
			var slot = m_Slots[index];
			if (slot.Kind == AssetFlashComponentMediaKind.Image) ApplyOutput(slot.Image);
			else if (slot.Kind == AssetFlashComponentMediaKind.Video) RestartVideo(index);
			else ApplyOutput(null);
			return true;
		}

		public void Clear() {
			StopVideosExcept(-1);
			m_ActiveSlot = -1;
			m_VisibleUntil = 0d;
			ApplyOutput(null);
		}

		public void SetImage(int slotNumber, Texture2D image) {
			var index = ValidateSlot(slotNumber);
			EnsureInitialized();
			DestroyPlayer(index);
			m_Slots[index].SetImage(image);
			if (m_ActiveSlot == index) ApplyOutput(image);
		}

		public void SetVideo(int slotNumber, VideoClip video) {
			var index = ValidateSlot(slotNumber);
			EnsureInitialized();
			DestroyPlayer(index);
			m_Slots[index].SetVideo(video);
			CreatePlayer(index);
			if (m_ActiveSlot == index) RestartVideo(index);
		}

		public void FireSlot1() => Trigger(1);
		public void FireSlot2() => Trigger(2);
		public void FireSlot3() => Trigger(3);
		public void FireSlot4() => Trigger(4);
		public void FireSlot5() => Trigger(5);
		public void FireSlot6() => Trigger(6);
		public void FireSlot7() => Trigger(7);
		public void FireSlot8() => Trigger(8);

		private double Now => m_UseUnscaledTime ? Time.unscaledTimeAsDouble : Time.timeAsDouble;

		private void EnsureInitialized() {
			EnsureSlots();
			if (!m_Initialized) InitializePlayers();
		}

		private void EnsureSlots() {
			if (m_Slots == null || m_Slots.Length != SlotCount) {
				var previous = m_Slots;
				m_Slots = CreateSlots();
				if (previous != null) Array.Copy(previous, m_Slots, Math.Min(previous.Length, m_Slots.Length));
			}
			for (var index = 0; index < m_Slots.Length; index++)
				if (m_Slots[index] == null) m_Slots[index] = new AssetFlashComponentSlot();
			for (var index = 0; index < m_Slots.Length; index++) m_Slots[index].EnsureKeyboardKey(DefaultKeyboardKeys[index]);
		}

		private void InitializePlayers() {
			if (m_Initialized) return;
			m_Initialized = true;
			for (var index = 0; index < SlotCount; index++) CreatePlayer(index);
		}

		private void CreatePlayer(int index) {
			var slot = m_Slots[index];
			if (slot.Kind != AssetFlashComponentMediaKind.Video || slot.Video == null || m_Players[index] != null) return;
			var host = new GameObject("AssetFlash.VideoSlot" + (index + 1));
			host.hideFlags = HideFlags.HideAndDontSave;
			host.transform.SetParent(transform, false);
			var player = host.AddComponent<VideoPlayer>();
			player.playOnAwake = false;
			player.isLooping = false;
			player.waitForFirstFrame = true;
			player.skipOnDrop = true;
			player.renderMode = VideoRenderMode.APIOnly;
			player.audioOutputMode = VideoAudioOutputMode.None;
			player.source = UnityEngine.Video.VideoSource.VideoClip;
			player.clip = slot.Video;
			player.prepareCompleted += OnVideoPrepared;
			m_Players[index] = player;
			m_PlayerSlots[player] = index;
			player.Prepare();
		}

		private void DestroyPlayer(int index) {
			var player = m_Players[index];
			if (player == null) return;
			player.prepareCompleted -= OnVideoPrepared;
			m_PlayerSlots.Remove(player);
			m_Players[index] = null;
			if (Application.isPlaying) Destroy(player.gameObject);
			else DestroyImmediate(player.gameObject);
		}

		private void OnVideoPrepared(VideoPlayer player) {
			if (!m_PlayerSlots.TryGetValue(player, out var index) || index != m_ActiveSlot || Now >= m_VisibleUntil) return;
			RestartVideo(index);
		}

		private void RestartVideo(int index) {
			var player = m_Players[index];
			if (player == null) { ApplyOutput(null); return; }
			if (!player.isPrepared) { player.Prepare(); ApplyOutput(null); return; }
			player.Pause();
			player.frame = 0;
			player.Play();
			ApplyOutput(player.texture);
		}

		private void StopVideosExcept(int activeIndex) {
			for (var index = 0; index < m_Players.Length; index++)
				if (index != activeIndex && m_Players[index] != null && m_Players[index].isPlaying) m_Players[index].Pause();
		}

		private void ApplyOutput(Texture texture) {
			if (ReferenceEquals(m_OutputTexture, texture) && (m_TargetRenderer == null || m_TargetRenderer.enabled == (texture != null || !m_DisableRendererWhenIdle))) return;
			m_OutputTexture = texture;
			if (m_TargetRenderer != null) {
				if (m_PropertyBlock == null) m_PropertyBlock = new MaterialPropertyBlock();
				m_PropertyBlock.Clear();
				if (texture != null) m_PropertyBlock.SetTexture(m_TextureProperty, texture);
				m_TargetRenderer.SetPropertyBlock(m_PropertyBlock);
				if (m_DisableRendererWhenIdle) m_TargetRenderer.enabled = texture != null;
			}
			OutputChanged?.Invoke(texture);
		}

		private static int ValidateSlot(int slotNumber) {
			if (slotNumber < 1 || slotNumber > SlotCount) throw new ArgumentOutOfRangeException(nameof(slotNumber), "Slot number must be between 1 and 8.");
			return slotNumber - 1;
		}
	}
}
