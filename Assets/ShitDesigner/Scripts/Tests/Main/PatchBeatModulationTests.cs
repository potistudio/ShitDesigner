using NUnit.Framework;
using ShitDesigner.Core;
using ShitDesigner.Scene;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class PatchBeatModulationTests {
		[Test]
		public void ResolveAddsBeatPulseAndClampsWithoutChangingTheBaseValue() {
			var modulation = new PatchBeatModulation(true, .8f);
			var beatStart = new BeatClockFrame(120f, 0d);
			var beatTail = new BeatClockFrame(120f, .25d);

			Assert.That(modulation.Resolve(.5f, beatStart), Is.EqualTo(1f));
			Assert.That(modulation.Resolve(.5f, beatTail), Is.EqualTo(.5f));
		}

		[Test]
		public void DisabledModulationLeavesTheBaseValueUntouched() {
			var modulation = new PatchBeatModulation();

			Assert.That(modulation.Resolve(.5f, new BeatClockFrame(120f, 0d)), Is.EqualTo(.5f));
		}
	}
}
