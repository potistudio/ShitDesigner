using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Scene;
using UnityEngine;

namespace ShitDesigner.Tests.Scene {
	public sealed class ChitoseCandyCutSceneTests {
		[Test]
		public void RebuildAssignsOwnerLayerToGeneratedHierarchy() {
			var root = new GameObject("Chitose Candy Test") { layer = 12 };
			try {
				var cutScene = root.AddComponent<ChitoseCandyCutScene>();
				cutScene.Rebuild();

				foreach (var item in root.GetComponentsInChildren<Transform>(true))
					Assert.That(item.gameObject.layer, Is.EqualTo(root.layer), item.name);
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void BpmClockProcessesEveryCrossedBeat() {
			var root = new GameObject("Chitose Candy Test");
			try {
				var cutScene = root.AddComponent<ChitoseCandyCutScene>();
				cutScene.SetBpmClock(new BeatClockFrame(120f, 0d));

				cutScene.SetBpmClock(new BeatClockFrame(120f, 3d));

				var dynamicBodies = 0;
				foreach (var body in root.GetComponentsInChildren<Rigidbody>(true))
					if (!body.isKinematic)
						dynamicBodies++;
				Assert.That(dynamicBodies, Is.GreaterThanOrEqualTo(24));
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}
	}
}
