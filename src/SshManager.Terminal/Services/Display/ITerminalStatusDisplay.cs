using System.Windows.Controls;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service interface for managing terminal status overlay display.
/// </summary>
/// <remarks>
/// This service handles the visual presentation of connection status messages
/// (connecting, disconnected, errors, reconnection attempts) by controlling
/// status overlay visibility, message text, and progress indicators.
/// </remarks>
public interface ITerminalStatusDisplay
{
    /// <summary>
    /// Gets whether the status overlay is currently visible.
    /// </summary>
    bool IsVisible { get; }

    /// <summary>
    /// Gets the current status message being displayed.
    /// </summary>
    string CurrentMessage { get; }

    /// <summary>
    /// Initializes the status display with references to UI elements.
    /// Must be called before using any other methods.
    /// </summary>
    /// <param name="statusOverlay">The border element that contains the status display.</param>
    /// <param name="statusText">The text block that displays the status message.</param>
    /// <param name="statusProgress">The progress bar that shows connection progress.</param>
    void Initialize(Border statusOverlay, TextBlock statusText, ProgressBar statusProgress);

    /// <summary>
    /// Shows the status overlay with a connecting message and progress indicator.
    /// </summary>
    /// <param name="message">The message to display (default: "Connecting...").</param>
    void ShowConnecting(string message = "Connecting...");

    /// <summary>
    /// Shows the status overlay with a disconnected message (no progress indicator).
    /// </summary>
    /// <param name="message">The message to display (default: "Disconnected").</param>
    void ShowDisconnected(string message = "Disconnected");

    /// <summary>
    /// Shows the status overlay with an error message (no progress indicator).
    /// </summary>
    /// <param name="message">The error message to display.</param>
    void ShowError(string message);

    /// <summary>
    /// Shows the status overlay with a reconnection attempt message and progress indicator.
    /// </summary>
    /// <param name="attempt">Current reconnection attempt number.</param>
    /// <param name="maxAttempts">Maximum number of reconnection attempts.</param>
    void ShowReconnecting(int attempt, int maxAttempts);

    /// <summary>
    /// Hides the status overlay.
    /// </summary>
    void Hide();
}
