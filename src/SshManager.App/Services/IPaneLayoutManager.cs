using SshManager.App.Models;
using SshManager.Terminal;

namespace SshManager.App.Services;

/// <summary>
/// Manages the pane layout tree for split pane functionality.
/// </summary>
public interface IPaneLayoutManager
{
    /// <summary>
    /// The root node of the pane tree. Null when no panes exist.
    /// </summary>
    PaneNode? RootNode { get; }

    /// <summary>
    /// The currently focused pane leaf node.
    /// </summary>
    PaneLeafNode? FocusedPane { get; }

    /// <summary>
    /// Event raised when the pane tree structure changes.
    /// </summary>
    event EventHandler? LayoutChanged;

    /// <summary>
    /// Event raised when focus changes to a different pane.
    /// </summary>
    event EventHandler<PaneLeafNode?>? FocusedPaneChanged;

    /// <summary>
    /// Creates the initial root pane with the specified session.
    /// </summary>
    /// <param name="session">The session to display in the root pane.</param>
    /// <returns>The created root pane.</returns>
    PaneLeafNode CreateRootPane(TerminalSession? session);

    /// <summary>
    /// Splits the specified pane, creating a new pane with the given session.
    /// </summary>
    /// <param name="pane">The pane to split.</param>
    /// <param name="orientation">Split orientation.</param>
    /// <param name="session">Session for the new pane (null for empty pane).</param>
    /// <returns>The newly created pane.</returns>
    PaneLeafNode SplitPane(PaneLeafNode pane, SplitOrientation orientation, TerminalSession? session);

    /// <summary>
    /// Mirrors the current session into a new split pane.
    /// The new pane will display the same session as the original pane.
    /// </summary>
    /// <param name="pane">The pane to mirror.</param>
    /// <param name="orientation">Split orientation.</param>
    /// <returns>The newly created mirrored pane.</returns>
    PaneLeafNode MirrorPane(PaneLeafNode pane, SplitOrientation orientation);

    /// <summary>
    /// Closes the specified pane and collapses the tree if needed.
    /// </summary>
    /// <param name="pane">The pane to close.</param>
    void ClosePane(PaneLeafNode pane);

    /// <summary>
    /// Sets focus to the specified pane.
    /// </summary>
    /// <param name="pane">The pane to focus.</param>
    void SetFocusedPane(PaneLeafNode pane);

    /// <summary>
    /// Navigates focus in the specified direction.
    /// </summary>
    /// <param name="direction">The direction to navigate.</param>
    void NavigateFocus(NavigationDirection direction);

    /// <summary>
    /// Cycles focus to the next pane in order.
    /// </summary>
    void CycleFocusNext();

    /// <summary>
    /// Cycles focus to the previous pane in order.
    /// </summary>
    void CycleFocusPrevious();

    /// <summary>
    /// Gets all leaf nodes in the tree in order.
    /// </summary>
    /// <returns>All leaf nodes.</returns>
    IEnumerable<PaneLeafNode> GetAllLeafNodes();

    /// <summary>
    /// Resets the layout to a single pane.
    /// </summary>
    void ResetLayout();

    /// <summary>
    /// Assigns a session to the focused pane if it's empty.
    /// </summary>
    /// <param name="session">The session to assign.</param>
    /// <returns>True if assigned, false if pane already has a session.</returns>
    bool AssignSessionToFocusedPane(TerminalSession session);

    /// <summary>
    /// Finds all panes displaying the specified session.
    /// </summary>
    /// <param name="session">The session to find.</param>
    /// <returns>All panes showing this session.</returns>
    IEnumerable<PaneLeafNode> FindPanesForSession(TerminalSession session);

    /// <summary>
    /// Updates primary pane assignments for a session when a pane is closed.
    /// </summary>
    /// <param name="session">The session to update.</param>
    void UpdatePrimaryPaneForSession(TerminalSession session);

    /// <summary>
    /// Creates a tabbed pane for a session (stacked with other tabbed panes).
    /// Only one tabbed pane is visible at a time.
    /// </summary>
    /// <param name="session">The session for the new pane.</param>
    /// <returns>The created pane.</returns>
    PaneLeafNode CreateTabbedPane(TerminalSession session);

    /// <summary>
    /// Sets the active tabbed session, updating visibility of all tabbed panes.
    /// </summary>
    /// <param name="session">The session to make active.</param>
    void SetActiveTabbedSession(TerminalSession session);

    /// <summary>
    /// Gets all tabbed panes (panes that use visibility switching).
    /// </summary>
    IEnumerable<PaneLeafNode> GetTabbedPanes();
}
