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

#define VJ_AUDIO_CUBE_COUNT 8
#define VJ_AUDIO_FRACTAL_STEPS 32
#define VJ_AUDIO_FRACTAL_FOLDS 3
#define VJ_AUDIO_FRACTAL_ACCUMULATION 0.28

float VJAudioFastGlow(float distanceToLine, float falloff)
{
    float attenuation = max(distanceToLine, 0.0) * falloff;
    return rcp(1.0 + attenuation + 0.48 * attenuation * attenuation);
}

float2x2 VJAudioCubeRotation(float angle)
{
    float sine;
    float cosine;
    sincos(angle, sine, cosine);
    return float2x2(cosine, -sine, sine, cosine);
}

float3 VJAudioCubePosition(int id, float time, float3 border)
{
    float cubeId = (float)id;
    float3 speed = float3(
        0.16 + sin(cubeId * 4.32) * 0.04,
        0.13 + cos(cubeId * 7.89) * 0.04,
        0.15 + sin(cubeId * 2.11) * 0.04);
    float3 startPhase = float3(sin(cubeId * 1.5), cos(cubeId * 2.3), sin(cubeId * 9.1)) * 10.0;
    float3 travel = startPhase + speed * time;
    float3 pingPong = abs(frac(travel * 0.5) * 2.0 - 1.0);
    return (pingPong * 2.0 - 1.0) * border;
}

float VJAudioBoxDistance(float2 samplePosition, float radius)
{
    float2 distanceToEdge = abs(samplePosition) - radius;
    return max(distanceToEdge.x, distanceToEdge.y);
}

float VJAudioSegmentDistance(float2 samplePosition, float2 start, float2 end)
{
    float2 pointOffset = samplePosition - start;
    float2 segment = end - start;
    float segmentLengthSquared = max(dot(segment, segment), 1.0e-8);
    float alongSegment = saturate(dot(pointOffset, segment) / segmentLengthSquared);
    return length(pointOffset - segment * alongSegment);
}

float3 VJAudioCubeVertex(int index)
{
    float x = (index == 0 || index == 3 || index == 4 || index == 7) ? -1.0 : 1.0;
    float y = (index == 0 || index == 1 || index == 4 || index == 5) ? -1.0 : 1.0;
    float z = index < 4 ? -1.0 : 1.0;
    return float3(x, y, z);
}

void VJAudioCubeEdge(int index, out int start, out int end)
{
    if (index < 4)
    {
        start = index;
        end = (index + 1) % 4;
        return;
    }
    if (index < 8)
    {
        start = index;
        end = 4 + (index - 3) % 4;
        return;
    }
    start = index - 8;
    end = start + 4;
}

float3 VJAudioCubeProjectWithDepth(float3 position, float2x2 cameraRotation)
{
    position.xz = mul(position.xz, cameraRotation);
    position.yz = mul(position.yz, cameraRotation);
    position.z += 2.2;
    float depth = max(position.z, 1.0e-4);
    return float3(position.xy / depth, depth);
}

float3 VJAudioTransformCubeVertex(float3 vertex, float3 center, float3 size, float2x2 objectRotation)
{
    float3 position = vertex * size;
    position.xy = mul(position.xy, objectRotation);
    position.xz = mul(position.xz, objectRotation);
    return position + center;
}

void VJAudioDrawWireframeBox(float2 coordinate, float3 center, float3 size, float2x2 cameraRotation,
    float2x2 objectRotation, float4 color, inout float4 outputColor)
{
    float2 projectedVertices[8];
    float vertexDepths[8];
    [unroll]
    for (int vertex = 0; vertex < 8; vertex++)
    {
        float3 position = VJAudioTransformCubeVertex(VJAudioCubeVertex(vertex), center, size, objectRotation);
        float3 projection = VJAudioCubeProjectWithDepth(position, cameraRotation);
        projectedVertices[vertex] = projection.xy;
        vertexDepths[vertex] = projection.z;
    }

    [unroll]
    for (int edge = 0; edge < 12; edge++)
    {
        int startIndex;
        int endIndex;
        VJAudioCubeEdge(edge, startIndex, endIndex);
        float averageDepth = (vertexDepths[startIndex] + vertexDepths[endIndex]) * 0.5;
        float lineDistance = VJAudioSegmentDistance(coordinate, projectedVertices[startIndex], projectedVertices[endIndex]);
        float thickness = 0.0015 / averageDepth;
        float core = smoothstep(thickness, 0.0, lineDistance);
        float glow = 0.1 * VJAudioFastGlow(lineDistance, 120.0);
        outputColor += color * (core + glow) / (1.0 + averageDepth * 0.5);
    }
}

void VJAudioDrawSubCube(float2 coordinate, float3 center, float3 size, float2x2 cameraRotation,
    float2x2 objectRotation, float4 color, float time, int seed, inout float4 outputColor)
{
    float traversalTime = time * 0.8 + (float)seed * 0.5;
    int edgeIndex = (int)fmod(floor(traversalTime), 12.0);
    int startIndex;
    int endIndex;
    VJAudioCubeEdge(edgeIndex, startIndex, endIndex);
    float3 start = VJAudioTransformCubeVertex(VJAudioCubeVertex(startIndex), center, size, objectRotation);
    float3 end = VJAudioTransformCubeVertex(VJAudioCubeVertex(endIndex), center, size, objectRotation);
    float3 subCubePosition = lerp(start, end, frac(traversalTime));
    float3 projection = VJAudioCubeProjectWithDepth(subCubePosition, cameraRotation);
    float2 projected = projection.xy;
    float depth = projection.z;
    float radius = size.x * 0.5 / depth;
    float boxDistance = VJAudioBoxDistance(coordinate - projected, radius);
    float core = smoothstep(0.004 / depth, 0.0, boxDistance);
    float glow = 0.3 * VJAudioFastGlow(boxDistance, 90.0);
    outputColor += color * 1.6 * (core + glow) / (1.0 + depth * 0.5);
}

float3 VJAudioFractalPalette(float value)
{
    return cos(value * 2.0 + float3(1.0, 2.0, 3.0)) * 0.5 + 0.3;
}

float2x2 VJAudioFractalRotation(float angle)
{
    return float2x2(cos(angle), cos(angle + 11.0), cos(angle + 33.0), cos(angle));
}

float4 VJAudioWireframeCubeFractal(float2 uv, float4 resolution, float time,
    float amount, float bpmStrength, float beatPulse, float beatPhase)
{
    float strength = max(VJFiniteScalar(bpmStrength), 0.0);
    float phase = frac(VJFiniteScalar(beatPhase));
    float pulse = saturate(VJFiniteScalar(beatPulse) * strength);
    float phaseAccent = pow(saturate(0.5 + 0.5 * cos(phase * VJ_TAU)), 12.0) * saturate(strength * 0.35);
    float beatAccent = max(pulse, phaseAccent);
    float colorAccentA = beatAccent;
    float colorAccentB = beatAccent * saturate(0.5 + 0.5 * sin(phase * VJ_TAU));
    float colorAccentC = beatAccent * saturate(0.5 + 0.5 * cos(phase * VJ_TAU));
    float bass = 1.0 + beatAccent * 1.2;
    float treble = 1.0 + beatAccent * 0.4;
    float2 pixel = uv * resolution.xy;
    float2 coordinate = (pixel - 0.5 * resolution.xy) / max(resolution.y, 1.0);
    float3 bounds = float3(0.5, 0.4, 0.5);
    float cubeRadius = 0.04 * bass;
    float2x2 cameraRotation = VJAudioCubeRotation(time * 0.2);
    float4 wireframe = 0.0;
    float3 positions[VJ_AUDIO_CUBE_COUNT];
    float2 projectedPositions[VJ_AUDIO_CUBE_COUNT];
    float depths[VJ_AUDIO_CUBE_COUNT];
    float4 colors[VJ_AUDIO_CUBE_COUNT];

    VJAudioDrawWireframeBox(coordinate, 0.0, bounds, cameraRotation, float2x2(1.0, 0.0, 0.0, 1.0),
        float4(0.1, 0.3, 0.6, 1.0), wireframe);

    [unroll]
    for (int cube = 0; cube < VJ_AUDIO_CUBE_COUNT; cube++)
    {
        float3 position = VJAudioCubePosition(cube, time, bounds - cubeRadius);
        positions[cube] = position;
        float3 projection = VJAudioCubeProjectWithDepth(position, cameraRotation);
        projectedPositions[cube] = projection.xy;
        depths[cube] = projection.z;
        float hue = (float)cube / (float)VJ_AUDIO_CUBE_COUNT * VJ_TAU + time * 0.2;
        float4 color = 0.5 + 0.5 * sin(hue + float4(0.0, 2.0, 4.0, 0.0));
        color.rgb = smoothstep(0.1, 0.9, color.rgb);
        if (color.r > 0.5) color.rgb *= 1.0 + colorAccentA * 1.5;
        if (color.g > 0.6) color.rb += colorAccentB * 1.8;
        if (color.b > 0.4) color.gb *= 1.0 + colorAccentC * 2.0;
        colors[cube] = color;
    }

    [unroll]
    for (int first = 0; first < VJ_AUDIO_CUBE_COUNT; first++)
    {
        [unroll]
        for (int second = first + 1; second < VJ_AUDIO_CUBE_COUNT; second++)
        {
            float distance3D = length(positions[first] - positions[second]);
            float maximumDistance = 0.5 + beatAccent * 0.15;
            if (distance3D < maximumDistance)
            {
                float lineDistance = VJAudioSegmentDistance(coordinate, projectedPositions[first], projectedPositions[second]);
                float alpha = smoothstep(maximumDistance, maximumDistance * 0.2, distance3D);
                float averageDepth = (depths[first] + depths[second]) * 0.5;
                float core = smoothstep(0.001 / averageDepth, 0.0, lineDistance);
                float glow = 0.08 * VJAudioFastGlow(lineDistance, 160.0) * alpha;
                float4 lineColor = (colors[first] + colors[second]) * 0.5;
                wireframe += lineColor * (core + glow) * treble / (1.0 + averageDepth * 0.8);
            }
        }
    }

    [unroll]
    for (int drawCube = 0; drawCube < VJ_AUDIO_CUBE_COUNT; drawCube++)
    {
        float2x2 objectRotation = VJAudioCubeRotation(time * 1.5 + (float)drawCube);
        VJAudioDrawWireframeBox(coordinate, positions[drawCube], cubeRadius, cameraRotation, objectRotation,
            colors[drawCube], wireframe);
        VJAudioDrawSubCube(coordinate, positions[drawCube], cubeRadius, cameraRotation, objectRotation,
            colors[drawCube], time, drawCube, wireframe);
    }

    float4 rayDirection = normalize(float4(pixel - 0.5 * resolution.xy, resolution.y, 0.3 * resolution.y)) * 50.0;
    rayDirection.xy = floor(rayDirection.xy * 3.0) / 3.0;
    rayDirection.xy = abs(rayDirection.xy) - 0.5;
    rayDirection.xy = mul(rayDirection.xy, VJAudioFractalRotation(0.785));
    rayDirection.xy = abs(rayDirection.xy) - 0.2;
    rayDirection.yz = mul(rayDirection.yz, VJAudioFractalRotation(0.5));
    rayDirection.xz = mul(rayDirection.xz, VJAudioFractalRotation(time / 7.0));

    float3 fractalColor = 0.0;
    float totalDistance = 0.0;
    [loop]
    for (int stepIndex = 0; stepIndex < VJ_AUDIO_FRACTAL_STEPS; stepIndex++)
    {
        float4 fractalPosition = rayDirection * totalDistance;
        float shell = length(fractalPosition) - 1.5;
        fractalPosition.z -= time / 11.0;
        fractalPosition.x += 2.0;
        fractalPosition.y -= time;
        fractalPosition = fractalPosition - 7.0 * floor((fractalPosition + 3.5) / 7.0);
        float scale = 2.0;
        [unroll]
        for (int fold = 0; fold < VJ_AUDIO_FRACTAL_FOLDS; fold++)
        {
            fractalPosition = abs(fractalPosition) * 0.95 - 0.25 * cos(fractalPosition * 0.5);
            fractalPosition.xw = mul(fractalPosition.xw, VJAudioFractalRotation(0.5));
            float inverseLength = clamp(1.0 / max(dot(fractalPosition, fractalPosition), 1.0e-8), 0.1, 6.0);
            scale *= inverseLength;
            fractalPosition = fractalPosition * inverseLength - 0.7;
        }
        float distanceToSurface = max(-shell, (length(fractalPosition.yzw) - 0.01) / max(scale, 1.0e-6));
        totalDistance += distanceToSurface / 12.0;
        if (distanceToSurface < 1.0e-9) break;
        if (stepIndex > 6)
            fractalColor += VJ_AUDIO_FRACTAL_ACCUMULATION * VJAudioFractalPalette(log(1.0 + scale))
                * VJAudioFastGlow(totalDistance, 10.0);
    }
    fractalColor = 1.0 - exp(-fractalColor * fractalColor);
    float mixAmount = lerp(0.35, 0.65, saturate(amount));
    float3 combined = lerp(wireframe.rgb, fractalColor, mixAmount) * 5.0 * (1.0 + beatAccent * 0.75);
    return VJFinite4(float4(combined, 1.0));
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

    if (variant == 30) // Wireframe Cube Fractal
    {
        return VJAudioWireframeCubeFractal(uv, resolution, safeTime, safeAmount, safeGain, safeBeat, phase);
    }

    // The family shader clamps the variant, but keep direct include callers
    // finite and visibly distinct from the last declared variant.
    return VJFinite4(float4(0.0, 0.0, 0.0, 1.0));
}

#endif
