using System.Collections;
using CSharpFunctionalExtensions;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace ShitDesigner.Tests.Scene {
	public sealed class SceneIsolationPlayModeTests {
		private static NodeInstanceId Node(int index) => new NodeInstanceId($"{index + 10:00000000}-0000-4000-8000-000000000000");

		[Test]
		public void PrefabValidation_RequiresOneCameraRecursiveLayerAndCanvasCamera() {
			var root = new GameObject("ScenePrefabRoot");
			try {
				var child = new GameObject("Child");
				child.transform.SetParent(root.transform, false);
				var camera = child.AddComponent<Camera>();
				var additionalCameraData = child.AddComponent<UniversalAdditionalCameraData>();
				additionalCameraData.renderType = CameraRenderType.Base;
				Assert.That(SceneIsolationManager.AssignLayerRecursively(root, 12).IsSuccess, Is.True);
				Assert.That(SceneIsolationManager.ValidatePrefab(root, SceneNodeKind.ThreeD, 12, camera).IsSuccess, Is.True);
				Assert.That(camera.rect.x, Is.EqualTo(0f));
				Assert.That(camera.rect.y, Is.EqualTo(0f));
				Assert.That(camera.rect.width, Is.EqualTo(1f));
				Assert.That(camera.rect.height, Is.EqualTo(1f));
				camera.rect = new Rect(0f, 0f, 0f, 1f);
				var emptyViewport = SceneIsolationManager.ValidatePrefab(root, SceneNodeKind.ThreeD, 12, camera);
				Assert.That(emptyViewport.IsFailure, Is.True);
				Assert.That(emptyViewport.Error.Code.Value, Is.EqualTo("scene.prefab.camera_rect"));
				camera.rect = new Rect(0f, 0f, 1f, 1f);

				var secondCameraObject = new GameObject("SecondCamera");
				secondCameraObject.transform.SetParent(root.transform, false);
				secondCameraObject.AddComponent<Camera>();
				SceneIsolationManager.AssignLayerRecursively(root, 12);
				Assert.That(SceneIsolationManager.ValidatePrefab(root, SceneNodeKind.ThreeD, 12).IsFailure, Is.True);
				Object.DestroyImmediate(secondCameraObject);

				var canvasObject = new GameObject("Canvas");
				canvasObject.transform.SetParent(root.transform, false);
				var canvas = canvasObject.AddComponent<Canvas>();
				canvas.renderMode = RenderMode.ScreenSpaceOverlay;
				SceneIsolationManager.AssignLayerRecursively(root, 12);
				Assert.That(SceneIsolationManager.ValidatePrefab(root, SceneNodeKind.ThreeD, 12, camera).IsFailure, Is.True);
				canvas.renderMode = RenderMode.ScreenSpaceCamera;
				canvas.worldCamera = camera;
				Assert.That(SceneIsolationManager.ValidatePrefab(root, SceneNodeKind.ThreeD, 12, camera).IsSuccess, Is.True);
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}

		[TestCase(SceneNodeKind.ThreeD)]
		[TestCase(SceneNodeKind.TwoD)]
		public void PrefabValidation_RequiresUrpBaseCameraWithoutStack(SceneNodeKind kind) {
			var root = new GameObject("UrpCameraPrefabRoot");
			var overlayObject = new GameObject("ExternalOverlayCamera");
			try {
				var camera = root.AddComponent<Camera>();
				Assert.That(SceneIsolationManager.AssignLayerRecursively(root, 12).IsSuccess, Is.True);

				var missingAdditionalData = SceneIsolationManager.ValidatePrefab(root, kind, 12, camera);
				Assert.That(missingAdditionalData.IsFailure, Is.True);
				Assert.That(missingAdditionalData.Error.Code.Value, Is.EqualTo("scene.prefab.camera_urp"));

				var additionalCameraData = root.AddComponent<UniversalAdditionalCameraData>();
				additionalCameraData.renderType = CameraRenderType.Overlay;
				var overlayResult = SceneIsolationManager.ValidatePrefab(root, kind, 12, camera);
				Assert.That(overlayResult.IsFailure, Is.True);
				Assert.That(overlayResult.Error.Code.Value, Is.EqualTo("scene.prefab.camera_render_type"));

				additionalCameraData.renderType = CameraRenderType.Base;
				Assert.That(SceneIsolationManager.ValidatePrefab(root, kind, 12, camera).IsSuccess, Is.True);

				var overlayCamera = overlayObject.AddComponent<Camera>();
				overlayObject.AddComponent<UniversalAdditionalCameraData>().renderType = CameraRenderType.Overlay;
				Assert.That(additionalCameraData.cameraStack, Is.Not.Null, "The configured URP renderer must expose the Base Camera stack for this contract test.");
				additionalCameraData.cameraStack.Add(overlayCamera);
				var stackedResult = SceneIsolationManager.ValidatePrefab(root, kind, 12, camera);
				Assert.That(stackedResult.IsFailure, Is.True);
				Assert.That(stackedResult.Error.Code.Value, Is.EqualTo("scene.prefab.camera_stack"));
				additionalCameraData.cameraStack.Clear();
			}
			finally {
				Object.DestroyImmediate(overlayObject);
				Object.DestroyImmediate(root);
			}
		}

		[UnityTest]
		public IEnumerator ManagerInstantiatesAndValidatesPrefabHierarchy() {
			var prefab = new GameObject("PrefabSource");
			var cameraObject = new GameObject("PrefabCamera");
			cameraObject.transform.SetParent(prefab.transform, false);
			cameraObject.AddComponent<Camera>();
			cameraObject.AddComponent<UniversalAdditionalCameraData>().renderType = CameraRenderType.Base;
			var manager = new SceneIsolationManager();
			var created = manager.Create(new SceneCreateRequest(Node(8), SceneNodeKind.ThreeD, "SceneIsolation.Prefab", prefab: prefab));
			Assert.That(created.IsSuccess, Is.True);
			Assert.That(created.Value.Root.GetComponentsInChildren<Camera>(true).Length, Is.EqualTo(1));
			foreach (var transform in created.Value.Root.GetComponentsInChildren<Transform>(true))
				Assert.That(transform.gameObject.layer, Is.EqualTo(created.Value.Layer));
			created.Value.Dispose();
			Object.DestroyImmediate(prefab);
			yield return WaitForDisposed(created.Value);

			var invalidPrefab = new GameObject("InvalidPrefabSource");
			var firstCamera = new GameObject("CameraA");
			firstCamera.transform.SetParent(invalidPrefab.transform, false);
			firstCamera.AddComponent<Camera>();
			var secondCamera = new GameObject("CameraB");
			secondCamera.transform.SetParent(invalidPrefab.transform, false);
			secondCamera.AddComponent<Camera>();
			var rejected = manager.Create(new SceneCreateRequest(Node(9), SceneNodeKind.ThreeD, "SceneIsolation.InvalidPrefab", prefab: invalidPrefab));
			Assert.That(rejected.IsFailure, Is.True);
			Object.DestroyImmediate(invalidPrefab);
			yield return WaitForLayers(manager);
			Assert.That(manager.Layers.ActiveCount, Is.EqualTo(0));
			manager.Dispose();
		}

		[UnityTest]
		public IEnumerator CreateRejectsNonBaseUrpCameraAndCleansLayerForBothSceneKinds() {
			var manager = new SceneIsolationManager();
			var kinds = new[] { SceneNodeKind.ThreeD, SceneNodeKind.TwoD };
			for (var index = 0; index < kinds.Length; index++) {
				var prefab = new GameObject("InvalidUrpCameraPrefab");
				var cameraObject = new GameObject("Camera");
				cameraObject.transform.SetParent(prefab.transform, false);
				cameraObject.AddComponent<Camera>();
				cameraObject.AddComponent<UniversalAdditionalCameraData>().renderType = CameraRenderType.Overlay;
				var sceneName = "SceneIsolation.InvalidUrpCamera." + kinds[index];
				var rejected = manager.Create(new SceneCreateRequest(Node(20 + index), kinds[index], sceneName, prefab: prefab));
				Assert.That(rejected.IsFailure, Is.True);
				Assert.That(rejected.Error.Code.Value, Is.EqualTo("scene.create.failed"));
				Object.DestroyImmediate(prefab);

				yield return WaitForLayers(manager);
				Assert.That(manager.ActiveNodeCount, Is.EqualTo(0));
				var failedScene = SceneManager.GetSceneByName(sceneName);
				Assert.That(!failedScene.IsValid() || !failedScene.isLoaded, Is.True, "Failed Scene creation must unload its additive Scene.");
			}
			manager.Dispose();
			Assert.That(manager.Layers.ActiveCount, Is.EqualTo(0));
		}

		[UnityTest]
		public IEnumerator AdditiveScenesOwnDistinctRootsCamerasAndLayers() {
			var manager = new SceneIsolationManager();
			var first = manager.Create(new SceneCreateRequest(Node(0), SceneNodeKind.ThreeD, "SceneIsolation.First"));
			var second = manager.Create(new SceneCreateRequest(Node(1), SceneNodeKind.TwoD, "SceneIsolation.Second"));
			Assert.That(first.IsSuccess, Is.True);
			Assert.That(second.IsSuccess, Is.True);
			Assert.That(first.Value.Scene.IsValid(), Is.True);
			Assert.That(second.Value.Scene.IsValid(), Is.True);
			Assert.That(first.Value.Scene, Is.Not.EqualTo(second.Value.Scene));
			Assert.That(first.Value.Root.scene, Is.EqualTo(first.Value.Scene));
			Assert.That(second.Value.Root.scene, Is.EqualTo(second.Value.Scene));
			Assert.That(first.Value.Camera, Is.Not.Null);
			Assert.That(second.Value.Camera, Is.Not.Null);
			Assert.That(first.Value.Layer, Is.Not.EqualTo(second.Value.Layer));
			Assert.That(first.Value.Camera.cullingMask, Is.EqualTo(1 << first.Value.Layer));
			Assert.That(second.Value.Camera.cullingMask, Is.EqualTo(1 << second.Value.Layer));
			var firstAdditionalData = first.Value.Camera.GetComponent<UniversalAdditionalCameraData>();
			var secondAdditionalData = second.Value.Camera.GetComponent<UniversalAdditionalCameraData>();
			Assert.That(firstAdditionalData, Is.Not.Null);
			Assert.That(secondAdditionalData, Is.Not.Null);
			Assert.That(firstAdditionalData.renderType, Is.EqualTo(CameraRenderType.Base));
			Assert.That(secondAdditionalData.renderType, Is.EqualTo(CameraRenderType.Base));
			Assert.That(firstAdditionalData.cameraStack, Is.Empty);
			Assert.That(secondAdditionalData.cameraStack, Is.Empty);
			yield return null;
			manager.Dispose();
			yield return WaitForDisposed(first.Value, second.Value);
			Assert.That(manager.Layers.ActiveCount, Is.EqualTo(0));
		}

		[UnityTest]
		public IEnumerator PhysicsUsesFixedStepMaximumFourAndCarriesRemainder() {
			var stepper = new RecordingPhysicsStepper();
			var manager = new SceneIsolationManager(physicsStepper: stepper);
			var created = manager.Create(new SceneCreateRequest(Node(2), SceneNodeKind.ThreeD, "SceneIsolation.Physics"));
			Assert.That(created.IsSuccess, Is.True);
			var first = created.Value.AdvancePhysics(.1d);
			Assert.That(first.IsSuccess, Is.True);
			Assert.That(first.Value, Is.EqualTo(4));
			var second = created.Value.AdvancePhysics(0d);
			Assert.That(second.IsSuccess, Is.True);
			Assert.That(second.Value, Is.EqualTo(2));
			Assert.That(stepper.Calls, Is.EqualTo(6));
			created.Value.Dispose();
			yield return WaitForDisposed(created.Value);
			Assert.That(manager.Layers.ActiveCount, Is.EqualTo(0));
		}

		[UnityTest]
		public IEnumerator UnloadCompletionPreventsLayerReuseUntilOldSceneIsDisposed() {
			var manager = new SceneIsolationManager();
			var old = manager.Create(new SceneCreateRequest(Node(3), SceneNodeKind.ThreeD, "SceneIsolation.UnloadOld", generationId: 7));
			Assert.That(old.IsSuccess, Is.True);
			var oldLayer = old.Value.Layer;
			old.Value.Dispose();
			Assert.That(manager.Layers.TryGet(Node(3), 7, out var pending), Is.True);
			Assert.That(pending.GenerationId, Is.EqualTo(7));

			// The same node ID may be recreated while the previous generation
			// is still unloading. The generation is the ownership boundary.
			var replacement = manager.Create(new SceneCreateRequest(Node(3), SceneNodeKind.ThreeD, "SceneIsolation.Replacement", generationId: 8));
			Assert.That(replacement.IsSuccess, Is.True);
			Assert.That(replacement.Value.Layer, Is.Not.EqualTo(oldLayer));
			yield return WaitForDisposed(old.Value);
			Assert.That(manager.Layers.TryGet(Node(3), 8, out var current), Is.True);
			Assert.That(current, Is.SameAs(replacement.Value.LayerLease));

			var reused = manager.Create(new SceneCreateRequest(Node(5), SceneNodeKind.ThreeD, "SceneIsolation.Reused", generationId: 2));
			Assert.That(reused.IsSuccess, Is.True);
			Assert.That(reused.Value.Layer, Is.EqualTo(oldLayer));
			replacement.Value.Dispose();
			reused.Value.Dispose();
			yield return WaitForDisposed(replacement.Value, reused.Value);
			Assert.That(manager.Layers.ActiveCount, Is.EqualTo(0));
		}

		[UnityTest]
		public IEnumerator ManagerCleanupUnloadsAllProjectOwnedScenes() {
			var manager = new SceneIsolationManager();
			var first = manager.Create(new SceneCreateRequest(Node(6), SceneNodeKind.ThreeD, "SceneIsolation.CleanupA"));
			var second = manager.Create(new SceneCreateRequest(Node(7), SceneNodeKind.TwoD, "SceneIsolation.CleanupB"));
			Assert.That(first.IsSuccess && second.IsSuccess, Is.True);
			manager.Dispose();
			Assert.That(manager.ActiveNodeCount, Is.EqualTo(0));
			yield return WaitForDisposed(first.Value, second.Value);
			Assert.That(manager.Layers.ActiveCount, Is.EqualTo(0));
			Assert.That(first.Value.Root == null, Is.True);
			Assert.That(second.Value.Root == null, Is.True);
		}

		private static IEnumerator WaitForDisposed(params SceneNodeRuntime[] nodes) {
			for (var frame = 0; frame < 120; frame++) {
				var allDisposed = true;
				foreach (var node in nodes) allDisposed &= node.State == SceneLifecycleState.Disposed;
				if (allDisposed) yield break;
				yield return null;
			}
			Assert.Fail("Scene unload did not complete within 120 frames.");
		}

		private static IEnumerator WaitForLayers(SceneIsolationManager manager) {
			for (var frame = 0; frame < 120; frame++) {
				if (manager.Layers.ActiveCount == 0) yield break;
				yield return null;
			}
			Assert.Fail("Scene create failure cleanup did not return its layer within 120 frames.");
		}

		private sealed class RecordingPhysicsStepper : IScenePhysicsStepper {
			public int Calls { get; private set; }
			public UnitResult<Diagnostic> Simulate(SceneNodeRuntime node, float stepSeconds) {
				Calls++;
				return UnitResult.Success<Diagnostic>();
			}
		}
	}
}
