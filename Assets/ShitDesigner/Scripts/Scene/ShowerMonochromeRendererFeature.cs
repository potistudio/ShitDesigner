using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace ShitDesigner.Scene {
	/// <summary>Redraws one rendering layer with an unlit single-color material.</summary>
	public sealed class ShowerMonochromeRendererFeature : ScriptableRendererFeature {
		[Serializable]
		public sealed class Settings {
			public RenderPassEvent renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
			public uint renderingLayerMask = 1u << 8;
			public Color color = Color.white;
		}

		[SerializeField] private Settings settings = new Settings();

		private ShowerMonochromeRenderPass _pass;

		public override void Create() {
			_pass?.Dispose();
			_pass = new ShowerMonochromeRenderPass(settings);
		}

		protected override void Dispose(bool disposing) {
			_pass?.Dispose();
			_pass = null;
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData) {
			if (_pass == null)
				return;

			_pass.Configure(settings);
			renderer.EnqueuePass(_pass);
		}

		private sealed class ShowerMonochromeRenderPass : ScriptableRenderPass, IDisposable {
			private static readonly List<ShaderTagId> ShaderTagIds = new List<ShaderTagId> {
				new ShaderTagId("UniversalForward"),
				new ShaderTagId("UniversalForwardOnly"),
				new ShaderTagId("SRPDefaultUnlit")
			};

			private FilteringSettings _filteringSettings;
			private Material _material;

			private sealed class PassData {
				public RendererListHandle RendererList;
			}

			public ShowerMonochromeRenderPass(Settings settings) {
				Configure(settings);
			}

			public void Configure(Settings settings) {
				renderPassEvent = settings.renderPassEvent;
				_filteringSettings = new FilteringSettings(RenderQueueRange.all, -1, settings.renderingLayerMask);
				if (_material == null) {
					var shader = Shader.Find("Universal Render Pipeline/Unlit");
					if (shader == null)
						return;
					_material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
				}

				_material.SetColor("_BaseColor", settings.color);
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData) {
				if (_material == null)
					return;

				var renderingData = frameData.Get<UniversalRenderingData>();
				var cameraData = frameData.Get<UniversalCameraData>();
				var lightData = frameData.Get<UniversalLightData>();
				var drawingSettings = RenderingUtils.CreateDrawingSettings(
					ShaderTagIds,
					renderingData,
					cameraData,
					lightData,
					cameraData.defaultOpaqueSortFlags);
				drawingSettings.overrideMaterial = _material;
				drawingSettings.overrideMaterialPassIndex = 0;
				var rendererListParameters = new RendererListParams(renderingData.cullResults, drawingSettings, _filteringSettings);

				using var builder = renderGraph.AddRasterRenderPass<PassData>("Shower Monochrome", out var passData);
				passData.RendererList = renderGraph.CreateRendererList(rendererListParameters);
				if (!passData.RendererList.IsValid())
					return;

				var resourceData = frameData.Get<UniversalResourceData>();
				builder.UseRendererList(passData.RendererList);
				builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
				builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Write);
				builder.AllowPassCulling(false);
				builder.SetRenderFunc(static (PassData data, RasterGraphContext context) => context.cmd.DrawRendererList(data.RendererList));
			}

			public void Dispose() {
				if (_material == null)
					return;
				CoreUtils.Destroy(_material);
				_material = null;
			}
		}
	}
}
