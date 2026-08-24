using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using ShitDesigner.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShitDesigner.Bootstrap {
	public enum SystemState {
		Cold,
		Preflight,
		Composing,
		Handshaking,
		Activating,
		Online,
		Degraded,
		Draining,
		Stopping,
		Teardown,
		Offline,
		Faulted
	}

	public enum CapabilityState {
		Ready,
		Unavailable,
		Deferred
	}

	public sealed class CapabilityStatus {
		public string Name { get; }
		public CapabilityState State { get; }
		public Diagnostic Diagnostic { get; }

		private CapabilityStatus(string name, CapabilityState state, Diagnostic diagnostic) {
			Name = string.IsNullOrWhiteSpace(name) ? throw new ArgumentException("A capability name is required.", nameof(name)) : name;
			State = state;
			Diagnostic = diagnostic;
		}

		public static CapabilityStatus Ready(string name) => new CapabilityStatus(name, CapabilityState.Ready, null);
		public static CapabilityStatus Unavailable(string name, Diagnostic diagnostic) => new CapabilityStatus(name, CapabilityState.Unavailable,
			diagnostic ?? throw new ArgumentNullException(nameof(diagnostic)));
		public static CapabilityStatus Deferred(string name) => new CapabilityStatus(name, CapabilityState.Deferred, null);

		public bool HasSameState(CapabilityStatus other) {
			if (other == null || !string.Equals(Name, other.Name, StringComparison.Ordinal) || State != other.State) return false;
			var code = Diagnostic?.Code.Value ?? string.Empty;
			var otherCode = other.Diagnostic?.Code.Value ?? string.Empty;
			return string.Equals(code, otherCode, StringComparison.Ordinal)
				&& string.Equals(Diagnostic?.Message ?? string.Empty, other.Diagnostic?.Message ?? string.Empty, StringComparison.Ordinal);
		}
	}

	public sealed class HandshakeReport {
		public CapabilityStatus Midi { get; }
		public CapabilityStatus Display { get; }
		public bool IsDegraded => Midi.State == CapabilityState.Unavailable || Display.State == CapabilityState.Unavailable;

		public HandshakeReport(CapabilityStatus midi, CapabilityStatus display) {
			Midi = midi ?? throw new ArgumentNullException(nameof(midi));
			Display = display ?? throw new ArgumentNullException(nameof(display));
		}

		public static HandshakeReport Ready => new HandshakeReport(CapabilityStatus.Ready("midi"), CapabilityStatus.Ready("display"));
		public bool HasSameState(HandshakeReport other) => other != null && Midi.HasSameState(other.Midi) && Display.HasSameState(other.Display);
	}

	/// <summary>Main-thread supervisor for optional external capabilities.
	/// Handshake captures the initial state; Tick keeps it current and publishes
	/// only meaningful transitions.</summary>
	public sealed class CapabilitySupervisor {
		public const double DefaultProbeIntervalSeconds = 1d;
		private readonly Func<Result<CapabilityStatus>> _midiProbe;
		private readonly Func<Result<CapabilityStatus>> _displayProbe;
		private readonly double _probeIntervalSeconds;
		private double _nextProbeTime = double.NegativeInfinity;

		public HandshakeReport CurrentReport { get; private set; }
		public event Action<HandshakeReport> Changed;

		public CapabilitySupervisor(Func<Result<CapabilityStatus>> midiProbe, Func<Result<CapabilityStatus>> displayProbe,
			double probeIntervalSeconds = DefaultProbeIntervalSeconds) {
			_midiProbe = midiProbe ?? throw new ArgumentNullException(nameof(midiProbe));
			_displayProbe = displayProbe ?? throw new ArgumentNullException(nameof(displayProbe));
			if (probeIntervalSeconds <= 0d || double.IsNaN(probeIntervalSeconds) || double.IsInfinity(probeIntervalSeconds))
				throw new ArgumentOutOfRangeException(nameof(probeIntervalSeconds));
			_probeIntervalSeconds = probeIntervalSeconds;
		}

		public Result<HandshakeReport> Handshake() {
			_nextProbeTime = double.NegativeInfinity;
			return Result<HandshakeReport>.Success(ProbeAndPublish());
		}

		public void Tick(double monotonicTime) {
			if (double.IsNaN(monotonicTime) || double.IsInfinity(monotonicTime) || monotonicTime < _nextProbeTime) return;
			_nextProbeTime = monotonicTime + _probeIntervalSeconds;
			ProbeAndPublish();
		}

		private HandshakeReport ProbeAndPublish() {
			var report = new HandshakeReport(Probe(_midiProbe, "midi"), Probe(_displayProbe, "display"));
			if (CurrentReport != null && CurrentReport.HasSameState(report)) return CurrentReport;
			CurrentReport = report;
			Changed?.Invoke(report);
			return report;
		}

		private static CapabilityStatus Probe(Func<Result<CapabilityStatus>> probe, string name) {
			try {
				var result = probe();
				if (result.IsSuccess && result.Value != null) return result.Value;
				return CapabilityStatus.Unavailable(name, result.Diagnostic ?? ProbeFailure(name, null));
			}
			catch (Exception exception) { return CapabilityStatus.Unavailable(name, ProbeFailure(name, exception)); }
		}

		private static Diagnostic ProbeFailure(string name, Exception exception) => new Diagnostic(
			new DiagnosticCode("bootstrap.capability.probe_failed"), Severity.Warning,
			name + " capability probe failed.", module: "bootstrap",
			exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception));
	}

	public enum ShutdownStage {
		Drain,
		Stop,
		Teardown
	}

	/// <summary>Small, deterministic startup state machine. Each callback owns
	/// one boundary; the sequence only enforces order and exposes failure.</summary>
	public sealed class StartupSequence {
		private readonly List<Action> _drain = new List<Action>();
		private readonly List<Action> _stop = new List<Action>();
		private readonly List<Action> _teardown = new List<Action>();
		private readonly List<Diagnostic> _shutdownDiagnostics = new List<Diagnostic>();
		public SystemState State { get; private set; } = SystemState.Cold;
		public Diagnostic LastDiagnostic { get; private set; }
		public HandshakeReport HandshakeReport { get; private set; }
		public IReadOnlyList<Diagnostic> ShutdownDiagnostics => _shutdownDiagnostics;

		public Result Run(Func<Result> preflight, Func<Result> compose, Func<Result<HandshakeReport>> handshake, Func<Result> activate) {
			if (State != SystemState.Cold && State != SystemState.Offline)
				return Failure("bootstrap.startup.state", "Production startup can only begin from Cold or Offline.");

			LastDiagnostic = null;
			HandshakeReport = null;
			_shutdownDiagnostics.Clear();
			ClearShutdownActions();
			var result = Execute(SystemState.Preflight, preflight);
			if (result.IsFailure) return result;
			result = Execute(SystemState.Composing, compose);
			if (result.IsFailure) return result;
			var handshakeResult = ExecuteHandshake(handshake);
			if (handshakeResult.IsFailure) return Result.Failure(handshakeResult.Diagnostic);
			HandshakeReport = handshakeResult.Value;
			result = Execute(SystemState.Activating, activate);
			if (result.IsFailure) return result;
			State = HandshakeReport.IsDegraded ? SystemState.Degraded : SystemState.Online;
			return Result.Success();
		}

		public void RegisterShutdown(ShutdownStage stage, Action action) {
			if (action == null) throw new ArgumentNullException(nameof(action));
			if (State != SystemState.Composing && State != SystemState.Handshaking && State != SystemState.Activating)
				throw new InvalidOperationException("Shutdown ownership can only be registered during startup.");
			Actions(stage).Add(action);
		}

		public void Observe(HandshakeReport report) {
			if (report == null) throw new ArgumentNullException(nameof(report));
			if (State != SystemState.Online && State != SystemState.Degraded) return;
			HandshakeReport = report;
			State = report.IsDegraded ? SystemState.Degraded : SystemState.Online;
		}

		public void Shutdown() {
			if (State == SystemState.Offline) return;
			ReleaseOwned();
			State = SystemState.Offline;
		}

		private Result Execute(SystemState state, Func<Result> phase) {
			State = state;
			if (phase == null) return Fail(new Diagnostic(new DiagnosticCode("bootstrap.startup.phase_missing"), Severity.Error, state + " phase is missing.", module: "bootstrap"));
			try {
				var result = phase();
				return result.IsFailure ? Fail(result.Diagnostic) : result;
			}
			catch (Exception exception) {
				return Fail(new Diagnostic(new DiagnosticCode("bootstrap.startup.phase_failed"), Severity.Error,
					state + " phase failed.", module: "bootstrap", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		private Result<HandshakeReport> ExecuteHandshake(Func<Result<HandshakeReport>> phase) {
			State = SystemState.Handshaking;
			if (phase == null)
				return FailHandshake(new Diagnostic(new DiagnosticCode("bootstrap.startup.phase_missing"), Severity.Error, "Handshaking phase is missing.", module: "bootstrap"));
			try {
				var result = phase();
				return result.IsFailure ? FailHandshake(result.Diagnostic) : result;
			}
			catch (Exception exception) {
				return FailHandshake(new Diagnostic(new DiagnosticCode("bootstrap.startup.phase_failed"), Severity.Error,
					"Handshaking phase failed.", module: "bootstrap", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		private void ReleaseOwned() {
			ExecuteShutdown(SystemState.Draining, _drain);
			ExecuteShutdown(SystemState.Stopping, _stop);
			ExecuteShutdown(SystemState.Teardown, _teardown);
			ClearShutdownActions();
		}

		private void ExecuteShutdown(SystemState state, List<Action> actions) {
			State = state;
			for (var index = actions.Count - 1; index >= 0; index--) {
				try { actions[index](); }
				catch (Exception exception) {
					var diagnostic = new Diagnostic(new DiagnosticCode("bootstrap.shutdown.phase_failed"), Severity.Error,
						state + " phase failed during shutdown.", module: "bootstrap", exception: DiagnosticExceptionInfo.FromException(exception));
					LastDiagnostic = diagnostic;
					_shutdownDiagnostics.Add(diagnostic);
				}
			}
		}

		private Result Fail(Diagnostic diagnostic) {
			var startupDiagnostic = diagnostic ?? new Diagnostic(new DiagnosticCode("bootstrap.startup.unknown_failure"), Severity.Error, "Startup failed without a diagnostic.", module: "bootstrap");
			LastDiagnostic = startupDiagnostic;
			ReleaseOwned();
			LastDiagnostic = startupDiagnostic;
			State = SystemState.Faulted;
			return Result.Failure(startupDiagnostic);
		}

		private Result<HandshakeReport> FailHandshake(Diagnostic diagnostic) {
			var failed = Fail(diagnostic);
			return Result<HandshakeReport>.Failure(failed.Diagnostic);
		}

		private List<Action> Actions(ShutdownStage stage) {
			switch (stage) {
				case ShutdownStage.Drain: return _drain;
				case ShutdownStage.Stop: return _stop;
				case ShutdownStage.Teardown: return _teardown;
				default: throw new ArgumentOutOfRangeException(nameof(stage), stage, null);
			}
		}

		private void ClearShutdownActions() {
			_drain.Clear();
			_stop.Clear();
			_teardown.Clear();
		}

		private Result Failure(string code, string message) => Fail(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "bootstrap"));
	}

	internal sealed class WindowLifecycle {
		private readonly IWindowAdapter _adapter;

		public WindowLifecycle(IWindowAdapter adapter) {
			_adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
		}

		public Result Activate() {
			ConfigureFramePacing();
			if (!_adapter.IsSupported || _adapter.IsFullscreen) return Result.Success();
			_adapter.SetWindowedSize(new WindowSize(WindowConstraints.InitialWidth, WindowConstraints.InitialHeight));
			EnforceMinimumSize();
			return Result.Success();
		}

		public void Tick() => EnforceMinimumSize();

		private void EnforceMinimumSize() {
			if (!_adapter.IsSupported || _adapter.IsFullscreen) return;
			var current = _adapter.CurrentSize;
			if (WindowConstraints.NeedsClamp(current))
				_adapter.SetWindowedSize(WindowConstraints.Clamp(current));
		}

		private static void ConfigureFramePacing() {
			var selected = QualitySettings.GetQualityLevel();
			for (var index = 0; index < QualitySettings.names.Length; index++) {
				QualitySettings.SetQualityLevel(index, applyExpensiveChanges: false);
				QualitySettings.vSyncCount = 0;
			}
			QualitySettings.SetQualityLevel(selected, applyExpensiveChanges: false);
			QualitySettings.vSyncCount = 0;
			UnityEngine.Application.targetFrameRate = ApplicationLoopDriverCore.HostTargetFramesPerSecond;
		}
	}

	internal sealed class PresentationHost : IDisposable {
		private readonly GameObject _owner;
		private readonly PanelSettings _panelSettingsSource;
		private PresentationRoot _root;

		public PresentationHost(GameObject owner, PresentationRoot root, PanelSettings panelSettingsSource) {
			_owner = owner != null ? owner : throw new ArgumentNullException(nameof(owner));
			_root = root;
			_panelSettingsSource = panelSettingsSource;
		}

		public PresentationRoot Root => _root;
		public PanelSettings RuntimePanelSettings { get; private set; }

		public Result Compose() {
			if (_root == null) _root = _owner.GetComponent<PresentationRoot>();
			if (_root == null) _root = _owner.AddComponent<PresentationRoot>();
			var document = _root.GetComponent<UIDocument>();
			if (document == null) document = _owner.AddComponent<UIDocument>();
			var source = _panelSettingsSource ?? document.panelSettings;
			RuntimePanelSettings = source == null ? ScriptableObject.CreateInstance<PanelSettings>() : UnityEngine.Object.Instantiate(source);
			RuntimePanelSettings.name = "ShitDesigner.RuntimePanelSettings";
			RuntimePanelSettings.hideFlags = HideFlags.DontSave;
			document.panelSettings = RuntimePanelSettings;
			_root.ConfigureDocument(document);
			return Result.Success();
		}

		public Result Activate(PresentationCoordinator coordinator) {
			if (_root == null) return Result.Failure(new Diagnostic(new DiagnosticCode("bootstrap.presentation.root_missing"), Severity.Error, "PresentationRoot was not composed.", module: "bootstrap"));
			_root.Configure(coordinator ?? throw new ArgumentNullException(nameof(coordinator)));
			return Result.Success();
		}

		public void Dispose() {
			if (RuntimePanelSettings != null) UnityEngine.Object.Destroy(RuntimePanelSettings);
			RuntimePanelSettings = null;
		}
	}
}
