using System;
using System.Collections.Generic;
using System.Linq;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Main {
	public readonly struct LiveParameterDefinition {
		public string Id { get; }
		public string DisplayName { get; }
		public ParameterType Type { get; }
		public ParameterValue TypedValue { get; }
		public bool HasRange { get; }
		public float Minimum { get; }
		public float Maximum { get; }
		public float Value { get; }
		public bool IsTrigger { get; }

		public LiveParameterDefinition(string id, string displayName, float minimum, float maximum, float value, bool isTrigger = false) {
			Id = id;
			DisplayName = displayName;
			Type = ParameterType.Float;
			TypedValue = ParameterValue.FromFloat(value);
			HasRange = true;
			Minimum = minimum;
			Maximum = maximum;
			Value = value;
			IsTrigger = isTrigger;
		}

		public LiveParameterDefinition(string id, string displayName, ParameterValue value, bool isTrigger = false) {
			Id = id;
			DisplayName = displayName;
			Type = value.Type;
			TypedValue = value;
			HasRange = false;
			Minimum = 0f;
			Maximum = 0f;
			Value = value.Type == ParameterType.Float ? value.AsFloat() : 0f;
			IsTrigger = isTrigger;
		}

		internal LiveParameterDefinition WithTrigger(bool isTrigger)
			=> HasRange
				? new LiveParameterDefinition(Id, DisplayName, Minimum, Maximum, Value, isTrigger)
				: new LiveParameterDefinition(Id, DisplayName, TypedValue, isTrigger);
	}

	/// <summary>
	/// Discovers and dispatches the public parameters authored on one live-scene prefab.
	/// </summary>
	[DisallowMultipleComponent]
	public sealed class LiveSceneRoot : MonoBehaviour {
		private readonly Dictionary<string, ILiveSceneParameter> _parameters = new Dictionary<string, ILiveSceneParameter>(StringComparer.Ordinal);
		private string m_SceneId = string.Empty;
		private ILiveSceneTimeScaleProvider _timeScaleProvider;
		private bool m_ParametersCollected;

		public string SceneId => m_SceneId;
		public float TimeScale => _timeScaleProvider == null ? 1f : _timeScaleProvider.TimeScale;
		public IReadOnlyList<string> PublicParameterIds {
			get {
				CollectParameters();
				return _parameters.Keys.ToArray();
			}
		}

		public void Initialize(string sceneId) {
			if (string.IsNullOrWhiteSpace(sceneId))
				throw new ArgumentException("A scene ID is required.", nameof(sceneId));

			m_SceneId = sceneId;
			CollectParameters();
		}

		public LiveParameterDefinition[] GetParameterDefinitions() {
			CollectParameters();
			return _parameters.Values
				.Select(parameter => parameter.Definition.WithTrigger(parameter is ILiveSceneTriggerParameter))
				.ToArray();
		}

		public bool TrySetParameter(string parameterId, float value, out string rejectionReason) {
			if (float.IsNaN(value) || float.IsInfinity(value)) {
				rejectionReason = "The parameter value must be finite.";
				return false;
			}
			CollectParameters();
			if (!_parameters.TryGetValue(parameterId, out var parameter)) {
				rejectionReason = "The parameter is not published by this live scene.";
				return false;
			}
			return parameter.TrySetValue(value, out rejectionReason);
		}

		public bool IsTriggerParameter(string parameterId) {
			CollectParameters();
			return _parameters.TryGetValue(parameterId, out var parameter) && parameter is ILiveSceneTriggerParameter;
		}

		private void CollectParameters() {
			if (m_ParametersCollected)
				return;

			var attachedComponents = GetComponents<MonoBehaviour>();
			var attachedParameters = attachedComponents.OfType<ILiveSceneParameter>()
				.Concat(attachedComponents.OfType<ILiveSceneParameterProvider>()
					.SelectMany(provider => provider.LiveParameters ?? Array.Empty<ILiveSceneParameter>()))
				.ToArray();
			foreach (var parameter in attachedParameters) {
				var definition = parameter.Definition;
				ValidateDefinition(definition, parameter);

				if (!_parameters.TryAdd(definition.Id, parameter))
					throw new InvalidOperationException("Live scene parameter IDs must be unique: " + definition.Id + ".");
				if (parameter is ILiveSceneTimeScaleProvider timeScaleProvider) {
					if (_timeScaleProvider != null) throw new InvalidOperationException("A live scene can provide only one graph-clock time scale.");
					_timeScaleProvider = timeScaleProvider;
				}
			}
			foreach (var parameter in attachedParameters.OfType<LiveSceneParameter>()) parameter.InitializeParameter();
			m_ParametersCollected = true;
		}

		private static void ValidateDefinition(LiveParameterDefinition definition, ILiveSceneParameter parameter) {
			if (string.IsNullOrWhiteSpace(definition.Id) || string.IsNullOrWhiteSpace(definition.DisplayName))
				throw new InvalidOperationException("Live scene parameters require IDs and display names: " + parameter.GetType().Name + ".");
			if (float.IsNaN(definition.Minimum) || float.IsInfinity(definition.Minimum) || float.IsNaN(definition.Maximum) || float.IsInfinity(definition.Maximum)
				|| definition.Minimum > definition.Maximum || float.IsNaN(definition.Value) || float.IsInfinity(definition.Value))
				throw new InvalidOperationException("Live scene parameter definitions must have finite ordered ranges and values: " + definition.Id + ".");
		}
	}
}
