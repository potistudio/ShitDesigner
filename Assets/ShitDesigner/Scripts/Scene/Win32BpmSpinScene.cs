using System;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Rotates one scattered Win32 icon field from the scene graph clock.</summary>
	[DisallowMultipleComponent]
	public sealed class Win32BpmSpinScene : MonoBehaviour, ISceneGraphClockReceiver {
		[SerializeField] private Texture2D[] _images = Array.Empty<Texture2D>();
		[Min(1f)][SerializeField] private float _bpm = 138f;
		[Min(.1f)][SerializeField] private float _sphereRadius = 5.2f;
		[Min(.1f)][SerializeField] private float _iconSize = .62f;

		private MaterialPropertyBlock _propertyBlock;
		private Transform _generatedRoot;
		private Material _material;
		private System.Random _random;
		private Quaternion _rotationStart;
		private Quaternion _rotationTarget;
		private double _elapsedSeconds;
		private int _nextBeat;
		private bool _graphClockDriven;

		private void Awake() {
			_propertyBlock = new MaterialPropertyBlock();
			_random = new System.Random(94827);
		}

		private void OnEnable() {
			Rebuild();
		}

		private void OnDisable() {
			ReleaseGeneratedContent();
		}

		private void OnDestroy() {
			ReleaseGeneratedContent();
		}

		private void OnValidate() {
			_bpm = Mathf.Max(1f, _bpm);
			_sphereRadius = Mathf.Max(.1f, _sphereRadius);
			_iconSize = Mathf.Max(.1f, _iconSize);
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			_graphClockDriven = graphClockDriven;
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!_graphClockDriven || deltaSeconds <= 0d) return;
			Advance(deltaSeconds);
		}

		[ContextMenu("Rebuild Icon Sphere")]
		public void Rebuild() {
			ReleaseGeneratedContent();
			if (_images == null || _images.Length == 0) return;
			_propertyBlock ??= new MaterialPropertyBlock();
			_random ??= new System.Random(94827);

			_generatedRoot = new GameObject("Win32 Icon Sphere").transform;
			_generatedRoot.SetParent(transform, false);
			_generatedRoot.gameObject.layer = gameObject.layer;
			_generatedRoot.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
			_material = CreateMaterial();

			var itemIndex = 0;
			for (var index = 0; index < _images.Length; index++) {
				var image = _images[index];
				if (image == null) continue;
				var icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
				icon.name = "Win32 Icon " + (itemIndex + 1).ToString("000");
				icon.transform.SetParent(_generatedRoot, false);
				icon.layer = gameObject.layer;
				icon.hideFlags = HideFlags.DontSave;
				var collider = icon.GetComponent<Collider>();
				if (collider != null) DestroyOwnedObject(collider);
				var renderer = icon.GetComponent<MeshRenderer>();
				renderer.sharedMaterial = _material;
				SetTexture(renderer, image);
				var scale = _iconSize * NextFloat(_random, .55f, 1.45f);
				var aspect = image.height == 0 ? 1f : (float)image.width / image.height;
				icon.transform.localPosition = RandomPointInSphere(_random, _sphereRadius);
				icon.transform.localScale = new Vector3(scale * aspect, scale, 1f);
				itemIndex++;
			}

			_rotationStart = Quaternion.identity;
			_rotationTarget = Quaternion.identity;
			_nextBeat = 0;
			ApplyAnimation();
		}

		private void Advance(double deltaSeconds) {
			_elapsedSeconds += deltaSeconds;
			ApplyAnimation();
		}

		private void ApplyAnimation() {
			if (_generatedRoot == null) return;
			var beats = (float)(_elapsedSeconds * _bpm / 60d);
			var currentBeat = Mathf.FloorToInt(beats);
			if (currentBeat >= _nextBeat) {
				_rotationStart = _generatedRoot.localRotation;
				_rotationTarget = RandomRotation(_random);
				_nextBeat = currentBeat + 1;
			}

			var phase = Mathf.Repeat(beats, 1f);
			var easedPhase = 1f - Mathf.Pow(1f - phase, 3f);
			_generatedRoot.localRotation = Quaternion.Slerp(_rotationStart, _rotationTarget, easedPhase);
		}

		private static Quaternion RandomRotation(System.Random random) {
			return Quaternion.Euler(
				NextFloat(random, -46f, 46f),
				NextFloat(random, -46f, 46f),
				NextFloat(random, -32f, 32f));
		}

		private static Vector3 RandomPointInSphere(System.Random random, float radius) {
			Vector3 point;
			do {
				point = new Vector3(NextFloat(random, -1f, 1f), NextFloat(random, -1f, 1f), NextFloat(random, -1f, 1f));
			} while (point.sqrMagnitude > 1f);
			return point * radius;
		}

		private Material CreateMaterial() {
			var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
			if (shader == null) throw new InvalidOperationException("An unlit shader is required for the Win32 icon scene.");
			var material = new Material(shader) {
				name = "Win32 BPM Icons",
				hideFlags = HideFlags.HideAndDontSave,
				renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent
			};
			if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
			if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
			if (material.HasProperty("_Surface")) {
				material.SetFloat("_Surface", 1f);
				material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
			}
			if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
			if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
			if (material.HasProperty("_ZWrite")) material.SetFloat("_ZWrite", 0f);
			material.SetOverrideTag("RenderType", "Transparent");
			return material;
		}

		private void SetTexture(Renderer renderer, Texture2D image) {
			_propertyBlock.Clear();
			_propertyBlock.SetTexture("_BaseMap", image);
			_propertyBlock.SetTexture("_MainTex", image);
			renderer.SetPropertyBlock(_propertyBlock);
		}

		private void ReleaseGeneratedContent() {
			if (_generatedRoot != null) DestroyOwnedObject(_generatedRoot.gameObject);
			_generatedRoot = null;
			if (_material != null) DestroyOwnedObject(_material);
			_material = null;
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
