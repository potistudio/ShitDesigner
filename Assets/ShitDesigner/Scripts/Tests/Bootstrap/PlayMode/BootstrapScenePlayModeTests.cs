using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using ShitDesigner.Application;
using ShitDesigner.Bootstrap;
using ShitDesigner.Core;
using ShitDesigner.Graph;
using ShitDesigner.Scene;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using UnityEngine.Rendering;

namespace ShitDesigner.Bootstrap.Tests {
	public sealed class BootstrapScenePlayModeTests {
		[UnityTest]
		public IEnumerator DedicatedBootstrapSceneCreatesCompositionAndPresentationHost() {
			yield return SceneManager.LoadSceneAsync("ShitDesignerBootstrap", LoadSceneMode.Single);
			yield return null;
			var behaviour = UnityEngine.Object.FindAnyObjectByType<ApplicationHost>();
			Assert.That(behaviour, Is.Not.Null);
			Assert.That(behaviour.Composition, Is.Not.Null);
			Assert.That(behaviour.State, Is.EqualTo(SystemState.Online).Or.EqualTo(SystemState.Degraded), behaviour.StartupDiagnostic?.Message);
			Assert.That(behaviour.HandshakeReport, Is.Not.Null);
			var presentation = UnityEngine.Object.FindAnyObjectByType<ShitDesigner.Presentation.PresentationRoot>();
			Assert.That(presentation, Is.Not.Null);
			var document = presentation.GetComponent<UIDocument>();
			Assert.That(document, Is.Not.Null);
			Assert.That(document.panelSettings, Is.Not.Null);
			Assert.That(behaviour.RuntimePanelSettings, Is.SameAs(document.panelSettings));
			Assert.That((document.panelSettings.hideFlags & HideFlags.DontSave) != 0, Is.True,
				"The Player must mutate its owned PanelSettings clone, never the serialized asset.");
			Assert.That(presentation.RootVisualElement, Is.Not.Null);
			Assert.That(presentation.RootVisualElement.Q("dock-tree"), Is.Not.Null);
			Assert.That(presentation.RootVisualElement.Q("node-graph-panel"), Is.Not.Null);
			Assert.That(presentation.RootVisualElement.Q("inspector-panel"), Is.Not.Null);
			Assert.That(presentation.Coordinator, Is.Not.Null);
			Assert.That(presentation.Coordinator.ProgramOutputControl, Is.SameAs(behaviour.Composition.OutputSurfaces),
				"The Main Top Bar callback requires the composition-owned Program output control port.");
		}

		[UnityTest, Category("GPU"), Category("PreviewPresentation"), Category("ProductionLoop")]
		public IEnumerator SerializedBootstrapProductionLoopPublishesTwoRequestedPreviewDisplaySurfaces() {
			var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.Bootstrap.DisplayTransform", Guid.NewGuid().ToString("N"));
			try {
				yield return SceneManager.LoadSceneAsync("ShitDesignerBootstrap", LoadSceneMode.Single);
				var deadline = Time.realtimeSinceStartupAsDouble + 10d;
				var behaviour = UnityEngine.Object.FindAnyObjectByType<ApplicationHost>();
				while ((behaviour == null || behaviour.Composition == null) && Time.realtimeSinceStartupAsDouble < deadline) {
					yield return null;
					behaviour = UnityEngine.Object.FindAnyObjectByType<ApplicationHost>();
				}

				Assert.That(behaviour, Is.Not.Null);
				Assert.That(behaviour.Composition, Is.Not.Null);
				var assets = UnityEngine.Object.FindAnyObjectByType<BootstrapAssets>();
				Assert.That(assets?.DisplayTransformShader, Is.Not.Null,
					"The bootstrap scene must serialize the DisplayTransform shader so Player stripping cannot remove the terminal Program/Preview path.");
				Assert.That(assets.DisplayTransformShader.name, Is.EqualTo("Hidden/ShitDesigner/DisplayTransform"));

				var application = behaviour.Composition.Application;
				Assert.That(application.NewProject("Display Transform Loop", target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
				var generatorId = NodeInstanceId.New().Value;
				var firstPreviewId = NodeInstanceId.New().Value;
				var secondPreviewId = NodeInstanceId.New().Value;
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, generatorId,
					nodeTypeId: "shitdesigner.shader.generator", nodeDisplayName: "Display Test Generator")).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, firstPreviewId,
					nodeTypeId: GraphConstants.PreviewTypeId, nodeDisplayName: "Display Test Preview 1")).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, secondPreviewId,
					nodeTypeId: GraphConstants.PreviewTypeId, nodeDisplayName: "Display Test Preview 2")).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));

				while (!GraphContains(application, generatorId, firstPreviewId, secondPreviewId) && Time.realtimeSinceStartupAsDouble < deadline)
					yield return null;
				var programId = application.ReadModel.Graph?.Model?.Nodes.SingleOrDefault(node => node.TypeId == GraphConstants.ProgramOutputTypeId)?.Id;
				Assert.That(programId, Is.Not.Null.And.Not.Empty);
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), generatorId, "image", programId, "image")).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), generatorId, "image", firstPreviewId, "image")).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				Assert.That(application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.Connect, Guid.NewGuid().ToString("D"), generatorId, "image", secondPreviewId, "image")).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));

				while (!GraphHasDisplayConnections(application, generatorId, programId, firstPreviewId, secondPreviewId) && Time.realtimeSinceStartupAsDouble < deadline)
					yield return null;
				Assert.That(application.OpenPreview(firstPreviewId).IsSuccess, Is.True);
				Assert.That(application.OpenPreview(secondPreviewId).IsSuccess, Is.True);
				Assert.That(application.RequestPreviewDemand(new ApplicationOutputDemandRequest(firstPreviewId, "image", 640, 360)).IsSuccess, Is.True);
				Assert.That(application.RequestPreviewDemand(new ApplicationOutputDemandRequest(secondPreviewId, "image", 640, 360)).IsSuccess, Is.True);

				CompositionOwnershipSnapshot ownership = null;
				while (Time.realtimeSinceStartupAsDouble < deadline) {
					ownership = behaviour.Composition.CaptureOwnershipSnapshot();
					if (ownership?.Previews?.Count == 2 && ownership.Previews.All(preview => preview.Width == 640 && preview.Height == 360 && preview.FrameNumber > 0)) break;
					yield return null;
				}
				Assert.That(ownership?.Previews, Is.Not.Null.And.Count.EqualTo(2));
				Assert.That(ownership.Previews.All(preview => preview.Width == 640 && preview.Height == 360 && preview.FrameNumber > 0), Is.True,
					"The production ApplicationLoop -> FrameCoordinator -> OutputSurfaceBridge path must publish requested 640x360 Preview display surfaces before the deadline.");
				Assert.That(ownership.TexturePool.Entries.Count(entry => entry.Owner.OwnerId == firstPreviewId || entry.Owner.OwnerId == secondPreviewId), Is.GreaterThanOrEqualTo(2),
					"Each requested Preview must own an explicit preview-display pool entry rather than falling back to the 1920x1080 upstream frame.");

				// Performance measurement must use the scalar/caller-buffer
				// path; the complete ownership projection remains only for
				// lifecycle/artifact boundaries. Compare every measurement
				// field against that real production ownership boundary once,
				// then repeat health reads without constructing another full
				// pool/node ownership snapshot. Hide/show, quality replacement
				// and delete/retire lifetime coverage remains in
				// OutputSurfaceBridgePlayModeTests, where borrowed
				// leases can be observed directly.
				var healthBuffer = new PerformanceSurfaceSnapshot[2];
				var callerOwnedHealthBuffer = healthBuffer;
				Assert.That(behaviour.Composition.TryCapturePerformanceHealth(healthBuffer, out var healthCount, out var health), Is.True);
				Assert.That(healthCount, Is.EqualTo(ownership.Previews.Count));
				Assert.That(health.RequiredPreviewCount, Is.EqualTo(ownership.Previews.Count));
				Assert.That(health.PoolBudgetBytes, Is.EqualTo(ownership.TexturePool.BudgetBytes));
				Assert.That(health.PoolLeasedBytes, Is.EqualTo(ownership.TexturePool.LeasedBytes));
				Assert.That(health.PoolFreeBytes, Is.EqualTo(ownership.TexturePool.FreeBytes));
				Assert.That(health.PoolHighWaterBytes, Is.EqualTo(ownership.TexturePool.HighWaterBytes));
				Assert.That(health.PoolBudgetWarning, Is.EqualTo(ownership.TexturePool.BudgetWarningActive));
				Assert.That(health.SceneCount, Is.EqualTo(ownership.SceneCount));
				Assert.That(health.LayerCount, Is.EqualTo(ownership.LayerCount));
				Assert.That(health.BackendCount, Is.EqualTo(ownership.BackendCount));
				Assert.That(health.NativeContextCount, Is.EqualTo(ownership.NativeContextCount));
				Assert.That(health.ActiveOutputLeaseCount, Is.EqualTo(ownership.ActiveOutputLeaseCount));
				Assert.That(health.RuntimeDisposed, Is.EqualTo(ownership.RuntimeDisposed));
				Assert.That(health.Program.Id, Is.EqualTo(ownership.Program.Id));
				Assert.That(health.Program.Width, Is.EqualTo(ownership.Program.Width));
				Assert.That(health.Program.Height, Is.EqualTo(ownership.Program.Height));
				Assert.That(health.Program.GraphicsFormat, Is.EqualTo(ownership.Program.GraphicsFormat));
				Assert.That(health.Program.TargetFramesPerSecond, Is.EqualTo(ownership.Program.TargetFramesPerSecond));
				Assert.That(health.Program.FrameNumber, Is.EqualTo(ownership.Program.FrameNumber));
				Assert.That(health.Program.IsBound, Is.EqualTo(ownership.Program.FrameNumber > 0));

				var undersizedBuffer = new PerformanceSurfaceSnapshot[1];
				Assert.That(behaviour.Composition.TryCapturePerformanceHealth(undersizedBuffer, out var insufficientCount, out var insufficient), Is.False);
				Assert.That(insufficientCount, Is.EqualTo(0));
				Assert.That(insufficient.RequiredPreviewCount, Is.EqualTo(2));
				Assert.That(insufficient.Program.Id, Is.EqualTo(ownership.Program.Id));
				Assert.That(insufficient.Program.FrameNumber, Is.EqualTo(ownership.Program.FrameNumber));
				var publicPreviews = application.ReadModel.Output.Model.Previews;
				for (var index = 0; index < healthCount; index++) {
					var fullPreview = ownership.Previews.Single(preview => preview.Id == healthBuffer[index].Id);
					var publicPreview = publicPreviews.Single(preview => preview.Id == healthBuffer[index].Id);
					Assert.That(healthBuffer[index].Id, Is.EqualTo(fullPreview.Id));
					Assert.That(healthBuffer[index].Width, Is.EqualTo(fullPreview.Width));
					Assert.That(healthBuffer[index].Height, Is.EqualTo(fullPreview.Height));
					Assert.That(healthBuffer[index].GraphicsFormat, Is.EqualTo(fullPreview.GraphicsFormat));
					Assert.That(healthBuffer[index].TargetFramesPerSecond, Is.EqualTo(fullPreview.TargetFramesPerSecond));
					Assert.That(healthBuffer[index].FrameNumber, Is.EqualTo(fullPreview.FrameNumber));
					Assert.That(healthBuffer[index].IsBound, Is.EqualTo(fullPreview.FrameNumber > 0));
					Assert.That(healthBuffer[index].TargetFramesPerSecond, Is.EqualTo(TargetFramesPerSecondForPublicQuality(publicPreview.Quality)),
						"The scalar bridge descriptor must remain aligned with the public Preview quality stage.");
				}
				for (var index = 0; index < 100; index++) {
					Assert.That(behaviour.Composition.TryCapturePerformanceHealth(healthBuffer, out var stableCount, out var stableHealth), Is.True);
					Assert.That(stableCount, Is.EqualTo(2));
					Assert.That(stableHealth.RequiredPreviewCount, Is.EqualTo(2));
					Assert.That(healthBuffer, Is.SameAs(callerOwnedHealthBuffer), "The measurement path must reuse caller-owned Preview storage.");
				}
			}
			finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
		}

		[UnityTest, Category("GUI_VisualTree"), Category("ProductionLoop"), Category("GUI_Parameters")]
		public IEnumerator SerializedBootstrapProductionLoopReusesStaticPresentationTreeWhileEffectiveValueAdvances() {
			var target = Path.Combine(Path.GetTempPath(), "ShitDesigner.Bootstrap.PresentationSeam", Guid.NewGuid().ToString("N"));
			try {
				yield return SceneManager.LoadSceneAsync("ShitDesignerBootstrap", LoadSceneMode.Single);
				var deadline = Time.realtimeSinceStartupAsDouble + 10d;
				var behaviour = UnityEngine.Object.FindAnyObjectByType<ApplicationHost>();
				var presentation = UnityEngine.Object.FindAnyObjectByType<ShitDesigner.Presentation.PresentationRoot>();
				while ((behaviour == null || behaviour.Composition == null || presentation == null || presentation.RootVisualElement == null) && Time.realtimeSinceStartupAsDouble < deadline) {
					yield return null;
					behaviour = UnityEngine.Object.FindAnyObjectByType<ApplicationHost>();
					presentation = UnityEngine.Object.FindAnyObjectByType<ShitDesigner.Presentation.PresentationRoot>();
				}

				Assert.That(behaviour?.Composition, Is.Not.Null);
				Assert.That(presentation?.RootVisualElement, Is.Not.Null);
				var document = presentation.GetComponent<UIDocument>();
				var driver = UnityEngine.Object.FindAnyObjectByType<ApplicationLoopDriver>();
				Assert.That(document, Is.Not.Null);
				Assert.That(driver?.Core, Is.SameAs(behaviour.Composition.Loop), "The seam must exercise the serialized production ApplicationLoopDriver, not a direct Application.Tick call.");
				Assert.That(ReferenceEquals(document.rootVisualElement, presentation.RootVisualElement), Is.True);

				var application = behaviour.Composition.Application;
				Assert.That(application.NewProject("Presentation seam", target, UnsavedChangesDecision.Discard).IsSuccess, Is.True);
				var generatorId = NodeInstanceId.New().Value;
				const string generatorTypeId = "shitdesigner.shader.generator";
				const string parameterId = "color";
				var addGenerator = application.SubmitGraph(new ApplicationGraphEditRequest(ApplicationGraphEditKind.AddNode, generatorId,
					nodeTypeId: generatorTypeId, nodeDisplayName: "Presentation Seam Generator"));
				Assert.That(addGenerator.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				while (!GraphContains(application, generatorId) && Time.realtimeSinceStartupAsDouble < deadline)
					yield return null;
				Assert.That(GraphContains(application, generatorId), Is.True, "The production loop did not apply the queued graph node before the deadline.");

				var controlId = LogicalControlId.New().Value;
				Assert.That(application.AddLogicalControl(new ApplicationLogicalControlRequest(controlId, "Presentation seam value", ApplicationLogicalControlKind.Value,
					mappings: new[] { new ApplicationControlMappingRequest("presentation.seam.value", "<PresentationSeam>/value") })).IsSuccess, Is.True);
				Assert.That(application.SetLogicalControlTargets(controlId, new[]
				{
					new ApplicationLogicalControlTargetRequest(generatorId, parameterId,
						ParameterValue.FromColor(new ColorValue(0f, 0f, 0f, 1f)), ParameterValue.FromColor(new ColorValue(1f, 1f, 1f, 1f)))
				}).IsSuccess, Is.True);
				Assert.That(application.ApplyExpression(new ApplicationExpressionDraft(generatorId, parameterId, ApplicationExpressionKind.Max,
					left: new ApplicationExpressionDraft(generatorId, parameterId, ApplicationExpressionKind.BaseValue),
					right: new ApplicationExpressionDraft(generatorId, parameterId, ApplicationExpressionKind.LogicalControl, controlId))).IsSuccess, Is.True);

				// Target/expression configuration publishes synchronously, while the serialized Presentation
				// adapter consumes that publication on its next LateUpdate.  Do not take a mixed Application/UI baseline.
				var configurationVersion = application.ReadModel.Shell.ReadModelVersion;
				while ((behaviour.Composition.Presentation.CurrentEnvelope == null ||
						behaviour.Composition.Presentation.CurrentEnvelope.ReadModelVersion < configurationVersion) &&
					   Time.realtimeSinceStartupAsDouble < deadline)
					yield return null;
				Assert.That(behaviour.Composition.Presentation.CurrentEnvelope, Is.Not.Null);
				Assert.That(behaviour.Composition.Presentation.CurrentEnvelope.ReadModelVersion,
					Is.GreaterThanOrEqualTo(configurationVersion),
					"The serialized Presentation adapter did not consume the synchronous logical-control target/expression publication before the deadline.");

				VisualElement parameterRow = null;
				while ((parameterRow = FindParameterRow(presentation.RootVisualElement, generatorId, parameterId)) == null && Time.realtimeSinceStartupAsDouble < deadline)
					yield return null;
				Assert.That(parameterRow, Is.Not.Null, "The live production PresentationRoot did not materialize the Generator color row before the deadline.");
				var root = presentation.RootVisualElement;
				var workspace = root.Q("dock-tree");
				var library = root.Q<Button>("node-library-" + generatorTypeId);
				var graphNode = root.Q<Button>("node-" + generatorId);
				var effective = parameterRow.Q<TextField>("parameter-row-effective-" + parameterId);
				Assert.That(workspace, Is.Not.Null);
				Assert.That(library, Is.Not.Null);
				Assert.That(graphNode, Is.Not.Null);
				Assert.That(effective, Is.Not.Null);
				var initialEffective = effective.value;
				var initialTreeCount = CountVisualTree(root);
				var initialVersion = behaviour.Composition.Presentation.CurrentEnvelope.ReadModelVersion;
				var initialApplicationGraph = application.ReadModel.Graph.Model;
				var initialApplicationParameters = application.ReadModel.Parameters.Model;
				var initialApplicationDiagnostics = application.ReadModel.DiagnosticModel.Model;
				var initialPresentationGraph = behaviour.Composition.Presentation.Current.Graph;
				var initialPresentationParameters = behaviour.Composition.Presentation.Current.Parameters;

				var stableTicks = behaviour.Composition.Loop.TickCount + 60;
				while (behaviour.Composition.Loop.TickCount < stableTicks && Time.realtimeSinceStartupAsDouble < deadline)
					yield return null;
				Assert.That(behaviour.Composition.Loop.TickCount, Is.GreaterThanOrEqualTo(stableTicks), "The serialized production ApplicationLoopDriver did not complete 60 stable Tick calls before the realtime deadline.");
				Assert.That(application.ReadModel.Graph.Model, Is.SameAs(initialApplicationGraph), "A frame-local publish must retain Application graph projection identity when topology and status are unchanged.");
				Assert.That(application.ReadModel.Parameters.Model, Is.SameAs(initialApplicationParameters), "A frame-local publish must retain the ordered Application parameter projection when effective/control revisions are unchanged.");
				Assert.That(application.ReadModel.DiagnosticModel.Model, Is.SameAs(initialApplicationDiagnostics), "An unchanged DiagnosticHub revision must retain the Application diagnostics projection.");
				Assert.That(behaviour.Composition.Presentation.Current.Graph, Is.SameAs(initialPresentationGraph), "The adapter must preserve its mapped GraphReadModel when the Application graph source is identical.");
				Assert.That(behaviour.Composition.Presentation.Current.Parameters, Is.SameAs(initialPresentationParameters), "The adapter must preserve its mapped parameter slice when the Application parameter source is identical.");
				Assert.That(FindParameterRow(presentation.RootVisualElement, generatorId, parameterId), Is.SameAs(parameterRow));
				Assert.That(CountVisualTree(presentation.RootVisualElement), Is.EqualTo(initialTreeCount), "Stable production ticks must not rebuild the workspace tree.");

				var input = application.HandleKeyboard(PhysicalKey.From("presentation.seam.value", "<PresentationSeam>/value"), true);
				Assert.That(input.Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
				var requiredTicks = behaviour.Composition.Loop.TickCount + 1;
				while ((behaviour.Composition.Loop.TickCount < requiredTicks || string.Equals(effective.value, initialEffective, StringComparison.Ordinal)) && Time.realtimeSinceStartupAsDouble < deadline)
					yield return null;
				Assert.That(behaviour.Composition.Loop.TickCount, Is.GreaterThanOrEqualTo(requiredTicks), "The serialized production ApplicationLoopDriver did not publish the input terminal frame before the realtime deadline.");

				var latest = behaviour.Composition.Presentation.Current.Parameters.Single(parameter => parameter.NodeId == generatorId && parameter.ParameterId == parameterId);
				Assert.That(behaviour.Composition.Presentation.CurrentEnvelope.ReadModelVersion, Is.GreaterThan(initialVersion), "Outer envelope metadata must advance while static Presentation slices are reused.");
				Assert.That(presentation.RootVisualElement.Q("dock-tree"), Is.SameAs(workspace));
				Assert.That(presentation.RootVisualElement.Q<Button>("node-library-" + generatorTypeId), Is.SameAs(library));
				Assert.That(presentation.RootVisualElement.Q<Button>("node-" + generatorId), Is.SameAs(graphNode));
				Assert.That(FindParameterRow(presentation.RootVisualElement, generatorId, parameterId), Is.SameAs(parameterRow));
				Assert.That(CountVisualTree(presentation.RootVisualElement), Is.EqualTo(initialTreeCount), "Stable production ticks must not rebuild the workspace, library, graph node, or parameter row.");
				Assert.That(effective.value, Is.EqualTo(latest.EffectiveValue));
				Assert.That(effective.value, Is.Not.EqualTo(initialEffective), "The final public effective value must update in the existing parameter row.");
				Assert.That(application.HandleKeyboard(PhysicalKey.From("presentation.seam.value", "<PresentationSeam>/value"), false).Status, Is.EqualTo(ApplicationCommandStatus.Accepted));
			}
			finally { try { if (Directory.Exists(target)) Directory.Delete(target, true); } catch { } }
		}

		[UnityTest, Category("GUI_VisualTree"), Category("GUI_Typography")]
		public IEnumerator SerializedBootstrapUsesBundledNotoSansAndMonoWithJapaneseFallback() {
			yield return SceneManager.LoadSceneAsync("ShitDesignerBootstrap", LoadSceneMode.Single);
			var deadline = Time.realtimeSinceStartupAsDouble + 5d;
			var presentation = UnityEngine.Object.FindAnyObjectByType<ShitDesigner.Presentation.PresentationRoot>();
			while ((presentation == null || presentation.RootVisualElement == null) && Time.realtimeSinceStartupAsDouble < deadline) {
				yield return null;
				presentation = UnityEngine.Object.FindAnyObjectByType<ShitDesigner.Presentation.PresentationRoot>();
			}

			var ui = Resources.Load<FontAsset>("NotoSans");
			var mono = Resources.Load<FontAsset>("NotoSansMono");
			var japanese = Resources.Load<FontAsset>("NotoSansJP");
			var japaneseSource = Resources.Load<Font>("Fonts/NotoSansJP");
			Assert.That(presentation, Is.Not.Null);
			Assert.That(presentation.RootVisualElement, Is.Not.Null);
			Assert.That(ui, Is.Not.Null, "The Resources theme requires the bundled NotoSans TextCore FontAsset.");
			Assert.That(mono, Is.Not.Null, "ID, timing, numeric, and diagnostic detail text require the bundled NotoSansMono TextCore FontAsset.");
			Assert.That(japanese, Is.Not.Null, "Unicode project labels require the bundled NotoSansJP fallback FontAsset.");
			Assert.That(japaneseSource, Is.Not.Null, "The Dynamic Japanese fallback requires its bundled source TTF in the Player.");
			Assert.That(japanese.atlasPopulationMode, Is.EqualTo(AtlasPopulationMode.Dynamic), "Japanese user labels require a Dynamic fallback rather than a small preseeded character table.");
			Assert.That(japanese.sourceFontFile, Is.SameAs(japaneseSource), "The Japanese fallback must remain Dynamic over the bundled source TTF.");
			Assert.That(ui.fallbackFontAssetTable, Is.Not.Null, "The generated UI FontAsset must initialize its fallback table.");
			Assert.That(mono.fallbackFontAssetTable, Is.Not.Null, "The generated Mono FontAsset must initialize its fallback table.");
			Assert.That(ui.fallbackFontAssetTable, Does.Contain(japanese));
			Assert.That(mono.fallbackFontAssetTable, Does.Contain(japanese));
			Assert.That(ui.fallbackFontAssetTable.Count(font => font == japanese), Is.EqualTo(1), "Repeated authoring must not duplicate the Japanese UI fallback.");
			Assert.That(mono.fallbackFontAssetTable.Count(font => font == japanese), Is.EqualTo(1), "Repeated authoring must not duplicate the Japanese Mono fallback.");
			Assert.That(ui.atlasTextures, Is.Not.Null.And.Not.Empty);
			Assert.That(mono.atlasTextures, Is.Not.Null.And.Not.Empty);
			Assert.That(japanese.atlasTextures, Is.Not.Null.And.Not.Empty);
			Assert.That(ui.atlasTextures.All(texture => texture != null), Is.True, "The UI FontAsset must retain its atlas texture.");
			Assert.That(mono.atlasTextures.All(texture => texture != null), Is.True, "The Mono FontAsset must retain its atlas texture.");
			Assert.That(japanese.atlasTextures.All(texture => texture != null), Is.True, "The Japanese fallback FontAsset must retain its atlas texture.");
			Assert.That(ui.material, Is.Not.Null, "The UI FontAsset must retain its distance-field material.");
			Assert.That(mono.material, Is.Not.Null, "The Mono FontAsset must retain its distance-field material.");
			Assert.That(japanese.material, Is.Not.Null, "The Japanese fallback FontAsset must retain its distance-field material.");
			var root = presentation.RootVisualElement;
			var graphClock = root.Q<Label>("graph-clock-status");
			while (!HasResolvedTypography(root, graphClock, ui, mono) && Time.realtimeSinceStartupAsDouble < deadline) {
				yield return null;
				root = presentation.RootVisualElement;
				graphClock = root?.Q<Label>("graph-clock-status");
			}

			var typographyDiagnostic = DescribeTypography(root, graphClock);
			Assert.That(HasResolvedTypography(root, graphClock, ui, mono), Is.True,
				"The live UIDocument did not resolve the Resources Noto typography before the deadline. " + typographyDiagnostic);
			Assert.That(graphClock.ClassListContains("sd-mono"), Is.True, typographyDiagnostic);

			const string JapaneseUserLabel = "日本語プロジェクト";
			var localizedLabel = new Label(JapaneseUserLabel) { name = "noto-japanese-user-label" };
			root.Add(localizedLabel);
			var localizedDeadline = Time.realtimeSinceStartupAsDouble + 5d;
			while (localizedLabel.panel == null || localizedLabel.resolvedStyle.unityFontDefinition.fontAsset != ui) {
				if (Time.realtimeSinceStartupAsDouble >= localizedDeadline) break;
				yield return null;
			}
			var addedAllJapaneseCharacters = japanese.TryAddCharacters(JapaneseUserLabel, out var missingJapaneseCharacters);
			Assert.That(localizedLabel.panel, Is.Not.Null, "User labels must attach to the live panel before typography is evaluated. " + DescribeTypography(root, localizedLabel));
			Assert.That(localizedLabel.resolvedStyle.unityFontDefinition.fontAsset, Is.SameAs(ui), "User labels must inherit the UI font and resolve missing Japanese glyphs through its local fallback. " + DescribeTypography(root, localizedLabel));
			var unavailableCharacters = new string(JapaneseUserLabel.Where(character => !japanese.HasCharacter(character)).ToArray());
			Assert.That(addedAllJapaneseCharacters, Is.True,
				"The bundled Japanese fallback could not add the user label. requested=" + DescribeUtf16Characters(JapaneseUserLabel) +
				"; apiMissing=" + DescribeUtf16Characters(missingJapaneseCharacters));
			Assert.That(unavailableCharacters, Is.Empty,
				"The Dynamic Japanese fallback did not retain every glyph for an actual user label. requested=" + DescribeUtf16Characters(JapaneseUserLabel) +
				"; unavailable=" + DescribeUtf16Characters(unavailableCharacters) +
				"; apiMissing=" + DescribeUtf16Characters(missingJapaneseCharacters));
			localizedLabel.RemoveFromHierarchy();
		}

		[UnityTest, Category("GUI_VisualTree"), Category("GUI_38_Project_SavePointer")]
		public IEnumerator SerializedBootstrapDocumentReconfigurationDoesNotDuplicateOrOverlayRuntimeUi() {
			yield return SceneManager.LoadSceneAsync("ShitDesignerBootstrap", LoadSceneMode.Single);
			yield return null;

			var behaviour = UnityEngine.Object.FindAnyObjectByType<ApplicationHost>();
			var presentation = UnityEngine.Object.FindAnyObjectByType<ShitDesigner.Presentation.PresentationRoot>();
			Assert.That(behaviour, Is.Not.Null);
			Assert.That(presentation, Is.Not.Null);
			var document = presentation.GetComponent<UIDocument>();
			Assert.That(document, Is.Not.Null);
			Assert.That(document.panelSettings, Is.Not.Null);
			var settings = document.panelSettings;
			var originalTarget = settings.targetTexture;
			try {
				// The batch runner's 640x480 window is below the specified
				// minimum. Each target drives the production UIDocument through
				// its own PanelSettings, without mutating the serialized asset.
				foreach (var specificationViewport in new[] { new Vector2Int(1600, 900), new Vector2Int(1280, 720) }) {
					var viewport = new RenderTexture(specificationViewport.x, specificationViewport.y, 0) {
						name = "BootstrapLayoutSpecificationViewport-" + specificationViewport.x + "x" + specificationViewport.y
					};
					viewport.Create();
					try {
						settings.targetTexture = viewport;

						// Match the production Awake sequence: PresentationRoot has already
						// built from its serialized UIDocument, then Bootstrap supplies that
						// exact same document and coordinator.
						presentation.ConfigureDocument(document);
						presentation.Configure(behaviour.Composition.Presentation);

						var deadline = Time.realtimeSinceStartupAsDouble + 5d;
						while (!HasSpecificationViewport(presentation, document, specificationViewport) && Time.realtimeSinceStartupAsDouble < deadline)
							yield return null;

						var root = presentation.RootVisualElement;
						var runtimeBinding = DescribeRuntimeBinding(presentation, document);
						Assert.That(HasSpecificationViewport(presentation, document, specificationViewport), Is.True,
							"The serialized UIDocument did not lay out at its owned " + specificationViewport.x + "x" + specificationViewport.y + " specification viewport before the deadline. " + runtimeBinding);

						var directChildren = DescribeDirectChildren(root);
						Assert.That(ReferenceEquals(root, document.rootVisualElement), Is.True,
							"PresentationRoot must retain the UIDocument visual root that the live panel uses. " + runtimeBinding);
						Assert.That(root.styleSheets.Contains(Resources.Load<StyleSheet>("PresentationTheme")), Is.True,
							"The serialized production root must use the Resources PresentationTheme stylesheet. " + runtimeBinding);
						Assert.That(CountByName(root, "top-bar"), Is.EqualTo(1), directChildren);
						Assert.That(CountByName(root, "dock-tree"), Is.EqualTo(1), directChildren);
						Assert.That(CountByName(root, "graph-toolbar"), Is.EqualTo(1), directChildren);
						Assert.That(CountByName(root, "project-save"), Is.EqualTo(1), directChildren);
						Assert.That(root.Q("dock-tree").ClassListContains("sd-dock-workspace"), Is.True, directChildren);

						var save = root.Q<Button>("project-save");
						var appMenu = root.Q<Button>("app-menu");
						var topBar = root.Q("top-bar");
						var projectActions = root.Q("project-actions");
						var shellActions = root.Q("shell-actions");
						var dockTree = root.Q("dock-tree");
						var graphToolbar = root.Q("graph-toolbar");
						var statusBar = root.Q("status-bar");
						Assert.That(save, Is.Not.Null);
						Assert.That(appMenu, Is.Not.Null);
						Assert.That(topBar, Is.Not.Null);
						Assert.That(projectActions, Is.Not.Null);
						Assert.That(shellActions, Is.Not.Null);
						Assert.That(dockTree, Is.Not.Null);
						Assert.That(graphToolbar, Is.Not.Null);
						Assert.That(statusBar, Is.Not.Null);
						Assert.That(root.ClassListContains("sd-root"), Is.True, runtimeBinding);
						Assert.That(topBar.ClassListContains("sd-top-bar"), Is.True, runtimeBinding);
						Assert.That(topBar.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row), runtimeBinding);
						Assert.That(projectActions.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row), runtimeBinding);
						Assert.That(shellActions.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row), runtimeBinding);
						Assert.That(graphToolbar.resolvedStyle.flexDirection, Is.EqualTo(FlexDirection.Row), runtimeBinding);
						AssertSnappedFixedLogicalHeight(topBar, 40f, runtimeBinding);
						AssertSnappedFixedLogicalHeight(statusBar, 24f, DescribeStatusBarLayout(statusBar, document, runtimeBinding));
						AssertTopBarControls(topBar, projectActions, shellActions, new VisualElement[]
						{
							appMenu,
							save,
							root.Q<Button>("shell-undo"),
							root.Q<Button>("shell-redo"),
							root.Q<Button>("graph-clock-pause"),
							root.Q<PopupField<string>>("top-layout-selector"),
							root.Q<Button>("top-layout-save"),
							root.Q<Button>("top-layout-save-as"),
							root.Q<Button>("top-layout-manage"),
							root.Q<PopupField<string>>("top-program-display-selector"),
							root.Q<Button>("top-diagnostics")
						});
						Assert.That(topBar.worldBound.Overlaps(dockTree.worldBound), Is.False,
							"The fixed top bar must not overlap the dock workspace.");
						Assert.That(dockTree.worldBound.Overlaps(statusBar.worldBound), Is.False,
							"The dock workspace must not overlap the fixed status bar.");
						Assert.That(save.worldBound.Overlaps(graphToolbar.worldBound), Is.False,
							"The one serialized-production graph toolbar must not cover Save.");
						appMenu.Focus();
						appMenu.SendEvent(NavigationSubmitEvent.GetPooled());
						yield return null;
						Assert.That(root.Q("project-menu-actions"), Is.Not.Null,
							"The App menu must expose project operations instead of overflowing the fixed top bar.");
						Assert.That(root.Q<Button>("project-open"), Is.Not.Null);
						Assert.That(root.Q<PopupField<string>>("project-open-recent"), Is.Not.Null);
						Assert.That(root.Q<Button>("project-save-as"), Is.Not.Null);
						Assert.That(root.Q<Button>("project-close"), Is.Not.Null);
						appMenu.Focus();
						appMenu.SendEvent(NavigationSubmitEvent.GetPooled());
						yield return null;
					}
					finally {
						settings.targetTexture = originalTarget;
						viewport.Release();
						UnityEngine.Object.Destroy(viewport);
					}
				}
			}
			finally {
				settings.targetTexture = originalTarget;
			}
		}

		private static bool HasSpecificationViewport(ShitDesigner.Presentation.PresentationRoot presentation, UIDocument document, Vector2Int viewport) {
			var root = presentation?.RootVisualElement;
			var reference = document?.panelSettings?.referenceResolution ?? Vector2Int.zero;
			if (reference.x <= 0 || reference.y <= 0 || viewport.x <= 0 || viewport.y <= 0) return false;
			var scale = viewport.x / (float)reference.x;
			return root != null && root.panel != null && ReferenceEquals(root, document?.rootVisualElement) &&
				   Mathf.Approximately(root.worldBound.width, viewport.x / scale) && Mathf.Approximately(root.worldBound.height, viewport.y / scale) &&
				   Mathf.Approximately(root.panel.scaledPixelsPerPoint, scale);
		}

		private static void AssertTopBarControls(VisualElement topBar, VisualElement projectActions, VisualElement shellActions, IReadOnlyList<VisualElement> controls) {
			Assert.That(projectActions.worldBound.xMin, Is.GreaterThanOrEqualTo(topBar.worldBound.xMin));
			Assert.That(projectActions.worldBound.xMax, Is.LessThanOrEqualTo(topBar.worldBound.xMax));
			Assert.That(shellActions.worldBound.xMin, Is.GreaterThanOrEqualTo(topBar.worldBound.xMin));
			Assert.That(shellActions.worldBound.xMax, Is.LessThanOrEqualTo(topBar.worldBound.xMax));
			for (var index = 0; index < controls.Count; index++) {
				var control = controls[index];
				Assert.That(control, Is.Not.Null, "A Workspace.md required top-bar control is missing at index " + index + ".");
				Assert.That(control.enabledInHierarchy, Is.True, control.name);
				AssertSnappedMinimumLogicalSize(control, 28f, control.name);
				Assert.That(control.worldBound.xMin, Is.GreaterThanOrEqualTo(topBar.worldBound.xMin), control.name);
				Assert.That(control.worldBound.xMax, Is.LessThanOrEqualTo(topBar.worldBound.xMax), control.name);
				Assert.That(control.worldBound.yMin, Is.GreaterThanOrEqualTo(topBar.worldBound.yMin), control.name);
				Assert.That(control.worldBound.yMax, Is.LessThanOrEqualTo(topBar.worldBound.yMax), control.name);
				var picked = control.panel.Pick(control.worldBound.center);
				Assert.That(ReferenceEquals(picked, control) || control.Contains(picked), Is.True,
					"A required top-bar control must receive pointer picking: " + control.name + ". " + DescribeElement(picked));
				for (var otherIndex = 0; otherIndex < index; otherIndex++)
					Assert.That(control.worldBound.Overlaps(controls[otherIndex].worldBound), Is.False,
						"Required top-bar controls overlap: " + controls[otherIndex].name + " and " + control.name + ".");
			}
		}

		private static void AssertSnappedMinimumLogicalSize(VisualElement element, float expectedLogicalSize, string diagnostic) {
			var pixelsPerPoint = element?.panel?.scaledPixelsPerPoint ?? 0f;
			Assert.That(pixelsPerPoint, Is.GreaterThan(0f), "The live panel scale is required to verify control raster snapping. " + diagnostic);
			var minimumPhysicalPixels = expectedLogicalSize * pixelsPerPoint - .501f;
			Assert.That(element.worldBound.width * pixelsPerPoint, Is.GreaterThanOrEqualTo(minimumPhysicalPixels),
				"The rendered control width may differ from its logical minimum only by nearest physical-pixel snapping. expectedLogical=" + expectedLogicalSize +
				":renderedLogical=" + element.worldBound.width + ":pixelsPerPoint=" + pixelsPerPoint + ". " + diagnostic);
			Assert.That(element.worldBound.height * pixelsPerPoint, Is.GreaterThanOrEqualTo(minimumPhysicalPixels),
				"The rendered control height may differ from its logical minimum only by nearest physical-pixel snapping. expectedLogical=" + expectedLogicalSize +
				":renderedLogical=" + element.worldBound.height + ":pixelsPerPoint=" + pixelsPerPoint + ". " + diagnostic);
		}

		private static void AssertSnappedFixedLogicalHeight(VisualElement element, float expectedLogicalHeight, string diagnostic) {
			var pixelsPerPoint = element?.panel?.scaledPixelsPerPoint ?? 0f;
			Assert.That(element, Is.Not.Null, "Expected a fixed-height shell region. " + diagnostic);
			Assert.That(pixelsPerPoint, Is.GreaterThan(0f), "The live panel scale is required to verify pixel snapping. " + diagnostic);
			var flexBasis = element.resolvedStyle.flexBasis;
			Assert.That(flexBasis.value, Is.EqualTo(expectedLogicalHeight).Within(.1f),
				"The declared logical height must remain fixed before raster pixel snapping. " + diagnostic);
			var physicalPixelError = Mathf.Abs(element.resolvedStyle.height - expectedLogicalHeight) * pixelsPerPoint;
			Assert.That(physicalPixelError, Is.LessThanOrEqualTo(.501f),
				"The rendered logical height may differ only by nearest physical-pixel snapping. expectedLogical=" + expectedLogicalHeight +
				":renderedLogical=" + element.resolvedStyle.height + ":pixelsPerPoint=" + pixelsPerPoint + ":physicalPixelError=" + physicalPixelError + ". " + diagnostic);
		}

		private static int CountByName(VisualElement root, string name) => root.Query<VisualElement>(name).ToList().Count;
		private static string DescribeDirectChildren(VisualElement root) => "Root direct children: " + string.Join(", ", root.Children().Select(child => string.IsNullOrEmpty(child.name) ? child.GetType().Name : child.name));

		private static bool HasResolvedTypography(VisualElement root, Label graphClock, FontAsset ui, FontAsset mono) {
			return root != null
				   && root.panel != null
				   && graphClock != null
				   && graphClock.panel != null
				   && root.resolvedStyle.unityFontDefinition.fontAsset == ui
				   && graphClock.resolvedStyle.unityFontDefinition.fontAsset == mono;
		}

		private static string DescribeTypography(VisualElement root, VisualElement clock) {
			var sheets = root == null
				? string.Empty
				: string.Join(",", Enumerable.Range(0, root.styleSheets.count).Select(index => root.styleSheets[index] == null ? "null" : root.styleSheets[index].name));
			return "root=" + DescribeTypographyElement(root) +
				   "; clock=" + DescribeTypographyElement(clock) +
				   "; rootStyleSheets=" + sheets;
		}

		private static string DescribeTypographyElement(VisualElement element) {
			if (element == null) return "<null>";
			var font = element.resolvedStyle.unityFontDefinition;
			return (string.IsNullOrEmpty(element.name) ? element.GetType().Name : element.name) +
				   ":classes=" + string.Join(",", element.GetClasses()) +
				   ":panel=" + (element.panel == null ? "null" : element.panel.GetType().Name) +
				   ":resolvedFont=" + (font.font == null ? "null" : font.font.name) +
				   ":resolvedFontAsset=" + (font.fontAsset == null ? "null" : font.fontAsset.name);
		}

		private static string DescribeUtf16Characters(string value) {
			if (value == null) return "<null>";
			if (value.Length == 0) return "<empty>";
			return "\"" + value + "\"[" + string.Join(",", value.Select(character => "U+" + ((int)character).ToString("X4"))) + "]";
		}

		private static string DescribePickFailure(VisualElement root, VisualElement save, VisualElement picked) {
			var named = new[]
			{
				root,
				root.Q("top-bar"),
				save,
				root.Q("project-actions"),
				root.Q("shell-actions"),
				root.Q("dock-tree"),
				root.Q("graph-toolbar"),
				root.Q("status-bar")
			};
			return "picked=" + DescribeElement(picked) + "; pickedParents=" + DescribeParents(picked) + "; regions=" +
				   string.Join(" | ", named.Select(DescribeElement));
		}

		private static string DescribeElement(VisualElement element) {
			if (element == null) return "none";
			var bounds = element.worldBound;
			var name = string.IsNullOrEmpty(element.name) ? "<unnamed>" : element.name;
			return name + "(" + element.GetType().Name + ")@" + bounds.x + "," + bounds.y + "," + bounds.width + "," + bounds.height +
				   ":classes=" + string.Join(",", element.GetClasses()) + ":picking=" + element.pickingMode + ":display=" + element.resolvedStyle.display +
				   ":direction=" + element.resolvedStyle.flexDirection + ":shrink=" + element.resolvedStyle.flexShrink;
		}

		private static string DescribeRuntimeBinding(ShitDesigner.Presentation.PresentationRoot presentation, UIDocument document) {
			var root = presentation?.RootVisualElement;
			var documentRoot = document?.rootVisualElement;
			var sheets = new List<string>();
			if (root != null)
				for (var index = 0; index < root.styleSheets.count; index++) {
					var sheet = root.styleSheets[index];
					sheets.Add(sheet == null ? "null" : sheet.name + "#" + sheet.GetHashCode());
				}
			return "presentationRoot=" + (root == null ? "null" : root.GetHashCode().ToString()) +
				   ":documentRoot=" + (documentRoot == null ? "null" : documentRoot.GetHashCode().ToString()) +
				   ":same=" + ReferenceEquals(root, documentRoot) +
				   ":rootClasses=" + (root == null ? string.Empty : string.Join(",", root.GetClasses())) +
				   ":styleSheets=" + string.Join(",", sheets);
		}

		private static string DescribeStatusBarLayout(VisualElement statusBar, UIDocument document, string runtimeBinding) {
			var style = statusBar.resolvedStyle;
			var settings = document.panelSettings;
			var panel = statusBar.panel;
			return runtimeBinding +
				   "; status:world=" + statusBar.worldBound +
				   ":layout=" + statusBar.layout +
				   ":content=" + statusBar.contentRect +
				   ":resolvedHeight=" + style.height +
				   ":minHeight=" + style.minHeight +
				   ":maxHeight=" + style.maxHeight +
				   ":borderTop=" + style.borderTopWidth +
				   ":padding=" + style.paddingLeft + "," + style.paddingTop + "," + style.paddingRight + "," + style.paddingBottom +
				   ":flex=" + style.flexDirection + "/grow=" + style.flexGrow + "/shrink=" + style.flexShrink + "/basis=" + style.flexBasis +
				   ":panelPixelsPerPoint=" + (panel == null ? "none" : panel.scaledPixelsPerPoint.ToString()) +
				   ":screen=" + Screen.width + "x" + Screen.height +
				   ":panelSettingsScale=" + settings.scale +
				   ":panelSettingsScaleMode=" + settings.scaleMode +
				   ":reference=" + settings.referenceResolution;
		}

		private static string DescribeParents(VisualElement element) {
			var chain = new List<string>();
			for (var current = element; current != null && chain.Count < 16; current = current.parent)
				chain.Add((string.IsNullOrEmpty(current.name) ? "<unnamed>" : current.name) + "(" + current.GetType().Name + ")");
			return string.Join(" <- ", chain);
		}

		private static bool GraphContains(ProjectApplication application, params string[] nodeIds) {
			var nodes = application?.ReadModel?.Graph?.Model?.Nodes;
			return nodes != null && (nodeIds ?? Array.Empty<string>()).All(id => nodes.Any(node => node.Id == id));
		}

		private static int TargetFramesPerSecondForPublicQuality(string quality) {
			var stage = -1;
			if (string.IsNullOrWhiteSpace(quality) || !quality.StartsWith("Stage", StringComparison.Ordinal) ||
				!int.TryParse(quality.Substring("Stage".Length), out stage)) {
				Assert.Fail("The public Preview quality must be an explicit Stage0 through Stage4 value, but was '" + (quality ?? string.Empty) + "'.");
			}
			switch (stage) {
				case 0: return 30;
				case 1: return 30;
				case 2: return 20;
				case 3: return 10;
				case 4: return 5;
				default:
					Assert.Fail("The public Preview quality stage must be between 0 and 4, but was " + stage + ".");
					return 0;
			}
		}

		private static VisualElement FindParameterRow(VisualElement root, string nodeId, string parameterId) {
			var key = (nodeId ?? string.Empty) + ":" + (parameterId ?? string.Empty);
			return root?.Query<VisualElement>(className: "sd-parameter-row").ToList()
				.FirstOrDefault(row => string.Equals(row.userData as string, key, StringComparison.Ordinal));
		}

		private static int CountVisualTree(VisualElement root) {
			if (root == null) return 0;
			var count = 1;
			foreach (var child in root.Children()) count += CountVisualTree(child);
			return count;
		}

		private static bool GraphHasDisplayConnections(ProjectApplication application, string generatorId, string programId, params string[] previewIds) {
			var connections = application?.ReadModel?.Graph?.Model?.Connections;
			if (connections == null) return false;
			var targets = new[] { programId }.Concat(previewIds ?? Array.Empty<string>());
			return targets.All(target => connections.Any(connection => connection.FromNodeId == generatorId &&
				connection.FromPortId == "image" && connection.ToNodeId == target && connection.ToPortId == "image"));
		}

		[UnityTest, Category("GPU"), Category("ScenePrefab"), Category("D3D12"), Category("Vulkan"), Category("Metal")]
		public IEnumerator ScenePrefabsContainVisibleDedicatedGeometryAndRenderPixels() {
			yield return SceneManager.LoadSceneAsync("ShitDesignerBootstrap", LoadSceneMode.Single);
			yield return null;
			var assets = UnityEngine.Object.FindAnyObjectByType<BootstrapAssets>();
			Assert.That(assets, Is.Not.Null, "BootstrapAssets must be present in the bootstrap scene.");
			var scene3d = assets.Scene3dPrefab;
			var scene2d = assets.Scene2dPrefab;
			Assert.That(scene3d, Is.Not.Null);
			Assert.That(scene2d, Is.Not.Null);
			Assert.That(scene3d.GetComponentsInChildren<Camera>(true), Has.Length.EqualTo(1));
			Assert.That(scene3d.GetComponent<CylindricalObjectFlythrough>(), Is.Not.Null,
				"The existing 3D node prefab slot must reference the cylindrical flythrough prefab.");
			Assert.That(scene3d.GetComponentsInChildren<AudioListener>(true), Is.Empty,
				"An isolated render-only Scene camera must not create a second application AudioListener.");
			Assert.That(scene2d.GetComponentsInChildren<Camera>(true), Has.Length.EqualTo(1));
			Assert.That(scene2d.GetComponent<RectTransform>(), Is.Null,
				"The isolated 2D root must remain a plain Transform so it cannot drive camera or geometry scale.");
			var scene2dCanvases = scene2d.GetComponentsInChildren<Canvas>(true);
			Assert.That(scene2dCanvases, Has.Length.EqualTo(1));
			var scene2dCanvas = scene2dCanvases[0];
			Assert.That(scene2dCanvas.transform.parent, Is.SameAs(scene2d.transform));
			Assert.That(scene2dCanvas.renderMode, Is.EqualTo(RenderMode.ScreenSpaceCamera));
			Assert.That(scene2dCanvas.worldCamera, Is.SameAs(scene2d.GetComponentInChildren<Camera>(true)));
			Assert.That(scene2d.GetComponentsInChildren<MeshFilter>(true), Is.Not.Empty);
			Assert.That(scene2d.GetComponentsInChildren<MeshRenderer>(true).Any(x => x.sharedMaterial != null && x.sharedMaterial.shader != null), Is.True);

			using (var manager = new SceneIsolationManager(renderSource: new UnityCameraRenderSource())) {
				var node3d = manager.Create(new SceneCreateRequest(Node("3d"), SceneNodeKind.ThreeD, "ProductionScene3D", prefab: scene3d));
				var node2d = manager.Create(new SceneCreateRequest(Node("2d"), SceneNodeKind.TwoD, "ProductionScene2D", prefab: scene2d));
				Assert.That(node3d.IsSuccess && node2d.IsSuccess, Is.True);
				Assert.That(node3d.Value.Root.GetComponent<CylindricalObjectFlythrough>(), Is.Not.Null);
				Assert.That(node3d.Value.Root.GetComponentsInChildren<MeshRenderer>(true), Is.Not.Empty,
					"The flythrough must generate its scattered geometry after the isolated prefab is instantiated.");
				var output3d = NewTarget();
				var output2d = NewTarget();
				try {
					var rendered3d = node3d.Value.Render(output3d, 32, 32, 1);
					var rendered2d = node2d.Value.Render(output2d, 32, 32, 1);
					Assert.That(rendered3d.IsSuccess, Is.True, rendered3d.IsFailure ? rendered3d.Error.Message : string.Empty);
					Assert.That(rendered2d.IsSuccess, Is.True, rendered2d.IsFailure ? rendered2d.Error.Message : string.Empty);
					Assert.That(node3d.Value.Camera.targetTexture, Is.Null,
						"SRP StandardRequest must own its destination without leaving a camera target override.");
					Assert.That(node2d.Value.Camera.targetTexture, Is.Null,
						"SRP StandardRequest must own its destination without leaving a camera target override.");
					Assert.That(node3d.Value.Camera.overrideSceneCullingMask, Is.EqualTo(ulong.MaxValue),
						"The isolated 3D camera must include its runtime additive Scene in scene culling.");
					Assert.That(node2d.Value.Camera.overrideSceneCullingMask, Is.EqualTo(ulong.MaxValue),
						"The isolated 2D camera must include its runtime additive Scene in scene culling.");
					Assert.That(node2d.Value.Camera.transform.position.z, Is.EqualTo(-10f).Within(0.001f),
						"The isolated 2D camera must keep its authored world-space depth.");
					var quadRenderer = node2d.Value.Root.GetComponentsInChildren<MeshRenderer>(true)
						.Single(renderer => renderer.gameObject.name == "DeterministicQuad");
					Assert.That(quadRenderer.bounds.extents.x, Is.GreaterThan(0.5f),
						"The isolated 2D geometry must not be shrunk by a Canvas RectTransform.");
					var quadFrustum = GeometryUtility.CalculateFrustumPlanes(node2d.Value.Camera);
					Assert.That(GeometryUtility.TestPlanesAABB(quadFrustum, quadRenderer.bounds), Is.True,
						"The isolated 2D geometry must remain inside the dedicated camera frustum.");
					AssertFullViewport(node3d.Value.Camera, "3D");
					AssertFullViewport(node2d.Value.Camera, "2D");
					yield return null;
					var color3d = ReadBrightestColor(output3d);
					var color2d = ReadBrightestColor(output2d);
					LogRuntimeDiagnostics("3D", node3d.Value, output3d, color3d);
					LogRuntimeDiagnostics("2D", node2d.Value, output2d, color2d);
					var perspectiveCamera = node3d.Value.Camera;
					var originalOrthographic = perspectiveCamera.orthographic;
					var originalOrthographicSize = perspectiveCamera.orthographicSize;
					var orthographicOutput = NewTarget();
					try {
						perspectiveCamera.orthographic = true;
						perspectiveCamera.orthographicSize = 5f;
						var orthographicRender = node3d.Value.Render(orthographicOutput, 32, 32, 2);
						Assert.That(orthographicRender.IsSuccess, Is.True, orthographicRender.IsFailure ? orthographicRender.Error.Message : string.Empty);
						yield return null;
						var orthographicPixel = ReadColor(orthographicOutput);
						Debug.Log(string.Format(
							"[ScenePrefabDiagnostic] 3D orthographic probe: position={0},forward={1},pixelRGBA=({2},{3},{4},{5})",
							perspectiveCamera.transform.position,
							perspectiveCamera.transform.forward,
							orthographicPixel.r,
							orthographicPixel.g,
							orthographicPixel.b,
							orthographicPixel.a));
					}
					finally {
						perspectiveCamera.orthographic = originalOrthographic;
						perspectiveCamera.orthographicSize = originalOrthographicSize;
						UnityEngine.Object.DestroyImmediate(orthographicOutput);
					}
					Assert.That(color3d.r, Is.GreaterThan(0.01f), "3D production prefab rendered transparent black.");
					Assert.That(color2d.g, Is.GreaterThan(0.01f), "2D production prefab rendered transparent black.");
				}
				finally {
					UnityEngine.Object.DestroyImmediate(output3d);
					UnityEngine.Object.DestroyImmediate(output2d);
					node3d.Value.Dispose();
					node2d.Value.Dispose();
				}
				for (var i = 0; i < 120 && manager.ActiveNodeCount > 0; i++) yield return null;
			}
		}

		private static void AssertFullViewport(Camera camera, string label) {
			Assert.That(camera, Is.Not.Null, label + " Scene camera is required.");
			var rect = camera.rect;
			Assert.That(rect.x, Is.EqualTo(0f), label + " Scene camera viewport x must be zero.");
			Assert.That(rect.y, Is.EqualTo(0f), label + " Scene camera viewport y must be zero.");
			Assert.That(rect.width, Is.EqualTo(1f), label + " Scene camera viewport width must be one.");
			Assert.That(rect.height, Is.EqualTo(1f), label + " Scene camera viewport height must be one.");
		}

		private static NodeInstanceId Node(string suffix) => new NodeInstanceId("00000000-0000-4000-8000-0000000000" + (suffix == "3d" ? "31" : "32"));
		private static RenderTexture NewTarget() {
			var target = new RenderTexture(32, 32, 24, RenderTextureFormat.ARGB32) { name = "ShitDesigner.ScenePrefabTest" };
			target.Create();
			return target;
		}
		private static Color ReadColor(RenderTexture target) {
			var previous = RenderTexture.active;
			RenderTexture.active = target;
			var read = new Texture2D(1, 1, TextureFormat.RGBA32, false, true);
			read.ReadPixels(new Rect(16, 16, 1, 1), 0, 0);
			read.Apply(false, false);
			var pixel = read.GetPixel(0, 0);
			UnityEngine.Object.DestroyImmediate(read);
			RenderTexture.active = previous;
			return pixel;
		}
		private static Color ReadBrightestColor(RenderTexture target) {
			var previous = RenderTexture.active;
			RenderTexture.active = target;
			var read = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false, true);
			read.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
			read.Apply(false, false);
			var pixel = read.GetPixels().OrderByDescending(color => color.maxColorComponent).First();
			UnityEngine.Object.DestroyImmediate(read);
			RenderTexture.active = previous;
			return pixel;
		}

		private static void LogRuntimeDiagnostics(string label, SceneNodeRuntime node, RenderTexture target, Color pixel) {
			var camera = node.Camera;
			var previousTarget = camera == null ? null : camera.targetTexture;
			try {
				if (camera != null) camera.targetTexture = target;
				var renderers = node.Root.GetComponentsInChildren<MeshRenderer>(true);
				var cullingParameters = default(ScriptableCullingParameters);
				var hasCullingParameters = camera != null && camera.TryGetCullingParameters(false, out cullingParameters);
				var effectiveCullingMask = hasCullingParameters ? Convert.ToUInt64(cullingParameters.cullingMask) : 0UL;
				var frustumPlanes = camera == null ? Array.Empty<Plane>() : GeometryUtility.CalculateFrustumPlanes(camera);
				var rendererDetails = renderers.Select(renderer => {
					var filter = renderer.GetComponent<MeshFilter>();
					var mesh = filter == null ? null : filter.sharedMesh;
					var material = renderer.sharedMaterial;
					var shader = material == null ? null : material.shader;
					var bounds = renderer.bounds;
					return string.Format(
						"{0}:active={1},enabled={2},layer={3},mesh={4},vertices={5},bounds={6},frustumVisible={7},material={8},shader={9},passes={10}",
						renderer.gameObject.name,
						renderer.gameObject.activeInHierarchy,
						renderer.enabled,
						renderer.gameObject.layer,
						mesh == null ? "null" : mesh.name,
						mesh == null ? 0 : mesh.vertexCount,
						bounds,
						frustumPlanes.Length > 0 && GeometryUtility.TestPlanesAABB(frustumPlanes, bounds),
						material == null ? "null" : material.name,
						shader == null ? "null" : shader.name,
						shader == null ? 0 : shader.passCount);
				});
				var projection = camera == null ? Matrix4x4.zero : camera.projectionMatrix;
				var projectionFinite = IsFiniteAndNonZero(projection);
				Debug.Log(string.Format(
				"[ScenePrefabDiagnostic] {0}: scene={1},loaded={2},target={3}x{4},root={5},camera={6},activeAndEnabled={7},cameraEnabled={8},position={9},forward={10},cameraType={11},stereoEnabled={12},stereoEye={13},pixel={14}x{15},scaledPixel={16}x{17},rect={18},pixelRect={19},aspect={20},orthographic={21},orthographicSize={22},fieldOfView={23},near={24},far={25},projectionFinite={26},cullingMask={27},forceIntoRT={28},sceneCulling={29},effectiveCulling={30},cullingParameters={31},occlusion={32},clear={33},rendererType={34},renderer={35},stack={36},pixelRGBA=({37},{38},{39},{40}),meshRenderers=[{41}]",
				label,
				node.Scene.name,
				node.Scene.isLoaded,
				target == null ? 0 : target.width,
				target == null ? 0 : target.height,
				node.Root == null ? "null" : node.Root.name,
				camera == null ? "null" : camera.name,
				camera != null && camera.isActiveAndEnabled,
				camera != null && camera.enabled,
				camera == null ? Vector3.zero : camera.transform.position,
				camera == null ? Vector3.forward : camera.transform.forward,
				camera == null ? "null" : camera.cameraType,
				camera != null && camera.stereoEnabled,
				camera == null ? "null" : camera.stereoTargetEye,
				camera == null ? 0 : camera.pixelWidth,
				camera == null ? 0 : camera.pixelHeight,
				camera == null ? 0 : camera.scaledPixelWidth,
				camera == null ? 0 : camera.scaledPixelHeight,
				camera == null ? default(Rect) : camera.rect,
				camera == null ? default(Rect) : camera.pixelRect,
				camera == null ? 0f : camera.aspect,
				camera != null && camera.orthographic,
				camera == null ? 0f : camera.orthographicSize,
				camera == null ? 0f : camera.fieldOfView,
				camera == null ? 0f : camera.nearClipPlane,
				camera == null ? 0f : camera.farClipPlane,
				projectionFinite,
				camera == null ? 0 : camera.cullingMask,
				camera != null && camera.forceIntoRenderTexture,
				camera == null ? 0UL : camera.overrideSceneCullingMask,
				effectiveCullingMask,
				hasCullingParameters,
				camera != null && camera.useOcclusionCulling,
				camera == null ? CameraClearFlags.Nothing : camera.clearFlags,
				camera == null || camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null
					? "null"
					: camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().renderType,
				camera == null || camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null || camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().scriptableRenderer == null
					? "null"
					: camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().scriptableRenderer.GetType().FullName,
				camera == null || camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() == null || camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().cameraStack == null
					? 0
					: camera.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>().cameraStack.Count,
				pixel.r,
				pixel.g,
				pixel.b,
				pixel.a,
				string.Join(";", rendererDetails.ToArray())));
			}
			finally {
				if (camera != null) camera.targetTexture = previousTarget;
			}
		}

		private static bool IsFiniteAndNonZero(Matrix4x4 matrix) {
			var nonZero = false;
			for (var row = 0; row < 4; row++) {
				for (var column = 0; column < 4; column++) {
					var value = matrix[row, column];
					if (float.IsNaN(value) || float.IsInfinity(value)) return false;
					if (Mathf.Abs(value) > 1e-6f) nonZero = true;
				}
			}
			return nonZero;
		}
	}
}
