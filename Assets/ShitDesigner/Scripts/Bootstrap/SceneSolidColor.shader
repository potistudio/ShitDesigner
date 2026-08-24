Shader "Hidden/ShitDesigner/SceneSolidColor"
{
	Properties
	{
		_BaseColor ("Color", Color) = (1, 1, 1, 1)
	}
	SubShader
	{
		Tags
		{
			"RenderType" = "Opaque"
			"Queue" = "Geometry"
			"RenderPipeline" = "UniversalPipeline"
		}

		// This material is deliberately unlit.  SRPDefaultUnlit is the
		// forward-only pass name URP accepts in Forward, Forward+ and
		// Deferred renderer assets.
		Pass
		{
			Name "SRPDefaultUnlit"
			Tags { "LightMode" = "SRPDefaultUnlit" }
			ZTest LEqual
			ZWrite On
			Cull Off

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
			half4 _BaseColor;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
				return output;
			}

			half4 Frag(Varyings input) : SV_Target
			{
				return _BaseColor;
			}
			ENDHLSL
		}
	}
}
