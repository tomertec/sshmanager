using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SshManager.App.ViewModels;

namespace SshManager.App.Views.Controls;

/// <summary>
/// Canvas-based control for the SSH Tunnel Visual Builder.
/// Supports interactive node placement, dragging, edge connections, pan, and zoom.
/// </summary>
public partial class TunnelCanvas : UserControl
{
    private TunnelNodeViewModel? _draggedNode;
    private Point _dragStartPoint;
    private Point _dragStartNodePosition;
    private bool _isPanning;
    private Point _panStartPoint;
    private Point _panStartTranslate;
    private const double ZoomMin = 0.25;
    private const double ZoomMax = 2.0;
    private const double ZoomStep = 0.1;

    public TunnelCanvas()
    {
        InitializeComponent();
    }

    private TunnelBuilderViewModel? ViewModel => DataContext as TunnelBuilderViewModel;

    #region Node Dragging

    private void Node_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.DataContext is not TunnelNodeViewModel node)
            return;

        // Check if we're in connection mode
        if (ViewModel != null && ViewModel.IsInConnectionMode && ViewModel.ConnectionSourceNode != null)
        {
            // Complete the connection
            ViewModel.CompleteConnection(node);
            e.Handled = true;
            return;
        }

        // Select the node (use the command to properly update IsSelected on all nodes)
        if (ViewModel != null)
        {
            ViewModel.SelectNodeCommand.Execute(node);
        }

        // Start drag operation
        _draggedNode = node;
        _dragStartPoint = e.GetPosition(MainCanvas);
        _dragStartNodePosition = new Point(node.X, node.Y);

        element.CaptureMouse();
        e.Handled = true;
    }

    private void Node_MouseMove(object sender, MouseEventArgs e)
    {
        if (_draggedNode == null || Mouse.Captured == null || !Mouse.Captured.Equals(sender))
            return;

        var currentPosition = e.GetPosition(MainCanvas);
        var delta = currentPosition - _dragStartPoint;

        // Apply scale factor to delta
        var scale = CanvasScaleTransform.ScaleX;
        delta.X /= scale;
        delta.Y /= scale;

        // Update node position
        _draggedNode.X = _dragStartNodePosition.X + delta.X;
        _draggedNode.Y = _dragStartNodePosition.Y + delta.Y;

        e.Handled = true;
    }

    private void Node_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_draggedNode == null)
            return;

        if (sender is FrameworkElement element)
        {
            element.ReleaseMouseCapture();
        }

        _draggedNode = null;
        e.Handled = true;
    }

    #endregion

    #region Canvas Pan

    private void Canvas_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Start panning with right mouse button
        _isPanning = true;
        _panStartPoint = e.GetPosition(RootGrid);
        _panStartTranslate = new Point(CanvasTranslateTransform.X, CanvasTranslateTransform.Y);

        MainCanvas.CaptureMouse();
        MainCanvas.Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    private void Canvas_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_isPanning)
        {
            _isPanning = false;
            MainCanvas.ReleaseMouseCapture();
            MainCanvas.Cursor = Cursors.Arrow;
            e.Handled = true;
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (_isPanning && e.RightButton == MouseButtonState.Pressed)
        {
            var currentPosition = e.GetPosition(RootGrid);
            var delta = currentPosition - _panStartPoint;

            CanvasTranslateTransform.X = _panStartTranslate.X + delta.X;
            CanvasTranslateTransform.Y = _panStartTranslate.Y + delta.Y;

            e.Handled = true;
        }
    }

    #endregion

    #region Canvas Zoom

    private void Canvas_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var delta = e.Delta > 0 ? ZoomStep : -ZoomStep;
        var newScale = Math.Clamp(CanvasScaleTransform.ScaleX + delta, ZoomMin, ZoomMax);

        // Zoom towards mouse cursor position
        var mousePosition = e.GetPosition(MainCanvas);

        var offsetX = mousePosition.X * (newScale - CanvasScaleTransform.ScaleX);
        var offsetY = mousePosition.Y * (newScale - CanvasScaleTransform.ScaleY);

        CanvasScaleTransform.ScaleX = newScale;
        CanvasScaleTransform.ScaleY = newScale;

        CanvasTranslateTransform.X -= offsetX;
        CanvasTranslateTransform.Y -= offsetY;

        e.Handled = true;
    }

    #endregion

    #region Canvas Click

    private void Canvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Deselect nodes when clicking on empty canvas area
        if (e.OriginalSource == MainCanvas)
        {
            ViewModel?.ClearSelectionCommand.Execute(null);
        }
    }

    private void Canvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        // Currently unused, but available for future functionality
    }

    #endregion

    #region Edge Selection

    private void Edge_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement element && element.DataContext is TunnelEdgeViewModel edge)
        {
            // Use the command to properly update IsSelected on all nodes and edges
            ViewModel?.SelectEdgeCommand.Execute(edge);
            e.Handled = true;
        }
    }

    #endregion

    #region View Reset

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        ResetCanvasView();
    }

    /// <summary>
    /// Resets the canvas view to default zoom (100%) and pan (0,0).
    /// </summary>
    private void ResetCanvasView()
    {
        // Access transforms through the RenderTransform property for reliability
        if (MainCanvas.RenderTransform is TransformGroup transformGroup)
        {
            foreach (var transform in transformGroup.Children)
            {
                if (transform is ScaleTransform scaleTransform)
                {
                    scaleTransform.ScaleX = 1.0;
                    scaleTransform.ScaleY = 1.0;
                }
                else if (transform is TranslateTransform translateTransform)
                {
                    translateTransform.X = 0;
                    translateTransform.Y = 0;
                }
            }
        }
    }

    #endregion
}
