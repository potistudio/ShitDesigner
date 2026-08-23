#ifndef SHITDESIGNER_VJ_CONVOLUTION_INCLUDED
#define SHITDESIGNER_VJ_CONVOLUTION_INCLUDED

float4 VJConvolutionSample(sampler2D textureSampler, float2 uv)
{
    return VJUnpremultiply(VJSample2D(textureSampler, uv));
}

float3 VJConvolutionBox(sampler2D textureSampler, float2 uv, float2 texel, float2 direction, int radius, out float alpha)
{
    float3 sum = 0.0;
    float alphaSum = 0.0;
    float weightSum = 0.0;
    int safeRadius = clamp(radius, 1, 8);
    for (int tap = -8; tap <= 8; tap++)
    {
        if (abs(tap) > safeRadius) continue;
        float weight = 1.0;
        float4 sampleValue = VJConvolutionSample(textureSampler, uv + texel * direction * tap);
        sum += sampleValue.rgb * weight;
        alphaSum += sampleValue.a * weight;
        weightSum += weight;
    }
    alpha = alphaSum / max(weightSum, 1.0e-4);
    return sum / max(weightSum, 1.0e-4);
}

float3 VJConvolutionGaussian(sampler2D textureSampler, float2 uv, float2 texel, float2 direction, int radius, out float alpha)
{
    float3 sum = 0.0;
    float alphaSum = 0.0;
    float weightSum = 0.0;
    int safeRadius = clamp(radius, 1, 8);
    for (int tap = -8; tap <= 8; tap++)
    {
        if (abs(tap) > safeRadius) continue;
        float normalized = tap / max((float)safeRadius, 1.0);
        float weight = exp(-normalized * normalized * 2.5);
        float4 sampleValue = VJConvolutionSample(textureSampler, uv + texel * direction * tap);
        sum += sampleValue.rgb * weight;
        alphaSum += sampleValue.a * weight;
        weightSum += weight;
    }
    alpha = alphaSum / max(weightSum, 1.0e-4);
    return sum / max(weightSum, 1.0e-4);
}

float4 VJBlurEvaluate(sampler2D textureSampler, float2 uv, float2 texel, int variant)
{
    float4 center = VJConvolutionSample(textureSampler, uv);
    float amount = VJFiniteScalar(_VJAmount);
    float radius = max(abs(VJFiniteScalar(_VJRadius)), 1.0);
    float2 direction = float2(cos(_VJAngle), sin(_VJAngle));
    float alpha = center.a;
    float3 result = center.rgb;

    switch (variant)
    {
        case 0: // Box Blur
            result = VJConvolutionBox(textureSampler, uv, texel, float2(1.0, 0.0), (int)radius, alpha);
            result = lerp(center.rgb, result, saturate(abs(amount)));
            break;
        case 1: // Gaussian Blur
            result = VJConvolutionGaussian(textureSampler, uv, texel, float2(1.0, 0.0), (int)radius, alpha);
            result = lerp(center.rgb, result, saturate(abs(amount)));
            break;
        case 2: // Directional Blur
            result = VJConvolutionGaussian(textureSampler, uv, texel, direction, (int)radius, alpha);
            break;
        case 3: // Radial Blur
        {
            float3 sum = 0.0;
            float alphaSum = 0.0;
            for (int tap = 0; tap < 8; tap++)
            {
                float t = tap / 7.0;
                float2 sampleUv = lerp(0.5, uv, 1.0 - t * saturate(abs(amount)) * 0.8);
                float4 sampleValue = VJConvolutionSample(textureSampler, sampleUv);
                sum += sampleValue.rgb;
                alphaSum += sampleValue.a;
            }
            result = sum / 8.0;
            alpha = alphaSum / 8.0;
            break;
        }
        case 4: // Zoom Blur
        {
            float3 sum = 0.0;
            float alphaSum = 0.0;
            for (int tap = 0; tap < 8; tap++)
            {
                float t = tap / 7.0 - 0.5;
                float4 sampleValue = VJConvolutionSample(textureSampler, 0.5 + (uv - 0.5) * (1.0 + t * amount * 0.7));
                sum += sampleValue.rgb;
                alphaSum += sampleValue.a;
            }
            result = sum / 8.0;
            alpha = alphaSum / 8.0;
            break;
        }
        case 5: // Motion Blur
            result = VJConvolutionBox(textureSampler, uv, texel, VJSafeNormalize2(direction + 1.0e-5, float2(1.0, 0.0)), (int)radius, alpha);
            break;
        case 6: // Sharpen
        {
            float4 horizontal = VJConvolutionSample(textureSampler, uv + texel * float2(1.0, 0.0));
            float4 vertical = VJConvolutionSample(textureSampler, uv + texel * float2(0.0, 1.0));
            result = center.rgb * (1.0 + amount * 2.0) - (horizontal.rgb + vertical.rgb) * amount;
            break;
        }
        case 7: // Unsharp Mask
        {
            float blurAlpha;
            float3 blur = VJConvolutionGaussian(textureSampler, uv, texel, float2(1.0, 0.0), (int)radius, blurAlpha);
            result = center.rgb + (center.rgb - blur) * amount;
            break;
        }
        case 8: // Bloom
        {
            float4 bright = max(center - _VJColorA, 0.0);
            float blurAlpha;
            float3 blur = VJConvolutionGaussian(textureSampler, uv, texel, float2(1.0, 1.0), (int)radius, blurAlpha);
            result = center.rgb + blur * smoothstep(_VJThreshold, _VJThreshold + max(_VJSoftness, 0.05), VJLuma(bright.rgb)) * amount;
            break;
        }
        case 9: // Glow
        {
            float blurAlpha;
            float3 blur = VJConvolutionGaussian(textureSampler, uv, texel, VJSafeNormalize2(direction + 1.0e-5, float2(1.0, 0.0)), (int)radius, blurAlpha);
            float mask = smoothstep(_VJThreshold, _VJThreshold + max(_VJSoftness, 0.05), VJLuma(center.rgb));
            result = lerp(center.rgb, center.rgb + blur, mask * amount);
            break;
        }
        case 10: // Light Rays/God Rays
        {
            float3 sum = 0.0;
            for (int ray = 0; ray < 8; ray++)
            {
                float t = ray / 7.0;
                float2 sampleUv = lerp(uv, _VJCenter.xy, t * saturate(abs(amount)));
                sum += VJConvolutionSample(textureSampler, sampleUv).rgb * (1.0 - t);
            }
            result = sum / 4.0;
            break;
        }
        case 11: // Streak Bloom
        {
            float leftAlpha;
            float rightAlpha;
            float3 left = VJConvolutionGaussian(textureSampler, uv, texel, float2(1.0, 0.0), (int)radius, leftAlpha);
            float3 right = VJConvolutionGaussian(textureSampler, uv, texel, float2(-1.0, 0.0), (int)radius, rightAlpha);
            result += (left + right) * amount * 0.5;
            break;
        }
        case 12: // Kawase Blur
        {
            float3 sum = center.rgb;
            for (int tap = 1; tap <= 4; tap++)
            {
                float2 offset = texel * (tap + 0.5) * direction;
                sum += VJConvolutionSample(textureSampler, uv + offset).rgb;
                sum += VJConvolutionSample(textureSampler, uv - offset).rgb;
            }
            result = sum / 9.0;
            break;
        }
        case 13: // Dual Blur
        {
            float blurAlpha;
            float3 horizontal = VJConvolutionGaussian(textureSampler, uv, texel, float2(1.0, 0.0), 4, blurAlpha);
            float3 vertical = VJConvolutionGaussian(textureSampler, uv, texel, float2(0.0, 1.0), 4, blurAlpha);
            result = lerp(horizontal, vertical, 0.5);
            break;
        }
        case 14: // Bokeh Blur
        {
            float3 sum = 0.0;
            float weightSum = 0.0;
            for (int tap = 0; tap < 8; tap++)
            {
                float angle = tap * VJ_TAU / 8.0;
                float2 offset = float2(cos(angle), sin(angle)) * texel * radius;
                float weight = smoothstep(0.0, 1.0, VJLuma(VJConvolutionSample(textureSampler, uv + offset).rgb));
                sum += VJConvolutionSample(textureSampler, uv + offset).rgb * weight;
                weightSum += weight;
            }
            result = sum / max(weightSum, 1.0e-4);
            break;
        }
        case 15: // Tilt Shift
        {
            float focus = smoothstep(0.05, 0.2, abs(uv.y - _VJCenter.y));
            float blurAlpha;
            float3 blur = VJConvolutionGaussian(textureSampler, uv, texel, float2(1.0, 0.0), (int)radius, blurAlpha);
            result = lerp(center.rgb, blur, focus * saturate(abs(amount)));
            break;
        }
        case 16: // Iris Blur
        {
            float edge = smoothstep(_VJRadius - _VJSoftness, _VJRadius + _VJSoftness, length(uv - _VJCenter.xy));
            float blurAlpha;
            float3 blur = VJConvolutionGaussian(textureSampler, uv, texel, direction, (int)radius, blurAlpha);
            result = lerp(center.rgb, blur, edge * saturate(abs(amount)));
            break;
        }
        case 17: // Median Filter
        {
            float4 a = VJConvolutionSample(textureSampler, uv - texel * direction);
            float4 b = VJConvolutionSample(textureSampler, uv + texel * direction);
            result = max(min(a.rgb, b.rgb), min(max(a.rgb, b.rgb), center.rgb));
            break;
        }
        case 18: // Bilateral Blur
        {
            float4 neighbor = VJConvolutionSample(textureSampler, uv + texel * direction * radius);
            float weight = exp(-dot(neighbor.rgb - center.rgb, neighbor.rgb - center.rgb) * max(_VJFalloff, 0.1));
            result = lerp(center.rgb, neighbor.rgb, weight * saturate(abs(amount)));
            break;
        }
        case 19: // Surface Blur
        {
            float4 neighbor = VJConvolutionSample(textureSampler, uv + texel * float2(1.0, 0.0) * radius);
            result = lerp(center.rgb, neighbor.rgb, step(_VJThreshold, abs(VJLuma(neighbor.rgb - center.rgb))) * saturate(abs(amount)));
            break;
        }
        case 20: // Lens Flare
        {
            float radial = smoothstep(0.8, 0.0, length(uv - _VJCenter.xy));
            result += _VJColorA.rgb * radial * amount;
            break;
        }
        case 21: // Anamorphic Flare
        {
            float streak = exp(-abs(uv.y - _VJCenter.y) * max(_VJFalloff, 1.0) * 80.0);
            result += _VJColorA.rgb * streak * amount;
            break;
        }
        case 22: // Starburst
        {
            float radial = smoothstep(0.7, 0.0, length(uv - _VJCenter.xy));
            float rays = pow(abs(cos(atan2(uv.y - _VJCenter.y, uv.x - _VJCenter.x) * max(_VJDetail, 4.0))), 8.0);
            result += _VJColorA.rgb * radial * rays * amount;
            break;
        }
        case 23: // Ghosting Flare
        {
            float3 ghost = VJConvolutionSample(textureSampler, lerp(uv, _VJCenter.xy, 0.35)).rgb;
            result = lerp(result, result + ghost * _VJColorA.rgb, saturate(abs(amount)));
            break;
        }
        case 24: // Depth of Field
        {
            float depth = VJConvolutionSample(textureSampler, uv).a;
            float blurAlpha;
            float3 blur = VJConvolutionGaussian(textureSampler, uv, texel, direction, (int)radius, blurAlpha);
            result = lerp(center.rgb, blur, saturate(abs(depth - _VJThreshold) * amount));
            break;
        }
        case 25: // Temporal Motion Blur
        {
            float3 previous = VJConvolutionSample(textureSampler, uv - _VJDisplacement.xy * _SD_DeltaTime).rgb;
            result = lerp(center.rgb, previous, saturate(abs(amount)));
            break;
        }
        case 26: // FFT Convolution approximation
        {
            float3 sum = 0.0;
            for (int tap = 0; tap < 8; tap++)
            {
                float angle = tap * VJ_TAU / 8.0;
                float2 offset = float2(cos(angle), sin(angle)) * texel * radius;
                sum += VJConvolutionSample(textureSampler, uv + offset).rgb * cos(angle * max(_VJFrequency, 1.0));
            }
            result = center.rgb + sum * amount * 0.06;
            break;
        }
        case 27: // Custom Kernel 3x3/5x5
        {
            float3 sum = 0.0;
            float weightSum = 0.0;
            for (int y = -2; y <= 2; y++)
            {
                for (int x = -2; x <= 2; x++)
                {
                    float weight = VJHash12(float2(x, y) + _VJSeed);
                    sum += VJConvolutionSample(textureSampler, uv + texel * float2(x, y)).rgb * weight;
                    weightSum += weight;
                }
            }
            result = sum / max(weightSum, 1.0e-4);
            break;
        }
        default:
            break;
    }

    return VJPremultiply(float4(VJFinite3(result), alpha));
}

float VJEdgeLuma(sampler2D textureSampler, float2 uv)
{
    return VJLuma(VJConvolutionSample(textureSampler, uv).rgb);
}

float3 VJEdgeGradient(sampler2D textureSampler, float2 uv, float2 texel, out float luma)
{
    float center = VJEdgeLuma(textureSampler, uv);
    float left = VJEdgeLuma(textureSampler, uv - float2(texel.x, 0.0));
    float right = VJEdgeLuma(textureSampler, uv + float2(texel.x, 0.0));
    float down = VJEdgeLuma(textureSampler, uv - float2(0.0, texel.y));
    float up = VJEdgeLuma(textureSampler, uv + float2(0.0, texel.y));
    luma = center;
    return float3(right - left, up - down, abs(right + left + up + down - center * 4.0));
}

float4 VJEdgeEvaluate(sampler2D textureSampler, float2 uv, float2 texel, int variant)
{
    float4 source = VJConvolutionSample(textureSampler, uv);
    float luma;
    float3 gradient = VJEdgeGradient(textureSampler, uv, texel, luma);
    float edge = length(gradient.xy);
    float3 result = source.rgb;
    float amount = VJFiniteScalar(_VJAmount);
    float2 direction = VJSafeNormalize2(float2(cos(_VJAngle), sin(_VJAngle)) + 1.0e-5, float2(1.0, 0.0));

    switch (variant)
    {
        case 0: // Sobel Edge
            result = edge.xxx;
            break;
        case 1: // Laplacian Edge
            result = abs(gradient.zzz);
            break;
        case 2: // Emboss
            result = saturate(0.5 + gradient.x * _VJAmount + gradient.y * _VJAmount);
            break;
        case 3: // Halftone
        {
            float2 local = frac(uv * max(_VJFrequency, 2.0)) - 0.5;
            float dotMask = smoothstep(0.45, 0.03, length(local));
            result = lerp(_VJColorB.rgb, _VJColorA.rgb, dotMask * luma);
            break;
        }
        case 4: // Comic
            result = lerp(source.rgb, _VJColorA.rgb, smoothstep(_VJThreshold, _VJThreshold + _VJSoftness, edge));
            result = floor(result * max(_VJDetail, 2.0)) / max(_VJDetail, 2.0);
            break;
        case 5: // Toon Quantize
            result = floor(source.rgb * max(_VJDetail, 2.0)) / max(_VJDetail, 2.0);
            break;
        case 6: // Neon Edge
            result = _VJColorA.rgb * pow(saturate(edge), max(_VJGain, 0.2));
            break;
        case 7: // Outline
            result = lerp(source.rgb, _VJColorA.rgb, smoothstep(_VJThreshold, _VJThreshold + _VJSoftness, edge));
            break;
        case 8: // Oil Paint
        {
            float3 s0 = VJConvolutionSample(textureSampler, uv + texel * float2(-1.0, -1.0)).rgb;
            float3 s1 = VJConvolutionSample(textureSampler, uv + texel * float2(1.0, 1.0)).rgb;
            result = (s0 + s1 + source.rgb) / 3.0;
            break;
        }
        case 9: // Watercolor
            result = source.rgb * (0.7 + 0.3 * VJValueNoise(uv * max(_VJFrequency, 2.0)));
            result = lerp(result, _VJColorA.rgb, saturate(amount) * 0.25);
            break;
        case 10: // Pencil Sketch
            result = 1.0 - smoothstep(0.0, max(_VJSoftness, 0.05), edge);
            break;
        case 11: // Crosshatch
        {
            float hatch = step(0.65, frac((uv.x + uv.y) * max(_VJFrequency, 2.0))) + step(0.65, frac((uv.x - uv.y) * max(_VJFrequency, 2.0)));
            result = 1.0 - saturate(hatch * (1.0 - luma));
            break;
        }
        case 12: // Stipple
        {
            float2 cell = floor(uv * max(_VJFrequency, 2.0));
            float stipple = step(VJHash12(cell), frac(luma + VJHash12(cell + 3.0)));
            result = stipple.xxx;
            break;
        }
        case 13: // Poster Print
            result = floor((source.rgb + edge.xxx * amount) * max(_VJDetail, 2.0)) / max(_VJDetail, 2.0);
            break;
        case 14: // Scharr Edge
            result = (abs(gradient.x) * 3.0 + abs(gradient.y) * 3.0).xxx;
            break;
        case 15: // Roberts Edge
        {
            float a = VJEdgeLuma(textureSampler, uv);
            float b = VJEdgeLuma(textureSampler, uv + texel);
            float c = VJEdgeLuma(textureSampler, uv + float2(texel.x, 0.0));
            float d = VJEdgeLuma(textureSampler, uv + float2(0.0, texel.y));
            result = abs(float3(a - b, c - d, 0.0));
            break;
        }
        case 16: // Difference of Gaussians
        {
            float coarseAlpha;
            float fineAlpha;
            float3 coarse = VJConvolutionGaussian(textureSampler, uv, texel, float2(1.0, 0.0), 4, coarseAlpha);
            float3 fine = VJConvolutionGaussian(textureSampler, uv, texel, float2(1.0, 0.0), 1, fineAlpha);
            result = abs(fine - coarse);
            break;
        }
        case 17: // Canny approximation
            result = step(_VJThreshold, edge).xxx;
            break;
        case 18: // Kuwahara
        {
            float3 q0 = VJConvolutionSample(textureSampler, uv + texel * float2(-1.0, -1.0)).rgb;
            float3 q1 = VJConvolutionSample(textureSampler, uv + texel * float2(1.0, -1.0)).rgb;
            float3 q2 = VJConvolutionSample(textureSampler, uv + texel * float2(-1.0, 1.0)).rgb;
            float3 q3 = VJConvolutionSample(textureSampler, uv + texel * float2(1.0, 1.0)).rgb;
            result = (q0 + q1 + q2 + q3) * 0.25;
            break;
        }
        case 19: // Anisotropic Kuwahara
            result = lerp(source.rgb, VJConvolutionSample(textureSampler, uv + gradient.xy * texel * amount).rgb, 0.5);
            break;
        case 20: // Smudge
        {
            float3 a = VJConvolutionSample(textureSampler, uv - texel * direction).rgb;
            float3 b = VJConvolutionSample(textureSampler, uv + texel * direction).rgb;
            result = lerp(a, b, 0.5 + gradient.x * amount);
            break;
        }
        case 21: // Brush Strokes
        {
            float2 offset = VJSafeNormalize2(gradient.xy + 1.0e-5, float2(1.0, 0.0)) * texel * max(_VJRadius, 1.0);
            result = (VJConvolutionSample(textureSampler, uv + offset).rgb + VJConvolutionSample(textureSampler, uv - offset).rgb) * 0.5;
            break;
        }
        case 22: // Palette Painting
            result = lerp(_VJColorA.rgb, _VJColorB.rgb, saturate(luma));
            break;
        case 23: // Engraving
        {
            float lineA = step(0.6, frac((uv.x + uv.y) * max(_VJFrequency, 2.0)));
            float lineB = step(0.6, frac((uv.x - uv.y) * max(_VJFrequency, 2.0)));
            result = 1.0 - (lineA * lineB).xxx * (1.0 - luma);
            break;
        }
        case 24: // Blueprint
            result = float3(0.04, 0.18, 0.35) + source.rgb * float3(0.08, 0.35, 0.45) + edge.xxx * _VJColorA.rgb;
            break;
        case 25: // Photocopy
            result = step(_VJThreshold, luma).xxx * (1.0 - edge.xxx * amount);
            break;
        case 26: // Risograph
            result = float3(step(0.5, frac(luma + 0.1)), step(0.5, frac(luma + 0.4)), step(0.5, frac(luma + 0.7)));
            break;
        case 27: // Screen Print
            result = floor((source.rgb + edge.xxx * amount) * 3.0) / 3.0;
            break;
        case 28: // Hologram
            result = float3(luma * 0.2, luma * 0.8, luma) * (0.7 + 0.3 * sin(uv.y * 80.0 + _SD_Time * 3.0));
            break;
        case 29: // Iridescence
            result = VJHSVToRGB(float3(frac(luma + _SD_Time * 0.05), 0.8, 0.8)) * (0.7 + 0.3 * edge);
            break;
        case 30: // Glass
        {
            float2 offset = gradient.xy * amount * texel * 30.0;
            result = VJConvolutionSample(textureSampler, uv + offset).rgb;
            break;
        }
        case 31: // Frosted Glass
        {
            float2 cell = floor(uv * max(_VJFrequency, 2.0));
            float2 jitter = float2(VJHash12(cell), VJHash12(cell + 17.0)) - 0.5;
            result = VJConvolutionSample(textureSampler, uv + jitter * amount * texel * 20.0).rgb;
            break;
        }
        case 32: // Mosaic Glass
        {
            float2 grid = max(_VJFrequency, 2.0);
            float2 cell = floor(uv * grid);
            result = VJConvolutionSample(textureSampler, (cell + 0.5) / grid).rgb;
            break;
        }
        case 33: // Refraction
            result = VJConvolutionSample(textureSampler, uv + gradient.xy * amount * texel * 40.0).rgb;
            break;
        case 34: // Signed-distance Contours
            result = step(_VJThreshold, abs(gradient.z)).xxx;
            break;
        case 35: // Normal-map Lighting
        {
            float3 normal = VJSafeNormalize3(float3(-gradient.x * amount, -gradient.y * amount, 1.0), float3(0.0, 0.0, 1.0));
            float light = saturate(dot(normal, VJSafeNormalize3(_VJDirection.xxx + float3(0.2, 0.4, 1.0), float3(0.0, 0.0, 1.0))));
            result = source.rgb * (0.35 + 0.65 * light);
            break;
        }
        case 36: // Height-map Relief
            result = source.rgb + gradient.zxy * amount;
            break;
        case 37: // Voronoi Mosaic Stylizer
        {
            float2 cell = floor(uv * max(_VJFrequency, 2.0));
            float2 nearest = frac(uv * max(_VJFrequency, 2.0)) - 0.5;
            float2 feature = float2(VJHash12(cell), VJHash12(cell + 9.0)) - 0.5;
            result = VJConvolutionSample(textureSampler, (cell + feature + 0.5) / max(_VJFrequency, 2.0)).rgb;
            break;
        }
        default:
            break;
    }
    return VJPremultiply(float4(VJFinite3(result), source.a));
}

#endif
