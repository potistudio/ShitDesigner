#ifndef SHITDESIGNER_VJ_UTILITY_INCLUDED
#define SHITDESIGNER_VJ_UTILITY_INCLUDED

#include "Assets/ShitDesigner/Shaders/Includes/VJCommon.hlsl"

float4 VJUtilitySource(sampler2D sourceTexture, float2 uv)
{
    return VJFinite4(tex2D(sourceTexture, saturate(uv)));
}

float VJUtilityLuma(float3 color) { return VJLuma(VJFinite3(color)); }

float VJUtilityDigit(float2 uv, int digit)
{
    float2 p = uv * float2(3.0, 5.0);
    float h = step(0.78, frac(p.x)) * step(0.1, p.y) * step(p.y, 0.9);
    float v = step(0.78, frac(p.y)) * step(0.1, p.x) * step(p.x, 0.9);
    if (digit == 1) return h + v * step(0.55, p.x);
    if (digit == 2) return v + h * step(0.5, p.y);
    if (digit == 3) return v * step(0.5, p.x) + h;
    return h + v;
}

float3 VJUtilitySmpte(float value)
{
    if (value < 0.125) return float3(0.75, 0.75, 0.75);
    if (value < 0.25) return float3(0.75, 0.75, 0.0);
    if (value < 0.375) return float3(0.0, 0.75, 0.75);
    if (value < 0.5) return float3(0.0, 0.75, 0.0);
    if (value < 0.625) return float3(0.75, 0.0, 0.75);
    if (value < 0.75) return float3(0.75, 0.0, 0.0);
    if (value < 0.875) return float3(0.0, 0.0, 0.75);
    return float3(0.0, 0.0, 0.0);
}

float3 VJUtilityDisplayTransform(float3 value, float exposure)
{
    value = max(VJFinite3(value) * exp2(exposure), 0.0);
    return saturate((value * (2.51 * value + 0.03)) / max(value * (2.43 * value + 0.59) + 0.14, 1.0e-4));
}

float4 VJUtilityEvaluate(int variant, sampler2D sourceTexture, sampler2D compareTexture,
    float2 uv, float4 resolution, float frame, float exposure, float channel,
    float threshold, float rangeMode)
{
    float2 safeUv = saturate(VJFinite2(uv));
    float4 source = VJUtilitySource(sourceTexture, safeUv);
    float4 compare = VJUtilitySource(compareTexture, safeUv);
    float3 sourceStraightRgb = VJUnpremultiply(source).rgb;
    float aspect = max(resolution.x / max(resolution.y, 1.0), 1.0e-4);
    float2 coord = (safeUv - 0.5) * float2(aspect, 1.0);
    float gridX = step(0.97, frac(safeUv.x * 10.0));
    float gridY = step(0.97, frac(safeUv.y * 10.0));
    float luma = VJUtilityLuma(source.rgb);

    if (variant == 0) // UV Map
        return float4(safeUv, 0.0, 1.0);
    if (variant == 1) // Test Grid
        return float4(lerp(float3(0.035, 0.04, 0.05), float3(0.75, 0.8, 0.9), max(gridX, gridY)), 1.0);
    if (variant == 2) // SMPTE Bars
        return float4(VJUtilitySmpte(safeUv.x), 1.0);
    if (variant == 3) // HDR Ramp
        return float4(safeUv.x * exp2(exposure), safeUv.x * 0.6, safeUv.x * 0.25, 1.0);
    if (variant == 4) // Alpha Checker
    {
        float checker = fmod(floor(safeUv.x * 16.0) + floor(safeUv.y * 16.0), 2.0);
        return VJPremultiply(float4(lerp(float3(0.08, 0.08, 0.08), float3(0.38, 0.38, 0.38), checker), source.a));
    }
    if (variant == 5) // Safe Area
    {
        float border = step(0.94, abs(coord.x * 2.0)) + step(0.92, abs(coord.y * 2.0));
        float center = step(0.98, abs(coord.x * 2.0)) + step(0.96, abs(coord.y * 2.0));
        return VJPremultiply(float4(lerp(float3(0.0, 0.6, 0.2), float3(1.0, 0.2, 0.05), saturate(center)), saturate(border + center)));
    }
    if (variant == 6) // Resolution Label
    {
        float glyph = VJUtilityDigit(frac(safeUv * float2(8.0, 3.0)), (int)floor(resolution.x) % 4);
        return float4(glyph.xxx, glyph);
    }
    if (variant == 7) // Frame Counter Glyphs
    {
        float glyph = VJUtilityDigit(frac(safeUv * float2(12.0, 3.0)), (int)floor(frame) % 4);
        return float4(float3(0.2, 0.8, 1.0) * glyph, glyph);
    }
    if (variant == 8) // Premultiplied Alpha Checker
    {
        float violation = step(source.a + 1.0e-4, max(source.r, max(source.g, source.b)));
        float checker = fmod(floor(safeUv.x * 12.0) + floor(safeUv.y * 12.0), 2.0);
        float3 base = lerp(float3(0.04, 0.04, 0.04), float3(0.18, 0.18, 0.18), checker);
        return float4(lerp(base, float3(1.0, 0.03, 0.0), violation), 1.0);
    }
    if (variant == 9) // NaN / Inf Highlight
    {
        float4 raw = tex2D(sourceTexture, safeUv);
        float invalid = (raw.r != raw.r || raw.g != raw.g || raw.b != raw.b || raw.a != raw.a || max(abs(raw.r), max(abs(raw.g), abs(raw.b))) > 1.0e19) ? 1.0 : 0.0;
        return float4(lerp(source.rgb, float3(1.0, 0.0, 1.0), invalid), 1.0);
    }
    if (variant == 10) // Out-of-Gamut Highlight
    {
        float outOfGamut = step(1.0, max(source.r, max(source.g, source.b))) + step(0.0, -min(source.r, min(source.g, source.b)));
        return float4(lerp(source.rgb, float3(1.0, 0.0, 0.0), saturate(outOfGamut)), 1.0);
    }
    if (variant == 11) // Luma Visualizer
        return float4(luma.xxx, 1.0);
    if (variant == 12) // RGB Parade
    {
        float3 parade = float3(step(1.0 - source.r, safeUv.y), step(1.0 - source.g, safeUv.y), step(1.0 - source.b, safeUv.y));
        return float4(parade, 1.0);
    }
    if (variant == 13) // Histogram
    {
        float binValue = smoothstep(0.0, 0.04, 1.0 - abs(luma - safeUv.x) * 10.0);
        float bar = step(safeUv.y, binValue);
        return float4(float3(0.3, 0.8, 0.4) * bar, 1.0);
    }
    if (variant == 14) // Waveform Monitor
    {
        float waveform = VJUtilityLuma(VJUtilitySource(sourceTexture, float2(safeUv.x, 0.5)).rgb);
        float trace = 1.0 - smoothstep(0.01, 0.04, abs(safeUv.y - waveform));
        return float4(float3(0.2, 0.9, 0.35) * trace, 1.0);
    }
    if (variant == 15) // Vectorscope Monitor
    {
        float2 chromaVector = float2(source.r - (source.g + source.b) * 0.5, (source.g - source.b) * 0.866);
        float trace = 1.0 - smoothstep(0.01, 0.06, length(coord - chromaVector));
        return float4(float3(trace, trace * 0.6, trace * 0.15), 1.0);
    }
    if (variant == 16) // False-color Exposure Monitor
    {
        float3 falseColor = VJHSVToRGB(float3(saturate(0.66 - luma * 0.66), 0.9, 0.85));
        return float4(falseColor, 1.0);
    }
    if (variant == 17) // Channel Viewer
    {
        int selected = clamp((int)floor(channel + 0.5), 0, 3);
        float value = selected == 0 ? source.r : selected == 1 ? source.g : selected == 2 ? source.b : source.a;
        return float4(value.xxx, 1.0);
    }
    if (variant == 18) // Difference Viewer
        return float4(abs(source.rgb - compare.rgb), 1.0);
    if (variant == 19) // Matte Viewer
        return float4(source.a.xxx, 1.0);
    if (variant == 20) // Pixel Inspector
    {
        float4 pixel = tex2D(sourceTexture, safeUv);
        return VJFinite4(pixel);
    }
    if (variant == 21) // Texture Info
    {
        float info = saturate(resolution.x / max(resolution.y, 1.0));
        return float4(info, 1.0 / max(info, 1.0), source.a, 1.0);
    }
    if (variant == 22) // Fit / Fill / Stretch
    {
        float mode = clamp(rangeMode, 0.0, 2.0);
        float2 fitUv = safeUv;
        if (mode < 0.5) fitUv = (safeUv - 0.5) * float2(1.0 / aspect, 1.0) + 0.5;
        else if (mode < 1.5) fitUv = (safeUv - 0.5) * float2(aspect, 1.0) + 0.5;
        return VJUtilitySource(sourceTexture, fitUv);
    }
    if (variant == 23) // Color Space Convert
        return VJPremultiply(float4(pow(max(sourceStraightRgb, 0.0), 1.0 / 2.2), source.a));
    if (variant == 24) // Linear / sRGB Convert
        return VJPremultiply(float4(sourceStraightRgb <= 0.04045 ? sourceStraightRgb / 12.92 : pow((sourceStraightRgb + 0.055) / 1.055, 2.4), source.a));
    if (variant == 25) // Rec.601 / 709 / 2020 Matrix
    {
        float3 rec2020 = float3(dot(sourceStraightRgb, float3(0.6274, 0.3293, 0.0433)), dot(sourceStraightRgb, float3(0.0691, 0.9195, 0.0114)), dot(sourceStraightRgb, float3(0.0164, 0.0880, 0.8956)));
        return VJPremultiply(float4(lerp(sourceStraightRgb, rec2020, saturate(rangeMode)), source.a));
    }
    if (variant == 26) // Limited / Full Range Convert
    {
        float3 converted = (sourceStraightRgb - 0.0625) / 0.875;
        return VJPremultiply(float4(lerp(sourceStraightRgb, converted, saturate(rangeMode)), source.a));
    }

    if (variant == 27) // SDR/HDR Display Transform Preview
        return VJPremultiply(float4(VJUtilityDisplayTransform(sourceStraightRgb, exposure), source.a));

    return VJFinite4(source);
}

#endif
