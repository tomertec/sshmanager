using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using SshManager.App.Models;
using SshManager.App.Services;
using SshManager.App.ViewModels;
using SshManager.App.Views.Controls;
using SshManager.App.Views.Dialogs;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal;
using SshManager.Terminal.Services;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Windows;

public partial class MainWindow : FluentWindow
{
    private readonly MainWindowViewModel _viewModel;
    private readonly ISystemTrayService _trayService;
    private readonly IPaneLayoutManager _paneLayoutManager;
    private readonly ITerminalSessionManager _sessionManager;
    private readonly ISnackbarService _snackbarService;
    private readonly IKeyboardShortcutHandler _keyboardHandler;
    private readonly IWindowStateManager _windowStateManager;
    private readonly IPaneOrchestrator _paneOrchestrator;
    private readonly IServiceProvider _serviceProvider;
    
    private bool _isUpdatingGroupFilter;
    private bool _isSyncingSessionFromPaneFocus;

    public MainWindow(
        MainWindowViewModel viewModel,
        ISystemTrayService trayService,
        IPaneLayoutManager paneLayoutManager,
        ITerminalSessionManager sessionManager,
        ISnackbarService snackbarService,
        IKeyboardShortcutHandler keyboardHandler,
        IWindowStateManager windowStateManager,
        IPaneOrchestrator paneOrchestrator,
        IServiceProvider serviceProvider)
    {
        _viewModel = viewModel;
        _trayService = trayService;
        _paneLayoutManager = paneLayoutManager;
        _sessionManager = sessionManager;
        _snackbarService = snackbarService;
        _keyboardHandler = keyboardHandler;
        _windowStateManager = windowStateManager;
        _paneOrchestrator = paneOrchestrator;
        _serviceProvider = serviceProvider;
        DataContext = viewModel;

        InitializeComponent();

        // Set the snackbar presenter for the snackbar service
        _snackbarService.SetSnackbarPresenter(SnackbarPresenter);

        // Initialize services
        _paneOrchestrator.SetViewModel(viewModel);

        Loaded += OnLoaded;
        StateChanged += OnStateChanged;
        Closing += OnClosing;

        // Attach keyboard handler and subscribe to events
        _keyboardHandler.AttachTo(this);
        _keyboardHandler.ActionRequested += OnShortcutActionRequested;

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

        // Subscribe to pane orchestrator events
        _paneOrchestrator.SessionPickerRequested += OnSessionPickerRequested;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Initialize PaneContainer with required dependencies
            PaneContainer.SetLayoutManager(_paneLayoutManager);
            PaneContainer.SetServiceProvider(_serviceProvider);

            _viewModel.IsLoadingHosts = true;
            await _viewModel.LoadDataAsync();
            _viewModel.IsLoadingHosts = false;

            // Load window state (position, size, minimize to tray setting)
            await _windowStateManager.LoadWindowStateAsync(this);

            // Load left panel width
            var savedPanelWidth = await _windowStateManager.GetLeftPanelWidthAsync();
            if (savedPanelWidth.HasValue && savedPanelWidth.Value >= LeftPanelColumn.MinWidth && savedPanelWidth.Value <= LeftPanelColumn.MaxWidth)
            {
                LeftPanelColumn.Width = new GridLength(savedPanelWidth.Value);
            }

            // Update tray menu with current hosts
            _trayService.UpdateContextMenu(_viewModel.HostManagement.Hosts, _viewModel.HostManagement.Groups);
            _trayService.Show();

            // Initialize group filter dropdown
            RefreshGroupFilter();

            // Subscribe to groups collection changes
            _viewModel.HostManagement.Groups.CollectionChanged += Groups_CollectionChanged;
            _viewModel.HostManagement.Hosts.CollectionChanged += Hosts_CollectionChanged;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnLoaded: {ex}");
        }
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
            foreach (var group in _viewModel.HostManagement.Groups.OrderBy(g => g.Name))
            {
                var hostCount = countsByGroup.TryGetValue(group.Id, out var count) ? count : 0;
                items.Add(GroupFilterItem.CreateGroupItem(group, hostCount));
            }

            // Update dropdown menu items via the HostListPanel
            HostListPanel.UpdateGroupFilterMenu(items, _viewModel.HostManagement.SelectedGroupFilter, OnGroupFilterItemClick);

            // Update the button text to show current selection
            UpdateGroupFilterButtonText();
        }
        finally
        {
            _isUpdatingGroupFilter = false;
        }
    }

    private async void OnGroupFilterItemClick(GroupFilterItem item)
    {
        try
        {
            if (_isUpdatingGroupFilter)
                return;

            await _viewModel.HostManagement.FilterByGroupCommand.ExecuteAsync(item.Group);
            UpdateGroupFilterButtonText();

            // Refresh the menu to update the checkmark
            await RefreshGroupFilterAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnGroupFilterItemClick: {ex}");
        }
    }

    private void UpdateGroupFilterButtonText()
    {
        var text = _viewModel.HostManagement.SelectedGroupFilter == null 
            ? "All Groups" 
            : _viewModel.HostManagement.SelectedGroupFilter.Name;
        HostListPanel.UpdateGroupFilterButtonText(text);
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // Always ensure the window is visible in the taskbar
        ShowInTaskbar = true;
    }

    /// <summary>
    /// Handles QuickConnectOverlay property changes to show/hide the terminal pane.
    /// WebView2 has "airspace" issues where it renders on top of WPF content.
    /// </summary>
    private void QuickConnectOverlay_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(QuickConnectOverlayViewModel.IsOpen))
        {
            PaneContainer.Visibility = _viewModel.QuickConnectOverlay.IsOpen
                ? Visibility.Hidden
                : Visibility.Visible;
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        try
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

            // Save window state - wrapped in try-catch as window properties may be unreliable during teardown
            try
            {
                await _windowStateManager.SaveWindowStateAsync(this);
            }
            catch (Exception saveEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving window state: {saveEx}");
            }

            // Save left panel width - wrapped in try-catch as properties may be unreliable during teardown
            try
            {
                await _windowStateManager.SaveLeftPanelWidthAsync(LeftPanelColumn.ActualWidth);
            }
            catch (Exception saveEx)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving panel width: {saveEx}");
            }

            // Unsubscribe from all events
            _keyboardHandler.ActionRequested -= OnShortcutActionRequested;
            _keyboardHandler.Detach();

            _viewModel.QuickConnectOverlay.PropertyChanged -= QuickConnectOverlay_PropertyChanged;
            _viewModel.SessionCreated -= OnSessionCreated;
            _sessionManager.SessionClosed -= OnSessionClosed;
            _paneLayoutManager.FocusedPaneChanged -= OnFocusedPaneChanged;
            _paneOrchestrator.SessionPickerRequested -= OnSessionPickerRequested;

            Loaded -= OnLoaded;
            StateChanged -= OnStateChanged;

            _viewModel.HostManagement.Groups.CollectionChanged -= Groups_CollectionChanged;
            _viewModel.HostManagement.Hosts.CollectionChanged -= Hosts_CollectionChanged;

            _trayService.QuickConnectRequested -= OnQuickConnectRequested;
            _trayService.ShowWindowRequested -= OnShowWindowRequested;
            _trayService.ExitRequested -= OnExitRequested;
            _trayService.SettingsRequested -= OnSettingsRequested;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnClosing: {ex}");
        }
    }

    #region Keyboard Shortcut Handler

    private void OnShortcutActionRequested(object? sender, ShortcutActionEventArgs e)
    {
        switch (e.Action)
        {
            case ShortcutAction.FocusSearch:
                HostListPanel.SearchBoxControl.Focus();
                HostListPanel.SearchBoxControl.SelectAll();
                break;
                
            case ShortcutAction.ClearSearch:
                if (HostListPanel.SearchBoxControl.IsFocused)
                {
                    _viewModel.HostManagement.SearchText = "";
                }
                break;
                
            case ShortcutAction.ShowHistory:
                ShowHistoryDialog();
                break;
                
            case ShortcutAction.ShowSettings:
                ShowSettingsDialog();
                break;
                
            case ShortcutAction.ShowSnippets:
                ShowSnippetsDialog();
                break;
                
            case ShortcutAction.ShowQuickConnectOverlay:
                _viewModel.OpenQuickConnect();
                break;
                
            case ShortcutAction.ShowQuickConnectDialog:
                ShowQuickConnectDialog();
                break;
                
            case ShortcutAction.AddHost:
                _viewModel.HostManagement.AddHostCommand.Execute(null);
                break;
                
            case ShortcutAction.EditHost:
                if (_viewModel.HostManagement.SelectedHost != null)
                {
                    _viewModel.HostManagement.EditHostCommand.Execute(_viewModel.HostManagement.SelectedHost);
                }
                break;
                
            case ShortcutAction.DeleteHost:
                if (_viewModel.HostManagement.SelectedHost != null)
                {
                    _viewModel.HostManagement.DeleteHostCommand.Execute(_viewModel.HostManagement.SelectedHost);
                }
                break;
                
            case ShortcutAction.OpenSftpBrowser:
                _viewModel.SftpLauncher.OpenSftpBrowserCommand.Execute(null);
                break;
                
            case ShortcutAction.ShowKeyboardShortcuts:
                var shortcutsDialog = new KeyboardShortcutsDialog { Owner = this };
                shortcutsDialog.ShowDialog();
                break;
                
            case ShortcutAction.ShowSerialQuickConnect:
                ShowSerialQuickConnectDialog();
                break;
                
            case ShortcutAction.SplitVertical:
                _paneOrchestrator.RequestSplit(SplitOrientation.Vertical);
                break;
                
            case ShortcutAction.SplitHorizontal:
                _paneOrchestrator.RequestSplit(SplitOrientation.Horizontal);
                break;
                
            case ShortcutAction.MirrorPane:
                _paneOrchestrator.MirrorCurrentPane();
                break;
                
            case ShortcutAction.ClosePane:
                _paneOrchestrator.CloseCurrentPane();
                break;
        }
    }

    #endregion

    #region System Tray Events

    private async void OnQuickConnectRequested(object? sender, HostEntry host)
    {
        try
        {
            ShowAndActivate();
            await _viewModel.Session.ConnectCommand.ExecuteAsync(host);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in OnQuickConnectRequested: {ex}");
        }
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
        ShowInTaskbar = true;
        Show();

        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    #endregion

    #region Session and Pane Events

    private void OnSessionCreated(object? sender, TerminalSession session)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _paneOrchestrator.OnSessionCreated(session);
            
            // Connect the new pane to its session
            var pane = _paneLayoutManager.FindPanesForSession(session).FirstOrDefault();
            if (pane != null)
            {
                _ = _paneOrchestrator.ConnectPaneToSessionAsync(pane, session, PaneContainer.GetPaneControl);
            }
        });
    }

    private void OnSessionClosed(object? sender, TerminalSession session)
    {
        Dispatcher.BeginInvoke(() =>
        {
            _paneOrchestrator.OnSessionClosed(session);
        });
    }

    private void OnFocusedPaneChanged(object? sender, PaneLeafNode? focusedPane)
    {
        if (focusedPane?.Session != null && _viewModel.Session.CurrentSession != focusedPane.Session)
        {
            _isSyncingSessionFromPaneFocus = true;
            try
            {
                _paneOrchestrator.OnFocusedPaneChanged(focusedPane);
            }
            finally
            {
                _isSyncingSessionFromPaneFocus = false;
            }
        }
    }

    private void OnSessionPickerRequested(object? sender, SessionPickerRequestEventArgs e)
    {
        ShowSessionPickerForSplit(e.Orientation, e.RequestingPane);
    }

    private void ShowSessionPickerForSplit(SplitOrientation orientation, PaneLeafNode? requestingPane = null)
    {
        var paneToSplit = requestingPane ?? _paneLayoutManager.FocusedPane;
        if (paneToSplit == null)
            return;

        var viewModel = new SessionPickerViewModel();
        viewModel.Initialize(_viewModel.HostManagement.Hosts, _viewModel.Session.Sessions);

        var dialog = new SessionPickerDialog(viewModel) { Owner = this };

        if (dialog.ShowDialog() == true && dialog.Result != null)
        {
            _ = HandleSessionPickerResultAsync(dialog.Result, orientation, paneToSplit);
        }
    }

    private async Task HandleSessionPickerResultAsync(SessionPickerResultData result, SplitOrientation orientation, PaneLeafNode paneToSplit)
    {
        await _paneOrchestrator.HandleSessionPickerResultAsync(result, orientation, paneToSplit);
        
        // Connect new panes after split
        if (result.Result == SessionPickerResult.NewConnection && result.SelectedHost != null)
        {
            // Find the newly created session's pane and connect it
            var newSession = _viewModel.Session.Sessions.LastOrDefault();
            if (newSession != null)
            {
                var newPane = _paneLayoutManager.FindPanesForSession(newSession).LastOrDefault();
                if (newPane != null)
                {
                    await _paneOrchestrator.ConnectPaneToSessionAsync(newPane, newSession, PaneContainer.GetPaneControl);
                }
            }
        }
    }

    #endregion

    #region Pane Container Events

    private void PaneContainer_PaneSplitRequested(object? sender, PaneSplitRequestedEventArgs e)
    {
        ShowSessionPickerForSplit(e.Orientation, e.Pane);
    }

    private async void PaneContainer_PaneCloseRequested(object? sender, PaneLeafNode e)
    {
        try
        {
            await _paneOrchestrator.OnPaneCloseRequestedAsync(e);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in PaneContainer_PaneCloseRequested: {ex}");
        }
    }

    private async void PaneContainer_SessionDisconnected(object? sender, TerminalSession session)
    {
        try
        {
            if (session != null)
            {
                await _paneOrchestrator.OnSessionDisconnectedAsync(session);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in PaneContainer_SessionDisconnected: {ex}");
        }
    }

    #endregion

    #region Host List Panel Events

    private void HostListPanel_SettingsRequested(object? sender, EventArgs e)
    {
        ShowSettingsDialog();
    }

    private void HostListPanel_QuickConnectOverlayRequested(object? sender, EventArgs e)
    {
        _viewModel.OpenQuickConnect();
    }

    private void HostListPanel_KeyboardShortcutsRequested(object? sender, EventArgs e)
    {
        var shortcutsDialog = new KeyboardShortcutsDialog { Owner = this };
        shortcutsDialog.ShowDialog();
    }

    private void HostListPanel_AboutRequested(object? sender, EventArgs e)
    {
        var aboutDialog = new AboutDialog { Owner = this };
        aboutDialog.ShowDialog();
    }

    private void HostListPanel_HistoryRequested(object? sender, EventArgs e)
    {
        ShowHistoryDialog();
    }

    private void HostListPanel_SnippetsRequested(object? sender, EventArgs e)
    {
        ShowSnippetsDialog();
    }

    private void HostListPanel_KeyManagerRequested(object? sender, EventArgs e)
    {
        ShowKeyManagerDialog();
    }

    private void HostListPanel_RecordingsRequested(object? sender, EventArgs e)
    {
        ShowRecordingsDialog();
    }

    private void HostListPanel_SerialQuickConnectRequested(object? sender, EventArgs e)
    {
        ShowSerialQuickConnectDialog();
    }

    #endregion

    #region Session Tab Strip Events

    private void SessionTabStrip_SessionSelectionChanged(object? sender, TerminalSession? session)
    {
        if (session == null)
            return;

        _paneOrchestrator.OnSessionTabSelected(session, _isSyncingSessionFromPaneFocus);

        // Focus terminal control if not from pane click
        if (!_isSyncingSessionFromPaneFocus)
        {
            var activePane = _paneLayoutManager.FindPanesForSession(session).FirstOrDefault();
            if (activePane != null)
            {
                var paneControl = PaneContainer.GetPaneControl(activePane);
                if (paneControl != null)
                {
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, () =>
                    {
                        paneControl.TerminalControl.FocusInput();
                    });
                }
            }
        }
    }

    #endregion

    #region Terminal Toolbar Events

    private async void TerminalToolbar_TunnelBuilderRequested(object? sender, EventArgs e)
    {
        try
        {
            var viewModel = _serviceProvider.GetRequiredService<TunnelBuilderViewModel>();
            var snackbarService = _serviceProvider.GetRequiredService<ISnackbarService>();
            var dialog = new TunnelBuilderDialog(viewModel, snackbarService) { Owner = this };
            await viewModel.InitializeAsync();
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in TerminalToolbar_TunnelBuilderRequested: {ex}");
        }
    }

    private async void TerminalToolbar_ManagePortForwardingProfilesRequested(object? sender, EventArgs e)
    {
        try
        {
            var portForwardingRepo = _serviceProvider.GetRequiredService<IPortForwardingProfileRepository>();
            var managerVm = new PortForwardingManagerViewModel(portForwardingRepo);
            var dialog = new PortForwardingListDialog(managerVm, portForwardingRepo, _viewModel.HostManagement.Hosts) { Owner = this };
            dialog.ShowDialog();
            await _viewModel.PortForwardingManager.LoadProfilesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in TerminalToolbar_ManagePortForwardingProfilesRequested: {ex}");
        }
    }

    #endregion

    #region Welcome Panel Events

    private void WelcomePanel_QuickConnectRequested(object? sender, EventArgs e)
    {
        ShowQuickConnectDialog();
    }

    private async void WelcomePanel_ImportFromFileRequested(object? sender, EventArgs e)
    {
        try
        {
            await _viewModel.ImportHostsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in WelcomePanel_ImportFromFileRequested: {ex}");
        }
    }

    private async void WelcomePanel_ImportFromSshConfigRequested(object? sender, EventArgs e)
    {
        try
        {
            await _viewModel.ImportFromSshConfigAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in WelcomePanel_ImportFromSshConfigRequested: {ex}");
        }
    }

    private async void WelcomePanel_ImportFromPuttyRequested(object? sender, EventArgs e)
    {
        try
        {
            await _viewModel.ImportFromPuttyAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in WelcomePanel_ImportFromPuttyRequested: {ex}");
        }
    }

    #endregion

    #region Dialog Helper Methods

    private void ShowSettingsDialog()
    {
        var settingsRepo = _serviceProvider.GetRequiredService<ISettingsRepository>();
        var historyRepo = _serviceProvider.GetRequiredService<IConnectionHistoryRepository>();
        var credentialCache = _serviceProvider.GetRequiredService<ICredentialCache>();
        var themeService = _serviceProvider.GetRequiredService<ITerminalThemeService>();
        var x11ForwardingService = _serviceProvider.GetRequiredService<IX11ForwardingService>();

        var viewModel = new SettingsViewModel(settingsRepo, historyRepo, credentialCache, themeService, x11ForwardingService);
        var dialog = new SettingsDialog(viewModel, _viewModel, _serviceProvider) { Owner = this };
        dialog.ShowDialog();
    }

    private void ShowHistoryDialog()
    {
        var historyRepo = _serviceProvider.GetRequiredService<IConnectionHistoryRepository>();
        var hostRepo = _serviceProvider.GetRequiredService<IHostRepository>();

        var viewModel = new ConnectionHistoryViewModel(historyRepo, hostRepo);
        var dialog = new ConnectionHistoryDialog(viewModel) { Owner = this };
        dialog.OnConnectRequested += async (host) =>
        {
            await _viewModel.Session.ConnectCommand.ExecuteAsync(host);
        };
        dialog.ShowDialog();
    }

    private void ShowSnippetsDialog()
    {
        var snippetRepo = _serviceProvider.GetRequiredService<ISnippetRepository>();
        var settingsRepo = _serviceProvider.GetRequiredService<ISettingsRepository>();

        var viewModel = new SnippetManagerViewModel(snippetRepo);
        var dialog = new SnippetManagerDialog(viewModel, settingsRepo) { Owner = this };
        dialog.OnExecuteSnippet += (snippet) =>
        {
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
        dialog.ShowDialog();
    }

    private void ShowKeyManagerDialog()
    {
        var keyManager = _serviceProvider.GetRequiredService<ISshKeyManager>();
        var managedKeyRepo = _serviceProvider.GetRequiredService<IManagedKeyRepository>();
        var ppkConverter = _serviceProvider.GetRequiredService<IPpkConverter>();
        var logger = _serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SshKeyManagerViewModel>>();

        var viewModel = new SshKeyManagerViewModel(keyManager, managedKeyRepo, ppkConverter, logger);
        var dialog = new SshKeyManagerDialog(viewModel, _serviceProvider) { Owner = this };
        dialog.ShowDialog();
    }

    private void ShowRecordingsDialog()
    {
        var viewModel = _serviceProvider.GetRequiredService<RecordingBrowserViewModel>();
        var dialog = new RecordingBrowserDialog(viewModel) { Owner = this };
        dialog.ShowDialog();
    }

    private async void ShowSerialQuickConnectDialog()
    {
        try
        {
            var serialService = _serviceProvider.GetRequiredService<ISerialConnectionService>();
            var viewModel = new SerialQuickConnectViewModel(serialService);
            var dialog = new SerialQuickConnectDialog(viewModel) { Owner = this };

            if (dialog.ShowDialog() == true)
            {
                var hostEntry = viewModel.CreateHostEntry();

                if (viewModel.ShouldSaveToHosts)
                {
                    await _viewModel.SaveTransientHostAsync(hostEntry);
                }

                await _viewModel.Session.ConnectCommand.ExecuteAsync(hostEntry);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in ShowSerialQuickConnectDialog: {ex}");
        }
    }

    private async void ShowQuickConnectDialog()
    {
        try
        {
            var serialService = _serviceProvider.GetRequiredService<ISerialConnectionService>();
            var viewModel = new ViewModels.Dialogs.QuickConnectViewModel(serialService);
            var dialog = new QuickConnectDialog(viewModel) { Owner = this };

            if (dialog.ShowDialog() == true && viewModel.CreatedHostEntry != null)
            {
                var hostEntry = viewModel.CreatedHostEntry;

                if (!string.IsNullOrEmpty(viewModel.Password) && viewModel.IsSshMode)
                {
                    var secretProtector = _serviceProvider.GetRequiredService<ISecretProtector>();
                    hostEntry.PasswordProtected = secretProtector.Protect(viewModel.Password);
                }

                await _viewModel.Session.ConnectCommand.ExecuteAsync(hostEntry);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in ShowQuickConnectDialog: {ex}");
        }
    }

    #endregion
}
