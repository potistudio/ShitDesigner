using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Scene {
	/// <summary>Generates a stylized field of candy sticks and animates a cutter across their decorated ends.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class ChitoseCandyCutScene : MonoBehaviour, ISceneGraphClockReceiver {
		private enum PatternType {
			Dot,
			Star,
			Ring,
			Diamond,
			Cross,
			Count
		}

		private sealed class Candy {
			public Transform Slice { get; }
			public float BaseSliceDistance { get; }
			public float CutCoordinate { get; }

			public Candy(Transform slice, float baseSliceDistance, float cutCoordinate) {
				Slice = slice;
				BaseSliceDistance = baseSliceDistance;
				CutCoordinate = cutCoordinate;
			}
		}

		[Header("Candy field")]
		[Min(3)] [SerializeField] private int m_CandyCount = 12;
		[Min(1f)] [SerializeField] private float m_CandyLength = 14f;
		[Min(0.05f)] [SerializeField] private float m_CandyRadius = 0.68f;
		[SerializeField] private Vector2 m_FieldSize = new Vector2(9.5f, 5.5f);
		[Tooltip("Direction from the rear of each stick to its cut end. The negative Z component points toward the camera.")]
		[SerializeField] private Vector3 m_CandyAxis = new Vector3(0.57f, -0.37f, -0.73f);
		[SerializeField] private int m_RandomSeed = 5108;
		[ColorUsage(true, true)] [SerializeField] private Color[] m_CandyColors = {
			new Color(0.05f, 0.72f, 0.74f, 1f),
			new Color(0.98f, 0.13f, 0.25f, 1f),
			new Color(0.98f, 0.91f, 0.04f, 1f),
			new Color(0.03f, 0.86f, 0.58f, 1f),
			new Color(0.92f, 0.17f, 0.43f, 1f)
		};

		[Header("Cut animation")]
		[Min(0.01f)] [SerializeField] private float m_CutterSpeed = 0.62f;
		[Min(0.1f)] [SerializeField] private float m_CutterTravel = 8f;
		[Min(0.01f)] [SerializeField] private float m_CutterImpactWidth = 0.72f;
		[Min(0f)] [SerializeField] private float m_SliceSeparation = 0.42f;
		[Range(0f, 30f)] [SerializeField] private float m_SliceRotation = 9f;

		[Header("Cutter")]
		[SerializeField] private Vector3 m_BladePosition = new Vector3(0f, 0f, -2.8f);
		[Min(1f)] [SerializeField] private float m_BladeLength = 17f;
		[Min(0.01f)] [SerializeField] private float m_BladeThickness = 0.18f;
		[Min(0.01f)] [SerializeField] private float m_BladeDepth = 0.16f;
		[SerializeField] private float m_BladeAngle = 52f;
		[Min(0.01f)] [SerializeField] private float m_BladeEdgeThickness = 0.07f;
		[ColorUsage(true, true)] [SerializeField] private Color m_BladeColor = new Color(0.92f, 0.98f, 0.95f, 1f);
		[ColorUsage(true, true)] [SerializeField] private Color m_BladeEdgeColor = new Color(0.08f, 0.25f, 0.27f, 1f);

		private readonly List<Candy> m_Candies = new List<Candy>();
		private readonly List<Material> m_GeneratedMaterials = new List<Material>();
		private Transform m_GeneratedRoot;
		private Transform m_Blade;
		private Mesh m_BodyMesh;
		private Mesh m_DiscMesh;
		private Mesh[] m_PatternMeshes = Array.Empty<Mesh>();
		private Material[] m_BodyMaterials = Array.Empty<Material>();
		private Material[] m_PatternMaterials = Array.Empty<Material>();
		private Material m_RimMaterial;
		private Material m_FaceMaterial;
		private Material m_BladeMaterial;
		private Material m_BladeEdgeMaterial;
		private Vector3 m_CandyAxisRuntime;
		private Vector3 m_CutterTravelDirection;
		private float m_AnimationTime;
		private bool m_RebuildRequested = true;
		private bool m_GraphClockDriven;

		private void OnEnable() {
			m_AnimationTime = 0f;
			Rebuild();
		}

		private void Update() {
			if (m_RebuildRequested)
				Rebuild();
			if (Application.isPlaying && !m_GraphClockDriven)
				Advance(Time.deltaTime);
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			m_GraphClockDriven = graphClockDriven;
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!m_GraphClockDriven || double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds <= 0d)
				return;

			Advance((float)Math.Min(deltaSeconds, float.MaxValue));
		}

		private void OnDisable() {
			ReleaseGeneratedContent();
		}

		private void OnDestroy() {
			ReleaseGeneratedContent();
		}

		private void OnValidate() {
			m_CandyCount = Mathf.Clamp(m_CandyCount, 3, 32);
			m_CandyLength = Mathf.Max(1f, m_CandyLength);
			m_CandyRadius = Mathf.Max(0.05f, m_CandyRadius);
			m_FieldSize.x = Mathf.Max(0.1f, m_FieldSize.x);
			m_FieldSize.y = Mathf.Max(0.1f, m_FieldSize.y);
			if (m_CandyAxis.sqrMagnitude < 0.0001f)
				m_CandyAxis = new Vector3(0.57f, -0.37f, -0.73f);
			m_CutterSpeed = Mathf.Max(0.01f, m_CutterSpeed);
			m_CutterTravel = Mathf.Max(0.1f, m_CutterTravel);
			m_CutterImpactWidth = Mathf.Max(0.01f, m_CutterImpactWidth);
			m_SliceSeparation = Mathf.Max(0f, m_SliceSeparation);
			m_BladeLength = Mathf.Max(1f, m_BladeLength);
			m_BladeThickness = Mathf.Max(0.01f, m_BladeThickness);
			m_BladeDepth = Mathf.Max(0.01f, m_BladeDepth);
			m_BladeEdgeThickness = Mathf.Max(0.01f, m_BladeEdgeThickness);
			m_RebuildRequested = true;
		}

		[ContextMenu("Rebuild Chitose Candy")]
		public void Rebuild() {
			m_RebuildRequested = false;
			ReleaseGeneratedContent();

			m_GeneratedRoot = new GameObject("Generated Chitose Candy").transform;
			m_GeneratedRoot.SetParent(transform, false);
			m_GeneratedRoot.gameObject.layer = gameObject.layer;
			m_GeneratedRoot.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;

			m_CandyAxisRuntime = m_CandyAxis.sqrMagnitude < 0.0001f
				? new Vector3(0.57f, -0.37f, -0.73f).normalized
				: m_CandyAxis.normalized;
			var projectedAxis = new Vector3(m_CandyAxisRuntime.x, m_CandyAxisRuntime.y, 0f);
			m_CutterTravelDirection = projectedAxis.sqrMagnitude < 0.0001f
				? Vector3.right
				: projectedAxis.normalized;

			m_BodyMesh = BuildBeveledCylinderMesh(m_CandyLength, m_CandyRadius, 18);
			m_DiscMesh = BuildCylinderMesh(1f, 1f, 24);
			m_PatternMeshes = BuildPatternMeshes();
			CreateMaterials();
			CreateCandies();
			CreateBlade();
			ApplyAnimationState();
		}

		[ContextMenu("Reset Chitose Candy Cut")]
		public void ResetAnimation() {
			m_AnimationTime = 0f;
			ApplyAnimationState();
		}

		private void Advance(float deltaSeconds) {
			if (deltaSeconds <= 0f || float.IsNaN(deltaSeconds) || float.IsInfinity(deltaSeconds))
				return;

			m_AnimationTime = Mathf.Repeat(m_AnimationTime + deltaSeconds, 2048f);
			ApplyAnimationState();
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
			m_BladeMaterial = CreateMaterial("Chitose Candy Cutter", m_BladeColor);
			m_BladeEdgeMaterial = CreateMaterial("Chitose Candy Cutter Edge", m_BladeEdgeColor);
		}

		private void CreateCandies() {
			var random = new System.Random(m_RandomSeed);
			var colors = ResolveCandyColors();
			var columns = Mathf.CeilToInt(Mathf.Sqrt(m_CandyCount));
			var rows = Mathf.CeilToInt(m_CandyCount / (float)columns);
			var xStep = columns <= 1 ? 0f : m_FieldSize.x / (columns - 1f);
			var yStep = rows <= 1 ? 0f : m_FieldSize.y / (rows - 1f);

			for (var index = 0; index < m_CandyCount; index++) {
				var column = index % columns;
				var row = index / columns;
				var x = (column - (columns - 1f) * 0.5f) * xStep;
				var y = ((rows - 1f) * 0.5f - row) * yStep;
				x += NextFloat(random, -0.16f, 0.16f);
				y += NextFloat(random, -0.16f, 0.16f);
				var frontPosition = new Vector3(x, y, NextFloat(random, -0.18f, 0.18f));
				var axis = Quaternion.AngleAxis(NextFloat(random, -2.5f, 2.5f), Vector3.forward) * m_CandyAxisRuntime;
				var candy = CreateCandy(index, frontPosition, axis.normalized, colors[index % colors.Length], random);
				m_Candies.Add(candy);
			}
		}

		private Candy CreateCandy(int index, Vector3 frontPosition, Vector3 axis, Color bodyColor, System.Random random) {
			var candyRoot = new GameObject($"Candy {index + 1:00}").transform;
			candyRoot.SetParent(m_GeneratedRoot, false);
			candyRoot.localPosition = frontPosition - axis * (m_CandyLength * 0.5f);
			candyRoot.localRotation = Quaternion.FromToRotation(Vector3.up, axis);

			CreateMeshObject("Stick", candyRoot, m_BodyMesh, m_BodyMaterials[index % m_BodyMaterials.Length], Vector3.one);

			var slice = new GameObject("Cut Slice").transform;
			slice.SetParent(candyRoot, false);
			var baseSliceDistance = m_CandyLength * 0.5f + m_CandyRadius * 0.02f;
			slice.localPosition = Vector3.up * baseSliceDistance;

			CreateMeshObject("Pale Rim", slice, m_DiscMesh, m_RimMaterial,
				new Vector3(m_CandyRadius * 0.94f, 0.055f, m_CandyRadius * 0.94f));
			var face = CreateMeshObject("Cut Face", slice, m_DiscMesh, m_FaceMaterial,
				new Vector3(m_CandyRadius * 0.76f, 0.028f, m_CandyRadius * 0.76f));
			face.localPosition = Vector3.up * 0.04f;

			var patternType = (PatternType)random.Next((int)PatternType.Count);
			var pattern = CreateMeshObject("Candy Pattern", slice, m_PatternMeshes[(int)patternType],
				m_PatternMaterials[(index + 1) % m_PatternMaterials.Length],
				Vector3.one * (m_CandyRadius * 0.34f));
			pattern.localPosition = Vector3.up * 0.064f;

			return new Candy(slice, baseSliceDistance, Vector3.Dot(frontPosition, m_CutterTravelDirection));
		}

		private void CreateBlade() {
			m_Blade = new GameObject("Cutter Blade").transform;
			m_Blade.SetParent(m_GeneratedRoot, false);
			m_Blade.localRotation = Quaternion.Euler(0f, 0f, m_BladeAngle);

			var shadow = CreatePrimitiveCube("Blade Shadow", m_Blade, m_BladeEdgeMaterial);
			shadow.localPosition = new Vector3(0f, 0f, 0.11f);
			shadow.localScale = new Vector3(m_BladeLength + 0.22f, m_BladeThickness + 0.12f, m_BladeDepth);

			var plate = CreatePrimitiveCube("Blade Plate", m_Blade, m_BladeMaterial);
			plate.localScale = new Vector3(m_BladeLength, m_BladeThickness, m_BladeDepth);

			var edge = CreatePrimitiveCube("Blade Highlight", m_Blade, m_BladeMaterial);
			edge.localPosition = new Vector3(0f, -m_BladeThickness * 0.34f, -m_BladeDepth * 0.58f);
			edge.localScale = new Vector3(m_BladeLength * 1.02f, m_BladeEdgeThickness, m_BladeDepth * 0.4f);
		}

		private void ApplyAnimationState() {
			if (m_Blade == null)
				return;

			var pingPong = Mathf.PingPong(m_AnimationTime * m_CutterSpeed, 1f);
			var progress = Mathf.SmoothStep(0f, 1f, pingPong);
			var travel = Mathf.Lerp(-m_CutterTravel, m_CutterTravel, progress);
			m_Blade.localPosition = m_BladePosition + m_CutterTravelDirection * travel;

			for (var index = 0; index < m_Candies.Count; index++) {
				var candy = m_Candies[index];
				var distance = Mathf.Abs(candy.CutCoordinate - travel);
				var impact = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(distance / m_CutterImpactWidth));
				var separation = impact * impact * m_SliceSeparation;
				var lean = Mathf.Sign(candy.CutCoordinate - travel) * impact * m_SliceRotation;
				candy.Slice.localPosition = Vector3.up * (candy.BaseSliceDistance + separation);
				candy.Slice.localRotation = Quaternion.AngleAxis(lean, Vector3.forward);
			}
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

			return new[] {
				new Color(0.05f, 0.72f, 0.74f, 1f),
				new Color(0.98f, 0.13f, 0.25f, 1f),
				new Color(0.98f, 0.91f, 0.04f, 1f)
			};
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

		private static Mesh BuildBeveledCylinderMesh(float length, float radius, int segments) {
			var halfLength = length * 0.5f;
			var bevel = Mathf.Min(radius * 0.38f, length * 0.08f);
			var ringPositions = new[] { -halfLength, -halfLength + bevel, halfLength - bevel, halfLength };
			var ringRadii = new[] { radius * 0.62f, radius, radius, radius * 0.62f };
			var vertices = new Vector3[ringPositions.Length * segments + 2];
			for (var ring = 0; ring < ringPositions.Length; ring++) {
				for (var segment = 0; segment < segments; segment++) {
					var angle = Mathf.PI * 2f * segment / segments;
					vertices[ring * segments + segment] = new Vector3(
						Mathf.Cos(angle) * ringRadii[ring], ringPositions[ring], Mathf.Sin(angle) * ringRadii[ring]);
				}
			}
			var bottomCenter = vertices.Length - 2;
			var topCenter = vertices.Length - 1;
			vertices[bottomCenter] = new Vector3(0f, -halfLength, 0f);
			vertices[topCenter] = new Vector3(0f, halfLength, 0f);

			var triangles = new int[(ringPositions.Length - 1) * segments * 6 + segments * 6];
			var triangleIndex = 0;
			for (var ring = 0; ring < ringPositions.Length - 1; ring++) {
				for (var segment = 0; segment < segments; segment++) {
					var next = (segment + 1) % segments;
					var lower = ring * segments + segment;
					var lowerNext = ring * segments + next;
					var upper = (ring + 1) * segments + segment;
					var upperNext = (ring + 1) * segments + next;
					AddTriangle(triangles, ref triangleIndex, lower, upper, lowerNext);
					AddTriangle(triangles, ref triangleIndex, lowerNext, upper, upperNext);
				}
			}
			for (var segment = 0; segment < segments; segment++) {
				var next = (segment + 1) % segments;
				AddTriangle(triangles, ref triangleIndex, bottomCenter, segment, next);
				var topStart = (ringPositions.Length - 1) * segments;
				AddTriangle(triangles, ref triangleIndex, topCenter, topStart + next, topStart + segment);
			}

			var mesh = new Mesh { name = "Chitose Candy Stick", hideFlags = HideFlags.HideAndDontSave };
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

		private static Transform CreateMeshObject(string objectName, Transform parent, Mesh mesh, Material material, Vector3 localScale) {
			var item = new GameObject(objectName).transform;
			item.gameObject.hideFlags = HideFlags.DontSave;
			item.SetParent(parent, false);
			item.localScale = localScale;
			var meshFilter = item.gameObject.AddComponent<MeshFilter>();
			meshFilter.sharedMesh = mesh;
			var meshRenderer = item.gameObject.AddComponent<MeshRenderer>();
			meshRenderer.sharedMaterial = material;
			meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
			meshRenderer.receiveShadows = false;
			return item;
		}

		private static Transform CreatePrimitiveCube(string objectName, Transform parent, Material material) {
			var item = GameObject.CreatePrimitive(PrimitiveType.Cube);
			item.name = objectName;
			item.hideFlags = HideFlags.DontSave;
			item.transform.SetParent(parent, false);
			var collider = item.GetComponent<Collider>();
			if (collider != null)
				DestroyOwnedObject(collider);
			var meshRenderer = item.GetComponent<MeshRenderer>();
			if (meshRenderer != null) {
				meshRenderer.sharedMaterial = material;
				meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
				meshRenderer.receiveShadows = false;
			}
			return item.transform;
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
			m_Blade = null;
			m_Candies.Clear();

			DestroyOwnedObject(m_BodyMesh);
			DestroyOwnedObject(m_DiscMesh);
			m_BodyMesh = null;
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
			m_BladeMaterial = null;
			m_BladeEdgeMaterial = null;
		}

		private static float NextFloat(System.Random random, float minimum, float maximum) {
			return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
		}

		private static void AddTriangle(int[] triangles, ref int index, int first, int second, int third) {
			triangles[index++] = first;
			triangles[index++] = second;
			triangles[index++] = third;
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
