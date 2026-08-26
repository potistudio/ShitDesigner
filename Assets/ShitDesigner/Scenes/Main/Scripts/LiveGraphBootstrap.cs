using System;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Main {
	/// <summary>Holds the graph sources for independently rendered Main ProgramOutputs.</summary>
	[DisallowMultipleComponent]
	public sealed class LiveGraphBootstrap : MonoBehaviour {
		[SerializeField] private Scene3DDefinition[] _programOutputs = Array.Empty<Scene3DDefinition>();

		public Scene3DDefinition[] ProgramOutputs => _programOutputs ?? Array.Empty<Scene3DDefinition>();
		public Scene3DDefinition[] Scenes => ProgramOutputs;
		public int ProgramOutputCount => ProgramOutputs.Length;

		public LiveGraphRuntime CreateRuntime() => new LiveGraphRuntime(ProgramOutputs);
	}
}
