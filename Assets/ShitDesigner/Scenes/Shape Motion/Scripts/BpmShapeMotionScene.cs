using System;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Animates one centered 2D shape from the shared BPM clock.</summary>
	[DisallowMultipleComponent]
	public sealed class BpmShapeMotionScene : MonoBehaviour, IBpmClockReceiver {
		[Range(30f, 300f)][SerializeField] private float _previewBpm = 138f;
		[SerializeField] private AnimationCurve _easing = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

		private Material _material;
		private MaterialPropertyBlock _propertyBlock;
		private Mesh _shapeMesh;
		private Vector3[] _outlineVertices = Array.Empty<Vector3>();
		private int _outlineSides;
		private float _outlineAngleOffset;
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
			_shapeMesh = new Mesh { name = "BPM Shape Outline", hideFlags = HideFlags.HideAndDontSave };
			_shapeMesh.MarkDynamic();
			_shape = new GameObject("BPM Shape").transform;
			_shape.SetParent(transform, false);
			_shape.gameObject.layer = gameObject.layer;
			_shape.gameObject.hideFlags = HideFlags.HideInHierarchy | HideFlags.DontSave;
			_shapeFilter = _shape.gameObject.AddComponent<MeshFilter>();
			_shapeFilter.sharedMesh = _shapeMesh;
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
			var progress = _easing == null || _easing.length == 0 ? phase : Mathf.Clamp01(_easing.Evaluate(phase));
			var size = _targetSize * Mathf.Lerp(.85f, 1f, progress);
			_shape.localPosition = Vector3.zero;
			_shape.localRotation = Quaternion.Euler(0f, 0f, _startAngle + _rotation * progress);
			_shape.localScale = Vector3.one * size;
			UpdateOutlineVertices(Mathf.Lerp(.08f, .012f, progress));
		}

		private void ConfigureBeat(long beat) {
			_configuredBeat = beat;
			var random = new System.Random(unchecked((int)(beat * 7919L + 1979L)));
			_targetSize = NextFloat(random, 3f, 5f);
			_startAngle = NextFloat(random, 0f, 360f);
			_rotation = NextFloat(random, 15f, 60f) * (random.Next(2) == 0 ? -1f : 1f);
			var shape = random.Next(3);
			ConfigureOutline(shape == 0 ? 3 : shape == 1 ? 4 : 32, shape == 0 ? 90f : shape == 1 ? 45f : 0f);
		}

		private void ConfigureOutline(int sides, float angleOffset) {
			_outlineSides = sides;
			_outlineAngleOffset = angleOffset;
			_outlineVertices = new Vector3[sides * 2];
			var triangles = new int[sides * 6];
			for (var index = 0; index < sides; index++) {
				var next = (index + 1) % sides;
				var triangle = index * 6;
				triangles[triangle] = index;
				triangles[triangle + 1] = next + sides;
				triangles[triangle + 2] = next;
				triangles[triangle + 3] = index;
				triangles[triangle + 4] = index + sides;
				triangles[triangle + 5] = next + sides;
			}
			_shapeMesh.Clear();
			UpdateOutlineVertices(.08f);
			_shapeMesh.triangles = triangles;
		}

		private void UpdateOutlineVertices(float thickness) {
			var innerRadius = .5f - thickness;
			for (var index = 0; index < _outlineSides; index++) {
				var radians = (_outlineAngleOffset + index * 360f / _outlineSides) * Mathf.Deg2Rad;
				var direction = new Vector3(Mathf.Cos(radians), Mathf.Sin(radians), 0f);
				_outlineVertices[index] = direction * .5f;
				_outlineVertices[index + _outlineSides] = direction * innerRadius;
			}
			_shapeMesh.vertices = _outlineVertices;
			_shapeMesh.bounds = new Bounds(Vector3.zero, Vector3.one);
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

		private void ReleaseGeneratedContent() {
			if (_shape != null) DestroyOwnedObject(_shape.gameObject);
			_shape = null;
			_shapeFilter = null;
			_shapeRenderer = null;
			if (_material != null) DestroyOwnedObject(_material);
			_material = null;
			if (_shapeMesh != null) DestroyOwnedObject(_shapeMesh);
			_shapeMesh = null;
			_outlineVertices = Array.Empty<Vector3>();
			_outlineSides = 0;
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
