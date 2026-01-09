using System.Windows.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Terminal.Services;

/// <summary>
/// Handles terminal keyboard shortcuts including special keys, copy/paste, zoom, and search.
/// </summary>
public sealed class TerminalKeyboardHandler : ITerminalKeyboardHandler
{
    private readonly ILogger<TerminalKeyboardHandler> _logger;

    // Escape sequences for special keys
    private const string DeleteKeySequence = "\x1b[3~";
    private const string InsertKeySequence = "\x1b[2~";

    public TerminalKeyboardHandler(ILogger<TerminalKeyboardHandler>? logger = null)
    {
        _logger = logger ?? NullLogger<TerminalKeyboardHandler>.Instance;
    }

    /// <inheritdoc />
    public bool HandleKeyDown(KeyEventArgs e, IKeyboardHandlerContext context)
    {
        ArgumentNullException.ThrowIfNull(e);
        ArgumentNullException.ThrowIfNull(context);

        // Handle Delete key - send escape sequence directly to SSH
        // WebView2 may intercept this key before it reaches xterm.js
        if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            context.SendText(DeleteKeySequence);
            _logger.LogDebug("Sent Delete key escape sequence");
            return true;
        }

        // Handle Insert key - send escape sequence directly to SSH
        if (e.Key == Key.Insert && Keyboard.Modifiers == ModifierKeys.None)
        {
            context.SendText(InsertKeySequence);
            _logger.LogDebug("Sent Insert key escape sequence");
            return true;
        }

        // Handle Ctrl+F for Find
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            context.ShowFindOverlay();
            _logger.LogDebug("Opened find overlay");
            return true;
        }

        // Handle Escape to close find overlay
        if (e.Key == Key.Escape && context.IsFindOverlayVisible)
        {
            context.HideFindOverlay();
            _logger.LogDebug("Closed find overlay");
            return true;
        }

        // Handle Ctrl+Shift+C for Copy
        if (e.Key == Key.C && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            context.CopyToClipboard();
            return true;
        }

        // Handle Ctrl+Shift+V for Paste
        if (e.Key == Key.V && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            context.PasteFromClipboard();
            return true;
        }

        // Handle Ctrl++ (Ctrl+Plus or Ctrl+OemPlus) for zoom in
        if (Keyboard.Modifiers == ModifierKeys.Control &&
            (e.Key == Key.OemPlus || e.Key == Key.Add))
        {
            context.ZoomIn();
            _logger.LogDebug("Zoomed in");
            return true;
        }

        // Handle Ctrl+- (Ctrl+Minus or Ctrl+OemMinus) for zoom out
        if (Keyboard.Modifiers == ModifierKeys.Control &&
            (e.Key == Key.OemMinus || e.Key == Key.Subtract))
        {
            context.ZoomOut();
            _logger.LogDebug("Zoomed out");
            return true;
        }

        // Handle Ctrl+0 for reset zoom
        if (Keyboard.Modifiers == ModifierKeys.Control &&
            (e.Key == Key.D0 || e.Key == Key.NumPad0))
        {
            context.ResetZoom();
            _logger.LogDebug("Reset zoom");
            return true;
        }

        return false;
    }
}
