using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Terminal.Controls;
using SshManager.Terminal.Utilities;

namespace SshManager.Terminal.Services.Search;

/// <summary>
/// Coordinates terminal search operations including overlay visibility and result navigation.
/// This service manages the search UI lifecycle and delegates actual searching to TerminalTextSearchService.
/// </summary>
public sealed class TerminalSearchCoordinator : ITerminalSearchCoordinator
{
    private readonly TerminalFindOverlay _findOverlay;
    private readonly Action _returnFocus;
    private readonly ILogger<TerminalSearchCoordinator> _logger;
    private TerminalTextSearchService? _searchService;

    /// <summary>
    /// Event raised when the search overlay is closed.
    /// </summary>
    public event EventHandler? SearchClosed;

    /// <summary>
    /// Event raised when search results change (for terminal refresh).
    /// </summary>
    public event EventHandler? ResultsChanged;

    /// <summary>
    /// Gets whether the search overlay is currently visible.
    /// </summary>
    public bool IsSearchVisible => _findOverlay.Visibility == System.Windows.Visibility.Visible;

    /// <summary>
    /// Creates a new instance of the search coordinator.
    /// </summary>
    /// <param name="findOverlay">The find overlay UI control.</param>
    /// <param name="returnFocus">Action to call when returning focus to the terminal.</param>
    /// <param name="logger">Optional logger instance.</param>
    public TerminalSearchCoordinator(
        TerminalFindOverlay findOverlay,
        Action returnFocus,
        ILogger<TerminalSearchCoordinator>? logger = null)
    {
        _findOverlay = findOverlay ?? throw new ArgumentNullException(nameof(findOverlay));
        _returnFocus = returnFocus ?? throw new ArgumentNullException(nameof(returnFocus));
        _logger = logger ?? NullLogger<TerminalSearchCoordinator>.Instance;

        // Wire up overlay events
        _findOverlay.CloseRequested += OnFindOverlayCloseRequested;
        _findOverlay.NavigateToLine += OnFindOverlayNavigateToLine;
        _findOverlay.SearchResultsChanged += OnFindOverlaySearchResultsChanged;
    }

    /// <summary>
    /// Initializes the search coordinator with the terminal output buffer.
    /// </summary>
    /// <param name="outputBuffer">The terminal output buffer to search.</param>
    public void Initialize(TerminalOutputBuffer outputBuffer)
    {
        if (outputBuffer == null)
        {
            throw new ArgumentNullException(nameof(outputBuffer));
        }

        // Create search service with the output buffer
        _searchService = new TerminalTextSearchService(outputBuffer);

        // Set search service on the overlay
        _findOverlay.SetSearchService(_searchService);

        _logger.LogDebug("Search coordinator initialized with output buffer");
    }

    /// <summary>
    /// Shows the search overlay and focuses the search input.
    /// </summary>
    public void ShowSearch()
    {
        _findOverlay.Show();
        _logger.LogDebug("Search overlay shown");
    }

    /// <summary>
    /// Hides the search overlay and clears search results.
    /// </summary>
    public void HideSearch()
    {
        _findOverlay.Hide();
        _returnFocus();
        _logger.LogDebug("Search overlay hidden");
    }

    /// <summary>
    /// Navigates to a specific search result line in the terminal.
    /// </summary>
    /// <param name="lineIndex">The line index to navigate to.</param>
    public void NavigateToResult(int lineIndex)
    {
        // Note: xterm.js manages its own scrollback.
        // The search match is indicated in the overlay.
        _logger.LogDebug("Navigate to line {LineIndex} requested", lineIndex);
    }

    private void OnFindOverlayCloseRequested(object? sender, EventArgs e)
    {
        HideSearch();
        SearchClosed?.Invoke(this, EventArgs.Empty);
    }

    private void OnFindOverlayNavigateToLine(object? sender, int lineIndex)
    {
        NavigateToResult(lineIndex);
    }

    private void OnFindOverlaySearchResultsChanged(object? sender, EventArgs e)
    {
        // Search results changed - terminal automatically highlights matches
        ResultsChanged?.Invoke(this, EventArgs.Empty);
    }
}
