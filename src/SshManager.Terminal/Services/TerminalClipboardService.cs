using System.Windows;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service implementation for terminal clipboard operations.
/// Encapsulates clipboard access with proper error handling.
/// </summary>
public sealed class TerminalClipboardService : ITerminalClipboardService
{
    private readonly ILogger<TerminalClipboardService> _logger;

    public TerminalClipboardService(ILogger<TerminalClipboardService>? logger = null)
    {
        _logger = logger ?? NullLogger<TerminalClipboardService>.Instance;
    }

    /// <inheritdoc />
    public bool HasClipboardText
    {
        get
        {
            try
            {
                return Clipboard.ContainsText();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check clipboard contents");
                return false;
            }
        }
    }

    /// <inheritdoc />
    public void CopyToClipboard()
    {
        try
        {
            // WebTerminalControl handles selection internally via xterm.js
            // This method is a placeholder for any additional copy logic
            _logger.LogDebug("Copy to clipboard requested");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy to clipboard");
        }
    }

    /// <inheritdoc />
    public void PasteFromClipboard(Action<string> sendText)
    {
        ArgumentNullException.ThrowIfNull(sendText);

        try
        {
            if (Clipboard.ContainsText())
            {
                var text = Clipboard.GetText();
                if (!string.IsNullOrEmpty(text))
                {
                    sendText(text);
                    _logger.LogDebug("Pasted {CharCount} characters from clipboard", text.Length);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to paste from clipboard");
        }
    }
}
