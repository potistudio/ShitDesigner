using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Scene {
	/// <summary>Draws random 2D strokes and fills a random subset of the regions they enclose.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class RandomStrokeIntersectionScene : MonoBehaviour, IBpmClockReceiver {
		[Header("Canvas")]
		[Min(1f)][SerializeField] private Vector2 m_CanvasSize = new Vector2(10f, 6f);

		[Header("Strokes")]
		[Range(2, 64)][SerializeField] private int m_StrokeCount = 8;
		[Min(0.005f)][SerializeField] private float m_StrokeWidth = 0.08f;

		[Header("Regions")]
		[Range(0, 128)][SerializeField] private int m_FilledRegionCount = 8;

		[Header("Beat")]
		[Range(30f, 300f)][SerializeField] private float m_PreviewBpm = 120f;

		[Header("Motion")]
		[Min(0f)][SerializeField] private float m_ContinuousRotationDegreesPerSecond = 2f;
		[Min(0f)][SerializeField] private float m_BeatRotationDegrees = 8f;

		[Header("Randomness")]
		[SerializeField] private bool m_RandomizeOnPlay = true;
		[SerializeField] private int m_Seed = 8127;

		[Header("Appearance")]
		[ColorUsage(false, true)][SerializeField] private Color m_StrokeColorA = new Color(0.2f, 0.85f, 1f, 1f);
		[ColorUsage(false, true)][SerializeField] private Color m_StrokeColorB = new Color(0.75f, 0.35f, 1f, 1f);
		[ColorUsage(false, true)][SerializeField] private Color m_RegionFillColor = new Color(1f, 0.35f, 0.1f, 1f);

		private Transform m_StrokeRoot;
		private Transform m_RegionRoot;
		private Material m_StrokeMaterial;
		private Material m_RegionMaterial;
		private Mesh m_RegionMesh;
		private GameObject m_RegionObject;
		private readonly List<LineRenderer> m_StrokeRenderers = new List<LineRenderer>();
		private List<PolygonFace> m_CurrentRegions;
		private List<StrokePath> m_TransitionTargetPaths;
		private List<PolygonFace> m_TransitionTargetRegions;
		private readonly List<Vector3> m_RegionVertices = new List<Vector3>();
		private readonly List<int> m_RegionTriangles = new List<int>();
		private double m_TransitionStartBeat;
		private double m_AdjustedTotalBeats;
		private double m_LastRotationBeat = double.NaN;
		private long m_LastGeneratedBeat = long.MinValue;
		private int m_GenerationSeed;
		private float m_ContinuousRotationDegrees;
		private bool m_TransitionRegenerated;
		private bool m_UsesExternalClock;

		private void OnEnable() {
			m_AdjustedTotalBeats = 0d;
			m_LastRotationBeat = double.NaN;
			m_LastGeneratedBeat = long.MinValue;
			m_ContinuousRotationDegrees = 0f;
			m_UsesExternalClock = false;
			m_GenerationSeed = GetGenerationSeed();
			if (!Application.isPlaying)
				GenerateWithSeed(m_GenerationSeed);
		}

		private void Start() {
			m_GenerationSeed = GetGenerationSeed();
			m_LastGeneratedBeat = 0L;
			GenerateWithSeed(GetBeatSeed(0L));
		}

		private void Update() {
			if (Application.isPlaying && !m_UsesExternalClock) {
				m_AdjustedTotalBeats += Time.unscaledDeltaTime * m_PreviewBpm / 60d;
				ProcessBeatPosition(m_AdjustedTotalBeats);
			}

			AdvanceContinuousRotation(m_AdjustedTotalBeats, m_PreviewBpm);
			ApplyTransition(m_AdjustedTotalBeats);
			ApplyGeneratedRotation();
		}

		private void OnDisable() {
			ReleaseGeneratedContent();
		}

		private void OnDestroy() {
			ReleaseGeneratedContent();
		}

		private void OnValidate() {
			m_CanvasSize.x = Mathf.Max(1f, m_CanvasSize.x);
			m_CanvasSize.y = Mathf.Max(1f, m_CanvasSize.y);
			m_StrokeCount = Mathf.Clamp(m_StrokeCount, 2, 64);
			m_StrokeWidth = Mathf.Max(0.005f, m_StrokeWidth);
			m_FilledRegionCount = Mathf.Clamp(m_FilledRegionCount, 0, 128);
			m_PreviewBpm = Mathf.Clamp(m_PreviewBpm, 30f, 300f);
			m_ContinuousRotationDegreesPerSecond = Mathf.Max(0f, m_ContinuousRotationDegreesPerSecond);
			m_BeatRotationDegrees = Mathf.Max(0f, m_BeatRotationDegrees);

			if (!Application.isPlaying && isActiveAndEnabled)
				Generate();
		}

		public void SetBpmClock(BeatClockFrame frame) {
			if (!frame.IsAvailable || double.IsNaN(frame.AdjustedTotalBeats) || double.IsInfinity(frame.AdjustedTotalBeats))
				return;

			m_UsesExternalClock = true;
			m_AdjustedTotalBeats = frame.AdjustedTotalBeats;
			ProcessBeatPosition(m_AdjustedTotalBeats);
			AdvanceContinuousRotation(m_AdjustedTotalBeats, frame.Bpm);
			ApplyTransition(m_AdjustedTotalBeats);
			ApplyGeneratedRotation();
		}

		[ContextMenu("Generate Random Strokes")]
		public void Generate() {
			m_GenerationSeed = GetGenerationSeed();
			GenerateWithSeed(m_GenerationSeed);
		}

		private void ProcessBeatPosition(double beatPosition) {
			if (double.IsNaN(beatPosition) || double.IsInfinity(beatPosition))
				return;

			var beatIndex = (long)Math.Floor(beatPosition + 1e-9d);
			if (beatIndex == m_LastGeneratedBeat)
				return;

			var isInitialGeneration = m_LastGeneratedBeat == long.MinValue || m_StrokeRenderers.Count == 0;
			m_LastGeneratedBeat = beatIndex;
			var seed = GetBeatSeed(beatIndex);
			if (isInitialGeneration)
				GenerateWithSeed(seed);
			else
				BeginTransition(beatIndex, seed);
		}

		private int GetBeatSeed(long beat) {
			unchecked {
				var beatHash = (int)(beat ^ (beat >> 32));
				beatHash ^= beatHash >> 16;
				beatHash *= 0x45d9f3b;
				beatHash ^= beatHash >> 16;
				beatHash *= 0x45d9f3b;
				beatHash ^= beatHash >> 16;
				return m_GenerationSeed ^ beatHash;
			}
		}

		private void GenerateWithSeed(int seed) {
			ReleaseGeneratedContent();

			var layout = BuildLayout(seed);
			m_TransitionTargetPaths = null;
			m_TransitionTargetRegions = null;

			m_StrokeRoot = CreateGeneratedRoot("Random Strokes");
			m_RegionRoot = CreateGeneratedRoot("Filled Regions");
			m_StrokeMaterial = CreateMaterial("Random Stroke Material");
			m_RegionMaterial = CreateMaterial("Random Region Material");
			SetMaterialColor(m_StrokeMaterial, Color.white);
			SetMaterialColor(m_RegionMaterial, m_RegionFillColor);

			CreateStrokeRenderers(layout.Paths);

			m_CurrentRegions = SelectFilledRegions(layout.Regions);
			if (m_CurrentRegions.Count > 0)
				CreateRegionRenderer(m_CurrentRegions, m_CurrentRegions.Count);
			ApplyGeneratedRotation();
		}

		private StrokeLayout BuildLayout(int seed) {
			List<StrokePath> bestPaths = null;
			var bestRegions = new List<PolygonFace>();
			for (var attempt = 0; attempt < 32; attempt++) {
				var pathRandom = new System.Random(unchecked(seed + attempt * 7919));
				var paths = BuildPaths(pathRandom);
				var regions = FindRegions(paths);
				if (bestPaths == null || regions.Count > bestRegions.Count) {
					bestPaths = paths;
					bestRegions = regions;
				}
				if (regions.Count > 0)
					break;
			}

			Shuffle(bestRegions, new System.Random(unchecked(seed ^ 0x5F3759DF)));
			return new StrokeLayout(bestPaths, bestRegions);
		}

		private void BeginTransition(long beat, int seed) {
			var layout = BuildLayout(seed);
			if (layout.Paths.Count != m_StrokeRenderers.Count) {
				GenerateWithSeed(seed);
				return;
			}

			m_TransitionTargetPaths = layout.Paths;
			m_TransitionTargetRegions = SelectFilledRegions(layout.Regions);
			m_TransitionStartBeat = beat;
			m_TransitionRegenerated = false;
		}

		private void ApplyTransition(double beatPosition) {
			if (m_TransitionTargetPaths == null || m_TransitionTargetRegions == null)
				return;

			var phase = Mathf.Clamp01((float)(beatPosition - m_TransitionStartBeat));
			if (phase >= 0.5f && !m_TransitionRegenerated) {
				for (var strokeIndex = 0; strokeIndex < m_StrokeRenderers.Count; strokeIndex++)
					m_StrokeRenderers[strokeIndex].SetPositions(ToVector3Array(m_TransitionTargetPaths[strokeIndex].Points));

				m_CurrentRegions = m_TransitionTargetRegions;
				UpdateRegionRenderer(m_CurrentRegions);
				m_TransitionRegenerated = true;
			}

			if (phase < 1f)
				return;

			m_TransitionTargetPaths = null;
			m_TransitionTargetRegions = null;
			m_TransitionRegenerated = false;
		}

		private void AdvanceContinuousRotation(double beatPosition, float bpm) {
			if (double.IsNaN(beatPosition) || double.IsInfinity(beatPosition) || float.IsNaN(bpm) || float.IsInfinity(bpm) || bpm <= 0f)
				return;
			if (!double.IsNaN(m_LastRotationBeat)) {
				var beatDelta = Math.Max(0d, beatPosition - m_LastRotationBeat);
				m_ContinuousRotationDegrees += (float)(beatDelta * 60d / bpm * m_ContinuousRotationDegreesPerSecond);
			}
			m_LastRotationBeat = beatPosition;
		}

		private void ApplyGeneratedRotation() {
			var rotationDegrees = m_ContinuousRotationDegrees;
			if (m_TransitionTargetPaths != null) {
				var phase = Mathf.Clamp01((float)(m_AdjustedTotalBeats - m_TransitionStartBeat));
				rotationDegrees += GetBeatRotationDegrees(phase);
			}

			var rotation = Quaternion.Euler(0f, 0f, rotationDegrees);
			if (m_StrokeRoot != null)
				m_StrokeRoot.localRotation = rotation;
			if (m_RegionRoot != null)
				m_RegionRoot.localRotation = rotation;
		}

		private float GetBeatRotationDegrees(float phase) {
			if (phase < 0.5f) {
				var upPhase = phase * 2f;
				return m_BeatRotationDegrees * upPhase * upPhase * upPhase;
			}

			var downPhase = (phase - 0.5f) * 2f;
			var easedDownPhase = 1f - Mathf.Pow(1f - downPhase, 3f);
			return m_BeatRotationDegrees * (1f - easedDownPhase);
		}

		private List<PolygonFace> SelectFilledRegions(List<PolygonFace> regions) {
			var fillCount = Mathf.Min(m_FilledRegionCount, regions == null ? 0 : regions.Count);
			var selectedRegions = new List<PolygonFace>(fillCount);
			for (var index = 0; index < fillCount; index++)
				selectedRegions.Add(regions[index]);
			return selectedRegions;
		}

		private List<StrokePath> BuildPaths(System.Random random) {
			var paths = new List<StrokePath>(m_StrokeCount);
			var halfWidth = m_CanvasSize.x * 0.5f;
			var halfHeight = m_CanvasSize.y * 0.5f;

			for (var strokeIndex = 0; strokeIndex < m_StrokeCount; strokeIndex++) {
				var angle = NextFloat(random, 0f, Mathf.PI);
				var direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
				var position = new Vector2(
					NextFloat(random, -halfWidth, halfWidth),
					NextFloat(random, -halfHeight, halfHeight));
				if (!TryClipLineToCanvas(position, direction, halfWidth, halfHeight, out var start, out var end))
					continue;

				var colorProgress = m_StrokeCount <= 1 ? 0f : strokeIndex / (float)(m_StrokeCount - 1);
				var color = Color.Lerp(m_StrokeColorA, m_StrokeColorB, colorProgress);
				paths.Add(new StrokePath(new[] { start, end }, color));
			}

			return paths;
		}

		private static bool TryClipLineToCanvas(
			Vector2 position, Vector2 direction, float halfWidth, float halfHeight,
			out Vector2 start, out Vector2 end) {
			var minimumProgress = float.NegativeInfinity;
			var maximumProgress = float.PositiveInfinity;
			if (Mathf.Abs(direction.x) > 0.00001f) {
				var firstProgress = (-halfWidth - position.x) / direction.x;
				var secondProgress = (halfWidth - position.x) / direction.x;
				minimumProgress = Mathf.Max(minimumProgress, Mathf.Min(firstProgress, secondProgress));
				maximumProgress = Mathf.Min(maximumProgress, Mathf.Max(firstProgress, secondProgress));
			} else if (Mathf.Abs(position.x) > halfWidth) {
				start = default;
				end = default;
				return false;
			}

			if (Mathf.Abs(direction.y) > 0.00001f) {
				var firstProgress = (-halfHeight - position.y) / direction.y;
				var secondProgress = (halfHeight - position.y) / direction.y;
				minimumProgress = Mathf.Max(minimumProgress, Mathf.Min(firstProgress, secondProgress));
				maximumProgress = Mathf.Min(maximumProgress, Mathf.Max(firstProgress, secondProgress));
			} else if (Mathf.Abs(position.y) > halfHeight) {
				start = default;
				end = default;
				return false;
			}

			if (minimumProgress >= maximumProgress) {
				start = default;
				end = default;
				return false;
			}

			start = position + direction * minimumProgress;
			end = position + direction * maximumProgress;
			return true;
		}

		private List<PolygonFace> FindRegions(List<StrokePath> paths) {
			var nodes = new List<Vector2>();
			var adjacency = new List<List<int>>();
			var segmentSplits = new List<SegmentSplit>[paths.Count][];
			var mergeDistance = Mathf.Max(0.001f, m_StrokeWidth * 0.1f);

			for (var pathIndex = 0; pathIndex < paths.Count; pathIndex++) {
				var points = paths[pathIndex].Points;
				segmentSplits[pathIndex] = new List<SegmentSplit>[points.Length - 1];
				for (var segmentIndex = 0; segmentIndex < points.Length - 1; segmentIndex++) {
					var splits = new List<SegmentSplit>(2) {
						new SegmentSplit(0f, GetOrCreateNode(nodes, adjacency, points[segmentIndex], mergeDistance)),
						new SegmentSplit(1f, GetOrCreateNode(nodes, adjacency, points[segmentIndex + 1], mergeDistance))
					};
					segmentSplits[pathIndex][segmentIndex] = splits;
				}
			}

			for (var firstPath = 0; firstPath < paths.Count - 1; firstPath++) {
				for (var secondPath = firstPath + 1; secondPath < paths.Count; secondPath++) {
					var firstPoints = paths[firstPath].Points;
					var secondPoints = paths[secondPath].Points;
					for (var firstSegment = 0; firstSegment < firstPoints.Length - 1; firstSegment++) {
						for (var secondSegment = 0; secondSegment < secondPoints.Length - 1; secondSegment++) {
							if (!TryGetSegmentIntersection(
								firstPoints[firstSegment], firstPoints[firstSegment + 1],
								secondPoints[secondSegment], secondPoints[secondSegment + 1],
								out var point, out var firstProgress, out var secondProgress))
								continue;

							var nodeIndex = GetOrCreateNode(nodes, adjacency, point, mergeDistance);
							segmentSplits[firstPath][firstSegment].Add(new SegmentSplit(firstProgress, nodeIndex));
							segmentSplits[secondPath][secondSegment].Add(new SegmentSplit(secondProgress, nodeIndex));
						}
					}
				}
			}

			for (var pathIndex = 0; pathIndex < segmentSplits.Length; pathIndex++) {
				for (var segmentIndex = 0; segmentIndex < segmentSplits[pathIndex].Length; segmentIndex++) {
					var splits = segmentSplits[pathIndex][segmentIndex];
					splits.Sort((first, second) => first.Progress.CompareTo(second.Progress));
					for (var splitIndex = 1; splitIndex < splits.Count; splitIndex++)
						AddGraphEdge(adjacency, splits[splitIndex - 1].NodeIndex, splits[splitIndex].NodeIndex);
				}
			}

			for (var nodeIndex = 0; nodeIndex < adjacency.Count; nodeIndex++) {
				var node = nodes[nodeIndex];
				adjacency[nodeIndex].Sort((first, second) => {
					var firstDirection = nodes[first] - node;
					var secondDirection = nodes[second] - node;
					return Mathf.Atan2(firstDirection.y, firstDirection.x)
						.CompareTo(Mathf.Atan2(secondDirection.y, secondDirection.x));
				});
			}

			var directedEdgeCount = 0;
			for (var nodeIndex = 0; nodeIndex < adjacency.Count; nodeIndex++)
				directedEdgeCount += adjacency[nodeIndex].Count;

			var regions = new List<PolygonFace>();
			var visitedEdges = new HashSet<ulong>();
			for (var fromNode = 0; fromNode < adjacency.Count; fromNode++) {
				for (var neighborIndex = 0; neighborIndex < adjacency[fromNode].Count; neighborIndex++) {
					var toNode = adjacency[fromNode][neighborIndex];
					if (visitedEdges.Contains(DirectedEdgeKey(fromNode, toNode)))
						continue;

					var polygonNodeIndices = new List<int>();
					var currentFrom = fromNode;
					var currentTo = toNode;
					var closed = false;
					for (var step = 0; step <= directedEdgeCount; step++) {
						if (step > 0 && currentFrom == fromNode && currentTo == toNode) {
							closed = true;
							break;
						}

						if (!visitedEdges.Add(DirectedEdgeKey(currentFrom, currentTo)))
							break;
						polygonNodeIndices.Add(currentFrom);

						var neighbors = adjacency[currentTo];
						var reverseIndex = neighbors.IndexOf(currentFrom);
						if (reverseIndex < 0 || neighbors.Count < 2)
							break;

						var nextNeighborIndex = (reverseIndex - 1 + neighbors.Count) % neighbors.Count;
						currentFrom = currentTo;
						currentTo = neighbors[nextNeighborIndex];
					}

					if (!closed || polygonNodeIndices.Count < 3)
						continue;

					var polygon = new List<Vector2>(polygonNodeIndices.Count);
					for (var index = 0; index < polygonNodeIndices.Count; index++)
						polygon.Add(nodes[polygonNodeIndices[index]]);
					polygon = SimplifyPolygon(polygon);
					var area = CalculateSignedArea(polygon);
					var minimumArea = Mathf.Max(0.005f, m_StrokeWidth * m_StrokeWidth * 2f);
					if (polygon.Count >= 3 && area > minimumArea)
						regions.Add(new PolygonFace(polygon));
				}
			}

			return regions;
		}

		private static int GetOrCreateNode(
			List<Vector2> nodes, List<List<int>> adjacency, Vector2 candidate, float mergeDistance) {
			var distanceSquared = mergeDistance * mergeDistance;
			for (var index = 0; index < nodes.Count; index++) {
				if ((nodes[index] - candidate).sqrMagnitude <= distanceSquared)
					return index;
			}

			nodes.Add(candidate);
			adjacency.Add(new List<int>());
			return nodes.Count - 1;
		}

		private static void AddGraphEdge(List<List<int>> adjacency, int firstNode, int secondNode) {
			if (firstNode == secondNode)
				return;
			if (!adjacency[firstNode].Contains(secondNode))
				adjacency[firstNode].Add(secondNode);
			if (!adjacency[secondNode].Contains(firstNode))
				adjacency[secondNode].Add(firstNode);
		}

		private static ulong DirectedEdgeKey(int fromNode, int toNode) {
			return ((ulong)(uint)fromNode << 32) | (uint)toNode;
		}

		private static List<Vector2> SimplifyPolygon(List<Vector2> polygon) {
			const float pointEpsilonSquared = 0.000001f;
			var simplified = new List<Vector2>(polygon.Count);
			for (var index = 0; index < polygon.Count; index++) {
				if (simplified.Count == 0 || (simplified[simplified.Count - 1] - polygon[index]).sqrMagnitude > pointEpsilonSquared)
					simplified.Add(polygon[index]);
			}

			if (simplified.Count > 1 && (simplified[0] - simplified[simplified.Count - 1]).sqrMagnitude <= pointEpsilonSquared)
				simplified.RemoveAt(simplified.Count - 1);

			var removedPoint = true;
			while (removedPoint && simplified.Count >= 3) {
				removedPoint = false;
				for (var index = 0; index < simplified.Count; index++) {
					var previous = simplified[(index - 1 + simplified.Count) % simplified.Count];
					var current = simplified[index];
					var next = simplified[(index + 1) % simplified.Count];
					if (Mathf.Abs(Cross(current - previous, next - current)) > 0.00001f)
						continue;

					simplified.RemoveAt(index);
					removedPoint = true;
					break;
				}
			}

			return simplified;
		}

		private static float CalculateSignedArea(IList<Vector2> polygon) {
			var area = 0f;
			for (var index = 0; index < polygon.Count; index++) {
				var nextIndex = (index + 1) % polygon.Count;
				area += Cross(polygon[index], polygon[nextIndex]);
			}

			return area * 0.5f;
		}

		private void CreateStrokeRenderers(List<StrokePath> paths) {
			m_StrokeRenderers.Clear();
			for (var index = 0; index < paths.Count; index++) {
				var strokeObject = new GameObject($"Stroke {index + 1:00}") {
					layer = gameObject.layer,
					hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave
				};
				strokeObject.transform.SetParent(m_StrokeRoot, false);

				var line = strokeObject.AddComponent<LineRenderer>();
				line.useWorldSpace = false;
				line.sharedMaterial = m_StrokeMaterial;
				line.widthMultiplier = m_StrokeWidth;
				line.numCornerVertices = 4;
				line.numCapVertices = 4;
				line.alignment = LineAlignment.View;
				line.textureMode = LineTextureMode.Stretch;
				line.startColor = paths[index].Color;
				line.endColor = paths[index].Color;
				line.shadowCastingMode = ShadowCastingMode.Off;
				line.receiveShadows = false;
				line.allowOcclusionWhenDynamic = false;
				line.sortingOrder = 1;
				line.positionCount = paths[index].Points.Length;
				line.SetPositions(ToVector3Array(paths[index].Points));
				m_StrokeRenderers.Add(line);
			}
		}

		private void UpdateRegionRenderer(List<PolygonFace> regions) {
			var fillCount = Mathf.Min(m_FilledRegionCount, regions == null ? 0 : regions.Count);
			if (fillCount <= 0)
				return;

			if (m_RegionMesh == null) {
				CreateRegionRenderer(regions, fillCount);
				return;
			}

			if (!TryPopulateRegionMesh(m_RegionMesh, regions, fillCount))
				return;
			if (m_RegionObject == null)
				CreateRegionObject();
		}

		private void CreateRegionRenderer(List<PolygonFace> regions, int count) {
			m_RegionMesh = BuildRegionMesh(regions, count);
			if (m_RegionMesh == null)
				return;
			CreateRegionObject();
		}

		private void CreateRegionObject() {
			var fillObject = new GameObject("Random Filled Regions") {
				layer = gameObject.layer,
				hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave
			};
			fillObject.transform.SetParent(m_RegionRoot, false);
			m_RegionObject = fillObject;

			var filter = fillObject.AddComponent<MeshFilter>();
			filter.sharedMesh = m_RegionMesh;
			var renderer = fillObject.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = m_RegionMaterial;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			renderer.allowOcclusionWhenDynamic = false;
			renderer.sortingOrder = 0;
		}

		private Mesh BuildRegionMesh(List<PolygonFace> regions, int count) {
			var mesh = new Mesh {
				name = "Random Region Fills",
				hideFlags = HideFlags.HideAndDontSave
			};
			mesh.MarkDynamic();
			if (TryPopulateRegionMesh(mesh, regions, count))
				return mesh;

			DestroyOwnedObject(mesh);
			return null;
		}

		private bool TryPopulateRegionMesh(Mesh mesh, List<PolygonFace> regions, int count) {
			m_RegionVertices.Clear();
			m_RegionTriangles.Clear();
			for (var regionIndex = 0; regionIndex < count; regionIndex++) {
				var polygon = regions[regionIndex].Points;
				var polygonTriangles = TriangulatePolygon(polygon);
				if (polygonTriangles.Count == 0)
					continue;

				var vertexStart = m_RegionVertices.Count;
				for (var pointIndex = 0; pointIndex < polygon.Count; pointIndex++) {
					var point = polygon[pointIndex];
					m_RegionVertices.Add(new Vector3(point.x, point.y, 0.02f));
				}

				for (var triangleIndex = 0; triangleIndex < polygonTriangles.Count; triangleIndex += 3) {
					m_RegionTriangles.Add(vertexStart + polygonTriangles[triangleIndex]);
					m_RegionTriangles.Add(vertexStart + polygonTriangles[triangleIndex + 2]);
					m_RegionTriangles.Add(vertexStart + polygonTriangles[triangleIndex + 1]);
				}
			}

			if (m_RegionTriangles.Count == 0)
				return false;

			mesh.Clear();
			mesh.SetVertices(m_RegionVertices);
			mesh.SetTriangles(m_RegionTriangles, 0);
			mesh.RecalculateBounds();
			return true;
		}

		private static List<int> TriangulatePolygon(IList<Vector2> polygon) {
			if (polygon.Count < 3)
				return new List<int>();

			var remaining = new List<int>(polygon.Count);
			for (var index = 0; index < polygon.Count; index++)
				remaining.Add(index);
			if (CalculateSignedArea(polygon) < 0f)
				remaining.Reverse();

			var triangles = new List<int>((polygon.Count - 2) * 3);
			var maximumIterations = polygon.Count * polygon.Count;
			while (remaining.Count > 3 && maximumIterations-- > 0) {
				var earFound = false;
				for (var index = 0; index < remaining.Count; index++) {
					var previousIndex = remaining[(index - 1 + remaining.Count) % remaining.Count];
					var currentIndex = remaining[index];
					var nextIndex = remaining[(index + 1) % remaining.Count];
					var previous = polygon[previousIndex];
					var current = polygon[currentIndex];
					var next = polygon[nextIndex];
					if (Cross(current - previous, next - current) <= 0.00001f)
						continue;

					var containsPoint = false;
					for (var candidateIndex = 0; candidateIndex < remaining.Count; candidateIndex++) {
						var candidate = remaining[candidateIndex];
						if (candidate == previousIndex || candidate == currentIndex || candidate == nextIndex)
							continue;
						if (!IsPointInTriangle(polygon[candidate], previous, current, next))
							continue;

						containsPoint = true;
						break;
					}

					if (containsPoint)
						continue;

					triangles.Add(previousIndex);
					triangles.Add(currentIndex);
					triangles.Add(nextIndex);
					remaining.RemoveAt(index);
					earFound = true;
					break;
				}

				if (!earFound)
					return new List<int>();
			}

			if (remaining.Count == 3) {
				triangles.Add(remaining[0]);
				triangles.Add(remaining[1]);
				triangles.Add(remaining[2]);
			}

			return triangles;
		}

		private static bool IsPointInTriangle(Vector2 point, Vector2 first, Vector2 second, Vector2 third) {
			const float edgeEpsilon = 0.00001f;
			var firstSide = Cross(second - first, point - first);
			var secondSide = Cross(third - second, point - second);
			var thirdSide = Cross(first - third, point - third);
			return firstSide >= -edgeEpsilon && secondSide >= -edgeEpsilon && thirdSide >= -edgeEpsilon;
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
				hideFlags = HideFlags.HideAndDontSave
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
			if (m_RegionRoot != null)
				DestroyOwnedObject(m_RegionRoot.gameObject);
			if (m_RegionMesh != null)
				DestroyOwnedObject(m_RegionMesh);
			if (m_StrokeMaterial != null)
				DestroyOwnedObject(m_StrokeMaterial);
			if (m_RegionMaterial != null)
				DestroyOwnedObject(m_RegionMaterial);

			m_StrokeRoot = null;
			m_RegionRoot = null;
			m_RegionObject = null;
			m_RegionMesh = null;
			m_StrokeMaterial = null;
			m_RegionMaterial = null;
			m_StrokeRenderers.Clear();
			m_TransitionTargetPaths = null;
			m_TransitionTargetRegions = null;
			m_CurrentRegions = null;
			m_TransitionRegenerated = false;
			m_RegionVertices.Clear();
			m_RegionTriangles.Clear();
		}

		private int GetGenerationSeed() {
			if (Application.isPlaying && m_RandomizeOnPlay)
				return Environment.TickCount;
			return m_Seed;
		}

		private static Vector3[] ToVector3Array(Vector2[] points) {
			var result = new Vector3[points.Length];
			for (var index = 0; index < points.Length; index++)
				result[index] = new Vector3(points[index].x, points[index].y, 0f);
			return result;
		}

		private static bool TryGetSegmentIntersection(
			Vector2 firstStart, Vector2 firstEnd,
			Vector2 secondStart, Vector2 secondEnd,
			out Vector2 intersection, out float firstProgress, out float secondProgress) {
			var firstDirection = firstEnd - firstStart;
			var secondDirection = secondEnd - secondStart;
			var denominator = Cross(firstDirection, secondDirection);
			if (Mathf.Abs(denominator) < 0.00001f) {
				intersection = default;
				firstProgress = 0f;
				secondProgress = 0f;
				return false;
			}

			var offset = secondStart - firstStart;
			firstProgress = Cross(offset, secondDirection) / denominator;
			secondProgress = Cross(offset, firstDirection) / denominator;
			if (firstProgress < 0f || firstProgress > 1f || secondProgress < 0f || secondProgress > 1f) {
				intersection = default;
				return false;
			}

			intersection = firstStart + firstDirection * firstProgress;
			return true;
		}

		private static float Cross(Vector2 first, Vector2 second) {
			return first.x * second.y - first.y * second.x;
		}

		private static void Shuffle<T>(IList<T> values, System.Random random) {
			for (var index = values.Count - 1; index > 0; index--) {
				var swapIndex = random.Next(index + 1);
				(values[index], values[swapIndex]) = (values[swapIndex], values[index]);
			}
		}

		private static float NextFloat(System.Random random, float minimum, float maximum) {
			return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
		}

		private static void DestroyOwnedObject(UnityEngine.Object value) {
			if (value == null)
				return;
			if (Application.isPlaying)
				Destroy(value);
			else
				DestroyImmediate(value);
		}

		private readonly struct SegmentSplit {
			public readonly float Progress;
			public readonly int NodeIndex;

			public SegmentSplit(float progress, int nodeIndex) {
				Progress = progress;
				NodeIndex = nodeIndex;
			}
		}

		private readonly struct PolygonFace {
			public readonly List<Vector2> Points;

			public PolygonFace(List<Vector2> points) {
				Points = points;
			}
		}

		private readonly struct StrokeLayout {
			public readonly List<StrokePath> Paths;
			public readonly List<PolygonFace> Regions;

			public StrokeLayout(List<StrokePath> paths, List<PolygonFace> regions) {
				Paths = paths;
				Regions = regions;
			}
		}

		private readonly struct StrokePath {
			public readonly Vector2[] Points;
			public readonly Color Color;

			public StrokePath(Vector2[] points, Color color) {
				Points = points;
				Color = color;
			}
		}
	}
}
