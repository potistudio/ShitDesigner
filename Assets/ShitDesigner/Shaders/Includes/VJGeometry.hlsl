#ifndef SHITDESIGNER_VJ_GEOMETRY_INCLUDED
#define SHITDESIGNER_VJ_GEOMETRY_INCLUDED

float2 VJGeometryMirror(float2 uv)
{
    float2 value = frac(uv);
    return 1.0 - abs(value * 2.0 - 1.0);
}

float2 VJGeometryWarp(float2 inputUv, int variant, sampler2D displacementSampler)
{
    float2 uv = VJSafeUV(inputUv);
    float2 center = _VJCenter.xy;
    if (center.x == 0.0 && center.y == 0.0) center = float2(0.5, 0.5);
    float2 p = uv - center;
    float2 aspectPoint = p;
    aspectPoint.x *= max(_VJAspect, 0.001);
    float amount = VJFiniteScalar(_VJAmount);
    float frequency = max(abs(VJFiniteScalar(_VJFrequency)), 0.01);
    float detail = max(abs(VJFiniteScalar(_VJDetail)), 2.0);
    float time = VJFiniteScalar(_SD_Time * _VJSpeed + _VJPhase);
    float radius = length(aspectPoint);
    float angle = atan2(aspectPoint.y, aspectPoint.x);

    switch (variant)
    {
        case 0: // Transform 2D
            p = VJRotate(p * max(abs(_VJScale), 0.001), _VJAngle);
            uv = p + center + _VJDisplacement.xy;
            break;
        case 1: // Crop
            uv = center + p / max(abs(_VJScale), 0.001);
            uv = saturate((uv - _VJPivot.xy) / max(abs(_VJRadius), 0.001) + _VJPivot.xy);
            break;
        case 2: // Tile
            uv = frac((uv - center) * max(abs(_VJTile), 1.0) + center);
            break;
        case 3: // Mirror X/Y
            uv = VJGeometryMirror((uv - center) * 2.0 + center);
            break;
        case 4: // Quad Mirror
            uv = 0.5 + abs(uv - 0.5);
            uv = frac(uv * max(abs(_VJTile), 1.0));
            break;
        case 5: // Kaleidoscope
        {
            float sectors = max(floor(frequency), 2.0);
            float folded = abs(frac(angle / VJ_TAU * sectors) * 2.0 - 1.0);
            float newAngle = folded * VJ_PI / sectors;
            uv = center + float2(cos(newAngle), sin(newAngle)) * radius;
            break;
        }
        case 6: // Polar Coordinates
            uv = float2(angle / VJ_TAU + 0.5, radius * 1.5);
            break;
        case 7: // Cartesian From Polar
        {
            float theta = (uv.x - 0.5) * VJ_TAU;
            float r = (uv.y - 0.5) * 2.0;
            uv = center + float2(cos(theta), sin(theta)) * r * 0.5;
            break;
        }
        case 8: // Ripple
            uv += VJSafeNormalize2(p + 1.0e-5, float2(1.0, 0.0)) * sin(radius * frequency * 20.0 - time * 3.0) * amount * 0.03;
            break;
        case 9: // Sine Wave
            uv.x += sin(uv.y * frequency * VJ_TAU + time) * amount * 0.05;
            uv.y += sin(uv.x * frequency * VJ_TAU - time) * amount * 0.05;
            break;
        case 10: // Twirl
            uv = center + VJRotate(p, amount * (1.0 - saturate(radius)) + time * 0.02);
            break;
        case 11: // Bulge
            uv = center + p * (1.0 - amount * exp(-radius * radius * 4.0));
            break;
        case 12: // Pinch
            uv = center + p * (1.0 + amount * exp(-radius * radius * 4.0));
            break;
        case 13: // Fisheye
        {
            float r = max(radius, 1.0e-4);
            uv = center + p * tan(r * (1.0 + amount)) / max(tan(1.0 + amount), 1.0e-4) / r;
            break;
        }
        case 14: // Lens Distortion
            uv = center + p * (1.0 + amount * radius * radius + _VJFalloff * radius * radius * radius);
            break;
        case 15: // Displacement Map
        {
            float4 displacement = VJUnpremultiply(VJSample2D(displacementSampler, uv));
            uv += (displacement.rg * 2.0 - 1.0) * _VJDisplacement.xy * amount;
            break;
        }
        case 16: // Perspective/Keystone
        {
            float top = max(abs(_VJScale), 0.1);
            float bottom = max(abs(_VJRadius), 0.1);
            float scaleY = lerp(top, bottom, uv.y);
            uv.x = center.x + (uv.x - center.x) / scaleY;
            uv.y += (uv.x - center.x) * amount * 0.25;
            break;
        }
        case 17: // Four-corner Pin
        {
            float2 top = lerp(_VJColorA.xy, _VJColorB.xy, uv.x);
            float2 bottom = lerp(_VJColorC.xy, _VJPivot.xy, uv.x);
            uv = lerp(top, bottom, uv.y);
            break;
        }
        case 18: // Bend
            uv.y += sin(uv.x * VJ_PI) * amount * 0.15;
            break;
        case 19: // Arch
            uv.y += (uv.x - center.x) * (uv.x - center.x) * amount;
            break;
        case 20: // Spherize
        {
            float r2 = dot(p, p);
            uv = center + p * (1.0 - saturate(r2) * amount * 0.5);
            break;
        }
        case 21: // Cylinder Wrap
            uv.x = center.x + sin((uv.x - center.x) * amount + _VJAngle) * 0.5;
            break;
        case 22: // Mobius Wrap
        {
            float twist = VJ_TAU * (uv.x - center.x);
            uv = center + float2(sin(twist) * 0.5 + p.y * cos(twist), p.y);
            break;
        }
        case 23: // Droste
        {
            float logRadius = log(max(radius, 0.01));
            float wrapped = frac(logRadius * max(frequency, 0.1) + time * 0.03);
            uv = center + VJSafeNormalize2(p + 1.0e-5, float2(1.0, 0.0)) * exp(lerp(-1.2, 0.0, wrapped));
            break;
        }
        case 24: // Infinite Zoom
        {
            float zoom = frac(time * 0.08) * 0.5 + 0.5;
            uv = center + p / max(zoom, 0.05);
            break;
        }
        case 25: // Tunnel Warp
        {
            float inv = 1.0 / max(radius, 0.05);
            uv = center + p * inv * max(_VJScale, 0.1);
            uv += float2(time * 0.08, time * 0.04);
            break;
        }
        case 26: // Swirl Tunnel
        {
            float inv = 1.0 / max(radius, 0.05);
            uv = center + VJRotate(p * inv, angle * amount + time * 0.1);
            break;
        }
        case 27: // Flow Map
        {
            float4 flow = VJUnpremultiply(VJSample2D(displacementSampler, uv));
            uv += (flow.rg * 2.0 - 1.0) * amount * _SD_DeltaTime;
            break;
        }
        case 28: // Noise Warp
        {
            float2 noise = float2(VJValueNoise(uv * frequency + time), VJValueNoise(uv * frequency + 17.0 - time));
            uv += (noise - 0.5) * amount * 0.15;
            break;
        }
        case 29: // FBM Warp
        {
            float2 noise = float2(VJFBM(uv * frequency + time * 0.03, 5), VJFBM(uv * frequency + 21.0 - time * 0.03, 5));
            uv += (noise - 0.5) * amount * 0.2;
            break;
        }
        case 30: // Voronoi Warp
        {
            float2 cell = floor(uv * frequency);
            float2 nearest = frac(uv * frequency) - 0.5;
            float2 feature = float2(VJHash12(cell), VJHash12(cell + 11.0)) - 0.5;
            uv += (feature - nearest) * amount * 0.06;
            break;
        }
        case 31: // Pixel Sort Warp
        {
            float2 pixel = floor(uv * max(frequency * 12.0, 2.0)) / max(frequency * 12.0, 2.0);
            float luminance = VJLuma(VJUnpremultiply(VJSample2D(displacementSampler, pixel)).rgb);
            uv.x += step(_VJThreshold, luminance) * amount * 0.08;
            break;
        }
        case 32: // Block Displace
        {
            float2 block = floor(uv * max(detail, 2.0));
            float2 offset = float2(VJHash12(block + _SD_Frame), VJHash12(block + 19.0 + _SD_Frame)) - 0.5;
            uv += offset * amount * 0.12;
            break;
        }
        case 33: // Slice Offset
        {
            float slice = floor(uv.y * max(detail, 2.0));
            uv.x += (VJHash11(slice + floor(time * 4.0) + _SD_Seed) - 0.5) * amount * 0.12;
            break;
        }
        case 34: // Scanline Displace
            uv.x += sin(uv.y * frequency * 100.0 + time * 5.0) * amount * 0.02;
            break;
        case 35: // RGB Channel Displace
            uv += float2(amount * 0.015, -amount * 0.01);
            break;
        case 36: // Mesh Warp Grid
        {
            float2 grid = floor(uv * max(detail, 2.0));
            float2 local = frac(uv * max(detail, 2.0));
            float2 bend = float2(sin((grid.y + local.y) * 0.7 + time), cos((grid.x + local.x) * 0.8 - time));
            uv += bend * amount * 0.03 / max(detail, 2.0);
            break;
        }
        case 37: // Thin Plate Spline Warp
        {
            float2 knot = _VJColorA.xy;
            float d = length(uv - knot);
            uv += VJSafeNormalize2(uv - knot + 1.0e-5, float2(1.0, 0.0)) * exp(-d * max(_VJFalloff, 0.1)) * amount * 0.06;
            break;
        }
        case 38: // Bezier Patch Warp
        {
            float curve = (1.0 - uv.x) * (1.0 - uv.x) * _VJColorA.y + 2.0 * (1.0 - uv.x) * uv.x * _VJColorB.y + uv.x * uv.x * _VJColorC.y;
            uv.y = lerp(uv.y, curve, saturate(abs(amount)));
            break;
        }
        case 39: // Optical-flow Warp
        {
            float4 flow = VJUnpremultiply(VJSample2D(displacementSampler, uv));
            float2 velocity = flow.rg * 2.0 - 1.0;
            uv += velocity * amount * max(_SD_DeltaTime, 0.016);
            break;
        }
        case 40: // Datamosh Motion Warp
        {
            float2 block = floor(uv * max(detail, 2.0));
            float2 offsetVector = float2(VJHash12(block + floor(time * 2.0)), VJHash12(block + 7.0 + floor(time * 2.0))) - 0.5;
            uv += offsetVector * amount * 0.1;
            break;
        }
        case 41: // Fluid Advection Warp
        {
            float2 flow = float2(VJFBM(uv * frequency + time * 0.02, 4), VJFBM(uv.yx * frequency - time * 0.02, 4)) - 0.5;
            uv += flow * amount * max(_SD_DeltaTime, 0.016);
            break;
        }
        default:
            break;
    }
    return VJSafeUV(uv);
}

float4 VJGeometryEvaluate(sampler2D textureSampler, sampler2D displacementSampler, float2 uv, int variant)
{
    if (variant == 35)
    {
        float2 offset = float2(_VJAmount, _VJAmount * 0.7) * 0.02;
        float4 red = VJUnpremultiply(VJSample2D(textureSampler, VJGeometryWarp(uv + offset, variant, displacementSampler)));
        float4 green = VJUnpremultiply(VJSample2D(textureSampler, VJGeometryWarp(uv, variant, displacementSampler)));
        float4 blue = VJUnpremultiply(VJSample2D(textureSampler, VJGeometryWarp(uv - offset, variant, displacementSampler)));
        return VJPremultiply(float4(red.r, green.g, blue.b, green.a));
    }
    float2 warped = VJGeometryWarp(uv, variant, displacementSampler);
    return VJFinite4(VJSample2D(textureSampler, warped));
}

#endif
