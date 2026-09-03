#ifndef SHITDESIGNER_VJ_BLEND_INCLUDED
#define SHITDESIGNER_VJ_BLEND_INCLUDED

#include "Assets/ShitDesigner/Shaders/Includes/VJCommon.hlsl"

float3 VJBlendOverlay(float3 a, float3 b)
{
    return float3(a.r < 0.5 ? 2.0 * a.r * b.r : 1.0 - 2.0 * (1.0 - a.r) * (1.0 - b.r),
        a.g < 0.5 ? 2.0 * a.g * b.g : 1.0 - 2.0 * (1.0 - a.g) * (1.0 - b.g),
        a.b < 0.5 ? 2.0 * a.b * b.b : 1.0 - 2.0 * (1.0 - a.b) * (1.0 - b.b));
}

float VJBlendSoftLightScalar(float a, float b)
{
    float d = b <= 0.25 ? ((16.0 * b - 12.0) * b + 4.0) * b : sqrt(max(b, 0.0));
    return a <= 0.5 ? b - (1.0 - 2.0 * a) * b * (1.0 - b) : b + (2.0 * a - 1.0) * (d - b);
}

float3 VJBlendSoftLight(float3 a, float3 b)
{
    return float3(VJBlendSoftLightScalar(a.r, b.r), VJBlendSoftLightScalar(a.g, b.g), VJBlendSoftLightScalar(a.b, b.b));
}

float VJBlendColorDodgeScalar(float a, float b)
{
    return a / max(1.0 - b, 1.0e-5);
}

float3 VJBlendColorDodge(float3 a, float3 b)
{
    return float3(VJBlendColorDodgeScalar(a.r, b.r), VJBlendColorDodgeScalar(a.g, b.g), VJBlendColorDodgeScalar(a.b, b.b));
}

float VJBlendColorBurnScalar(float a, float b)
{
    return 1.0 - (1.0 - a) / max(b, 1.0e-5);
}

float3 VJBlendColorBurn(float3 a, float3 b)
{
    return float3(VJBlendColorBurnScalar(a.r, b.r), VJBlendColorBurnScalar(a.g, b.g), VJBlendColorBurnScalar(a.b, b.b));
}

float3 VJBlendVividLight(float3 a, float3 b)
{
    return float3(a.r < 0.5 ? VJBlendColorBurnScalar(a.r * 2.0, b.r) : VJBlendColorDodgeScalar(a.r * 2.0 - 1.0, b.r),
        a.g < 0.5 ? VJBlendColorBurnScalar(a.g * 2.0, b.g) : VJBlendColorDodgeScalar(a.g * 2.0 - 1.0, b.g),
        a.b < 0.5 ? VJBlendColorBurnScalar(a.b * 2.0, b.b) : VJBlendColorDodgeScalar(a.b * 2.0 - 1.0, b.b));
}

float3 VJBlendPinLight(float3 a, float3 b)
{
    return float3(a.r < 0.5 ? min(b.r, 2.0 * a.r) : max(b.r, 2.0 * a.r - 1.0),
        a.g < 0.5 ? min(b.g, 2.0 * a.g) : max(b.g, 2.0 * a.g - 1.0),
        a.b < 0.5 ? min(b.b, 2.0 * a.b) : max(b.b, 2.0 * a.b - 1.0));
}

float3 VJBlendColorDodgeSafe(float3 a, float3 b) { return VJBlendColorDodge(a, b); }
float3 VJBlendColorBurnSafe(float3 a, float3 b) { return VJBlendColorBurn(a, b); }
float3 VJBlendDivide(float3 a, float3 b)
{
    return a / max(b, float3(1.0e-5, 1.0e-5, 1.0e-5));
}

float3 VJBlendReflect(float3 a, float3 b)
{
    return b * b / max(1.0 - a, float3(1.0e-5, 1.0e-5, 1.0e-5));
}

float3 VJBlendGlow(float3 a, float3 b)
{
    return a * a / max(1.0 - b, float3(1.0e-5, 1.0e-5, 1.0e-5));
}

float3 VJBlendSetHue(float3 a, float3 b)
{
    float3 ah = VJRGBToHSV(a);
    float3 bh = VJRGBToHSV(b);
    return VJHSVToRGB(float3(ah.x, bh.y, bh.z));
}

float3 VJBlendSetSaturation(float3 a, float3 b)
{
    float3 ah = VJRGBToHSV(a);
    float3 bh = VJRGBToHSV(b);
    return VJHSVToRGB(float3(bh.x, ah.y, bh.z));
}

float3 VJBlendSetColor(float3 a, float3 b)
{
    float3 ah = VJRGBToHSV(a);
    float3 bh = VJRGBToHSV(b);
    return VJHSVToRGB(float3(ah.x, ah.y, bh.z));
}

float3 VJBlendSetLuminosity(float3 a, float3 b)
{
    float3 ah = VJRGBToHSV(a);
    float3 bh = VJRGBToHSV(b);
    return VJHSVToRGB(float3(bh.x, bh.y, ah.z));
}

float3 VJBlendRgb(int variant, float3 a, float3 b)
{
    switch (variant)
    {
        case 3: return a + b;
        case 4: return saturate(a + b);
        case 5: return a - b;
        case 6: return b - a;
        case 7: return a * b;
        case 8: return 1.0 - (1.0 - a) * (1.0 - b);
        case 9: return VJBlendOverlay(a, b);
        case 10: return VJBlendOverlay(b, a);
        case 11: return VJBlendSoftLight(a, b);
        case 12: return VJBlendVividLight(a, b);
        case 13: return b + 2.0 * a - 1.0;
        case 14: return VJBlendPinLight(a, b);
        case 15: return step(0.5, a + b - 1.0);
        case 16: return abs(a - b);
        case 17: return a + b - 2.0 * a * b;
        case 18: return min(a, b);
        case 19: return max(a, b);
        case 20: return VJBlendColorDodgeSafe(a, b);
        case 21: return VJBlendColorBurnSafe(a, b);
        case 22: return a + b - 1.0;
        case 23: return VJBlendDivide(a, b);
        case 24: return (a + b) * 0.5;
        case 25: return 1.0 - abs(1.0 - a - b);
        case 26: return 1.0 - abs(a - b);
        case 27: return VJBlendReflect(a, b);
        case 28: return VJBlendGlow(a, b);
        case 29: return VJBlendSetHue(a, b);
        case 30: return VJBlendSetSaturation(a, b);
        case 31: return VJBlendSetColor(a, b);
        case 32: return VJBlendSetLuminosity(a, b);
        default: return a;
    }
}

float4 VJBlendAlphaOver(float4 foreground, float4 background)
{
    float alpha = foreground.a + background.a * (1.0 - foreground.a);
    if (alpha <= 1.0e-5) return 0.0;
    float3 rgb = (foreground.rgb * foreground.a + background.rgb * background.a * (1.0 - foreground.a)) / alpha;
    return float4(VJFinite3(rgb), saturate(alpha));
}

float4 VJBlendEvaluate(int variant, float4 a, float4 b, float amount, float externalMask, float depthA, float depthB)
{
    amount = saturate(VJFiniteScalar(amount));
    a = VJUnpremultiply(a);
    b = VJUnpremultiply(b);
    if (amount <= 0.0) return a;

    float4 result;
    if (variant == 0 || variant == 1) result = VJBlendAlphaOver(a, b);
    else if (variant == 2) result = VJBlendAlphaOver(b, a);
    else if (variant == 33) result = lerp(a, b, saturate(b.a));
    else if (variant == 34) result = lerp(a, b, saturate(externalMask));
    else if (variant == 35) result = depthB < depthA ? b : a;
    else
    {
        float3 rgb = VJBlendRgb(variant, saturate(a.rgb), saturate(b.rgb));
        float alpha = saturate(a.a + b.a - a.a * b.a);
        result = float4(rgb, alpha);
    }

    return VJFinite4(lerp(a, result, amount));
}

#endif
