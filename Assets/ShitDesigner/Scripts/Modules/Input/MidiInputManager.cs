using System;
using System.Collections.Generic;
using ShitDesigner.Application;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Input {
	public sealed class MidiLiveControlBindingState {
		public bool IsValid { get; internal set; }
		public string Error { get; internal set; } = string.Empty;
		public bool HasValue { get; internal set; }
		public int LastRawValue { get; internal set; }
		public float LastNormalizedValue { get; internal set; }
		public long MatchCount { get; internal set; }

		internal void Record(int rawValue, float normalizedValue) {
			HasValue = true;
			LastRawValue = rawValue;
			LastNormalizedValue = normalizedValue;
			MatchCount++;
		}
	}

	public sealed class MidiInputActivity {
		public MidiInputEvent InputEvent { get; }
		public int MatchedBindings { get; }
		public bool ApplicationConnected { get; }
		public bool ForwardedToMidiLearn { get; }

		internal MidiInputActivity(MidiInputEvent inputEvent, int matchedBindings, bool applicationConnected, bool forwardedToMidiLearn) {
			InputEvent = inputEvent;
			MatchedBindings = matchedBindings;
			ApplicationConnected = applicationConnected;
			ForwardedToMidiLearn = forwardedToMidiLearn;
		}
	}

	[Serializable]
	public sealed class MidiLiveControlBinding {
		[SerializeField] private string _liveControlId = string.Empty;
		[SerializeField] private MidiControlKind _messageType = MidiControlKind.ControlChange;
		[SerializeField] private string m_DeviceName = string.Empty;
		[SerializeField, Range(1, 16)] private int _channel = 1;
		[SerializeField, Range(0, 127)] private int _number;
		[SerializeField] private int _rawMinimum;
		[SerializeField] private int _rawMaximum = 127;
		[SerializeField] private bool _invert;

		public string LiveControlId => _liveControlId ?? string.Empty;
		public MidiControlKind MessageType => _messageType;
		public string DeviceName => (m_DeviceName ?? string.Empty).Trim();
		public int Channel => _channel;
		public int Number => _number;
		public int RawMinimum => _rawMinimum;
		public int RawMaximum => _rawMaximum;
		public bool Invert => _invert;

		public MidiLiveControlBinding() { }

		public MidiLiveControlBinding(string liveControlId, MidiControlKind messageType, int channel, int number, int rawMinimum = 0, int rawMaximum = 127, bool invert = false,
			string deviceName = "") {
			_liveControlId = liveControlId ?? string.Empty;
			_messageType = messageType;
			m_DeviceName = deviceName ?? string.Empty;
			_channel = channel;
			_number = number;
			_rawMinimum = rawMinimum;
			_rawMaximum = rawMaximum;
			_invert = invert;
		}

		public bool TryResolve(out LogicalControlId id, out string error) {
			id = default(LogicalControlId);
			if (string.IsNullOrWhiteSpace(LiveControlId)) { error = "Select a Live Control."; return false; }
			if (!LogicalControlId.TryParseUuidV4(LiveControlId, out id)) { error = "Live Control ID must be a UUID v4."; return false; }
			if (_channel < 1 || _channel > 16) { error = "MIDI channel must be between 1 and 16."; return false; }
			if (_number < 0 || _number > 127) { error = "MIDI number must be between 0 and 127."; return false; }
			if (_rawMinimum >= _rawMaximum) { error = "Raw Minimum must be less than Raw Maximum."; return false; }
			error = string.Empty;
			return true;
		}

		public bool Matches(MidiControl control) => (string.IsNullOrEmpty(DeviceName) || string.Equals(control.DeviceName, DeviceName, StringComparison.Ordinal))
			&& control.Kind == _messageType && control.Channel == _channel && control.Number == _number;

		public float Normalize(int rawValue) {
			if (_rawMinimum >= _rawMaximum) throw new InvalidOperationException("Raw Minimum must be less than Raw Maximum.");
			var normalized = Math.Min(1f, Math.Max(0f, (rawValue - _rawMinimum) / (float)(_rawMaximum - _rawMinimum)));
			return _invert ? 1f - normalized : normalized;
		}
	}

	[AddComponentMenu("ShitDesigner/Input/MIDI Input Manager")]
	[DisallowMultipleComponent]
	public sealed class MidiInputManager : MonoBehaviour {
		private const int MaximumEventsPerPoll = 4096;
		private const int RecentActivityCapacity = 12;

		private sealed class RuntimeBinding {
			public MidiLiveControlBinding Definition { get; }
			public LogicalControlId LiveControlId { get; }
			public MidiLiveControlBindingState State { get; }
			public RuntimeBinding(MidiLiveControlBinding definition, LogicalControlId liveControlId, MidiLiveControlBindingState state) {
				Definition = definition;
				LiveControlId = liveControlId;
				State = state;
			}
		}

		[SerializeField, Min(0)] private int _deviceId;
		[SerializeField] private int m_SecondaryDeviceId = -1;
		[SerializeField] private bool _openOnConfigure = true;
		[SerializeField] private List<MidiLiveControlBinding> _bindings = new List<MidiLiveControlBinding>();

		private readonly List<RuntimeBinding> _runtimeBindings = new List<RuntimeBinding>();
		private readonly List<MidiLiveControlBindingState> _bindingStates = new List<MidiLiveControlBindingState>();
		private readonly List<MidiInputActivity> _recentActivity = new List<MidiInputActivity>();
		private IMidiInputApplicationPort _midiApplication;
		private ILiveControlApplicationPort _liveControlApplication;
		private IProjectApplicationReadPort _projectApplication;
		private readonly List<IMidiInputSource> m_Sources = new List<IMidiInputSource>();
		private readonly List<int> m_SourceDeviceIds = new List<int>();
		private bool _ownsSource;
		private bool _usesInjectedSource;
		private bool _deferOpenUntilReconnect;
		private string _reportedConnectionError = string.Empty;
		private int m_LaunchControlXl3RelativeEncoderRow;

		public int DeviceId => _deviceId;
		public int SecondaryDeviceId => m_SecondaryDeviceId;
		public string DeviceName => string.Join(", ", m_Sources.ConvertAll(source => source.DeviceName));
		public IReadOnlyList<string> DeviceNames => m_Sources.ConvertAll(source => source.DeviceName);
		public string LastError { get; private set; } = string.Empty;
		public bool IsOpen {
			get {
				if (_usesInjectedSource) return m_Sources.Count > 0 && m_Sources.TrueForAll(IsSourceAvailable);
				if (!IsDeviceOpen(_deviceId)) return false;
				return m_SecondaryDeviceId < 0 || m_SecondaryDeviceId == _deviceId || IsDeviceOpen(m_SecondaryDeviceId);
			}
		}
		public bool IsConfigured => _midiApplication != null && _liveControlApplication != null;
		public bool IsRoutingConnected => IsConfigured || InputReceived != null;
		public IReadOnlyList<MidiLiveControlBinding> Bindings => _bindings;
		public IReadOnlyList<MidiLiveControlBindingState> BindingStates => _bindingStates;
		public IReadOnlyList<MidiInputActivity> RecentActivity => _recentActivity;
		public IReadOnlyList<LogicalControlReadModel> AvailableLiveControls {
			get {
				var project = _projectApplication?.ReadModel?.Project?.Model;
				return project?.LogicalControls ?? Array.Empty<LogicalControlReadModel>();
			}
		}
		public long ReceivedEventCount { get; private set; }
		public long MatchedBindingCount { get; private set; }
		public long ForwardedEventCount { get; private set; }
		public bool HasLastEvent { get; private set; }
		public MidiInputEvent LastEvent { get; private set; }
		public event Action<MidiInputEvent> InputReceived;

		public void SetBindings(IEnumerable<MidiLiveControlBinding> bindings) {
			_bindings = new List<MidiLiveControlBinding>(bindings ?? Array.Empty<MidiLiveControlBinding>());
			if (_liveControlApplication != null) RefreshBindings();
		}

		public void Configure(IMidiInputApplicationPort midiApplication, ILiveControlApplicationPort liveControlApplication, IMidiInputSource source = null) {
			Shutdown();
			ConfigureApplications(midiApplication, liveControlApplication);

			if (source != null) {
				m_Sources.Add(source);
				m_SourceDeviceIds.Add(-1);
				_ownsSource = false;
				_usesInjectedSource = true;
				return;
			}
			if (!_openOnConfigure) return;

			OpenConfiguredDevice();
		}

		public void ConfigureSources(IMidiInputApplicationPort midiApplication, ILiveControlApplicationPort liveControlApplication,
			IEnumerable<IMidiInputSource> sources) {
			Shutdown();
			ConfigureApplications(midiApplication, liveControlApplication);
			foreach (var source in sources ?? throw new ArgumentNullException(nameof(sources))) {
				if (source == null) throw new ArgumentException("MIDI input sources cannot contain null.", nameof(sources));
				m_Sources.Add(source);
				m_SourceDeviceIds.Add(-1);
			}
			_ownsSource = false;
			_usesInjectedSource = true;
		}

		private void ConfigureApplications(IMidiInputApplicationPort midiApplication, ILiveControlApplicationPort liveControlApplication) {
			_midiApplication = midiApplication ?? throw new ArgumentNullException(nameof(midiApplication));
			_liveControlApplication = liveControlApplication ?? throw new ArgumentNullException(nameof(liveControlApplication));
			_projectApplication = liveControlApplication as IProjectApplicationReadPort;
			RefreshBindings();
		}

		public void ApplyInspectorConfiguration(bool reopenDevice) {
			RefreshBindings();
			if (!UnityEngine.Application.isPlaying || !reopenDevice) return;
			if (_usesInjectedSource) return;
			CloseOwnedSources();
			if (_openOnConfigure) OpenConfiguredDevice();
		}

		/// <summary>Prepares host-owned polling while deferring device discovery to its capability loop.</summary>
		public void InitializeForHostPolling() {
			Shutdown();
			RefreshBindings();
			_deferOpenUntilReconnect = true;
		}

		public void ConfigureLaunchControlXl3RelativeEncoder(int channel, int controlNumber) {
			if (!LaunchControlXl3DawModeProtocol.TryResolveRelativeEncoderRow(channel, controlNumber, out var row))
				throw new ArgumentException("Launch Control XL 3 relative encoders must use channel 16 and CC 77 through 100.");
			m_LaunchControlXl3RelativeEncoderRow = row;
			ApplyRequestedDeviceMode();
		}

		/// <summary>Retries an owned device that was absent during startup or
		/// became unavailable later. Injected sources keep their own lifetime.</summary>
		public bool TryReconnect() {
			if (_usesInjectedSource || !_openOnConfigure) return false;
			for (var index = m_Sources.Count - 1; index >= 0; index--) {
				if (IsSourceAvailable(m_Sources[index])) continue;
				m_Sources[index].Dispose();
				m_Sources.RemoveAt(index);
				m_SourceDeviceIds.RemoveAt(index);
			}
			OpenMissingConfiguredDevices();
			return IsOpen;
		}

		public void ResetMonitor() {
			ReceivedEventCount = 0;
			MatchedBindingCount = 0;
			ForwardedEventCount = 0;
			HasLastEvent = false;
			LastEvent = default(MidiInputEvent);
			_recentActivity.Clear();
			foreach (var state in _bindingStates) {
				state.HasValue = false;
				state.LastRawValue = 0;
				state.LastNormalizedValue = 0f;
				state.MatchCount = 0;
			}
		}

		private void OpenConfiguredDevice() {
			LastError = string.Empty;
			OpenMissingConfiguredDevices();
		}

		private void OpenMissingConfiguredDevices() {
			OpenDeviceIfMissing(_deviceId);
			if (m_SecondaryDeviceId >= 0 && m_SecondaryDeviceId != _deviceId) OpenDeviceIfMissing(m_SecondaryDeviceId);
			if (IsOpen) {
				LastError = string.Empty;
				_reportedConnectionError = string.Empty;
			}
		}

		private void OpenDeviceIfMissing(int deviceId) {
			if (m_SourceDeviceIds.Contains(deviceId)) return;
			IMidiInputSource source = null;
			try {
				source = MidiInputDevices.Open((uint)Math.Max(0, deviceId));
				ApplyRequestedDeviceMode(source);
				m_Sources.Add(source);
				m_SourceDeviceIds.Add(deviceId);
				_ownsSource = true;
			}
			catch (Exception exception) {
				source?.Dispose();
				LastError = "Device " + deviceId + ": " + exception.Message;
				if (string.Equals(_reportedConnectionError, LastError, StringComparison.Ordinal)) return;
				_reportedConnectionError = LastError;
				Debug.LogWarning("MIDI Input Manager could not open device " + deviceId + ": " + exception.Message, this);
			}
		}

		private void ApplyRequestedDeviceMode() {
			if (m_LaunchControlXl3RelativeEncoderRow == 0) return;
			foreach (var source in m_Sources) ApplyRequestedDeviceMode(source);
		}

		private void ApplyRequestedDeviceMode(IMidiInputSource source) {
			if (m_LaunchControlXl3RelativeEncoderRow == 0 || source == null) return;
			if (!LaunchControlXl3DawModeProtocol.IsLaunchControlXl3DawOutput(source.DeviceName)) return;
			if (source is ILaunchControlXl3DawModeController controller) controller.EnableRelativeEncoderRow(m_LaunchControlXl3RelativeEncoderRow);
		}

		public void RefreshBindings() {
			_runtimeBindings.Clear();
			_bindingStates.Clear();
			LastError = string.Empty;
			for (var index = 0; index < _bindings.Count; index++) {
				var binding = _bindings[index];
				var state = new MidiLiveControlBindingState();
				_bindingStates.Add(state);
				if (binding == null) {
					state.Error = "Binding is missing.";
					if (string.IsNullOrEmpty(LastError)) LastError = "Binding " + index + ": " + state.Error;
					continue;
				}
				if (binding.TryResolve(out var id, out var error)) {
					state.IsValid = true;
					_runtimeBindings.Add(new RuntimeBinding(binding, id, state));
				}
				else {
					state.Error = error;
					if (string.IsNullOrEmpty(LastError)) LastError = "Binding " + index + ": " + error;
				}
			}
			if (!string.IsNullOrEmpty(LastError)) Debug.LogWarning("MIDI Input Manager ignored an invalid binding. " + LastError, this);
		}

		public int Poll() {
			if (!isActiveAndEnabled || m_Sources.Count == 0) return 0;
			var count = 0;
			var receivedAny = true;
			while (count < MaximumEventsPerPoll && receivedAny) {
				receivedAny = false;
				foreach (var source in m_Sources) {
					if (count >= MaximumEventsPerPoll || !source.TryDequeue(out var inputEvent)) continue;
					receivedAny = true;
					ReceivedEventCount++;
					HasLastEvent = true;
					LastEvent = inputEvent;
					InputReceived?.Invoke(inputEvent);
					var handled = false;
					var matches = 0;
					foreach (var binding in _runtimeBindings) {
						if (!binding.Definition.Matches(inputEvent.Control)) continue;
						var normalizedValue = binding.Definition.Normalize(inputEvent.RawValue);
						binding.State.Record(inputEvent.RawValue, normalizedValue);
						_liveControlApplication?.SetLiveControlValue(binding.LiveControlId, normalizedValue);
						MatchedBindingCount++;
						matches++;
						handled = true;
					}
					var forwarded = !handled && _midiApplication != null;
					if (forwarded) {
						_midiApplication.HandleMidi(inputEvent);
						ForwardedEventCount++;
					}
					_recentActivity.Insert(0, new MidiInputActivity(inputEvent, matches, IsRoutingConnected, forwarded));
					if (_recentActivity.Count > RecentActivityCapacity) _recentActivity.RemoveAt(_recentActivity.Count - 1);
					count++;
				}
			}
			return count;
		}

		public void Shutdown() {
			CloseOwnedSources();
			m_Sources.Clear();
			m_SourceDeviceIds.Clear();
			m_LaunchControlXl3RelativeEncoderRow = 0;
			_usesInjectedSource = false;
			_deferOpenUntilReconnect = false;
			_midiApplication = null;
			_liveControlApplication = null;
			_projectApplication = null;
			_runtimeBindings.Clear();
			_bindingStates.Clear();
			_recentActivity.Clear();
		}

		private void CloseOwnedSources() {
			if (_ownsSource) foreach (var source in m_Sources) source.Dispose();
			m_Sources.Clear();
			m_SourceDeviceIds.Clear();
			_ownsSource = false;
		}

		private static bool IsSourceAvailable(IMidiInputSource source)
			=> source != null && (!(source is IMidiInputAvailability availability) || availability.IsAvailable);

		private bool IsDeviceOpen(int deviceId) {
			var index = m_SourceDeviceIds.IndexOf(deviceId);
			return index >= 0 && index < m_Sources.Count && IsSourceAvailable(m_Sources[index]);
		}

		private void OnValidate() {
			if (_deviceId < 0) _deviceId = 0;
			if (m_SecondaryDeviceId < -1) m_SecondaryDeviceId = -1;
			if (UnityEngine.Application.isPlaying && _liveControlApplication != null) RefreshBindings();
		}

		private void Start() {
			if (m_Sources.Count > 0 || !_openOnConfigure || _deferOpenUntilReconnect) return;
			RefreshBindings();
			OpenConfiguredDevice();
		}

		private void OnDisable() => Shutdown();
		private void OnDestroy() => Shutdown();
	}
}
