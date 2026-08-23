using System;
using System.Collections.Generic;
using ShitDesigner.Application;
using ShitDesigner.Core;
using UnityEngine;

namespace ShitDesigner.Input
{
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

        private sealed class RuntimeBinding
        {
            public MidiLiveControlBinding Definition { get; }
            public LogicalControlId LiveControlId { get; }
            public RuntimeBinding(MidiLiveControlBinding definition, LogicalControlId liveControlId) { Definition = definition; LiveControlId = liveControlId; }
        }

        [SerializeField, Min(0)] private int _deviceId;
        [SerializeField] private bool _openOnConfigure = true;
        [SerializeField] private List<MidiLiveControlBinding> _bindings = new List<MidiLiveControlBinding>();

        private readonly List<RuntimeBinding> _runtimeBindings = new List<RuntimeBinding>();
        private IMidiInputApplicationPort _midiApplication;
        private ILiveControlApplicationPort _liveControlApplication;
        private IMidiInputSource _source;
        private bool _ownsSource;

        public int DeviceId => _deviceId;
        public string DeviceName => _source?.DeviceName ?? string.Empty;
        public string LastError { get; private set; } = string.Empty;
        public bool IsOpen => _source != null;
        public IReadOnlyList<MidiLiveControlBinding> Bindings => _bindings;

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
            RefreshBindings();

            if (source != null)
            {
                _source = source;
                _ownsSource = false;
                return;
            }
            if (!_openOnConfigure) return;

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
            LastError = string.Empty;
            for (var index = 0; index < _bindings.Count; index++)
            {
                var binding = _bindings[index];
                if (binding == null)
                {
                    if (string.IsNullOrEmpty(LastError)) LastError = "Binding " + index + ": Binding is missing.";
                    continue;
                }
                if (binding.TryResolve(out var id, out var error)) _runtimeBindings.Add(new RuntimeBinding(binding, id));
                else if (string.IsNullOrEmpty(LastError)) LastError = "Binding " + index + ": " + error;
            }
            if (!string.IsNullOrEmpty(LastError)) Debug.LogWarning("MIDI Input Manager ignored an invalid binding. " + LastError, this);
        }

        public int Poll()
        {
            if (!isActiveAndEnabled || _source == null || _midiApplication == null || _liveControlApplication == null) return 0;
            var count = 0;
            while (count < MaximumEventsPerPoll && _source.TryDequeue(out var inputEvent))
            {
                var handled = false;
                foreach (var binding in _runtimeBindings)
                {
                    if (!binding.Definition.Matches(inputEvent.Control)) continue;
                    _liveControlApplication.SetLiveControlValue(binding.LiveControlId, binding.Definition.Normalize(inputEvent.RawValue));
                    handled = true;
                }
                if (!handled) _midiApplication.HandleMidi(inputEvent);
                count++;
            }
            return count;
        }

        public void Shutdown()
        {
            if (_ownsSource) _source?.Dispose();
            _source = null;
            _ownsSource = false;
            _midiApplication = null;
            _liveControlApplication = null;
            _runtimeBindings.Clear();
        }

        private void OnValidate()
        {
            if (_deviceId < 0) _deviceId = 0;
            if (UnityEngine.Application.isPlaying && _liveControlApplication != null) RefreshBindings();
        }

        private void OnDestroy() => Shutdown();
    }
}
