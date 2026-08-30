Shader "Hidden/ShitDesigner/ProgramDisplay" {
	Properties {
		_MainTex("Program", 2D) = "black" {}
	}
	SubShader {
		Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Overlay" "RenderType" = "Opaque" }
		Pass {
			Cull Off
			ZTest Always
			ZWrite Off

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			struct Attributes {
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Varyings {
				float4 positionHCS : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			TEXTURE2D(_MainTex);
			SAMPLER(sampler_MainTex);

			Varyings Vert(Attributes input) {
				Varyings output;
				output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
				output.uv = input.uv;
				return output;
			}

			half4 Frag(Varyings input) : SV_Target {
				return SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
			}
			ENDHLSL
		}
	}
}
