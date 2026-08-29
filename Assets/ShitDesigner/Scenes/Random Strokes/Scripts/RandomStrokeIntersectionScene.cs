using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Scene {
	/// <summary>Draws random 2D strokes and fills a random subset of their intersections.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class RandomStrokeIntersectionScene : MonoBehaviour {
		[Header("Canvas")]
		[Min(1f)][SerializeField] private Vector2 m_CanvasSize = new Vector2(10f, 6f);

		[Header("Strokes")]
		[Range(2, 64)][SerializeField] private int m_StrokeCount = 8;
		[Range(2, 64)][SerializeField] private int m_PointsPerStroke = 11;
		[Min(0.005f)][SerializeField] private float m_StrokeWidth = 0.08f;
		[Min(0f)][SerializeField] private float m_Wobble = 0.35f;
		[Range(0f, 1f)][SerializeField] private float m_PointJitter = 0.1f;

		[Header("Intersections")]
		[Range(0, 128)][SerializeField] private int m_FilledIntersectionCount = 14;
		[Min(0.01f)][SerializeField] private float m_IntersectionRadius = 0.17f;

		[Header("Randomness")]
		[SerializeField] private bool m_RandomizeOnPlay = true;
		[SerializeField] private int m_Seed = 8127;

		[Header("Appearance")]
		[ColorUsage(false, true)][SerializeField] private Color m_StrokeColorA = new Color(0.2f, 0.85f, 1f, 1f);
		[ColorUsage(false, true)][SerializeField] private Color m_StrokeColorB = new Color(0.75f, 0.35f, 1f, 1f);
		[ColorUsage(false, true)][SerializeField] private Color m_IntersectionColor = new Color(1f, 0.35f, 0.1f, 1f);

		private const int IntersectionCircleSegments = 24;

		private Transform m_StrokeRoot;
		private Transform m_IntersectionRoot;
		private Material m_StrokeMaterial;
		private Material m_IntersectionMaterial;
		private Mesh m_IntersectionMesh;

		private void OnEnable() {
			if (!Application.isPlaying)
				Generate();
		}

		private void Start() {
			Generate();
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
			m_PointsPerStroke = Mathf.Clamp(m_PointsPerStroke, 2, 64);
			m_StrokeWidth = Mathf.Max(0.005f, m_StrokeWidth);
			m_Wobble = Mathf.Max(0f, m_Wobble);
			m_PointJitter = Mathf.Clamp01(m_PointJitter);
			m_FilledIntersectionCount = Mathf.Clamp(m_FilledIntersectionCount, 0, 128);
			m_IntersectionRadius = Mathf.Max(0.01f, m_IntersectionRadius);

			if (!Application.isPlaying && isActiveAndEnabled)
				Generate();
		}

		[ContextMenu("Generate Random Strokes")]
		public void Generate() {
			ReleaseGeneratedContent();

			var seed = GetGenerationSeed();
			List<StrokePath> paths = null;
			List<Vector2> intersections = null;
			for (var attempt = 0; attempt < 4; attempt++) {
				var pathRandom = new System.Random(unchecked(seed + attempt * 7919));
				paths = BuildPaths(pathRandom, Mathf.Pow(0.65f, attempt));
				intersections = FindIntersections(paths);
				if (intersections.Count > 0)
					break;
			}

			if (intersections == null || intersections.Count == 0) {
				var fallbackRandom = new System.Random(seed);
				paths = BuildPaths(fallbackRandom, 0f);
				intersections = FindIntersections(paths);
			}

			m_StrokeRoot = CreateGeneratedRoot("Random Strokes");
			m_IntersectionRoot = CreateGeneratedRoot("Filled Intersections");
			m_StrokeMaterial = CreateMaterial("Random Stroke Material");
			m_IntersectionMaterial = CreateMaterial("Random Intersection Material");
			SetMaterialColor(m_StrokeMaterial, Color.white);
			SetMaterialColor(m_IntersectionMaterial, m_IntersectionColor);

			CreateStrokeRenderers(paths);

			Shuffle(intersections, new System.Random(unchecked(seed ^ 0x5F3759DF)));
			var fillCount = Mathf.Min(m_FilledIntersectionCount, intersections.Count);
			if (fillCount > 0)
				CreateIntersectionRenderer(intersections, fillCount);
		}

		private List<StrokePath> BuildPaths(System.Random random, float variationScale) {
			var horizontalCount = Mathf.Clamp(m_StrokeCount / 2, 1, m_StrokeCount - 1);
			var orientations = new List<bool>(m_StrokeCount);
			for (var index = 0; index < horizontalCount; index++)
				orientations.Add(true);
			for (var index = horizontalCount; index < m_StrokeCount; index++)
				orientations.Add(false);
			Shuffle(orientations, random);

			var paths = new List<StrokePath>(m_StrokeCount);
			var halfWidth = m_CanvasSize.x * 0.5f;
			var halfHeight = m_CanvasSize.y * 0.5f;
			var edgeMargin = Mathf.Min(0.3f, Mathf.Min(halfWidth, halfHeight) * 0.75f);

			for (var strokeIndex = 0; strokeIndex < m_StrokeCount; strokeIndex++) {
				var horizontal = orientations[strokeIndex];
				var start = horizontal
					? new Vector2(-halfWidth, NextFloat(random, -halfHeight + edgeMargin, halfHeight - edgeMargin))
					: new Vector2(NextFloat(random, -halfWidth + edgeMargin, halfWidth - edgeMargin), -halfHeight);
				var end = horizontal
					? new Vector2(halfWidth, NextFloat(random, -halfHeight + edgeMargin, halfHeight - edgeMargin))
					: new Vector2(NextFloat(random, -halfWidth + edgeMargin, halfWidth - edgeMargin), halfHeight);

				var points = new Vector2[m_PointsPerStroke];
				var phase = NextFloat(random, 0f, Mathf.PI * 2f);
				var amplitude = NextFloat(random, 0.75f, 1.25f) * m_Wobble * variationScale;
				var normal = horizontal ? Vector2.up : Vector2.right;
				for (var pointIndex = 0; pointIndex < points.Length; pointIndex++) {
					var normalized = pointIndex / (float)(points.Length - 1);
					var center = Vector2.Lerp(start, end, normalized);
					var envelope = Mathf.Sin(normalized * Mathf.PI);
					var wave = Mathf.Sin(normalized * Mathf.PI * 2f + phase) * amplitude * envelope;
					var jitter = NextFloat(random, -m_PointJitter, m_PointJitter) * variationScale * envelope;
					points[pointIndex] = center + normal * (wave + jitter);
				}

				var colorProgress = m_StrokeCount <= 1 ? 0f : strokeIndex / (float)(m_StrokeCount - 1);
				var color = Color.Lerp(m_StrokeColorA, m_StrokeColorB, colorProgress);
				paths.Add(new StrokePath(points, color));
			}

			return paths;
		}

		private List<Vector2> FindIntersections(List<StrokePath> paths) {
			var intersections = new List<Vector2>();
			var mergeDistance = Mathf.Max(0.02f, m_IntersectionRadius * 0.5f);
			for (var firstPath = 0; firstPath < paths.Count - 1; firstPath++) {
				for (var secondPath = firstPath + 1; secondPath < paths.Count; secondPath++) {
					var firstPoints = paths[firstPath].Points;
					var secondPoints = paths[secondPath].Points;
					for (var firstSegment = 0; firstSegment < firstPoints.Length - 1; firstSegment++) {
						for (var secondSegment = 0; secondSegment < secondPoints.Length - 1; secondSegment++) {
							if (!TryGetSegmentIntersection(
								firstPoints[firstSegment], firstPoints[firstSegment + 1],
								secondPoints[secondSegment], secondPoints[secondSegment + 1], out var point))
								continue;

							if (!ContainsNearby(intersections, point, mergeDistance))
								intersections.Add(point);
						}
					}
				}
			}

			return intersections;
		}

		private void CreateStrokeRenderers(List<StrokePath> paths) {
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
			}
		}

		private void CreateIntersectionRenderer(List<Vector2> intersections, int count) {
			var fillObject = new GameObject("Random Filled Intersections") {
				layer = gameObject.layer,
				hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave
			};
			fillObject.transform.SetParent(m_IntersectionRoot, false);

			m_IntersectionMesh = BuildIntersectionMesh(intersections, count);
			var filter = fillObject.AddComponent<MeshFilter>();
			filter.sharedMesh = m_IntersectionMesh;
			var renderer = fillObject.AddComponent<MeshRenderer>();
			renderer.sharedMaterial = m_IntersectionMaterial;
			renderer.shadowCastingMode = ShadowCastingMode.Off;
			renderer.receiveShadows = false;
			renderer.allowOcclusionWhenDynamic = false;
			renderer.sortingOrder = 10;
		}

		private Mesh BuildIntersectionMesh(List<Vector2> intersections, int count) {
			var vertexCountPerCircle = IntersectionCircleSegments + 1;
			var vertices = new Vector3[count * vertexCountPerCircle];
			var triangles = new int[count * IntersectionCircleSegments * 3];
			var triangleIndex = 0;

			for (var circle = 0; circle < count; circle++) {
				var vertexStart = circle * vertexCountPerCircle;
				var center = intersections[circle];
				vertices[vertexStart] = new Vector3(center.x, center.y, -0.05f);
				for (var segment = 0; segment < IntersectionCircleSegments; segment++) {
					var angle = segment * Mathf.PI * 2f / IntersectionCircleSegments;
					vertices[vertexStart + segment + 1] = new Vector3(
						center.x + Mathf.Cos(angle) * m_IntersectionRadius,
						center.y + Mathf.Sin(angle) * m_IntersectionRadius,
						-0.05f);
				}

				for (var segment = 0; segment < IntersectionCircleSegments; segment++) {
					var current = vertexStart + segment + 1;
					var next = vertexStart + (segment + 1) % IntersectionCircleSegments + 1;
					triangles[triangleIndex++] = vertexStart;
					triangles[triangleIndex++] = next;
					triangles[triangleIndex++] = current;
				}
			}

			var mesh = new Mesh {
				name = "Random Intersection Fills",
				hideFlags = HideFlags.HideAndDontSave
			};
			mesh.vertices = vertices;
			mesh.triangles = triangles;
			mesh.RecalculateBounds();
			return mesh;
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

			return new Material(shader) {
				name = materialName,
				hideFlags = HideFlags.HideAndDontSave
			};
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
			if (m_IntersectionRoot != null)
				DestroyOwnedObject(m_IntersectionRoot.gameObject);
			if (m_IntersectionMesh != null)
				DestroyOwnedObject(m_IntersectionMesh);
			if (m_StrokeMaterial != null)
				DestroyOwnedObject(m_StrokeMaterial);
			if (m_IntersectionMaterial != null)
				DestroyOwnedObject(m_IntersectionMaterial);

			m_StrokeRoot = null;
			m_IntersectionRoot = null;
			m_IntersectionMesh = null;
			m_StrokeMaterial = null;
			m_IntersectionMaterial = null;
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
			out Vector2 intersection) {
			var firstDirection = firstEnd - firstStart;
			var secondDirection = secondEnd - secondStart;
			var denominator = Cross(firstDirection, secondDirection);
			if (Mathf.Abs(denominator) < 0.00001f) {
				intersection = default;
				return false;
			}

			var offset = secondStart - firstStart;
			var firstProgress = Cross(offset, secondDirection) / denominator;
			var secondProgress = Cross(offset, firstDirection) / denominator;
			if (firstProgress < 0f || firstProgress > 1f || secondProgress < 0f || secondProgress > 1f) {
				intersection = default;
				return false;
			}

			intersection = firstStart + firstDirection * firstProgress;
			return true;
		}

		private static bool ContainsNearby(List<Vector2> points, Vector2 candidate, float distance) {
			var distanceSquared = distance * distance;
			for (var index = 0; index < points.Count; index++) {
				if ((points[index] - candidate).sqrMagnitude <= distanceSquared)
					return true;
			}

			return false;
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
