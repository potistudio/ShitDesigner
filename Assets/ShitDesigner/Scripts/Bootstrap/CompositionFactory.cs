using System;
using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using ShitDesigner.Input;
using ShitDesigner.Persistence;
using ShitDesigner.Presentation;
using ShitDesigner.Rendering;

namespace ShitDesigner.Bootstrap {
	/// <summary>Owns concrete production construction. The scene Host supplies
	/// serialized inputs but does not know the runtime implementation graph.</summary>
	internal sealed class CompositionFactory {
		private readonly BootstrapAssets _assets;
		private readonly PresentationRoot _presentationRoot;
		private readonly MidiInputManager _midiInputManager;

		public CompositionFactory(BootstrapAssets assets, PresentationRoot presentationRoot, MidiInputManager midiInputManager) {
			_assets = assets ?? throw new ArgumentNullException(nameof(assets));
			_presentationRoot = presentationRoot;
			_midiInputManager = midiInputManager;
		}

		public Result<CompositionRoot, Diagnostic> Create() {
			var fileSystem = new LocalProjectFileSystem();
			var pool = new RenderTexturePool();
			var provider = _assets.BuildProvider(fileSystem, pool);
			if (provider.IsFailure) {
				pool.Dispose();
				return Result.Failure<CompositionRoot, Diagnostic>(provider.Error);
			}

			return CompositionRoot.Create(fileSystem, provider.Value, pool: pool, presentationRoot: _presentationRoot,
				inputFactory: application => new InputPoller(application, midiManager: _midiInputManager),
				nodeTypeCatalog: _assets.NodeTypeCatalog, displayTransformShader: _assets.DisplayTransformShader);
		}
	}
}
