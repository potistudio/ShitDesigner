#ifndef SHITDESIGNER_VJ_SDF_INCLUDED
#define SHITDESIGNER_VJ_SDF_INCLUDED

#include "Assets/ShitDesigner/Shaders/Includes/VJCommon.hlsl"

float VJSdfSphere(float3 p, float radius) { return length(p) - radius; }

float VJSdfBox(float3 p, float3 bounds)
{
    float3 q = abs(p) - bounds;
    return length(max(q, 0.0)) + min(max(q.x, max(q.y, q.z)), 0.0);
}

float VJSdfRoundBox(float3 p, float3 bounds, float radius) { return VJSdfBox(p, max(bounds - radius, 0.0)) - radius; }

float VJSdfTorus(float3 p, float majorRadius, float minorRadius)
{
    return length(float2(length(p.xz) - majorRadius, p.y)) - minorRadius;
}

float VJSdfCapsule(float3 p, float3 a, float3 b, float radius)
{
    float3 ba = b - a;
    float h = saturate(dot(p - a, ba) / max(dot(ba, ba), 1.0e-5));
    return length(p - lerp(a, b, h)) - radius;
}

float VJSdfSmoothUnion(float a, float b, float k)
{
    float h = saturate(0.5 + 0.5 * (b - a) / max(abs(k), 1.0e-5));
    return lerp(b, a, h) - k * h * (1.0 - h);
}

float VJSdfRepeat1(float value, float cell)
{
    return cell == 0.0 ? value : (value - cell * floor(value / cell + 0.5));
}

float3 VJSdfRepeat(float3 p, float3 cell)
{
    return float3(VJSdfRepeat1(p.x, cell.x), VJSdfRepeat1(p.y, cell.y), VJSdfRepeat1(p.z, cell.z));
}

float VJSdfGyroid(float3 p, float scale)
{
    p *= max(abs(scale), 1.0e-4);
    return (dot(sin(p), cos(p.yzx)) / max(abs(scale), 1.0e-4)) * 0.25;
}

float VJSdfMenger(float3 p)
{
    float scale = 1.0;
    float distance = VJSdfBox(p, 0.8);
    for (int iteration = 0; iteration < 4; iteration++)
    {
        p = abs(frac(p * 0.5 + 0.5) * 2.0 - 1.0);
        float cross = max(p.x, max(p.y, p.z));
        distance = max(distance, -min(p.x, min(p.y, p.z)) + 0.16 / scale);
        distance = min(distance, cross - 0.28 / scale);
        scale *= 3.0;
    }
    return distance / max(scale, 1.0);
}

float VJSdfNoise(float3 p)
{
    return VJHash13(p);
}

#endif
