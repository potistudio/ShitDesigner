Shader "ShitDesigner/Stage/LED Screen"
{
	Properties
	{
		[MainTexture] _BaseMap ("Video Texture", 2D) = "white" {}
		[HDR][MainColor] _BaseColor ("Video Tint", Color) = (1, 1, 1, 1)
		[HDR] _FallbackColorA ("Fallback Color A", Color) = (1.15, 0.02, 0.32, 1)
		[HDR] _FallbackColorB ("Fallback Color B", Color) = (0.72, 1.1, 0.03, 1)
		_VideoBlend ("Video Blend", Range(0, 1)) = 1
		_EmissionStrength ("Emission Strength", Range(0, 8)) = 2.5
		_ResolutionScale ("LED Resolution Ratio", Range(0.001, 1)) = 0.1
		_PixelGap ("Pixel Gap", Range(0.01, 0.45)) = 0.16
		_ScanlineStrength ("Scanline Strength", Range(0, 1)) = 0.2
		_FlickerStrength ("Flicker Strength", Range(0, 0.2)) = 0.035
	}

	SubShader
	{
		Tags
		{
			"RenderPipeline" = "UniversalPipeline"
			"RenderType" = "Opaque"
			"Queue" = "Geometry"
		}

		Pass
		{
			Name "LED Screen"
			Tags { "LightMode" = "UniversalForward" }

			Cull Off
			ZWrite On

			HLSLPROGRAM
			#pragma target 3.5
			#pragma vertex Vert
			#pragma fragment Frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			TEXTURE2D(_BaseMap);
			SAMPLER(sampler_BaseMap);

			CBUFFER_START(UnityPerMaterial)
			float4 _BaseMap_ST;
			half4 _BaseColor;
			half4 _FallbackColorA;
			half4 _FallbackColorB;
			half _VideoBlend;
			half _EmissionStrength;
			half _ResolutionScale;
			float4 _BaseMap_TexelSize;
			half _PixelGap;
			half _ScanlineStrength;
			half _FlickerStrength;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float2 uv : TEXCOORD0;
			};

			struct Varyings
			{
				float4 positionHCS : SV_POSITION;
				float2 uv : TEXCOORD0;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
				output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
				return output;
			}

			half3 FallbackVisual(float2 uv)
			{
				half wave = sin((uv.x * 2.5h - uv.y * 1.4h + _Time.y * 0.12h) * 6.2831853h) * 0.5h + 0.5h;
				half band = smoothstep(0.34h, 0.66h, sin((uv.y + _Time.y * 0.035h) * 31.415926h) * 0.5h + 0.5h);
				return lerp(_FallbackColorA.rgb, _FallbackColorB.rgb, saturate(wave + band * 0.28h));
			}

			half4 Frag(Varyings input) : SV_Target
			{
				float2 cellCount = max(round(_BaseMap_TexelSize.zw * _ResolutionScale), 1.0h);
				float2 cellGrid = input.uv * cellCount;
				float2 cellPosition = frac(cellGrid);
				float2 cellUv = (floor(cellGrid) + 0.5h) / cellCount;
				half pixelMask = step(_PixelGap, cellPosition.x) * step(_PixelGap, cellPosition.y)
					* step(cellPosition.x, 1.0h - _PixelGap) * step(cellPosition.y, 1.0h - _PixelGap);

				half3 video = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, cellUv).rgb * _BaseColor.rgb;
				half3 content = lerp(FallbackVisual(cellUv), video, _VideoBlend);

				half redEmitter = step(cellPosition.x, 0.3333333h);
				half greenEmitter = step(0.3333333h, cellPosition.x) * step(cellPosition.x, 0.6666667h);
				half blueEmitter = step(0.6666667h, cellPosition.x);
				half3 emitterMask = half3(redEmitter, greenEmitter, blueEmitter);
				float emitterFootprint = fwidth(cellGrid.x * 3.0h);
				half emitterFade = smoothstep(0.35h, 0.7h, emitterFootprint);
				half3 resolvedEmitter = lerp(emitterMask * 3.0h, half3(1.0h, 1.0h, 1.0h), emitterFade);
				half scanline = lerp(1.0h, 0.72h, _ScanlineStrength * step(0.5h, frac(cellGrid.y)));
				half flicker = 1.0h + sin(_Time.y * 73.0h + input.uv.y * 194.0h) * _FlickerStrength;
				half3 emission = content * resolvedEmitter * pixelMask * scanline * flicker * _EmissionStrength;
				return half4(emission, 1.0h);
			}
			ENDHLSL
		}
	}
}
