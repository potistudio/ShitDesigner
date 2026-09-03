using UnityEngine;
using UnityEngine.Video;

namespace ShitDesigner.Stage {
	/// <summary>Authored tempo of a video at normal playback speed.</summary>
	[DisallowMultipleComponent]
	[RequireComponent(typeof(VideoPlayer))]
	public sealed class VideoBpmMetadata : MonoBehaviour {
		[SerializeField, Min(1f)] private float m_Bpm = 120f;

		public float Bpm => float.IsNaN(m_Bpm) || float.IsInfinity(m_Bpm) ? 120f : Mathf.Max(1f, m_Bpm);

		private void OnValidate() {
			m_Bpm = Bpm;
		}
	}
}
