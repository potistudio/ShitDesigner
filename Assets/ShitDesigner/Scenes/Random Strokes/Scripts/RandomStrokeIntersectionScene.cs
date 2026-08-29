using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Scene {
	/// <summary>Draws full-height vertical strokes that contract over each beat.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class RandomStrokeIntersectionScene : MonoBehaviour, IBpmClockReceiver {
		[Header("Canvas")]
		[Min(1f)][SerializeField] private Vector2 m_CanvasSize = new Vector2(16f, 9f);

		[Header("Strokes")]
		[Range(1, 64)][SerializeField] private int m_StrokeCount = 8;
		[Min(0.001f)][SerializeField] private float m_MinStrokeWidth = 0.01f;
		[Min(0.001f)][SerializeField] private float m_MaxStrokeWidth = 0.24f;
		[SerializeField] private AnimationCurve m_WidthEase = CreateWidthEase();

		[Header("Beat")]
		[Range(30f, 300f)][SerializeField] private float m_PreviewBpm = 120f;

		[Header("Randomness")]
		[SerializeField] private bool m_RandomizeOnPlay = true;
		[SerializeField] private int m_Seed = 8127;

		[Header("Appearance")]
		[ColorUsage(false, true)][SerializeField] private Color m_StrokeColor = Color.white;

		private Transform m_StrokeRoot;
		private Material m_StrokeMaterial;
		private readonly List<LineRenderer> m_StrokeRenderers = new List<LineRenderer>();
		private readonly List<float> m_InitialWidths = new List<float>();
		private double m_AdjustedTotalBeats;
		private long m_LastGeneratedBeat = long.MinValue;
		private int m_GenerationSeed;
		private bool m_UsesExternalClock;

		private void OnEnable() {
			m_AdjustedTotalBeats = 0d;
			m_LastGeneratedBeat = long.MinValue;
			m_UsesExternalClock = false;
			m_GenerationSeed = GetGenerationSeed();
			if (!Application.isPlaying)
				GenerateForBeat(0L);
		}

		private void Start() {
			m_GenerationSeed = GetGenerationSeed();
			ProcessBeatPosition(0d);
		}

		private void Update() {
			if (Application.isPlaying && !m_UsesExternalClock) {
				m_AdjustedTotalBeats += Math.Max(0d, Time.unscaledDeltaTime) * m_PreviewBpm / 60d;
				ProcessBeatPosition(m_AdjustedTotalBeats);
			}

			ApplyStrokeWidths(GetBeatPhase(m_AdjustedTotalBeats));
		}

		private void OnDisable() => ReleaseGeneratedContent();

		private void OnDestroy() => ReleaseGeneratedContent();

		private void OnValidate() {
			m_CanvasSize.x = Mathf.Max(1f, m_CanvasSize.x);
			m_CanvasSize.y = Mathf.Max(1f, m_CanvasSize.y);
			m_StrokeCount = Mathf.Clamp(m_StrokeCount, 1, 64);
			m_MinStrokeWidth = Mathf.Max(.001f, m_MinStrokeWidth);
			m_MaxStrokeWidth = Mathf.Max(m_MinStrokeWidth, m_MaxStrokeWidth);
			m_PreviewBpm = Mathf.Clamp(m_PreviewBpm, 30f, 300f);

			if (!Application.isPlaying && isActiveAndEnabled)
				GenerateForBeat(0L);
		}

		public void SetBpmClock(BeatClockFrame frame) {
			if (!frame.IsAvailable || double.IsNaN(frame.AdjustedTotalBeats) || double.IsInfinity(frame.AdjustedTotalBeats))
				return;

			m_UsesExternalClock = true;
			m_AdjustedTotalBeats = frame.AdjustedTotalBeats;
			ProcessBeatPosition(m_AdjustedTotalBeats);
			ApplyStrokeWidths(GetBeatPhase(m_AdjustedTotalBeats));
		}

		[ContextMenu("Generate Random Strokes")]
		public void Generate() {
			m_GenerationSeed = GetGenerationSeed();
			GenerateForBeat((long)Math.Floor(m_AdjustedTotalBeats));
		}

		private void ProcessBeatPosition(double beatPosition) {
			var beat = (long)Math.Floor(beatPosition + 1e-9d);
			if (beat == m_LastGeneratedBeat)
				return;

			GenerateForBeat(beat);
		}

		private void GenerateForBeat(long beat) {
			ReleaseGeneratedContent();
			m_LastGeneratedBeat = beat;
			var random = new System.Random(GetBeatSeed(beat));

			m_StrokeRoot = CreateGeneratedRoot("Random Vertical Strokes");
			m_StrokeMaterial = CreateMaterial("Random Vertical Stroke Material");
			SetMaterialColor(m_StrokeMaterial, Color.white);
			var halfWidth = m_CanvasSize.x * .5f;
			var halfHeight = m_CanvasSize.y * .5f;
			for (var index = 0; index < m_StrokeCount; index++) {
				var stroke = CreateStroke(index, NextFloat(random, -halfWidth, halfWidth), halfHeight);
				stroke.startColor = m_StrokeColor;
				stroke.endColor = m_StrokeColor;
				m_StrokeRenderers.Add(stroke);
				m_InitialWidths.Add(NextFloat(random, m_MinStrokeWidth, m_MaxStrokeWidth));
			}

			ApplyStrokeWidths(GetBeatPhase(m_AdjustedTotalBeats));
		}

		private LineRenderer CreateStroke(int index, float x, float halfHeight) {
			var strokeObject = new GameObject($"Stroke {index + 1:00}") {
				layer = gameObject.layer,
				hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave
			};
			strokeObject.transform.SetParent(m_StrokeRoot, false);

			var stroke = strokeObject.AddComponent<LineRenderer>();
			stroke.useWorldSpace = false;
			stroke.sharedMaterial = m_StrokeMaterial;
			stroke.numCornerVertices = 0;
			stroke.numCapVertices = 0;
			stroke.alignment = LineAlignment.View;
			stroke.textureMode = LineTextureMode.Stretch;
			stroke.shadowCastingMode = ShadowCastingMode.Off;
			stroke.receiveShadows = false;
			stroke.allowOcclusionWhenDynamic = false;
			stroke.sortingOrder = 1;
			stroke.positionCount = 2;
			stroke.SetPositions(new[] { new Vector3(x, -halfHeight, 0f), new Vector3(x, halfHeight, 0f) });
			return stroke;
		}

		private void ApplyStrokeWidths(float phase) {
			var easedPhase = EvaluateWidthEase(phase);
			for (var index = 0; index < m_StrokeRenderers.Count; index++)
				m_StrokeRenderers[index].widthMultiplier = Mathf.Lerp(m_InitialWidths[index], 0f, easedPhase);
		}

		private float EvaluateWidthEase(float phase) {
			phase = Mathf.Clamp01(phase);
			return m_WidthEase == null || m_WidthEase.length == 0
				? phase * (2f - phase)
				: Mathf.Clamp01(m_WidthEase.Evaluate(phase));
		}

		private static float GetBeatPhase(double totalBeats) => Mathf.Clamp01((float)(totalBeats - Math.Floor(totalBeats)));

		private int GetBeatSeed(long beat) {
			unchecked {
				var beatHash = (int)(beat ^ (beat >> 32));
				beatHash ^= beatHash >> 16;
				beatHash *= 0x45d9f3b;
				beatHash ^= beatHash >> 16;
				return m_GenerationSeed ^ beatHash;
			}
		}

		private Transform CreateGeneratedRoot(string rootName) {
			var root = new GameObject(rootName) {
				layer = gameObject.layer,
				hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave
			}.transform;
			root.SetParent(transform, false);
			return root;
		}

		private static Material CreateMaterial(string materialName) {
			var shader = Shader.Find("Universal Render Pipeline/Unlit")
				?? Shader.Find("Sprites/Default")
				?? Shader.Find("Unlit/Color");
			if (shader == null)
				throw new InvalidOperationException("An unlit shader is required for the random stroke scene.");

			var material = new Material(shader) {
				name = materialName,
				hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave
			};
			if (material.HasProperty("_Cull"))
				material.SetInt("_Cull", (int)CullMode.Off);
			return material;
		}

		private static void SetMaterialColor(Material material, Color color) {
			if (material == null)
				return;
			if (material.HasProperty("_BaseColor"))
				material.SetColor("_BaseColor", color);
			if (material.HasProperty("_Color"))
				material.SetColor("_Color", color);
		}

		private void ReleaseGeneratedContent() {
			if (m_StrokeRoot != null)
				DestroyOwnedObject(m_StrokeRoot.gameObject);
			if (m_StrokeMaterial != null)
				DestroyOwnedObject(m_StrokeMaterial);

			m_StrokeRoot = null;
			m_StrokeMaterial = null;
			m_StrokeRenderers.Clear();
			m_InitialWidths.Clear();
		}

		private int GetGenerationSeed() => Application.isPlaying && m_RandomizeOnPlay ? Environment.TickCount : m_Seed;

		private static float NextFloat(System.Random random, float minimum, float maximum)
			=> Mathf.Lerp(minimum, maximum, (float)random.NextDouble());

		private static AnimationCurve CreateWidthEase() {
			return new AnimationCurve(
				new Keyframe(0f, 0f, 0f, 2f),
				new Keyframe(1f, 1f, 0f, 0f));
		}

		private static void DestroyOwnedObject(UnityEngine.Object value) {
			if (value == null)
				return;
			if (Application.isPlaying)
				Destroy(value);
			else
				DestroyImmediate(value);
		}
	}
}
