Shader "Hidden/ShitDesigner/VJ/Generator"
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
		[NoScaleOffset]_HistoryTex ("History Slot 0", 2D) = "black" {}
		[NoScaleOffset]_HistoryTex2 ("History Slot 1", 2D) = "black" {}
		[NoScaleOffset]_HistoryTex3 ("History Slot 2", 2D) = "black" {}
		[NoScaleOffset]_SD_SourceTex ("Original Graph Input", 2D) = "black" {}
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }
		CGINCLUDE
		#include "UnityCG.cginc"
		#include "../Includes/VJCommon.hlsl"
		#include "../Includes/VJDaniloTunnel.hlsl"
		#include "../Includes/VJGenerator.hlsl"
		struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
		struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };
		v2f vert(appdata v) { v2f o; o.vertex = UnityObjectToClipPos(v.vertex); o.uv = v.uv; return o; }
		sampler2D _MainTex;
		sampler2D _HistoryTex;
		sampler2D _HistoryTex2;
		sampler2D _HistoryTex3;
		sampler2D _SD_SourceTex;
		float _SD_PassIndex;
		float _SD_PassCount;
		float4 VJGeneratorGraphFragment(v2f i) : SV_Target
		{
			float2 uv = saturate(i.uv);
			int variant = clamp((int)round(_VJVariant), 0, 47);
			int stage = clamp((int)round(_SD_PassIndex), 0, 1);
			float4 generated = VJFinite4(VJGeneratorEvaluate(uv, variant));
			if (stage == 0 && variant < 44) return generated;
				float4 history = VJFinite4(tex2D(_HistoryTex, uv));
			float4 current = VJFinite4(tex2D(_MainTex, uv));
			if (stage == 0)
			{
				// Reaction diffusion and cellular generators seed their first
				// pass from the newest history slot instead of recomputing a
				// stateless image. A cleared slot falls back to the authored
				// deterministic generator output.
				float historyEnergy = max(history.a, max(history.r, max(history.g, history.b)));
				float seedWeight = step(1.0e-4, historyEnergy) * 0.72;
				return VJFinite4(lerp(generated, lerp(generated, history, 0.5), seedWeight));
			}
			// The second pass publishes a stable color/composite stage from
			// the simulation result. It consumes the first pass and keeps a
			// small source/history contribution so it cannot be a duplicate stage.
			float4 source = VJFinite4(tex2D(_SD_SourceTex, uv));
			float4 history2 = VJFinite4(tex2D(_HistoryTex2, uv));
			float3 state = lerp(current.rgb, history2.rgb, variant >= 45 ? 0.18 : 0.08);
			return VJFinite4(float4(lerp(state, source.rgb, 0.06), max(current.a, generated.a)));
		}
		ENDCG
		Pass
		{
			Name "GeneratorSimulation"
			ZTest Always Cull Off ZWrite Off
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment VJGeneratorGraphFragment
			ENDCG
		}
		Pass
		{
			Name "GeneratorComposite"
			ZTest Always Cull Off ZWrite Off
			CGPROGRAM
			#pragma vertex vert
			#pragma fragment VJGeneratorGraphFragment
			ENDCG
		}
	}
}
