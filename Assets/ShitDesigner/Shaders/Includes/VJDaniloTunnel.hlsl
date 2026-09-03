#ifndef SHITDESIGNER_VJ_DANILO_TUNNEL_INCLUDED
#define SHITDESIGNER_VJ_DANILO_TUNNEL_INCLUDED

// Original shader by Danilo Guanabara: http://www.pouet.net/prod.php?which=57245
float4 VJDaniloTunnelEvaluate(float2 inputUv)
{
    float2 uv = VJSafeUV(inputUv);
    float2 resolution = max(abs(VJFinite2(_SD_Resolution.xy)), float2(1.0, 1.0));
    float2 coordinate = uv - 0.5;
    coordinate.x *= resolution.x / resolution.y;

    float radialDistance = max(length(coordinate), 1.0e-4);
    float phase = VJFiniteScalar(_SD_Time);
    float3 channelIntensity = 0.0;

    for (int channel = 0; channel < 3; channel++)
    {
        phase += 0.07;
        float2 warpedUv = uv + coordinate / radialDistance
            * (sin(phase) + 1.0)
            * abs(sin(radialDistance * 9.0 - phase - phase));
        float tileDistance = length(frac(warpedUv) - 0.5);
        float intensity = 0.01 / max(tileDistance, 1.0e-4);

        if (channel == 0) channelIntensity.x = intensity;
        else if (channel == 1) channelIntensity.y = intensity;
        else channelIntensity.z = intensity;
    }

    float3 color = channelIntensity / radialDistance;
    return VJFinite4(float4(VJFinite3(color), 1.0));
}

#endif
