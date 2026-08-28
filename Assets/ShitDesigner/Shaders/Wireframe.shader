Shader "ShitDesigner/Wireframe"
{
	Properties
	{
		[HDR] _LineColor ("Line Color", Color) = (0.1, 0.8, 1, 1)
		_LineWidth ("Line Width (Pixels)", Range(0.5, 5)) = 1.5
	}

	SubShader
	{
		Tags
		{
			"RenderType" = "Transparent"
			"Queue" = "Transparent"
			"RenderPipeline" = "UniversalPipeline"
		}

		Pass
		{
			Name "Wireframe"
			Tags { "LightMode" = "SRPDefaultUnlit" }
			Blend SrcAlpha OneMinusSrcAlpha
			Cull Off
			ZTest LEqual
			ZWrite On

			HLSLPROGRAM
			#pragma target 4.0
			#pragma require geometry
			#pragma vertex Vert
			#pragma geometry Geom
			#pragma fragment Frag

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

			CBUFFER_START(UnityPerMaterial)
			half4 _LineColor;
			half _LineWidth;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
			};

			struct VertexOutput
			{
				float4 positionHCS : SV_POSITION;
			};

			struct GeometryOutput
			{
				float4 positionHCS : SV_POSITION;
				noperspective float3 barycentric : TEXCOORD0;
			};

			VertexOutput Vert(Attributes input)
			{
				VertexOutput output;
				output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
				return output;
			}

			[maxvertexcount(3)]
			void Geom(triangle VertexOutput input[3], inout TriangleStream<GeometryOutput> output)
			{
				for (uint vertexIndex = 0; vertexIndex < 3; vertexIndex++)
				{
					GeometryOutput vertex;
					vertex.positionHCS = input[vertexIndex].positionHCS;
					vertex.barycentric = vertexIndex == 0
						? float3(1, 0, 0)
						: vertexIndex == 1 ? float3(0, 1, 0) : float3(0, 0, 1);
					output.Append(vertex);
				}
				output.RestartStrip();
			}

			half4 Frag(GeometryOutput input) : SV_Target
			{
				float3 pixelWidth = max(fwidth(input.barycentric), float3(0.0001, 0.0001, 0.0001));
				float edgeDistance = min(
					input.barycentric.x / pixelWidth.x,
					min(input.barycentric.y / pixelWidth.y, input.barycentric.z / pixelWidth.z));
				float coverage = saturate(_LineWidth + 0.5 - edgeDistance);
				clip(coverage - 0.001);
				return half4(_LineColor.rgb, _LineColor.a * coverage);
			}
			ENDHLSL
		}
	}

	Fallback Off
}
