using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.TestTools;

namespace ShitDesigner.Main.Tests {
	public sealed class MainLiveScenePlayModeTests {
		[UnityTest]
		public IEnumerator BlackoutClearsRenderedProgramAndOverlayFramesAndThenRestoresRendering() {
			SceneManager.LoadScene("Main", LoadSceneMode.Single);
			yield return null;

			var host = Object.FindAnyObjectByType<ApplicationLiveHost>();
			Assert.That(host, Is.Not.Null);
			for (var frame = 0; frame < 60 && (host.State != ApplicationLiveHostState.Running || host.ReadModel?.ProgramFrameNumber == 0); frame++)
				yield return null;
			Assert.That(host.State, Is.EqualTo(ApplicationLiveHostState.Running), host.LastDiagnostic);
			host.enabled = false;

			var runtime = (LiveGraphRuntime)typeof(ApplicationLiveHost).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
			Assert.That(runtime, Is.Not.Null);
			var launch = new LiveParameterQueue();
			Assert.That(launch.EnqueueLaunchPatch(host.ReadModel.Patches.First().Id).Accepted, Is.True);
			var requests = new List<LiveParameterRequest>();
			launch.Drain(requests);
			Assert.That(runtime.Apply(requests.Single()).Applied, Is.True);
			runtime.Evaluate(1d / 60d);
			runtime.SceneUpdate();
			Assert.That(HasVisiblePixels(runtime.Render().Primary.Texture), Is.True);

			var blackoutFrames = runtime.Render(blackout: true);
			Assert.That(blackoutFrames.Count, Is.EqualTo(2));
			Assert.That(HasVisiblePixels(blackoutFrames[0].Texture), Is.False);
			Assert.That(HasVisiblePixels(blackoutFrames[1].Texture), Is.False);

			var restoredFrames = runtime.Render();
			Assert.That(HasVisiblePixels(restoredFrames.Primary.Texture), Is.True);
			host.Shutdown();
		}

		[UnityTest]
		public IEnumerator SceneTimeJogReturnsToNormalAfterOneEvaluation() {
			SceneManager.LoadScene("Main", LoadSceneMode.Single);
			yield return null;

			var host = Object.FindAnyObjectByType<ApplicationLiveHost>();
			Assert.That(host, Is.Not.Null);
			for (var frame = 0; frame < 60 && host.State != ApplicationLiveHostState.Running; frame++) yield return null;
			Assert.That(host.State, Is.EqualTo(ApplicationLiveHostState.Running), host.LastDiagnostic);
			host.enabled = false;

			var runtime = (LiveGraphRuntime)typeof(ApplicationLiveHost).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
			Assert.That(runtime, Is.Not.Null);
			var queue = new LiveParameterQueue();
			Assert.That(queue.EnqueueJogSceneTime(.5f).Accepted, Is.True);
			var requests = new List<LiveParameterRequest>();
			queue.Drain(requests);
			Assert.That(runtime.Apply(requests.Single()).Applied, Is.True);
			Assert.That(runtime.SceneTimePlaybackRate, Is.EqualTo(1.5d));

			runtime.Evaluate(1d / 60d);

			Assert.That(runtime.SceneTimePlaybackRate, Is.EqualTo(1d));
			host.Shutdown();
		}

		[UnityTest]
		public IEnumerator MainBootsUnloadedThenRendersAndSwitchesItsAuthoredLiveGraph() {
			SceneManager.LoadScene("Main", LoadSceneMode.Single);
			yield return null;

			var host = Object.FindAnyObjectByType<ApplicationLiveHost>();
			Assert.That(host, Is.Not.Null);
			for (var frame = 0; frame < 60 && (host.State != ApplicationLiveHostState.Running || host.ReadModel == null || host.ReadModel.ProgramFrameNumber == 0); frame++)
				yield return null;

			Assert.That(host.State, Is.EqualTo(ApplicationLiveHostState.Running), host.LastDiagnostic);
			Assert.That(host.ReadModel, Is.Not.Null);
			Assert.That(host.ReadModel.Patches.Count, Is.EqualTo(4));
			Assert.That(host.ReadModel.ProgramTexture, Is.Not.Null);
			var outputSizes = LiveGraphBootstrap.ResolveOutputRenderSizes();
			Assert.That(host.ReadModel.ProgramTexture.width, Is.EqualTo(outputSizes.Program.Width));
			Assert.That(host.ReadModel.ProgramTexture.height, Is.EqualTo(outputSizes.Program.Height));
			Assert.That(host.ReadModel.ProgramTexture.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
			Assert.That(host.ReadModel.ProgramFrameNumber, Is.GreaterThan(0));
			Assert.That(host.ReadModel.Sequencers, Has.Count.EqualTo(2));
			Assert.That(host.ReadModel.Sequencers.All(sequencer => sequencer.ActiveLaneMasks.Count == LiveStepSequencer.StepCount), Is.True);
			var runtime = (LiveGraphRuntime)typeof(ApplicationLiveHost).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
			Assert.That(host.ReadModel.LoadedPatchId, Is.Empty);
			Assert.That(runtime.MainCuePatchIds, Has.All.Empty);
			Assert.That(host.MainCuePatchIds, Is.EqualTo(runtime.MainCuePatchIds));
			Assert.That(runtime.ActiveMainCueIndex, Is.Zero);
			Assert.That(runtime.CurrentFrames.Count, Is.EqualTo(2));
			Assert.That(runtime.CurrentFrames[0].Texture, Is.SameAs(host.ReadModel.ProgramTexture));
			Assert.That(runtime.CurrentFrames[1].Texture, Is.Not.SameAs(runtime.CurrentFrames[0].Texture));
			Assert.That(runtime.CurrentFrames[1].Texture.width, Is.EqualTo(outputSizes.Overlay.Width));
			Assert.That(runtime.CurrentFrames[1].Texture.height, Is.EqualTo(outputSizes.Overlay.Height));
			Assert.That(HasVisiblePixels(host.ReadModel.ProgramTexture), Is.False);

			var initialPatch = host.ReadModel.Patches.First();
			Assert.That(host.AssignMainPatchToCue(0, initialPatch.Id), Is.True);
			for (var frame = 0; frame < 60 && host.ReadModel.LoadedPatchId != initialPatch.Id; frame++) yield return null;

			Assert.That(host.ReadModel.LoadedPatchId, Is.EqualTo(initialPatch.Id));
			Assert.That(runtime.MainCuePatchIds, Is.EqualTo(new[] { initialPatch.Id, string.Empty }));
			Assert.That(runtime.ActiveMainCueIndex, Is.Zero);

			var nextPatch = host.ReadModel.Patches.First(patch => patch.Id != initialPatch.Id);
			var preload = host.ParameterQueue.EnqueuePreloadPatch(nextPatch.Id);
			Assert.That(preload.Accepted, Is.True);
			for (var frame = 0; frame < 60 && runtime.PreloadedPatchId != nextPatch.Id; frame++) yield return null;

			Assert.That(host.ReadModel.LoadedPatchId, Is.EqualTo(initialPatch.Id));
			Assert.That(runtime.PreloadedPatchId, Is.EqualTo(nextPatch.Id));
			Assert.That(runtime.MainCuePatchIds, Is.EqualTo(new[] { initialPatch.Id, nextPatch.Id }));
			Assert.That(runtime.ActiveMainCueIndex, Is.Zero);
			Assert.That(host.ReadModel.RequestResults.Any(result => result.SequenceNumber == preload.SequenceNumber && result.Applied), Is.True);
			var load = host.ParameterQueue.EnqueueLoadPatch(nextPatch.Id);
			Assert.That(load.Accepted, Is.True);
			for (var frame = 0; frame < 60 && host.ReadModel.LoadedPatchId != nextPatch.Id; frame++) yield return null;

			Assert.That(host.ReadModel.LoadedPatchId, Is.EqualTo(nextPatch.Id));
			Assert.That(runtime.MainCuePatchIds, Is.EqualTo(new[] { initialPatch.Id, nextPatch.Id }));
			Assert.That(runtime.ActiveMainCueIndex, Is.EqualTo(1));
			Assert.That(host.ReadModel.RequestResults.Any(result => result.SequenceNumber == load.SequenceNumber && result.Applied), Is.True);
			Assert.That(host.ReadModel.ProgramFrameNumber, Is.GreaterThan(1));
			var parameter = host.ParameterQueue.EnqueueSetParameter(nextPatch.Id, "scale", 1f);
			for (var frame = 0; frame < 60 && !host.ReadModel.RequestResults.Any(result => result.SequenceNumber == parameter.SequenceNumber); frame++) yield return null;
			Assert.That(host.ReadModel.RequestResults.Any(result => result.SequenceNumber == parameter.SequenceNumber && result.Applied), Is.True);
			Assert.That(host.ReadModel.Parameters.Single(item => item.Id == "scale").Value, Is.EqualTo(1f));
			var panelRenderer = host.GetComponent<PanelRenderer>();
			VisualElement ui = null;
			panelRenderer.RegisterUIReloadCallback((_, root) => ui = root);
			var visualTreeAsset = panelRenderer.visualTreeAsset;
			panelRenderer.visualTreeAsset = null;
			panelRenderer.visualTreeAsset = visualTreeAsset;
			for (var frame = 0; frame < 60 && ui == null; frame++) yield return null;
			Assert.That(ui, Is.Not.Null);
			yield return null;
			Assert.That(ui.Q<VisualElement>("parameter-channel-scale"), Is.Not.Null);
			Assert.That(ui.Q<Slider>("parameter-scale").direction, Is.EqualTo(SliderDirection.Vertical));
			Assert.That(ui.Q<Label>("parameter-value-scale").text, Is.EqualTo("1.00"));
			Assert.That(ui.Q<VisualElement>("top-bar"), Is.Null);
			Assert.That(ui.Q<Label>("display-selector"), Is.Null);
			Assert.That(ui.Q<Button>("output-toggle"), Is.Null);
			Assert.That(ui.Q<Button>("identify-display"), Is.Null);
			Assert.That(ui.Q<VisualElement>("output-confirm-overlay"), Is.Null);
			var instantEffectCues = ui.Q<VisualElement>("instant-effect-cues");
			Assert.That(instantEffectCues, Is.Not.Null);
			Assert.That(instantEffectCues.parent.IndexOf(instantEffectCues),
				Is.EqualTo(instantEffectCues.parent.IndexOf(ui.Q<VisualElement>("sequencer-controls")) + 1));
			Assert.That(instantEffectCues.Query<Button>(className: "instant-effect-cue-button").ToList().Select(button => button.text),
				Is.EqualTo(Enumerable.Range(1, InstantEffectTriggerContract.TriggerCount).Select(index => index.ToString())));
			var patchControls = ui.Q<VisualElement>("patch-controls");
			var sidebarTabs = ui.Q<VisualElement>("sidebar-tabs");
			var mainPatchControls = ui.Q<ScrollView>("main-patch-controls");
			var overlayPatchControls = ui.Q<ScrollView>("overlay-patch-controls");
			var effectNodeControls = ui.Q<ScrollView>("effect-node-controls");
			Assert.That(sidebarTabs.layout.height, Is.EqualTo(40f).Within(0.5f));
			Assert.That(sidebarTabs.layout.width, Is.GreaterThanOrEqualTo(240f));
			Assert.That(ui.Query<Button>(className: "sidebar-tab").ToList().Select(tab => tab.layout.width),
				Is.All.GreaterThanOrEqualTo(80f),
				"Every catalog tab must retain a usable pointer target when the preview area is constrained.");
			Assert.That(mainPatchControls.worldBound.yMin, Is.GreaterThanOrEqualTo(sidebarTabs.worldBound.yMax - 0.5f));
			Assert.That(ui.Query<Button>(className: "sidebar-tab").ToList().Select(tab => tab.text), Is.EqualTo(new[] { "MAIN", "OVERLAY", "FX" }));
			Assert.That(mainPatchControls, Is.Not.Null);
			Assert.That(overlayPatchControls, Is.Not.Null);
			Assert.That(effectNodeControls, Is.Not.Null);
			Assert.That(mainPatchControls.horizontalScrollerVisibility, Is.EqualTo(ScrollerVisibility.Hidden));
			Assert.That(overlayPatchControls.horizontalScrollerVisibility, Is.EqualTo(ScrollerVisibility.Hidden));
			Assert.That(effectNodeControls.horizontalScrollerVisibility, Is.EqualTo(ScrollerVisibility.Hidden));
			Assert.That(mainPatchControls.mode, Is.EqualTo(ScrollViewMode.Vertical));
			Assert.That(overlayPatchControls.mode, Is.EqualTo(ScrollViewMode.Vertical));
			Assert.That(effectNodeControls.mode, Is.EqualTo(ScrollViewMode.Vertical));
			Assert.That(mainPatchControls.parent, Is.SameAs(patchControls));
			Assert.That(mainPatchControls, Is.Not.SameAs(overlayPatchControls));
			Assert.That(mainPatchControls.Query<Button>().ToList().Count, Is.EqualTo(host.ReadModel.Patches.Count(patch => patch.Role == LivePatchRole.Main)));
			Assert.That(overlayPatchControls.Query<Button>().ToList().Count, Is.EqualTo(host.ReadModel.Patches.Count(patch => patch.Role == LivePatchRole.Overlay)));
			Assert.That(effectNodeControls.Query<Button>(className: "effect-node-button").ToList().Count, Is.EqualTo(host.ReadModel.EffectNodes.Count));
			Assert.That(host.ReadModel.EffectNodes, Is.Not.Empty);
			Assert.That(effectNodeControls.Query<Button>(className: "effect-category-button").ToList().Count,
				Is.EqualTo(host.ReadModel.EffectNodes.Select(effect => effect.Category).Distinct().Count()));
			Assert.That(effectNodeControls.Query<VisualElement>(className: "effect-category-items").ToList().Count(items => !items.ClassListContains("is-hidden")), Is.EqualTo(1));
			Assert.That(effectNodeControls.Query<Button>(className: "effect-node-button").ToList().All(button =>
				host.ReadModel.EffectNodes.Any(node => node.TypeId == (string)button.userData)), Is.True);
			var firstMainButton = mainPatchControls.Query<Button>().First();
			var mainPatches = host.ReadModel.Patches.Where(patch => patch.Role == LivePatchRole.Main).ToArray();
			var selectedOnlyPatch = mainPatches.First(patch => patch.Id != host.ReadModel.LoadedPatchId);
			var loadedPatchBeforeSelection = host.ReadModel.LoadedPatchId;
			var uiController = host.GetComponent<LiveUiController>();
			typeof(LiveUiController).GetMethod("ChoosePatch", BindingFlags.Instance | BindingFlags.NonPublic)
				?.Invoke(uiController, new object[] { selectedOnlyPatch.Id });
			yield return null;
			Assert.That(host.ReadModel.SelectedCatalogItemId, Is.EqualTo(selectedOnlyPatch.Id));
			Assert.That(host.ReadModel.LoadedPatchId, Is.EqualTo(loadedPatchBeforeSelection));
			var cueSlots = Enumerable.Range(1, ApplicationLiveHost.MainCueCount)
				.Select(index => ui.Q<VisualElement>("cue-slot-" + index)).ToArray();
			Assert.That(cueSlots, Has.All.Not.Null);
			Assert.That(host.ReadModel.MainCuePreviews, Has.Count.EqualTo(ApplicationLiveHost.MainCueCount));
			Assert.That(host.ReadModel.MainCuePreviews[host.ActiveMainCueIndex], Is.Not.Null);
			Assert.That(cueSlots[host.ActiveMainCueIndex].ClassListContains("has-preview"), Is.True);
			var dragStroke = ui.Q<VisualElement>("main-cue-drag-stroke");
			using (var pointerDown = PointerDownEvent.GetPooled(new Event {
				type = EventType.MouseDown,
				button = 0,
				mousePosition = firstMainButton.worldBound.center
			})) {
				firstMainButton.SendEvent(pointerDown);
			}
			Assert.That(firstMainButton.ClassListContains("is-dragging"), Is.True);
			Assert.That(dragStroke.ClassListContains("is-active"), Is.True);
			using (var pointerMove = PointerMoveEvent.GetPooled(new Event {
				type = EventType.MouseMove,
				mousePosition = cueSlots[0].worldBound.center
			})) {
				ui.SendEvent(pointerMove);
			}
			Assert.That(cueSlots[0].ClassListContains("is-drop-target"), Is.True);
			using (var pointerUp = PointerUpEvent.GetPooled(new Event {
				type = EventType.MouseUp,
				button = 0,
				mousePosition = cueSlots[0].worldBound.center
			})) {
				ui.SendEvent(pointerUp);
			}
			yield return null;
			Assert.That(host.MainCuePatchIds[0], Is.EqualTo(firstMainButton.userData as string));
			Assert.That(host.ReadModel.MainCuePreviews[0], Is.Not.Null);
			Assert.That(cueSlots[0].ClassListContains("has-preview"), Is.True);
			Assert.That(firstMainButton.ClassListContains("is-dragging"), Is.False);
			Assert.That(cueSlots[0].ClassListContains("is-drop-target"), Is.False);
			Assert.That(dragStroke.ClassListContains("is-active"), Is.False);
			Assert.That(host.AssignMainPatchToCue(1 - host.ActiveMainCueIndex,
				host.ReadModel.Patches.First(patch => patch.Role == LivePatchRole.Overlay).Id), Is.False);
			Assert.That(host.MainCuePatchIds, Has.All.Not.Empty);
			Assert.That(cueSlots.Select(slot => slot.Q<Label>().text),
				Is.EqualTo(host.MainCuePatchIds.Select(patchId => mainPatches.Single(patch => patch.Id == patchId).Name)));
			Assert.That(cueSlots.Select(slot => slot.ClassListContains("is-active")),
				Is.EqualTo(Enumerable.Range(0, ApplicationLiveHost.MainCueCount).Select(index => index == host.ActiveMainCueIndex)));
			var activeCueIndex = System.Array.FindIndex(cueSlots, slot => slot.ClassListContains("is-active"));
			Assert.That(activeCueIndex, Is.GreaterThanOrEqualTo(0));
			var activeCuePatchId = host.MainCuePatchIds[activeCueIndex];
			var replacementButton = ui.Q<Button>("patch-" + mainPatches.First(patch => patch.Id != activeCuePatchId).Id);
			using (var pointerDown = PointerDownEvent.GetPooled(new Event {
				type = EventType.MouseDown,
				button = 0,
				mousePosition = replacementButton.worldBound.center
			})) {
				replacementButton.SendEvent(pointerDown);
			}
			using (var pointerMove = PointerMoveEvent.GetPooled(new Event {
				type = EventType.MouseMove,
				mousePosition = cueSlots[activeCueIndex].worldBound.center
			})) {
				ui.SendEvent(pointerMove);
			}
			Assert.That(cueSlots[activeCueIndex].ClassListContains("is-drop-target"), Is.True);
			Assert.That(dragStroke.ClassListContains("is-rejected"), Is.False);
			using (var pointerUp = PointerUpEvent.GetPooled(new Event {
				type = EventType.MouseUp,
				button = 0,
				mousePosition = cueSlots[activeCueIndex].worldBound.center
			})) {
				ui.SendEvent(pointerUp);
			}
			for (var frame = 0; frame < 60 && host.MainCuePatchIds[activeCueIndex] != replacementButton.userData as string; frame++) yield return null;
			Assert.That(host.MainCuePatchIds[activeCueIndex], Is.EqualTo(replacementButton.userData as string));
			Assert.That(dragStroke.ClassListContains("is-rejected"), Is.False);
			var remainingCueIndex = 1 - activeCueIndex;
			using (var pointerDown = PointerDownEvent.GetPooled(new Event {
				type = EventType.MouseDown,
				button = 1,
				mousePosition = cueSlots[activeCueIndex].worldBound.center
			})) {
				cueSlots[activeCueIndex].SendEvent(pointerDown);
			}
			for (var frame = 0; frame < 60 && !string.IsNullOrEmpty(host.MainCuePatchIds[activeCueIndex]); frame++) yield return null;
			Assert.That(host.MainCuePatchIds[activeCueIndex], Is.Empty);
			Assert.That(host.ActiveMainCueIndex, Is.EqualTo(remainingCueIndex));
			Assert.That(host.ReadModel.MainCuePreviews[activeCueIndex], Is.Null);
			Assert.That(cueSlots[activeCueIndex].Q<Label>().text, Is.EqualTo("Cue Slot " + (activeCueIndex + 1)));
			Assert.That(cueSlots[activeCueIndex].ClassListContains("has-preview"), Is.False);
			using (var pointerDown = PointerDownEvent.GetPooled(new Event {
				type = EventType.MouseDown,
				button = 1,
				mousePosition = cueSlots[remainingCueIndex].worldBound.center
			})) {
				cueSlots[remainingCueIndex].SendEvent(pointerDown);
			}
			for (var frame = 0; frame < 60 && !string.IsNullOrEmpty(host.MainCuePatchIds[remainingCueIndex]); frame++) yield return null;
			Assert.That(host.MainCuePatchIds, Has.All.Empty);
			Assert.That(host.ReadModel.LoadedPatchId, Is.Empty);
			Assert.That(host.ReadModel.MainCuePreviews, Has.All.Null);
			Assert.That(host.ReadModel.ProgramTexture, Is.Not.Null);
			Assert.That(host.State, Is.EqualTo(ApplicationLiveHostState.Running), host.LastDiagnostic);
			var relaunch = host.ParameterQueue.EnqueueLaunchPatch(mainPatches[0].Id);
			for (var frame = 0; frame < 60 && host.ReadModel.LoadedPatchId != mainPatches[0].Id; frame++) yield return null;
			Assert.That(relaunch.Accepted, Is.True);
			Assert.That(host.ReadModel.LoadedPatchId, Is.EqualTo(mainPatches[0].Id));
			Assert.That(host.State, Is.EqualTo(ApplicationLiveHostState.Running), host.LastDiagnostic);
			Assert.That(firstMainButton.worldBound.yMin, Is.EqualTo(mainPatchControls.contentViewport.worldBound.yMin).Within(0.5f));
			var initialMainListTop = firstMainButton.worldBound.yMin;
			host.MoveCatalogSelection(0, 1);
			yield return null;
			Assert.That(firstMainButton.worldBound.yMin, Is.EqualTo(initialMainListTop).Within(0.5f));
			host.MoveCatalogSelection(0, -1);
			yield return null;
			var sequencerControls = ui.Q<VisualElement>("sequencer-controls");
			Assert.That(sequencerControls.Query<Button>(className: "sequencer-step").ToList(),
				Has.Count.EqualTo(LiveStepSequencer.OverlayLaneCount * LiveStepSequencer.StepCount));
			Assert.That(ui.Q<Button>("sequencer-overlay-lane-label-7"), Is.Not.Null);
			Assert.That(ui.Q<VisualElement>("effect-sequencer"), Is.Null);
			var overlayCell = ui.Q<Button>("sequencer-overlay-lane-2-step-4");
			Assert.That(host.CycleSequencerCellMode(LiveSequencerKind.Overlay, 2, 4).Accepted, Is.True);
			Assert.That(host.CycleSequencerCellMode(LiveSequencerKind.Overlay, 2, 4).Accepted, Is.True);
			yield return null;
			Assert.That(overlayCell.text, Is.EqualTo("ADD"));
			using (var pointerDown = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 1 }))
				overlayCell.SendEvent(pointerDown);
			yield return null;
			var overlayAfterRightClick = host.ReadModel.Sequencers.Single(sequencer => sequencer.Kind == LiveSequencerKind.Overlay);
			Assert.That(overlayAfterRightClick.GetCellMode(2, 4), Is.EqualTo(LiveSequencerCellMode.Off));
			Assert.That(overlayCell.text, Is.EqualTo("OFF"));
			Assert.That(overlayCell.ClassListContains("is-set"), Is.False);
			Assert.That(ui.Q<VisualElement>("patch-slot-controls"), Is.Null);
			Assert.That(ui.Query<Button>(className: "is-loaded").ToList(), Is.Empty);
			var rememberedMainPatch = host.ReadModel.Patches.Last(patch => patch.Role == LivePatchRole.Main);
			var rememberedOverlayPatch = host.ReadModel.Patches.First(patch => patch.Role == LivePatchRole.Overlay);
			var nextOverlayPatch = host.ReadModel.Patches.Where(patch => patch.Role == LivePatchRole.Overlay).Skip(1).First();
			host.MoveCatalogSelection(1, 0);
			yield return null;
			Assert.That(host.ReadModel.SelectedCatalogRole, Is.EqualTo(LiveCatalogRole.Overlay));
			var overlayPatchButton = ui.Q<Button>("patch-" + rememberedOverlayPatch.Id);
			var overlayDropLane = ui.Q<VisualElement>("sequencer-overlay-lane-label-3").parent;
			using (var pointerDown = MouseDownEvent.GetPooled(new Event {
				type = EventType.MouseDown,
				button = 0,
				mousePosition = overlayPatchButton.worldBound.center
			})) {
				overlayPatchButton.SendEvent(pointerDown);
			}
			using (var pointerMove = MouseMoveEvent.GetPooled(new Event {
				type = EventType.MouseMove,
				mousePosition = overlayDropLane.worldBound.center
			})) {
				ui.SendEvent(pointerMove);
			}
			Assert.That(overlayDropLane.ClassListContains("is-drop-target"), Is.True);
			using (var pointerUp = MouseUpEvent.GetPooled(new Event {
				type = EventType.MouseUp,
				button = 0,
				mousePosition = overlayDropLane.worldBound.center
			})) {
				ui.SendEvent(pointerUp);
			}
			using (var click = ClickEvent.GetPooled()) ui.Q<Button>("sequencer-overlay-lane-label-3").SendEvent(click);
			yield return null;
			var overlayAfterDrop = host.ReadModel.Sequencers.Single(sequencer => sequencer.Kind == LiveSequencerKind.Overlay);
			Assert.That(overlayAfterDrop.LanePatchIds[3], Is.EqualTo(rememberedOverlayPatch.Id));
			Assert.That(Enumerable.Range(0, LiveStepSequencer.StepCount).Any(stepIndex => overlayAfterDrop.IsActive(3, stepIndex)), Is.False,
				"The click synthesized after a drop must not toggle the destination row.");
			Assert.That(overlayDropLane.ClassListContains("is-drop-target"), Is.False);
			Assert.That(host.UnassignOverlayPatchFromLane(3).Accepted, Is.True);
			Assert.That(host.AssignOverlayPatchToLane(0, rememberedOverlayPatch.Id).Accepted, Is.True);
			yield return null;
			Assert.That(ui.Q<Button>("sequencer-overlay-lane-label-0").ClassListContains("is-assigned"), Is.True);
			Assert.That(host.ReadModel.OverlayLanePreviews, Has.Count.EqualTo(LiveStepSequencer.OverlayLaneCount));
			Assert.That(host.ReadModel.OverlayLanePreviews[0], Is.Not.Null);
			Assert.That(ui.Q<Button>("sequencer-overlay-lane-label-0").ClassListContains("has-preview"), Is.True);
			using (var pointerDown = PointerDownEvent.GetPooled(new Event { type = EventType.MouseDown, button = 1 }))
				ui.Q<Button>("sequencer-overlay-lane-label-0").SendEvent(pointerDown);
			yield return null;
			Assert.That(host.ReadModel.Sequencers.Single(sequencer => sequencer.Kind == LiveSequencerKind.Overlay).LanePatchIds[0], Is.Empty);
			Assert.That(host.ReadModel.OverlayLanePreviews[0], Is.Null);
			Assert.That(ui.Q<Button>("sequencer-overlay-lane-label-0").ClassListContains("is-assigned"), Is.False);
			Assert.That(host.AssignOverlayPatchToLane(0, rememberedOverlayPatch.Id).Accepted, Is.True);
			Assert.That(host.AssignOverlayPatchToLane(1, rememberedOverlayPatch.Id).Accepted, Is.True);
			yield return null;
			Assert.That(host.ReadModel.OverlayLanePreviews[1], Is.SameAs(host.ReadModel.OverlayLanePreviews[0]));
			var sharedOverlayScenePrefix = "ShitDesigner.Main.LiveScene." + rememberedOverlayPatch.Id + ".1560x854.";
			Assert.That(Enumerable.Range(0, SceneManager.sceneCount).Select(SceneManager.GetSceneAt)
				.Count(scene => scene.name.StartsWith(sharedOverlayScenePrefix, System.StringComparison.Ordinal)), Is.EqualTo(1),
				"Overlay lanes assigned to the same scene must share one full-resolution runtime.");
			host.MoveCatalogSelection(-1, 0);
			yield return null;
			Assert.That(host.ReadModel.SelectedCatalogRole, Is.EqualTo(LiveCatalogRole.Main));
			Assert.That(host.ReadModel.SelectedCatalogItemId, Is.EqualTo(rememberedMainPatch.Id));
			Assert.That(host.AssignSelectedOverlayPatchToLane(7).Accepted, Is.False);
			Assert.That(host.AssignOverlayPatchToLane(7, rememberedOverlayPatch.Id).Accepted, Is.True);
			Assert.That(host.AssignOverlayPatchToLane(7, rememberedMainPatch.Id).Accepted, Is.False);
			Assert.That(ui.Q<Button>("main-tab").ClassListContains("is-selected"), Is.True);
			host.MoveCatalogSelection(1, 0);
			yield return null;
			Assert.That(host.ReadModel.SelectedCatalogRole, Is.EqualTo(LiveCatalogRole.Overlay));
			Assert.That(host.ReadModel.SelectedCatalogItemId, Is.EqualTo(rememberedOverlayPatch.Id));
			Assert.That(ui.Q<Button>("overlay-tab").ClassListContains("is-selected"), Is.True);
			host.MoveCatalogSelection(1, 0);
			yield return null;
			Assert.That(host.ReadModel.SelectedCatalogRole, Is.EqualTo(LiveCatalogRole.Effect));
			Assert.That(host.ReadModel.SelectedCatalogItemId, Is.EqualTo(host.ReadModel.EffectNodes[0].TypeId));
			Assert.That(host.LaunchSelectedCatalogPatch().Accepted, Is.False);
			Assert.That(ui.Q<Button>("effect-tab").ClassListContains("is-selected"), Is.True);
			var mainUi = ui.Q<VisualElement>("main-ui");
			var layoutBeforeEditMode = mainUi.layout;
			host.ToggleEditMode();
			yield return null;
			Assert.That(host.ReadModel.IsEffectCategorySelected, Is.True);
			Assert.That(host.AssignSelectedEffectToCue(0), Is.False);
			host.MoveCatalogSelection(0, 1);
			yield return null;
			Assert.That(host.ReadModel.IsEffectCategorySelected, Is.False);
			Assert.That(host.AssignSelectedEffectToCue(0), Is.True);
			yield return null;
			Assert.That(host.ReadModel.IsEditMode, Is.True);
			Assert.That(host.ReadModel.InstantEffectTypeIds[0], Is.EqualTo(host.ReadModel.SelectedCatalogItemId));
			Assert.That(mainUi.ClassListContains("is-edit-mode"), Is.True);
			Assert.That(mainUi.layout.width, Is.EqualTo(layoutBeforeEditMode.width).Within(0.01f));
			Assert.That(mainUi.layout.height, Is.EqualTo(layoutBeforeEditMode.height).Within(0.01f));
			Assert.That(ui.Q<VisualElement>("edit-mode-highlight").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
			Assert.That(ui.Q<VisualElement>("sequencer-controls").enabledSelf, Is.False);
			Assert.That(ui.Q<Button>("instant-effect-cue-1").ClassListContains("is-assigned"), Is.True);
			Assert.That(ui.Q<Button>("instant-effect-cue-1").text,
				Is.EqualTo(host.ReadModel.EffectNodes.First(effect => effect.TypeId == host.ReadModel.InstantEffectTypeIds[0]).Name));
			Assert.That(ui.Q<Button>("main-tab").enabledSelf, Is.False);
			var effectCategories = host.ReadModel.EffectNodes.Select(effect => effect.Category).Distinct().ToArray();
			if (effectCategories.Length > 1) {
				var openCategory = host.ReadModel.OpenEffectCategory;
				var openCategoryEffectCount = host.ReadModel.EffectNodes.Count(effect => effect.Category == openCategory);
				for (var index = 0; index < openCategoryEffectCount; index++) host.MoveCatalogSelection(0, 1);
				yield return null;
				Assert.That(host.ReadModel.IsEffectCategorySelected, Is.True);
				Assert.That(host.ReadModel.SelectedEffectCategory, Is.Not.EqualTo(openCategory));
				host.MoveCatalogSelection(0, -1);
				yield return null;
				Assert.That(host.ReadModel.IsEffectCategorySelected, Is.False);
			}
			var anotherCategory = effectCategories.FirstOrDefault(category => category != host.ReadModel.OpenEffectCategory);
			if (anotherCategory != null) {
				host.ToggleEffectCategory(anotherCategory);
				yield return null;
				Assert.That(host.ReadModel.OpenEffectCategory, Is.EqualTo(anotherCategory));
				Assert.That(host.ReadModel.IsEffectCategorySelected, Is.True);
				Assert.That(host.ReadModel.SelectedEffectCategory, Is.EqualTo(anotherCategory));
				Assert.That(effectNodeControls.Query<VisualElement>(className: "effect-category-items").ToList().Count(items => !items.ClassListContains("is-hidden")), Is.EqualTo(1));
			}
			host.ToggleSelectedEffectCategory();
			yield return null;
			Assert.That(host.ReadModel.OpenEffectCategory, Is.Empty);
			Assert.That(effectNodeControls.Query<VisualElement>(className: "effect-category-items").ToList().All(items => items.ClassListContains("is-hidden")), Is.True);
			host.ToggleSelectedEffectCategory();
			yield return null;
			Assert.That(host.ReadModel.OpenEffectCategory, Is.Not.Empty);
			host.ToggleEditMode();
			yield return null;
			Assert.That(mainUi.ClassListContains("is-edit-mode"), Is.False);
			host.MoveCatalogSelection(-1, 0);
			yield return null;
			host.MoveCatalogSelection(0, 1);
			yield return null;
			Assert.That(host.ReadModel.SelectedCatalogRole, Is.EqualTo(LiveCatalogRole.Overlay));
			Assert.That(host.ReadModel.SelectedCatalogItemId, Is.EqualTo(nextOverlayPatch.Id));
			Assert.That(host.AssignSelectedOverlayPatchToLane(7).Accepted, Is.True);
			yield return null;
			Assert.That(host.ReadModel.Sequencers.Single(sequencer => sequencer.Kind == LiveSequencerKind.Overlay).LanePatchIds[7], Is.EqualTo(nextOverlayPatch.Id));

			Assert.That(host.ParameterQueue.EnqueueLaunchPatch(rememberedMainPatch.Id).Accepted, Is.True);
			for (var frame = 0; frame < 60 && host.ReadModel.LoadedPatchId != rememberedMainPatch.Id; frame++) yield return null;
			Assert.That(host.ReadModel.LoadedPatchId, Is.EqualTo(rememberedMainPatch.Id));
			var mainOnlyProgramTexture = host.ReadModel.ProgramTexture;
			var overlayOutputTexture = host.ReadModel.ProgramFrames[1].Texture;
			var overlaySequencer = host.ReadModel.Sequencers.Single(sequencer => sequencer.Kind == LiveSequencerKind.Overlay);
			var triggerStep = (overlaySequencer.CurrentStep + 1) % LiveStepSequencer.StepCount;
			Assert.That(host.CycleSequencerCellMode(LiveSequencerKind.Overlay, 0, triggerStep).Accepted, Is.True);
			using (var altClick = ClickEvent.GetPooled(new Event {
				type = EventType.MouseUp,
				button = 0,
				modifiers = EventModifiers.Alt
			})) {
				ui.Q<Button>("sequencer-overlay-lane-0-step-" + triggerStep).SendEvent(altClick);
			}
			yield return null;
			Assert.That(host.ReadModel.Sequencers.Single(sequencer => sequencer.Kind == LiveSequencerKind.Overlay).IsCopiedToOutput2(0), Is.True);
			for (var frame = 0; frame < 120 && host.ReadModel.Sequencers.Single(sequencer => sequencer.Kind == LiveSequencerKind.Overlay).CurrentStep != triggerStep; frame++) yield return null;
			Assert.That(host.ReadModel.LoadedPatchId, Is.EqualTo(rememberedMainPatch.Id));
			Assert.That(host.ReadModel.ProgramTexture, Is.Not.Null);
			Assert.That(host.ReadModel.ProgramTexture, Is.SameAs(mainOnlyProgramTexture));
			Assert.That(HasVisiblePixels(host.ReadModel.ProgramTexture), Is.True);
			Assert.That(host.ReadModel.ProgramFrames[1].Texture, Is.SameAs(overlayOutputTexture));
			Assert.That(HasVisiblePixels(overlayOutputTexture), Is.True);
			for (var frame = 0; frame < 120 && host.ReadModel.Sequencers.Single(sequencer => sequencer.Kind == LiveSequencerKind.Overlay).CurrentStep == triggerStep; frame++) yield return null;
			Assert.That(host.ReadModel.ProgramTexture, Is.SameAs(mainOnlyProgramTexture));
			Assert.That(HasVisiblePixels(overlayOutputTexture), Is.False);

			var midi = (Component)typeof(ApplicationLiveHost).GetField("_midiInputManager", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
			host.Shutdown();
			Assert.That(host.State, Is.EqualTo(ApplicationLiveHostState.Offline));
			Assert.That((bool)midi.GetType().GetProperty("IsOpen")?.GetValue(midi), Is.False);
		}

		private static bool HasVisiblePixels(RenderTexture source) {
			var sample = RenderTexture.GetTemporary(64, 36, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
			var texture = new Texture2D(64, 36, TextureFormat.RGB24, false, true);
			var previous = RenderTexture.active;
			try {
				Graphics.Blit(source, sample);
				RenderTexture.active = sample;
				texture.ReadPixels(new Rect(0f, 0f, sample.width, sample.height), 0, 0);
				texture.Apply();
				return texture.GetPixels().Any(color => color.maxColorComponent > 0.01f);
			}
			finally {
				RenderTexture.active = previous;
				Object.Destroy(texture);
				RenderTexture.ReleaseTemporary(sample);
			}
		}
	}
}
