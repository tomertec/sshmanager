using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using SshManager.Core.Models;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for a connection edge in the SSH tunnel visual builder.
/// </summary>
public partial class TunnelEdgeViewModel : ObservableObject, IDisposable
{
    private bool _disposed;
    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private Guid _sourceNodeId;

    [ObservableProperty]
    private Guid _targetNodeId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartPoint))]
    [NotifyPropertyChangedFor(nameof(EndPoint))]
    [NotifyPropertyChangedFor(nameof(ControlPoint1))]
    [NotifyPropertyChangedFor(nameof(ControlPoint2))]
    private TunnelNodeViewModel? _sourceNode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartPoint))]
    [NotifyPropertyChangedFor(nameof(EndPoint))]
    [NotifyPropertyChangedFor(nameof(ControlPoint1))]
    [NotifyPropertyChangedFor(nameof(ControlPoint2))]
    private TunnelNodeViewModel? _targetNode;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>
    /// Gets the starting point of the edge.
    /// </summary>
    public Point StartPoint => SourceNode?.CenterPoint ?? new Point(0, 0);

    /// <summary>
    /// Gets the ending point of the edge.
    /// </summary>
    public Point EndPoint => TargetNode?.CenterPoint ?? new Point(0, 0);

    /// <summary>
    /// Gets the first control point for the bezier curve.
    /// </summary>
    public Point ControlPoint1
    {
        get
        {
            var start = StartPoint;
            var end = EndPoint;
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;

            // Use one-third of the horizontal distance for smooth curves
            return new Point(start.X + dx * 0.33, start.Y + dy * 0.15);
        }
    }

    /// <summary>
    /// Gets the second control point for the bezier curve.
    /// </summary>
    public Point ControlPoint2
    {
        get
        {
            var start = StartPoint;
            var end = EndPoint;
            var dx = end.X - start.X;
            var dy = end.Y - start.Y;

            // Use two-thirds of the horizontal distance for smooth curves
            return new Point(start.X + dx * 0.67, start.Y + dy * 0.85);
        }
    }

    /// <summary>
    /// Default constructor.
    /// </summary>
    public TunnelEdgeViewModel()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Initializes a new instance from a TunnelEdge model.
    /// </summary>
    public TunnelEdgeViewModel(TunnelEdge edge)
    {
        Id = edge.Id;
        SourceNodeId = edge.SourceNodeId;
        TargetNodeId = edge.TargetNodeId;
    }

    /// <summary>
    /// Converts this ViewModel to a TunnelEdge model.
    /// </summary>
    public TunnelEdge ToModel()
    {
        return new TunnelEdge
        {
            Id = Id,
            SourceNodeId = SourceNodeId,
            TargetNodeId = TargetNodeId
        };
    }

    /// <summary>
    /// Sets up property change notifications when source node properties change.
    /// </summary>
    private void SubscribeToNodePropertyChanges(TunnelNodeViewModel? oldNode, TunnelNodeViewModel? newNode)
    {
        if (_disposed)
        {
            return;
        }

        if (oldNode != null)
        {
            oldNode.PropertyChanged -= OnNodePropertyChanged;
        }

        if (newNode != null)
        {
            newNode.PropertyChanged += OnNodePropertyChanged;
        }
    }

    /// <summary>
    /// Handles property changes from source or target nodes.
    /// </summary>
    private void OnNodePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TunnelNodeViewModel.CenterPoint))
        {
            OnPropertyChanged(nameof(StartPoint));
            OnPropertyChanged(nameof(EndPoint));
            OnPropertyChanged(nameof(ControlPoint1));
            OnPropertyChanged(nameof(ControlPoint2));
        }
    }

    partial void OnSourceNodeChanged(TunnelNodeViewModel? oldValue, TunnelNodeViewModel? newValue)
    {
        SubscribeToNodePropertyChanges(oldValue, newValue);
    }

    partial void OnTargetNodeChanged(TunnelNodeViewModel? oldValue, TunnelNodeViewModel? newValue)
    {
        SubscribeToNodePropertyChanges(oldValue, newValue);
    }

    /// <summary>
    /// Releases all resources used by the TunnelEdgeViewModel.
    /// </summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Releases the unmanaged resources and optionally releases the managed resources.
    /// </summary>
    /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
        {
            return;
        }

        if (disposing)
        {
            // Unsubscribe from PropertyChanged events to prevent memory leaks
            if (SourceNode != null)
            {
                SourceNode.PropertyChanged -= OnNodePropertyChanged;
            }

            if (TargetNode != null)
            {
                TargetNode.PropertyChanged -= OnNodePropertyChanged;
            }

            // Clear references
            SourceNode = null;
            TargetNode = null;
        }

        _disposed = true;
    }
}
