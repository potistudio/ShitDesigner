using System.Collections;
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
		public IEnumerator MainBootsRendersAndSwitchesItsAuthoredLiveGraph() {
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
			Assert.That(host.ReadModel.ProgramTexture.width, Is.EqualTo(1920));
			Assert.That(host.ReadModel.ProgramTexture.height, Is.EqualTo(1080));
			Assert.That(host.ReadModel.ProgramTexture.format, Is.EqualTo(RenderTextureFormat.ARGBHalf));
			Assert.That(host.ReadModel.ProgramFrameNumber, Is.GreaterThan(0));
			Assert.That(host.ReadModel.Sequencers, Has.Count.EqualTo(2));
			Assert.That(host.ReadModel.Sequencers.All(sequencer => sequencer.ActiveLaneMasks.Count == LiveStepSequencer.StepCount), Is.True);
			var runtime = (LiveGraphRuntime)typeof(ApplicationLiveHost).GetField("_runtime", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(host);
			Assert.That(runtime.CurrentFrames.Count, Is.EqualTo(1));
			Assert.That(runtime.CurrentFrames[0].Texture, Is.SameAs(host.ReadModel.ProgramTexture));
			Assert.That(HasVisiblePixels(host.ReadModel.ProgramTexture), Is.True);

			var loadedPatchId = host.ReadModel.LoadedPatchId;
			var nextPatch = host.ReadModel.Patches.First(patch => patch.Id != loadedPatchId);
			var preload = host.ParameterQueue.EnqueuePreloadPatch(nextPatch.Id);
			Assert.That(preload.Accepted, Is.True);
			for (var frame = 0; frame < 60 && runtime.PreloadedPatchId != nextPatch.Id; frame++) yield return null;

			Assert.That(host.ReadModel.LoadedPatchId, Is.EqualTo(loadedPatchId));
			Assert.That(runtime.PreloadedPatchId, Is.EqualTo(nextPatch.Id));
			Assert.That(host.ReadModel.RequestResults.Any(result => result.SequenceNumber == preload.SequenceNumber && result.Applied), Is.True);
			var load = host.ParameterQueue.EnqueueLoadPatch(nextPatch.Id);
			Assert.That(load.Accepted, Is.True);
			for (var frame = 0; frame < 60 && host.ReadModel.LoadedPatchId != nextPatch.Id; frame++) yield return null;

			Assert.That(host.ReadModel.LoadedPatchId, Is.EqualTo(nextPatch.Id));
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
				Is.EqualTo(new[] { "Q", "W", "E", "R", "T", "Y", "U", "I", "O", "P" }));
			var patchControls = ui.Q<VisualElement>("patch-controls");
			var mainPatchControls = ui.Q<ScrollView>("main-patch-controls");
			var overlayPatchControls = ui.Q<ScrollView>("overlay-patch-controls");
			var effectNodeControls = ui.Q<ScrollView>("effect-node-controls");
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
			Assert.That(effectNodeControls.Query<Button>().ToList().Count, Is.EqualTo(host.ReadModel.EffectNodes.Count));
			Assert.That(host.ReadModel.EffectNodes, Is.Not.Empty);
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
			Assert.That(host.AssignMainPatchToCue(0, mainPatches[0].Id), Is.True);
			Assert.That(host.AssignMainPatchToCue(1, mainPatches[1].Id), Is.True);
			Assert.That(host.AssignMainPatchToCue(0, host.ReadModel.Patches.First(patch => patch.Role == LivePatchRole.Overlay).Id), Is.False);
			yield return null;
			Assert.That(host.MainCuePatchIds, Is.EqualTo(mainPatches.Select(patch => patch.Id).Take(ApplicationLiveHost.MainCueCount)));
			Assert.That(cueSlots.Select(slot => slot.Q<Label>().text),
				Is.EqualTo(mainPatches.Select(patch => patch.Name).Take(ApplicationLiveHost.MainCueCount)));
			Assert.That(cueSlots.All(slot => slot.ClassListContains("is-assigned")), Is.True);
			Assert.That(firstMainButton.worldBound.yMin, Is.EqualTo(mainPatchControls.contentViewport.worldBound.yMin).Within(0.5f));
			var initialMainListTop = firstMainButton.worldBound.yMin;
			host.MoveCatalogSelection(0, 1);
			yield return null;
			Assert.That(firstMainButton.worldBound.yMin, Is.EqualTo(initialMainListTop).Within(0.5f));
			host.MoveCatalogSelection(0, -1);
			yield return null;
			var sequencerControls = ui.Q<VisualElement>("sequencer-controls");
			Assert.That(sequencerControls.Query<Button>(className: "sequencer-step").ToList(),
				Has.Count.EqualTo((LiveStepSequencer.OverlayLaneCount + LiveStepSequencer.EffectLaneCount) * LiveStepSequencer.StepCount));
			Assert.That(ui.Q<Button>("sequencer-overlay-lane-label-7"), Is.Not.Null);
			Assert.That(ui.Q<VisualElement>("patch-slot-controls"), Is.Null);
			var effectCell = ui.Q<Button>("sequencer-effect-lane-2-step-4");
			using (var click = ClickEvent.GetPooled()) effectCell.SendEvent(click);
			Assert.That(host.CycleSequencerCellMode(LiveSequencerKind.Effect, 1, 4).Accepted, Is.True);
			yield return null;
			Assert.That(effectCell.ClassListContains("is-set"), Is.True);
			Assert.That(ui.Q<Button>("sequencer-effect-lane-1-step-4").ClassListContains("is-set"), Is.True);
			Assert.That(effectCell.text, Is.EqualTo("NORMAL"));
			Assert.That(ui.Query<Button>(className: "is-loaded").ToList(), Is.Empty);
			var rememberedMainPatch = host.ReadModel.Patches.Last(patch => patch.Role == LivePatchRole.Main);
			var rememberedOverlayPatch = host.ReadModel.Patches.First(patch => patch.Role == LivePatchRole.Overlay);
			var nextOverlayPatch = host.ReadModel.Patches.Where(patch => patch.Role == LivePatchRole.Overlay).Skip(1).First();
			Assert.That(host.SelectSequencerLane(LiveSequencerKind.Overlay, 0).Accepted, Is.True);
			yield return null;
			Assert.That(ui.Q<Button>("sequencer-overlay-lane-label-0").ClassListContains("is-selecting"), Is.True);
			Assert.That(host.AssignSelectedSequencerPatch(rememberedOverlayPatch.Id).Accepted, Is.True);
			yield return null;
			Assert.That(ui.Q<Button>("sequencer-overlay-lane-label-0").ClassListContains("is-assigned"), Is.True);
			Assert.That(host.ReadModel.OverlayLanePreviews, Has.Count.EqualTo(LiveStepSequencer.OverlayLaneCount));
			Assert.That(host.ReadModel.OverlayLanePreviews[0], Is.Not.Null);
			Assert.That(ui.Q<Button>("sequencer-overlay-lane-label-0").ClassListContains("has-preview"), Is.True);
			Assert.That(host.SelectSequencerLane(LiveSequencerKind.Overlay, 1).Accepted, Is.True);
			Assert.That(host.AssignSelectedSequencerPatch(rememberedOverlayPatch.Id).Accepted, Is.True);
			yield return null;
			Assert.That(host.ReadModel.OverlayLanePreviews[1], Is.SameAs(host.ReadModel.OverlayLanePreviews[0]));
			host.MoveCatalogSelection(-1, 0);
			yield return null;
			Assert.That(host.ReadModel.SelectedCatalogRole, Is.EqualTo(LiveCatalogRole.Main));
			Assert.That(host.ReadModel.SelectedCatalogItemId, Is.EqualTo(rememberedMainPatch.Id));
			Assert.That(host.AssignSelectedOverlayPatchToLane(7).Accepted, Is.False);
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
			var overlaySequencer = host.ReadModel.Sequencers.Single(sequencer => sequencer.Kind == LiveSequencerKind.Overlay);
			var triggerStep = (overlaySequencer.CurrentStep + 1) % LiveStepSequencer.StepCount;
			Assert.That(host.CycleSequencerCellMode(LiveSequencerKind.Overlay, 0, triggerStep).Accepted, Is.True);
			for (var frame = 0; frame < 120 && host.ReadModel.Sequencers.Single(sequencer => sequencer.Kind == LiveSequencerKind.Overlay).CurrentStep != triggerStep; frame++) yield return null;
			Assert.That(host.ReadModel.LoadedPatchId, Is.EqualTo(rememberedMainPatch.Id));
			Assert.That(host.ReadModel.ProgramTexture, Is.Not.Null);
			Assert.That(host.ReadModel.ProgramTexture, Is.Not.SameAs(mainOnlyProgramTexture));
			Assert.That(HasVisiblePixels(host.ReadModel.ProgramTexture), Is.True);
			for (var frame = 0; frame < 120 && host.ReadModel.Sequencers.Single(sequencer => sequencer.Kind == LiveSequencerKind.Overlay).CurrentStep == triggerStep; frame++) yield return null;
			Assert.That(host.ReadModel.ProgramTexture, Is.SameAs(mainOnlyProgramTexture));

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
