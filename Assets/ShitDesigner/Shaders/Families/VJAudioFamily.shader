Shader "Hidden/ShitDesigner/VJ/AudioFamily"
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
		[NoScaleOffset]_WaveformTex ("Waveform", 2D) = "gray" {}
		[NoScaleOffset]_SpectrumTex ("FFT Spectrum", 2D) = "black" {}
		[NoScaleOffset]_MelTex ("Mel Bands", 2D) = "black" {}
		[NoScaleOffset]_OnsetTex ("Onset History", 2D) = "black" {}
		[NoScaleOffset]_MainTex ("Current Graph Frame", 2D) = "black" {}
		_Variant ("Audio Variant", Float) = 0
		_Amount ("Amount", Range(0, 1)) = 0.75
		_Gain ("Analysis Gain", Float) = 1
		_Rms ("RMS", Range(0, 1)) = 0
		_Peak ("Peak", Range(0, 1)) = 0
		_Beat ("Beat", Range(0, 1)) = 0
		_BpmPhase ("BPM Phase", Range(0, 1)) = 0
		_GraphTime ("Graph Clock Time", Float) = 0
		_Frame ("Frame", Float) = 0
		_Seed ("Deterministic Seed", Float) = 0
		_Resolution ("Resolution", Vector) = (1920, 1080, 0, 0)
		[NoScaleOffset]_HistoryTex ("History Slot 0", 2D) = "black" {}
		[NoScaleOffset]_HistoryTex2 ("History Slot 1", 2D) = "black" {}
		[NoScaleOffset]_HistoryTex3 ("History Slot 2", 2D) = "black" {}
		[NoScaleOffset]_SD_SourceTex ("Original Graph Input", 2D) = "black" {}
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
		#include "Assets/ShitDesigner/Shaders/Includes/VJAudio.hlsl"
		sampler2D _WaveformTex;
		sampler2D _SpectrumTex;
		sampler2D _MelTex;
		sampler2D _OnsetTex;
		sampler2D _MainTex;
		sampler2D _HistoryTex;
		sampler2D _HistoryTex2;
		sampler2D _HistoryTex3;
		sampler2D _SD_SourceTex;
		float _Variant;
		float _Amount;
		float _Gain;
		float _Rms;
		float _Peak;
		float _Beat;
		float _BpmPhase;
		float _GraphTime;
		float _Frame;
		float _Seed;
		float4 _Resolution;
		float _SD_PassIndex;
		float _SD_PassCount;
		struct VJAudioAttributes { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
		struct VJAudioVaryings { float4 position : SV_POSITION; float2 uv : TEXCOORD0; };
		VJAudioVaryings VJAudioVertex(VJAudioAttributes input)
		{
			VJAudioVaryings output;
			output.position = UnityObjectToClipPos(input.vertex);
			output.uv = input.uv;
			return output;
		}
		float4 VJAudioGraphFragment(VJAudioVaryings input) : SV_Target
		{
			float4 resolution = max(VJFinite4(_SD_Resolution), float4(1.0, 1.0, 1.0, 1.0));
			int variant = clamp((int)floor(_Variant + 0.5), 0, 30);
			int stage = clamp((int)round(_SD_PassIndex), 0, 1);
			float beatPulse = max(_Beat, VJFiniteScalar(_SD_BeatPulse));
			float beatPhase = abs(_SD_BeatPhase) > 1.0e-6 ? _SD_BeatPhase : _BpmPhase;
			float4 result = VJAudioEvaluate(variant, _WaveformTex, _SpectrumTex, _MelTex, _OnsetTex,
			input.uv, resolution, VJFiniteScalar(_SD_Time), VJFiniteScalar(_SD_Frame), _Rms, _Peak, beatPulse, beatPhase,
			_Amount, _Gain, VJFiniteScalar(_SD_Seed));
			if (stage == 0) return VJFinite4(result);
				float4 current = VJFinite4(tex2D(_MainTex, input.uv));
			float4 history = VJFinite4(tex2D(_HistoryTex, input.uv));
			float stateWeight = (variant == 27 || variant == 29) ? 0.55 : 0.0;
			return VJFinite4(lerp(current, history, stateWeight));
		}
		ENDHLSL
		Pass
		{
			Name "AudioAnalysis"
			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex VJAudioVertex
			#pragma fragment VJAudioGraphFragment
			ENDHLSL
		}
		Pass
		{
			Name "AudioStateComposite"
			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex VJAudioVertex
			#pragma fragment VJAudioGraphFragment
			ENDHLSL
		}
	}
}
