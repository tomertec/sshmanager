using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SshManager.App.Models;
using SshManager.App.Services;
using SshManager.App.ViewModels;
using SshManager.App.Views.Controls;
using SshManager.App.Views.Dialogs;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal;
using SshManager.Terminal.Controls;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Windows;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ISystemTrayService _trayService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IPaneLayoutManager _paneLayoutManager;
    private readonly IProxyJumpService _proxyJumpService;
    private readonly IPortForwardingService _portForwardingService;
    private bool _minimizeToTray;
    private bool _isUpdatingGroupFilter;

    public MainWindow(
        MainWindowViewModel viewModel,
        ISystemTrayService trayService,
        ISettingsRepository settingsRepo,
        IPaneLayoutManager paneLayoutManager,
        IProxyJumpService proxyJumpService,
        IPortForwardingService portForwardingService)
    {
        _viewModel = viewModel;
        _trayService = trayService;
        _settingsRepo = settingsRepo;
        _paneLayoutManager = paneLayoutManager;
        _proxyJumpService = proxyJumpService;
        _portForwardingService = portForwardingService;
        DataContext = viewModel;

        InitializeComponent();

        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
        StateChanged += OnStateChanged;
        Closing += OnClosing;

        // Wire up system tray events
        _trayService.QuickConnectRequested += OnQuickConnectRequested;
        _trayService.ShowWindowRequested += OnShowWindowRequested;
        _trayService.ExitRequested += OnExitRequested;
        _trayService.SettingsRequested += OnSettingsRequested;

        // Subscribe to session creation for pane management
        _viewModel.SessionCreated += OnSessionCreated;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadDataAsync();

        // Load settings and update tray
        var settings = await _settingsRepo.GetAsync();
        _minimizeToTray = settings.MinimizeToTray;

        // Update tray menu with current hosts
        _trayService.UpdateContextMenu(_viewModel.Hosts, _viewModel.Groups);
        _trayService.Show();

        // Initialize group filter dropdown
        RefreshGroupFilter();

        // Subscribe to groups collection changes
        _viewModel.Groups.CollectionChanged += Groups_CollectionChanged;
        _viewModel.Hosts.CollectionChanged += Hosts_CollectionChanged;
    }

    private void Groups_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshGroupFilter();
    }

    private void Hosts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshGroupFilter();
    }

    private void RefreshGroupFilter()
    {
        _isUpdatingGroupFilter = true;
        try
        {
            var items = new List<GroupFilterItem>();

            // Add "All Groups" item
            items.Add(GroupFilterItem.CreateAllItem(_viewModel.Hosts.Count));

            // Add individual groups with host counts
            foreach (var group in _viewModel.Groups.OrderBy(g => g.Name))
            {
                var hostCount = _viewModel.Hosts.Count(h => h.GroupId == group.Id);
                items.Add(GroupFilterItem.CreateGroupItem(group, hostCount));
            }

            // Remember current selection
            var currentSelection = _viewModel.SelectedGroupFilter;

            GroupFilterCombo.ItemsSource = items;

            // Restore selection
            if (currentSelection != null)
            {
                var matchingItem = items.FirstOrDefault(i => i.Group?.Id == currentSelection.Id);
                if (matchingItem != null)
                {
                    GroupFilterCombo.SelectedItem = matchingItem;
                }
                else
                {
                    GroupFilterCombo.SelectedIndex = 0; // Fall back to "All"
                }
            }
            else
            {
                GroupFilterCombo.SelectedIndex = 0;
            }
        }
        finally
        {
            _isUpdatingGroupFilter = false;
        }
    }

    private async void GroupFilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingGroupFilter)
            return;

        if (GroupFilterCombo.SelectedItem is GroupFilterItem item)
        {
            await _viewModel.FilterByGroupCommand.ExecuteAsync(item.Group);
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && _minimizeToTray)
        {
            Hide();
        }
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Show confirmation dialog
        var result = System.Windows.MessageBox.Show(
            "Are you sure you want to exit SSH Manager?",
            "Confirm Exit",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (result != System.Windows.MessageBoxResult.Yes)
        {
            e.Cancel = true;
            return;
        }

        // Unsubscribe from tray events
        _trayService.QuickConnectRequested -= OnQuickConnectRequested;
        _trayService.ShowWindowRequested -= OnShowWindowRequested;
        _trayService.ExitRequested -= OnExitRequested;
        _trayService.SettingsRequested -= OnSettingsRequested;
    }

    private async void OnQuickConnectRequested(object? sender, HostEntry host)
    {
        // Show window and connect to host
        ShowAndActivate();
        await _viewModel.ConnectCommand.ExecuteAsync(host);
    }

    private void OnShowWindowRequested(object? sender, EventArgs e)
    {
        ShowAndActivate();
    }

    private void OnExitRequested(object? sender, EventArgs e)
    {
        Application.Current.Shutdown();
    }

    private void OnSettingsRequested(object? sender, EventArgs e)
    {
        ShowAndActivate();
        ShowSettingsDialog();
    }

    private void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Handle Ctrl+F to focus search
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
            e.Handled = true;
        }
        // Handle Escape to clear search
        else if (e.Key == Key.Escape && SearchBox.IsFocused)
        {
            _viewModel.SearchText = "";
            e.Handled = true;
        }
        // Handle Ctrl+H for history
        else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowHistoryDialog();
            e.Handled = true;
        }
        // Handle Ctrl+, for settings
        else if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowSettingsDialog();
            e.Handled = true;
        }
        // Handle Ctrl+Shift+S for snippets
        else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowSnippetsDialog();
            e.Handled = true;
        }
        // Handle Ctrl+K for SSH Key Manager
        else if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowKeyManagerDialog();
            e.Handled = true;
        }
        // Handle Ctrl+Tab to cycle through panes
        else if (e.Key == Key.Tab && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _paneLayoutManager.CycleFocusNext();
            e.Handled = true;
        }
        // Handle Ctrl+Shift+Tab to cycle previous pane
        else if (e.Key == Key.Tab && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            _paneLayoutManager.CycleFocusPrevious();
            e.Handled = true;
        }
        // Handle Ctrl+Shift+D for vertical split
        else if (e.Key == Key.D && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowSessionPickerForSplit(SplitOrientation.Vertical);
            e.Handled = true;
        }
        // Handle Ctrl+Shift+E for horizontal split
        else if (e.Key == Key.E && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowSessionPickerForSplit(SplitOrientation.Horizontal);
            e.Handled = true;
        }
        // Handle Ctrl+Shift+M for mirror pane
        else if (e.Key == Key.M && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            MirrorCurrentPane();
            e.Handled = true;
        }
        // Handle Ctrl+Shift+W for close pane
        else if (e.Key == Key.W && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            CloseCurrentPane();
            e.Handled = true;
        }
        // Handle Alt+Arrow for pane navigation
        else if (Keyboard.Modifiers == ModifierKeys.Alt)
        {
            var direction = e.Key switch
            {
                Key.Left => NavigationDirection.Left,
                Key.Right => NavigationDirection.Right,
                Key.Up => NavigationDirection.Up,
                Key.Down => NavigationDirection.Down,
                _ => (NavigationDirection?)null
            };

            if (direction.HasValue)
            {
                _paneLayoutManager.NavigateFocus(direction.Value);
                e.Handled = true;
            }
        }
    }

    // Split pane methods
    private void ShowSessionPickerForSplit(SplitOrientation orientation)
    {
        if (_paneLayoutManager.FocusedPane == null)
            return;

        var viewModel = new SessionPickerViewModel();
        viewModel.Initialize(_viewModel.Hosts, _viewModel.Sessions);

        var dialog = new SessionPickerDialog(viewModel)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            HandleSessionPickerResult(dialog.Result, orientation);
        }
    }

    private async void HandleSessionPickerResult(SessionPickerResultData result, SplitOrientation orientation)
    {
        var focusedPane = _paneLayoutManager.FocusedPane;
        if (focusedPane == null)
            return;

        switch (result.Result)
        {
            case SessionPickerResult.NewConnection:
                if (result.SelectedHost != null)
                {
                    // Create new session and split
                    var newSession = await _viewModel.CreateSessionForHostAsync(result.SelectedHost);
                    if (newSession != null)
                    {
                        var newPane = _paneLayoutManager.SplitPane(focusedPane, orientation, newSession);
                        // Connection will be handled by TerminalPane.Terminal_Loaded
                    }
                }
                break;

            case SessionPickerResult.ExistingSession:
                if (result.SelectedSession != null)
                {
                    // Mirror the existing session
                    var newPane = _paneLayoutManager.SplitPane(focusedPane, orientation, result.SelectedSession);
                }
                break;

            case SessionPickerResult.EmptyPane:
                _paneLayoutManager.SplitPane(focusedPane, orientation, null);
                break;
        }
    }

    private void MirrorCurrentPane()
    {
        var focusedPane = _paneLayoutManager.FocusedPane;
        if (focusedPane?.Session == null)
            return;

        _paneLayoutManager.MirrorPane(focusedPane, SplitOrientation.Vertical);
    }

    private void CloseCurrentPane()
    {
        var focusedPane = _paneLayoutManager.FocusedPane;
        if (focusedPane == null)
            return;

        _paneLayoutManager.ClosePane(focusedPane);
    }

    private void OnSessionCreated(object? sender, TerminalSession session)
    {
        // When a new session is created, add it to a tabbed pane
        Dispatcher.BeginInvoke(() =>
        {
            // Create a tabbed pane for the session (stacked with visibility switching)
            var pane = _paneLayoutManager.CreateTabbedPane(session);
            ConnectPaneToSession(pane, session);
        });
    }

    private async void ConnectPaneToSession(PaneLeafNode pane, TerminalSession session)
    {
        if (session.Host == null)
            return;

        // Find the TerminalPane control for this pane
        var paneControl = PaneContainer.GetPaneControl(pane);
        if (paneControl == null)
            return;

        var connectionStartedAt = DateTimeOffset.UtcNow;

        try
        {
            var hostKeyCallback = _viewModel.CreateHostKeyVerificationCallback(session.Host.Id);
            var kbInteractiveCallback = _viewModel.CreateKeyboardInteractiveCallback();

            // Check if host has a ProxyJump profile configured
            if (session.Host.ProxyJumpProfileId.HasValue)
            {
                // Build passwords dictionary for the chain
                var passwords = new Dictionary<Guid, string>();
                if (session.DecryptedPassword != null)
                {
                    passwords[session.Host.Id] = session.DecryptedPassword.ToUnsecureString()!;
                }

                // Resolve the connection chain
                var connectionChain = await _proxyJumpService.ResolveConnectionChainAsync(
                    session.Host,
                    passwords);

                if (connectionChain.Count > 0)
                {
                    // Connect through the proxy chain
                    session.SessionLogger?.LogEvent("CONNECT",
                        $"Connecting via proxy chain: {string.Join(" → ", connectionChain.Select(c => c.Hostname))}");

                    await paneControl.ConnectWithProxyChainAsync(
                        _viewModel.SshService,
                        connectionChain,
                        hostKeyCallback,
                        kbInteractiveCallback);
                }
                else
                {
                    // Fallback to direct connection if chain resolution returned empty (disabled profile, etc.)
                    var connectionInfo = await _viewModel.CreateConnectionInfoAsync(
                        session.Host,
                        session.DecryptedPassword?.ToUnsecureString());

                    await paneControl.ConnectAsync(_viewModel.SshService, connectionInfo, hostKeyCallback, kbInteractiveCallback);
                }
            }
            else
            {
                // Direct connection (no proxy jump)
                var connectionInfo = await _viewModel.CreateConnectionInfoAsync(
                    session.Host,
                    session.DecryptedPassword?.ToUnsecureString());

                await paneControl.ConnectAsync(_viewModel.SshService, connectionInfo, hostKeyCallback, kbInteractiveCallback);
            }

            session.Status = "Connected";
            await _viewModel.RecordConnectionResultAsync(session.Host, true, null, connectionStartedAt);

            // Start auto-start port forwardings after successful connection
            await StartAutoStartPortForwardingsAsync(session);

            // Subscribe to session disconnection to clean up port forwardings
            session.SessionClosed += OnSessionClosedForPortForwarding;
        }
        catch (Exception ex)
        {
            session.Status = $"Failed: {ex.Message}";
            session.SessionLogger?.LogEvent("ERROR", $"Connection failed: {ex.Message}");
            await _viewModel.RecordConnectionResultAsync(session.Host, false, ex.Message, connectionStartedAt);
        }
    }

    /// <summary>
    /// Starts auto-start port forwardings for a session after connection.
    /// </summary>
    private async Task StartAutoStartPortForwardingsAsync(TerminalSession session)
    {
        if (session.Host == null || session.Connection == null)
            return;

        try
        {
            // Start auto-start port forwardings via the service
            var handles = await _portForwardingService.StartAutoStartForwardingsAsync(
                session.Connection,
                session.Id,
                session.Host.Id);

            if (handles.Count > 0)
            {
                session.SessionLogger?.LogEvent("PORT_FORWARD",
                    $"Started {handles.Count} auto-start port forwarding(s)");

                // Get the active forwardings (which include the profile) for UI tracking
                var activeForwardings = _portForwardingService.GetActiveForwardings(session.Id);

                foreach (var forwarding in activeForwardings)
                {
                    var vmForwarding = ActivePortForwardingViewModel.FromProfile(
                        forwarding.Profile,
                        session.Id,
                        session.Host.DisplayName);

                    vmForwarding.MarkActive();
                    _viewModel.PortForwardingManager.AddActiveForwarding(vmForwarding);
                }
            }
        }
        catch (Exception ex)
        {
            session.SessionLogger?.LogEvent("ERROR",
                $"Failed to start auto-start port forwardings: {ex.Message}");
        }
    }

    /// <summary>
    /// Handles session close event to clean up port forwardings.
    /// </summary>
    private async void OnSessionClosedForPortForwarding(object? sender, EventArgs e)
    {
        if (sender is not TerminalSession session)
            return;

        // Unsubscribe to prevent memory leaks
        session.SessionClosed -= OnSessionClosedForPortForwarding;

        try
        {
            // Stop all port forwardings for this session
            await _portForwardingService.StopAllForSessionAsync(session.Id);

            // Update the UI manager
            _viewModel.PortForwardingManager.StopAllForSession(session.Id);
        }
        catch (Exception)
        {
            // Ignore errors during cleanup
        }
    }

    // Pane container event handlers
    private void PaneContainer_PaneSplitRequested(object? sender, PaneSplitRequestedEventArgs e)
    {
        ShowSessionPickerForSplit(e.Orientation);
    }

    private void PaneContainer_PaneCloseRequested(object? sender, PaneLeafNode e)
    {
        _paneLayoutManager.ClosePane(e);
    }

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSettingsDialog();
    }

    private void ShowSettingsDialog()
    {
        var settingsDialog = new SettingsDialog();
        settingsDialog.Owner = this;
        settingsDialog.ShowDialog();
    }

    private void HistoryButton_Click(object sender, RoutedEventArgs e)
    {
        ShowHistoryDialog();
    }

    private void ShowHistoryDialog()
    {
        var historyDialog = new ConnectionHistoryDialog();
        historyDialog.Owner = this;
        historyDialog.OnConnectRequested += async (host) =>
        {
            await _viewModel.ConnectCommand.ExecuteAsync(host);
        };
        historyDialog.ShowDialog();
    }

    private async void ImportHostsButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ImportHostsAsync();
    }

    private async void ImportFromSshConfigButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ImportFromSshConfigAsync();
    }

    private async void ImportFromPuttyButton_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ImportFromPuttyAsync();
    }

    private void SnippetsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSnippetsDialog();
    }

    private void KeyManagerButton_Click(object sender, RoutedEventArgs e)
    {
        ShowKeyManagerDialog();
    }

    private void ShowKeyManagerDialog()
    {
        var keyManager = App.GetService<ISshKeyManager>();
        var managedKeyRepo = App.GetService<IManagedKeyRepository>();
        var logger = App.GetLogger<SshKeyManagerViewModel>();
        var viewModel = new SshKeyManagerViewModel(keyManager, managedKeyRepo, logger);
        var dialog = new SshKeyManagerDialog(viewModel)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private void ShowSnippetsDialog()
    {
        var snippetsDialog = new SnippetManagerDialog();
        snippetsDialog.Owner = this;
        snippetsDialog.OnExecuteSnippet += (snippet) =>
        {
            // Send the command to the focused terminal pane
            var focusedPane = _paneLayoutManager.FocusedPane;
            if (focusedPane?.Session?.IsConnected == true)
            {
                var paneControl = PaneContainer.GetPaneControl(focusedPane);
                if (paneControl != null)
                {
                    paneControl.TerminalControl.SendCommand(snippet.Command);
                    paneControl.TerminalControl.FocusInput();
                }
            }
        };
        snippetsDialog.ShowDialog();
    }

    // Session tab selection - switch to the selected session's pane
    private void SessionTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox && listBox.SelectedItem is TerminalSession session)
        {
            _viewModel.CurrentSession = session;

            // Switch visibility to show only the selected session's pane
            _paneLayoutManager.SetActiveTabbedSession(session);

            // Focus the terminal control in the active pane
            var activePane = _paneLayoutManager.FindPanesForSession(session).FirstOrDefault();
            if (activePane != null)
            {
                var paneControl = PaneContainer.GetPaneControl(activePane);
                paneControl?.TerminalControl.FocusInput();
            }
        }
    }

    // Port forwarding panel event handler
    private async void PortForwardingPanel_ManageProfilesRequested(object? sender, EventArgs e)
    {
        var portForwardingRepo = App.GetService<IPortForwardingProfileRepository>();
        var hostRepo = App.GetService<IHostRepository>();

        if (portForwardingRepo == null || hostRepo == null)
        {
            System.Windows.MessageBox.Show(
                "Port forwarding management is not available.",
                "Feature Not Available",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Information);
            return;
        }

        var hosts = await hostRepo.GetAllAsync();
        var managerVm = new PortForwardingManagerViewModel(portForwardingRepo);
        var dialog = new PortForwardingListDialog(managerVm, portForwardingRepo, hosts)
        {
            Owner = this
        };

        dialog.ShowDialog();

        // Reload profiles after management
        await _viewModel.PortForwardingManager.LoadProfilesAsync();
    }
}
