using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Scene;

namespace ShitDesigner.Tests.Scene {
	public sealed class SceneIsolationContractTests {
		private static NodeInstanceId Node(int index) => new NodeInstanceId($"{index + 1:00000000}-0000-4000-8000-000000000000");

		[Test]
		public void LayerPool_AllocatesReservedLayersAndReleasesAfterLeaseDispose() {
			var pool = new SceneLayerPool();
			var first = pool.Acquire(Node(0));
			var second = pool.Acquire(Node(1));
			Assert.That(first.IsSuccess, Is.True);
			Assert.That(second.IsSuccess, Is.True);
			Assert.That(first.Value.Layer, Is.EqualTo(8));
			Assert.That(second.Value.Layer, Is.EqualTo(9));
			first.Value.Dispose();
			var reused = pool.Acquire(Node(2));
			Assert.That(reused.Value.Layer, Is.EqualTo(8));
		}

		[Test]
		public void LayerPool_RejectsTwentyFifthNode() {
			var pool = new SceneLayerPool();
			for (var i = 0; i < SceneLayerPool.Capacity; i++) Assert.That(pool.Acquire(Node(i)).IsSuccess, Is.True);
			Assert.That(pool.Acquire(Node(SceneLayerPool.Capacity)).IsFailure, Is.True);
		}

		[Test]
		public void LayerPool_DoesNotReleaseDifferentGeneration() {
			var pool = new SceneLayerPool();
			var oldGeneration = pool.Acquire(Node(0), 7);
			Assert.That(oldGeneration.IsSuccess, Is.True);
			Assert.That(pool.Release(Node(0), 8).IsFailure, Is.True);
			Assert.That(pool.ActiveCount, Is.EqualTo(1));
			oldGeneration.Value.Dispose();
			Assert.That(pool.ActiveCount, Is.EqualTo(0));
		}

		[Test]
		public void LayerPool_AllowsSameNodeIdAcrossOverlappingGenerations() {
			var pool = new SceneLayerPool();
			var oldGeneration = pool.Acquire(Node(0), 7);
			var newGeneration = pool.Acquire(Node(0), 8);
			Assert.That(oldGeneration.IsSuccess && newGeneration.IsSuccess, Is.True);
			Assert.That(oldGeneration.Value.Layer, Is.Not.EqualTo(newGeneration.Value.Layer));

			oldGeneration.Value.Dispose();
			Assert.That(pool.TryGet(Node(0), 8, out var current), Is.True);
			Assert.That(current, Is.SameAs(newGeneration.Value));
			Assert.That(pool.ActiveCount, Is.EqualTo(1));
			newGeneration.Value.Dispose();
			Assert.That(pool.ActiveCount, Is.EqualTo(0));
		}
	}
}
