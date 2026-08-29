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
		_ColorA ("Color A", Vector) = (0.05, 0.12, 0.22, 1)
		_ColorB ("Color B", Vector) = (0.95, 0.32, 0.14, 1)
		_Gutter ("Gutter", Range(0, 0.1)) = 0.004
		_LineColor ("Line Color", Vector) = (0.01, 0.01, 0.01, 1)
		_SD_BeatPhase ("Beat Phase", Float) = 0
		_SD_BeatIndex ("Beat Index", Float) = 0
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
			float4 _ColorB;
			float _Gutter;
			float4 _LineColor;
			float _SD_BeatPhase;
			float _SD_BeatIndex;
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
				return lerp(_ColorA, _ColorB, Random01(seed, path, 47u));
			}

			float4 Premultiply(float4 color)
			{
				color.rgb *= color.a;
				return color;
			}

			float4 frag(v2f input) : SV_Target
			{
				// The depth limit establishes the internal Leaf cap at 256.
				const int InternalMaxDepth = 8;
				int maxDepth = clamp(_MaxDepth, 0, InternalMaxDepth);
				float minLeafSize = max(_MinLeafSize, 0.0);
				float duration = max(_SplitDuration, 0.0001);
				float stagger = max(_SplitStagger, 0.0);
				float timeline = maxDepth > 0
					? duration + (maxDepth - 1) * (duration + stagger)
					: duration;
				float revealProgress = _BeatSync > 0.5 && _SD_HasBeatClock > 0.5
					? saturate(_SD_BeatPhase)
					: saturate(_RevealProgress);
				float progress = revealProgress * timeline;
				uint seed = (uint)_StructureSeed;
				if (_BeatSync > 0.5 && _SD_HasBeatClock > 0.5)
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
					if (size.x < minLeafSize || size.y < minLeafSize) break;
					int axis;
					if (_AxisMode == 1) axis = 1;
					else if (_AxisMode == 2) axis = 0;
					else if (_AxisMode == 3) axis = Random01(seed, path, 23u) < 0.5 ? 0 : 1;
					else axis = size.x >= size.y ? 0 : 1;

					float axisSize = axis == 0 ? size.x : size.y;
					float legalMin = max(_RatioMin, minLeafSize / max(axisSize, 0.000001));
					float legalMax = min(_RatioMax, 1.0 - minLeafSize / max(axisSize, 0.000001));
					if (legalMin > legalMax || Random01(seed, path, 11u) >= saturate(_SplitProbability)) break;

					float ratio = lerp(legalMin, legalMax, Random01(seed, path, 31u));
					float split = axis == 0
						? lerp(boundsMin.x, boundsMax.x, ratio)
						: lerp(boundsMin.y, boundsMax.y, ratio);
					float eventStart = depth * (duration + stagger);
					float localProgress = saturate((progress - eventStart) / duration);
					if (progress < eventStart) break;

					bool firstChild = axis == 0 ? input.uv.x <= split : input.uv.y <= split;
					uint childPath = path * 2u + (firstChild ? 0u : 1u);
					float2 childMin = boundsMin;
					float2 childMax = boundsMax;
					if (axis == 0)
					{
						if (firstChild) childMax.x = split;
						else childMin.x = split;
					}
					else
					{
						if (firstChild) childMax.y = split;
						else childMin.y = split;
					}

					float eased = Ease(localProgress);
					if (_Gutter > 0.0)
					{
						float coordinate = axis == 0 ? input.uv.x : input.uv.y;
						bool inParent = all(input.uv >= boundsMin) && all(input.uv <= boundsMax);
						lineCoverage = max(lineCoverage, saturate(1.0 - abs(coordinate - split) / _Gutter) * eased * (inParent ? 1.0 : 0.0));
					}

					if (localProgress < 1.0)
					{
						float2 animatedMin = childMin;
						float2 animatedMax = childMax;
						if (axis == 0)
						{
							if (firstChild) animatedMin.x = lerp(split, childMin.x, eased);
							else animatedMax.x = lerp(split, childMax.x, eased);
						}
						else
						{
							if (firstChild) animatedMin.y = lerp(split, childMin.y, eased);
							else animatedMax.y = lerp(split, childMax.y, eased);
						}
						bool inside = localProgress > 0.0 && all(input.uv >= animatedMin) && all(input.uv <= animatedMax);
						if (inside) color = PathColor(seed, childPath);
						break;
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
