using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SshManager.Terminal.Services;

namespace SshManager.Terminal.Controls;

/// <summary>
/// Overlay control for finding text in terminal scrollback buffer.
/// Uses the text-based TerminalTextSearchService.
/// </summary>
public partial class TerminalFindOverlay : UserControl
{
    private TerminalTextSearchService? _searchService;

    /// <summary>
    /// Fired when search results change (for renderer refresh).
    /// </summary>
    public event EventHandler? SearchResultsChanged;

    /// <summary>
    /// Fired when the overlay should be closed.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Fired when navigation to a specific line is needed.
    /// </summary>
    public event EventHandler<int>? NavigateToLine;

    public TerminalFindOverlay()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Sets the search service.
    /// </summary>
    public void SetSearchService(TerminalTextSearchService? searchService)
    {
        _searchService = searchService;
    }

    /// <summary>
    /// Shows the overlay and focuses the search box.
    /// </summary>
    public void Show()
    {
        Visibility = Visibility.Visible;
        SearchTextBox.Focus();
        SearchTextBox.SelectAll();
    }

    /// <summary>
    /// Hides the overlay and clears search.
    /// </summary>
    public void Hide()
    {
        Visibility = Visibility.Collapsed;
        _searchService?.ClearSearch();
        SearchResultsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        PerformSearch();
    }

    private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                GoToNextMatch();
                e.Handled = true;
                break;

            case Key.F3 when !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                GoToNextMatch();
                e.Handled = true;
                break;

            case Key.F3 when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                GoToPreviousMatch();
                e.Handled = true;
                break;

            case Key.Escape:
                CloseRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;
        }
    }

    private void UserControl_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
            e.Handled = true;
        }
    }

    private void PreviousButton_Click(object sender, RoutedEventArgs e)
    {
        GoToPreviousMatch();
    }

    private void NextButton_Click(object sender, RoutedEventArgs e)
    {
        GoToNextMatch();
    }

    private void CaseSensitiveToggle_Click(object sender, RoutedEventArgs e)
    {
        PerformSearch();
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void PerformSearch()
    {
        if (_searchService == null) return;

        var searchTerm = SearchTextBox.Text;
        var caseSensitive = CaseSensitiveToggle.IsChecked == true;

        _searchService.Search(searchTerm, caseSensitive);
        UpdateMatchDisplay();
        SearchResultsChanged?.Invoke(this, EventArgs.Empty);

        // Navigate to current match
        var currentMatch = _searchService.CurrentMatch;
        if (currentMatch != null)
        {
            NavigateToLine?.Invoke(this, currentMatch.LineIndex);
        }
    }

    private void GoToNextMatch()
    {
        if (_searchService == null) return;

        if (_searchService.NextMatch())
        {
            UpdateMatchDisplay();
            SearchResultsChanged?.Invoke(this, EventArgs.Empty);

            var currentMatch = _searchService.CurrentMatch;
            if (currentMatch != null)
            {
                NavigateToLine?.Invoke(this, currentMatch.LineIndex);
            }
        }
    }

    private void GoToPreviousMatch()
    {
        if (_searchService == null) return;

        if (_searchService.PreviousMatch())
        {
            UpdateMatchDisplay();
            SearchResultsChanged?.Invoke(this, EventArgs.Empty);

            var currentMatch = _searchService.CurrentMatch;
            if (currentMatch != null)
            {
                NavigateToLine?.Invoke(this, currentMatch.LineIndex);
            }
        }
    }

    private void UpdateMatchDisplay()
    {
        int count = _searchService?.MatchCount ?? 0;
        int current = count > 0 ? (_searchService?.CurrentMatchIndex ?? -1) + 1 : 0;

        MatchCountText.Text = $"{current}/{count}";
        PreviousButton.IsEnabled = count > 0;
        NextButton.IsEnabled = count > 0;
    }
}
