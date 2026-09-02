Shader "Hidden/ShitDesigner/Main/InstantFlash"
{
	Properties
	{
		_MainTex ("Input", 2D) = "black" {}
		_FlashTime ("Flash Time", Float) = 0
		_Amount ("Amount", Range(0, 1)) = 1
		_StrobeRate ("Strobe Rate", Float) = 12
		_Duty ("Duty", Range(0.05, 0.95)) = 0.35
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }
		Pass
		{
			ZTest Always Cull Off ZWrite Off
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
			struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

			sampler2D _MainTex;
			float _FlashTime;
			float _Amount;
			float _StrobeRate;
			float _Duty;

			v2f vert(appdata input)
			{
				v2f output;
				output.vertex = UnityObjectToClipPos(input.vertex);
				output.uv = input.uv;
				return output;
			}

			float4 frag(v2f input) : SV_Target
			{
				float4 source = tex2D(_MainTex, input.uv);
				float phase = frac(max(_FlashTime, 0.0) * max(_StrobeRate, 0.01));
				float pulse = 1.0 - step(saturate(_Duty), phase);
				float flashPhase = phase / max(_Duty, 0.01);
				float3 inverted = 1.0.xxx - source.rgb;
				float luminance = dot(inverted, float3(0.2126, 0.7152, 0.0722));
				float3 flash = flashPhase < 1.0 / 3.0 ? inverted
					: flashPhase < 2.0 / 3.0 ? luminance.xxx
					: 1.0.xxx;
				float3 color = lerp(source.rgb, flash, saturate(_Amount) * pulse);
				return float4(color, source.a);
			}
			ENDCG
		}
	}
}
