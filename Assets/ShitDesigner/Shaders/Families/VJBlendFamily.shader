Shader "Hidden/ShitDesigner/VJ/BlendFamily"
{
    Properties
    {
        _SD_Time ("Graph Clock Time", Float) = 0
        _SD_DeltaTime ("Graph Delta Time", Float) = 0
        _SD_Frame ("Graph Frame", Float) = 0
        _SD_Resolution ("Graph Resolution", Vector) = (1920,1080,0,0)
        _SD_Seed ("Deterministic Seed", Float) = 0
        _SD_BeatPhase ("Beat Phase", Float) = 0
        _SD_BeatPulse ("Beat Pulse", Float) = 0
        _SD_BarPhase ("Bar Phase", Float) = 0
        _SD_Pointer ("Pointer", Vector) = (0.5,0.5,0,0)
        [NoScaleOffset] _TexA ("Input A (premultiplied)", 2D) = "black" {}
        [NoScaleOffset] _TexB ("Input B (premultiplied)", 2D) = "black" {}
        [NoScaleOffset] _MaskTex ("External Mask", 2D) = "white" {}
        [NoScaleOffset] _DepthTexA ("Depth A", 2D) = "black" {}
        [NoScaleOffset] _DepthTexB ("Depth B", 2D) = "white" {}
        _Variant ("Blend Variant", Float) = 0
        _Amount ("Blend Amount", Range(0, 1)) = 1
        _ExternalMask ("External Mask Amount", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        Pass
        {
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VJBlendVertex
            #pragma fragment VJBlendFragment
            #include "UnityCG.cginc"
            #include "Assets/ShitDesigner/Shaders/Includes/VJBlend.hlsl"

            sampler2D _TexA;
            sampler2D _TexB;
            sampler2D _MaskTex;
            sampler2D _DepthTexA;
            sampler2D _DepthTexB;
            float _Variant;
            float _Amount;
            float _ExternalMask;

            struct VJBlendAttributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct VJBlendVaryings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            VJBlendVaryings VJBlendVertex(VJBlendAttributes input)
            {
                VJBlendVaryings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            float4 VJBlendFragment(VJBlendVaryings input) : SV_Target
            {
                float2 uv = saturate(input.uv);
                float4 rawA = VJSample2D(_TexA, uv);
                float amount = saturate(VJFiniteScalar(_Amount));
                // Preserve the input bit pattern at the first endpoint.  It
                // is useful for graph bypasses and avoids an unnecessary
                // premultiply/unpremultiply round trip.
                if (amount <= 0.0) return rawA;

                float4 rawB = VJSample2D(_TexB, uv);
                float externalMask = VJSample2D(_MaskTex, uv).r * saturate(_ExternalMask);
                float depthA = VJSample2D(_DepthTexA, uv).r;
                float depthB = VJSample2D(_DepthTexB, uv).r;
                int variant = clamp((int)floor(_Variant + 0.5), 0, 35);
                float4 result = VJBlendEvaluate(variant, rawA, rawB, amount, externalMask, depthA, depthB);
                return VJPremultiply(result);
            }
            ENDHLSL
        }
    }
}
