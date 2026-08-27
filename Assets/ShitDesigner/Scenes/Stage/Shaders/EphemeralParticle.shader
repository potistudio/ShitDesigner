Shader "ShitDesigner/Stage/Ephemeral Particle"
{
	Properties
	{
		[HDR] _BaseColor ("Base Color", Color) = (0.5, 0.8, 1, 0.9)
		_Softness ("Softness", Range(0.01, 1)) = 0.65
	}

	SubShader
	{
		Tags
		{
			"Queue" = "Transparent"
			"RenderType" = "Transparent"
			"RenderPipeline" = "UniversalPipeline"
			"IgnoreProjector" = "True"
		}

		Pass
		{
			Blend SrcAlpha One
			Cull Off
			ZWrite Off
			ZTest LEqual

			HLSLPROGRAM
			#pragma target 2.0
			#pragma vertex Vert
			#pragma fragment Frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
			half4 _BaseColor;
			half _Softness;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
				float4 color : COLOR;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float2 uv : TEXCOORD0;
				float4 color : COLOR;
				UNITY_VERTEX_OUTPUT_STEREO
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
				output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
				output.uv = input.uv;
				output.color = input.color;
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float distanceFromCenter = length(input.uv * 2 - 1);
				float edge = max(_Softness, 0.01);
				half alpha = 1 - smoothstep(1 - edge, 1, distanceFromCenter);
				return half4(_BaseColor.rgb * input.color.rgb, _BaseColor.a * input.color.a * alpha);
			}
			ENDHLSL
		}
	}

	Fallback Off
}
