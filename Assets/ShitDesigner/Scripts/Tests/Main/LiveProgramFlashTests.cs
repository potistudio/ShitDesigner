using NUnit.Framework;

namespace ShitDesigner.Main.Tests {
	[TestFixture]
	public sealed class LiveProgramFlashTests {
		[Test]
		public void TriggerStartsAtFullIntensityAndFadesToTransparent() {
			var flash = new LiveProgramFlashState(.12d);

			flash.Trigger(2d);

			Assert.That(flash.Sample(2d), Is.EqualTo(1f));
			Assert.That(flash.Sample(2.06d), Is.EqualTo(.5f).Within(.0001f));
			Assert.That(flash.Sample(2.12d), Is.Zero);
		}

		[Test]
		public void TriggerRestartsTheFlashEnvelope() {
			var flash = new LiveProgramFlashState(.12d);
			flash.Trigger(2d);

			flash.Trigger(2.1d);

			Assert.That(flash.Sample(2.1d), Is.EqualTo(1f));
			Assert.That(flash.Sample(2.22d), Is.Zero);
		}
	}
}
