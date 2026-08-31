using System.Collections;
using NUnit.Framework;
using ShitDesigner.AssetFlush;
using ShitDesigner.Main;
using ShitDesigner.Media;
using UnityEngine;
using UnityEngine.TestTools;

namespace ShitDesigner.Tests.Media {
	public sealed class AssetFlashTests {
		[Test]
		public void TriggerState_UsesRisingEdgesRetriggersAndExpires() {
			var state = new AssetFlashTriggerState();
			var triggers = new bool[AssetFlashContract.SlotCount];

			Assert.That(state.Sample(triggers, 0d, .25d), Is.EqualTo(-1));
			triggers[2] = true;
			Assert.That(state.Sample(triggers, .1d, .25d), Is.EqualTo(2));
			Assert.That(state.LastFiredSlot, Is.EqualTo(2));
			Assert.That(state.VisibleUntil, Is.EqualTo(.35d).Within(.000001d));
			Assert.That(state.Sample(triggers, .2d, .25d), Is.EqualTo(2), "A held signal must not retrigger.");
			triggers[2] = false;
			state.Sample(triggers, .3d, .25d);
			triggers[2] = true;
			Assert.That(state.Sample(triggers, .34d, .25d), Is.EqualTo(2));
			Assert.That(state.VisibleUntil, Is.EqualTo(.59d).Within(.000001d));
			Assert.That(state.Sample(triggers, .59d, .25d), Is.EqualTo(-1));
		}

		[Test]
		public void TriggerState_LaterSlotWinsSimultaneousEdges() {
			var state = new AssetFlashTriggerState();
			var triggers = new bool[AssetFlashContract.SlotCount];
			triggers[0] = true;
			triggers[7] = true;

			Assert.That(state.Sample(triggers, 1d, .25d), Is.EqualTo(7));
			Assert.That(state.LastFiredSlot, Is.EqualTo(7));
		}

		[Test]
		public void Scene_CanRandomlyTriggerWithoutGraphRuntime() {
			var host = new GameObject("AssetFlushSceneTest");
			var image = new Texture2D(2, 2);
			try {
				var scene = host.AddComponent<AssetFlushScene>();
				scene.SetImages(image);

				Assert.That(scene.TryTriggerRandom(), Is.True);
				Assert.That(scene.OutputTexture, Is.SameAs(image));
				scene.Clear();
				Assert.That(scene.OutputTexture, Is.Null);
			}
			finally {
				Object.DestroyImmediate(image);
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void Scene_RandomTriggerSupportsVariableAssetCounts() {
			var host = new GameObject("AssetFlashRandomTest");
			var image = new Texture2D(2, 2);
			try {
				var scene = host.AddComponent<AssetFlushScene>();
				var images = new Texture2D[13];
				for (var index = 0; index < 12; index++) images[index] = image;
				scene.SetImages(images);

				Assert.That(scene.AvailableAssetCount, Is.EqualTo(12));
				Assert.That(scene.TryTriggerRandom(), Is.True);
				Assert.That(scene.OutputTexture, Is.SameAs(image));

				scene.SetImages(null, null);
				Assert.That(scene.AvailableAssetCount, Is.Zero);
				Assert.That(scene.TryTriggerRandom(), Is.False);
				Assert.That(scene.OutputTexture, Is.Null);
			}
			finally {
				Object.DestroyImmediate(image);
				Object.DestroyImmediate(host);
			}
		}

		[Test]
		public void Scene_LiveParameterTriggersAssetWithMatchingId() {
			var host = new GameObject("AssetFlushLiveParameterTest");
			var first = new Texture2D(2, 2);
			var second = new Texture2D(2, 2);
			try {
				var scene = host.AddComponent<AssetFlushScene>();
				scene.SetImageEntries(
					new AssetFlushImageEntry("first", first),
					new AssetFlushImageEntry("second", second));
				var liveRoot = host.AddComponent<LiveSceneRoot>();
				liveRoot.Initialize("asset-flush-test");

				Assert.That(liveRoot.PublicParameterIds, Is.EquivalentTo(new[] { "first", "second" }));
				Assert.That(liveRoot.IsTriggerParameter("second"), Is.True);
				Assert.That(liveRoot.TrySetParameter("second", 0f, out var inactiveRejection), Is.True);
				Assert.That(inactiveRejection, Is.Empty);
				Assert.That(scene.OutputTexture, Is.Null);
				Assert.That(liveRoot.TrySetParameter("first", 1f, out var firstRejection), Is.True);
				Assert.That(firstRejection, Is.Empty);
				Assert.That(scene.OutputTexture, Is.SameAs(first));
				Assert.That(liveRoot.TrySetParameter("second", 1f, out var triggerRejection), Is.True);
				Assert.That(triggerRejection, Is.Empty);
				Assert.That(scene.OutputTexture, Is.SameAs(second));
				Assert.That(liveRoot.TrySetParameter("second", 0f, out var fallbackRejection), Is.True);
				Assert.That(fallbackRejection, Is.Empty);
				Assert.That(scene.OutputTexture, Is.SameAs(first));
				scene.FadeOutSeconds = 0f;
				Assert.That(liveRoot.TrySetParameter("first", 0f, out var releaseRejection), Is.True);
				Assert.That(releaseRejection, Is.Empty);
				Assert.That(scene.OutputTexture, Is.Null);
			}
			finally {
				Object.DestroyImmediate(first);
				Object.DestroyImmediate(second);
				Object.DestroyImmediate(host);
			}
		}

		[UnityTest]
		public IEnumerator Scene_LiveParameterHoldsUntilReleaseThenFadesOut() {
			var host = new GameObject("AssetFlushHoldTest");
			var image = new Texture2D(2, 2);
			try {
				var scene = host.AddComponent<AssetFlushScene>();
				scene.FadeOutSeconds = 5f;
				scene.SetImageEntries(new AssetFlushImageEntry("held", image));
				var liveRoot = host.AddComponent<LiveSceneRoot>();
				liveRoot.Initialize("asset-flush-hold-test");

				Assert.That(liveRoot.TrySetParameter("held", 1f, out var pressRejection), Is.True);
				Assert.That(pressRejection, Is.Empty);
				yield return null;
				Assert.That(scene.OutputTexture, Is.SameAs(image));
				Assert.That(scene.Opacity, Is.EqualTo(1f));

				Assert.That(liveRoot.TrySetParameter("held", 0f, out var releaseRejection), Is.True);
				Assert.That(releaseRejection, Is.Empty);
				Assert.That(scene.OutputTexture, Is.SameAs(image));
				yield return null;
				Assert.That(scene.OutputTexture, Is.SameAs(image));
				Assert.That(scene.Opacity, Is.GreaterThan(0f).And.LessThan(1f));

				scene.FadeOutSeconds = 0f;
				yield return null;
				Assert.That(scene.OutputTexture, Is.Null);
				Assert.That(scene.Opacity, Is.Zero);
			}
			finally {
				Object.DestroyImmediate(image);
				Object.DestroyImmediate(host);
			}
		}
	}
}
