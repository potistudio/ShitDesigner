using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.IO.Hashing;
using System.Text.Json;
using XxHash128Hasher = System.IO.Hashing.XxHash128;

var output = args.Length == 0 ? Path.Combine("Assets", "ShitDesigner", "Tests", "Media", "Fixtures") : args[0];
if (args.Length > 0 && string.Equals(args[0], "--verify", StringComparison.Ordinal))
{
    output = args.Length > 1 ? args[1] : Path.Combine("Assets", "ShitDesigner", "Tests", "Media", "Fixtures");
    VerifyManifest(output);
    Console.WriteLine($"fixture manifest verified: {output}");
    return;
}
Directory.CreateDirectory(output);
var fixtures = new[]
{
    ("hap1.mov", "Hap1", new[] { Hap1(0xF800), Hap1(0x001F) }),
    ("hap5.mov", "Hap5", new[] { Hap5(0xF800, 255), Hap5(0x001F, 128) }),
    ("hapy.mov", "HapY", new[] { HapY(128, 128, 255, 255), HapY(192, 96, 255, 255) }),
    ("hapm.mov", "HapM", new[] { HapM(128, 128, 255, 255, 255), HapM(192, 96, 255, 255, 128) }),
    ("hap1-snappy-multichunk.mov", "Hap1", new[] { Hap1Complex(0xF800), Hap1Complex(0x001F) }),
};
var preservedVideoEntries = ReadPreservedVideoEntries(Path.Combine(output, "manifest.json"));
var manifest = new StringBuilder("{\n  \"generator\": \"Tools/HapFixtures/Program.cs + Tools/VideoFixtures/generate.ps1\",\n  \"license\": \"self-authored deterministic 4x4 blocks/patterns; no external media\",\n  \"fixtures\": [\n");
var emitted = 0;
for (var i = 0; i < fixtures.Length; i++)
{
    var (name, codec, frames) = fixtures[i];
    var bytes = Movie(codec, frames);
    var path = Path.Combine(output, name);
    File.WriteAllBytes(path, bytes);
    var hash = Convert.ToHexString(XxHash128(bytes)).ToLowerInvariant();
    if (emitted++ > 0) manifest.Append(",\n");
    manifest.Append($"    {{ \"file\": \"{name}\", \"codec\": \"{codec}\", \"width\": 4, \"height\": 4, \"fps\": 60, \"xxh3_128\": \"{hash}\", \"bytes\": {bytes.Length} }}");
}
foreach (var entry in preservedVideoEntries)
{
    if (emitted++ > 0) manifest.Append(",\n");
    manifest.Append("    ").Append(entry);
}
manifest.Append("\n  ]\n}\n");
File.WriteAllText(Path.Combine(output, "manifest.json"), manifest.ToString());
File.WriteAllBytes(Path.Combine(output, "malformed-truncated.mov"), new byte[] { 0, 0, 0, 16, (byte)'f', (byte)'t', (byte)'y', (byte)'p', 0, 0, 0, 0 });
var malformedStsc = Movie("Hap1", new[] { Hap1(0xF800), Hap1(0x001F) });
var stscMarker = Encoding.ASCII.GetBytes("stsc");
for (var p = 0; p <= malformedStsc.Length - stscMarker.Length; p++)
{
    if (!malformedStsc.AsSpan(p, 4).SequenceEqual(stscMarker)) continue;
    Array.Clear(malformedStsc, p + 12, 4); // first_chunk must be one
    break;
}
File.WriteAllBytes(Path.Combine(output, "malformed-stsc.mov"), malformedStsc);

static byte[] Movie(string codec, byte[][] frames)
{
    var ftyp = Atom("ftyp", Encoding.ASCII.GetBytes("qt  \0\0\0\0qt  "));
    var payload = frames.SelectMany(x => x).ToArray();
    var mdat = Atom("mdat", payload);
    var sampleTable = Stbl(codec, frames[0].Length, frames.Length, ftyp.Length + 8);
    var mdia = Atom("mdia", Mdhd(60, frames.Length) .Concat(Atom("hdlr", HdlrVideo())).Concat(Atom("minf", Atom("stbl", sampleTable))).ToArray());
    var moov = Atom("moov", Atom("trak", mdia).ToArray());
    return ftyp.Concat(mdat).Concat(moov).ToArray();
}

static byte[] Stbl(string codec, int sampleSize, int count, int dataOffset)
{
    var stsdEntry = new List<byte>(); stsdEntry.AddRange(new byte[4]); stsdEntry.AddRange(Encoding.ASCII.GetBytes(codec)); stsdEntry.AddRange(new byte[6]); stsdEntry.AddRange(new byte[] { 0, 1 }); stsdEntry.AddRange(new byte[8]); stsdEntry.AddRange(new byte[8]); stsdEntry.AddRange(U16(4)); stsdEntry.AddRange(U16(4)); stsdEntry.AddRange(new byte[8]); stsdEntry.AddRange(U16(1)); stsdEntry.AddRange(new byte[32]); stsdEntry.AddRange(U16(24)); stsdEntry.AddRange(U16(0)); var entryBytes = stsdEntry.ToArray(); BinaryPrimitives.WriteUInt32BigEndian(entryBytes.AsSpan(0, 4), (uint)entryBytes.Length);
    var stsd = Atom("stsd", U32(0).Concat(U32(1)).Concat(entryBytes).ToArray());
    var stts = Atom("stts", U32(0).Concat(U32(1)).Concat(U32((uint)count)).Concat(U32(1)).ToArray());
    var stsc = Atom("stsc", U32(0).Concat(U32(1)).Concat(U32(1)).Concat(U32((uint)count)).Concat(U32(1)).ToArray());
    var stsz = Atom("stsz", U32(0).Concat(U32(0)).Concat(U32((uint)count)).Concat(Enumerable.Repeat(U32((uint)sampleSize), count).SelectMany(x => x)).ToArray());
    var stco = Atom("stco", U32(0).Concat(U32(1)).Concat(U32((uint)dataOffset)).ToArray());
    return stsd.Concat(stts).Concat(stsc).Concat(stsz).Concat(stco).ToArray();
}

static byte[] Mdhd(uint timeScale, int count) => Atom("mdhd", U32(0).Concat(U32(0)).Concat(U32(0)).Concat(U32(timeScale)).Concat(U32((uint)count)).Concat(U16(0x55C4)).Concat(U16(0)).ToArray());
static byte[] HdlrVideo() => U32(0).Concat(U32(0)).Concat(Encoding.ASCII.GetBytes("vide")).Concat(new byte[12]).ToArray();
static byte[] Atom(string type, byte[] data) => U32((uint)(8 + data.Length)).Concat(Encoding.ASCII.GetBytes(type)).Concat(data).ToArray();
static byte[] U16(ushort value) { var result = new byte[2]; BinaryPrimitives.WriteUInt16BigEndian(result, value); return result; }
static byte[] L16(ushort value) { var result = new byte[2]; BinaryPrimitives.WriteUInt16LittleEndian(result, value); return result; }
static byte[] U32(uint value) { var result = new byte[4]; BinaryPrimitives.WriteUInt32BigEndian(result, value); return result; }
static byte[] L32(uint value) { var result = new byte[4]; BinaryPrimitives.WriteUInt32LittleEndian(result, value); return result; }
static byte[] Section(byte type, byte[] data) => U24((uint)data.Length).Concat(new[] { type }).Concat(data).ToArray();
static byte[] U24(uint value) => new[] { (byte)value, (byte)(value >> 8), (byte)(value >> 16) };

static byte[] Hap1(ushort color) => Section(0xAB, Bc1(color));
static byte[] Hap5(ushort color, byte alpha) => Section(0xAE, AlphaBlock(alpha).Concat(Bc1(color)).ToArray());
static byte[] HapY(byte co, byte cg, byte scale, byte y) => Section(0xAF, AlphaBlock(y).Concat(Bc1((ushort)(scale >> 3))).Select((value, index) => index < 8 ? value : index == 8 ? co : index == 9 ? cg : index == 10 ? scale : value).ToArray());
static byte[] HapM(byte co, byte cg, byte scale, byte y, byte alpha) => Section(0x0D, HapY(co, cg, scale, y).Concat(Section(0xA1, AlphaBlock(alpha))).ToArray());
static byte[] Hap1Complex(ushort color)
{
    var block = Bc1(color); var left = Snappy(block[..4]); var right = Snappy(block[4..]);
    var instructions = Section(0x01, Section(0x02, new byte[] { 0x0B, 0x0B }).Concat(Section(0x03, L32((uint)left.Length).Concat(L32((uint)right.Length)).ToArray())).ToArray());
    return Section(0xCB, instructions.Concat(left).Concat(right).ToArray());
}
static byte[] AlphaBlock(byte value) => new byte[] { value, value, 0, 0, 0, 0, 0, 0 };
static byte[] Bc1(ushort color) => L16(color).Concat(L16(color)).Concat(L32(0)).ToArray();
static byte[] Snappy(byte[] literal) => new[] { (byte)literal.Length, (byte)((literal.Length - 1) << 2) }.Concat(literal).ToArray();

static byte[] XxHash128(byte[] bytes) => XxHash128Hasher.Hash(bytes).ToArray();

static void VerifyManifest(string output)
{
    var manifestPath = Path.Combine(output, "manifest.json");
    if (!File.Exists(manifestPath)) throw new InvalidDataException($"Missing fixture manifest: {manifestPath}");
    using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
    if (!document.RootElement.TryGetProperty("fixtures", out var entries) || entries.GetArrayLength() == 0) throw new InvalidDataException("Fixture manifest has no entries.");
    foreach (var entry in entries.EnumerateArray())
    {
        var name = entry.GetProperty("file").GetString() ?? throw new InvalidDataException("Fixture entry has no file name.");
        var expected = entry.GetProperty("xxh3_128").GetString() ?? throw new InvalidDataException("Fixture entry has no XXH3-128 hash.");
        var path = Path.Combine(output, name);
        if (!File.Exists(path)) throw new FileNotFoundException("Fixture listed in manifest is missing.", path);
        var actual = Convert.ToHexString(XxHash128(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"XXH3-128 mismatch for {name}: expected {expected}, got {actual}");
    }
}

static List<string> ReadPreservedVideoEntries(string manifestPath)
{
    var result = new List<string>();
    if (!File.Exists(manifestPath)) return result;
    try
    {
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        if (!document.RootElement.TryGetProperty("fixtures", out var entries)) return result;
        foreach (var entry in entries.EnumerateArray())
        {
            var codec = entry.TryGetProperty("codec", out var value) ? value.GetString() : string.Empty;
            if (!string.Equals(codec, "Hap1", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(codec, "Hap5", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(codec, "HapY", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(codec, "HapM", StringComparison.OrdinalIgnoreCase))
                result.Add(entry.GetRawText());
        }
    }
    catch (JsonException) { /* a fresh Hap generation can repair a bad manifest */ }
    return result;
}
