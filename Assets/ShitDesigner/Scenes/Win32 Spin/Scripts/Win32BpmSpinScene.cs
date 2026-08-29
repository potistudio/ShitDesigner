using System;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Rotates one scattered Win32 icon field from the shared BPM clock.</summary>
	[DisallowMultipleComponent]
	public sealed class Win32BpmSpinScene : MonoBehaviour, IBpmClockReceiver {
		[SerializeField] private Texture2D[] _images = Array.Empty<Texture2D>();
		[Min(.1f)][SerializeField] private float _sphereRadius = 5.2f;
		[Min(.1f)][SerializeField] private float _iconSize = .62f;
		[Min(1)][SerializeField] private int m_Count = 1000;

		private MaterialPropertyBlock _propertyBlock;
		private Transform _generatedRoot;
		private Material _material;
		private System.Random _random;
		private Quaternion _rotationStart;
		private Vector3 _rotationAxis;
		private float _rotationDegrees;
		private float _rotationStartBeat;
		private double m_AdjustedTotalBeats;
		private int _nextRotationIndex;

		private const int BeatsPerRotation = 2;

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
			m_Count = Mathf.Max(1, m_Count);
			_sphereRadius = Mathf.Max(.1f, _sphereRadius);
			_iconSize = Mathf.Max(.1f, _iconSize);
		}

		public void SetBpmClock(BeatClockFrame frame) {
			m_AdjustedTotalBeats = frame.AdjustedTotalBeats;
			ApplyAnimation();
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

			var images = Array.FindAll(_images, image => image != null);
			if (images.Length == 0) return;
			for (var index = 0; index < m_Count; index++) {
				var image = images[_random.Next(images.Length)];
				var icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
				icon.name = "Win32 Icon " + (index + 1).ToString("000");
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
				icon.transform.localRotation = Quaternion.AngleAxis(NextFloat(_random, 0f, 360f), RandomAxis(_random));
				icon.transform.localScale = new Vector3(scale * aspect, scale, 1f);
			}

			_rotationStart = Quaternion.identity;
			_rotationAxis = Vector3.forward;
			_rotationDegrees = 0f;
			_rotationStartBeat = 0f;
			_nextRotationIndex = 0;
			ApplyAnimation();
		}

		private void ApplyAnimation() {
			if (_generatedRoot == null) return;
			var beats = (float)m_AdjustedTotalBeats;
			var currentRotationIndex = Mathf.FloorToInt(beats / BeatsPerRotation);
			if (currentRotationIndex >= _nextRotationIndex) {
				_rotationStart = _generatedRoot.localRotation;
				_rotationAxis = RandomAxis(_random);
				_rotationDegrees = NextFloat(_random, 400f, 720f) * (_random.Next(2) == 0 ? -1f : 1f);
				_rotationStartBeat = currentRotationIndex * BeatsPerRotation;
				_nextRotationIndex = currentRotationIndex + 1;
			}

			var phase = Mathf.Clamp01((beats - _rotationStartBeat) / BeatsPerRotation);
			var easedPhase = 1f - Mathf.Pow(1f - phase, 3f);
			_generatedRoot.localRotation = _rotationStart * Quaternion.AngleAxis(_rotationDegrees * easedPhase, _rotationAxis);
		}

		private static Vector3 RandomAxis(System.Random random) {
			Vector3 axis;
			do {
				axis = new Vector3(NextFloat(random, -1f, 1f), NextFloat(random, -1f, 1f), NextFloat(random, -1f, 1f));
			} while (axis.sqrMagnitude < .01f);
			return axis.normalized;
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
			if (UnityEngine.Application.isPlaying) Destroy(value); else DestroyImmediate(value);
		}
	}
}
