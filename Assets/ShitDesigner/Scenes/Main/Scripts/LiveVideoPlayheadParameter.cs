using System;
using ShitDesigner.Media;
using UnityEngine;
using UnityEngine.Video;

namespace ShitDesigner.Main {
	/// <summary>Publishes a VideoPlayer playhead in seconds for live control.</summary>
	[DisallowMultipleComponent]
	public sealed class LiveVideoPlayheadParameter : LiveSceneParameter {
		public const string ParameterId = VideoPlayerContract.PlayheadParameterId;

		[SerializeField] private string m_Id = ParameterId;
		[SerializeField] private string m_DisplayName = "Video Playhead";
		[SerializeField] private VideoPlayer m_VideoPlayer;

		private float m_Value;
		private bool m_SeekPending;

		public override LiveParameterDefinition Definition => new LiveParameterDefinition(
			m_Id, m_DisplayName, 0f, DurationSeconds, m_Value);

		public override void InitializeParameter() {
			ResolveVideoPlayer();
			if (m_VideoPlayer == null)
				return;

			m_Value = ClampToDuration(m_VideoPlayer.time);
		}

		private void Update() {
			if (m_SeekPending && m_VideoPlayer != null && m_VideoPlayer.isPrepared)
				ApplySeek();
		}

		public override bool TrySetValue(float value, out string rejectionReason) {
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				rejectionReason = "The video playhead must be finite.";
				return false;
			}

			ResolveVideoPlayer();
			if (m_VideoPlayer == null) {
				rejectionReason = "The video player is missing.";
				return false;
			}

			m_Value = ClampToDuration(value);
			m_SeekPending = true;
			if (m_VideoPlayer.isPrepared)
				ApplySeek();
			rejectionReason = string.Empty;
			return true;
		}

		private float DurationSeconds {
			get {
				ResolveVideoPlayer();
				if (m_VideoPlayer == null)
					return 0f;

				var duration = m_VideoPlayer.clip == null ? m_VideoPlayer.length : m_VideoPlayer.clip.length;
				return double.IsNaN(duration) || double.IsInfinity(duration) || duration <= 0d
					? 0f
					: (float)Math.Min(duration, float.MaxValue);
			}
		}

		private void ResolveVideoPlayer() {
			if (m_VideoPlayer == null)
				m_VideoPlayer = GetComponentInChildren<VideoPlayer>(true);
		}

		private float ClampToDuration(double value) {
			if (double.IsNaN(value) || double.IsInfinity(value))
				return 0f;

			var maximum = DurationSeconds;
			return Mathf.Clamp((float)Math.Max(0d, value), 0f, maximum);
		}

		private void ApplySeek() {
			m_VideoPlayer.time = m_Value;
			m_SeekPending = false;
		}
	}
}
