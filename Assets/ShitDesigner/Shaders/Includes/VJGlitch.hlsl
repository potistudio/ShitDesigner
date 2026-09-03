#ifndef SHITDESIGNER_VJ_GLITCH_INCLUDED
#define SHITDESIGNER_VJ_GLITCH_INCLUDED

float2 VJGlitchCell(float2 uv, float2 scale)
{
    return floor(uv * scale);
}

float4 VJGlitchStraightSample(sampler2D textureSampler, float2 uv)
{
    return VJUnpremultiply(VJSample2D(textureSampler, uv));
}

float4 VJGlitchEvaluate(sampler2D textureSampler, sampler2D historySampler, sampler2D historySampler2, sampler2D historySampler3, float2 uv, int variant)
{
    float2 safeUv = VJSafeUV(uv);
    float amount = VJFiniteScalar(_VJAmount);
    float frequency = max(abs(VJFiniteScalar(_VJFrequency)), 1.0);
    float time = VJFiniteScalar(_SD_Time * _VJSpeed + _VJPhase);
    float2 texel = float2(1.0 / max(abs(_SD_Resolution.x), 1.0), 1.0 / max(abs(_SD_Resolution.y), 1.0));
    float4 source = VJGlitchStraightSample(textureSampler, safeUv);
    float3 result = source.rgb;
    float alpha = source.a;

    switch (variant)
    {
        case 0: // RGB Split
        {
            float2 offset = float2(amount, amount * 0.7) * texel * 12.0;
            float r = VJGlitchStraightSample(textureSampler, safeUv + offset).r;
            float g = source.g;
            float b = VJGlitchStraightSample(textureSampler, safeUv - offset).b;
            result = float3(r, g, b);
            break;
        }
        case 1: // Chromatic Aberration
        {
            float2 radial = (safeUv - 0.5) * amount * texel * 35.0;
            result = float3(VJGlitchStraightSample(textureSampler, safeUv + radial).r, source.g, VJGlitchStraightSample(textureSampler, safeUv - radial).b);
            break;
        }
        case 2: // Block Glitch
        {
            float2 cell = VJGlitchCell(safeUv, float2(frequency * 8.0, frequency * 12.0));
            float trigger = step(0.75, VJHash12(cell + floor(time * 4.0) + _SD_Seed));
            float2 offset = (float2(VJHash12(cell + 3.0), VJHash12(cell + 9.0)) - 0.5) * amount * 0.2 * trigger;
            result = VJGlitchStraightSample(textureSampler, safeUv + offset).rgb;
            break;
        }
        case 3: // Slice Glitch
        {
            float row = floor(safeUv.y * max(frequency * 20.0, 2.0));
            float offset = (VJHash11(row + floor(time * 3.0) + _SD_Seed) - 0.5) * amount * 0.15;
            result = VJGlitchStraightSample(textureSampler, safeUv + float2(offset, 0.0)).rgb;
            break;
        }
        case 4: // Scanline
            result *= lerp(1.0, 0.65 + 0.35 * sin(safeUv.y * frequency * 300.0), saturate(amount));
            break;
        case 5: // Interlace
        {
            float lineValue = fmod(floor(safeUv.y * max(frequency * 240.0, 2.0)), 2.0);
            result *= lineValue > 0.5 ? (1.0 - saturate(amount) * 0.35) : 1.0;
            break;
        }
        case 6: // VHS
        {
            float wobble = sin(safeUv.y * frequency * 12.0 + time * 4.0) * amount * 0.015;
            result = VJGlitchStraightSample(textureSampler, safeUv + float2(wobble, 0.0)).rgb;
            result *= 0.88 + 0.12 * sin(safeUv.y * 400.0);
            break;
        }
        case 7: // CRT
        {
            float2 crt = safeUv * 2.0 - 1.0;
            float vignette = saturate(1.0 - dot(crt * crt, float2(0.18, 0.25)));
            float mask = 0.92 + 0.08 * sin(safeUv.x * 900.0);
            result *= vignette * mask;
            break;
        }
        case 8: // Analog Noise
        {
            float noise = VJHash12(safeUv * frequency * 200.0 + floor(time * 60.0));
            result += (noise - 0.5) * amount * 0.25;
            break;
        }
        case 9: // Digital Noise
        {
            float2 cell = floor(safeUv * max(frequency * 64.0, 2.0));
            float noise = step(0.5, VJHash12(cell + floor(time * 12.0)));
            result = lerp(result, float3(noise, noise, noise), saturate(amount) * 0.4);
            break;
        }
        case 10: // Pixelate
        {
            float2 grid = max(frequency * (8.0 + amount * 64.0), 2.0);
            float2 pixelUv = (floor(safeUv * grid) + 0.5) / grid;
            result = VJGlitchStraightSample(textureSampler, pixelUv).rgb;
            break;
        }
        case 11: // Mosaic
        {
            float2 grid = max(frequency * 12.0, 2.0);
            float2 local = frac(safeUv * grid);
            float2 offset = (local - 0.5) * amount * 0.04;
            result = VJGlitchStraightSample(textureSampler, safeUv - offset).rgb;
            break;
        }
        case 12: // Dither
        {
            float2 pixel = floor(safeUv * max(frequency * 180.0, 2.0));
            float threshold = frac(dot(pixel, float2(0.754877, 0.56984)) + 0.5);
            result = step(threshold.xxx, result);
            break;
        }
        case 13: // Compression Blocks
        {
            float2 block = floor(safeUv * max(frequency * 16.0, 2.0));
            float quant = max(_VJDetail, 2.0);
            result = floor(result * quant + VJHash12(block) * 0.4) / quant;
            break;
        }
        case 14: // Frame Tear
        {
            float tear = step(0.85, VJHash11(floor(time * 5.0) + _SD_Seed));
            float row = step(0.5, frac(safeUv.y * 17.0 + VJHash11(floor(time * 2.0))));
            float offset = (VJHash11(floor(time * 11.0)) - 0.5) * amount * 0.2 * tear * row;
            result = VJGlitchStraightSample(textureSampler, safeUv + float2(offset, 0.0)).rgb;
            break;
        }
        case 15: // Rolling Sync
        {
            float phase = frac(safeUv.y + time * 0.2);
            result = VJGlitchStraightSample(textureSampler, float2(safeUv.x, phase)).rgb;
            break;
        }
        case 16: // Bad TV
        {
            float wave = sin(safeUv.y * frequency * 20.0 + time) * amount * 0.03;
            float noise = VJValueNoise(safeUv * frequency * 30.0 + time);
            result = VJGlitchStraightSample(textureSampler, safeUv + float2(wave + (noise - 0.5) * amount * 0.03, 0.0)).rgb;
            result *= 0.75 + 0.25 * noise;
            break;
        }
        case 17: // RF Interference
        {
            float interference = sin(safeUv.x * frequency * 120.0 + sin(safeUv.y * 25.0 + time) * 10.0);
            result += interference.xxx * amount * 0.08;
            break;
        }
        case 18: // Tracking Error
        {
            float lineValue = step(0.72, VJHash11(floor(safeUv.y * 8.0) + floor(time * 2.0)));
            float shift = (VJHash11(floor(safeUv.y * 32.0) + time) - 0.5) * amount * 0.25 * lineValue;
            result = VJGlitchStraightSample(textureSampler, safeUv + float2(shift, 0.0)).rgb;
            break;
        }
        case 19: // Head-switch Noise
        {
            float head = smoothstep(0.95, 1.0, frac(safeUv.y * 2.0 + time * 0.4));
            result = lerp(result, float3(VJHash12(safeUv * 200.0 + time), VJHash12(safeUv * 170.0 - time), VJHash12(safeUv * 240.0)), head * saturate(amount));
            break;
        }
        case 20: // Color Bleed
        {
            float2 bleed = float2(amount * texel.x * 18.0, 0.0);
            float3 a = VJGlitchStraightSample(textureSampler, safeUv - bleed).rgb;
            float3 b = VJGlitchStraightSample(textureSampler, safeUv + bleed).rgb;
            result = float3(lerp(result.r, a.r, 0.4), lerp(result.g, result.r, 0.3), lerp(result.b, b.b, 0.4));
            break;
        }
        case 21: // Dot Crawl
        {
            float crawl = sin((safeUv.x + safeUv.y * 0.5) * frequency * 300.0 + time * 6.0);
            result += float3(crawl, -crawl, crawl) * amount * 0.06;
            break;
        }
        case 22: // Chroma Delay
        {
            float delay = amount * texel.x * 20.0;
            result.r = VJGlitchStraightSample(textureSampler, safeUv + float2(delay, 0.0)).r;
            result.b = VJGlitchStraightSample(textureSampler, safeUv - float2(delay, 0.0)).b;
            break;
        }
        case 23: // Phosphor Mask
        {
            float3 mask = float3(0.9 + 0.1 * sin(safeUv.x * 800.0), 0.9 + 0.1 * sin(safeUv.x * 800.0 + 2.1), 0.9 + 0.1 * sin(safeUv.x * 800.0 + 4.2));
            result *= mask;
            break;
        }
        case 24: // LCD Subpixel
        {
            float3 mask = frac(safeUv.x * frequency * 120.0).xxx;
            mask = float3(step(mask.x, 0.33), step(mask.x, 0.66), 1.0);
            result *= 0.8 + 0.2 * mask;
            break;
        }
        case 25: // Security Camera
        {
            float scan = 0.7 + 0.3 * sin(safeUv.y * frequency * 120.0 + time * 5.0);
            float vignette = saturate(1.0 - length(safeUv - 0.5) * 1.3);
            result = VJGlitchStraightSample(textureSampler, safeUv).rgb;
            result = result.ggg * scan * vignette;
            break;
        }
        case 26: // Night Vision
        {
            float luma = VJLuma(result);
            result = float3(luma * 0.18, luma, luma * 0.25);
            result += (VJHash12(safeUv * 100.0 + time) - 0.5).xxx * amount * 0.04;
            break;
        }
        case 27: // Infrared Camera
        {
            float luma = VJLuma(result);
            result = float3(saturate(luma * 2.0), saturate((luma - 0.25) * 2.0), saturate(1.0 - luma * 2.0));
            break;
        }
        case 28: // Terminal Green
        {
            float luma = VJLuma(result);
            result = float3(luma * 0.1, luma, luma * 0.25);
            result *= 0.75 + 0.25 * sin(safeUv.y * frequency * 200.0);
            break;
        }
        case 29: // ASCII Mosaic
        {
            float2 grid = max(frequency * 18.0, 2.0);
            float2 cell = floor(safeUv * grid);
            float2 local = frac(safeUv * grid) - 0.5;
            float luma = VJLuma(VJGlitchStraightSample(textureSampler, (cell + 0.5) / grid).rgb);
            float glyph = step(length(local), lerp(0.1, 0.45, luma));
            result = glyph.xxx;
            break;
        }
        case 30: // Hex Dump Mosaic
        {
            float2 cell = floor(safeUv * max(frequency * 20.0, 2.0));
            float code = frac(VJHash12(cell + _SD_Seed) * 17.0);
            result = step(code.xxx, frac(safeUv.x * max(frequency * 20.0, 2.0))).xxx;
            result *= source.rgb;
            break;
        }
        case 31: // Databend Simulation
        {
            float2 cell = floor(safeUv * max(frequency * 14.0, 2.0));
            float2 offsetVector = float2(VJHash12(cell + floor(time * 2.0)), VJHash12(cell + 23.0 + floor(time * 2.0))) - 0.5;
            result = VJGlitchStraightSample(textureSampler, safeUv + offsetVector * amount * 0.18).rgb;
            result = lerp(result, result.gbr, step(0.65, VJHash12(cell + 5.0)));
            float3 history = VJGlitchStraightSample(historySampler, safeUv).rgb;
            float3 history2 = VJGlitchStraightSample(historySampler2, safeUv).rgb;
            result = lerp(result, lerp(history, history2, 0.35), saturate(amount) * 0.25);
            break;
        }
        case 32: // Packet Loss
        {
            float row = floor(safeUv.y * max(frequency * 20.0, 2.0));
            float epoch = floor(time * 5.0);
            float loss = step(1.0 - saturate(amount) * 0.85, VJHash11(row + epoch * 17.0 + _SD_Seed));
            float replacementMode = floor(VJHash11(row + epoch * 31.0 + _VJSeed) * 3.0);
            float3 held = VJGlitchStraightSample(historySampler, safeUv).rgb;
            float horizontalOffset = (VJHash11(row + epoch * 7.0) - 0.5) * amount * 0.2;
            float3 displaced = VJGlitchStraightSample(textureSampler, safeUv + float2(horizontalOffset, 0.0)).rgb;
            float3 replacement = replacementMode < 1.0 ? 0.0 : (replacementMode < 2.0 ? held : displaced);
            result = lerp(result, replacement, loss);
            break;
        }
        case 33: // Buffer Underrun
        {
            float boundary = frac(time * 0.12 + VJHash11(_SD_Seed) * 0.5);
            float stalled = step(boundary, safeUv.y) * saturate(amount);
            float3 held = VJGlitchStraightSample(historySampler, safeUv).rgb;
            float edgeNoise = VJHash11(floor(safeUv.x * frequency * 24.0) + floor(time * 4.0));
            float edge = 1.0 - smoothstep(0.0, texel.y * (4.0 + frequency), abs(safeUv.y - boundary));
            result = lerp(result, held, stalled);
            result = lerp(result, edgeNoise.xxx, edge * saturate(amount));
            break;
        }
        case 34: // Frame Address Error
        {
            float2 grid = max(float2(frequency * 9.0, frequency * 6.0), 2.0);
            float2 cell = floor(safeUv * grid);
            float address = floor(VJHash12(cell + floor(time * 2.0) + _SD_Seed) * 4.0);
            float3 frame0 = source.rgb;
            float3 frame1 = VJGlitchStraightSample(historySampler, safeUv).rgb;
            float3 frame2 = VJGlitchStraightSample(historySampler2, safeUv).rgb;
            float3 frame3 = VJGlitchStraightSample(historySampler3, safeUv).rgb;
            float3 addressed = address < 1.0 ? frame0 : (address < 2.0 ? frame1 : (address < 3.0 ? frame2 : frame3));
            float enabled = step(1.0 - saturate(amount), VJHash12(cell + 43.0));
            result = lerp(result, addressed, enabled);
            break;
        }
        case 35: // Codec Collapse
        {
            float2 grid = max(float2(frequency * 16.0, frequency * 9.0), 2.0);
            float2 blockUv = (floor(safeUv * grid) + 0.5) / grid;
            float quantization = max(2.0, _VJDetail * lerp(8.0, 2.0, saturate(amount)));
            float2 chromaOffset = float2(amount / grid.x, 0.0);
            float3 blocked = VJGlitchStraightSample(textureSampler, blockUv).rgb;
            blocked.r = VJGlitchStraightSample(textureSampler, blockUv + chromaOffset).r;
            blocked.b = VJGlitchStraightSample(textureSampler, blockUv - chromaOffset).b;
            blocked = floor(blocked * quantization + 0.5) / quantization;
            result = lerp(result, blocked, saturate(amount));
            break;
        }
        case 36: // Bitplane Failure
        {
            float bitIndex = clamp(floor(abs(_VJDetail)), 1.0, 8.0);
            float bitValue = exp2(-bitIndex);
            float3 bitState = fmod(floor(saturate(result) / bitValue), 2.0);
            float channel = floor(VJHash11(floor(time * 3.0) + _SD_Seed) * 3.0);
            float3 channelMask = float3(1.0 - step(0.5, channel), step(0.5, channel) * (1.0 - step(1.5, channel)), step(1.5, channel));
            float cell = VJHash12(floor(safeUv * max(frequency * 32.0, 2.0)) + floor(time * 3.0));
            float failure = step(1.0 - saturate(amount), cell);
            result = saturate(result + (1.0 - 2.0 * bitState) * bitValue * channelMask * failure);
            break;
        }
        case 37: // Memory Corruption
        {
            float2 grid = max(float2(frequency * 12.0, frequency * 8.0), 2.0);
            float2 cell = floor(safeUv * grid);
            float epoch = floor(time * 2.0);
            float corruption = step(1.0 - saturate(amount) * 0.75, VJHash12(cell + epoch + _SD_Seed));
            float2 offset = (float2(VJHash12(cell + epoch * 11.0), VJHash12(cell + epoch * 19.0)) - 0.5) * 0.5;
            float historyAddress = floor(VJHash12(cell + 71.0) * 3.0);
            float3 memory0 = VJGlitchStraightSample(historySampler, safeUv + offset).rgb;
            float3 memory1 = VJGlitchStraightSample(historySampler2, safeUv + offset).rgb;
            float3 memory2 = VJGlitchStraightSample(historySampler3, safeUv + offset).rgb;
            float3 memory = historyAddress < 1.0 ? memory0 : (historyAddress < 2.0 ? memory1 : memory2);
            result = lerp(result, memory, corruption);
            break;
        }
        case 38: // Decode Drift
        {
            float2 grid = max(float2(frequency * 14.0, frequency * 9.0), 2.0);
            float2 cell = floor(safeUv * grid);
            float blockPhase = VJHash12(cell + _SD_Seed) - 0.5;
            float2 drift = float2(blockPhase * amount * 0.16, sin(time + blockPhase * 6.2831853) * amount * 0.015);
            float3 decoded = VJGlitchStraightSample(historySampler, safeUv - drift).rgb;
            float3 older = VJGlitchStraightSample(historySampler2, safeUv - drift * 1.7).rgb;
            decoded = lerp(decoded, older.gbr, step(0.55, VJHash12(cell + floor(time) + 13.0)));
            result = lerp(result, decoded, saturate(amount) * 0.85);
            break;
        }
        case 39: // Header Damage
        {
            float headerX = frac(safeUv.x + floor(time * 2.0) / max(frequency * 8.0, 2.0));
            float3 header = VJGlitchStraightSample(textureSampler, float2(headerX, texel.y * 0.5)).rgb;
            float control = frac(dot(header, float3(5.31, 9.17, 13.73)) + _SD_Seed);
            float row = floor(safeUv.y * max(frequency * 18.0, 2.0));
            float shift = (control - 0.5) * amount * 0.25 * step(0.45, VJHash11(row + floor(time * 4.0)));
            float3 damaged = VJGlitchStraightSample(textureSampler, safeUv + float2(shift, 0.0)).rgb;
            damaged = lerp(damaged, damaged.brg, step(0.65, control) * saturate(amount));
            result = damaged;
            break;
        }
        case 40: // Channel Dropout
        {
            float channel = floor(VJHash11(floor(time * max(_VJSpeed, 0.1) * 4.0) + _SD_Seed) * 3.0);
            float3 dropout = float3(1.0 - step(0.5, channel), step(0.5, channel) * (1.0 - step(1.5, channel)), step(1.5, channel));
            float offset = amount * texel.x * 28.0;
            float3 separated = float3(
                VJGlitchStraightSample(textureSampler, safeUv + float2(offset, 0.0)).r,
                source.g,
                VJGlitchStraightSample(textureSampler, safeUv - float2(offset, 0.0)).b);
            result = separated * (1.0 - dropout * saturate(amount));
            break;
        }
        case 41: // Data Rain Replacement
        {
            float2 grid = max(float2(frequency * 24.0, frequency * 14.0), 2.0);
            float2 cell = floor(safeUv * grid);
            float2 local = frac(safeUv * grid);
            float columnSpeed = 0.35 + VJHash11(cell.x + _SD_Seed) * 1.4;
            float head = frac(time * columnSpeed + VJHash11(cell.x + 23.0));
            float trail = saturate(1.0 - frac(head - safeUv.y + 1.0) * 5.0);
            float bit = step(0.5, VJHash12(cell + floor(time * columnSpeed * grid.y)));
            float glyph = step(0.18, local.x) * step(local.x, 0.82) * step(0.12, local.y) * step(local.y, 0.88);
            glyph *= step(0.35, frac(local.x * 3.0 + bit * 0.5));
            float replace = step(1.0 - saturate(amount), VJHash12(cell.xx + floor(time))) * trail;
            float3 dataColor = lerp(_VJColorA.rgb, _VJColorB.rgb, bit) * glyph * (0.25 + trail);
            result = lerp(result, dataColor, replace);
            break;
        }
        default:
            break;
    }

    return VJPremultiply(float4(VJFinite3(result), alpha));
}

#endif
