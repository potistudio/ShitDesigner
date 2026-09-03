Shader "ShitDesigner/Beat Freeze"
{
	Properties
	{
		_BaseMap ("Texture", 2D) = "white" {}
	}

	SubShader
	{
		Tags
		{
			"Queue" = "Geometry"
			"RenderType" = "Opaque"
			"RenderPipeline" = "UniversalPipeline"
		}

		Pass
		{
			Name "Beat Freeze"
			Tags { "LightMode" = "UniversalForward" }
			Cull Off
			ZTest Always
			ZWrite Off

			HLSLPROGRAM
			#pragma vertex Vertex
			#pragma fragment Fragment
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

			Varyings Vertex(Attributes input)
			{
				Varyings output;
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.uv = input.uv;
				return output;
			}

			half4 Fragment(Varyings input) : SV_Target
			{
				return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
			}
			ENDHLSL
		}
	}
}
