#ifndef SHITDESIGNER_VJ_TEMPORAL_INCLUDED
#define SHITDESIGNER_VJ_TEMPORAL_INCLUDED

#include "Assets/ShitDesigner/Shaders/Includes/VJCommon.hlsl"

float2 VJTemporalTexel(float4 resolution)
{
    return 1.0 / max(resolution.xy, float2(1.0, 1.0));
}

float4 VJTemporalMix(float4 current, float4 history, float feedback)
{
    return VJFinite4(lerp(current, history, saturate(feedback)));
}

float4 VJTemporalTap(sampler2D textureSampler, float2 uv, float2 texel, float amount)
{
    float4 value = VJSample2D(textureSampler, uv) * 0.5;
    value += VJSample2D(textureSampler, uv + texel * amount) * 0.25;
    value += VJSample2D(textureSampler, uv - texel * amount) * 0.25;
    return VJFinite4(value);
}

float VJTemporalMedianScalar(float a, float b, float c)
{
    return a + b + c - min(a, min(b, c)) - max(a, max(b, c));
}

float4 VJTemporalMedian(float4 a, float4 b, float4 c)
{
    a = VJUnpremultiply(a);
    b = VJUnpremultiply(b);
    c = VJUnpremultiply(c);
    float alpha = VJTemporalMedianScalar(a.a, b.a, c.a);
    return VJPremultiply(float4(VJTemporalMedianScalar(a.r, b.r, c.r), VJTemporalMedianScalar(a.g, b.g, c.g),
        VJTemporalMedianScalar(a.b, b.b, c.b), alpha));
}

float4 VJTemporalEvaluate(int variant, sampler2D currentTexture, sampler2D historyTexture,
    sampler2D historyTexture2, sampler2D historyTexture3, sampler2D displacementTexture,
    float2 uv, float amount, float feedback, float progress, float frame, float graphTime,
    float paused, float beat, float4 resolution, float seed)
{
    float2 texel = VJTemporalTexel(resolution);
    float activeTime = paused > 0.5 ? 0.0 : VJFiniteScalar(graphTime);
    float activeFrame = paused > 0.5 ? 0.0 : VJFiniteScalar(frame);
    float safeAmount = saturate(VJFiniteScalar(amount));
    float safeFeedback = saturate(VJFiniteScalar(feedback));
    float safeProgress = saturate(VJFiniteScalar(progress));
    float4 current = VJSample2D(currentTexture, uv);
    float4 history = VJSample2D(historyTexture, uv);
    float4 history2 = VJSample2D(historyTexture2, uv);
    float4 history3 = VJSample2D(historyTexture3, uv);
    if (variant < 0 || variant > 31) variant = 0;

    if (variant == 0) // Feedback Transform
    {
        float2 centered = uv - 0.5;
        float angle = (0.002 + safeAmount * 0.02) * activeTime;
        float2 warped = VJRotate(centered, angle) * (1.0 - safeAmount * 0.01) + 0.5;
        return VJTemporalMix(current, VJSample2D(historyTexture, warped), safeFeedback);
    }
    if (variant == 1) // Feedback Zoom
    {
        float2 warped = (uv - 0.5) * (1.0 - safeAmount * 0.25) + 0.5;
        return VJTemporalMix(current, VJSample2D(historyTexture, warped), safeFeedback);
    }
    if (variant == 2) // Feedback Rotate
    {
        float2 warped = VJRotate(uv - 0.5, safeAmount * 0.25 + activeTime * 0.002) + 0.5;
        return VJTemporalMix(current, VJSample2D(historyTexture, warped), safeFeedback);
    }
    if (variant == 3) // Feedback Kaleidoscope
    {
        float2 coord = abs((uv - 0.5) * 2.0);
        coord = abs(frac(coord * 3.0) * 2.0 - 1.0) * 0.5 + 0.5;
        return VJTemporalMix(current, VJSample2D(historyTexture, coord), safeFeedback);
    }
    if (variant == 4) // Echo
        return VJTemporalMix(current, (history + history2) * 0.5, safeFeedback * 0.8);
    if (variant == 5) // Trails
        return VJFinite4(current * (1.0 - safeFeedback) + history * safeFeedback);
    if (variant == 6) // Frame Delay
        return VJTemporalMix(current, history2, safeFeedback);
    if (variant == 7) // Strobe
    {
        float pulse = step(0.5, 0.5 + 0.5 * sin(activeTime * 20.0 + activeFrame * 0.1));
        return lerp(history, current, pulse);
    }
    if (variant == 8) // Freeze/Hold
        return history;
    if (variant == 9) // Accumulate Add
        return VJFinite4(current + history * safeFeedback * max(safeAmount, 0.01));
    if (variant == 10) // Accumulate Max
        return max(current, history * safeFeedback);
    if (variant == 11) // Temporal Average
        return VJFinite4((current + history + history2 + history3) * 0.25);
    if (variant == 12) // Multi-tap Echo
        return VJFinite4(current * 0.4 + history * 0.3 + history2 * 0.2 + history3 * 0.1);
    if (variant == 13) // Slit Scan Horizontal
        return lerp(current, history, step(uv.x, safeProgress));
    if (variant == 14) // Slit Scan Vertical
        return lerp(current, history, step(uv.y, safeProgress));
    if (variant == 15) // Time Displacement Map
    {
        float2 displacement = VJSample2D(displacementTexture, uv).rg * 2.0 - 1.0;
        return VJTemporalMix(current, VJSample2D(historyTexture, uv + displacement * texel * 32.0 * safeAmount), safeFeedback);
    }
    if (variant == 16) // Temporal RGB Split
    {
        float4 straightCurrent = VJUnpremultiply(current);
        float4 straightLeft = VJUnpremultiply(VJSample2D(historyTexture, uv - float2(texel.x * (1.0 + safeAmount * 12.0), 0.0)));
        float4 straightRight = VJUnpremultiply(VJSample2D(historyTexture, uv + float2(texel.x * (1.0 + safeAmount * 12.0), 0.0)));
        return VJPremultiply(float4(lerp(straightCurrent.r, straightLeft.r, safeFeedback),
            lerp(straightCurrent.g, VJUnpremultiply(history).g, safeFeedback),
            lerp(straightCurrent.b, straightRight.b, safeFeedback), straightCurrent.a));
    }
    if (variant == 17) // Datamosh Feedback
    {
        float2 block = floor(uv * 24.0);
        float2 offset = (float2(VJHash12(block + seed), VJHash12(block + seed + 3.0)) - 0.5) * texel * 20.0 * safeAmount;
        return VJTemporalMix(current, VJSample2D(historyTexture, uv + offset), safeFeedback);
    }
    if (variant == 18) // Motion Trails
    {
        float4 straightCurrent = VJUnpremultiply(current);
        float4 straightHistory = VJUnpremultiply(history);
        float4 delta = abs(straightCurrent - straightHistory);
        return VJPremultiply(lerp(straightHistory, straightCurrent + delta * safeAmount, 1.0 - safeFeedback));
    }
    if (variant == 19) // Persistence / Phosphor
    {
        float4 straightCurrent = VJUnpremultiply(current);
        float4 straightHistory = VJUnpremultiply(history);
        float3 decay = exp(-float3(1.0, 1.7, 2.3) * max(safeAmount, 0.01));
        return VJPremultiply(float4(max(straightCurrent.rgb, straightHistory.rgb * decay), max(straightCurrent.a, straightHistory.a)));
    }
    if (variant == 20) // Long Exposure
        return VJFinite4(history * safeFeedback + current * max(safeAmount, 0.01));
    if (variant == 21) // Frame Difference
    {
        float4 difference = abs(VJUnpremultiply(current) - VJUnpremultiply(history));
        return VJPremultiply(float4(difference.rgb, 1.0));
    }
    if (variant == 22) // Background Subtract
    {
        float3 difference = abs(VJUnpremultiply(current).rgb - VJUnpremultiply(history).rgb) * (1.0 + safeAmount);
        return VJPremultiply(float4(difference, 1.0));
    }
    if (variant == 23) // Temporal Median
        return VJTemporalMedian(current, history, history2);
    if (variant == 24) // Temporal Posterize
    {
        float levels = max(2.0, floor(2.0 + safeAmount * 14.0));
        float4 straightCurrent = VJUnpremultiply(current);
        return VJPremultiply(float4(floor(straightCurrent.rgb * levels) / levels, straightCurrent.a));
    }
    if (variant == 25) // Beat Repeat
    {
        float beatPhase = frac(VJFiniteScalar(beat));
        float held = step(beatPhase, safeProgress);
        return lerp(current, history, held * safeFeedback);
    }
    if (variant == 26) // Optical Flow Visualizer
    {
        float4 straightCurrent = VJUnpremultiply(current);
        float4 straightHistory = VJUnpremultiply(history);
        float2 flow = (straightCurrent.rg - straightHistory.rg) * 2.0;
        return VJPremultiply(float4(flow * 0.5 + 0.5, abs(straightCurrent.b - straightHistory.b), 1.0));
    }
    if (variant == 27) // Optical Flow Warp
    {
        float2 flow = (history.rg - history2.rg) * texel * (1.0 + safeAmount * 12.0);
        return VJTemporalMix(current, VJSample2D(historyTexture, uv + flow), safeFeedback);
    }
    if (variant == 28) // Frame Interpolation
        return lerp(history, current, safeProgress);
    if (variant == 29) // Fluid Feedback
    {
        float2 flow = (VJSample2D(displacementTexture, uv).rg * 2.0 - 1.0) * texel * 16.0 * safeAmount;
        return VJTemporalMix(current, VJTemporalTap(historyTexture, uv + flow, texel, 1.0), safeFeedback);
    }
    if (variant == 30) // Reaction Diffusion Feedback
    {
        float4 laplacian = VJTemporalTap(historyTexture, uv, texel, 1.0) - history;
        return VJFinite4(lerp(current, history + laplacian * safeAmount, safeFeedback));
    }

    if (variant == 31) // Multi-buffer Cellular Simulation
    {
        // The cell state is seeded from the previous buffers and a paused
        // graph deliberately freezes the graph-clock terms.
        float cell = VJValueNoise(uv * (8.0 + safeAmount * 16.0) + activeFrame * 0.01 + seed);
        float previousCell = VJLuma(VJUnpremultiply(history).rgb);
        float neighbor = VJValueNoise((uv + texel * 2.0) * 12.0 + activeTime * 0.1 + seed + previousCell);
        float state = saturate(lerp(previousCell, cell * 0.65 + neighbor * 0.35, safeAmount));
        float4 straightCurrent = VJUnpremultiply(current);
        return VJPremultiply(lerp(straightCurrent, float4(state, state * 0.7 + 0.1, 1.0 - state, straightCurrent.a), safeFeedback));
    }

    return VJFinite4(current);
}

#endif
