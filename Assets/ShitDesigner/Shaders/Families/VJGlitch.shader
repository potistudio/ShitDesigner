Shader "Hidden/ShitDesigner/VJ/Glitch"
{
	Properties
	{
		_SD_Time ("Graph Clock Time", Float) = 0
		_SD_DeltaTime ("Graph Delta Time", Float) = 0
		_SD_Frame ("Graph Frame", Float) = 0
		_SD_Resolution ("Graph Resolution", Vector) = (1920, 1080, 0, 0)
		_SD_Seed ("Deterministic Seed", Float) = 0
		_SD_BeatPhase ("Beat Phase", Float) = 0
		_SD_BeatPulse ("Beat Pulse", Float) = 0
		_SD_BarPhase ("Bar Phase", Float) = 0
		_SD_Pointer ("Pointer", Vector) = (0.5, 0.5, 0, 0)
		_VJVariant ("Variant", Float) = 0
		_VJAmount ("Amount", Float) = 0.5
		_VJFrequency ("Frequency", Float) = 4
		_VJDetail ("Detail", Float) = 4
		_VJSoftness ("Softness", Float) = 0.05
		_VJThreshold ("Threshold", Float) = 0.5
		_VJGain ("Gain", Float) = 1
		_VJMix ("Mix", Float) = 0.5
		_VJSpeed ("Speed", Float) = 1
		_VJPhase ("Phase", Float) = 0
		_VJDirection ("Direction", Float) = 1
		_VJAspect ("Aspect", Float) = 1
		_VJSeed ("Seed", Float) = 1
		_VJScale ("Scale", Float) = 1
		_VJRadius ("Radius", Float) = 1
		_VJFalloff ("Falloff", Float) = 1
		_VJExposure ("Exposure", Float) = 0
		_VJGamma ("Gamma", Float) = 1
		_VJHue ("Hue", Float) = 0
		_VJSaturation ("Saturation", Float) = 1
		_VJContrast ("Contrast", Float) = 1
		_VJTemperature ("Temperature", Float) = 0
		_VJTile ("Tile", Float) = 1
		_VJAngle ("Angle", Float) = 0
		_VJCenter ("Center", Vector) = (0.5, 0.5, 0, 0)
		_VJColorA ("Color A", Vector) = (1, 0, 0, 1)
		_VJColorB ("Color B", Vector) = (0, 0, 1, 1)
		_VJColorC ("Color C", Vector) = (0, 1, 0, 1)
		_VJPivot ("Pivot", Vector) = (0.5, 0.5, 0, 0)
		_VJDisplacement ("Displacement", Vector) = (0.01, 0.01, 0, 0)
		_MainTex ("Input", 2D) = "black" {}
		[NoScaleOffset]_HistoryTex ("History Slot 0", 2D) = "black" {}
		[NoScaleOffset]_HistoryTex2 ("History Slot 1", 2D) = "black" {}
		[NoScaleOffset]_HistoryTex3 ("History Slot 2", 2D) = "black" {}
	}
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
			#include "../Includes/VJCommon.hlsl"
			struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
			struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
			v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
			sampler2D _MainTex;
			sampler2D _HistoryTex;
			sampler2D _HistoryTex2;
			sampler2D _HistoryTex3;
			#include "../Includes/VJGlitch.hlsl"
			float4 frag(v2f i) : SV_Target
			{
				return VJGlitchEvaluate(_MainTex, _HistoryTex, _HistoryTex2, _HistoryTex3, i.uv, (int)round(_VJVariant));
			}
			ENDCG
		}
	}
}
