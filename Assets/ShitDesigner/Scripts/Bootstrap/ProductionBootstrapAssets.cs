using System;
using System.IO;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Media;
using ShitDesigner.Nodes;
using ShitDesigner.Persistence;
using ShitDesigner.Project;
using ShitDesigner.Rendering;
using UnityEngine;

namespace ShitDesigner.Bootstrap
{
    /// <summary>Explicit serialized production inputs.  The composition root
    /// never searches Resources, scenes, or global objects for these assets.
    /// Missing inputs are a startup failure with a visible diagnostic.</summary>
    public sealed class ProductionBootstrapAssets : MonoBehaviour
    {
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

        // Read-only seams keep production asset verification deterministic without
        // reflection.  The composition root still consumes the serialized fields.
        public GameObject Scene3dPrefab => _scene3dPrefab;
        public GameObject Scene2dPrefab => _scene2dPrefab;
        public NodeTypeCatalog NodeTypeCatalog => _nodeTypeCatalog;
        public Shader DisplayTransformShader => _displayTransformShader;

        public Result<IProductionVisualBindingProvider> BuildProvider(IProjectFileSystem fileSystem, RenderTexturePool pool)
        {
            if (fileSystem == null || pool == null) return Failure("bootstrap.assets.arguments", "A file system and shared RenderTexturePool are required.");
            if (_nodeTypeCatalog == null) return Failure("bootstrap.assets.catalog_missing", "The generated NodeTypeCatalog asset is required.");
            if (_scene3dPrefab == null || _scene2dPrefab == null) return Failure("bootstrap.assets.scene_missing", "Explicit 3D and 2D Scene prefabs are required.");
            if (_shaderGenerator == null || _shaderEffect == null || _shaderBlend2 == null) return Failure("bootstrap.assets.shader_missing", "All three builtin shader role assets are required.");
            if (_displayTransformShader == null) return Failure("bootstrap.assets.display_transform_missing", "The explicit DisplayTransform shader is required.");
            var catalogManifest = _nodeTypeCatalog.ValidateManifest();
            if (catalogManifest.IsFailure) return Result<IProductionVisualBindingProvider>.Failure(catalogManifest.Diagnostic);
            var catalogReferences = _nodeTypeCatalog.ValidateAssetReferences(_scene3dPrefab, _scene2dPrefab, _shaderGenerator, _shaderEffect, _shaderBlend2);
            if (catalogReferences.IsFailure) return Result<IProductionVisualBindingProvider>.Failure(catalogReferences.Diagnostic);
            if (_videoConversionMaterial == null) return Failure("bootstrap.assets.video_material_missing", "The explicit VideoToLinearPremultiplied material is required.");

            var shaders = new ShaderMaterialRegistry();
            foreach (var pair in new[]
            {
                new System.Collections.Generic.KeyValuePair<string, Shader>("builtin.shader.generator", _shaderGenerator),
                new System.Collections.Generic.KeyValuePair<string, Shader>("builtin.shader.effect", _shaderEffect),
                new System.Collections.Generic.KeyValuePair<string, Shader>("builtin.shader.blend2", _shaderBlend2)
            })
            {
                System.Collections.Generic.IDictionary<PortId, string> inputs = null;
                if (pair.Key == "builtin.shader.effect")
                    inputs = new System.Collections.Generic.Dictionary<PortId, string> { { new PortId("input"), "_MainTex" } };
                else if (pair.Key == "builtin.shader.blend2")
                    inputs = new System.Collections.Generic.Dictionary<PortId, string>
                    {
                        { new PortId("a"), "_TexA" },
                        { new PortId("b"), "_TexB" }
                    };
                var parameters = pair.Key == "builtin.shader.generator"
                    ? new System.Collections.Generic.Dictionary<ParameterId, string> { { new ParameterId("color"), "_Color" } }
                    : null;
                var registered = shaders.Register(new ShaderMaterialBinding(pair.Key, pair.Value, inputs, parameters));
                if (registered.IsFailure) return Result<IProductionVisualBindingProvider>.Failure(registered.Diagnostic);
            }

            var context = new ProductionProjectContext();
            var videoProbe = new ExtensionVideoCapabilityProbe(new FileVideoMetadataProbe());
            var resolver = new ProjectMediaVideoResolver(() => context.Document, () => context.ProjectRoot, fileSystem, videoProbe);
            var unityBackends = new UnityVideoBackendFactory(() => _videoHostPrefab == null
                ? new GameObject("ShitDesigner.VideoBackend")
                : Instantiate(_videoHostPrefab));
            if (_hapPremultiplyMaterial == null || _hapYCoCgMaterial == null || _hapAlphaMaterial == null || _hapDecodeShader == null)
                return Failure("bootstrap.assets.hap_missing", "Explicit Hap conversion materials and compute shader are required.");
            var hapGraphics = new UnityHapGraphicsCapabilityProbe();
            var hapBridge = new HapUnityGraphicsBridge(hapGraphics, _hapDecodeShader, _hapPremultiplyMaterial, _hapYCoCgMaterial, _hapAlphaMaterial);
            var hapApi = new PInvokeHapNativeApi(hapBridge);
            var nativeProbe = hapApi.ProbeInstalledBinary();
            if (!nativeProbe.IsAvailable)
            {
                hapBridge.Dispose();
                return Failure("bootstrap.hap.native_unavailable", nativeProbe.DiagnosticCode + ": " + nativeProbe.Message);
            }
            var hapBackends = new HapVideoBackendFactory(() => new HapNativeDecoder(hapApi));
            var backends = new CompositeVideoBackendFactory(unityBackends, hapBackends);
            var conversion = new VideoOutputSurfaceFrameAdapter(new UnityVideoFrameConversionPass(_videoConversionMaterial));
            var graphics = new VideoGraphicsCapabilities(hapGraphics.SupportsDirectCompressed, hapGraphics.SupportsCompute, hapGraphics.SupportsCpu);
            var provider = new ExplicitProductionVisualBindingProvider(
                () => NodeCatalogBootstrap.CreateUnitySceneIsolation(),
                _scene3dPrefab,
                _scene2dPrefab,
                shaders,
                backends,
                resolver,
                conversion,
                graphics,
                pool,
                projectContextSetter: (document, root) =>
                {
                    context.Document = document;
                    context.ProjectRoot = root ?? string.Empty;
                },
                applicationResources: new IDisposable[] { hapBridge, conversion });
            return Result<IProductionVisualBindingProvider>.Success(provider);
        }

        private static Result<IProductionVisualBindingProvider> Failure(string code, string message)
            => Result<IProductionVisualBindingProvider>.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "bootstrap"));

        private sealed class ProductionProjectContext
        {
            public ProjectDocument Document;
            public string ProjectRoot = string.Empty;
        }

    }
}
