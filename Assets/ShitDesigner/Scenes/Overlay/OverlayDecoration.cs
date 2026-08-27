using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShitDesigner.Presentation.Overlay {
	[RequireComponent(typeof(PanelRenderer))]
	[DefaultExecutionOrder(1100)]
	public sealed class OverlayDecoration : MonoBehaviour {
		[SerializeField] private PanelRenderer _panelRenderer;
		[SerializeField, Min(0f)] private float _edgeInset = 32f;
		[SerializeField, Min(1f)] private float _cornerSize = 112f;
		[SerializeField, Min(0.1f)] private float _cornerLineThickness = 1f;
		[SerializeField, Min(0f)] private float _innerCornerInset = 12f;
		[SerializeField, Min(0.1f)] private float _innerCornerLength = 44f;
		[SerializeField, Min(0.1f)] private float _cornerNodeSize = 5f;
		[SerializeField, Min(0.1f)] private float _cornerNodeOffset = 3f;
		[SerializeField, Min(0f)] private float _cornerNodeRadius = 3f;
		[SerializeField, Min(1f)] private float _stripSize = 176f;
		[SerializeField, Min(0.1f)] private float _stripHeight = 5f;
		[SerializeField, Range(0f, 1f)] private float _stripOpacity = 0.62f;
		[SerializeField, Min(1)] private int _circularStrokeCount = 24;
		[SerializeField, Min(1f)] private float _circularStrokeRadius = 132f;
		[SerializeField, Min(0.1f)] private float _circularStrokeLength = 14f;
		[SerializeField, Min(0.1f)] private float _circularStrokeThickness = 2f;
		[SerializeField, Range(0f, 1f)] private float _circularStrokeOpacity = 0.54f;
		[SerializeField] private float _circularStrokeStartAngle = -90f;
		[SerializeField, Min(0f)] private float _circularStrokeRotationMaxSpeed = 3f;
		[SerializeField, Min(0f)] private float _circularStrokeRotationNoiseFrequency = 0.15f;
		[SerializeField, Min(1)] private int _accentStrokeInterval = 6;
		[SerializeField] private Color _lineColor = new Color(0.91f, 0.93f, 0.95f, 0.74f);
		[SerializeField] private Color _accentColor = new Color(0.34f, 0.65f, 1f, 0.68f);

		private VisualElement _decorationRoot;
		private VisualElement _circularStrokeRoot;
		private Coroutine _reloadRoutine;
		private float _circularStrokeRotation;
		private float _circularStrokeNoiseTime;
		private float _circularStrokeNoiseSeed;

		private void OnEnable() {
			if (_panelRenderer == null) _panelRenderer = GetComponent<PanelRenderer>();
			if (_panelRenderer == null) return;

			_circularStrokeNoiseSeed = Random.Range(0f, 1000f);
			_circularStrokeNoiseTime = 0f;
			_panelRenderer.RegisterUIReloadCallback(OnUIReload);
			_reloadRoutine = StartCoroutine(ReloadUiAfterPanelInitialization());
		}

		private void OnUIReload(PanelRenderer renderer, VisualElement rootElement) {
			if (rootElement == null) return;

			if (_decorationRoot != null) _decorationRoot.RemoveFromHierarchy();
			_decorationRoot = new VisualElement {
				name = "overlay-decoration",
				pickingMode = PickingMode.Ignore
			};
			_decorationRoot.style.position = Position.Absolute;
			_decorationRoot.style.left = Pixels(0f);
			_decorationRoot.style.right = Pixels(0f);
			_decorationRoot.style.top = Pixels(0f);
			_decorationRoot.style.bottom = Pixels(0f);
			rootElement.Add(_decorationRoot);

			AddCorner(right: false, bottom: false);
			AddCorner(right: true, bottom: false);
			AddCorner(right: false, bottom: true);
			AddCorner(right: true, bottom: true);

			AddHorizontalStrip(top: true);
			AddHorizontalStrip(top: false);
			AddVerticalStrip(right: false);
			AddVerticalStrip(right: true);
			AddCircularStrokes();
		}

		private void Update() {
			if (_circularStrokeRoot == null) return;

			_circularStrokeNoiseTime += Time.deltaTime * _circularStrokeRotationNoiseFrequency;
			var noise = Mathf.PerlinNoise(_circularStrokeNoiseSeed, _circularStrokeNoiseTime) * 2f - 1f;
			var rotationSpeed = noise * _circularStrokeRotationMaxSpeed;
			_circularStrokeRotation = Mathf.Repeat(_circularStrokeRotation + rotationSpeed * Time.deltaTime, 360f);
			_circularStrokeRoot.style.rotate = new StyleRotate(new Rotate(_circularStrokeRotation));
		}

		private void OnDisable() {
			if (_reloadRoutine != null) {
				StopCoroutine(_reloadRoutine);
				_reloadRoutine = null;
			}
			if (_panelRenderer != null) _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
			_decorationRoot?.RemoveFromHierarchy();
			_decorationRoot = null;
			_circularStrokeRoot = null;
			_circularStrokeRotation = 0f;
			_circularStrokeNoiseTime = 0f;
			_circularStrokeNoiseSeed = 0f;
		}

		private IEnumerator ReloadUiAfterPanelInitialization() {
			yield return null;
			_reloadRoutine = null;
			if (_panelRenderer == null) yield break;

			var asset = _panelRenderer.visualTreeAsset;
			_panelRenderer.visualTreeAsset = null;
			_panelRenderer.visualTreeAsset = asset;
		}

		private void AddCorner(bool right, bool bottom) {
			var corner = new VisualElement { pickingMode = PickingMode.Ignore };
			corner.style.position = Position.Absolute;
			corner.style.width = Pixels(_cornerSize);
			corner.style.height = Pixels(_cornerSize);
			if (right) corner.style.right = Pixels(_edgeInset);
			else corner.style.left = Pixels(_edgeInset);
			if (bottom) corner.style.bottom = Pixels(_edgeInset);
			else corner.style.top = Pixels(_edgeInset);
			_decorationRoot.Add(corner);

			var horizontalY = bottom ? _cornerSize - _cornerLineThickness : 0f;
			var verticalX = right ? _cornerSize - _cornerLineThickness : 0f;
			AddBar(corner, 0f, horizontalY, _cornerSize, _cornerLineThickness, _lineColor);
			AddBar(corner, verticalX, 0f, _cornerLineThickness, _cornerSize, _lineColor);

			AddInnerCorner(corner, right, bottom);

			var nodeX = right ? _cornerSize - _cornerNodeOffset : _cornerNodeOffset - _cornerNodeSize;
			var nodeY = bottom ? _cornerSize - _cornerNodeOffset : _cornerNodeOffset - _cornerNodeSize;
			var node = AddBar(corner, nodeX, nodeY, _cornerNodeSize, _cornerNodeSize, _lineColor);
			node.style.borderTopLeftRadius = Pixels(_cornerNodeRadius);
			node.style.borderTopRightRadius = Pixels(_cornerNodeRadius);
			node.style.borderBottomLeftRadius = Pixels(_cornerNodeRadius);
			node.style.borderBottomRightRadius = Pixels(_cornerNodeRadius);
		}

		private void AddInnerCorner(VisualElement parent, bool right, bool bottom) {
			var cornerX = right ? _cornerSize - _innerCornerInset : _innerCornerInset;
			var cornerY = bottom ? _cornerSize - _innerCornerInset : _innerCornerInset;
			var horizontalX = right ? cornerX - _innerCornerLength : cornerX;
			var verticalY = bottom ? cornerY - _innerCornerLength : cornerY;

			AddBar(parent, horizontalX, cornerY, _innerCornerLength, _cornerLineThickness, _accentColor);
			AddBar(parent, cornerX, verticalY, _cornerLineThickness, _innerCornerLength, _accentColor);
		}

		private void AddCircularStrokes() {
			var ring = new VisualElement {
				name = "overlay-circular-strokes",
				pickingMode = PickingMode.Ignore
			};
			ring.style.position = Position.Absolute;
			ring.style.left = Percent(50f);
			ring.style.top = Percent(50f);
			ring.style.width = Pixels(_circularStrokeRadius * 2f);
			ring.style.height = Pixels(_circularStrokeRadius * 2f);
			ring.style.marginLeft = Pixels(-_circularStrokeRadius);
			ring.style.marginTop = Pixels(-_circularStrokeRadius);
			_decorationRoot.Add(ring);
			_circularStrokeRoot = ring;
			ring.style.rotate = new StyleRotate(new Rotate(_circularStrokeRotation));

			for (var index = 0; index < _circularStrokeCount; index++) {
				var angle = index * 360f / _circularStrokeCount + _circularStrokeStartAngle;
				var radians = angle * Mathf.Deg2Rad;
				var stroke = new VisualElement { pickingMode = PickingMode.Ignore };
				stroke.style.position = Position.Absolute;
				stroke.style.left = Pixels(_circularStrokeRadius + Mathf.Cos(radians) * _circularStrokeRadius - _circularStrokeLength / 2f);
				stroke.style.top = Pixels(_circularStrokeRadius + Mathf.Sin(radians) * _circularStrokeRadius - _circularStrokeThickness / 2f);
				stroke.style.width = Pixels(_circularStrokeLength);
				stroke.style.height = Pixels(_circularStrokeThickness);
				stroke.style.opacity = _circularStrokeOpacity;
				stroke.style.backgroundColor = index % _accentStrokeInterval == 0 ? _accentColor : _lineColor;
				stroke.style.rotate = new StyleRotate(new Rotate(angle));
				ring.Add(stroke);
			}
		}

		private void AddHorizontalStrip(bool top) {
			var strip = CreateStrip();
			strip.style.left = Percent(50f);
			strip.style.width = Pixels(_stripSize);
			strip.style.height = Pixels(_stripHeight);
			strip.style.marginLeft = Pixels(-_stripSize / 2f);
			strip.style.flexDirection = FlexDirection.Row;
			strip.style.alignItems = Align.Center;
			strip.style.justifyContent = Justify.SpaceBetween;
			if (top) strip.style.top = Pixels(_edgeInset);
			else strip.style.bottom = Pixels(_edgeInset);

			AddSegment(strip, 12f, _cornerLineThickness, _lineColor);
			AddSegment(strip, 72f, _cornerLineThickness, _lineColor);
			AddSegment(strip, 28f, _cornerLineThickness, _accentColor);
			AddSegment(strip, 12f, _cornerLineThickness, _lineColor);
		}

		private void AddVerticalStrip(bool right) {
			var strip = CreateStrip();
			strip.style.top = Percent(50f);
			strip.style.width = Pixels(_stripHeight);
			strip.style.height = Pixels(_stripSize);
			strip.style.marginTop = Pixels(-_stripSize / 2f);
			strip.style.flexDirection = FlexDirection.Column;
			strip.style.alignItems = Align.Center;
			strip.style.justifyContent = Justify.SpaceBetween;
			if (right) strip.style.right = Pixels(_edgeInset);
			else strip.style.left = Pixels(_edgeInset);

			AddSegment(strip, _cornerLineThickness, 12f, _lineColor);
			AddSegment(strip, _cornerLineThickness, 72f, _lineColor);
			AddSegment(strip, _cornerLineThickness, 28f, _accentColor);
		}

		private VisualElement CreateStrip() {
			var strip = new VisualElement { pickingMode = PickingMode.Ignore };
			strip.style.position = Position.Absolute;
			strip.style.opacity = _stripOpacity;
			_decorationRoot.Add(strip);
			return strip;
		}

		private static VisualElement AddSegment(VisualElement parent, float width, float height, Color color) {
			var segment = new VisualElement { pickingMode = PickingMode.Ignore };
			segment.style.width = Pixels(width);
			segment.style.height = Pixels(height);
			segment.style.flexShrink = 0f;
			segment.style.backgroundColor = color;
			parent.Add(segment);
			return segment;
		}

		private static VisualElement AddBar(VisualElement parent, float left, float top, float width, float height, Color color) {
			var bar = new VisualElement { pickingMode = PickingMode.Ignore };
			bar.style.position = Position.Absolute;
			bar.style.left = Pixels(left);
			bar.style.top = Pixels(top);
			bar.style.width = Pixels(width);
			bar.style.height = Pixels(height);
			bar.style.backgroundColor = color;
			parent.Add(bar);
			return bar;
		}

		private static Length Pixels(float value) => new Length(value, LengthUnit.Pixel);
		private static Length Percent(float value) => new Length(value, LengthUnit.Percent);
	}
}
