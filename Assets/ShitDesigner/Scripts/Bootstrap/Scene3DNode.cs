using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Bootstrap {
	/// <summary>Authored instance of the production 3D scene node.</summary>
	[DisallowMultipleComponent]
	public sealed class Scene3DNode : UnityGraphNode {
		[SerializeField] private Scene3DDefinition _definition;

		public override string TypeId => "shitdesigner.scene.3d";
		public Scene3DDefinition Definition => _definition;

		protected override Result<string, Diagnostic> BuildRawState(BootstrapAssets assets) {
			if (_definition == null)
				return Result.Failure<string, Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.graph.scene3d_definition_missing"), Severity.Error,
					"A Scene3DNode requires a Scene3DDefinition.", module: "bootstrap"));
			var installed = assets.ValidateInstalled(_definition);
			return installed.IsFailure
				? Result.Failure<string, Diagnostic>(installed.Error)
				: Result.Success<string, Diagnostic>(_definition.CreateRawState());
		}
	}
}
