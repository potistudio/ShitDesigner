using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using NUnit.Framework.Constraints;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Application = UnityEngine.Application;
using Object = UnityEngine.Object;

namespace ShitDesigner.Presentation.Tests.PlayMode {
	public sealed class PresentationUiPlayModeTests {
		private sealed class EmptyReadPort : IPresentationReadPort {
			private readonly Guid _session = Guid.NewGuid();

			public PresentationEnvelope<PresentationReadModel> ReadLatest(bool fullSnapshot) {
				return new PresentationEnvelope<PresentationReadModel>(_session, 1L, 1uL, 1L, 1L, isFullSnapshot: true, new PresentationReadModel());
			}
		}

		private sealed class CatalogReadPort : IPresentationReadPort {
			private readonly Guid _session = Guid.NewGuid();

			public PresentationEnvelope<PresentationReadModel> ReadLatest(bool fullSnapshot) {
				return new PresentationEnvelope<PresentationReadModel>(_session, 1L, 1uL, 1L, 1L, isFullSnapshot: true, new PresentationReadModel(null, null, null, null, null, null, null, null, new NodeCatalogItem[2]
				{
					new NodeCatalogItem("fx.blur", "Blur", isAvailable: true, null, userAddable: true, "FX", isFavorite: true, isRecent: true),
					new NodeCatalogItem("fx.color", "Color", isAvailable: true, null, userAddable: true, "FX")
				}));
			}
		}

		private sealed class RecentReadPort : IPresentationReadPort {
			private readonly Guid _session = Guid.NewGuid();

			public PresentationEnvelope<PresentationReadModel> ReadLatest(bool fullSnapshot) {
				return new PresentationEnvelope<PresentationReadModel>(_session, 1L, 1uL, 1L, 1L, isFullSnapshot: true, new PresentationReadModel(new ShellReadModel(PresentationProjectState.Ready, "Current", projectDirty: false, recovered: false, canUndo: false, canRedo: false, null, 0uL), null, null, null, null, null, null, null, null, null, null, null, null, ExistingRoots()));
			}

			private static string[] ExistingRoots() {
				string projectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
				return new string[12]
				{
					"Assets", "Packages", "ProjectSettings", "docs", "Library", "Temp", "TestResults", "Tools", "Media", "Presentation",
					"Bootstrap", "Tests"
				}.Select((string name) => Path.Combine(projectRoot ?? string.Empty, name)).ToArray();
			}
		}

		private sealed class UiScaleReadPort : IPresentationReadPort {
			private readonly Guid _session = Guid.NewGuid();

			public float Scale { get; set; } = 1f;

			public long Version { get; private set; }

			public void Set(float scale, long version) {
				Scale = scale;
				Version = version;
			}

			public PresentationEnvelope<PresentationReadModel> ReadLatest(bool fullSnapshot) {
				return new PresentationEnvelope<PresentationReadModel>(_session, Version, (ulong)Version, Version, Version, isFullSnapshot: true, new PresentationReadModel(null, new WorkspaceReadModel("Edit", layoutDirty: false, null, null, Scale)));
			}
		}

		private sealed class RecordingDisplayIdentifyPort : IDisplayIdentifyPort {
			public int DisplayCount { get; }

			public List<int> Requested { get; } = new List<int>();

			public RecordingDisplayIdentifyPort(int displayCount) {
				DisplayCount = displayCount;
			}

			public bool TryIdentify(int displayNumber, out string error) {
				Requested.Add(displayNumber);
				error = string.Empty;
				return displayNumber <= DisplayCount;
			}
		}

		private sealed class GraphInteractionReadPort : IPresentationReadPort {
			private readonly Guid _session = Guid.NewGuid();

			public PresentationEnvelope<PresentationReadModel> ReadLatest(bool fullSnapshot) {
				GraphReadModel graph = new GraphReadModel(new GraphNodeReadModel[4]
				{
					new GraphNodeReadModel("source", "source", "Source", 0f, 0f),
					new GraphNodeReadModel("system.program_output", "system.program_output", "Program", 120f, 0f),
					new GraphNodeReadModel("blur", "fx.blur", "Blur", 240f, 0f),
					new GraphNodeReadModel("bad", "fx.bad", "Bad", 360f, 0f)
				}, new GraphPortReadModel[3]
				{
					new GraphPortReadModel("source", "out", "Output", "Color", PresentationPortDirection.Output, PresentationPortRequirement.Optional),
					new GraphPortReadModel("blur", "in", "Input", "Color", PresentationPortDirection.Input, PresentationPortRequirement.Required),
					new GraphPortReadModel("bad", "in", "Input", "Float", PresentationPortDirection.Input, PresentationPortRequirement.Required)
				});
				return new PresentationEnvelope<PresentationReadModel>(_session, 1L, 1uL, 1L, 1L, isFullSnapshot: true, new PresentationReadModel(null, null, graph, null, null, null, null, null, new NodeCatalogItem[2]
				{
					new NodeCatalogItem("fx.blur", "Blur", isAvailable: true, null, userAddable: true, "FX"),
					new NodeCatalogItem("fx.bad", "Bad", isAvailable: true, null, userAddable: true, "FX")
				}));
			}
		}

		private sealed class DirtyReadPort : IPresentationReadPort {
			private readonly Guid _session = Guid.NewGuid();

			public PresentationEnvelope<PresentationReadModel> ReadLatest(bool fullSnapshot) {
				return new PresentationEnvelope<PresentationReadModel>(_session, 1L, 1uL, 1L, 1L, isFullSnapshot: true, new PresentationReadModel(new ShellReadModel(PresentationProjectState.Ready, "Dirty", projectDirty: true, recovered: false, canUndo: false, canRedo: false, "Unsaved", 0uL)));
			}
		}

		private sealed class RecordingCommandPort : IPresentationCommandPort {
			private readonly List<PresentationCommandRequest> _requests;

			public RecordingCommandPort(List<PresentationCommandRequest> requests) {
				_requests = requests;
			}

			public CommandReadModel Submit(PresentationCommandRequest request) {
				_requests.Add(request);
				return new CommandReadModel(request.CommandRequestId, request.InteractionId, PresentationCommandStatus.Accepted);
			}
		}

		private sealed class RecordingPlatformFiles : IPlatformFileInteractionAdapter {
			private readonly IReadOnlyList<string> _paths;

			public PlatformPathRequest LastRequest { get; private set; }

			public RecordingPlatformFiles(params string[] paths) {
				_paths = paths;
			}

			public void PickPath(PlatformPathRequest request, Action<PlatformPathResult> completed) {
				LastRequest = request;
				completed?.Invoke(new PlatformPathResult(request.RequestId, request.ProjectSessionId, succeeded: true, _paths));
			}

			public void Cancel(Guid requestId) {
			}
		}

		private sealed class RecordingProgramPresenter : IProgramPresenterPort {
			private readonly List<bool> _changes = new List<bool>();

			public IReadOnlyList<bool> VisibilityChanges => _changes;

			public void SetVisible(bool visible) {
				_changes.Add(visible);
			}
		}

		[UnityTest]
		[Category("GUI_VisualTree")]
		public IEnumerator PresentationRoot_LoadsThemeIntoRuntimeDocument() {
			GameObject gameObject = new GameObject("PresentationRootThemeTest");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			PresentationRoot root = gameObject.AddComponent<PresentationRoot>();
			yield return null;
			Assert.That<VisualElement>(root.RootVisualElement, (IResolveConstraint)(object)Is.Not.Null);
			VisualElementStyleSheetSet styleSheets = root.RootVisualElement.styleSheets;
			Assert.That<bool>(styleSheets.Contains(Resources.Load<StyleSheet>("PresentationTheme")), (IResolveConstraint)(object)Is.True);
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_VisualTree")]
		[Category("GUI_UiScale")]
		public IEnumerator PresentationRoot_AppliesAllPersistedUiScalesWithoutAccumulationOrInputOverlap() {
			GameObject gameObject = new GameObject("PresentationRootUiScaleTest");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			panel.scale = 0.8f;
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			UiScaleReadPort read = new UiScaleReadPort();
			PresentationCoordinator coordinator = new PresentationCoordinator(read, new RecordingCommandPort(new List<PresentationCommandRequest>()));
			PresentationRoot presentation = gameObject.AddComponent<PresentationRoot>();
			presentation.Configure(coordinator);
			ApplyScale(read, coordinator, 1f, 1L);
			yield return null;
			Click(UQueryExtensions.Q<Button>(presentation.RootVisualElement, "app-menu", (string)null));
			Click(UQueryExtensions.Q<Button>(presentation.RootVisualElement, "top-settings", (string)null));
			yield return null;
			float[] array = new float[4] { 1f, 1.25f, 1.5f, 1.25f };
			foreach (float scale in array) {
				ApplyScale(read, coordinator, scale, read.Version + 1);
				yield return null;
				Assert.That<float>(panel.scale, (IResolveConstraint)(object)Is.EqualTo((object)(0.8f * scale)).Within((object)0.0001f), "Panel scale must use the immutable base scale, not the previous application.", Array.Empty<object>());
				Assert.That<bool>(presentation.RootVisualElement.ClassListContains("sd-ui-scale-" + (int)(scale * 100f)), (IResolveConstraint)(object)Is.True);
				Assert.That<string>(((BaseField<string>)(object)UQueryExtensions.Q<PopupField<string>>(presentation.RootVisualElement, "settings-ui-scale", (string)null)).value, (IResolveConstraint)(object)Is.EqualTo((object)((int)(scale * 100f) + "%")));
				AssertUsableUiScaleLayout(presentation.RootVisualElement);
			}
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_VisualTree")]
		public IEnumerator RuntimeComposition_ContainsNamedSurfacesAndPanels() {
			VisualElement host = new VisualElement {
				name = "test-host"
			};
			PresentationUiComposition.ComposeWorkspace(host, null);
			yield return null;
			Assert.That<VisualElement>(UQueryExtensions.Q(host, "node-library", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(UQueryExtensions.Q(host, "node-graph-canvas", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(UQueryExtensions.Q(host, "inspector-panel", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(UQueryExtensions.Q(host, "dashboard-grid-12-columns", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(UQueryExtensions.Q(host, "program-monitor", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(UQueryExtensions.Q(host, "preview-viewer-host", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(UQueryExtensions.Q(host, "diagnostics-panel", (string)null), (IResolveConstraint)(object)Is.Not.Null);
		}

		[UnityTest]
		[Category("GUI_VisualInteraction")]
		public IEnumerator CommandPalette_PrimaryKBuildsButtonsAndRoutesSelection() {
			GameObject gameObject = new GameObject("CommandPaletteHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			PresentationCoordinator coordinator = new PresentationCoordinator(new EmptyReadPort(), new RecordingCommandPort(commands));
			coordinator.ApplyLatestReadModels(1uL);
			PresentationRoot presentation = gameObject.AddComponent<PresentationRoot>();
			presentation.Configure(coordinator);
			yield return null;
			((Focusable)presentation.RootVisualElement).Focus();
			((CallbackEventHandler)presentation.RootVisualElement).SendEvent((EventBase)(object)KeyboardEventBase<KeyDownEvent>.GetPooled('k', (KeyCode)107, (EventModifiers)2));
			yield return null;
			VisualElement palette = UQueryExtensions.Q(presentation.RootVisualElement, "command-palette", (string)null);
			Assert.That<VisualElement>(palette, (IResolveConstraint)(object)Is.Not.Null);
			Click(UQueryExtensions.Q<Button>(palette, "command-palette-project-save", (string)null));
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "project.save"), (IResolveConstraint)(object)Is.True);
			Assert.That<VisualElement>(UQueryExtensions.Q(presentation.RootVisualElement, "command-palette", (string)null), (IResolveConstraint)(object)Is.Null);
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_DiagnosticsProject")]
		public IEnumerator ProjectOpenRecentUsesReadModelMaximumTenAndRoutesIndex() {
			GameObject gameObject = new GameObject("RecentProjectsHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			PresentationCoordinator coordinator = new PresentationCoordinator(new RecentReadPort(), new RecordingCommandPort(commands));
			PresentationRoot presentation = gameObject.AddComponent<PresentationRoot>();
			presentation.Configure(coordinator);
			coordinator.ApplyLatestReadModels(1uL);
			yield return null;
			Click(UQueryExtensions.Q<Button>(presentation.RootVisualElement, "app-menu", (string)null));
			PopupField<string> recent = UQueryExtensions.Q<PopupField<string>>(presentation.RootVisualElement, "project-open-recent", (string)null);
			Assert.That<int>(((BasePopupField<string, string>)(object)recent).choices.Count, (IResolveConstraint)(object)Is.EqualTo((object)10));
			((BaseField<string>)(object)recent).value = ((BasePopupField<string, string>)(object)recent).choices[3];
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "project.open_recent" && x.Payload["index"] == "3"), (IResolveConstraint)(object)Is.True);
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_OutputSurface")]
		public IEnumerator IdentifyDisplaysUsesDisplayPortAndRendersTransientNumbersOutsideProgramImage() {
			GameObject gameObject = new GameObject("DisplayIdentifyHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			VisualElement host = document.rootVisualElement;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			RecordingDisplayIdentifyPort identify = new RecordingDisplayIdentifyPort(2);
			PresentationCoordinator coordinator = new PresentationCoordinator(new EmptyReadPort(), new RecordingCommandPort(commands), null, null, null, null, identify);
			coordinator.ApplyLatestReadModels(1uL);
			PresentationUiComposition.ComposeWorkspace(host, coordinator);
			yield return null;
			Click(UQueryExtensions.Q<Button>(host, "program-identify-display", (string)null));
			Assert.That<List<int>>(identify.Requested, (IResolveConstraint)(object)Is.EqualTo((object)new int[2] { 1, 2 }));
			Assert.That<VisualElement>(UQueryExtensions.Q(host, "display-identify-number-1", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(UQueryExtensions.Q(host, "display-identify-number-2", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(UQueryExtensions.Q(UQueryExtensions.Q(host, "program-image", (string)null), "display-identify-number-1", (string)null), (IResolveConstraint)(object)Is.Null);
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_VisualTree")]
		public IEnumerator RuntimeComposition_ProgramSurfaceHasNoOverlayChild() {
			VisualElement host = new VisualElement();
			PresentationUiComposition.ComposeWorkspace(host, null);
			yield return null;
			VisualElement program = UQueryExtensions.Q(host, "program-monitor", (string)null);
			Assert.That<VisualElement>(UQueryExtensions.Q(program, "program-image", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(UQueryExtensions.Q(program, "diagnostics-overlay", (string)null), (IResolveConstraint)(object)Is.Null);
		}

		[UnityTest]
		[Category("GUI_OutputSurface")]
		public IEnumerator RuntimeComposition_ProgramDisplaySelectorFollowsReadModelWithoutSubmitting() {
			GameObject gameObject = new GameObject("ProgramDisplayReadModelHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			VisualElement root = document.rootVisualElement;
			PresentationUiComposition.ComposeWorkspace(root, new PresentationCoordinator(new EmptyReadPort(), new RecordingCommandPort(commands)));
			PresentationUiComposition.ApplyReadModel(root, new PresentationReadModel(null, null, null, null, null, new OutputReadModel(null, null, externalDisplayActive: false, null, isPaused: false, 3)));
			yield return null;
			PopupField<string> selector = UQueryExtensions.Q<PopupField<string>>(root, "program-display-selector", (string)null);
			Assert.That<string>(((BaseField<string>)(object)selector).value, (IResolveConstraint)(object)Is.EqualTo((object)"Display 3"));
			Assert.That<List<PresentationCommandRequest>>(commands, (IResolveConstraint)(object)Is.Empty);
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_OutputSurface")]
		public IEnumerator RuntimeComposition_BindsAndClearsProgramAndPreviewTextures() {
			GameObject gameObject = new GameObject("OutputSurfaceHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			VisualElement root = document.rootVisualElement;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			PresentationCoordinator coordinator = new PresentationCoordinator(new EmptyReadPort(), new RecordingCommandPort(commands));
			coordinator.ApplyLatestReadModels(1uL);
			Texture2D texture = new Texture2D(2, 2);
			PresentationUiComposition.ComposeWorkspace(root, coordinator);
			PreviewReadModel preview = new PreviewReadModel("node", "tab", isVisible: true, PresentationOutputFit.Fill, PresentationOutputBackground.Black, PresentationQualityStage.Stage4, "Ready", new OutputSurfaceReadModel("tab", 4uL, 2, 2, 8uL, texture, isProgram: false, isBound: true));
			PresentationUiComposition.ApplyReadModel(root, new PresentationReadModel(null, null, null, null, null, new OutputReadModel(new OutputSurfaceReadModel("program", 3uL, 2, 2, 8uL, texture, isProgram: true, isBound: true), new PreviewReadModel[1] { preview })));
			PresentationUiComposition.ApplyReadModel(root, new PresentationReadModel(null, null, null, null, null, new OutputReadModel(new OutputSurfaceReadModel("program", 3uL, 2, 2, 8uL, texture, isProgram: true, isBound: true), new PreviewReadModel[1] { preview })), coordinator);
			yield return null;
			VisualElement programImage = UQueryExtensions.Q(root, "program-image", (string)null);
			Assert.That<bool>(programImage.ClassListContains("is-bound"), (IResolveConstraint)(object)Is.True);
			StyleBackground backgroundImage = programImage.style.backgroundImage;
			Background value = backgroundImage.value;
			Assert.That<Texture2D>(value.texture, (IResolveConstraint)(object)Is.SameAs((object)texture));
			Assert.That<bool>(UQueryExtensions.Q(root, "preview-image-tab", (string)null).ClassListContains("is-bound"), (IResolveConstraint)(object)Is.True);
			Assert.That<bool>(((VisualElement)UQueryExtensions.Q<Label>(root, "preview-tab-tab", (string)null)).ClassListContains("is-fill"), (IResolveConstraint)(object)Is.True);
			Click(UQueryExtensions.Q<Button>(root, "preview-fit-tab", (string)null));
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "preview.settings" && x.TargetId == "tab"), (IResolveConstraint)(object)Is.True);
			PresentationUiComposition.ApplyReadModel(root, new PresentationReadModel(null, null, null, null, null, new OutputReadModel(new OutputSurfaceReadModel("program", 3uL, 2, 2, 8uL, texture, isProgram: true, isBound: true), new PreviewReadModel[1]
			{
				new PreviewReadModel("node", "checker", isVisible: true, PresentationOutputFit.Fit, PresentationOutputBackground.Checker, PresentationQualityStage.Stage4, "Unavailable")
			})));
			yield return null;
			VisualElement checkerImage = UQueryExtensions.Q(root, "preview-image-checker", (string)null);
			Assert.That<bool>(checkerImage.ClassListContains("is-unavailable"), (IResolveConstraint)(object)Is.True);
			backgroundImage = checkerImage.style.backgroundImage;
			value = backgroundImage.value;
			Assert.That<Texture2D>(value.texture, (IResolveConstraint)(object)Is.Not.Null);
			backgroundImage = checkerImage.style.backgroundImage;
			value = backgroundImage.value;
			Assert.That<Texture2D>(value.texture, (IResolveConstraint)(object)Is.Not.SameAs((object)texture));
			backgroundImage = checkerImage.style.backgroundImage;
			value = backgroundImage.value;
			Texture2D checkerTexture = value.texture;
			Assert.That<Color>(checkerTexture.GetPixel(0, 0), (IResolveConstraint)(object)Is.Not.EqualTo((object)checkerTexture.GetPixel(4, 0)));
			PresentationUiComposition.ApplyReadModel(root, new PresentationReadModel(null, null, null, null, null, new OutputReadModel(new OutputSurfaceReadModel("program", 4uL, 2, 2, 9uL, null, isProgram: true), Array.Empty<PreviewReadModel>())));
			yield return null;
			Assert.That<bool>(programImage.ClassListContains("is-unavailable"), (IResolveConstraint)(object)Is.True);
			backgroundImage = programImage.style.backgroundImage;
			Assert.That<StyleKeyword>(backgroundImage.keyword, (IResolveConstraint)(object)Is.EqualTo((object)(StyleKeyword)3));
			Assert.That<int>(((VisualElement)UQueryExtensions.Q<TabView>(root, "preview-tabs", (string)null)).childCount, (IResolveConstraint)(object)Is.EqualTo((object)0));
			Object.DestroyImmediate((Object)(object)texture);
			PresentationUiComposition.ReleasePreviewResources();
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_OutputSurface")]
		public IEnumerator RuntimeComposition_DynamicOutputUpdatesWithoutReplacingPreviewInteractionTree() {
			GameObject gameObject = new GameObject("DynamicOutputHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			VisualElement root = document.rootVisualElement;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			PresentationCoordinator coordinator = new PresentationCoordinator(new EmptyReadPort(), new RecordingCommandPort(commands));
			coordinator.ApplyLatestReadModels(1uL);
			PresentationUiComposition.ComposeWorkspace(root, coordinator);
			Texture2D first = new Texture2D(2, 2);
			Texture2D second = new Texture2D(2, 2);
			PreviewReadModel initialPreview = new PreviewReadModel("node", "tab", isVisible: true, PresentationOutputFit.Fit, PresentationOutputBackground.Black, PresentationQualityStage.Stage4, "Ready", new OutputSurfaceReadModel("tab", 1uL, 2, 2, 8uL, first, isProgram: false, isBound: true));
			PresentationUiComposition.ApplyReadModel(root, new PresentationReadModel(null, null, null, null, null, new OutputReadModel(new OutputSurfaceReadModel("program", 1uL, 2, 2, 8uL, first, isProgram: true, isBound: true), new PreviewReadModel[1] { initialPreview })), coordinator);
			yield return null;
			Label tab = UQueryExtensions.Q<Label>(root, "preview-tab-tab", (string)null);
			VisualElement image = UQueryExtensions.Q(root, "preview-image-tab", (string)null);
			PreviewReadModel updatedPreview = new PreviewReadModel("node", "tab", isVisible: true, PresentationOutputFit.Fill, PresentationOutputBackground.Checker, PresentationQualityStage.Stage1, "Ready", new OutputSurfaceReadModel("tab", 2uL, 2, 2, 8uL, second, isProgram: false, isBound: true));
			Assert.That<bool>(PresentationUiComposition.ApplyDynamicReadModel(root, new PresentationReadModel(null, null, null, null, null, new OutputReadModel(new OutputSurfaceReadModel("program", 2uL, 2, 2, 8uL, second, isProgram: true, isBound: true), new PreviewReadModel[1] { updatedPreview })), coordinator), (IResolveConstraint)(object)Is.True);
			yield return null;
			Assert.That<Label>(UQueryExtensions.Q<Label>(root, "preview-tab-tab", (string)null), (IResolveConstraint)(object)Is.SameAs((object)tab));
			Assert.That<VisualElement>(UQueryExtensions.Q(root, "preview-image-tab", (string)null), (IResolveConstraint)(object)Is.SameAs((object)image));
			StyleBackground backgroundImage = image.style.backgroundImage;
			Background value = backgroundImage.value;
			Assert.That<Texture2D>(value.texture, (IResolveConstraint)(object)Is.SameAs((object)second));
			backgroundImage = UQueryExtensions.Q(root, "program-image", (string)null).style.backgroundImage;
			value = backgroundImage.value;
			Assert.That<Texture2D>(value.texture, (IResolveConstraint)(object)Is.SameAs((object)second));
			Assert.That<bool>(((VisualElement)tab).ClassListContains("is-fill"), (IResolveConstraint)(object)Is.True);
			Assert.That<bool>(((VisualElement)tab).ClassListContains("is-checker"), (IResolveConstraint)(object)Is.True);
			Click(UQueryExtensions.Q<Button>(root, "preview-fit-tab", (string)null));
			Assert.That<string>(commands.Last().Payload["background"], (IResolveConstraint)(object)Is.EqualTo((object)"Checker"));
			Object.DestroyImmediate((Object)(object)first);
			Object.DestroyImmediate((Object)(object)second);
			PresentationUiComposition.ReleasePreviewResources();
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_DiagnosticsProject")]
		public IEnumerator RuntimeComposition_DynamicDiagnosticsKeepsElementAndUsesLatestDetail() {
			GameObject gameObject = new GameObject("DynamicDiagnosticsHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			PresentationCoordinator coordinator = new PresentationCoordinator(new EmptyReadPort(), new RecordingCommandPort(commands));
			coordinator.ApplyLatestReadModels(1uL);
			VisualElement root = document.rootVisualElement;
			PresentationUiComposition.ComposeWorkspace(root, coordinator);
			DiagnosticReadModel first = new DiagnosticReadModel("id", PresentationSeverity.Warning, "old", "Old detail", "node-a", 1, isCurrent: true, 0uL, 0uL);
			PresentationUiComposition.ApplyReadModel(root, new PresentationReadModel(null, null, null, null, null, null, new DiagnosticReadModel[1] { first }), coordinator);
			yield return null;
			Button row = UQueryExtensions.Q<Button>(root, "diagnostic-id", (string)null);
			DiagnosticReadModel second = new DiagnosticReadModel("id", PresentationSeverity.Error, "new", "New detail", "node-b", 2, isCurrent: true, 0uL, 0uL);
			Assert.That<bool>(PresentationUiComposition.ApplyDynamicReadModel(root, new PresentationReadModel(null, null, null, null, null, null, new DiagnosticReadModel[1] { second }), coordinator), (IResolveConstraint)(object)Is.True);
			Assert.That<Button>(UQueryExtensions.Q<Button>(root, "diagnostic-id", (string)null), (IResolveConstraint)(object)Is.SameAs((object)row));
			Click(row);
			Assert.That<string>(commands.Last().TargetId, (IResolveConstraint)(object)Is.EqualTo((object)"node-b"));
			Assert.That<string>(((TextElement)UQueryExtensions.Q<Label>(UQueryExtensions.Q(root, "diagnostics-detail-pane", (string)null), (string)null, (string)null)).text, (IResolveConstraint)(object)Does.Contain("id"));
			Assert.That<bool>(UQueryExtensions.Q(root, "diagnostics-detail-pane", (string)null).Children().OfType<Label>()
				.Any((Label x) => ((TextElement)x).text.Contains("New detail")), (IResolveConstraint)(object)Is.True);
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_Parameters")]
		public IEnumerator RuntimeComposition_DynamicParameterValuesUseNodeAndParameterIdentity() {
			GameObject gameObject = new GameObject("DynamicParameterHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			VisualElement root = document.rootVisualElement;
			PresentationUiComposition.ComposeWorkspace(root, null);
			ParameterReadModel[] initial = new ParameterReadModel[2]
			{
				new ParameterReadModel("node-a", "gain", "Gain A", "1", "1", isReadOnly: false, isBroken: false, isClamped: false, null, "Float"),
				new ParameterReadModel("node-b", "gain", "Gain B", "2", "2", isReadOnly: false, isBroken: false, isClamped: false, null, "Float")
			};
			PresentationUiComposition.ApplyReadModel(root, new PresentationReadModel(null, null, null, initial));
			yield return null;
			List<VisualElement> rows = UQueryExtensions.Query<VisualElement>(root, (string)null, "sd-parameter-row").ToList();
			VisualElement nodeARow = rows.Single((VisualElement row) => string.Equals(row.userData as string, "node-a:gain", StringComparison.Ordinal));
			VisualElement nodeBRow = rows.Single((VisualElement row) => string.Equals(row.userData as string, "node-b:gain", StringComparison.Ordinal));
			ParameterReadModel[] updated = new ParameterReadModel[2]
			{
				new ParameterReadModel("node-a", "gain", "Gain A", "1", "3", isReadOnly: false, isBroken: false, isClamped: false, null, "Float"),
				new ParameterReadModel("node-b", "gain", "Gain B", "2", "4", isReadOnly: false, isBroken: false, isClamped: false, null, "Float")
			};
			Assert.That<bool>(PresentationUiComposition.ApplyDynamicReadModel(root, new PresentationReadModel(null, null, null, updated)), (IResolveConstraint)(object)Is.True);
			Assert.That<string>(((BaseField<string>)(object)UQueryExtensions.Q<TextField>(nodeARow, "parameter-row-effective-gain", (string)null)).value, (IResolveConstraint)(object)Is.EqualTo((object)"3"));
			Assert.That<string>(((BaseField<string>)(object)UQueryExtensions.Q<TextField>(nodeBRow, "parameter-row-effective-gain", (string)null)).value, (IResolveConstraint)(object)Is.EqualTo((object)"4"));
			VisualElement stableRow = nodeARow;
			for (int iteration = 0; iteration < 120; iteration++) {
				Assert.That<bool>(PresentationUiComposition.ApplyDynamicReadModel(root, new PresentationReadModel(null, null, null, updated)), (IResolveConstraint)(object)Is.True);
			}
			Assert.That<VisualElement>(rows.Single((VisualElement row) => string.Equals(row.userData as string, "node-a:gain", StringComparison.Ordinal)), (IResolveConstraint)(object)Is.SameAs((object)stableRow));
			TextField inspectorBase = UQueryExtensions.Q<TextField>(root, "inspector-base-value", (string)null);
			((BaseField<string>)(object)inspectorBase).value = "draft";
			((Focusable)inspectorBase).Focus();
			Assert.That<bool>(PresentationUiComposition.ApplyDynamicReadModel(root, new PresentationReadModel(null, null, null, updated)), (IResolveConstraint)(object)Is.True);
			Assert.That<string>(((BaseField<string>)(object)inspectorBase).value, (IResolveConstraint)(object)Is.EqualTo((object)"draft"));
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_Parameters")]
		public IEnumerator RuntimeComposition_ParameterCatalogBuildsTypedEditorsAndInlineEffectiveValues() {
			GameObject gameObject = new GameObject("TypedParameterHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			PresentationCoordinator coordinator = new PresentationCoordinator(new EmptyReadPort(), new RecordingCommandPort(commands));
			coordinator.ApplyLatestReadModels(1uL);
			VisualElement root = document.rootVisualElement;
			PresentationUiComposition.ComposeWorkspace(root, coordinator);
			ParameterReadModel[] parameters = new ParameterReadModel[4]
			{
				new ParameterReadModel("node", "gain", "Gain", "0.5", "0.75", isReadOnly: false, isBroken: false, isClamped: false, null, "Float", null, null, "Main", 1, null, "x", 0.1, "0..2"),
				new ParameterReadModel("node", "enabled", "Enabled", "true", "true", isReadOnly: false, isBroken: false, isClamped: false, null, "Bool", null, null, "Main", 2),
				new ParameterReadModel("node", "color", "Color", "(1, 0.5, 0, 1)", "(1, 0.5, 0, 1)", isReadOnly: false, isBroken: false, isClamped: false, null, "Color", null, null, "Look", 3, null, null, 0.0, null, new ParameterComponentRangeReadModel[4]
				{
					new ParameterComponentRangeReadModel("R", "0", "1"),
					new ParameterComponentRangeReadModel("G", "0", "1"),
					new ParameterComponentRangeReadModel("B", "0", "1"),
					new ParameterComponentRangeReadModel("A", "0", "1")
				}),
				new ParameterReadModel("node", "mode", "Mode", "fit", "fit", isReadOnly: false, isBroken: false, isClamped: false, null, "Enum", null, null, "Look", 4, null, null, 0.0, null, null, new ParameterOptionReadModel[2]
				{
					new ParameterOptionReadModel("fit", "Fit"),
					new ParameterOptionReadModel("fill", "Fill")
				})
			};
			PresentationUiComposition.ApplyReadModel(root, new PresentationReadModel(null, null, null, parameters), coordinator);
			yield return null;
			Assert.That<FloatField>(UQueryExtensions.Q<FloatField>(root, "parameter-float-field", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<Slider>(UQueryExtensions.Q<Slider>(root, "parameter-slider", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<float>(((BaseSlider<float>)(object)UQueryExtensions.Q<Slider>(root, "parameter-slider", (string)null)).highValue, (IResolveConstraint)(object)Is.EqualTo((object)2f));
			Assert.That<Toggle>(UQueryExtensions.Q<Toggle>(root, "parameter-row-base-enabled", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(UQueryExtensions.Q<VisualElement>(root, "parameter-row-base-color-r", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<PopupField<string>>(UQueryExtensions.Q<PopupField<string>>(root, "parameter-row-base-mode", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<bool>(((TextInputBaseField<string>)(object)UQueryExtensions.Q<TextField>(root, "parameter-row-effective-gain", (string)null)).isReadOnly, (IResolveConstraint)(object)Is.True);
			((BaseField<float>)(object)UQueryExtensions.Q<FloatField>(root, "parameter-float-field", (string)null)).value = 1.25f;
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "parameter.set_base"), (IResolveConstraint)(object)Is.True);
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_MediaImport")]
		public IEnumerator RuntimeComposition_MediaPickerRoutesMultiFileBatchAndProgress() {
			GameObject gameObject = new GameObject("MediaImportHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			RecordingPlatformFiles platform = new RecordingPlatformFiles("C:/media/a.png", "C:/media/b.png");
			PresentationCoordinator coordinator = new PresentationCoordinator(new EmptyReadPort(), new RecordingCommandPort(commands), null, null, null, platform);
			coordinator.ApplyLatestReadModels(1uL);
			yield return null;
			VisualElement root = document.rootVisualElement;
			PresentationUiComposition.ComposeWorkspace(root, coordinator);
			Click(UQueryExtensions.Q<Button>(root, "media-import-button", (string)null));
			yield return null;
			Assert.That<PlatformPathRequest>(platform.LastRequest, (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<PlatformPathRequestKind>(platform.LastRequest.Kind, (IResolveConstraint)(object)Is.EqualTo((object)PlatformPathRequestKind.MultiFile));
			PresentationCommandRequest request = commands.Single((PresentationCommandRequest x) => x.CommandId == "media.import.batch");
			Assert.That<string>(request.Payload["paths"], (IResolveConstraint)(object)Does.Contain("a.png"));
			Assert.That<string>(request.Payload["paths"], (IResolveConstraint)(object)Does.Contain("b.png"));
			Assert.That<string>(((TextElement)UQueryExtensions.Q<Label>(root, "media-import-progress", (string)null)).text, (IResolveConstraint)(object)Does.Contain("Importing"));
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_OutputSurface")]
		public IEnumerator RuntimeComposition_ProgramCloseOnlyHidesPresenterAndKeepsSurfaceRead() {
			GameObject gameObject = new GameObject("ProgramPresenterHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			RecordingProgramPresenter presenter = new RecordingProgramPresenter();
			PresentationCoordinator coordinator = new PresentationCoordinator(new EmptyReadPort(), new RecordingCommandPort(new List<PresentationCommandRequest>()), null, null, presenter);
			coordinator.ApplyLatestReadModels(1uL);
			yield return null;
			VisualElement root = document.rootVisualElement;
			PresentationUiComposition.ComposeWorkspace(root, coordinator);
			Click(UQueryExtensions.Q<Button>(root, "program-close", (string)null));
			yield return null;
			Assert.That<IReadOnlyList<bool>>(presenter.VisibilityChanges, (IResolveConstraint)(object)Is.EqualTo((object)new bool[1]));
			Assert.That<VisualElement>(UQueryExtensions.Q(root, "program-image", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<bool>(UQueryExtensions.Q(root, "program-monitor", (string)null).ClassListContains("is-closed"), (IResolveConstraint)(object)Is.True);
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_GraphInteraction")]
		public IEnumerator GraphContextSearch_UsesCatalogPositionAndSessionToggles() {
			GameObject gameObject = new GameObject("GraphSearchHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			PresentationCoordinator coordinator = new PresentationCoordinator(new CatalogReadPort(), new RecordingCommandPort(commands));
			coordinator.ApplyLatestReadModels(1uL);
			VisualElement root = document.rootVisualElement;
			PresentationUiComposition.ComposeWorkspace(root, coordinator);
			GraphCanvasElement canvas = UQueryExtensions.Q<GraphCanvasElement>(root, "node-graph-canvas", (string)null);
			Assert.That<GraphCanvasElement>(canvas, (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<bool>(canvas.IsMinimapVisible, (IResolveConstraint)(object)Is.True);
			canvas.ToggleMinimap();
			Assert.That<bool>(canvas.IsMinimapVisible, (IResolveConstraint)(object)Is.False);
			canvas.ToggleMinimap();
			Assert.That<bool>(canvas.IsMinimapVisible, (IResolveConstraint)(object)Is.True);
			bool snapBefore = canvas.IsGridSnapEnabled;
			canvas.ToggleGridSnap();
			Assert.That<bool>(canvas.IsGridSnapEnabled, (IResolveConstraint)(object)Is.Not.EqualTo((object)snapBefore));
			canvas.ShowNodeSearch(new PresentationPoint(128f, 256f));
			yield return null;
			VisualElement popup = UQueryExtensions.Q<VisualElement>(root, "graph-node-search-popup", (string)null);
			Assert.That<VisualElement>(popup, (IResolveConstraint)(object)Is.Not.Null);
			Button result = UQueryExtensions.Q<Button>(popup, (string)null, (string)null);
			Assert.That<Button>(result, (IResolveConstraint)(object)Is.Not.Null);
			Click(result);
			yield return null;
			PresentationCommandRequest add = commands.LastOrDefault((PresentationCommandRequest x) => x.CommandId == "graph.add_node");
			Assert.That<PresentationCommandRequest>(add, (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<string>(add.Payload["nodeTypeId"], (IResolveConstraint)(object)Is.EqualTo((object)"fx.blur"));
			Assert.That<string>(add.Payload["x"], (IResolveConstraint)(object)Is.EqualTo((object)"128"));
			Assert.That<string>(add.Payload["y"], (IResolveConstraint)(object)Is.EqualTo((object)"256"));
			Assert.That<VisualElement>(UQueryExtensions.Q(root, "graph-node-search-popup", (string)null), (IResolveConstraint)(object)Is.Null);
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_GraphInteraction")]
		public IEnumerator GraphBlankDropFiltersCompatibleNodesAndSelectionShortcutsAreCommands() {
			GameObject gameObject = new GameObject("GraphBlankDropHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			PresentationCoordinator coordinator = new PresentationCoordinator(new GraphInteractionReadPort(), new RecordingCommandPort(commands));
			coordinator.ApplyLatestReadModels(1uL);
			VisualElement root = document.rootVisualElement;
			PresentationUiComposition.ComposeWorkspace(root, coordinator);
			GraphCanvasElement canvas = UQueryExtensions.Q<GraphCanvasElement>(root, "node-graph-canvas", (string)null);
			GraphReadModel graph = coordinator.Current.Graph;
			canvas.SetGraph(new GraphReadModel(graph.Nodes.Select((GraphNodeReadModel node) => (node.Id == "blur") ? new GraphNodeReadModel(node.Id, node.TypeId, node.DisplayName, node.X, node.Y, node.Status, node.IsPending, node.StatusReason, from index in Enumerable.Range(0, 5)
																																																													select new ParameterReadModel("blur", "p" + index, "P" + index, index.ToString(), index.ToString())) : node), graph.Ports, graph.Connections));
			yield return null;
			Assert.That<VisualElement>(UQueryExtensions.Q((VisualElement)(object)canvas, "node-blur-parameter-p0", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(UQueryExtensions.Q((VisualElement)(object)canvas, "node-blur-parameter-p3", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(UQueryExtensions.Q((VisualElement)(object)canvas, "node-blur-parameter-p4", (string)null), (IResolveConstraint)(object)Is.Null);
			VisualElement source = UQueryExtensions.Q((VisualElement)(object)canvas, "port-source-out", (string)null);
			SendPointer(source, "down");
			SendPointer(source, "up");
			canvas.ShowCompatibleNodeSearch(graph.Ports.First((GraphPortReadModel x) => x.NodeId == "source" && x.Direction == PresentationPortDirection.Output), new PresentationPoint(0f, 0f));
			yield return null;
			VisualElement popup = UQueryExtensions.Q<VisualElement>(root, "graph-node-search-popup", (string)null);
			Assert.That<VisualElement>(popup, (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<Button>(UQueryExtensions.Q<Button>(popup, "graph-search-result-fx.blur", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<Button>(UQueryExtensions.Q<Button>(popup, "graph-search-result-fx.bad", (string)null), (IResolveConstraint)(object)Is.Null);
			((Focusable)canvas).Focus();
			((CallbackEventHandler)canvas).SendEvent((EventBase)(object)KeyboardEventBase<KeyDownEvent>.GetPooled('a', (KeyCode)97, (EventModifiers)2));
			Assert.That<IReadOnlyCollection<string>>(canvas.Selection.Selected, (IResolveConstraint)(object)Does.Contain("source"));
			((CallbackEventHandler)canvas).SendEvent((EventBase)(object)KeyboardEventBase<KeyDownEvent>.GetPooled('c', (KeyCode)99, (EventModifiers)2));
			((CallbackEventHandler)canvas).SendEvent((EventBase)(object)KeyboardEventBase<KeyDownEvent>.GetPooled('v', (KeyCode)118, (EventModifiers)2));
			((CallbackEventHandler)canvas).SendEvent((EventBase)(object)KeyboardEventBase<KeyDownEvent>.GetPooled('d', (KeyCode)100, (EventModifiers)2));
			((CallbackEventHandler)canvas).SendEvent((EventBase)(object)KeyboardEventBase<KeyDownEvent>.GetPooled('f', (KeyCode)102, (EventModifiers)0));
			((CallbackEventHandler)canvas).SendEvent((EventBase)(object)KeyboardEventBase<KeyDownEvent>.GetPooled('\0', (KeyCode)278, (EventModifiers)0));
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "graph.copy"), (IResolveConstraint)(object)Is.True);
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "graph.paste"), (IResolveConstraint)(object)Is.True);
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "graph.duplicate"), (IResolveConstraint)(object)Is.True);
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "graph.focus_selection"), (IResolveConstraint)(object)Is.True);
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "graph.focus_all"), (IResolveConstraint)(object)Is.True);
			((CallbackEventHandler)canvas).SendEvent((EventBase)(object)KeyboardEventBase<KeyDownEvent>.GetPooled('\0', (KeyCode)27, (EventModifiers)0));
			Assert.That<IReadOnlyCollection<string>>(canvas.Selection.Selected, (IResolveConstraint)(object)Is.Empty);
			canvas.Selection.Replace(new string[2] { "system.program_output", "source" });
			canvas.RemoveSelectedNodes();
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "graph.delete_node" && x.TargetId == "system.program_output"), (IResolveConstraint)(object)Is.False);
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "graph.delete_node" && x.TargetId == "source"), (IResolveConstraint)(object)Is.True);
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_DiagnosticsProject")]
		public IEnumerator DiagnosticsCurrentHistoryFiltersDetailAndNodeFocusUseReadModel() {
			GameObject gameObject = new GameObject("DiagnosticsHost");
			PanelSettings panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
			panelSettings.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panelSettings;
			VisualElement host = document.rootVisualElement;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			PresentationCoordinator coordinator = new PresentationCoordinator(new EmptyReadPort(), new RecordingCommandPort(commands));
			coordinator.ApplyLatestReadModels(1uL);
			PresentationUiComposition.ComposeWorkspace(host, coordinator);
			PresentationReadModel model = new PresentationReadModel(null, null, null, null, null, null, new DiagnosticReadModel[2]
			{
				new DiagnosticReadModel("current", PresentationSeverity.Error, "node.fault", "Broken input", "node-a", 2, isCurrent: true, 0uL, 0uL),
				new DiagnosticReadModel("history", PresentationSeverity.Warning, "old.warning", "Recovered", "node-b", 1, isCurrent: false, 0uL, 0uL)
			});
			PresentationUiComposition.ApplyReadModel(host, model, coordinator);
			yield return null;
			VisualElement panel = UQueryExtensions.Q(host, "diagnostics-panel", (string)null);
			Assert.That<VisualElement>(UQueryExtensions.Q(panel, "diagnostic-current", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(UQueryExtensions.Q(panel, "diagnostic-history", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Click(UQueryExtensions.Q<Button>(panel, "diagnostics-history-tab", (string)null));
			Assert.That<DisplayStyle>(UQueryExtensions.Q(panel, "diagnostic-current", (string)null).resolvedStyle.display, (IResolveConstraint)(object)Is.EqualTo((object)(DisplayStyle)1));
			Assert.That<DisplayStyle>(UQueryExtensions.Q(panel, "diagnostic-history", (string)null).resolvedStyle.display, (IResolveConstraint)(object)Is.Not.EqualTo((object)(DisplayStyle)1));
			Click(UQueryExtensions.Q<Button>(panel, "diagnostic-history", (string)null));
			Assert.That<int>(UQueryExtensions.Q(panel, "diagnostics-detail-pane", (string)null).childCount, (IResolveConstraint)(object)Is.GreaterThan((object)0));
			Click(UQueryExtensions.Q<Button>(panel, "diagnostics-current-tab", (string)null));
			Click(UQueryExtensions.Q<Button>(panel, "diagnostic-current", (string)null));
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "graph.focus_selection" && x.TargetId == "node-a"), (IResolveConstraint)(object)Is.True);
			Object.DestroyImmediate((Object)(object)panelSettings);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_ProjectDialogs")]
		public IEnumerator PresentationRoot_UnsavedDialogRoutesSaveDiscardCancelPayloads() {
			GameObject gameObject = new GameObject("ProjectDialogHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			PresentationCoordinator coordinator = new PresentationCoordinator(new DirtyReadPort(), new RecordingCommandPort(commands));
			PresentationRoot rootComponent = gameObject.AddComponent<PresentationRoot>();
			rootComponent.Configure(coordinator);
			coordinator.ApplyLatestReadModels(1uL);
			yield return null;
			VisualElement root = document.rootVisualElement;
			Click(UQueryExtensions.Q<Button>(root, "app-menu", (string)null));
			Click(UQueryExtensions.Q<Button>(root, "project-close", (string)null));
			yield return null;
			Assert.That<VisualElement>(UQueryExtensions.Q(root, "unsaved-changes-dialog", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Click(UQueryExtensions.Q<Button>(root, "unsaved-save", (string)null));
			Assert.That<string>(commands.Last().Payload["decision"], (IResolveConstraint)(object)Is.EqualTo((object)"Save"));
			Click(UQueryExtensions.Q<Button>(root, "project-close", (string)null));
			Click(UQueryExtensions.Q<Button>(root, "unsaved-discard", (string)null));
			Assert.That<string>(commands.Last().Payload["decision"], (IResolveConstraint)(object)Is.EqualTo((object)"Discard"));
			int countBeforeCancel = commands.Count;
			Click(UQueryExtensions.Q<Button>(root, "project-close", (string)null));
			Click(UQueryExtensions.Q<Button>(root, "unsaved-cancel", (string)null));
			Assert.That<int>(commands.Count, (IResolveConstraint)(object)Is.EqualTo((object)countBeforeCancel));
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_VisualInteraction")]
		public IEnumerator RuntimeComposition_DockAndGraphControls_AreInteractive() {
			GameObject gameObject = new GameObject("DockInteractionHost");
			PanelSettings panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
			panelSettings.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panelSettings;
			VisualElement host = document.rootVisualElement;
			PresentationUiComposition.ComposeWorkspace(host, null);
			yield return null;
			Button split = UQueryExtensions.Q<Button>(host, "dock-split-horizontal", (string)null);
			Click(split);
			Assert.That<string>(((TextElement)UQueryExtensions.Q<Label>(host, "layout-dirty-state", (string)null)).text, (IResolveConstraint)(object)Does.Contain("Horizontal Split"));
			GraphCanvasElement canvas = UQueryExtensions.Q<GraphCanvasElement>(host, "node-graph-canvas", (string)null);
			canvas.AddNode("n", new PresentationPoint(16f, 16f), "Node");
			Assert.That<VisualElement>(UQueryExtensions.Q((VisualElement)(object)canvas, "node-n", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			canvas.Selection.Replace(new string[1] { "n" }, "n");
			canvas.RemoveSelectedNodes();
			Assert.That<VisualElement>(UQueryExtensions.Q((VisualElement)(object)canvas, "node-n", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Object.DestroyImmediate((Object)(object)panelSettings);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_GraphInteraction")]
		public IEnumerator GraphPortDrag_SubmitsTypedConnectAndDisconnectCommands() {
			GameObject gameObject = new GameObject("GraphInteractionHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			PresentationCoordinator coordinator = new PresentationCoordinator(new EmptyReadPort(), new RecordingCommandPort(commands));
			coordinator.ApplyLatestReadModels(1uL);
			yield return null;
			VisualElement root = document.rootVisualElement;
			PresentationUiComposition.ComposeWorkspace(root, coordinator);
			GraphCanvasElement canvas = UQueryExtensions.Q<GraphCanvasElement>(root, "node-graph-canvas", (string)null);
			GraphReadModel graph = new GraphReadModel(new GraphNodeReadModel[2]
			{
				new GraphNodeReadModel("source", "image", "Source", 16f, 16f),
				new GraphNodeReadModel("destination", "display", "Destination", 320f, 16f)
			}, new GraphPortReadModel[2]
			{
				new GraphPortReadModel("source", "out", "Output", "Color", PresentationPortDirection.Output, PresentationPortRequirement.Optional),
				new GraphPortReadModel("destination", "in", "Input", "Vector4", PresentationPortDirection.Input, PresentationPortRequirement.Required)
			}, new GraphConnectionReadModel[1]
			{
				new GraphConnectionReadModel("edge", "source", "out", "destination", "in", isImplicitConversion: true, "Color → Vector4")
			});
			canvas.SetGraph(graph);
			yield return null;
			VisualElement source = UQueryExtensions.Q((VisualElement)(object)canvas, "port-source-out", (string)null);
			VisualElement target = UQueryExtensions.Q((VisualElement)(object)canvas, "port-destination-in", (string)null);
			Assert.That<VisualElement>(source, (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(target, (IResolveConstraint)(object)Is.Not.Null);
			SendPointer(source, "down");
			SendPointer(target, "move");
			SendPointer(target, "up");
			yield return null;
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "graph.replace_input_connection"), (IResolveConstraint)(object)Is.True);
			canvas.AddNode("delete-me", new PresentationPoint(640f, 16f), "Delete Me");
			canvas.Selection.Replace(new string[1] { "delete-me" }, "delete-me");
			canvas.RemoveSelectedNodes();
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "graph.delete_node"), (IResolveConstraint)(object)Is.True);
			VisualElement edge = UQueryExtensions.Q((VisualElement)(object)canvas, "connection-edge", (string)null);
			SendPointer(edge, "down");
			((CallbackEventHandler)canvas).SendEvent((EventBase)(object)KeyboardEventBase<KeyDownEvent>.GetPooled('\0', (KeyCode)127, (EventModifiers)0));
			yield return null;
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "graph.disconnect"), (IResolveConstraint)(object)Is.True);
			((CallbackEventHandler)canvas).SendEvent((EventBase)(object)KeyboardEventBase<KeyDownEvent>.GetPooled('z', (KeyCode)122, (EventModifiers)2));
			((CallbackEventHandler)canvas).SendEvent((EventBase)(object)KeyboardEventBase<KeyDownEvent>.GetPooled('y', (KeyCode)121, (EventModifiers)2));
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "project.undo"), (IResolveConstraint)(object)Is.True);
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "project.redo"), (IResolveConstraint)(object)Is.True);
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		[UnityTest]
		[Category("GUI_GraphInteraction")]
		public IEnumerator GraphPortDrag_IncompatibleDropKeepsExistingEdgeAndDoesNotSubmit() {
			GameObject gameObject = new GameObject("GraphRejectHost");
			PanelSettings panel = ScriptableObject.CreateInstance<PanelSettings>();
			panel.referenceResolution = new Vector2Int(1280, 720);
			UIDocument document = gameObject.AddComponent<UIDocument>();
			document.panelSettings = panel;
			List<PresentationCommandRequest> commands = new List<PresentationCommandRequest>();
			PresentationCoordinator coordinator = new PresentationCoordinator(new EmptyReadPort(), new RecordingCommandPort(commands));
			coordinator.ApplyLatestReadModels(1uL);
			yield return null;
			VisualElement root = document.rootVisualElement;
			PresentationUiComposition.ComposeWorkspace(root, coordinator);
			GraphCanvasElement canvas = UQueryExtensions.Q<GraphCanvasElement>(root, "node-graph-canvas", (string)null);
			canvas.SetGraph(new GraphReadModel(new GraphNodeReadModel[2]
			{
				new GraphNodeReadModel("source", "source", "Source", 16f, 16f),
				new GraphNodeReadModel("destination", "destination", "Destination", 320f, 16f)
			}, new GraphPortReadModel[2]
			{
				new GraphPortReadModel("source", "out", "Output", "Float", PresentationPortDirection.Output, PresentationPortRequirement.Optional),
				new GraphPortReadModel("destination", "in", "Input", "Texture", PresentationPortDirection.Input, PresentationPortRequirement.Required)
			}, new GraphConnectionReadModel[1]
			{
				new GraphConnectionReadModel("edge", "source", "out", "destination", "in")
			}));
			yield return null;
			SendPointer(UQueryExtensions.Q((VisualElement)(object)canvas, "port-source-out", (string)null), "down");
			SendPointer(UQueryExtensions.Q((VisualElement)(object)canvas, "port-destination-in", (string)null), "move");
			SendPointer(UQueryExtensions.Q((VisualElement)(object)canvas, "port-destination-in", (string)null), "up");
			yield return null;
			Assert.That<bool>(commands.Any((PresentationCommandRequest x) => x.CommandId == "graph.connect" || x.CommandId == "graph.replace_input_connection"), (IResolveConstraint)(object)Is.False);
			Assert.That<VisualElement>(UQueryExtensions.Q((VisualElement)(object)canvas, "connection-edge", (string)null), (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<bool>(((VisualElement)UQueryExtensions.Q<Label>((VisualElement)(object)canvas, "graph-drop-status", (string)null)).ClassListContains("is-error"), (IResolveConstraint)(object)Is.True);
			Object.DestroyImmediate((Object)(object)panel);
			Object.DestroyImmediate((Object)(object)gameObject);
		}

		private static void SendPointer(VisualElement target, string kind) {
			//IL_0013: Unknown result type (might be due to invalid IL or missing references)
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_001b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0020: Unknown result type (might be due to invalid IL or missing references)
			//IL_0048: Unknown result type (might be due to invalid IL or missing references)
			//IL_004d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0055: Unknown result type (might be due to invalid IL or missing references)
			//IL_005d: Unknown result type (might be due to invalid IL or missing references)
			//IL_005e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0067: Expected O, but got Unknown
			//IL_00bb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00da: Expected O, but got Unknown
			//IL_008a: Unknown result type (might be due to invalid IL or missing references)
			//IL_008f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0097: Unknown result type (might be due to invalid IL or missing references)
			//IL_009f: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a9: Expected O, but got Unknown
			Assert.That<VisualElement>(target, (IResolveConstraint)(object)Is.Not.Null);
			Rect worldBound = target.worldBound;
			Vector2 position = worldBound.center;
			VisualElement dispatchRoot = ((target.panel == null) ? target : target.panel.visualTree);
			if (kind == "down") {
				Event systemEvent = new Event {
					type = (EventType)0,
					button = 0,
					mousePosition = position
				};
				((CallbackEventHandler)dispatchRoot).SendEvent((EventBase)(object)PointerEventBase<PointerDownEvent>.GetPooled(systemEvent));
			}
			else if (kind == "move") {
				Event systemEvent2 = new Event {
					type = (EventType)3,
					button = 0,
					mousePosition = position
				};
				((CallbackEventHandler)dispatchRoot).SendEvent((EventBase)(object)PointerEventBase<PointerMoveEvent>.GetPooled(systemEvent2));
			}
			else {
				Event systemEvent3 = new Event {
					type = (EventType)1,
					button = 0,
					mousePosition = position
				};
				((CallbackEventHandler)dispatchRoot).SendEvent((EventBase)(object)PointerEventBase<PointerUpEvent>.GetPooled(systemEvent3));
			}
		}

		private static void Click(Button button) {
			Assert.That<Button>(button, (IResolveConstraint)(object)Is.Not.Null, "Expected an interactive Button in the composed UI.", Array.Empty<object>());
			((Focusable)button).Focus();
			((CallbackEventHandler)button).SendEvent((EventBase)(object)NavigationEventBase<NavigationSubmitEvent>.GetPooled((EventModifiers)0));
		}

		private static void ApplyScale(UiScaleReadPort read, PresentationCoordinator coordinator, float scale, long version) {
			read.Set(scale, version);
			PresentationApplyReport report = coordinator.ApplyLatestReadModels((ulong)version);
			Assert.That<bool>(report.Applied, (IResolveConstraint)(object)Is.True);
		}

		private static void AssertUsableUiScaleLayout(VisualElement root) {
			//IL_00a2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00eb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
			//IL_0113: Unknown result type (might be due to invalid IL or missing references)
			//IL_0118: Unknown result type (might be due to invalid IL or missing references)
			//IL_011d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0133: Unknown result type (might be due to invalid IL or missing references)
			//IL_0138: Unknown result type (might be due to invalid IL or missing references)
			//IL_013d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0153: Unknown result type (might be due to invalid IL or missing references)
			//IL_0158: Unknown result type (might be due to invalid IL or missing references)
			//IL_015e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0174: Unknown result type (might be due to invalid IL or missing references)
			//IL_0179: Unknown result type (might be due to invalid IL or missing references)
			//IL_018a: Unknown result type (might be due to invalid IL or missing references)
			//IL_018f: Unknown result type (might be due to invalid IL or missing references)
			VisualElement top = UQueryExtensions.Q(root, "top-bar", (string)null);
			Button save = UQueryExtensions.Q<Button>(root, "project-save", (string)null);
			VisualElement dock = UQueryExtensions.Q(root, "dock-tree", (string)null);
			VisualElement graphToolbar = UQueryExtensions.Q(root, "graph-toolbar", (string)null);
			VisualElement status = UQueryExtensions.Q(root, "status-bar", (string)null);
			Assert.That<Button>(save, (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(top, (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(dock, (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(graphToolbar, (IResolveConstraint)(object)Is.Not.Null);
			Assert.That<VisualElement>(status, (IResolveConstraint)(object)Is.Not.Null);
			Rect worldBound;
			int num;
			if (((VisualElement)save).enabledInHierarchy) {
				worldBound = ((VisualElement)save).worldBound;
				if (worldBound.width > 0f) {
					worldBound = ((VisualElement)save).worldBound;
					num = ((worldBound.height > 0f) ? 1 : 0);
					goto IL_00d0;
				}
			}
			num = 0;
			goto IL_00d0;
		IL_00d0:
			Assert.That<bool>((byte)num != 0, (IResolveConstraint)(object)Is.True);
			IPanel panel = ((VisualElement)save).panel;
			worldBound = ((VisualElement)save).worldBound;
			VisualElement picked = panel.Pick(worldBound.center);
			Assert.That<bool>((object)picked == save || ((VisualElement)save).Contains(picked), (IResolveConstraint)(object)Is.True);
			worldBound = ((VisualElement)save).worldBound;
			Assert.That<bool>(worldBound.Overlaps(graphToolbar.worldBound), (IResolveConstraint)(object)Is.False);
			worldBound = top.worldBound;
			Assert.That<bool>(worldBound.Overlaps(dock.worldBound), (IResolveConstraint)(object)Is.False);
			worldBound = dock.worldBound;
			Assert.That<bool>(worldBound.Overlaps(status.worldBound), (IResolveConstraint)(object)Is.False);
			worldBound = graphToolbar.worldBound;
			int num2;
			if (worldBound.width > 0f) {
				worldBound = graphToolbar.worldBound;
				num2 = ((worldBound.height > 0f) ? 1 : 0);
			}
			else {
				num2 = 0;
			}
			Assert.That<bool>((byte)num2 != 0, (IResolveConstraint)(object)Is.True);
		}
	}
}
