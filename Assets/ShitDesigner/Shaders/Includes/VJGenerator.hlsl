#ifndef SHITDESIGNER_VJ_GENERATOR_INCLUDED
#define SHITDESIGNER_VJ_GENERATOR_INCLUDED

float3 VJGeneratorGradient(float value)
{
    value = frac(value);
    float3 a = _VJColorA.rgb;
    float3 b = _VJColorB.rgb;
    float3 c = _VJColorC.rgb;
    float3 ab = lerp(a, b, saturate(value * 2.0));
    float3 bc = lerp(b, c, saturate(value * 2.0 - 1.0));
    return lerp(ab, bc, step(0.5, value));
}

float VJGeneratorVoronoi(float2 coord, out float edge)
{
    float2 cell = floor(coord);
    float2 local = frac(coord);
    float nearest = 10.0;
    float second = 10.0;
    for (int y = -1; y <= 1; y++)
    {
        for (int x = -1; x <= 1; x++)
        {
            float2 offset = float2(x, y);
            float2 feature = offset + float2(VJHash12(cell + offset), VJHash12(cell + offset + 31.7));
            float distanceToFeature = length(feature - local);
            if (distanceToFeature < nearest)
            {
                second = nearest;
                nearest = distanceToFeature;
            }
            else if (distanceToFeature < second)
            {
                second = distanceToFeature;
            }
        }
    }
    edge = second - nearest;
    return nearest;
}

float VJGeneratorSdf(float2 coord, int shape)
{
    float2 p = abs(coord);
    if (shape == 0) return length(coord) - 0.45;
    if (shape == 1) return max(p.x, p.y) - 0.35;
    if (shape == 2) return max(p.y, p.x * 0.866025 + p.y * 0.5) - 0.35;
    if (shape == 3) return max(p.x - 0.18, p.y - 0.42);
    return max(p.x + p.y * 0.35, p.y + p.x * 0.35) - 0.35;
}

float4 VJGeneratorEvaluate(float2 inputUv, int variant)
{
    float2 uv = VJSafeUV(inputUv);
    float2 centered = VJAspectUV(uv) - 0.5;
    float2 coord = centered * 2.0;
    float time = VJFiniteScalar(_SD_Time * _VJSpeed + _VJPhase);
    float frequency = max(abs(VJFiniteScalar(_VJFrequency)), 0.001);
    float detail = max(abs(VJFiniteScalar(_VJDetail)), 1.0);
    float amount = VJFiniteScalar(_VJAmount);
    float value = 0.0;
    float edge = 0.0;
    float2 warped = coord;
    float3 color = _VJColorA.rgb;

    switch (variant)
    {
        case 0: // Solid Color
            color = _VJColorA.rgb;
            break;
        case 1: // Linear Gradient
            value = dot(coord, float2(cos(_VJAngle), sin(_VJAngle))) * 0.5 + 0.5 + time * _VJSpeed * 0.05;
            color = VJGeneratorGradient(value);
            break;
        case 2: // Radial Gradient
            value = length(coord - (_VJCenter.xy * 2.0 - 1.0)) / max(abs(_VJRadius), 0.001);
            color = VJGeneratorGradient(pow(saturate(value), max(abs(_VJFalloff), 0.05)));
            break;
        case 3: // Conic Gradient
            value = frac(atan2(coord.y, coord.x) / VJ_TAU + 0.5 + _VJAngle / VJ_TAU + time * 0.03);
            color = VJGeneratorGradient(value);
            break;
        case 4: // Checkerboard
            value = fmod(floor((uv.x + time * 0.03) * frequency) + floor((uv.y + time * 0.02) * frequency), 2.0);
            color = value > 0.5 ? _VJColorA.rgb : _VJColorB.rgb;
            break;
        case 5: // Grid
            warped = frac((uv - 0.5) * frequency + 0.5) - 0.5;
            value = max(abs(warped.x), abs(warped.y));
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, step(value, max(_VJSoftness, 0.03)));
            break;
        case 6: // Stripes
            value = frac(dot(coord, float2(cos(_VJAngle), sin(_VJAngle))) * frequency + time * 0.1);
            color = VJGeneratorGradient(value > saturate(_VJMix) ? 0.0 : 0.5);
            break;
        case 7: // Concentric Rings
            value = frac(length(coord - (_VJCenter.xy * 2.0 - 1.0)) * frequency + time * 0.08);
            color = VJGeneratorGradient(value);
            break;
        case 8: // Plasma
            value = sin(coord.x * frequency + time) + sin(coord.y * frequency * 1.31 - time * 0.7);
            value += sin((coord.x + coord.y) * frequency * 0.7 + time * 1.2);
            color = VJGeneratorGradient(value * 0.13 + 0.5);
            break;
        case 9: // FBM Clouds
            value = VJFBM(coord * frequency + float2(time * 0.08, -time * 0.04), (int)detail);
            color = VJGeneratorGradient(value);
            break;
        case 10: // Voronoi Cells
            value = VJGeneratorVoronoi(coord * frequency + time * 0.03, edge);
            color = VJGeneratorGradient(value);
            break;
        case 11: // Noise
            value = lerp(VJHash12(coord * frequency + time), VJValueNoise(coord * frequency + time), saturate(_VJMix));
            value = lerp(value, VJGradientNoise(coord * frequency + time) * 0.8 + 0.1, saturate(_VJDetail * 0.1));
            color = VJGeneratorGradient(value);
            break;
        case 12: // Starfield
        {
            float2 grid = coord * frequency * 8.0;
            float2 cell = floor(grid);
            float2 local = frac(grid) - 0.5;
            float star = smoothstep(0.15, 0.0, length(local - (float2(VJHash12(cell), VJHash12(cell + 9.7)) - 0.5) * 0.7));
            star *= step(0.82, VJHash12(cell + _VJSeed));
            value = star * (0.4 + 0.6 * VJHash12(cell + time * 0.1));
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        }
        case 13: // Kaleidoscope Pattern
        {
            float2 polar = VJPolarUV(uv);
            float sectors = max(floor(frequency * 0.5), 2.0);
            float sector = abs(frac(polar.x * sectors + time * 0.05) * 2.0 - 1.0);
            value = sin((polar.y + sector) * frequency * VJ_PI);
            color = VJGeneratorGradient(value * 0.5 + 0.5);
            break;
        }
        case 14: // Tunnel
        {
            float radius = max(length(coord), 0.05);
            value = frac(1.0 / radius + time * 0.3 + atan2(coord.y, coord.x) * _VJAmount);
            color = VJGeneratorGradient(value) * exp(-radius * max(_VJFalloff, 0.1));
            break;
        }
        case 15: // SDF Shapes
            value = 1.0 - VJSoftMask(VJGeneratorSdf(coord, (int)fmod(abs(_VJSeed), 5.0)), 0.0, max(_VJSoftness, 0.02));
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        case 16: // Domain Warp Clouds
            warped += float2(VJValueNoise(coord * frequency + time), VJValueNoise(coord * frequency + 13.7 - time)) * amount;
            value = VJFBM(warped * frequency, (int)detail);
            color = VJGeneratorGradient(value);
            break;
        case 17: // Ridged Noise
            value = VJFBM(coord * frequency + time * 0.05, (int)detail);
            value = 1.0 - abs(value * 2.0 - 1.0);
            color = VJGeneratorGradient(value);
            break;
        case 18: // Turbulence
            value = abs(VJFBM(coord * frequency + time * 0.07, (int)detail) * 2.0 - 1.0);
            color = VJGeneratorGradient(value);
            break;
        case 19: // Marble
            value = 0.5 + 0.5 * sin(coord.x * frequency * 4.0 + VJFBM(coord * frequency + time * 0.04, (int)detail) * amount * 8.0);
            color = VJGeneratorGradient(value);
            break;
        case 20: // Wood Rings
            value = frac(length(coord * float2(1.2, 0.8)) * frequency + VJFBM(coord * frequency, (int)detail) * amount);
            color = VJGeneratorGradient(value);
            break;
        case 21: // Caustics
            value = pow(saturate(1.0 - abs(sin(coord.x * frequency + time) * sin(coord.y * frequency * 1.1 - time))), max(_VJGain, 0.2));
            color = VJGeneratorGradient(value);
            break;
        case 22: // Cellular Sparks
        {
            float2 cell = floor(coord * frequency);
            float spark = step(0.92, VJHash12(cell + floor(time * 3.0)));
            value = spark * smoothstep(0.7, 0.0, length(frac(coord * frequency) - 0.5));
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        }
        case 23: // Metaballs
        {
            float sum = 0.0;
            for (int blob = 0; blob < 4; blob++)
            {
                float2 center = float2(VJHash12(float2(blob, _VJSeed)), VJHash12(float2(blob + 4, _VJSeed))) * 2.0 - 1.0;
                center += float2(sin(time * (0.4 + blob * 0.1) + blob), cos(time * (0.3 + blob * 0.12) - blob)) * 0.2;
                sum += 0.025 / max(dot(coord - center, coord - center), 0.001);
            }
            value = saturate(sum * 0.12);
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        }
        case 24: // Hexagon Field
            value = abs(frac((coord.x + coord.y * 0.577) * frequency) - 0.5) + abs(frac(coord.y * frequency * 0.866) - 0.5);
            value = step(value, 0.5);
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        case 25: // Triangle Grid
            value = frac((coord.x + coord.y) * frequency) + frac((coord.x - coord.y) * frequency);
            color = VJGeneratorGradient(frac(value));
            break;
        case 26: // Dot Matrix
        {
            float2 local = frac(coord * frequency) - 0.5;
            value = smoothstep(0.28, 0.02, length(local));
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        }
        case 27: // Halftone Field
        {
            float2 local = frac(coord * frequency) - 0.5;
            float source = VJFBM(coord * frequency * 0.1 + time * 0.02, 3);
            value = smoothstep(0.35, source * 0.5, length(local));
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        }
        case 28: // Moire Rings
            value = 0.5 + 0.5 * sin(length(coord) * frequency * 10.0 + sin(coord.x * frequency) * 2.0);
            color = VJGeneratorGradient(value);
            break;
        case 29: // Interference Waves
            value = 0.5 + 0.5 * sin(length(coord - float2(0.35, 0.0)) * frequency * 8.0 - time);
            value *= 0.5 + 0.5 * sin(length(coord + float2(0.35, 0.0)) * frequency * 7.0 + time * 1.2);
            color = VJGeneratorGradient(value);
            break;
        case 30: // Lissajous Curve
            value = exp(-abs(coord.y - sin(coord.x * frequency + time) * 0.45) * 20.0);
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        case 31: // Spirograph
        {
            float angle = atan2(coord.y, coord.x);
            float radius = length(coord);
            value = exp(-abs(radius - 0.45 - 0.14 * sin(angle * frequency + time)) * 32.0);
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        }
        case 32: // Radial Scope
            value = abs(sin(atan2(coord.y, coord.x) * frequency + time));
            color = VJGeneratorGradient(value);
            break;
        case 33: // Mandala
        {
            float2 polar = VJPolarUV(uv);
            value = 0.5 + 0.5 * cos(polar.x * frequency * VJ_TAU + sin(polar.y * 8.0 + time) * amount);
            color = VJGeneratorGradient(value);
            break;
        }
        case 34: // Guilloche
            value = 0.5 + 0.5 * sin((coord.x + sin(coord.y * frequency + time) * amount) * frequency * 10.0);
            color = VJGeneratorGradient(value);
            break;
        case 35: // Topographic Contours
            value = VJFBM(coord * frequency * 0.5 + time * 0.02, 5);
            value = smoothstep(0.35, 0.65, frac(value * max(detail, 2.0)));
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        case 36: // Digital Rain
        {
            float2 cell = floor(uv * float2(frequency * 20.0, frequency * 12.0));
            float stream = frac(uv.y * frequency * 12.0 + time * (0.4 + VJHash11(cell.x + _VJSeed)));
            value = step(0.75, VJHash12(cell + floor(time * 4.0))) * smoothstep(1.0, 0.0, stream);
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        }
        case 37: // Glitch Bars
        {
            float bar = floor(uv.y * max(detail, 2.0));
            float shift = (VJHash11(bar + floor(time * 3.0) + _VJSeed) - 0.5) * amount;
            value = VJValueNoise(float2(uv.x * frequency + shift, bar));
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        }
        case 38: // Scanline Burst
            value = pow(saturate(0.5 + 0.5 * sin(uv.y * frequency * 100.0 + time * 8.0)), max(_VJGain, 0.2));
            value *= smoothstep(1.0, 0.0, length(coord));
            color = VJGeneratorGradient(value);
            break;
        case 39: // Aurora Curtain
        {
            float curtain = 0.0;
            for (int layer = 0; layer < 4; layer++)
            {
                float l = layer * 0.19;
                curtain += exp(-abs(coord.y - sin(coord.x * (frequency + layer) + time * (0.4 + l)) * 0.25 - l) * 18.0) * (1.0 - layer * 0.18);
            }
            value = saturate(curtain);
            color = VJGeneratorGradient(value);
            break;
        }
        case 40: // Julia Fractal
        case 41: // Mandelbrot Fractal
        case 42: // Burning Ship Fractal
        case 43: // Newton Fractal
        {
            float2 z = variant == 40 ? coord * 1.6 : (variant == 43 ? coord * 1.5 : float2(0.0, 0.0));
            float2 c = variant == 40 ? float2(-0.7, 0.27) : coord * 1.5;
            float iterations = 0.0;
            for (int iteration = 0; iteration < 20; iteration++)
            {
                if (variant == 40) z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c;
                else if (variant == 41) z = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y) + c;
                else if (variant == 42) z = abs(z);
                else
                {
                    float2 z2 = float2(z.x * z.x - z.y * z.y, 2.0 * z.x * z.y);
                    float2 z3 = float2(z2.x * z.x - z2.y * z.y, z2.x * z.y + z2.y * z.x);
                    float2 numerator = z3 - float2(1.0, 0.0);
                    float2 denominator = 3.0 * z2;
                    float inverseDenominator = 1.0 / max(dot(denominator, denominator), 1.0e-4);
                    z -= float2(dot(numerator, denominator), numerator.y * denominator.x - numerator.x * denominator.y) * inverseDenominator;
                }
                if (dot(z, z) > 16.0) break;
                iterations += 1.0;
            }
            value = iterations / 20.0;
            color = VJGeneratorGradient(value);
            break;
        }
        case 44: // Reaction-Diffusion approximation
        {
            float n = VJValueNoise(coord * frequency + time * 0.06);
            float n2 = VJValueNoise(coord * frequency * 1.7 - time * 0.04);
            value = saturate(n * (1.0 - n2) * 2.5);
            color = VJGeneratorGradient(value);
            break;
        }
        case 45: // Gray-Scott Simulation
        {
            float a = VJValueNoise(coord * frequency + time * 0.03);
            float b = VJValueNoise(coord * frequency * 2.4 - time * 0.02);
            value = saturate(a * a + b * (1.0 - a));
            color = VJGeneratorGradient(value);
            break;
        }
        case 46: // Game of Life
        {
            float2 cell = floor((uv + time * 0.002) * max(detail, 2.0) * 6.0);
            float alive = step(0.58, VJHash12(cell + floor(time * 0.5)));
            value = alive * step(0.45, VJHash12(cell + 13.0));
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        }
        case 47: // Elementary Cellular Automata
        {
            float2 cell = floor(uv * max(detail, 2.0) * 8.0);
            float row = floor(time * 2.0);
            value = step(0.5, VJHash12(float2(cell.x + row * 0.7, cell.y + row)));
            value *= step(0.65, VJHash12(float2(cell.x - 1.0, row)));
            color = lerp(_VJColorB.rgb, _VJColorA.rgb, value);
            break;
        }
        default:
            color = _VJColorA.rgb;
            break;
    }

    return VJFinite4(float4(VJFinite3(color), 1.0));
}

#endif
