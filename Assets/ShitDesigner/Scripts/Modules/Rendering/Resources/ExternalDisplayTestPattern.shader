Shader "Hidden/ShitDesigner/ExternalDisplayTestPattern" {
	Properties {
		_DisplayNumber("Display Number", Float) = 2
		_PatternTime("Pattern Time", Float) = 0
	}
	SubShader {
		Tags { "Queue" = "Overlay" "RenderType" = "Opaque" }
		Pass {
			Cull Off
			ZTest Always
			ZWrite Off

			CGPROGRAM
			#pragma vertex vert_img
			#pragma fragment Frag
			#include "UnityCG.cginc"

			float _DisplayNumber;
			float _PatternTime;

			float Box(float2 coordinate, float2 center, float2 halfSize) {
				float2 distance = abs(coordinate - center) - halfSize;
				return 1.0 - smoothstep(0.0, 0.012, max(distance.x, distance.y));
			}

			float DisplayDigit(float2 coordinate) {
				float top = Box(coordinate, float2(0.0, 0.82), float2(0.34, 0.075));
				float middle = Box(coordinate, float2(0.0, 0.0), float2(0.34, 0.075));
				float bottom = Box(coordinate, float2(0.0, -0.82), float2(0.34, 0.075));
				float upperRight = Box(coordinate, float2(0.36, 0.41), float2(0.075, 0.34));
				float lowerRight = Box(coordinate, float2(0.36, -0.41), float2(0.075, 0.34));
				float lowerLeft = Box(coordinate, float2(-0.36, -0.41), float2(0.075, 0.34));
				if (_DisplayNumber < 2.5)
					return max(max(max(top, upperRight), middle), max(lowerLeft, bottom));
				return max(max(max(top, upperRight), middle), max(lowerRight, bottom));
			}

			fixed4 Frag(v2f_img input) : SV_Target {
				float2 uv = input.uv;
				float3 color = float3(0.025, 0.025, 0.025);

				if (uv.y > 0.78) {
					float bar = floor(saturate(uv.x) * 8.0);
					if (bar < 1.0) color = float3(1.0, 1.0, 1.0);
					else if (bar < 2.0) color = float3(1.0, 1.0, 0.0);
					else if (bar < 3.0) color = float3(0.0, 1.0, 1.0);
					else if (bar < 4.0) color = float3(0.0, 1.0, 0.0);
					else if (bar < 5.0) color = float3(1.0, 0.0, 1.0);
					else if (bar < 6.0) color = float3(1.0, 0.0, 0.0);
					else if (bar < 7.0) color = float3(0.0, 0.0, 1.0);
					else color = float3(0.0, 0.0, 0.0);
				}
				else if (uv.y < 0.16) {
					float level = floor(saturate(uv.x) * 11.0) / 10.0;
					color = level.xxx;
				}
				else {
					float grid = max(1.0 - step(0.008, abs(frac(uv.x * 16.0) - 0.5)),
						1.0 - step(0.012, abs(frac(uv.y * 9.0) - 0.5)));
					color = lerp(color, float3(0.14, 0.14, 0.14), grid);
					float2 centered = float2((uv.x - 0.5) * 2.0, (uv.y - 0.47) * 2.0);
					centered.x *= 16.0 / 9.0;
					float circle = 1.0 - smoothstep(0.008, 0.018, abs(length(centered) - 0.55));
					color = lerp(color, float3(0.6, 0.6, 0.6), circle);

					float sweep = 0.5 + 0.45 * sin(_PatternTime * 1.5707963);
					float sweepMarker = 1.0 - smoothstep(0.006, 0.012, abs(uv.x - sweep));
					color = lerp(color, float3(0.0, 1.0, 0.35), sweepMarker);

					float angle = _PatternTime * 3.1415927;
					float2 handDirection = float2(sin(angle), cos(angle));
					float alongHand = dot(centered, handDirection);
					float acrossHand = abs(centered.x * handDirection.y - centered.y * handDirection.x);
					float hand = (1.0 - step(0.018, acrossHand)) * step(0.0, alongHand) * (1.0 - step(0.50, alongHand));
					color = lerp(color, float3(1.0, 0.35, 0.0), hand);

					float frameSlot = floor(frac(_PatternTime) * 60.0);
					float frameCenter = (frameSlot + 0.5) / 60.0;
					float frameChase = Box(uv, float2(frameCenter, 0.19), float2(0.007, 0.018));
					color = lerp(color, float3(1.0, 0.35, 0.0), frameChase);
					float digit = DisplayDigit(float2((uv.x - 0.5) * 4.2, (uv.y - 0.48) * 4.2));
					color = lerp(color, float3(1.0, 1.0, 1.0), digit);
				}

				float border = step(uv.x, 0.008) + step(0.992, uv.x) + step(uv.y, 0.008) + step(0.992, uv.y);
				color = lerp(color, float3(1.0, 1.0, 1.0), saturate(border));
				return fixed4(color, 1.0);
			}
			ENDCG
		}
	}
}
