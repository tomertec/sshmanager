using CommunityToolkit.Mvvm.ComponentModel;
using SshManager.Terminal;

namespace SshManager.App.Models;

/// <summary>
/// Split orientation for container pane nodes.
/// </summary>
public enum SplitOrientation
{
    /// <summary>
    /// Children arranged left-to-right (vertical splitter line).
    /// </summary>
    Vertical,

    /// <summary>
    /// Children arranged top-to-bottom (horizontal splitter line).
    /// </summary>
    Horizontal
}

/// <summary>
/// Navigation direction for pane focus.
/// </summary>
public enum NavigationDirection
{
    Left,
    Right,
    Up,
    Down
}

/// <summary>
/// Represents a node in the pane layout tree.
/// Can be either a leaf node (terminal pane) or a container node (split).
/// </summary>
public abstract partial class PaneNode : ObservableObject
{
    /// <summary>
    /// Unique identifier for this pane node.
    /// </summary>
    public Guid Id { get; } = Guid.NewGuid();

    /// <summary>
    /// Parent node in the tree (null for root).
    /// </summary>
    public PaneContainerNode? Parent { get; set; }

    /// <summary>
    /// Whether this node is currently focused.
    /// </summary>
    [ObservableProperty]
    private bool _isFocused;
}

/// <summary>
/// A leaf node containing a terminal pane.
/// </summary>
public sealed partial class PaneLeafNode : PaneNode
{
    /// <summary>
    /// The terminal session displayed in this pane.
    /// Multiple panes can share the same session (mirroring).
    /// </summary>
    [ObservableProperty]
    private TerminalSession? _session;

    /// <summary>
    /// Whether this pane is the "primary" pane for the session.
    /// Only the primary pane sends resize notifications to SSH.
    /// </summary>
    [ObservableProperty]
    private bool _isPrimaryForSession = true;

    /// <summary>
    /// Whether this pane is a "tabbed" pane (one per session) vs a split pane.
    /// Tabbed panes use visibility switching instead of split layout.
    /// </summary>
    [ObservableProperty]
    private bool _isTabbed = true;

    /// <summary>
    /// Whether this pane should be visible.
    /// For tabbed panes, only the active session's pane is visible.
    /// </summary>
    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>
    /// Display title for the pane header.
    /// Returns session title or "Empty" if no session.
    /// </summary>
    public string DisplayTitle => Session?.Title ?? "Empty";

    /// <summary>
    /// Whether this pane contains a serial session (for UI styling).
    /// Uses Host.ConnectionType which is available immediately, unlike SerialConnection.
    /// </summary>
    public bool IsSerialSession => Session?.Host?.ConnectionType == Core.Models.ConnectionType.Serial;

    partial void OnSessionChanged(TerminalSession? value)
    {
        OnPropertyChanged(nameof(DisplayTitle));
        OnPropertyChanged(nameof(IsSerialSession));
    }
}

/// <summary>
/// A container node representing a horizontal or vertical split.
/// </summary>
public sealed partial class PaneContainerNode : PaneNode
{
    /// <summary>
    /// The split orientation.
    /// </summary>
    [ObservableProperty]
    private SplitOrientation _orientation;

    /// <summary>
    /// First child (left or top depending on orientation).
    /// </summary>
    [ObservableProperty]
    private PaneNode _first = null!;

    /// <summary>
    /// Second child (right or bottom depending on orientation).
    /// </summary>
    [ObservableProperty]
    private PaneNode _second = null!;

    /// <summary>
    /// Proportion of space allocated to first child (0.0 to 1.0).
    /// Default is 0.5 for equal split.
    /// </summary>
    [ObservableProperty]
    private double _splitRatio = 0.5;

    partial void OnFirstChanged(PaneNode value)
    {
        if (value != null)
            value.Parent = this;
    }

    partial void OnSecondChanged(PaneNode value)
    {
        if (value != null)
            value.Parent = this;
    }
}
