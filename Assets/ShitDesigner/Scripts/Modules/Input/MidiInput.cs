using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using AOT;
using ShitDesigner.Application;
using ShitDesigner.Core;

namespace ShitDesigner.Input {
	public sealed class MidiInputDeviceInfo {
		public uint Id { get; }
		public string Name { get; }

		internal MidiInputDeviceInfo(uint id, string name) {
			Id = id;
			Name = name ?? string.Empty;
		}
	}

	public interface IMidiInputSource : IDisposable {
		string DeviceName { get; }
		bool TryDequeue(out MidiInputEvent inputEvent);
	}

	public interface IMidiInputAvailability {
		bool IsAvailable { get; }
	}

	public interface ILaunchControlXl3DawModeController {
		void EnableRelativeEncoderRow(int row);
	}

	public static class LaunchControlXl3DawModeProtocol {
		public const uint EnableDawModeMessage = 0x007f0c9f;
		public const uint DisableDawModeMessage = 0x00000c9f;

		public static bool TryResolveRelativeEncoderRow(int channel, int controlNumber, out int row) {
			row = 0;
			if (channel != 16) return false;
			if (controlNumber >= 77 && controlNumber <= 84) row = 1;
			else if (controlNumber >= 85 && controlNumber <= 92) row = 2;
			else if (controlNumber >= 93 && controlNumber <= 100) row = 3;
			return row != 0;
		}

		public static uint EnableRelativeEncoderRowMessage(int row) {
			int controlNumber;
			switch (row) {
				case 1: controlNumber = 69; break;
				case 2: controlNumber = 72; break;
				case 3: controlNumber = 73; break;
				default: throw new ArgumentOutOfRangeException(nameof(row));
			}
			return (uint)(0xb6 | (controlNumber << 8) | (127 << 16));
		}

		public static string ResolveDawInputName(string dawOutputName) {
			const string suffix = " DAW Out";
			if (string.IsNullOrWhiteSpace(dawOutputName) || !dawOutputName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
				throw new ArgumentException("The Launch Control XL 3 DAW Out port is required.", nameof(dawOutputName));
			return dawOutputName.Substring(0, dawOutputName.Length - suffix.Length) + " DAW In";
		}
	}

	public static class MidiShortMessageDecoder {
		public static bool TryDecode(string deviceName, uint packedMessage, out MidiInputEvent inputEvent) {
			var status = (byte)(packedMessage & 0xff);
			var data1 = (int)((packedMessage >> 8) & 0x7f);
			var data2 = (int)((packedMessage >> 16) & 0x7f);
			var channel = (status & 0x0f) + 1;
			MidiControl control;
			int value;

			switch (status & 0xf0) {
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
	public sealed class WindowsMidiInputSource : IMidiInputSource, IMidiInputAvailability {
		private const uint CallbackFunction = 0x00030000;
		private const uint MidiDataMessage = 0x3c3;
		private const uint NoError = 0;
		private readonly ConcurrentQueue<MidiInputEvent> _events = new ConcurrentQueue<MidiInputEvent>();
		private static readonly MidiInCallback Callback = OnMidiMessage;
		private readonly uint _deviceId;
		private IntPtr _handle;
		private GCHandle _selfHandle;
		private bool _hasSelfHandle;
		private bool _disposed;

		public string DeviceName { get; }
		public bool IsAvailable {
			get {
				if (_disposed) return false;
				try {
					var devices = GetDevices();
					return _deviceId < (uint)devices.Count && string.Equals(devices[(int)_deviceId].Name, DeviceName, StringComparison.Ordinal);
				}
				catch { }
				return false;
			}
		}

		public WindowsMidiInputSource(uint deviceId) {
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) throw new PlatformNotSupportedException("WinMM MIDI input is only available on Windows.");
			var devices = GetDevices();
			if (deviceId >= devices.Count) throw new ArgumentOutOfRangeException(nameof(deviceId), "The MIDI input device does not exist.");
			_deviceId = deviceId;
			DeviceName = devices[(int)deviceId].Name;
			_selfHandle = GCHandle.Alloc(this);
			_hasSelfHandle = true;
			try {
				ThrowIfError(midiInOpen(out _handle, deviceId, Callback, GCHandle.ToIntPtr(_selfHandle), CallbackFunction), "midiInOpen");
				ThrowIfError(midiInStart(_handle), "midiInStart");
			}
			catch {
				if (_handle != IntPtr.Zero) midiInClose(_handle);
				_handle = IntPtr.Zero;
				_selfHandle.Free();
				_hasSelfHandle = false;
				throw;
			}
		}

		public static IReadOnlyList<MidiInputDeviceInfo> GetDevices() {
			var devices = new List<MidiInputDeviceInfo>();
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return devices;
			var count = midiInGetNumDevs();
			for (uint id = 0; id < count; id++) {
				var caps = new MidiInCaps();
				ThrowIfError(midiInGetDevCaps(new UIntPtr(id), ref caps, (uint)Marshal.SizeOf(typeof(MidiInCaps))), "midiInGetDevCaps");
				devices.Add(new MidiInputDeviceInfo(id, caps.Name));
			}
			return devices;
		}

		public static bool TryOpenDefault(out WindowsMidiInputSource source, out string error) {
			source = null;
			error = string.Empty;
			try {
				if (GetDevices().Count == 0) { error = "No MIDI input devices were found."; return false; }
				source = new WindowsMidiInputSource(0);
				return true;
			}
			catch (Exception exception) {
				error = exception.Message;
				source?.Dispose();
				source = null;
				return false;
			}
		}

		public bool TryDequeue(out MidiInputEvent inputEvent) => _events.TryDequeue(out inputEvent);

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			if (_handle != IntPtr.Zero) {
				midiInStop(_handle);
				midiInReset(_handle);
				midiInClose(_handle);
				_handle = IntPtr.Zero;
			}
			if (_hasSelfHandle) {
				_selfHandle.Free();
				_hasSelfHandle = false;
			}
		}

		[MonoPInvokeCallback(typeof(MidiInCallback))]
		private static void OnMidiMessage(IntPtr midiIn, uint message, IntPtr instance, UIntPtr parameter1, UIntPtr parameter2) {
			if (message != MidiDataMessage || instance == IntPtr.Zero) return;
			var target = GCHandle.FromIntPtr(instance).Target as WindowsMidiInputSource;
			if (target == null || target._disposed) return;
			if (MidiShortMessageDecoder.TryDecode(target.DeviceName, unchecked((uint)parameter1.ToUInt64()), out var inputEvent)) target._events.Enqueue(inputEvent);
		}

		private static void ThrowIfError(uint result, string operation) {
			if (result == NoError) return;
			var message = "Windows MIDI error " + result;
			var buffer = new System.Text.StringBuilder(256);
			if (midiInGetErrorText(result, buffer, (uint)buffer.Capacity) == NoError) message = buffer.ToString();
			throw new Win32Exception((int)result, operation + " failed: " + message);
		}

		[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
		private struct MidiInCaps {
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

	/// <summary>macOS CoreMIDI input. CoreMIDI invokes the native callback on its
	/// high-priority thread; decoded events are consumed later on Unity's main thread.</summary>
	public sealed class MacOsMidiInputSource : IMidiInputSource, IMidiInputAvailability, ILaunchControlXl3DawModeController {
		private const string CoreMidiLibrary = "/System/Library/Frameworks/CoreMIDI.framework/CoreMIDI";
		private const string CoreFoundationLibrary = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
		private const uint Utf8Encoding = 0x08000100;
		private const int NoError = 0;
		private const int MidiPacketListFirstPacketOffset = 4;
		private const int MidiPacketLengthOffset = 8;
		private const int MidiPacketDataOffset = 10;
		private const int MidiNameBufferCapacity = 1024;

		private static readonly MidiReadCallback m_Callback = OnMidiPacketList;
		private static readonly bool m_AlignPacketsToFourBytes = RuntimeInformation.ProcessArchitecture == Architecture.Arm ||
			RuntimeInformation.ProcessArchitecture == Architecture.Arm64;

		private readonly ConcurrentQueue<MidiInputEvent> m_Events = new ConcurrentQueue<MidiInputEvent>();
		private readonly uint m_DeviceId;
		private readonly uint m_Source;
		private uint m_Client;
		private uint m_Port;
		private uint m_OutputPort;
		private uint m_Destination;
		private GCHandle m_SelfHandle;
		private bool m_HasSelfHandle;
		private bool m_DawModeEnabled;
		private volatile bool m_Disposed;

		public string DeviceName { get; }
		public bool IsAvailable {
			get {
				if (m_Disposed) return false;
				try {
					var count = MIDIGetNumberOfSources().ToUInt64();
					return m_DeviceId < count && MIDIGetSource(new UIntPtr(m_DeviceId)) == m_Source;
				}
				catch { }
				return false;
			}
		}

		public MacOsMidiInputSource(uint deviceId) {
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) throw new PlatformNotSupportedException("CoreMIDI input is only available on macOS.");
			var devices = GetDevices();
			if (deviceId >= devices.Count) throw new ArgumentOutOfRangeException(nameof(deviceId), "The MIDI input device does not exist.");

			m_DeviceId = deviceId;
			m_Source = MIDIGetSource(new UIntPtr(deviceId));
			if (m_Source == 0) throw new InvalidOperationException("The MIDI input device is no longer available.");
			DeviceName = devices[(int)deviceId].Name;
			m_SelfHandle = GCHandle.Alloc(this);
			m_HasSelfHandle = true;

			var clientName = CreateString("ShitDesigner MIDI Client");
			var portName = CreateString("ShitDesigner MIDI Input");
			try {
				ThrowIfError(MIDIClientCreate(clientName, IntPtr.Zero, IntPtr.Zero, out m_Client), "MIDIClientCreate");
				ThrowIfError(MIDIInputPortCreate(m_Client, portName, m_Callback, GCHandle.ToIntPtr(m_SelfHandle), out m_Port), "MIDIInputPortCreate");
				ThrowIfError(MIDIPortConnectSource(m_Port, m_Source, IntPtr.Zero), "MIDIPortConnectSource");
			}
			catch {
				ReleaseNativeResources();
				throw;
			}
			finally {
				CFRelease(clientName);
				CFRelease(portName);
			}
		}

		public static IReadOnlyList<MidiInputDeviceInfo> GetDevices() {
			var devices = new List<MidiInputDeviceInfo>();
			if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return devices;
			var count = MIDIGetNumberOfSources().ToUInt64();
			for (ulong index = 0; index < count && index <= uint.MaxValue; index++) {
				var source = MIDIGetSource(new UIntPtr(index));
				if (source == 0) continue;
				var name = GetStringProperty(source, "displayName");
				if (string.IsNullOrEmpty(name)) name = GetStringProperty(source, "name");
				if (string.IsNullOrEmpty(name)) name = "MIDI Source " + index;
				devices.Add(new MidiInputDeviceInfo((uint)index, name));
			}
			return devices;
		}

		public bool TryDequeue(out MidiInputEvent inputEvent) => m_Events.TryDequeue(out inputEvent);

		public void EnableRelativeEncoderRow(int row) {
			if (m_Disposed) throw new ObjectDisposedException(nameof(MacOsMidiInputSource));
			var relativeModeMessage = LaunchControlXl3DawModeProtocol.EnableRelativeEncoderRowMessage(row);
			EnsureDawOutput();
			SendShortMessage(LaunchControlXl3DawModeProtocol.EnableDawModeMessage);
			m_DawModeEnabled = true;
			SendShortMessage(relativeModeMessage);
		}

		public void Dispose() {
			if (m_Disposed) return;
			if (m_DawModeEnabled) {
				try { SendShortMessage(LaunchControlXl3DawModeProtocol.DisableDawModeMessage); }
				catch { }
			}
			m_Disposed = true;
			ReleaseNativeResources();
		}

		private void ReleaseNativeResources() {
			if (m_OutputPort != 0) {
				LogCleanupError(MIDIPortDispose(m_OutputPort), "MIDIOutputPortDispose");
				m_OutputPort = 0;
				m_Destination = 0;
			}
			if (m_Port != 0) {
				if (m_Source != 0) LogCleanupError(MIDIPortDisconnectSource(m_Port, m_Source), "MIDIPortDisconnectSource");
				LogCleanupError(MIDIPortDispose(m_Port), "MIDIInputPortDispose");
				m_Port = 0;
			}
			if (m_Client != 0) {
				LogCleanupError(MIDIClientDispose(m_Client), "MIDIClientDispose");
				m_Client = 0;
			}
			if (m_HasSelfHandle) {
				m_SelfHandle.Free();
				m_HasSelfHandle = false;
			}
		}

		private void EnsureDawOutput() {
			if (m_OutputPort != 0) return;
			var expectedName = LaunchControlXl3DawModeProtocol.ResolveDawInputName(DeviceName);
			var count = MIDIGetNumberOfDestinations().ToUInt64();
			for (ulong index = 0; index < count; index++) {
				var destination = MIDIGetDestination(new UIntPtr(index));
				if (destination == 0 || !string.Equals(GetStringProperty(destination, "displayName"), expectedName, StringComparison.OrdinalIgnoreCase)) continue;
				m_Destination = destination;
				break;
			}
			if (m_Destination == 0) throw new InvalidOperationException("MIDI destination '" + expectedName + "' was not found.");

			var portName = CreateString("ShitDesigner MIDI Output");
			try { ThrowIfError(MIDIOutputPortCreate(m_Client, portName, out m_OutputPort), "MIDIOutputPortCreate"); }
			finally { CFRelease(portName); }
		}

		private void SendShortMessage(uint message) {
			const int packetListSize = 272;
			var packetList = Marshal.AllocHGlobal(packetListSize);
			try {
				Marshal.WriteInt32(packetList, 1);
				var packet = IntPtr.Add(packetList, MidiPacketListFirstPacketOffset);
				Marshal.WriteInt64(packet, 0, 0L);
				Marshal.WriteInt16(packet, MidiPacketLengthOffset, 3);
				Marshal.WriteByte(packet, MidiPacketDataOffset, (byte)(message & 0xff));
				Marshal.WriteByte(packet, MidiPacketDataOffset + 1, (byte)((message >> 8) & 0x7f));
				Marshal.WriteByte(packet, MidiPacketDataOffset + 2, (byte)((message >> 16) & 0x7f));
				ThrowIfError(MIDISend(m_OutputPort, m_Destination, packetList), "MIDISend");
			}
			finally { Marshal.FreeHGlobal(packetList); }
		}

		[MonoPInvokeCallback(typeof(MidiReadCallback))]
		private static void OnMidiPacketList(IntPtr packetList, IntPtr readContext, IntPtr sourceContext) {
			if (packetList == IntPtr.Zero || readContext == IntPtr.Zero) return;
			try {
				var target = GCHandle.FromIntPtr(readContext).Target as MacOsMidiInputSource;
				if (target == null || target.m_Disposed) return;
				var packetCount = unchecked((uint)Marshal.ReadInt32(packetList));
				var packet = IntPtr.Add(packetList, MidiPacketListFirstPacketOffset);
				for (uint packetIndex = 0; packetIndex < packetCount; packetIndex++) {
					var length = unchecked((ushort)Marshal.ReadInt16(packet, MidiPacketLengthOffset));
					target.EnqueuePacket(IntPtr.Add(packet, MidiPacketDataOffset), length);
					var nextAddress = packet.ToInt64() + MidiPacketDataOffset + length;
					if (m_AlignPacketsToFourBytes) nextAddress = (nextAddress + 3L) & ~3L;
					packet = new IntPtr(nextAddress);
				}
			}
			catch {
				// Never allow a managed exception to cross the CoreMIDI callback boundary.
			}
		}

		private void EnqueuePacket(IntPtr data, int length) {
			var offset = 0;
			while (offset < length) {
				var status = Marshal.ReadByte(data, offset);
				if (status < 0x80 || status == 0xf0) return;
				var messageLength = GetMessageLength(status);
				if (offset + messageLength > length) return;
				var data1 = messageLength > 1 ? Marshal.ReadByte(data, offset + 1) : (byte)0;
				var data2 = messageLength > 2 ? Marshal.ReadByte(data, offset + 2) : (byte)0;
				var packedMessage = (uint)(status | (data1 << 8) | (data2 << 16));
				if (MidiShortMessageDecoder.TryDecode(DeviceName, packedMessage, out var inputEvent)) m_Events.Enqueue(inputEvent);
				offset += messageLength;
			}
		}

		private static int GetMessageLength(byte status) {
			if (status < 0xf0) return (status & 0xf0) == 0xc0 || (status & 0xf0) == 0xd0 ? 2 : 3;
			switch (status) {
				case 0xf1:
				case 0xf3:
					return 2;
				case 0xf2:
					return 3;
				default:
					return 1;
			}
		}

		private static string GetStringProperty(uint source, string propertyName) {
			var property = CreateString(propertyName);
			try {
				if (MIDIObjectGetStringProperty(source, property, out var value) != NoError || value == IntPtr.Zero) return string.Empty;
				var buffer = new System.Text.StringBuilder(MidiNameBufferCapacity);
				return CFStringGetCString(value, buffer, buffer.Capacity, Utf8Encoding) ? buffer.ToString() : string.Empty;
			}
			finally {
				CFRelease(property);
			}
		}

		private static IntPtr CreateString(string value) {
			var result = CFStringCreateWithCString(IntPtr.Zero, value, Utf8Encoding);
			if (result == IntPtr.Zero) throw new InvalidOperationException("CoreFoundation could not create a MIDI string.");
			return result;
		}

		private static void ThrowIfError(int result, string operation) {
			if (result != NoError) throw new InvalidOperationException(operation + " failed with CoreMIDI OSStatus " + result + ".");
		}

		private static void LogCleanupError(int result, string operation) {
			if (result != NoError) Debug.LogWarning(operation + " failed with CoreMIDI OSStatus " + result + ".");
		}

		[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
		private delegate void MidiReadCallback(IntPtr packetList, IntPtr readContext, IntPtr sourceContext);

		[DllImport(CoreMidiLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int MIDIClientCreate(IntPtr name, IntPtr notifyProc, IntPtr notifyContext, out uint client);
		[DllImport(CoreMidiLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int MIDIClientDispose(uint client);
		[DllImport(CoreMidiLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int MIDIInputPortCreate(uint client, IntPtr portName, MidiReadCallback readProc, IntPtr readContext, out uint port);
		[DllImport(CoreMidiLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int MIDIOutputPortCreate(uint client, IntPtr portName, out uint port);
		[DllImport(CoreMidiLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int MIDIPortDispose(uint port);
		[DllImport(CoreMidiLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int MIDIPortConnectSource(uint port, uint source, IntPtr sourceContext);
		[DllImport(CoreMidiLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int MIDIPortDisconnectSource(uint port, uint source);
		[DllImport(CoreMidiLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int MIDISend(uint port, uint destination, IntPtr packetList);
		[DllImport(CoreMidiLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern UIntPtr MIDIGetNumberOfSources();
		[DllImport(CoreMidiLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern uint MIDIGetSource(UIntPtr sourceIndex);
		[DllImport(CoreMidiLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern UIntPtr MIDIGetNumberOfDestinations();
		[DllImport(CoreMidiLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern uint MIDIGetDestination(UIntPtr destinationIndex);
		[DllImport(CoreMidiLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern int MIDIObjectGetStringProperty(uint source, IntPtr property, out IntPtr value);
		[DllImport(CoreFoundationLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern IntPtr CFStringCreateWithCString(IntPtr allocator, [MarshalAs(UnmanagedType.LPUTF8Str)] string value, uint encoding);
		[DllImport(CoreFoundationLibrary, CallingConvention = CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] private static extern bool CFStringGetCString(IntPtr value, System.Text.StringBuilder buffer, long bufferSize, uint encoding);
		[DllImport(CoreFoundationLibrary, CallingConvention = CallingConvention.Cdecl)] private static extern void CFRelease(IntPtr value);
	}

	public static class MidiInputDevices {
		public static IReadOnlyList<MidiInputDeviceInfo> GetDevices() {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return WindowsMidiInputSource.GetDevices();
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return MacOsMidiInputSource.GetDevices();
			return Array.Empty<MidiInputDeviceInfo>();
		}

		public static IMidiInputSource Open(uint deviceId) {
			if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return new WindowsMidiInputSource(deviceId);
			if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) return new MacOsMidiInputSource(deviceId);
			throw new PlatformNotSupportedException("MIDI input is supported on Windows and macOS.");
		}
	}

	public sealed class MidiInputRouter {
		private const int MaximumEventsPerPoll = 4096;
		private readonly IMidiInputApplicationPort _application;
		private readonly IMidiInputSource _source;

		public MidiInputRouter(IMidiInputApplicationPort application, IMidiInputSource source) {
			_application = application ?? throw new ArgumentNullException(nameof(application));
			_source = source ?? throw new ArgumentNullException(nameof(source));
		}

		public int Poll() {
			var count = 0;
			while (count < MaximumEventsPerPoll && _source.TryDequeue(out var inputEvent)) {
				_application.HandleMidi(inputEvent);
				count++;
			}
			return count;
		}
	}
}
