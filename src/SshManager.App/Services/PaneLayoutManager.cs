using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.App.Models;
using SshManager.Terminal;

namespace SshManager.App.Services;

/// <summary>
/// Manages the pane layout tree for split pane functionality.
/// Implements binary tree operations for pane splitting, closing, and navigation.
/// </summary>
public sealed class PaneLayoutManager : IPaneLayoutManager
{
    private readonly ILogger<PaneLayoutManager> _logger;
    private PaneNode? _rootNode;
    private PaneLeafNode? _focusedPane;
    private readonly List<PaneLeafNode> _tabbedPanes = new();
    private TerminalSession? _activeTabbedSession;

    // Background panes that were hidden when switching to split mode
    // These are restored when switching back to tabbed mode
    private readonly List<PaneLeafNode> _backgroundPanes = new();

    public event EventHandler? LayoutChanged;
    public event EventHandler<PaneLeafNode?>? FocusedPaneChanged;

    public PaneNode? RootNode => _rootNode;
    public PaneLeafNode? FocusedPane => _focusedPane;

    public PaneLayoutManager(ILogger<PaneLayoutManager>? logger = null)
    {
        _logger = logger ?? NullLogger<PaneLayoutManager>.Instance;
    }

    /// <inheritdoc />
    public PaneLeafNode CreateRootPane(TerminalSession? session)
    {
        var leaf = new PaneLeafNode
        {
            Session = session,
            IsPrimaryForSession = true,
            IsFocused = true
        };

        _rootNode = leaf;
        _focusedPane = leaf;

        _logger.LogDebug("Created root pane with session: {Session}", session?.Title ?? "Empty");

        LayoutChanged?.Invoke(this, EventArgs.Empty);
        FocusedPaneChanged?.Invoke(this, leaf);

        return leaf;
    }

    /// <inheritdoc />
    public PaneLeafNode SplitPane(PaneLeafNode pane, SplitOrientation orientation, TerminalSession? session)
    {
        // Check if pane is tabbed - we need to exit tabbed mode and switch to tree mode
        bool wasTabbed = pane.IsTabbed;

        if (wasTabbed)
        {
            _logger.LogDebug("Converting tabbed pane to tree mode for split");

            // Remove from tabbed list
            _tabbedPanes.Remove(pane);
            pane.IsTabbed = false;
            pane.IsVisible = true;

            // Save other tabbed panes to background so they can be restored later
            // when switching back to tabbed mode
            foreach (var tabbedPane in _tabbedPanes.ToList())
            {
                tabbedPane.IsTabbed = false;
                tabbedPane.IsVisible = false;
                _backgroundPanes.Add(tabbedPane);
            }
            _tabbedPanes.Clear();
            _activeTabbedSession = null;
        }

        // Remember the parent before creating the container (which will change pane.Parent)
        var originalParent = pane.Parent;

        var newLeaf = new PaneLeafNode
        {
            Session = session,
            IsPrimaryForSession = session != null && !IsSessionAlreadyPrimary(session),
            IsFocused = false,
            IsTabbed = false,  // Not a tabbed pane - it's part of a split
            IsVisible = true
        };

        // Ensure the original pane is also marked as non-tabbed
        pane.IsTabbed = false;
        pane.IsVisible = true;

        // Create container - NOTE: This will set pane.Parent and newLeaf.Parent to container
        var container = new PaneContainerNode
        {
            Orientation = orientation,
            First = pane,
            Second = newLeaf,
            SplitRatio = 0.5
        };

        // Now replace in tree using the ORIGINAL parent reference
        if (originalParent == null || wasTabbed)
        {
            // Pane was root (or tabbed which is treated as root) - container becomes new root
            _rootNode = container;
            container.Parent = null;
        }
        else
        {
            // Replace pane with container in the original parent
            if (originalParent.First == pane || originalParent.First?.Id == pane.Id)
            {
                originalParent.First = container;
            }
            else
            {
                originalParent.Second = container;
            }
            container.Parent = originalParent;
        }

        _logger.LogDebug("Split pane {Orientation} with new session: {Session}",
            orientation, session?.Title ?? "Empty");

        LayoutChanged?.Invoke(this, EventArgs.Empty);

        // Focus the new pane
        SetFocusedPane(newLeaf);

        return newLeaf;
    }

    /// <inheritdoc />
    public PaneLeafNode MirrorPane(PaneLeafNode pane, SplitOrientation orientation)
    {
        if (pane.Session == null)
        {
            _logger.LogWarning("Cannot mirror pane with no session");
            return pane;
        }

        // Check if pane is tabbed - we need to exit tabbed mode and switch to tree mode
        bool wasTabbed = pane.IsTabbed;

        if (wasTabbed)
        {
            _logger.LogDebug("Converting tabbed pane to tree mode for mirror");

            // Remove from tabbed list
            _tabbedPanes.Remove(pane);
            pane.IsTabbed = false;
            pane.IsVisible = true;

            // Save other tabbed panes to background so they can be restored later
            // when switching back to tabbed mode
            foreach (var tabbedPane in _tabbedPanes.ToList())
            {
                tabbedPane.IsTabbed = false;
                tabbedPane.IsVisible = false;
                _backgroundPanes.Add(tabbedPane);
            }
            _tabbedPanes.Clear();
            _activeTabbedSession = null;
        }

        // Remember the parent before creating the container (which will change pane.Parent)
        var originalParent = pane.Parent;

        var newLeaf = new PaneLeafNode
        {
            Session = pane.Session,
            IsPrimaryForSession = false, // Original pane remains primary
            IsFocused = false,
            IsTabbed = false,  // Not a tabbed pane - it's part of a split
            IsVisible = true
        };

        // Ensure the original pane is also marked as non-tabbed
        pane.IsTabbed = false;
        pane.IsVisible = true;

        // Create container - NOTE: This will set pane.Parent and newLeaf.Parent to container
        var container = new PaneContainerNode
        {
            Orientation = orientation,
            First = pane,
            Second = newLeaf,
            SplitRatio = 0.5
        };

        // Now replace in tree using the ORIGINAL parent reference
        if (originalParent == null || wasTabbed)
        {
            // Pane was root (or tabbed which is treated as root) - container becomes new root
            _rootNode = container;
            container.Parent = null;
        }
        else
        {
            // Replace pane with container in the original parent
            if (originalParent.First == pane || originalParent.First?.Id == pane.Id)
            {
                originalParent.First = container;
            }
            else
            {
                originalParent.Second = container;
            }
            container.Parent = originalParent;
        }

        _logger.LogDebug("Mirrored pane for session: {Session}", pane.Session.Title);

        LayoutChanged?.Invoke(this, EventArgs.Empty);

        // Focus the new mirrored pane
        SetFocusedPane(newLeaf);

        return newLeaf;
    }

    /// <inheritdoc />
    public void ClosePane(PaneLeafNode pane)
    {
        // Handle tabbed panes separately
        if (pane.IsTabbed && _tabbedPanes.Contains(pane))
        {
            _tabbedPanes.Remove(pane);

            // If this was the active session, switch to another
            if (_activeTabbedSession == pane.Session && _tabbedPanes.Count > 0)
            {
                var nextPane = _tabbedPanes.LastOrDefault();
                if (nextPane?.Session != null)
                {
                    SetActiveTabbedSession(nextPane.Session);
                }
            }

            // Reset root if no tabbed panes left
            if (_tabbedPanes.Count == 0)
            {
                _rootNode = null;
                _focusedPane = null;
                _activeTabbedSession = null;
                _logger.LogDebug("Closed last tabbed pane, layout is now empty");
            }

            LayoutChanged?.Invoke(this, EventArgs.Empty);
            FocusedPaneChanged?.Invoke(this, _focusedPane);
            return;
        }

        if (_rootNode == pane)
        {
            // Closing the only pane - reset layout
            _rootNode = null;
            _focusedPane = null;

            _logger.LogDebug("Closed last pane, layout is now empty");

            LayoutChanged?.Invoke(this, EventArgs.Empty);
            FocusedPaneChanged?.Invoke(this, null);
            return;
        }

        var parent = pane.Parent;
        if (parent == null)
        {
            _logger.LogWarning("Pane has no parent but is not root");
            return;
        }

        // Find the sibling
        var sibling = parent.First == pane ? parent.Second : parent.First;

        // Update primary pane if needed
        if (pane.IsPrimaryForSession && pane.Session != null)
        {
            UpdatePrimaryPaneForSession(pane.Session);
        }

        // Replace parent with sibling in the tree
        ReplacePaneInTree(parent, sibling);

        _logger.LogDebug("Closed pane, promoted sibling");

        // If closed pane was focused, focus the sibling or first leaf
        if (_focusedPane == pane)
        {
            var newFocus = sibling is PaneLeafNode leaf ? leaf : GetAllLeafNodes().FirstOrDefault();
            if (newFocus != null)
            {
                SetFocusedPane(newFocus);
            }
        }

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void SetFocusedPane(PaneLeafNode pane)
    {
        if (_focusedPane == pane)
            return;

        if (_focusedPane != null)
            _focusedPane.IsFocused = false;

        _focusedPane = pane;

        if (pane != null)
            pane.IsFocused = true;

        _logger.LogDebug("Focus changed to pane: {Session}", pane?.Session?.Title ?? "Empty");

        FocusedPaneChanged?.Invoke(this, pane);
    }

    /// <inheritdoc />
    public void NavigateFocus(NavigationDirection direction)
    {
        if (_focusedPane == null || _rootNode == null)
            return;

        var allLeaves = GetAllLeafNodes().ToList();
        if (allLeaves.Count <= 1)
            return;

        // For simplicity, use linear navigation based on direction
        // Left/Up goes to previous, Right/Down goes to next
        var currentIndex = allLeaves.IndexOf(_focusedPane);
        if (currentIndex < 0)
            return;

        int newIndex;
        switch (direction)
        {
            case NavigationDirection.Left:
            case NavigationDirection.Up:
                newIndex = currentIndex > 0 ? currentIndex - 1 : allLeaves.Count - 1;
                break;
            case NavigationDirection.Right:
            case NavigationDirection.Down:
                newIndex = currentIndex < allLeaves.Count - 1 ? currentIndex + 1 : 0;
                break;
            default:
                return;
        }

        SetFocusedPane(allLeaves[newIndex]);
    }

    /// <inheritdoc />
    public void CycleFocusNext()
    {
        NavigateFocus(NavigationDirection.Right);
    }

    /// <inheritdoc />
    public void CycleFocusPrevious()
    {
        NavigateFocus(NavigationDirection.Left);
    }

    /// <inheritdoc />
    public IEnumerable<PaneLeafNode> GetAllLeafNodes()
    {
        if (_rootNode == null)
            yield break;

        foreach (var leaf in GetLeafNodesRecursive(_rootNode))
        {
            yield return leaf;
        }
    }

    /// <inheritdoc />
    public void ResetLayout()
    {
        _rootNode = null;
        _focusedPane = null;
        _tabbedPanes.Clear();
        _backgroundPanes.Clear();
        _activeTabbedSession = null;

        _logger.LogDebug("Layout reset");

        LayoutChanged?.Invoke(this, EventArgs.Empty);
        FocusedPaneChanged?.Invoke(this, null);
    }

    /// <inheritdoc />
    public bool AssignSessionToFocusedPane(TerminalSession session)
    {
        if (_focusedPane == null)
        {
            // No panes exist, create root
            CreateRootPane(session);
            return true;
        }

        if (_focusedPane.Session != null)
        {
            _logger.LogDebug("Focused pane already has a session");
            return false;
        }

        _focusedPane.Session = session;
        _focusedPane.IsPrimaryForSession = !IsSessionAlreadyPrimary(session);

        _logger.LogDebug("Assigned session {Session} to focused pane", session.Title);

        LayoutChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    /// <inheritdoc />
    public IEnumerable<PaneLeafNode> FindPanesForSession(TerminalSession session)
    {
        return GetAllLeafNodes().Where(p => p.Session == session);
    }

    /// <inheritdoc />
    public void UpdatePrimaryPaneForSession(TerminalSession session)
    {
        var panes = FindPanesForSession(session).ToList();

        // If no pane is primary, make the first one primary
        if (!panes.Any(p => p.IsPrimaryForSession))
        {
            var firstPane = panes.FirstOrDefault();
            if (firstPane != null)
            {
                firstPane.IsPrimaryForSession = true;
                _logger.LogDebug("Updated primary pane for session: {Session}", session.Title);
            }
        }
    }

    private bool IsSessionAlreadyPrimary(TerminalSession session)
    {
        return GetAllLeafNodes().Any(p => p.Session == session && p.IsPrimaryForSession);
    }

    private void ReplacePaneInTree(PaneNode oldNode, PaneNode newNode)
    {
        var parent = oldNode.Parent;

        if (parent == null)
        {
            // oldNode is the root
            _rootNode = newNode;
            newNode.Parent = null;
        }
        else
        {
            // Replace in parent
            if (parent.First == oldNode)
            {
                parent.First = newNode;
            }
            else
            {
                parent.Second = newNode;
            }
            newNode.Parent = parent;
        }

        oldNode.Parent = null;
    }

    private static IEnumerable<PaneLeafNode> GetLeafNodesRecursive(PaneNode node)
    {
        switch (node)
        {
            case PaneLeafNode leaf:
                yield return leaf;
                break;
            case PaneContainerNode container:
                foreach (var leaf in GetLeafNodesRecursive(container.First))
                    yield return leaf;
                foreach (var leaf in GetLeafNodesRecursive(container.Second))
                    yield return leaf;
                break;
        }
    }

    /// <inheritdoc />
    public PaneLeafNode CreateTabbedPane(TerminalSession session)
    {
        // If we're in tree/split mode (no tabbed panes but have a root node),
        // convert all existing tree panes back to tabbed mode first
        if (_tabbedPanes.Count == 0 && _rootNode != null)
        {
            _logger.LogDebug("Converting tree mode back to tabbed mode");

            // Collect all existing leaf nodes from the tree
            var existingLeaves = GetAllLeafNodes().ToList();

            // Convert them to tabbed panes, but only keep one pane per session
            // (mirrored panes share the same session, so we deduplicate)
            var seenSessions = new HashSet<TerminalSession>();
            foreach (var existingLeaf in existingLeaves)
            {
                if (existingLeaf.Session == null || seenSessions.Contains(existingLeaf.Session))
                {
                    // Skip duplicate session panes (mirrors) or empty panes
                    continue;
                }

                seenSessions.Add(existingLeaf.Session);
                existingLeaf.IsTabbed = true;
                existingLeaf.IsVisible = false;
                existingLeaf.IsFocused = false;
                existingLeaf.IsPrimaryForSession = true; // This is now the only pane for this session
                existingLeaf.Parent = null; // Detach from tree structure
                _tabbedPanes.Add(existingLeaf);
            }

            // Also restore background panes (other tabs that were hidden when switching to split mode)
            foreach (var backgroundPane in _backgroundPanes)
            {
                if (backgroundPane.Session == null || seenSessions.Contains(backgroundPane.Session))
                {
                    // Skip duplicate session panes or empty panes
                    continue;
                }

                seenSessions.Add(backgroundPane.Session);
                backgroundPane.IsTabbed = true;
                backgroundPane.IsVisible = false;
                backgroundPane.IsFocused = false;
                backgroundPane.IsPrimaryForSession = true;
                _tabbedPanes.Add(backgroundPane);
            }
            _backgroundPanes.Clear();

            // Clear the tree structure
            _rootNode = null;
        }

        var leaf = new PaneLeafNode
        {
            Session = session,
            IsPrimaryForSession = true,
            IsFocused = true,
            IsTabbed = true,
            IsVisible = true
        };

        // Hide all other tabbed panes
        foreach (var existingPane in _tabbedPanes)
        {
            existingPane.IsVisible = false;
            existingPane.IsFocused = false;
        }

        _tabbedPanes.Add(leaf);
        _activeTabbedSession = session;

        // Set as root if no root exists
        if (_rootNode == null)
        {
            _rootNode = leaf;
        }

        _focusedPane = leaf;

        _logger.LogDebug("Created tabbed pane for session: {Session}, total tabbed panes: {Count}",
            session.Title, _tabbedPanes.Count);

        LayoutChanged?.Invoke(this, EventArgs.Empty);
        FocusedPaneChanged?.Invoke(this, leaf);

        return leaf;
    }

    /// <inheritdoc />
    public void SetActiveTabbedSession(TerminalSession session)
    {
        if (_activeTabbedSession == session)
            return;

        // If there are no tabbed panes (we're in split mode), handle switching differently
        if (_tabbedPanes.Count == 0)
        {
            _activeTabbedSession = session;

            // Check if the session is already visible in one of the tree panes
            var existingPane = GetAllLeafNodes().FirstOrDefault(p => p.Session == session);
            if (existingPane != null)
            {
                // Session is already visible, just focus that pane
                SetFocusedPane(existingPane);
                _logger.LogDebug("Focused existing pane for session (split mode): {Session}", session.Title);
                return;
            }

            // Session is not visible (it's in background panes)
            // Restore to tabbed mode with the selected session active
            var backgroundPane = _backgroundPanes.FirstOrDefault(p => p.Session == session);
            if (backgroundPane != null)
            {
                _logger.LogDebug("Restoring tabbed mode to show background session: {Session}", session.Title);

                // Convert all current tree panes to tabbed mode
                var existingLeaves = GetAllLeafNodes().ToList();
                var seenSessions = new HashSet<TerminalSession>();

                foreach (var existingLeaf in existingLeaves)
                {
                    if (existingLeaf.Session == null || seenSessions.Contains(existingLeaf.Session))
                        continue;

                    seenSessions.Add(existingLeaf.Session);
                    existingLeaf.IsTabbed = true;
                    existingLeaf.IsVisible = false;
                    existingLeaf.IsFocused = false;
                    existingLeaf.IsPrimaryForSession = true;
                    existingLeaf.Parent = null;
                    _tabbedPanes.Add(existingLeaf);
                }

                // Add all background panes back to tabbed mode
                foreach (var bgPane in _backgroundPanes.ToList())
                {
                    if (bgPane.Session == null || seenSessions.Contains(bgPane.Session))
                        continue;

                    seenSessions.Add(bgPane.Session);
                    bgPane.IsTabbed = true;
                    bgPane.IsVisible = false;
                    bgPane.IsFocused = false;
                    bgPane.IsPrimaryForSession = true;
                    _tabbedPanes.Add(bgPane);
                }
                _backgroundPanes.Clear();

                // Clear tree structure
                _rootNode = null;

                // Now show the selected session
                var targetPane = _tabbedPanes.FirstOrDefault(p => p.Session == session);
                if (targetPane != null)
                {
                    targetPane.IsVisible = true;
                    targetPane.IsFocused = true;
                    _focusedPane = targetPane;
                }

                LayoutChanged?.Invoke(this, EventArgs.Empty);
                FocusedPaneChanged?.Invoke(this, _focusedPane);
                return;
            }

            _logger.LogDebug("Set active session (split mode): {Session}", session.Title);
            return;
        }

        _activeTabbedSession = session;

        // Update visibility for all tabbed panes
        PaneLeafNode? activePane = null;
        foreach (var pane in _tabbedPanes)
        {
            var shouldBeVisible = pane.Session == session;
            pane.IsVisible = shouldBeVisible;

            if (shouldBeVisible)
            {
                activePane = pane;
            }
        }

        // Focus the active pane
        if (activePane != null)
        {
            SetFocusedPane(activePane);
        }

        _logger.LogDebug("Set active tabbed session: {Session}", session.Title);

        LayoutChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public IEnumerable<PaneLeafNode> GetTabbedPanes()
    {
        return _tabbedPanes.AsReadOnly();
    }
}
