Shader "Hidden/ShitDesigner/HapPremultiply"
{
	SubShader
	{
		Cull Off ZWrite Off ZTest Always
		Pass
		{
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"
			struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
			struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
			sampler2D _MainTex;
			v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
			float LinearFromSrgb(float value)
			{
				return value <= 0.04045 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4);
			}
			float4 frag(v2f i) : SV_Target
			{
				float4 straight = tex2D(_MainTex, i.uv);
				float alpha = saturate(straight.a);
				return float4(LinearFromSrgb(straight.r) * alpha,
				LinearFromSrgb(straight.g) * alpha,
				LinearFromSrgb(straight.b) * alpha, alpha);
			}
			ENDHLSL
		}
	}
}
