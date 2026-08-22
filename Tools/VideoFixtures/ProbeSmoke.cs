using System;
using System.IO;
using System.IO.Hashing;
using System.Text.Json;
using ShitDesigner.Media;

if (args.Length > 0 && string.Equals(args[0], "--hash", StringComparison.Ordinal))
{
    if (args.Length < 2) throw new ArgumentException("--hash requires a file path.");
    Console.WriteLine(Convert.ToHexString(XxHash128.Hash(File.ReadAllBytes(args[1]))).ToLowerInvariant());
    return;
}

var root = Path.GetFullPath(Path.Combine("Assets", "ShitDesigner", "Tests", "Media", "Fixtures"));
if (args.Length > 0 && string.Equals(args[0], "--verify", StringComparison.Ordinal))
{
    using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "manifest.json")));
    foreach (var entry in document.RootElement.GetProperty("fixtures").EnumerateArray())
    {
        var path = Path.Combine(root, entry.GetProperty("file").GetString()!);
        if (!File.Exists(path)) throw new FileNotFoundException("Fixture listed in manifest is missing.", path);
        var actual = Convert.ToHexString(XxHash128.Hash(File.ReadAllBytes(path))).ToLowerInvariant();
        if (!string.Equals(actual, entry.GetProperty("xxh3_128").GetString(), StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException($"XXH3-128 mismatch: {path}");
        if (new FileInfo(path).Length != entry.GetProperty("bytes").GetInt64()) throw new InvalidDataException($"Byte count mismatch: {path}");
    }
    Console.WriteLine("video fixture manifest verified");
    return;
}

var probe = new FileVideoMetadataProbe();
var supported = probe.Probe(Path.Combine(root, "h264.mp4"));
if (supported.IsFailure || !supported.Value.Supported || supported.Value.Codec != VideoCodec.H264 || supported.Value.HasAudio) throw new InvalidDataException("H.264 fixture probe contract failed.");
var alpha = probe.Probe(Path.Combine(root, "vp8-alpha.webm"));
if (alpha.IsFailure || !alpha.Value.Supported || alpha.Value.Codec != VideoCodec.VP8 || !alpha.Value.HasAlpha) throw new InvalidDataException("VP8 alpha fixture probe contract failed.");
var audio = probe.Probe(Path.Combine(root, "h264-audio.mp4"));
if (audio.IsFailure || !audio.Value.Supported || !audio.Value.HasAudio) throw new InvalidDataException("Audio metadata fixture probe contract failed.");
var unsupported = probe.Probe(Path.Combine(root, "unsupported-vp9.webm"));
if (unsupported.IsFailure || unsupported.Value.Supported) throw new InvalidDataException("VP9 fixture was incorrectly accepted.");
var malformed = probe.Probe(Path.Combine(root, "malformed-h264-truncated.mp4"));
if (malformed.IsFailure || malformed.Value.Supported) throw new InvalidDataException("Truncated MP4 fixture was incorrectly accepted.");
Console.WriteLine($"video probe smoke: H264={supported.Value.Codec}, VP8Alpha={alpha.Value.HasAlpha}, Audio={audio.Value.HasAudio}, VP9={unsupported.Value.Supported}, Truncated={malformed.Value.Supported}");
