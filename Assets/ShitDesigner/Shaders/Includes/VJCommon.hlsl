#ifndef SHITDESIGNER_VJ_COMMON_INCLUDED
#define SHITDESIGNER_VJ_COMMON_INCLUDED

// Shared, deterministic uniforms for the VJ shader families.  The runtime
// may leave these values at their defaults while a material is being probed;
// every expression below remains finite in that state.
#define VJ_PI 3.14159265358979323846
#define VJ_TAU 6.28318530717958647692

float _VJVariant;
float _VJAmount;
float _VJFrequency;
float _VJDetail;
float _VJSoftness;
float _VJThreshold;
float _VJGain;
float _VJMix;
float _VJSpeed;
float _VJPhase;
float _VJDirection;
float _VJAspect;
float _VJSeed;
float _VJScale;
float _VJRadius;
float _VJFalloff;
float _VJExposure;
float _VJGamma;
float _VJHue;
float _VJSaturation;
float _VJContrast;
float _VJTemperature;
float _VJTile;
float _VJAngle;
float _VJCenterX;
float _VJCenterY;
float4 _VJCenter;
float4 _VJColorA;
float4 _VJColorB;
float4 _VJColorC;
float4 _VJPivot;
float4 _VJDisplacement;

float _SD_Time;
float _SD_DeltaTime;
float _SD_Frame;
float4 _SD_Resolution;
float _SD_Seed;
float _SD_BeatPhase;
float _SD_BeatPulse;
float _SD_BarPhase;
float4 _SD_Pointer;

float VJFiniteScalar(float value)
{
    return (value == value && abs(value) < 1.0e20) ? value : 0.0;
}

float2 VJFinite2(float2 value)
{
    return float2(VJFiniteScalar(value.x), VJFiniteScalar(value.y));
}

float3 VJFinite3(float3 value)
{
    return float3(VJFiniteScalar(value.x), VJFiniteScalar(value.y), VJFiniteScalar(value.z));
}

float4 VJFinite4(float4 value)
{
    return float4(VJFiniteScalar(value.x), VJFiniteScalar(value.y), VJFiniteScalar(value.z), VJFiniteScalar(value.w));
}

float2 VJSafeNormalize2(float2 value, float2 fallback)
{
    float2 safeValue = VJFinite2(value);
    float lengthValue = length(safeValue);
    return lengthValue > 1.0e-5 && lengthValue == lengthValue ? safeValue / lengthValue : fallback;
}

float3 VJSafeNormalize3(float3 value, float3 fallback)
{
    float3 safeValue = VJFinite3(value);
    float lengthValue = length(safeValue);
    return lengthValue > 1.0e-5 && lengthValue == lengthValue ? safeValue / lengthValue : fallback;
}

float VJSafeDiv(float numerator, float denominator)
{
    return numerator / (abs(denominator) < 1.0e-5 ? (denominator < 0.0 ? -1.0e-5 : 1.0e-5) : denominator);
}

float2 VJSafeUV(float2 uv)
{
    return saturate(VJFinite2(uv));
}

float2 VJAspectUV(float2 uv)
{
    float width = max(abs(_SD_Resolution.x), 1.0);
    float height = max(abs(_SD_Resolution.y), 1.0);
    float aspect = _VJAspect > 0.0 ? _VJAspect : width / height;
    float2 centered = uv - 0.5;
    centered.x *= aspect;
    return centered + 0.5;
}

float2 VJRotate(float2 coord, float angle)
{
    float sine = sin(angle);
    float cosine = cos(angle);
    return float2(cosine * coord.x - sine * coord.y, sine * coord.x + cosine * coord.y);
}

float VJHash11(float value)
{
    value = frac(value * 0.1031);
    value *= value + 33.33;
    value *= value + value;
    return frac(value);
}

float VJHash12(float2 value)
{
    float3 p = frac(float3(value.x, value.y, value.x) * 0.1031);
    p += dot(p, p.yzx + 33.33);
    return frac((p.x + p.y) * p.z);
}

float VJHash13(float3 value)
{
    value = frac(value * 0.1031);
    value += dot(value, value.yzx + 33.33);
    return frac((value.x + value.y) * value.z);
}

float VJValueNoise(float2 coord)
{
    float2 cell = floor(coord);
    float2 local = frac(coord);
    local = local * local * (3.0 - 2.0 * local);
    float a = VJHash12(cell);
    float b = VJHash12(cell + float2(1.0, 0.0));
    float c = VJHash12(cell + float2(0.0, 1.0));
    float d = VJHash12(cell + float2(1.0, 1.0));
    return lerp(lerp(a, b, local.x), lerp(c, d, local.x), local.y);
}

float VJGradientNoise(float2 coord)
{
    float2 cell = floor(coord);
    float2 local = frac(coord) * 2.0 - 1.0;
    float2 g0 = VJSafeNormalize2(float2(VJHash12(cell), VJHash12(cell + 17.0)) * 2.0 - 1.0, float2(1.0, 0.0));
    float2 g1 = VJSafeNormalize2(float2(VJHash12(cell + float2(1.0, 0.0)), VJHash12(cell + float2(18.0, 0.0))) * 2.0 - 1.0, float2(1.0, 0.0));
    float2 g2 = VJSafeNormalize2(float2(VJHash12(cell + float2(0.0, 1.0)), VJHash12(cell + float2(17.0, 1.0))) * 2.0 - 1.0, float2(1.0, 0.0));
    float2 g3 = VJSafeNormalize2(float2(VJHash12(cell + float2(1.0, 1.0)), VJHash12(cell + float2(18.0, 1.0))) * 2.0 - 1.0, float2(1.0, 0.0));
    float2 f = frac(coord);
    f = f * f * (3.0 - 2.0 * f);
    float n0 = dot(g0, local);
    float n1 = dot(g1, local - float2(2.0, 0.0));
    float n2 = dot(g2, local - float2(0.0, 2.0));
    float n3 = dot(g3, local - 2.0);
    return 0.5 + 0.5 * lerp(lerp(n0, n1, f.x), lerp(n2, n3, f.x), f.y);
}

float VJFBM(float2 coord, int octaves)
{
    float value = 0.0;
    float amplitude = 0.5;
    float frequency = 1.0;
    int count = clamp(octaves, 1, 8);
    for (int index = 0; index < 8; index++)
    {
        if (index >= count) break;
        value += VJValueNoise(coord * frequency) * amplitude;
        frequency *= 2.0;
        amplitude *= 0.5;
    }
    return value / max(1.0 - pow(0.5, count), 1.0e-4);
}

float VJLuma(float3 color)
{
    return dot(color, float3(0.2126, 0.7152, 0.0722));
}

float3 VJRGBToHSV(float3 color)
{
    float4 k = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 p = color.g < color.b ? float4(color.bg, k.wz) : float4(color.gb, k.xy);
    float4 q = color.r < p.x ? float4(p.xyw, color.r) : float4(color.r, p.yzx);
    float difference = q.x - min(q.w, q.y);
    float epsilon = 1.0e-6;
    return float3(abs(q.z + (q.w - q.y) / (6.0 * difference + epsilon)), difference / (q.x + epsilon), q.x);
}

float3 VJHSVToRGB(float3 hsv)
{
    float3 p = abs(frac(hsv.xxx + float3(0.0, 2.0 / 3.0, 1.0 / 3.0)) * 6.0 - 3.0);
    return hsv.z * lerp(float3(1.0, 1.0, 1.0), saturate(p - 1.0), hsv.y);
}

float4 VJUnpremultiply(float4 premultiplied)
{
    float alpha = max(premultiplied.a, 1.0e-5);
    float3 rgb = premultiplied.a > 1.0e-5 ? premultiplied.rgb / alpha : 0.0;
    return float4(VJFinite3(rgb), saturate(VJFiniteScalar(premultiplied.a)));
}

float4 VJPremultiply(float4 straight)
{
    float alpha = saturate(VJFiniteScalar(straight.a));
    return VJFinite4(float4(VJFinite3(straight.rgb) * alpha, alpha));
}

float4 VJSample2D(sampler2D textureSampler, float2 uv)
{
    return VJFinite4(tex2D(textureSampler, saturate(uv)));
}

float2 VJPolarUV(float2 uv)
{
    float2 coord = uv * 2.0 - 1.0;
    return float2(atan2(coord.y, coord.x) / VJ_TAU + 0.5, length(coord));
}

float VJSoftMask(float value, float threshold, float softness)
{
    float width = max(abs(softness), 1.0e-4);
    return smoothstep(threshold - width, threshold + width, value);
}

#endif
