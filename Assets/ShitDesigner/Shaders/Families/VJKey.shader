Shader "Hidden/ShitDesigner/VJ/Key"
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
		[NoScaleOffset]_SD_SourceTex ("Original Graph Input", 2D) = "black" {}
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }
		CGINCLUDE
		#include "UnityCG.cginc"
		#include "../Includes/VJCommon.hlsl"
		struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
		struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
		v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
		sampler2D _MainTex;
		sampler2D _SD_SourceTex;
		float4 _MainTex_TexelSize;
		float _SD_PassIndex;
		float _SD_PassCount;
		#include "../Includes/VJKey.hlsl"
		float4 VJKeyGraphFragment(v2f i) : SV_Target
		{
			float2 uv = saturate(i.uv);
			int variant = clamp((int)round(_VJVariant), 0, 9);
			int stage = clamp((int)round(_SD_PassIndex), 0, 1);
			if (stage == 0)
				return VJFinite4(VJKeyEvaluate(_MainTex, uv, max(_MainTex_TexelSize.xy, 1.0e-5), variant));
			float4 mask = VJFinite4(tex2D(_MainTex, uv));
			float4 source = VJFinite4(tex2D(_SD_SourceTex, uv));
			// Matte extraction is followed by an explicit source composite;
			// the second pass therefore consumes the mask from pass zero.
			return VJFinite4(float4(source.rgb * saturate(mask.a), saturate(mask.a)));
		}
		ENDCG
		Pass
		{
			Name "KeyExtract"
			ZTest Always Cull Off ZWrite Off
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment VJKeyGraphFragment
			ENDCG
		}
		Pass
		{
			Name "KeyComposite"
			ZTest Always Cull Off ZWrite Off
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment VJKeyGraphFragment
			ENDCG
		}
	}
}
