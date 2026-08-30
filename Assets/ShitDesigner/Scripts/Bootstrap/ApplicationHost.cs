using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using ShitDesigner.Core;
using ShitDesigner.Input;
using ShitDesigner.Presentation;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace ShitDesigner.Bootstrap {
	/// <summary>
	/// Unity scene entrypoint. The composition root itself is a
	/// plain IDisposable; this component owns only the Player lifecycle and
	/// hands frame execution to the shared ApplicationLoopDriver.
	/// </summary>
	public sealed class ApplicationHost : MonoBehaviour {
		[SerializeField] private PresentationRoot _presentationRoot;
		[SerializeField] private BootstrapAssets m_Assets;
		[SerializeField] private PanelSettings _panelSettings;
		[SerializeField] private MidiInputManager m_MidiInputManager;
		[SerializeField] private bool m_CreateOnAwake = true;

		private CompositionRoot m_Composition;
		private ApplicationLoopDriver _driver;
		private IWindowAdapter m_WindowAdapter;
		private WindowLifecycle m_WindowLifecycle;
		private PresentationHost m_PresentationHost;
		private readonly List<Action> _drainShutdown = new List<Action>();
		private readonly List<Action> _stopShutdown = new List<Action>();
		private readonly List<Action> m_TeardownShutdown = new List<Action>();
		private readonly List<Diagnostic> m_ShutdownDiagnostics = new List<Diagnostic>();

		private SystemState m_State = SystemState.Cold;
		private Diagnostic _startupDiagnostic;
		private HandshakeReport m_HandshakeReport;
		public CompositionRoot Composition => m_Composition;
		public BootstrapAssets Assets => m_Assets;
		public SystemState State => m_State;
		public Diagnostic StartupDiagnostic => _startupDiagnostic;
		public HandshakeReport HandshakeReport => m_HandshakeReport;
		public IReadOnlyList<Diagnostic> ShutdownDiagnostics => m_ShutdownDiagnostics;
		/// <summary>The Player-owned copy of the serialized PanelSettings.
		/// It is deliberately distinct from the asset so user UI Scale never
		/// dirties an authoring asset at runtime.</summary>
		public PanelSettings RuntimePanelSettings => m_PresentationHost?.RuntimePanelSettings;

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
			Shutdown();
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
		private UnitResult<Diagnostic> StartHost() {
			if (m_State != SystemState.Cold && m_State != SystemState.Offline)
				return Failure("bootstrap.startup.state", "Production startup can only begin from Cold or Offline.");

			_startupDiagnostic = null;
			m_HandshakeReport = null;
			m_ShutdownDiagnostics.Clear();
			ClearShutdownActions();
			RegisterMetalUiOverlayOwnership();
			m_WindowAdapter ??= new WindowAdapter();
			m_WindowLifecycle = new WindowLifecycle(m_WindowAdapter);

			var started = Execute(SystemState.Preflight, Preflight);
			if (started.IsSuccess) started = Execute(SystemState.Composing, Compose);
			if (started.IsSuccess) started = Execute(SystemState.Handshaking, Handshake);
			if (started.IsSuccess) started = Execute(SystemState.Activating, Activate);
			if (started.IsSuccess) m_State = m_HandshakeReport.IsDegraded ? SystemState.Degraded : SystemState.Online;

			if (started.IsFailure) {
				Debug.LogError(started.Error == null ? "Production startup failed." : started.Error.Code + ": " + started.Error.Message, this);
			}
			else {
				Debug.Log(m_State == SystemState.Degraded ? "[System] Degraded" : "[System] Online", this);
			}

			return started;
		}

		private void RegisterMetalUiOverlayOwnership() {
			RenderPipelineManager.beginContextRendering += UseNativeUiOverlayOnMetal;
			m_TeardownShutdown.Add(() => RenderPipelineManager.beginContextRendering -= UseNativeUiOverlayOnMetal);
		}

		private static void UseNativeUiOverlayOnMetal(ScriptableRenderContext context, List<Camera> cameras) {
			if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal) return;
			// URP 6000.5 records screen-space UI into the active RenderGraph
			// target by default.  Offscreen camera requests can leave that pass
			// paired with the Retina backbuffer descriptor on Metal.  Returning
			// ownership to Unity renders the same overlay after SRP instead.
			SupportedRenderingFeatures.active.rendersUIOverlay = false;
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

		private UnitResult<Diagnostic> Preflight() {
			if (m_Assets == null)
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.preflight.assets_missing"), Severity.Error,
					"An explicit BootstrapAssets component is required.", module: "bootstrap"));

			var asset_result = m_Assets.Preflight();
			if (asset_result.IsSuccess) Debug.Log("[Preflight] Production assets verified", this);
			return asset_result;
		}

		private UnitResult<Diagnostic> Compose() {
			if (m_MidiInputManager == null)
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.composition.midi_missing"), Severity.Error,
					"A MidiInputManager component is required.", module: "bootstrap"));

			m_PresentationHost = new PresentationHost(gameObject, _presentationRoot, _panelSettings);
			m_TeardownShutdown.Add(() => {
				m_PresentationHost?.Dispose();
				m_PresentationHost = null;
			});
			var presentation = m_PresentationHost.Compose();
			if (presentation.IsFailure) return presentation;
			_presentationRoot = m_PresentationHost.Root;

			var created = new CompositionFactory(m_Assets, _presentationRoot, m_MidiInputManager).Create();
			if (created.IsFailure) return UnitResult.Failure<Diagnostic>(created.Error);
			m_Composition = created.Value;
			_stopShutdown.Add(() => {
				m_Composition?.Dispose();
				m_Composition = null;
			});
			Debug.Log("[Compose] Application services composed", this);
			return UnitResult.Success<Diagnostic>();
		}

		private UnitResult<Diagnostic> Handshake() {
			var result = m_Composition == null
				? Result.Failure<HandshakeReport, Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.handshake.composition_missing"), Severity.Error, "Production composition is unavailable.", module: "bootstrap"))
				: m_Composition.Handshake();
			if (result.IsFailure) return UnitResult.Failure<Diagnostic>(result.Error);
			if (result.Value == null)
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.handshake.report_missing"), Severity.Error,
					"Production handshake completed without a report.", module: "bootstrap"));
			m_HandshakeReport = result.Value;
			Debug.Log(m_HandshakeReport.IsDegraded ? "[Handshake] Optional capabilities unavailable" : "[Handshake] Capabilities ready", this);
			return UnitResult.Success<Diagnostic>();
		}

		private UnitResult<Diagnostic> Activate() {
			var window = m_WindowLifecycle.Activate();
			if (window.IsFailure) return window;
			var presentation = m_PresentationHost.Activate(m_Composition.Presentation);
			if (presentation.IsFailure) return presentation;
			if (_driver == null) _driver = gameObject.AddComponent<ApplicationLoopDriver>();
			m_Composition.Capabilities.Changed += OnCapabilitiesChanged;
			_drainShutdown.Add(() => {
				if (m_Composition != null) m_Composition.Capabilities.Changed -= OnCapabilitiesChanged;
				_driver?.Disable();
				_driver = null;
			});
			_driver.Configure(m_Composition.Loop);
			Debug.Log("[Activate] Application loop started", this);
			return UnitResult.Success<Diagnostic>();
		}

		private void OnCapabilitiesChanged(HandshakeReport report) {
			var previous = State;
			if (report == null) throw new ArgumentNullException(nameof(report));
			if (m_State == SystemState.Online || m_State == SystemState.Degraded) {
				m_HandshakeReport = report;
				m_State = report.IsDegraded ? SystemState.Degraded : SystemState.Online;
			}
			if (State != previous) Debug.Log(State == SystemState.Degraded ? "[System] Degraded" : "[System] Online", this);
		}

		private void EnsureCold() {
			if (m_State != SystemState.Cold && m_State != SystemState.Offline)
				throw new InvalidOperationException("Host configuration is only allowed before startup.");
		}

		private void Shutdown() {
			if (m_State == SystemState.Cold || m_State == SystemState.Offline) return;
			ReleaseOwned();
			m_State = SystemState.Offline;
		}

		private UnitResult<Diagnostic> Execute(SystemState state, Func<UnitResult<Diagnostic>> phase) {
			m_State = state;

			try {
				var result = phase();
				return result.IsFailure ? Fail(result.Error) : result;
			}
			catch (Exception exception) {
				return Fail(new Diagnostic(new DiagnosticCode("bootstrap.startup.phase_failed"), Severity.Error,
					state + " phase failed.", module: "bootstrap", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		private void ReleaseOwned() {
			ExecuteShutdown(SystemState.Draining, _drainShutdown);
			ExecuteShutdown(SystemState.Stopping, _stopShutdown);
			ExecuteShutdown(SystemState.Teardown, m_TeardownShutdown);
			ClearShutdownActions();
		}

		private void ExecuteShutdown(SystemState state, List<Action> actions) {
			m_State = state;
			for (var index = actions.Count - 1; index >= 0; index--) {
				try { actions[index](); }
				catch (Exception exception) {
					var diagnostic = new Diagnostic(new DiagnosticCode("bootstrap.shutdown.phase_failed"), Severity.Error,
						state + " phase failed during shutdown.", module: "bootstrap", exception: DiagnosticExceptionInfo.FromException(exception));
					_startupDiagnostic = diagnostic;
					m_ShutdownDiagnostics.Add(diagnostic);
				}
			}
		}

		private UnitResult<Diagnostic> Fail(Diagnostic diagnostic) {
			var startupDiagnostic = diagnostic ?? new Diagnostic(new DiagnosticCode("bootstrap.startup.unknown_failure"), Severity.Error, "Startup failed without a diagnostic.", module: "bootstrap");
			_startupDiagnostic = startupDiagnostic;
			ReleaseOwned();
			_startupDiagnostic = startupDiagnostic;
			m_State = SystemState.Faulted;
			return UnitResult.Failure<Diagnostic>(startupDiagnostic);
		}

		private void ClearShutdownActions() {
			_drainShutdown.Clear();
			_stopShutdown.Clear();
			m_TeardownShutdown.Clear();
		}

		private UnitResult<Diagnostic> Failure(string code, string message)
			=> Fail(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "bootstrap"));
	}
}
