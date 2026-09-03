Shader "Hidden/ShitDesigner/DisplayTransform"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }
		Pass
		{
			ZTest Always
			ZWrite Off
			Cull Off
			HLSLPROGRAM
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			sampler2D _MainTex;
			float4 _MainTex_TexelSize;
			float _Mode;
			float _SourceSrgb;
			float _Premultiply;

			struct appdata
			{
				float4 vertex : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct v2f
			{
				float4 vertex : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			v2f vert(appdata input)
			{
				v2f output;
				output.vertex = UnityObjectToClipPos(input.vertex);
				output.uv = input.uv;
				return output;
			}

			float Aces(float value)
			{
				const float a = 2.51;
				const float b = 0.03;
				const float c = 2.43;
				const float d = 0.59;
				const float e = 0.14;
				return saturate((value * (a * value + b)) / (value * (c * value + d) + e));
			}

			float3 AcesPreservingChroma(float3 color)
			{
				color = max(color, 0.0);
				float peak = max(color.r, max(color.g, color.b));
				float mappedPeak = Aces(peak);
				return color * (mappedPeak / max(peak, 1.0e-6));
			}

			float3 LinearToSrgb(float3 color)
			{
				float3 low = color * 12.92;
				float3 high = 1.055 * pow(max(color, 0.0), 1.0 / 2.4) - 0.055;
				return lerp(high, low, step(color, 0.0031308));
			}

			float3 SrgbToLinear(float3 color)
			{
				float3 low = color / 12.92;
				float3 high = pow((max(color, 0.0) + 0.055) / 1.055, 2.4);
				return lerp(high, low, step(color, 0.04045));
			}

			float4 frag(v2f input) : SV_Target
			{
				// Keep HDR source values above one intact until the ACES
				// branch.  fixed precision can clamp/quantize those values
				// on D3D12 before tone mapping.
				float4 color = tex2D(_MainTex, input.uv);
				color.rgb = _SourceSrgb > 0.5 ? SrgbToLinear(color.rgb) : color.rgb;
				color.rgb = _Premultiply > 0.5 ? color.rgb * color.a : color.rgb;
				// Mapping each channel independently drives bright colors toward
				// neutral white. Map the peak once and scale all channels together
				// so HDR highlights retain their original chroma and hue.
				float3 rgb = _Mode > 0.5 ? AcesPreservingChroma(color.rgb) : max(color.rgb, 0.0);
				// Internal surfaces are premultiplied; display output is opaque black.
				return float4(saturate(LinearToSrgb(rgb)), 1.0);
			}
			ENDHLSL
		}
	}
	FallBack Off
}
