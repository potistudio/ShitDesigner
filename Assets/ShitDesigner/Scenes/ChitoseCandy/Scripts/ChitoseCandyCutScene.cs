using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using ShitDesigner.Main;
using UnityEngine;
using UnityEngine.Rendering;
using UnityApplication = UnityEngine.Application;

namespace ShitDesigner.Scene {
	/// <summary>Generates a stylized field of candy sticks, alternates their cuts and body pushes on each beat, and animates new sticks into the field.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class ChitoseCandyCutScene : MonoBehaviour, IBpmClockReceiver, ILiveSceneParameterProvider {
		private const int CandyDivisionCount = 10;
		public const string SplitGapParameterId = "split_gap";
		public const string HorizontalImpulseParameterId = "horizontal_impulse";
		public const float MinimumSplitGap = 0f;
		public const float MaximumSplitGap = 3f;
		public const float MinimumHorizontalImpulse = 0f;
		public const float MaximumHorizontalImpulse = 10f;
		private static readonly Vector3 DefaultCandyAxis = new Vector3(0.57f, -0.37f, -0.73f);
		private static readonly Color[] DefaultCandyColors = {
			new Color(0.05f, 0.72f, 0.74f, 1f),
			new Color(0.98f, 0.13f, 0.25f, 1f),
			new Color(0.98f, 0.91f, 0.04f, 1f),
			new Color(0.03f, 0.86f, 0.58f, 1f),
			new Color(0.92f, 0.17f, 0.43f, 1f)
		};
		private static readonly Color[] FallbackCandyColors = {
			DefaultCandyColors[0], DefaultCandyColors[1], DefaultCandyColors[2]
		};

		private enum PatternType {
			Dot,
			Star,
			Ring,
			Diamond,
			Cross,
			Count
		}

		private sealed class CandyFragment {
			public readonly Transform Segment;
			public readonly Transform RearCutFace;
			public readonly Transform FrontCutFace;
			public readonly Rigidbody Body;
			public readonly float BasePosition;
			public readonly Vector3 ImpulseFactor;
			public Vector3 PushStartPosition;

			public CandyFragment(Transform segment, Transform rearCutFace, Transform frontCutFace,
				Rigidbody body, float basePosition, Vector3 impulseFactor) {
				Segment = segment;
				RearCutFace = rearCutFace;
				FrontCutFace = frontCutFace;
				Body = body;
				BasePosition = basePosition;
				ImpulseFactor = impulseFactor;
			}
		}

		private sealed class SplitGapLiveParameter : ILiveSceneParameter {
			private readonly ChitoseCandyCutScene m_Scene;

			public LiveParameterDefinition Definition => new LiveParameterDefinition(
				SplitGapParameterId, "Split Gap", MinimumSplitGap, MaximumSplitGap, m_Scene.SplitGap);

			public SplitGapLiveParameter(ChitoseCandyCutScene scene) {
				m_Scene = scene;
			}

			public bool TrySetValue(float value, out string rejectionReason) {
				if (float.IsNaN(value) || float.IsInfinity(value)) {
					rejectionReason = "The split gap must be finite.";
					return false;
				}

				m_Scene.SetSplitGap(value);
				rejectionReason = string.Empty;
				return true;
			}
		}

		private sealed class HorizontalImpulseLiveParameter : ILiveSceneParameter {
			private readonly ChitoseCandyCutScene m_Scene;

			public LiveParameterDefinition Definition => new LiveParameterDefinition(
				HorizontalImpulseParameterId, "Horizontal Impulse", MinimumHorizontalImpulse,
				MaximumHorizontalImpulse, m_Scene.HorizontalImpulse);

			public HorizontalImpulseLiveParameter(ChitoseCandyCutScene scene) {
				m_Scene = scene;
			}

			public bool TrySetValue(float value, out string rejectionReason) {
				if (float.IsNaN(value) || float.IsInfinity(value)) {
					rejectionReason = "The horizontal impulse must be finite.";
					return false;
				}

				m_Scene.SetHorizontalImpulse(value);
				rejectionReason = string.Empty;
				return true;
			}
		}

		private sealed class Candy {
			public readonly Transform EntryRoot;
			public readonly CandyFragment[] Fragments;
			public int NextCutLayer;
			public int PendingPushLayer = -1;
			public bool IsEntering;
			public Vector3 EntryStartPosition;
			public long EntryStartBeat;

			public Candy(Transform entryRoot, CandyFragment[] fragments) {
				EntryRoot = entryRoot;
				Fragments = fragments;
			}
		}

		[Header("Candy field")]
		[Min(3)][SerializeField] private int m_CandyCount = 12;
		[Min(1f)][SerializeField] private float m_CandyLength = 14f;
		[Min(0.05f)][SerializeField] private float m_CandyRadius = 0.68f;
		[SerializeField] private Vector2 m_FieldSize = new Vector2(9.5f, 5.5f);
		[Tooltip("Direction from the rear of each stick to its cut end. The negative Z component points toward the camera.")]
		[SerializeField] private Vector3 m_CandyAxis = DefaultCandyAxis;
		[SerializeField] private int m_RandomSeed = 5108;
		[ColorUsage(true, true)]
		[SerializeField] private Color[] m_CandyColors = DefaultCandyColors;

		[Header("Cut")]
		[Range(30f, 300f)][SerializeField] private float m_PreviewBpm = 138f;
		[Range(MinimumSplitGap, MaximumSplitGap)][SerializeField] private float m_SplitGap = 0.55f;
		[Range(MinimumHorizontalImpulse, MaximumHorizontalImpulse)][SerializeField] private float m_HorizontalImpulse = 0.9f;
		[Tooltip("Easing applied while the body is pushed during one beat.")]
		[SerializeField] private AnimationCurve m_PushEasing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
		[Tooltip("Easing applied while a new candy approaches from behind during one beat.")]
		[SerializeField] private AnimationCurve m_EntryEasing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		private readonly List<Candy> m_Candies = new List<Candy>();
		private readonly List<Material> m_GeneratedMaterials = new List<Material>();
		private Transform m_GeneratedRoot;
		private Mesh m_FragmentBodyMesh;
		private Mesh m_DiscMesh;
		private Mesh[] m_PatternMeshes = Array.Empty<Mesh>();
		private Material[] m_BodyMaterials = Array.Empty<Material>();
		private Material[] m_PatternMaterials = Array.Empty<Material>();
		private Material m_RimMaterial;
		private Material m_FaceMaterial;
		private Vector3 m_CandyAxisRuntime;
		private float m_FragmentLength;
		private System.Random m_RuntimeRandom;
		private bool m_PushPending;
		private long m_PushStartBeat;
		private bool m_RebuildRequested = true;
		private double m_AdjustedTotalBeats;
		private long m_LastProcessedBeat = long.MinValue;
		private bool m_UsesExternalClock;
		private bool m_WasPlaying;
		private int m_GenerationParametersHash;
		private IReadOnlyList<ILiveSceneParameter> m_LiveParameters;

		public float SplitGap => m_SplitGap;
		public float HorizontalImpulse => m_HorizontalImpulse;
		public IReadOnlyList<ILiveSceneParameter> LiveParameters => m_LiveParameters ??= new ILiveSceneParameter[] {
			new SplitGapLiveParameter(this),
			new HorizontalImpulseLiveParameter(this)
		};

		private void OnEnable() {
			ResetPlaybackState();
			m_WasPlaying = UnityApplication.isPlaying;
			Rebuild();
		}

		private void Update() {
			var isPlaying = UnityApplication.isPlaying;
			if (isPlaying && !m_WasPlaying) {
				ResetPlaybackState();
				Rebuild();
			}
			m_WasPlaying = isPlaying;

			if (m_RebuildRequested) {
				Rebuild();
				return;
			}
			if (!UnityApplication.isPlaying)
				return;

			if (!m_UsesExternalClock)
				m_AdjustedTotalBeats += Time.unscaledDeltaTime * m_PreviewBpm / 60d;
			ApplyBeatPosition(m_AdjustedTotalBeats);
		}

		public void SetBpmClock(BeatClockFrame frame) {
			if (!frame.IsAvailable || double.IsNaN(frame.AdjustedTotalBeats) || double.IsInfinity(frame.AdjustedTotalBeats))
				return;

			if (!m_UsesExternalClock) {
				m_UsesExternalClock = true;
				m_AdjustedTotalBeats = frame.AdjustedTotalBeats;
				m_LastProcessedBeat = GetBeatIndex(frame.AdjustedTotalBeats);
				return;
			}

			m_AdjustedTotalBeats = frame.AdjustedTotalBeats;
			ApplyBeatPosition(m_AdjustedTotalBeats);
		}

		private void OnDisable() {
			ReleaseGeneratedContent();
		}

		private void OnValidate() {
			m_CandyCount = Mathf.Clamp(m_CandyCount, 3, 32);
			m_CandyLength = Mathf.Max(1f, m_CandyLength);
			m_CandyRadius = Mathf.Max(0.05f, m_CandyRadius);
			m_FieldSize.x = Mathf.Max(0.1f, m_FieldSize.x);
			m_FieldSize.y = Mathf.Max(0.1f, m_FieldSize.y);
			if (m_CandyAxis.sqrMagnitude < 0.0001f)
				m_CandyAxis = DefaultCandyAxis;
			m_PreviewBpm = Mathf.Clamp(m_PreviewBpm, 30f, 300f);
			m_SplitGap = Mathf.Clamp(m_SplitGap, MinimumSplitGap, MaximumSplitGap);
			m_HorizontalImpulse = Mathf.Clamp(
				m_HorizontalImpulse, MinimumHorizontalImpulse, MaximumHorizontalImpulse);
			if (m_GeneratedRoot == null || m_GenerationParametersHash != CalculateGenerationParametersHash())
				m_RebuildRequested = true;
		}

		[ContextMenu("Rebuild Chitose Candy")]
		public void Rebuild() {
			m_RebuildRequested = false;
			m_LastProcessedBeat = long.MinValue;
			m_PushPending = false;
			m_PushStartBeat = long.MinValue;
			ReleaseGeneratedContent();

			m_GeneratedRoot = CreateGeneratedTransform(
				"Generated Chitose Candy", transform, HideFlags.HideInHierarchy | HideFlags.DontSave);

			m_CandyAxisRuntime = m_CandyAxis.sqrMagnitude < 0.0001f
				? DefaultCandyAxis.normalized
				: m_CandyAxis.normalized;
			m_GenerationParametersHash = CalculateGenerationParametersHash();

			m_FragmentLength = m_CandyLength / CandyDivisionCount;
			m_RuntimeRandom = new System.Random(m_RandomSeed);
			m_FragmentBodyMesh = BuildCylinderMesh(m_FragmentLength, m_CandyRadius, 18);
			m_DiscMesh = BuildCylinderMesh(1f, 1f, 24);
			m_PatternMeshes = BuildPatternMeshes();
			CreateMaterials();
			CreateCandies();
		}

		public void SetSplitGap(float splitGap) {
			m_SplitGap = Mathf.Clamp(splitGap, MinimumSplitGap, MaximumSplitGap);
		}

		public void SetHorizontalImpulse(float horizontalImpulse) {
			m_HorizontalImpulse = Mathf.Clamp(
				horizontalImpulse, MinimumHorizontalImpulse, MaximumHorizontalImpulse);
		}

		private void ResetPlaybackState() {
			m_AdjustedTotalBeats = 0d;
			m_LastProcessedBeat = long.MinValue;
			m_PushPending = false;
			m_PushStartBeat = long.MinValue;
			m_UsesExternalClock = false;
		}

		private void CreateMaterials() {
			var colors = ResolveCandyColors();
			m_BodyMaterials = new Material[colors.Length];
			m_PatternMaterials = new Material[colors.Length];
			for (var index = 0; index < colors.Length; index++) {
				m_BodyMaterials[index] = CreateMaterial($"Chitose Candy Body {index + 1}", colors[index]);
				m_PatternMaterials[index] = CreateMaterial($"Chitose Candy Pattern {index + 1}", colors[(index + 1) % colors.Length]);
			}

			m_RimMaterial = CreateMaterial("Chitose Candy Rim", new Color(0.72f, 0.98f, 0.82f, 1f));
			m_FaceMaterial = CreateMaterial("Chitose Candy Face", new Color(1f, 0.94f, 0.68f, 1f));
		}

		private void CreateCandies() {
			var columns = Mathf.CeilToInt(Mathf.Sqrt(m_CandyCount));
			var rows = Mathf.CeilToInt(m_CandyCount / (float)columns);
			var xStep = columns <= 1 ? 0f : m_FieldSize.x / (columns - 1f);
			var yStep = rows <= 1 ? 0f : m_FieldSize.y / (rows - 1f);

			for (var index = 0; index < m_CandyCount; index++) {
				var column = index % columns;
				var row = index / columns;
				var x = (column - (columns - 1f) * 0.5f) * xStep;
				var y = ((rows - 1f) * 0.5f - row) * yStep;
				x += NextFloat(-0.16f, 0.16f);
				y += NextFloat(-0.16f, 0.16f);
				var frontPosition = new Vector3(x, y, NextFloat(-0.18f, 0.18f));
				var axis = Quaternion.AngleAxis(NextFloat(-2.5f, 2.5f), Vector3.forward)
					* m_CandyAxisRuntime;
				var candy = CreateCandy(index, frontPosition, axis.normalized);
				m_Candies.Add(candy);
			}
		}

		private void AddNewCandy(long startBeat) {
			var frontPosition = new Vector3(
				NextFloat(-m_FieldSize.x * 0.5f, m_FieldSize.x * 0.5f),
				NextFloat(-m_FieldSize.y * 0.5f, m_FieldSize.y * 0.5f),
				NextFloat(-0.18f, 0.18f));
			var axis = (Quaternion.AngleAxis(NextFloat(-2.5f, 2.5f), Vector3.forward)
				* m_CandyAxisRuntime).normalized;
			var candy = CreateCandy(m_Candies.Count, frontPosition, axis);
			candy.EntryStartPosition = Vector3.down * (m_CandyLength * 0.5f);
			candy.EntryStartBeat = startBeat;
			candy.IsEntering = true;
			candy.EntryRoot.localPosition = candy.EntryStartPosition;
			m_Candies.Add(candy);
		}

		private Candy CreateCandy(int index, Vector3 frontPosition, Vector3 axis) {
			var candyRoot = CreateGeneratedTransform($"Candy {index + 1:00}", m_GeneratedRoot);
			candyRoot.localPosition = frontPosition - axis * (m_CandyLength * 0.5f);
			candyRoot.localRotation = Quaternion.FromToRotation(Vector3.up, axis);

			var entryRoot = CreateGeneratedTransform("Candy Entry", candyRoot);

			var cutFaceScale = new Vector3(m_CandyRadius * 0.76f, 0.028f, m_CandyRadius * 0.76f);
			var fragments = new CandyFragment[CandyDivisionCount];
			for (var fragmentIndex = 0; fragmentIndex < fragments.Length; fragmentIndex++) {
				var fragment = CreateGeneratedTransform($"Cut Fragment {fragmentIndex + 1:00}", entryRoot);
				var basePosition = m_CandyLength * 0.5f - m_FragmentLength * (fragmentIndex + 0.5f);
				fragment.localPosition = Vector3.up * basePosition;
				CreateMeshObject("Fragment Candy Body", fragment, m_FragmentBodyMesh,
					m_BodyMaterials[index % m_BodyMaterials.Length], Vector3.one);
				var body = AddPhysicsBody(fragment, m_FragmentLength);

				var rearCutFace = CreateMeshObject("Fragment Rear Cut Face", fragment, m_DiscMesh,
					m_FaceMaterial, cutFaceScale);
				rearCutFace.localPosition = Vector3.up * (-m_FragmentLength * 0.5f - 0.018f);
				rearCutFace.gameObject.SetActive(false);
				var frontCutFace = CreateMeshObject("Fragment Front Cut Face", fragment, m_DiscMesh,
					m_FaceMaterial, cutFaceScale);
				frontCutFace.localPosition = Vector3.up * (m_FragmentLength * 0.5f + 0.018f);
				frontCutFace.gameObject.SetActive(false);

				if (fragmentIndex == 0)
					CreateOriginalCandyEnd(fragment, index);

				fragments[fragmentIndex] = new CandyFragment(fragment, rearCutFace, frontCutFace,
					body, basePosition, CreateImpactImpulseFactor());
			}

			return new Candy(entryRoot, fragments);
		}

		private void CreateOriginalCandyEnd(Transform frontFragment, int index) {
			var originalEnd = CreateGeneratedTransform("Original Candy End", frontFragment);
			originalEnd.localPosition = Vector3.up * (m_FragmentLength * 0.5f + m_CandyRadius * 0.02f);
			CreateMeshObject("Pale Rim", originalEnd, m_DiscMesh, m_RimMaterial,
				new Vector3(m_CandyRadius * 0.94f, 0.055f, m_CandyRadius * 0.94f));
			var face = CreateMeshObject("Original Cut Face", originalEnd, m_DiscMesh, m_FaceMaterial,
				new Vector3(m_CandyRadius * 0.76f, 0.028f, m_CandyRadius * 0.76f));
			face.localPosition = Vector3.up * 0.04f;

			var patternType = (PatternType)m_RuntimeRandom.Next((int)PatternType.Count);
			var pattern = CreateMeshObject("Candy Pattern", originalEnd, m_PatternMeshes[(int)patternType],
				m_PatternMaterials[(index + 1) % m_PatternMaterials.Length],
				Vector3.one * (m_CandyRadius * 0.34f));
			pattern.localPosition = Vector3.up * 0.064f;
		}

		private Rigidbody AddPhysicsBody(Transform segment, float length) {
			var rigidbody = segment.gameObject.AddComponent<Rigidbody>();
			rigidbody.isKinematic = true;
			rigidbody.useGravity = false;
			rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
			rigidbody.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

			var collider = segment.gameObject.AddComponent<CapsuleCollider>();
			collider.direction = 1;
			collider.radius = m_CandyRadius * 0.94f;
			collider.height = Mathf.Max(length, collider.radius * 2f);
			return rigidbody;
		}

		private Vector3 CreateImpactImpulseFactor() {
			var magnitude = NextFloat(0.55f, 1f);
			var direction = m_RuntimeRandom.Next(2) == 0 ? -1f : 1f;
			return new Vector3(direction, 1f, 0f).normalized * magnitude;
		}

		private void ApplyBeatPosition(double beatPosition) {
			if (double.IsNaN(beatPosition) || double.IsInfinity(beatPosition))
				return;

			var beatIndex = GetBeatIndex(beatPosition);
			if (m_LastProcessedBeat == long.MinValue) {
				m_LastProcessedBeat = beatIndex;
			}
			else {
				while (m_LastProcessedBeat < beatIndex) {
					var nextBeat = m_LastProcessedBeat + 1L;
					UpdateCandyEntries(nextBeat);
					UpdatePushAnimation(nextBeat);
					ProcessNextBeat(nextBeat);
					m_LastProcessedBeat = nextBeat;
				}
			}

			UpdateCandyEntries(beatPosition);
			UpdatePushAnimation(beatPosition);
		}

		private static long GetBeatIndex(double beatPosition) => (long)Math.Floor(beatPosition + 1e-9d);

		private void ProcessNextBeat(long beatIndex) {
			if (m_PushPending) {
				m_PushPending = false;
				m_PushStartBeat = beatIndex;
				CapturePushStartPositions();
				PushCutLayers(0f);
				return;
			}

			if (!CutNextLayers() || m_RuntimeRandom.Next(2) == 0)
				AddNewCandy(beatIndex);
			m_PushPending = true;
		}

		private void UpdateCandyEntries(double beatPosition) {
			var moved = false;
			for (var index = 0; index < m_Candies.Count; index++) {
				var candy = m_Candies[index];
				if (!candy.IsEntering)
					continue;

				var progress = Mathf.Clamp01((float)(beatPosition - candy.EntryStartBeat));
				var easedProgress = EvaluateEasing(m_EntryEasing, progress);
				candy.EntryRoot.localPosition = Vector3.Lerp(candy.EntryStartPosition, Vector3.zero, easedProgress);
				moved = true;
				if (progress >= 1f) {
					candy.EntryRoot.localPosition = Vector3.zero;
					candy.IsEntering = false;
				}
			}
			if (moved)
				Physics.SyncTransforms();
		}

		private void UpdatePushAnimation(double beatPosition) {
			if (m_PushStartBeat == long.MinValue)
				return;

			var progress = Mathf.Clamp01((float)(beatPosition - m_PushStartBeat));
			var easedProgress = EvaluateEasing(m_PushEasing, progress);
			PushCutLayers(easedProgress);
			if (progress >= 1f) {
				m_PushStartBeat = long.MinValue;
				for (var index = 0; index < m_Candies.Count; index++)
					m_Candies[index].PendingPushLayer = -1;
			}
		}

		private static float EvaluateEasing(AnimationCurve easing, float progress) {
			if (easing != null && easing.length > 0)
				return Mathf.Clamp01(easing.Evaluate(progress));
			return progress;
		}

		private bool CutNextLayers() {
			var cutOccurred = false;
			for (var index = 0; index < m_Candies.Count; index++) {
				var candy = m_Candies[index];
				if (candy.NextCutLayer >= candy.Fragments.Length) {
					candy.PendingPushLayer = -1;
					continue;
				}

				var layerIndex = candy.NextCutLayer++;
				candy.PendingPushLayer = layerIndex;
				cutOccurred = true;
				var fragment = candy.Fragments[layerIndex];
				fragment.RearCutFace.gameObject.SetActive(true);
				if (layerIndex + 1 < candy.Fragments.Length)
					candy.Fragments[layerIndex + 1].FrontCutFace.gameObject.SetActive(true);
			}
			if (!UnityApplication.isPlaying)
				return cutOccurred;

			Physics.SyncTransforms();
			for (var index = 0; index < m_Candies.Count; index++) {
				var candy = m_Candies[index];
				var layerIndex = candy.PendingPushLayer;
				if (layerIndex < 0)
					continue;
				var fragment = candy.Fragments[layerIndex];
				ActivatePhysics(fragment.Body, fragment.ImpulseFactor * m_HorizontalImpulse);
			}
			return cutOccurred;
		}

		private void PushCutLayers(float progress) {
			var pushDistance = m_FragmentLength + m_SplitGap;
			for (var index = 0; index < m_Candies.Count; index++) {
				var candy = m_Candies[index];
				var layerIndex = candy.PendingPushLayer;
				if (layerIndex < 0)
					continue;

				for (var fragmentIndex = layerIndex + 1; fragmentIndex < candy.Fragments.Length; fragmentIndex++) {
					var fragment = candy.Fragments[fragmentIndex];
					var targetPosition = Vector3.up * (fragment.BasePosition + pushDistance * (layerIndex + 1));
					fragment.Segment.localPosition = Vector3.Lerp(
						fragment.PushStartPosition, targetPosition, progress);
				}
			}
			if (UnityApplication.isPlaying)
				Physics.SyncTransforms();
		}

		private void CapturePushStartPositions() {
			for (var index = 0; index < m_Candies.Count; index++) {
				var candy = m_Candies[index];
				var layerIndex = candy.PendingPushLayer;
				if (layerIndex < 0)
					continue;

				for (var fragmentIndex = layerIndex + 1; fragmentIndex < candy.Fragments.Length; fragmentIndex++)
					candy.Fragments[fragmentIndex].PushStartPosition = candy.Fragments[fragmentIndex].Segment.localPosition;
			}
		}

		private static void ActivatePhysics(Rigidbody rigidbody, Vector3 impactImpulse) {
			if (rigidbody == null)
				return;

			rigidbody.isKinematic = false;
			rigidbody.useGravity = true;
			rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
			rigidbody.AddForce(impactImpulse, ForceMode.Impulse);
		}

		private Color[] ResolveCandyColors() {
			if (m_CandyColors != null && m_CandyColors.Length > 0) {
				var validColors = new List<Color>(m_CandyColors.Length);
				for (var index = 0; index < m_CandyColors.Length; index++)
					if (m_CandyColors[index].a > 0f)
						validColors.Add(m_CandyColors[index]);
				if (validColors.Count > 0)
					return validColors.ToArray();
			}

			return FallbackCandyColors;
		}

		private int CalculateGenerationParametersHash() {
			unchecked {
				var hash = 17;
				hash = hash * 31 + m_CandyCount;
				hash = hash * 31 + m_CandyLength.GetHashCode();
				hash = hash * 31 + m_CandyRadius.GetHashCode();
				hash = hash * 31 + m_FieldSize.GetHashCode();
				hash = hash * 31 + m_CandyAxis.GetHashCode();
				hash = hash * 31 + m_RandomSeed;
				hash = hash * 31 + (m_CandyColors?.Length ?? 0);
				if (m_CandyColors != null)
					for (var index = 0; index < m_CandyColors.Length; index++)
						hash = hash * 31 + m_CandyColors[index].GetHashCode();
				return hash;
			}
		}

		private Mesh[] BuildPatternMeshes() {
			var meshes = new Mesh[(int)PatternType.Count];
			meshes[(int)PatternType.Dot] = BuildPolygonMesh("Candy Pattern Dot", CreateCirclePoints(20));
			meshes[(int)PatternType.Star] = BuildPolygonMesh("Candy Pattern Star", CreateStarPoints());
			meshes[(int)PatternType.Ring] = BuildRingMesh("Candy Pattern Ring", 20, 0.95f, 0.55f);
			meshes[(int)PatternType.Diamond] = BuildPolygonMesh("Candy Pattern Diamond", new[] {
				new Vector2(0f, 1f), new Vector2(0.82f, 0f), new Vector2(0f, -1f), new Vector2(-0.82f, 0f)
			});
			meshes[(int)PatternType.Cross] = BuildPolygonMesh("Candy Pattern Cross", new[] {
				new Vector2(-0.28f, 1f), new Vector2(0.28f, 1f), new Vector2(0.28f, 0.28f),
				new Vector2(1f, 0.28f), new Vector2(1f, -0.28f), new Vector2(0.28f, -0.28f),
				new Vector2(0.28f, -1f), new Vector2(-0.28f, -1f), new Vector2(-0.28f, -0.28f),
				new Vector2(-1f, -0.28f), new Vector2(-1f, 0.28f), new Vector2(-0.28f, 0.28f)
			});
			return meshes;
		}

		private static Vector2[] CreateCirclePoints(int segments) {
			var points = new Vector2[segments];
			for (var index = 0; index < segments; index++) {
				var angle = -Mathf.PI * 2f * index / segments;
				points[index] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
			}
			return points;
		}

		private static Vector2[] CreateStarPoints() {
			var points = new Vector2[10];
			for (var index = 0; index < points.Length; index++) {
				var angle = -Mathf.PI * 2f * index / points.Length;
				var radius = index % 2 == 0 ? 1f : 0.42f;
				points[index] = new Vector2(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius);
			}
			return points;
		}

		private static Mesh BuildPolygonMesh(string meshName, Vector2[] points) {
			var vertices = new Vector3[points.Length];
			for (var index = 0; index < points.Length; index++)
				vertices[index] = new Vector3(points[index].x, 0f, points[index].y);

			var triangles = new int[(points.Length - 2) * 3];
			var triangleIndex = 0;
			for (var index = 1; index < points.Length - 1; index++) {
				triangles[triangleIndex++] = 0;
				triangles[triangleIndex++] = index;
				triangles[triangleIndex++] = index + 1;
			}

			var mesh = new Mesh { name = meshName, hideFlags = HideFlags.HideAndDontSave };
			mesh.vertices = vertices;
			mesh.triangles = triangles;
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		private static Mesh BuildRingMesh(string meshName, int segments, float outerRadius, float innerRadius) {
			var vertices = new Vector3[segments * 2];
			for (var index = 0; index < segments; index++) {
				var angle = -Mathf.PI * 2f * index / segments;
				var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
				vertices[index * 2] = new Vector3(direction.x * outerRadius, 0f, direction.y * outerRadius);
				vertices[index * 2 + 1] = new Vector3(direction.x * innerRadius, 0f, direction.y * innerRadius);
			}

			var triangles = new int[segments * 6];
			var triangleIndex = 0;
			for (var index = 0; index < segments; index++) {
				var next = (index + 1) % segments;
				triangles[triangleIndex++] = index * 2;
				triangles[triangleIndex++] = next * 2;
				triangles[triangleIndex++] = index * 2 + 1;
				triangles[triangleIndex++] = index * 2 + 1;
				triangles[triangleIndex++] = next * 2;
				triangles[triangleIndex++] = next * 2 + 1;
			}

			var mesh = new Mesh { name = meshName, hideFlags = HideFlags.HideAndDontSave };
			mesh.vertices = vertices;
			mesh.triangles = triangles;
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		private static Mesh BuildCylinderMesh(float length, float radius, int segments) {
			var halfLength = length * 0.5f;
			var vertices = new Vector3[segments * 2 + 2];
			for (var segment = 0; segment < segments; segment++) {
				var angle = Mathf.PI * 2f * segment / segments;
				var x = Mathf.Cos(angle) * radius;
				var z = Mathf.Sin(angle) * radius;
				vertices[segment] = new Vector3(x, -halfLength, z);
				vertices[segments + segment] = new Vector3(x, halfLength, z);
			}
			var bottomCenter = segments * 2;
			var topCenter = bottomCenter + 1;
			vertices[bottomCenter] = new Vector3(0f, -halfLength, 0f);
			vertices[topCenter] = new Vector3(0f, halfLength, 0f);

			var triangles = new int[segments * 12];
			var triangleIndex = 0;
			for (var segment = 0; segment < segments; segment++) {
				var next = (segment + 1) % segments;
				AddTriangle(triangles, ref triangleIndex, segment, segments + segment, next);
				AddTriangle(triangles, ref triangleIndex, next, segments + segment, segments + next);
				AddTriangle(triangles, ref triangleIndex, bottomCenter, next, segment);
				AddTriangle(triangles, ref triangleIndex, topCenter, segments + segment, segments + next);
			}

			var mesh = new Mesh { name = "Chitose Candy Disc", hideFlags = HideFlags.HideAndDontSave };
			mesh.vertices = vertices;
			mesh.triangles = triangles;
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			return mesh;
		}

		private Transform CreateMeshObject(string objectName, Transform parent, Mesh mesh, Material material, Vector3 localScale) {
			var item = CreateGeneratedTransform(objectName, parent);
			item.localScale = localScale;
			var meshFilter = item.gameObject.AddComponent<MeshFilter>();
			meshFilter.sharedMesh = mesh;
			var meshRenderer = item.gameObject.AddComponent<MeshRenderer>();
			meshRenderer.sharedMaterial = material;
			meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
			meshRenderer.receiveShadows = false;
			return item;
		}

		private Transform CreateGeneratedTransform(
			string objectName, Transform parent, HideFlags hideFlags = HideFlags.DontSave) {
			var item = new GameObject(objectName).transform;
			item.gameObject.layer = gameObject.layer;
			item.gameObject.hideFlags = hideFlags;
			item.SetParent(parent, false);
			return item;
		}

		private Material CreateMaterial(string materialName, Color color) {
			var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
			if (shader == null)
				return null;

			var material = new Material(shader) {
				name = materialName,
				hideFlags = HideFlags.HideAndDontSave,
				enableInstancing = true
			};
			if (material.HasProperty("_BaseColor"))
				material.SetColor("_BaseColor", color);
			if (material.HasProperty("_Color"))
				material.SetColor("_Color", color);
			if (material.HasProperty("_Cull"))
				material.SetInt("_Cull", (int)CullMode.Off);
			m_GeneratedMaterials.Add(material);
			return material;
		}

		private void ReleaseGeneratedContent() {
			if (m_GeneratedRoot != null)
				DestroyOwnedObject(m_GeneratedRoot.gameObject);
			m_GeneratedRoot = null;
			m_Candies.Clear();

			DestroyOwnedObject(m_FragmentBodyMesh);
			DestroyOwnedObject(m_DiscMesh);
			m_FragmentBodyMesh = null;
			m_DiscMesh = null;
			for (var index = 0; index < m_PatternMeshes.Length; index++)
				DestroyOwnedObject(m_PatternMeshes[index]);
			m_PatternMeshes = Array.Empty<Mesh>();

			for (var index = 0; index < m_GeneratedMaterials.Count; index++)
				DestroyOwnedObject(m_GeneratedMaterials[index]);
			m_GeneratedMaterials.Clear();
			m_BodyMaterials = Array.Empty<Material>();
			m_PatternMaterials = Array.Empty<Material>();
			m_RimMaterial = null;
			m_FaceMaterial = null;
		}

		private float NextFloat(float minimum, float maximum) {
			return Mathf.Lerp(minimum, maximum, (float)m_RuntimeRandom.NextDouble());
		}

		private static void AddTriangle(int[] triangles, ref int index, int first, int second, int third) {
			triangles[index++] = first;
			triangles[index++] = second;
			triangles[index++] = third;
		}

		private static void DestroyOwnedObject(UnityEngine.Object value) {
			if (value == null)
				return;
			if (UnityApplication.isPlaying)
				Destroy(value);
			else
				DestroyImmediate(value);
		}
	}
}
