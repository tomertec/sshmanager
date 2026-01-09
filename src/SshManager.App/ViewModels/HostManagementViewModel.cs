using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Net;
using System.Net.Sockets;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal.Services;
using SshManager.App.Views.Dialogs;
using SshManager.App.Behaviors;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel responsible for host and group data management.
/// Handles CRUD operations for hosts and groups, search, and filtering.
/// </summary>
public partial class HostManagementViewModel : ObservableObject, IDisposable
{
    private bool _isDisposed;
    private readonly IHostRepository _hostRepo;
    private readonly IGroupRepository _groupRepo;
    private readonly IHostProfileRepository _hostProfileRepo;
    private readonly IProxyJumpProfileRepository _proxyJumpRepo;
    private readonly IPortForwardingProfileRepository _portForwardingRepo;
    private readonly ITagRepository _tagRepo;
    private readonly ISecretProtector _secretProtector;
    private readonly ISerialConnectionService _serialConnectionService;
    private readonly IAgentDiagnosticsService? _agentDiagnosticsService;
    private readonly ILogger<HostManagementViewModel> _logger;

    private CancellationTokenSource? _searchCancellationTokenSource;

    [ObservableProperty]
    private ObservableCollection<HostEntry> _hosts = [];

    [ObservableProperty]
    private ObservableCollection<HostGroup> _groups = [];

    [ObservableProperty]
    private ObservableCollection<Tag> _allTags = [];

    [ObservableProperty]
    private ObservableCollection<Tag> _selectedFilterTags = [];

    /// <summary>
    /// Gets whether there are any hosts in the collection.
    /// </summary>
    public bool HasHosts => Hosts.Count > 0;

    [ObservableProperty]
    private HostEntry? _selectedHost;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private HostGroup? _selectedGroupFilter;

    public HostManagementViewModel(
        IHostRepository hostRepo,
        IGroupRepository groupRepo,
        IHostProfileRepository hostProfileRepo,
        IProxyJumpProfileRepository proxyJumpRepo,
        IPortForwardingProfileRepository portForwardingRepo,
        ITagRepository tagRepo,
        ISecretProtector secretProtector,
        ISerialConnectionService serialConnectionService,
        IAgentDiagnosticsService? agentDiagnosticsService = null,
        ILogger<HostManagementViewModel>? logger = null)
    {
        _hostRepo = hostRepo;
        _groupRepo = groupRepo;
        _hostProfileRepo = hostProfileRepo;
        _proxyJumpRepo = proxyJumpRepo;
        _portForwardingRepo = portForwardingRepo;
        _tagRepo = tagRepo;
        _secretProtector = secretProtector;
        _serialConnectionService = serialConnectionService;
        _agentDiagnosticsService = agentDiagnosticsService;
        _logger = logger ?? NullLogger<HostManagementViewModel>.Instance;

        // Subscribe to initial collection changes
        _hosts.CollectionChanged += OnHostsCollectionChanged;

        _logger.LogDebug("HostManagementViewModel initialized");
    }

    /// <summary>
    /// Called when the Hosts collection is replaced.
    /// </summary>
    partial void OnHostsChanged(ObservableCollection<HostEntry>? oldValue, ObservableCollection<HostEntry> newValue)
    {
        if (oldValue != null)
        {
            oldValue.CollectionChanged -= OnHostsCollectionChanged;
        }
        newValue.CollectionChanged += OnHostsCollectionChanged;
        OnPropertyChanged(nameof(HasHosts));
    }

    private void OnHostsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasHosts));
    }

    /// <summary>
    /// Loads hosts and groups from the database.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    public async Task LoadDataAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Loading hosts and groups from database");
        IsLoading = true;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            var hosts = await _hostRepo.GetAllAsync(cancellationToken);
            Hosts = new ObservableCollection<HostEntry>(hosts);
            _logger.LogInformation("Loaded {HostCount} hosts from database", hosts.Count);

            cancellationToken.ThrowIfCancellationRequested();
            
            var groups = await _groupRepo.GetAllAsync(cancellationToken);
            Groups = new ObservableCollection<HostGroup>(groups);
            _logger.LogInformation("Loaded {GroupCount} groups from database", groups.Count);

            await LoadTagsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("LoadDataAsync was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load data from database");
            throw;
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Loads all tags from the database.
    /// </summary>
    /// <param name="cancellationToken">Optional cancellation token.</param>
    private async Task LoadTagsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var tags = await _tagRepo.GetAllAsync(cancellationToken);
            AllTags = new ObservableCollection<Tag>(tags);
            _logger.LogInformation("Loaded {TagCount} tags from database", tags.Count);
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("LoadTagsAsync was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load tags from database");
            throw;
        }
    }

    /// <summary>
    /// Refreshes the hosts list based on current search text, group filter, and tag filter.
    /// </summary>
    public async Task RefreshHostsAsync()
    {
        IsLoading = true;
        try
        {
            IEnumerable<HostEntry> hosts;

            // Get tag IDs from selected filter tags
            var tagIds = SelectedFilterTags.Any()
                ? SelectedFilterTags.Select(t => t.Id).ToList()
                : null;

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                // If tags are selected, use SearchAsync with empty search term to apply tag filter
                if (tagIds != null)
                {
                    hosts = await _hostRepo.SearchAsync(null, tagIds);
                }
                else
                {
                    hosts = await _hostRepo.GetAllAsync();
                }
            }
            else
            {
                hosts = await _hostRepo.SearchAsync(SearchText, tagIds);
            }

            // Apply group filter if selected
            if (SelectedGroupFilter != null)
            {
                hosts = hosts.Where(h => h.GroupId == SelectedGroupFilter.Id);
            }

            Hosts = new ObservableCollection<HostEntry>(hosts);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SearchAsync(CancellationToken cancellationToken = default)
    {
        IsLoading = true;
        try
        {
            // Get tag IDs from selected filter tags
            var tagIds = SelectedFilterTags.Any()
                ? SelectedFilterTags.Select(t => t.Id).ToList()
                : null;

            var hosts = string.IsNullOrWhiteSpace(SearchText)
                ? (tagIds != null ? await _hostRepo.SearchAsync(null, tagIds) : await _hostRepo.GetAllAsync())
                : await _hostRepo.SearchAsync(SearchText, tagIds);

            // Check if cancelled before updating UI
            if (cancellationToken.IsCancellationRequested)
                return;

            // Marshal to UI thread for ObservableCollection update
            Application.Current.Dispatcher.Invoke(() =>
            {
                Hosts = new ObservableCollection<HostEntry>(hosts);
            });
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        // Cancel any pending search
        _searchCancellationTokenSource?.Cancel();
        _searchCancellationTokenSource?.Dispose();
        _searchCancellationTokenSource = new CancellationTokenSource();

        var cts = _searchCancellationTokenSource;

        // Debounce search with 300ms delay
        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(300, cts.Token);
                await SearchAsync(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when user types quickly - search was cancelled
                _logger.LogDebug("Search cancelled due to new input");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during debounced search");
            }
        });
    }

    [RelayCommand]
    private async Task FilterByGroupAsync(HostGroup? group)
    {
        SelectedGroupFilter = group;
        await RefreshHostsAsync();
    }

    /// <summary>
    /// Toggles a tag filter on or off.
    /// </summary>
    [RelayCommand]
    private async Task ToggleTagFilterAsync(Tag tag)
    {
        if (tag == null) return;

        if (SelectedFilterTags.Contains(tag))
        {
            SelectedFilterTags.Remove(tag);
            _logger.LogDebug("Removed tag filter: {TagName}", tag.Name);
        }
        else
        {
            SelectedFilterTags.Add(tag);
            _logger.LogDebug("Added tag filter: {TagName}", tag.Name);
        }

        await RefreshHostsAsync();
    }

    /// <summary>
    /// Gets the total host count for each group from the database (unfiltered).
    /// Returns a dictionary mapping group ID to host count.
    /// </summary>
    public async Task<(int totalCount, Dictionary<Guid, int> countsByGroup)> GetTotalHostCountsAsync()
    {
        var allHosts = await _hostRepo.GetAllAsync();
        var totalCount = allHosts.Count;
        var countsByGroup = allHosts
            .Where(h => h.GroupId.HasValue)
            .GroupBy(h => h.GroupId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());
        return (totalCount, countsByGroup);
    }

    [RelayCommand]
    private async Task AddHostAsync()
    {
        var viewModel = new HostDialogViewModel(
            _secretProtector,
            _serialConnectionService,
            null,
            Groups,
            _hostProfileRepo,
            _proxyJumpRepo,
            _portForwardingRepo,
            _tagRepo,
            null,
            _agentDiagnosticsService);

        // Load profiles asynchronously
        await Task.WhenAll(
            viewModel.LoadHostProfilesAsync(),
            viewModel.LoadProxyJumpProfilesAsync());

        var dialog = new HostEditDialog(
            viewModel,
            _hostProfileRepo,
            _proxyJumpRepo,
            _portForwardingRepo,
            _hostRepo);
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var host = viewModel.GetHost();
            await _hostRepo.AddAsync(host);
            Hosts.Add(host);
            SelectedHost = host;
        }
    }

    [RelayCommand]
    private async Task EditHostAsync(HostEntry? host)
    {
        if (host == null) return;

        var viewModel = new HostDialogViewModel(
            _secretProtector,
            _serialConnectionService,
            host,
            Groups,
            _hostProfileRepo,
            _proxyJumpRepo,
            _portForwardingRepo,
            _tagRepo,
            null,
            _agentDiagnosticsService);

        // Load profiles and port forwarding count asynchronously
        await Task.WhenAll(
            viewModel.LoadHostProfilesAsync(),
            viewModel.LoadProxyJumpProfilesAsync(),
            viewModel.LoadPortForwardingCountAsync());

        var dialog = new HostEditDialog(
            viewModel,
            _hostProfileRepo,
            _proxyJumpRepo,
            _portForwardingRepo,
            _hostRepo);
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var updatedHost = viewModel.GetHost();
            await _hostRepo.UpdateAsync(updatedHost);

            // Refresh the host in the list
            var index = Hosts.IndexOf(host);
            if (index >= 0)
            {
                Hosts[index] = updatedHost;
            }
        }
    }

    [RelayCommand]
    private async Task DeleteHostAsync(HostEntry? host)
    {
        if (host == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the host '{host.DisplayName}'?\n\nThis will also delete all connection history for this host.",
            "Delete Host",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        await _hostRepo.DeleteAsync(host.Id);
        Hosts.Remove(host);

        if (SelectedHost == host)
            SelectedHost = null;
    }

    [RelayCommand]
    private void CopyHostname(HostEntry? host)
    {
        if (host == null) return;

        try
        {
            Clipboard.SetText(host.Hostname);
            _logger.LogDebug("Copied hostname to clipboard: {Hostname}", host.Hostname);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy hostname to clipboard");
        }
    }

    [RelayCommand]
    private async Task CopyIpAddressAsync(HostEntry? host)
    {
        if (host == null) return;

        try
        {
            // Check if hostname is already an IP address
            if (IPAddress.TryParse(host.Hostname, out var existingIp))
            {
                Clipboard.SetText(existingIp.ToString());
                _logger.LogDebug("Copied IP address to clipboard: {IpAddress}", existingIp);
                return;
            }

            // Resolve hostname to IP address
            var addresses = await Dns.GetHostAddressesAsync(host.Hostname);
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            var ip = ipv4 ?? addresses.FirstOrDefault();

            if (ip != null)
            {
                Clipboard.SetText(ip.ToString());
                _logger.LogDebug("Resolved and copied IP address to clipboard: {IpAddress}", ip);
            }
            else
            {
                MessageBox.Show(
                    $"Could not resolve IP address for '{host.Hostname}'.",
                    "DNS Resolution Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resolve or copy IP address for {Hostname}", host.Hostname);
            MessageBox.Show(
                $"Could not resolve IP address for '{host.Hostname}'.\n\n{ex.Message}",
                "DNS Resolution Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    [RelayCommand]
    private async Task DuplicateHostAsync(HostEntry? host)
    {
        if (host == null) return;

        try
        {
            var duplicatedHost = new HostEntry
            {
                Id = Guid.NewGuid(),
                DisplayName = $"{host.DisplayName} (Copy)",
                Hostname = host.Hostname,
                Port = host.Port,
                Username = host.Username,
                AuthType = host.AuthType,
                PrivateKeyPath = host.PrivateKeyPath,
                PasswordProtected = host.PasswordProtected,
                Notes = host.Notes,
                GroupId = host.GroupId,
                Group = host.Group,
                HostProfileId = host.HostProfileId,
                HostProfile = host.HostProfile,
                ProxyJumpProfileId = host.ProxyJumpProfileId,
                ProxyJumpProfile = host.ProxyJumpProfile,
                SortOrder = host.SortOrder + 1,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            await _hostRepo.AddAsync(duplicatedHost);
            Hosts.Add(duplicatedHost);
            SelectedHost = duplicatedHost;

            _logger.LogInformation("Duplicated host {OriginalDisplayName} as {NewDisplayName}",
                host.DisplayName, duplicatedHost.DisplayName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to duplicate host {HostId}", host.Id);
            MessageBox.Show(
                $"Failed to duplicate host.\n\n{ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task SaveHostAsync(HostEntry? host)
    {
        if (host == null) return;

        await _hostRepo.UpdateAsync(host);
    }

    /// <summary>
    /// Saves a transient host entry (one that was created without going through the Add Host dialog).
    /// Used for Serial Quick Connect when the user wants to save the connection.
    /// </summary>
    public async Task SaveTransientHostAsync(HostEntry host)
    {
        if (host == null) return;

        await _hostRepo.AddAsync(host);
        Hosts.Add(host);
        _logger.LogInformation("Saved transient host {DisplayName}", host.DisplayName);
    }

    [RelayCommand]
    private async Task AddGroupAsync()
    {
        var viewModel = new GroupDialogViewModel(null);
        var dialog = new GroupDialog(viewModel);
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var group = viewModel.GetGroup();
            await _groupRepo.AddAsync(group);
            Groups.Add(group);
        }
    }

    [RelayCommand]
    private async Task EditGroupAsync(HostGroup? group)
    {
        if (group == null) return;

        var viewModel = new GroupDialogViewModel(group);
        var dialog = new GroupDialog(viewModel);
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var updatedGroup = viewModel.GetGroup();
            await _groupRepo.UpdateAsync(updatedGroup);

            // Refresh the group in the list
            var index = Groups.IndexOf(group);
            if (index >= 0)
            {
                Groups[index] = updatedGroup;
            }
        }
    }

    [RelayCommand]
    private async Task DeleteGroupAsync(HostGroup? group)
    {
        if (group == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the group '{group.Name}'?\n\nHosts in this group will be moved to 'Ungrouped'.",
            "Delete Group",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await _groupRepo.DeleteAsync(group.Id);
            Groups.Remove(group);

            // Update hosts that belonged to this group
            foreach (var host in Hosts.Where(h => h.GroupId == group.Id))
            {
                host.GroupId = null;
                host.Group = null;
            }
        }
    }

    /// <summary>
    /// Gets the host repository for external access.
    /// </summary>
    public IHostRepository HostRepository => _hostRepo;

    /// <summary>
    /// Gets the group repository for external access.
    /// </summary>
    public IGroupRepository GroupRepository => _groupRepo;

    /// <summary>
    /// Gets the proxy jump profile repository for external access.
    /// </summary>
    public IProxyJumpProfileRepository ProxyJumpProfileRepository => _proxyJumpRepo;

    /// <summary>
    /// Gets the port forwarding profile repository for external access.
    /// </summary>
    public IPortForwardingProfileRepository PortForwardingProfileRepository => _portForwardingRepo;

    /// <summary>
    /// Handles drag-and-drop reordering of hosts.
    /// </summary>
    [RelayCommand]
    private async Task ReorderHostsAsync(DragDropReorderEventArgs args)
    {
        if (args.DroppedItem == null)
            return;

        _logger.LogDebug("Reordering host {DroppedHostId} to target {TargetHostId}",
            args.DroppedItem.Id,
            args.TargetItem?.Id);

        try
        {
            var droppedHost = args.DroppedItem;
            var targetHost = args.TargetItem;

            // Skip if dropped on itself
            if (targetHost != null && droppedHost.Id == targetHost.Id)
            {
                _logger.LogDebug("Host dropped on itself, skipping reorder");
                return;
            }

            // Determine the target group (use target's group if different, otherwise keep same)
            var targetGroupId = targetHost?.GroupId ?? droppedHost.GroupId;
            var isChangingGroup = droppedHost.GroupId != targetGroupId;

            if (isChangingGroup)
            {
                _logger.LogDebug("Moving host to different group: {OldGroupId} -> {NewGroupId}",
                    droppedHost.GroupId,
                    targetGroupId);

                droppedHost.GroupId = targetGroupId;
                droppedHost.Group = targetHost?.Group;

                // Save the group change to database
                await _hostRepo.UpdateAsync(droppedHost);
            }

            // Get all hosts in the target group, excluding the dropped host
            var targetGroupHosts = Hosts
                .Where(h => h.GroupId == targetGroupId && h.Id != droppedHost.Id)
                .OrderBy(h => h.SortOrder)
                .ToList();

            int insertIndex;

            if (targetHost == null)
            {
                // Dropped on empty space - move to end
                insertIndex = targetGroupHosts.Count;
            }
            else
            {
                // Find where to insert based on target position
                insertIndex = targetGroupHosts.FindIndex(h => h.Id == targetHost.Id);
                if (insertIndex == -1)
                {
                    // Target not found (shouldn't happen), add to end
                    insertIndex = targetGroupHosts.Count;
                }
                else
                {
                    // Determine if dropping above or below target based on Y position
                    // Use 30 pixels as threshold (roughly half a card height)
                    bool insertAfter = args.DropPosition.Y > 30;
                    if (insertAfter)
                    {
                        insertIndex++;
                    }
                }
            }

            // Insert the dropped host at the calculated position
            targetGroupHosts.Insert(insertIndex, droppedHost);

            // Update sort orders for all hosts in the group
            var reorderList = new List<(Guid Id, int SortOrder)>();
            for (int i = 0; i < targetGroupHosts.Count; i++)
            {
                var host = targetGroupHosts[i];
                if (host.SortOrder != i)
                {
                    host.SortOrder = i;
                    reorderList.Add((host.Id, i));
                }
            }

            // Only save if there are actual changes
            if (reorderList.Count > 0)
            {
                await _hostRepo.ReorderHostsAsync(reorderList);
                _logger.LogInformation("Successfully reordered {Count} hosts in group {GroupId}",
                    reorderList.Count, targetGroupId);
            }

            // Refresh the list to show new order
            await RefreshHostsAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to reorder hosts");
            MessageBox.Show(
                "Failed to reorder hosts. Please try again.",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        _searchCancellationTokenSource?.Cancel();
        _searchCancellationTokenSource?.Dispose();
        _searchCancellationTokenSource = null;
    }
}
