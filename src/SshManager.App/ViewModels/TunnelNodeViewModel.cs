using System.Windows;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SshManager.Core.Models;
using Wpf.Ui.Controls;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for a node in the SSH tunnel visual builder.
/// </summary>
public partial class TunnelNodeViewModel : ObservableObject
{
    // Node size constants for calculating the center point
    private const double NodeWidth = 120;
    private const double NodeHeight = 80;

    [ObservableProperty]
    private Guid _id;

    [ObservableProperty]
    private TunnelNodeType _nodeType;

    [ObservableProperty]
    private Guid? _hostId;

    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private double _x;

    [ObservableProperty]
    private double _y;

    [ObservableProperty]
    private int? _localPort;

    [ObservableProperty]
    private int? _remotePort;

    [ObservableProperty]
    private string? _remoteHost;

    [ObservableProperty]
    private string? _bindAddress;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private HostEntry? _selectedHost;

    /// <summary>
    /// Gets the icon for this node type.
    /// </summary>
    public string NodeIcon => NodeType switch
    {
        TunnelNodeType.LocalMachine => "\ue977", // Computer icon
        TunnelNodeType.SshHost => "\ue968", // Server icon
        TunnelNodeType.LocalPort => "\ue945", // Port/plug icon
        TunnelNodeType.RemotePort => "\ue946", // Remote port icon
        TunnelNodeType.DynamicProxy => "\ue943", // Proxy icon
        TunnelNodeType.TargetHost => "\ue969", // Target host icon
        _ => "\ue946" // Default icon
    };

    /// <summary>
    /// Gets the color for this node type.
    /// </summary>
    public string NodeColor => NodeType switch
    {
        TunnelNodeType.LocalMachine => "#4A90E2", // Blue
        TunnelNodeType.SshHost => "#50C878", // Green
        TunnelNodeType.LocalPort => "#FFA500", // Orange
        TunnelNodeType.RemotePort => "#E74C3C", // Red
        TunnelNodeType.DynamicProxy => "#9B59B6", // Purple
        TunnelNodeType.TargetHost => "#1ABC9C", // Teal
        _ => "#95A5A6" // Gray
    };

    /// <summary>
    /// Gets the WPF-UI symbol icon for this node type.
    /// </summary>
    public SymbolRegular NodeSymbol => NodeType switch
    {
        TunnelNodeType.LocalMachine => SymbolRegular.Desktop24,
        TunnelNodeType.SshHost => SymbolRegular.Server24,
        TunnelNodeType.LocalPort => SymbolRegular.PlugConnected24,
        TunnelNodeType.RemotePort => SymbolRegular.CloudArrowUp24,
        TunnelNodeType.DynamicProxy => SymbolRegular.ArrowSync24,
        TunnelNodeType.TargetHost => SymbolRegular.Target24,
        _ => SymbolRegular.Circle24
    };

    /// <summary>
    /// Gets the border brush based on selection state.
    /// </summary>
    public Brush BorderBrush => IsSelected
        ? new SolidColorBrush(Color.FromRgb(0x3B, 0x82, 0xF6))
        : Brushes.Transparent;

    /// <summary>
    /// Gets the border thickness based on selection state.
    /// </summary>
    public Thickness BorderThickness => IsSelected
        ? new Thickness(3)
        : new Thickness(0);

    /// <summary>
    /// Gets the display label with port information if applicable.
    /// </summary>
    public string DisplayLabel
    {
        get
        {
            if (NodeType == TunnelNodeType.LocalPort && LocalPort.HasValue)
            {
                return $"{Label}\n:{LocalPort}";
            }
            else if (NodeType == TunnelNodeType.RemotePort && RemotePort.HasValue)
            {
                return $"{Label}\n→ {RemoteHost ?? "localhost"}:{RemotePort}";
            }
            else if (NodeType == TunnelNodeType.DynamicProxy && LocalPort.HasValue)
            {
                return $"{Label}\nSOCKS:{LocalPort}";
            }
            else if (NodeType == TunnelNodeType.TargetHost && !string.IsNullOrEmpty(RemoteHost))
            {
                return $"{Label}\n{RemoteHost}";
            }
            else if (NodeType == TunnelNodeType.SshHost && SelectedHost != null)
            {
                return $"{Label}\n{SelectedHost.Username}@{SelectedHost.Hostname}";
            }
            return Label;
        }
    }

    /// <summary>
    /// Gets the center point of the node for edge connections.
    /// </summary>
    public Point CenterPoint => new Point(X + NodeWidth / 2, Y + NodeHeight / 2);

    /// <summary>
    /// Default constructor.
    /// </summary>
    public TunnelNodeViewModel()
    {
        Id = Guid.NewGuid();
    }

    /// <summary>
    /// Initializes a new instance from a TunnelNode model.
    /// </summary>
    public TunnelNodeViewModel(TunnelNode node)
    {
        Id = node.Id;
        NodeType = node.NodeType;
        HostId = node.HostId;
        Label = node.Label;
        X = node.X;
        Y = node.Y;
        LocalPort = node.LocalPort;
        RemotePort = node.RemotePort;
        RemoteHost = node.RemoteHost;
        BindAddress = node.BindAddress;
    }

    /// <summary>
    /// Converts this ViewModel to a TunnelNode model.
    /// </summary>
    public TunnelNode ToModel()
    {
        return new TunnelNode
        {
            Id = Id,
            NodeType = NodeType,
            HostId = HostId,
            Label = Label,
            X = X,
            Y = Y,
            LocalPort = LocalPort,
            RemotePort = RemotePort,
            RemoteHost = RemoteHost,
            BindAddress = BindAddress
        };
    }

    /// <summary>
    /// Notifies that display label has changed when relevant properties change.
    /// </summary>
    partial void OnLabelChanged(string value) => OnPropertyChanged(nameof(DisplayLabel));
    partial void OnLocalPortChanged(int? value) => OnPropertyChanged(nameof(DisplayLabel));
    partial void OnRemotePortChanged(int? value) => OnPropertyChanged(nameof(DisplayLabel));
    partial void OnRemoteHostChanged(string? value) => OnPropertyChanged(nameof(DisplayLabel));
    partial void OnSelectedHostChanged(HostEntry? value)
    {
        HostId = value?.Id;
        OnPropertyChanged(nameof(DisplayLabel));
    }
    partial void OnXChanged(double value) => OnPropertyChanged(nameof(CenterPoint));
    partial void OnYChanged(double value) => OnPropertyChanged(nameof(CenterPoint));
    partial void OnNodeTypeChanged(TunnelNodeType value)
    {
        OnPropertyChanged(nameof(NodeIcon));
        OnPropertyChanged(nameof(NodeColor));
        OnPropertyChanged(nameof(NodeSymbol));
        OnPropertyChanged(nameof(DisplayLabel));
    }

    partial void OnIsSelectedChanged(bool value)
    {
        OnPropertyChanged(nameof(BorderBrush));
        OnPropertyChanged(nameof(BorderThickness));
    }
}
