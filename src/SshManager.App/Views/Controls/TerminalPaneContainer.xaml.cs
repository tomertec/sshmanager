using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SshManager.App.Models;
using SshManager.App.Services;
using SshManager.Terminal;

namespace SshManager.App.Views.Controls;

/// <summary>
/// Container control that renders the pane tree using nested Grid layouts.
/// </summary>
public partial class TerminalPaneContainer : UserControl
{
    private IPaneLayoutManager? _layoutManager;
    private IServiceProvider? _serviceProvider;
    private readonly Dictionary<Guid, TerminalPane> _paneControls = new();

    /// <summary>
    /// Event raised when a pane requests a split operation.
    /// </summary>
    public event EventHandler<PaneSplitRequestedEventArgs>? PaneSplitRequested;

    /// <summary>
    /// Event raised when a pane requests to be closed.
    /// </summary>
    public event EventHandler<PaneLeafNode>? PaneCloseRequested;

    /// <summary>
    /// Event raised when a session is disconnected (remote disconnect, error, etc.).
    /// </summary>
    public event EventHandler<TerminalSession>? SessionDisconnected;

    public TerminalPaneContainer()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    /// <summary>
    /// Sets the pane layout manager. Must be called before the control is loaded.
    /// </summary>
    /// <param name="layoutManager">The pane layout manager.</param>
    public void SetLayoutManager(IPaneLayoutManager layoutManager)
    {
        if (_layoutManager != null)
        {
            _layoutManager.LayoutChanged -= OnLayoutChanged;
        }

        _layoutManager = layoutManager;
        _layoutManager.LayoutChanged += OnLayoutChanged;
    }

    /// <summary>
    /// Sets the service provider for resolving dependencies.
    /// Must be called before the control is loaded.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_layoutManager != null)
        {
            RebuildLayout();
        }
    }

    private void OnLayoutChanged(object? sender, EventArgs e)
    {
        Dispatcher.Invoke(RebuildLayout);
    }

    private Grid? _tabbedPanesGrid;

    private void RebuildLayout()
    {
        if (_layoutManager == null)
            return;

        var tabbedPanes = _layoutManager.GetTabbedPanes().ToList();

        if (tabbedPanes.Count == 0 && _layoutManager.RootNode == null)
        {
            // Detach all pane controls from their parents before clearing
            foreach (var pane in _paneControls.Values)
            {
                EnsureDetachedFromParent(pane);
            }
            // Clear controls only when layout is truly empty
            _paneControls.Clear();
            _tabbedPanesGrid = null;
            RootPresenter.Content = null;
            WelcomePanel.Visibility = Visibility.Visible;
            return;
        }

        WelcomePanel.Visibility = Visibility.Collapsed;

        // If we have tabbed panes, render them stacked with visibility binding
        if (tabbedPanes.Count > 0)
        {
            // Check if we need to rebuild or just update visibility
            var existingPaneIds = new HashSet<Guid>(_paneControls.Keys);
            var currentPaneIds = new HashSet<Guid>(tabbedPanes.Select(p => p.Id));

            // Only rebuild if panes were added or removed
            if (!existingPaneIds.SetEquals(currentPaneIds) || _tabbedPanesGrid == null)
            {
                // CRITICAL: Clear the visual tree first to prevent composition errors
                // This must happen BEFORE detaching individual panes to ensure clean state
                RootPresenter.Content = null;

                // Clear the tabbed panes grid children if it exists
                if (_tabbedPanesGrid != null)
                {
                    _tabbedPanesGrid.Children.Clear();
                }

                // Detach all existing pane controls from any remaining parent references
                foreach (var pane in _paneControls.Values)
                {
                    EnsureDetachedFromParent(pane);
                }

                // Remove panes that no longer exist and clean up their resources
                var removedIds = existingPaneIds.Except(currentPaneIds).ToList();
                foreach (var id in removedIds)
                {
                    if (_paneControls.TryGetValue(id, out var removedPane))
                    {
                        removedPane.CleanupResources();
                    }
                    _paneControls.Remove(id);
                }

                // Reuse the grid if possible, otherwise create new one
                if (_tabbedPanesGrid == null)
                {
                    _tabbedPanesGrid = new Grid();
                }

                foreach (var leaf in tabbedPanes)
                {
                    TerminalPane pane;
                    if (_paneControls.TryGetValue(leaf.Id, out var existingPane))
                    {
                        pane = existingPane;
                    }
                    else
                    {
                        // Create new pane control only for new panes
                        pane = BuildLeafUI(leaf);
                    }

                    // Use a converter that returns Hidden instead of Collapsed
                    // This keeps the control in the visual tree
                    pane.SetBinding(UIElement.VisibilityProperty, new System.Windows.Data.Binding("IsVisible")
                    {
                        Source = leaf,
                        Converter = new BooleanToHiddenVisibilityConverter()
                    });
                    _tabbedPanesGrid.Children.Add(pane);
                }
                RootPresenter.Content = _tabbedPanesGrid;
            }
            // If panes are the same, visibility is handled by the binding - no rebuild needed
        }
        else if (_layoutManager.RootNode != null)
        {
            // Tree-based layout for non-tabbed/split mode
            // CRITICAL: Clear the visual tree first to prevent composition errors
            RootPresenter.Content = null;

            // Clear the tabbed panes grid children if it exists
            if (_tabbedPanesGrid != null)
            {
                _tabbedPanesGrid.Children.Clear();
            }

            // Detach all existing pane controls from any remaining parent references
            foreach (var pane in _paneControls.Values)
            {
                EnsureDetachedFromParent(pane);
            }

            // Track which panes we need to keep
            var neededPaneIds = new HashSet<Guid>();
            CollectLeafIds(_layoutManager.RootNode, neededPaneIds);

            // Remove panes that are no longer in the tree and clean up their resources
            var toRemove = _paneControls.Keys.Except(neededPaneIds).ToList();
            foreach (var id in toRemove)
            {
                if (_paneControls.TryGetValue(id, out var removedPane))
                {
                    removedPane.CleanupResources();
                }
                _paneControls.Remove(id);
            }

            _tabbedPanesGrid = null;
            RootPresenter.Content = BuildNodeUI(_layoutManager.RootNode);
        }
    }

    private static void CollectLeafIds(PaneNode node, HashSet<Guid> ids)
    {
        switch (node)
        {
            case PaneLeafNode leaf:
                ids.Add(leaf.Id);
                break;
            case PaneContainerNode container:
                CollectLeafIds(container.First, ids);
                CollectLeafIds(container.Second, ids);
                break;
        }
    }

    private FrameworkElement BuildNodeUI(PaneNode node)
    {
        return node switch
        {
            PaneLeafNode leaf => BuildLeafUI(leaf),
            PaneContainerNode container => BuildContainerUI(container),
            _ => throw new InvalidOperationException($"Unknown node type: {node.GetType()}")
        };
    }

    /// <summary>
    /// Ensures a pane control is fully detached from any parent before adding to a new container.
    /// This prevents "Visual is already a child of another Visual" errors.
    /// </summary>
    private static void EnsureDetachedFromParent(TerminalPane pane)
    {
        if (pane.Parent == null)
            return;

        // Handle Panel parents (Grid, StackPanel, etc.)
        if (pane.Parent is Panel parentPanel)
        {
            parentPanel.Children.Remove(pane);
        }
        // Handle ContentControl parents
        else if (pane.Parent is ContentControl contentControl)
        {
            contentControl.Content = null;
        }
        // Handle ContentPresenter parents
        else if (pane.Parent is ContentPresenter contentPresenter)
        {
            contentPresenter.Content = null;
        }
        // Handle Decorator parents (Border, etc.)
        else if (pane.Parent is Decorator decorator)
        {
            decorator.Child = null;
        }

        // Force visual tree disconnection if still connected
        // This is a safety net for edge cases
        if (pane.Parent != null)
        {
            System.Diagnostics.Debug.WriteLine(
                $"WARNING: Could not detach pane from parent of type {pane.Parent.GetType().Name}");
        }
    }

    private TerminalPane BuildLeafUI(PaneLeafNode leaf)
    {
        // Reuse existing pane control if available (preserves WebView2 state)
        if (_paneControls.TryGetValue(leaf.Id, out var existingPane))
        {
            // Update DataContext in case it changed
            existingPane.DataContext = leaf;
            return existingPane;
        }

        // Create new pane control only for new panes
        var pane = new TerminalPane
        {
            DataContext = leaf
        };

        // Set the service provider so the pane can resolve dependencies
        if (_serviceProvider != null)
        {
            pane.SetServiceProvider(_serviceProvider);
        }

        pane.SplitRequested += (s, e) =>
        {
            PaneSplitRequested?.Invoke(this, new PaneSplitRequestedEventArgs(leaf, e.Orientation));
        };

        pane.CloseRequested += (s, e) =>
        {
            PaneCloseRequested?.Invoke(this, leaf);
        };

        pane.SessionDisconnected += (s, session) =>
        {
            SessionDisconnected?.Invoke(this, session);
        };

        _paneControls[leaf.Id] = pane;

        return pane;
    }

    private Grid BuildContainerUI(PaneContainerNode container)
    {
        var grid = new Grid();

        if (container.Orientation == SplitOrientation.Horizontal)
        {
            // Top/bottom arrangement
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(container.SplitRatio, GridUnitType.Star)
            });
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            grid.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1 - container.SplitRatio, GridUnitType.Star)
            });

            var first = BuildNodeUI(container.First);
            Grid.SetRow(first, 0);
            // Ensure child is detached before adding to prevent Visual parent errors
            if (first is TerminalPane firstPane)
                EnsureDetachedFromParent(firstPane);
            grid.Children.Add(first);

            var splitter = CreateSplitter(container, isHorizontal: true);
            Grid.SetRow(splitter, 1);
            grid.Children.Add(splitter);

            var second = BuildNodeUI(container.Second);
            Grid.SetRow(second, 2);
            // Ensure child is detached before adding to prevent Visual parent errors
            if (second is TerminalPane secondPane)
                EnsureDetachedFromParent(secondPane);
            grid.Children.Add(second);
        }
        else
        {
            // Left/right arrangement
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(container.SplitRatio, GridUnitType.Star)
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            grid.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1 - container.SplitRatio, GridUnitType.Star)
            });

            var first = BuildNodeUI(container.First);
            Grid.SetColumn(first, 0);
            // Ensure child is detached before adding to prevent Visual parent errors
            if (first is TerminalPane firstPane)
                EnsureDetachedFromParent(firstPane);
            grid.Children.Add(first);

            var splitter = CreateSplitter(container, isHorizontal: false);
            Grid.SetColumn(splitter, 1);
            grid.Children.Add(splitter);

            var second = BuildNodeUI(container.Second);
            Grid.SetColumn(second, 2);
            // Ensure child is detached before adding to prevent Visual parent errors
            if (second is TerminalPane secondPane)
                EnsureDetachedFromParent(secondPane);
            grid.Children.Add(second);
        }

        return grid;
    }

    private GridSplitter CreateSplitter(PaneContainerNode container, bool isHorizontal)
    {
        var splitter = new GridSplitter
        {
            Background = Brushes.Transparent,
            HorizontalAlignment = isHorizontal ? HorizontalAlignment.Stretch : HorizontalAlignment.Center,
            VerticalAlignment = isHorizontal ? VerticalAlignment.Center : VerticalAlignment.Stretch,
            ResizeDirection = isHorizontal ? GridResizeDirection.Rows : GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext
        };

        if (isHorizontal)
        {
            splitter.Height = 6;
        }
        else
        {
            splitter.Width = 6;
        }

        // Create enhanced visual template
        var controlTemplate = CreateSplitterTemplate(isHorizontal);
        splitter.Template = controlTemplate;

        // Update split ratio when splitter is dragged
        splitter.DragCompleted += (s, e) =>
        {
            UpdateSplitRatio(container);
        };

        return splitter;
    }

    private ControlTemplate CreateSplitterTemplate(bool isHorizontal)
    {
        var template = new ControlTemplate(typeof(GridSplitter));

        // Create a grid as the root element
        var gridFactory = new FrameworkElementFactory(typeof(Grid));
        gridFactory.SetValue(Grid.BackgroundProperty, Brushes.Transparent);

        // Create the splitter border
        var borderFactory = new FrameworkElementFactory(typeof(Border));
        borderFactory.Name = "SplitterBorder";
        borderFactory.SetValue(Border.BackgroundProperty, new SolidColorBrush(Color.FromArgb(0x10, 0xFF, 0xFF, 0xFF)));
        borderFactory.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));

        if (isHorizontal)
        {
            borderFactory.SetValue(Border.HeightProperty, 4.0);
            borderFactory.SetValue(Border.HorizontalAlignmentProperty, HorizontalAlignment.Stretch);
        }
        else
        {
            borderFactory.SetValue(Border.WidthProperty, 4.0);
            borderFactory.SetValue(Border.VerticalAlignmentProperty, VerticalAlignment.Stretch);
        }

        // Create grip dots container
        var stackPanelFactory = new FrameworkElementFactory(typeof(StackPanel));
        stackPanelFactory.Name = "GripDots";
        stackPanelFactory.SetValue(StackPanel.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        stackPanelFactory.SetValue(StackPanel.VerticalAlignmentProperty, VerticalAlignment.Center);
        stackPanelFactory.SetValue(UIElement.VisibilityProperty, Visibility.Collapsed);

        if (isHorizontal)
        {
            stackPanelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Horizontal);
        }
        else
        {
            stackPanelFactory.SetValue(StackPanel.OrientationProperty, Orientation.Vertical);
        }

        // Create three grip dots
        var dotBrush = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0xFF, 0xFF));
        for (int i = 0; i < 3; i++)
        {
            var ellipseFactory = new FrameworkElementFactory(typeof(System.Windows.Shapes.Ellipse));
            ellipseFactory.SetValue(System.Windows.Shapes.Ellipse.WidthProperty, 3.0);
            ellipseFactory.SetValue(System.Windows.Shapes.Ellipse.HeightProperty, 3.0);
            ellipseFactory.SetValue(System.Windows.Shapes.Ellipse.FillProperty, dotBrush);

            if (isHorizontal)
            {
                ellipseFactory.SetValue(System.Windows.Shapes.Ellipse.MarginProperty, new Thickness(1, 0, 1, 0));
            }
            else
            {
                ellipseFactory.SetValue(System.Windows.Shapes.Ellipse.MarginProperty, new Thickness(0, 1, 0, 1));
            }

            stackPanelFactory.AppendChild(ellipseFactory);
        }

        borderFactory.AppendChild(stackPanelFactory);
        gridFactory.AppendChild(borderFactory);

        template.VisualTree = gridFactory;

        // Add triggers for hover and drag states
        var isMouseOverTrigger = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        isMouseOverTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF)), "SplitterBorder"));
        isMouseOverTrigger.Setters.Add(new Setter(UIElement.VisibilityProperty, Visibility.Visible, "GripDots"));
        template.Triggers.Add(isMouseOverTrigger);

        var isDraggingTrigger = new Trigger { Property = System.Windows.Controls.Primitives.Thumb.IsDraggingProperty, Value = true };
        isDraggingTrigger.Setters.Add(new Setter(Border.BackgroundProperty,
            new SolidColorBrush(Color.FromRgb(0x00, 0x78, 0xD4)), "SplitterBorder"));
        template.Triggers.Add(isDraggingTrigger);

        return template;
    }

    private void UpdateSplitRatio(PaneContainerNode container)
    {
        // Find the grid that contains this container
        // The split ratio is determined by the row/column definitions
        // This is a simplified approach - we update based on the actual rendered sizes

        var grid = FindGridForContainer(container);
        if (grid == null)
            return;

        if (container.Orientation == SplitOrientation.Horizontal)
        {
            var totalHeight = grid.RowDefinitions[0].ActualHeight + grid.RowDefinitions[2].ActualHeight;
            if (totalHeight > 0)
            {
                container.SplitRatio = grid.RowDefinitions[0].ActualHeight / totalHeight;
            }
        }
        else
        {
            var totalWidth = grid.ColumnDefinitions[0].ActualWidth + grid.ColumnDefinitions[2].ActualWidth;
            if (totalWidth > 0)
            {
                container.SplitRatio = grid.ColumnDefinitions[0].ActualWidth / totalWidth;
            }
        }
    }

    private Grid? FindGridForContainer(PaneContainerNode container)
    {
        // Traverse the visual tree to find the grid
        return FindGridRecursive(RootPresenter.Content as FrameworkElement, container);
    }

    private Grid? FindGridRecursive(FrameworkElement? element, PaneContainerNode target)
    {
        if (element is Grid grid)
        {
            // Check if this grid represents the target container
            // We identify it by checking its structure matches the container
            if (IsGridForContainer(grid, target))
            {
                return grid;
            }

            // Search children
            foreach (var child in grid.Children.OfType<FrameworkElement>())
            {
                var result = FindGridRecursive(child, target);
                if (result != null)
                    return result;
            }
        }

        return null;
    }

    private bool IsGridForContainer(Grid grid, PaneContainerNode container)
    {
        // A simple heuristic: count row/column definitions
        if (container.Orientation == SplitOrientation.Horizontal)
        {
            return grid.RowDefinitions.Count == 3 && grid.ColumnDefinitions.Count == 0;
        }
        else
        {
            return grid.ColumnDefinitions.Count == 3 && grid.RowDefinitions.Count == 0;
        }
    }

    /// <summary>
    /// Gets the TerminalPane control for a specific leaf node.
    /// </summary>
    public TerminalPane? GetPaneControl(PaneLeafNode leaf)
    {
        return _paneControls.TryGetValue(leaf.Id, out var pane) ? pane : null;
    }

    /// <summary>
    /// Gets all TerminalPane controls.
    /// </summary>
    public IEnumerable<TerminalPane> GetAllPaneControls()
    {
        return _paneControls.Values;
    }
}

/// <summary>
/// Event args for pane split requests.
/// </summary>
public class PaneSplitRequestedEventArgs : EventArgs
{
    public PaneLeafNode Pane { get; }
    public SplitOrientation Orientation { get; }

    public PaneSplitRequestedEventArgs(PaneLeafNode pane, SplitOrientation orientation)
    {
        Pane = pane;
        Orientation = orientation;
    }
}

/// <summary>
/// Converts boolean to Visibility, using Hidden instead of Collapsed.
/// This keeps controls in the visual tree and prevents Unloaded from firing.
/// </summary>
public class BooleanToHiddenVisibilityConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Visible : Visibility.Hidden;
        }
        return Visibility.Hidden;
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            return visibility == Visibility.Visible;
        }
        return false;
    }
}
