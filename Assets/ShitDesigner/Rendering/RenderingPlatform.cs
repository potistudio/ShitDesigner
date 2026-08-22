using System;
using System.Collections.Generic;
using ShitDesigner.Core;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace ShitDesigner.Rendering
{
    public interface IRenderingPlatformCapabilityPort
    {
        RenderingPlatformCapabilities Capabilities { get; }
        bool IsFormatSupported(GraphicsFormat format, GraphicsFormatUsage usage);
    }

    public sealed class UnityRenderingPlatformCapabilityPort : IRenderingPlatformCapabilityPort
    {
        public RenderingPlatformCapabilities Capabilities => RenderingPlatformCapabilities.FromUnity();
        public bool IsFormatSupported(GraphicsFormat format, GraphicsFormatUsage usage) => SystemInfo.IsFormatSupported(format, usage);
    }

    public static class RenderingFormatPolicy
    {
        public static Result ValidateInternalFormat(ProgramDynamicRange range, IRenderingPlatformCapabilityPort capabilities)
        {
            if (capabilities == null) throw new ArgumentNullException(nameof(capabilities));
            var format = ProgramHoldFormatPolicy.FormatFor(range);
            if (!capabilities.IsFormatSupported(format, GraphicsFormatUsage.Render)
                || !capabilities.IsFormatSupported(format, GraphicsFormatUsage.Sample)
                || !capabilities.IsFormatSupported(format, GraphicsFormatUsage.LoadStore))
                return Result.Failure(new Diagnostic(new DiagnosticCode("rendering.format.unsupported"), Severity.Fatal,
                    "The selected project color format is not supported for render, sample, and load/store usage.",
                    detail: new DiagnosticDetail(new[] { new KeyValuePair<string, string>("format", format.ToString()) })));
            return Result.Success();
        }
    }
}
