#ifndef SHITDESIGNER_VJ_MENGER_ORB_INCLUDED
#define SHITDESIGNER_VJ_MENGER_ORB_INCLUDED

float3 VJMengerOrbPath(float z)
{
	return VJFinite3(float3(cos(z * 0.05) * 16.0, cos(z * 0.1) * 8.0, z));
}

float2 VJMengerOrbRotate(float2 value, float angle)
{
	float diagonal = cos(angle);
	float upperRight = cos(angle + 11.0);
	float lowerLeft = cos(angle + 33.0);
	return float2(diagonal * value.x + upperRight * value.y, lowerLeft * value.x + diagonal * value.y);
}

float VJMengerOrbDistance(float3 position, float time)
{
	float3 path = VJMengerOrbPath(position.z);
	float3 center = float3(
		path.x + sin(position.z * 0.4) * 0.4,
		path.y + sin(sin(position.z * 0.3) + time) * 0.5,
		5.0 + time + tan(cos(time * 0.2) * 0.5) * 3.2);
	return length(position - center);
}

float VJMengerCrossDistance(float3 coordinate)
{
	return min(max(coordinate.x, coordinate.y), min(max(coordinate.y, coordinate.z), max(coordinate.x, coordinate.z)));
}

float VJMengerFractal(float3 coordinate)
{
	float3 cell;
	float scale = 4.0;
	float distanceValue = 9.0e9;

	cell = abs(frac(coordinate / scale) * scale - scale * 0.5);
	distanceValue = min(distanceValue, VJMengerCrossDistance(cell) - scale / 6.0);

	scale /= 4.0;
	cell = abs(frac(coordinate / scale) * scale - scale * 0.5);
	distanceValue = max(distanceValue, VJMengerCrossDistance(cell) - scale / 3.5);

	return distanceValue;
}

float VJMengerMap(float3 position, float time, inout float light)
{
	float3 original = position;
	position.xy -= VJMengerOrbPath(position.z).xy;
	position.y += 0.1;

	float structureDistance = max(1.0 - abs(position.x), 1.0 - abs(position.y));
	structureDistance = min(structureDistance, VJMengerFractal(position));

	float orbDistance = VJMengerOrbDistance(original, time) - 0.01;
	structureDistance = min(structureDistance, orbDistance);
	light += 1.0 / max(orbDistance, 0.001);
	return min(orbDistance, max(-original.y - 5.35, structureDistance));
}

float4 VJMengerOrbEvaluate(float2 uv, float2 resolution, float time, float maxSteps, float farDistance)
{
	float2 safeResolution = max(abs(VJFinite2(resolution)), float2(1.0, 1.0));
	float2 centered = (VJFinite2(uv) * safeResolution - safeResolution * 0.5) / safeResolution.y;

	float3 origin = VJMengerOrbPath(time);
	float3 forward = VJSafeNormalize3(VJMengerOrbPath(time + 3.0) - origin, float3(0.0, 0.0, 1.0));
	float3 right = VJSafeNormalize3(float3(forward.z, 0.0, -forward.x), float3(1.0, 0.0, 0.0));
	float3 up = VJSafeNormalize3(cross(right, forward), float3(0.0, 1.0, 0.0));
	float2 rotated = VJMengerOrbRotate(centered, sin(time * 0.2) * 0.3);
	float3 ray = VJSafeNormalize3(-right * rotated.x + up * rotated.y + forward, forward);

	int stepLimit = clamp((int)floor(VJFiniteScalar(maxSteps) + 0.5), 1, 256);
	float farClip = clamp(abs(VJFiniteScalar(farDistance)), 1.0, 500.0);
	float travelled = 0.001;
	float3 accumulated = 0.0;
	float light = 0.0;

	for (int stepIndex = 0; stepIndex < 256; stepIndex++)
	{
		if (stepIndex >= stepLimit || travelled >= farClip) break;
		float3 position = origin + ray * travelled;
		float mappedDistance = VJMengerMap(position, time, light);
		float marchStep = 0.01 + 0.65 * abs(mappedDistance);
		travelled += marchStep;
		float safeTravelled = max(travelled, 0.001);
		accumulated += float3(2.0, 10.0, 4.0) / max(marchStep, 0.001)
			+ 60.0 * float3(2.0, 1.0, 8.0) * light / safeTravelled;
	}

	float3 color = tanh(accumulated * accumulated / 4.0e8);
	return VJFinite4(float4(color, 1.0));
}

#endif
