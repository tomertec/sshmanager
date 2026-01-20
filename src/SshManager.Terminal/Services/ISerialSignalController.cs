using System;
using System.ComponentModel;
using System.Windows.Input;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service for controlling serial port signals (DTR, RTS, Break) and local echo.
/// Manages the state of serial connection control signals and provides commands for UI binding.
/// </summary>
/// <remarks>
/// <para>
/// This service abstracts serial signal control logic from the terminal control,
/// providing a clean interface for managing:
/// </para>
/// <list type="bullet">
/// <item>DTR (Data Terminal Ready) signal control</item>
/// <item>RTS (Request To Send) signal control</item>
/// <item>Break signal transmission</item>
/// <item>Local echo mode for serial connections</item>
/// </list>
/// <para>
/// The service must be attached to a <see cref="TerminalSession"/> and <see cref="SerialTerminalBridge"/>
/// before it can control signals. It handles null session/bridge gracefully and logs operations.
/// </para>
/// </remarks>
public interface ISerialSignalController : INotifyPropertyChanged
{
    /// <summary>
    /// Gets whether DTR (Data Terminal Ready) signal is currently enabled.
    /// </summary>
    bool IsDtrEnabled { get; }

    /// <summary>
    /// Gets whether RTS (Request To Send) signal is currently enabled.
    /// </summary>
    bool IsRtsEnabled { get; }

    /// <summary>
    /// Gets whether local echo is enabled for serial connections.
    /// When enabled, typed characters are echoed back locally instead of waiting for the remote device.
    /// </summary>
    bool IsLocalEchoEnabled { get; }

    /// <summary>
    /// Gets whether the serial connection is currently active and ready for signal control.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Command to toggle the DTR signal on serial connections.
    /// </summary>
    ICommand ToggleDtrCommand { get; }

    /// <summary>
    /// Command to toggle the RTS signal on serial connections.
    /// </summary>
    ICommand ToggleRtsCommand { get; }

    /// <summary>
    /// Command to send a break signal on serial connections.
    /// </summary>
    ICommand SendBreakCommand { get; }

    /// <summary>
    /// Command to toggle local echo for serial connections.
    /// </summary>
    ICommand ToggleLocalEchoCommand { get; }

    /// <summary>
    /// Sets the DTR (Data Terminal Ready) signal state.
    /// </summary>
    /// <param name="enabled">True to enable DTR, false to disable.</param>
    void SetDtr(bool enabled);

    /// <summary>
    /// Sets the RTS (Request To Send) signal state.
    /// </summary>
    /// <param name="enabled">True to enable RTS, false to disable.</param>
    void SetRts(bool enabled);

    /// <summary>
    /// Sends a break signal on the serial connection.
    /// </summary>
    /// <param name="durationMs">Duration of the break signal in milliseconds (default: 250ms).</param>
    void SendBreak(int durationMs = 250);

    /// <summary>
    /// Toggles the DTR signal (enable if disabled, disable if enabled).
    /// </summary>
    void ToggleDtr();

    /// <summary>
    /// Toggles the RTS signal (enable if disabled, disable if enabled).
    /// </summary>
    void ToggleRts();

    /// <summary>
    /// Toggles local echo for serial connections.
    /// </summary>
    void ToggleLocalEcho();

    /// <summary>
    /// Attaches the controller to a terminal session and serial bridge.
    /// Must be called before signal control operations.
    /// </summary>
    /// <param name="session">The terminal session containing serial connection.</param>
    /// <param name="bridge">The serial terminal bridge for local echo control (optional).</param>
    void AttachToSession(TerminalSession session, SerialTerminalBridge? bridge);

    /// <summary>
    /// Detaches from the current session and bridge.
    /// </summary>
    void Detach();

    /// <summary>
    /// Event raised when any signal state changes (DTR, RTS, local echo).
    /// </summary>
    event EventHandler? StateChanged;
}
