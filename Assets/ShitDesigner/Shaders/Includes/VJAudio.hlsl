#ifndef SHITDESIGNER_VJ_AUDIO_INCLUDED
#define SHITDESIGNER_VJ_AUDIO_INCLUDED

#include "Assets/ShitDesigner/Shaders/Includes/VJCommon.hlsl"

float VJAudioWave(sampler2D waveformTexture, float x, int channel)
{
    float4 sampleValue = VJSample2D(waveformTexture, float2(saturate(x), 0.5));
    return channel == 1 ? sampleValue.g : sampleValue.r;
}

float VJAudioSpectrum(sampler2D spectrumTexture, float frequency)
{
    return saturate(VJFiniteScalar(VJSample2D(spectrumTexture, float2(saturate(frequency), 0.5)).r));
}

float VJAudioMel(sampler2D melTexture, float band)
{
    return saturate(VJFiniteScalar(VJSample2D(melTexture, float2(saturate(band), 0.5)).r));
}

float3 VJAudioPalette(float value, float phase)
{
    float3 hsv = float3(frac(value * 0.33 + phase), 0.72, saturate(0.25 + value * 0.85));
    return VJHSVToRGB(hsv);
}

float VJAudioLine(float coordinate, float waveform, float thickness)
{
    return 1.0 - smoothstep(thickness, thickness + 0.025, abs(coordinate - waveform));
}

float4 VJAudioEvaluate(int variant, sampler2D waveformTexture, sampler2D spectrumTexture,
    sampler2D melTexture, sampler2D onsetTexture, float2 uv, float4 resolution,
    float time, float frame, float rms, float peak, float beat, float bpmPhase,
    float amount, float gain, float seed)
{
    uv = saturate(VJFinite2(uv));
    float2 coord = uv * 2.0 - 1.0;
    float aspect = max(resolution.x / max(resolution.y, 1.0), 1.0e-4);
    coord.x *= aspect;
    float safeTime = VJFiniteScalar(time);
    float safeFrame = VJFiniteScalar(frame);
    float safeAmount = saturate(VJFiniteScalar(amount));
    float safeGain = max(VJFiniteScalar(gain), 0.0);
    float safeRms = saturate(VJFiniteScalar(rms) * safeGain);
    float safePeak = saturate(VJFiniteScalar(peak) * safeGain);
    float safeBeat = saturate(VJFiniteScalar(beat));
    float phase = frac(VJFiniteScalar(bpmPhase));
    float wave = VJAudioWave(waveformTexture, uv.x, 0) * safeGain;
    float waveOther = VJAudioWave(waveformTexture, uv.x, 1) * safeGain;
    float spectrum = VJAudioSpectrum(spectrumTexture, uv.x) * safeGain;
    float low = VJAudioSpectrum(spectrumTexture, 0.08) * safeGain;
    float mid = VJAudioSpectrum(spectrumTexture, 0.42) * safeGain;
    float high = VJAudioSpectrum(spectrumTexture, 0.82) * safeGain;
    float mel = VJAudioMel(melTexture, uv.x) * safeGain;
    float angle = atan2(coord.y, coord.x) / VJ_TAU + 0.5;
    float radius = length(coord);
    float stripe = frac(uv.x * 16.0);
    float3 color;

    if (variant == 0) // Waveform Line
    {
        float lineValue = VJAudioLine(uv.y * 2.0 - 1.0, wave, safeAmount * 0.08 + 0.01);
        return VJFinite4(float4(VJAudioPalette(lineValue, phase) * lineValue, lineValue));
    }
    if (variant == 1) // Waveform Fill
    {
        float fill = step(uv.y * 2.0 - 1.0, wave);
        return VJFinite4(float4(VJAudioPalette(fill, phase) * fill, fill));
    }
    if (variant == 2) // Dual Waveform
    {
        float left = VJAudioLine(coord.y, wave, 0.025 + safeAmount * 0.05);
        float right = VJAudioLine(coord.y, waveOther, 0.025 + safeAmount * 0.05);
        return VJFinite4(float4(left, right, max(left, right), max(left, right)));
    }
    if (variant == 3) // XY / Lissajous Scope
    {
        float2 target = float2(wave, waveOther);
        float lineValue = 1.0 - smoothstep(0.01, 0.055, length(coord - target));
        return VJFinite4(float4(lineValue * (0.4 + 0.6 * abs(target.x)), lineValue, lineValue * (0.5 + 0.5 * abs(target.y)), lineValue));
    }
    if (variant == 4) // Vectorscope
    {
        float2 directionVector = float2(wave, waveOther);
        float spoke = 1.0 - smoothstep(0.01, 0.04, abs(cross(float3(coord, 0.0), float3(directionVector, 0.0)).z));
        float dotValue = 1.0 - smoothstep(0.025, 0.1, length(coord - directionVector));
        return VJFinite4(float4(spoke * 0.15 + dotValue, dotValue * 0.8 + spoke * 0.2, dotValue, saturate(spoke + dotValue)));
    }
    if (variant == 5) // Radial Waveform
    {
        float radialWave = VJAudioWave(waveformTexture, angle, 0);
        float lineValue = 1.0 - smoothstep(0.02, 0.07, abs(radius - 0.45 - radialWave * 0.3 * safeAmount));
        return VJFinite4(float4(VJAudioPalette(radialWave, phase) * lineValue, lineValue));
    }
    if (variant == 6) // Circular Oscilloscope
    {
        float cyclic = sin(angle * VJ_TAU * 2.0 + safeTime) * 0.15 + wave * 0.25;
        float lineValue = 1.0 - smoothstep(0.02, 0.06, abs(radius - 0.52 - cyclic));
        return VJFinite4(float4(lineValue, lineValue * (0.3 + safeRms), lineValue * (0.8 + high * 0.2), lineValue));
    }
    if (variant == 7) // Spectrum Bars
    {
        float bar = floor(uv.x * 32.0) / 32.0;
        float height = VJAudioSpectrum(spectrumTexture, bar) * safeAmount + 0.02;
        float mask = step(1.0 - height, uv.y) * step(frac(uv.x * 32.0), 0.92);
        return VJFinite4(float4(VJAudioPalette(height, phase) * mask, mask));
    }
    if (variant == 8) // Mirrored Spectrum
    {
        float mirrored = abs(coord.x);
        float height = VJAudioSpectrum(spectrumTexture, mirrored) * safeAmount + 0.015;
        float mask = step(abs(coord.y), height) * step(abs(frac(mirrored * 32.0) - 0.5), 0.46);
        return VJFinite4(float4(mask * (0.2 + height), mask * height, mask * (1.0 - height), mask));
    }
    if (variant == 9) // Radial Spectrum
    {
        float bar = VJAudioSpectrum(spectrumTexture, angle);
        float mask = step(radius, 0.22 + bar * 0.62 * safeAmount);
        return VJFinite4(float4(VJAudioPalette(bar, phase) * mask, mask));
    }
    if (variant == 10) // Spectrum Ring
    {
        float bar = VJAudioSpectrum(spectrumTexture, angle);
        float ring = 1.0 - smoothstep(0.02, 0.07, abs(radius - 0.55 - bar * 0.2 * safeAmount));
        return VJFinite4(float4(VJAudioPalette(bar, phase) * ring, ring));
    }
    if (variant == 11) // Spectrum Terrain
    {
        float terrain = VJAudioSpectrum(spectrumTexture, uv.x) * safeAmount;
        float y = uv.y - 0.5 - terrain * 0.45;
        float edge = 1.0 - smoothstep(0.01, 0.04, abs(y));
        float body = step(uv.y, 0.5 + terrain * 0.45);
        return VJFinite4(float4(VJAudioPalette(terrain, phase) * (body * 0.35 + edge), saturate(body + edge)));
    }
    if (variant == 12) // Spectrogram
    {
        float movingBand = frac(uv.x + phase * 0.25);
        float value = VJAudioSpectrum(spectrumTexture, movingBand) * (0.5 + uv.y * 0.5);
        return VJFinite4(float4(VJAudioPalette(value, movingBand) * value, 1.0));
    }
    if (variant == 13) // Waterfall Spectrum
    {
        float value = VJAudioSpectrum(spectrumTexture, uv.x + frac(uv.y + phase) * 0.05);
        float lineValue = smoothstep(0.0, 0.15, value * safeAmount + 0.01);
        return VJFinite4(float4(VJAudioPalette(value, uv.y + phase) * lineValue, 1.0));
    }
    if (variant == 14) // Frequency Dots
    {
        float2 cell = floor(uv * float2(24.0, 12.0));
        float frequency = (cell.x + 0.5) / 24.0;
        float value = VJAudioSpectrum(spectrumTexture, frequency) * safeAmount;
        float dotValue = 1.0 - smoothstep(0.06, 0.2, length(frac(uv * float2(24.0, 12.0)) - 0.5));
        dotValue *= step(cell.y / 12.0, value);
        return VJFinite4(float4(VJAudioPalette(value, phase) * dotValue, dotValue));
    }
    if (variant == 15) // Frequency Ribbons
    {
        float ribbon = sin((uv.y + sin(uv.x * VJ_TAU * 3.0) * 0.08 * safeAmount) * VJ_TAU * 5.0 + phase * VJ_TAU);
        float value = VJAudioSpectrum(spectrumTexture, uv.x);
        float mask = 1.0 - smoothstep(0.04, 0.18, abs(ribbon) - value * safeAmount);
        return VJFinite4(float4(VJAudioPalette(value, phase) * mask, mask));
    }
    if (variant == 16) // Beat Rings
    {
        float ringRadius = frac(radius * 2.5 - safeBeat * 0.7);
        float ring = (1.0 - smoothstep(0.03, 0.14, ringRadius)) * safeBeat;
        return VJFinite4(float4(VJAudioPalette(ring, phase) * ring, ring));
    }
    if (variant == 17) // Beat Tunnel
    {
        float tunnel = abs(sin((1.0 / max(radius, 0.04) + phase * 4.0) * VJ_PI));
        float mask = tunnel * (0.2 + safeBeat * 0.8);
        return VJFinite4(float4(VJAudioPalette(tunnel, phase) * mask, 1.0));
    }
    if (variant == 18) // Beat Flash
    {
        color = lerp(float3(0.015, 0.02, 0.035), VJAudioPalette(peak, phase), safeBeat * safeAmount);
        return VJFinite4(float4(color, 1.0));
    }
    if (variant == 19) // Beat Strobe
    {
        float strobe = step(0.5, frac(phase * 4.0 + safeBeat * 0.5));
        strobe *= safeBeat > 0.01 ? 1.0 : 0.1;
        return VJFinite4(float4(strobe.xxx * VJAudioPalette(strobe, phase), 1.0));
    }
    if (variant == 20) // Bass Pulse
    {
        float pulse = saturate(low * safeAmount + safeBeat * 0.75);
        float mask = 1.0 - smoothstep(0.15 + pulse * 0.3, 0.16 + pulse * 0.3, radius);
        return VJFinite4(float4(VJAudioPalette(pulse, phase) * mask, mask));
    }
    if (variant == 21) // Band Colorizer
    {
        color = float3(low, mel, high);
        return VJFinite4(float4(color * (0.4 + safeAmount * 0.6), 1.0));
    }
    if (variant == 22) // Audio Displacement
    {
        float displacement = VJAudioWave(waveformTexture, uv.x + coord.y * 0.1, 0);
        float mask = saturate(0.5 + 0.5 * sin((uv.y + displacement * safeAmount) * VJ_TAU * 8.0));
        return VJFinite4(float4(VJAudioPalette(mask, phase) * mask, 1.0));
    }
    if (variant == 23) // Audio Kaleidoscope
    {
        float sectors = max(3.0, floor(3.0 + safeAmount * 9.0));
        float folded = abs(frac(angle * sectors) * 2.0 - 1.0);
        float value = VJAudioSpectrum(spectrumTexture, folded) * (1.0 - radius * 0.5);
        return VJFinite4(float4(VJAudioPalette(value, folded + phase) * value, 1.0));
    }
    if (variant == 24) // Audio Particle Field
    {
        float2 cell = floor((uv + 0.5) * 20.0);
        float2 local = frac((uv + 0.5) * 20.0) - 0.5;
        float2 particleOffset = float2(VJHash12(cell + seed), VJHash12(cell + seed + 4.0)) - 0.5;
        float value = VJAudioSpectrum(spectrumTexture, VJHash12(cell)) * safeAmount;
        float dotValue = 1.0 - smoothstep(0.04, 0.2, length(local - particleOffset * value));
        return VJFinite4(float4(VJAudioPalette(value, phase) * dotValue, dotValue));
    }
    if (variant == 25) // Audio Starfield
    {
        float2 cell = floor((coord + 2.0) * 12.0);
        float star = step(0.94, VJHash12(cell + seed + floor(safeFrame * 0.02)));
        float brightness = star * (0.2 + high * safeAmount * 1.5);
        return VJFinite4(float4(VJAudioPalette(brightness, phase) * brightness, brightness));
    }
    if (variant == 26) // Audio Metaballs
    {
        float2 p0 = float2(sin(safeTime * 0.8), cos(safeTime * 0.7)) * 0.35;
        float2 p1 = float2(cos(safeTime * 0.5), sin(safeTime * 0.9)) * 0.3;
        float field = 0.08 / max(dot(coord - p0, coord - p0), 1.0e-4) + 0.06 / max(dot(coord - p1, coord - p1), 1.0e-4);
        float mask = smoothstep(0.7, 1.0 + safeAmount * 1.5, field * (0.5 + low));
        return VJFinite4(float4(VJAudioPalette(mask, phase) * mask, mask));
    }
    if (variant == 27) // Audio Fluid
    {
        float flow = VJValueNoise((coord + float2(safeTime * 0.04, -safeTime * 0.03)) * (4.0 + safeAmount * 8.0));
        flow = saturate(flow + low * 0.5 + sin(coord.x * 5.0 + coord.y * 3.0) * safeAmount * 0.1);
        return VJFinite4(float4(VJAudioPalette(flow, phase) * flow, 1.0));
    }
    if (variant == 28) // Audio Fractal Modulator
    {
        float2 q = coord;
        float value = 0.0;
        float amplitude = 0.5;
        for (int octave = 0; octave < 4; octave++)
        {
            value += VJValueNoise(q * (2.0 + low * safeAmount)) * amplitude;
            q = abs(q) * 1.9 - 0.8;
            amplitude *= 0.5;
        }
        value = saturate(value * (1.0 + mid * safeAmount));
        return VJFinite4(float4(VJAudioPalette(value, phase) * value, 1.0));
    }

    if (variant == 29) // Onset History Grid
    {
        // The optional history texture is sampled with a deterministic frame
        // coordinate; a missing texture simply yields black.
        float2 grid = floor(uv * float2(32.0, 16.0));
        float history = VJSample2D(onsetTexture, float2((grid.x + 0.5) / 32.0, (grid.y + phase * 16.0) / 16.0)).r;
        float onset = max(history, safeBeat * step(0.6, VJHash12(grid + seed)));
        return VJFinite4(float4(VJAudioPalette(onset, phase) * onset, 1.0));
    }

    // The family shader clamps the variant, but keep direct include callers
    // finite and visibly distinct from the last declared variant.
    return VJFinite4(float4(0.0, 0.0, 0.0, 1.0));
}

#endif
