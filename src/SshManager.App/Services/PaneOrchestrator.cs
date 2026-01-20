using SshManager.App.Models;
using SshManager.App.ViewModels;
using SshManager.Core.Models;
using SshManager.Terminal;
using SshManager.Terminal.Services;
using Wpf.Ui;

namespace SshManager.App.Services;

/// <summary>
/// Orchestrates pane management, session creation, and pane-session connections.
/// </summary>
public class PaneOrchestrator : IPaneOrchestrator
{
    private readonly IPaneLayoutManager _paneLayoutManager;
    private readonly ISessionConnectionService _sessionConnectionService;
    private readonly IPortForwardingService _portForwardingService;
    private readonly ITerminalSessionManager _sessionManager;
    private readonly ISnackbarService _snackbarService;
    
    private MainWindowViewModel? _viewModel;
    
    public event EventHandler<SessionPickerRequestEventArgs>? SessionPickerRequested;

    public PaneOrchestrator(
        IPaneLayoutManager paneLayoutManager,
        ISessionConnectionService sessionConnectionService,
        IPortForwardingService portForwardingService,
        ITerminalSessionManager sessionManager,
        ISnackbarService snackbarService)
    {
        _paneLayoutManager = paneLayoutManager;
        _sessionConnectionService = sessionConnectionService;
        _portForwardingService = portForwardingService;
        _sessionManager = sessionManager;
        _snackbarService = snackbarService;
    }

    public void SetViewModel(MainWindowViewModel viewModel)
    {
        _viewModel = viewModel;
    }

    public void OnSessionCreated(TerminalSession session)
    {
        // Create a tabbed pane for the session (stacked with visibility switching)
        _paneLayoutManager.CreateTabbedPane(session);
    }

    public void OnSessionClosed(TerminalSession session)
    {
        // Find all panes for this session and close them
        var panes = _paneLayoutManager.FindPanesForSession(session).ToList();
        foreach (var pane in panes)
        {
            _paneLayoutManager.ClosePane(pane);
        }

        // Update port forwarding UI to remove forwardings for this session
        _viewModel?.PortForwardingManager.StopAllForSession(session.Id);
    }

    public void RequestSplit(SplitOrientation orientation, PaneLeafNode? requestingPane = null)
    {
        var paneToSplit = requestingPane ?? _paneLayoutManager.FocusedPane;
        if (paneToSplit == null)
            return;

        SessionPickerRequested?.Invoke(this, new SessionPickerRequestEventArgs(orientation, paneToSplit));
    }

    public async Task HandleSessionPickerResultAsync(SessionPickerResultData result, SplitOrientation orientation, PaneLeafNode paneToSplit)
    {
        if (_viewModel == null)
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
                        _paneLayoutManager.SplitPane(paneToSplit, orientation, newSession);
                        // Note: ConnectPaneToSession will be called by the UI layer after it gets the pane control
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

    public void MirrorCurrentPane()
    {
        var focusedPane = _paneLayoutManager.FocusedPane;
        if (focusedPane?.Session == null)
            return;

        _paneLayoutManager.MirrorPane(focusedPane, SplitOrientation.Vertical);
    }

    public void CloseCurrentPane()
    {
        var focusedPane = _paneLayoutManager.FocusedPane;
        if (focusedPane == null)
            return;

        _paneLayoutManager.ClosePane(focusedPane);
    }

    public void OnFocusedPaneChanged(PaneLeafNode? focusedPane)
    {
        // When pane focus changes, sync the session tabs selection
        if (_viewModel != null && focusedPane?.Session != null && _viewModel.CurrentSession != focusedPane.Session)
        {
            _viewModel.CurrentSession = focusedPane.Session;
        }
    }

    public void OnSessionTabSelected(TerminalSession? session, bool isSyncFromPaneFocus)
    {
        if (_viewModel == null || session == null)
            return;

        _viewModel.CurrentSession = session;

        // Switch visibility to show only the selected session's pane (for tabbed mode)
        _paneLayoutManager.SetActiveTabbedSession(session);

        // If this selection change was triggered by clicking on a terminal pane,
        // skip the focus operations to prevent an infinite focus loop
        if (isSyncFromPaneFocus)
            return;

        // Focus the pane for this session (works for both tabbed and split modes)
        var activePane = _paneLayoutManager.FindPanesForSession(session).FirstOrDefault();
        if (activePane != null)
        {
            // Update the visual focus indicator (border color)
            _paneLayoutManager.SetFocusedPane(activePane);
        }
    }

    public async Task ConnectPaneToSessionAsync(PaneLeafNode pane, TerminalSession session, Func<PaneLeafNode, ITerminalPaneTarget?> getPaneControlFunc)
    {
        if (session.Host == null)
            return;

        var paneControl = getPaneControlFunc(pane);
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
            if (session.Host.ConnectionType == ConnectionType.Ssh && session.Connection != null && _viewModel != null)
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

    public async Task OnPaneCloseRequestedAsync(PaneLeafNode pane)
    {
        var session = pane.Session;
        _paneLayoutManager.ClosePane(pane);

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

    public async Task OnSessionDisconnectedAsync(TerminalSession session)
    {
        // When a session disconnects (e.g., VM reboots), close it and remove all associated panes
        // Find and close all panes for this session
        var panes = _paneLayoutManager.FindPanesForSession(session).ToList();
        foreach (var pane in panes)
        {
            _paneLayoutManager.ClosePane(pane);
        }

        // Close the session itself
        await _sessionManager.CloseSessionAsync(session.Id);
    }
}
