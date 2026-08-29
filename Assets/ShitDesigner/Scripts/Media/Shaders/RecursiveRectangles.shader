Shader "Hidden/ShitDesigner/RecursiveRectangles"
{
	Properties
	{
		_MaxDepth ("Max Depth", Int) = 5
		_MinLeafSize ("Min Leaf Size", Float) = 0.08
		_SplitProbability ("Split Probability", Range(0, 1)) = 0.9
		_AxisMode ("Axis Mode", Int) = 0
		_RatioMin ("Ratio Min", Range(0, 1)) = 0.25
		_RatioMax ("Ratio Max", Range(0, 1)) = 0.75
		_StructureSeed ("Seed", Int) = 1
		_BeatSync ("Beat Sync", Float) = 1
		_RevealProgress ("Reveal Progress", Range(0, 1)) = 1
		_SplitDuration ("Split Duration", Float) = 0.15
		_SplitStagger ("Split Stagger", Float) = 0.04
		_Easing ("Easing", Int) = 1
		_ColorA ("Color", Vector) = (0.05, 0.12, 0.22, 1)
		_Gutter ("Gutter", Range(0, 0.1)) = 0.004
		_LineColor ("Line Color", Vector) = (0.01, 0.01, 0.01, 1)
		_SD_BeatPhase ("Beat Phase", Float) = 0
		_SD_BeatIndex ("Beat Index", Float) = 0
		_SD_BeatDuration ("Beat Duration", Float) = 1
		_SD_HasBeatClock ("Has Beat Clock", Float) = 0
	}
	SubShader
	{
		Tags { "RenderType" = "Opaque" "Queue" = "Overlay" }
		Pass
		{
			ZTest Always Cull Off ZWrite Off
			CGPROGRAM
			#pragma target 3.5
			#pragma vertex vert
			#pragma fragment frag
			#include "UnityCG.cginc"

			int _MaxDepth;
			float _MinLeafSize;
			float _SplitProbability;
			int _AxisMode;
			float _RatioMin;
			float _RatioMax;
			int _StructureSeed;
			float _BeatSync;
			float _RevealProgress;
			float _SplitDuration;
			float _SplitStagger;
			int _Easing;
			float4 _ColorA;
			float _Gutter;
			float4 _LineColor;
			float _SD_BeatPhase;
			float _SD_BeatIndex;
			float _SD_BeatDuration;
			float _SD_HasBeatClock;

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

			uint MixBits(uint value)
			{
				value ^= value >> 16;
				value *= 0x7feb352du;
				value ^= value >> 15;
				value *= 0x846ca68bu;
				value ^= value >> 16;
				return value;
			}

			float Random01(uint seed, uint path, uint salt)
			{
				return (MixBits(seed ^ MixBits(path + salt)) & 0x00ffffffu) / 16777216.0;
			}

			float Ease(float value)
			{
				value = saturate(value);
				if (_Easing == 1) return value * value * (3.0 - 2.0 * value);
				if (_Easing == 2) return value * value;
				if (_Easing == 3) return 1.0 - (1.0 - value) * (1.0 - value);
				if (_Easing == 4) return value < 0.5
					? 2.0 * value * value
					: 1.0 - 2.0 * (1.0 - value) * (1.0 - value);
				return value;
			}

			float4 PathColor(uint seed, uint path)
			{
				if (path <= 1u) return float4(0.0, 0.0, 0.0, 0.0);
				return Random01(seed, path, 47u) < 0.5 ? _ColorA : float4(0.0, 0.0, 0.0, 0.0);
			}

			float4 Premultiply(float4 color)
			{
				color.rgb *= color.a;
				return color;
			}

			float4 frag(v2f input) : SV_Target
			{
				// Each pixel follows one branch, so recursive square subdivision does not allocate the full tree.
				const int InternalMaxDepth = 8;
				int maxDepth = clamp(_MaxDepth, 0, InternalMaxDepth);
				float minLeafSize = max(_MinLeafSize, 0.0);
				float duration = max(_SplitDuration, 0.0001);
				float stagger = max(_SplitStagger, 0.0);
				float timeline = maxDepth > 0
					? duration + (maxDepth - 1) * stagger
					: duration;
				bool synchronized = _BeatSync > 0.5 && _SD_HasBeatClock > 0.5;
				float revealProgress = synchronized
					? saturate(_SD_BeatPhase)
					: saturate(_RevealProgress);
				float progress = revealProgress * (synchronized ? max(_SD_BeatDuration, 0.0) : timeline);
				uint seed = (uint)_StructureSeed;
				if (synchronized)
					seed ^= MixBits((uint)_SD_BeatIndex + 0x9e3779b9u);
				uint path = 1u;
				float2 boundsMin = float2(0.0, 0.0);
				float2 boundsMax = float2(1.0, 1.0);
				float4 color = PathColor(seed, path);
				float lineCoverage = 0.0;

				[unroll]
				for (int depth = 0; depth < InternalMaxDepth; depth++)
				{
					if (depth >= maxDepth) break;
					float2 size = boundsMax - boundsMin;
					float2 childSize = size * 0.5;
					if (childSize.x < minLeafSize || childSize.y < minLeafSize) break;
					float splitProbability = saturate(_SplitProbability);
					if (splitProbability <= 0.0 || (depth > 0 && Random01(seed, path, 11u) >= splitProbability)) break;

					float2 split = (boundsMin + boundsMax) * 0.5;
					float eventStart = depth * stagger;
					float localProgress = saturate((progress - eventStart) / duration);
					if (progress < eventStart) break;

					bool right = input.uv.x > split.x;
					bool upper = input.uv.y > split.y;
					uint quadrant = (right ? 1u : 0u) + (upper ? 2u : 0u);
					uint childPath = path * 4u + quadrant;
					float2 childMin = float2(right ? split.x : boundsMin.x, upper ? split.y : boundsMin.y);
					float2 childMax = float2(right ? boundsMax.x : split.x, upper ? boundsMax.y : split.y);

					float eased = Ease(localProgress);
					if (_Gutter > 0.0)
					{
						bool inParent = all(input.uv >= boundsMin) && all(input.uv <= boundsMax);
						float verticalLine = saturate(1.0 - abs(input.uv.x - split.x) / _Gutter);
						float horizontalLine = saturate(1.0 - abs(input.uv.y - split.y) / _Gutter);
						lineCoverage = max(lineCoverage, max(verticalLine, horizontalLine) * eased * (inParent ? 1.0 : 0.0));
					}

					if (localProgress < 1.0)
					{
						float2 animatedMin = childMin;
						float2 animatedMax = childMax;
						animatedMax.x = lerp(childMin.x, childMax.x, eased);
						bool inside = localProgress > 0.0 && all(input.uv >= animatedMin) && all(input.uv <= animatedMax);
						if (!inside) break;
					}

					boundsMin = childMin;
					boundsMax = childMax;
					path = childPath;
					color = PathColor(seed, path);
				}

				float4 result = Premultiply(color);
				float4 lineColor = Premultiply(_LineColor);
				float lineAlpha = saturate(lineCoverage) * lineColor.a;
				result.rgb = lineColor.rgb * saturate(lineCoverage) + result.rgb * (1.0 - lineAlpha);
				result.a = lineAlpha + result.a * (1.0 - lineAlpha);
				return result;
			}
			ENDCG
		}
	}
}
