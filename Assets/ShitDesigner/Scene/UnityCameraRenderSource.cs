using System;
using ShitDesigner.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace ShitDesigner.Scene
{
    /// <summary>Default Unity render source for an isolated Scene node. The
    /// camera target and RenderTexture.active are borrowed only for the
    /// built-in synchronous Camera.Render path. URP StandardRequest owns its
    /// destination and leaves the camera state untouched.</summary>
    public sealed class UnityCameraRenderSource : ISceneRenderSource
    {
        public Result<SceneRenderResult> Render(SceneRenderRequest request)
        {
            if (request == null) return Failure("scene.render.request", "Scene render request is required.");
            if (!(request.OutputTarget is RenderTexture target)) return Failure("scene.render.target", "Scene output must be a RenderTexture.");
            if (target == null || !target.IsCreated()) return Failure("scene.render.target", "Scene output RenderTexture must be created.");
            if (target.width != request.Width || target.height != request.Height)
                return Failure("scene.render.descriptor", "Scene output dimensions do not match the prepared surface.");
            if (request.Camera == null) return Failure("scene.render.camera", "Scene camera is required.");
            var viewport = request.Camera.rect;
            if (viewport.width <= 0f || viewport.height <= 0f)
                return Failure("scene.render.viewport", "Scene camera viewport rect must be non-empty.");
            var expectedMask = 1 << request.Layer;
            if (request.Camera.cullingMask != expectedMask)
                return Failure("scene.render.culling", "Scene camera culling must be limited to its leased layer.");
            if (request.Camera.gameObject.layer != request.Layer)
                return Failure("scene.render.layer", "Scene camera is not on its leased layer.");

            var previousTarget = request.Camera.targetTexture;
            var previousActive = RenderTexture.active;
            var useBuiltInCameraRender = GraphicsSettings.currentRenderPipeline == null;
            try
            {
                if (useBuiltInCameraRender)
                {
                    // Built-in pipeline has no SRP render-request support.
                    request.Camera.targetTexture = target;
                    RenderTexture.active = target;
                    request.Camera.Render();
                }
                else
                {
                    var renderRequest = new RenderPipeline.StandardRequest { destination = target };
                    if (!RenderPipeline.SupportsRenderRequest(request.Camera, renderRequest))
                        return Failure("scene.render.request_unsupported", "The active render pipeline does not support StandardRequest for this Scene camera.");
                    RenderPipeline.SubmitRenderRequest(request.Camera, renderRequest);
                }
                return Result<SceneRenderResult>.Success(SceneRenderResult.Success());
            }
            catch (Exception exception)
            {
                return Result<SceneRenderResult>.Failure(new Diagnostic(new DiagnosticCode("scene.render.exception"), Severity.Error, exception.Message, nodeId: request.NodeId, generationId: request.GenerationId, module: "scene", exception: DiagnosticExceptionInfo.FromException(exception)));
            }
            finally
            {
                // URP's StandardRequest owns its destination and temporarily
                // borrows the camera target internally.  Do not pre-populate
                // targetTexture or RenderTexture.active for the SRP path:
                // doing so makes the camera carry an unrelated target into
                // the request and can leave the destination transparent.
                if (useBuiltInCameraRender)
                {
                    request.Camera.targetTexture = previousTarget;
                    RenderTexture.active = previousActive;
                }
            }
        }

        private static Result<SceneRenderResult> Failure(string code, string message)
            => Result<SceneRenderResult>.Failure(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
    }
}
