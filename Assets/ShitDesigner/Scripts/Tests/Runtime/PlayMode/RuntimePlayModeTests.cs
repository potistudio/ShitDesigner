using System.Collections;
using NUnit.Framework;
using UnityEngine.TestTools;
using ShitDesigner.Runtime;

namespace ShitDesigner.Tests.Runtime {
	public sealed class RuntimePlayModeTests {
		[UnityTest]
		public IEnumerator GraphClockAndCoordinatorRemainTickableAcrossPlayerFrames() {
			// The edit-mode suite covers graph construction and node contracts.
			// This PlayMode smoke test keeps the Player-frame boundary executable
			// without creating Unity objects or touching rendering ownership.
			var clock = new GraphClock(new ManualSource(0d));
			clock.Update(0d);
			clock.Update(1d / 60d);
			Assert.That(clock.Time, Is.EqualTo(1d / 60d).Within(0.0001));
			yield return null;
		}

		private sealed class ManualSource : IMonotonicClock {
			public double Now { get; private set; }
			public ManualSource(double now) { Now = now; }
		}
	}
}
