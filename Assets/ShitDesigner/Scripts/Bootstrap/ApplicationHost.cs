using System;
using ShitDesigner.Core;
using ShitDesigner.Input;
using ShitDesigner.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShitDesigner.Bootstrap {
	/// <summary>
	/// Unity scene entrypoint. The composition root itself is a
	/// plain IDisposable; this component owns only the Player lifecycle and
	/// hands frame execution to the shared ApplicationLoopDriver.
	/// </summary>
	public sealed class ApplicationHost : MonoBehaviour {
		[SerializeField] private PresentationRoot _presentationRoot;
		[SerializeField] private BootstrapAssets _assets;
		[SerializeField] private PanelSettings _panelSettings;
		[SerializeField] private MidiInputManager _midiInputManager;
		[SerializeField] private bool m_CreateOnAwake = true;

		private CompositionRoot m_Composition;
		private ApplicationLoopDriver _driver;
		private IWindowAdapter m_WindowAdapter;
		private WindowLifecycle m_WindowLifecycle;
		private PresentationHost _presentationHost;
		private StartupSequence m_Startup;
		public CompositionRoot Composition => m_Composition;
		public SystemState State => m_Startup?.State ?? SystemState.Cold;
		public Diagnostic StartupDiagnostic => m_Startup?.LastDiagnostic;
		public HandshakeReport HandshakeReport => m_Startup?.HandshakeReport;
		/// <summary>The Player-owned copy of the serialized PanelSettings.
		/// It is deliberately distinct from the asset so user UI Scale never
		/// dirties an authoring asset at runtime.</summary>
		public PanelSettings RuntimePanelSettings => _presentationHost?.RuntimePanelSettings;

		// ------------------------------------------------------------------ //
		// Unity lifecycle
		// ------------------------------------------------------------------ //

		private void Awake() {
			if (!m_CreateOnAwake) return;
			StartHost();
		}

		private void Update() {
			m_WindowLifecycle?.Tick();
		}

		private void OnDestroy() {
			m_Startup?.Shutdown();
		}

		// ------------------------------------------------------------------ //
		// Public API
		// ------------------------------------------------------------------ //

		/// <summary>
		/// Starts the Production host. This is called automatically by Awake if
		/// m_CreateOnAwake is true, but can be called manually if the host is
		/// configured in code before startup. The host can be started only once per
		/// scene load; it cannot be restarted after shutdown.
		/// </summary>
		private Result StartHost() {
			m_WindowAdapter ??= new WindowAdapter();
			m_WindowLifecycle = new WindowLifecycle(m_WindowAdapter);
			m_Startup ??= new StartupSequence();
			var started = m_Startup.Run(Preflight, Compose, Handshake, Activate);
			if (started.IsFailure) {
				Debug.LogError(started.Diagnostic == null ? "Production startup failed." : started.Diagnostic.Code + ": " + started.Diagnostic.Message, this);
			}
			else Debug.Log(State == SystemState.Degraded ? "[System] Degraded" : "[System] Online", this);
			return started;
		}

		/// <summary>
		/// Injected before Awake by the Player harness or a native
		/// platform bootstrap. Production uses the Unity adapter when no
		/// adapter was supplied.
		/// </summary>
		public void ConfigureWindowAdapter(IWindowAdapter adapter) {
			EnsureCold();
			m_WindowAdapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
		}



		private Result Preflight() {
			if (_assets == null)
				return Result.Failure(new Diagnostic(new DiagnosticCode("bootstrap.preflight.assets_missing"), Severity.Error,
					"An explicit BootstrapAssets component is required.", module: "bootstrap"));
			var result = _assets.Preflight();
			if (result.IsSuccess) Debug.Log("[Preflight] Production assets verified", this);
			return result;
		}

		private Result Compose() {
			if (_midiInputManager == null) _midiInputManager = GetComponent<MidiInputManager>();
			_presentationHost = new PresentationHost(gameObject, _presentationRoot, _panelSettings);
			m_Startup.RegisterShutdown(ShutdownStage.Teardown, () => {
				_presentationHost?.Dispose();
				_presentationHost = null;
			});
			var presentation = _presentationHost.Compose();
			if (presentation.IsFailure) return presentation;
			_presentationRoot = _presentationHost.Root;

			var created = new CompositionFactory(_assets, _presentationRoot, _midiInputManager).Create();
			if (created.IsFailure) return Result.Failure(created.Diagnostic);
			m_Composition = created.Value;
			m_Startup.RegisterShutdown(ShutdownStage.Stop, () => {
				m_Composition?.Dispose();
				m_Composition = null;
			});
			Debug.Log("[Compose] Application services composed", this);
			return Result.Success();
		}

		private Result<HandshakeReport> Handshake() {
			var result = m_Composition == null
				? Result<HandshakeReport>.Failure(new Diagnostic(new DiagnosticCode("bootstrap.handshake.composition_missing"), Severity.Error, "Production composition is unavailable.", module: "bootstrap"))
				: m_Composition.Handshake();
			if (result.IsSuccess) Debug.Log(result.Value.IsDegraded ? "[Handshake] Optional capabilities unavailable" : "[Handshake] Capabilities ready", this);
			return result;
		}

		private Result Activate() {
			var window = m_WindowLifecycle.Activate();
			if (window.IsFailure) return window;
			var presentation = _presentationHost.Activate(m_Composition.Presentation);
			if (presentation.IsFailure) return presentation;
			if (_driver == null) _driver = gameObject.AddComponent<ApplicationLoopDriver>();
			m_Composition.Capabilities.Changed += OnCapabilitiesChanged;
			m_Startup.RegisterShutdown(ShutdownStage.Drain, () => {
				if (m_Composition != null) m_Composition.Capabilities.Changed -= OnCapabilitiesChanged;
				_driver?.Disable();
				_driver = null;
			});
			_driver.Configure(m_Composition.Loop);
			Debug.Log("[Activate] Application loop started", this);
			return Result.Success();
		}

		private void OnCapabilitiesChanged(HandshakeReport report) {
			var previous = State;
			m_Startup.Observe(report);
			if (State != previous) Debug.Log(State == SystemState.Degraded ? "[System] Degraded" : "[System] Online", this);
		}

		private void EnsureCold() {
			if (m_Startup != null && m_Startup.State != SystemState.Cold && m_Startup.State != SystemState.Offline)
				throw new InvalidOperationException("Host configuration is only allowed before startup.");
		}
	}
}
