using System;
using System.Collections.Generic;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Animates selected Win32 icons around a BPM-locked circular layout.</summary>
	[DisallowMultipleComponent]
	public sealed class Win32BpmSpinScene : MonoBehaviour, ISceneGraphClockReceiver {
		[SerializeField] private Texture2D[] _images = Array.Empty<Texture2D>();
		[Min(1f)][SerializeField] private float _bpm = 138f;
		[Min(0.1f)][SerializeField] private float _ringRadius = 3.8f;
		[Min(0.1f)][SerializeField] private float _iconSize = 1.45f;
		[Min(0f)][SerializeField] private float _spinsPerBeat = 1.75f;

		private readonly List<IconState> _icons = new List<IconState>();
		private readonly List<Material> _materials = new List<Material>();
		private Transform _generatedRoot;
		private double _elapsedSeconds;
		private bool _graphClockDriven;

		private sealed class IconState {
			public Transform Transform { get; }
			public Vector3 Position { get; }
			public float BaseScale { get; }
			public float BaseAngle { get; }
			public float Direction { get; }

			public IconState(Transform transform, Vector3 position, float baseScale, float baseAngle, float direction) {
				Transform = transform;
				Position = position;
				BaseScale = baseScale;
				BaseAngle = baseAngle;
				Direction = direction;
			}
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
			_ringRadius = Mathf.Max(.1f, _ringRadius);
			_iconSize = Mathf.Max(.1f, _iconSize);
			_spinsPerBeat = Mathf.Max(0f, _spinsPerBeat);
		}

		public void SetGraphClockDriven(bool graphClockDriven) {
			_graphClockDriven = graphClockDriven;
		}

		public void AdvanceGraphClock(double deltaSeconds) {
			if (!_graphClockDriven || deltaSeconds <= 0d) return;
			Advance((float)Math.Min(deltaSeconds, float.MaxValue));
		}

		[ContextMenu("Rebuild Icon Ring")]
		public void Rebuild() {
			ReleaseGeneratedContent();
			if (_images == null || _images.Length == 0) return;

			_generatedRoot = new GameObject("Generated Win32 Icon Ring").transform;
			_generatedRoot.SetParent(transform, false);
			_generatedRoot.gameObject.layer = gameObject.layer;
			_generatedRoot.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;

			var count = 0;
			for (var index = 0; index < _images.Length; index++)
				if (_images[index] != null) count++;
			if (count == 0) return;

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
				renderer.sharedMaterial = CreateMaterial(image, itemIndex);
				var angle = itemIndex * Mathf.PI * 2f / count;
				var radius = _ringRadius + (itemIndex % 3 - 1) * .35f;
				var position = new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, itemIndex * -.01f);
				var scale = _iconSize * Mathf.Lerp(.8f, 1.2f, (itemIndex % 4) / 3f);
				_icons.Add(new IconState(icon.transform, position, scale, itemIndex * 29f, itemIndex % 2 == 0 ? 1f : -1f));
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
			var pulse = Mathf.Pow(1f - beatPhase, 5f);
			_generatedRoot.localRotation = Quaternion.Euler(0f, 0f, beats * 18f + pulse * 12f);
			for (var index = 0; index < _icons.Count; index++) {
				var icon = _icons[index];
				var orbit = Mathf.Sin(beats * Mathf.PI * 2f + index * .9f) * .14f;
				icon.Transform.localPosition = icon.Position + icon.Position.normalized * orbit;
				icon.Transform.localRotation = Quaternion.Euler(0f, 0f,
					icon.BaseAngle + icon.Direction * beats * 360f * _spinsPerBeat + pulse * icon.Direction * 90f);
				icon.Transform.localScale = Vector3.one * icon.BaseScale * (1f + pulse * .42f);
			}
		}

		private Material CreateMaterial(Texture2D image, int index) {
			var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
			if (shader == null) throw new InvalidOperationException("An unlit shader is required for the Win32 icon scene.");
			var material = new Material(shader) {
				name = "Win32 BPM Icon " + (index + 1).ToString("00"),
				hideFlags = HideFlags.HideAndDontSave,
				renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent
			};
			if (material.HasProperty("_BaseMap")) material.SetTexture("_BaseMap", image);
			if (material.HasProperty("_MainTex")) material.SetTexture("_MainTex", image);
			if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", Color.white);
			if (material.HasProperty("_Color")) material.SetColor("_Color", Color.white);
			if (material.HasProperty("_Surface")) material.SetFloat("_Surface", 1f);
			if (material.HasProperty("_SrcBlend")) material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
			if (material.HasProperty("_DstBlend")) material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
			material.SetOverrideTag("RenderType", "Transparent");
			_materials.Add(material);
			return material;
		}

		private void ReleaseGeneratedContent() {
			if (_generatedRoot != null) DestroyOwnedObject(_generatedRoot.gameObject);
			_generatedRoot = null;
			_icons.Clear();
			for (var index = 0; index < _materials.Count; index++)
				if (_materials[index] != null) DestroyOwnedObject(_materials[index]);
			_materials.Clear();
		}

		private static void DestroyOwnedObject(UnityEngine.Object value) {
			if (value == null) return;
			if (Application.isPlaying) Destroy(value); else DestroyImmediate(value);
		}
	}
}
