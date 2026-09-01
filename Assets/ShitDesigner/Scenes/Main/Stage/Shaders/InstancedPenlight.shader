Shader "ShitDesigner/Stage/Instanced Penlight"
{
	Properties
	{
		[HDR] _BaseColor ("Base Color", Color) = (0.1, 0.8, 1, 1)
		_GlowStrength ("Glow Strength", Range(0, 10)) = 3
		_RattleAngle ("Rattle Angle", Range(0, 30)) = 9
		_RattleIntensity ("Rattle Intensity", Range(0, 1)) = 0.85
		_DownstrokeDuration ("Downstroke Duration", Range(0.05, 0.5)) = 0.16
		_WristBounce ("Wrist Bounce", Range(0, 0.5)) = 0.06
		[HideInInspector] _BeatPosition ("Beat Position", Float) = 0
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
			half _RattleIntensity;
			half _DownstrokeDuration;
			half _WristBounce;
			float _BeatPosition;
			CBUFFER_END

			UNITY_INSTANCING_BUFFER_START(PerInstance)
				UNITY_DEFINE_INSTANCED_PROP(float4, _BaseColor)
				UNITY_DEFINE_INSTANCED_PROP(float, _Direction)
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
				float direction = UNITY_ACCESS_INSTANCED_PROP(PerInstance, _Direction);
				float animationTime = (_BeatPosition + phase) * 6.2831853f;
				float cycle = frac(animationTime * 0.15915494f);
				float downstrokeDuration = _DownstrokeDuration;
				float downstroke = smoothstep(0.0, 1.0, saturate(cycle / downstrokeDuration));
				float recovery = smoothstep(0.0, 1.0, saturate((cycle - downstrokeDuration) / (1.0 - downstrokeDuration)));
				float rattle = (cycle < downstrokeDuration ? 1.0 - 2.0 * downstroke : -1.0 + 2.0 * recovery) * _RattleIntensity;
				float directionRadians = direction * 6.2831853f;
				float directionX = sin(directionRadians);
				float directionZ = cos(directionRadians);
				float angle = radians(_RattleAngle);
				float3 positionOS = RotateZ(RotateX(input.positionOS.xyz, rattle * angle * directionX), rattle * angle * directionZ);
				positionOS.y += rattle * _WristBounce;
				output.positionHCS = TransformObjectToHClip(positionOS);

				half pulse = 0.82h + 0.18h * abs(rattle);
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
