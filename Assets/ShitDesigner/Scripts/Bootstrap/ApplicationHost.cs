using System;
using ShitDesigner.Core;
using ShitDesigner.Input;
using ShitDesigner.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShitDesigner.Bootstrap {
	/// <summary>Unity scene entrypoint. The composition root itself is a
	/// plain IDisposable; this component owns only the Player lifecycle and
	/// hands frame execution to the shared ApplicationLoopDriver.</summary>
	public sealed class ApplicationHost : MonoBehaviour {
		[SerializeField] private PresentationRoot _presentationRoot;
		[SerializeField] private ProductionBootstrapAssets _assets;
		[SerializeField] private PanelSettings _panelSettings;
		[SerializeField] private MidiInputManager _midiInputManager;
		[SerializeField] private bool _createOnAwake = true;
		private ProductionCompositionRoot _composition;
		private ProductionCompositionRoot _compositionOverride;
		private ApplicationLoopDriver _driver;
		private IProductionWindowAdapter _windowAdapter;
		private ProductionWindowLifecycle _windowLifecycle;
		private ProductionPresentationHost _presentationHost;
		private ProductionStartupSequence _startup;
		public ProductionCompositionRoot Composition => _composition;
		public ProductionSystemState State => _startup?.State ?? ProductionSystemState.Cold;
		public Diagnostic StartupDiagnostic => _startup?.LastDiagnostic;
		public HandshakeReport HandshakeReport => _startup?.HandshakeReport;
		/// <summary>The Player-owned copy of the serialized PanelSettings.
		/// It is deliberately distinct from the asset so user UI Scale never
		/// dirties an authoring asset at runtime.</summary>
		public PanelSettings RuntimePanelSettings => _presentationHost?.RuntimePanelSettings;

		/// <summary>Injected before Awake by the Player harness or a native
		/// platform bootstrap. Production uses the Unity adapter when no
		/// adapter was supplied.</summary>
		public void ConfigureWindowAdapter(IProductionWindowAdapter adapter) {
			EnsureCold();
			_windowAdapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
		}

		private void Awake() {
			if (!_createOnAwake || _composition != null) return;
			StartHost();
		}

		private Result StartHost() {
			_windowAdapter ??= new UnityProductionWindowAdapter();
			_windowLifecycle = new ProductionWindowLifecycle(_windowAdapter);
			_startup ??= new ProductionStartupSequence();
			var started = _startup.Run(Preflight, Compose, Handshake, Activate);
			if (started.IsFailure) {
				Debug.LogError(started.Diagnostic == null ? "Production startup failed." : started.Diagnostic.Code + ": " + started.Diagnostic.Message, this);
			}
			else Debug.Log(State == ProductionSystemState.Degraded ? "[System] Degraded" : "[System] Online", this);
			return started;
		}

		private Result Preflight() {
			if (_compositionOverride != null) {
				Debug.Log("[Preflight] Injected composition accepted", this);
				return Result.Success();
			}
			if (_assets == null)
				return Result.Failure(new Diagnostic(new DiagnosticCode("bootstrap.preflight.assets_missing"), Severity.Error,
					"An explicit ProductionBootstrapAssets component is required.", module: "bootstrap"));
			var result = _assets.Preflight();
			if (result.IsSuccess) Debug.Log("[Preflight] Production assets verified", this);
			return result;
		}

		private Result Compose() {
			if (_midiInputManager == null) _midiInputManager = GetComponent<MidiInputManager>();
			_presentationHost = new ProductionPresentationHost(gameObject, _presentationRoot, _panelSettings);
			_startup.RegisterShutdown(ShutdownStage.Teardown, () => {
				_presentationHost?.Dispose();
				_presentationHost = null;
			});
			var presentation = _presentationHost.Compose();
			if (presentation.IsFailure) return presentation;
			_presentationRoot = _presentationHost.Root;

			if (_compositionOverride != null) {
				_composition = _compositionOverride;
				_compositionOverride = null;
			}
			else {
				var created = new CompositionFactory(_assets, _presentationRoot, _midiInputManager).Create();
				if (created.IsFailure) return Result.Failure(created.Diagnostic);
				_composition = created.Value;
			}
			_startup.RegisterShutdown(ShutdownStage.Stop, () => {
				_composition?.Dispose();
				_composition = null;
			});
			Debug.Log("[Compose] Application services composed", this);
			return Result.Success();
		}

		private Result<HandshakeReport> Handshake() {
			var result = _composition == null
				? Result<HandshakeReport>.Failure(new Diagnostic(new DiagnosticCode("bootstrap.handshake.composition_missing"), Severity.Error, "Production composition is unavailable.", module: "bootstrap"))
				: _composition.Handshake();
			if (result.IsSuccess) Debug.Log(result.Value.IsDegraded ? "[Handshake] Optional capabilities unavailable" : "[Handshake] Capabilities ready", this);
			return result;
		}

		private Result Activate() {
			var window = _windowLifecycle.Activate();
			if (window.IsFailure) return window;
			var presentation = _presentationHost.Activate(_composition.Presentation);
			if (presentation.IsFailure) return presentation;
			if (_driver == null) _driver = gameObject.AddComponent<ApplicationLoopDriver>();
			_startup.RegisterShutdown(ShutdownStage.Drain, () => {
				_driver?.Disable();
				_driver = null;
			});
			_driver.Configure(_composition.Loop);
			Debug.Log("[Activate] Application loop started", this);
			return Result.Success();
		}

		private void Update() => _windowLifecycle?.Tick();

		public Result Configure(ProductionCompositionRoot composition) {
			EnsureCold();
			_compositionOverride = composition ?? throw new ArgumentNullException(nameof(composition));
			return StartHost();
		}

		private void OnDestroy() {
			_startup?.Shutdown();
			_compositionOverride?.Dispose();
			_compositionOverride = null;
			_windowLifecycle = null;
		}

		private void EnsureCold() {
			if (_startup != null && _startup.State != ProductionSystemState.Cold && _startup.State != ProductionSystemState.Offline)
				throw new InvalidOperationException("Host configuration is only allowed before startup.");
		}
	}
}
