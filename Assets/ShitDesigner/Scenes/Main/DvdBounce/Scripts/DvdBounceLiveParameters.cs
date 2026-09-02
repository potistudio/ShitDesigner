using System;
using System.Collections.Generic;
using ShitDesigner.Main;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Publishes the DVD bounce live parameters from one component.</summary>
	[DisallowMultipleComponent]
	public sealed class DvdBounceLiveParameters : MonoBehaviour, ILiveSceneParameterProvider {
		public const string SpeedParameterId = "speed";
		public const string InstanceCountParameterId = "instance_count";
		public const float MaximumSpeed = 30f;

		[SerializeField] private string m_SpeedId = SpeedParameterId;
		[SerializeField] private string m_SpeedDisplayName = "Speed";
		[SerializeField] private string m_InstanceCountId = InstanceCountParameterId;
		[SerializeField] private string m_InstanceCountDisplayName = "Instance Count";
		[SerializeField] private DvdBounceScene m_Scene;

		private IReadOnlyList<ILiveSceneParameter> m_LiveParameters;

		public IReadOnlyList<ILiveSceneParameter> LiveParameters => m_LiveParameters ??= new ILiveSceneParameter[] {
			new DelegatedLiveParameter(GetSpeedDefinition, TrySetSpeed),
			new DelegatedLiveParameter(GetInstanceCountDefinition, TrySetInstanceCount)
		};

		private LiveParameterDefinition GetSpeedDefinition() {
			var scene = ResolveScene();
			return new LiveParameterDefinition(
				m_SpeedId, m_SpeedDisplayName, DvdBounceScene.MinimumSpeed, MaximumSpeed,
				scene == null ? DvdBounceScene.MinimumSpeed : scene.Speed);
		}

		private LiveParameterDefinition GetInstanceCountDefinition() {
			var scene = ResolveScene();
			return new LiveParameterDefinition(
				m_InstanceCountId, m_InstanceCountDisplayName,
				DvdBounceScene.MinimumInstanceCount, DvdBounceScene.MaximumInstanceCount,
				scene == null ? DvdBounceScene.MinimumInstanceCount : scene.InstanceCount);
		}

		private bool TrySetSpeed(float value, out string rejectionReason) {
			if (!TryGetScene(value, out var scene, out rejectionReason))
				return false;

			scene.SetSpeed(Mathf.Clamp(value, DvdBounceScene.MinimumSpeed, MaximumSpeed));
			rejectionReason = string.Empty;
			return true;
		}

		private bool TrySetInstanceCount(float value, out string rejectionReason) {
			if (!TryGetScene(value, out var scene, out rejectionReason))
				return false;

			scene.SetInstanceCount(Mathf.RoundToInt(value));
			rejectionReason = string.Empty;
			return true;
		}

		private bool TryGetScene(float value, out DvdBounceScene scene, out string rejectionReason) {
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				scene = null;
				rejectionReason = "The parameter value must be finite.";
				return false;
			}

			scene = ResolveScene();
			if (scene != null) {
				rejectionReason = string.Empty;
				return true;
			}

			rejectionReason = "The DVD bounce scene is missing.";
			return false;
		}

		private DvdBounceScene ResolveScene() {
			if (m_Scene == null)
				m_Scene = GetComponentInChildren<DvdBounceScene>(true);
			return m_Scene;
		}

		private delegate bool TrySetValue(float value, out string rejectionReason);

		private sealed class DelegatedLiveParameter : ILiveSceneParameter {
			private readonly Func<LiveParameterDefinition> m_GetDefinition;
			private readonly TrySetValue m_TrySetValue;

			public LiveParameterDefinition Definition => m_GetDefinition();

			public DelegatedLiveParameter(Func<LiveParameterDefinition> getDefinition, TrySetValue trySetValue) {
				m_GetDefinition = getDefinition;
				m_TrySetValue = trySetValue;
			}

			public bool TrySetValue(float value, out string rejectionReason) {
				return m_TrySetValue(value, out rejectionReason);
			}
		}
	}
}
