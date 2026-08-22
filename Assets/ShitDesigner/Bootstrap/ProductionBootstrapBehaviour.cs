using System;
using System.IO;
using ShitDesigner.Application;
using ShitDesigner.Input;
using ShitDesigner.Persistence;
using ShitDesigner.Presentation;
using ShitDesigner.Rendering;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShitDesigner.Bootstrap
{
    /// <summary>Unity scene entrypoint. The composition root itself is a
    /// plain IDisposable; this component owns only the Player lifecycle and
    /// hands frame execution to the shared ApplicationLoopDriver.</summary>
    public sealed class ProductionBootstrapBehaviour : MonoBehaviour
    {
        [SerializeField] private PresentationRoot _presentationRoot;
        [SerializeField] private ProductionBootstrapAssets _assets;
        [SerializeField] private PanelSettings _panelSettings;
        [SerializeField] private bool _createOnAwake = true;
        [SerializeField] private bool _createDefaultProject = true;
        [SerializeField] private string _defaultProjectName = "Untitled";
        private ProductionCompositionRoot _composition;
        private ApplicationLoopDriver _driver;
        private PanelSettings _runtimePanelSettings;
        private IProductionWindowAdapter _windowAdapter;
        public ProductionCompositionRoot Composition => _composition;
        /// <summary>The Player-owned copy of the serialized PanelSettings.
        /// It is deliberately distinct from the asset so user UI Scale never
        /// dirties an authoring asset at runtime.</summary>
        public PanelSettings RuntimePanelSettings => _runtimePanelSettings;

        /// <summary>Injected before Awake by the Player harness or a native
        /// platform bootstrap. Production uses the Unity adapter when no
        /// adapter was supplied.</summary>
        public void ConfigureWindowAdapter(IProductionWindowAdapter adapter) => _windowAdapter = adapter;

        private void Awake()
        {
            if (!_createOnAwake || _composition != null) return;
            ConfigureFramePacing();
            _windowAdapter ??= new UnityProductionWindowAdapter();
            ConfigureInitialWindowSize();
            EnforceMinimumWindowSize();
            if (_presentationRoot == null) _presentationRoot = GetComponent<PresentationRoot>();
            if (_presentationRoot == null) _presentationRoot = gameObject.AddComponent<PresentationRoot>();
            var document = _presentationRoot.GetComponent<UIDocument>();
            if (document == null) document = gameObject.AddComponent<UIDocument>();
            var panelSettingsSource = _panelSettings ?? document.panelSettings;
            _runtimePanelSettings = panelSettingsSource == null
                ? ScriptableObject.CreateInstance<PanelSettings>()
                : Instantiate(panelSettingsSource);
            _runtimePanelSettings.name = "ShitDesigner.RuntimePanelSettings";
            _runtimePanelSettings.hideFlags = HideFlags.DontSave;
            document.panelSettings = _runtimePanelSettings;
            _presentationRoot.ConfigureDocument(document);
            if (_assets == null)
            {
                Debug.LogError("ShitDesigner bootstrap requires an explicit ProductionBootstrapAssets component.");
                return;
            }
            var pool = new RenderTexturePool();
            var provider = _assets.BuildProvider(new LocalProjectFileSystem(), pool);
            if (provider.IsFailure)
            {
                pool.Dispose();
                Debug.LogError(provider.Diagnostic == null ? "Production bindings are unavailable." : provider.Diagnostic.Message);
                return;
            }
            var created = ProductionCompositionRoot.Create(new LocalProjectFileSystem(), provider.Value, pool: pool, presentationRoot: _presentationRoot,
                nodeTypeCatalog: _assets.NodeTypeCatalog, displayTransformShader: _assets.DisplayTransformShader);
            if (created.IsFailure)
            {
                pool.Dispose();
                Debug.LogError(created.Diagnostic == null ? "Production composition could not be created." : created.Diagnostic.Message);
                return;
            }
            Configure(created.Value);
        }

        private void Update() => EnforceMinimumWindowSize();

        private void ConfigureInitialWindowSize()
        {
            if (_windowAdapter == null || !_windowAdapter.IsSupported || !_windowAdapter.IsWindowed) return;
            _windowAdapter.SetWindowedSize(new ProductionWindowSize(ProductionWindowConstraints.InitialWidth, ProductionWindowConstraints.InitialHeight));
        }

        private void EnforceMinimumWindowSize()
        {
            if (_windowAdapter == null || !_windowAdapter.IsSupported || !_windowAdapter.IsWindowed) return;
            var current = _windowAdapter.CurrentSize;
            if (ProductionWindowConstraints.NeedsClamp(current))
                _windowAdapter.SetWindowedSize(ProductionWindowConstraints.Clamp(current));
        }

        private static void ConfigureFramePacing()
        {
            var selected = QualitySettings.GetQualityLevel();
            for (var index = 0; index < QualitySettings.names.Length; index++)
            {
                QualitySettings.SetQualityLevel(index, applyExpensiveChanges: false);
                QualitySettings.vSyncCount = 0;
            }
            QualitySettings.SetQualityLevel(selected, applyExpensiveChanges: false);
            QualitySettings.vSyncCount = 0;
            // Keep vSync disabled so the desktop target is the active pacing
            // request. Application.targetFrameRate remains a trial value on
            // desktop; ApplicationLoopDriver performs one Tick per LateUpdate
            // and the Program target remains 60fps.
            UnityEngine.Application.targetFrameRate = ApplicationLoopDriverCore.ProductionHostTargetFramesPerSecond;
        }

        public void Configure(ProductionCompositionRoot composition)
        {
            _composition = composition ?? throw new ArgumentNullException(nameof(composition));
            if (_presentationRoot != null) _presentationRoot.Configure(_composition.Presentation);
            if (_driver == null) _driver = gameObject.AddComponent<ApplicationLoopDriver>();
            _driver.Configure(_composition.Loop);
            if (_createDefaultProject)
            {
                var defaultRoot = Path.Combine(UnityEngine.Application.persistentDataPath, "ShitDesigner", "Untitled");
                _composition.Application.NewProject(string.IsNullOrWhiteSpace(_defaultProjectName) ? "Untitled" : _defaultProjectName, defaultRoot, UnsavedChangesDecision.Discard);
            }
        }

        private void OnDestroy()
        {
            _driver?.Disable();
            _composition?.Dispose();
            _driver = null;
            _composition = null;
            if (_runtimePanelSettings != null) Destroy(_runtimePanelSettings);
            _runtimePanelSettings = null;
        }
    }
}
