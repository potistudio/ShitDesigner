using System;
using System.IO;
using ShitDesigner.Media;

var api = new PInvokeHapNativeApi();
var plugin = api.ProbeInstalledBinary();
if (!plugin.IsAvailable) throw new InvalidOperationException($"native probe failed: {plugin.DiagnosticCode} {plugin.Message}");
Console.WriteLine($"managed native probe abi={plugin.AbiVersion} caps=0x{plugin.Capabilities:X8}");
var cases = new[]
{
    ("hap1.mov", VideoCodec.Hap1, 1, 0, 0, 255, 255),
    ("hap5.mov", VideoCodec.Hap5, 1, 0, 0, 128, 128),
    ("hapy.mov", VideoCodec.HapY, 1, 255, 79, 255, 255),
    ("hapm.mov", VideoCodec.HapM, 1, 128, 40, 128, 128),
    ("hap1-snappy-multichunk.mov", VideoCodec.Hap1, 1, 0, 0, 255, 255),
};
foreach (var item in cases)
{
    var path = Path.GetFullPath(Path.Combine("Assets", "ShitDesigner", "Tests", "Media", "Fixtures", item.Item1));
    var request = new VideoPrepareRequest(VideoSource.FromFile(path), VideoProbeResult.SupportedVideo(VideoContainer.Mov, item.Item2, hasAlpha: item.Item2 == VideoCodec.Hap5 || item.Item2 == VideoCodec.HapM));
    var open = api.Open(request);
    if (open.IsFailure) throw new InvalidOperationException(open.Diagnostic.Code.Value);
    try
    {
        Ensure(api.SetSpeed(open.Value, 1.25), item.Item1 + " speed");
        Ensure(api.SetLoop(open.Value, true), item.Item1 + " loop");
        Ensure(api.Play(open.Value), item.Item1 + " play");
        Ensure(api.SyncToGraphClock(open.Value, 0.25, true), item.Item1 + " sync");
        Ensure(api.Pause(open.Value), item.Item1 + " pause");
        Ensure(api.Seek(open.Value, 0.0), item.Item1 + " seek");
        var frame = api.AcquireFrame(open.Value, item.Item3);
        if (frame.IsFailure) throw new InvalidOperationException(frame.Diagnostic.Code.Value);
        var rgba = frame.Value.Rgba8PremultipliedLinear;
        if (rgba[0] != item.Item4 || rgba[1] != item.Item5 || rgba[2] != item.Item6 || rgba[3] != item.Item7) throw new InvalidOperationException($"{item.Item1} pixel contract failed: {rgba[0]},{rgba[1]},{rgba[2]},{rgba[3]}");
        if (rgba[0] > rgba[3] || rgba[1] > rgba[3] || rgba[2] > rgba[3]) throw new InvalidOperationException($"{item.Item1} is not premultiplied");
        if ((item.Item2 == VideoCodec.HapM && frame.Value.Planes.Length != 2) || (item.Item2 != VideoCodec.HapM && frame.Value.Planes.Length != 1)) throw new InvalidOperationException($"{item.Item1} plane contract failed");
        Console.WriteLine($"managed {item.Item1} frame={frame.Value.FrameIndex} planes={frame.Value.Planes.Length} rgba={rgba[0]},{rgba[1]},{rgba[2]},{rgba[3]}");
    }
    finally { api.Close(open.Value); api.Close(open.Value); }
}

static void Ensure(ShitDesigner.Core.Result result, string operation)
{
    if (result.IsFailure) throw new InvalidOperationException(operation + ": " + result.Diagnostic.Code.Value);
}
