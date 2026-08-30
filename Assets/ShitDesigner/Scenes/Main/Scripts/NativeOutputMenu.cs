using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
#if (UNITY_STANDALONE_WIN || UNITY_STANDALONE_OSX) && !UNITY_EDITOR
using System.Runtime.InteropServices;
using AOT;
#endif
[assembly: InternalsVisibleTo("ShitDesigner.Main.Tests.EditMode")]

namespace ShitDesigner.Main {
	internal enum OutputMenuCommand {
		Start,
		Stop,
		IdentifyDisplays
	}

	internal readonly struct OutputMenuState : IEquatable<OutputMenuState> {
		public bool CanStart { get; }
		public bool CanStop { get; }
		public bool CanIdentifyDisplays { get; }

		public OutputMenuState(bool canStart, bool canStop, bool canIdentifyDisplays) {
			CanStart = canStart;
			CanStop = canStop;
			CanIdentifyDisplays = canIdentifyDisplays;
		}

		public bool Equals(OutputMenuState other) =>
			CanStart == other.CanStart &&
			CanStop == other.CanStop &&
			CanIdentifyDisplays == other.CanIdentifyDisplays;

		public override bool Equals(object obj) => obj is OutputMenuState other && Equals(other);
		public override int GetHashCode() => (CanStart ? 1 : 0) | (CanStop ? 2 : 0) | (CanIdentifyDisplays ? 4 : 0);
	}

	internal interface ILiveOutputMenuTarget {
		bool IsOutputActive { get; }
		bool IsAvailable { get; }
		bool SetOutputActive(bool active);
		void IdentifyDisplay();
	}

	internal interface INativeOutputMenuBackend : IDisposable {
		bool TryDequeueCommand(out OutputMenuCommand command);
		void Refresh(OutputMenuState state);
	}

	internal sealed class LiveOutputMenuController : IDisposable {
		private readonly ILiveOutputMenuTarget m_Output;
		private readonly INativeOutputMenuBackend m_Backend;

		public LiveOutputMenuController(ILiveOutputMenuTarget output)
			: this(output, NativeOutputMenuBackend.Create()) { }

		internal LiveOutputMenuController(ILiveOutputMenuTarget output, INativeOutputMenuBackend backend) {
			m_Output = output ?? throw new ArgumentNullException(nameof(output));
			m_Backend = backend ?? throw new ArgumentNullException(nameof(backend));
		}

		public void Tick() {
			m_Backend.Refresh(CaptureState());
			while (m_Backend.TryDequeueCommand(out var command)) Execute(command);
			m_Backend.Refresh(CaptureState());
		}

		public void Dispose() => m_Backend.Dispose();

		private OutputMenuState CaptureState() => new OutputMenuState(
			m_Output.IsAvailable && !m_Output.IsOutputActive,
			m_Output.IsOutputActive,
			m_Output.IsAvailable);

		private void Execute(OutputMenuCommand command) {
			switch (command) {
				case OutputMenuCommand.Start:
					if (m_Output.IsAvailable && !m_Output.IsOutputActive) m_Output.SetOutputActive(true);
					break;
				case OutputMenuCommand.Stop:
					if (m_Output.IsOutputActive) m_Output.SetOutputActive(false);
					break;
				case OutputMenuCommand.IdentifyDisplays:
					if (m_Output.IsAvailable) m_Output.IdentifyDisplay();
					break;
			}
		}
	}

	internal static class NativeOutputMenuBackend {
		public static INativeOutputMenuBackend Create() {
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
			return new WindowsNativeOutputMenuBackend();
#elif UNITY_STANDALONE_OSX && !UNITY_EDITOR
			return new MacOsNativeOutputMenuBackend();
#else
			return new NullNativeOutputMenuBackend();
#endif
		}
	}

	internal sealed class NullNativeOutputMenuBackend : INativeOutputMenuBackend {
		public bool TryDequeueCommand(out OutputMenuCommand command) { command = default; return false; }
		public void Refresh(OutputMenuState state) { }
		public void Dispose() { }
	}

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
	internal sealed class WindowsNativeOutputMenuBackend : INativeOutputMenuBackend {
		private const int WindowProcedureIndex = -4;
		private const uint WindowCommandMessage = 0x0111;
		private const uint ByCommand = 0x0000;
		private const uint ByPosition = 0x0400;
		private const uint StringItem = 0x0000;
		private const uint PopupItem = 0x0010;
		private const uint SeparatorItem = 0x0800;
		private const uint EnabledItem = 0x0000;
		private const uint GrayedItem = 0x0001;
		private const int StartCommandId = 0x6D01;
		private const int StopCommandId = 0x6D02;
		private const int IdentifyCommandId = 0x6D03;
		private static readonly WindowProcedure MenuWindowProcedure = HandleWindowMessage;
		private static readonly IntPtr MenuWindowProcedurePointer = Marshal.GetFunctionPointerForDelegate(MenuWindowProcedure);
		private static readonly Dictionary<IntPtr, WindowsNativeOutputMenuBackend> Instances = new Dictionary<IntPtr, WindowsNativeOutputMenuBackend>();

		private readonly Queue<OutputMenuCommand> m_Commands = new Queue<OutputMenuCommand>();
		private IntPtr m_Window;
		private IntPtr m_MenuBar;
		private IntPtr m_OutputMenu;
		private IntPtr m_PreviousWindowProcedure;
		private int m_OutputMenuPosition;
		private bool m_OwnsMenuBar;
		private bool m_Disposed;
		private OutputMenuState m_AppliedState;
		private bool m_HasAppliedState;

		public bool TryDequeueCommand(out OutputMenuCommand command) {
			if (m_Commands.Count == 0) { command = default; return false; }
			command = m_Commands.Dequeue();
			return true;
		}

		public void Refresh(OutputMenuState state) {
			if (m_Disposed || (!EnsureCreated() || (m_HasAppliedState && m_AppliedState.Equals(state)))) return;
			SetEnabled(StartCommandId, state.CanStart);
			SetEnabled(StopCommandId, state.CanStop);
			SetEnabled(IdentifyCommandId, state.CanIdentifyDisplays);
			m_AppliedState = state;
			m_HasAppliedState = true;
		}

		public void Dispose() {
			if (m_Disposed) return;
			m_Disposed = true;
			if (m_Window != IntPtr.Zero && Instances.Remove(m_Window) && m_PreviousWindowProcedure != IntPtr.Zero)
				SetWindowLongPtr(m_Window, WindowProcedureIndex, m_PreviousWindowProcedure);
			if (m_MenuBar != IntPtr.Zero) {
				if (m_OwnsMenuBar) {
					SetMenu(m_Window, IntPtr.Zero);
					DestroyMenu(m_MenuBar);
				}
				else {
					RemoveMenu(m_MenuBar, (uint)m_OutputMenuPosition, ByPosition);
					DestroyMenu(m_OutputMenu);
				}
				DrawMenuBar(m_Window);
			}
			m_Window = IntPtr.Zero;
			m_MenuBar = IntPtr.Zero;
			m_OutputMenu = IntPtr.Zero;
		}

		private bool EnsureCreated() {
			if (m_Window != IntPtr.Zero) return true;
			var window = WindowsDisplayWindowController.CaptureMainWindow();
			if (window == IntPtr.Zero) return false;
			var menuBar = GetMenu(window);
			m_OwnsMenuBar = menuBar == IntPtr.Zero;
			if (m_OwnsMenuBar) menuBar = CreateMenu();
			var outputMenu = CreatePopupMenu();
			if (menuBar == IntPtr.Zero || outputMenu == IntPtr.Zero) {
				if (m_OwnsMenuBar && menuBar != IntPtr.Zero) DestroyMenu(menuBar);
				if (outputMenu != IntPtr.Zero) DestroyMenu(outputMenu);
				return false;
			}

			AppendMenu(outputMenu, StringItem, new UIntPtr(StartCommandId), "Start External Output");
			AppendMenu(outputMenu, StringItem, new UIntPtr(StopCommandId), "Stop External Output");
			AppendMenu(outputMenu, SeparatorItem, UIntPtr.Zero, null);
			AppendMenu(outputMenu, StringItem, new UIntPtr(IdentifyCommandId), "Identify Displays");
			m_OutputMenuPosition = GetMenuItemCount(menuBar);
			if (!AppendMenu(menuBar, PopupItem, new UIntPtr(unchecked((ulong)outputMenu.ToInt64())), "Output") || !SetMenu(window, menuBar)) {
				DestroyMenu(outputMenu);
				if (m_OwnsMenuBar) DestroyMenu(menuBar);
				return false;
			}

			m_Window = window;
			m_MenuBar = menuBar;
			m_OutputMenu = outputMenu;
			m_PreviousWindowProcedure = SetWindowLongPtr(window, WindowProcedureIndex, MenuWindowProcedurePointer);
			if (m_PreviousWindowProcedure == IntPtr.Zero) {
				Dispose();
				return false;
			}
			Instances[window] = this;
			DrawMenuBar(window);
			return true;
		}

		private void SetEnabled(int commandId, bool enabled) =>
			EnableMenuItem(m_OutputMenu, (uint)commandId, ByCommand | (enabled ? EnabledItem : GrayedItem));

		[MonoPInvokeCallback(typeof(WindowProcedure))]
		private static IntPtr HandleWindowMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam) {
			if (!Instances.TryGetValue(window, out var instance)) return DefWindowProc(window, message, wParam, lParam);
			if (message == WindowCommandMessage) {
				switch (unchecked((ushort)wParam.ToInt64())) {
					case StartCommandId: instance.m_Commands.Enqueue(OutputMenuCommand.Start); return IntPtr.Zero;
					case StopCommandId: instance.m_Commands.Enqueue(OutputMenuCommand.Stop); return IntPtr.Zero;
					case IdentifyCommandId: instance.m_Commands.Enqueue(OutputMenuCommand.IdentifyDisplays); return IntPtr.Zero;
				}
			}
			return CallWindowProc(instance.m_PreviousWindowProcedure, window, message, wParam, lParam);
		}

		private delegate IntPtr WindowProcedure(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

		[DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool AppendMenu(IntPtr menu, uint flags, UIntPtr item, string text);
		[DllImport("user32.dll")] private static extern IntPtr CallWindowProc(IntPtr previous, IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
		[DllImport("user32.dll")] private static extern IntPtr CreateMenu();
		[DllImport("user32.dll")] private static extern IntPtr CreatePopupMenu();
		[DllImport("user32.dll")] private static extern IntPtr DefWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
		[DllImport("user32.dll")] private static extern bool DestroyMenu(IntPtr menu);
		[DllImport("user32.dll")] private static extern bool DrawMenuBar(IntPtr window);
		[DllImport("user32.dll")] private static extern uint EnableMenuItem(IntPtr menu, uint item, uint enable);
		[DllImport("user32.dll")] private static extern IntPtr GetMenu(IntPtr window);
		[DllImport("user32.dll")] private static extern int GetMenuItemCount(IntPtr menu);
		[DllImport("user32.dll")] private static extern bool RemoveMenu(IntPtr menu, uint position, uint flags);
		[DllImport("user32.dll")] private static extern bool SetMenu(IntPtr window, IntPtr menu);
		[DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr value);
	}
#endif

#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
	internal sealed class MacOsNativeOutputMenuBackend : INativeOutputMenuBackend {
		private bool m_Disposed;
		private bool m_HasAppliedState;
		private OutputMenuState m_AppliedState;

		public bool TryDequeueCommand(out OutputMenuCommand command) {
			if (!ShitDesignerOutputMenuTryDequeue(out var commandValue)) { command = default; return false; }
			command = (OutputMenuCommand)commandValue;
			return true;
		}

		public void Refresh(OutputMenuState state) {
			if (m_Disposed) return;
			ShitDesignerOutputMenuCreate();
			if (m_HasAppliedState && m_AppliedState.Equals(state)) return;
			ShitDesignerOutputMenuSetState(state.CanStart, state.CanStop, state.CanIdentifyDisplays);
			m_AppliedState = state;
			m_HasAppliedState = true;
		}

		public void Dispose() {
			if (m_Disposed) return;
			m_Disposed = true;
			ShitDesignerOutputMenuDestroy();
		}

		[DllImport("__Internal")] private static extern void ShitDesignerOutputMenuCreate();
		[DllImport("__Internal")] private static extern void ShitDesignerOutputMenuDestroy();
		[DllImport("__Internal")] private static extern void ShitDesignerOutputMenuSetState(
			[MarshalAs(UnmanagedType.I1)] bool canStart,
			[MarshalAs(UnmanagedType.I1)] bool canStop,
			[MarshalAs(UnmanagedType.I1)] bool canIdentifyDisplays);
		[DllImport("__Internal")] [return: MarshalAs(UnmanagedType.I1)] private static extern bool ShitDesignerOutputMenuTryDequeue(out int command);
	}
#endif
}
