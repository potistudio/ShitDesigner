Shader "Hidden/ShitDesigner/LiveProgramFlash"
{
	Properties
	{
		_MainTex ("Source", 2D) = "white" {}
		_FlashAmount ("Flash Amount", Range(0, 1)) = 0
		_AssetFlashTexture ("Asset Flash Texture", 2D) = "black" {}
		_AssetFlashAmount ("Asset Flash Amount", Range(0, 1)) = 0
	}

	SubShader
	{
		Cull Off
		ZWrite Off
		ZTest Always

		Pass
		{
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			sampler2D _MainTex;
			float _FlashAmount;
			sampler2D _AssetFlashTexture;
			float _AssetFlashAmount;

			v2f vert(appdata input)
			{
				v2f output;
				output.vertex = UnityObjectToClipPos(input.vertex);
				output.uv = input.uv;
				return output;
			}

			fixed4 frag(v2f input) : SV_Target
			{
				fixed4 color = tex2D(_MainTex, input.uv);
				fixed4 asset = tex2D(_AssetFlashTexture, input.uv);
				color.rgb = lerp(color.rgb, 1.0, saturate(_FlashAmount));
				color.rgb = lerp(color.rgb, asset.rgb, saturate(asset.a * _AssetFlashAmount));
				return color;
			}
			ENDCG
		}
	}
}
