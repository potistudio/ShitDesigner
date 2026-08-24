using System;
using NUnit.Framework;
using ShitDesigner.Application;
using ShitDesigner.Core;
using ShitDesigner.Input;

namespace ShitDesigner.Input.Tests {
	[TestFixture]
	public sealed class KeyboardInputTests {
		private sealed class FakeApplication : IKeyboardInputApplicationPort {
			public bool IsKeyboardLearnActive { get; set; }
			public int Calls { get; private set; }
			public PhysicalKey LastKey { get; private set; }
			public bool LastPressed { get; private set; }
			public ApplicationCommandResult HandleKeyboard(PhysicalKey key, bool pressed) { Calls++; LastKey = key; LastPressed = pressed; return ApplicationCommandResult.Ignored(); }
			public ApplicationCommandResult BeginKeyboardLearn(LogicalControlId id, Guid? interactionId = null) { IsKeyboardLearnActive = true; return ApplicationCommandResult.Ignored(); }
			public ApplicationCommandResult CancelKeyboardLearn(Guid? interactionId = null) { IsKeyboardLearnActive = false; return ApplicationCommandResult.Ignored(); }
		}

		[Test]
		public void TextFocusSuppressesNormalKeyboardInput() {
			var fake = new FakeApplication();
			var focus = new KeyboardFocusState { IsTextInputFocused = true };
			var router = new KeyboardInputRouter(fake, focus);
			router.Route(new PhysicalKey("a"), true);
			Assert.That(fake.Calls, Is.EqualTo(0));
		}

		[Test]
		public void LearnCaptureBypassesTextFocusButNormalInputIsExclusive() {
			var fake = new FakeApplication { IsKeyboardLearnActive = true };
			var focus = new KeyboardFocusState { IsTextInputFocused = true };
			var router = new KeyboardInputRouter(fake, focus);
			router.Route(new PhysicalKey("k"), true);
			Assert.That(fake.Calls, Is.EqualTo(1));
			Assert.That(fake.LastKey.PhysicalId, Is.EqualTo("k"));
		}

		[Test]
		public void ModifierOnlyKeysAreDeliveredToApplicationLearnBoundary() {
			var fake = new FakeApplication { IsKeyboardLearnActive = true };
			var router = new KeyboardInputRouter(fake, new KeyboardFocusState());
			router.Route(new PhysicalKey("leftCtrl", "<Keyboard>/leftCtrl", true), true);
			Assert.That(fake.Calls, Is.EqualTo(1));
		}

		[Test]
		public void ShortcutRouterSuppressesTextFieldShortcuts() {
			var command = new FakeShortcutPort();
			var router = new KeyboardShortcutRouter(command, new KeyboardFocusState { IsTextInputFocused = true });
			var result = router.Route(new ShortcutKey("s", control: true));
			Assert.That(command.SaveCalls, Is.EqualTo(0));
			Assert.That(router.Resolve(new ShortcutKey("s", control: true)), Is.EqualTo(KeyboardShortcut.None));
			Assert.That(result.Status, Is.EqualTo(ApplicationCommandStatus.Ignored));
		}

		[Test]
		public void PrimarySpaceRemainsGlobalWhenTextFieldHasFocus() {
			var command = new FakeShortcutPort();
			var router = new KeyboardShortcutRouter(command, new KeyboardFocusState { IsTextInputFocused = true });
			Assert.That(router.Resolve(new ShortcutKey("space", control: true)), Is.EqualTo(KeyboardShortcut.PauseResume));
		}

		[Test]
		public void PrimaryModifierUsesPlatformAdapterForWindowsAndMacOS() {
			var command = new FakeShortcutPort();
			var windows = new KeyboardShortcutRouter(command, null, new DesktopPrimaryModifierPlatformAdapter(KeyboardPlatform.Windows));
			var mac = new KeyboardShortcutRouter(command, null, new DesktopPrimaryModifierPlatformAdapter(KeyboardPlatform.MacOS));
			Assert.That(windows.Resolve(new ShortcutKey("s", control: true)), Is.EqualTo(KeyboardShortcut.Save));
			Assert.That(windows.Resolve(new ShortcutKey("s", command: true)), Is.EqualTo(KeyboardShortcut.None));
			Assert.That(mac.Resolve(new ShortcutKey("s", command: true)), Is.EqualTo(KeyboardShortcut.Save));
			Assert.That(mac.Resolve(new ShortcutKey("s", control: true)), Is.EqualTo(KeyboardShortcut.None));
		}

		[Test]
		public void GraphSingleKeyRequiresCanvasFocusAndModalEscapeIsNotGraphEscape() {
			var command = new FakeShortcutPort();
			var unfocused = new KeyboardShortcutRouter(command, new KeyboardFocusState { IsGraphCanvasFocused = false });
			Assert.That(unfocused.Resolve(new ShortcutKey("f")), Is.EqualTo(KeyboardShortcut.None));
			var modal = new KeyboardShortcutRouter(command, new KeyboardFocusState { IsModalBlockingShortcuts = true });
			Assert.That(modal.Resolve(new ShortcutKey("escape")), Is.EqualTo(KeyboardShortcut.Dismiss));
		}

		private sealed class FakeShortcutPort : IKeyboardShortcutPort {
			public int SaveCalls { get; private set; }
			public ApplicationCommandResult Save() { SaveCalls++; return ApplicationCommandResult.Ignored(); }
			public ApplicationCommandResult NewProject() => ApplicationCommandResult.Ignored();
			public ApplicationCommandResult OpenProject() => ApplicationCommandResult.Ignored();
			public ApplicationCommandResult CloseProject() => ApplicationCommandResult.Ignored();
		}
	}
}
