using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using H.NotifyIcon;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;

namespace SshManager.App.Services;

/// <summary>
/// System tray service for quick host connections.
/// </summary>
public class SystemTrayService : ISystemTrayService
{
    private readonly ILogger<SystemTrayService> _logger;
    private TaskbarIcon? _taskbarIcon;
    private ContextMenu? _contextMenu;
    private IReadOnlyList<HostEntry> _hosts = Array.Empty<HostEntry>();
    private IReadOnlyList<HostGroup> _groups = Array.Empty<HostGroup>();
    private bool _disposed;

    public event EventHandler<HostEntry>? QuickConnectRequested;
    public event EventHandler? ShowWindowRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler? SettingsRequested;

    public SystemTrayService(ILogger<SystemTrayService>? logger = null)
    {
        _logger = logger ?? NullLogger<SystemTrayService>.Instance;
    }

    public void Initialize()
    {
        if (_taskbarIcon != null) return;

        _logger.LogDebug("Initializing system tray service");

        _contextMenu = new ContextMenu();
        BuildContextMenu();

        _taskbarIcon = new TaskbarIcon
        {
            ToolTipText = "SSH Manager",
            ContextMenu = _contextMenu
        };

        // Try to load icon from application resources
        try
        {
            var iconUri = new Uri("pack://application:,,,/Resources/app-icon.ico", UriKind.Absolute);
            var iconStream = Application.GetResourceStream(iconUri);
            if (iconStream != null)
            {
                _taskbarIcon.Icon = new Icon(iconStream.Stream);
            }
            else
            {
                _taskbarIcon.Icon = SystemIcons.Application;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load tray icon, using default");
            _taskbarIcon.Icon = SystemIcons.Application;
        }

        // Handle left-click to show window (more discoverable than double-click)
        _taskbarIcon.TrayLeftMouseUp += (s, e) =>
        {
            _logger.LogDebug("Tray icon clicked, showing window");
            ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        };

        // Also handle double-click for consistency
        _taskbarIcon.TrayMouseDoubleClick += (s, e) =>
        {
            _logger.LogDebug("Tray icon double-clicked, showing window");
            ShowWindowRequested?.Invoke(this, EventArgs.Empty);
        };

        _logger.LogInformation("System tray service initialized");
    }

    public void UpdateContextMenu(IEnumerable<HostEntry> hosts, IEnumerable<HostGroup> groups)
    {
        _hosts = hosts.ToList();
        _groups = groups.ToList();
        BuildContextMenu();
    }

    private void BuildContextMenu()
    {
        if (_contextMenu == null) return;

        Application.Current.Dispatcher.Invoke(() =>
        {
            _contextMenu.Items.Clear();

            // Header
            var headerItem = new MenuItem
            {
                Header = "SSH Manager",
                IsEnabled = false,
                FontWeight = FontWeights.Bold
            };
            _contextMenu.Items.Add(headerItem);
            _contextMenu.Items.Add(new Separator());

            // Show Window
            var showItem = new MenuItem { Header = "Show Window" };
            showItem.Click += (s, e) => ShowWindowRequested?.Invoke(this, EventArgs.Empty);
            _contextMenu.Items.Add(showItem);

            _contextMenu.Items.Add(new Separator());

            // Hosts by group
            if (_hosts.Any())
            {
                // Group hosts by group
                var groupedHosts = _hosts.GroupBy(h => h.GroupId);
                var ungroupedHosts = _hosts.Where(h => h.GroupId == null).ToList();

                // Add grouped hosts
                foreach (var group in _groups.OrderBy(g => g.SortOrder))
                {
                    var hostsInGroup = _hosts.Where(h => h.GroupId == group.Id).ToList();
                    if (hostsInGroup.Count == 0) continue;

                    var groupMenu = new MenuItem { Header = group.Name };

                    foreach (var host in hostsInGroup.OrderBy(h => h.DisplayName))
                    {
                        var hostItem = CreateHostMenuItem(host);
                        groupMenu.Items.Add(hostItem);
                    }

                    _contextMenu.Items.Add(groupMenu);
                }

                // Add ungrouped hosts
                if (ungroupedHosts.Count > 0)
                {
                    if (_groups.Any())
                    {
                        var ungroupedMenu = new MenuItem { Header = "Ungrouped" };
                        foreach (var host in ungroupedHosts.OrderBy(h => h.DisplayName))
                        {
                            var hostItem = CreateHostMenuItem(host);
                            ungroupedMenu.Items.Add(hostItem);
                        }
                        _contextMenu.Items.Add(ungroupedMenu);
                    }
                    else
                    {
                        // No groups, add hosts directly
                        foreach (var host in ungroupedHosts.OrderBy(h => h.DisplayName))
                        {
                            var hostItem = CreateHostMenuItem(host);
                            _contextMenu.Items.Add(hostItem);
                        }
                    }
                }
            }
            else
            {
                var noHostsItem = new MenuItem
                {
                    Header = "(No hosts configured)",
                    IsEnabled = false
                };
                _contextMenu.Items.Add(noHostsItem);
            }

            _contextMenu.Items.Add(new Separator());

            // Settings
            var settingsItem = new MenuItem { Header = "Settings..." };
            settingsItem.Click += (s, e) => SettingsRequested?.Invoke(this, EventArgs.Empty);
            _contextMenu.Items.Add(settingsItem);

            _contextMenu.Items.Add(new Separator());

            // Exit
            var exitItem = new MenuItem { Header = "Exit" };
            exitItem.Click += (s, e) => ExitRequested?.Invoke(this, EventArgs.Empty);
            _contextMenu.Items.Add(exitItem);
        });
    }

    private MenuItem CreateHostMenuItem(HostEntry host)
    {
        var item = new MenuItem
        {
            Header = host.DisplayName,
            ToolTip = $"{host.Username}@{host.Hostname}:{host.Port}"
        };

        item.Click += (s, e) =>
        {
            _logger.LogInformation("Quick connect requested for host {DisplayName}", host.DisplayName);
            QuickConnectRequested?.Invoke(this, host);
        };

        return item;
    }

    public void Show()
    {
        if (_taskbarIcon != null)
        {
            _taskbarIcon.Visibility = Visibility.Visible;
            _logger.LogDebug("Tray icon shown");
        }
    }

    public void Hide()
    {
        if (_taskbarIcon != null)
        {
            _taskbarIcon.Visibility = Visibility.Collapsed;
            _logger.LogDebug("Tray icon hidden");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _logger.LogDebug("Disposing system tray service");

        if (_taskbarIcon != null)
        {
            _taskbarIcon.Dispose();
            _taskbarIcon = null;
        }

        _logger.LogInformation("System tray service disposed");
    }
}
