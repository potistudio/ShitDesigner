<#
.SYNOPSIS
Lists Windows MIDI input devices and monitors messages without starting Unity.

.EXAMPLE
.\Tools\Test-MidiController.ps1 -ListOnly

.EXAMPLE
.\Tools\Test-MidiController.ps1

.EXAMPLE
.\Tools\Test-MidiController.ps1 -Device 'Launch Control'
#>
[CmdletBinding()]
param(
    [string]$Device = '0',

    [switch]$ListOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not [Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([Runtime.InteropServices.OSPlatform]::Windows)) {
    throw 'This script uses the Windows MIDI API (winmm.dll) and only runs on Windows.'
}

if (-not ('ShitDesigner.Tools.MidiInputMonitor' -as [type])) {
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ShitDesigner.Tools
{
    public sealed class MidiDeviceInfo
    {
        public uint Id { get; private set; }
        public string Name { get; private set; }
        public ushort ManufacturerId { get; private set; }
        public ushort ProductId { get; private set; }
        public uint DriverVersion { get; private set; }

        internal MidiDeviceInfo(uint id, string name, ushort manufacturerId, ushort productId, uint driverVersion)
        {
            Id = id;
            Name = name;
            ManufacturerId = manufacturerId;
            ProductId = productId;
            DriverVersion = driverVersion;
        }
    }

    public sealed class MidiInputMonitor : IDisposable
    {
        private const uint CallbackFunction = 0x00030000;
        private const uint MimData = 0x3C3;
        private const uint MimLongData = 0x3C4;
        private const uint MmNoError = 0;

        private readonly MidiInProc callback;
        private IntPtr handle;
        private bool started;
        private bool disposed;

        public MidiInputMonitor(uint deviceId)
        {
            callback = OnMidiMessage;
            uint result = midiInOpen(out handle, deviceId, callback, IntPtr.Zero, CallbackFunction);
            ThrowIfError(result, "midiInOpen");
        }

        public static MidiDeviceInfo[] GetDevices()
        {
            uint count = midiInGetNumDevs();
            var devices = new List<MidiDeviceInfo>((int)count);

            for (uint id = 0; id < count; id++)
            {
                MidiInCaps caps = new MidiInCaps();
                uint result = midiInGetDevCaps(new UIntPtr(id), ref caps, (uint)Marshal.SizeOf(typeof(MidiInCaps)));
                ThrowIfError(result, "midiInGetDevCaps");
                devices.Add(new MidiDeviceInfo(id, caps.Name, caps.ManufacturerId, caps.ProductId, caps.DriverVersion));
            }

            return devices.ToArray();
        }

        public void Start()
        {
            ThrowIfDisposed();
            if (started)
                return;

            ThrowIfError(midiInStart(handle), "midiInStart");
            started = true;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            if (handle != IntPtr.Zero)
            {
                if (started)
                {
                    midiInStop(handle);
                    midiInReset(handle);
                }

                midiInClose(handle);
                handle = IntPtr.Zero;
            }

            disposed = true;
            GC.SuppressFinalize(this);
        }

        private void OnMidiMessage(IntPtr midiIn, uint message, IntPtr instance, UIntPtr parameter1, UIntPtr parameter2)
        {
            if (message == MimData)
            {
                uint packedMessage = unchecked((uint)parameter1.ToUInt64());
                uint timestampMilliseconds = unchecked((uint)parameter2.ToUInt64());
                Console.WriteLine(FormatShortMessage(timestampMilliseconds, packedMessage));
            }
            else if (message == MimLongData)
            {
                uint timestampMilliseconds = unchecked((uint)parameter2.ToUInt64());
                Console.WriteLine("[{0,8} ms] SysEx message received", timestampMilliseconds);
            }
        }

        private static string FormatShortMessage(uint timestampMilliseconds, uint packedMessage)
        {
            byte status = (byte)(packedMessage & 0xFF);
            byte data1 = (byte)((packedMessage >> 8) & 0x7F);
            byte data2 = (byte)((packedMessage >> 16) & 0x7F);
            int channel = (status & 0x0F) + 1;

            switch (status & 0xF0)
            {
                case 0x80:
                    return Format(timestampMilliseconds, status, "Note Off", channel, "note={0} velocity={1}", data1, data2);
                case 0x90:
                    string noteType = data2 == 0 ? "Note Off" : "Note On";
                    return Format(timestampMilliseconds, status, noteType, channel, "note={0} velocity={1}", data1, data2);
                case 0xA0:
                    return Format(timestampMilliseconds, status, "Poly Pressure", channel, "note={0} pressure={1}", data1, data2);
                case 0xB0:
                    return Format(timestampMilliseconds, status, "Control Change", channel, "controller={0} value={1}", data1, data2);
                case 0xC0:
                    return Format(timestampMilliseconds, status, "Program Change", channel, "program={0}", data1);
                case 0xD0:
                    return Format(timestampMilliseconds, status, "Channel Pressure", channel, "pressure={0}", data1);
                case 0xE0:
                    int pitchBend = ((data2 << 7) | data1) - 8192;
                    return Format(timestampMilliseconds, status, "Pitch Bend", channel, "value={0}", pitchBend);
                case 0xF0:
                    return String.Format("[{0,8} ms] {1,-17} status=0x{2:X2}", timestampMilliseconds, GetSystemMessageName(status), status);
                default:
                    return String.Format("[{0,8} ms] Unknown           status=0x{1:X2} data1={2} data2={3}", timestampMilliseconds, status, data1, data2);
            }
        }

        private static string Format(uint timestamp, byte status, string type, int channel, string details, params object[] values)
        {
            return String.Format("[{0,8} ms] {1,-17} ch={2,2} {3} (0x{4:X2})", timestamp, type, channel, String.Format(details, values), status);
        }

        private static string GetSystemMessageName(byte status)
        {
            switch (status)
            {
                case 0xF1: return "MTC Quarter Frame";
                case 0xF2: return "Song Position";
                case 0xF3: return "Song Select";
                case 0xF6: return "Tune Request";
                case 0xF8: return "Timing Clock";
                case 0xFA: return "Start";
                case 0xFB: return "Continue";
                case 0xFC: return "Stop";
                case 0xFE: return "Active Sensing";
                case 0xFF: return "System Reset";
                default: return "System Message";
            }
        }

        private static void ThrowIfError(uint result, string operation)
        {
            if (result == MmNoError)
                return;

            string message = "Unknown Windows MIDI error";
            var buffer = new System.Text.StringBuilder(256);
            if (midiInGetErrorText(result, buffer, (uint)buffer.Capacity) == MmNoError)
                message = buffer.ToString();

            throw new Win32Exception((int)result, operation + " failed: " + message);
        }

        private void ThrowIfDisposed()
        {
            if (disposed)
                throw new ObjectDisposedException("MidiInputMonitor");
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct MidiInCaps
        {
            public ushort ManufacturerId;
            public ushort ProductId;
            public uint DriverVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
            public string Name;
            public uint Support;
        }

        private delegate void MidiInProc(IntPtr midiIn, uint message, IntPtr instance, UIntPtr parameter1, UIntPtr parameter2);

        [DllImport("winmm.dll")]
        private static extern uint midiInGetNumDevs();

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "midiInGetDevCapsW")]
        private static extern uint midiInGetDevCaps(UIntPtr deviceId, ref MidiInCaps caps, uint capsSize);

        [DllImport("winmm.dll")]
        private static extern uint midiInOpen(out IntPtr midiIn, uint deviceId, MidiInProc callback, IntPtr instance, uint flags);

        [DllImport("winmm.dll")]
        private static extern uint midiInStart(IntPtr midiIn);

        [DllImport("winmm.dll")]
        private static extern uint midiInStop(IntPtr midiIn);

        [DllImport("winmm.dll")]
        private static extern uint midiInReset(IntPtr midiIn);

        [DllImport("winmm.dll")]
        private static extern uint midiInClose(IntPtr midiIn);

        [DllImport("winmm.dll", CharSet = CharSet.Unicode, EntryPoint = "midiInGetErrorTextW")]
        private static extern uint midiInGetErrorText(uint error, System.Text.StringBuilder text, uint textLength);
    }
}
'@
}

$devices = @([ShitDesigner.Tools.MidiInputMonitor]::GetDevices())

if ($devices.Count -eq 0) {
    Write-Host 'No MIDI input devices were found.' -ForegroundColor Yellow
    Write-Host 'Connect the controller, wait a moment, and run this script again.'
    exit 2
}

Write-Host "MIDI input devices ($($devices.Count)):" -ForegroundColor Cyan
foreach ($midiDevice in $devices) {
    Write-Host ("  [{0}] {1}  (manufacturer={2}, product={3}, driver=0x{4:X8})" -f `
        $midiDevice.Id, $midiDevice.Name, $midiDevice.ManufacturerId, $midiDevice.ProductId, $midiDevice.DriverVersion)
}

if ($ListOnly) {
    exit 0
}

$selectedDevice = $null
if ([string]::IsNullOrWhiteSpace($Device)) {
    if ($devices.Count -eq 1) {
        $selectedDevice = $devices[0]
    }
    else {
        Write-Host ''
        Write-Host 'More than one MIDI input device was found. Select one by ID or name:' -ForegroundColor Yellow
        Write-Host "  .\Tools\Test-MidiController.ps1 -Device 0"
        Write-Host "  .\Tools\Test-MidiController.ps1 -Device 'controller name'"
        exit 0
    }
}
elseif ($Device -match '^\d+$') {
    $deviceId = [uint32]$Device
    $selectedDevice = @($devices | Where-Object { $_.Id -eq $deviceId }) | Select-Object -First 1
}
else {
    $matches = @($devices | Where-Object { $_.Name.IndexOf($Device, [StringComparison]::OrdinalIgnoreCase) -ge 0 })
    if ($matches.Count -gt 1) {
        throw "Device name '$Device' matched more than one device. Select one by numeric ID instead."
    }
    if ($matches.Count -eq 1) {
        $selectedDevice = $matches[0]
    }
}

if ($null -eq $selectedDevice) {
    throw "MIDI input device '$Device' was not found."
}

Write-Host ''
Write-Host "Monitoring [$($selectedDevice.Id)] $($selectedDevice.Name). Move a knob or press a key." -ForegroundColor Green
Write-Host 'Press Ctrl+C to stop.'

$monitor = $null
try {
    $monitor = [ShitDesigner.Tools.MidiInputMonitor]::new([uint32]$selectedDevice.Id)
}
catch {
    $cause = $_.Exception
    while ($null -ne $cause.InnerException) {
        $cause = $cause.InnerException
    }

    if ($cause -is [ComponentModel.Win32Exception] -and $cause.NativeErrorCode -eq 7) {
        Write-Host ''
        Write-Host "The device was detected, but Windows could not open its MIDI input port (error 7)." -ForegroundColor Red
        Write-Host 'This message usually does not mean that system RAM is exhausted.' -ForegroundColor Yellow
        Write-Host 'Try these steps:'
        Write-Host '  1. Close DAWs, MIDI utilities, Unity, and browser pages using Web MIDI.'
        Write-Host '  2. Disconnect and reconnect the controller, then rerun this command.'
        Write-Host '  3. Install or update the manufacturer USB driver and device firmware.'
        Write-Host '  4. If the device exposes multiple ports, try the other numeric device ID.'
        exit 3
    }

    throw
}

try {
    $monitor.Start()
    while ($true) {
        Start-Sleep -Milliseconds 50
    }
}
finally {
    $monitor.Dispose()
}

Write-Host 'MIDI monitoring finished.' -ForegroundColor Cyan
