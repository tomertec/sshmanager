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
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Windows;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ISystemTrayService _trayService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IPaneLayoutManager _paneLayoutManager;
    private readonly ISessionConnectionService _sessionConnectionService;
    private readonly IPortForwardingService _portForwardingService;
    private readonly ITerminalSessionManager _sessionManager;
    private readonly ITerminalFocusTracker _focusTracker;
    private readonly ISnackbarService _snackbarService;
    private bool _minimizeToTray;
    private bool _isUpdatingGroupFilter;
    private bool _isSyncingSessionFromPaneFocus;

    public MainWindow(
        MainWindowViewModel viewModel,
        ISystemTrayService trayService,
        ISettingsRepository settingsRepo,
        IPaneLayoutManager paneLayoutManager,
        ISessionConnectionService sessionConnectionService,
        IPortForwardingService portForwardingService,
        ITerminalSessionManager sessionManager,
        ITerminalFocusTracker focusTracker,
        ISnackbarService snackbarService)
    {
        _viewModel = viewModel;
        _trayService = trayService;
        _settingsRepo = settingsRepo;
        _paneLayoutManager = paneLayoutManager;
        _sessionConnectionService = sessionConnectionService;
        _portForwardingService = portForwardingService;
        _sessionManager = sessionManager;
        _focusTracker = focusTracker;
        _snackbarService = snackbarService;
        DataContext = viewModel;

        InitializeComponent();

        // Set the snackbar presenter for the snackbar service
        _snackbarService.SetSnackbarPresenter(SnackbarPresenter);

        Loaded += OnLoaded;
        PreviewKeyDown += OnPreviewKeyDown;
        StateChanged += OnStateChanged;
        Closing += OnClosing;

        // Subscribe to QuickConnectOverlay changes to handle WebView2 airspace issues
        _viewModel.QuickConnectOverlay.PropertyChanged += QuickConnectOverlay_PropertyChanged;

        // Wire up system tray events
        _trayService.QuickConnectRequested += OnQuickConnectRequested;
        _trayService.ShowWindowRequested += OnShowWindowRequested;
        _trayService.ExitRequested += OnExitRequested;
        _trayService.SettingsRequested += OnSettingsRequested;

        // Subscribe to session creation for pane management
        _viewModel.SessionCreated += OnSessionCreated;

        // Subscribe to session close to remove panes when sessions are closed
        _sessionManager.SessionClosed += OnSessionClosed;

        // Subscribe to pane focus changes to sync with session tabs
        _paneLayoutManager.FocusedPaneChanged += OnFocusedPaneChanged;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        _viewModel.IsLoadingHosts = true;
        await _viewModel.LoadDataAsync();
        _viewModel.IsLoadingHosts = false;

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

            // Update dropdown menu items
            UpdateGroupFilterMenu(items);

            // Update the button text to show current selection
            UpdateGroupFilterButtonText();
        }
        finally
        {
            _isUpdatingGroupFilter = false;
        }
    }

    private void UpdateGroupFilterMenu(List<GroupFilterItem> items)
    {
        // Find the separator in the menu (after management items)
        var menu = GroupFilterMenu;
        if (menu == null) return;

        // Remove all items after the separator (dynamically added group items)
        var separatorIndex = -1;
        for (int i = 0; i < menu.Items.Count; i++)
        {
            if (menu.Items[i] is Separator)
            {
                separatorIndex = i;
                break;
            }
        }

        if (separatorIndex >= 0)
        {
            // Remove items after separator
            while (menu.Items.Count > separatorIndex + 1)
            {
                menu.Items.RemoveAt(menu.Items.Count - 1);
            }
        }

        // Add group filter items
        foreach (var item in items)
        {
            var menuItem = new Wpf.Ui.Controls.MenuItem
            {
                Header = item.HasCount ? $"{item.Name} ({item.Count})" : item.Name,
                Tag = item,
                FontWeight = item.Group == null ? FontWeights.SemiBold : FontWeights.Normal
            };

            // Add icon based on group
            if (item.Group == null)
            {
                menuItem.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Grid24 };
            }
            else
            {
                menuItem.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Folder24 };
            }

            // Mark the selected item
            if ((_viewModel.SelectedGroupFilter == null && item.Group == null) ||
                (_viewModel.SelectedGroupFilter != null && item.Group?.Id == _viewModel.SelectedGroupFilter.Id))
            {
                menuItem.Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Checkmark24 };
            }

            menuItem.Click += GroupFilterMenuItem_Click;
            menu.Items.Add(menuItem);
        }
    }

    private void UpdateGroupFilterButtonText()
    {
        if (GroupFilterText == null) return;

        if (_viewModel.SelectedGroupFilter == null)
        {
            GroupFilterText.Text = "All Groups";
        }
        else
        {
            GroupFilterText.Text = _viewModel.SelectedGroupFilter.Name;
        }
    }

    private async void GroupFilterMenuItem_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingGroupFilter)
            return;

        if (sender is Wpf.Ui.Controls.MenuItem menuItem && menuItem.Tag is GroupFilterItem item)
        {
            await _viewModel.FilterByGroupCommand.ExecuteAsync(item.Group);
            UpdateGroupFilterButtonText();

            // Refresh the menu to update the checkmark
            await RefreshGroupFilterAsync();
        }
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Always ensure the window is visible in the taskbar
        // This prevents the window from being "lost" when minimized
        ShowInTaskbar = true;
    }

    /// <summary>
    /// Handles QuickConnectOverlay property changes to show/hide the terminal pane.
    /// WebView2 has "airspace" issues where it renders on top of WPF content,
    /// so we need to hide the terminal pane when the overlay is open.
    /// </summary>
    private void QuickConnectOverlay_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickConnectOverlayViewModel.IsOpen))
        {
            // Hide/show the terminal pane to work around WebView2 airspace issues
            PaneContainer.Visibility = _viewModel.QuickConnectOverlay.IsOpen
                ? Visibility.Hidden
                : Visibility.Visible;
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

        // Unsubscribe from ViewModel events
        _viewModel.QuickConnectOverlay.PropertyChanged -= QuickConnectOverlay_PropertyChanged;
        _viewModel.SessionCreated -= OnSessionCreated;
        _sessionManager.SessionClosed -= OnSessionClosed;
        _paneLayoutManager.FocusedPaneChanged -= OnFocusedPaneChanged;

        // Unsubscribe from window events
        Loaded -= OnLoaded;
        PreviewKeyDown -= OnPreviewKeyDown;
        StateChanged -= OnStateChanged;

        // Unsubscribe from collection changed handlers
        _viewModel.Groups.CollectionChanged -= Groups_CollectionChanged;
        _viewModel.Hosts.CollectionChanged -= Hosts_CollectionChanged;

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
        // Ensure window is visible in taskbar
        ShowInTaskbar = true;

        // Ensure window is visible
        Show();

        // Restore from minimized state
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        // Bring to foreground
        Activate();
        Topmost = true;  // Temporarily set topmost to ensure visibility
        Topmost = false; // Then reset to normal
        Focus();
    }

    /// <summary>
    /// Checks if the keyboard focus is currently within a terminal control (WebView2).
    /// When true, most Ctrl+letter shortcuts should pass through to the terminal
    /// instead of being handled by the application.
    /// </summary>
    /// <remarks>
    /// Uses the <see cref="ITerminalFocusTracker"/> service for reliable focus detection.
    /// The tracker is updated by WebTerminalControl via GotFocus/LostFocus events,
    /// which is more reliable than walking the visual tree for WebView2 controls.
    /// </remarks>
    private bool IsTerminalFocused()
    {
        // Primary: Use the focus tracker service (most reliable for WebView2)
        if (_focusTracker.IsAnyTerminalFocused)
            return true;

        // Fallback: Walk the visual tree as a safety net
        // This handles edge cases where focus tracking might not be wired up
        var focused = Keyboard.FocusedElement as DependencyObject;
        if (focused == null) return false;

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
        // Handle Ctrl+K for Quick Connect overlay - only when terminal NOT focused
        // (Ctrl+K is kill-line in bash, cut-line in nano)
        else if (e.Key == Key.K && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (!terminalHasFocus)
            {
                _viewModel.OpenQuickConnect();
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
        // Handle Ctrl+Shift+P for Serial Port Quick Connect
        else if (e.Key == Key.P && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
        {
            ShowSerialQuickConnectDialog();
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

            // Update port forwarding UI to remove forwardings for this session
            _viewModel.PortForwardingManager.StopAllForSession(session.Id);
        });
    }

    private void OnFocusedPaneChanged(object? sender, PaneLeafNode? focusedPane)
    {
        // When pane focus changes (e.g., user clicks on a terminal pane),
        // sync the session tabs selection to match the focused pane's session.
        // Use a flag to prevent SessionTabs_SelectionChanged from re-triggering
        // focus changes, which would cause an infinite loop.
        if (focusedPane?.Session != null && _viewModel.CurrentSession != focusedPane.Session)
        {
            _isSyncingSessionFromPaneFocus = true;
            try
            {
                _viewModel.CurrentSession = focusedPane.Session;
            }
            finally
            {
                _isSyncingSessionFromPaneFocus = false;
            }
        }
    }

    private async void ConnectPaneToSession(PaneLeafNode pane, TerminalSession session)
    {
        if (session.Host == null)
            return;

        // Find the TerminalPane control for this pane
        var paneControl = PaneContainer.GetPaneControl(pane);
        if (paneControl == null)
            return;

        try
        {
            // Delegate connection logic to the service based on connection type
            if (session.Host.ConnectionType == ConnectionType.Serial)
            {
                await _sessionConnectionService.ConnectSerialSessionAsync(paneControl, session);
            }
            else
            {
                await _sessionConnectionService.ConnectSshSessionAsync(paneControl, session);
            }

            // Update port forwarding UI for SSH sessions
            if (session.Host.ConnectionType == ConnectionType.Ssh && session.Connection != null)
            {
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
            // Show error to user via snackbar
            _snackbarService.Show(
                "Connection Failed",
                $"Failed to connect: {ex.Message}",
                Wpf.Ui.Controls.ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(5));
        }
    }


    // Pane container event handlers
    private void PaneContainer_PaneSplitRequested(object? sender, PaneSplitRequestedEventArgs e)
    {
        ShowSessionPickerForSplit(e.Orientation, e.Pane);
    }

    private async void PaneContainer_PaneCloseRequested(object? sender, PaneLeafNode e)
    {
        var session = e.Session;
        _paneLayoutManager.ClosePane(e);

        // If no other panes are using this session, close the session
        if (session != null)
        {
            var remainingPanes = _paneLayoutManager.FindPanesForSession(session);
            if (!remainingPanes.Any())
            {
                await _sessionManager.CloseSessionAsync(session.Id);
            }
        }
    }

    private async void PaneContainer_SessionDisconnected(object? sender, TerminalSession session)
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
        await _sessionManager.CloseSessionAsync(session.Id);
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

    private void RecordingsButton_Click(object sender, RoutedEventArgs e)
    {
        ShowRecordingsDialog();
    }

    private void ShowRecordingsDialog()
    {
        var viewModel = App.GetService<ViewModels.RecordingBrowserViewModel>();
        var dialog = new Dialogs.RecordingBrowserDialog(viewModel);
        dialog.Owner = this;
        dialog.ShowDialog();
    }

    private void SerialQuickConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ShowSerialQuickConnectDialog();
    }

    private async void ShowSerialQuickConnectDialog()
    {
        var serialService = App.GetService<ISerialConnectionService>();
        var viewModel = new SerialQuickConnectViewModel(serialService);
        var dialog = new SerialQuickConnectDialog(viewModel)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true)
        {
            var hostEntry = viewModel.CreateHostEntry();

            if (viewModel.ShouldSaveToHosts)
            {
                // Save the host entry to the database first
                await _viewModel.SaveTransientHostAsync(hostEntry);
            }

            // Connect using the host entry
            await _viewModel.ConnectCommand.ExecuteAsync(hostEntry);
        }
    }

    private void QuickConnectButton_Click(object sender, RoutedEventArgs e)
    {
        ShowQuickConnectDialog();
    }

    private void QuickConnectOverlayButton_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.OpenQuickConnect();
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
        var serialService = App.GetService<ISerialConnectionService>();
        var viewModel = new ViewModels.Dialogs.QuickConnectViewModel(serialService);
        var dialog = new QuickConnectDialog(viewModel)
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && viewModel.CreatedHostEntry != null)
        {
            var hostEntry = viewModel.CreatedHostEntry;

            // If password was provided (SSH mode), encrypt it for the connection flow
            if (!string.IsNullOrEmpty(viewModel.Password) && viewModel.IsSshMode)
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
        var ppkConverter = App.GetService<IPpkConverter>();
        var logger = App.GetLogger<SshKeyManagerViewModel>();
        var viewModel = new SshKeyManagerViewModel(keyManager, managedKeyRepo, ppkConverter, logger);
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

            // Switch visibility to show only the selected session's pane (for tabbed mode)
            _paneLayoutManager.SetActiveTabbedSession(session);

            // If this selection change was triggered by clicking on a terminal pane
            // (via OnFocusedPaneChanged), skip the focus operations to prevent
            // an infinite focus loop: click pane -> FocusedPaneChanged ->
            // CurrentSession changed -> SelectionChanged -> SetFocusedPane -> FocusInput -> repeat
            if (_isSyncingSessionFromPaneFocus)
            {
                return;
            }

            // Focus the pane for this session (works for both tabbed and split modes)
            var activePane = _paneLayoutManager.FindPanesForSession(session).FirstOrDefault();
            if (activePane != null)
            {
                // Update the visual focus indicator (border color)
                _paneLayoutManager.SetFocusedPane(activePane);

                // Focus the terminal control after UI updates complete
                var paneControl = PaneContainer.GetPaneControl(activePane);
                if (paneControl != null)
                {
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
                    {
                        paneControl.TerminalControl.FocusInput();
                    }));
                }
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
