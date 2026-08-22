Shader "Hidden/ShitDesigner/BuiltinBlend2"
{
    Properties { _TexA ("A", 2D) = "black" {} _TexB ("B", 2D) = "black" {} }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Overlay" }
        Pass
        {
            ZTest Always Cull Off ZWrite Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _TexA; sampler2D _TexB;
            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
            v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
            float4 frag(v2f i) : SV_Target { return lerp(tex2D(_TexA, i.uv), tex2D(_TexB, i.uv), 0.5); }
            ENDCG
        }
    }
}
