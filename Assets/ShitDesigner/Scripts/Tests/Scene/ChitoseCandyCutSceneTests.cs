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
		public void ChangingCandyShapeUpdatesGeneratedCandyInPlace() {
			var root = new GameObject("Chitose Candy Test");
			try {
				var cutScene = root.AddComponent<ChitoseCandyCutScene>();
				var generatedRoot = root.transform.Find("Generated Chitose Candy");
				var candyRoot = generatedRoot.GetChild(0);
				var originalDirection = candyRoot.localRotation * Vector3.up;
				var originalFrontPosition = candyRoot.localPosition + originalDirection * (cutScene.CandyLength * 0.5f);
				var candyLength = typeof(ChitoseCandyCutScene).GetField(
					"m_CandyLength", BindingFlags.Instance | BindingFlags.NonPublic);
				var candyRadius = typeof(ChitoseCandyCutScene).GetField(
					"m_CandyRadius", BindingFlags.Instance | BindingFlags.NonPublic);
				var candyDirection = typeof(ChitoseCandyCutScene).GetField(
					"m_CandyAxis", BindingFlags.Instance | BindingFlags.NonPublic);
				Assert.That(candyLength, Is.Not.Null);
				Assert.That(candyRadius, Is.Not.Null);
				Assert.That(candyDirection, Is.Not.Null);

				candyLength.SetValue(cutScene, 20f);
				candyRadius.SetValue(cutScene, 1f);
				candyDirection.SetValue(cutScene, Vector3.right);
				cutScene.SendMessage("OnValidate");
				cutScene.SendMessage("Update");

				Assert.That(root.transform.GetChild(root.transform.childCount - 1), Is.SameAs(generatedRoot));
				Assert.That(generatedRoot.GetChild(0), Is.SameAs(candyRoot));
				var updatedDirection = candyRoot.localRotation * Vector3.up;
				var updatedFrontPosition = candyRoot.localPosition + updatedDirection * 10f;
				Assert.That(Vector3.Distance(updatedFrontPosition, originalFrontPosition), Is.LessThan(0.0001f));
				Assert.That(Mathf.Abs(updatedDirection.z), Is.LessThan(0.0001f));

				var fragment = candyRoot.Find("Candy Entry/Cut Fragment 01");
				Assert.That(fragment, Is.Not.Null);
				Assert.That(fragment.localPosition.y, Is.EqualTo(9f).Within(0.0001f));
				var bodyVisual = fragment.Find("Fragment Candy Body");
				Assert.That(bodyVisual.localScale.x, Is.EqualTo(1f).Within(0.0001f));
				Assert.That(bodyVisual.localScale.y, Is.EqualTo(2f).Within(0.0001f));
				Assert.That(bodyVisual.localScale.z, Is.EqualTo(1f).Within(0.0001f));
				var rearCutFace = fragment.Find("Fragment Rear Cut Face");
				Assert.That(rearCutFace.localPosition.y, Is.EqualTo(-1.018f).Within(0.0001f));
				Assert.That(rearCutFace.localScale.x, Is.EqualTo(0.76f).Within(0.0001f));
				var collider = fragment.GetComponent<CapsuleCollider>();
				Assert.That(collider.radius, Is.EqualTo(0.94f).Within(0.0001f));
				Assert.That(collider.height, Is.EqualTo(2f).Within(0.0001f));
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void ChangingCandyShapePreservesCutPhysicsState() {
			var root = new GameObject("Chitose Candy Test");
			try {
				var cutScene = root.AddComponent<ChitoseCandyCutScene>();
				cutScene.SetBpmClock(new BeatClockFrame(120f, 0d));
				cutScene.SetBpmClock(new BeatClockFrame(120f, 3d));
				var generatedRoot = root.transform.Find("Generated Chitose Candy");
				Rigidbody dynamicBody = null;
				foreach (var body in generatedRoot.GetComponentsInChildren<Rigidbody>(true)) {
					if (body.isKinematic) continue;
					dynamicBody = body;
					break;
				}
				Assert.That(dynamicBody, Is.Not.Null);

				cutScene.SetCandyLength(20f);
				cutScene.SetCandyRadius(1f);
				cutScene.SetCandyDirection(Vector3.right);

				Assert.That(root.transform.GetChild(root.transform.childCount - 1), Is.SameAs(generatedRoot));
				Assert.That(generatedRoot.GetComponentsInChildren<Rigidbody>(true), Does.Contain(dynamicBody));
				Assert.That(dynamicBody.isKinematic, Is.False);
				Assert.That(dynamicBody.useGravity, Is.True);
			}
			finally {
				Object.DestroyImmediate(root);
			}
		}

		[Test]
		public void ChangingCandyCountRebuildsGeneratedCandy() {
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
					ChitoseCandyCutScene.CandyLengthParameterId,
					ChitoseCandyCutScene.CandyRadiusParameterId,
					ChitoseCandyCutScene.CandyDirectionXParameterId,
					ChitoseCandyCutScene.CandyDirectionYParameterId,
					ChitoseCandyCutScene.CandyDirectionZParameterId,
					ChitoseCandyCutScene.SplitGapParameterId,
					ChitoseCandyCutScene.HorizontalImpulseParameterId
				}));
				Assert.That(liveRoot.TrySetParameter(
					ChitoseCandyCutScene.SplitGapParameterId, 1.25f, out var splitGapRejection), Is.True);
				Assert.That(splitGapRejection, Is.Empty);
				Assert.That(liveRoot.TrySetParameter(
					ChitoseCandyCutScene.HorizontalImpulseParameterId, 4.5f, out var impulseRejection), Is.True);
				Assert.That(impulseRejection, Is.Empty);
				Assert.That(liveRoot.TrySetParameter(
					ChitoseCandyCutScene.CandyLengthParameterId, 20f, out var lengthRejection), Is.True);
				Assert.That(lengthRejection, Is.Empty);
				Assert.That(liveRoot.TrySetParameter(
					ChitoseCandyCutScene.CandyRadiusParameterId, 1f, out var radiusRejection), Is.True);
				Assert.That(radiusRejection, Is.Empty);
				Assert.That(liveRoot.TrySetParameter(
					ChitoseCandyCutScene.CandyDirectionXParameterId, 1f, out var directionXRejection), Is.True);
				Assert.That(directionXRejection, Is.Empty);
				Assert.That(liveRoot.TrySetParameter(
					ChitoseCandyCutScene.CandyDirectionYParameterId, 0f, out var directionYRejection), Is.True);
				Assert.That(directionYRejection, Is.Empty);
				Assert.That(liveRoot.TrySetParameter(
					ChitoseCandyCutScene.CandyDirectionZParameterId, 0f, out var directionZRejection), Is.True);
				Assert.That(directionZRejection, Is.Empty);
				Assert.That(cutScene.CandyLength, Is.EqualTo(20f));
				Assert.That(cutScene.CandyRadius, Is.EqualTo(1f));
				Assert.That(cutScene.CandyDirection, Is.EqualTo(Vector3.right));
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
