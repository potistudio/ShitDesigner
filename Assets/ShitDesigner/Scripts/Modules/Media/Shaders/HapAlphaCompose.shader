Shader "Hidden/ShitDesigner/HapAlphaCompose"
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
			sampler2D _AlphaTex;
			v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
			float4 frag(v2f i) : SV_Target
			{
				float4 color = tex2D(_MainTex, i.uv);
				float alpha = tex2D(_AlphaTex, i.uv).r;
				return float4(color.rgb * alpha, alpha);
			}
			ENDHLSL
		}
	}
}
