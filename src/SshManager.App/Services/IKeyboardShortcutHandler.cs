using System.Windows;

namespace SshManager.App.Services;

/// <summary>
/// Event arguments for keyboard shortcut actions that request UI operations.
/// </summary>
public class ShortcutActionEventArgs : EventArgs
{
    public ShortcutAction Action { get; }
    
    public ShortcutActionEventArgs(ShortcutAction action)
    {
        Action = action;
    }
}

/// <summary>
/// Defines the types of actions that can be triggered by keyboard shortcuts.
/// </summary>
public enum ShortcutAction
{
    FocusSearch,
    ClearSearch,
    ShowHistory,
    ShowSettings,
    ShowSnippets,
    ShowQuickConnectOverlay,
    ShowQuickConnectDialog,
    AddHost,
    EditHost,
    DeleteHost,
    OpenSftpBrowser,
    ShowKeyboardShortcuts,
    ShowSerialQuickConnect,
    SplitVertical,
    SplitHorizontal,
    MirrorPane,
    ClosePane,
    CycleFocusNext,
    CycleFocusPrevious,
    NavigateLeft,
    NavigateRight,
    NavigateUp,
    NavigateDown
}

/// <summary>
/// Handles keyboard shortcuts for the main window, delegating actions via events.
/// </summary>
public interface IKeyboardShortcutHandler
{
    /// <summary>
    /// Raised when a keyboard shortcut triggers an action.
    /// </summary>
    event EventHandler<ShortcutActionEventArgs>? ActionRequested;
    
    /// <summary>
    /// Attaches the keyboard handler to a window.
    /// </summary>
    /// <param name="window">The window to handle keyboard input for.</param>
    void AttachTo(Window window);
    
    /// <summary>
    /// Detaches the keyboard handler from the attached window.
    /// </summary>
    void Detach();
    
    /// <summary>
    /// Gets whether the handler is currently attached to a window.
    /// </summary>
    bool IsAttached { get; }
}
