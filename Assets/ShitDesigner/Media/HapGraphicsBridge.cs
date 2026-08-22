using System;
using ShitDesigner.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace ShitDesigner.Media
{
    internal readonly struct HapNativePlaneView
    {
        public uint Format { get; }
        public uint Bytes { get; }
        public IntPtr Data { get; }
        public bool IsValid => (Format == 1u || Format == 2u || Format == 3u) && Bytes > 0 && Data != IntPtr.Zero;
        public HapNativePlaneView(uint format, uint bytes, IntPtr data) { Format = format; Bytes = bytes; Data = data; }
    }

    internal readonly struct HapNativeFrameView
    {
        public uint Width { get; }
        public uint Height { get; }
        public bool IsYCoCg { get; }
        public int PlaneCount { get; }
        public HapNativePlaneView Plane0 { get; }
        public HapNativePlaneView Plane1 { get; }
        public HapNativeFrameView(uint width, uint height, bool isYCoCg, int planeCount, HapNativePlaneView plane0, HapNativePlaneView plane1)
        { Width = width; Height = height; IsYCoCg = isYCoCg; PlaneCount = planeCount; Plane0 = plane0; Plane1 = plane1; }
        public HapNativePlaneView Plane(int index) => index == 0 ? Plane0 : Plane1;
    }

    public interface IHapGraphicsCapabilityProbe
    {
        bool SupportsDirectCompressed { get; }
        bool SupportsCompute { get; }
        bool SupportsCpu { get; }
        string Diagnostic { get; }
    }

    /// <summary>Optional per-plane probe. Keeping this separate preserves the
    /// small fake probe contract used by EditMode tests.</summary>
    public interface IHapPlaneGraphicsCapabilityProbe
    {
        bool SupportsPlane(uint format);
    }

    public sealed class UnityHapGraphicsCapabilityProbe : IHapGraphicsCapabilityProbe, IHapPlaneGraphicsCapabilityProbe
    {
        public bool SupportsDirectCompressed { get; }
        public bool SupportsCompute => SystemInfo.supportsComputeShaders;
        public bool SupportsCpu => SystemInfo.IsFormatSupported(GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormatUsage.Render) && SystemInfo.IsFormatSupported(GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormatUsage.Render);
        public string Diagnostic { get; }

        public UnityHapGraphicsCapabilityProbe()
        {
            var bc1 = SystemInfo.SupportsTextureFormat(TextureFormat.DXT1);
            var bc3 = SystemInfo.SupportsTextureFormat(TextureFormat.DXT5);
            var bc4 = SystemInfo.IsFormatSupported(GraphicsFormat.R_BC4_UNorm, GraphicsFormatUsage.Sample);
            SupportsDirectCompressed = bc1 || bc3 || bc4;
            Diagnostic = $"api={SystemInfo.graphicsDeviceType};bc1={bc1};bc3={bc3};bc4={bc4};compute={SupportsCompute};cpu={SupportsCpu}";
            _bc1 = bc1; _bc3 = bc3; _bc4 = bc4;
        }

        private readonly bool _bc1;
        private readonly bool _bc3;
        private readonly bool _bc4;
        public bool SupportsPlane(uint format) => format == 1 ? _bc1 : format == 2 ? _bc3 : format == 3 && _bc4;
    }

    public sealed class HapTextureLease : IDisposable
    {
        private bool _released;
        internal HapTextureLease(RenderTexture texture, Texture2D[] sources, RenderTexture[] intermediates, ComputeBuffer[] computeBuffers, HapDecodePath path, string diagnostic)
        { Texture = texture; Sources = sources ?? Array.Empty<Texture2D>(); Intermediates = intermediates ?? Array.Empty<RenderTexture>(); ComputeBuffers = computeBuffers ?? Array.Empty<ComputeBuffer>(); Path = path; Diagnostic = diagnostic ?? string.Empty; }
        public RenderTexture Texture { get; }
        public HapDecodePath Path { get; }
        public string Diagnostic { get; }
        internal Texture2D[] Sources { get; }
        internal RenderTexture[] Intermediates { get; }
        internal ComputeBuffer[] ComputeBuffers { get; }
        public bool IsReleased => _released;
        public void Dispose()
        {
            if (_released) return;
            _released = true;
            if (Texture != null) { if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(Texture); else UnityEngine.Object.DestroyImmediate(Texture); }
            foreach (var intermediate in Intermediates) if (intermediate != null) { if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(intermediate); else UnityEngine.Object.DestroyImmediate(intermediate); }
            foreach (var source in Sources) if (source != null) { if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(source); else UnityEngine.Object.DestroyImmediate(source); }
            foreach (var computeBuffer in ComputeBuffers) if (computeBuffer != null) computeBuffer.Release();
        }
    }

    /// <summary>Media-owned Unity bridge. Rendering never crosses into this
    /// class: the lease exposes only a RenderTexture and decode diagnostic.
    /// Every path ends in RGBA16F linear premultiplied output.</summary>
    public sealed class HapUnityGraphicsBridge : IDisposable
    {
        private readonly IHapGraphicsCapabilityProbe _probe;
        private readonly Material _yCoCgMaterial;
        private readonly Material _alphaMaterial;
        private readonly Material _premultiplyMaterial;
        private readonly ComputeShader _computeShader;
        private readonly bool _ownsYCoCgMaterial;
        private readonly bool _ownsAlphaMaterial;
        private readonly bool _ownsPremultiplyMaterial;
        private bool _disposed;

        public HapUnityGraphicsBridge(IHapGraphicsCapabilityProbe probe = null, ComputeShader computeShader = null)
            : this(probe, computeShader, null, null, null)
        {
        }

        // The explicit resource overload is used by deterministic graphics
        // tests. Production callers use the shader assets loaded by name above.
        public HapUnityGraphicsBridge(IHapGraphicsCapabilityProbe probe, ComputeShader computeShader, Material premultiplyMaterial, Material yCoCgMaterial, Material alphaMaterial)
        {
            _probe = probe ?? new UnityHapGraphicsCapabilityProbe();
            _ownsYCoCgMaterial = yCoCgMaterial == null;
            _ownsAlphaMaterial = alphaMaterial == null;
            _ownsPremultiplyMaterial = premultiplyMaterial == null;
            _yCoCgMaterial = yCoCgMaterial ?? LoadMaterial("Hidden/ShitDesigner/HapYToLinear");
            _alphaMaterial = alphaMaterial ?? LoadMaterial("Hidden/ShitDesigner/HapAlphaCompose");
            _premultiplyMaterial = premultiplyMaterial ?? LoadMaterial("Hidden/ShitDesigner/HapPremultiply");
            _computeShader = computeShader ?? Resources.Load<ComputeShader>("HapDecode");
        }

        public IHapGraphicsCapabilityProbe Probe => _probe;

        /// <summary>Exposes the deterministic path decision for diagnostics and
        /// EditMode tests without allocating Unity objects.</summary>
        public HapDecodePath SelectDecodePath(HapDecodedFrame frame) => SelectPath(frame);

        public Result<HapTextureLease> Upload(HapDecodedFrame frame)
        {
            if (_disposed) return Failure<HapTextureLease>("media.hap.graphics.disposed", "Hap graphics bridge is disposed.");
            if (frame == null || frame.Width == 0 || frame.Height == 0) return Failure<HapTextureLease>("media.hap.graphics.frame", "A decoded Hap frame is required.");
            var path = SelectPath(frame);
            if (path == HapDecodePath.Unsupported) return Failure<HapTextureLease>("media.hap.graphics.unsupported", _probe.Diagnostic);
            Texture2D[] sources = Array.Empty<Texture2D>();
            RenderTexture output = null;
            var intermediates = new System.Collections.Generic.List<RenderTexture>();
            ComputeBuffer[] computeBuffers = Array.Empty<ComputeBuffer>();
            try
            {
                var descriptor = new RenderTextureDescriptor((int)frame.Width, (int)frame.Height, GraphicsFormat.R16G16B16A16_SFloat, 0) { msaaSamples = 1, useMipMap = false, autoGenerateMips = false, enableRandomWrite = path == HapDecodePath.Compute };
                output = new RenderTexture(descriptor) { name = "ShitDesigner.Hap.Output" };
                output.Create();
                switch (path)
                {
                    case HapDecodePath.DirectCompressed:
                        sources = UploadSources(frame);
                        RenderDirect(frame.IsYCoCg, sources, output, null, intermediates);
                        break;
                    case HapDecodePath.Compute:
                        RenderCompute(frame, output, out computeBuffers);
                        break;
                    default:
                        RenderCpu(frame, output, sources, out sources);
                        break;
                }
                return Result<HapTextureLease>.Success(new HapTextureLease(output, sources, intermediates.ToArray(), computeBuffers, path, _probe.Diagnostic));
            }
            catch (Exception exception)
            {
                if (output != null) Destroy(output);
                foreach (var intermediate in intermediates) if (intermediate != null) Destroy(intermediate);
                foreach (var source in sources) if (source != null) Destroy(source);
                foreach (var computeBuffer in computeBuffers) if (computeBuffer != null) computeBuffer.Release();
                return Failure<HapTextureLease>("media.hap.graphics.upload", exception.Message);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_ownsYCoCgMaterial && _yCoCgMaterial != null) Destroy(_yCoCgMaterial);
            if (_ownsAlphaMaterial && _alphaMaterial != null) Destroy(_alphaMaterial);
            if (_ownsPremultiplyMaterial && _premultiplyMaterial != null) Destroy(_premultiplyMaterial);
        }

        internal bool TryUploadNativeDirect(HapNativeFrameView frame, HapTextureLease reusable, out HapTextureLease lease)
        {
            lease = null;
            if (_disposed || !CanUploadNativeDirect(frame)) return false;
            if (CanReuseNativeDirect(frame, reusable))
            {
                try
                {
                    UpdateNativeSources(frame, reusable.Sources);
                    var intermediate = reusable.Intermediates.Length == 0 ? null : reusable.Intermediates[0];
                    RenderDirect(frame.IsYCoCg, reusable.Sources, reusable.Texture, intermediate, null);
                    lease = reusable;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            Texture2D[] sources = Array.Empty<Texture2D>();
            RenderTexture output = null;
            var createdIntermediates = new System.Collections.Generic.List<RenderTexture>();
            try
            {
                var descriptor = new RenderTextureDescriptor((int)frame.Width, (int)frame.Height, GraphicsFormat.R16G16B16A16_SFloat, 0) { msaaSamples = 1, useMipMap = false, autoGenerateMips = false, enableRandomWrite = false };
                output = new RenderTexture(descriptor) { name = "ShitDesigner.Hap.Output" };
                output.Create();
                sources = CreateNativeSources(frame);
                RenderDirect(frame.IsYCoCg, sources, output, null, createdIntermediates);
                lease = new HapTextureLease(output, sources, createdIntermediates.ToArray(), Array.Empty<ComputeBuffer>(), HapDecodePath.DirectCompressed, _probe.Diagnostic);
                return true;
            }
            catch
            {
                if (output != null) Destroy(output);
                foreach (var intermediate in createdIntermediates) if (intermediate != null) Destroy(intermediate);
                foreach (var source in sources) if (source != null) Destroy(source);
                return false;
            }
        }

        private HapDecodePath SelectPath(HapDecodedFrame frame)
        {
            if (_probe.SupportsDirectCompressed && frame.Planes.Length > 0 && SupportsAllPlanes(frame) && CanRenderDirect(frame)) return HapDecodePath.DirectCompressed;
            if (_probe.SupportsCompute && _computeShader != null && frame.Planes.Length > 0) return HapDecodePath.Compute;
            return _probe.SupportsCpu ? HapDecodePath.Cpu : HapDecodePath.Unsupported;
        }

        private bool CanRenderDirect(HapDecodedFrame frame)
        {
            if (frame.IsYCoCg) return _yCoCgMaterial != null && (frame.Planes.Length < 2 || _alphaMaterial != null);
            return _premultiplyMaterial != null;
        }

        private bool SupportsAllPlanes(HapDecodedFrame frame)
        {
            var detailed = _probe as IHapPlaneGraphicsCapabilityProbe;
            if (detailed == null) return _probe.SupportsDirectCompressed;
            foreach (var plane in frame.Planes) if (!detailed.SupportsPlane(plane.Format)) return false;
            return true;
        }

        private void RenderDirect(bool isYCoCg, Texture2D[] sources, RenderTexture output, RenderTexture reusableColor, System.Collections.Generic.List<RenderTexture> createdIntermediates)
        {
            if (sources.Length == 0) throw new InvalidOperationException("A compressed plane is required for the direct Hap path.");
            if (!isYCoCg)
            {
                _premultiplyMaterial.SetTexture("_MainTex", sources[0]);
                Graphics.Blit(sources[0], output, _premultiplyMaterial);
                return;
            }

            if (sources.Length == 1)
            {
                _yCoCgMaterial.SetTexture("_MainTex", sources[0]);
                Graphics.Blit(sources[0], output, _yCoCgMaterial);
                return;
            }

            var color = reusableColor;
            if (color == null)
            {
                var colorDescriptor = output.descriptor;
                colorDescriptor.enableRandomWrite = false;
                color = new RenderTexture(colorDescriptor) { name = "ShitDesigner.Hap.QColor" };
                color.Create();
                createdIntermediates?.Add(color);
            }
            _yCoCgMaterial.SetTexture("_MainTex", sources[0]);
            Graphics.Blit(sources[0], color, _yCoCgMaterial);
            _alphaMaterial.SetTexture("_MainTex", color);
            _alphaMaterial.SetTexture("_AlphaTex", sources[1]);
            Graphics.Blit(color, output, _alphaMaterial);
        }

        private bool CanUploadNativeDirect(HapNativeFrameView frame)
        {
            if (frame.Width == 0 || frame.Height == 0 || frame.PlaneCount < 1 || frame.PlaneCount > 2 || !_probe.SupportsDirectCompressed) return false;
            if (!frame.Plane0.IsValid || frame.PlaneCount == 2 && !frame.Plane1.IsValid) return false;
            if (frame.IsYCoCg ? _yCoCgMaterial == null || frame.PlaneCount == 2 && _alphaMaterial == null : _premultiplyMaterial == null) return false;
            var detailed = _probe as IHapPlaneGraphicsCapabilityProbe;
            for (var i = 0; i < frame.PlaneCount; i++)
            {
                var plane = frame.Plane(i);
                if (detailed != null && !detailed.SupportsPlane(plane.Format)) return false;
                if (plane.Bytes != ExpectedPlaneBytes(plane.Format, frame.Width, frame.Height)) return false;
            }
            return true;
        }

        private static bool CanReuseNativeDirect(HapNativeFrameView frame, HapTextureLease reusable)
        {
            if (reusable == null || reusable.IsReleased || reusable.Path != HapDecodePath.DirectCompressed || reusable.Texture == null || reusable.Sources.Length != frame.PlaneCount) return false;
            if (reusable.Texture.width != (int)frame.Width || reusable.Texture.height != (int)frame.Height) return false;
            if (frame.IsYCoCg && frame.PlaneCount == 2 && reusable.Intermediates.Length != 1) return false;
            if ((!frame.IsYCoCg || frame.PlaneCount == 1) && reusable.Intermediates.Length != 0) return false;
            return true;
        }

        private static Texture2D[] CreateNativeSources(HapNativeFrameView frame)
        {
            var sources = new Texture2D[frame.PlaneCount];
            try
            {
                for (var i = 0; i < sources.Length; i++)
                {
                    var plane = frame.Plane(i);
                    sources[i] = CreateCompressedTexture(frame.Width, frame.Height, plane.Format);
                    sources[i].name = "ShitDesigner.Hap.BlockPlane" + i;
                    sources[i].LoadRawTextureData(plane.Data, checked((int)plane.Bytes));
                    sources[i].Apply(false, false);
                }
                return sources;
            }
            catch
            {
                foreach (var source in sources) if (source != null) Destroy(source);
                throw;
            }
        }

        private static void UpdateNativeSources(HapNativeFrameView frame, Texture2D[] sources)
        {
            for (var i = 0; i < sources.Length; i++)
            {
                var plane = frame.Plane(i);
                sources[i].LoadRawTextureData(plane.Data, checked((int)plane.Bytes));
                sources[i].Apply(false, false);
            }
        }

        private static Texture2D CreateCompressedTexture(uint width, uint height, uint format)
        {
            if (format == 1u) return new Texture2D((int)width, (int)height, TextureFormat.DXT1, false, true);
            if (format == 2u) return new Texture2D((int)width, (int)height, TextureFormat.DXT5, false, true);
            if (format == 3u) return new Texture2D((int)width, (int)height, GraphicsFormat.R_BC4_UNorm, TextureCreationFlags.None);
            throw new InvalidOperationException("The Hap frame contains an unknown compressed plane format.");
        }

        private static uint ExpectedPlaneBytes(uint format, uint width, uint height)
        {
            var blocks = checked(((ulong)width + 3UL) / 4UL * (((ulong)height + 3UL) / 4UL));
            var bytes = checked(blocks * (format == 1u || format == 3u ? 8UL : 16UL));
            return bytes > uint.MaxValue ? 0u : (uint)bytes;
        }

        private void RenderCompute(HapDecodedFrame frame, RenderTexture output, out ComputeBuffer[] buffers)
        {
            if (_computeShader == null) throw new InvalidOperationException("Hap compute shader is unavailable.");
            if (frame.Planes.Length == 0) throw new InvalidOperationException("A compressed plane is required for the compute Hap path.");
            var kernel = _computeShader.FindKernel("HapDecode");
            buffers = new ComputeBuffer[frame.Planes.Length];
            try
            {
                for (var i = 0; i < frame.Planes.Length; i++)
                {
                    var words = ToWords(frame.Planes[i].Blocks);
                    buffers[i] = new ComputeBuffer(words.Length, sizeof(uint), ComputeBufferType.Structured);
                    buffers[i].SetData(words);
                }
            }
            catch
            {
                for (var i = 0; i < buffers.Length; i++) if (buffers[i] != null) { buffers[i].Release(); buffers[i] = null; }
                throw;
            }
            _computeShader.SetBuffer(kernel, "ColorBlocks", buffers[0]);
            // The kernel declares AlphaBlocks unconditionally.  Bind a valid
            // buffer even for one-plane Hap1/Hap5/HapY frames; an unbound
            // StructuredBuffer is a GPU validation error on D3D12 and makes
            // the result undefined even when the shader branch does not read
            // it.  Only Hap Q Alpha (the YCoCg two-plane form) uses the
            // second buffer as an alpha plane.
            _computeShader.SetBuffer(kernel, "AlphaBlocks", buffers.Length > 1 ? buffers[1] : buffers[0]);
            _computeShader.SetTexture(kernel, "Result", output);
            _computeShader.SetInt("Width", (int)frame.Width);
            _computeShader.SetInt("Height", (int)frame.Height);
            _computeShader.SetInt("ColorFormat", (int)frame.Planes[0].Format);
            _computeShader.SetInt("AlphaFormat", buffers.Length > 1 ? (int)frame.Planes[1].Format : 0);
            _computeShader.SetInt("HasAlpha", frame.IsYCoCg && buffers.Length > 1 ? 1 : 0);
            _computeShader.SetInt("IsYCoCg", frame.IsYCoCg ? 1 : 0);
            _computeShader.Dispatch(kernel, ((int)frame.Width + 7) / 8, ((int)frame.Height + 7) / 8, 1);
        }

        private static uint[] ToWords(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || (bytes.Length & 3) != 0) throw new InvalidOperationException("A Hap compute plane must be 32-bit aligned.");
            var words = new uint[bytes.Length / 4];
            for (var i = 0; i < words.Length; i++) words[i] = (uint)(bytes[i * 4] | (bytes[i * 4 + 1] << 8) | (bytes[i * 4 + 2] << 16) | (bytes[i * 4 + 3] << 24));
            return words;
        }

        private void RenderCpu(HapDecodedFrame frame, RenderTexture output, Texture2D[] oldSources, out Texture2D[] sources)
        {
            var requiredBytes = checked((ulong)frame.Width * frame.Height * 4u);
            if (frame.Rgba8PremultipliedLinear == null || (ulong)frame.Rgba8PremultipliedLinear.Length < requiredBytes) throw new InvalidOperationException("The CPU fallback frame is incomplete.");
            var cpu = new Texture2D((int)frame.Width, (int)frame.Height, TextureFormat.RGBA32, false, true) { name = "ShitDesigner.Hap.CpuUpload" };
            cpu.SetPixelData(frame.Rgba8PremultipliedLinear, 0); cpu.Apply(false, true); Graphics.Blit(cpu, output);
            foreach (var source in oldSources) if (source != null) Destroy(source);
            sources = new[] { cpu };
        }

        private Texture2D[] UploadSources(HapDecodedFrame frame)
        {
            var sources = new Texture2D[frame.Planes.Length];
            try
            {
                for (var i = 0; i < frame.Planes.Length; i++)
                {
                    var plane = frame.Planes[i];
                    if (plane.Format == 1)
                        sources[i] = new Texture2D((int)frame.Width, (int)frame.Height, TextureFormat.DXT1, false, true);
                    else if (plane.Format == 2)
                        sources[i] = new Texture2D((int)frame.Width, (int)frame.Height, TextureFormat.DXT5, false, true);
                    else if (plane.Format == 3)
                        sources[i] = new Texture2D((int)frame.Width, (int)frame.Height, GraphicsFormat.R_BC4_UNorm, TextureCreationFlags.None);
                    else throw new InvalidOperationException("The Hap frame contains an unknown compressed plane format.");
                    sources[i].name = "ShitDesigner.Hap.BlockPlane" + i;
                    sources[i].LoadRawTextureData(plane.Blocks); sources[i].Apply(false, true);
                }
                return sources;
            }
            catch
            {
                foreach (var source in sources) if (source != null) Destroy(source);
                throw;
            }
        }

        private static Material LoadMaterial(string shaderName) { var shader = Shader.Find(shaderName); return shader == null ? null : new Material(shader); }
        private static Texture2D[] Append(Texture2D[] array, Texture2D item) { var result = new Texture2D[array.Length + 1]; Array.Copy(array, result, array.Length); result[array.Length] = item; return result; }
        private static void Destroy(UnityEngine.Object value) { if (UnityEngine.Application.isPlaying) UnityEngine.Object.Destroy(value); else UnityEngine.Object.DestroyImmediate(value); }
        private static Result<T> Failure<T>(string code, string message) => Result<T>.Failure(new ShitDesigner.Core.Diagnostic(new ShitDesigner.Core.DiagnosticCode(code), ShitDesigner.Core.Severity.Error, message ?? string.Empty, module: "media"));
    }
}
