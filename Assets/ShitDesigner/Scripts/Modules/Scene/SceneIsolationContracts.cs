using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ShitDesigner.Core;
using ShitDesigner.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace ShitDesigner.Scene {
	public enum SceneNodeKind {
		ThreeD,
		TwoD
	}

	public enum SceneLifecycleState {
		Preparing,
		Ready,
		Retiring,
		Disposed
	}

	/// <summary>Receives the graph clock for deterministic animation immediately before an isolated Scene is rendered.</summary>
	public interface ISceneGraphClockReceiver {
		void SetGraphClockDriven(bool graphClockDriven);
		void AdvanceGraphClock(double deltaSeconds);
	}

	/// <summary>Receives the shared tempo clock immediately before an isolated Scene is rendered.</summary>
	public interface IBpmClockReceiver {
		void SetBpmClock(BeatClockFrame frame);
	}

	/// <summary>Receives explicit transitions between prepared and presented Scene states.</summary>
	public interface ISceneActivationReceiver {
		void ActivateScene();
		void DeactivateScene();
	}

	/// <summary>One of the reserved user layers 8..31. Releasing the lease is
	/// deliberately separate from destroying a node; the Scene manager returns
	/// it only after unload has completed.</summary>
	public sealed class SceneLayerLease : IDisposable {
		private readonly Action<SceneLayerLease> _release;
		private bool _released;

		public NodeInstanceId NodeId { get; }
		public ulong GenerationId { get; }
		public int Layer { get; }
		public bool IsReleased => _released;

		internal SceneLayerLease(NodeInstanceId nodeId, ulong generationId, int layer, Action<SceneLayerLease> release) {
			NodeId = nodeId;
			GenerationId = generationId;
			Layer = layer;
			_release = release;
		}

		public void Dispose() {
			if (_released) return;
			_released = true;
			_release?.Invoke(this);
		}
	}

	/// <summary>Deterministic pool for the 24 Unity user layers reserved by the
	/// Scene module. It has no Unity object dependency and is used directly by
	/// EditMode tests.</summary>
	public sealed class SceneLayerPool {
		public const int FirstReservedLayer = 8;
		public const int LastReservedLayer = 31;
		public const int Capacity = LastReservedLayer - FirstReservedLayer + 1;
		private readonly struct OwnerKey : IEquatable<OwnerKey> {
			public readonly NodeInstanceId NodeId;
			public readonly ulong GenerationId;
			public OwnerKey(NodeInstanceId nodeId, ulong generationId) { NodeId = nodeId; GenerationId = generationId; }
			public bool Equals(OwnerKey other) => NodeId == other.NodeId && GenerationId == other.GenerationId;
			public override bool Equals(object obj) => obj is OwnerKey other && Equals(other);
			public override int GetHashCode() => HashCode.Combine(NodeId, GenerationId);
		}
		private readonly Dictionary<OwnerKey, SceneLayerLease> _leases = new Dictionary<OwnerKey, SceneLayerLease>();
		private readonly SortedSet<int> _available = new SortedSet<int>(Enumerable.Range(FirstReservedLayer, Capacity));

		public int ActiveCount => _leases.Count;
		public int AvailableCount => _available.Count;
		public IReadOnlyCollection<int> AvailableLayers => new ReadOnlyCollection<int>(_available.ToList());

		public Result<SceneLayerLease, Diagnostic> Acquire(NodeInstanceId nodeId) => Acquire(nodeId, 1);

		public Result<SceneLayerLease, Diagnostic> Acquire(NodeInstanceId nodeId, ulong generationId) {
			if (nodeId.IsEmpty || generationId == 0) return Failure<SceneLayerLease>("scene.layer.node", "Scene layer owner identity is required.");
			var key = new OwnerKey(nodeId, generationId);
			if (_leases.ContainsKey(key)) return Failure<SceneLayerLease>("scene.layer.duplicate", "The node generation already owns a Scene layer.");
			if (_available.Count == 0) return Failure<SceneLayerLease>("scene.layer.exhausted", "All reserved Scene layers are in use.");
			var layer = _available.Min;
			_available.Remove(layer);
			var lease = new SceneLayerLease(nodeId, generationId, layer, Release);
			_leases.Add(key, lease);
			return Result.Success<SceneLayerLease, Diagnostic>(lease);
		}

		public bool TryGet(NodeInstanceId nodeId, ulong generationId, out SceneLayerLease lease) => _leases.TryGetValue(new OwnerKey(nodeId, generationId), out lease);

		public UnitResult<Diagnostic> Release(NodeInstanceId nodeId, ulong generationId) {
			if (!_leases.TryGetValue(new OwnerKey(nodeId, generationId), out var lease)) return Failure("scene.layer.missing", "Scene layer owner was not found.");
			lease.Dispose();
			return UnitResult.Success<Diagnostic>();
		}

		private void Release(SceneLayerLease lease) {
			// Completion from a retired generation must never release a layer
			// now owned by a newer generation with the same node ID.
			var key = new OwnerKey(lease.NodeId, lease.GenerationId);
			if (_leases.TryGetValue(key, out var current) && ReferenceEquals(current, lease)) {
				_leases.Remove(key);
				_available.Add(lease.Layer);
			}
		}

		private static Result<T, Diagnostic> Failure<T>(string code, string message) => Result.Failure<T, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
	}

	public sealed class SceneCreateRequest {
		public NodeInstanceId NodeId { get; }
		public ulong GenerationId { get; }
		public SceneNodeKind Kind { get; }
		public string Name { get; }
		public GameObject Prefab { get; }
		public bool TransparentBackground { get; }
		public SceneCreateRequest(NodeInstanceId nodeId, SceneNodeKind kind, string name = null, ulong generationId = 1,
			GameObject prefab = null, bool transparentBackground = false) {
			if (nodeId.IsEmpty || generationId == 0) throw new ArgumentException("Scene node identity is required.", nameof(nodeId));
			NodeId = nodeId;
			GenerationId = generationId;
			Kind = kind;
			Name = string.IsNullOrWhiteSpace(name) ? "ShitDesigner.Node." + nodeId.Value : name.Trim();
			Prefab = prefab;
			TransparentBackground = transparentBackground;
		}
	}

	public sealed class SceneRenderRequest {
		public NodeInstanceId NodeId { get; }
		public ulong GenerationId { get; }
		public SceneNodeKind Kind { get; }
		public Camera Camera { get; }
		public int Layer { get; }
		public object OutputTarget { get; }
		public int Width { get; }
		public int Height { get; }
		public ulong FrameNumber { get; }

		public SceneRenderRequest(NodeInstanceId nodeId, SceneNodeKind kind, Camera camera, int layer, object outputTarget, int width, int height, ulong frameNumber, ulong generationId = 1) {
			if (nodeId.IsEmpty || generationId == 0 || camera == null || layer < SceneLayerPool.FirstReservedLayer || layer > SceneLayerPool.LastReservedLayer)
				throw new ArgumentException("Scene render owner, camera and reserved layer are required.");
			if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
			NodeId = nodeId; Kind = kind; GenerationId = generationId; Camera = camera; Layer = layer; OutputTarget = outputTarget; Width = width; Height = height; FrameNumber = frameNumber;
		}
	}

	public sealed class SceneRenderResult {
		public bool Rendered { get; }
		public Diagnostic Diagnostic { get; }
		private SceneRenderResult(bool rendered, Diagnostic diagnostic) { Rendered = rendered; Diagnostic = diagnostic; }
		public static SceneRenderResult Success() => new SceneRenderResult(true, null);
		public static SceneRenderResult Failure(Diagnostic diagnostic) => new SceneRenderResult(false, diagnostic ?? throw new ArgumentNullException(nameof(diagnostic)));
	}

	/// <summary>Rendering is kept outside Scene. Bootstrap may provide a URP
	/// SingleCameraRequest implementation, while tests can use a recording source.</summary>
	public interface ISceneRenderSource {
		Result<SceneRenderResult, Diagnostic> Render(SceneRenderRequest request);
	}

	public interface IScenePhysicsStepper {
		UnitResult<Diagnostic> Simulate(SceneNodeRuntime node, float stepSeconds);
	}

	public sealed class DefaultScenePhysicsStepper : IScenePhysicsStepper {
		public UnitResult<Diagnostic> Simulate(SceneNodeRuntime node, float stepSeconds) {
			if (node == null) return Failure("scene.physics.node", "Scene node is required.");
			if (stepSeconds <= 0f || float.IsNaN(stepSeconds) || float.IsInfinity(stepSeconds)) return Failure("scene.physics.step", "Physics step must be positive and finite.");
			if (!node.IsLoaded) return Failure("scene.physics.unloaded", "Scene is not loaded.");
			if (node.Kind == SceneNodeKind.ThreeD) {
				if (!node.PhysicsScene3D.IsValid()) return Failure("scene.physics.invalid", "Local 3D physics scene is invalid.");
				node.PhysicsScene3D.Simulate(stepSeconds);
			}
			else {
				if (!node.PhysicsScene2D.IsValid()) return Failure("scene.physics.invalid", "Local 2D physics scene is invalid.");
				node.PhysicsScene2D.Simulate(stepSeconds);
			}
			return UnitResult.Success<Diagnostic>();
		}

		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
	}

	public sealed class SceneNodeRuntime : IDisposable {
		private readonly SceneIsolationManager _owner;
		private ISceneGraphClockReceiver[] _graphClockReceivers = Array.Empty<ISceneGraphClockReceiver>();
		private IBpmClockReceiver[] _bpmClockReceivers = Array.Empty<IBpmClockReceiver>();
		private ISceneActivationReceiver[] m_ActivationReceivers = Array.Empty<ISceneActivationReceiver>();
		private bool _destroyRequested;
		private bool m_IsActive;
		private double _physicsAccumulator;

		public NodeInstanceId NodeId { get; }
		public ulong GenerationId { get; }
		public SceneNodeKind Kind { get; }
		public SceneLifecycleState State { get; internal set; }
		public UnityEngine.SceneManagement.Scene Scene { get; internal set; }
		public GameObject Root { get; internal set; }
		public Camera Camera { get; internal set; }
		public SceneLayerLease LayerLease { get; internal set; }
		public int Layer => LayerLease?.Layer ?? -1;
		public bool IsLoaded => Scene.IsValid() && Scene.isLoaded && Root != null && Camera != null;
		public bool IsActive => m_IsActive;
		public PhysicsScene PhysicsScene3D => Scene.GetPhysicsScene();
		public PhysicsScene2D PhysicsScene2D => Scene.GetPhysicsScene2D();

		internal SceneNodeRuntime(SceneIsolationManager owner, SceneCreateRequest request, SceneLayerLease layerLease) {
			_owner = owner;
			NodeId = request.NodeId;
			GenerationId = request.GenerationId;
			Kind = request.Kind;
			LayerLease = layerLease;
			State = SceneLifecycleState.Preparing;
		}

		public Result<SceneRenderResult, Diagnostic> Render(object outputTarget, int width, int height, ulong frameNumber) {
			if (State != SceneLifecycleState.Ready) return Failure<SceneRenderResult>("scene.render.state", "Scene node is not ready for rendering.");
			return _owner.Render(this, outputTarget, width, height, frameNumber);
		}

		public UnitResult<Diagnostic> SimulatePhysics(float stepSeconds) => _owner.SimulatePhysics(this, stepSeconds);

		public void BindGraphClock() {
			var receivers = Root == null ? Array.Empty<MonoBehaviour>() : Root.GetComponentsInChildren<MonoBehaviour>(true);
			_graphClockReceivers = receivers.OfType<ISceneGraphClockReceiver>().ToArray();
			_bpmClockReceivers = receivers.OfType<IBpmClockReceiver>().ToArray();
			m_ActivationReceivers = receivers.OfType<ISceneActivationReceiver>().ToArray();
			foreach (var receiver in _graphClockReceivers) receiver.SetGraphClockDriven(true);
		}

		public UnitResult<Diagnostic> Activate() => SetActive(true);

		public UnitResult<Diagnostic> Deactivate() => SetActive(false);

		public UnitResult<Diagnostic> AdvanceGraphClock(double deltaSeconds) {
			if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
				return AnimationFailure("scene.animation.delta", "Scene animation delta must be finite and non-negative.");
			if (State != SceneLifecycleState.Ready)
				return AnimationFailure("scene.animation.state", "Scene node is not ready for animation.");
			try {
				foreach (var receiver in _graphClockReceivers) receiver.AdvanceGraphClock(deltaSeconds);
				return UnitResult.Success<Diagnostic>();
			}
			catch (Exception exception) {
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("scene.animation.failed"), Severity.Error,
					"A Scene prefab animation failed while advancing the graph clock.", nodeId: NodeId, generationId: GenerationId,
					module: "scene", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		public UnitResult<Diagnostic> ApplyBpmClock(BeatClockFrame frame) {
			if (!frame.IsAvailable || float.IsNaN(frame.Bpm) || float.IsInfinity(frame.Bpm) || frame.Bpm <= 0f
				|| double.IsNaN(frame.TotalBeats) || double.IsInfinity(frame.TotalBeats) || frame.TotalBeats < 0d
				|| double.IsNaN(frame.AdjustedTotalBeats) || double.IsInfinity(frame.AdjustedTotalBeats))
				return AnimationFailure("scene.bpm-clock.state", "BPM clock values must be finite and the BPM must be positive.");
			if (State != SceneLifecycleState.Ready)
				return AnimationFailure("scene.bpm-clock.scene", "Scene node is not ready for BPM animation.");
			try {
				foreach (var receiver in _bpmClockReceivers) receiver.SetBpmClock(frame);
				return UnitResult.Success<Diagnostic>();
			}
			catch (Exception exception) {
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("scene.bpm-clock.failed"), Severity.Error,
					"A Scene prefab animation failed while receiving the BPM clock.", nodeId: NodeId, generationId: GenerationId,
					module: "scene", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		/// <summary>Advances local physics in the same fixed-step cadence as
		/// GraphClock. At most four steps are consumed per evaluation frame;
		/// remaining time stays queued for the next frame.</summary>
		public Result<int, Diagnostic> AdvancePhysics(double deltaSeconds) {
			if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0d)
				return Result.Failure<int, Diagnostic>(new Diagnostic(new DiagnosticCode("scene.physics.delta"), Severity.Error, "Physics delta must be finite and non-negative.", module: "scene"));
			if (State != SceneLifecycleState.Ready) return Result.Failure<int, Diagnostic>(new Diagnostic(new DiagnosticCode("scene.physics.state"), Severity.Error, "Scene node is not ready for physics.", module: "scene"));
			_physicsAccumulator += deltaSeconds;
			var steps = 0;
			// The public cadence is exactly 1/60 s, but FixedStepSeconds is a
			// float because the Unity PhysicsScene APIs take float.  Repeated
			// subtraction of that rounded value can leave an accumulator a
			// few ulps below the next step (for example, 0.1 s leaves the
			// remainder for two steps as 0.033333329...).  Compare with a
			// small, fixed tolerance so the documented fixed-step count is
			// independent of float rounding while still never consuming time
			// that is materially less than one step.
			const double stepTolerance = 1e-8d;
			while (_physicsAccumulator + stepTolerance >= SceneIsolationManager.FixedStepSeconds && steps < 4) {
				var simulated = SimulatePhysics(SceneIsolationManager.FixedStepSeconds);
				if (simulated.IsFailure) return Result.Failure<int, Diagnostic>(simulated.Error);
				_physicsAccumulator -= SceneIsolationManager.FixedStepSeconds;
				if (_physicsAccumulator < 0d && _physicsAccumulator > -stepTolerance) _physicsAccumulator = 0d;
				steps++;
			}
			return Result.Success<int, Diagnostic>(steps);
		}

		public void Dispose() {
			if (_destroyRequested) return;
			_destroyRequested = true;
			if (m_IsActive) SetActive(false);
			_owner.Retire(this);
		}

		private UnitResult<Diagnostic> SetActive(bool active) {
			if (State != SceneLifecycleState.Ready)
				return AnimationFailure("scene.activation.state", "Scene activation requires a ready Scene node.");
			if (m_IsActive == active) return UnitResult.Success<Diagnostic>();
			try {
				foreach (var receiver in m_ActivationReceivers) {
					if (active) receiver.ActivateScene();
					else receiver.DeactivateScene();
				}
				m_IsActive = active;
				return UnitResult.Success<Diagnostic>();
			}
			catch (Exception exception) {
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("scene.activation.failed"), Severity.Error,
					"A Scene prefab failed while changing activation state.", nodeId: NodeId, generationId: GenerationId,
					module: "scene", exception: DiagnosticExceptionInfo.FromException(exception)));
			}
		}

		private static Result<T, Diagnostic> Failure<T>(string code, string message) => Result.Failure<T, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: default(NodeInstanceId), module: "scene"));
		private UnitResult<Diagnostic> AnimationFailure(string code, string message) => UnitResult.Failure<Diagnostic>(
			new Diagnostic(new DiagnosticCode(code), Severity.Error, message, nodeId: NodeId, generationId: GenerationId, module: "scene"));
	}

	/// <summary>Owns Additive Scene, root, camera and layer for every Scene
	/// node. Unload completion returns the layer, preventing a new node from
	/// inheriting objects from an old node during async teardown.</summary>
	public sealed class SceneIsolationManager : IDisposable {
		public const int MaxSceneNodes = 24;
		public const float FixedStepSeconds = 1f / 60f;
		private readonly Dictionary<NodeInstanceId, SceneNodeRuntime> _nodes = new Dictionary<NodeInstanceId, SceneNodeRuntime>();
		private readonly SceneLayerPool _layers;
		private readonly ISceneRenderSource _renderSource;
		private readonly IScenePhysicsStepper _physicsStepper;
		private bool _disposed;

		public int ActiveNodeCount => _nodes.Count;
		public SceneLayerPool Layers => _layers;
		public IReadOnlyCollection<SceneNodeRuntime> Nodes => new ReadOnlyCollection<SceneNodeRuntime>(_nodes.Values.ToList());

		public SceneIsolationManager(SceneLayerPool layers = null, ISceneRenderSource renderSource = null, IScenePhysicsStepper physicsStepper = null) {
			_layers = layers ?? new SceneLayerPool();
			_renderSource = renderSource;
			_physicsStepper = physicsStepper ?? new DefaultScenePhysicsStepper();
		}

		public Result<SceneNodeRuntime, Diagnostic> Create(SceneCreateRequest request) {
			if (_disposed) return Failure<SceneNodeRuntime>("scene.lifecycle.disposed", "Scene isolation manager is disposed.");
			if (request == null) return Failure<SceneNodeRuntime>("scene.create.request", "Scene create request is required.");
			if (_nodes.Count >= MaxSceneNodes) return Failure<SceneNodeRuntime>("scene.node.limit", "At most 24 Scene nodes may exist.");
			if (_nodes.ContainsKey(request.NodeId)) return Failure<SceneNodeRuntime>("scene.node.duplicate", "A Scene node with this ID already exists.");
			var layer = _layers.Acquire(request.NodeId, request.GenerationId);
			if (layer.IsFailure) return Result.Failure<SceneNodeRuntime, Diagnostic>(layer.Error);

			SceneNodeRuntime runtime = null;
			UnityEngine.SceneManagement.Scene createdScene = default(UnityEngine.SceneManagement.Scene);
			try {
				var parameters = new CreateSceneParameters(request.Kind == SceneNodeKind.ThreeD ? LocalPhysicsMode.Physics3D : LocalPhysicsMode.Physics2D);
				var sceneName = request.Name;
				if (SceneManager.GetSceneByName(sceneName).IsValid())
					sceneName += "." + request.NodeId.Value + "." + request.GenerationId;
				createdScene = SceneManager.CreateScene(sceneName, parameters);
				GameObject root;
				Camera camera;
				if (request.Prefab != null) {
					root = UnityEngine.Object.Instantiate(request.Prefab);
					root.name = "NodeRoot";
					SceneManager.MoveGameObjectToScene(root, createdScene);
					var assigned = AssignLayerRecursively(root, layer.Value.Layer);
					if (assigned.IsFailure) throw new InvalidOperationException(assigned.Error.Message);
					var valid = ValidatePrefab(root, request.Kind, layer.Value.Layer);
					if (valid.IsFailure) throw new InvalidOperationException(valid.Error.Message);
					camera = root.GetComponentsInChildren<Camera>(true)[0];
					ConfigureRuntimeCamera(camera);
				}
				else {
					root = new GameObject("NodeRoot");
					SceneManager.MoveGameObjectToScene(root, createdScene);
					root.layer = layer.Value.Layer;
					var cameraObject = new GameObject("NodeCamera");
					SceneManager.MoveGameObjectToScene(cameraObject, createdScene);
					cameraObject.layer = layer.Value.Layer;
					cameraObject.transform.SetParent(root.transform, false);
					camera = cameraObject.AddComponent<Camera>();
					camera.cullingMask = 1 << layer.Value.Layer;
					camera.clearFlags = CameraClearFlags.SolidColor;
					camera.backgroundColor = Color.clear;
					var additionalCameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
					additionalCameraData.renderType = CameraRenderType.Base;
					ConfigureRuntimeCamera(camera);
				}
				if (request.TransparentBackground) ConfigureTransparentCamera(camera);
				runtime = new SceneNodeRuntime(this, request, layer.Value) { Scene = createdScene, Root = root, Camera = camera, State = SceneLifecycleState.Ready };
				_nodes.Add(request.NodeId, runtime);
				return Result.Success<SceneNodeRuntime, Diagnostic>(runtime);
			}
			catch (Exception exception) {
				if (createdScene.IsValid() && createdScene.isLoaded) {
					var unload = SceneManager.UnloadSceneAsync(createdScene);
					if (unload != null) unload.completed += _ => layer.Value.Dispose();
					else layer.Value.Dispose();
				}
				else layer.Value.Dispose();
				return Result.Failure<SceneNodeRuntime, Diagnostic>(new Diagnostic(new DiagnosticCode("scene.create.failed"), Severity.Error, exception.Message, nodeId: request.NodeId, exception: DiagnosticExceptionInfo.FromException(exception), module: "scene"));
			}
		}

		internal Result<SceneRenderResult, Diagnostic> Render(SceneNodeRuntime node, object outputTarget, int width, int height, ulong frameNumber) {
			if (_renderSource == null) return Failure<SceneRenderResult>("scene.render.source", "A Scene render source was not configured.");
			var request = new SceneRenderRequest(node.NodeId, node.Kind, node.Camera, node.Layer, outputTarget, width, height, frameNumber, node.GenerationId);
			return _renderSource.Render(request);
		}

		private static void ConfigureRuntimeCamera(Camera camera) {
			// Isolated nodes live in runtime-created additive Scenes.  Those
			// Scenes do not contain baked occlusion data, so retaining a
			// prefab's editor occlusion setting can cull all ordinary 3D
			// renderers during a URP render request.  The node owns its whole
			// camera space, therefore disabling that optimisation is both
			// deterministic and scoped to this camera.
			// The render source submits this camera explicitly into a graph-owned
			// RenderTexture. Enabling it would also render to Display 0 during the
			// normal PlayerLoop because isolated cameras have no targetTexture.
			camera.enabled = false;
			// Keep the isolated camera's viewport explicit.  Unity 6 stores
			// this as a versioned Rect in prefabs, but enforcing it here also
			// protects runtime-created nodes from malformed/legacy assets.
			camera.rect = new Rect(0f, 0f, 1f, 1f);
			camera.useOcclusionCulling = false;
			camera.forceIntoRenderTexture = true;
			// Camera scene culling is a separate ulong mask from the
			// GameObject layer cullingMask.  The prefab is instantiated while
			// the bootstrap Scene is active and then moved to a newly created
			// additive Scene; keep the camera eligible to draw that runtime
			// Scene while the layer mask still isolates the node's objects.
			camera.overrideSceneCullingMask = ulong.MaxValue;
		}

		private static void ConfigureTransparentCamera(Camera camera) {
			camera.clearFlags = CameraClearFlags.SolidColor;
			camera.backgroundColor = Color.clear;
		}

		internal UnitResult<Diagnostic> SimulatePhysics(SceneNodeRuntime node, float stepSeconds) => _physicsStepper.Simulate(node, stepSeconds);

		/// <summary>Applies a borrowed layer to an entire prefab hierarchy and
		/// keeps camera/light/renderer culling scoped to that layer.</summary>
		public static UnitResult<Diagnostic> AssignLayerRecursively(GameObject root, int layer) {
			if (root == null) return Failure("scene.layer.root", "A Scene hierarchy root is required.");
			if (layer < SceneLayerPool.FirstReservedLayer || layer > SceneLayerPool.LastReservedLayer)
				return Failure("scene.layer.range", "Only reserved Scene layers 8..31 may be assigned.");
			var layerMask = 1 << layer;
			foreach (var transform in root.GetComponentsInChildren<Transform>(true)) transform.gameObject.layer = layer;
			foreach (var camera in root.GetComponentsInChildren<Camera>(true)) {
				camera.cullingMask = layerMask;
				var additionalCameraData = camera.GetComponent<UniversalAdditionalCameraData>();
				if (additionalCameraData != null) additionalCameraData.volumeLayerMask = layerMask;
			}
			foreach (var light in root.GetComponentsInChildren<Light>(true)) light.cullingMask = layerMask;
			foreach (var reflectionProbe in root.GetComponentsInChildren<ReflectionProbe>(true)) reflectionProbe.cullingMask = layerMask;
			return UnitResult.Success<Diagnostic>();
		}

		public static UnitResult<Diagnostic> ValidatePrefab(GameObject root, SceneNodeKind kind, int layer, Camera expectedCamera = null) {
			if (root == null) return Failure("scene.prefab.root", "A Scene prefab root is required.");
			if (layer < SceneLayerPool.FirstReservedLayer || layer > SceneLayerPool.LastReservedLayer)
				return Failure("scene.layer.range", "Only reserved Scene layers 8..31 may be validated.");
			if (root.GetComponentsInChildren<Transform>(true).Any(x => x.gameObject.layer != layer))
				return Failure("scene.prefab.object_layer", "Every Scene prefab object must use its borrowed layer.");
			var cameras = root.GetComponentsInChildren<Camera>(true);
			if (cameras.Length != 1 || (expectedCamera != null && cameras[0] != expectedCamera))
				return Failure("scene.prefab.camera_count", "A Scene prefab must contain exactly one dedicated Camera.");
			var cameraRect = cameras[0].rect;
			if (cameraRect.width <= 0f || cameraRect.height <= 0f)
				return Failure("scene.prefab.camera_rect", "The Scene camera viewport rect must be non-empty.");
			if ((cameras[0].cullingMask & (1 << layer)) == 0 || cameras[0].cullingMask != (1 << layer))
				return Failure("scene.prefab.camera_layer", "The Scene camera culling mask must be limited to its borrowed layer.");
			var additionalCameraData = cameras[0].GetComponent<UniversalAdditionalCameraData>();
			if (additionalCameraData == null)
				return Failure("scene.prefab.camera_urp", "The Scene camera must have UniversalAdditionalCameraData.");
			if (additionalCameraData.renderType != CameraRenderType.Base)
				return Failure("scene.prefab.camera_render_type", "The Scene camera must be a URP Base Camera.");
			if (additionalCameraData.scriptableRenderer == null)
				return Failure("scene.prefab.camera_renderer", "The Scene camera must resolve a URP ScriptableRenderer.");
			var cameraStack = additionalCameraData.cameraStack;
			if (cameraStack != null && cameraStack.Count != 0)
				return Failure("scene.prefab.camera_stack", "The Scene camera must not use a Camera Stack.");
			foreach (var light in root.GetComponentsInChildren<Light>(true))
				if (light.cullingMask != (1 << layer)) return Failure("scene.prefab.light_layer", "Scene lights must be limited to their borrowed layer.");
			foreach (var canvas in root.GetComponentsInChildren<Canvas>(true)) {
				if (canvas.renderMode == RenderMode.ScreenSpaceOverlay) return Failure("scene.prefab.overlay_canvas", "Screen Space - Overlay Canvas is not allowed in an isolated Scene.");
				if (canvas.renderMode == RenderMode.ScreenSpaceCamera && canvas.worldCamera != cameras[0])
					return Failure("scene.prefab.canvas_camera", "Screen Space - Camera Canvas must use the Scene camera.");
			}
			foreach (var panelRenderer in root.GetComponentsInChildren<PanelRenderer>(true))
				if (panelRenderer.panelSettings == null || panelRenderer.panelSettings.renderMode != PanelRenderMode.WorldSpace)
					return Failure("scene.prefab.overlay_panel", "UI Toolkit panels in an isolated Scene must use World Space rendering.");
			return UnitResult.Success<Diagnostic>();
		}

		internal void Retire(SceneNodeRuntime node) {
			if (node == null || !_nodes.Remove(node.NodeId)) return;
			node.State = SceneLifecycleState.Retiring;
			if (node.Camera != null) node.Camera.enabled = false;
			var lease = node.LayerLease;
			node.LayerLease = null;
			var scene = node.Scene;
			var generation = node.GenerationId;
			if (scene.IsValid() && scene.isLoaded) {
				var operation = SceneManager.UnloadSceneAsync(scene);
				if (operation != null) {
					operation.completed += _ => {
						// The completion carries the retiring generation and
						// its exact lease. A late callback can therefore not
						// release a newer lease if the node ID was recreated.
						if (_layers.TryGet(node.NodeId, generation, out var current)
							&& ReferenceEquals(current, lease))
							lease?.Dispose();
						node.State = SceneLifecycleState.Disposed;
					};
					return;
				}
			}
			lease?.Dispose();
			node.State = SceneLifecycleState.Disposed;
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			foreach (var node in _nodes.Values.ToList()) Retire(node);
			_nodes.Clear();
		}

		private static Result<T, Diagnostic> Failure<T>(string code, string message) => Result.Failure<T, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
		private static UnitResult<Diagnostic> Failure(string code, string message) => UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "scene"));
	}
}
