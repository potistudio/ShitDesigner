using System;
using CSharpFunctionalExtensions;
using ShitDesigner.Core;
using ShitDesigner.Presentation;
using UnityEngine;
using UnityEngine.UIElements;

namespace ShitDesigner.Bootstrap {
	public enum SystemState {
		Cold, // Startup has not yet begun.
		Preflight, // Startup is performing preflight checks and preparing to compose the presentation and other runtime resources.
		Composing, // Startup is composing the presentation and other runtime resources.
		Handshaking, // Startup is probing optional external capabilities.
		Activating, // Startup is activating the presentation and other runtime resources.
		Online, // Startup has completed successfully and the system is online.
		Degraded, // Startup has completed, but one or more optional external capabilities are unavailable.
		Draining, // Startup is shutting down the system and draining resources.
		Stopping, // Startup is stopping the system and releasing resources.
		Teardown, // Startup is tearing down the system and releasing resources.
		Offline, // Startup has completed and the system is offline.
		Faulted // Startup has failed and the system is faulted.
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
		private readonly Func<Result<CapabilityStatus, Diagnostic>> _midiProbe;
		private readonly Func<Result<CapabilityStatus, Diagnostic>> _displayProbe;
		private readonly double _probeIntervalSeconds;
		private double _nextProbeTime = double.NegativeInfinity;

		public HandshakeReport CurrentReport { get; private set; }
		public event Action<HandshakeReport> Changed;

		public CapabilitySupervisor(Func<Result<CapabilityStatus, Diagnostic>> midiProbe, Func<Result<CapabilityStatus, Diagnostic>> displayProbe,
			double probeIntervalSeconds = DefaultProbeIntervalSeconds) {
			_midiProbe = midiProbe ?? throw new ArgumentNullException(nameof(midiProbe));
			_displayProbe = displayProbe ?? throw new ArgumentNullException(nameof(displayProbe));
			if (probeIntervalSeconds <= 0d || double.IsNaN(probeIntervalSeconds) || double.IsInfinity(probeIntervalSeconds))
				throw new ArgumentOutOfRangeException(nameof(probeIntervalSeconds));
			_probeIntervalSeconds = probeIntervalSeconds;
		}

		public Result<HandshakeReport, Diagnostic> Handshake() {
			_nextProbeTime = double.NegativeInfinity;
			return Result.Success<HandshakeReport, Diagnostic>(ProbeAndPublish());
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

		private static CapabilityStatus Probe(Func<Result<CapabilityStatus, Diagnostic>> probe, string name) {
			try {
				var result = probe();
				if (result.IsSuccess && result.Value != null) return result.Value;
				return CapabilityStatus.Unavailable(name, result.Error ?? ProbeFailure(name, null));
			}
			catch (Exception exception) { return CapabilityStatus.Unavailable(name, ProbeFailure(name, exception)); }
		}

		private static Diagnostic ProbeFailure(string name, Exception exception) => new Diagnostic(
			new DiagnosticCode("bootstrap.capability.probe_failed"), Severity.Warning,
			name + " capability probe failed.", module: "bootstrap",
			exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception));
	}

	internal sealed class WindowLifecycle {
		private readonly IWindowAdapter _adapter;

		public WindowLifecycle(IWindowAdapter adapter) {
			_adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
		}

		public UnitResult<Diagnostic> Activate() {
			ConfigureFramePacing();
			if (!_adapter.IsSupported) return UnitResult.Success<Diagnostic>();
			_adapter.SetWindowedSize(new WindowSize(WindowConstraints.InitialWidth, WindowConstraints.InitialHeight));
			EnforceMinimumSize();
			return UnitResult.Success<Diagnostic>();
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

		public UnitResult<Diagnostic> Compose() {
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
			return UnitResult.Success<Diagnostic>();
		}

		public UnitResult<Diagnostic> Activate(PresentationCoordinator coordinator) {
			if (_root == null) return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.presentation.root_missing"), Severity.Error, "PresentationRoot was not composed.", module: "bootstrap"));
			_root.Configure(coordinator ?? throw new ArgumentNullException(nameof(coordinator)));
			return UnitResult.Success<Diagnostic>();
		}

		public void Dispose() {
			if (RuntimePanelSettings != null) UnityEngine.Object.Destroy(RuntimePanelSettings);
			RuntimePanelSettings = null;
		}
	}
}
