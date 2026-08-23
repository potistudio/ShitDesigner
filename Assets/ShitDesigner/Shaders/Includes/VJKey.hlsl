#ifndef SHITDESIGNER_VJ_KEY_INCLUDED
#define SHITDESIGNER_VJ_KEY_INCLUDED

float VJKeyLuma(float3 color)
{
    return saturate(VJLuma(color));
}

float4 VJKeyEvaluate(sampler2D textureSampler, float2 uv, float2 texel, int variant)
{
    float4 source = VJUnpremultiply(VJSample2D(textureSampler, uv));
    float alpha = source.a;
    float value = alpha;
    float amount = VJFiniteScalar(_VJAmount);
    float threshold = VJFiniteScalar(_VJThreshold);
    float softness = max(abs(VJFiniteScalar(_VJSoftness)), 0.001);
    float3 result = source.rgb;

    switch (variant)
    {
        case 0: // Luma Key
            value = 1.0 - smoothstep(threshold - softness, threshold + softness, VJKeyLuma(source.rgb));
            alpha *= value;
            break;
        case 1: // Chroma Key
        {
            float3 key = _VJColorA.rgb;
            float distanceToKey = distance(source.rgb, key);
            value = 1.0 - smoothstep(threshold - softness, threshold + softness, distanceToKey);
            alpha *= 1.0 - value;
            break;
        }
        case 2: // Color Distance Key
        {
            float distanceToKey = distance(source.rgb, _VJColorA.rgb);
            alpha *= smoothstep(threshold - softness, threshold + softness, distanceToKey);
            break;
        }
        case 3: // Alpha Extract
            result = alpha.xxx;
            alpha = 1.0;
            break;
        case 4: // Alpha Set
            alpha = saturate(_VJAmount);
            break;
        case 5: // Premultiply
            result = source.rgb;
            break;
        case 6: // Unpremultiply
            result = source.rgb;
            break;
        case 7: // Invert Matte
            alpha = 1.0 - alpha;
            break;
        case 8: // Threshold Matte
            alpha = step(threshold, alpha);
            break;
        case 9: // Matte Blur
        {
            float neighborA = 0.0;
            for (int tap = -3; tap <= 3; tap++)
            {
                neighborA += VJUnpremultiply(VJSample2D(textureSampler, uv + float2(tap, 0.0) * texel * max(_VJRadius, 1.0))).a;
            }
            alpha = neighborA / 7.0;
            break;
        }
        case 10: // Matte Erode
        {
            float minimum = 1.0;
            for (int tap = -2; tap <= 2; tap++)
            {
                minimum = min(minimum, VJUnpremultiply(VJSample2D(textureSampler, uv + float2(tap, 0.0) * texel)).a);
            }
            alpha = minimum;
            break;
        }
        case 11: // Matte Dilate
        {
            float maximum = 0.0;
            for (int tap = -2; tap <= 2; tap++)
            {
                maximum = max(maximum, VJUnpremultiply(VJSample2D(textureSampler, uv + float2(tap, 0.0) * texel)).a);
            }
            alpha = maximum;
            break;
        }
        case 12: // Difference Key
            alpha *= smoothstep(threshold - softness, threshold + softness, distance(source.rgb, _VJColorA.rgb));
            break;
        case 13: // Additive Key
            value = saturate(VJKeyLuma(source.rgb) * max(_VJGain, 1.0));
            alpha *= smoothstep(threshold - softness, threshold + softness, value);
            break;
        case 14: // Screen Key
            value = 1.0 - (1.0 - source.r) * (1.0 - source.g) * (1.0 - source.b);
            alpha *= smoothstep(threshold - softness, threshold + softness, value);
            break;
        case 15: // Despill
        {
            float greenExcess = max(source.g - max(source.r, source.b), 0.0);
            result.g -= greenExcess * saturate(amount);
            break;
        }
        case 16: // Edge Color Replace
        {
            float2 gradient = float2(
                VJKeyLuma(VJUnpremultiply(VJSample2D(textureSampler, uv + texel * float2(1.0, 0.0))).rgb) -
                VJKeyLuma(VJUnpremultiply(VJSample2D(textureSampler, uv - texel * float2(1.0, 0.0))).rgb),
                VJKeyLuma(VJUnpremultiply(VJSample2D(textureSampler, uv + texel * float2(0.0, 1.0))).rgb) -
                VJKeyLuma(VJUnpremultiply(VJSample2D(textureSampler, uv - texel * float2(0.0, 1.0))).rgb));
            float edge = length(gradient);
            result = lerp(result, _VJColorA.rgb, smoothstep(threshold, threshold + softness, edge));
            break;
        }
        case 17: // Garbage Matte Rectangle
        {
            float2 minCorner = _VJColorA.xy;
            float2 maxCorner = _VJColorB.xy;
            alpha *= step(minCorner.x, uv.x) * step(uv.x, maxCorner.x) * step(minCorner.y, uv.y) * step(uv.y, maxCorner.y);
            break;
        }
        case 18: // Garbage Matte Circle
        {
            float2 center = _VJCenter.xy;
            if (center.x == 0.0 && center.y == 0.0) center = 0.5;
            alpha *= 1.0 - smoothstep(_VJRadius - softness, _VJRadius + softness, distance(uv, center));
            break;
        }
        case 19: // SDF Shape Matte
        {
            float2 shapeCenter = _VJCenter.xy;
            if (shapeCenter.x == 0.0 && shapeCenter.y == 0.0) shapeCenter = 0.5;
            float2 p = uv - shapeCenter;
            float sdf = max(abs(p.x), abs(p.y)) - max(_VJRadius, 0.001);
            alpha *= 1.0 - smoothstep(-softness, softness, sdf);
            break;
        }
        case 20: // Gradient Matte
        {
            float direction = dot(uv - 0.5, float2(cos(_VJAngle), sin(_VJAngle))) + 0.5;
            alpha *= smoothstep(threshold - softness, threshold + softness, direction);
            break;
        }
        case 21: // Noise Matte
            alpha *= smoothstep(threshold - softness, threshold + softness, VJValueNoise(uv * max(_VJFrequency, 1.0) * 8.0 + _SD_Seed));
            break;
        case 22: // Voronoi Matte
        {
            float2 cell = floor(uv * max(_VJFrequency, 1.0) * 6.0);
            float nearest = length(frac(uv * max(_VJFrequency, 1.0) * 6.0) - (float2(VJHash12(cell), VJHash12(cell + 3.0))));
            alpha *= step(threshold, nearest);
            break;
        }
        case 23: // Channel-as-Matte
        {
            int channel = (int)fmod(abs(_VJSeed), 3.0);
            value = channel == 0 ? source.r : (channel == 1 ? source.g : source.b);
            alpha = value;
            break;
        }
        default:
            break;
    }

    return VJPremultiply(float4(VJFinite3(result), saturate(alpha)));
}

#endif
