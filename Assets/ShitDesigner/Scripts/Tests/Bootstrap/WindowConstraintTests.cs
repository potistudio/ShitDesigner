using NUnit.Framework;
using ShitDesigner.Bootstrap;

namespace ShitDesigner.Bootstrap.Tests {
	[TestFixture]
	public sealed class WindowConstraintTests {
		[Test]
		public void MinimumWindowContractClampsBothDimensions() {
			var clamped = WindowConstraints.Clamp(new WindowSize(900, 480));
			Assert.That(clamped, Is.EqualTo(new WindowSize(1280, 720)));
			Assert.That(WindowConstraints.NeedsClamp(clamped), Is.False);
		}

		[Test]
		public void PlatformAdapterIsAppliedAgainAfterUserResize() {
			var adapter = new RecordingWindowAdapter(new WindowSize(1600, 900));
			var lifecycle = new WindowLifecycle(adapter);
			Assert.That(WindowConstraints.NeedsClamp(adapter.CurrentSize), Is.False);

			adapter.ResizeFromUser(new WindowSize(1024, 640));
			lifecycle.Tick();

			Assert.That(adapter.CurrentSize, Is.EqualTo(new WindowSize(1280, 720)));
			Assert.That(adapter.MaintainCount, Is.EqualTo(1));
			Assert.That(adapter.SetCount, Is.EqualTo(1));
			Assert.That(adapter.LastSetSize, Is.EqualTo(new WindowSize(1280, 720)));
		}

		[Test]
		public void InitialPlayerWindowContractIs1600By900() {
			Assert.That(WindowConstraints.InitialWidth, Is.EqualTo(1600));
			Assert.That(WindowConstraints.InitialHeight, Is.EqualTo(900));
			Assert.That(WindowConstraints.MinimumWidth, Is.EqualTo(1280));
			Assert.That(WindowConstraints.MinimumHeight, Is.EqualTo(720));
		}

		private sealed class RecordingWindowAdapter : IWindowAdapter {
			public bool IsSupported => true;
			public bool IsFullscreen => false;
			public WindowSize CurrentSize { get; private set; }
			public int SetCount { get; private set; }
			public int MaintainCount { get; private set; }
			public WindowSize LastSetSize { get; private set; }

			public RecordingWindowAdapter(WindowSize size) { CurrentSize = size; }
			public void ResizeFromUser(WindowSize size) { CurrentSize = size; }
			public void MaintainWindowFrame() { MaintainCount++; }
			public void SetWindowedSize(WindowSize size) {
				LastSetSize = size;
				CurrentSize = size;
				SetCount++;
			}
		}
	}
}
