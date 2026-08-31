using NUnit.Framework;
using ShitDesigner.AssetFlush;
using ShitDesigner.Media;
using UnityEngine;

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
	}
}
