Shader "Hidden/ShitDesigner/VJ/TransitionFamily"
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
        [NoScaleOffset] _TexA ("Input A", 2D) = "black" {}
        [NoScaleOffset] _TexB ("Input B", 2D) = "black" {}
        [NoScaleOffset] _DisplacementTex ("Displacement", 2D) = "gray" {}
        _Progress ("Progress", Range(0, 1)) = 0
        _Softness ("Edge Softness", Range(0, 1)) = 0.02
        _Direction ("Direction", Float) = 0
        _Reverse ("Reverse", Float) = 0
        _Variant ("Transition Variant", Float) = 0
        _Seed ("Deterministic Seed", Float) = 0
        _Frame ("Frame", Float) = 0
        _Aspect ("Aspect", Float) = 1
        _Color ("Dip Color", Color) = (0, 0, 0, 1)
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
            #pragma vertex VJTransitionVertex
            #pragma fragment VJTransitionFragment
            #include "UnityCG.cginc"
            #include "Assets/ShitDesigner/Shaders/Includes/VJTransition.hlsl"

            sampler2D _TexA;
            sampler2D _TexB;
            sampler2D _DisplacementTex;
            float _Progress;
            float _Softness;
            float _Direction;
            float _Reverse;
            float _Variant;
            float _Seed;
            float _Frame;
            float _Aspect;
            float4 _Color;

            struct VJTransitionAttributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct VJTransitionVaryings
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            VJTransitionVaryings VJTransitionVertex(VJTransitionAttributes input)
            {
                VJTransitionVaryings output;
                output.position = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                return output;
            }

            float4 VJTransitionFragment(VJTransitionVaryings input) : SV_Target
            {
                float2 uv = saturate(input.uv);
                float4 sourceA = VJSample2D(_TexA, uv);
                float4 sourceB = VJSample2D(_TexB, uv);
                float rawProgress = VJFiniteScalar(_Progress);
                // Endpoint identity is intentionally checked before reverse:
                // callers can always use progress 0/1 as graph bypasses.
                if (rawProgress <= 0.0) return sourceA;
                if (rawProgress >= 1.0) return sourceB;

                float progress = saturate(rawProgress);
                if (_Reverse > 0.5) progress = 1.0 - progress;
                int variant = clamp((int)floor(_Variant + 0.5), 0, 35);
                float softness = max(abs(VJFiniteScalar(_Softness)), 1.0e-4);

                if (variant == 1)
                {
                    float halfProgress = progress * 2.0;
                    float4 dipColor = VJPremultiply(VJFinite4(_Color));
                    return halfProgress < 1.0
                        ? lerp(sourceA, dipColor, halfProgress)
                        : lerp(dipColor, sourceB, halfProgress - 1.0);
                }

                float2 uvA = uv;
                float2 uvB = uv;
                float aspect = _Aspect > 0.0 ? _Aspect : 1.0;
                if (variant >= 12 && variant <= 30)
                {
                    uvA = VJTransitionWarp(variant, uv, progress, _Direction, aspect, _DisplacementTex);
                    uvB = VJTransitionWarp(variant, uv, 1.0 - progress, _Direction, aspect, _DisplacementTex);
                }

                float4 a = VJSample2D(_TexA, uvA);
                float4 b = VJSample2D(_TexB, uvB);
                if (variant == 24 || variant == 25)
                {
                    float2 axis = normalize(float2(1.0, 0.75));
                    a = VJTransitionBlurSample(_TexA, uvA, axis, 0.01 + progress * 0.06);
                    b = VJTransitionBlurSample(_TexB, uvB, axis, 0.01 + (1.0 - progress) * 0.06);
                }
                else if (variant == 31)
                {
                    float split = 0.01 * sin(progress * VJ_TAU);
                    a = float4(VJSample2D(_TexA, uvA + float2(split, 0.0)).r,
                        VJSample2D(_TexA, uvA).g, VJSample2D(_TexA, uvA - float2(split, 0.0)).b, a.a);
                    b = float4(VJSample2D(_TexB, uvB + float2(split, 0.0)).r,
                        VJSample2D(_TexB, uvB).g, VJSample2D(_TexB, uvB - float2(split, 0.0)).b, b.a);
                }

                float mask = saturate(VJTransitionMask(variant, uv, progress, softness, _Direction, VJFiniteScalar(_SD_Seed) + VJFiniteScalar(_SD_Frame) * 0.001));
                return VJFinite4(lerp(a, b, mask));
            }
            ENDHLSL
        }
    }
}
