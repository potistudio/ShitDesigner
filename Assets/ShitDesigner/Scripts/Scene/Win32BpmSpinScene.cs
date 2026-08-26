using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Animates a scattered Win32 icon field from the scene graph clock.</summary>
	[DisallowMultipleComponent]
	public sealed class Win32BpmSpinScene : MonoBehaviour, ISceneGraphClockReceiver {
		[SerializeField] private Texture2D[] _images = Array.Empty<Texture2D>();
		[Min(1f)][SerializeField] private float _bpm = 138f;
		[SerializeField] private Vector2 _spread = new Vector2(10f, 5.5f);
		[Min(0.1f)][SerializeField] private float _iconSize = .78f;

		private readonly List<IconState> _icons = new List<IconState>();
		private MaterialPropertyBlock _propertyBlock;
		private Transform _generatedRoot;
		private Material _material;
		private double _elapsedSeconds;
		private bool _graphClockDriven;

		private sealed class IconState {
			public Transform Transform { get; }
			public Vector3 Position { get; }
			public float BaseScale { get; }
			public float BaseAngle { get; }
			public float Aspect { get; }
			public float Phase { get; }

			public IconState(Transform transform, Vector3 position, float baseScale, float baseAngle, float aspect, float phase) {
				Transform = transform;
				Position = position;
				BaseScale = baseScale;
				BaseAngle = baseAngle;
				Aspect = aspect;
				Phase = phase;
			}
		}

		private void Awake() {
			_propertyBlock = new MaterialPropertyBlock();
		}

		private void OnEnable() {
			Rebuild();
		}

		private void Update() {
			if (!_graphClockDriven && Application.isPlaying) Advance(Time.deltaTime);
		}

		private void OnDisable() {
			ReleaseGeneratedContent();
		}

		private void OnDestroy() {
			ReleaseGeneratedContent();
		}

		private void OnValidate() {
			_bpm = Mathf.Max(1f, _bpm);
			_spread.x = Mathf.Max(.1f, _spread.x);
			_spread.y = Mathf.Max(.1f, _spread.y);
			_iconSize = Mathf.Max(.1f, _iconSize);
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			_graphClockDriven = graphClockDriven;
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!_graphClockDriven || deltaSeconds <= 0d) return;
			Advance((float)Math.Min(deltaSeconds, float.MaxValue));
		}

		[ContextMenu("Rebuild Icon Scatter")]
		public void Rebuild() {
			ReleaseGeneratedContent();
			if (_images == null || _images.Length == 0) return;
			_propertyBlock ??= new MaterialPropertyBlock();

			_generatedRoot = new GameObject("Generated Win32 Icon Scatter").transform;
			_generatedRoot.SetParent(transform, false);
			_generatedRoot.gameObject.layer = gameObject.layer;
			_generatedRoot.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;

			var count = 0;
			for (var index = 0; index < _images.Length; index++)
				if (_images[index] != null) count++;
			if (count == 0) return;
			_material = CreateMaterial();
			var random = new System.Random(214748);

			var itemIndex = 0;
			for (var index = 0; index < _images.Length; index++) {
				var image = _images[index];
				if (image == null) continue;
				var icon = GameObject.CreatePrimitive(PrimitiveType.Quad);
				icon.name = "Win32 Icon " + (itemIndex + 1).ToString("00");
				icon.transform.SetParent(_generatedRoot, false);
				icon.layer = gameObject.layer;
				icon.hideFlags = HideFlags.DontSave;
				var collider = icon.GetComponent<Collider>();
				if (collider != null) DestroyOwnedObject(collider);
				var renderer = icon.GetComponent<MeshRenderer>();
				renderer.sharedMaterial = _material;
				SetTexture(renderer, image);
				var position = new Vector3(NextFloat(random, -_spread.x, _spread.x), NextFloat(random, -_spread.y, _spread.y), NextFloat(random, -.5f, .5f));
				var scale = _iconSize * NextFloat(random, .5f, 1.5f);
				var aspect = image.height == 0 ? 1f : (float)image.width / image.height;
				_icons.Add(new IconState(icon.transform, position, scale, NextFloat(random, 0f, 360f), aspect, NextFloat(random, 0f, Mathf.PI * 2f)));
				itemIndex++;
			}
			ApplyAnimation();
		}

		private void Advance(float deltaSeconds) {
			_elapsedSeconds += deltaSeconds;
			ApplyAnimation();
		}

		private void ApplyAnimation() {
			if (_icons.Count == 0) return;
			var beats = (float)(_elapsedSeconds * _bpm / 60d);
			var beatPhase = Mathf.Repeat(beats, 1f);
			var pulse = Mathf.Pow(1f - beatPhase, 4f);
			_generatedRoot.localRotation = Quaternion.Euler(0f, 0f,
				Mathf.Sin(beats * Mathf.PI * .5f) * 12f + Mathf.Sin(beats * Mathf.PI * 2f) * 4f);
			for (var index = 0; index < _icons.Count; index++) {
				var icon = _icons[index];
				var wave = Mathf.Sin(beats * Mathf.PI * 2f + icon.Phase);
				icon.Transform.localPosition = icon.Position + new Vector3(wave * .18f, Mathf.Cos(beats * Mathf.PI * 2f + icon.Phase) * .12f, 0f);
				icon.Transform.localRotation = Quaternion.Euler(0f, 0f,
					icon.BaseAngle + wave * (35f + pulse * 25f) + Mathf.Sin(beats * Mathf.PI + icon.Phase) * 20f);
				var scale = icon.BaseScale * (1f + pulse * .28f);
				icon.Transform.localScale = new Vector3(scale * icon.Aspect, scale, 1f);
			}
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
			_icons.Clear();
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
