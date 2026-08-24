using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using ShitDesigner.Core;

namespace ShitDesigner.Media {
	public sealed class HapDecodedPlane {
		public uint Format { get; }
		public byte[] Blocks { get; }
		public HapDecodedPlane(uint format, byte[] blocks) { Format = format; Blocks = blocks ?? Array.Empty<byte>(); }
	}

	/// <summary>Native frame copied out of the plugin while its lease is held.
	/// The managed copy makes release deterministic and prevents a subsequent
	/// native acquire from invalidating a borrowed pointer. LinearPremultiplied
	/// is the CPU fallback representation used when no Unity GPU bridge exists.
	/// </summary>
	public sealed class HapDecodedFrame {
		public uint Width { get; }
		public uint Height { get; }
		public ulong FrameIndex { get; }
		public ulong PresentationTicks { get; }
		public byte[] Rgba8PremultipliedLinear { get; }
		public HapDecodedPlane[] Planes { get; }
		public bool UsesCpuFallback { get; }
		public bool IsYCoCg { get; }
		public HapDecodedFrame(uint width, uint height, ulong frameIndex, ulong presentationTicks, byte[] rgba, HapDecodedPlane[] planes, bool usesCpuFallback = true, bool isYCoCg = false) { Width = width; Height = height; FrameIndex = frameIndex; PresentationTicks = presentationTicks; Rgba8PremultipliedLinear = rgba; Planes = planes ?? Array.Empty<HapDecodedPlane>(); UsesCpuFallback = usesCpuFallback; IsYCoCg = isYCoCg; }
	}

	public interface IHapNativePreparedApi {
		bool OpensPrepared { get; }
	}

	/// <summary>CSharpFunctionalExtensions.UnitResult<Diagnostic> of loading the installed native binary and querying its
	/// C ABI.  Source files and CMake configuration are deliberately not
	/// considered a capability; the exported functions must be callable.</summary>
	public sealed class HapNativePluginProbeResult {
		public bool IsAvailable { get; }
		public uint AbiVersion { get; }
		public uint Capabilities { get; }
		public string DiagnosticCode { get; }
		public string Message { get; }

		public HapNativePluginProbeResult(bool available, uint abiVersion, uint capabilities, string diagnosticCode, string message) {
			IsAvailable = available;
			AbiVersion = abiVersion;
			Capabilities = capabilities;
			DiagnosticCode = diagnosticCode ?? string.Empty;
			Message = message ?? string.Empty;
		}
	}

	/// <summary>
	/// Production P/Invoke boundary for the optional Hap native plugin. The
	/// native handle is intentionally opaque. This adapter never synthesizes
	/// Prepared/FrameReady callbacks; the native host must call the decoder's
	/// Notify* bridge after an actual decode.
	/// </summary>
	public sealed class PInvokeHapNativeApi : IHapNativeApi, IHapNativePreparedApi {
		private const string LibraryName = "shitdesigner_hap";
		private HapUnityGraphicsBridge _graphicsBridge;
		private readonly object _gate = new object();
		private readonly Dictionary<IntPtr, HapTextureLease> _leases = new Dictionary<IntPtr, HapTextureLease>();
		private readonly HashSet<IntPtr> _activeHandles = new HashSet<IntPtr>();

		public PInvokeHapNativeApi(HapUnityGraphicsBridge graphicsBridge = null) {
			_graphicsBridge = graphicsBridge;
		}

		// Editor/player builds only advertise a supported OS here. A missing
		// or incompatible binary is converted to a deterministic diagnostic by
		// each call below instead of failing during type initialization.
		public bool IsSupportedPlatform {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN || UNITY_EDITOR_OSX || UNITY_STANDALONE_OSX
			get { return true; }
#else
            // PlatformID.Unix is also returned by some modern Mono/macOS
            // combinations. RuntimeInformation is the stable native-platform
            // seam for source-direct runners and non-Unity hosts.
            get { return RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX); }
#endif
		}
		public bool OpensPrepared => true;

		/// <summary>Loads the platform binary and checks the complete ABI and
		/// plane contract. This is intentionally callable before Open so a
		/// production capability report cannot be satisfied by source alone.</summary>
		public HapNativePluginProbeResult ProbeInstalledBinary() {
			if (!IsSupportedPlatform)
				return new HapNativePluginProbeResult(false, 0, 0, UnsupportedCode, UnsupportedMessage);
			try {
				var abi = sd_hap_abi_version();
				var caps = sd_hap_capabilities();
				var required = NativeCpuRgbaCapability | NativeCompressedPlaneCapability;
				if (abi != RequiredAbiVersion)
					return new HapNativePluginProbeResult(false, abi, caps, "media.hap.abi", "The installed Hap native plugin ABI version is incompatible.");
				if ((caps & required) != required)
					return new HapNativePluginProbeResult(false, abi, caps, "media.hap.capability", "The installed Hap native plugin is missing a required CPU or compressed-plane capability.");
				return new HapNativePluginProbeResult(true, abi, caps, string.Empty, "The installed Hap native plugin ABI and capabilities are verified.");
			}
			catch (DllNotFoundException) {
				return new HapNativePluginProbeResult(false, 0, 0, "media.hap.plugin_missing", "The Hap native plugin binary is not installed for this platform.");
			}
			catch (EntryPointNotFoundException) {
				return new HapNativePluginProbeResult(false, 0, 0, "media.hap.plugin_api_missing", "The Hap native plugin is missing the ABI or capability export.");
			}
			catch (Exception exception) {
				return new HapNativePluginProbeResult(false, 0, 0, "media.hap.plugin_probe_failed", exception.Message);
			}
		}

		public CSharpFunctionalExtensions.Result<IntPtr, Diagnostic> Open(VideoPrepareRequest request) {
			if (!IsSupportedPlatform) return Failure<IntPtr>(UnsupportedCode, UnsupportedMessage);
			if (request == null || string.IsNullOrWhiteSpace(request.Url)) return Failure<IntPtr>("media.prepare.request", "A verified media path is required.");
			IntPtr handle = IntPtr.Zero;
			try {
				var probe = ProbeInstalledBinary();
				if (!probe.IsAvailable) return Failure<IntPtr>(probe.DiagnosticCode, probe.Message);
				var status = sd_hap_open(request.Url, (int)request.Probe.Codec, out handle);
				if (status != 0 || handle == IntPtr.Zero) {
					if (handle != IntPtr.Zero) sd_hap_close(handle);
					return Failure<IntPtr>("media.hap.open_failed", "The Hap native plugin could not open the media stream.");
				}
				if (sd_hap_prepare(handle) != 0) { sd_hap_close(handle); return Failure<IntPtr>("media.hap.prepare_failed", "The Hap native plugin could not decode the first sample."); }
				lock (_gate) _activeHandles.Add(handle);
				return CSharpFunctionalExtensions.Result.Success<IntPtr, Diagnostic>(handle);
			}
			catch (DllNotFoundException) { return Failure<IntPtr>("media.hap.plugin_missing", "The Hap native plugin binary is not installed for this platform."); }
			catch (EntryPointNotFoundException) { return Failure<IntPtr>("media.hap.plugin_api_missing", "The Hap native plugin is missing the required C API."); }
			catch (Exception exception) {
				if (handle != IntPtr.Zero) { try { sd_hap_close(handle); } catch { } }
				return Failure<IntPtr>("media.hap.native_exception", exception.Message);
			}
		}

		public CSharpFunctionalExtensions.UnitResult<Diagnostic> Play(IntPtr handle) => Call(() => sd_hap_play(handle), "media.hap.play_failed");
		public CSharpFunctionalExtensions.UnitResult<Diagnostic> Pause(IntPtr handle) => Call(() => sd_hap_pause(handle), "media.hap.pause_failed");
		public CSharpFunctionalExtensions.UnitResult<Diagnostic> Stop(IntPtr handle) => Call(() => sd_hap_stop(handle), "media.hap.stop_failed");
		public CSharpFunctionalExtensions.UnitResult<Diagnostic> SetSpeed(IntPtr handle, double speed) => Call(() => sd_hap_set_speed(handle, speed), "media.hap.speed_failed");
		public CSharpFunctionalExtensions.UnitResult<Diagnostic> SetLoop(IntPtr handle, bool loop) => Call(() => sd_hap_set_loop(handle, loop ? 1 : 0), "media.hap.loop_failed");
		public CSharpFunctionalExtensions.UnitResult<Diagnostic> Seek(IntPtr handle, double seconds) => Call(() => sd_hap_seek(handle, seconds), "media.hap.seek_failed");
		public CSharpFunctionalExtensions.UnitResult<Diagnostic> SyncToGraphClock(IntPtr handle, double logicalSeconds, bool demanded) => Call(() => sd_hap_sync(handle, logicalSeconds, demanded ? 1 : 0), "media.hap.clock_failed");

		public object GetBorrowedTexture(IntPtr handle) {
			lock (_gate) {
				if (_graphicsBridge == null) _graphicsBridge = new HapUnityGraphicsBridge();
				if (TryUploadBorrowedDirectFrame(handle, out var directTexture)) return directTexture;
				var frame = AcquireFrameCore(handle, -1, compressedOnly: true);
				if (frame.IsFailure) return null;
				if (_graphicsBridge.SelectDecodePath(frame.Value) == HapDecodePath.Cpu) {
					frame = AcquireFrameCore(handle, -1, compressedOnly: false);
					if (frame.IsFailure) return null;
				}
				var uploaded = _graphicsBridge.Upload(frame.Value);
				if (uploaded.IsFailure) return null;
				if (_leases.TryGetValue(handle, out var previous)) previous.Dispose();
				_leases[handle] = uploaded.Value;
				// The backend contract is an actual Unity texture. Lease ownership
				// remains inside this API and is reclaimed on the next frame/close.
				return uploaded.Value.Texture;
			}
		}

		private bool TryUploadBorrowedDirectFrame(IntPtr handle, out object texture) {
			texture = null;
			var native = new NativeFrame { StructSize = (uint)Marshal.SizeOf<NativeFrame>(), DecodePath = 1u };
			var acquired = false;
			try {
				var status = sd_hap_acquire_frame(handle, -1, ref native);
				if (status != 0) return false;
				acquired = true;
				if (native.DecodePath != 1u || native.PlaneCount == 0 || native.PlaneCount > 2) return false;
				var frame = new HapNativeFrameView(
					native.Width,
					native.Height,
					native.TextureFormat == 0x0Fu,
					(int)native.PlaneCount,
					new HapNativePlaneView(native.Plane0.Format, native.Plane0.Bytes, native.Plane0.Data),
					new HapNativePlaneView(native.Plane1.Format, native.Plane1.Bytes, native.Plane1.Data));
				_leases.TryGetValue(handle, out var previous);
				if (!_graphicsBridge.TryUploadNativeDirect(frame, previous, out var uploaded) || uploaded == null) return false;
				if (!ReferenceEquals(previous, uploaded)) {
					if (previous != null) previous.Dispose();
					_leases[handle] = uploaded;
				}
				texture = uploaded.Texture;
				return texture != null;
			}
			catch {
				return false;
			}
			finally {
				if (acquired) sd_hap_release_frame(handle, ref native);
			}
		}

		public CSharpFunctionalExtensions.Result<HapDecodedFrame, Diagnostic> AcquireFrame(IntPtr handle, long index)
			=> AcquireFrameCore(handle, index, compressedOnly: false);

		private CSharpFunctionalExtensions.Result<HapDecodedFrame, Diagnostic> AcquireFrameCore(IntPtr handle, long index, bool compressedOnly) {
			if (!IsSupportedPlatform) return Failure<HapDecodedFrame>(UnsupportedCode, UnsupportedMessage);
			if (handle == IntPtr.Zero) return Failure<HapDecodedFrame>("media.hap.frame_handle", "A native Hap handle is required.");
			lock (_gate) {
				try {
					var native = new NativeFrame { StructSize = (uint)Marshal.SizeOf<NativeFrame>(), DecodePath = compressedOnly ? 1u : 0u };
					var status = sd_hap_acquire_frame(handle, index, ref native);
					if (status != 0 || native.PlaneCount == 0 || (!compressedOnly && (native.Rgba == IntPtr.Zero || native.RgbaBytes == 0))) return Failure<HapDecodedFrame>("media.hap.frame_failed", "The Hap native plugin could not acquire a decoded frame.");
					try {
						var rgba = native.RgbaBytes == 0 ? Array.Empty<byte>() : new byte[native.RgbaBytes];
						if (rgba.Length > 0) Marshal.Copy(native.Rgba, rgba, 0, rgba.Length);
						var planes = new HapDecodedPlane[native.PlaneCount > 2 ? 0 : native.PlaneCount];
						if (native.PlaneCount > 2) return Failure<HapDecodedFrame>("media.hap.plane_count", "The Hap native plugin returned an invalid plane count.");
						for (var i = 0; i < planes.Length; i++) {
							var nativePlane = i == 0 ? native.Plane0 : native.Plane1;
							if (nativePlane.Data == IntPtr.Zero || nativePlane.Bytes == 0) return Failure<HapDecodedFrame>("media.hap.plane", "The Hap native plugin returned an invalid compressed plane.");
							var data = new byte[nativePlane.Bytes]; Marshal.Copy(nativePlane.Data, data, 0, data.Length); planes[i] = new HapDecodedPlane(nativePlane.Format, data);
						}
						if (rgba.Length > 0) rgba = HapColorConversion.ToLinearPremultipliedRgba8(rgba);
						return CSharpFunctionalExtensions.Result.Success<HapDecodedFrame, Diagnostic>(new HapDecodedFrame(native.Width, native.Height, native.FrameIndex, native.PresentationTicks, rgba, planes, usesCpuFallback: native.DecodePath != 1u, isYCoCg: native.TextureFormat == 0x0Fu));
					}
					finally {
						// The native lease is always released after the
						// managed copies, including validation/copy failures.
						sd_hap_release_frame(handle, ref native);
					}
				}
				catch (DllNotFoundException) { return Failure<HapDecodedFrame>("media.hap.plugin_missing", "The Hap native plugin binary is not installed for this platform."); }
				catch (EntryPointNotFoundException) { return Failure<HapDecodedFrame>("media.hap.plugin_api_missing", "The Hap native plugin is missing the frame ABI."); }
				catch (Exception exception) { return Failure<HapDecodedFrame>("media.hap.frame_exception", exception.Message); }
			}
		}

		public void Close(IntPtr handle) {
			if (handle == IntPtr.Zero) return;
			lock (_gate) {
				// Close is intentionally idempotent. This protects native
				// ownership when both a backend and its session teardown call
				// Dispose at the same boundary.
				if (!_activeHandles.Remove(handle)) return;
				if (_leases.TryGetValue(handle, out var lease)) { lease.Dispose(); _leases.Remove(handle); }
			}
			try { sd_hap_close(handle); } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
		}

		private CSharpFunctionalExtensions.UnitResult<Diagnostic> Call(Func<int> call, string code) {
			if (!IsSupportedPlatform) return Failure(UnsupportedCode, UnsupportedMessage);
			lock (_gate) {
				try {
					var status = call();
					return status == 0 ? CSharpFunctionalExtensions.UnitResult.Success<Diagnostic>() : Failure(code, "The Hap native plugin rejected the operation.");
				}
				catch (DllNotFoundException) { return Failure("media.hap.plugin_missing", "The Hap native plugin binary is not installed for this platform."); }
				catch (EntryPointNotFoundException) { return Failure("media.hap.plugin_api_missing", "The Hap native plugin is missing the required C API."); }
				catch (Exception exception) { return Failure(code, exception.Message); }
			}
		}

		private const uint RequiredAbiVersion = 1u;
		private static readonly string UnsupportedCode = "media.hap.platform_unsupported";
		private static readonly string UnsupportedMessage = "Hap native decoding is unsupported on this platform.";
		private const uint NativeCpuRgbaCapability = 1u << 3;
		private const uint NativeCompressedPlaneCapability = (1u << 0) | (1u << 1) | (1u << 2);
		private static CSharpFunctionalExtensions.UnitResult<Diagnostic> Failure(string code, string message) => CSharpFunctionalExtensions.UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media"));
		private static CSharpFunctionalExtensions.Result<T, Diagnostic> Failure<T>(string code, string message) => CSharpFunctionalExtensions.Result.Failure<T, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media"));

		[StructLayout(LayoutKind.Sequential)] private struct NativePlane { public uint Format; public uint Bytes; public IntPtr Data; }
		[StructLayout(LayoutKind.Sequential)]
		private struct NativeFrame {
			public uint StructSize; public uint Width; public uint Height; public uint TextureFormat; public ulong FrameIndex; public ulong PresentationTicks; public uint RgbaBytes; public IntPtr Rgba; public uint PlaneCount; public NativePlane Plane0; public NativePlane Plane1; public uint DecodePath;
		}

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_abi_version")] private static extern uint sd_hap_abi_version();
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_capabilities")] private static extern uint sd_hap_capabilities();
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_prepare")] private static extern int sd_hap_prepare(IntPtr handle);
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_acquire_frame")] private static extern int sd_hap_acquire_frame(IntPtr handle, long index, ref NativeFrame frame);
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_release_frame")] private static extern void sd_hap_release_frame(IntPtr handle, ref NativeFrame frame);

		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_open")]
		private static extern int sd_hap_open([MarshalAs(UnmanagedType.LPStr)] string path, int codec, out IntPtr handle);
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_close")]
		private static extern void sd_hap_close(IntPtr handle);
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_play")]
		private static extern int sd_hap_play(IntPtr handle);
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_pause")]
		private static extern int sd_hap_pause(IntPtr handle);
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_stop")]
		private static extern int sd_hap_stop(IntPtr handle);
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_set_speed")]
		private static extern int sd_hap_set_speed(IntPtr handle, double speed);
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_set_loop")]
		private static extern int sd_hap_set_loop(IntPtr handle, int loop);
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_seek")]
		private static extern int sd_hap_seek(IntPtr handle, double seconds);
		[DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "sd_hap_sync")]
		private static extern int sd_hap_sync(IntPtr handle, double logicalSeconds, int demanded);
	}
}
