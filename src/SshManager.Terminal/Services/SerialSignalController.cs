using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service implementation for controlling serial port signals and local echo.
/// </summary>
/// <remarks>
/// <para>
/// This service manages serial connection control signals (DTR, RTS, Break) and local echo mode.
/// It provides ICommand properties for UI binding and handles state management with proper
/// property change notifications.
/// </para>
/// <para>
/// <b>Usage Pattern:</b>
/// </para>
/// <code>
/// var controller = new SerialSignalController(logger);
/// controller.AttachToSession(session, serialBridge);
///
/// // Use commands in UI
/// myButton.Command = controller.ToggleDtrCommand;
///
/// // Or call methods directly
/// controller.SetDtr(true);
/// controller.SendBreak(250);
///
/// // Cleanup
/// controller.Detach();
/// </code>
/// <para>
/// The controller handles null session/bridge gracefully and logs all operations.
/// Commands are automatically enabled/disabled based on connection state.
/// </para>
/// </remarks>
public class SerialSignalController : ISerialSignalController
{
    private readonly ILogger<SerialSignalController> _logger;
    private TerminalSession? _session;
    private SerialTerminalBridge? _bridge;

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Event raised when any signal state changes (DTR, RTS, local echo).
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>
    /// Command to toggle the DTR signal on serial connections.
    /// </summary>
    public ICommand ToggleDtrCommand { get; }

    /// <summary>
    /// Command to toggle the RTS signal on serial connections.
    /// </summary>
    public ICommand ToggleRtsCommand { get; }

    /// <summary>
    /// Command to send a break signal on serial connections.
    /// </summary>
    public ICommand SendBreakCommand { get; }

    /// <summary>
    /// Command to toggle local echo for serial connections.
    /// </summary>
    public ICommand ToggleLocalEchoCommand { get; }

    /// <summary>
    /// Gets whether DTR (Data Terminal Ready) signal is currently enabled.
    /// </summary>
    public bool IsDtrEnabled => _session?.Host?.SerialDtrEnable ?? true;

    /// <summary>
    /// Gets whether RTS (Request To Send) signal is currently enabled.
    /// </summary>
    public bool IsRtsEnabled => _session?.Host?.SerialRtsEnable ?? true;

    /// <summary>
    /// Gets whether local echo is enabled for serial connections.
    /// When enabled, typed characters are echoed back locally instead of waiting for the remote device.
    /// </summary>
    public bool IsLocalEchoEnabled => _bridge?.LocalEcho ?? _session?.Host?.SerialLocalEcho ?? false;

    /// <summary>
    /// Gets whether the serial connection is currently active and ready for signal control.
    /// </summary>
    public bool IsConnected => _session?.SerialConnection?.IsConnected == true;

    /// <summary>
    /// Initializes a new instance of the <see cref="SerialSignalController"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output (optional, uses NullLogger if not provided).</param>
    public SerialSignalController(ILogger<SerialSignalController>? logger = null)
    {
        _logger = logger ?? NullLogger<SerialSignalController>.Instance;

        // Initialize commands with can-execute predicates
        ToggleDtrCommand = new RelayCommand(ToggleDtr, () => IsConnected);
        ToggleRtsCommand = new RelayCommand(ToggleRts, () => IsConnected);
        SendBreakCommand = new RelayCommand(() => SendBreak(), () => IsConnected);
        ToggleLocalEchoCommand = new RelayCommand(ToggleLocalEcho, () => IsConnected);
    }

    /// <summary>
    /// Attaches the controller to a terminal session and serial bridge.
    /// Must be called before signal control operations.
    /// </summary>
    /// <param name="session">The terminal session containing serial connection.</param>
    /// <param name="bridge">The serial terminal bridge for local echo control (optional).</param>
    public void AttachToSession(TerminalSession session, SerialTerminalBridge? bridge)
    {
        if (session == null)
        {
            throw new ArgumentNullException(nameof(session));
        }

        _session = session;
        _bridge = bridge;

        // Notify all properties have changed
        NotifyAllPropertiesChanged();

        _logger.LogDebug("SerialSignalController attached to session: {Title}", session.Title);
    }

    /// <summary>
    /// Detaches from the current session and bridge.
    /// </summary>
    public void Detach()
    {
        _session = null;
        _bridge = null;

        // Notify all properties have changed
        NotifyAllPropertiesChanged();

        _logger.LogDebug("SerialSignalController detached from session");
    }

    /// <summary>
    /// Sets the DTR (Data Terminal Ready) signal state.
    /// </summary>
    /// <param name="enabled">True to enable DTR, false to disable.</param>
    public void SetDtr(bool enabled)
    {
        if (_session?.SerialConnection == null)
        {
            _logger.LogWarning("Cannot set DTR: no serial connection available");
            return;
        }

        try
        {
            _session.SerialConnection.SetDtr(enabled);
            if (_session.Host != null)
            {
                _session.Host.SerialDtrEnable = enabled;
            }
            OnPropertyChanged(nameof(IsDtrEnabled));
            StateChanged?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("DTR signal set to {State}", enabled ? "enabled" : "disabled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set DTR signal");
        }
    }

    /// <summary>
    /// Sets the RTS (Request To Send) signal state.
    /// </summary>
    /// <param name="enabled">True to enable RTS, false to disable.</param>
    public void SetRts(bool enabled)
    {
        if (_session?.SerialConnection == null)
        {
            _logger.LogWarning("Cannot set RTS: no serial connection available");
            return;
        }

        try
        {
            _session.SerialConnection.SetRts(enabled);
            if (_session.Host != null)
            {
                _session.Host.SerialRtsEnable = enabled;
            }
            OnPropertyChanged(nameof(IsRtsEnabled));
            StateChanged?.Invoke(this, EventArgs.Empty);
            _logger.LogInformation("RTS signal set to {State}", enabled ? "enabled" : "disabled");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set RTS signal");
        }
    }

    /// <summary>
    /// Sends a break signal on the serial connection.
    /// </summary>
    /// <param name="durationMs">Duration of the break signal in milliseconds (default: 250ms).</param>
    public void SendBreak(int durationMs = 250)
    {
        if (_session?.SerialConnection == null)
        {
            _logger.LogWarning("Cannot send break: no serial connection available");
            return;
        }

        try
        {
            _session.SerialConnection.SendBreak(durationMs);
            _logger.LogInformation("Sent break signal ({DurationMs}ms)", durationMs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send break signal");
        }
    }

    /// <summary>
    /// Toggles the DTR signal (enable if disabled, disable if enabled).
    /// </summary>
    public void ToggleDtr()
    {
        SetDtr(!IsDtrEnabled);
    }

    /// <summary>
    /// Toggles the RTS signal (enable if disabled, disable if enabled).
    /// </summary>
    public void ToggleRts()
    {
        SetRts(!IsRtsEnabled);
    }

    /// <summary>
    /// Toggles local echo for serial connections.
    /// </summary>
    public void ToggleLocalEcho()
    {
        if (_bridge == null)
        {
            _logger.LogWarning("Cannot toggle local echo: no serial bridge available");
            return;
        }

        var newValue = !IsLocalEchoEnabled;
        _bridge.LocalEcho = newValue;
        OnPropertyChanged(nameof(IsLocalEchoEnabled));
        StateChanged?.Invoke(this, EventArgs.Empty);
        _logger.LogDebug("Local echo {State} for serial connection", newValue ? "enabled" : "disabled");
    }

    /// <summary>
    /// Notifies that all properties have changed and updates command states.
    /// </summary>
    private void NotifyAllPropertiesChanged()
    {
        OnPropertyChanged(nameof(IsConnected));
        OnPropertyChanged(nameof(IsDtrEnabled));
        OnPropertyChanged(nameof(IsRtsEnabled));
        OnPropertyChanged(nameof(IsLocalEchoEnabled));

        // Re-evaluate command CanExecute states
        (ToggleDtrCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ToggleRtsCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (SendBreakCommand as RelayCommand)?.NotifyCanExecuteChanged();
        (ToggleLocalEchoCommand as RelayCommand)?.NotifyCanExecuteChanged();
    }

    /// <summary>
    /// Raises the <see cref="PropertyChanged"/> event.
    /// </summary>
    /// <param name="propertyName">The name of the property that changed.</param>
    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
