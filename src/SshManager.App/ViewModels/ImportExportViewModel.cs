using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using SshManager.Core.Models;
using SshManager.App.Services;
using SshManager.App.Views.Dialogs;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel responsible for host data import/export operations.
/// Handles exporting hosts to JSON and importing from JSON, SSH config, and PuTTY.
/// </summary>
public partial class ImportExportViewModel : ObservableObject
{
    private readonly IExportImportService _exportImportService;
    private readonly ISshConfigExportService _sshConfigExportService;
    private readonly HostManagementViewModel _hostManagement;
    private readonly ISshConfigParser _sshConfigParser;
    private readonly IPuttySessionImporter _puttyImporter;
    private readonly ILogger<ImportExportViewModel> _logger;

    /// <summary>
    /// Event raised when hosts have been imported successfully.
    /// </summary>
    public event EventHandler? HostsImported;

    public ImportExportViewModel(
        IExportImportService exportImportService,
        ISshConfigExportService sshConfigExportService,
        HostManagementViewModel hostManagement,
        ISshConfigParser sshConfigParser,
        IPuttySessionImporter puttyImporter,
        ILogger<ImportExportViewModel>? logger = null)
    {
        _exportImportService = exportImportService;
        _sshConfigExportService = sshConfigExportService;
        _hostManagement = hostManagement;
        _sshConfigParser = sshConfigParser;
        _puttyImporter = puttyImporter;
        _logger = logger ?? NullLogger<ImportExportViewModel>.Instance;

        _logger.LogDebug("ImportExportViewModel initialized");
    }

    /// <summary>
    /// Gets the hosts collection from HostManagementViewModel for export.
    /// </summary>
    private ObservableCollection<HostEntry> Hosts => _hostManagement.Hosts;

    /// <summary>
    /// Gets the groups collection from HostManagementViewModel for export.
    /// </summary>
    private ObservableCollection<HostGroup> Groups => _hostManagement.Groups;

    /// <summary>
    /// Exports all hosts and groups to a JSON file.
    /// </summary>
    public async Task ExportHostsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export SSH Hosts",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*",
            DefaultExt = "json",
            FileName = $"sshmanager-export-{DateTime.Now:yyyy-MM-dd}"
        };

        if (dialog.ShowDialog() == true)
        {
            _logger.LogInformation("Exporting hosts to {FilePath}", dialog.FileName);
            try
            {
                await _exportImportService.ExportAsync(dialog.FileName, Hosts, Groups);
                _logger.LogInformation("Successfully exported {HostCount} hosts and {GroupCount} groups to {FilePath}",
                    Hosts.Count, Groups.Count, dialog.FileName);
                MessageBox.Show(
                    $"Successfully exported {Hosts.Count} hosts and {Groups.Count} groups.",
                    "Export Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to export hosts to {FilePath}", dialog.FileName);
                MessageBox.Show(
                    $"Failed to export: {ex.Message}\n\nCheck logs for details.",
                    "Export Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Imports hosts and groups from a JSON file.
    /// </summary>
    public async Task ImportHostsAsync()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Import SSH Hosts",
            Filter = "JSON Files (*.json)|*.json|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            _logger.LogInformation("Importing hosts from {FilePath}", dialog.FileName);
            try
            {
                var (hosts, groups) = await _exportImportService.ImportAsync(dialog.FileName);
                _logger.LogDebug("Parsed {HostCount} hosts and {GroupCount} groups from import file", hosts.Count, groups.Count);

                var result = MessageBox.Show(
                    $"Import will add {hosts.Count} hosts and {groups.Count} groups.\n\n" +
                    "Note: Passwords are not imported for security reasons.\n" +
                    "You will need to re-enter passwords for hosts that use password authentication.\n\n" +
                    "Do you want to continue?",
                    "Confirm Import",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    // Add groups first
                    foreach (var group in groups)
                    {
                        await _hostManagement.GroupRepository.AddAsync(group);
                        Groups.Add(group);
                    }
                    _logger.LogDebug("Added {GroupCount} groups to database", groups.Count);

                    // Then add hosts
                    foreach (var host in hosts)
                    {
                        await _hostManagement.HostRepository.AddAsync(host);
                        Hosts.Add(host);
                    }
                    _logger.LogDebug("Added {HostCount} hosts to database", hosts.Count);

                    _logger.LogInformation("Successfully imported {HostCount} hosts and {GroupCount} groups from {FilePath}",
                        hosts.Count, groups.Count, dialog.FileName);
                    MessageBox.Show(
                        $"Successfully imported {hosts.Count} hosts and {groups.Count} groups.",
                        "Import Complete",
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);

                    // Raise event to notify that hosts were imported
                    OnHostsImported();
                }
                else
                {
                    _logger.LogInformation("Import cancelled by user");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import hosts from {FilePath}", dialog.FileName);
                MessageBox.Show(
                    $"Failed to import: {ex.Message}\n\nCheck logs for details.",
                    "Import Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    /// <summary>
    /// Exports hosts to an SSH config file format.
    /// </summary>
    public async Task ExportToSshConfigAsync()
    {
        _logger.LogInformation("Opening SSH config export dialog");

        var viewModel = new SshConfigExportDialogViewModel(_sshConfigExportService, _hostManagement.HostRepository);
        var dialog = new SshConfigExportDialog(viewModel)
        {
            Owner = Application.Current.MainWindow
        };

        // Initialize the view model (loads hosts and generates preview)
        await viewModel.InitializeAsync();

        dialog.ShowDialog();

        _logger.LogDebug("SSH config export dialog closed with result: {Result}", viewModel.DialogResult);
    }

    /// <summary>
    /// Imports hosts from an SSH config file (~/.ssh/config).
    /// </summary>
    public async Task ImportFromSshConfigAsync()
    {
        _logger.LogInformation("Opening SSH config import dialog");

        var importItems = await PromptForSshConfigImportSelectionAsync();
        if (importItems == null || importItems.Count == 0)
        {
            return;
        }

        _logger.LogInformation("Importing {HostCount} hosts from SSH config", importItems.Count);

        try
        {
            var stats = await PerformSshConfigImportAsync(importItems);
            ShowImportSuccessMessage(stats);
            OnHostsImported();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import hosts from SSH config");
            MessageBox.Show(
                $"Failed to import: {ex.Message}\n\nCheck logs for details.",
                "Import Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    /// <summary>
    /// Prompts the user to select SSH config hosts to import.
    /// </summary>
    /// <returns>Selected import items, or null if cancelled.</returns>
    private async Task<List<SshConfigImportItem>?> PromptForSshConfigImportSelectionAsync()
    {
        var viewModel = new SshConfigImportViewModel(_sshConfigParser);
        var dialog = new SshConfigImportDialog(viewModel);
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() != true)
        {
            _logger.LogInformation("SSH config import cancelled by user");
            return null;
        }

        var importItems = dialog.GetSelectedImportItems();
        if (importItems.Count == 0)
        {
            _logger.LogInformation("No hosts selected for import");
            return null;
        }

        return await Task.FromResult(importItems);
    }

    /// <summary>
    /// Performs the SSH config import operation for the selected items.
    /// </summary>
    /// <returns>Import statistics.</returns>
    private async Task<SshConfigImportStats> PerformSshConfigImportAsync(List<SshConfigImportItem> importItems)
    {
        var stats = new SshConfigImportStats();

        // First pass: Import all hosts and build alias mapping
        var aliasToHost = await ImportHostsAndBuildAliasMapAsync(importItems, stats);

        // Second pass: Create port forwarding profiles
        await CreatePortForwardingProfilesAsync(importItems, stats);

        // Third pass: Handle ProxyJump configurations
        await CreateProxyJumpProfilesAsync(importItems, aliasToHost, stats);

        _logger.LogInformation(
            "Successfully imported {HostCount} hosts, {ProxyCount} proxy profiles, {ForwardCount} port forwarding profiles from SSH config",
            stats.HostsImported, stats.ProxyJumpProfilesCreated, stats.PortForwardingProfilesCreated);

        return stats;
    }

    /// <summary>
    /// Imports all hosts and builds a mapping from alias to host entry for ProxyJump resolution.
    /// </summary>
    private async Task<Dictionary<string, HostEntry>> ImportHostsAndBuildAliasMapAsync(
        List<SshConfigImportItem> importItems,
        SshConfigImportStats stats)
    {
        var aliasToHost = new Dictionary<string, HostEntry>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in importItems)
        {
            await _hostManagement.HostRepository.AddAsync(item.HostEntry);
            Hosts.Add(item.HostEntry);
            aliasToHost[item.ConfigHost.Alias] = item.HostEntry;
            stats.HostsImported++;
        }

        return aliasToHost;
    }

    /// <summary>
    /// Creates port forwarding profiles for all imported hosts.
    /// </summary>
    private async Task CreatePortForwardingProfilesAsync(
        List<SshConfigImportItem> importItems,
        SshConfigImportStats stats)
    {
        foreach (var item in importItems)
        {
            var configHost = item.ConfigHost;
            var host = item.HostEntry;

            await CreateLocalForwardProfilesAsync(configHost, host, stats);
            await CreateRemoteForwardProfilesAsync(configHost, host, stats);
            await CreateDynamicForwardProfilesAsync(configHost, host, stats);
        }
    }

    /// <summary>
    /// Creates LocalForward profiles for a host.
    /// </summary>
    private async Task CreateLocalForwardProfilesAsync(
        SshConfigHost configHost,
        HostEntry host,
        SshConfigImportStats stats)
    {
        foreach (var lf in configHost.LocalForwards)
        {
            var profile = new PortForwardingProfile
            {
                DisplayName = $"L:{lf.LocalPort}→{lf.RemoteHost}:{lf.RemotePort}",
                Description = $"Imported from SSH config - Local forward",
                ForwardingType = PortForwardingType.LocalForward,
                LocalBindAddress = lf.BindAddress,
                LocalPort = lf.LocalPort,
                RemoteHost = lf.RemoteHost,
                RemotePort = lf.RemotePort,
                HostId = host.Id,
                IsEnabled = true,
                AutoStart = false
            };

            await _hostManagement.PortForwardingProfileRepository.AddAsync(profile);
            stats.PortForwardingProfilesCreated++;
            _logger.LogDebug("Created LocalForward profile for host {HostName}: {ProfileName}",
                host.DisplayName, profile.DisplayName);
        }
    }

    /// <summary>
    /// Creates RemoteForward profiles for a host.
    /// </summary>
    private async Task CreateRemoteForwardProfilesAsync(
        SshConfigHost configHost,
        HostEntry host,
        SshConfigImportStats stats)
    {
        foreach (var rf in configHost.RemoteForwards)
        {
            var profile = new PortForwardingProfile
            {
                DisplayName = $"R:{rf.RemotePort}→{rf.LocalHost}:{rf.LocalPort}",
                Description = $"Imported from SSH config - Remote forward",
                ForwardingType = PortForwardingType.RemoteForward,
                LocalBindAddress = rf.BindAddress,
                LocalPort = rf.LocalPort,
                RemoteHost = rf.LocalHost,
                RemotePort = rf.RemotePort,
                HostId = host.Id,
                IsEnabled = true,
                AutoStart = false
            };

            await _hostManagement.PortForwardingProfileRepository.AddAsync(profile);
            stats.PortForwardingProfilesCreated++;
            _logger.LogDebug("Created RemoteForward profile for host {HostName}: {ProfileName}",
                host.DisplayName, profile.DisplayName);
        }
    }

    /// <summary>
    /// Creates DynamicForward profiles (SOCKS proxy) for a host.
    /// </summary>
    private async Task CreateDynamicForwardProfilesAsync(
        SshConfigHost configHost,
        HostEntry host,
        SshConfigImportStats stats)
    {
        foreach (var df in configHost.DynamicForwards)
        {
            var profile = new PortForwardingProfile
            {
                DisplayName = $"D:{df.Port} (SOCKS)",
                Description = $"Imported from SSH config - SOCKS proxy",
                ForwardingType = PortForwardingType.DynamicForward,
                LocalBindAddress = df.BindAddress,
                LocalPort = df.Port,
                RemoteHost = null,
                RemotePort = null,
                HostId = host.Id,
                IsEnabled = true,
                AutoStart = false
            };

            await _hostManagement.PortForwardingProfileRepository.AddAsync(profile);
            stats.PortForwardingProfilesCreated++;
            _logger.LogDebug("Created DynamicForward profile for host {HostName}: {ProfileName}",
                host.DisplayName, profile.DisplayName);
        }
    }

    /// <summary>
    /// Creates ProxyJump profiles for hosts that have ProxyJump configuration.
    /// </summary>
    private async Task CreateProxyJumpProfilesAsync(
        List<SshConfigImportItem> importItems,
        Dictionary<string, HostEntry> aliasToHost,
        SshConfigImportStats stats)
    {
        foreach (var item in importItems)
        {
            if (string.IsNullOrEmpty(item.ConfigHost.ProxyJump))
                continue;

            var host = item.HostEntry;
            var proxyJumpValue = item.ConfigHost.ProxyJump;

            var resolvedHops = ResolveProxyJumpHosts(proxyJumpValue, aliasToHost, host);

            if (resolvedHops != null)
            {
                await CreateAndAssociateProxyJumpProfileAsync(host, resolvedHops, stats);
            }
            else
            {
                await AddUnresolvedProxyJumpNoteAsync(host, proxyJumpValue);
            }
        }
    }

    /// <summary>
    /// Resolves ProxyJump host aliases to HostEntry objects.
    /// </summary>
    /// <returns>List of resolved hosts, or null if resolution failed.</returns>
    private List<HostEntry>? ResolveProxyJumpHosts(
        string proxyJumpValue,
        Dictionary<string, HostEntry> aliasToHost,
        HostEntry targetHost)
    {
        // ProxyJump can be comma-separated list of jump hosts
        var jumpHosts = proxyJumpValue.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var resolvedHops = new List<HostEntry>();

        foreach (var jumpAlias in jumpHosts)
        {
            // First check if it's in our just-imported hosts
            if (aliasToHost.TryGetValue(jumpAlias, out var jumpHost))
            {
                resolvedHops.Add(jumpHost);
            }
            // Check if it exists in the database already (by display name)
            else
            {
                var existingHost = Hosts.FirstOrDefault(h =>
                    h.DisplayName.Equals(jumpAlias, StringComparison.OrdinalIgnoreCase));

                if (existingHost != null)
                {
                    resolvedHops.Add(existingHost);
                }
                else
                {
                    _logger.LogWarning("ProxyJump host '{JumpAlias}' for '{HostName}' not found - skipping profile creation",
                        jumpAlias, targetHost.DisplayName);
                    return null;
                }
            }
        }

        return resolvedHops.Count > 0 ? resolvedHops : null;
    }

    /// <summary>
    /// Creates a ProxyJump profile and associates it with the target host.
    /// </summary>
    private async Task CreateAndAssociateProxyJumpProfileAsync(
        HostEntry host,
        List<HostEntry> resolvedHops,
        SshConfigImportStats stats)
    {
        var profileName = resolvedHops.Count == 1
            ? $"Jump via {resolvedHops[0].DisplayName}"
            : $"Jump chain: {string.Join(" → ", resolvedHops.Select(h => h.DisplayName))}";

        var proxyProfile = new ProxyJumpProfile
        {
            DisplayName = profileName,
            Description = $"Imported from SSH config for {host.DisplayName}",
            IsEnabled = true
        };

        // Add the hops
        for (int i = 0; i < resolvedHops.Count; i++)
        {
            proxyProfile.JumpHops.Add(new ProxyJumpHop
            {
                JumpHostId = resolvedHops[i].Id,
                SortOrder = i
            });
        }

        var savedProfile = await _hostManagement.ProxyJumpProfileRepository.AddAsync(proxyProfile);
        stats.ProxyJumpProfilesCreated++;

        // Associate the profile with the target host
        host.ProxyJumpProfileId = savedProfile.Id;
        await _hostManagement.HostRepository.UpdateAsync(host);

        _logger.LogDebug("Created ProxyJump profile '{ProfileName}' for host {HostName}",
            profileName, host.DisplayName);
    }

    /// <summary>
    /// Adds a note to the host about unresolved ProxyJump configuration.
    /// </summary>
    private async Task AddUnresolvedProxyJumpNoteAsync(HostEntry host, string proxyJumpValue)
    {
        var note = host.Notes ?? "";
        if (!string.IsNullOrEmpty(note))
            note += "\n\n";
        note += $"Note: ProxyJump '{proxyJumpValue}' could not be fully resolved during import.";
        host.Notes = note;
        await _hostManagement.HostRepository.UpdateAsync(host);
    }

    /// <summary>
    /// Shows the import success message with statistics.
    /// </summary>
    private void ShowImportSuccessMessage(SshConfigImportStats stats)
    {
        var message = $"Successfully imported {stats.HostsImported} host(s) from SSH config.";
        if (stats.ProxyJumpProfilesCreated > 0 || stats.PortForwardingProfilesCreated > 0)
        {
            message += $"\n\nAlso created:";
            if (stats.ProxyJumpProfilesCreated > 0)
                message += $"\n• {stats.ProxyJumpProfilesCreated} ProxyJump profile(s)";
            if (stats.PortForwardingProfilesCreated > 0)
                message += $"\n• {stats.PortForwardingProfilesCreated} port forwarding profile(s)";
        }

        MessageBox.Show(
            message,
            "Import Complete",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>
    /// Statistics for SSH config import operations.
    /// </summary>
    private class SshConfigImportStats
    {
        public int HostsImported { get; set; }
        public int ProxyJumpProfilesCreated { get; set; }
        public int PortForwardingProfilesCreated { get; set; }
    }

    /// <summary>
    /// Imports hosts from PuTTY sessions stored in the Windows registry.
    /// </summary>
    public async Task ImportFromPuttyAsync()
    {
        _logger.LogInformation("Opening PuTTY import dialog");

        var viewModel = new PuttyImportViewModel(_puttyImporter);
        var dialog = new PuttyImportDialog(viewModel);
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var hosts = dialog.GetSelectedHosts();
            if (hosts.Count == 0)
            {
                _logger.LogInformation("No PuTTY sessions selected for import");
                return;
            }

            _logger.LogInformation("Importing {HostCount} hosts from PuTTY", hosts.Count);

            try
            {
                foreach (var host in hosts)
                {
                    await _hostManagement.HostRepository.AddAsync(host);
                    Hosts.Add(host);
                }

                _logger.LogInformation("Successfully imported {HostCount} hosts from PuTTY", hosts.Count);
                MessageBox.Show(
                    $"Successfully imported {hosts.Count} host(s) from PuTTY.",
                    "Import Complete",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);

                // Raise event to notify that hosts were imported
                OnHostsImported();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to import hosts from PuTTY");
                MessageBox.Show(
                    $"Failed to import: {ex.Message}\n\nCheck logs for details.",
                    "Import Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
        else
        {
            _logger.LogInformation("PuTTY import cancelled by user");
        }
    }

    /// <summary>
    /// Raises the HostsImported event.
    /// </summary>
    protected virtual void OnHostsImported()
    {
        HostsImported?.Invoke(this, EventArgs.Empty);
    }
}
