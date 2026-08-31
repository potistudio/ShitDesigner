using ShitDesigner.Media;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.AssetFlush {
	/// <summary>Fits the flash surface and selects media whenever the scene becomes active.</summary>
	[DisallowMultipleComponent]
	public sealed class AssetFlushScene : MonoBehaviour, ISceneActivationReceiver {
		[SerializeField] private Camera m_Camera;
		[SerializeField] private Transform m_Surface;
		[SerializeField] private AssetFlashComponent m_AssetFlash;

		private float m_LastAspect = -1f;
		private float m_LastOrthographicSize = -1f;

		private void OnEnable() {
			RefreshLayout();
		}

		private void Start() {
			if (Application.isPlaying) m_AssetFlash?.TryTriggerRandom();
		}

		private void LateUpdate() {
			if (m_Camera == null || !m_Camera.orthographic) return;
			if (Mathf.Approximately(m_LastAspect, m_Camera.aspect)
				&& Mathf.Approximately(m_LastOrthographicSize, m_Camera.orthographicSize)) return;
			RefreshLayout();
		}

		private void OnValidate() {
			RefreshLayout();
		}

		[ContextMenu("Refresh Layout")]
		public void RefreshLayout() {
			if (m_Camera == null || m_Surface == null || !m_Camera.orthographic) return;
			var height = m_Camera.orthographicSize * 2f;
			m_Surface.localScale = new Vector3(height * m_Camera.aspect, height, 1f);
			m_LastAspect = m_Camera.aspect;
			m_LastOrthographicSize = m_Camera.orthographicSize;
		}

		public void ActivateScene() {
			m_AssetFlash?.TryTriggerRandom();
		}

		public void DeactivateScene() {
			m_AssetFlash?.Clear();
		}
	}
}
