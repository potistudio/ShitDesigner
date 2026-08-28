using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Maintains a randomized field of objects that continuously falls through the scene.</summary>
	[DisallowMultipleComponent]
	public sealed class FallingObjectShower : MonoBehaviour, ISceneGraphClockReceiver {
		[Header("Object")]
		[SerializeField] private GameObject[] objectPrefabs = Array.Empty<GameObject>();
		[SerializeField, Min(1)] private int objectCount = 180;
		[SerializeField] private Vector2 objectScaleRange = new Vector2(0.65f, 1.15f);
		[SerializeField] private int randomSeed = 2718;

		[Header("Spawn area")]
		[SerializeField] private Vector3 spawnCenter = new Vector3(0f, 2f, -7f);
		[SerializeField] private Vector3 spawnExtents = new Vector3(7f, 7f, 6f);

		[Header("Motion")]
		[SerializeField] private Vector2 fallSpeedRange = new Vector2(0.35f, 0.75f);
		[SerializeField] private Vector2 rotationSpeedRange = new Vector2(15f, 50f);

		private readonly List<FallingObject> _objects = new List<FallingObject>();
		private System.Random _random;
		private bool _graphClockDriven;

		private void OnEnable() {
			CreateObjects();
		}

		private void OnDisable() {
			ReleaseObjects();
		}

		private void Update() {
			if (Application.isPlaying && !_graphClockDriven)
				MoveObjects(Time.deltaTime);
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			_graphClockDriven = graphClockDriven;
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!_graphClockDriven || deltaSeconds <= 0d)
				return;

			MoveObjects((float)Math.Min(deltaSeconds, float.MaxValue));
		}

		public void Rearrange() {
			if (_objects.Count == 0) {
				CreateObjects();
				return;
			}

			_random ??= new System.Random(randomSeed);
			foreach (var fallingObject in _objects)
				if (fallingObject.Transform != null)
					ResetPosition(fallingObject, true);
		}

		private void OnValidate() {
			objectCount = Mathf.Clamp(objectCount, 1, 1000);
			objectScaleRange.x = Mathf.Max(0.01f, objectScaleRange.x);
			objectScaleRange.y = Mathf.Max(objectScaleRange.x, objectScaleRange.y);
			spawnExtents.x = Mathf.Max(0f, spawnExtents.x);
			spawnExtents.y = Mathf.Max(0.01f, spawnExtents.y);
			spawnExtents.z = Mathf.Max(0f, spawnExtents.z);
			fallSpeedRange.x = Mathf.Max(0f, fallSpeedRange.x);
			fallSpeedRange.y = Mathf.Max(fallSpeedRange.x, fallSpeedRange.y);
			rotationSpeedRange.x = Mathf.Max(0f, rotationSpeedRange.x);
			rotationSpeedRange.y = Mathf.Max(rotationSpeedRange.x, rotationSpeedRange.y);
		}

		private void CreateObjects() {
			ReleaseObjects();
			var prefabCount = GetPrefabCount();
			if (prefabCount == 0)
				return;

			_random = new System.Random(randomSeed);
			for (var index = 0; index < objectCount; index++) {
				var item = Instantiate(GetPrefab(_random.Next(prefabCount)), transform);
				item.name = $"Falling Object {index + 1:000}";
				SetLayerRecursively(item, gameObject.layer);
				(item.GetComponent<ShowerMonochromeApplicator>() ?? item.AddComponent<ShowerMonochromeApplicator>()).SetMonochromeEnabled(false);
				var fallingObject = new FallingObject(item.transform, NextFloat(fallSpeedRange.x, fallSpeedRange.y));
				ResetPosition(fallingObject, true);
				_objects.Add(fallingObject);
			}
		}

		private int GetPrefabCount() {
			if (objectPrefabs == null)
				return 0;

			var count = 0;
			foreach (var prefab in objectPrefabs)
				if (prefab != null)
					count++;
			return count;
		}

		private GameObject GetPrefab(int index) {
			var found = 0;
			foreach (var prefab in objectPrefabs) {
				if (prefab == null)
					continue;
				if (found == index)
					return prefab;
				found++;
			}
			return null;
		}

		private void MoveObjects(float deltaTime) {
			if (deltaTime <= 0f)
				return;

			var bottom = spawnCenter.y - spawnExtents.y;
			foreach (var fallingObject in _objects) {
				if (fallingObject.Transform == null)
					continue;

				var position = fallingObject.Transform.localPosition;
				position.y -= fallingObject.Speed * deltaTime;
				fallingObject.Transform.localPosition = position;
				fallingObject.Transform.localRotation = Quaternion.Euler(fallingObject.RotationSpeed * deltaTime)
					* fallingObject.Transform.localRotation;
				if (position.y < bottom)
					ResetPosition(fallingObject, false);
			}
		}

		private void ResetPosition(FallingObject fallingObject, bool randomizeHeight) {
			var position = spawnCenter;
			position.x += NextFloat(-spawnExtents.x, spawnExtents.x);
			position.y += randomizeHeight
				? NextFloat(-spawnExtents.y, spawnExtents.y)
				: spawnExtents.y;
			position.z += NextFloat(-spawnExtents.z, spawnExtents.z);
			fallingObject.Transform.localPosition = position;
			fallingObject.Transform.localRotation = Quaternion.Euler(
				NextFloat(0f, 360f),
				NextFloat(0f, 360f),
				NextFloat(0f, 360f));
			fallingObject.Transform.localScale = Vector3.one * NextFloat(objectScaleRange.x, objectScaleRange.y);
			fallingObject.Speed = NextFloat(fallSpeedRange.x, fallSpeedRange.y);
			fallingObject.RotationSpeed = GetRotationSpeed();
		}

		private void ReleaseObjects() {
			foreach (var fallingObject in _objects)
				if (fallingObject.Transform != null)
					DestroyOwnedObject(fallingObject.Transform.gameObject);
			_objects.Clear();
		}

		private float NextFloat(float minimum, float maximum) {
			return Mathf.Lerp(minimum, maximum, (float)_random.NextDouble());
		}

		private Vector3 GetRotationSpeed() {
			var axis = new Vector3(NextFloat(-1f, 1f), NextFloat(-1f, 1f), NextFloat(-1f, 1f));
			if (axis.sqrMagnitude < 0.0001f)
				axis = Vector3.up;
			return axis.normalized * NextFloat(rotationSpeedRange.x, rotationSpeedRange.y);
		}

		private static void SetLayerRecursively(GameObject root, int layer) {
			foreach (var item in root.GetComponentsInChildren<Transform>(true))
				item.gameObject.layer = layer;
		}

		private static void DestroyOwnedObject(UnityEngine.Object value) {
			if (Application.isPlaying)
				Destroy(value);
			else
				DestroyImmediate(value);
		}

		private sealed class FallingObject {
			public FallingObject(Transform transform, float speed) {
				Transform = transform;
				Speed = speed;
			}

			public Transform Transform { get; }
			public float Speed { get; set; }
			public Vector3 RotationSpeed { get; set; }
		}
	}
}
