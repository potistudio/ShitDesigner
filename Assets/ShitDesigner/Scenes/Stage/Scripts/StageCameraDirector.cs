using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using ShitDesigner.Main;
using ShitDesigner.Scene;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;
using UnityEngine.Video;

namespace ShitDesigner.Stage {
	public enum StageCameraCueMotion {
		Cut,
		Blend,
		Rail
	}

	public enum StageCameraCueCompletion {
		Hold,
		ResumeRandomDrift
	}

	[Serializable]
	public sealed class StageCameraCueDefinition {
		[SerializeField] private string m_DisplayName = "Camera Cue";
		[SerializeField] private StageCameraCueMotion m_Motion = StageCameraCueMotion.Blend;
		[SerializeField] private Vector3 m_LocalPosition = new Vector3(0f, 3f, -10f);
		[SerializeField] private Vector3 m_LocalEulerAngles;
		[SerializeField] private SplineContainer m_Path;
		[SerializeField] private Transform m_LookTarget;
		[SerializeField, Min(0f)] private float m_DurationBeats = 4f;
		[SerializeField] private AnimationCurve m_Easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
		[SerializeField, Range(1f, 179f)] private float m_FieldOfView = 45f;
		[SerializeField] private bool m_ControlVideoPlayhead;
		[SerializeField, Min(0f)] private float m_VideoPlayheadSeconds;
		[SerializeField] private StageCameraCueCompletion m_Completion = StageCameraCueCompletion.Hold;

		public string DisplayName => string.IsNullOrWhiteSpace(m_DisplayName) ? "Camera Cue" : m_DisplayName;
		public StageCameraCueMotion Motion => m_Motion;
		public Vector3 LocalPosition => m_LocalPosition;
		public Quaternion LocalRotation => Quaternion.Euler(m_LocalEulerAngles);
		public SplineContainer Path => m_Path;
		public Transform LookTarget => m_LookTarget;
		public float DurationBeats => Mathf.Max(0f, m_DurationBeats);
		public AnimationCurve Easing => m_Easing;
		public float FieldOfView => Mathf.Clamp(m_FieldOfView, 1f, 179f);
		public bool ControlsVideoPlayhead => m_ControlVideoPlayhead;
		public float VideoPlayheadSeconds => float.IsNaN(m_VideoPlayheadSeconds) || float.IsInfinity(m_VideoPlayheadSeconds)
			? 0f : Mathf.Max(0f, m_VideoPlayheadSeconds);
		public StageCameraCueCompletion Completion => m_Completion;
	}

	/// <summary>Recalls two authored, beat-synchronized Stage camera movements from live Hot Cues.</summary>
	[DisallowMultipleComponent]
	public sealed class StageCameraDirector : MonoBehaviour, ILiveSceneParameterProvider, ISceneGraphClockReceiver,
		IBpmClockReceiver, ISceneActivationReceiver {
		public const int CueCount = 2;
		public const string Cue1ParameterId = "camera_cue_1";
		public const string Cue2ParameterId = "camera_cue_2";
		private const double MinimumVideoSeekToleranceSeconds = 0.05d;

		[Header("References")]
		[SerializeField] private Camera m_Camera;
		[SerializeField] private Transform m_DefaultLookTarget;
		[SerializeField] private StageRandomCamera m_RandomCamera;
		[SerializeField] private VideoPlayer m_VideoPlayer;

		[Header("Hot Cues")]
		[SerializeField] private StageCameraCueDefinition[] m_Cues = {
			new StageCameraCueDefinition(),
			new StageCameraCueDefinition()
		};

		private readonly List<ILiveSceneParameter> m_LiveParameters = new List<ILiveSceneParameter>(CueCount);
		private bool m_GraphClockDriven;
		private bool m_IsSceneActive;
		private bool m_IsCuePlaying;
		private bool m_HasBpmFrame;
		private int m_ActiveCueIndex = -1;
		private double m_CueStartBeat;
		private double m_FallbackBeatProgress;
		private double m_LastAdjustedBeat;
		private float m_LastBpm = 120f;
		private Vector3 m_StartLocalPosition;
		private Quaternion m_StartLocalRotation;
		private float m_StartFieldOfView;
		private bool m_VideoSeekPending;
		private double m_PendingVideoPlayheadSeconds;
		private double m_ActiveVideoSeekSeconds;
		private VideoPlayer m_ObservedVideoPlayer;
		private bool m_VideoPauseSettling;
		private int m_VideoSeekEarliestFrame;
		private bool m_VideoSeekInFlight;
		private bool m_ResumeVideoAfterSeek;
		private bool m_VideoResumePending;
		private int m_VideoResumeEarliestFrame;
		private bool m_VideoFrameReadyOverrideActive;
		private bool m_PreviousSendFrameReadyEvents;
		private bool m_UsesManagedVideoOutput;
		private VideoRenderMode m_OriginalVideoRenderMode;
		private RenderTexture m_VideoOutputTexture;

		public IReadOnlyList<ILiveSceneParameter> LiveParameters {
			get {
				EnsureLiveParameters();
				return m_LiveParameters;
			}
		}
		public int ActiveCueIndex => m_ActiveCueIndex;
		public bool IsCuePlaying => m_IsCuePlaying;

		private void OnEnable() {
			m_GraphClockDriven = false;
			m_IsSceneActive = !Application.isPlaying;
			m_IsCuePlaying = false;
			m_HasBpmFrame = false;
			m_ActiveCueIndex = -1;
			ResolveReferences();
			m_RandomCamera?.SetSuspended(false);
			EnsureLiveParameters();
		}

		private void OnDisable() {
			RestoreVideoPlayback();
			RestoreVideoOutput();
			ObserveVideoPlayer(null);
			m_RandomCamera?.SetSuspended(false);
		}

		private void Update() {
			ApplyPendingVideoSeek();
			ApplyPendingVideoResume();
			if (!Application.isPlaying || m_GraphClockDriven || !m_IsSceneActive || !m_IsCuePlaying)
				return;

			AdvanceFallback(Time.deltaTime);
		}

		private void LateUpdate() {
			if (!m_UsesManagedVideoOutput || m_VideoOutputTexture == null || m_VideoPlayer == null || m_VideoPlayer.texture == null)
				return;

			Graphics.Blit(m_VideoPlayer.texture, m_VideoOutputTexture);
		}

		private void OnValidate() {
			if (m_Cues == null || m_Cues.Length != CueCount)
				Array.Resize(ref m_Cues, CueCount);
			for (var index = 0; index < m_Cues.Length; index++)
				if (m_Cues[index] == null) m_Cues[index] = new StageCameraCueDefinition();
		}

		public bool TriggerCue(int cueIndex, out string rejectionReason) {
			ResolveReferences();
			if (cueIndex < 0 || cueIndex >= CueCount || m_Cues == null || cueIndex >= m_Cues.Length || m_Cues[cueIndex] == null) {
				rejectionReason = "The Stage camera cue index must be 0 or 1.";
				return false;
			}
			if (m_Camera == null) {
				rejectionReason = "The Stage camera is not assigned.";
				return false;
			}

			var cue = m_Cues[cueIndex];
			if (cue.ControlsVideoPlayhead && m_VideoPlayer == null) {
				rejectionReason = "The Stage video player is not assigned.";
				return false;
			}

			m_ActiveCueIndex = cueIndex;
			m_IsCuePlaying = true;
			m_CueStartBeat = m_HasBpmFrame ? m_LastAdjustedBeat : 0d;
			m_FallbackBeatProgress = 0d;
			m_StartLocalPosition = m_Camera.transform.localPosition;
			m_StartLocalRotation = m_Camera.transform.localRotation;
			m_StartFieldOfView = m_Camera.fieldOfView;
			m_RandomCamera?.SetSuspended(true);
			QueueVideoSeek(cue);

			if (cue.Motion == StageCameraCueMotion.Cut || cue.DurationBeats <= Mathf.Epsilon) {
				ApplyCue(cue, 1f);
				CompleteCue(cue);
			}
			else {
				ApplyCue(cue, 0f);
			}

			rejectionReason = string.Empty;
			return true;
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			m_GraphClockDriven = graphClockDriven;
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!m_GraphClockDriven || m_HasBpmFrame || !m_IsSceneActive || !m_IsCuePlaying
				|| double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds <= 0d)
				return;

			AdvanceFallback((float)Math.Min(deltaSeconds, float.MaxValue));
		}

		public void SetBpmClock(BeatClockFrame frame) {
			m_HasBpmFrame = frame.IsAvailable;
			if (!frame.IsAvailable)
				return;

			m_LastBpm = frame.Bpm;
			m_LastAdjustedBeat = frame.AdjustedTotalBeats;
			if (!m_IsSceneActive || !m_IsCuePlaying)
				return;

			var cue = m_Cues[m_ActiveCueIndex];
			var normalized = cue.DurationBeats <= Mathf.Epsilon
				? 1f
				: (float)((frame.AdjustedTotalBeats - m_CueStartBeat) / cue.DurationBeats);
			AdvanceCue(cue, normalized);
		}

		public void ActivateScene() {
			m_IsSceneActive = true;
			if (m_IsCuePlaying) {
				m_CueStartBeat = m_HasBpmFrame ? m_LastAdjustedBeat : 0d;
				m_FallbackBeatProgress = 0d;
			}
		}

		public void DeactivateScene() {
			m_IsSceneActive = false;
			m_IsCuePlaying = false;
			m_ActiveCueIndex = -1;
			m_RandomCamera?.SetSuspended(false);
		}

		private void AdvanceFallback(float deltaSeconds) {
			m_FallbackBeatProgress += deltaSeconds * m_LastBpm / 60d;
			var cue = m_Cues[m_ActiveCueIndex];
			var normalized = cue.DurationBeats <= Mathf.Epsilon ? 1f : (float)(m_FallbackBeatProgress / cue.DurationBeats);
			AdvanceCue(cue, normalized);
		}

		private void AdvanceCue(StageCameraCueDefinition cue, float normalized) {
			var clamped = Mathf.Clamp01(normalized);
			ApplyCue(cue, clamped);
			if (normalized >= 1f)
				CompleteCue(cue);
		}

		private void ApplyCue(StageCameraCueDefinition cue, float normalized) {
			if (m_Camera == null)
				return;

			var eased = cue.Easing == null ? normalized : cue.Easing.Evaluate(normalized);
			if (cue.Motion == StageCameraCueMotion.Rail && cue.Path != null
				&& cue.Path.Evaluate(Mathf.Clamp01(eased), out float3 railPosition, out float3 railTangent, out float3 railUp)) {
				m_Camera.transform.position = (Vector3)railPosition;
				var lookTarget = cue.LookTarget != null ? cue.LookTarget : m_DefaultLookTarget;
				if (!TryLookAt(m_Camera.transform, lookTarget) && math.lengthsq(railTangent) > 0.000001f && math.lengthsq(railUp) > 0.000001f)
					m_Camera.transform.rotation = Quaternion.LookRotation((Vector3)railTangent, (Vector3)railUp);
			}
			else {
				m_Camera.transform.localPosition = Vector3.LerpUnclamped(m_StartLocalPosition, cue.LocalPosition, eased);
				var lookTarget = cue.LookTarget != null ? cue.LookTarget : m_DefaultLookTarget;
				var targetRotation = ResolveLocalRotation(m_Camera.transform, lookTarget, cue.LocalRotation);
				m_Camera.transform.localRotation = Quaternion.SlerpUnclamped(m_StartLocalRotation, targetRotation, eased);
			}
			m_Camera.fieldOfView = Mathf.LerpUnclamped(m_StartFieldOfView, cue.FieldOfView, eased);
		}

		private void CompleteCue(StageCameraCueDefinition cue) {
			m_IsCuePlaying = false;
			if (cue.Completion == StageCameraCueCompletion.ResumeRandomDrift) {
				m_ActiveCueIndex = -1;
				m_RandomCamera?.SetSuspended(false);
			}
		}

		private void ResolveReferences() {
			if (m_Camera == null) m_Camera = GetComponentInChildren<Camera>(true);
			if (m_DefaultLookTarget == null) m_DefaultLookTarget = transform.Find("Camera Target");
			if (m_RandomCamera == null) m_RandomCamera = GetComponent<StageRandomCamera>();
			if (m_VideoPlayer == null) m_VideoPlayer = GetComponentInChildren<VideoPlayer>(true);
			ObserveVideoPlayer(m_VideoPlayer);
			ConfigureVideoOutput();
		}

		private void ConfigureVideoOutput() {
			if (m_UsesManagedVideoOutput || m_VideoPlayer == null || m_VideoPlayer.targetTexture == null)
				return;

			m_OriginalVideoRenderMode = m_VideoPlayer.renderMode;
			m_VideoOutputTexture = m_VideoPlayer.targetTexture;
			if (m_VideoPlayer.renderMode != VideoRenderMode.APIOnly)
				m_VideoPlayer.renderMode = VideoRenderMode.APIOnly;
			m_UsesManagedVideoOutput = true;
		}

		private void RestoreVideoOutput() {
			if (!m_UsesManagedVideoOutput)
				return;

			if (m_VideoPlayer != null)
				m_VideoPlayer.renderMode = m_OriginalVideoRenderMode;
			m_UsesManagedVideoOutput = false;
			m_VideoOutputTexture = null;
		}

		private void QueueVideoSeek(StageCameraCueDefinition cue) {
			if (!cue.ControlsVideoPlayhead || m_VideoPlayer == null)
				return;

			var playhead = (double)cue.VideoPlayheadSeconds;
			var duration = m_VideoPlayer.clip == null ? m_VideoPlayer.length : m_VideoPlayer.clip.length;
			if (!double.IsNaN(duration) && !double.IsInfinity(duration) && duration > 0d)
				playhead = Math.Min(playhead, duration);
			m_PendingVideoPlayheadSeconds = playhead;
			m_VideoSeekPending = true;
			m_VideoResumePending = false;
			ApplyPendingVideoSeek();
		}

		private void ApplyPendingVideoSeek() {
			if (!m_VideoSeekPending || m_VideoSeekInFlight || m_VideoPlayer == null || !m_VideoPlayer.isPrepared)
				return;

			if (!m_VideoPauseSettling) {
				m_ResumeVideoAfterSeek = m_ResumeVideoAfterSeek || m_VideoPlayer.isPlaying;
				m_VideoPlayer.Pause();
				EnableVideoFrameReadyEvents();
				m_VideoPauseSettling = true;
				m_VideoSeekEarliestFrame = Time.frameCount + 1;
				return;
			}
			if (Time.frameCount < m_VideoSeekEarliestFrame)
				return;

			m_VideoPauseSettling = false;
			m_VideoSeekInFlight = true;
			m_ActiveVideoSeekSeconds = m_PendingVideoPlayheadSeconds;
			m_VideoPlayer.time = m_ActiveVideoSeekSeconds;
			m_VideoSeekPending = false;
		}

		private void ObserveVideoPlayer(VideoPlayer videoPlayer) {
			if (m_ObservedVideoPlayer == videoPlayer)
				return;
			if (m_ObservedVideoPlayer != null)
				m_ObservedVideoPlayer.frameReady -= OnVideoFrameReady;

			m_ObservedVideoPlayer = videoPlayer;
			if (m_ObservedVideoPlayer != null)
				m_ObservedVideoPlayer.frameReady += OnVideoFrameReady;
		}

		private void OnVideoFrameReady(VideoPlayer source, long _) {
			if (!m_VideoSeekInFlight || source != m_VideoPlayer)
				return;
			var frameTolerance = source.frameRate > 0f ? 0.5d / source.frameRate + 0.001d : 0d;
			var tolerance = Math.Max(MinimumVideoSeekToleranceSeconds, frameTolerance);
			if (Math.Abs(source.time - m_ActiveVideoSeekSeconds) > tolerance)
				return;

			m_VideoSeekInFlight = false;
			if (m_VideoSeekPending)
				return;

			m_VideoResumePending = true;
			m_VideoResumeEarliestFrame = Time.frameCount + 1;
		}

		private void EnableVideoFrameReadyEvents() {
			if (m_VideoFrameReadyOverrideActive || m_VideoPlayer == null)
				return;

			m_PreviousSendFrameReadyEvents = m_VideoPlayer.sendFrameReadyEvents;
			m_VideoPlayer.sendFrameReadyEvents = true;
			m_VideoFrameReadyOverrideActive = true;
		}

		private void ApplyPendingVideoResume() {
			if (!m_VideoResumePending || Time.frameCount < m_VideoResumeEarliestFrame)
				return;

			RestoreVideoPlayback();
		}

		private void RestoreVideoPlayback() {
			if (m_ResumeVideoAfterSeek && m_VideoPlayer != null && m_VideoPlayer.isPrepared)
				m_VideoPlayer.Play();
			if (m_VideoFrameReadyOverrideActive && m_VideoPlayer != null)
				m_VideoPlayer.sendFrameReadyEvents = m_PreviousSendFrameReadyEvents;
			m_ResumeVideoAfterSeek = false;
			m_VideoPauseSettling = false;
			m_VideoSeekInFlight = false;
			m_VideoResumePending = false;
			m_VideoFrameReadyOverrideActive = false;
		}

		private void EnsureLiveParameters() {
			if (m_LiveParameters.Count == CueCount)
				return;

			m_LiveParameters.Clear();
			m_LiveParameters.Add(new CueTriggerParameter(this, 0, Cue1ParameterId));
			m_LiveParameters.Add(new CueTriggerParameter(this, 1, Cue2ParameterId));
		}

		private static Quaternion ResolveLocalRotation(Transform cameraTransform, Transform lookTarget, Quaternion fallback) {
			if (lookTarget == null || cameraTransform.parent == null)
				return fallback;

			var direction = lookTarget.position - cameraTransform.position;
			if (direction.sqrMagnitude <= 0.000001f)
				return fallback;
			return Quaternion.Inverse(cameraTransform.parent.rotation) * Quaternion.LookRotation(direction, Vector3.up);
		}

		private static bool TryLookAt(Transform cameraTransform, Transform lookTarget) {
			if (lookTarget == null)
				return false;

			var direction = lookTarget.position - cameraTransform.position;
			if (direction.sqrMagnitude <= 0.000001f)
				return false;
			cameraTransform.rotation = Quaternion.LookRotation(direction, Vector3.up);
			return true;
		}

		private sealed class CueTriggerParameter : ILiveSceneParameter, ILiveSceneTriggerParameter {
			private readonly StageCameraDirector m_Owner;
			private readonly int m_CueIndex;
			private readonly string m_ParameterId;

			public CueTriggerParameter(StageCameraDirector owner, int cueIndex, string parameterId) {
				m_Owner = owner;
				m_CueIndex = cueIndex;
				m_ParameterId = parameterId;
			}

			public LiveParameterDefinition Definition {
				get {
					var displayName = m_Owner.m_Cues != null && m_CueIndex < m_Owner.m_Cues.Length && m_Owner.m_Cues[m_CueIndex] != null
						? m_Owner.m_Cues[m_CueIndex].DisplayName
						: "Camera Cue " + (m_CueIndex + 1);
					return new LiveParameterDefinition(m_ParameterId, displayName, 0f, 1f, 0f);
				}
			}

			public bool TrySetValue(float value, out string rejectionReason) {
				if (float.IsNaN(value) || float.IsInfinity(value)) {
					rejectionReason = "The Stage camera cue value must be finite.";
					return false;
				}
				if (value <= Mathf.Epsilon) {
					rejectionReason = string.Empty;
					return true;
				}
				return m_Owner.TriggerCue(m_CueIndex, out rejectionReason);
			}
		}
	}
}
