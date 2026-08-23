using System;

namespace ShitDesigner.Core
{
    public enum MidiControlKind
    {
        Note,
        ControlChange,
        PitchBend
    }

    /// <summary>Stable identity for a MIDI control crossing the Input/Application boundary.</summary>
    public readonly struct MidiControl : IEquatable<MidiControl>
    {
        public string DeviceName { get; }
        public MidiControlKind Kind { get; }
        public int Channel { get; }
        public int Number { get; }
        public int RawMinimum => 0;
        public int RawMaximum => Kind == MidiControlKind.PitchBend ? 16383 : 127;
        public string PhysicalId => DeviceName + ":" + Kind.ToString().ToLowerInvariant() + ":" + Channel + ":" + Number;
        public string ControlPath => "<MIDI>/" + DeviceName + "/" + Kind.ToString().ToLowerInvariant() + "/" + Channel + "/" + Number;

        public MidiControl(string deviceName, MidiControlKind kind, int channel, int number)
        {
            if (string.IsNullOrWhiteSpace(deviceName)) throw new ArgumentException("A MIDI device name is required.", nameof(deviceName));
            if (channel < 1 || channel > 16) throw new ArgumentOutOfRangeException(nameof(channel), "MIDI channels are numbered 1 through 16.");
            if (number < 0 || number > 127) throw new ArgumentOutOfRangeException(nameof(number), "MIDI control numbers must be between 0 and 127.");
            DeviceName = deviceName.Trim();
            Kind = kind;
            Channel = channel;
            Number = number;
        }

        public bool Equals(MidiControl other) => Kind == other.Kind && Channel == other.Channel && Number == other.Number && string.Equals(DeviceName, other.DeviceName, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is MidiControl other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(DeviceName, Kind, Channel, Number);
        public override string ToString() => PhysicalId;
        public static bool operator ==(MidiControl left, MidiControl right) => left.Equals(right);
        public static bool operator !=(MidiControl left, MidiControl right) => !left.Equals(right);
    }

    public readonly struct MidiInputEvent : IEquatable<MidiInputEvent>
    {
        public MidiControl Control { get; }
        public int RawValue { get; }

        public MidiInputEvent(MidiControl control, int rawValue)
        {
            if (rawValue < control.RawMinimum || rawValue > control.RawMaximum) throw new ArgumentOutOfRangeException(nameof(rawValue));
            Control = control;
            RawValue = rawValue;
        }

        public bool Equals(MidiInputEvent other) => Control.Equals(other.Control) && RawValue == other.RawValue;
        public override bool Equals(object obj) => obj is MidiInputEvent other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Control, RawValue);
        public static bool operator ==(MidiInputEvent left, MidiInputEvent right) => left.Equals(right);
        public static bool operator !=(MidiInputEvent left, MidiInputEvent right) => !left.Equals(right);
    }
}
