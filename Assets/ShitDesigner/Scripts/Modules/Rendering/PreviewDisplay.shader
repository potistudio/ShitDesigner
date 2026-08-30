Shader "Hidden/ShitDesigner/PreviewDisplay"
{
	Properties
	{
		_MainTex ("Texture", 2D) = "white" {}
	}
	SubShader
	{
		Tags { "RenderType" = "Transparent" "Queue" = "Overlay" }
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
			float4 _SourceSize;
			float4 _DestinationSize;
			float _DisplayMode;

			struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
			struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

			v2f vert(appdata input)
			{
				v2f output;
				output.vertex = UnityObjectToClipPos(input.vertex);
				output.uv = input.uv;
				return output;
			}

			float4 frag(v2f input) : SV_Target
			{
				float2 uv = input.uv;
				float sourceAspect = _SourceSize.x / max(_SourceSize.y, 1.0);
				float destinationAspect = _DestinationSize.x / max(_DestinationSize.y, 1.0);
				if (_DisplayMode < 0.5)
				{
					float2 fitScale = destinationAspect >= sourceAspect
					? float2(sourceAspect / destinationAspect, 1.0)
					: float2(1.0, destinationAspect / sourceAspect);
					float2 local = (uv - 0.5) / fitScale + 0.5;
					if (any(local < 0.0) || any(local > 1.0)) return float4(0, 0, 0, 0);
						// Clamp to the first/last source texel center. This keeps
					// the first interior Fit pixel from bilinearly mixing
					// with the transparent padding at the boundary.
					uv = clamp(local, _MainTex_TexelSize.xy * 0.5,
					1.0 - _MainTex_TexelSize.xy * 0.5);
				}
				else if (_DisplayMode < 1.5)
				{
					float2 crop = destinationAspect >= sourceAspect
					? float2(1.0, sourceAspect / destinationAspect)
					: float2(destinationAspect / sourceAspect, 1.0);
					uv = (uv - 0.5) * crop + 0.5;
				}
				else
				{
					// A Stretch edge must sample the first/last texel, not
					// the half-texel seam between the image and the sampler
					// border.  D3D12 exposes that seam as a 50/50 edge sample
					// even with Clamp on the RenderTexture object, which
					// loses the source edge color.  Clamping to texel centers
					// makes the GPU path match the defined full-image stretch.
					uv = clamp(uv, _MainTex_TexelSize.xy * 0.5,
					1.0 - _MainTex_TexelSize.xy * 0.5);
				}
				float4 sample = tex2D(_MainTex, uv);
				// The terminal Viewer surface is an opaque display surface;
				// only Fit padding remains transparent so the host can draw
				// its configured background. This also prevents a
				// premultiplied edge sample from producing a half-alpha seam
				// on the first interior pixel.
				sample.a = 1.0;
				return sample;
			}
			ENDHLSL
		}
	}
	FallBack Off
}
