using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using AOT;
using ShitDesigner.Application;
using ShitDesigner.Core;

namespace ShitDesigner.Input
{
    public sealed class MidiInputDeviceInfo
    {
        public uint Id { get; }
        public string Name { get; }

        internal MidiInputDeviceInfo(uint id, string name)
        {
            Id = id;
            Name = name ?? string.Empty;
        }
    }

    public interface IMidiInputSource : IDisposable
    {
        string DeviceName { get; }
        bool TryDequeue(out MidiInputEvent inputEvent);
    }

    public static class MidiShortMessageDecoder
    {
        public static bool TryDecode(string deviceName, uint packedMessage, out MidiInputEvent inputEvent)
        {
            var status = (byte)(packedMessage & 0xff);
            var data1 = (int)((packedMessage >> 8) & 0x7f);
            var data2 = (int)((packedMessage >> 16) & 0x7f);
            var channel = (status & 0x0f) + 1;
            MidiControl control;
            int value;

            switch (status & 0xf0)
            {
                case 0x80:
                    control = new MidiControl(deviceName, MidiControlKind.Note, channel, data1);
                    value = 0;
                    break;
                case 0x90:
                    control = new MidiControl(deviceName, MidiControlKind.Note, channel, data1);
                    value = data2;
                    break;
                case 0xb0:
                    control = new MidiControl(deviceName, MidiControlKind.ControlChange, channel, data1);
                    value = data2;
                    break;
                case 0xe0:
                    control = new MidiControl(deviceName, MidiControlKind.PitchBend, channel, 0);
                    value = data1 | (data2 << 7);
                    break;
                default:
                    inputEvent = default(MidiInputEvent);
                    return false;
            }

            inputEvent = new MidiInputEvent(control, value);
            return true;
        }
    }

    /// <summary>Windows WinMM MIDI input. Native callbacks only enqueue data;
    /// Application state is touched later by Poll on Unity's main thread.</summary>
    public sealed class WindowsMidiInputSource : IMidiInputSource
    {
        private const uint CallbackFunction = 0x00030000;
        private const uint MidiDataMessage = 0x3c3;
        private const uint NoError = 0;
        private readonly ConcurrentQueue<MidiInputEvent> _events = new ConcurrentQueue<MidiInputEvent>();
        private static readonly MidiInCallback Callback = OnMidiMessage;
        private IntPtr _handle;
        private GCHandle _selfHandle;
        private bool _hasSelfHandle;
        private bool _disposed;

        public string DeviceName { get; }

        public WindowsMidiInputSource(uint deviceId)
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) throw new PlatformNotSupportedException("WinMM MIDI input is only available on Windows.");
            var devices = GetDevices();
            if (deviceId >= devices.Count) throw new ArgumentOutOfRangeException(nameof(deviceId), "The MIDI input device does not exist.");
            DeviceName = devices[(int)deviceId].Name;
            _selfHandle = GCHandle.Alloc(this);
            _hasSelfHandle = true;
            try
            {
                ThrowIfError(midiInOpen(out _handle, deviceId, Callback, GCHandle.ToIntPtr(_selfHandle), CallbackFunction), "midiInOpen");
                ThrowIfError(midiInStart(_handle), "midiInStart");
            }
            catch
            {
                if (_handle != IntPtr.Zero) midiInClose(_handle);
                _handle = IntPtr.Zero;
                _selfHandle.Free();
                _hasSelfHandle = false;
                throw;
            }
        }

        public static IReadOnlyList<MidiInputDeviceInfo> GetDevices()
        {
            var devices = new List<MidiInputDeviceInfo>();
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return devices;
            var count = midiInGetNumDevs();
            for (uint id = 0; id < count; id++)
            {
                var caps = new MidiInCaps();
                ThrowIfError(midiInGetDevCaps(new UIntPtr(id), ref caps, (uint)Marshal.SizeOf(typeof(MidiInCaps))), "midiInGetDevCaps");
                devices.Add(new MidiInputDeviceInfo(id, caps.Name));
            }
            return devices;
        }

        public static bool TryOpenDefault(out WindowsMidiInputSource source, out string error)
        {
            source = null;
            error = string.Empty;
            try
            {
                if (GetDevices().Count == 0) { error = "No MIDI input devices were found."; return false; }
                source = new WindowsMidiInputSource(0);
                return true;
            }
            catch (Exception exception)
            {
                error = exception.Message;
                source?.Dispose();
                source = null;
                return false;
            }
        }

        public bool TryDequeue(out MidiInputEvent inputEvent) => _events.TryDequeue(out inputEvent);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_handle != IntPtr.Zero)
            {
                midiInStop(_handle);
                midiInReset(_handle);
                midiInClose(_handle);
                _handle = IntPtr.Zero;
            }
            if (_hasSelfHandle)
            {
                _selfHandle.Free();
                _hasSelfHandle = false;
            }
        }

        [MonoPInvokeCallback(typeof(MidiInCallback))]
        private static void OnMidiMessage(IntPtr midiIn, uint message, IntPtr instance, UIntPtr parameter1, UIntPtr parameter2)
        {
            if (message != MidiDataMessage || instance == IntPtr.Zero) return;
            var target = GCHandle.FromIntPtr(instance).Target as WindowsMidiInputSource;
            if (target == null || target._disposed) return;
            if (MidiShortMessageDecoder.TryDecode(target.DeviceName, unchecked((uint)parameter1.ToUInt64()), out var inputEvent)) target._events.Enqueue(inputEvent);
        }

        private static void ThrowIfError(uint result, string operation)
        {
            if (result == NoError) return;
            var message = "Windows MIDI error " + result;
            var buffer = new System.Text.StringBuilder(256);
            if (midiInGetErrorText(result, buffer, (uint)buffer.Capacity) == NoError) message = buffer.ToString();
            throw new Win32Exception((int)result, operation + " failed: " + message);
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MidiInCaps
        {
            public ushort ManufacturerId;
            public ushort ProductId;
            public uint DriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string Name;
            public uint Support;
        }

        [UnmanagedFunctionPointer(CallingConvention.Winapi)]
        private delegate void MidiInCallback(IntPtr midiIn, uint message, IntPtr instance, UIntPtr parameter1, UIntPtr parameter2);

        [DllImport("winmm.dll")] private static extern uint midiInGetNumDevs();
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "midiInGetDevCapsW")] private static extern uint midiInGetDevCaps(UIntPtr deviceId, ref MidiInCaps caps, uint capsSize);
        [DllImport("winmm.dll")] private static extern uint midiInOpen(out IntPtr midiIn, uint deviceId, MidiInCallback callback, IntPtr instance, uint flags);
        [DllImport("winmm.dll")] private static extern uint midiInStart(IntPtr midiIn);
        [DllImport("winmm.dll")] private static extern uint midiInStop(IntPtr midiIn);
        [DllImport("winmm.dll")] private static extern uint midiInReset(IntPtr midiIn);
        [DllImport("winmm.dll")] private static extern uint midiInClose(IntPtr midiIn);
        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "midiInGetErrorTextW")] private static extern uint midiInGetErrorText(uint error, System.Text.StringBuilder text, uint textLength);
    }

    public sealed class MidiInputRouter
    {
        private const int MaximumEventsPerPoll = 4096;
        private readonly IMidiInputApplicationPort _application;
        private readonly IMidiInputSource _source;

        public MidiInputRouter(IMidiInputApplicationPort application, IMidiInputSource source)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _source = source ?? throw new ArgumentNullException(nameof(source));
        }

        public int Poll()
        {
            var count = 0;
            while (count < MaximumEventsPerPoll && _source.TryDequeue(out var inputEvent))
            {
                _application.HandleMidi(inputEvent);
                count++;
            }
            return count;
        }
    }
}
