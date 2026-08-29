using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Publishes one authored live-scene control.</summary>
	public interface ILiveSceneParameter {
		LiveParameterDefinition Definition { get; }
		bool TrySetValue(float value, out string rejectionReason);
	}

	/// <summary>Marks a scene parameter whose active transition invokes a one-shot action.</summary>
	public interface ILiveSceneTriggerParameter { }

	/// <summary>Supplies the graph-clock rate for a live scene.</summary>
	public interface ILiveSceneTimeScaleProvider {
		float TimeScale { get; }
	}

	/// <summary>Receives a one-shot action from a live scene parameter.</summary>
	public interface ILiveParameterTriggerReceiver {
		void OnLiveParameterTriggered();
	}

	/// <summary>Base component for authored live-scene controls.</summary>
	public abstract class LiveSceneParameter : MonoBehaviour, ILiveSceneParameter {
		public abstract LiveParameterDefinition Definition { get; }
		public abstract bool TrySetValue(float value, out string rejectionReason);
		public virtual void InitializeParameter() { }
	}
}
