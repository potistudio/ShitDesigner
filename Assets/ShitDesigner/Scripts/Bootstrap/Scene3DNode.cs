using UnityEngine;

namespace ShitDesigner.Bootstrap {
	/// <summary>Authored instance of the production 3D scene node.</summary>
	[DisallowMultipleComponent]
	public sealed class Scene3DNode : UnityGraphNode {
		public override string TypeId => "shitdesigner.scene.3d";
	}
}
