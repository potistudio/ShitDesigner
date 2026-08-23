using System;
using System.Collections.Generic;
using ShitDesigner.Application;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Input
{
    public sealed class MidiLiveControlBindingState
    {
        public bool IsValid { get; internal set; }
        public string Error { get; internal set; } = string.Empty;
        public bool HasValue { get; internal set; }
        public int LastRawValue { get; internal set; }
        public float LastNormalizedValue { get; internal set; }
        public long MatchCount { get; internal set; }

        internal void Record(int rawValue, float normalizedValue)
        {
            HasValue = true;
            LastRawValue = rawValue;
            LastNormalizedValue = normalizedValue;
            MatchCount++;
        }
    }

    public sealed class MidiInputActivity
    {
        public MidiInputEvent InputEvent { get; }
        public int MatchedBindings { get; }
        public bool ApplicationConnected { get; }
        public bool ForwardedToMidiLearn { get; }

        internal MidiInputActivity(MidiInputEvent inputEvent, int matchedBindings, bool applicationConnected, bool forwardedToMidiLearn)
        {
            InputEvent = inputEvent;
            MatchedBindings = matchedBindings;
            ApplicationConnected = applicationConnected;
            ForwardedToMidiLearn = forwardedToMidiLearn;
        }
    }

    [Serializable]
    public sealed class MidiLiveControlBinding
    {
        [SerializeField] private string _liveControlId = string.Empty;
        [SerializeField] private MidiControlKind _messageType = MidiControlKind.ControlChange;
        [SerializeField, Range(1, 16)] private int _channel = 1;
        [SerializeField, Range(0, 127)] private int _number;
        [SerializeField] private int _rawMinimum;
        [SerializeField] private int _rawMaximum = 127;
        [SerializeField] private bool _invert;

        public string LiveControlId => _liveControlId ?? string.Empty;
        public MidiControlKind MessageType => _messageType;
        public int Channel => _channel;
        public int Number => _number;
        public int RawMinimum => _rawMinimum;
        public int RawMaximum => _rawMaximum;
        public bool Invert => _invert;

        public MidiLiveControlBinding() { }

        public MidiLiveControlBinding(string liveControlId, MidiControlKind messageType, int channel, int number, int rawMinimum = 0, int rawMaximum = 127, bool invert = false)
        {
            _liveControlId = liveControlId ?? string.Empty;
            _messageType = messageType;
            _channel = channel;
            _number = number;
            _rawMinimum = rawMinimum;
            _rawMaximum = rawMaximum;
            _invert = invert;
        }

        public bool TryResolve(out LogicalControlId id, out string error)
        {
            id = default(LogicalControlId);
            if (string.IsNullOrWhiteSpace(LiveControlId)) { error = "Select a Live Control."; return false; }
            if (!LogicalControlId.TryParseUuidV4(LiveControlId, out id)) { error = "Live Control ID must be a UUID v4."; return false; }
            if (_channel < 1 || _channel > 16) { error = "MIDI channel must be between 1 and 16."; return false; }
            if (_number < 0 || _number > 127) { error = "MIDI number must be between 0 and 127."; return false; }
            if (_rawMinimum >= _rawMaximum) { error = "Raw Minimum must be less than Raw Maximum."; return false; }
            error = string.Empty;
            return true;
        }

        public bool Matches(MidiControl control) => control.Kind == _messageType && control.Channel == _channel && control.Number == _number;

        public float Normalize(int rawValue)
        {
            if (_rawMinimum >= _rawMaximum) throw new InvalidOperationException("Raw Minimum must be less than Raw Maximum.");
            var normalized = Math.Min(1f, Math.Max(0f, (rawValue - _rawMinimum) / (float)(_rawMaximum - _rawMinimum)));
            return _invert ? 1f - normalized : normalized;
        }
    }

    [AddComponentMenu("ShitDesigner/Input/MIDI Input Manager")]
    [DisallowMultipleComponent]
    public sealed class MidiInputManager : MonoBehaviour
    {
        private const int MaximumEventsPerPoll = 4096;
        private const int RecentActivityCapacity = 12;

        private sealed class RuntimeBinding
        {
            public MidiLiveControlBinding Definition { get; }
            public LogicalControlId LiveControlId { get; }
            public MidiLiveControlBindingState State { get; }
            public RuntimeBinding(MidiLiveControlBinding definition, LogicalControlId liveControlId, MidiLiveControlBindingState state)
            {
                Definition = definition;
                LiveControlId = liveControlId;
                State = state;
            }
        }

        [SerializeField, Min(0)] private int _deviceId;
        [SerializeField] private bool _openOnConfigure = true;
        [SerializeField] private List<MidiLiveControlBinding> _bindings = new List<MidiLiveControlBinding>();

        private readonly List<RuntimeBinding> _runtimeBindings = new List<RuntimeBinding>();
        private readonly List<MidiLiveControlBindingState> _bindingStates = new List<MidiLiveControlBindingState>();
        private readonly List<MidiInputActivity> _recentActivity = new List<MidiInputActivity>();
        private IMidiInputApplicationPort _midiApplication;
        private ILiveControlApplicationPort _liveControlApplication;
        private IProjectApplicationReadPort _projectApplication;
        private IMidiInputSource _source;
        private bool _ownsSource;
        private bool _usesInjectedSource;

        public int DeviceId => _deviceId;
        public string DeviceName => _source?.DeviceName ?? string.Empty;
        public string LastError { get; private set; } = string.Empty;
        public bool IsOpen => _source != null;
        public bool IsConfigured => _midiApplication != null && _liveControlApplication != null;
        public IReadOnlyList<MidiLiveControlBinding> Bindings => _bindings;
        public IReadOnlyList<MidiLiveControlBindingState> BindingStates => _bindingStates;
        public IReadOnlyList<MidiInputActivity> RecentActivity => _recentActivity;
        public IReadOnlyList<LogicalControlReadModel> AvailableLiveControls
        {
            get
            {
                var project = _projectApplication?.ReadModel?.Project?.Model;
                return project?.LogicalControls ?? Array.Empty<LogicalControlReadModel>();
            }
        }
        public long ReceivedEventCount { get; private set; }
        public long MatchedBindingCount { get; private set; }
        public long ForwardedEventCount { get; private set; }
        public bool HasLastEvent { get; private set; }
        public MidiInputEvent LastEvent { get; private set; }

        public void SetBindings(IEnumerable<MidiLiveControlBinding> bindings)
        {
            _bindings = new List<MidiLiveControlBinding>(bindings ?? Array.Empty<MidiLiveControlBinding>());
            if (_liveControlApplication != null) RefreshBindings();
        }

        public void Configure(IMidiInputApplicationPort midiApplication, ILiveControlApplicationPort liveControlApplication, IMidiInputSource source = null)
        {
            Shutdown();
            _midiApplication = midiApplication ?? throw new ArgumentNullException(nameof(midiApplication));
            _liveControlApplication = liveControlApplication ?? throw new ArgumentNullException(nameof(liveControlApplication));
            _projectApplication = liveControlApplication as IProjectApplicationReadPort;
            RefreshBindings();

            if (source != null)
            {
                _source = source;
                _ownsSource = false;
                _usesInjectedSource = true;
                return;
            }
            if (!_openOnConfigure) return;

            OpenConfiguredDevice();
        }

        public void ApplyInspectorConfiguration(bool reopenDevice)
        {
            RefreshBindings();
            if (!UnityEngine.Application.isPlaying || !reopenDevice) return;
            if (_usesInjectedSource) return;
            CloseOwnedSource();
            if (_openOnConfigure) OpenConfiguredDevice();
        }

        public void ResetMonitor()
        {
            ReceivedEventCount = 0;
            MatchedBindingCount = 0;
            ForwardedEventCount = 0;
            HasLastEvent = false;
            LastEvent = default(MidiInputEvent);
            _recentActivity.Clear();
            foreach (var state in _bindingStates)
            {
                state.HasValue = false;
                state.LastRawValue = 0;
                state.LastNormalizedValue = 0f;
                state.MatchCount = 0;
            }
        }

        private void OpenConfiguredDevice()
        {
            LastError = string.Empty;

            try
            {
                _source = new WindowsMidiInputSource((uint)Math.Max(0, _deviceId));
                _ownsSource = true;
            }
            catch (Exception exception)
            {
                LastError = exception.Message;
                Debug.LogWarning("MIDI Input Manager could not open device " + _deviceId + ": " + LastError, this);
            }
        }

        public void RefreshBindings()
        {
            _runtimeBindings.Clear();
            _bindingStates.Clear();
            LastError = string.Empty;
            for (var index = 0; index < _bindings.Count; index++)
            {
                var binding = _bindings[index];
                var state = new MidiLiveControlBindingState();
                _bindingStates.Add(state);
                if (binding == null)
                {
                    state.Error = "Binding is missing.";
                    if (string.IsNullOrEmpty(LastError)) LastError = "Binding " + index + ": " + state.Error;
                    continue;
                }
                if (binding.TryResolve(out var id, out var error))
                {
                    state.IsValid = true;
                    _runtimeBindings.Add(new RuntimeBinding(binding, id, state));
                }
                else
                {
                    state.Error = error;
                    if (string.IsNullOrEmpty(LastError)) LastError = "Binding " + index + ": " + error;
                }
            }
            if (!string.IsNullOrEmpty(LastError)) Debug.LogWarning("MIDI Input Manager ignored an invalid binding. " + LastError, this);
        }

        public int Poll()
        {
            if (!isActiveAndEnabled || _source == null) return 0;
            var count = 0;
            while (count < MaximumEventsPerPoll && _source.TryDequeue(out var inputEvent))
            {
                ReceivedEventCount++;
                HasLastEvent = true;
                LastEvent = inputEvent;
                var handled = false;
                var matches = 0;
                foreach (var binding in _runtimeBindings)
                {
                    if (!binding.Definition.Matches(inputEvent.Control)) continue;
                    var normalizedValue = binding.Definition.Normalize(inputEvent.RawValue);
                    _liveControlApplication?.SetLiveControlValue(binding.LiveControlId, normalizedValue);
                    binding.State.Record(inputEvent.RawValue, normalizedValue);
                    MatchedBindingCount++;
                    matches++;
                    handled = true;
                }
                var forwarded = !handled && _midiApplication != null;
                if (forwarded)
                {
                    _midiApplication.HandleMidi(inputEvent);
                    ForwardedEventCount++;
                }
                _recentActivity.Insert(0, new MidiInputActivity(inputEvent, matches, IsConfigured, forwarded));
                if (_recentActivity.Count > RecentActivityCapacity) _recentActivity.RemoveAt(_recentActivity.Count - 1);
                count++;
            }
            return count;
        }

        public void Shutdown()
        {
            CloseOwnedSource();
            _source = null;
            _usesInjectedSource = false;
            _midiApplication = null;
            _liveControlApplication = null;
            _projectApplication = null;
            _runtimeBindings.Clear();
            _bindingStates.Clear();
            _recentActivity.Clear();
        }

        private void CloseOwnedSource()
        {
            if (_ownsSource) _source?.Dispose();
            _source = null;
            _ownsSource = false;
        }

        private void OnValidate()
        {
            if (_deviceId < 0) _deviceId = 0;
            if (UnityEngine.Application.isPlaying && _liveControlApplication != null) RefreshBindings();
        }

        private void Start()
        {
            if (_source != null || !_openOnConfigure) return;
            RefreshBindings();
            OpenConfiguredDevice();
        }

        private void Update() => Poll();

        private void OnDestroy() => Shutdown();
    }
}
