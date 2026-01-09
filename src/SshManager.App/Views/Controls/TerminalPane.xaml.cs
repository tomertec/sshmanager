using System.Windows;
using System.Windows.Controls;
using SshManager.App.Models;
using SshManager.App.Services;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Terminal;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;

namespace SshManager.App.Views.Controls;

/// <summary>
/// A single terminal pane control with header and terminal display.
/// </summary>
public partial class TerminalPane : UserControl
{
    private PaneLeafNode? _paneNode;
    private bool _terminalAttached;

    /// <summary>
    /// Event raised when user requests a split operation.
    /// </summary>
    public event EventHandler<SplitRequestedEventArgs>? SplitRequested;

    /// <summary>
    /// Event raised when user requests to close this pane.
    /// </summary>
    public event EventHandler? CloseRequested;

    public TerminalPane()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        _paneNode = e.NewValue as PaneLeafNode;
        _terminalAttached = false;

        if (_paneNode != null)
        {
            // Subscribe to pane node property changes
            _paneNode.PropertyChanged += PaneNode_PropertyChanged;

            // Set terminal's DataContext to the session
            if (_paneNode.Session != null)
            {
                Terminal.DataContext = _paneNode.Session;
            }

            // Sync primary pane status for resize coordination
            Terminal.IsPrimaryPane = _paneNode.IsPrimaryForSession;
        }

        if (e.OldValue is PaneLeafNode oldNode)
        {
            oldNode.PropertyChanged -= PaneNode_PropertyChanged;
        }
    }

    private void PaneNode_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_paneNode == null)
            return;

        if (e.PropertyName == nameof(PaneLeafNode.Session))
        {
            Terminal.DataContext = _paneNode.Session;
            _terminalAttached = false;

            // If terminal is already loaded and we got a new session, attach to it
            if (Terminal.IsLoaded && _paneNode.Session != null)
            {
                AttachToSession(_paneNode.Session);
            }
        }
        else if (e.PropertyName == nameof(PaneLeafNode.IsPrimaryForSession))
        {
            // Sync primary pane status for resize coordination
            Terminal.IsPrimaryPane = _paneNode.IsPrimaryForSession;
        }
    }

    private void Terminal_Loaded(object sender, RoutedEventArgs e)
    {
        if (_paneNode?.Session != null && !_terminalAttached)
        {
            AttachToSession(_paneNode.Session);
        }
    }

    private void AttachToSession(TerminalSession session)
    {
        if (_terminalAttached)
            return;

        // Get services from DI
        var sshService = App.GetService<ISshConnectionService>();
        var broadcastService = App.GetService<IBroadcastInputService>();
        var serverStatsService = App.GetService<IServerStatsService>();

        // Set services on terminal
        Terminal.SetBroadcastService(broadcastService);
        Terminal.SetServerStatsService(serverStatsService);

        // Apply current terminal theme
        ApplyCurrentTheme();

        // If session is already connected, just attach to it
        if (session.IsConnected)
        {
            Terminal.AttachToSession(session);
            _terminalAttached = true;
            return;
        }

        // Otherwise, need to establish connection
        // This is handled by the MainWindow when a host is connected
        _terminalAttached = true;
    }

    /// <summary>
    /// Applies the current terminal theme from settings.
    /// </summary>
    private void ApplyCurrentTheme()
    {
        try
        {
            var settingsRepo = App.GetService<ISettingsRepository>();
            var themeService = App.GetService<ITerminalThemeService>();

            var settings = settingsRepo.GetAsync().GetAwaiter().GetResult();
            var theme = themeService.GetTheme(settings.TerminalThemeId)
                ?? themeService.GetTheme("default");

            if (theme != null)
            {
                Terminal.ApplyTheme(theme);
            }
        }
        catch
        {
            // Fall back to default theme on error
        }
    }

    /// <summary>
    /// Applies a specific terminal theme to this pane.
    /// </summary>
    public void ApplyTheme(TerminalTheme theme)
    {
        Terminal.ApplyTheme(theme);
    }

    private void Terminal_GotFocus(object sender, RoutedEventArgs e)
    {
        SetPaneFocus();
    }

    private void UserControl_GotFocus(object sender, RoutedEventArgs e)
    {
        SetPaneFocus();
    }

    private void SetPaneFocus()
    {
        if (_paneNode == null)
            return;

        var layoutManager = App.GetService<IPaneLayoutManager>();
        layoutManager.SetFocusedPane(_paneNode);
    }

    private void SplitVertical_Click(object sender, RoutedEventArgs e)
    {
        SplitRequested?.Invoke(this, new SplitRequestedEventArgs(SplitOrientation.Vertical));
    }

    private void SplitHorizontal_Click(object sender, RoutedEventArgs e)
    {
        SplitRequested?.Invoke(this, new SplitRequestedEventArgs(SplitOrientation.Horizontal));
    }

    private void ClosePane_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets the pane node associated with this control.
    /// </summary>
    public PaneLeafNode? PaneNode => _paneNode;

    /// <summary>
    /// Gets the terminal control.
    /// </summary>
    public SshManager.Terminal.Controls.SshTerminalControl TerminalControl => Terminal;

    /// <summary>
    /// Connects the terminal to SSH.
    /// </summary>
    public async Task ConnectAsync(
        ISshConnectionService sshService,
        TerminalConnectionInfo connectionInfo,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback)
    {
        await Terminal.ConnectAsync(sshService, connectionInfo, hostKeyCallback, kbInteractiveCallback);
        _terminalAttached = true;
    }

    /// <summary>
    /// Connects the terminal to SSH through a proxy chain.
    /// </summary>
    /// <param name="sshService">The SSH connection service.</param>
    /// <param name="connectionChain">Ordered list of connection info for each hop, ending with the target host.</param>
    /// <param name="hostKeyCallback">Callback for verifying host keys at each hop.</param>
    /// <param name="kbInteractiveCallback">Callback for keyboard-interactive auth at each hop.</param>
    public async Task ConnectWithProxyChainAsync(
        ISshConnectionService sshService,
        IReadOnlyList<TerminalConnectionInfo> connectionChain,
        HostKeyVerificationCallback? hostKeyCallback,
        KeyboardInteractiveCallback? kbInteractiveCallback)
    {
        await Terminal.ConnectWithProxyChainAsync(sshService, connectionChain, hostKeyCallback, kbInteractiveCallback);
        _terminalAttached = true;
    }
}

/// <summary>
/// Event args for split requests.
/// </summary>
public class SplitRequestedEventArgs : EventArgs
{
    public SplitOrientation Orientation { get; }

    public SplitRequestedEventArgs(SplitOrientation orientation)
    {
        Orientation = orientation;
    }
}
