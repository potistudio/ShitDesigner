Shader "ShitDesigner/Stage/Volumetric Lighting"
{
	Properties
	{
		[HDR] _Color ("Color", Color) = (1, 1, 1, 1)
		_Intensity ("Intensity", Range(0, 20)) = 2
		_Density ("Density", Range(0, 1)) = 0.35
		_BeamAngle ("Beam Angle", Range(1, 120)) = 30
		_BeamDistance ("Beam Distance", Range(0.1, 100)) = 10
		_EdgeSoftness ("Edge Softness", Range(0.1, 8)) = 2
		_StartFade ("Start Fade", Range(0, 1)) = 0.05
		_EndFade ("End Fade", Range(0, 1)) = 0.2
		_DepthFadeDistance ("Depth Fade Distance", Range(0.001, 5)) = 0.5
		[NoScaleOffset] _NoiseMap ("Noise", 2D) = "white" {}
		_NoiseScale ("Noise Scale", Range(0.01, 10)) = 1
		_NoiseStrength ("Noise Strength", Range(0, 1)) = 0
		_NoiseSpeed ("Noise Speed", Vector) = (0, 0.2, 0, 0)
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Transparent"
			"Queue" = "Transparent+10"
			"RenderPipeline" = "UniversalPipeline"
		}

		Pass
		{
			Name "VolumetricLighting"
			Tags { "LightMode" = "UniversalForwardOnly" }

			Blend SrcAlpha One
			ColorMask RGB
			Cull Back
			ZTest LEqual
			ZWrite Off

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex Vert
			#pragma fragment Frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

			TEXTURE2D(_NoiseMap);
			SAMPLER(sampler_NoiseMap);

			CBUFFER_START(UnityPerMaterial)
			half4 _Color;
			half _Intensity;
			half _Density;
			half _BeamAngle;
			half _BeamDistance;
			half _EdgeSoftness;
			half _StartFade;
			half _EndFade;
			half _DepthFadeDistance;
			half _NoiseScale;
			half _NoiseStrength;
			float4 _NoiseSpeed;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				half3 normalWS : TEXCOORD1;
				half beamProgress : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				half beamProgress = saturate(0.5h - input.positionOS.y);
				half halfAngleTangent = tan(radians(_BeamAngle * 0.5h));
				float2 radialDirection = input.positionOS.xz / max(length(input.positionOS.xz), 0.0001);
				float3 positionOS = float3(
					radialDirection.x * beamProgress * _BeamDistance * halfAngleTangent,
					-beamProgress * _BeamDistance,
					radialDirection.y * beamProgress * _BeamDistance * halfAngleTangent);
				half3 normalOS = normalize(half3(radialDirection.x, halfAngleTangent, radialDirection.y));

				VertexPositionInputs positionInputs = GetVertexPositionInputs(positionOS);
				VertexNormalInputs normalInputs = GetVertexNormalInputs(normalOS);
				output.positionHCS = positionInputs.positionCS;
				output.positionWS = positionInputs.positionWS;
				output.normalWS = normalInputs.normalWS;
				output.beamProgress = beamProgress;
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

				half3 viewDirectionWS = SafeNormalize(GetWorldSpaceViewDir(input.positionWS));
				half viewFacing = abs(dot(normalize(input.normalWS), viewDirectionWS));
				half edgeFade = pow(saturate(viewFacing), _EdgeSoftness);

				half startFade = smoothstep(0, max(_StartFade, 0.0001h), input.beamProgress);
				half endFade = 1 - smoothstep(1 - max(_EndFade, 0.0001h), 1, input.beamProgress);

				float2 noiseOffset = _Time.y * _NoiseSpeed.xy;
				float2 noiseUV = input.positionWS.xz * _NoiseScale + noiseOffset;
				half noise = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, noiseUV).r;
				half noiseAttenuation = lerp(1, noise, _NoiseStrength);

				float2 screenUV = GetNormalizedScreenSpaceUV(input.positionHCS);
				float sceneDepth = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
				float fragmentDepth = -TransformWorldToView(input.positionWS).z;
				half depthFade = saturate((sceneDepth - fragmentDepth) / _DepthFadeDistance);

				half opacity = saturate(_Color.a * _Density * edgeFade * startFade * endFade * noiseAttenuation * depthFade);
				return half4(_Color.rgb * _Intensity, opacity);
			}
			ENDHLSL
		}
	}

	Fallback Off
}
