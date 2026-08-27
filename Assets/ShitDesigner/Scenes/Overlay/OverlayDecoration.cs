using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShitDesigner.Presentation.Overlay {
	[RequireComponent(typeof(PanelRenderer))]
	[DefaultExecutionOrder(1100)]
	public sealed class OverlayDecoration : MonoBehaviour {
		private const float EdgeInset = 32f;
		private const float CornerSize = 112f;
		private const float StripSize = 176f;

		private static readonly Color LineColor = new Color(0.91f, 0.93f, 0.95f, 0.74f);
		private static readonly Color AccentColor = new Color(0.34f, 0.65f, 1f, 0.68f);

		[SerializeField] private PanelRenderer _panelRenderer;

		private VisualElement _decorationRoot;
		private Coroutine _reloadRoutine;

		private void OnEnable() {
			if (_panelRenderer == null) _panelRenderer = GetComponent<PanelRenderer>();
			if (_panelRenderer == null) return;

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
		}

		private void OnDisable() {
			if (_reloadRoutine != null) {
				StopCoroutine(_reloadRoutine);
				_reloadRoutine = null;
			}
			if (_panelRenderer != null) _panelRenderer.UnregisterUIReloadCallback(OnUIReload);
			_decorationRoot?.RemoveFromHierarchy();
			_decorationRoot = null;
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
			corner.style.width = Pixels(CornerSize);
			corner.style.height = Pixels(CornerSize);
			if (right) corner.style.right = Pixels(EdgeInset);
			else corner.style.left = Pixels(EdgeInset);
			if (bottom) corner.style.bottom = Pixels(EdgeInset);
			else corner.style.top = Pixels(EdgeInset);
			_decorationRoot.Add(corner);

			var horizontalY = bottom ? CornerSize - 1f : 0f;
			var verticalX = right ? CornerSize - 1f : 0f;
			AddBar(corner, 0f, horizontalY, CornerSize, 1f, LineColor);
			AddBar(corner, verticalX, 0f, 1f, CornerSize, LineColor);

			AddInnerCorner(corner, right, bottom);

			var nodeX = right ? CornerSize - 3f : -2f;
			var nodeY = bottom ? CornerSize - 3f : -2f;
			var node = AddBar(corner, nodeX, nodeY, 5f, 5f, LineColor);
			node.style.borderTopLeftRadius = Pixels(3f);
			node.style.borderTopRightRadius = Pixels(3f);
			node.style.borderBottomLeftRadius = Pixels(3f);
			node.style.borderBottomRightRadius = Pixels(3f);
		}

		private static void AddInnerCorner(VisualElement parent, bool right, bool bottom) {
			const float inset = 12f;
			const float length = 44f;
			var cornerX = right ? CornerSize - inset : inset;
			var cornerY = bottom ? CornerSize - inset : inset;
			var horizontalX = right ? cornerX - length : cornerX;
			var verticalY = bottom ? cornerY - length : cornerY;

			AddBar(parent, horizontalX, cornerY, length, 1f, AccentColor);
			AddBar(parent, cornerX, verticalY, 1f, length, AccentColor);
		}

		private void AddHorizontalStrip(bool top) {
			var strip = CreateStrip();
			strip.style.left = Percent(50f);
			strip.style.width = Pixels(StripSize);
			strip.style.height = Pixels(5f);
			strip.style.marginLeft = Pixels(-StripSize / 2f);
			strip.style.flexDirection = FlexDirection.Row;
			strip.style.alignItems = Align.Center;
			strip.style.justifyContent = Justify.SpaceBetween;
			if (top) strip.style.top = Pixels(EdgeInset);
			else strip.style.bottom = Pixels(EdgeInset);

			AddSegment(strip, 12f, 1f, LineColor);
			AddSegment(strip, 72f, 1f, LineColor);
			AddSegment(strip, 28f, 1f, AccentColor);
			AddSegment(strip, 12f, 1f, LineColor);
		}

		private void AddVerticalStrip(bool right) {
			var strip = CreateStrip();
			strip.style.top = Percent(50f);
			strip.style.width = Pixels(5f);
			strip.style.height = Pixels(StripSize);
			strip.style.marginTop = Pixels(-StripSize / 2f);
			strip.style.flexDirection = FlexDirection.Column;
			strip.style.alignItems = Align.Center;
			strip.style.justifyContent = Justify.SpaceBetween;
			if (right) strip.style.right = Pixels(EdgeInset);
			else strip.style.left = Pixels(EdgeInset);

			AddSegment(strip, 1f, 12f, LineColor);
			AddSegment(strip, 1f, 72f, LineColor);
			AddSegment(strip, 1f, 28f, AccentColor);
		}

		private VisualElement CreateStrip() {
			var strip = new VisualElement { pickingMode = PickingMode.Ignore };
			strip.style.position = Position.Absolute;
			strip.style.opacity = 0.62f;
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
