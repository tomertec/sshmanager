using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private readonly ITerminalSessionManager _sessionManager;
    private bool _minimizeToTray;
    private bool _isUpdatingGroupFilter;

    public MainWindow(
        MainWindowViewModel viewModel,
        ISystemTrayService trayService,
        ISettingsRepository settingsRepo,
        IPaneLayoutManager paneLayoutManager,
        IProxyJumpService proxyJumpService,
        IPortForwardingService portForwardingService,
        ITerminalSessionManager sessionManager)
    {
        _viewModel = viewModel;
        _trayService = trayService;
        _settingsRepo = settingsRepo;
        _paneLayoutManager = paneLayoutManager;
        _proxyJumpService = proxyJumpService;
        _portForwardingService = portForwardingService;
        _sessionManager = sessionManager;
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

        // Subscribe to session close to remove panes when sessions are closed
        _sessionManager.SessionClosed += OnSessionClosed;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        await _viewModel.LoadDataAsync();

        // Load settings and update tray
        var settings = await _settingsRepo.GetAsync();
        _minimizeToTray = settings.MinimizeToTray;

        // Restore window position if enabled and values are saved
        if (settings.RememberWindowPosition &&
            settings.WindowX.HasValue &&
            settings.WindowY.HasValue &&
            settings.WindowWidth.HasValue &&
            settings.WindowHeight.HasValue)
        {
            // Validate the position is within screen bounds
            var screenWidth = SystemParameters.VirtualScreenWidth;
            var screenHeight = SystemParameters.VirtualScreenHeight;
            var screenLeft = SystemParameters.VirtualScreenLeft;
            var screenTop = SystemParameters.VirtualScreenTop;

            var x = settings.WindowX.Value;
            var y = settings.WindowY.Value;
            var width = settings.WindowWidth.Value;
            var height = settings.WindowHeight.Value;

            // Ensure window is at least partially visible
            if (x + width > screenLeft && x < screenLeft + screenWidth &&
                y + height > screenTop && y < screenTop + screenHeight)
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = x;
                Top = y;
                Width = width;
                Height = height;
            }
        }

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
        _ = RefreshGroupFilterAsync();
    }

    private void Hosts_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = RefreshGroupFilterAsync();
    }

    private void RefreshGroupFilter() => _ = RefreshGroupFilterAsync();

    private async Task RefreshGroupFilterAsync()
    {
        _isUpdatingGroupFilter = true;
        try
        {
            // Get unfiltered host counts from database
            var (totalCount, countsByGroup) = await _viewModel.GetTotalHostCountsAsync();

            var items = new List<GroupFilterItem>();

            // Add "All Groups" item with total count
            items.Add(GroupFilterItem.CreateAllItem(totalCount));

            // Add individual groups with host counts from unfiltered data
            foreach (var group in _viewModel.Groups.OrderBy(g => g.Name))
            {
                var hostCount = countsByGroup.TryGetValue(group.Id, out var count) ? count : 0;
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

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
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

        // Save window position if enabled
        try
        {
            var settings = await _settingsRepo.GetAsync();
            if (settings.RememberWindowPosition && WindowState == WindowState.Normal)
            {
                settings.WindowX = (int)Left;
                settings.WindowY = (int)Top;
                settings.WindowWidth = (int)Width;
                settings.WindowHeight = (int)Height;
                await _settingsRepo.UpdateAsync(settings);
            }
        }
        catch
        {
            // Ignore errors during shutdown
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

    /// <summary>
    /// Checks if the keyboard focus is currently within a terminal control (WebView2).
    /// When true, most Ctrl+letter shortcuts should pass through to the terminal
    /// instead of being handled by the application.
    /// </summary>
    private bool IsTerminalFocused()
    {
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused == null) return false;

        // Walk up the visual tree to check if focus is within a WebTerminalControl or SshTerminalControl
        while (focused != null)
        {
            var typeName = focused.GetType().FullName;

            // Check for WebView2 control (the actual terminal renderer)
            if (typeName == "Microsoft.Web.WebView2.Wpf.WebView2")
                return true;

            // Check for our terminal control types
            if (focused is SshTerminalControl)
                return true;

            focused = VisualTreeHelper.GetParent(focused);
        }

        return false;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Check if terminal has focus - if so, let terminal-conflicting shortcuts pass through
        var terminalHasFocus = IsTerminalFocused();

        // Handle Ctrl+F - only intercept when terminal is NOT focused
        // (Ctrl+F is forward-char in bash/readline)
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!terminalHasFocus)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
            // When terminal is focused, let it pass through for forward-char
            return;
        }
        // Handle Escape to clear search
        else if (e.Key == Key.Escape && SearchBox.IsFocused)
        {
            _viewModel.SearchText = "";
            e.Handled = true;
        }
        // Handle Ctrl+H for history - only when terminal NOT focused
        // (Ctrl+H is backspace in terminals)
        else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!terminalHasFocus)
            {
                ShowHistoryDialog();
                e.Handled = true;
            }
            return;
        }
        // Handle Ctrl+, for settings (not a terminal shortcut, always handle)
        else if (e.Key == Key.OemComma && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowSettingsDialog();
            e.Handled = true;
        }
        // Handle Ctrl+Shift+S for snippets (Shift modifier, always handle)
        else if (e.Key == Key.S && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowSnippetsDialog();
            e.Handled = true;
        }
        // Handle Ctrl+K for SSH Key Manager - only when terminal NOT focused
        // (Ctrl+K is kill-line in bash, cut-line in nano)
        else if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!terminalHasFocus)
            {
                ShowKeyManagerDialog();
                e.Handled = true;
            }
            return;
        }
        // Handle Ctrl+Q for Quick Connect (not a common terminal shortcut, always handle)
        else if (e.Key == Key.Q && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowQuickConnectDialog();
            e.Handled = true;
        }
        // Handle Ctrl+N for Add Host - only when terminal NOT focused
        // (Ctrl+N is next-history in bash)
        else if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!terminalHasFocus)
            {
                _viewModel.AddHostCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }
        // Handle Ctrl+E for Edit Host - only when terminal NOT focused
        // (Ctrl+E is end-of-line in bash)
        else if (e.Key == Key.E && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!terminalHasFocus && _viewModel.SelectedHost != null)
            {
                _viewModel.EditHostCommand.Execute(_viewModel.SelectedHost);
                e.Handled = true;
            }
            return;
        }
        // Handle Ctrl+B for SFTP Browser - only when terminal NOT focused
        // (Ctrl+B is back-char in bash)
        else if (e.Key == Key.B && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!terminalHasFocus)
            {
                _viewModel.OpenSftpBrowserCommand.Execute(null);
                e.Handled = true;
            }
            return;
        }
        // Handle Delete for Delete Host - only when terminal NOT focused
        else if (e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (!terminalHasFocus && _viewModel.SelectedHost != null)
            {
                _viewModel.DeleteHostCommand.Execute(_viewModel.SelectedHost);
                e.Handled = true;
            }
            return;
        }
        // Note: Ctrl+W is NOT used for closing sessions to allow it to work in terminal apps
        // like nano, vim, etc. Use Ctrl+F4 to close sessions instead.
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
    private void ShowSessionPickerForSplit(SplitOrientation orientation, PaneLeafNode? requestingPane = null)
    {
        // Use the requesting pane if provided, otherwise fall back to focused pane
        var paneToSplit = requestingPane ?? _paneLayoutManager.FocusedPane;
        if (paneToSplit == null)
            return;

        var viewModel = new SessionPickerViewModel();
        viewModel.Initialize(_viewModel.Hosts, _viewModel.Sessions);

        var dialog = new SessionPickerDialog(viewModel)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            HandleSessionPickerResult(dialog.Result, orientation, paneToSplit);
        }
    }

    private async void HandleSessionPickerResult(SessionPickerResultData result, SplitOrientation orientation, PaneLeafNode paneToSplit)
    {
        if (paneToSplit == null)
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
                        var newPane = _paneLayoutManager.SplitPane(paneToSplit, orientation, newSession);
                        // Connect the new pane to SSH
                        ConnectPaneToSession(newPane, newSession);
                    }
                }
                break;

            case SessionPickerResult.ExistingSession:
                if (result.SelectedSession != null)
                {
                    // Mirror the existing session
                    _paneLayoutManager.SplitPane(paneToSplit, orientation, result.SelectedSession);
                }
                break;

            case SessionPickerResult.EmptyPane:
                _paneLayoutManager.SplitPane(paneToSplit, orientation, null);
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

    private void OnSessionClosed(object? sender, TerminalSession session)
    {
        // When a session is closed, remove all panes that were displaying it
        Dispatcher.BeginInvoke(() =>
        {
            // Find all panes for this session and close them
            var panes = _paneLayoutManager.FindPanesForSession(session).ToList();
            foreach (var pane in panes)
            {
                _paneLayoutManager.ClosePane(pane);
            }
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

            // Focus the terminal after connection
            _ = Dispatcher.BeginInvoke(() => paneControl.TerminalControl.FocusInput());

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
        ShowSessionPickerForSplit(e.Orientation, e.Pane);
    }

    private void PaneContainer_PaneCloseRequested(object? sender, PaneLeafNode e)
    {
        var session = e.Session;
        _paneLayoutManager.ClosePane(e);

        // If no other panes are using this session, close the session
        if (session != null)
        {
            var remainingPanes = _paneLayoutManager.FindPanesForSession(session);
            if (!remainingPanes.Any())
            {
                _sessionManager.CloseSession(session.Id);
            }
        }
    }

    private void PaneContainer_SessionDisconnected(object? sender, TerminalSession session)
    {
        // When a session disconnects (e.g., VM reboots), close it and remove all associated panes
        if (session == null) return;

        // Find and close all panes for this session
        var panes = _paneLayoutManager.FindPanesForSession(session).ToList();
        foreach (var pane in panes)
        {
            _paneLayoutManager.ClosePane(pane);
        }

        // Close the session itself
        _sessionManager.CloseSession(session.Id);
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

    private void QuickConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ShowQuickConnectDialog();
    }

    private void QuickConnectCircle_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // Play click animation
        PlayQuickConnectClickAnimation();
        ShowQuickConnectDialog();
    }

    private void PlayQuickConnectClickAnimation()
    {
        var scaleDown = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = 0.92,
            Duration = TimeSpan.FromMilliseconds(100),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };

        var scaleUp = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(150),
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };

        var glowBright = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = 50,
            Duration = TimeSpan.FromMilliseconds(100)
        };

        var glowNormal = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = 20,
            Duration = TimeSpan.FromMilliseconds(200),
            BeginTime = TimeSpan.FromMilliseconds(100)
        };

        var storyboard = new System.Windows.Media.Animation.Storyboard();

        System.Windows.Media.Animation.Storyboard.SetTarget(scaleDown, QuickConnectCircle);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleDown,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
        storyboard.Children.Add(scaleDown);

        var scaleDownY = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = 0.92,
            Duration = TimeSpan.FromMilliseconds(100),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        System.Windows.Media.Animation.Storyboard.SetTarget(scaleDownY, QuickConnectCircle);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleDownY,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
        storyboard.Children.Add(scaleDownY);

        System.Windows.Media.Animation.Storyboard.SetTarget(scaleUp, QuickConnectCircle);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleUp,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
        storyboard.Children.Add(scaleUp);

        var scaleUpY = new System.Windows.Media.Animation.DoubleAnimation
        {
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(150),
            BeginTime = TimeSpan.FromMilliseconds(100),
            EasingFunction = new System.Windows.Media.Animation.QuadraticEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut }
        };
        System.Windows.Media.Animation.Storyboard.SetTarget(scaleUpY, QuickConnectCircle);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleUpY,
            new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
        storyboard.Children.Add(scaleUpY);

        System.Windows.Media.Animation.Storyboard.SetTarget(glowBright, QuickConnectCircle);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(glowBright,
            new PropertyPath("(UIElement.Effect).(DropShadowEffect.BlurRadius)"));
        storyboard.Children.Add(glowBright);

        System.Windows.Media.Animation.Storyboard.SetTarget(glowNormal, QuickConnectCircle);
        System.Windows.Media.Animation.Storyboard.SetTargetProperty(glowNormal,
            new PropertyPath("(UIElement.Effect).(DropShadowEffect.BlurRadius)"));
        storyboard.Children.Add(glowNormal);

        storyboard.Begin();
    }

    private async void ShowQuickConnectDialog()
    {
        var viewModel = new ViewModels.Dialogs.QuickConnectViewModel();
        var dialog = new QuickConnectDialog(viewModel)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && viewModel.CreatedHostEntry != null)
        {
            var hostEntry = viewModel.CreatedHostEntry;

            // If password was provided, encrypt it for the connection flow
            if (!string.IsNullOrEmpty(viewModel.Password))
            {
                var secretProtector = App.GetService<ISecretProtector>();
                hostEntry.PasswordProtected = secretProtector.Protect(viewModel.Password);
            }

            // Connect using the temporary host entry
            await _viewModel.ConnectCommand.ExecuteAsync(hostEntry);
        }
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
