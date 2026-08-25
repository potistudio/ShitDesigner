using System;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Holds the assets required to construct the fixed Main live graph.</summary>
	[DisallowMultipleComponent]
	public sealed class LiveGraphBootstrap : MonoBehaviour {
		[SerializeField] private Scene3DDefinition[] _scenes = Array.Empty<Scene3DDefinition>();

		public Scene3DDefinition[] Scenes => _scenes ?? Array.Empty<Scene3DDefinition>();

		public LiveGraphRuntime CreateRuntime() => new LiveGraphRuntime(Scenes);
	}
}
