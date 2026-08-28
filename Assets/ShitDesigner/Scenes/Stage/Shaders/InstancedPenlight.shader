Shader "ShitDesigner/Stage/Instanced Penlight"
{
	Properties
	{
		[HDR] _BaseColor ("Base Color", Color) = (0.1, 0.8, 1, 1)
		_GlowStrength ("Glow Strength", Range(0, 10)) = 3
		_RattleAngle ("Rattle Angle", Range(0, 90)) = 38
		_RattleSpeed ("Rattle Speed", Range(0, 24)) = 16
		_WristBounce ("Wrist Bounce", Range(0, 0.5)) = 0.08
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
			"Queue" = "Transparent"
			"RenderType" = "Transparent"
		}

		Pass
		{
			Name "InstancedPenlight"
			Tags { "LightMode" = "UniversalForward" }

			Blend One One
			Cull Off
			ZWrite Off

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma multi_compile_instancing

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
			half _GlowStrength;
			half _RattleAngle;
			half _RattleSpeed;
			half _WristBounce;
			CBUFFER_END

			UNITY_INSTANCING_BUFFER_START(PerInstance)
				UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
				UNITY_DEFINE_INSTANCED_PROP(float, _Phase)
			UNITY_INSTANCING_BUFFER_END(PerInstance)

			struct Attributes
			{
				float4 positionOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				half3 color : TEXCOORD0;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			float3 RotateX(float3 position, float angle)
			{
				float sine = sin(angle);
				float cosine = cos(angle);
				return float3(position.x, position.y * cosine - position.z * sine, position.y * sine + position.z * cosine);
			}

			float3 RotateZ(float3 position, float angle)
			{
				float sine = sin(angle);
				float cosine = cos(angle);
				return float3(position.x * cosine - position.y * sine, position.x * sine + position.y * cosine, position.z);
			}

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

				float phase = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Phase);
				float animationTime = _Time.y * _RattleSpeed + phase * 6.2831853f;
				float primarySwing = sin(animationTime);
				float bellShake = sign(primarySwing) * pow(abs(primarySwing), 0.45f);
				float wristFlick = sin(animationTime * 2f + phase * PI) * (1f - abs(primarySwing)) * 0.16f;
				float roll = sin(animationTime * 1.5f + phase * PI) * 0.13f;
				float angle = radians(_RattleAngle);
				float3 positionOS = RotateZ(RotateX(input.positionOS.xyz, (roll + wristFlick) * angle), (bellShake + wristFlick) * angle);
				positionOS.y += abs(primarySwing) * _WristBounce;
				output.positionHCS = TransformObjectToHClip(positionOS);

				half pulse = 0.82h + 0.18h * abs(primarySwing);
				output.color = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _BaseColor).rgb * (_GlowStrength * pulse);
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				return half4(input.color, 1);
			}
			ENDHLSL
		}
	}
}
