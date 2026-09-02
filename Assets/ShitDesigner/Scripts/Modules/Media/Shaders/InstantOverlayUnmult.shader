Shader "Hidden/ShitDesigner/InstantOverlayUnmult"
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

			v2f vert(appdata value) {
				v2f result;
				result.vertex = UnityObjectToClipPos(value.vertex);
				result.uv = value.uv;
				return result;
			}

			float4 frag(v2f input) : SV_Target {
				float3 premultipliedRgb = max(tex2D(_MainTex, input.uv).rgb, 0.0);
				float alpha = max(premultipliedRgb.r, max(premultipliedRgb.g, premultipliedRgb.b));
				return float4(premultipliedRgb, alpha);
			}
			ENDHLSL
		}
	}
}
