using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace ShitDesigner.Scene {
	/// <summary>Builds an inspector-configurable cylindrical object field that moves around a fixed camera.</summary>
	[ExecuteAlways]
	[DisallowMultipleComponent]
	public sealed class CylindricalObjectFlythrough : MonoBehaviour, ISceneGraphClockReceiver {
		public enum PrimitiveSelection {
			Cube,
			Sphere,
			Capsule,
			Mixed
		}

		[Header("Object field")]
		[Tooltip("Optional prefab used for every scattered object. A primitive is created when this is empty.")]
		[SerializeField] private GameObject objectPrefab;
		[SerializeField] private PrimitiveSelection primitiveSelection = PrimitiveSelection.Mixed;
		[Min(1)][SerializeField] private int objectCount = 300;
		[Min(0.1f)][SerializeField] private float cylinderRadius = 8f;
		[Min(0f)][SerializeField] private float radialJitter = 1.5f;
		[Min(1f)][SerializeField] private float tunnelLength = 100f;
		[SerializeField] private Vector2 objectScaleRange = new Vector2(0.35f, 1.4f);
		[SerializeField] private bool alignToCylinder = true;
		[SerializeField] private int randomSeed = 8721;

		[Header("Appearance")]
		[Tooltip("Optional materials assigned at random. A built-in emissive palette is used when this is empty.")]
		[SerializeField] private Material[] materials = Array.Empty<Material>();
		[ColorUsage(true, true)]
		[SerializeField]
		private Color[] fallbackColors = {
			new Color(0.08f, 0.8f, 1f, 1f),
			new Color(1f, 0.12f, 0.55f, 1f),
			new Color(0.6f, 0.2f, 1f, 1f),
			new Color(1f, 0.65f, 0.08f, 1f)
		};

		[Header("Object motion")]
		[FormerlySerializedAs("cameraSpeed")]
		[Min(0f)][SerializeField] private float objectSpeed = 1.5f;
		[FormerlySerializedAs("cameraStartZ")]
		[SerializeField] private float objectStartZ;
		[FormerlySerializedAs("loopCamera")]
		[SerializeField] private bool loopObjects = true;

		private Transform _generatedRoot;
		private Material[] _generatedMaterials = Array.Empty<Material>();
		private bool _rebuildRequested = true;
		private bool _graphClockDriven;

		public int GeneratedObjectCount => _generatedRoot == null ? 0 : _generatedRoot.childCount;
		public Vector3 GeneratedFieldLocalPosition => _generatedRoot == null ? Vector3.zero : _generatedRoot.localPosition;

		private void OnEnable() {
			Rebuild();
			ResetObjectFieldPosition();
		}

		private void Update() {
			if (_rebuildRequested) Rebuild();
			if (Application.isPlaying && !_graphClockDriven) MoveObjects(Time.deltaTime);
		}

		public void SetGraphClockDriven(bool graphClockDriven) => _graphClockDriven = graphClockDriven;

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!_graphClockDriven || deltaSeconds <= 0d) return;
			MoveObjects((float)Math.Min(deltaSeconds, float.MaxValue));
		}

		private void OnDisable() => ReleaseGeneratedContent();

		private void OnDestroy() => ReleaseGeneratedContent();

		private void OnValidate() {
			objectCount = Mathf.Clamp(objectCount, 1, 5000);
			cylinderRadius = Mathf.Max(0.1f, cylinderRadius);
			radialJitter = Mathf.Clamp(radialJitter, 0f, cylinderRadius * 0.95f);
			tunnelLength = Mathf.Max(1f, tunnelLength);
			objectScaleRange.x = Mathf.Max(0.01f, objectScaleRange.x);
			objectScaleRange.y = Mathf.Max(objectScaleRange.x, objectScaleRange.y);
			objectSpeed = Mathf.Max(0f, objectSpeed);
			_rebuildRequested = true;
		}

		[ContextMenu("Rebuild Object Field")]
		public void Rebuild() {
			_rebuildRequested = false;
			ReleaseGeneratedContent();
			_generatedRoot = new GameObject("Generated Object Field").transform;
			_generatedRoot.SetParent(transform, false);
			_generatedRoot.gameObject.layer = gameObject.layer;
			_generatedRoot.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;

			var random = new System.Random(randomSeed);
			var usableMaterials = ResolveMaterials();
			for (var index = 0; index < objectCount; index++) {
				var item = CreateObject(index, random);
				item.transform.SetParent(_generatedRoot, false);
				item.hideFlags = HideFlags.DontSave;
				PlaceObject(item.transform, random);
				var renderer = item.GetComponentInChildren<Renderer>();
				if (renderer != null && usableMaterials.Length != 0)
					renderer.sharedMaterial = usableMaterials[random.Next(usableMaterials.Length)];
			}
		}

		[ContextMenu("Reset Object Field Position")]
		public void ResetObjectFieldPosition() {
			if (_generatedRoot == null) return;
			_generatedRoot.localPosition = Vector3.zero;
		}

		private void MoveObjects(float deltaTime) {
			if (_generatedRoot == null) return;
			var position = _generatedRoot.localPosition;
			position.z -= objectSpeed * deltaTime;
			if (loopObjects && position.z <= -tunnelLength)
				position.z = -Mathf.Repeat(-position.z, tunnelLength);
			_generatedRoot.localPosition = position;
		}

		private GameObject CreateObject(int index, System.Random random) {
			GameObject item;
			if (objectPrefab != null) {
				item = Instantiate(objectPrefab);
			}
			else {
				var primitive = ResolvePrimitive(random);
				item = GameObject.CreatePrimitive(primitive);
				var collider = item.GetComponent<Collider>();
				if (collider != null) DestroyOwnedObject(collider);
			}
			item.name = $"Scattered Object {index + 1:0000}";
			foreach (var child in item.GetComponentsInChildren<Transform>(true))
				child.gameObject.layer = gameObject.layer;
			return item;
		}

		private PrimitiveType ResolvePrimitive(System.Random random) {
			switch (primitiveSelection) {
				case PrimitiveSelection.Sphere: return PrimitiveType.Sphere;
				case PrimitiveSelection.Capsule: return PrimitiveType.Capsule;
				case PrimitiveSelection.Mixed:
					var choices = new[] { PrimitiveType.Cube, PrimitiveType.Sphere, PrimitiveType.Capsule };
					return choices[random.Next(choices.Length)];
				default: return PrimitiveType.Cube;
			}
		}

		private void PlaceObject(Transform item, System.Random random) {
			var angle = NextFloat(random, 0f, Mathf.PI * 2f);
			var radius = cylinderRadius + NextFloat(random, -radialJitter, radialJitter);
			var radialDirection = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0f);
			item.localPosition = radialDirection * radius + Vector3.forward * NextFloat(random, objectStartZ, objectStartZ + tunnelLength);

			var uniformScale = NextFloat(random, objectScaleRange.x, objectScaleRange.y);
			var stretch = NextFloat(random, 0.65f, 1.6f);
			item.localScale = new Vector3(uniformScale, uniformScale * stretch, uniformScale);
			item.localRotation = alignToCylinder
				? Quaternion.FromToRotation(Vector3.up, radialDirection) * Quaternion.AngleAxis(NextFloat(random, 0f, 360f), Vector3.up)
				: Quaternion.Euler(NextFloat(random, 0f, 360f), NextFloat(random, 0f, 360f), NextFloat(random, 0f, 360f));
		}

		private Material[] ResolveMaterials() {
			if (materials != null) {
				var validCount = 0;
				for (var index = 0; index < materials.Length; index++)
					if (materials[index] != null) validCount++;
				if (validCount != 0) {
					var validMaterials = new Material[validCount];
					var destination = 0;
					for (var index = 0; index < materials.Length; index++)
						if (materials[index] != null) validMaterials[destination++] = materials[index];
					return validMaterials;
				}
			}

			var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
			if (shader == null || fallbackColors == null || fallbackColors.Length == 0) return Array.Empty<Material>();
			_generatedMaterials = new Material[fallbackColors.Length];
			for (var index = 0; index < fallbackColors.Length; index++) {
				var material = new Material(shader) {
					name = $"Cylinder Flythrough Color {index + 1}",
					hideFlags = HideFlags.HideAndDontSave
				};
				if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", fallbackColors[index]);
				if (material.HasProperty("_Color")) material.SetColor("_Color", fallbackColors[index]);
				if (material.HasProperty("_EmissionColor")) {
					material.SetColor("_EmissionColor", fallbackColors[index] * 0.45f);
					material.EnableKeyword("_EMISSION");
				}
				_generatedMaterials[index] = material;
			}
			return _generatedMaterials;
		}

		private void ReleaseGeneratedContent() {
			if (_generatedRoot != null) DestroyOwnedObject(_generatedRoot.gameObject);
			_generatedRoot = null;
			for (var index = 0; index < _generatedMaterials.Length; index++)
				if (_generatedMaterials[index] != null) DestroyOwnedObject(_generatedMaterials[index]);
			_generatedMaterials = Array.Empty<Material>();
		}

		private static float NextFloat(System.Random random, float minimum, float maximum) {
			return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
		}

		private static void DestroyOwnedObject(UnityEngine.Object value) {
			if (value == null) return;
			if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
		}
	}
}
