using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Prefab selected by a Main-authored 3D scene node.</summary>
	[CreateAssetMenu(fileName = "Scene3DDefinition", menuName = "ShitDesigner/Scene 3D Definition")]
	public sealed class Scene3DDefinition : ScriptableObject {
		[SerializeField] private string _id;
		[SerializeField] private GameObject _prefab;

		public string Id => _id ?? string.Empty;
		public GameObject Prefab => _prefab;

		public UnitResult<Diagnostic> Validate() {
			if (string.IsNullOrWhiteSpace(Id)) return Failure("scene.definition.id", "A Scene3DDefinition requires an ID.");
			if (_prefab == null) return Failure("scene.definition.prefab", "A Scene3DDefinition requires a prefab.");
			return UnitResult.Success<Diagnostic>();
		}

		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(Diagnostic(code, message));
		private static Diagnostic Diagnostic(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene");
	}
}
