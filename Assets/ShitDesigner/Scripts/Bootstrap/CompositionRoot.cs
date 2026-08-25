using System;
using CSharpFunctionalExtensions;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using ShitDesigner.Application;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Input;
using ShitDesigner.Media;
using ShitDesigner.Nodes;
using ShitDesigner.Persistence;
using ShitDesigner.Presentation;
using ShitDesigner.Project;
using ShitDesigner.Rendering;
using ShitDesigner.Runtime;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

[assembly: InternalsVisibleTo("ShitDesigner.Bootstrap.Tests.EditMode")]
[assembly: InternalsVisibleTo("ShitDesigner.Bootstrap.Tests.PlayMode")]

namespace ShitDesigner.Bootstrap {
	/// <summary>One explicit visual binding set is created for one Runtime
	/// session. The set owns only session resources; catalog assets remain
	/// application-owned and are never destroyed here.</summary>
	public sealed class VisualBindingSet : IDisposable {
		public IReadOnlyList<IRuntimeVisualNodeBinding> Bindings { get; }
		public IReadOnlyList<IDisposable> OwnedResources { get; }
		private bool _disposed;

		public VisualBindingSet(IEnumerable<IRuntimeVisualNodeBinding> bindings, IEnumerable<IDisposable> ownedResources = null) {
			var list = (bindings ?? Enumerable.Empty<IRuntimeVisualNodeBinding>()).Where(x => x != null).ToList();
			if (list.Count == 0) throw new ArgumentException("At least one explicit visual binding is required.", nameof(bindings));
			Bindings = new ReadOnlyCollection<IRuntimeVisualNodeBinding>(list);
			OwnedResources = new ReadOnlyCollection<IDisposable>((ownedResources ?? Enumerable.Empty<IDisposable>()).Where(x => x != null).ToList());
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			for (var i = OwnedResources.Count - 1; i >= 0; i--) {
				try { OwnedResources[i].Dispose(); } catch { }
			}
		}
	}

	/// <summary>Bootstrap-owned factory boundary. Production and deterministic
	/// EditMode Harnesses use this exact contract; only the provider differs.
	/// </summary>
	public interface IVisualBindingProvider {
		Result<VisualBindingSet, Diagnostic> Create(string sessionId);
	}

	/// <summary>Optional read-only ownership projection for concrete providers.
	/// Keeping this separate preserves the narrow production provider factory
	/// contract used by deterministic bootstrap tests.</summary>
	public interface IVisualBindingOwnershipProvider {
		BindingOwnershipSnapshot CaptureOwnership();
	}

	/// <summary>Allocation-free ownership count seam for per-frame Performance
	/// health. The complete snapshot contract remains separate.</summary>
	public interface IVisualBindingPerformanceHealthProvider {
		void CapturePerformanceCounts(out int sceneCount, out int layerCount);
	}

	/// <summary>Optional session policy/context hooks used by the production
	/// provider.  The base provider interface stays small so deterministic
	/// Harness providers do not need Unity or Project references.</summary>
	public interface IVisualBindingPolicy {
		void SetOutputFormatPolicy(IRuntimeOutputFormatPolicy policy);
	}

	public interface IVisualBindingPoolAware {
		bool UsesPool(RenderTexturePool pool);
	}

	public interface IProjectContextAware {
		void SetProjectContext(ProjectDocument document, string projectRoot);
	}

	/// <summary>Builds the seven required bindings from explicit shader,
	/// prefab, Scene and Media services. It never calls Shader.Find,
	/// Resources.FindObjectsOfTypeAll or a project-wide object search.</summary>
	public sealed class ExplicitVisualBindingProvider : IVisualBindingProvider, IVisualBindingOwnershipProvider, IVisualBindingPerformanceHealthProvider, IVisualBindingPolicy, IVisualBindingPoolAware, IProjectContextAware, IDisposable {
		private readonly Func<SceneIsolationManager> _sceneManagerFactory;
		private readonly GameObject _scene3dPrefab;
		private readonly Func<RuntimeNodeCreateInfo, GameObject> _scene3dPrefabResolver;
		private readonly GameObject _scene2dPrefab;
		private readonly ShaderMaterialRegistry _shaders;
		private readonly IVideoBackendFactory _videoBackends;
		private readonly IVideoPrepareResolver _videoResolver;
		private readonly IAssetFlashPrepareResolver _assetFlashResolver;
		private readonly IVideoFrameAdapter _videoFrameAdapter;
		private readonly IVideoGraphicsCapabilities _videoGraphics;
		private readonly RenderTexturePool _pool;
		private readonly Func<SceneNodeRuntime, FrameSnapshot, Action> _sceneParameterApplier;
		private IRuntimeOutputFormatPolicy _formatPolicy = new RuntimeOutputFormatPolicy(RuntimeDynamicRange.Hdr);
		private readonly Action<ProjectDocument, string> _projectContextSetter;
		private readonly IReadOnlyList<IDisposable> _applicationResources;
		private SceneIsolationManager _activeScenes;
		private bool _disposed;

		public ExplicitVisualBindingProvider(
			Func<SceneIsolationManager> sceneManagerFactory,
			GameObject scene3dPrefab,
			GameObject scene2dPrefab,
			ShaderMaterialRegistry shaders,
			IVideoBackendFactory videoBackends,
			IVideoPrepareResolver videoResolver,
			IAssetFlashPrepareResolver assetFlashResolver,
			IVideoFrameAdapter videoFrameAdapter,
			IVideoGraphicsCapabilities videoGraphics,
			RenderTexturePool pool,
			Func<SceneNodeRuntime, FrameSnapshot, Action> sceneParameterApplier = null,
			Action<ProjectDocument, string> projectContextSetter = null,
			IEnumerable<IDisposable> applicationResources = null,
			Func<RuntimeNodeCreateInfo, GameObject> scene3dPrefabResolver = null) {
			_sceneManagerFactory = sceneManagerFactory;
			_scene3dPrefab = scene3dPrefab;
			_scene3dPrefabResolver = scene3dPrefabResolver;
			_scene2dPrefab = scene2dPrefab;
			_shaders = shaders;
			_videoBackends = videoBackends;
			_videoResolver = videoResolver;
			_assetFlashResolver = assetFlashResolver;
			_videoFrameAdapter = videoFrameAdapter;
			_videoGraphics = videoGraphics;
			_pool = pool;
			_sceneParameterApplier = sceneParameterApplier;
			_projectContextSetter = projectContextSetter;
			_applicationResources = new ReadOnlyCollection<IDisposable>((applicationResources ?? Enumerable.Empty<IDisposable>()).Where(x => x != null).ToList());
			if (_pool == null) throw new ArgumentNullException(nameof(pool));
		}

		public bool UsesPool(RenderTexturePool pool) => ReferenceEquals(_pool, pool);
		public void SetOutputFormatPolicy(IRuntimeOutputFormatPolicy policy) => _formatPolicy = policy ?? throw new ArgumentNullException(nameof(policy));
		public void SetProjectContext(ProjectDocument document, string projectRoot) => _projectContextSetter?.Invoke(document, projectRoot);

		public BindingOwnershipSnapshot CaptureOwnership() {
			var scenes = _activeScenes;
			return scenes == null
				? new BindingOwnershipSnapshot(0, 0)
				: new BindingOwnershipSnapshot(scenes.ActiveNodeCount, scenes.Layers?.ActiveCount ?? 0);
		}

		public void CapturePerformanceCounts(out int sceneCount, out int layerCount) {
			var scenes = _activeScenes;
			sceneCount = scenes?.ActiveNodeCount ?? 0;
			layerCount = scenes?.Layers?.ActiveCount ?? 0;
		}

		public Result<VisualBindingSet, Diagnostic> Create(string sessionId) {
			if (_disposed) return Failure("bootstrap.binding.disposed", "The production visual binding provider is disposed.");
			if (string.IsNullOrWhiteSpace(sessionId)) return Failure("bootstrap.binding.session", "A session ID is required.");
			if (_sceneManagerFactory == null || _scene3dPrefab == null || _scene2dPrefab == null)
				return Failure("bootstrap.binding.scene_missing", "Explicit 3D/2D prefabs and a Scene manager factory are required.");
			if (_shaders == null || !_shaders.TryGet("builtin.shader.generator", out _)
				|| !_shaders.TryGet("builtin.shader.effect", out _)
				|| !_shaders.TryGet("builtin.shader.blend2", out _))
				return Failure("bootstrap.binding.shader_missing", "All three builtin shader/material bindings are required.");
			if (_videoBackends == null || _videoFrameAdapter == null || _videoResolver == null || _assetFlashResolver == null)
				return Failure("bootstrap.binding.video_missing", "Unity/Hap backend, verified video/flash media resolvers, and frame adapter are required.");

			SceneIsolationManager scenes;
			try { scenes = _sceneManagerFactory(); }
			catch (Exception exception) { return Failure("bootstrap.binding.scene_factory", exception.Message, exception); }
			if (scenes == null) return Failure("bootstrap.binding.scene_factory", "Scene manager factory returned null.");
			_activeScenes = scenes;

			var bindings = new List<IRuntimeVisualNodeBinding>();
			var owned = new List<IDisposable> { scenes };
			try {
				Func<RuntimeNodeCreateInfo, GameObject> prefab = node => node == null ? null
					: node.TypeId.Value == "shitdesigner.scene.3d" ? (_scene3dPrefabResolver == null ? _scene3dPrefab : _scene3dPrefabResolver(node))
					: node.TypeId.Value == "shitdesigner.scene.2d" ? _scene2dPrefab : null;
				bindings.Add(new SceneVisualNodeBinding(new NodeTypeId("shitdesigner.scene.3d"), SceneNodeKind.ThreeD, scenes, prefab));
				bindings.Add(new SceneVisualNodeBinding(new NodeTypeId("shitdesigner.scene.2d"), SceneNodeKind.TwoD, scenes, prefab));
				var manifestShaderBindings = _shaders.Bindings
					.Select(x => x.Descriptor)
					.Where(x => x != null && !x.TypeId.IsEmpty)
					.OrderBy(x => x.TypeId.Value, StringComparer.Ordinal)
					.ToList();
				if (manifestShaderBindings.Count > 0) {
					foreach (var shader in manifestShaderBindings)
						bindings.Add(new ShaderVisualNodeBinding(shader.TypeId, shader.ShaderKey, _shaders,
							pool: _pool, sessionId: sessionId));
				}
				else {
					// Keep the old explicit path for callers that construct a
					// registry manually with legacy ShaderMaterialBinding data.
					bindings.Add(new ShaderVisualNodeBinding(new NodeTypeId("shitdesigner.shader.generator"), "builtin.shader.generator", _shaders, generator: true));
					bindings.Add(new ShaderVisualNodeBinding(new NodeTypeId("shitdesigner.shader.effect"), "builtin.shader.effect", _shaders));
					bindings.Add(new ShaderVisualNodeBinding(new NodeTypeId("shitdesigner.shader.blend2"), "builtin.shader.blend2", _shaders, blend: true));
				}
				bindings.Add(new VideoPlayerVisualNodeBinding(_videoBackends, _videoFrameAdapter, _videoResolver, _videoGraphics));
				bindings.Add(new AssetFlashVisualNodeBinding(_assetFlashResolver, _videoBackends, _videoFrameAdapter, _videoGraphics));
				var feedback = new FeedbackVisualNodeBinding(_pool, sessionId, _formatPolicy);
				if (!feedback.IsAvailable) return Failure("bootstrap.binding.feedback_missing", "Feedback requires the shared RenderTexturePool.");
				bindings.Add(feedback);
				owned.Add(feedback);
				return Result.Success<VisualBindingSet, Diagnostic>(new VisualBindingSet(bindings, owned));
			}
			catch (Exception exception) {
				for (var i = owned.Count - 1; i >= 0; i--) try { owned[i].Dispose(); } catch { }
				_activeScenes = null;
				return Failure("bootstrap.binding.create_failed", exception.Message, exception);
			}
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			_activeScenes = null;
			for (var i = _applicationResources.Count - 1; i >= 0; i--)
				try { _applicationResources[i].Dispose(); } catch { }
		}

		private static Result<VisualBindingSet, Diagnostic> Failure(string code, string message, Exception exception = null) =>
			Result.Failure<VisualBindingSet, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "bootstrap", exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception)));
	}

	/// <summary>Project-root and integrity checked resolver used by Video
	/// nodes. The persisted relative path is the only project-owned path;
	/// the verified absolute path is never copied into Project diagnostics.</summary>
	public sealed class ProjectMediaVideoResolver : IVideoPrepareResolver {
		private readonly Func<ProjectDocument> _document;
		private readonly Func<string> _projectRoot;
		private readonly IProjectFileSystem _fileSystem;
		private readonly IVideoCapabilityProbe _probe;

		public ProjectMediaVideoResolver(Func<ProjectDocument> document, Func<string> projectRoot, IProjectFileSystem fileSystem, IVideoCapabilityProbe probe) {
			_document = document ?? throw new ArgumentNullException(nameof(document));
			_projectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));
			_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
			_probe = probe ?? throw new ArgumentNullException(nameof(probe));
		}

		public Result<VideoPrepareRequest, Diagnostic> Resolve(MediaAssetId mediaAssetId) {
			var document = _document();
			if (document == null || mediaAssetId.IsEmpty) return Failure("media.resolve.asset", "A selected media asset is required.");
			var asset = document.MediaAssets.FirstOrDefault(x => x.Id == mediaAssetId);
			if (asset == null) return Failure("media.resolve.missing", "The selected media asset is not in the project manifest.");
			var relative = MediaPathRules.Normalize(asset.Id, asset.RelativePath);
			if (relative.IsFailure) return Failure("media.resolve.path", "The media asset path is invalid.");
			var root = _projectRoot();
			if (string.IsNullOrWhiteSpace(root)) return Failure("media.resolve.root", "The project root is unavailable.");
			var fullRoot = Path.GetFullPath(root);
			var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative.Value.Replace('/', Path.DirectorySeparatorChar)));
			if (!IsContained(fullRoot, fullPath) || IsReparsePoint(fullPath)) return Failure("media.resolve.containment", "The media asset is outside the project root or is a reparse point.");
			if (!_fileSystem.Exists(fullPath)) return Failure("media.resolve.missing_file", "The project media file is missing.");
			try {
				using (var stream = (_fileSystem as IProjectStreamingFileOperations)?.OpenRead(fullPath) ?? File.OpenRead(fullPath)) {
					if (stream.Length != asset.ByteSize || !string.Equals(AssetIntegrity.Hash(stream), asset.IntegrityHash, StringComparison.Ordinal))
						return Failure("media.resolve.integrity", "The project media file failed its manifest integrity check.");
				}
				var probe = _probe.Probe(fullPath);
				if (probe.IsFailure) return Result.Failure<VideoPrepareRequest, Diagnostic>(probe.Error);
				return Result.Success<VideoPrepareRequest, Diagnostic>(new VideoPrepareRequest(fullPath, probe.Value));
			}
			catch (Exception exception) { return Failure("media.resolve.failed", "The project media file could not be prepared.", exception); }
		}

		private static bool IsContained(string root, string path) {
			var normalizedRoot = root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
			return path.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
		}

		private bool IsReparsePoint(string path) {
			try { return (_fileSystem.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; } catch { return false; }
		}

		private static Result<VideoPrepareRequest, Diagnostic> Failure(string code, string message, Exception exception = null) => Result.Failure<VideoPrepareRequest, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media", exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception)));
	}

	/// <summary>Resolves both still images and videos through the same project
	/// containment and integrity boundary used by the video player.</summary>
	public sealed class ProjectAssetFlashResolver : IAssetFlashPrepareResolver {
		private readonly Func<ProjectDocument> _document;
		private readonly Func<string> _projectRoot;
		private readonly IProjectFileSystem _fileSystem;
		private readonly IVideoPrepareResolver _videos;

		public ProjectAssetFlashResolver(Func<ProjectDocument> document, Func<string> projectRoot,
			IProjectFileSystem fileSystem, IVideoPrepareResolver videos) {
			_document = document ?? throw new ArgumentNullException(nameof(document));
			_projectRoot = projectRoot ?? throw new ArgumentNullException(nameof(projectRoot));
			_fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
			_videos = videos ?? throw new ArgumentNullException(nameof(videos));
		}

		public Result<AssetFlashPrepareRequest, Diagnostic> Resolve(MediaAssetId mediaAssetId) {
			var document = _document();
			if (document == null || mediaAssetId.IsEmpty) return Failure("media.flash.resolve.asset", "A selected media asset is required.");
			var asset = document.MediaAssets.FirstOrDefault(x => x.Id == mediaAssetId);
			if (asset == null) return Failure("media.flash.resolve.missing", "The selected media asset is not in the project manifest.");
			if (asset.Kind == MediaAssetKind.Video) {
				var video = _videos.Resolve(mediaAssetId);
				return video.IsFailure ? Result.Failure<AssetFlashPrepareRequest, Diagnostic>(video.Error)
					: Result.Success<AssetFlashPrepareRequest, Diagnostic>(AssetFlashPrepareRequest.VideoFile(video.Value));
			}
			if (asset.Kind != MediaAssetKind.Image)
				return Failure("media.flash.resolve.kind", "Asset Flash supports Image and Video media assets only.");

			var relative = MediaPathRules.Normalize(asset.Id, asset.RelativePath);
			if (relative.IsFailure) return Failure("media.flash.resolve.path", "The media asset path is invalid.");
			var root = _projectRoot();
			if (string.IsNullOrWhiteSpace(root)) return Failure("media.flash.resolve.root", "The project root is unavailable.");
			try {
				var fullRoot = Path.GetFullPath(root);
				var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative.Value.Replace('/', Path.DirectorySeparatorChar)));
				var normalizedRoot = fullRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
				if (!fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase)) return Failure("media.flash.resolve.containment", "The media asset is outside the project root.");
				if (!_fileSystem.Exists(fullPath)) return Failure("media.flash.resolve.missing_file", "The project media file is missing.");
				try {
					if ((_fileSystem.GetAttributes(fullPath) & FileAttributes.ReparsePoint) != 0)
						return Failure("media.flash.resolve.containment", "The media asset is a reparse point.");
				}
				catch { }
				byte[] bytes;
				using (var source = (_fileSystem as IProjectStreamingFileOperations)?.OpenRead(fullPath) ?? File.OpenRead(fullPath))
				using (var copy = new MemoryStream()) { source.CopyTo(copy); bytes = copy.ToArray(); }
				using (var integrity = new MemoryStream(bytes, false))
					if (bytes.LongLength != asset.ByteSize || !string.Equals(AssetIntegrity.Hash(integrity), asset.IntegrityHash, StringComparison.Ordinal))
						return Failure("media.flash.resolve.integrity", "The project media file failed its manifest integrity check.");
				var color = asset.ColorSpace == MediaColorSpace.Linear ? VideoColorEncoding.Linear
					: asset.ColorSpace == MediaColorSpace.Rec709 ? VideoColorEncoding.Rec709 : VideoColorEncoding.Srgb;
				var alpha = asset.AlphaMode == MediaAlphaMode.Premultiplied ? VideoAlphaMode.Premultiplied
					: asset.AlphaMode == MediaAlphaMode.Straight ? VideoAlphaMode.Straight : VideoAlphaMode.Opaque;
				return Result.Success<AssetFlashPrepareRequest, Diagnostic>(AssetFlashPrepareRequest.Image(bytes, new VideoFrameConversionMetadata(color, alpha)));
			}
			catch (Exception exception) { return Failure("media.flash.resolve.failed", "The project image could not be prepared.", exception); }
		}

		private static Result<AssetFlashPrepareRequest, Diagnostic> Failure(string code, string message, Exception exception = null)
			=> Result.Failure<AssetFlashPrepareRequest, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "media", exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception)));
	}

	/// <summary>Routes a selected codec to the explicit Unity or Hap factory.
	/// Neither backend performs discovery or changes the other backend's
	/// ownership.</summary>
	public sealed class CompositeVideoBackendFactory : IVideoBackendFactory {
		private readonly IVideoBackendFactory _unity;
		private readonly IVideoBackendFactory _hap;
		public CompositeVideoBackendFactory(IVideoBackendFactory unity, IVideoBackendFactory hap) { _unity = unity; _hap = hap; }
		public Result<IVideoBackendHandle, Diagnostic> Create(NodeInstanceId nodeId, ulong generationId, VideoBackendKind kind) {
			var selected = kind == VideoBackendKind.HapVideoBackend ? _hap : _unity;
			return selected == null
				? Result.Failure<IVideoBackendHandle, Diagnostic>(new Diagnostic(new DiagnosticCode("media.backend.unavailable"), Severity.Error, "The selected video backend is unavailable.", module: "media"))
				: selected.Create(nodeId, generationId, kind);
		}
	}

	/// <summary>Application-lifetime Runtime factory. It creates all
	/// session-scoped services and gives them to Runtime through narrow
	/// contracts; Application never references Rendering, Scene, Media or
	/// Nodes concrete types.</summary>
	public sealed class RuntimeSessionFactory : IApplicationRuntimeSessionFactory, IProjectRootAwareRuntimeSessionFactory {
		private readonly IVisualBindingProvider _provider;
		private readonly RenderTexturePool _pool;
		private readonly OutputSurfaceBridge _surfaceBridge;
		private readonly NodeTypeCatalog _nodeTypeCatalog;
		private string _projectRoot = string.Empty;

		public RenderTexturePool Pool => _pool;
		public string ProjectRoot => _projectRoot;
		public IVisualBindingProvider Provider => _provider;
		public ApplicationRuntimeComposition CurrentComposition { get; private set; }

		public RuntimeSessionFactory(IVisualBindingProvider provider, RenderTexturePool pool, OutputSurfaceBridge surfaceBridge = null, NodeTypeCatalog nodeTypeCatalog = null) {
			_provider = provider ?? throw new ArgumentNullException(nameof(provider));
			_pool = pool ?? throw new ArgumentNullException(nameof(pool));
			_surfaceBridge = surfaceBridge;
			_nodeTypeCatalog = nodeTypeCatalog ?? throw new ArgumentNullException(nameof(nodeTypeCatalog));
		}

		public void SetProjectRoot(string projectRoot) => _projectRoot = projectRoot ?? string.Empty;

		public Result<ApplicationRuntimeComposition, Diagnostic> Create(ProjectDocument document, NodeTypeRegistry registry) {
			if (document == null || registry == null) return Failure("bootstrap.runtime.arguments", "A document and registry are required.");
			var sessionId = Guid.NewGuid().ToString("D");
			VisualBindingSet set = null;
			RuntimeSession session = null;
			ResourceLifecycle lifecycle = null;
			ProgramHoldController programHold = null;
			DefaultImageProvider defaultImages = null;
			var owned = new List<IDisposable>();
			try {
				var formatPolicy = NodeCatalogBootstrap.CreateOutputFormatPolicy(document.Settings);
				var programRange = document.Settings.DynamicRange == ProjectDynamicRange.Ldr ? ProgramDynamicRange.Ldr : ProgramDynamicRange.Hdr;
				var formatValidation = RenderingFormatPolicy.ValidateInternalFormat(programRange, new UnityRenderingPlatformCapabilityPort());
				if (formatValidation.IsFailure) return Result.Failure<ApplicationRuntimeComposition, Diagnostic>(formatValidation.Error);
				if (_provider is IVisualBindingPolicy policyAware) policyAware.SetOutputFormatPolicy(formatPolicy);
				if (_provider is IProjectContextAware contextAware) contextAware.SetProjectContext(document, _projectRoot);
				if (_provider is IVisualBindingPoolAware poolAware && !poolAware.UsesPool(_pool))
					return Failure("bootstrap.runtime.pool_mismatch", "The visual binding provider must use the composition root RenderTexturePool.");
				var created = _provider.Create(sessionId);
				if (created.IsFailure) return Result.Failure<ApplicationRuntimeComposition, Diagnostic>(created.Error);
				set = created.Value;
				var bindingResult = NodeCatalogBootstrap.BuildProductionBindings(set.Bindings);
				if (bindingResult.IsFailure) { set.Dispose(); return Result.Failure<ApplicationRuntimeComposition, Diagnostic>(bindingResult.Error); }
				var bindings = bindingResult.Value;
				var catalogResult = _nodeTypeCatalog.BuildRuntimeCatalog(bindings);
				if (catalogResult.IsFailure) { set.Dispose(); return Result.Failure<ApplicationRuntimeComposition, Diagnostic>(catalogResult.Error); }
				var catalog = catalogResult.Value;
				var definitions = NodeCatalogBootstrap.EnsureDefinitions(catalog, registry);
				if (definitions.IsFailure) { set.Dispose(); return Result.Failure<ApplicationRuntimeComposition, Diagnostic>(definitions.Error); }

				// Program output is a fixed 1920x1080/60 contract. Unity's
				// global targetFrameRate may be zero (platform default) or a
				// value selected for some unrelated host surface; neither is
				// allowed to change the Program target.
				session = new RuntimeSession(document, registry, new DiagnosticHub("runtime." + sessionId), programTargetFramesPerSecond: 60);
				// The runtime owns only narrow policy boundaries. Production
				// injects the Rendering moving-average Preview manager and the
				// actual Program performance monitor here; headless hosts keep
				// the deterministic Runtime fallback implementation.
				session.PreviewQualityPolicy = new PreviewQualityManager();
				session.ProgramPerformanceSink = new ProgramPerformanceMonitor();
				defaultImages = new DefaultImageProvider(_pool, new ResourceOwnerKey(sessionId, ResourceOwnerKind.DefaultImageProvider, "defaults", 1, "default", LeaseRole.DefaultImage),
					document.Settings.DynamicRange == ProjectDynamicRange.Ldr ? RuntimeDynamicRange.Ldr : RuntimeDynamicRange.Hdr);
				var surfaces = new RuntimeOutputSurfaceService(session, _pool, sessionId, formatPolicy);
				programHold = new ProgramHoldController(_pool, new ResourceOwnerKey(sessionId, ResourceOwnerKind.ProgramPresenter, "program", 1, "hold", LeaseRole.ProgramHold), programRange);
				var ensured = programHold.Ensure(1);
				if (ensured.IsFailure) { session.Dispose(); set.Dispose(); programHold.Dispose(); return Result.Failure<ApplicationRuntimeComposition, Diagnostic>(ensured.Error); }
				session.DefaultImageProvider = defaultImages;
				session.OutputSurfaces = surfaces;
				var feedbackBinding = set.Bindings.OfType<FeedbackVisualNodeBinding>().SingleOrDefault();
				lifecycle = new ResourceLifecycle(surfaces, feedbackBinding);
				session.ResourcePreparation = lifecycle;
				session.ResourceFinalization = lifecycle;
				var registered = NodeCatalogBootstrap.RegisterProduction(catalog, registry, session, bindings);
				if (registered.IsFailure) {
					session.Dispose();
					set.Dispose();
					programHold.Dispose();
					return Result.Failure<ApplicationRuntimeComposition, Diagnostic>(registered.Error);
				}
				owned.Add(programHold);
				var compositionResources = new List<IDisposable> { new VisualBindingSetLease(set) };
				compositionResources.AddRange(owned);
				if (_surfaceBridge != null) {
					_surfaceBridge.Bind(session, programHold, _pool);
					compositionResources.Add(new SurfaceBridgeLease(_surfaceBridge));
				}
				var frames = new FrameCoordinator(session);
				var composition = new ApplicationRuntimeComposition(session, frames, true, string.Empty, compositionResources);
				CurrentComposition = composition;
				return Result.Success<ApplicationRuntimeComposition, Diagnostic>(composition);
			}
			catch (Exception exception) {
				try { lifecycle?.Dispose(); } catch { }
				try { session?.Dispose(); } catch { }
				if (defaultImages != null && (session == null || !ReferenceEquals(session.DefaultImageProvider, defaultImages))) try { defaultImages.Dispose(); } catch { }
				set?.Dispose();
				if (programHold != null && !owned.Contains(programHold)) try { programHold.Dispose(); } catch { }
				for (var i = owned.Count - 1; i >= 0; i--) try { owned[i].Dispose(); } catch { }
				return Failure("bootstrap.runtime.create_failed", "Production runtime composition could not be created.", exception);
			}
		}

		private static Result<ApplicationRuntimeComposition, Diagnostic> Failure(string code, string message, Exception exception = null) =>
			Result.Failure<ApplicationRuntimeComposition, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "bootstrap", exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception)));

		private sealed class VisualBindingSetLease : IDisposable {
			private VisualBindingSet _set;
			public VisualBindingSetLease(VisualBindingSet set) { _set = set; }
			public void Dispose() { var set = _set; _set = null; set?.Dispose(); }
		}

		private sealed class SurfaceBridgeLease : IDisposable {
			private OutputSurfaceBridge _bridge;
			public SurfaceBridgeLease(OutputSurfaceBridge bridge) { _bridge = bridge; }
			public void Dispose() { var bridge = _bridge; _bridge = null; bridge?.Clear(); }
		}

		/// <summary>Runtime exposes one preparation/finalization slot.  This
		/// ordered adapter keeps output surfaces first and Feedback history
		/// second, while the binding set remains the sole owner of Feedback.
		/// It is deliberately not responsible for disposing the binding.</summary>
		private sealed class ResourceLifecycle : IRuntimeResourcePreparationWithPlan, IRuntimeResourceFinalizationWithPlan, IDisposable {
			private readonly RuntimeOutputSurfaceService _surfaces;
			private readonly FeedbackVisualNodeBinding _feedback;
			private bool _disposed;

			public ResourceLifecycle(RuntimeOutputSurfaceService surfaces, FeedbackVisualNodeBinding feedback) {
				_surfaces = surfaces ?? throw new ArgumentNullException(nameof(surfaces));
				_feedback = feedback;
			}

			public UnitResult<Diagnostic> Prepare(FrameSnapshot snapshot) => Prepare(snapshot, null);
			public UnitResult<Diagnostic> Prepare(FrameSnapshot snapshot, FrameEvaluationContext evaluation) {
				if (_disposed) return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.resource.disposed"), Severity.Error, "Production resource lifecycle is disposed.", module: "bootstrap"));
				var surface = _surfaces.Prepare(snapshot, evaluation);
				if (surface.IsFailure) return surface;
				return _feedback == null ? UnitResult.Success<Diagnostic>() : _feedback.Prepare(snapshot, evaluation);
			}

			public UnitResult<Diagnostic> Finalize(FrameSnapshot snapshot, bool frameSucceeded) => Finalize(snapshot, null, frameSucceeded);
			public UnitResult<Diagnostic> Finalize(FrameSnapshot snapshot, FrameEvaluationContext evaluation, bool frameSucceeded) {
				if (_disposed) return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.resource.disposed"), Severity.Error, "Production resource lifecycle is disposed.", module: "bootstrap"));
				return _surfaces.Finalize(snapshot, evaluation, frameSucceeded);
			}

			public void Dispose() {
				if (_disposed) return;
				_disposed = true;
				_surfaces.Dispose();
			}
		}
	}

	/// <summary>Presentation-only bridge. A Presentation lease has no pool
	/// release operation; the owning Runtime/Program Hold keeps the texture
	/// alive until the next Phase-9 or session teardown.</summary>
	public sealed class OutputSurfaceBridge : IOutputSurfaceDescriptorPort, IProgramPresenterPort, IProgramOutputControlPort, ICapabilityProbe, IDisposable {
		private sealed class PreviewDisplaySurface {
			public string SurfaceId;
			public TextureLeaseHandle Lease;
			public ulong SourceFrameNumber;
			public string GraphicsFormat;
			public int Borrowers;
			public bool Retired;
		}

		private sealed class ProgramDisplaySurface {
			public TextureLeaseHandle Lease;
			public int Borrowers;
			public bool Retired;
		}

		private RuntimeSession _session;
		private ProgramHoldController _program;
		private ProgramDisplayPresenter _programPresenter;
		private RenderTexturePool _pool;
		private readonly Shader _displayTransformShader;
		private ProgramDisplaySurface _displaySurface;
		private IRuntimeImageFrameSurface _programSourceOverride;
		private DisplayTransformPass _displayTransform;
		private readonly Dictionary<string, PreviewDisplaySurface> _previewDisplaySurfaces = new Dictionary<string, PreviewDisplaySurface>(StringComparer.Ordinal);
		private readonly List<PreviewDisplaySurface> _retiredPreviewDisplaySurfaces = new List<PreviewDisplaySurface>();
		private readonly List<ProgramDisplaySurface> _retiredProgramDisplaySurfaces = new List<ProgramDisplaySurface>();
		private readonly HashSet<string> _projectPreviewIds = new HashSet<string>(StringComparer.Ordinal);
		private readonly List<string> _removedPreviewSurfaceIds = new List<string>();
		private readonly List<RuntimePreviewOutputSnapshot> _visiblePreviewSnapshots = new List<RuntimePreviewOutputSnapshot>();
		private IReadOnlyList<RuntimePreviewOutputSnapshot> _visiblePreviewSnapshotSource;
		private long _previewTopologyDocumentRevision = long.MinValue;
		private long _previewTopologyGraphRevision = long.MinValue;
		private static readonly string HdrSourceGraphicsFormat = UnityEngine.Experimental.Rendering.GraphicsFormat.R16G16B16A16_SFloat.ToString();
		private ulong _lastFrame = 1;
		private ulong _bindingGeneration;
		private int _activeLeaseCount;
		private int _lastDisplayCount;
		private int _lastRequestedDisplayIndex = int.MinValue;
		private ulong _lastProgramOverrideConsumedFrameNumber;
		private int _requestedDisplayOverride;
		private string _lastDisplayDiagnostic;
		private bool _displayHandshakeAttempted;
		private CapabilityStatus _displayHandshakeStatus;
		private bool _outputActive;
		private string _lastOutputError = string.Empty;
		private bool _disposed;

		public ulong BindingGeneration => _bindingGeneration;
		public int ActiveLeaseCount => _activeLeaseCount;
		internal int PreviewDisplayBlitCount { get; private set; }
		public Diagnostic LastDisplayDiagnostic { get; private set; }
		public int DisplayNumber => _programPresenter == null
			? (_session?.Document?.Settings?.ProgramDisplay ?? ProjectOutputSettings.DefaultProgramDisplay)
			: _programPresenter.Selection.RequestedDisplay + 1;
		public int ConnectedDisplayCount => _programPresenter?.DisplayCount ?? (Display.displays == null || Display.displays.Length == 0 ? 1 : Display.displays.Length);
		public bool IsOutputActive => _outputActive && _programPresenter != null && _programPresenter.IsOutputActive;
		public string LastError => _lastOutputError ?? string.Empty;
		public bool HasProgramSourceOverride => _programSourceOverride != null;
		public ulong ProgramSourceOverrideFrameNumber => _programSourceOverride?.FrameNumber ?? 0;
		public ulong LastProgramOverrideConsumedFrameNumber => _lastProgramOverrideConsumedFrameNumber;
		public event Action<bool> OutputActiveChanged;

		public OutputSurfaceBridge(Shader displayTransformShader) {
			_displayTransformShader = displayTransformShader ?? throw new ArgumentNullException(nameof(displayTransformShader));
		}

		internal void Bind(RuntimeSession session, ProgramHoldController program, RenderTexturePool pool) {
			if (_disposed) throw new ObjectDisposedException(nameof(OutputSurfaceBridge));
			_session = session ?? throw new ArgumentNullException(nameof(session));
			_program = program ?? throw new ArgumentNullException(nameof(program));
			_pool = pool ?? throw new ArgumentNullException(nameof(pool));
			try { _displayTransform = new DisplayTransformPass(_displayTransformShader); }
			catch { _displayTransform = null; }
			EnsureDisplayLease(1);
			_displayHandshakeAttempted = false;
			_requestedDisplayOverride = 0;
			_outputActive = false;
			_lastOutputError = string.Empty;
			unchecked { _bindingGeneration++; }
			if (_bindingGeneration == 0) _bindingGeneration = 1;
			PreviewDisplayBlitCount = 0;
		}

		/// <summary>Discovers and activates the selected Unity display. A
		/// missing external display is a supported degraded mode and keeps the
		/// in-application Program monitor available.</summary>
		public Result<CapabilityStatus, Diagnostic> Handshake() {
			if (_disposed)
				return Result.Failure<CapabilityStatus, Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.handshake.display_disposed"), Severity.Error, "Program display output is disposed.", module: "bootstrap"));
			if (_session == null || _program == null)
				return Result.Failure<CapabilityStatus, Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.handshake.display_unbound"), Severity.Error, "Program display output has no runtime session.", module: "bootstrap"));
			if (_displayHandshakeAttempted) return Result.Success<CapabilityStatus, Diagnostic>(_displayHandshakeStatus);
			_displayHandshakeAttempted = true;
			var unityDisplayIndex = ProgramDisplayPolicy.ToUnityIndex(_session.Document.Settings.ProgramDisplay);
			try {
				_programPresenter = new ProgramDisplayPresenter(_program, new UnityProgramDisplayPort(), unityDisplayIndex);
				_programPresenter.SetOutputActive(_outputActive);
				_lastDisplayCount = _programPresenter.DisplayCount;
				_lastRequestedDisplayIndex = _programPresenter.Selection.RequestedDisplay;
				_displayHandshakeStatus = StatusFor(_programPresenter.Selection);
			}
			catch (Exception exception) {
				_programPresenter = null;
				var diagnostic = new Diagnostic(new DiagnosticCode("rendering.display.bind_failed"), Severity.Warning,
					"The Program external Display could not be activated; the Program monitor remains available.", module: "rendering", exception: DiagnosticExceptionInfo.FromException(exception));
				ReportDisplayDiagnostic(diagnostic);
				_displayHandshakeStatus = CapabilityStatus.Unavailable("display", diagnostic);
			}
			return Result.Success<CapabilityStatus, Diagnostic>(_displayHandshakeStatus);
		}

		public Result<CapabilityStatus, Diagnostic> Probe() {
			if (_disposed)
				return Result.Failure<CapabilityStatus, Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.handshake.display_disposed"), Severity.Error, "Program display output is disposed.", module: "bootstrap"));
			if (_session == null || _program == null)
				return Result.Failure<CapabilityStatus, Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.handshake.display_unbound"), Severity.Error, "Program display output has no runtime session.", module: "bootstrap"));
			if (_programPresenter == null) {
				_displayHandshakeAttempted = false;
				return Handshake();
			}
			RefreshDisplaySelection();
			return Result.Success<CapabilityStatus, Diagnostic>(_displayHandshakeStatus ?? StatusFor(_programPresenter.Selection));
		}

		internal void Sync(ulong frameNumber) {
			if (_disposed || _session == null || _program == null) return;
			if (!_displayHandshakeAttempted) Handshake();
			_lastFrame = Math.Max(1UL, frameNumber);
			if (IsUsableProgramSourceOverride(_programSourceOverride)) {
				if (_program.SubmitAvailable(_programSourceOverride, _lastFrame).IsSuccess)
					_lastProgramOverrideConsumedFrameNumber = _programSourceOverride.FrameNumber;
				else _program.SubmitUnavailable(_lastFrame);
			}
			else {
				_programSourceOverride = null;
				_lastProgramOverrideConsumedFrameNumber = 0;
				var result = _session.LastProgramResult;
				if (result.IsAvailable && result.HasValue && result.Value.IsImageFrame) {
					var source = result.Value.AsImageFrame();
					if (_program.SubmitAvailable(source, _lastFrame).IsFailure) _program.SubmitUnavailable(_lastFrame);
				}
				else _program.SubmitUnavailable(_lastFrame);
			}
			RefreshDisplaySelection();
			var presented = _program.GetFrame(_lastFrame);
			if (presented.IsSuccess && presented.Value.Texture != null && EnsureDisplayLease(_lastFrame).IsSuccess) {
				try {
					if (_displayTransform == null) throw new InvalidOperationException("Display transform shader is unavailable.");
					_displayTransform.Blit(presented.Value.Texture, _displaySurface.Lease.Texture, _program.DisplayMode);
					var displayResult = _programPresenter?.Present(_displaySurface.Lease.Texture);
					if (displayResult.HasValue && displayResult.Value.IsFailure) ReportDisplayDiagnostic(displayResult.Value.Error);
				}
				catch (Exception exception) {
					ReportDisplayDiagnostic(new Diagnostic(new DiagnosticCode("rendering.display.transform_failed"), Severity.Error,
						"The Program display frame could not be converted or presented.", module: "rendering", exception: DiagnosticExceptionInfo.FromException(exception)));
				}
			}
			if (_displayTransform != null) {
				// Hiding a Preview stops demand/presentation, but does not
				// return an acquired texture. Retire only when its graph node
				// is removed; reopen then observes the same texture identity.
				RefreshPreviewTopologyAndRetireRemovedSurfaces();
				var presentedPreviews = _session.LastPresentation.Previews;
				foreach (var previewDemand in VisiblePreviewSnapshots()) {
					if (!NodeInstanceId.TryParse(previewDemand.PreviewId, out var previewId) ||
						presentedPreviews == null || !presentedPreviews.TryGetValue(previewId, out var output) ||
						!output.IsAvailable || !output.HasValue || !output.Value.IsImageFrame) continue;
					var source = output.Value.AsImageFrame() as IRuntimeImageFrameSurface;
					if (!(source?.NativeSurface is RenderTexture texture)) continue;
					// A shared upstream output is evaluated at the Program's
					// merged resolution. The Preview boundary owns the
					// downsampled display texture at its requested quality.
					var preview = EnsurePreviewDisplayLease(previewDemand.PreviewId, previewDemand.Width, previewDemand.Height, _lastFrame);
					if (preview.IsFailure) continue;
					if (preview.Value.SourceFrameNumber == source.FrameNumber) continue;
					try {
						var mode = string.Equals(source.ColorFormat, HdrSourceGraphicsFormat, StringComparison.Ordinal)
							? DisplayTransformMode.HdrAces : DisplayTransformMode.Ldr;
						_displayTransform.Blit(texture, preview.Value.Lease.Texture, mode);
						preview.Value.SourceFrameNumber = source.FrameNumber;
						PreviewDisplayBlitCount++;
					}
					catch { }
				}
			}
		}

		public bool CanActivate(int displayNumber, out string error) {
			if (displayNumber < 1) {
				error = "Program display number must be positive.";
				return false;
			}
			if (displayNumber == 1) {
				error = string.Empty;
				return true;
			}
			if (UnityEngine.Application.isEditor) {
				error = "Unity Editor exposes only Display 1. Run a standalone build to use an external Display.";
				return false;
			}
			var count = ConnectedDisplayCount;
			if (displayNumber > count) {
				error = $"Display {displayNumber} is not connected. Connected displays: {count}.";
				return false;
			}
			error = string.Empty;
			return true;
		}

		public bool SelectDisplay(int displayNumber) {
			if (IsOutputActive) {
				_lastOutputError = "The Program display cannot be changed while output is active.";
				return false;
			}
			if (!CanActivate(displayNumber, out var error)) {
				_lastOutputError = error;
				return false;
			}
			_requestedDisplayOverride = displayNumber;
			_lastOutputError = string.Empty;
			return true;
		}

		public bool SetOutputActive(bool active) {
			if (_programPresenter == null) {
				_lastOutputError = "The Program display is not ready.";
				return false;
			}
			if (active) {
				var displayNumber = _requestedDisplayOverride > 0 ? _requestedDisplayOverride : DisplayNumber;
				if (!CanActivate(displayNumber, out var error)) {
					_lastOutputError = error;
					return false;
				}
				var selected = _programPresenter.SetRequestedDisplay(ProgramDisplayPolicy.ToUnityIndex(displayNumber));
				if (selected.IsFailure) {
					_lastOutputError = selected.Error?.Message ?? "The Program display could not be selected.";
					return false;
				}
				_lastRequestedDisplayIndex = selected.Value.RequestedDisplay;
			}
			_outputActive = active;
			_programPresenter.SetOutputActive(active);
			_lastOutputError = string.Empty;
			OutputActiveChanged?.Invoke(active);
			return true;
		}

		public UnitResult<Diagnostic> SetProgramSourceOverride(IRuntimeImageFrameSurface source) {
			if (_disposed) return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.program_override.disposed"), Severity.Error, "The Program output bridge is disposed.", module: "bootstrap"));
			if (!IsUsableProgramSourceOverride(source)) return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.program_override.invalid"), Severity.Error, "A created 1920x1080 HDR RenderTexture frame is required for the Program output override.", module: "bootstrap"));
			_programSourceOverride = source;
			return UnitResult.Success<Diagnostic>();
		}

		public void ClearProgramSourceOverride(IRuntimeImageFrameSurface source = null) {
			if (source != null && !ReferenceEquals(source, _programSourceOverride)) return;
			_programSourceOverride = null;
			_lastProgramOverrideConsumedFrameNumber = 0;
		}

		private static bool IsUsableProgramSourceOverride(IRuntimeImageFrameSurface source) {
			return source != null
				&& source.Width == ProgramHoldController.ProgramSize.x
				&& source.Height == ProgramHoldController.ProgramSize.y
				&& string.Equals(source.ColorFormat, ProgramHoldController.DefaultColorFormat.ToString(), StringComparison.Ordinal)
				&& source.NativeSurface is RenderTexture texture
				&& texture.IsCreated();
		}

		public void SetVisible(bool visible) {
			if (visible) _programPresenter?.OpenMonitor();
			else _programPresenter?.CloseMonitor();
		}

		public bool TryAcquire(string surfaceId, out OutputSurfaceLease lease) {
			lease = null;
			if (_disposed || string.IsNullOrWhiteSpace(surfaceId)) return false;
			if (string.Equals(surfaceId, "program", StringComparison.Ordinal)) {
				var frame = _program?.GetFrame(_lastFrame);
				if (!frame.HasValue || frame.Value.IsFailure || _displaySurface?.Lease == null || _displaySurface.Lease.IsReleased) return false;
				var value = frame.Value.Value;
				var generation = _displaySurface.Lease.LeaseId.Value;
				var displaySurface = _displaySurface;
				displaySurface.Borrowers++;
				_activeLeaseCount++;
				lease = new OutputSurfaceLease(surfaceId, generation, displaySurface.Lease.Descriptor.Width, displaySurface.Lease.Descriptor.Height, value.FrameNumber, displaySurface.Lease.Texture,
					() => ReleaseProgramLease(displaySurface));
				return true;
			}
			if (!NodeInstanceId.TryParse(surfaceId, out var nodeId) || _session == null) return false;
			if (!IsVisiblePreview(nodeId)) return false;
			if (!_previewDisplaySurfaces.TryGetValue(surfaceId, out var preview) || preview == null || preview.Lease == null || preview.Lease.IsReleased || preview.SourceFrameNumber == 0) return false;
			preview.Borrowers++;
			_activeLeaseCount++;
			lease = new OutputSurfaceLease(surfaceId, preview.Lease.LeaseId.Value, preview.Lease.Descriptor.Width, preview.Lease.Descriptor.Height, preview.SourceFrameNumber, preview.Lease.Texture,
				() => ReleasePreviewLease(preview));
			return true;
		}

		public bool TryDescribe(string surfaceId, out OutputSurfaceDescriptor descriptor) {
			descriptor = default(OutputSurfaceDescriptor);
			if (_disposed || string.IsNullOrWhiteSpace(surfaceId)) return false;
			if (string.Equals(surfaceId, "program", StringComparison.Ordinal)) {
				var frame = _program?.GetFrame(_lastFrame);
				if (!frame.HasValue || frame.Value.IsFailure || _displaySurface?.Lease == null || _displaySurface.Lease.IsReleased) return false;
				var value = frame.Value.Value;
				descriptor = new OutputSurfaceDescriptor(surfaceId, _displaySurface.Lease.LeaseId.Value,
					_displaySurface.Lease.Descriptor.Width, _displaySurface.Lease.Descriptor.Height,
					value.FrameNumber, _displaySurface.Lease.Texture, true);
				return true;
			}
			if (!NodeInstanceId.TryParse(surfaceId, out var nodeId) || _session == null || !IsVisiblePreview(nodeId)) return false;
			if (!_previewDisplaySurfaces.TryGetValue(surfaceId, out var preview) || preview == null || preview.Lease == null || preview.Lease.IsReleased || preview.SourceFrameNumber == 0) return false;
			descriptor = new OutputSurfaceDescriptor(surfaceId, preview.Lease.LeaseId.Value,
				preview.Lease.Descriptor.Width, preview.Lease.Descriptor.Height,
				preview.SourceFrameNumber, preview.Lease.Texture, true);
			return true;
		}

		private void ReleaseProgramLease(ProgramDisplaySurface program) {
			if (program == null) return;
			if (_activeLeaseCount > 0) _activeLeaseCount--;
			if (program.Borrowers > 0) program.Borrowers--;
			if (program.Retired && program.Borrowers == 0) ReleaseRetiredProgramDisplaySurface(program);
		}

		private void ReleasePreviewLease(PreviewDisplaySurface preview) {
			if (preview == null) return;
			if (_activeLeaseCount > 0) _activeLeaseCount--;
			if (preview.Borrowers > 0) preview.Borrowers--;
			if (preview.Retired && preview.Borrowers == 0) ReleaseRetiredPreviewDisplaySurface(preview);
		}

		internal void Clear() {
			_programSourceOverride = null;
			_lastProgramOverrideConsumedFrameNumber = 0;
			_programPresenter?.Dispose();
			_displayTransform?.Dispose();
			_displayTransform = null;
			RetireProgramDisplaySurface(_displaySurface);
			_displaySurface = null;
			foreach (var preview in _previewDisplaySurfaces.Values.ToList()) RetirePreviewDisplaySurface(preview);
			_previewDisplaySurfaces.Clear();
			unchecked { _bindingGeneration++; }
			if (_bindingGeneration == 0) _bindingGeneration = 1;
			_session = null;
			_program = null;
			_pool = null;
			_programPresenter = null;
			_lastDisplayCount = 0;
			_lastRequestedDisplayIndex = int.MinValue;
			_requestedDisplayOverride = 0;
			_displayHandshakeAttempted = false;
			_displayHandshakeStatus = null;
			PreviewDisplayBlitCount = 0;
			_projectPreviewIds.Clear();
			_removedPreviewSurfaceIds.Clear();
			_visiblePreviewSnapshots.Clear();
			_visiblePreviewSnapshotSource = null;
			_previewTopologyDocumentRevision = long.MinValue;
			_previewTopologyGraphRevision = long.MinValue;
		}

		private void RefreshDisplaySelection() {
			if (_programPresenter == null || _session?.Document == null) return;
			var projectDisplay = _session.Document.Settings.ProgramDisplay;
			if (_requestedDisplayOverride > 0 && projectDisplay == _requestedDisplayOverride) _requestedDisplayOverride = 0;
			var requestedIndex = ProgramDisplayPolicy.ToUnityIndex(_requestedDisplayOverride > 0 ? _requestedDisplayOverride : projectDisplay);
			var count = _programPresenter.DisplayCount;
			if (requestedIndex == _lastRequestedDisplayIndex && count == _lastDisplayCount) return;
			var refreshed = _programPresenter.SetRequestedDisplay(requestedIndex);
			_lastDisplayCount = count;
			if (refreshed.IsFailure) {
				ReportDisplayDiagnostic(refreshed.Error);
				_displayHandshakeStatus = CapabilityStatus.Unavailable("display", refreshed.Error);
				return;
			}
			_lastRequestedDisplayIndex = requestedIndex;
			LastDisplayDiagnostic = null;
			_lastDisplayDiagnostic = null;
			_displayHandshakeStatus = StatusFor(refreshed.Value);
		}

		private CapabilityStatus StatusFor(ProgramDisplaySelection selection) {
			if (!selection.UsesProgramMonitor) return CapabilityStatus.Ready("display");
			var diagnostic = new Diagnostic(new DiagnosticCode("rendering.display.external_unavailable"), Severity.Warning,
				"The requested external Display is unavailable; the Program monitor is active.", module: "rendering");
			ReportDisplayDiagnostic(diagnostic);
			return CapabilityStatus.Unavailable("display", diagnostic);
		}

		private void ReportDisplayDiagnostic(Diagnostic diagnostic) {
			if (diagnostic == null) return;
			LastDisplayDiagnostic = diagnostic;
			var key = diagnostic.Code.Value + ":" + diagnostic.Message;
			if (string.Equals(key, _lastDisplayDiagnostic, StringComparison.Ordinal)) return;
			_lastDisplayDiagnostic = key;
			_session?.Diagnostics?.Report(diagnostic);
		}

		private UnitResult<Diagnostic> EnsureDisplayLease(ulong frameNumber) {
			if (_displaySurface?.Lease != null && !_displaySurface.Lease.IsReleased) return UnitResult.Success<Diagnostic>();
			if (_pool == null || _program == null)
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.display.pool_missing"), Severity.Error, "Program display conversion pool is unavailable.", module: "bootstrap"));
			var descriptor = new TextureDescriptor(ProgramHoldController.ProgramSize.x, ProgramHoldController.ProgramSize.y, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm);
			var owner = new ResourceOwnerKey("program-display", ResourceOwnerKind.ProgramPresenter, "program-display", 1, "display", LeaseRole.Output);
			var acquired = _pool.Acquire(descriptor, owner, Math.Max(1UL, frameNumber));
			if (acquired.IsFailure) return UnitResult.Failure<Diagnostic>(acquired.Error);
			_displaySurface = new ProgramDisplaySurface { Lease = acquired.Value };
			return UnitResult.Success<Diagnostic>();
		}

		private Result<PreviewDisplaySurface, Diagnostic> EnsurePreviewDisplayLease(string surfaceId, int width, int height, ulong frameNumber) {
			if (string.IsNullOrWhiteSpace(surfaceId) || width <= 0 || height <= 0)
				return Result.Failure<PreviewDisplaySurface, Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.display.preview_invalid"), Severity.Error, "Preview display surface identity or size is invalid.", module: "bootstrap"));
			if (_previewDisplaySurfaces.TryGetValue(surfaceId, out var existing) && existing != null && existing.Lease != null && !existing.Lease.IsReleased && existing.Lease.Descriptor.Width == width && existing.Lease.Descriptor.Height == height)
				return Result.Success<PreviewDisplaySurface, Diagnostic>(existing);
			if (_pool == null)
				return Result.Failure<PreviewDisplaySurface, Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.display.pool_missing"), Severity.Error, "Preview display conversion pool is unavailable.", module: "bootstrap"));
			var owner = new ResourceOwnerKey("preview-display", ResourceOwnerKind.ProgramPresenter, surfaceId, Math.Max(1UL, _bindingGeneration), "display", LeaseRole.Output);
			var acquired = _pool.Acquire(new TextureDescriptor(width, height, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_UNorm), owner, Math.Max(1UL, frameNumber));
			if (acquired.IsFailure) return Result.Failure<PreviewDisplaySurface, Diagnostic>(acquired.Error);
			var replacement = new PreviewDisplaySurface { SurfaceId = surfaceId, Lease = acquired.Value, GraphicsFormat = acquired.Value.Descriptor.GraphicsFormat.ToString() };
			if (existing != null) RetirePreviewDisplaySurface(existing);
			_previewDisplaySurfaces[surfaceId] = replacement;
			return Result.Success<PreviewDisplaySurface, Diagnostic>(replacement);
		}

		internal bool TryCapturePreviewSurface(string surfaceId, out int width, out int height, out string graphicsFormat, out ulong frameNumber) {
			width = 0;
			height = 0;
			graphicsFormat = string.Empty;
			frameNumber = 0;
			if (_disposed || string.IsNullOrWhiteSpace(surfaceId) ||
				!_previewDisplaySurfaces.TryGetValue(surfaceId, out var preview) || preview == null || preview.Lease == null || preview.Lease.IsReleased || preview.SourceFrameNumber == 0) return false;
			width = preview.Lease.Descriptor.Width;
			height = preview.Lease.Descriptor.Height;
			graphicsFormat = preview.GraphicsFormat;
			frameNumber = preview.SourceFrameNumber;
			return true;
		}

		private void RetirePreviewDisplaySurface(PreviewDisplaySurface preview) {
			if (preview == null || preview.Retired) return;
			preview.Retired = true;
			if (preview.Borrowers == 0) {
				ReleasePreviewDisplaySurface(preview);
				return;
			}
			_retiredPreviewDisplaySurfaces.Add(preview);
		}

		private void RetireProgramDisplaySurface(ProgramDisplaySurface program) {
			if (program == null || program.Retired) return;
			program.Retired = true;
			if (program.Borrowers == 0) {
				ReleaseProgramDisplaySurface(program);
				return;
			}
			_retiredProgramDisplaySurfaces.Add(program);
		}

		private void ReleaseRetiredProgramDisplaySurface(ProgramDisplaySurface program) {
			ReleaseProgramDisplaySurface(program);
			_retiredProgramDisplaySurfaces.Remove(program);
		}

		private static void ReleaseProgramDisplaySurface(ProgramDisplaySurface program) {
			if (program?.Lease != null && !program.Lease.IsReleased) program.Lease.Release();
		}

		private void ReleaseRetiredPreviewDisplaySurface(PreviewDisplaySurface preview) {
			ReleasePreviewDisplaySurface(preview);
			_retiredPreviewDisplaySurfaces.Remove(preview);
		}

		private static void ReleasePreviewDisplaySurface(PreviewDisplaySurface preview) {
			if (preview?.Lease != null && !preview.Lease.IsReleased) preview.Lease.Release();
		}

		private IReadOnlyList<RuntimePreviewOutputSnapshot> VisiblePreviewSnapshots() {
			if (_session == null) return Array.Empty<RuntimePreviewOutputSnapshot>();
			RefreshPreviewTopologyAndRetireRemovedSurfaces();
			var source = _session.CapturePreviewOutputSnapshots();
			if (ReferenceEquals(source, _visiblePreviewSnapshotSource)) return _visiblePreviewSnapshots;
			_visiblePreviewSnapshots.Clear();
			foreach (var snapshot in source)
				if (snapshot != null && _projectPreviewIds.Contains(snapshot.PreviewId)) _visiblePreviewSnapshots.Add(snapshot);
			_visiblePreviewSnapshotSource = source;
			return _visiblePreviewSnapshots;
		}

		private bool IsVisiblePreview(NodeInstanceId nodeId) {
			if (_session == null || nodeId.IsEmpty) return false;
			// Do not issue a stale lease between project mutation and the
			// next Sync. Sync owns retirement; the stable path is a pair of
			// cached membership lookups.
			if (_previewTopologyDocumentRevision != (_session.Document?.DocumentRevision ?? 0L) ||
				_previewTopologyGraphRevision != (_session.GraphEditor?.State?.Revision ?? 0L)) return false;
			return _projectPreviewIds.Contains(nodeId.Value) && _session.IsPreviewRequested(nodeId);
		}

		private void RefreshPreviewTopologyAndRetireRemovedSurfaces() {
			if (_session == null) return;
			var documentRevision = _session.Document?.DocumentRevision ?? 0L;
			var graphRevision = _session.GraphEditor?.State?.Revision ?? 0L;
			if (_previewTopologyDocumentRevision == documentRevision && _previewTopologyGraphRevision == graphRevision) return;

			_projectPreviewIds.Clear();
			foreach (var node in _session.Document?.Nodes ?? Array.Empty<NodeRecord>()) {
				if (node == null || node.TypeId.Value != GraphConstants.PreviewTypeId) continue;
				var graphNode = _session.GraphEditor.State.FindNode(node.Id);
				if (graphNode != null && graphNode.TypeId.Value == GraphConstants.PreviewTypeId) _projectPreviewIds.Add(node.Id.Value);
			}
			_removedPreviewSurfaceIds.Clear();
			foreach (var pair in _previewDisplaySurfaces)
				if (!_projectPreviewIds.Contains(pair.Key)) _removedPreviewSurfaceIds.Add(pair.Key);
			foreach (var surfaceId in _removedPreviewSurfaceIds) {
				RetirePreviewDisplaySurface(_previewDisplaySurfaces[surfaceId]);
				_previewDisplaySurfaces.Remove(surfaceId);
			}
			_visiblePreviewSnapshotSource = null;
			_previewTopologyDocumentRevision = documentRevision;
			_previewTopologyGraphRevision = graphRevision;
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			Clear();
		}
	}

	/// <summary>Explicit order used by Bootstrap's PlayerLoop adapter.</summary>
	public interface IApplicationInputPoller {
		void Poll();
	}

	public interface IApplicationPresentationFrame {
		void Read(ApplicationFrameResult frame);
		void Apply(ApplicationFrameResult frame);
		void Present(ApplicationFrameResult frame);
	}

	/// <summary>Optional external-device boundary. Composition creates the
	/// service; startup performs device discovery and connection explicitly.</summary>
	public interface ICapabilityHandshake {
		Result<CapabilityStatus, Diagnostic> Handshake();
	}

	public interface ICapabilityProbe : ICapabilityHandshake {
		Result<CapabilityStatus, Diagnostic> Probe();
	}

	public sealed class NullApplicationInputPoller : IApplicationInputPoller {
		public void Poll() { }
	}

#if ENABLE_INPUT_SYSTEM
	/// <summary>Production Input System bridge. KeyboardInputRouter owns the
	/// logical mapping and shortcut policy; this adapter only polls once at
	/// the start of the Application LateUpdate.</summary>
	public sealed class UnityKeyboardInputPoller : IApplicationInputPoller {
		private readonly UnityKeyboardAdapter _keyboard;
		public UnityKeyboardInputPoller(ProjectApplication application) {
			if (application == null) throw new ArgumentNullException(nameof(application));
			_keyboard = new UnityKeyboardAdapter(application);
		}
		public void Poll() => _keyboard.Poll();
	}
#endif

	/// <summary>Production desktop input boundary. MIDI callbacks are drained
	/// before the Application frame and the native device is owned here.</summary>
	public sealed class InputPoller : IApplicationInputPoller, ICapabilityProbe, IDisposable {
#if ENABLE_INPUT_SYSTEM
		private readonly UnityKeyboardAdapter _keyboard;
#endif
		private readonly ProjectApplication _application;
		private readonly IMidiInputSource _injectedMidiSource;
		private IMidiInputSource _midiSource;
		private MidiInputRouter _midi;
		private readonly MidiInputManager _midiManager;
		private bool _handshakeComplete;
		private CapabilityStatus _handshakeStatus;
		private bool _disposed;

		public string MidiDeviceName => _midiManager?.DeviceName ?? _midiSource?.DeviceName ?? string.Empty;

		public InputPoller(ProjectApplication application, IMidiInputSource midiSource = null, MidiInputManager midiManager = null) {
			if (application == null) throw new ArgumentNullException(nameof(application));
			_application = application;
#if ENABLE_INPUT_SYSTEM
			_keyboard = new UnityKeyboardAdapter(application);
#endif
			_midiManager = midiManager;
			_injectedMidiSource = midiSource;
		}

		public Result<CapabilityStatus, Diagnostic> Handshake() {
			if (_disposed)
				return Result.Failure<CapabilityStatus, Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.handshake.input_disposed"), Severity.Error, "Production input is disposed.", module: "bootstrap"));
			if (_handshakeComplete) return Result.Success<CapabilityStatus, Diagnostic>(_handshakeStatus);
			_handshakeComplete = true;
			if (_midiManager != null) {
				_midiManager.Configure(_application, _application, _injectedMidiSource);
				_handshakeStatus = ManagerStatus();
				return Result.Success<CapabilityStatus, Diagnostic>(_handshakeStatus);
			}
			if (_injectedMidiSource != null) {
				_midiSource = _injectedMidiSource;
			}
			else TryOpenDefaultMidiSource();
			if (_midiSource != null) _midi = new MidiInputRouter(_application, _midiSource);
			_handshakeStatus = _midiSource == null
				? MidiUnavailable("No MIDI input device is available.")
				: CapabilityStatus.Ready("midi");
			return Result.Success<CapabilityStatus, Diagnostic>(_handshakeStatus);
		}

		public Result<CapabilityStatus, Diagnostic> Probe() {
			if (_disposed)
				return Result.Failure<CapabilityStatus, Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.capability.input_disposed"), Severity.Error, "Production input is disposed.", module: "bootstrap"));
			if (!_handshakeComplete) return Handshake();
			if (_midiManager != null) {
				if (!_midiManager.IsOpen) _midiManager.TryReconnect();
				_handshakeStatus = ManagerStatus();
				return Result.Success<CapabilityStatus, Diagnostic>(_handshakeStatus);
			}
			if (_midiSource is IMidiInputAvailability availability && !availability.IsAvailable) {
				if (_injectedMidiSource == null) _midiSource.Dispose();
				_midiSource = null;
				_midi = null;
			}
			if (_midiSource == null && _injectedMidiSource == null) {
				TryOpenDefaultMidiSource();
				if (_midiSource != null) _midi = new MidiInputRouter(_application, _midiSource);
			}
			_handshakeStatus = _midiSource == null
				? MidiUnavailable("No MIDI input device is available.")
				: CapabilityStatus.Ready("midi");
			return Result.Success<CapabilityStatus, Diagnostic>(_handshakeStatus);
		}

		public void Poll() {
			if (_disposed) return;
			if (!_handshakeComplete) Handshake();
#if ENABLE_INPUT_SYSTEM
			_keyboard.Poll();
#endif
			if (_midiManager != null) _midiManager.Poll();
			else _midi?.Poll();
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			if (_handshakeComplete) {
				if (_midiManager != null) _midiManager.Shutdown();
				else _midiSource?.Dispose();
			}
			_midi = null;
			_midiSource = null;
			_handshakeStatus = null;
		}

		private static CapabilityStatus MidiUnavailable(string message) {
			var diagnostic = new Diagnostic(new DiagnosticCode("input.midi.unavailable"), Severity.Warning,
				string.IsNullOrWhiteSpace(message) ? "MIDI input is unavailable." : message, module: "input");
			return CapabilityStatus.Unavailable("midi", diagnostic);
		}

		private CapabilityStatus ManagerStatus() => _midiManager.IsOpen
			? CapabilityStatus.Ready("midi")
			: string.IsNullOrWhiteSpace(_midiManager.LastError)
				? CapabilityStatus.Deferred("midi")
				: MidiUnavailable(_midiManager.LastError);

		private void TryOpenDefaultMidiSource() {
			try {
				var devices = WindowsMidiInputSource.GetDevices();
				if (devices.Count == 0) return;
				if (WindowsMidiInputSource.TryOpenDefault(out var opened, out var error)) _midiSource = opened;
				else Debug.LogWarning("MIDI input device 0 could not be opened: " + error);
			}
			catch (Exception exception) { Debug.LogWarning("MIDI input discovery failed: " + exception.Message); }
		}
	}

	/// <summary>Pure driver used by both MonoBehaviour production and EditMode
	/// Harnesses. It guards re-entry and performs one Application Tick for
	/// each normal LateUpdate callback. Unity host pacing is configured at
	/// the production boundary; this driver does not add a second scheduler
	/// or accumulate a backlog.</summary>
	public sealed class ApplicationLoopDriverCore {
		public const int ProgramFramesPerSecond = 60;
		// Application and Program use the same requested desktop pacing. The
		// value remains a Unity target request, not a promise about the host's
		// actual refresh cadence.
		public const int HostTargetFramesPerSecond = ProgramFramesPerSecond;
		private readonly ProjectApplication _application;
		private readonly IApplicationInputPoller _input;
		private readonly IApplicationPresentationFrame _presentation;
		private readonly IFrameTimingSource _timingSource;
		private readonly CapabilitySupervisor _capabilities;
		private bool _ticking;
		public int TickCount { get; private set; }
		public bool IsDisposed { get; private set; }
		public FrameTimingDiagnostic FrameTimingDiagnostic =>
			(_timingSource as IFrameTimingDiagnosticsSource)?.LastDiagnostic ?? FrameTimingDiagnostic.Unavailable;

		public ApplicationLoopDriverCore(ProjectApplication application, IApplicationInputPoller input, IApplicationPresentationFrame presentation,
			IFrameTimingSource timingSource = null, CapabilitySupervisor capabilities = null) {
			_application = application ?? throw new ArgumentNullException(nameof(application));
			_input = input ?? throw new ArgumentNullException(nameof(input));
			_presentation = presentation ?? throw new ArgumentNullException(nameof(presentation));
			_timingSource = timingSource;
			_capabilities = capabilities;
		}

		public ApplicationFrameResult LateUpdate(double monotonicTime) {
			if (IsDisposed || _ticking) return null;
			_ticking = true;
			try {
				_capabilities?.Tick(monotonicTime);
				_input.Poll();
				var frame = _application.Tick(monotonicTime);
				if (frame == null) return null;
				TickCount++;
				_presentation.Read(frame);
				_presentation.Apply(frame);
				_presentation.Present(frame);
				// Unity FrameTiming completion is delayed by several frames.
				// A poll that has no completed timing is not an unavailable
				// timing observation for this presentation: publishing one
				// would erase the last completed public sample. Only a new,
				// uniquely completed sample crosses this boundary.
				if (_timingSource != null && _timingSource.TryReadCompleted(frame.FrameNumber, out var timing))
					_application.ObserveFrameTiming(timing);
				return frame;
			}
			finally { _ticking = false; }
		}

		public void Dispose() => IsDisposed = true;
	}

	/// <summary>Unity PlayerLoop adapter. Exactly one instance is expected in
	/// the bootstrap scene; nodes and panels do not own Update/LateUpdate.</summary>
	[DisallowMultipleComponent]
	public sealed class ApplicationLoopDriver : MonoBehaviour {
		private ApplicationLoopDriverCore _core;
		public ApplicationLoopDriverCore Core => _core;
		public void Configure(ApplicationLoopDriverCore core) => _core = core ?? throw new ArgumentNullException(nameof(core));
		public void Disable() {
			_core?.Dispose();
			_core = null;
			enabled = false;
		}
		private void LateUpdate() { _core?.LateUpdate(Time.realtimeSinceStartupAsDouble); }
		private void OnDestroy() { Disable(); }
	}

	/// <summary>Splits the frame lifecycle around the existing Presentation
	/// coordinator while keeping the bridge/read/apply/present order explicit.
	/// </summary>
	public sealed class PresentationFrame : IApplicationPresentationFrame {
		private readonly ApplicationPresentationAdapter _adapter;
		private readonly PresentationCoordinator _coordinator;
		private readonly OutputSurfaceBridge _surfaces;
		private readonly PresentationRoot _root;
		private ulong _frame;

		public PresentationFrame(ApplicationPresentationAdapter adapter, PresentationCoordinator coordinator, OutputSurfaceBridge surfaces, PresentationRoot root = null) {
			_adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
			_coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
			_surfaces = surfaces;
			_root = root;
		}

		public void Read(ApplicationFrameResult frame) {
			_frame = frame == null ? 0 : frame.FrameNumber;
			_surfaces?.Sync(Math.Max(1UL, _frame));
		}

		public void Apply(ApplicationFrameResult frame) => _coordinator.ApplyLatestReadModels(Math.Max(1UL, _frame));

		public void Present(ApplicationFrameResult frame) => _root?.PresentCurrent();
	}

	/// <summary>Unity-only timing boundary. FrameTimingManager is queried
	/// after a Program frame is presented; Runtime receives only the immutable
	/// sample and never touches Unity APIs.</summary>
	public interface IFrameTimingSource {
		bool TryReadCompleted(ulong presentedFrameNumber, out RuntimeFrameTimingSample sample);
	}

	/// <summary>Read-only Player-side evidence for the most recent
	/// FrameTiming poll. It keeps Unity raw values at the Bootstrap boundary;
	/// Runtime and Application continue to receive only immutable samples.
	/// The public CPU sample is a workload critical path, so the total/waiting
	/// CPU values remain here for diagnosis instead of being silently lost.</summary>
	public sealed class FrameTimingDiagnostic {
		public int RawCount { get; }
		public double RawIdentity { get; }
		/// <summary>Unity's total CPU frame time, including pacing waits.</summary>
		public double RawCpuFrameTimeMilliseconds { get; }
		/// <summary>Unity main-thread frame time used to form the workload path.</summary>
		public double RawCpuMainThreadFrameTimeMilliseconds { get; }
		/// <summary>Unity render-thread frame time used to form the workload path.</summary>
		public double RawCpuRenderThreadFrameTimeMilliseconds { get; }
		/// <summary>Unity main-thread Present/target-fps wait time.</summary>
		public double RawCpuMainThreadPresentWaitMilliseconds { get; }
		public double RawGpuMilliseconds { get; }
		public int PendingBefore { get; }
		public int PendingAfter { get; }
		public string Outcome { get; }
		public string CandidateOutcome { get; }
		public ulong PerformanceFrameNumber { get; }
		public string ExceptionType { get; }

		/// <summary>Legacy alias for callers that still use the ambiguous raw
		/// CPU name. It is the total CPU frame time, not the public workload.</summary>
		public double RawCpuMilliseconds => RawCpuFrameTimeMilliseconds;

		public FrameTimingDiagnostic(int rawCount, double rawIdentity, double rawCpuMilliseconds, double rawGpuMilliseconds,
			int pendingBefore, int pendingAfter, string outcome, string candidateOutcome, ulong performanceFrameNumber, string exceptionType = null)
			: this(rawCount, rawIdentity, rawCpuMilliseconds, rawCpuMilliseconds, rawCpuMilliseconds, double.NaN, rawGpuMilliseconds,
				pendingBefore, pendingAfter, outcome, candidateOutcome, performanceFrameNumber, exceptionType) { }

		public FrameTimingDiagnostic(int rawCount, double rawIdentity, double rawCpuFrameTimeMilliseconds,
			double rawCpuMainThreadFrameTimeMilliseconds, double rawCpuRenderThreadFrameTimeMilliseconds,
			double rawCpuMainThreadPresentWaitMilliseconds, double rawGpuMilliseconds, int pendingBefore, int pendingAfter,
			string outcome, string candidateOutcome, ulong performanceFrameNumber, string exceptionType = null) {
			RawCount = Math.Max(0, rawCount); RawIdentity = rawIdentity;
			RawCpuFrameTimeMilliseconds = rawCpuFrameTimeMilliseconds;
			RawCpuMainThreadFrameTimeMilliseconds = rawCpuMainThreadFrameTimeMilliseconds;
			RawCpuRenderThreadFrameTimeMilliseconds = rawCpuRenderThreadFrameTimeMilliseconds;
			RawCpuMainThreadPresentWaitMilliseconds = rawCpuMainThreadPresentWaitMilliseconds;
			RawGpuMilliseconds = rawGpuMilliseconds;
			PendingBefore = Math.Max(0, pendingBefore); PendingAfter = Math.Max(0, pendingAfter); Outcome = outcome ?? "None";
			CandidateOutcome = candidateOutcome ?? "None"; PerformanceFrameNumber = performanceFrameNumber; ExceptionType = exceptionType ?? string.Empty;
		}

		public static FrameTimingDiagnostic Unavailable { get; } = new FrameTimingDiagnostic(0, double.NaN,
			double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, 0, 0, "Unavailable", "None", 0UL);
	}

	internal interface IFrameTimingDiagnosticsSource {
		FrameTimingDiagnostic LastDiagnostic { get; }
	}

	internal enum FrameTimingConsumeOutcome { Completed, Bootstrap, RawInvalid, Duplicate, StaleIdentity, InvalidIdentity, NoPending }

	/// <summary>Correlates Unity's delayed timing completions to their own
	/// earlier presentation boundary. Unity documents a four-frame delay, so
	/// this keeps exactly that finite window. A completion is consumed once;
	/// when Unity never completes the oldest boundary it is emitted once as
	/// an explicit unavailable sample instead of retaining an unbounded queue.
	/// </summary>
	public sealed class FrameTimingCompletionCorrelation {
		public const int CompletionDelayFrames = 4;
		// Unity documents a four-frame result delay, but also documents that
		// a GPU can temporarily be unable to return a result. Keep four
		// normal delay windows as a finite jitter allowance; a missing result
		// still becomes an explicit unavailable completion at this boundary.
		public const int CompletionJitterAllowanceWindows = 3;
		public const int MaximumPendingFrames = CompletionDelayFrames * (1 + CompletionJitterAllowanceWindows);
		private readonly Queue<PresentedTimingBoundary> _pending = new Queue<PresentedTimingBoundary>();
		private readonly HashSet<double> _completedIdentities = new HashSet<double>();
		private readonly Queue<double> _completedIdentityOrder = new Queue<double>();
		private double _lastCompletedPresentationTimestamp = double.NaN;
		private double _lastConsumedIdentity = double.NaN;

		public int PendingCount => _pending.Count;

		/// <summary>FrameTimingManager fills a requested history with the
		/// newest completion at index zero, followed by older completions.
		/// This maps an oldest-first ordinal to that API order without making
		/// the scalar public timing boundary buffer a batch.</summary>
		public static int OldestFirstIndex(int availableCount, int ordinal) {
			if (availableCount <= 0 || ordinal < 0 || ordinal >= availableCount) return -1;
			return availableCount - 1 - ordinal;
		}

		public void RecordPresentation(ulong frameNumber, double timestamp) =>
			_pending.Enqueue(new PresentedTimingBoundary(frameNumber, timestamp));

		/// <summary>Compatibility overload for injected histories that only
		/// expose the old total CPU/GPU pair. New Unity data must use the
		/// workload-aware overload below.</summary>
		public bool TryComplete(double completionIdentity, double cpuMilliseconds, double gpuMilliseconds, out RuntimeFrameTimingSample sample) {
			var outcome = TryConsume(completionIdentity, cpuMilliseconds, cpuMilliseconds, cpuMilliseconds, double.NaN, gpuMilliseconds, out sample);
			return outcome == FrameTimingConsumeOutcome.Completed || outcome == FrameTimingConsumeOutcome.Bootstrap;
		}

		public bool TryComplete(double completionIdentity, double cpuFrameTimeMilliseconds,
			double cpuMainThreadFrameTimeMilliseconds, double cpuRenderThreadFrameTimeMilliseconds,
			double cpuMainThreadPresentWaitMilliseconds, double gpuFrameTimeMilliseconds, out RuntimeFrameTimingSample sample) {
			var outcome = TryConsume(completionIdentity, cpuFrameTimeMilliseconds, cpuMainThreadFrameTimeMilliseconds,
				cpuRenderThreadFrameTimeMilliseconds, cpuMainThreadPresentWaitMilliseconds, gpuFrameTimeMilliseconds, out sample);
			return outcome == FrameTimingConsumeOutcome.Completed || outcome == FrameTimingConsumeOutcome.Bootstrap;
		}

		internal FrameTimingConsumeOutcome TryConsume(double completionIdentity, double cpuMilliseconds, double gpuMilliseconds, out RuntimeFrameTimingSample sample)
			=> TryConsume(completionIdentity, cpuMilliseconds, cpuMilliseconds, cpuMilliseconds, double.NaN, gpuMilliseconds, out sample);

		internal FrameTimingConsumeOutcome TryConsume(double completionIdentity, double cpuFrameTimeMilliseconds,
			double cpuMainThreadFrameTimeMilliseconds, double cpuRenderThreadFrameTimeMilliseconds,
			double cpuMainThreadPresentWaitMilliseconds, double gpuFrameTimeMilliseconds, out RuntimeFrameTimingSample sample) {
			sample = default(RuntimeFrameTimingSample);
			if (completionIdentity <= 0d || double.IsNaN(completionIdentity) || double.IsInfinity(completionIdentity))
				return FrameTimingConsumeOutcome.InvalidIdentity;
			if (_completedIdentities.Contains(completionIdentity)) return FrameTimingConsumeOutcome.Duplicate;
			if (_pending.Count == 0) return FrameTimingConsumeOutcome.NoPending;
			// FrameTiming.frameStartTimestamp is monotonic. Once a newer
			// completion has been consumed, an older raw identity can only
			// be a late result for an already retired boundary; rejecting it
			// keeps its CPU/GPU values from being attached to the oldest
			// still-pending presentation. This is intentionally a one-sided
			// policy: an expired boundary has no Unity identity, and there is
			// no shared token that can prove whether a future identity belongs
			// to that boundary or to the next pending one. Comparing the raw
			// identity to our presentation clock would invent such a mapping.
			if (!double.IsNaN(_lastConsumedIdentity) && completionIdentity <= _lastConsumedIdentity)
				return FrameTimingConsumeOutcome.StaleIdentity;

			var boundary = _pending.Dequeue();
			var delta = double.IsNaN(_lastCompletedPresentationTimestamp) ? double.NaN : boundary.Timestamp - _lastCompletedPresentationTimestamp;
			_lastCompletedPresentationTimestamp = boundary.Timestamp;
			_lastConsumedIdentity = completionIdentity;
			_completedIdentities.Add(completionIdentity);
			_completedIdentityOrder.Enqueue(completionIdentity);
			while (_completedIdentityOrder.Count > MaximumPendingFrames)
				_completedIdentities.Remove(_completedIdentityOrder.Dequeue());
			var cpuWorkloadMilliseconds = ComputeCpuWorkloadMilliseconds(cpuMainThreadFrameTimeMilliseconds, cpuRenderThreadFrameTimeMilliseconds);
			if (!IsPositiveFinite(cpuWorkloadMilliseconds) || !IsPositiveFinite(gpuFrameTimeMilliseconds)) {
				sample = RuntimeFrameTimingSample.Unavailable(boundary.FrameNumber);
				return FrameTimingConsumeOutcome.RawInvalid;
			}
			var fps = delta > 0d && !double.IsInfinity(delta) ? 1d / delta : double.NaN;
			// cpuFrameTimeMilliseconds and cpuMainThreadPresentWaitMilliseconds
			// are intentionally diagnostic-only here. The public CPU value is
			// the critical-path workload, so target-fps/Present waits cannot
			// lower the 99% quality result by inflating it.
			sample = new RuntimeFrameTimingSample(boundary.FrameNumber, fps, cpuWorkloadMilliseconds, gpuFrameTimeMilliseconds);
			return sample.IsAvailable ? FrameTimingConsumeOutcome.Completed : FrameTimingConsumeOutcome.Bootstrap;
		}

		/// <summary>Emits one overdue boundary after completion processing.
		/// The documented four-frame delay plus the finite jitter allowance
		/// bounds retained presentations while still making a missing timing
		/// an explicit unavailable observation.</summary>
		public bool TryExpire(out RuntimeFrameTimingSample sample) {
			sample = default(RuntimeFrameTimingSample);
			if (_pending.Count <= MaximumPendingFrames) return false;
			var expired = _pending.Dequeue();
			// An unavailable presentation still advances cadence. The next
			// valid completion is measured from this immediately preceding
			// boundary, rather than from an older successful completion.
			_lastCompletedPresentationTimestamp = expired.Timestamp;
			sample = RuntimeFrameTimingSample.Unavailable(expired.FrameNumber);
			return true;
		}

		private readonly struct PresentedTimingBoundary {
			internal readonly ulong FrameNumber;
			internal readonly double Timestamp;
			internal PresentedTimingBoundary(ulong frameNumber, double timestamp) { FrameNumber = frameNumber; Timestamp = timestamp; }
		}

		internal static double ComputeCpuWorkloadMilliseconds(double cpuMainThreadFrameTimeMilliseconds, double cpuRenderThreadFrameTimeMilliseconds) {
			var workload = 0d;
			if (IsPositiveFinite(cpuMainThreadFrameTimeMilliseconds)) workload = Math.Max(workload, cpuMainThreadFrameTimeMilliseconds);
			if (IsPositiveFinite(cpuRenderThreadFrameTimeMilliseconds)) workload = Math.Max(workload, cpuRenderThreadFrameTimeMilliseconds);
			return workload > 0d ? workload : double.NaN;
		}

		private static bool IsPositiveFinite(double value) => value > 0d && !double.IsNaN(value) && !double.IsInfinity(value);
	}

	internal readonly struct UnityFrameTimingHistoryEntry {
		internal UnityFrameTimingHistoryEntry(double identity, double cpuMilliseconds, double gpuMilliseconds)
			: this(identity, cpuMilliseconds, cpuMilliseconds, cpuMilliseconds, double.NaN, gpuMilliseconds) { }

		internal UnityFrameTimingHistoryEntry(double identity, double cpuFrameTimeMilliseconds,
			double cpuMainThreadFrameTimeMilliseconds, double cpuRenderThreadFrameTimeMilliseconds,
			double cpuMainThreadPresentWaitMilliseconds, double gpuFrameTimeMilliseconds) {
			Identity = identity;
			CpuFrameTimeMilliseconds = cpuFrameTimeMilliseconds;
			CpuMainThreadFrameTimeMilliseconds = cpuMainThreadFrameTimeMilliseconds;
			CpuRenderThreadFrameTimeMilliseconds = cpuRenderThreadFrameTimeMilliseconds;
			CpuMainThreadPresentWaitMilliseconds = cpuMainThreadPresentWaitMilliseconds;
			GpuFrameTimeMilliseconds = gpuFrameTimeMilliseconds;
		}

		internal double Identity { get; }
		/// <summary>Total CPU time, including pacing waits.</summary>
		internal double CpuFrameTimeMilliseconds { get; }
		/// <summary>Main-thread workload duration.</summary>
		internal double CpuMainThreadFrameTimeMilliseconds { get; }
		/// <summary>Render-thread workload duration.</summary>
		internal double CpuRenderThreadFrameTimeMilliseconds { get; }
		/// <summary>Present/target-fps wait duration.</summary>
		internal double CpuMainThreadPresentWaitMilliseconds { get; }
		internal double GpuFrameTimeMilliseconds { get; }

		// Compatibility aliases for existing injected-history tests.
		internal double CpuMilliseconds => CpuFrameTimeMilliseconds;
		internal double GpuMilliseconds => GpuFrameTimeMilliseconds;
	}

	/// <summary>Unity-only adapter kept behind the source's scalar public
	/// boundary. Tests inject a finite history without exposing this seam to
	/// runtime consumers.</summary>
	internal interface IUnityFrameTimingHistoryReader {
		int CaptureAndRead(UnityFrameTimingHistoryEntry[] destination);
	}

	internal sealed class UnityFrameTimingHistoryReader : IUnityFrameTimingHistoryReader {
		private readonly FrameTiming[] _timings = new FrameTiming[FrameTimingCompletionCorrelation.MaximumPendingFrames];

		public int CaptureAndRead(UnityFrameTimingHistoryEntry[] destination) {
			if (destination == null) throw new ArgumentNullException(nameof(destination));
			FrameTimingManager.CaptureFrameTimings();
			var count = (int)FrameTimingManager.GetLatestTimings((uint)Math.Min(destination.Length, _timings.Length), _timings);
			for (var index = 0; index < count; index++) {
				var timing = _timings[index];
				destination[index] = new UnityFrameTimingHistoryEntry(timing.frameStartTimestamp, timing.cpuFrameTime,
					timing.cpuMainThreadFrameTime, timing.cpuRenderThreadFrameTime, timing.cpuMainThreadPresentWaitTime,
					timing.gpuFrameTime);
			}
			return count;
		}
	}

	public sealed class FrameTimingSource : IFrameTimingSource, IFrameTimingDiagnosticsSource {
		// GetLatestTimings fills index zero with the newest completion and
		// walks backwards. Reading the finite window prevents a one-element
		// poll from skipping recoverable completed timings after GPU jitter.
		private readonly UnityFrameTimingHistoryEntry[] _history = new UnityFrameTimingHistoryEntry[FrameTimingCompletionCorrelation.MaximumPendingFrames];
		private readonly IUnityFrameTimingHistoryReader _historyReader;
		private readonly Func<double> _clock;
		private readonly FrameTimingCompletionCorrelation _correlation = new FrameTimingCompletionCorrelation();
		public FrameTimingDiagnostic LastDiagnostic { get; private set; } = FrameTimingDiagnostic.Unavailable;

		public FrameTimingSource() : this(new UnityFrameTimingHistoryReader()) { }

		internal FrameTimingSource(IUnityFrameTimingHistoryReader historyReader)
			: this(historyReader, () => Time.realtimeSinceStartupAsDouble) { }

		internal FrameTimingSource(IUnityFrameTimingHistoryReader historyReader, Func<double> clock) {
			_historyReader = historyReader ?? throw new ArgumentNullException(nameof(historyReader));
			_clock = clock ?? throw new ArgumentNullException(nameof(clock));
		}

		internal int PendingCount => _correlation.PendingCount;

		public bool TryReadCompleted(ulong presentedFrameNumber, out RuntimeFrameTimingSample sample) {
			sample = default(RuntimeFrameTimingSample);
			var now = _clock();
			var pendingBefore = _correlation.PendingCount;
			// Record every presented frame before attempting completion. A
			// valid delayed completion consumes an older boundary, preserving
			// this one for its own later timing; F1..F4, F5->F1 is the first
			// normal four-frame sequence.
			_correlation.RecordPresentation(presentedFrameNumber, now);
			try {
				var count = _historyReader.CaptureAndRead(_history);
				var candidateOutcome = "None";
				var rawIdentity = double.NaN;
				var rawCpuFrameTime = double.NaN;
				var rawCpuMainThreadFrameTime = double.NaN;
				var rawCpuRenderThreadFrameTime = double.NaN;
				var rawCpuMainThreadPresentWait = double.NaN;
				var rawGpuFrameTime = double.NaN;
				// The Application read model publishes one timing per frame.
				// Select only the oldest unseen completion this poll; later
				// history remains unseen and is reconsidered next poll. This
				// keeps the source and scalar public boundary at rate 1:1.
				for (var ordinal = 0; ordinal < (int)count; ordinal++) {
					var index = FrameTimingCompletionCorrelation.OldestFirstIndex((int)count, ordinal);
					var timing = _history[index];
					rawIdentity = timing.Identity;
					rawCpuFrameTime = timing.CpuFrameTimeMilliseconds;
					rawCpuMainThreadFrameTime = timing.CpuMainThreadFrameTimeMilliseconds;
					rawCpuRenderThreadFrameTime = timing.CpuRenderThreadFrameTimeMilliseconds;
					rawCpuMainThreadPresentWait = timing.CpuMainThreadPresentWaitMilliseconds;
					rawGpuFrameTime = timing.GpuFrameTimeMilliseconds;
					var consumed = _correlation.TryConsume(timing.Identity, timing.CpuFrameTimeMilliseconds,
						timing.CpuMainThreadFrameTimeMilliseconds, timing.CpuRenderThreadFrameTimeMilliseconds,
						timing.CpuMainThreadPresentWaitMilliseconds, timing.GpuFrameTimeMilliseconds, out var completed);
					candidateOutcome = consumed.ToString();
					if (consumed == FrameTimingConsumeOutcome.Completed || consumed == FrameTimingConsumeOutcome.Bootstrap ||
						consumed == FrameTimingConsumeOutcome.RawInvalid) {
						sample = completed;
						LastDiagnostic = new FrameTimingDiagnostic((int)count, rawIdentity, rawCpuFrameTime,
							rawCpuMainThreadFrameTime, rawCpuRenderThreadFrameTime, rawCpuMainThreadPresentWait, rawGpuFrameTime, pendingBefore,
							_correlation.PendingCount, consumed.ToString(), candidateOutcome, sample.FrameNumber);
						return true;
					}
				}
				if (_correlation.TryExpire(out sample)) {
					LastDiagnostic = new FrameTimingDiagnostic((int)count, rawIdentity, rawCpuFrameTime,
						rawCpuMainThreadFrameTime, rawCpuRenderThreadFrameTime, rawCpuMainThreadPresentWait, rawGpuFrameTime, pendingBefore,
						_correlation.PendingCount, "Expired", candidateOutcome, sample.FrameNumber);
					return true;
				}
				LastDiagnostic = new FrameTimingDiagnostic((int)count, rawIdentity, rawCpuFrameTime,
					rawCpuMainThreadFrameTime, rawCpuRenderThreadFrameTime, rawCpuMainThreadPresentWait, rawGpuFrameTime, pendingBefore,
					_correlation.PendingCount, candidateOutcome, candidateOutcome, 0UL);
			}
			catch (Exception exception) {
				// A Unity API failure is not silently ignored: retire one
				// overdue presentation so the public Harness can report the
				// missing completion, while the queue stays bounded.
				if (_correlation.TryExpire(out sample)) {
					LastDiagnostic = new FrameTimingDiagnostic(0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, pendingBefore,
						_correlation.PendingCount, "ApiException", "Expired", sample.FrameNumber, exception.GetType().Name);
					return true;
				}
				LastDiagnostic = new FrameTimingDiagnostic(0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, pendingBefore,
					_correlation.PendingCount, "ApiException", "None", 0UL, exception.GetType().Name);
			}
			return false;
		}
	}

	/// <summary>Full composition root. It owns Application lifetime objects,
	/// the shared pool, input router, presentation bridge and the one Player
	/// Loop driver. Test Harnesses can call Create with fake binding providers
	/// and reuse the same path without Unity scene discovery.</summary>
	public sealed class CompositionRoot : IDisposable {
		public ProjectApplication Application { get; }
		public RuntimeSessionFactory RuntimeFactory { get; }
		public RenderTexturePool RenderPool { get; }
		public OutputSurfaceBridge OutputSurfaces { get; }
		public ApplicationPresentationAdapter PresentationAdapter { get; }
		public PresentationCoordinator Presentation { get; }
		public IPlatformFileInteractionAdapter PlatformFiles { get; }
		public IApplicationInputPoller Input { get; }
		public ApplicationLoopDriverCore Loop { get; }
		public CapabilitySupervisor Capabilities { get; }
		public HandshakeReport LastHandshakeReport { get; private set; }
		private readonly IDisposable _providerLifetime;
		private readonly IDisposable _platformFilesLifetime;
		private bool _disposed;

		/// <summary>Starts a complete composition-authored project without exposing
		/// the interactive graph-editor command surface to Unity components.</summary>
		public UnitResult<Diagnostic> CreateAuthoredProject(ProjectDocument document, string projectRoot) {
			if (_disposed) return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.project.composition_disposed"), Severity.Error, "The composition is disposed.", module: "bootstrap"));
			if (Application.State != ApplicationProjectState.Empty)
				return UnitResult.Failure<Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.project.already_open"), Severity.Error, "An authored project can only be created while no project is open.", module: "bootstrap"));
			var created = Application.NewProject(document, projectRoot, UnsavedChangesDecision.Discard);
			return created.IsSuccess
				? UnitResult.Success<Diagnostic>()
				: UnitResult.Failure<Diagnostic>(created.Diagnostic ?? new Diagnostic(new DiagnosticCode("bootstrap.project.create_failed"), Severity.Error, "The authored project could not be created.", module: "bootstrap"));
		}

		/// <summary>Captures concrete ownership and active output descriptors
		/// from the same production composition that drives the Player. This is
		/// deliberately read-only so a test cannot manufacture a passing
		/// teardown state.</summary>
		public CompositionOwnershipSnapshot CaptureOwnershipSnapshot() {
			var pool = RenderPool?.CaptureOwnershipSnapshot();
			var bindingProvider = RuntimeFactory?.Provider as IVisualBindingOwnershipProvider;
			var binding = bindingProvider?.CaptureOwnership() ?? new BindingOwnershipSnapshot(0, 0);
			var composition = RuntimeFactory?.CurrentComposition;
			var session = composition?.Session;
			var backendCount = 0;
			var nativeContextCount = 0;
			var program = CaptureSurface("program", "Program", session == null ? default(NodeOutputResult) : session.LastProgramResult,
				session?.ProgramTargetFramesPerSecond ?? 0);
			var previews = new List<SurfaceOwnershipSnapshot>();
			if (session != null) {
				foreach (var preview in session.CapturePreviewOutputSnapshots()) {
					if (OutputSurfaces != null && OutputSurfaces.TryCapturePreviewSurface(preview.PreviewId,
						out var width, out var height, out var graphicsFormat, out var frameNumber)) {
						previews.Add(new SurfaceOwnershipSnapshot(preview.PreviewId, "Preview", width, height,
							graphicsFormat, preview.TargetFramesPerSecond, frameNumber));
					}
					else {
						var result = FindPresentedPreview(session, preview.PreviewId);
						previews.Add(CaptureSurface(preview.PreviewId, "Preview", result, preview.TargetFramesPerSecond,
							preview.Width, preview.Height));
					}
				}
				foreach (var handle in session.Nodes.Values) {
					if (!(handle?.Node is VideoPlayerRuntimeNode video)) continue;
					if (video.BackendState != VideoBackendState.Disposed) backendCount++;
					if (video.NativeContextActive) nativeContextCount++;
				}
			}
			return new CompositionOwnershipSnapshot(pool, binding.SceneCount, binding.LayerCount, backendCount,
				nativeContextCount, OutputSurfaces?.ActiveLeaseCount ?? 0, program, previews,
				session == null || session.IsDisposed);
		}

		/// <summary>Captures only the scalar/descriptors required for each
		/// Performance presentation. It intentionally avoids full pool entry
		/// sorting, Runtime Nodes defensive copies, and ownership list builds.
		/// Callers supply preview storage; insufficient capacity is explicit.</summary>
		public bool TryCapturePerformanceHealth(PerformanceSurfaceSnapshot[] previews, out int previewCount, out PerformanceHealthSnapshot health) {
			var composition = RuntimeFactory?.CurrentComposition;
			var session = composition?.Session;
			var requested = session?.CapturePreviewOutputSnapshots();
			var required = requested?.Count ?? 0;
			previewCount = 0;
			var pool = RenderPool;
			var provider = RuntimeFactory?.Provider as IVisualBindingPerformanceHealthProvider;
			var sceneCount = 0;
			var layerCount = 0;
			var backendCount = 0;
			var nativeContextCount = 0;
			provider?.CapturePerformanceCounts(out sceneCount, out layerCount);
			if (session != null) session.CaptureMediaBackendCounts(out backendCount, out nativeContextCount);
			var program = CapturePerformanceSurface("program", session == null ? default(NodeOutputResult) : session.LastProgramResult,
				session?.ProgramTargetFramesPerSecond ?? 0);
			health = new PerformanceHealthSnapshot(pool?.BudgetBytes ?? 0, pool?.LeasedBytes ?? 0, pool?.FreeBytes ?? 0,
				pool?.HighWaterBytes ?? 0, pool?.BudgetWarningActive ?? false, sceneCount, layerCount, backendCount, nativeContextCount,
				OutputSurfaces?.ActiveLeaseCount ?? 0, required, session == null || session.IsDisposed, program);
			if (previews == null || previews.Length < required) return false;

			for (var index = 0; index < required; index++) {
				var preview = requested[index];
				if (OutputSurfaces != null && OutputSurfaces.TryCapturePreviewSurface(preview.PreviewId,
					out var width, out var height, out var graphicsFormat, out var frameNumber)) {
					previews[index] = new PerformanceSurfaceSnapshot(preview.PreviewId, width, height, graphicsFormat,
						preview.TargetFramesPerSecond, frameNumber, true);
				}
				else {
					previews[index] = CapturePerformanceSurface(preview.PreviewId, FindPresentedPreview(session, preview.PreviewId),
						preview.TargetFramesPerSecond, preview.Width, preview.Height);
				}
			}
			previewCount = required;
			return true;
		}

		private static NodeOutputResult FindOutput(RuntimeSession session, string nodeId) {
			if (session == null || string.IsNullOrWhiteSpace(nodeId) || !NodeInstanceId.TryParse(nodeId, out var id)) return default(NodeOutputResult);
			if (!session.OutputResults.TryGetValue(id, out var outputs) || outputs == null || !outputs.TryGetValue(new PortId("image"), out var result)) return default(NodeOutputResult);
			return result;
		}

		private static NodeOutputResult FindPresentedPreview(RuntimeSession session, string nodeId) {
			if (session == null || string.IsNullOrWhiteSpace(nodeId) || !NodeInstanceId.TryParse(nodeId, out var id) ||
				session.LastPresentation.Previews == null || !session.LastPresentation.Previews.TryGetValue(id, out var result)) return default(NodeOutputResult);
			return result;
		}

		private static SurfaceOwnershipSnapshot CaptureSurface(string id, string targetKind, NodeOutputResult result, int targetFps, int width = 0, int height = 0) {
			if (result.IsAvailable && result.HasValue && result.Value.IsImageFrame) {
				var image = result.Value.AsImageFrame();
				return new SurfaceOwnershipSnapshot(id, targetKind, image.Width, image.Height, image.ColorFormat, targetFps, image.FrameNumber);
			}
			return new SurfaceOwnershipSnapshot(id, targetKind, width, height, string.Empty, targetFps, 0);
		}

		private static PerformanceSurfaceSnapshot CapturePerformanceSurface(string id, NodeOutputResult result, int targetFps, int width = 0, int height = 0) {
			if (result.IsAvailable && result.HasValue && result.Value.IsImageFrame) {
				var image = result.Value.AsImageFrame();
				return new PerformanceSurfaceSnapshot(id, image.Width, image.Height, image.ColorFormat, targetFps, image.FrameNumber, true);
			}
			return new PerformanceSurfaceSnapshot(id, width, height, string.Empty, targetFps, 0, false);
		}

		private CompositionRoot(ProjectApplication application, RuntimeSessionFactory runtimeFactory, RenderTexturePool pool, OutputSurfaceBridge surfaces,
			ApplicationPresentationAdapter adapter, PresentationCoordinator presentation, IPlatformFileInteractionAdapter platformFiles, IApplicationInputPoller input,
			ApplicationLoopDriverCore loop, CapabilitySupervisor capabilities, IDisposable providerLifetime, IDisposable platformFilesLifetime) {
			Application = application; RuntimeFactory = runtimeFactory; RenderPool = pool; OutputSurfaces = surfaces;
			PresentationAdapter = adapter; Presentation = presentation; PlatformFiles = platformFiles; Input = input; Loop = loop; Capabilities = capabilities;
			_providerLifetime = providerLifetime; _platformFilesLifetime = platformFilesLifetime;
			Capabilities.Changed += OnCapabilitiesChanged;
		}

		public static Result<CompositionRoot, Diagnostic> Create(IProjectFileSystem fileSystem, IVisualBindingProvider provider, IApplicationInputPoller input = null, RenderTexturePool pool = null, PresentationRoot presentationRoot = null, Func<ProjectApplication, IApplicationInputPoller> inputFactory = null, IPlatformFileInteractionAdapter platformFiles = null, NodeTypeCatalog nodeTypeCatalog = null, Shader displayTransformShader = null) {
			if (fileSystem == null || provider == null) return Failure("bootstrap.root.arguments", "A file system and production binding provider are required.");
			if (nodeTypeCatalog == null) return Failure("bootstrap.root.catalog_missing", "The generated NodeTypeCatalog asset is required before the project can open.");
			if (displayTransformShader == null) return Failure("bootstrap.root.display_transform_missing", "The serialized DisplayTransform shader is required before the project can open.");
			var catalogManifest = nodeTypeCatalog.BuildRuntimeCatalog();
			if (catalogManifest.IsFailure) return Result.Failure<CompositionRoot, Diagnostic>(catalogManifest.Error);
			var renderPool = pool ?? new RenderTexturePool();
			var surfaces = new OutputSurfaceBridge(displayTransformShader);
			var ownedPlatformFiles = platformFiles == null ? new PlatformFileInteractionAdapter() : null;
			var actualPlatformFiles = platformFiles ?? (IPlatformFileInteractionAdapter)ownedPlatformFiles;
			try {
				// The application registry is built from the generated asset.
				// Runtime visual factories are injected only per session, but
				// the immutable catalog metadata is never replaced by code.
				var catalogResult = catalogManifest;
				var registry = new NodeTypeRegistry();
				var ensured = NodeCatalogBootstrap.EnsureDefinitions(catalogResult.Value, registry);
				if (ensured.IsFailure) { renderPool.Dispose(); surfaces.Dispose(); ownedPlatformFiles?.Dispose(); (provider as IDisposable)?.Dispose(); return Result.Failure<CompositionRoot, Diagnostic>(ensured.Error); }
				var factory = new RuntimeSessionFactory(provider, renderPool, surfaces, nodeTypeCatalog);
				var userSettingsStorage = new ProjectUserSettingsStorage(fileSystem);
				var application = new ProjectApplication(fileSystem, registry, runtimeFactory: factory, recentProjectStore: userSettingsStorage);
				var userSettings = new ProjectUserSettingsPort(userSettingsStorage);
				var adapter = new ApplicationPresentationAdapter(application, application, surfaces, userSettings);
				var coordinator = new PresentationCoordinator(adapter, adapter, outputSurfacePort: surfaces, programPresenter: surfaces,
					platformFiles: actualPlatformFiles, programOutputControl: surfaces);
				var poller = input ?? inputFactory?.Invoke(application) ?? CreateDefaultInputPoller(application);
				var frame = new PresentationFrame(adapter, coordinator, surfaces, presentationRoot);
				var capabilities = new CapabilitySupervisor(
					() => ProbeInput(poller),
					() => factory.CurrentComposition == null
						? Result.Success<CapabilityStatus, Diagnostic>(CapabilityStatus.Deferred("display"))
						: surfaces.Probe());
				// The Unity host requests the same 60 Hz pacing as the
				// Program. ApplicationLoopDriver follows each LateUpdate;
				// it does not gate or accumulate application ticks.
				var loop = new ApplicationLoopDriverCore(application, poller, frame, new FrameTimingSource(), capabilities);
				return Result.Success<CompositionRoot, Diagnostic>(new CompositionRoot(application, factory, renderPool, surfaces, adapter, coordinator,
					actualPlatformFiles, poller, loop, capabilities, provider as IDisposable, ownedPlatformFiles));
			}
			catch (Exception exception) {
				renderPool.Dispose(); surfaces.Dispose(); ownedPlatformFiles?.Dispose();
				(provider as IDisposable)?.Dispose();
				return Failure("bootstrap.root.create_failed", "Production composition root could not be created.", exception);
			}
		}

		/// <summary>Connects optional external devices after the application
		/// and its first runtime session have been composed.</summary>
		public Result<HandshakeReport, Diagnostic> Handshake() {
			if (_disposed)
				return Result.Failure<HandshakeReport, Diagnostic>(new Diagnostic(new DiagnosticCode("bootstrap.handshake.composition_disposed"), Severity.Error, "Production composition is disposed.", module: "bootstrap"));
			var report = Capabilities.Handshake();
			if (report.IsFailure) return report;
			LastHandshakeReport = report.Value;
			return report;
		}

		public void Dispose() {
			if (_disposed) return;
			_disposed = true;
			Capabilities.Changed -= OnCapabilitiesChanged;
			Loop.Dispose();
			(Input as IDisposable)?.Dispose();
			Application.Dispose();
			PresentationAdapter.Dispose();
			OutputSurfaces.Dispose();
			_platformFilesLifetime?.Dispose();
			RenderPool.Dispose();
			_providerLifetime?.Dispose();
		}

		private static Result<CompositionRoot, Diagnostic> Failure(string code, string message, Exception exception = null) =>
			Result.Failure<CompositionRoot, Diagnostic>(new Diagnostic(new DiagnosticCode(code), Severity.Error, message, module: "bootstrap", exception: exception == null ? null : DiagnosticExceptionInfo.FromException(exception)));

		private static IApplicationInputPoller CreateDefaultInputPoller(ProjectApplication application) {
			return new InputPoller(application);
		}

		private static Result<CapabilityStatus, Diagnostic> ProbeInput(IApplicationInputPoller input) {
			if (input is ICapabilityProbe probe) return probe.Probe();
			if (input is ICapabilityHandshake handshake) return handshake.Handshake();
			return Result.Success<CapabilityStatus, Diagnostic>(CapabilityStatus.Deferred("midi"));
		}

		private void OnCapabilitiesChanged(HandshakeReport report) => LastHandshakeReport = report;
	}

}
