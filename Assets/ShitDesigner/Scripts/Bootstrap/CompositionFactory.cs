using System;
using ShitDesigner.Core;
using ShitDesigner.Input;
using ShitDesigner.Persistence;
using ShitDesigner.Presentation;
using ShitDesigner.Rendering;

namespace ShitDesigner.Bootstrap
{
    /// <summary>Owns concrete production construction. The scene Host supplies
    /// serialized inputs but does not know the runtime implementation graph.</summary>
    internal sealed class CompositionFactory
    {
        private readonly ProductionBootstrapAssets _assets;
        private readonly PresentationRoot _presentationRoot;
        private readonly MidiInputManager _midiInputManager;

        public CompositionFactory(ProductionBootstrapAssets assets, PresentationRoot presentationRoot, MidiInputManager midiInputManager)
        {
            _assets = assets ?? throw new ArgumentNullException(nameof(assets));
            _presentationRoot = presentationRoot;
            _midiInputManager = midiInputManager;
        }

        public Result<ProductionCompositionRoot> Create()
        {
            var fileSystem = new LocalProjectFileSystem();
            var pool = new RenderTexturePool();
            var provider = _assets.BuildProvider(fileSystem, pool);
            if (provider.IsFailure)
            {
                pool.Dispose();
                return Result<ProductionCompositionRoot>.Failure(provider.Diagnostic);
            }

            return ProductionCompositionRoot.Create(fileSystem, provider.Value, pool: pool, presentationRoot: _presentationRoot,
                inputFactory: application => new UnityProductionInputPoller(application, midiManager: _midiInputManager),
                nodeTypeCatalog: _assets.NodeTypeCatalog, displayTransformShader: _assets.DisplayTransformShader);
        }
    }
}
