using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ShitDesigner.Media
{
    public static class HapColorConversion
    {
        public static byte[] ToLinearPremultipliedRgba8(byte[] straightSrgbRgba)
        {
            if (straightSrgbRgba == null) throw new ArgumentNullException(nameof(straightSrgbRgba));
            var result = (byte[])straightSrgbRgba.Clone();
            for (var i = 0; i + 3 < result.Length; i += 4)
            {
                var alpha = result[i + 3] / 255f;
                result[i] = ToByte(LinearFromSrgb(result[i] / 255f) * alpha);
                result[i + 1] = ToByte(LinearFromSrgb(result[i + 1] / 255f) * alpha);
                result[i + 2] = ToByte(LinearFromSrgb(result[i + 2] / 255f) * alpha);
            }
            return result;
        }
        private static float LinearFromSrgb(float value) => value <= 0.04045f ? value / 12.92f : (float)Math.Pow((value + 0.055f) / 1.055f, 2.4f);
        private static byte ToByte(float value) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value * 255f)));
    }

    /// <summary>
    /// Pure managed Hap frame and QuickTime reader. It is deliberately free of
    /// Unity types so the exact bounds/overflow behaviour is covered by the
    /// EditMode runner and can also be used by the native-fixture generator.
    /// The section values and texture mappings follow the Vidvox Hap frame
    /// specification (BSD-2-Clause reference, see ThirdParty/Hap/LICENSE).
    /// </summary>
    public enum HapPlaneFormat
    {
        Bc1,
        Bc3,
        Bc4,
    }

    public sealed class HapPlane
    {
        public HapPlaneFormat Format { get; }
        public byte[] CompressedBlocks { get; }
        public byte[] Rgba8 { get; }

        internal HapPlane(HapPlaneFormat format, byte[] blocks, byte[] rgba8)
        {
            Format = format;
            CompressedBlocks = blocks;
            Rgba8 = rgba8;
        }
    }

    public sealed class HapManagedDecodedFrame
    {
        public int Width { get; }
        public int Height { get; }
        public HapPlane Color { get; }
        public HapPlane Alpha { get; }
        public byte[] Rgba8 { get; }

        internal HapManagedDecodedFrame(int width, int height, HapPlane color, HapPlane alpha, byte[] rgba8)
        {
            Width = width;
            Height = height;
            Color = color;
            Alpha = alpha;
            Rgba8 = rgba8;
        }
    }

    public static class HapFrameDecoder
    {
        private const byte MultiImage = 0x0D;
        private const byte Instructions = 0x01;
        private const byte CompressorTable = 0x02;
        private const byte SizeTable = 0x03;
        private const byte OffsetTable = 0x04;

        public static bool TryDecode(byte[] frame, VideoCodec codec, int width, int height, out HapManagedDecodedFrame decoded, out string error)
        {
            decoded = null;
            error = null;
            if (frame == null || frame.Length == 0) return Fail("frame.empty", out error);
            if (width <= 0 || height <= 0 || width > 16384 || height > 16384) return Fail("frame.dimension", out error);
            if (!IsGuaranteed(codec)) return Fail(codec == VideoCodec.HapR || codec == VideoCodec.HapHdr ? "codec.unsupported.future" : "codec.unsupported", out error);
            try
            {
                var root = ReadSection(frame, 0, frame.Length, out var rootEnd);
                if (root.Type == MultiImage)
                {
                    var children = ReadAllSections(frame, root.DataOffset, root.Length);
                    if (children.Count != 2 || children.Any(x => x.Type == MultiImage)) return Fail("frame.multi_image", out error);
                    if (!TryDecodePlane(frame, children[0], codec, width, height, out var first, out error)) return false;
                    if (!TryDecodePlane(frame, children[1], codec, width, height, out var second, out error)) return false;
                    HapPlane colorPlane;
                    HapPlane alphaPlane;
                    if (first.Format == HapPlaneFormat.Bc3 && second.Format == HapPlaneFormat.Bc4) { colorPlane = first; alphaPlane = second; }
                    else if (first.Format == HapPlaneFormat.Bc4 && second.Format == HapPlaneFormat.Bc3) { colorPlane = second; alphaPlane = first; }
                    else return Fail("frame.plane_pair", out error);
                    var rgba = CombineYCoCg(colorPlane.Rgba8, alphaPlane.Rgba8, width, height);
                    decoded = new HapManagedDecodedFrame(width, height, colorPlane, alphaPlane, rgba);
                    return true;
                }

                if (!TryDecodePlane(frame, root, codec, width, height, out var color, out error)) return false;
                // HapY stores a single BC3 color plane in YCoCg form.  The
                // managed decoder must expose the same RGB-domain frame as
                // the GPU/native backends; leaving this plane in its packed
                // YCoCg channels makes the CPU expected frame disagree with
                // both production GPU paths.
                var output = codec == VideoCodec.HapY
                    ? CombineYCoCg(color.Rgba8, null, width, height)
                    : color.Rgba8;
                if (codec == VideoCodec.Hap1)
                {
                    for (var i = 3; i < output.Length; i += 4) output[i] = 255;
                }
                decoded = new HapManagedDecodedFrame(width, height, color, null, output);
                return true;
            }
            catch (InvalidDataException exception)
            {
                error = exception.Message;
                return false;
            }
            catch (OverflowException)
            {
                return Fail("frame.overflow", out error);
            }
        }

        public static bool TryDecodeSnappy(byte[] compressed, int expectedLength, out byte[] data, out string error)
        {
            data = null;
            error = null;
            try
            {
                if (compressed == null || compressed.Length == 0) return Fail("snappy.empty", out error);
                var p = 0;
                var length = ReadVarint(compressed, ref p);
                if (length > int.MaxValue || (expectedLength >= 0 && length != (ulong)expectedLength)) return Fail("snappy.length", out error);
                data = new byte[checked((int)length)];
                var output = 0;
                while (p < compressed.Length && output < data.Length)
                {
                    var tag = compressed[p++];
                    switch (tag & 3)
                    {
                        case 0:
                            var literalLength = (tag >> 2) & 0x3F;
                            if (literalLength < 60) literalLength++;
                            else
                            {
                                var bytes = literalLength - 59;
                                literalLength = 0;
                                if (bytes > 4 || p + bytes > compressed.Length) throw new InvalidDataException("snappy literal length is truncated");
                                for (var i = 0; i < bytes; i++) literalLength |= compressed[p++] << (8 * i);
                                literalLength++;
                            }
                            if (literalLength < 0 || p > compressed.Length - literalLength || output > data.Length - literalLength) throw new InvalidDataException("snappy literal exceeds bounds");
                            Buffer.BlockCopy(compressed, p, data, output, literalLength);
                            p += literalLength;
                            output += literalLength;
                            break;
                        case 1:
                            if (p >= compressed.Length) throw new InvalidDataException("snappy copy-1 is truncated");
                            var copy1Length = 4 + ((tag >> 2) & 7);
                            var copy1Offset = ((tag & 0xE0) << 3) | compressed[p++];
                            Copy(data, ref output, copy1Offset, copy1Length);
                            break;
                        case 2:
                            if (p + 2 > compressed.Length) throw new InvalidDataException("snappy copy-2 is truncated");
                            var copy2Length = 1 + ((tag >> 2) & 0x3F);
                            var copy2Offset = compressed[p] | (compressed[p + 1] << 8);
                            p += 2;
                            Copy(data, ref output, copy2Offset, copy2Length);
                            break;
                        default:
                            if (p + 4 > compressed.Length) throw new InvalidDataException("snappy copy-4 is truncated");
                            var copy4Length = 1 + ((tag >> 2) & 0x3F);
                            var copy4Offset = compressed[p] | (compressed[p + 1] << 8) | (compressed[p + 2] << 16) | (compressed[p + 3] << 24);
                            p += 4;
                            Copy(data, ref output, copy4Offset, copy4Length);
                            break;
                    }
                }
                if (output != data.Length || p != compressed.Length) throw new InvalidDataException("snappy stream did not end at the declared output length");
                return true;
            }
            catch (InvalidDataException exception) { error = exception.Message; return false; }
            catch (OverflowException) { return Fail("snappy.overflow", out error); }
        }

        private static void Copy(byte[] data, ref int output, int offset, int length)
        {
            if (offset <= 0 || offset > output || length < 0 || output > data.Length - length) throw new InvalidDataException("snappy copy exceeds bounds");
            for (var i = 0; i < length; i++) data[output++] = data[output - offset];
        }

        private static ulong ReadVarint(byte[] bytes, ref int p)
        {
            ulong value = 0;
            for (var shift = 0; shift < 64; shift += 7)
            {
                if (p >= bytes.Length) throw new InvalidDataException("snappy varint is truncated");
                var b = bytes[p++];
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
            }
            throw new InvalidDataException("snappy varint overflow");
        }

        private static bool TryDecodePlane(byte[] frame, Section section, VideoCodec codec, int width, int height, out HapPlane plane, out string error)
        {
            plane = null;
            error = null;
            var format = section.Type & 0x0F;
            var compressor = section.Type >> 4;
            var expected = ExpectedBlockBytes(format, width, height);
            if (expected <= 0) return Fail("frame.texture_format", out error);
            if (codec == VideoCodec.Hap1 && format != 0x0B || codec == VideoCodec.Hap5 && format != 0x0E || codec == VideoCodec.HapY && format != 0x0F || codec == VideoCodec.HapM && format != 0x0F && format != 0x01) return Fail("frame.codec_format", out error);

            byte[] blocks;
            if (compressor == 0x0A)
            {
                if (section.Length != expected) return Fail("frame.raw_size", out error);
                blocks = Slice(frame, section.DataOffset, section.Length);
            }
            else if (compressor == 0x0B)
            {
                if (!TryDecodeSnappy(Slice(frame, section.DataOffset, section.Length), expected, out blocks, out error)) return false;
            }
            else if (compressor == 0x0C)
            {
                if (!TryDecodeComplex(frame, section, expected, out blocks, out error)) return false;
            }
            else return Fail("frame.compressor", out error);

            var planeFormat = format == 0x0B ? HapPlaneFormat.Bc1 : format == 0x01 ? HapPlaneFormat.Bc4 : HapPlaneFormat.Bc3;
            var rgba = DecodeBlocks(blocks, planeFormat, width, height);
            plane = new HapPlane(planeFormat, blocks, rgba);
            return true;
        }

        private static bool TryDecodeComplex(byte[] frame, Section section, int expected, out byte[] blocks, out string error)
        {
            blocks = null;
            error = null;
            var instruction = ReadSection(frame, section.DataOffset, section.Length, out var instructionEnd);
            if (instruction.Type != Instructions) return Fail("frame.instructions", out error);
            var tables = ReadAllSections(frame, instruction.DataOffset, instruction.Length);
            byte[] compressors = null, sizes = null, offsets = null;
            foreach (var table in tables)
            {
                var value = Slice(frame, table.DataOffset, table.Length);
                if (table.Type == CompressorTable) compressors = value;
                else if (table.Type == SizeTable) sizes = value;
                else if (table.Type == OffsetTable) offsets = value;
            }
            if (compressors == null || sizes == null || sizes.Length % 4 != 0 || compressors.Length != sizes.Length / 4) return Fail("frame.tables", out error);
            if (offsets != null && (offsets.Length != sizes.Length || offsets.Length % 4 != 0)) return Fail("frame.offsets", out error);
            blocks = new byte[expected];
            var frameData = instructionEnd;
            var runningOffset = 0;
            var runningOutput = 0;
            for (var i = 0; i < compressors.Length; i++)
            {
                var size = ReadUInt32(sizes, i * 4);
                var offset = offsets == null ? checked((ulong)runningOffset) : (ulong)ReadUInt32(offsets, i * 4);
                if (offset > (ulong)section.Length || size > (ulong)section.Length - offset || (ulong)frameData > (ulong)frame.Length - offset - size) return Fail("frame.chunk_bounds", out error);
                var chunk = Slice(frame, frameData + checked((int)offset), checked((int)size));
                var chunkOutput = compressors[i] == 0x0A ? chunk : compressors[i] == 0x0B ? DecodeSnappyOrThrow(chunk) : throw new InvalidDataException("unsupported chunk compressor");
                if (runningOutput > blocks.Length - chunkOutput.Length) return Fail("frame.chunk_output", out error);
                Buffer.BlockCopy(chunkOutput, 0, blocks, runningOutput, chunkOutput.Length);
                runningOutput += chunkOutput.Length;
                runningOffset = checked(runningOffset + (int)size);
            }
            if (runningOutput != blocks.Length) return Fail("frame.chunk_total", out error);
            return true;
        }

        private static byte[] DecodeSnappyOrThrow(byte[] bytes)
        {
            if (!TryDecodeSnappy(bytes, -1, out var result, out var error)) throw new InvalidDataException(error);
            return result;
        }

        private static int ExpectedBlockBytes(int format, int width, int height)
        {
            var blocks = checked(((width + 3) / 4) * ((height + 3) / 4));
            return format == 0x0B || format == 0x01 ? checked(blocks * 8) : format == 0x0E || format == 0x0F ? checked(blocks * 16) : 0;
        }

        private static byte[] DecodeBlocks(byte[] blocks, HapPlaneFormat format, int width, int height)
        {
            var output = new byte[checked(width * height * 4)];
            var blockWidth = (width + 3) / 4;
            var blockHeight = (height + 3) / 4;
            var bytesPerBlock = format == HapPlaneFormat.Bc1 || format == HapPlaneFormat.Bc4 ? 8 : 16;
            for (var by = 0; by < blockHeight; by++) for (var bx = 0; bx < blockWidth; bx++)
            {
                var offset = (by * blockWidth + bx) * bytesPerBlock;
                for (var py = 0; py < 4; py++) for (var px = 0; px < 4; px++)
                {
                    var x = bx * 4 + px; var y = by * 4 + py;
                    if (x >= width || y >= height) continue;
                    var dst = (y * width + x) * 4;
                    if (format == HapPlaneFormat.Bc1) DecodeBc1(blocks, offset, px, py, output, dst);
                    else if (format == HapPlaneFormat.Bc3) DecodeBc3(blocks, offset, px, py, output, dst);
                    else DecodeBc4(blocks, offset, px, py, output, dst);
                }
            }
            return output;
        }

        private static void DecodeBc1(byte[] b, int o, int x, int y, byte[] dst, int d)
        {
            var c0 = (ushort)(b[o] | b[o + 1] << 8); var c1 = (ushort)(b[o + 2] | b[o + 3] << 8);
            var index = (int)((uint)(b[o + 4] | b[o + 5] << 8 | b[o + 6] << 16 | b[o + 7] << 24) >> (2 * (y * 4 + x)) & 3);
            var colors = new[] { Rgb565(c0), Rgb565(c1), default(byte[]), default(byte[]) };
            if (c0 > c1) { colors[2] = Mix(colors[0], colors[1], 2, 1); colors[3] = Mix(colors[0], colors[1], 1, 2); }
            else { colors[2] = Mix(colors[0], colors[1], 1, 1); colors[3] = new byte[3]; }
            dst[d] = colors[index][0]; dst[d + 1] = colors[index][1]; dst[d + 2] = colors[index][2]; dst[d + 3] = index == 3 && c0 <= c1 ? (byte)0 : (byte)255;
        }

        private static void DecodeBc3(byte[] b, int o, int x, int y, byte[] dst, int d)
        {
            var alpha = DecodeAlpha(b, o, x, y); DecodeBc1(b, o + 8, x, y, dst, d); dst[d + 3] = alpha;
        }

        private static void DecodeBc4(byte[] b, int o, int x, int y, byte[] dst, int d)
        {
            var v = DecodeAlpha(b, o, x, y); dst[d] = v; dst[d + 1] = v; dst[d + 2] = v; dst[d + 3] = 255;
        }

        private static byte DecodeAlpha(byte[] b, int o, int x, int y)
        {
            var a0 = b[o]; var a1 = b[o + 1]; ulong bits = 0;
            for (var i = 0; i < 6; i++) bits |= (ulong)b[o + 2 + i] << (8 * i);
            var index = (int)((bits >> (3 * (y * 4 + x))) & 7);
            if (index == 0) return a0; if (index == 1) return a1;
            if (a0 > a1) return (byte)(((8 - index) * a0 + (index - 1) * a1) / 7);
            if (index <= 5) return (byte)(((6 - index) * a0 + (index - 1) * a1) / 5);
            return index == 6 ? (byte)0 : (byte)255;
        }

        private static byte[] CombineYCoCg(byte[] color, byte[] alpha, int width, int height)
        {
            var output = new byte[color.Length];
            for (var i = 0; i < width * height; i++)
            {
                var co = color[i * 4] / 255f - 0.5f;
                var cg = color[i * 4 + 1] / 255f - 0.5f;
                var scale = color[i * 4 + 2] * (255f / 8f) / 255f + 1f;
                var y = color[i * 4 + 3] / 255f;
                output[i * 4] = ToByte(y + co / scale - cg / scale);
                output[i * 4 + 1] = ToByte(y + cg / scale);
                output[i * 4 + 2] = ToByte(y - co / scale - cg / scale);
                output[i * 4 + 3] = alpha == null ? (byte)255 : alpha[i * 4];
            }
            return output;
        }

        private static byte ToByte(float value) => (byte)Math.Max(0, Math.Min(255, (int)Math.Round(value * 255f)));
        private static byte[] Rgb565(ushort value) => new[] { (byte)(((value >> 11) & 31) * 255 / 31), (byte)(((value >> 5) & 63) * 255 / 63), (byte)((value & 31) * 255 / 31) };
        private static byte[] Mix(byte[] a, byte[] b, int aw, int bw) => new[] { (byte)((a[0] * aw + b[0] * bw) / (aw + bw)), (byte)((a[1] * aw + b[1] * bw) / (aw + bw)), (byte)((a[2] * aw + b[2] * bw) / (aw + bw)) };
        private static bool IsGuaranteed(VideoCodec codec) => codec == VideoCodec.Hap1 || codec == VideoCodec.Hap5 || codec == VideoCodec.HapY || codec == VideoCodec.HapM;
        private static bool Fail(string message, out string error) { error = message; return false; }

        private readonly struct Section
        {
            public readonly int DataOffset; public readonly int Length; public readonly byte Type;
            public Section(int dataOffset, int length, byte type) { DataOffset = dataOffset; Length = length; Type = type; }
        }

        private static Section ReadSection(byte[] bytes, int offset, int available, out int end)
        {
            if (offset < 0 || available < 4 || offset > bytes.Length - available) throw new InvalidDataException("section header is truncated");
            var shortLength = bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16;
            var header = shortLength == 0 ? 8 : 4;
            if (available < header) throw new InvalidDataException("section extended header is truncated");
            var length = shortLength == 0 ? checked(bytes[offset + 4] | bytes[offset + 5] << 8 | bytes[offset + 6] << 16 | bytes[offset + 7] << 24) : shortLength;
            if (length < 0 || length > available - header) throw new InvalidDataException("section exceeds frame bounds");
            end = checked(offset + header + length);
            return new Section(offset + header, length, bytes[offset + 3]);
        }

        private static List<Section> ReadAllSections(byte[] bytes, int offset, int length)
        {
            var result = new List<Section>(); var p = offset; var end = checked(offset + length);
            while (p < end) { var section = ReadSection(bytes, p, end - p, out var next); result.Add(section); p = next; }
            if (p != end) throw new InvalidDataException("section container has trailing bytes");
            return result;
        }

        private static byte[] Slice(byte[] bytes, int offset, int length)
        {
            if (offset < 0 || length < 0 || offset > bytes.Length - length) throw new InvalidDataException("slice exceeds frame bounds");
            var result = new byte[length]; Buffer.BlockCopy(bytes, offset, result, 0, length); return result;
        }

        private static uint ReadUInt32(byte[] bytes, int offset) => checked((uint)(bytes[offset] | bytes[offset + 1] << 8 | bytes[offset + 2] << 16 | bytes[offset + 3] << 24));
    }

    public sealed class HapMovie
    {
        public VideoCodec Codec { get; }
        public int Width { get; }
        public int Height { get; }
        public uint TimeScale { get; }
        public ulong DurationTicks { get; }
        public IReadOnlyList<HapMovieSample> Samples { get; }
        private readonly byte[] _file;

        private HapMovie(byte[] file, VideoCodec codec, int width, int height, uint timeScale, ulong durationTicks, List<HapMovieSample> samples)
        { _file = file; Codec = codec; Width = width; Height = height; TimeScale = timeScale; DurationTicks = durationTicks; Samples = samples; }

        public HapMovieSample ReadSample(int index)
        {
            if (index < 0 || index >= Samples.Count) throw new ArgumentOutOfRangeException(nameof(index));
            var sample = Samples[index];
            var bytes = new byte[sample.Size]; Buffer.BlockCopy(_file, checked((int)sample.Offset), bytes, 0, sample.Size); return sample.WithData(bytes);
        }

        public static bool TryOpen(string path, out HapMovie movie, out string error)
        {
            movie = null; error = null;
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return FailMovie("movie.file_missing", out error);
                var file = File.ReadAllBytes(path); if (file.Length < 16) return FailMovie("movie.truncated", out error);
                var atoms = new List<Atom>(); ParseAtoms(file, 0, file.Length, atoms, 0); _atoms = atoms;
                var moov = atoms.FirstOrDefault(x => x.Type == "moov"); if (moov.Type == null) return FailMovie("movie.moov_missing", out error);
                var trak = FindAtoms(moov, "trak").FirstOrDefault(x => FindAtoms(x, "hdlr").Any(h => HandlerIsVideo(file, h))); if (trak.Type == null) return FailMovie("movie.video_track_missing", out error);
                var mdia = FindAtoms(trak, "mdia").FirstOrDefault(); var mdhd = FindAtoms(mdia, "mdhd").FirstOrDefault(); var stbl = FindAtoms(mdia, "stbl").FirstOrDefault();
                if (mdia.Type == null || mdhd.Type == null || stbl.Type == null) return FailMovie("movie.tables_missing", out error);
                var timeScale = ParseMdhdTimeScale(file, mdhd); var stsd = FindAtoms(stbl, "stsd").FirstOrDefault(); var descriptor = ParseStsd(file, stsd, out var codec);
                if (!descriptor.Valid) return FailMovie(descriptor.Error, out error);
                var stts = ParseStts(file, FindAtoms(stbl, "stts").FirstOrDefault()); var stsc = ParseStsc(file, FindAtoms(stbl, "stsc").FirstOrDefault()); var sizes = ParseStsz(file, FindAtoms(stbl, "stsz").FirstOrDefault()); var offsets = ParseOffsets(file, FindAtoms(stbl, "co64").FirstOrDefault(), FindAtoms(stbl, "stco").FirstOrDefault());
                var samples = BuildSamples(file.Length, stsc, sizes, offsets, stts);
                if (samples.Count == 0) return FailMovie("movie.no_samples", out error);
                var duration = samples.Aggregate<HapMovieSample, ulong>(0, (sum, item) => checked(sum + item.DurationTicks));
                movie = new HapMovie(file, codec, descriptor.Width, descriptor.Height, timeScale, duration, samples); return true;
            }
            catch (InvalidDataException exception) { error = exception.Message; return false; }
            catch (OverflowException) { return FailMovie("movie.overflow", out error); }
            catch (IOException exception) { error = exception.Message; return false; }
        }

        private static bool FailMovie(string value, out string error) { error = value; return false; }
        private readonly struct Atom
        {
            public readonly int Offset; public readonly int Header; public readonly int Size; public readonly string Type;
            public Atom(int offset, int header, int size, string type) { Offset = offset; Header = header; Size = size; Type = type; }
            public int DataOffset => Offset + Header; public int End => Offset + Size;
        }

        private static void ParseAtoms(byte[] bytes, int offset, int length, List<Atom> output, int depth)
        {
            if (depth > 16) throw new InvalidDataException("movie atom nesting is too deep");
            var end = checked(offset + length); var p = offset;
            while (p < end)
            {
                if (end - p < 8) throw new InvalidDataException("movie atom header is truncated");
                var size32 = Be32(bytes, p); var type = Encoding.ASCII.GetString(bytes, p + 4, 4); var header = 8; ulong size = size32;
                if (size32 == 1) { if (end - p < 16) throw new InvalidDataException("movie extended atom header is truncated"); size = Be64(bytes, p + 8); header = 16; }
                else if (size32 == 0) size = (ulong)(end - p);
                if (size < (ulong)header || size > (ulong)(end - p) || size > int.MaxValue) throw new InvalidDataException("movie atom exceeds bounds");
                var atom = new Atom(p, header, checked((int)size), type); output.Add(atom); if (type == "moov" || type == "trak" || type == "mdia" || type == "minf" || type == "stbl") ParseAtoms(bytes, atom.DataOffset, atom.Size - atom.Header, output, depth + 1); p = atom.End;
            }
            if (p != end) throw new InvalidDataException("movie atom table has trailing bytes");
        }

        private static IEnumerable<Atom> FindAtoms(Atom parent, string type) => _atoms.Where(x => x.Offset >= parent.DataOffset && x.End <= parent.End && x.Type == type && x.Offset != parent.Offset).OrderBy(x => x.Offset);
        [ThreadStatic] private static List<Atom> _atoms;
        private static bool HandlerIsVideo(byte[] file, Atom hdlr) => hdlr.Size - hdlr.Header >= 12 && Encoding.ASCII.GetString(file, hdlr.DataOffset + 8, 4) == "vide";
        private static uint ParseMdhdTimeScale(byte[] file, Atom atom) { var p = atom.DataOffset; var version = file[p]; var offset = version == 1 ? 20 : 12; if (atom.Size - atom.Header < offset + 4) throw new InvalidDataException("mdhd is truncated"); var scale = Be32(file, p + offset); if (scale == 0) throw new InvalidDataException("mdhd timescale is zero"); return scale; }

        private readonly struct Descriptor { public readonly int Width, Height; public readonly bool Valid; public readonly string Error; public Descriptor(int width, int height, bool valid, string error) { Width = width; Height = height; Valid = valid; Error = error; } }
        private static Descriptor ParseStsd(byte[] file, Atom atom, out VideoCodec codec)
        {
            codec = VideoCodec.Unknown; var p = atom.DataOffset; if (atom.Size - atom.Header < 16) return new Descriptor(0, 0, false, "movie.stsd_truncated"); var count = Be32(file, p + 4); if (count != 1) return new Descriptor(0, 0, false, "movie.stsd_entry_count"); var entry = p + 8; var size = Be32(file, entry); if (size < 36 || size > atom.End - entry) return new Descriptor(0, 0, false, "movie.stsd_entry_bounds"); var fourcc = Encoding.ASCII.GetString(file, entry + 4, 4); codec = fourcc == "Hap1" ? VideoCodec.Hap1 : fourcc == "Hap5" ? VideoCodec.Hap5 : fourcc == "HapY" ? VideoCodec.HapY : fourcc == "HapM" ? VideoCodec.HapM : fourcc == "HapR" ? VideoCodec.HapR : fourcc == "HapH" ? VideoCodec.HapHdr : VideoCodec.Unknown; if (codec == VideoCodec.Unknown) return new Descriptor(0, 0, false, "movie.codec_unsupported"); var width = (int)Be16(file, entry + 32); var height = (int)Be16(file, entry + 34); if (width <= 0 || height <= 0) return new Descriptor(0, 0, false, "movie.dimension"); return new Descriptor(width, height, true, null);
        }

        private readonly struct StscEntry { public readonly uint FirstChunk, SamplesPerChunk; public StscEntry(uint firstChunk, uint samplesPerChunk) { FirstChunk = firstChunk; SamplesPerChunk = samplesPerChunk; } }
        private readonly struct SttsEntry { public readonly uint Count, Delta; public SttsEntry(uint count, uint delta) { Count = count; Delta = delta; } }
        private static List<SttsEntry> ParseStts(byte[] f, Atom a) { var p = a.DataOffset; var count = Be32(f, p + 4); if (a.Size - a.Header < 8 + count * 8L || count == 0 || count > 1000000) throw new InvalidDataException("movie.stts_bounds"); var list = new List<SttsEntry>(); for (uint i = 0; i < count; i++) list.Add(new SttsEntry(Be32(f, p + 8 + (int)i * 8), Be32(f, p + 12 + (int)i * 8))); return list; }
        private static List<StscEntry> ParseStsc(byte[] f, Atom a) { var p = a.DataOffset; var count = Be32(f, p + 4); if (a.Size - a.Header < 8 + count * 12L || count == 0 || count > 1000000) throw new InvalidDataException("movie.stsc_bounds"); var list = new List<StscEntry>(); for (uint i = 0; i < count; i++) { var first = Be32(f, p + 8 + (int)i * 12); var per = Be32(f, p + 12 + (int)i * 12); if (per == 0) throw new InvalidDataException("movie.stsc_zero"); list.Add(new StscEntry(first, per)); } return list; }
        private static List<uint> ParseStsz(byte[] f, Atom a) { var p = a.DataOffset; var size = Be32(f, p + 4); var count = Be32(f, p + 8); if (count == 0 || count > 1000000) throw new InvalidDataException("movie.stsz_count"); var list = new List<uint>((int)count); if (size != 0) { for (var i = 0; i < count; i++) list.Add(size); } else { if (a.Size - a.Header < 12 + count * 4L) throw new InvalidDataException("movie.stsz_bounds"); for (uint i = 0; i < count; i++) { var value = Be32(f, p + 12 + (int)i * 4); if (value == 0) throw new InvalidDataException("movie.stsz_zero"); list.Add(value); } } return list; }
        private static List<ulong> ParseOffsets(byte[] f, Atom co64, Atom stco) { var a = co64.Type != null ? co64 : stco; if (a.Type == null) throw new InvalidDataException("movie.chunk_offsets_missing"); var p = a.DataOffset; var count = Be32(f, p + 4); if (count == 0 || count > 1000000 || a.Size - a.Header < 8 + count * (a.Type == "co64" ? 8L : 4L)) throw new InvalidDataException("movie.chunk_offsets_bounds"); var list = new List<ulong>((int)count); for (uint i = 0; i < count; i++) list.Add(a.Type == "co64" ? Be64(f, p + 8 + (int)i * 8) : Be32(f, p + 8 + (int)i * 4)); return list; }

        private static List<HapMovieSample> BuildSamples(int fileLength, List<StscEntry> stsc, List<uint> sizes, List<ulong> offsets, List<SttsEntry> stts)
        {
            var samples = new List<HapMovieSample>(); var sampleIndex = 0; ulong tick = 0; var sttsIndex = 0; uint sttsRemain = stts[0].Count;
            for (uint chunk = 1; chunk <= offsets.Count && sampleIndex < sizes.Count; chunk++)
            {
                var mapping = stsc.LastOrDefault(x => x.FirstChunk <= chunk); if (mapping.SamplesPerChunk == 0) throw new InvalidDataException("movie.stsc_mapping"); var offset = offsets[(int)chunk - 1]; for (uint inChunk = 0; inChunk < mapping.SamplesPerChunk && sampleIndex < sizes.Count; inChunk++) { var size = sizes[sampleIndex]; if (offset > (ulong)fileLength || size > fileLength - (long)offset) throw new InvalidDataException("movie.sample_bounds"); if (sttsIndex >= stts.Count || sttsRemain == 0) throw new InvalidDataException("movie.stts_mapping"); samples.Add(new HapMovieSample(offset, checked((int)size), tick, stts[sttsIndex].Delta, null)); offset += size; tick = checked(tick + stts[sttsIndex].Delta); sampleIndex++; if (--sttsRemain == 0 && ++sttsIndex < stts.Count) sttsRemain = stts[sttsIndex].Count; } }
            if (sampleIndex != sizes.Count || sttsIndex != stts.Count) throw new InvalidDataException("movie.sample_table_incomplete"); return samples;
        }
        private static ushort Be16(byte[] f, int p) => checked((ushort)(((uint)f[p] << 8) | f[p + 1])); private static uint Be32(byte[] f, int p) => ((uint)f[p] << 24) | ((uint)f[p + 1] << 16) | ((uint)f[p + 2] << 8) | f[p + 3]; private static ulong Be64(byte[] f, int p) => ((ulong)Be32(f, p) << 32) | Be32(f, p + 4);
    }

    public sealed class HapMovieSample
    {
        public ulong Offset { get; } public int Size { get; } public ulong PresentationTicks { get; } public uint DurationTicks { get; } public byte[] Data { get; }
        internal HapMovieSample(ulong offset, int size, ulong presentationTicks, uint durationTicks, byte[] data) { Offset = offset; Size = size; PresentationTicks = presentationTicks; DurationTicks = durationTicks; Data = data; }
        internal HapMovieSample WithData(byte[] data) => new HapMovieSample(Offset, Size, PresentationTicks, DurationTicks, data);
    }
}
