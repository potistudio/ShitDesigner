using System.Reflection;
using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Main;
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

		[Test]
		public void RewindingBpmClockDoesNotRebuildGeneratedCandy() {
			var root = new GameObject("Chitose Candy Test");
			try {
				var cutScene = root.AddComponent<ChitoseCandyCutScene>();
				cutScene.SetBpmClock(new BeatClockFrame(120f, 0d));
				cutScene.SetBpmClock(new BeatClockFrame(120f, 3d));
				var generatedRoot = root.transform.Find("Generated Chitose Candy");
				Assert.That(generatedRoot, Is.Not.Null);

				cutScene.SetBpmClock(new BeatClockFrame(120f, 1d));

				Assert.That(root.transform.GetChild(root.transform.childCount - 1), Is.SameAs(generatedRoot));
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void ChangingCutParametersDoesNotRebuildGeneratedCandy() {
			var root = new GameObject("Chitose Candy Test");
			try {
				var cutScene = root.AddComponent<ChitoseCandyCutScene>();
				var generatedRoot = root.transform.Find("Generated Chitose Candy");
				Assert.That(generatedRoot, Is.Not.Null);

				cutScene.SetSplitGap(1.25f);
				cutScene.SetHorizontalImpulse(4.5f);
				cutScene.SendMessage("OnValidate");
				cutScene.SendMessage("Update");

				Assert.That(root.transform.GetChild(root.transform.childCount - 1), Is.SameAs(generatedRoot));
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void ChangingCandyFieldParametersRebuildsGeneratedCandy() {
			var root = new GameObject("Chitose Candy Test");
			try {
				var cutScene = root.AddComponent<ChitoseCandyCutScene>();
				var generatedRoot = root.transform.Find("Generated Chitose Candy");
				var candyCount = typeof(ChitoseCandyCutScene).GetField(
					"m_CandyCount", BindingFlags.Instance | BindingFlags.NonPublic);
				Assert.That(generatedRoot, Is.Not.Null);
				Assert.That(candyCount, Is.Not.Null);

				candyCount.SetValue(cutScene, 13);
				cutScene.SendMessage("OnValidate");
				cutScene.SendMessage("Update");

				Assert.That(root.transform.GetChild(root.transform.childCount - 1), Is.Not.SameAs(generatedRoot));
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void LiveParametersUpdateCutWithoutRebuildingGeneratedCandy() {
			var root = new GameObject("Chitose Candy Test");
			try {
				var cutScene = root.AddComponent<ChitoseCandyCutScene>();
				var liveRoot = root.AddComponent<LiveSceneRoot>();
				liveRoot.Initialize("chitose-candy-test");
				var generatedRoot = root.transform.Find("Generated Chitose Candy");

				Assert.That(liveRoot.PublicParameterIds, Is.EquivalentTo(new[] {
					ChitoseCandyCutScene.SplitGapParameterId,
					ChitoseCandyCutScene.HorizontalImpulseParameterId
				}));
				Assert.That(liveRoot.TrySetParameter(
					ChitoseCandyCutScene.SplitGapParameterId, 1.25f, out var splitGapRejection), Is.True);
				Assert.That(splitGapRejection, Is.Empty);
				Assert.That(liveRoot.TrySetParameter(
					ChitoseCandyCutScene.HorizontalImpulseParameterId, 4.5f, out var impulseRejection), Is.True);
				Assert.That(impulseRejection, Is.Empty);
				Assert.That(cutScene.SplitGap, Is.EqualTo(1.25f));
				Assert.That(cutScene.HorizontalImpulse, Is.EqualTo(4.5f));
				Assert.That(root.transform.GetChild(root.transform.childCount - 1), Is.SameAs(generatedRoot));
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void AddedCandyKeepsItsFrontInsideTheSpawnField() {
			var root = new GameObject("Chitose Candy Test");
			try {
				var cutScene = root.AddComponent<ChitoseCandyCutScene>();
				cutScene.SetBpmClock(new BeatClockFrame(120f, 0d));

				cutScene.SetBpmClock(new BeatClockFrame(120f, 21d));

				var generatedRoot = root.transform.Find("Generated Chitose Candy");
				Assert.That(generatedRoot, Is.Not.Null);
				Assert.That(generatedRoot.childCount, Is.GreaterThan(12));
				for (var index = 12; index < generatedRoot.childCount; index++) {
					var candyRoot = generatedRoot.GetChild(index);
					var axis = candyRoot.localRotation * Vector3.up;
					var frontPosition = candyRoot.localPosition + axis * 7f;
					Assert.That(Mathf.Abs(frontPosition.x), Is.LessThanOrEqualTo(4.751f));
					Assert.That(Mathf.Abs(frontPosition.y), Is.LessThanOrEqualTo(2.751f));
					Assert.That(Mathf.Abs(frontPosition.z), Is.LessThanOrEqualTo(0.181f));
				}
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}
	}
}
