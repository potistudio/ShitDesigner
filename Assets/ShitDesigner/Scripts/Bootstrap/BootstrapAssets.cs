using System;
using System.IO;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Media;
using ShitDesigner.Nodes;
using ShitDesigner.Persistence;
using ShitDesigner.Project;
using ShitDesigner.Rendering;
using UnityEngine;

namespace ShitDesigner.Bootstrap {
	/// <summary>Explicit serialized production inputs.  The composition root
	/// never searches Resources, scenes, or global objects for these assets.
	/// Missing inputs are a startup failure with a visible diagnostic.</summary>
	public sealed class BootstrapAssets : MonoBehaviour {
		[Header("Isolated Scene prefabs")]
		[SerializeField] private GameObject _scene3dPrefab;
		[SerializeField] private GameObject _scene2dPrefab;
		[SerializeField] private GameObject _videoHostPrefab;

		[Header("Builtin shader roles")]
		[SerializeField] private Shader _shaderGenerator;
		[SerializeField] private Shader _shaderEffect;
		[SerializeField] private Shader _shaderBlend2;
		[SerializeField] private Shader _displayTransformShader;
		[SerializeField] private Material _videoConversionMaterial;
		[SerializeField] private Material _hapPremultiplyMaterial;
		[SerializeField] private Material _hapYCoCgMaterial;
		[SerializeField] private Material _hapAlphaMaterial;
		[SerializeField] private ComputeShader _hapDecodeShader;
		[Header("Generated runtime catalog")]
		[SerializeField] private NodeTypeCatalog _nodeTypeCatalog;
		[SerializeField] private ShaderNodeManifestAsset _shaderManifest;

		// Read-only seams keep production asset verification deterministic without
		// reflection.  The composition root still consumes the serialized fields.
		public GameObject Scene3dPrefab => _scene3dPrefab;
		public GameObject Scene2dPrefab => _scene2dPrefab;
		public NodeTypeCatalog NodeTypeCatalog => _nodeTypeCatalog;
		public ShaderNodeManifestAsset ShaderManifest => _shaderManifest != null ? _shaderManifest : _nodeTypeCatalog?.ShaderManifest;
		public Shader DisplayTransformShader => _displayTransformShader;

		/// <summary>Validates immutable startup inputs without opening devices,
		/// creating runtime objects, or taking ownership of GPU resources.</summary>
		public CSharpFunctionalExtensions.UnitResult<Diagnostic> Preflight() {
			if (_nodeTypeCatalog == null) return PreflightFailure("bootstrap.preflight.catalog_missing", "The generated NodeTypeCatalog asset is required.");
			if (_scene3dPrefab == null || _scene2dPrefab == null) return PreflightFailure("bootstrap.preflight.scene_missing", "Explicit 3D and 2D Scene prefabs are required.");
			if (_shaderGenerator == null || _shaderEffect == null || _shaderBlend2 == null) return PreflightFailure("bootstrap.preflight.shader_missing", "All three builtin shader role assets are required.");
			if (_displayTransformShader == null) return PreflightFailure("bootstrap.preflight.display_transform_missing", "The explicit DisplayTransform shader is required.");
			if (_videoConversionMaterial == null) return PreflightFailure("bootstrap.preflight.video_material_missing", "The explicit VideoToLinearPremultiplied material is required.");
			if (_hapPremultiplyMaterial == null || _hapYCoCgMaterial == null || _hapAlphaMaterial == null || _hapDecodeShader == null)
				return PreflightFailure("bootstrap.preflight.hap_missing", "Explicit Hap conversion materials and compute shader are required.");

			var catalogManifest = _nodeTypeCatalog.ValidateManifest();
			if (catalogManifest.IsFailure) return catalogManifest;
			var catalogReferences = _nodeTypeCatalog.ValidateAssetReferences(_scene3dPrefab, _scene2dPrefab, _shaderGenerator, _shaderEffect, _shaderBlend2);
			if (catalogReferences.IsFailure) return catalogReferences;

			var shaderManifestAsset = ShaderManifest;
			if (shaderManifestAsset == null) return PreflightFailure("bootstrap.preflight.shader_manifest_missing", "The generated ShaderNodeManifest asset is required.");
			var shaderReferences = shaderManifestAsset.ValidateShaderReferences();
			if (shaderReferences.IsFailure) return shaderReferences;
			return ShaderNodeManifestValidator.Validate(shaderManifestAsset.BuildRuntimeManifest());
		}

		public CSharpFunctionalExtensions.Result<IVisualBindingProvider, Diagnostic> BuildProvider(IProjectFileSystem fileSystem, RenderTexturePool pool) {
			if (fileSystem == null || pool == null) return Failure("bootstrap.assets.arguments", "A file system and shared RenderTexturePool are required.");
			var preflight = Preflight();
			if (preflight.IsFailure) return CSharpFunctionalExtensions.Result.Failure<IVisualBindingProvider, Diagnostic>(preflight.Error);

			var shaders = new ShaderMaterialRegistry();
			var shaderManifestAsset = ShaderManifest;
			var shaderManifest = shaderManifestAsset.BuildRuntimeManifest();
			foreach (var pair in new[]
			{
				new System.Collections.Generic.KeyValuePair<string, Shader>("builtin.shader.generator", _shaderGenerator),
				new System.Collections.Generic.KeyValuePair<string, Shader>("builtin.shader.effect", _shaderEffect),
				new System.Collections.Generic.KeyValuePair<string, Shader>("builtin.shader.blend2", _shaderBlend2)
			}) {
				var entry = shaderManifest.Entries.Single(x => string.Equals(x.ShaderKey, pair.Key, StringComparison.Ordinal));
				var registered = shaders.Register(new ShaderMaterialBinding(pair.Key, pair.Value, descriptor: entry.ToShaderBinding()));
				if (registered.IsFailure) return CSharpFunctionalExtensions.Result.Failure<IVisualBindingProvider, Diagnostic>(registered.Error);
			}
			// Every generated ledger entry keeps a direct Shader reference in
			// the manifest asset.  Register by TypeId as well as family key so
			// all variants can share a family shader without collapsing to the
			// first variant in a key-only dictionary.
			foreach (var entry in shaderManifest.Entries.Where(x => !x.ShaderKey.StartsWith("builtin.", StringComparison.Ordinal))) {
				var assetEntry = shaderManifestAsset.Find(entry.TypeId.Value);
				if (assetEntry == null || assetEntry.Shader == null)
					return Failure("bootstrap.assets.shader_reference_missing", "A generated shader entry is missing its direct Shader reference: " + entry.TypeId.Value + ".");
				var registered = shaders.Register(new ShaderMaterialBinding(entry.ShaderKey, assetEntry.Shader, descriptor: entry.ToShaderBinding()));
				if (registered.IsFailure) return CSharpFunctionalExtensions.Result.Failure<IVisualBindingProvider, Diagnostic>(registered.Error);
			}

			var context = new ProjectContext();
			var videoProbe = new ExtensionVideoCapabilityProbe(new FileVideoMetadataProbe());
			var resolver = new ProjectMediaVideoResolver(() => context.Document, () => context.ProjectRoot, fileSystem, videoProbe);
			var flashResolver = new ProjectAssetFlashResolver(() => context.Document, () => context.ProjectRoot, fileSystem, resolver);
			var unityBackends = new UnityVideoBackendFactory(() => _videoHostPrefab == null
				? new GameObject("ShitDesigner.VideoBackend")
				: Instantiate(_videoHostPrefab));
			var hapGraphics = new UnityHapGraphicsCapabilityProbe();
			var hapBridge = new HapUnityGraphicsBridge(hapGraphics, _hapDecodeShader, _hapPremultiplyMaterial, _hapYCoCgMaterial, _hapAlphaMaterial);
			var hapApi = new PInvokeHapNativeApi(hapBridge);
			var nativeProbe = hapApi.ProbeInstalledBinary();
			if (!nativeProbe.IsAvailable) {
				hapBridge.Dispose();
				return Failure("bootstrap.hap.native_unavailable", nativeProbe.DiagnosticCode + ": " + nativeProbe.Message);
			}
			var hapBackends = new HapVideoBackendFactory(() => new HapNativeDecoder(hapApi));
			var backends = new CompositeVideoBackendFactory(unityBackends, hapBackends);
			var conversion = new VideoOutputSurfaceFrameAdapter(new UnityVideoFrameConversionPass(_videoConversionMaterial));
			var graphics = new VideoGraphicsCapabilities(hapGraphics.SupportsDirectCompressed, hapGraphics.SupportsCompute, hapGraphics.SupportsCpu);
			var provider = new ExplicitVisualBindingProvider(
				() => NodeCatalogBootstrap.CreateUnitySceneIsolation(),
				_scene3dPrefab,
				_scene2dPrefab,
				shaders,
				backends,
				resolver,
				flashResolver,
				conversion,
				graphics,
				pool,
				projectContextSetter: (document, root) => {
					context.Document = document;
					context.ProjectRoot = root ?? string.Empty;
				},
				applicationResources: new IDisposable[] { hapBridge, conversion });
			return CSharpFunctionalExtensions.Result.Success<IVisualBindingProvider, Diagnostic>(provider);
		}

		private static CSharpFunctionalExtensions.Result<IVisualBindingProvider, Diagnostic> Failure(string code, string message)
			=> CSharpFunctionalExtensions.Result.Failure<IVisualBindingProvider, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "bootstrap"));

		private static CSharpFunctionalExtensions.UnitResult<Diagnostic> PreflightFailure(string code, string message)
			=> CSharpFunctionalExtensions.UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "bootstrap"));

		private sealed class ProjectContext {
			public ProjectDocument Document;
			public string ProjectRoot = string.Empty;
		}

	}
}
