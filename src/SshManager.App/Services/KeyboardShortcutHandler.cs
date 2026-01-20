using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using SshManager.App.Models;
using SshManager.Terminal.Controls;
using SshManager.Terminal.Services;

namespace SshManager.App.Services;

/// <summary>
/// Handles keyboard shortcuts for the main window.
/// Detects terminal focus to avoid conflicting with terminal input.
/// </summary>
public class KeyboardShortcutHandler : IKeyboardShortcutHandler
{
    private readonly ITerminalFocusTracker _focusTracker;
    private readonly IPaneLayoutManager _paneLayoutManager;
    private Window? _attachedWindow;

    public event EventHandler<ShortcutActionEventArgs>? ActionRequested;

    public bool IsAttached => _attachedWindow != null;

    public KeyboardShortcutHandler(
        ITerminalFocusTracker focusTracker,
        IPaneLayoutManager paneLayoutManager)
    {
        _focusTracker = focusTracker;
        _paneLayoutManager = paneLayoutManager;
    }

    public void AttachTo(Window window)
    {
        if (_attachedWindow != null)
        {
            Detach();
        }

        _attachedWindow = window;
        _attachedWindow.PreviewKeyDown += OnPreviewKeyDown;
    }

    public void Detach()
    {
        if (_attachedWindow != null)
        {
            _attachedWindow.PreviewKeyDown -= OnPreviewKeyDown;
            _attachedWindow = null;
        }
    }

    /// <summary>
    /// Checks if the keyboard focus is currently within a terminal control (WebView2).
    /// When true, most Ctrl+letter shortcuts should pass through to the terminal
    /// instead of being handled by the application.
    /// </summary>
    private bool IsTerminalFocused()
    {
        // Primary: Use the focus tracker service (most reliable for WebView2)
        if (_focusTracker.IsAnyTerminalFocused)
            return true;

        // Fallback: Walk the visual tree as a safety net
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused == null) return false;

        while (focused != null)
        {
            var typeName = focused.GetType().FullName;

            // Check for WebView2 control (the actual terminal renderer)
            if (typeName == "Microsoft.Web.WebView2.Wpf.WebView2")
                return true;

            // Check for our terminal control types
            if (focused is SshTerminalControl)
                return true;

            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    private void RaiseAction(ShortcutAction action)
    {
        ActionRequested?.Invoke(this, new ShortcutActionEventArgs(action));
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Check if terminal has focus - if so, let terminal-conflicting shortcuts pass through
        var terminalHasFocus = IsTerminalFocused();

        // Handle Ctrl+F - only intercept when terminal is NOT focused
        // (Ctrl+F is forward-char in bash/readline)
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!terminalHasFocus)
            {
                RaiseAction(ShortcutAction.FocusSearch);
                e.Handled = true;
            }
            return;
        }
        // Handle Escape to clear search (only when search is focused - handled by MainWindow)
        else if (e.Key == Key.Escape)
        {
            RaiseAction(ShortcutAction.ClearSearch);
            // Don't mark as handled - let the caller decide
            return;
        }
        // Handle Ctrl+H for history - only when terminal NOT focused
        // (Ctrl+H is backspace in terminals)
        else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!terminalHasFocus)
            {
                RaiseAction(ShortcutAction.ShowHistory);
                e.Handled = true;
            }
            return;
        }
        // Handle Ctrl+, for settings (not a terminal shortcut, always handle)
        else if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
        {
            RaiseAction(ShortcutAction.ShowSettings);
            e.Handled = true;
        }
        // Handle Ctrl+Shift+S for snippets (Shift modifier, always handle)
        else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            RaiseAction(ShortcutAction.ShowSnippets);
            e.Handled = true;
        }
        // Handle Ctrl+K for Quick Connect overlay - only when terminal NOT focused
        // (Ctrl+K is kill-line in bash, cut-line in nano)
        else if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!terminalHasFocus)
            {
                RaiseAction(ShortcutAction.ShowQuickConnectOverlay);
                e.Handled = true;
            }
            return;
        }
        // Handle Ctrl+Q for Quick Connect (not a common terminal shortcut, always handle)
        else if (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control)
        {
            RaiseAction(ShortcutAction.ShowQuickConnectDialog);
            e.Handled = true;
        }
        // Handle Ctrl+N for Add Host - only when terminal NOT focused
        // (Ctrl+N is next-history in bash)
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!terminalHasFocus)
            {
                RaiseAction(ShortcutAction.AddHost);
                e.Handled = true;
            }
            return;
        }
        // Handle Ctrl+E for Edit Host - only when terminal NOT focused
        // (Ctrl+E is end-of-line in bash)
        else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!terminalHasFocus)
            {
                RaiseAction(ShortcutAction.EditHost);
                e.Handled = true;
            }
            return;
        }
        // Handle Ctrl+B for SFTP Browser - only when terminal NOT focused
        // (Ctrl+B is back-char in bash)
        else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!terminalHasFocus)
            {
                RaiseAction(ShortcutAction.OpenSftpBrowser);
                e.Handled = true;
            }
            return;
        }
        // Handle Delete for Delete Host - only when terminal NOT focused
        else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (!terminalHasFocus)
            {
                RaiseAction(ShortcutAction.DeleteHost);
                e.Handled = true;
            }
            return;
        }
        // Handle Ctrl+Tab to cycle through panes
        else if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _paneLayoutManager.CycleFocusNext();
            e.Handled = true;
        }
        // Handle Ctrl+Shift+Tab to cycle previous pane
        else if (e.Key == Key.Tab && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _paneLayoutManager.CycleFocusPrevious();
            e.Handled = true;
        }
        // Handle Ctrl+Shift+D for vertical split
        else if (e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            RaiseAction(ShortcutAction.SplitVertical);
            e.Handled = true;
        }
        // Handle Ctrl+Shift+E for horizontal split
        else if (e.Key == Key.E && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            RaiseAction(ShortcutAction.SplitHorizontal);
            e.Handled = true;
        }
        // Handle Ctrl+Shift+M for mirror pane
        else if (e.Key == Key.M && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            RaiseAction(ShortcutAction.MirrorPane);
            e.Handled = true;
        }
        // Handle Ctrl+Shift+W for close pane
        else if (e.Key == Key.W && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            RaiseAction(ShortcutAction.ClosePane);
            e.Handled = true;
        }
        // Handle Ctrl+Shift+P for Serial Port Quick Connect
        else if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            RaiseAction(ShortcutAction.ShowSerialQuickConnect);
            e.Handled = true;
        }
        // Handle Alt+Arrow for pane navigation
        else if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            var action = e.Key switch
            {
                Key.Left => ShortcutAction.NavigateLeft,
                Key.Right => ShortcutAction.NavigateRight,
                Key.Up => ShortcutAction.NavigateUp,
                Key.Down => ShortcutAction.NavigateDown,
                _ => (ShortcutAction?)null
            };

            if (action.HasValue)
            {
                var direction = e.Key switch
                {
                    Key.Left => NavigationDirection.Left,
                    Key.Right => NavigationDirection.Right,
                    Key.Up => NavigationDirection.Up,
                    Key.Down => NavigationDirection.Down,
                    _ => NavigationDirection.Left
                };
                _paneLayoutManager.NavigateFocus(direction);
                e.Handled = true;
            }
        }
        // Handle F1 for keyboard shortcuts help
        else if (e.Key == Key.F1)
        {
            RaiseAction(ShortcutAction.ShowKeyboardShortcuts);
            e.Handled = true;
        }
    }
}
