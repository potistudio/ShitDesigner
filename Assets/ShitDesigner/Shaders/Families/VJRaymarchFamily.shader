Shader "Hidden/ShitDesigner/VJ/RaymarchFamily"
{
	Properties
	{
		_SD_Time ("Graph Clock Time", Float) = 0
		_SD_DeltaTime ("Graph Delta Time", Float) = 0
		_SD_Frame ("Graph Frame", Float) = 0
		_SD_Resolution ("Graph Resolution", Vector) = (1920, 1080, 0, 0)
		_SD_Seed ("Deterministic Seed", Float) = 0
		_SD_BeatPhase ("Beat Phase", Float) = 0
		_SD_BeatPulse ("Beat Pulse", Float) = 0
		_SD_BarPhase ("Bar Phase", Float) = 0
		_SD_Pointer ("Pointer", Vector) = (0.5, 0.5, 0, 0)
		_Variant ("Raymarch Variant", Float) = 0
		_Steps ("Maximum Steps", Range(1, 256)) = 96
		_Epsilon ("Hit Epsilon", Range(0.0001, 0.1)) = 0.001
		_FarDistance ("Far Distance", Float) = 30
		_CameraPosition ("Camera Position", Vector) = (0, 0, 3, 0)
		_CameraTarget ("Camera Target", Vector) = (0, 0, 0, 0)
		_Fov ("Field Of View", Range(10, 160)) = 55
		_LightDirection ("Light Direction", Vector) = (0.4, 0.7, 0.6, 0)
		_AudioRms ("Audio RMS", Range(0, 1)) = 0
		_GraphTime ("Graph Clock Time", Float) = 0
		_Frame ("Frame", Float) = 0
		_Fog ("Fog", Range(0, 1)) = 0.15
		_AmbientOcclusion ("Ambient Occlusion", Range(0, 1)) = 0.35
		_Resolution ("Resolution", Vector) = (1920, 1080, 0, 0)
	}

	SubShader
	{
		Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
		Cull Off
		ZWrite Off
		ZTest Always
		Blend One Zero

		Pass
		{
			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex VJRaymarchVertex
			#pragma fragment VJRaymarchFragment
			#include "UnityCG.cginc"
			#include "Assets/ShitDesigner/Shaders/Includes/VJRaymarch.hlsl"

			float _Variant;
			float _Steps;
			float _Epsilon;
			float _FarDistance;
			float4 _CameraPosition;
			float4 _CameraTarget;
			float _Fov;
			float4 _LightDirection;
			float _AudioRms;
			float _GraphTime;
			float _Frame;
			float _Fog;
			float _AmbientOcclusion;
			float4 _Resolution;

			struct VJRaymarchAttributes
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct VJRaymarchVaryings
			{
				float4 position : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			VJRaymarchVaryings VJRaymarchVertex(VJRaymarchAttributes input)
			{
				VJRaymarchVaryings output;
				output.position = UnityObjectToClipPos(input.vertex);
				output.uv = input.uv;
				return output;
			}

			float3 VJRaymarchNormalize(float3 value, float3 fallback)
			{
				float lengthValue = length(value);
				return lengthValue > 1.0e-5 && lengthValue == lengthValue ? value / lengthValue : fallback;
			}

			float3 VJRaymarchPalette(float value)
			{
				return VJHSVToRGB(float3(frac(value * 0.22 + 0.58), 0.72, saturate(0.28 + value * 0.72)));
			}

			float4 VJRaymarchFragment(VJRaymarchVaryings input) : SV_Target
			{
				float2 centered = input.uv * 2.0 - 1.0;
				float4 graphResolution = VJFinite4(_SD_Resolution);
				float aspect = max(graphResolution.x / max(graphResolution.y, 1.0), 1.0e-4);
				centered.x *= aspect;
				float3 camera = VJFinite3(_CameraPosition.xyz);
				float3 target = VJFinite3(_CameraTarget.xyz);
				float3 forward = VJRaymarchNormalize(target - camera, float3(0.0, 0.0, -1.0));
				float3 upReference = abs(forward.y) > 0.98 ? float3(0.0, 0.0, 1.0) : float3(0.0, 1.0, 0.0);
				float3 right = VJRaymarchNormalize(cross(forward, upReference), float3(1.0, 0.0, 0.0));
				float3 up = VJRaymarchNormalize(cross(right, forward), float3(0.0, 1.0, 0.0));
				float tangent = tan(radians(clamp(_Fov, 10.0, 160.0)) * 0.5);
				float3 direction = VJRaymarchNormalize(forward + right * centered.x * tangent + up * centered.y * tangent, forward);
				int variant = clamp((int)floor(_Variant + 0.5), 0, 29);
				VJRaymarchHit hit = VJRaymarchTrace(variant, camera, direction, (int)floor(_Steps + 0.5), _Epsilon,
				_FarDistance, _AudioRms, VJFiniteScalar(_SD_Time) + VJFiniteScalar(_SD_Frame) * 0.0);
				float3 background = lerp(float3(0.004, 0.006, 0.012), float3(0.03, 0.06, 0.12), saturate(centered.y * 0.5 + 0.5));
				if (hit.hit < 0.5) return VJFinite4(float4(background, 1.0));

					float3 light = VJRaymarchNormalize(_LightDirection.xyz, float3(0.4, 0.7, 0.6));
				float diffuse = saturate(dot(hit.normal, light));
				float rim = pow(saturate(1.0 - dot(hit.normal, -direction)), 2.0);
				float ao = lerp(1.0, saturate(1.0 - hit.distance / max(_FarDistance, 0.1)), saturate(_AmbientOcclusion));
				float3 color = VJRaymarchPalette(variant * 0.17 + hit.distance * 0.04) * (0.15 + diffuse * 0.85) * ao + rim * 0.25;
				float fog = exp( -max(hit.distance, 0.0) * saturate(_Fog));
				return VJFinite4(float4(lerp(background, color, fog), 1.0));
			}
			ENDHLSL
		}
	}
}
