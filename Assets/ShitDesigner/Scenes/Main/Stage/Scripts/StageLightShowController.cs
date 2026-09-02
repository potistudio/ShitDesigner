using System.Collections.Generic;
using ShitDesigner.Core;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Stage {
	[DisallowMultipleComponent]
	[DefaultExecutionOrder(10000)]
	public sealed class StageLightShowController : MonoBehaviour, IBpmClockReceiver {
		private static readonly int IntensityId = Shader.PropertyToID("_Intensity");
		private static readonly int ColorId = Shader.PropertyToID("_Color");

		[Header("Rotation")]
		[SerializeField, Min(0f), Tooltip("左右の首振り角度です。")]
		private float _panAmplitude = 55f;

		[SerializeField, Min(0f), Tooltip("上下の首振り角度です。")]
		private float _tiltAmplitude = 28f;

		[SerializeField, Min(0f), Tooltip("左右の首振りの速さです（1 ビートあたりの周回数）。")]
		private float _panSpeed = 0.55f;

		[SerializeField, Min(0f), Tooltip("上下の首振りの速さです（1 ビートあたりの周回数）。")]
		private float _tiltSpeed = 0.8f;

		[Header("Strobe")]
		[SerializeField, Tooltip("点滅を有効にします。")]
		private bool _strobeEnabled = true;

		[SerializeField, Min(0f), Tooltip("1 ビートあたりの点滅回数です。")]
		private float _strobeFrequency = 2.5f;

		[SerializeField, Range(0.01f, 1f), Tooltip("1 回の点滅で点灯している時間の割合です。")]
		private float _strobeDutyCycle = 0.32f;

		[SerializeField, Range(0f, 1f), Tooltip("消灯中の明るさです。")]
		private float _minimumBrightness = 0.03f;

		[Header("Color")]
		[SerializeField, Tooltip("パレットの色を順番に切り替えます。")]
		private bool _colorCycleEnabled = true;

		[SerializeField, Min(0f), Tooltip("パレットを 1 周する速さです（1 ビートあたりの周回数）。")]
		private float _colorCycleSpeed = 0.12f;

		[SerializeField, Tooltip("ライトとビームに適用する色です。")]
		private Color[] _colorPalette = {
			new Color(0.1f, 0.75f, 1f),
			new Color(1f, 0.12f, 0.72f),
			new Color(0.55f, 0.2f, 1f),
			new Color(1f, 0.45f, 0.08f)
		};

		[Header("Beat Preview")]
		[SerializeField, Range(30f, 300f), Tooltip("共有 BPM クロックがないときに使用するプレビュー BPM です。")]
		private float m_PreviewBpm = 145f;

		private readonly List<LightRig> _lightRigs = new();
		private MaterialPropertyBlock _propertyBlock;
		private double m_AdjustedTotalBeats;
		private bool m_UsesExternalBpmClock;

		private void Awake() {
			_propertyBlock = new MaterialPropertyBlock();
		}

		private void OnEnable() {
			m_AdjustedTotalBeats = 0d;
			m_UsesExternalBpmClock = false;
			BuildLightRigs();
		}

		private void OnDisable() {
			RestoreLightRigs();
		}

		private void LateUpdate() {
			if (_lightRigs.Count == 0) BuildLightRigs();
			if (!m_UsesExternalBpmClock)
				m_AdjustedTotalBeats += Mathf.Max(0f, Time.unscaledDeltaTime) * m_PreviewBpm / 60d;

			var beatPosition = m_AdjustedTotalBeats;
			foreach (var lightRig in _lightRigs) {
				var pan = Mathf.Sin(((float)beatPosition * _panSpeed + lightRig.Phase) * Mathf.PI * 2f) * _panAmplitude;
				var tilt = Mathf.Sin(((float)beatPosition * _tiltSpeed + lightRig.Phase * 1.7f) * Mathf.PI * 2f) * _tiltAmplitude;
				lightRig.Pivot.localRotation = lightRig.InitialRotation * Quaternion.Euler(tilt, pan, 0f);

				var brightness = GetBrightness(beatPosition, lightRig.Phase);
				lightRig.Apply(_propertyBlock, brightness, GetColor(beatPosition, lightRig.Phase));
			}
		}

		public void SetBpmClock(BeatClockFrame frame) {
			if (!frame.IsAvailable || double.IsNaN(frame.AdjustedTotalBeats) || double.IsInfinity(frame.AdjustedTotalBeats))
				return;

			m_UsesExternalBpmClock = true;
			m_AdjustedTotalBeats = frame.AdjustedTotalBeats;
		}

		private float GetBrightness(double beatPosition, float phase) {
			if (!_strobeEnabled || _strobeFrequency <= 0f) return 1f;

			var strobeTime = Mathf.Repeat((float)beatPosition * _strobeFrequency + phase, 1f);
			return strobeTime <= _strobeDutyCycle ? 1f : _minimumBrightness;
		}

		private Color GetColor(double beatPosition, float phase) {
			if (!_colorCycleEnabled || _colorPalette == null || _colorPalette.Length == 0) return Color.white;

			var colorPosition = Mathf.Repeat((float)beatPosition * _colorCycleSpeed + phase, 1f) * _colorPalette.Length;
			var colorIndex = Mathf.FloorToInt(colorPosition);
			var nextColorIndex = (colorIndex + 1) % _colorPalette.Length;
			return Color.Lerp(_colorPalette[colorIndex], _colorPalette[nextColorIndex], colorPosition - colorIndex);
		}

		private void BuildLightRigs() {
			_lightRigs.Clear();

			var pivots = new HashSet<Transform>();
			foreach (var volumetricBounds in GetComponentsInChildren<VolumetricLightingBounds>(true)) {
				var pivot = volumetricBounds.transform.parent;
				if (pivot == null || !pivots.Add(pivot)) continue;

				var lights = pivot.GetComponentsInChildren<Light>(true);
				if (lights.Length == 0) continue;

				_lightRigs.Add(new LightRig(pivot, lights, volumetricBounds.GetComponent<Renderer>(), GetPhase(pivot)));
			}
		}

		private static float GetPhase(Transform pivot) {
			var position = pivot.position;
			return Mathf.Repeat(position.x * 0.137f + position.y * 0.271f + position.z * 0.389f, 1f);
		}

		private void RestoreLightRigs() {
			foreach (var lightRig in _lightRigs) lightRig.Restore(_propertyBlock);
		}

		private sealed class LightRig {
			private readonly Light[] _lights;
			private readonly float[] _lightIntensities;
			private readonly Color[] _lightColors;
			private readonly Renderer _volumetricRenderer;
			private readonly float _volumetricIntensity;
			private readonly Color _volumetricColor;
			private readonly bool _hasVolumetricColor;

			public LightRig(Transform pivot, Light[] lights, Renderer volumetricRenderer, float phase) {
				Pivot = pivot;
				InitialRotation = pivot.localRotation;
				Phase = phase;
				_lights = lights;
				_lightIntensities = new float[lights.Length];
				_lightColors = new Color[lights.Length];
				for (var index = 0; index < lights.Length; index++) {
					_lightIntensities[index] = lights[index].intensity;
					_lightColors[index] = lights[index].color;
				}

				_volumetricRenderer = volumetricRenderer;
				var material = volumetricRenderer == null ? null : volumetricRenderer.sharedMaterial;
				_volumetricIntensity = material != null && material.HasProperty(IntensityId) ? material.GetFloat(IntensityId) : 0f;
				_hasVolumetricColor = material != null && material.HasProperty(ColorId);
				_volumetricColor = _hasVolumetricColor ? material.GetColor(ColorId) : Color.white;
			}

			public Transform Pivot { get; }
			public Quaternion InitialRotation { get; }
			public float Phase { get; }

			public void Apply(MaterialPropertyBlock propertyBlock, float brightness, Color color) {
				for (var index = 0; index < _lights.Length; index++) {
					_lights[index].intensity = _lightIntensities[index] * brightness;
					_lights[index].color = _lightColors[index] * color;
				}
				if (_volumetricRenderer == null) return;

				_volumetricRenderer.GetPropertyBlock(propertyBlock);
				propertyBlock.SetFloat(IntensityId, _volumetricIntensity * brightness);
				if (_hasVolumetricColor) propertyBlock.SetColor(ColorId, _volumetricColor * color);
				_volumetricRenderer.SetPropertyBlock(propertyBlock);
			}

			public void Restore(MaterialPropertyBlock propertyBlock) {
				Pivot.localRotation = InitialRotation;
				for (var index = 0; index < _lights.Length; index++) {
					_lights[index].intensity = _lightIntensities[index];
					_lights[index].color = _lightColors[index];
				}
				if (_volumetricRenderer == null) return;

				_volumetricRenderer.GetPropertyBlock(propertyBlock);
				propertyBlock.SetFloat(IntensityId, _volumetricIntensity);
				if (_hasVolumetricColor) propertyBlock.SetColor(ColorId, _volumetricColor);
				_volumetricRenderer.SetPropertyBlock(propertyBlock);
			}
		}
	}
}
