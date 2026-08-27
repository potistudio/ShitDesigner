using UnityEngine;

namespace ShitDesigner.Stage {
	[ExecuteAlways]
	[DisallowMultipleComponent]
	[RequireComponent(typeof(Renderer))]
	public sealed class VolumetricLightingBounds : MonoBehaviour {
		private static readonly int BeamAngleId = Shader.PropertyToID("_BeamAngle");
		private static readonly int BeamDistanceId = Shader.PropertyToID("_BeamDistance");
		private const float BoundsPadding = 0.1f;

		private Renderer _renderer;
		private Material _material;
		private float _beamAngle = -1;
		private float _beamDistance = -1;

		private void OnEnable() {
			RefreshBounds();
		}

		private void Update() {
			RefreshBounds();
		}

		private void RefreshBounds() {
			if (_renderer == null) _renderer = GetComponent<Renderer>();
			var material = _renderer.sharedMaterial;
			if (material == null || !material.HasProperty(BeamAngleId) || !material.HasProperty(BeamDistanceId)) return;

			var beamAngle = material.GetFloat(BeamAngleId);
			var beamDistance = material.GetFloat(BeamDistanceId);
			if (_material == material && Mathf.Approximately(_beamAngle, beamAngle) && Mathf.Approximately(_beamDistance, beamDistance)) return;

			_material = material;
			_beamAngle = beamAngle;
			_beamDistance = beamDistance;
			var radius = Mathf.Tan(beamAngle * 0.5f * Mathf.Deg2Rad) * beamDistance;
			_renderer.localBounds = new Bounds(
				new Vector3(0, -beamDistance * 0.5f, 0),
				new Vector3(radius * 2 + BoundsPadding, beamDistance + BoundsPadding, radius * 2 + BoundsPadding));
		}
	}
}
