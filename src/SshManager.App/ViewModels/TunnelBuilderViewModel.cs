using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using SshManager.App.Services;
using SshManager.App.Views.Dialogs;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Terminal.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the SSH Tunnel Visual Builder dialog.
/// </summary>
public partial class TunnelBuilderViewModel : ObservableObject
{
    // Node positioning constants
    private const int NodeGridColumns = 3;
    private const int NodeSpacingX = 200;
    private const int NodeSpacingY = 150;
    private const int NodeGridOffsetX = 50;
    private const int NodeGridOffsetY = 50;

    private readonly ITunnelBuilderService _tunnelBuilderService;

    private readonly ITunnelProfileRepository _tunnelProfileRepository;
    private readonly IHostRepository _hostRepository;
    private readonly IHostFingerprintRepository _fingerprintRepository;
    private readonly ISnackbarService _snackbarService;
    private readonly ILogger<TunnelBuilderViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<TunnelNodeViewModel> _nodes = new();

    [ObservableProperty]
    private ObservableCollection<TunnelEdgeViewModel> _edges = new();

    [ObservableProperty]
    private ObservableCollection<HostEntry> _availableHosts = new();

    [ObservableProperty]
    private Guid? _profileId;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private string? _description;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommandCommand))]
    private string _commandPreview = string.Empty;

    [ObservableProperty]
    private bool _isExecuting;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveNodeCommand))]
    [NotifyCanExecuteChangedFor(nameof(DisconnectNodesCommand))]
    private TunnelNodeViewModel? _selectedNode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DisconnectNodesCommand))]
    private TunnelEdgeViewModel? _selectedEdge;

    [ObservableProperty]
    private string? _validationError;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CopyCommandCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExecuteCommand))]
    private bool _isValid;

    /// <summary>
    /// Gets a value indicating whether the copy command can be executed.
    /// </summary>
    private bool CanCopyCommand => IsValid && !string.IsNullOrEmpty(CommandPreview);

    /// <summary>
    /// Mode for connecting nodes.
    /// </summary>
    private TunnelNodeViewModel? _connectionSourceNode;

    /// <summary>
    /// Gets a value indicating whether the builder is in connection mode.
    /// </summary>
    public bool IsInConnectionMode => _connectionSourceNode != null;

    /// <summary>
    /// Gets the source node for the current connection being created, if any.
    /// </summary>
    public TunnelNodeViewModel? ConnectionSourceNode => _connectionSourceNode;

    public event Action? RequestClose;

    public TunnelBuilderViewModel(
        ITunnelBuilderService tunnelBuilderService,
        ITunnelProfileRepository tunnelProfileRepository,
        IHostRepository hostRepository,
        IHostFingerprintRepository fingerprintRepository,
        ISnackbarService snackbarService,
        ILogger<TunnelBuilderViewModel> logger)
    {
        _tunnelBuilderService = tunnelBuilderService;
        _tunnelProfileRepository = tunnelProfileRepository;
        _hostRepository = hostRepository;
        _fingerprintRepository = fingerprintRepository;
        _snackbarService = snackbarService;
        _logger = logger;
    }

    /// <summary>
    /// Initializes the ViewModel by loading available hosts.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        try
        {
            var hosts = await _hostRepository.GetAllAsync(ct);
            AvailableHosts = new ObservableCollection<HostEntry>(
                hosts.Where(h => h.ConnectionType == ConnectionType.Ssh));

            _logger.LogInformation("Loaded {HostCount} SSH hosts for tunnel builder", AvailableHosts.Count);

            // Add a default LocalMachine node if starting fresh
            if (!Nodes.Any())
            {
                AddNode(TunnelNodeType.LocalMachine);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize tunnel builder");
            _snackbarService.Show(
                "Error",
                $"Failed to load hosts: {ex.Message}",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>
    /// Loads an existing tunnel profile.
    /// </summary>
    public async Task LoadProfileAsync(Guid profileId, CancellationToken ct = default)
    {
        try
        {
            var profile = await _tunnelProfileRepository.GetByIdAsync(profileId, ct);
            if (profile == null)
            {
                _logger.LogWarning("Tunnel profile {ProfileId} not found", profileId);
                return;
            }

            ProfileId = profile.Id;
            DisplayName = profile.DisplayName;
            Description = profile.Description;

            // Load nodes
            Nodes.Clear();
            var nodeViewModels = profile.Nodes
                .Select(n => new TunnelNodeViewModel(n))
                .ToList();

            foreach (var node in nodeViewModels)
            {
                // If this is an SSH host node, try to find the corresponding HostEntry
                if (node.NodeType == TunnelNodeType.SshHost && node.HostId.HasValue)
                {
                    node.SelectedHost = AvailableHosts.FirstOrDefault(h => h.Id == node.HostId.Value);
                }
                Nodes.Add(node);
            }

            // Load edges (dispose old edges before clearing)
            foreach (var oldEdge in Edges)
            {
                oldEdge.Dispose();
            }
            Edges.Clear();
            foreach (var edge in profile.Edges)
            {
                var edgeVm = new TunnelEdgeViewModel(edge)
                {
                    SourceNode = Nodes.FirstOrDefault(n => n.Id == edge.SourceNodeId),
                    TargetNode = Nodes.FirstOrDefault(n => n.Id == edge.TargetNodeId)
                };
                Edges.Add(edgeVm);
            }

            _logger.LogInformation("Loaded tunnel profile '{DisplayName}' with {NodeCount} nodes and {EdgeCount} edges",
                DisplayName, Nodes.Count, Edges.Count);

            // Validate and generate preview
            await ValidateAndGeneratePreviewAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tunnel profile {ProfileId}", profileId);
            _snackbarService.Show(
                "Error",
                $"Failed to load profile: {ex.Message}",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>
    /// Saves the current tunnel profile.
    /// </summary>
    [RelayCommand]
    private async Task SaveAsync()
    {
        if (string.IsNullOrWhiteSpace(DisplayName))
        {
            _snackbarService.Show(
                "Validation Error",
                "Display name is required",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(3));
            return;
        }

        try
        {
            var profile = ToProfile();

            if (ProfileId.HasValue)
            {
                await _tunnelProfileRepository.UpdateAsync(profile);
                _logger.LogInformation("Updated tunnel profile '{DisplayName}'", DisplayName);
            }
            else
            {
                await _tunnelProfileRepository.AddAsync(profile);
                ProfileId = profile.Id;
                _logger.LogInformation("Created new tunnel profile '{DisplayName}'", DisplayName);
            }

            _snackbarService.Show(
                "Success",
                "Tunnel profile saved successfully",
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save tunnel profile");
            _snackbarService.Show(
                "Error",
                $"Failed to save profile: {ex.Message}",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>
    /// Adds a new node to the canvas.
    /// </summary>
    [RelayCommand]
    private void AddNode(TunnelNodeType nodeType)
    {
        // Calculate position for new node (simple grid layout)
        var column = Nodes.Count % NodeGridColumns;
        var row = Nodes.Count / NodeGridColumns;
        var x = NodeGridOffsetX + (column * NodeSpacingX);
        var y = NodeGridOffsetY + (row * NodeSpacingY);

        var node = new TunnelNodeViewModel
        {
            Id = Guid.NewGuid(),
            NodeType = nodeType,
            Label = GetDefaultLabelForNodeType(nodeType),
            X = x,
            Y = y,
            BindAddress = "localhost" // Default bind address
        };

        Nodes.Add(node);
        _logger.LogDebug("Added {NodeType} node at ({X}, {Y})", nodeType, x, y);

        // Auto-validate after adding a node
        SafeFireAndForget(ValidateAndGeneratePreviewAsync());
    }

    /// <summary>
    /// Removes the selected node from the canvas.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRemoveNode))]
    private void RemoveNode()
    {
        if (SelectedNode == null) return;

        // Don't allow removing the last LocalMachine node
        if (SelectedNode.NodeType == TunnelNodeType.LocalMachine &&
            Nodes.Count(n => n.NodeType == TunnelNodeType.LocalMachine) == 1)
        {
            _snackbarService.Show(
                "Cannot Remove",
                "At least one LocalMachine node is required",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(2));
            return;
        }

        // Remove connected edges
        var connectedEdges = Edges
            .Where(e => e.SourceNodeId == SelectedNode.Id || e.TargetNodeId == SelectedNode.Id)
            .ToList();

        foreach (var edge in connectedEdges)
        {
            Edges.Remove(edge);
            edge.Dispose();
        }

        Nodes.Remove(SelectedNode);
        SelectedNode = null;

        _logger.LogDebug("Removed node and {EdgeCount} connected edges", connectedEdges.Count);

        // Auto-validate after removing a node
        SafeFireAndForget(ValidateAndGeneratePreviewAsync());
    }

    private bool CanRemoveNode() => SelectedNode != null;

    /// <summary>
    /// Selects a node on the canvas.
    /// </summary>
    [RelayCommand]
    private void SelectNode(TunnelNodeViewModel? node)
    {
        // If in connection mode, complete the connection instead of selecting
        if (_connectionSourceNode != null && node != null)
        {
            CompleteConnection(node);
            return;
        }

        // Deselect all nodes first
        foreach (var n in Nodes)
        {
            n.IsSelected = false;
        }

        // Deselect all edges
        foreach (var e in Edges)
        {
            e.IsSelected = false;
        }

        // Select the clicked node
        if (node != null)
        {
            node.IsSelected = true;
        }

        SelectedNode = node;
        SelectedEdge = null;

        _logger.LogDebug("Selected node: {NodeLabel}", node?.Label ?? "none");
    }

    /// <summary>
    /// Selects an edge on the canvas.
    /// </summary>
    [RelayCommand]
    private void SelectEdge(TunnelEdgeViewModel? edge)
    {
        // Deselect all nodes first
        foreach (var n in Nodes)
        {
            n.IsSelected = false;
        }

        // Deselect all edges
        foreach (var e in Edges)
        {
            e.IsSelected = false;
        }

        // Select the clicked edge
        if (edge != null)
        {
            edge.IsSelected = true;
        }

        SelectedEdge = edge;
        SelectedNode = null;

        _logger.LogDebug("Selected edge from {Source} to {Target}",
            edge?.SourceNode?.Label ?? "none",
            edge?.TargetNode?.Label ?? "none");
    }

    /// <summary>
    /// Clears the current selection (nodes and edges).
    /// </summary>
    [RelayCommand]
    private void ClearSelection()
    {
        // Cancel connection mode if active
        SetConnectionSourceNode(null);

        // Deselect all nodes
        foreach (var n in Nodes)
        {
            n.IsSelected = false;
        }

        // Deselect all edges
        foreach (var e in Edges)
        {
            e.IsSelected = false;
        }

        SelectedNode = null;
        SelectedEdge = null;
    }

    /// <summary>
    /// Starts connection mode to connect two nodes.
    /// </summary>
    [RelayCommand]
    private void ConnectNodes()
    {
        if (SelectedNode == null)
        {
            _snackbarService.Show(
                "Select Node",
                "Please select a source node first",
                ControlAppearance.Info,
                null,
                TimeSpan.FromSeconds(2));
            return;
        }

        SetConnectionSourceNode(SelectedNode);
        _snackbarService.Show(
            "Connection Mode",
            $"Click on a target node to connect from '{_connectionSourceNode!.Label}'",
            ControlAppearance.Info,
            null,
            TimeSpan.FromSeconds(3));
    }

    /// <summary>
    /// Completes the connection to a target node.
    /// Called from the UI when a node is clicked in connection mode.
    /// </summary>
    public void CompleteConnection(TunnelNodeViewModel targetNode)
    {
        if (_connectionSourceNode == null || _connectionSourceNode.Id == targetNode.Id)
        {
            SetConnectionSourceNode(null);
            return;
        }

        // Check if edge already exists
        if (Edges.Any(e => e.SourceNodeId == _connectionSourceNode.Id && e.TargetNodeId == targetNode.Id))
        {
            _snackbarService.Show(
                "Connection Exists",
                "These nodes are already connected",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(2));
            SetConnectionSourceNode(null);
            return;
        }

        var edge = new TunnelEdgeViewModel
        {
            Id = Guid.NewGuid(),
            SourceNodeId = _connectionSourceNode.Id,
            TargetNodeId = targetNode.Id,
            SourceNode = _connectionSourceNode,
            TargetNode = targetNode
        };

        Edges.Add(edge);
        _logger.LogDebug("Connected '{Source}' to '{Target}'", _connectionSourceNode.Label, targetNode.Label);

        SetConnectionSourceNode(null);

        // Auto-validate after adding an edge
        SafeFireAndForget(ValidateAndGeneratePreviewAsync());
    }

    /// <summary>
    /// Disconnects the selected edge or nodes.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanDisconnectNodes))]
    private void DisconnectNodes()
    {
        if (SelectedEdge != null)
        {
            var edgeToRemove = SelectedEdge;
            Edges.Remove(edgeToRemove);
            SelectedEdge = null;
            edgeToRemove.Dispose();
            _logger.LogDebug("Removed selected edge");

            // Auto-validate after removing an edge
            SafeFireAndForget(ValidateAndGeneratePreviewAsync());
        }
    }

    private bool CanDisconnectNodes() => SelectedEdge != null;

    /// <summary>
    /// Validates the current tunnel configuration.
    /// </summary>
    [RelayCommand]
    private async Task ValidateAsync()
    {
        await ValidateAndGeneratePreviewAsync();
    }

    /// <summary>
    /// Generates the SSH command preview.
    /// </summary>
    [RelayCommand]
    private async Task GenerateCommandAsync()
    {
        await ValidateAndGeneratePreviewAsync();
    }

    /// <summary>
    /// Executes the tunnel configuration.
    /// </summary>
    [RelayCommand]
    private async Task ExecuteAsync()
    {
        if (!IsValid)
        {
            _snackbarService.Show(
                "Validation Error",
                "Please fix validation errors before executing",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(3));
            return;
        }

        IsExecuting = true;
        try
        {
            var profile = ToProfile();
            var hostKeyCallback = CreateHostKeyVerificationCallback(profile);
            var result = await _tunnelBuilderService.ExecuteAsync(profile, hostKeyCallback);

            if (result.Success)
            {
                _snackbarService.Show(
                    "Tunnel Active",
                    $"SSH tunnel '{DisplayName}' is now active",
                    ControlAppearance.Success,
                    null,
                    TimeSpan.FromSeconds(3));

                _logger.LogInformation("Successfully executed tunnel profile '{DisplayName}'", DisplayName);

                // Close the dialog after successful execution
                RequestClose?.Invoke();
            }
            else
            {
                _snackbarService.Show(
                    "Execution Failed",
                    result.ErrorMessage ?? "Unknown error occurred",
                    ControlAppearance.Danger,
                    null,
                    TimeSpan.FromSeconds(5));

                _logger.LogWarning("Failed to execute tunnel profile '{DisplayName}': {Error}",
                    DisplayName, result.ErrorMessage);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing tunnel profile");
            _snackbarService.Show(
                "Error",
                $"Failed to execute tunnel: {ex.Message}",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(5));
        }
        finally
        {
            IsExecuting = false;
        }
    }

    /// <summary>
    /// Copies the generated command to clipboard.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanCopyCommand))]
    private void CopyCommand()
    {
        if (string.IsNullOrEmpty(CommandPreview) || !IsValid)
        {
            _snackbarService.Show(
                "Cannot Copy",
                "Fix validation errors first",
                ControlAppearance.Caution,
                null,
                TimeSpan.FromSeconds(2));
            return;
        }

        try
        {
            Clipboard.SetText(CommandPreview);
            _snackbarService.Show(
                "Copied",
                "Command copied to clipboard",
                ControlAppearance.Success,
                null,
                TimeSpan.FromSeconds(2));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to copy command to clipboard");
            _snackbarService.Show(
                "Error",
                "Failed to copy to clipboard",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>
    /// Converts the ViewModel to a TunnelProfile model.
    /// </summary>
    private TunnelProfile ToProfile()
    {
        var profile = new TunnelProfile
        {
            Id = ProfileId ?? Guid.NewGuid(),
            DisplayName = DisplayName,
            Description = Description,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        // Convert nodes
        foreach (var nodeVm in Nodes)
        {
            var node = nodeVm.ToModel();
            node.TunnelProfileId = profile.Id;
            profile.Nodes.Add(node);
        }

        // Convert edges
        foreach (var edgeVm in Edges)
        {
            var edge = edgeVm.ToModel();
            edge.TunnelProfileId = profile.Id;
            profile.Edges.Add(edge);
        }

        return profile;
    }

    /// <summary>
    /// Validates the configuration and generates the command preview.
    /// </summary>
    private async Task ValidateAndGeneratePreviewAsync()
    {
        try
        {
            var profile = ToProfile();

            // Validate
            var validationResult = _tunnelBuilderService.Validate(profile);
            IsValid = validationResult.IsValid;

            if (!validationResult.IsValid)
            {
                ValidationError = string.Join("\n", validationResult.Errors);
                CommandPreview = "// Fix validation errors to generate command";
                _logger.LogDebug("Validation failed: {Errors}",
                    string.Join("; ", validationResult.Errors));
            }
            else
            {
                ValidationError = null;

                // Generate command preview
                CommandPreview = await _tunnelBuilderService.GenerateSshCommandAsync(profile);

                if (validationResult.Warnings.Any())
                {
                    var warningsText = string.Join("\n", validationResult.Warnings);
                    CommandPreview = $"// Warnings:\n// {string.Join("\n// ", validationResult.Warnings)}\n\n{CommandPreview}";
                }

                _logger.LogDebug("Generated SSH command: {Command}", CommandPreview);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during validation and command generation");
            ValidationError = $"Error: {ex.Message}";
            CommandPreview = string.Empty;
            IsValid = false;
        }
    }

    /// <summary>
    /// Sets the connection source node and raises property change notifications.
    /// </summary>
    private void SetConnectionSourceNode(TunnelNodeViewModel? value)
    {
        if (_connectionSourceNode != value)
        {
            _connectionSourceNode = value;
            OnPropertyChanged(nameof(IsInConnectionMode));
            OnPropertyChanged(nameof(ConnectionSourceNode));
        }
    }

    /// <summary>
    /// Gets the default label for a node type.
    /// </summary>
    private static string GetDefaultLabelForNodeType(TunnelNodeType nodeType)
    {
        return nodeType switch
        {
            TunnelNodeType.LocalMachine => "Local Machine",
            TunnelNodeType.SshHost => "SSH Host",
            TunnelNodeType.LocalPort => "Local Port",
            TunnelNodeType.RemotePort => "Remote Port",
            TunnelNodeType.DynamicProxy => "SOCKS Proxy",
            TunnelNodeType.TargetHost => "Target Host",
            _ => "Node"
        };
    }

    /// <summary>
    /// Creates a host key verification callback for tunnel execution.
    /// Matches hosts by hostname/port and supports multiple key algorithms per host.
    /// </summary>
    private HostKeyVerificationCallback CreateHostKeyVerificationCallback(TunnelProfile profile)
    {
        // Build a lookup of hostname:port -> HostEntry for quick matching
        var hostLookup = new Dictionary<string, HostEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var node in profile.Nodes.Where(n => n.NodeType == TunnelNodeType.SshHost && n.HostId.HasValue))
        {
            var hostId = node.HostId!.Value; // Safe: Where clause guarantees HasValue
            var host = AvailableHosts.FirstOrDefault(h => h.Id == hostId);
            if (host != null)
            {
                var key = $"{host.Hostname}:{host.Port}";
                hostLookup.TryAdd(key, host);
            }
        }

        return async (hostname, port, algorithm, fingerprint, keyBytes) =>
        {
            _logger.LogDebug("Verifying host key for tunnel: {Hostname}:{Port} - {Algorithm}", hostname, port, algorithm);

            // Find the matching host entry
            var lookupKey = $"{hostname}:{port}";
            if (!hostLookup.TryGetValue(lookupKey, out var hostEntry))
            {
                _logger.LogWarning("No matching host entry found for {Hostname}:{Port} in tunnel profile", hostname, port);
                // Fall through to show verification dialog with null hostId
                hostEntry = null;
            }

            var hostId = hostEntry?.Id;

            // If we don't have a hostId, we can't use the helper (it requires a hostId)
            // Fall back to showing the dialog without fingerprint storage
            if (!hostId.HasValue)
            {
                // Check if application is available before showing dialog
                if (Application.Current?.Dispatcher == null)
                {
                    _logger.LogWarning("Cannot show host key verification dialog - application is shutting down");
                    return false;
                }

                // Show verification dialog on UI thread without storing fingerprint
                var accepted = await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    var dialog = new HostKeyVerificationDialog();
                    dialog.Owner = Application.Current.MainWindow;
                    dialog.Initialize(hostname, port, algorithm, fingerprint, null);
                    dialog.ShowDialog();
                    return dialog.IsAccepted;
                });

                if (!accepted)
                {
                    _logger.LogWarning("Host key rejected by user for {Hostname}:{Port} ({Algorithm})", hostname, port, algorithm);
                }

                return accepted;
            }

            // Use the shared helper for standard host key verification
            var callback = HostKeyVerificationHelper.CreateCallback(hostId.Value, _fingerprintRepository, _logger);
            return await callback(hostname, port, algorithm, fingerprint, keyBytes);
        };
    }

    /// <summary>
    /// Safely executes a fire-and-forget async task with proper exception handling.
    /// Prevents silent failures by logging any exceptions that occur.
    /// </summary>
    /// <param name="task">The task to execute.</param>
    private void SafeFireAndForget(Task task)
    {
        task.ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                _logger.LogError(t.Exception, "Background task failed in TunnelBuilderViewModel");
            }
        }, TaskScheduler.Default);
    }
}
