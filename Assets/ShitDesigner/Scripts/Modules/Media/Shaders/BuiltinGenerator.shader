Shader "Hidden/ShitDesigner/BuiltinGenerator"
{
	// The graph's Color values are already Linear.  Declaring this as a
	// ShaderLab Color makes Unity's Material.SetColor apply the project's
	// sRGB-to-linear conversion a second time in a Linear project.  Keep the
	// binding a raw four-component vector so the shader receives the exact
	// value supplied by the runtime parameter snapshot.
	Properties { _Color ("Color", Vector) = (0, 0, 0, 1) }
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
			float4 _Color;
			struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
			struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
			v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
			float4 frag(v2f i) : SV_Target { return _Color; }
			ENDCG
		}
	}
}
