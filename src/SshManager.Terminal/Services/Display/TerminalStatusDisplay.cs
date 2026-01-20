using System.Windows;
using System.Windows.Controls;

namespace SshManager.Terminal.Services;

/// <summary>
/// Default implementation of <see cref="ITerminalStatusDisplay"/> that manages
/// status overlay visibility and message display.
/// </summary>
/// <remarks>
/// This service controls three UI elements:
/// <list type="bullet">
/// <item><description>StatusOverlay: Border element containing the status message</description></item>
/// <item><description>StatusText: TextBlock displaying the status message</description></item>
/// <item><description>StatusProgress: ProgressBar showing indeterminate progress</description></item>
/// </list>
/// The service must be initialized with references to these UI elements before use.
/// Thread safety: All methods must be called on the UI thread.
/// </remarks>
public sealed class TerminalStatusDisplay : ITerminalStatusDisplay
{
    private Border? _statusOverlay;
    private TextBlock? _statusText;
    private ProgressBar? _statusProgress;

    private bool _isVisible;
    private string _currentMessage = string.Empty;

    /// <inheritdoc />
    public bool IsVisible => _isVisible;

    /// <inheritdoc />
    public string CurrentMessage => _currentMessage;

    /// <summary>
    /// Initializes the status display with references to UI elements.
    /// </summary>
    /// <param name="statusOverlay">The border element that contains the status display.</param>
    /// <param name="statusText">The text block that displays the status message.</param>
    /// <param name="statusProgress">The progress bar that shows connection progress.</param>
    /// <exception cref="ArgumentNullException">Thrown if any parameter is null.</exception>
    public void Initialize(Border statusOverlay, TextBlock statusText, ProgressBar statusProgress)
    {
        ArgumentNullException.ThrowIfNull(statusOverlay);
        ArgumentNullException.ThrowIfNull(statusText);
        ArgumentNullException.ThrowIfNull(statusProgress);

        _statusOverlay = statusOverlay;
        _statusText = statusText;
        _statusProgress = statusProgress;
    }

    /// <inheritdoc />
    public void ShowConnecting(string message = "Connecting...")
    {
        ShowStatus(message, showProgress: true);
    }

    /// <inheritdoc />
    public void ShowDisconnected(string message = "Disconnected")
    {
        ShowStatus(message, showProgress: false);
    }

    /// <inheritdoc />
    public void ShowError(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        ShowStatus(message, showProgress: false);
    }

    /// <inheritdoc />
    public void ShowReconnecting(int attempt, int maxAttempts)
    {
        if (attempt < 0) throw new ArgumentOutOfRangeException(nameof(attempt), "Attempt must be non-negative");
        if (maxAttempts < 0) throw new ArgumentOutOfRangeException(nameof(maxAttempts), "MaxAttempts must be non-negative");

        var message = $"Reconnecting ({attempt}/{maxAttempts})...";
        ShowStatus(message, showProgress: true);
    }

    /// <inheritdoc />
    public void Hide()
    {
        EnsureInitialized();

        _statusOverlay!.Visibility = Visibility.Collapsed;
        _isVisible = false;
    }

    /// <summary>
    /// Internal method to show status with optional progress indicator.
    /// </summary>
    /// <param name="message">The message to display.</param>
    /// <param name="showProgress">Whether to show the progress indicator.</param>
    private void ShowStatus(string message, bool showProgress)
    {
        ArgumentNullException.ThrowIfNull(message);
        EnsureInitialized();

        _currentMessage = message;
        _statusText!.Text = message;
        _statusProgress!.Visibility = showProgress ? Visibility.Visible : Visibility.Collapsed;
        _statusOverlay!.Visibility = Visibility.Visible;
        _isVisible = true;
    }

    /// <summary>
    /// Ensures the service has been initialized with UI elements.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if Initialize has not been called.</exception>
    private void EnsureInitialized()
    {
        if (_statusOverlay == null || _statusText == null || _statusProgress == null)
        {
            throw new InvalidOperationException(
                "TerminalStatusDisplay must be initialized with UI elements before use. " +
                "Call Initialize() with statusOverlay, statusText, and statusProgress parameters.");
        }
    }
}
