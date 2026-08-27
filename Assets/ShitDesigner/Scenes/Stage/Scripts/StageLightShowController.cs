using System.Collections.Generic;
using UnityEngine;

namespace ShitDesigner.Stage {
	[DisallowMultipleComponent]
	[DefaultExecutionOrder(10000)]
	public sealed class StageLightShowController : MonoBehaviour {
		private static readonly int IntensityId = Shader.PropertyToID("_Intensity");

		[Header("Rotation")]
		[SerializeField, Min(0f), Tooltip("左右の首振り角度です。")]
		private float _panAmplitude = 55f;

		[SerializeField, Min(0f), Tooltip("上下の首振り角度です。")]
		private float _tiltAmplitude = 28f;

		[SerializeField, Min(0f), Tooltip("左右の首振りの速さです。")]
		private float _panSpeed = 0.55f;

		[SerializeField, Min(0f), Tooltip("上下の首振りの速さです。")]
		private float _tiltSpeed = 0.8f;

		[Header("Strobe")]
		[SerializeField, Tooltip("点滅を有効にします。")]
		private bool _strobeEnabled = true;

		[SerializeField, Min(0f), Tooltip("1 秒あたりの点滅回数です。")]
		private float _strobeFrequency = 2.5f;

		[SerializeField, Range(0.01f, 1f), Tooltip("1 回の点滅で点灯している時間の割合です。")]
		private float _strobeDutyCycle = 0.32f;

		[SerializeField, Range(0f, 1f), Tooltip("消灯中の明るさです。")]
		private float _minimumBrightness = 0.03f;

		private readonly List<LightRig> _lightRigs = new();
		private readonly MaterialPropertyBlock _propertyBlock = new();

		private void OnEnable() {
			BuildLightRigs();
		}

		private void OnDisable() {
			RestoreLightRigs();
		}

		private void LateUpdate() {
			if (_lightRigs.Count == 0) BuildLightRigs();

			var time = Time.time;
			foreach (var lightRig in _lightRigs) {
				var pan = Mathf.Sin((time * _panSpeed + lightRig.Phase) * Mathf.PI * 2f) * _panAmplitude;
				var tilt = Mathf.Sin((time * _tiltSpeed + lightRig.Phase * 1.7f) * Mathf.PI * 2f) * _tiltAmplitude;
				lightRig.Pivot.localRotation = lightRig.InitialRotation * Quaternion.Euler(tilt, pan, 0f);

				var brightness = GetBrightness(time, lightRig.Phase);
				lightRig.ApplyBrightness(_propertyBlock, brightness);
			}
		}

		private float GetBrightness(float time, float phase) {
			if (!_strobeEnabled || _strobeFrequency <= 0f) return 1f;

			var strobeTime = Mathf.Repeat(time * _strobeFrequency + phase, 1f);
			return strobeTime <= _strobeDutyCycle ? 1f : _minimumBrightness;
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
			private readonly Renderer _volumetricRenderer;
			private readonly float _volumetricIntensity;

			public LightRig(Transform pivot, Light[] lights, Renderer volumetricRenderer, float phase) {
				Pivot = pivot;
				InitialRotation = pivot.localRotation;
				Phase = phase;
				_lights = lights;
				_lightIntensities = new float[lights.Length];
				for (var index = 0; index < lights.Length; index++) _lightIntensities[index] = lights[index].intensity;

				_volumetricRenderer = volumetricRenderer;
				var material = volumetricRenderer == null ? null : volumetricRenderer.sharedMaterial;
				_volumetricIntensity = material != null && material.HasProperty(IntensityId) ? material.GetFloat(IntensityId) : 0f;
			}

			public Transform Pivot { get; }
			public Quaternion InitialRotation { get; }
			public float Phase { get; }

			public void ApplyBrightness(MaterialPropertyBlock propertyBlock, float brightness) {
				for (var index = 0; index < _lights.Length; index++) _lights[index].intensity = _lightIntensities[index] * brightness;
				if (_volumetricRenderer == null) return;

				_volumetricRenderer.GetPropertyBlock(propertyBlock);
				propertyBlock.SetFloat(IntensityId, _volumetricIntensity * brightness);
				_volumetricRenderer.SetPropertyBlock(propertyBlock);
			}

			public void Restore(MaterialPropertyBlock propertyBlock) {
				Pivot.localRotation = InitialRotation;
				for (var index = 0; index < _lights.Length; index++) _lights[index].intensity = _lightIntensities[index];
				if (_volumetricRenderer == null) return;

				_volumetricRenderer.GetPropertyBlock(propertyBlock);
				propertyBlock.SetFloat(IntensityId, _volumetricIntensity);
				_volumetricRenderer.SetPropertyBlock(propertyBlock);
			}
		}
	}
}
