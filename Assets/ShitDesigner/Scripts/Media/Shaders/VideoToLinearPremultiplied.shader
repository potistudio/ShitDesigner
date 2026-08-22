Shader "Hidden/ShitDesigner/VideoToLinearPremultiplied"
{
    SubShader
    {
        Cull Off ZWrite Off ZTest Always
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag
            #include "UnityCG.cginc"
            sampler2D _MainTex;
            float _ColorEncoding;
            float _AlphaMode;

            float ToLinear(float c)
            {
                return c <= 0.04045 ? c / 12.92 : pow((c + 0.055) / 1.055, 2.4);
            }

            fixed4 frag(v2f_img i) : SV_Target
            {
                float4 value = tex2D(_MainTex, i.uv);
                // Rec.709 and sRGB use the same transfer approximation here;
                // metadata still selects the explicit conversion path.
                if (_ColorEncoding < 2.0)
                    value.rgb = float3(ToLinear(value.r), ToLinear(value.g), ToLinear(value.b));
                if (_AlphaMode < 0.5)
                    value.a = 1.0;
                else if (_AlphaMode < 1.5)
                    value.rgb *= value.a;
                return value;
            }
            ENDCG
        }
    }
}
