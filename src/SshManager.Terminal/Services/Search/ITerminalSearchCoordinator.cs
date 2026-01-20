using SshManager.Terminal.Utilities;

namespace SshManager.Terminal.Services.Search;

/// <summary>
/// Coordinates terminal search operations including overlay visibility and result navigation.
/// Extracted from SshTerminalControl to reduce complexity and improve testability.
/// </summary>
public interface ITerminalSearchCoordinator
{
    /// <summary>
    /// Gets whether the search overlay is currently visible.
    /// </summary>
    bool IsSearchVisible { get; }

    /// <summary>
    /// Initializes the search coordinator with the terminal output buffer.
    /// </summary>
    /// <param name="outputBuffer">The terminal output buffer to search.</param>
    void Initialize(TerminalOutputBuffer outputBuffer);

    /// <summary>
    /// Shows the search overlay and focuses the search input.
    /// </summary>
    void ShowSearch();

    /// <summary>
    /// Hides the search overlay and clears search results.
    /// </summary>
    void HideSearch();

    /// <summary>
    /// Navigates to a specific search result line in the terminal.
    /// </summary>
    /// <param name="lineIndex">The line index to navigate to.</param>
    void NavigateToResult(int lineIndex);

    /// <summary>
    /// Event raised when the search overlay is closed.
    /// </summary>
    event EventHandler? SearchClosed;

    /// <summary>
    /// Event raised when search results change (for terminal refresh).
    /// </summary>
    event EventHandler? ResultsChanged;
}
