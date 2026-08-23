#ifndef SHITDESIGNER_VJ_RAYMARCH_INCLUDED
#define SHITDESIGNER_VJ_RAYMARCH_INCLUDED

#include "Assets/ShitDesigner/Shaders/Includes/VJCommon.hlsl"
#include "Assets/ShitDesigner/Shaders/Includes/VJSdf.hlsl"

float VJRaymarchHash(float2 value) { return VJHash12(value); }

float VJRaymarchColumns(float3 p)
{
    float2 cell = float2(VJSdfRepeat1(p.x, 1.5), VJSdfRepeat1(p.z, 1.5));
    return max(length(cell) - 0.48, abs(p.y) - 1.0);
}

float VJRaymarchCity(float3 p)
{
    float3 cell = VJSdfRepeat(p, float3(1.4, 2.0, 1.4));
    float height = 0.25 + 0.8 * VJRaymarchHash(floor(p.xz * 1.3));
    return VJSdfBox(cell - float3(0.0, height - 1.0, 0.0), float3(0.38, height, 0.38));
}

float VJRaymarchFractalTunnel(float3 p)
{
    float3 q = p;
    float distance = 10.0;
    for (int iteration = 0; iteration < 5; iteration++)
    {
        q = abs(q) - 0.35;
        float2 rotated = VJRotate(q.xy, 0.3);
        q = float3(rotated.x, rotated.y, q.z);
        distance = min(distance, VJSdfBox(q, float3(0.35, 0.35, 1.5)) / (iteration + 1.0));
    }
    return distance;
}

float VJRaymarchMandelbulb(float3 p)
{
    float3 z = p;
    float derivative = 1.0;
    float radius = length(z);
    for (int iteration = 0; iteration < 8; iteration++)
    {
        if (radius >= 2.0) break;
        float theta = acos(clamp(z.z / max(radius, 1.0e-4), -1.0, 1.0));
        float phi = atan2(z.y, z.x);
        float power = 8.0;
        derivative = pow(max(radius, 1.0e-4), power - 1.0) * power * derivative + 1.0;
        float zr = pow(max(radius, 1.0e-4), power);
        z = zr * float3(sin(theta * power) * cos(phi * power), sin(phi * power) * sin(theta * power), cos(theta * power)) + p;
        radius = length(z);
    }
    return 0.5 * log(max(radius, 1.0e-4)) * radius / max(abs(derivative), 1.0e-4);
}

float VJRaymarchMandelbox(float3 p)
{
    float3 z = p;
    float derivative = 1.0;
    for (int iteration = 0; iteration < 8; iteration++)
    {
        z = clamp(z, -1.0, 1.0) * 2.0 - z;
        float radius = length(z);
        if (radius < 0.35) { z *= 1.0 / 0.35; derivative *= 1.0 / 0.35; }
        else if (radius < 1.0) { z *= 1.0 / max(radius, 1.0e-4); derivative *= 1.0 / max(radius, 1.0e-4); }
        z = z * 2.2 + p;
        derivative = abs(derivative * 2.2) + 1.0;
    }
    return length(z) / max(abs(derivative), 1.0e-4);
}

float VJRaymarchMetaballs(float3 p)
{
    float field = 0.0;
    for (int index = 0; index < 4; index++)
    {
        float3 center = float3(sin(index * 2.1) * 0.5, cos(index * 1.7) * 0.4, sin(index * 1.3) * 0.45);
        field += 0.08 / max(dot(p - center, p - center), 1.0e-4);
    }
    return 0.55 - field;
}

float VJRaymarchClouds(float3 p)
{
    float density = 0.0;
    float3 q = p * 1.3;
    for (int octave = 0; octave < 4; octave++)
    {
        density += VJValueNoise(q) * 0.5;
        q = q * 2.02 + 0.17;
    }
    return 0.45 - density * 0.42;
}

float VJRaymarchSceneDistance(int variant, float3 p, float audio, float time)
{
    p = VJFinite3(p);
    float safeAudio = clamp(VJFiniteScalar(audio), -4.0, 4.0);
    float safeTime = VJFiniteScalar(time);
    switch (variant)
    {
        case 0: return VJSdfSphere(p, 0.75);
        case 1: return VJSdfBox(p, 0.7);
        case 2: return VJSdfTorus(p, 0.72, 0.18);
        case 3: return VJSdfCapsule(p, float3(0.0, -0.5, 0.0), float3(0.0, 0.5, 0.0), 0.28);
        case 4: return VJSdfRoundBox(p, 0.58, 0.15);
        case 5: return VJSdfSmoothUnion(VJSdfSphere(p - float3(-0.35, 0.0, 0.0), 0.55), VJSdfTorus(p - float3(0.35, 0.0, 0.0), 0.45, 0.16), 0.18);
        case 6: return VJSdfSphere(VJSdfRepeat(p, 1.5), 0.42);
        case 7: return VJRaymarchColumns(p);
        case 8: return VJRaymarchCity(p);
        case 9: return VJSdfTorus(float3(p.x, p.y, VJSdfRepeat1(p.z, 3.0)), 1.0, 0.07);
        case 10: return VJRaymarchFractalTunnel(p);
        case 11: return VJSdfMenger(p);
        case 12: return VJRaymarchMandelbulb(p);
        case 13: return VJRaymarchMandelbox(p);
        case 14:
        {
            float3 q = p;
            for (int iteration = 0; iteration < 4; iteration++)
            {
                q.x = -abs(q.x);
                q = q.yzx * 1.35 - 0.42;
            }
            return VJSdfSphere(q, 0.3);
        }
        case 15: return VJSdfGyroid(p, 3.0);
        case 16: return VJRaymarchMetaballs(p);
        case 17:
        {
            float3 cell = VJSdfRepeat(p, 1.0);
            float height = 0.2 + VJRaymarchHash(floor(p.xz * 1.7)) * 0.8;
            return max(max(abs(cell.x) - 0.45, abs(cell.z) - 0.45), p.y - height);
        }
        case 18: return p.y - (sin(p.x * 1.5) * 0.2 + cos(p.z * 1.3) * 0.2);
        case 19: return p.y + 0.35 + sin(p.x * 2.3 + p.z) * 0.12 + sin(p.z * 3.1) * 0.08;
        case 20: return VJRaymarchClouds(p);
        case 21: return 0.3 - VJValueNoise(p * 2.4) * 0.45 - VJValueNoise(p * 6.3) * 0.12;
        case 22: return abs(p.z) + 0.12 - max(0.2, length(p.xy) * 0.2);
        case 23: return abs(length(p.xy) - 0.45) - 0.08;
        case 24: return max(VJSdfBox(p, float3(0.42, 0.9, 0.42)), abs(p.y) - 0.9) + sin(p.y * 8.0) * 0.05;
        case 25: return abs(VJSdfBox(p, 0.65)) - 0.03;
        case 26: return min(abs(abs(p.x) - 0.6), min(abs(abs(p.y) - 0.6), abs(abs(p.z) - 0.6))) - 0.035;
        case 27:
        {
            float3 cell = VJSdfRepeat(p, 1.5);
            return max(abs(length(cell.xz) - 0.5) - 0.07, abs(cell.y) - 0.5);
        }
        case 28: return VJSdfSphere(p, 0.55 + safeAudio * 0.08) + sin(p.y * 12.0 + safeTime) * 0.04;
        case 29: // Signed-distance Text Extrusion
        {
            float stem = max(abs(p.x) - 0.08, abs(p.y) - 0.55);
            float crossbar = max(abs(p.y) - 0.08, abs(p.x) - 0.4);
            float shoulder = max(abs(p.y - 0.42) - 0.08, abs(p.x) - 0.4);
            float glyph = min(stem, min(crossbar, shoulder));
            return max(glyph, abs(p.z) - 0.25);
        }
        default: return VJSdfSphere(p, 0.45);
    }
}

float3 VJRaymarchNormal(int variant, float3 coord, float epsilon, float audio, float time)
{
    float e = max(abs(epsilon), 1.0e-4);
    float dx = VJRaymarchSceneDistance(variant, coord + float3(e, 0.0, 0.0), audio, time) - VJRaymarchSceneDistance(variant, coord - float3(e, 0.0, 0.0), audio, time);
    float dy = VJRaymarchSceneDistance(variant, coord + float3(0.0, e, 0.0), audio, time) - VJRaymarchSceneDistance(variant, coord - float3(0.0, e, 0.0), audio, time);
    float dz = VJRaymarchSceneDistance(variant, coord + float3(0.0, 0.0, e), audio, time) - VJRaymarchSceneDistance(variant, coord - float3(0.0, 0.0, e), audio, time);
    float3 normal = VJSafeNormalize3(float3(dx, dy, dz), float3(0.0, 1.0, 0.0));
    return VJFinite3(normal);
}

struct VJRaymarchHit
{
    float hit;
    float distance;
    float3 normal;
    float steps;
};

VJRaymarchHit VJRaymarchTrace(int variant, float3 origin, float3 direction, int maxSteps, float epsilon, float farDistance, float audio, float time)
{
    VJRaymarchHit result;
    result.hit = 0.0;
    result.distance = 0.0;
    result.normal = float3(0.0, 1.0, 0.0);
    result.steps = 0.0;
    int cap = clamp(maxSteps, 1, 256);
    float safeEpsilon = clamp(abs(epsilon), 1.0e-5, 0.1);
    float safeFar = clamp(abs(farDistance), 0.1, 1000.0);
    float3 ray = VJSafeNormalize3(VJFinite3(direction), float3(0.0, 0.0, -1.0));
    float travelled = 0.0;
    for (int stepIndex = 0; stepIndex < 256; stepIndex++)
    {
        if (stepIndex >= cap) break;
        float3 coord = origin + ray * travelled;
        float distanceValue = VJFiniteScalar(VJRaymarchSceneDistance(variant, coord, audio, time));
        result.steps = stepIndex + 1.0;
        if (distanceValue <= safeEpsilon)
        {
            result.hit = 1.0;
            result.distance = travelled;
            result.normal = VJRaymarchNormal(variant, coord, safeEpsilon, audio, time);
            return result;
        }
        travelled += max(distanceValue, safeEpsilon * 0.5);
        if (travelled > safeFar) break;
    }
    result.distance = min(travelled, safeFar);
    return result;
}

#endif
