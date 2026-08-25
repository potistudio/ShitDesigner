using System;
using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Scene {
	/// <summary>Reusable Unity asset selected by a 3D graph node.</summary>
	[CreateAssetMenu(fileName = "Scene3DDefinition", menuName = "ShitDesigner/Scene 3D Definition")]
	public sealed class Scene3DDefinition : ScriptableObject {
		[Serializable]
		private sealed class PersistedState {
			public string definitionId = string.Empty;
		}

		[SerializeField] private string _id = string.Empty;
		[SerializeField] private GameObject _prefab;

		public string Id => Guid.TryParse(_id?.Trim(), out var parsed) ? parsed.ToString("D") : _id?.Trim() ?? string.Empty;
		public GameObject Prefab => _prefab;

		private void OnValidate() {
			if (string.IsNullOrWhiteSpace(_id)) _id = Guid.NewGuid().ToString("D");
		}

		public UnitResult<Diagnostic> Validate() {
			if (!Guid.TryParse(Id, out _)) return Failure("scene.definition.id", "A Scene3DDefinition requires a stable UUID.");
			if (_prefab == null) return Failure("scene.definition.prefab", "A Scene3DDefinition requires a prefab.");
			return UnitResult.Success<Diagnostic>();
		}

		public string CreateRawState() {
			var valid = Validate();
			if (valid.IsFailure) throw new InvalidOperationException(valid.Error.Message);
			return JsonUtility.ToJson(new PersistedState { definitionId = Id });
		}

		public static Result<string, Diagnostic> ReadDefinitionId(string rawState) {
			if (string.IsNullOrWhiteSpace(rawState) || string.Equals(rawState.Trim(), "{}", StringComparison.Ordinal))
				return Result.Success<string, Diagnostic>(string.Empty);
			try {
				var state = JsonUtility.FromJson<PersistedState>(rawState);
				if (state == null || !Guid.TryParse(state.definitionId, out _))
					return Result.Failure<string, Diagnostic>(Diagnostic("scene.definition.state", "The 3D scene node state does not contain a valid definition UUID."));
				return Result.Success<string, Diagnostic>(Guid.Parse(state.definitionId).ToString("D"));
			}
			catch (Exception exception) {
				return Result.Failure<string, Diagnostic>(new Diagnostic(new DiagnosticCode("scene.definition.state"), Severity.Error,
					"The 3D scene node state could not be decoded.", module: "scene", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(Diagnostic(code, message));
		private static Diagnostic Diagnostic(string code, string message) => new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene");
	}
}
