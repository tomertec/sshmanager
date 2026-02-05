using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.App.Services;
using SshManager.Core.Models;

namespace SshManager.App.ViewModels;

public partial class SshConfigImportViewModel : ObservableObject
{
    private readonly ISshConfigParser _parser;

    [ObservableProperty]
    private string _configFilePath = "";

    [ObservableProperty]
    private ObservableCollection<SshConfigHostItem> _hosts = [];

    [ObservableProperty]
    private ObservableCollection<string> _warnings = [];

    [ObservableProperty]
    private ObservableCollection<string> _errors = [];

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _hasHosts;

    [ObservableProperty]
    private bool _hasWarnings;

    [ObservableProperty]
    private bool _hasErrors;

    [ObservableProperty]
    private int _selectedCount;

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    public SshConfigImportViewModel(ISshConfigParser parser)
    {
        _parser = parser;
        ConfigFilePath = parser.GetDefaultConfigPath();
    }

    [RelayCommand]
    private void Browse()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select SSH Config File",
            Filter = "All files (*.*)|*.*|Config files (config)|config",
            FilterIndex = 1,
            FileName = "config",
            InitialDirectory = System.IO.Path.GetDirectoryName(ConfigFilePath)
        };

        if (dialog.ShowDialog() == true)
        {
            ConfigFilePath = dialog.FileName;
            _ = ParseConfigAsync().ContinueWith(t =>
                System.Diagnostics.Debug.WriteLine($"Config parse error: {t.Exception}"),
                TaskContinuationOptions.OnlyOnFaulted);
        }
    }

    [RelayCommand]
    private async Task ParseConfigAsync()
    {
        if (string.IsNullOrWhiteSpace(ConfigFilePath))
            return;

        IsLoading = true;
        Hosts.Clear();
        Warnings.Clear();
        Errors.Clear();

        try
        {
            var result = await _parser.ParseAsync(ConfigFilePath);

            foreach (var host in result.Hosts)
            {
                Hosts.Add(new SshConfigHostItem(host) { IsSelected = true });
            }

            foreach (var warning in result.Warnings)
            {
                Warnings.Add(warning);
            }

            foreach (var error in result.Errors)
            {
                Errors.Add(error);
            }

            HasHosts = Hosts.Count > 0;
            HasWarnings = Warnings.Count > 0;
            HasErrors = Errors.Count > 0;
            UpdateSelectedCount();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var host in Hosts)
        {
            host.IsSelected = true;
        }
        UpdateSelectedCount();
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var host in Hosts)
        {
            host.IsSelected = false;
        }
        UpdateSelectedCount();
    }

    public void UpdateSelectedCount()
    {
        SelectedCount = Hosts.Count(h => h.IsSelected);
    }

    [RelayCommand]
    private void Import()
    {
        DialogResult = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    public List<HostEntry> GetSelectedHosts()
    {
        return Hosts
            .Where(h => h.IsSelected)
            .Select(h => h.Host.ToHostEntry())
            .ToList();
    }

    /// <summary>
    /// Gets the selected hosts as import items with full advanced configuration data.
    /// </summary>
    public List<SshConfigImportItem> GetSelectedImportItems()
    {
        return Hosts
            .Where(h => h.IsSelected)
            .Select(h => new SshConfigImportItem
            {
                HostEntry = h.Host.ToHostEntry(),
                ConfigHost = h.Host
            })
            .ToList();
    }

    /// <summary>
    /// Gets the count of hosts with advanced configuration (ProxyJump or port forwarding).
    /// </summary>
    public int AdvancedConfigHostCount => Hosts.Count(h => h.IsSelected && h.HasAdvancedConfig);
}

/// <summary>
/// Wrapper for SshConfigHost with selection state.
/// </summary>
public partial class SshConfigHostItem : ObservableObject
{
    public SshConfigHost Host { get; }

    [ObservableProperty]
    private bool _isSelected;

    public string DisplayAlias => Host.Alias;
    public string DisplayHostname => Host.Hostname ?? Host.Alias;
    public int DisplayPort => Host.Port;
    public string DisplayUser => Host.User ?? Environment.UserName;
    public string DisplayAuthType => !string.IsNullOrEmpty(Host.IdentityFile) ? "Private Key" : "SSH Agent";

    /// <summary>
    /// Whether this host has ProxyJump or port forwarding configuration.
    /// </summary>
    public bool HasAdvancedConfig => Host.HasAdvancedConfig;

    /// <summary>
    /// Display string for ProxyJump configuration.
    /// </summary>
    public string? DisplayProxyJump => Host.ProxyJump;

    /// <summary>
    /// Display string summarizing port forwarding configuration.
    /// </summary>
    public string DisplayPortForwarding
    {
        get
        {
            var parts = new List<string>();

            if (Host.LocalForwards.Count > 0)
                parts.Add($"{Host.LocalForwards.Count} local");

            if (Host.RemoteForwards.Count > 0)
                parts.Add($"{Host.RemoteForwards.Count} remote");

            if (Host.DynamicForwards.Count > 0)
                parts.Add($"{Host.DynamicForwards.Count} dynamic");

            return parts.Count > 0 ? string.Join(", ", parts) : string.Empty;
        }
    }

    /// <summary>
    /// Detailed tooltip for port forwarding.
    /// </summary>
    public string PortForwardingTooltip
    {
        get
        {
            var lines = new List<string>();

            foreach (var lf in Host.LocalForwards)
            {
                lines.Add($"L: {lf.BindAddress}:{lf.LocalPort} → {lf.RemoteHost}:{lf.RemotePort}");
            }

            foreach (var rf in Host.RemoteForwards)
            {
                lines.Add($"R: {rf.BindAddress}:{rf.RemotePort} → {rf.LocalHost}:{rf.LocalPort}");
            }

            foreach (var df in Host.DynamicForwards)
            {
                lines.Add($"D: {df.BindAddress}:{df.Port} (SOCKS)");
            }

            return lines.Count > 0 ? string.Join("\n", lines) : string.Empty;
        }
    }

    public SshConfigHostItem(SshConfigHost host)
    {
        Host = host;
    }
}
