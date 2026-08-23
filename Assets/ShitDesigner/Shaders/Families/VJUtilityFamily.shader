Shader "Hidden/ShitDesigner/VJ/UtilityFamily"
{
    Properties
    {
        _SD_Time ("Graph Clock Time", Float) = 0
        _SD_DeltaTime ("Graph Delta Time", Float) = 0
        _SD_Frame ("Graph Frame", Float) = 0
        _SD_Resolution ("Graph Resolution", Vector) = (1920,1080,0,0)
        _SD_Seed ("Deterministic Seed", Float) = 0
        _SD_PassIndex ("Graph Pass Index", Float) = 0
        _SD_PassCount ("Graph Pass Count", Float) = 1
        _SD_BeatPhase ("Beat Phase", Float) = 0
        _SD_BeatPulse ("Beat Pulse", Float) = 0
        _SD_BarPhase ("Bar Phase", Float) = 0
        _SD_Pointer ("Pointer", Vector) = (0.5,0.5,0,0)
        [NoScaleOffset] _MainTex ("Source", 2D) = "black" {}
        [NoScaleOffset] _CompareTex ("Compare", 2D) = "black" {}
        [NoScaleOffset] _SD_SourceTex ("Original Graph Input", 2D) = "black" {}
        _Variant ("Utility Variant", Float) = 0
        _Channel ("Channel", Range(0, 3)) = 0
        _Exposure ("Exposure", Float) = 0
        _Threshold ("Threshold", Range(0, 1)) = 0.5
        _RangeMode ("Mode", Range(0, 2)) = 0
        _Frame ("Frame", Float) = 0
        _Resolution ("Resolution", Vector) = (1920, 1080, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }
        Cull Off
        ZWrite Off
        ZTest Always
        Blend One Zero

        HLSLINCLUDE
        #include "UnityCG.cginc"
        #include "Assets/ShitDesigner/Shaders/Includes/VJUtility.hlsl"
        sampler2D _MainTex;
        sampler2D _CompareTex;
        sampler2D _SD_SourceTex;
        float _Variant;
        float _Channel;
        float _Exposure;
        float _Threshold;
        float _RangeMode;
        float _Frame;
        float4 _Resolution;
        float _SD_PassIndex;
        float _SD_PassCount;
        struct VJUtilityAttributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
        struct VJUtilityVaryings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
        VJUtilityVaryings VJUtilityVertex(VJUtilityAttributes input)
        {
            VJUtilityVaryings output;
            output.position = UnityObjectToClipPos(input.vertex);
            output.uv = input.uv;
            return output;
        }
        float4 VJUtilityGraphFragment(VJUtilityVaryings input) : SV_Target
        {
            int variant = clamp((int)floor(_Variant + 0.5), 0, 27);
            int stage = clamp((int)round(_SD_PassIndex), 0, 1);
            float4 resolution = max(VJFinite4(_SD_Resolution), float4(1.0, 1.0, 1.0, 1.0));
            float4 result = VJUtilityEvaluate(variant, _MainTex, _CompareTex, input.uv, resolution,
                VJFiniteScalar(_SD_Frame), _Exposure, _Channel, _Threshold, _RangeMode);
            if (stage == 0) return VJFinite4(result);
            // Histogram/waveform/vectorscope graphs use a second reduction /
            // monitor stage that consumes the first pass rather than drawing
            // the same utility kernel again.
            float4 current = VJFinite4(tex2D(_MainTex, input.uv));
            float4 source = VJFinite4(tex2D(_SD_SourceTex, input.uv));
            float luma = VJUtilityLuma(VJUnpremultiply(current).rgb);
            float sourceLuma = VJUtilityLuma(VJUnpremultiply(source).rgb);
            return VJFinite4(float4(lerp(luma.xxx, sourceLuma.xxx, 0.12), 1.0));
        }
        ENDHLSL
        Pass
        {
            Name "UtilityAnalysis"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VJUtilityVertex
            #pragma fragment VJUtilityGraphFragment
            ENDHLSL
        }
        Pass
        {
            Name "UtilityReduction"
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex VJUtilityVertex
            #pragma fragment VJUtilityGraphFragment
            ENDHLSL
        }
    }
}
