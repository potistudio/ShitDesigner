Shader "Hidden/ShitDesigner/HapYToLinear"
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
			float SrgbToLinear(float value)
			{
				return value <= 0.04045 ? value / 12.92 : pow((value + 0.055) / 1.055, 2.4);
			}
			v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
			float4 frag(v2f i) : SV_Target
			{
				float4 ycocg = tex2D(_MainTex, i.uv);
				float2 coCg = ycocg.rg - (0.5 * 256.0 / 255.0);
				float scale = ycocg.b * (255.0 / 8.0) + 1.0;
				float co = coCg.x / scale;
				float cg = coCg.y / scale;
				float y = ycocg.a;
				float3 rgb = saturate(float3(y + co - cg, y + cg, y - co - cg));
				rgb = float3(SrgbToLinear(rgb.r), SrgbToLinear(rgb.g), SrgbToLinear(rgb.b));
				return float4(rgb, 1.0);
			}
			ENDHLSL
		}
	}
}
