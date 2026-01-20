using System.Windows;
using SshManager.App.Models;
using SshManager.App.ViewModels;
using SshManager.Terminal;

namespace SshManager.App.Services;

/// <summary>
/// Event arguments for session picker operations.
/// </summary>
public class SessionPickerRequestEventArgs : EventArgs
{
    public SplitOrientation Orientation { get; }
    public PaneLeafNode? RequestingPane { get; }
    
    public SessionPickerRequestEventArgs(SplitOrientation orientation, PaneLeafNode? requestingPane = null)
    {
        Orientation = orientation;
        RequestingPane = requestingPane;
    }
}

/// <summary>
/// Orchestrates pane management, session creation, and pane-session connections.
/// Delegates to IPaneLayoutManager for layout operations.
/// </summary>
public interface IPaneOrchestrator
{
    /// <summary>
    /// Event raised when a session picker dialog should be shown.
    /// The UI layer handles showing the dialog and calls HandleSessionPickerResult.
    /// </summary>
    event EventHandler<SessionPickerRequestEventArgs>? SessionPickerRequested;
    
    /// <summary>
    /// Sets the view model reference for session creation.
    /// </summary>
    /// <param name="viewModel">The main window view model.</param>
    void SetViewModel(MainWindowViewModel viewModel);
    
    /// <summary>
    /// Handles a new session being created, adding it to a pane.
    /// </summary>
    /// <param name="session">The newly created session.</param>
    void OnSessionCreated(TerminalSession session);
    
    /// <summary>
    /// Handles a session being closed, removing associated panes.
    /// </summary>
    /// <param name="session">The closed session.</param>
    void OnSessionClosed(TerminalSession session);
    
    /// <summary>
    /// Requests a split operation with session picker.
    /// </summary>
    /// <param name="orientation">The split orientation.</param>
    /// <param name="requestingPane">The pane to split (null for focused pane).</param>
    void RequestSplit(SplitOrientation orientation, PaneLeafNode? requestingPane = null);
    
    /// <summary>
    /// Handles the result from a session picker dialog.
    /// </summary>
    /// <param name="result">The picker result.</param>
    /// <param name="orientation">The split orientation.</param>
    /// <param name="paneToSplit">The pane being split.</param>
    Task HandleSessionPickerResultAsync(SessionPickerResultData result, SplitOrientation orientation, PaneLeafNode paneToSplit);
    
    /// <summary>
    /// Mirrors the currently focused pane.
    /// </summary>
    void MirrorCurrentPane();
    
    /// <summary>
    /// Closes the currently focused pane.
    /// </summary>
    void CloseCurrentPane();
    
    /// <summary>
    /// Handles focus change from pane to sync with session selection.
    /// </summary>
    /// <param name="focusedPane">The newly focused pane.</param>
    void OnFocusedPaneChanged(PaneLeafNode? focusedPane);
    
    /// <summary>
    /// Handles session tab selection changes.
    /// </summary>
    /// <param name="session">The selected session.</param>
    /// <param name="isSyncFromPaneFocus">Whether this change originated from pane focus.</param>
    void OnSessionTabSelected(TerminalSession? session, bool isSyncFromPaneFocus);
    
    /// <summary>
    /// Connects a pane to its session (SSH or Serial).
    /// </summary>
    /// <param name="pane">The pane to connect.</param>
    /// <param name="session">The session to connect.</param>
    /// <param name="getPaneControlFunc">Function to get the pane control for a pane node.</param>
    Task ConnectPaneToSessionAsync(PaneLeafNode pane, TerminalSession session, Func<PaneLeafNode, ITerminalPaneTarget?> getPaneControlFunc);
    
    /// <summary>
    /// Handles pane close request from container.
    /// </summary>
    /// <param name="pane">The pane to close.</param>
    Task OnPaneCloseRequestedAsync(PaneLeafNode pane);
    
    /// <summary>
    /// Handles session disconnection (e.g., VM reboot).
    /// </summary>
    /// <param name="session">The disconnected session.</param>
    Task OnSessionDisconnectedAsync(TerminalSession session);
}
