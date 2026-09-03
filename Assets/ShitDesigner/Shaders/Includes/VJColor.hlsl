#ifndef SHITDESIGNER_VJ_COLOR_INCLUDED
#define SHITDESIGNER_VJ_COLOR_INCLUDED

float3 VJColorCurve(float3 color)
{
    return color * color * (3.0 - 2.0 * color);
}

float3 VJColorApply(int variant, float3 color, float2 uv)
{
    float3 value = VJFinite3(color);
    float luma = VJLuma(value);
    float3 hsv;
    float3 palette;
    switch (variant)
    {
        case 0: value = 1.0 - value; break; // Invert
        case 1: value = luma.xxx; break; // Grayscale
        case 2: value = step(_VJThreshold, luma).xxx; break; // Threshold
        case 3: value = floor(value * max(_VJDetail, 2.0)) / max(_VJDetail - 1.0, 1.0); break; // Posterize
        case 4: value = luma > _VJThreshold ? 1.0 - value : value; break; // Solarize
        case 5: hsv = VJRGBToHSV(value); hsv.x = frac(hsv.x + _VJHue); value = VJHSVToRGB(hsv); break; // Hue Shift
        case 6: hsv = VJRGBToHSV(value); hsv.y *= max(_VJSaturation, 0.0); value = VJHSVToRGB(hsv); break; // Saturation
        case 7: hsv = VJRGBToHSV(value); hsv.y += (1.0 - hsv.y) * saturate(_VJAmount) * (1.0 - abs(2.0 * hsv.z - 1.0)); value = VJHSVToRGB(hsv); break; // Vibrance
        case 8: value += _VJAmount; break; // Brightness
        case 9: value = (value - 0.5) * max(_VJContrast, 0.0) + 0.5; break; // Contrast
        case 10: value *= exp2(_VJExposure); break; // Exposure
        case 11: value = pow(max(value, 0.0), 1.0 / max(_VJGamma, 1.0e-3)); break; // Gamma
        case 12: value = max(value + _VJColorA.rgb, 0.0) * _VJColorB.rgb + _VJColorC.rgb; break; // Lift/Gamma/Gain
        case 13: value = saturate((value - _VJColorA.rgb) / max(_VJColorB.rgb - _VJColorA.rgb, 1.0e-3)) * _VJColorC.rgb; break; // Levels
        case 14: value = lerp(_VJColorA.rgb, _VJColorB.rgb, saturate(luma)); break; // Duotone
        case 15: value = lerp(value, value * _VJColorA.rgb, saturate(_VJMix)); break; // Tint/Colorize
        case 16: value.r *= 1.0 + _VJTemperature; value.b *= 1.0 - _VJTemperature; value.g += _VJAmount * 0.25; break; // Temperature/Tint
        case 17: value = VJColorCurve(value); break; // RGB Curves
        case 18: value = float3(dot(value, float3(1.0, 0.0, 0.0)), dot(value, float3(0.0, 1.0, 0.0)), dot(value, float3(0.0, 0.0, 1.0))); value += float3(_VJColorA.r, _VJColorB.g, _VJColorC.b) * 0.25; break; // Channel Mixer
        case 19: value = float3(value.g, value.b, value.r); break; // Channel Shuffle
        case 20: value = float3(value.r, value.r, value.r); break; // Monochrome Channel
        case 21: value = float3(luma * 0.393 + value.g * 0.769 + value.b * 0.189, luma * 0.349 + value.g * 0.686 + value.b * 0.168, luma * 0.272 + value.g * 0.534 + value.b * 0.131); break; // Sepia
        case 22: value = float3(value.r * 1.4 + value.g * 0.1, value.g * 1.2 + value.b * 0.1, value.b * 1.6); break; // Technicolor
        case 23: value = lerp(value, value * (1.0 - value) * 2.0, saturate(_VJAmount)); break; // Bleach Bypass
        case 24: value = float3(value.r * 1.1 + value.b * 0.1, value.g * 0.9 + value.r * 0.05, value.b * 1.2 - value.r * 0.05); break; // Cross Process
        case 25: value = float3(pow(max(value.r, 0.0), 0.9), pow(max(value.g, 0.0), 1.05), pow(max(value.b, 0.0), 1.15)); break; // Film Stock Matrix
        case 26: value = float3(luma * 0.5, saturate(1.0 - abs(luma - 0.5) * 2.0), 1.0 - luma * 0.5); break; // False Color
        case 27: value = lerp(float3(0.0, 0.0, 0.2), float3(1.0, 0.1, 0.0), saturate(luma * 1.5)); break; // Thermal Map
        case 28: hsv = VJRGBToHSV(value); hsv.y = 1.0; hsv.z = max(hsv.z, 0.15); value = VJHSVToRGB(hsv) * (1.0 + 0.5 * sin(uv.x * VJ_TAU)); break; // Neon Palette
        case 29: value = lerp(_VJColorA.rgb, _VJColorB.rgb, saturate(luma)); break; // Gradient Map
        case 30: value = floor(value * max(_VJDetail, 2.0)) / max(_VJDetail, 2.0); break; // Color Quantize
        case 31: palette = lerp(_VJColorA.rgb, _VJColorB.rgb, step(0.5, frac(luma * max(_VJDetail, 2.0)))); value = lerp(value, palette, saturate(_VJMix)); break; // Palette Lookup
        case 32: value = tex3D(_VJLutTex, saturate(value)).rgb; break; // 3D LUT
        case 33: // ACES/AgX Display Preview
            value = max(value, 0.0);
            value = (value * (2.51 * value + 0.03)) / max(value * (2.43 * value + 0.59) + 0.14, 1.0e-4);
            break;
        default: value = color; break;
    }
    return VJFinite3(value);
}

float4 VJColorEvaluate(sampler2D textureSampler, float2 uv, int variant)
{
    float4 source = VJUnpremultiply(VJSample2D(textureSampler, uv));
    float3 result = VJColorApply(variant, source.rgb, uv);
    return VJPremultiply(float4(result, source.a));
}

#endif
