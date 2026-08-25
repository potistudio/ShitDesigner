using NUnit.Framework;
using ShitDesigner.Bootstrap;
using UnityEditor;
using UnityEngine;

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
			Assert.That(WindowConstraints.NeedsClamp(adapter.CurrentSize), Is.False);

			adapter.ResizeFromUser(new WindowSize(1024, 640));
			var corrected = WindowConstraints.Clamp(adapter.CurrentSize);
			adapter.SetWindowedSize(corrected);

			Assert.That(adapter.CurrentSize, Is.EqualTo(new WindowSize(1280, 720)));
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

		[Test]
		public void InitialPlayerWindowContractRestoresTheControllerAfterFullscreenStartup() {
			var adapter = new RecordingWindowAdapter(new WindowSize(1920, 1080), isFullscreen: true);
			var lifecycle = new WindowLifecycle(adapter);

			Assert.That(lifecycle.Activate().IsSuccess, Is.True);
			Assert.That(adapter.SetCount, Is.EqualTo(1));
			Assert.That(adapter.LastSetSize, Is.EqualTo(new WindowSize(1600, 900)));
			Assert.That(adapter.IsFullscreen, Is.False);
		}

		[Test]
		public void PlayerStartsFullscreenBeforeTheControllerWindowIsRestored() {
			Assert.That(PlayerSettings.fullScreenMode, Is.EqualTo(FullScreenMode.FullScreenWindow));
		}

		private sealed class RecordingWindowAdapter : IWindowAdapter {
			public bool IsSupported => true;
			public bool IsFullscreen { get; private set; }
			public WindowSize CurrentSize { get; private set; }
			public int SetCount { get; private set; }
			public WindowSize LastSetSize { get; private set; }

			public RecordingWindowAdapter(WindowSize size, bool isFullscreen = false) {
				CurrentSize = size;
				IsFullscreen = isFullscreen;
			}
			public void ResizeFromUser(WindowSize size) { CurrentSize = size; }
			public void SetWindowedSize(WindowSize size) {
				LastSetSize = size;
				CurrentSize = size;
				IsFullscreen = false;
				SetCount++;
			}
		}
	}
}
