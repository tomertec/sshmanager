using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using SshManager.App.Models;
using SshManager.App.Services;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Terminal;
using SshManager.Terminal.Controls;
using SshManager.Terminal.Models;
using SshManager.Terminal.Services;
using SshManager.Terminal.Services.Recording;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Controls;

/// <summary>
/// A single terminal pane control with header and terminal display.
/// Implements ITerminalPaneTarget for connection orchestration.
/// </summary>
public partial class TerminalPane : UserControl, ITerminalPaneTarget
{
    private PaneLeafNode? _paneNode;
    private bool _terminalAttached;
    private readonly object _attachLock = new();
    private IServiceProvider? _serviceProvider;

    /// <summary>
    /// Event raised when user requests a split operation.
    /// </summary>
    public event EventHandler<SplitRequestedEventArgs>? SplitRequested;

    /// <summary>
    /// Event raised when user requests to close this pane.
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Event raised when the SSH session is disconnected (remote disconnect, error, etc.).
    /// </summary>
    public event EventHandler<TerminalSession>? SessionDisconnected;

    public TerminalPane()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Subscribe to terminal disconnect event
        Terminal.Disconnected += Terminal_Disconnected;

        // Subscribe to terminal focus event for reliable focus tracking
        // This is more reliable than WPF's GotFocus for WebView2-based controls
        Terminal.FocusReceived += Terminal_FocusReceived;

        // Handle visibility changes to refresh WebView2 when becoming visible
        // WebView2 controls may not properly repaint after Hidden → Visible transitions
        IsVisibleChanged += OnIsVisibleChanged;

        // Handle permanent removal for cleanup
        Unloaded += OnUnloaded;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        // Only perform cleanup if the pane is being removed from the visual tree permanently,
        // not during tab switching. Check if we still have a parent in the visual tree.
        // When truly removed, the parent will be null after the Unloaded event processes.
    }

    /// <summary>
    /// Permanently cleans up this pane's resources. Call when the pane is being removed
    /// from the layout (session closed, pane closed), NOT during tab switches.
    /// </summary>
    public void CleanupResources()
    {
        // Unsubscribe all event handlers to prevent leaks
        DataContextChanged -= OnDataContextChanged;
        Terminal.Disconnected -= Terminal_Disconnected;
        Terminal.FocusReceived -= Terminal_FocusReceived;
        IsVisibleChanged -= OnIsVisibleChanged;
        Unloaded -= OnUnloaded;

        if (_paneNode != null)
        {
            _paneNode.PropertyChanged -= PaneNode_PropertyChanged;
            _paneNode = null;
        }

        // Dispose the WebView2 terminal and output buffer
        Terminal.DisposeTerminal();
    }

    /// <summary>
    /// Sets the service provider for resolving dependencies.
    /// Must be called before using services.
    /// </summary>
    /// <param name="serviceProvider">The service provider.</param>
    public void SetServiceProvider(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true && _terminalAttached)
        {
            // Dispatch the refresh to allow the layout to complete first
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() =>
            {
                Terminal.RefreshTerminal();
                Terminal.FocusInput();
            }));
        }
    }

    private void Terminal_Disconnected(object? sender, EventArgs e)
    {
        // Propagate the disconnect event with the session info
        if (_paneNode?.Session != null)
        {
            SessionDisconnected?.Invoke(this, _paneNode.Session);
        }
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

    private async void PaneNode_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        try
        {
            if (_paneNode == null)
                return;

            if (e.PropertyName == nameof(PaneLeafNode.Session))
            {
                Terminal.DataContext = _paneNode.Session;
                _terminalAttached = false;

                // Sync recording button state with the new session
                UpdateRecordButtonState(_paneNode.Session?.IsRecording ?? false);

                // If terminal is already loaded and we got a new session, attach to it
                if (Terminal.IsLoaded && _paneNode.Session != null)
                {
                    await AttachToSessionAsync(_paneNode.Session);
                }
            }
            else if (e.PropertyName == nameof(PaneLeafNode.IsPrimaryForSession))
            {
                // Sync primary pane status for resize coordination
                Terminal.IsPrimaryPane = _paneNode.IsPrimaryForSession;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in PaneNode_PropertyChanged: {ex.Message}");
        }
    }

    private async void Terminal_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (_paneNode?.Session != null && !_terminalAttached)
            {
                await AttachToSessionAsync(_paneNode.Session);
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error in Terminal_Loaded: {ex.Message}");
        }
    }

    private async Task AttachToSessionAsync(TerminalSession session)
    {
        // Thread-safe guard against double-attach from concurrent async void callers
        lock (_attachLock)
        {
            if (_terminalAttached)
                return;
            _terminalAttached = true;
        }

        if (_serviceProvider == null)
        {
            System.Diagnostics.Debug.WriteLine("Service provider not set on TerminalPane");
            return;
        }

        // Get services from DI
        var broadcastService = _serviceProvider.GetRequiredService<IBroadcastInputService>();
        var serverStatsService = _serviceProvider.GetRequiredService<IServerStatsService>();
        var focusTracker = _serviceProvider.GetRequiredService<ITerminalFocusTracker>();

        // Set services on terminal
        Terminal.SetBroadcastService(broadcastService);
        Terminal.SetServerStatsService(serverStatsService);
        Terminal.SetFocusTracker(focusTracker);

        // Apply current terminal theme
        ApplyCurrentTheme();

        // If session is already connected, just attach to it
        if (session.IsConnected)
        {
            await Terminal.AttachToSessionAsync(session);
        }
    }

    /// <summary>
    /// Applies the current terminal theme from settings.
    /// </summary>
    private async void ApplyCurrentTheme()
    {
        try
        {
            if (_serviceProvider == null)
            {
                System.Diagnostics.Debug.WriteLine("Service provider not set on TerminalPane");
                return;
            }

            var settingsRepo = _serviceProvider.GetRequiredService<ISettingsRepository>();
            var themeService = _serviceProvider.GetRequiredService<ITerminalThemeService>();

            var settings = await settingsRepo.GetAsync();
            var theme = themeService.GetTheme(settings.TerminalThemeId)
                ?? themeService.GetTheme("default");

            if (theme != null)
            {
                Terminal.ApplyTheme(theme);
            }

            Terminal.TerminalFontFamily = settings.TerminalFontFamily;
            Terminal.TerminalFontSize = settings.TerminalFontSize;
            Terminal.ScrollbackBufferSize = settings.ScrollbackBufferSize;
            Terminal.MaxLinesInMemory = settings.TerminalBufferInMemoryLines;
        }
        catch (Exception ex)
        {
            // Log error and fall back to default theme
            System.Diagnostics.Debug.WriteLine($"Failed to apply theme: {ex.Message}");
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

    private void Terminal_FocusReceived(object? sender, EventArgs e)
    {
        // Handle focus when WebView2 receives focus (more reliable than WPF GotFocus for WebView2)
        SetPaneFocus();
    }

    private void UserControl_GotFocus(object sender, RoutedEventArgs e)
    {
        SetPaneFocus();
    }

    private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        SetPaneFocus();
        // Focus the terminal to receive keyboard input after the click event processing completes
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Input, new Action(() =>
        {
            Terminal.FocusInput();
        }));
    }

    private void SetPaneFocus()
    {
        if (_paneNode == null || _serviceProvider == null)
            return;

        var layoutManager = _serviceProvider.GetRequiredService<IPaneLayoutManager>();
        layoutManager.SetFocusedPane(_paneNode);
    }

    private void SplitVertical_Click(object sender, RoutedEventArgs e)
    {
        SetPaneFocus();
        SplitRequested?.Invoke(this, new SplitRequestedEventArgs(SplitOrientation.Vertical));
    }

    private void SplitHorizontal_Click(object sender, RoutedEventArgs e)
    {
        SetPaneFocus();
        SplitRequested?.Invoke(this, new SplitRequestedEventArgs(SplitOrientation.Horizontal));
    }

    private void ClosePane_Click(object sender, RoutedEventArgs e)
    {
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private async void RecordButton_Click(object sender, RoutedEventArgs e)
    {
        if (_paneNode?.Session == null || _serviceProvider == null)
            return;

        var recordingService = _serviceProvider.GetRequiredService<ISessionRecordingService>();
        var session = _paneNode.Session;

        if (session.IsRecording)
        {
            // Stop recording
            await recordingService.StopRecordingAsync(session.Id);
            session.SessionRecorder = null;
            UpdateRecordButtonState(false);
        }
        else
        {
            // Start recording - use default terminal dimensions
            var cols = 80;
            var rows = 24;
            var recorder = await recordingService.StartRecordingAsync(
                session.Id,
                session.Host,
                cols,
                rows,
                $"{session.Host?.DisplayName ?? "Session"} - {DateTime.Now:yyyy-MM-dd HH:mm}");
            session.SessionRecorder = recorder;
            UpdateRecordButtonState(true);
        }
    }

    private void UpdateRecordButtonState(bool isRecording)
    {
        if (isRecording)
        {
            RecordButton.Icon = new SymbolIcon(SymbolRegular.RecordStop24);
            RecordButton.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE8, 0x11, 0x23));
            RecordButton.ToolTip = "Stop Recording";
            RecordingIndicator.Visibility = Visibility.Visible;
        }
        else
        {
            RecordButton.Icon = new SymbolIcon(SymbolRegular.Record20);
            RecordButton.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.White);
            RecordButton.ToolTip = "Start Recording";
            RecordingIndicator.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Gets the pane node associated with this control.
    /// </summary>
    public PaneLeafNode? PaneNode => _paneNode;

    /// <summary>
    /// Gets the terminal control.
    /// </summary>
    public SshTerminalControl TerminalControl => Terminal;

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

    /// <summary>
    /// Connects the terminal to a serial port.
    /// </summary>
    /// <param name="serialService">The serial connection service.</param>
    /// <param name="connectionInfo">Serial port connection parameters.</param>
    /// <param name="session">The terminal session to associate with the connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ConnectSerialAsync(
        ISerialConnectionService serialService,
        SerialConnectionInfo connectionInfo,
        TerminalSession session,
        CancellationToken cancellationToken = default)
    {
        await Terminal.ConnectSerialAsync(serialService, connectionInfo, session, cancellationToken);
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
