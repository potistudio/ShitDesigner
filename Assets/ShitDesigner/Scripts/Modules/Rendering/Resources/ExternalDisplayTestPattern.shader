Shader "Hidden/ShitDesigner/ExternalDisplayTestPattern" {
	Properties {
		_DisplayNumber("Display Number", Float) = 2
		_DisplayResolution("Display Resolution", Vector) = (1920, 1080, 0, 0)
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
			float4 _DisplayResolution;
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

			float IsDigit(float value, float expected) {
				return 1.0 - step(0.25, abs(value - expected));
			}

			float RulerDigit(float2 coordinate, float value) {
				float top = Box(coordinate, float2(0.0, 0.82), float2(0.34, 0.075));
				float middle = Box(coordinate, float2(0.0, 0.0), float2(0.34, 0.075));
				float bottom = Box(coordinate, float2(0.0, -0.82), float2(0.34, 0.075));
				float upperRight = Box(coordinate, float2(0.36, 0.41), float2(0.075, 0.34));
				float lowerRight = Box(coordinate, float2(0.36, -0.41), float2(0.075, 0.34));
				float lowerLeft = Box(coordinate, float2(-0.36, -0.41), float2(0.075, 0.34));
				float upperLeft = Box(coordinate, float2(-0.36, 0.41), float2(0.075, 0.34));

				float segmentTop = IsDigit(value, 0.0) + IsDigit(value, 2.0) + IsDigit(value, 3.0) +
					IsDigit(value, 5.0) + IsDigit(value, 6.0) + IsDigit(value, 7.0) + IsDigit(value, 8.0) + IsDigit(value, 9.0);
				float segmentMiddle = IsDigit(value, 2.0) + IsDigit(value, 3.0) + IsDigit(value, 4.0) +
					IsDigit(value, 5.0) + IsDigit(value, 6.0) + IsDigit(value, 8.0) + IsDigit(value, 9.0);
				float segmentBottom = IsDigit(value, 0.0) + IsDigit(value, 2.0) + IsDigit(value, 3.0) +
					IsDigit(value, 5.0) + IsDigit(value, 6.0) + IsDigit(value, 8.0) + IsDigit(value, 9.0);
				float segmentUpperRight = IsDigit(value, 0.0) + IsDigit(value, 1.0) + IsDigit(value, 2.0) +
					IsDigit(value, 3.0) + IsDigit(value, 4.0) + IsDigit(value, 7.0) + IsDigit(value, 8.0) + IsDigit(value, 9.0);
				float segmentLowerRight = IsDigit(value, 0.0) + IsDigit(value, 1.0) + IsDigit(value, 3.0) +
					IsDigit(value, 4.0) + IsDigit(value, 5.0) + IsDigit(value, 6.0) + IsDigit(value, 7.0) +
					IsDigit(value, 8.0) + IsDigit(value, 9.0);
				float segmentLowerLeft = IsDigit(value, 0.0) + IsDigit(value, 2.0) + IsDigit(value, 6.0) + IsDigit(value, 8.0);
				float segmentUpperLeft = IsDigit(value, 0.0) + IsDigit(value, 4.0) + IsDigit(value, 5.0) +
					IsDigit(value, 6.0) + IsDigit(value, 8.0) + IsDigit(value, 9.0);

				return saturate(top * segmentTop + middle * segmentMiddle + bottom * segmentBottom +
					upperRight * segmentUpperRight + lowerRight * segmentLowerRight + lowerLeft * segmentLowerLeft +
					upperLeft * segmentUpperLeft);
			}

			float FourDigitNumber(float2 coordinate, float value) {
				float safeValue = clamp(floor(value + 0.5), 0.0, 9999.0);
				float thousands = floor(safeValue / 1000.0);
				float hundreds = floor(fmod(safeValue, 1000.0) / 100.0);
				float tens = floor(fmod(safeValue, 100.0) / 10.0);
				float ones = fmod(safeValue, 10.0);
				return max(max(RulerDigit(coordinate - float2(-1.8, 0.0), thousands) * step(999.5, safeValue),
					RulerDigit(coordinate - float2(-0.6, 0.0), hundreds) * step(99.5, safeValue)),
					max(RulerDigit(coordinate - float2(0.6, 0.0), tens) * step(9.5, safeValue),
					RulerDigit(coordinate - float2(1.8, 0.0), ones)));
			}

			float RulerMask(float2 uv, float lineWidth, float extraTickLength) {
				const float inset = 0.035;
				const float span = 0.93;
				float insideX = step(inset, uv.x) * step(uv.x, 1.0 - inset);
				float insideY = step(inset, uv.y) * step(uv.y, 1.0 - inset);
				float horizontalLines = insideX * max(
					1.0 - smoothstep(lineWidth, lineWidth * 2.0, abs(uv.y - inset)),
					1.0 - smoothstep(lineWidth, lineWidth * 2.0, abs(uv.y - (1.0 - inset))));
				float verticalLines = insideY * max(
					1.0 - smoothstep(lineWidth, lineWidth * 2.0, abs(uv.x - inset)),
					1.0 - smoothstep(lineWidth, lineWidth * 2.0, abs(uv.x - (1.0 - inset))));

				float horizontalPosition = saturate((uv.x - inset) / span);
				float horizontalIndex = floor(horizontalPosition * 50.0 + 0.5);
				float horizontalDistance = abs(frac(horizontalPosition * 50.0 + 0.5) - 0.5) * span / 50.0;
				float horizontalTick = insideX * (1.0 - smoothstep(lineWidth, lineWidth * 2.0, horizontalDistance));
				float horizontalMajor = 1.0 - step(0.5, fmod(horizontalIndex, 5.0));
				float horizontalTickLength = lerp(0.010, 0.022, horizontalMajor) + extraTickLength;
				float horizontalTicks = horizontalTick * max(
					step(inset, uv.y) * step(uv.y, inset + horizontalTickLength),
					step(1.0 - inset - horizontalTickLength, uv.y) * step(uv.y, 1.0 - inset));

				float verticalPosition = saturate((uv.y - inset) / span);
				float verticalIndex = floor(verticalPosition * 50.0 + 0.5);
				float verticalDistance = abs(frac(verticalPosition * 50.0 + 0.5) - 0.5) * span / 50.0;
				float verticalTick = insideY * (1.0 - smoothstep(lineWidth, lineWidth * 2.0, verticalDistance));
				float verticalMajor = 1.0 - step(0.5, fmod(verticalIndex, 5.0));
				float verticalTickLength = lerp(0.010, 0.022, verticalMajor) + extraTickLength;
				float verticalTicks = verticalTick * max(
					step(inset, uv.x) * step(uv.x, inset + verticalTickLength),
					step(1.0 - inset - verticalTickLength, uv.x) * step(uv.x, 1.0 - inset));

				return saturate(horizontalLines + verticalLines + horizontalTicks + verticalTicks);
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

				float rulerShadow = RulerMask(uv, 0.003, 0.003);
				float ruler = RulerMask(uv, 0.0015, 0.0);
				color = lerp(color, float3(0.0, 0.0, 0.0), rulerShadow);
				color = lerp(color, float3(1.0, 1.0, 1.0), ruler);

				float widthBackdrop = Box(uv, float2(0.5, 0.91), float2(0.10, 0.030));
				float heightBackdrop = Box(uv, float2(0.91, 0.5), float2(0.030, 0.10));
				color = lerp(color, float3(0.0, 0.0, 0.0), saturate(widthBackdrop + heightBackdrop));
				float widthLabel = FourDigitNumber(float2((uv.x - 0.5) / 0.035, (uv.y - 0.91) / 0.018), _DisplayResolution.x);
				float heightLabel = FourDigitNumber(float2((uv.y - 0.5) / 0.035, (0.91 - uv.x) / 0.018), _DisplayResolution.y);
				color = lerp(color, float3(1.0, 1.0, 1.0), saturate(widthLabel + heightLabel));
				return fixed4(color, 1.0);
			}
			ENDCG
		}
	}
}
