using System;
using ShitDesigner.Application;
using ShitDesigner.Core;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
#endif

namespace ShitDesigner.Input
{
    /// <summary>Presentation focus state supplied by the active UI shell.</summary>
    public interface IKeyboardFocusState
    {
        bool IsTextInputFocused { get; }
        bool IsModalBlockingShortcuts { get; }
        bool IsGraphCanvasFocused { get; }
    }

    public sealed class KeyboardFocusState : IKeyboardFocusState
    {
        public bool IsTextInputFocused { get; set; }
        public bool IsModalBlockingShortcuts { get; set; }
        public bool IsGraphCanvasFocused { get; set; } = true;
    }

    /// <summary>
    /// Routes only keyboard events.  Text fields and modal surfaces suppress
    /// normal shortcuts, while Learn mode remains exclusive and is handled by
    /// the Application port itself.
    /// </summary>
    public sealed class KeyboardInputRouter
    {
        private readonly IKeyboardInputApplicationPort _application;
        private readonly IKeyboardFocusState _focus;

        public KeyboardInputRouter(IKeyboardInputApplicationPort application, IKeyboardFocusState focus = null)
        {
            _application = application ?? throw new ArgumentNullException(nameof(application));
            _focus = focus;
        }

        public ApplicationCommandResult Route(PhysicalKey key, bool pressed)
        {
            if (!_application.IsKeyboardLearnActive && _focus != null && (_focus.IsTextInputFocused || _focus.IsModalBlockingShortcuts)) return ApplicationCommandResult.Ignored();
            return _application.HandleKeyboard(key, pressed);
        }

        public ApplicationCommandResult BeginLearn(LogicalControlId id, Guid? interactionId = null) => _application.BeginKeyboardLearn(id, interactionId);
        public ApplicationCommandResult CancelLearn(Guid? interactionId = null) => _application.CancelKeyboardLearn(interactionId);
    }

    public enum KeyboardShortcut
    {
        None,
        Save,
        SaveAs,
        NewProject,
        OpenProject,
        CloseProject,
        Undo,
        Redo,
        CommandPalette,
        PauseResume,
        CloseActivePanel,
        FocusDiagnostics,
        FocusProgram,
        GraphAddNode,
        GraphDelete,
        GraphCopy,
        GraphPaste,
        GraphDuplicate,
        GraphSelectAll,
        GraphEscape,
        GraphFrameSelection,
        GraphHome,
        GraphToggleGrid,
        GraphToggleMinimap,
        Dismiss
    }

    public enum KeyboardPlatform { Windows, Linux, MacOS }

    public interface IPrimaryModifierPlatformAdapter
    {
        bool IsPrimary(ShortcutKey key);
    }

    public sealed class DesktopPrimaryModifierPlatformAdapter : IPrimaryModifierPlatformAdapter
    {
        public KeyboardPlatform Platform { get; }
        public DesktopPrimaryModifierPlatformAdapter(KeyboardPlatform platform) { Platform = platform; }
        public bool IsPrimary(ShortcutKey key) => Platform == KeyboardPlatform.MacOS ? key.Command && !key.Control : key.Control && !key.Command;
    }

    public readonly struct ShortcutKey : IEquatable<ShortcutKey>
    {
        public string Key { get; }
        public bool Control { get; }
        public bool Command { get; }
        public bool Shift { get; }
        public bool Alt { get; }

        public ShortcutKey(string key, bool control = false, bool shift = false, bool alt = false, bool command = false)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Shortcut key is required.", nameof(key));
            Key = key.Trim();
            Control = control;
            Command = command;
            Shift = shift;
            Alt = alt;
        }

        public bool IsPrimary => Control || Command;
        public bool Equals(ShortcutKey other) => string.Equals(Key, other.Key, StringComparison.OrdinalIgnoreCase) && Control == other.Control && Command == other.Command && Shift == other.Shift && Alt == other.Alt;
        public override bool Equals(object obj) => obj is ShortcutKey && Equals((ShortcutKey)obj);
        public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Key) ^ Control.GetHashCode() ^ Command.GetHashCode() ^ Shift.GetHashCode() ^ Alt.GetHashCode();
    }

    public interface IKeyboardShortcutPort
    {
        ApplicationCommandResult Save();
        ApplicationCommandResult NewProject();
        ApplicationCommandResult OpenProject();
        ApplicationCommandResult CloseProject();
    }

    /// <summary>Optional extended command surface.  The original four-method
    /// port remains source compatible for small adapters and tests.</summary>
    public interface IKeyboardShortcutExtendedPort : IKeyboardShortcutPort
    {
        ApplicationCommandResult SaveAs();
        ApplicationCommandResult Undo();
        ApplicationCommandResult Redo();
        ApplicationCommandResult CommandPalette();
        ApplicationCommandResult PauseResume();
        ApplicationCommandResult CloseActivePanel();
        ApplicationCommandResult FocusDiagnostics();
        ApplicationCommandResult FocusProgram();
        ApplicationCommandResult Graph(KeyboardShortcut shortcut);
        ApplicationCommandResult Dismiss();
    }

    public sealed class ApplicationKeyboardShortcutPort : IKeyboardShortcutExtendedPort
    {
        private readonly IApplicationShortcutCommandPort _application;
        public ApplicationKeyboardShortcutPort(IApplicationShortcutCommandPort application) { _application = application ?? throw new ArgumentNullException(nameof(application)); }
        public ApplicationCommandResult Save() => _application.ExecuteShortcut(ApplicationShortcutCommand.Save);
        public ApplicationCommandResult NewProject() => _application.ExecuteShortcut(ApplicationShortcutCommand.NewProject);
        public ApplicationCommandResult OpenProject() => _application.ExecuteShortcut(ApplicationShortcutCommand.OpenProject);
        public ApplicationCommandResult CloseProject() => _application.ExecuteShortcut(ApplicationShortcutCommand.CloseProject);
        public ApplicationCommandResult SaveAs() => _application.ExecuteShortcut(ApplicationShortcutCommand.SaveAs);
        public ApplicationCommandResult Undo() => _application.ExecuteShortcut(ApplicationShortcutCommand.Undo);
        public ApplicationCommandResult Redo() => _application.ExecuteShortcut(ApplicationShortcutCommand.Redo);
        public ApplicationCommandResult CommandPalette() => _application.ExecuteShortcut(ApplicationShortcutCommand.CommandPalette);
        public ApplicationCommandResult PauseResume() => _application.ExecuteShortcut(ApplicationShortcutCommand.PauseResume);
        public ApplicationCommandResult CloseActivePanel() => _application.ExecuteShortcut(ApplicationShortcutCommand.CloseActivePanel);
        public ApplicationCommandResult FocusDiagnostics() => _application.ExecuteShortcut(ApplicationShortcutCommand.FocusDiagnostics);
        public ApplicationCommandResult FocusProgram() => _application.ExecuteShortcut(ApplicationShortcutCommand.FocusProgram);
        public ApplicationCommandResult Graph(KeyboardShortcut shortcut) => ApplicationCommandResult.Ignored();
        public ApplicationCommandResult Dismiss() => _application.ExecuteShortcut(ApplicationShortcutCommand.Dismiss);
    }

    /// <summary>Small, deterministic shortcut resolver kept outside the UI.</summary>
    public sealed class KeyboardShortcutRouter
    {
        private readonly IKeyboardShortcutPort _commands;
        private readonly IKeyboardFocusState _focus;
        private readonly IPrimaryModifierPlatformAdapter _platform;
        public KeyboardShortcutRouter(IKeyboardShortcutPort commands, IKeyboardFocusState focus = null)
            : this(commands, focus, new DesktopPrimaryModifierPlatformAdapter(KeyboardPlatform.Windows)) { }
        public KeyboardShortcutRouter(IKeyboardShortcutPort commands, IKeyboardFocusState focus, IPrimaryModifierPlatformAdapter platform)
        {
            _commands = commands ?? throw new ArgumentNullException(nameof(commands));
            _focus = focus;
            _platform = platform ?? throw new ArgumentNullException(nameof(platform));
        }

        public KeyboardShortcut Resolve(ShortcutKey key)
        {
            var primary = _platform.IsPrimary(key);
            if (primary && !key.Shift && string.Equals(key.Key, "SPACE", StringComparison.OrdinalIgnoreCase) && (_focus == null || !_focus.IsModalBlockingShortcuts)) return KeyboardShortcut.PauseResume;
            if (_focus != null && _focus.IsTextInputFocused) return KeyboardShortcut.None;
            if (_focus != null && _focus.IsModalBlockingShortcuts) return string.Equals(key.Key, "escape", StringComparison.OrdinalIgnoreCase) ? KeyboardShortcut.Dismiss : KeyboardShortcut.None;
            if (key.Alt) return KeyboardShortcut.None;
            if (primary && !key.Shift)
            {
                switch (key.Key.ToUpperInvariant())
                {
                    case "S": return KeyboardShortcut.Save;
                    case "N": return KeyboardShortcut.NewProject;
                    case "O": return KeyboardShortcut.OpenProject;
                    case "W": return KeyboardShortcut.CloseActivePanel;
                    case "Z": return KeyboardShortcut.Undo;
                    case "K": return KeyboardShortcut.CommandPalette;
                    case "SPACE": return KeyboardShortcut.PauseResume;
                    case "C": return GraphAllowed() ? KeyboardShortcut.GraphCopy : KeyboardShortcut.None;
                    case "V": return GraphAllowed() ? KeyboardShortcut.GraphPaste : KeyboardShortcut.None;
                    case "D": return GraphAllowed() ? KeyboardShortcut.GraphDuplicate : KeyboardShortcut.None;
                    case "A": return GraphAllowed() ? KeyboardShortcut.GraphSelectAll : KeyboardShortcut.None;
                    default: break;
                }
            }
            if (primary && key.Shift)
            {
                switch (key.Key.ToUpperInvariant())
                {
                    case "S": return KeyboardShortcut.SaveAs;
                    case "Z": return KeyboardShortcut.Redo;
                    case "D": return KeyboardShortcut.FocusDiagnostics;
                    case "P": return KeyboardShortcut.FocusProgram;
                    default: break;
                }
            }
            if (primary) return KeyboardShortcut.None;
            if (!GraphAllowed()) return KeyboardShortcut.None;
            switch (key.Key.ToUpperInvariant())
            {
                case "TAB": return KeyboardShortcut.GraphAddNode;
                case "DELETE":
                case "BACKSPACE": return KeyboardShortcut.GraphDelete;
                case "ESCAPE": return KeyboardShortcut.GraphEscape;
                case "F": return KeyboardShortcut.GraphFrameSelection;
                case "HOME": return KeyboardShortcut.GraphHome;
                case "G": return KeyboardShortcut.GraphToggleGrid;
                case "M": return KeyboardShortcut.GraphToggleMinimap;
                default: return KeyboardShortcut.None;
            }
        }

        private bool GraphAllowed() => _focus == null || _focus.IsGraphCanvasFocused;

        public ApplicationCommandResult Route(ShortcutKey key)
        {
            switch (Resolve(key))
            {
                case KeyboardShortcut.Save: return _commands.Save();
                case KeyboardShortcut.SaveAs: return Extended()?.SaveAs() ?? ApplicationCommandResult.Ignored();
                case KeyboardShortcut.NewProject: return _commands.NewProject();
                case KeyboardShortcut.OpenProject: return _commands.OpenProject();
                case KeyboardShortcut.CloseProject: return _commands.CloseProject();
                case KeyboardShortcut.Undo: return Extended()?.Undo() ?? ApplicationCommandResult.Ignored();
                case KeyboardShortcut.Redo: return Extended()?.Redo() ?? ApplicationCommandResult.Ignored();
                case KeyboardShortcut.CommandPalette: return Extended()?.CommandPalette() ?? ApplicationCommandResult.Ignored();
                case KeyboardShortcut.PauseResume: return Extended()?.PauseResume() ?? ApplicationCommandResult.Ignored();
                case KeyboardShortcut.CloseActivePanel: return Extended()?.CloseActivePanel() ?? ApplicationCommandResult.Ignored();
                case KeyboardShortcut.FocusDiagnostics: return Extended()?.FocusDiagnostics() ?? ApplicationCommandResult.Ignored();
                case KeyboardShortcut.FocusProgram: return Extended()?.FocusProgram() ?? ApplicationCommandResult.Ignored();
                case KeyboardShortcut.Dismiss: return Extended()?.Dismiss() ?? ApplicationCommandResult.Ignored();
                default:
                    return Extended()?.Graph(Resolve(key)) ?? ApplicationCommandResult.Ignored();
            }
        }

        private IKeyboardShortcutExtendedPort Extended() => _commands as IKeyboardShortcutExtendedPort;
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>
    /// Thin Unity Input System adapter. It owns no Project or Runtime state;
    /// all events cross the Application keyboard port.
    /// </summary>
    public sealed class UnityKeyboardAdapter
    {
        private readonly KeyboardInputRouter _router;
        public UnityKeyboardAdapter(IKeyboardInputApplicationPort application, IKeyboardFocusState focus = null) { _router = new KeyboardInputRouter(application ?? throw new ArgumentNullException(nameof(application)), focus); }
        public UnityKeyboardAdapter(KeyboardInputRouter router) { _router = router ?? throw new ArgumentNullException(nameof(router)); }

        public void Poll()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;
            foreach (var key in keyboard.allKeys)
            {
                if (key.wasPressedThisFrame) _router.Route(new PhysicalKey(key.name, key.path, IsModifier(key)), true);
                if (key.wasReleasedThisFrame) _router.Route(new PhysicalKey(key.name, key.path, IsModifier(key)), false);
            }
        }

        private static bool IsModifier(KeyControl key)
        {
            var name = key == null ? string.Empty : key.name;
            return string.Equals(name, "leftCtrl", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "rightCtrl", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "leftShift", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "rightShift", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "leftAlt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "rightAlt", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "leftMeta", StringComparison.OrdinalIgnoreCase)
                || string.Equals(name, "rightMeta", StringComparison.OrdinalIgnoreCase);
        }
    }
#endif
}
