using System;
using ShitDesigner.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Scene {
	/// <summary>Renders random full-height vertical strokes for each beat.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class VerticalBeatStrokesScene : MonoBehaviour, IBpmClockReceiver {
		[Header("Canvas")]
		[Min(1f)][SerializeField] private Vector2 m_CanvasSize = new Vector2(16f, 9f);

		[Header("Strokes")]
		[Range(1, 64)][SerializeField] private int m_StrokeCount = 12;
		[Min(.001f)][SerializeField] private float m_MinWidth = .01f;
		[Min(.001f)][SerializeField] private float m_MaxWidth = .24f;
		[SerializeField] private AnimationCurve m_WidthEase = CreateEaseOutCurve();
		[ColorUsage(false, true)][SerializeField] private Color m_Color = Color.white;

		[Header("Beat")]
		[Range(30f, 300f)][SerializeField] private float m_PreviewBpm = 120f;

		[Header("Randomness")]
		[SerializeField] private bool m_RandomizeOnPlay = true;
		[SerializeField] private int m_Seed = 8127;

		private Transform m_StrokeRoot;
		private Material m_Material;
		private LineRenderer[] m_Strokes = Array.Empty<LineRenderer>();
		private float[] m_InitialWidths = Array.Empty<float>();
		private double m_TotalBeats;
		private long m_LastBeat = long.MinValue;
		private int m_GenerationSeed;
		private bool m_UsesExternalClock;

		private void OnEnable() {
			m_TotalBeats = 0d;
			m_LastBeat = long.MinValue;
			m_UsesExternalClock = false;
			m_GenerationSeed = GenerationSeed();
			GenerateForBeat(0L);
		}

		private void Update() {
			if (Application.isPlaying && !m_UsesExternalClock) {
				m_TotalBeats += Math.Max(0d, Time.unscaledDeltaTime) * m_PreviewBpm / 60d;
				ProcessBeat(m_TotalBeats);
			}

			ApplyWidths(BeatPhase(m_TotalBeats));
		}

		private void OnDisable() => ReleaseGeneratedStrokes();

		private void OnDestroy() => ReleaseGeneratedStrokes();

		private void OnValidate() {
			m_CanvasSize.x = Mathf.Max(1f, m_CanvasSize.x);
			m_CanvasSize.y = Mathf.Max(1f, m_CanvasSize.y);
			m_StrokeCount = Mathf.Clamp(m_StrokeCount, 1, 64);
			m_MinWidth = Mathf.Max(.001f, m_MinWidth);
			m_MaxWidth = Mathf.Max(m_MinWidth, m_MaxWidth);
			m_PreviewBpm = Mathf.Clamp(m_PreviewBpm, 30f, 300f);
			if (!Application.isPlaying && isActiveAndEnabled)
				GenerateForBeat(0L);
		}

		public void SetBpmClock(BeatClockFrame frame) {
			if (!frame.IsAvailable || double.IsNaN(frame.AdjustedTotalBeats) || double.IsInfinity(frame.AdjustedTotalBeats))
				return;

			m_UsesExternalClock = true;
			m_TotalBeats = frame.AdjustedTotalBeats;
			ProcessBeat(m_TotalBeats);
			ApplyWidths(BeatPhase(m_TotalBeats));
		}

		[ContextMenu("Generate Strokes")]
		public void Generate() {
			m_GenerationSeed = GenerationSeed();
			GenerateForBeat((long)Math.Floor(m_TotalBeats));
		}

		private void ProcessBeat(double totalBeats) {
			var beat = (long)Math.Floor(totalBeats + 1e-9d);
			if (beat != m_LastBeat)
				GenerateForBeat(beat);
		}

		private void GenerateForBeat(long beat) {
			ReleaseGeneratedStrokes();
			m_LastBeat = beat;
			var random = new System.Random(BeatSeed(beat));
			var halfWidth = m_CanvasSize.x * .5f;
			var halfHeight = m_CanvasSize.y * .5f;
			m_StrokeRoot = new GameObject("Vertical Beat Strokes") {
				layer = gameObject.layer,
				hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave
			}.transform;
			m_StrokeRoot.SetParent(transform, false);
			m_Material = CreateMaterial();
			m_Strokes = new LineRenderer[m_StrokeCount];
			m_InitialWidths = new float[m_StrokeCount];

			for (var index = 0; index < m_StrokeCount; index++) {
				var stroke = CreateStroke(index, NextFloat(random, -halfWidth, halfWidth), halfHeight);
				stroke.startColor = m_Color;
				stroke.endColor = m_Color;
				m_Strokes[index] = stroke;
				m_InitialWidths[index] = NextFloat(random, m_MinWidth, m_MaxWidth);
			}

			ApplyWidths(BeatPhase(m_TotalBeats));
		}

		private LineRenderer CreateStroke(int index, float x, float halfHeight) {
			var strokeObject = new GameObject($"Stroke {index + 1:00}") {
				layer = gameObject.layer,
				hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave
			};
			strokeObject.transform.SetParent(m_StrokeRoot, false);
			var stroke = strokeObject.AddComponent<LineRenderer>();
			stroke.useWorldSpace = false;
			stroke.sharedMaterial = m_Material;
			stroke.numCornerVertices = 0;
			stroke.numCapVertices = 0;
			stroke.alignment = LineAlignment.View;
			stroke.shadowCastingMode = ShadowCastingMode.Off;
			stroke.receiveShadows = false;
			stroke.allowOcclusionWhenDynamic = false;
			stroke.positionCount = 2;
			stroke.SetPositions(new[] { new Vector3(x, -halfHeight, 0f), new Vector3(x, halfHeight, 0f) });
			return stroke;
		}

		private void ApplyWidths(float phase) {
			var eased = m_WidthEase == null || m_WidthEase.length == 0
				? phase * (2f - phase)
				: Mathf.Clamp01(m_WidthEase.Evaluate(phase));
			for (var index = 0; index < m_Strokes.Length; index++)
				if (m_Strokes[index] != null)
					m_Strokes[index].widthMultiplier = Mathf.Lerp(m_InitialWidths[index], 0f, eased);
		}

		private static float BeatPhase(double totalBeats) => Mathf.Clamp01((float)(totalBeats - Math.Floor(totalBeats)));

		private int GenerationSeed() => Application.isPlaying && m_RandomizeOnPlay ? Environment.TickCount : m_Seed;

		private int BeatSeed(long beat) {
			unchecked {
				var hash = (int)(beat ^ (beat >> 32));
				hash ^= hash >> 16;
				hash *= 0x45d9f3b;
				hash ^= hash >> 16;
				return m_GenerationSeed ^ hash;
			}
		}

		private static Material CreateMaterial() {
			var shader = Shader.Find("Universal Render Pipeline/Unlit")
				?? Shader.Find("Sprites/Default")
				?? Shader.Find("Unlit/Color");
			if (shader == null)
				throw new InvalidOperationException("An unlit shader is required for vertical beat strokes.");

			var material = new Material(shader) { hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave };
			if (material.HasProperty("_Cull"))
				material.SetInt("_Cull", (int)CullMode.Off);
			return material;
		}

		private void ReleaseGeneratedStrokes() {
			if (m_StrokeRoot != null)
				DestroyOwned(m_StrokeRoot.gameObject);
			if (m_Material != null)
				DestroyOwned(m_Material);
			m_StrokeRoot = null;
			m_Material = null;
			m_Strokes = Array.Empty<LineRenderer>();
			m_InitialWidths = Array.Empty<float>();
		}

		private static float NextFloat(System.Random random, float minimum, float maximum)
			=> Mathf.Lerp(minimum, maximum, (float)random.NextDouble());

		private static AnimationCurve CreateEaseOutCurve() => new AnimationCurve(
			new Keyframe(0f, 0f, 0f, 2f),
			new Keyframe(1f, 1f, 0f, 0f));

		private static void DestroyOwned(UnityEngine.Object value) {
			if (Application.isPlaying)
				Destroy(value);
			else
				DestroyImmediate(value);
		}
	}
}
