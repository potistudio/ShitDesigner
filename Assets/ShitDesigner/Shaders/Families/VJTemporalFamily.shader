Shader "Hidden/ShitDesigner/VJ/TemporalFamily"
{
	Properties
	{
		_SD_Time ("Graph Clock Time", Float) = 0
		_SD_DeltaTime ("Graph Delta Time", Float) = 0
		_SD_Frame ("Graph Frame", Float) = 0
		_SD_Resolution ("Graph Resolution", Vector) = (1920, 1080, 0, 0)
		_SD_Seed ("Deterministic Seed", Float) = 0
		_SD_PassIndex ("Graph Pass Index", Float) = 0
		_SD_PassCount ("Graph Pass Count", Float) = 1
		_SD_BeatPhase ("Beat Phase", Float) = 0
		_SD_BeatPulse ("Beat Pulse", Float) = 0
		_SD_BarPhase ("Bar Phase", Float) = 0
		_SD_Pointer ("Pointer", Vector) = (0.5, 0.5, 0, 0)
		[NoScaleOffset]_MainTex ("Current Frame", 2D) = "black" {}
		[NoScaleOffset]_HistoryTex ("History Slot 0", 2D) = "black" {}
		[NoScaleOffset]_HistoryTex2 ("History Slot 1", 2D) = "black" {}
		[NoScaleOffset]_HistoryTex3 ("History Slot 2", 2D) = "black" {}
		[NoScaleOffset]_DisplacementTex ("Displacement / Flow", 2D) = "gray" {}
		[NoScaleOffset]_SD_SourceTex ("Original Graph Input", 2D) = "black" {}
		_Variant ("Temporal Variant", Float) = 0
		_Amount ("Effect Amount", Range(0, 1)) = 0.5
		_Feedback ("Feedback", Range(0, 1)) = 0.8
		_Progress ("Progress", Range(0, 1)) = 0
		_Frame ("Frame", Float) = 0
		_GraphTime ("Graph Clock Time", Float) = 0
		_Paused ("Graph Clock Paused", Float) = 0
		_Reset ("Reset History", Float) = 0
		_Seed ("Deterministic Seed", Float) = 0
		_Resolution ("History Resolution", Vector) = (1920, 1080, 0, 0)
		_Beat ("Beat Phase", Range(0, 1)) = 0
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
		#include "Assets/ShitDesigner/Shaders/Includes/VJTemporal.hlsl"
		sampler2D _MainTex;
		sampler2D _HistoryTex;
		sampler2D _HistoryTex2;
		sampler2D _HistoryTex3;
		sampler2D _DisplacementTex;
		sampler2D _SD_SourceTex;
		float _Variant;
		float _Amount;
		float _Feedback;
		float _Progress;
		float _Frame;
		float _GraphTime;
		float _Paused;
		float _Reset;
		float _Seed;
		float4 _Resolution;
		float _Beat;
		float _SD_PassIndex;
		float _SD_PassCount;
		struct VJTemporalAttributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
		struct VJTemporalVaryings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
		VJTemporalVaryings VJTemporalVertex(VJTemporalAttributes input)
		{
			VJTemporalVaryings output;
			output.position = UnityObjectToClipPos(input.vertex);
			output.uv = input.uv;
			return output;
		}
		float4 VJTemporalGraphFragment(VJTemporalVaryings input) : SV_Target
		{
			float2 uv = saturate(input.uv);
			float4 current = VJSample2D(_MainTex, uv);
			if (_Reset > 0.5) return current;
				int variant = clamp((int)floor(_Variant + 0.5), 0, 31);
			int stage = clamp((int)round(_SD_PassIndex), 0, 2);
			float4 resolution = max(VJFinite4(_SD_Resolution), float4(1.0, 1.0, 1.0, 1.0));
			float graphPaused = max(saturate(VJFiniteScalar(_Paused)),
			(VJFiniteScalar(_SD_DeltaTime) <= 0.0 && VJFiniteScalar(_SD_Frame) > 0.5) ? 1.0 : 0.0);
			float4 result = VJTemporalEvaluate(variant, _MainTex, _HistoryTex, _HistoryTex2, _HistoryTex3,
			_DisplacementTex, uv, _Amount, _Feedback, _Progress, VJFiniteScalar(_SD_Frame), VJFiniteScalar(_SD_Time),
			graphPaused, _Beat, resolution, VJFiniteScalar(_SD_Seed));
			if (stage == 0) return VJFinite4(result);
				float4 history = VJFinite4(tex2D(_HistoryTex, uv));
			float4 history2 = VJFinite4(tex2D(_HistoryTex2, uv));
			if (stage == 1)
			{
				// P2 temporal variants use a distinct state stage. The
				// interpolation/cellular variants also consume a second
				// history slot rather than rerunning the first kernel.
				if (variant == 28) return VJFinite4(lerp(history2, current, saturate(_Progress)));
					if (variant == 31) return VJFinite4(lerp(current, history2, 0.35));
					return VJFinite4(lerp(current, history, saturate(_Feedback) * 0.5));
			}
			float4 source = VJFinite4(tex2D(_SD_SourceTex, uv));
			return VJFinite4(lerp(current, source, 0.08));
		}
		ENDHLSL
		Pass
		{
			Name "TemporalEvaluate"
			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex VJTemporalVertex
			#pragma fragment VJTemporalGraphFragment
			ENDHLSL
		}
		Pass
		{
			Name "TemporalState"
			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex VJTemporalVertex
			#pragma fragment VJTemporalGraphFragment
			ENDHLSL
		}
		Pass
		{
			Name "TemporalComposite"
			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex VJTemporalVertex
			#pragma fragment VJTemporalGraphFragment
			ENDHLSL
		}
	}
}
