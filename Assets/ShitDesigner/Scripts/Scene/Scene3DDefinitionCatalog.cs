using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Explicit installed set used to resolve persisted 3D definition UUIDs.</summary>
	[CreateAssetMenu(fileName = "Scene3DDefinitionCatalog", menuName = "ShitDesigner/Scene 3D Definition Catalog")]
	public sealed class Scene3DDefinitionCatalog : ScriptableObject {
		[SerializeField] private List<Scene3DDefinition> _definitions = new List<Scene3DDefinition>();

		public IReadOnlyList<Scene3DDefinition> Definitions => new ReadOnlyCollection<Scene3DDefinition>(_definitions ?? new List<Scene3DDefinition>());

		public UnitResult<Diagnostic> Validate() {
			if (_definitions == null || _definitions.Count == 0) return Failure("scene.definition_catalog.empty", "The Scene3DDefinitionCatalog requires at least one definition.");
			if (_definitions.Any(definition => definition == null)) return Failure("scene.definition_catalog.null", "The Scene3DDefinitionCatalog contains a null definition.");
			foreach (var definition in _definitions) {
				var valid = definition.Validate();
				if (valid.IsFailure) return valid;
			}
			if (_definitions.GroupBy(definition => definition.Id, StringComparer.Ordinal).Any(group => group.Count() > 1))
				return Failure("scene.definition_catalog.duplicate", "Scene3DDefinition UUIDs must be unique.");
			return UnitResult.Success<Diagnostic>();
		}

		public bool TryGet(string id, out Scene3DDefinition definition) {
			definition = (_definitions ?? new List<Scene3DDefinition>()).FirstOrDefault(candidate => candidate != null && string.Equals(candidate.Id, id, StringComparison.Ordinal));
			return definition != null;
		}

		private static UnitResult<Diagnostic> Failure(string code, string message) =>
			UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
	}
}
