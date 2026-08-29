using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Scene {
	/// <summary>Generates a stylized field of candy sticks, cuts their decorated ends, and drops the split fragments.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class ChitoseCandyCutScene : MonoBehaviour {
		private enum PatternType {
			Dot,
			Star,
			Ring,
			Diamond,
			Cross,
			Count
		}

		private sealed class Candy {
			public Transform RearSegment { get; }
			public Transform FrontSegment { get; }
			public Transform RearCutFace { get; }
			public Transform FrontCutFace { get; }
			public Rigidbody RearBody { get; }
			public Rigidbody FrontBody { get; }
			public float RearBasePosition { get; }
			public float FrontBasePosition { get; }
			public Vector3 FrontImpulse { get; }
			public bool PhysicsActivated { get; set; }

			public Candy(Transform rearSegment, Transform frontSegment, Transform rearCutFace, Transform frontCutFace,
				Rigidbody rearBody, Rigidbody frontBody, float rearBasePosition, float frontBasePosition,
				Vector3 frontImpulse) {
				RearSegment = rearSegment;
				FrontSegment = frontSegment;
				RearCutFace = rearCutFace;
				FrontCutFace = frontCutFace;
				RearBody = rearBody;
				FrontBody = frontBody;
				RearBasePosition = rearBasePosition;
				FrontBasePosition = frontBasePosition;
				FrontImpulse = frontImpulse;
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

		[Header("Cut")]
		[Min(0.1f)] [SerializeField] private float m_CutPieceLength = 1.75f;
		[Min(0f)] [SerializeField] private float m_SplitGap = 0.55f;
		[Min(0f)] [SerializeField] private float m_HorizontalImpulse = 0.9f;

		private readonly List<Candy> m_Candies = new List<Candy>();
		private readonly List<Material> m_GeneratedMaterials = new List<Material>();
		private Transform m_GeneratedRoot;
		private Mesh m_RearBodyMesh;
		private Mesh m_FrontBodyMesh;
		private Mesh m_DiscMesh;
		private Mesh[] m_PatternMeshes = Array.Empty<Mesh>();
		private Material[] m_BodyMaterials = Array.Empty<Material>();
		private Material[] m_PatternMaterials = Array.Empty<Material>();
		private Material m_RimMaterial;
		private Material m_FaceMaterial;
		private Vector3 m_CandyAxisRuntime;
		private float m_RearPieceLength;
		private float m_FrontPieceLength;
		private bool m_RebuildRequested = true;

		private void OnEnable() {
			Rebuild();
		}

		private void Update() {
			if (m_RebuildRequested)
				Rebuild();
			else if (Application.isPlaying)
				ApplyCutState();
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
			m_CutPieceLength = Mathf.Clamp(m_CutPieceLength, 0.1f, m_CandyLength - 0.1f);
			m_SplitGap = Mathf.Max(0f, m_SplitGap);
			m_HorizontalImpulse = Mathf.Max(0f, m_HorizontalImpulse);
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

			m_FrontPieceLength = Mathf.Clamp(m_CutPieceLength, 0.1f, m_CandyLength - 0.1f);
			m_RearPieceLength = m_CandyLength - m_FrontPieceLength;
			m_RearBodyMesh = BuildCylinderMesh(m_RearPieceLength, m_CandyRadius, 18);
			m_FrontBodyMesh = BuildCylinderMesh(m_FrontPieceLength, m_CandyRadius, 18);
			m_DiscMesh = BuildCylinderMesh(1f, 1f, 24);
			m_PatternMeshes = BuildPatternMeshes();
			CreateMaterials();
			CreateCandies();
			ApplyCutState();
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
				var candy = CreateCandy(index, frontPosition, axis.normalized, random);
				m_Candies.Add(candy);
			}
		}

		private Candy CreateCandy(int index, Vector3 frontPosition, Vector3 axis, System.Random random) {
			var candyRoot = new GameObject($"Candy {index + 1:00}").transform;
			candyRoot.gameObject.hideFlags = HideFlags.DontSave;
			candyRoot.SetParent(m_GeneratedRoot, false);
			candyRoot.localPosition = frontPosition - axis * (m_CandyLength * 0.5f);
			candyRoot.localRotation = Quaternion.FromToRotation(Vector3.up, axis);

			var rearSegment = new GameObject("Rear Segment").transform;
			rearSegment.gameObject.hideFlags = HideFlags.DontSave;
			rearSegment.SetParent(candyRoot, false);
			var rearBasePosition = -m_FrontPieceLength * 0.5f;
			rearSegment.localPosition = Vector3.up * rearBasePosition;
			CreateMeshObject("Rear Candy", rearSegment, m_RearBodyMesh,
				m_BodyMaterials[index % m_BodyMaterials.Length], Vector3.one);
			var rearBody = AddPhysicsBody(rearSegment, m_RearPieceLength);

			var frontSegment = new GameObject("Front Segment").transform;
			frontSegment.gameObject.hideFlags = HideFlags.DontSave;
			frontSegment.SetParent(candyRoot, false);
			var frontBasePosition = m_CandyLength * 0.5f - m_FrontPieceLength * 0.5f;
			frontSegment.localPosition = Vector3.up * frontBasePosition;
			CreateMeshObject("Front Candy", frontSegment, m_FrontBodyMesh,
				m_BodyMaterials[index % m_BodyMaterials.Length], Vector3.one);
			var frontBody = AddPhysicsBody(frontSegment, m_FrontPieceLength);

			var cutFaceScale = new Vector3(m_CandyRadius * 0.76f, 0.028f, m_CandyRadius * 0.76f);
			var rearCutFace = CreateMeshObject("Rear Cut Face", rearSegment, m_DiscMesh, m_FaceMaterial, cutFaceScale);
			rearCutFace.localPosition = Vector3.up * (m_RearPieceLength * 0.5f + 0.018f);
			rearCutFace.gameObject.SetActive(false);
			var frontCutFace = CreateMeshObject("Front Cut Face", frontSegment, m_DiscMesh, m_FaceMaterial, cutFaceScale);
			frontCutFace.localPosition = Vector3.up * (-m_FrontPieceLength * 0.5f - 0.018f);
			frontCutFace.gameObject.SetActive(false);

			var originalEnd = new GameObject("Original Candy End").transform;
			originalEnd.gameObject.hideFlags = HideFlags.DontSave;
			originalEnd.SetParent(frontSegment, false);
			originalEnd.localPosition = Vector3.up * (m_FrontPieceLength * 0.5f + m_CandyRadius * 0.02f);
			CreateMeshObject("Pale Rim", originalEnd, m_DiscMesh, m_RimMaterial,
				new Vector3(m_CandyRadius * 0.94f, 0.055f, m_CandyRadius * 0.94f));
			var face = CreateMeshObject("Original Cut Face", originalEnd, m_DiscMesh, m_FaceMaterial,
				new Vector3(m_CandyRadius * 0.76f, 0.028f, m_CandyRadius * 0.76f));
			face.localPosition = Vector3.up * 0.04f;

			var patternType = (PatternType)random.Next((int)PatternType.Count);
			var pattern = CreateMeshObject("Candy Pattern", originalEnd, m_PatternMeshes[(int)patternType],
				m_PatternMaterials[(index + 1) % m_PatternMaterials.Length],
				Vector3.one * (m_CandyRadius * 0.34f));
			pattern.localPosition = Vector3.up * 0.064f;

			return new Candy(rearSegment, frontSegment, rearCutFace, frontCutFace, rearBody, frontBody,
				rearBasePosition, frontBasePosition, CreateHorizontalImpulse(random));
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

		private Vector3 CreateHorizontalImpulse(System.Random random) {
			var magnitude = NextFloat(random, m_HorizontalImpulse * 0.55f, m_HorizontalImpulse);
			var direction = random.Next(2) == 0 ? -1f : 1f;
			return new Vector3(direction * magnitude, 0f, 0f);
		}

		private void ApplyCutState() {
			var splitOffset = m_SplitGap * 0.5f;
			for (var index = 0; index < m_Candies.Count; index++) {
				var candy = m_Candies[index];
				if (!candy.PhysicsActivated) {
					candy.RearSegment.localPosition = Vector3.up * (candy.RearBasePosition - splitOffset);
					candy.FrontSegment.localPosition = Vector3.up * (candy.FrontBasePosition + splitOffset);
					if (Application.isPlaying)
						ActivatePhysics(candy);
				}
				if (!candy.RearCutFace.gameObject.activeSelf)
					candy.RearCutFace.gameObject.SetActive(true);
				if (!candy.FrontCutFace.gameObject.activeSelf)
					candy.FrontCutFace.gameObject.SetActive(true);
			}
		}

		private void ActivatePhysics(Candy candy) {
			if (candy.PhysicsActivated)
				return;

			candy.PhysicsActivated = true;
			Physics.SyncTransforms();
			ActivatePhysics(candy.FrontBody, candy.FrontImpulse);
		}

		private static void ActivatePhysics(Rigidbody rigidbody, Vector3 horizontalImpulse) {
			if (rigidbody == null)
				return;

			rigidbody.isKinematic = false;
			rigidbody.useGravity = true;
			rigidbody.collisionDetectionMode = CollisionDetectionMode.Continuous;
			rigidbody.AddForce(horizontalImpulse, ForceMode.Impulse);
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

			DestroyOwnedObject(m_RearBodyMesh);
			DestroyOwnedObject(m_FrontBodyMesh);
			DestroyOwnedObject(m_DiscMesh);
			m_RearBodyMesh = null;
			m_FrontBodyMesh = null;
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
