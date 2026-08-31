Shader "ShitDesigner/Asset Flush"
{
	Properties
	{
		_BaseMap ("Texture", 2D) = "white" {}
	}

	SubShader
	{
		Tags
		{
			"Queue" = "Transparent"
			"RenderType" = "Transparent"
			"RenderPipeline" = "UniversalPipeline"
		}

		Pass
		{
			Name "Asset Flush"
			Tags { "LightMode" = "UniversalForward" }
			Blend One OneMinusSrcAlpha
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

			CBUFFER_START(UnityPerMaterial)
				float4 _BaseMap_ST;
			CBUFFER_END

			Varyings Vertex(Attributes input)
			{
				Varyings output;
				output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
				output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
				return output;
			}

			half4 Fragment(Varyings input) : SV_Target
			{
				half4 color = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
				return half4(color.rgb * color.a, color.a);
			}
			ENDHLSL
		}
	}
}
