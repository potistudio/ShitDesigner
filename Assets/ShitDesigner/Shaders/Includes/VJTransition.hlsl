#ifndef SHITDESIGNER_VJ_TRANSITION_INCLUDED
#define SHITDESIGNER_VJ_TRANSITION_INCLUDED

#include "Assets/ShitDesigner/Shaders/Includes/VJCommon.hlsl"

float VJTransitionSmooth(float value, float threshold, float softness)
{
    float width = max(abs(softness), 1.0e-4);
    return smoothstep(threshold - width, threshold + width, value);
}

float VJTransitionHash(float2 cell, float seed)
{
    return VJHash12(cell + float2(seed * 0.173, seed * 0.371));
}

float2 VJTransitionDirection(float direction)
{
    int index = clamp((int)floor(direction + 0.5), 0, 3);
    if (index == 1) return float2(-1.0, 0.0);
    if (index == 2) return float2(0.0, 1.0);
    if (index == 3) return float2(0.0, -1.0);
    return float2(1.0, 0.0);
}

float VJTransitionMask(int variant, float2 uv, float progress, float softness, float direction, float seed)
{
    float2 coord = uv - 0.5;
    float x = saturate(uv.x);
    float y = saturate(uv.y);
    float angle = atan2(coord.y, coord.x) / VJ_TAU + 0.5;
    float radius = length(coord) * 1.41421356;
    float noise = VJTransitionHash(floor(uv * 32.0), seed);
    float pixelNoise = VJTransitionHash(floor(uv * 16.0), seed + 7.0);
    float threshold;

    switch (variant)
    {
        case 0: return progress;
        case 1: return progress; // Dip To Color is composed in the family pass.
        case 2: return VJTransitionSmooth(x, progress, softness);
        case 3: return VJTransitionSmooth(y, progress, softness);
        case 4: return VJTransitionSmooth(1.0 - angle, progress, softness);
        case 5: return VJTransitionSmooth(1.0 - radius, progress, softness);
        case 6: return VJTransitionSmooth(1.0 - max(abs(coord.x), abs(coord.y)) * 2.0, progress, softness);
        case 7: return VJTransitionSmooth(1.0 - angle, progress, softness);
        case 8: return VJTransitionSmooth(1.0 - x, progress, softness);
        case 9: return VJTransitionSmooth(noise, progress, softness);
        case 10: return VJTransitionSmooth(pixelNoise, progress, softness);
        case 11: return VJTransitionSmooth(0.5 + 0.5 * sin((x + y) * VJ_PI), progress, softness);
        case 12: return VJTransitionSmooth(x, progress, softness);
        case 13: return VJTransitionSmooth(y, progress, softness);
        case 14: return VJTransitionSmooth(abs(coord.x) * 2.0, progress, softness);
        case 15: return VJTransitionSmooth(max(abs(coord.x), abs(coord.y)) * 2.0, progress, softness);
        case 16: return VJTransitionSmooth(frac(y * 8.0) < 0.5 ? x : 1.0 - x, progress, softness);
        case 17: return VJTransitionSmooth(fmod(floor(x * 8.0) + floor(y * 8.0), 2.0) > 0.5 ? 1.0 - x : x, progress, softness);
        case 18: return VJTransitionSmooth(abs(sin(x * VJ_PI * 4.0)), progress, softness);
        case 19: return VJTransitionSmooth(1.0 - radius, progress, softness);
        case 20: return VJTransitionSmooth(progress + 0.2 * sin(angle * VJ_TAU), progress, softness);
        case 21: return VJTransitionSmooth(radius, progress, softness);
        case 22: return VJTransitionSmooth(progress, progress, softness);
        case 23: return VJTransitionSmooth(0.5 + 0.5 * sin(radius * 12.0), progress, softness);
        case 24: return VJTransitionSmooth(0.5 + 0.5 * sin((x + y) * 20.0), progress, softness);
        case 25: return VJTransitionSmooth(1.0 - radius, progress, softness);
        case 26: return VJTransitionSmooth(0.5 + 0.5 * sin(angle * 10.0 + radius * 8.0), progress, softness);
        case 27: return VJTransitionSmooth(1.0 - abs(sin(angle * VJ_PI * 4.0)), progress, softness);
        case 28: return VJTransitionSmooth(1.0 - radius + 0.1 * sin(radius * 30.0), progress, softness);
        case 29: return VJTransitionSmooth(noise, progress, softness);
        case 30: return VJTransitionSmooth(abs(sin((x * 17.0 + y * 11.0 + seed) * VJ_PI)), progress, softness);
        case 31: return VJTransitionSmooth(abs(sin((x + y) * VJ_PI * 8.0)), progress, softness);
        case 32: return VJTransitionSmooth(pow(saturate(x), 0.75), progress, softness);
        case 33: return VJTransitionSmooth(1.0 - radius, progress, softness);
        case 34: return VJTransitionSmooth(0.5 + 0.5 * sin((x * 13.0 + y * 7.0) * VJ_PI), progress, softness);
        case 35: return VJTransitionSmooth(noise, progress, softness);
        default: return progress;
    }
}

float2 VJTransitionWarp(int variant, float2 uv, float progress, float direction, float aspect, sampler2D displacementTexture)
{
    float2 result = saturate(uv);
    float2 dir = VJTransitionDirection(direction);
    float2 centered = (result - 0.5) * float2(max(aspect, 1.0e-4), 1.0);
    float radius = length(centered);
    if (variant == 12 || variant == 13)
        result -= dir * progress;
    else if (variant == 14 || variant == 15)
        result -= dir * (progress * (centered.x >= 0.0 ? 1.0 : -1.0));
    else if (variant == 19 || variant == 20 || variant == 21)
        result = 0.5 + centered * (1.0 + progress * (0.75 + radius));
    else if (variant == 22 || variant == 23)
        result = 0.5 + centered * (1.0 + progress * 1.5);
    else if (variant == 26)
        result = 0.5 + VJRotate(centered, progress * VJ_TAU * 0.25);
    else if (variant == 27)
    {
        float2 k = result * 6.0;
        k = abs(frac(k) * 2.0 - 1.0);
        result = k;
    }
    else if (variant == 28)
        result += VJSafeNormalize2(centered + 1.0e-4, float2(1.0, 0.0)) * sin(radius * 28.0 - progress * VJ_TAU) * 0.025 * progress;
    else if (variant == 29 || variant == 33 || variant == 34)
    {
        float4 displacement = VJSample2D(displacementTexture, result);
        result += (displacement.xy * 2.0 - 1.0) * 0.08 * progress;
    }
    else if (variant == 30)
    {
        float glitch = step(0.8, VJTransitionHash(float2(floor(result.y * 24.0), floor(_SD_Frame)), _SD_Seed));
        result.x += (glitch * 2.0 - 1.0) * 0.05 * progress;
    }
    return saturate(VJFinite2(result));
}

float4 VJTransitionBlurSample(sampler2D textureSampler, float2 uv, float2 axis, float radius)
{
    float4 value = VJSample2D(textureSampler, uv) * 0.4;
    value += VJSample2D(textureSampler, uv + axis * radius) * 0.25;
    value += VJSample2D(textureSampler, uv - axis * radius) * 0.25;
    value += VJSample2D(textureSampler, uv + axis * radius * 2.0) * 0.05;
    value += VJSample2D(textureSampler, uv - axis * radius * 2.0) * 0.05;
    return VJFinite4(value);
}

#endif
