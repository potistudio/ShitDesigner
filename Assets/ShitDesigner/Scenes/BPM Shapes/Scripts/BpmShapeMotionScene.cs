using System;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Animates one centered 2D shape from the shared BPM clock.</summary>
	[DisallowMultipleComponent]
	public sealed class BpmShapeMotionScene : MonoBehaviour, IBpmClockReceiver {
		[Range(30f, 300f)][SerializeField] private float _previewBpm = 138f;

		private Material _material;
		private MaterialPropertyBlock _propertyBlock;
		private Mesh[] _meshes = Array.Empty<Mesh>();
		private Transform _shape;
		private MeshFilter _shapeFilter;
		private MeshRenderer _shapeRenderer;
		private float _targetSize;
		private float _startAngle;
		private float _rotation;
		private double _totalBeats;
		private long _configuredBeat = long.MinValue;
		private bool _usesExternalClock;

		private void OnEnable() {
			Rebuild();
		}

		private void Update() {
			if (_usesExternalClock) return;
			_totalBeats += Time.unscaledDeltaTime * _previewBpm / 60d;
			ApplyAnimation();
		}

		private void OnDisable() {
			ReleaseGeneratedContent();
		}

		private void OnDestroy() {
			ReleaseGeneratedContent();
		}

		private void OnValidate() {
			_previewBpm = Mathf.Clamp(_previewBpm, 30f, 300f);
		}

		public void SetBpmClock(BpmClockState clock) {
			_usesExternalClock = true;
			_totalBeats = clock.TotalBeats;
			ApplyAnimation();
		}

		[ContextMenu("Rebuild Shape")]
		public void Rebuild() {
			ReleaseGeneratedContent();
			_material = CreateMaterial();
			_propertyBlock = new MaterialPropertyBlock();
			_meshes = new[] { CreatePolygonMesh("Triangle", 3, 90f), CreateQuadMesh(), CreatePolygonMesh("Circle", 32, 0f) };
			_shape = new GameObject("BPM Shape").transform;
			_shape.SetParent(transform, false);
			_shape.gameObject.layer = gameObject.layer;
			_shape.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
			_shapeFilter = _shape.gameObject.AddComponent<MeshFilter>();
			_shapeRenderer = _shape.gameObject.AddComponent<MeshRenderer>();
			_shapeRenderer.sharedMaterial = _material;
			SetColor(_shapeRenderer, Color.white);
			_configuredBeat = long.MinValue;
			ApplyAnimation();
		}

		private void ApplyAnimation() {
			if (_shape == null) return;
			var beat = (long)Math.Floor(_totalBeats);
			if (beat != _configuredBeat) ConfigureBeat(beat);

			var phase = Mathf.Clamp01((float)(_totalBeats - beat));
			var progress = phase * phase * (3f - 2f * phase);
			var size = _targetSize * Mathf.Lerp(.85f, 1f, progress);
			var width = size * Mathf.Lerp(1f, .65f, progress);
			_shape.localPosition = Vector3.zero;
			_shape.localRotation = Quaternion.Euler(0f, 0f, _startAngle + _rotation * progress);
			_shape.localScale = new Vector3(width, size, 1f);
		}

		private void ConfigureBeat(long beat) {
			_configuredBeat = beat;
			var random = new System.Random(unchecked((int)(beat * 7919L + 1979L)));
			_targetSize = NextFloat(random, 3f, 5f);
			_startAngle = NextFloat(random, 0f, 360f);
			_rotation = NextFloat(random, 15f, 60f) * (random.Next(2) == 0 ? -1f : 1f);
			_shapeFilter.sharedMesh = _meshes[random.Next(_meshes.Length)];
		}

		private void SetColor(Renderer renderer, Color color) {
			_propertyBlock.Clear();
			_propertyBlock.SetColor("_BaseColor", color);
			_propertyBlock.SetColor("_Color", color);
			renderer.SetPropertyBlock(_propertyBlock);
		}

		private static Material CreateMaterial() {
			var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
			if (shader == null) throw new InvalidOperationException("An unlit shader is required for the BPM shape scene.");
			return new Material(shader) { name = "BPM Shapes", hideFlags = HideFlags.HideAndDontSave };
		}

		private static Mesh CreateQuadMesh() {
			var mesh = new Mesh { name = "Square", hideFlags = HideFlags.HideAndDontSave };
			mesh.vertices = new[] { new Vector3(-.5f, -.5f), new Vector3(.5f, -.5f), new Vector3(.5f, .5f), new Vector3(-.5f, .5f) };
			mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
			mesh.RecalculateBounds();
			return mesh;
		}

		private static Mesh CreatePolygonMesh(string name, int sides, float angleOffset) {
			var vertices = new Vector3[sides + 1];
			var triangles = new int[sides * 3];
			for (var index = 0; index < sides; index++) {
				var radians = (angleOffset + index * 360f / sides) * Mathf.Deg2Rad;
				vertices[index + 1] = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f) * .5f;
				var triangle = index * 3;
				triangles[triangle] = 0;
				triangles[triangle + 1] = (index + 1) % sides + 1;
				triangles[triangle + 2] = index + 1;
			}
			var mesh = new Mesh { name = name, hideFlags = HideFlags.HideAndDontSave, vertices = vertices, triangles = triangles };
			mesh.RecalculateBounds();
			return mesh;
		}

		private void ReleaseGeneratedContent() {
			if (_shape != null) DestroyOwnedObject(_shape.gameObject);
			_shape = null;
			_shapeFilter = null;
			_shapeRenderer = null;
			if (_material != null) DestroyOwnedObject(_material);
			_material = null;
			foreach (var mesh in _meshes) if (mesh != null) DestroyOwnedObject(mesh);
			_meshes = Array.Empty<Mesh>();
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
