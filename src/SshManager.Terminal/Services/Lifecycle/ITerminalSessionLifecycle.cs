using SshManager.Terminal.Controls;

namespace SshManager.Terminal.Services.Lifecycle;

/// <summary>
/// Service for managing terminal session lifecycle operations.
/// Handles session attachment, detachment, and disconnection coordination.
/// </summary>
/// <remarks>
/// <para>
/// This service encapsulates the session lifecycle management logic previously embedded
/// in SshTerminalControl. It coordinates with SSH and Serial session connectors
/// for proper cleanup and handles session mirroring (split pane attachment) scenarios.
/// </para>
/// <para>
/// <b>Session Ownership:</b> When a session is created via ConnectAsync on a connector,
/// the control owns the bridge. When attaching to an existing session (for split panes),
/// the control does NOT own the bridge and must not dispose it.
/// </para>
/// <para>
/// <b>Thread Safety:</b> Methods should be called from the UI thread. Events may fire
/// on background threads and callers should marshal to UI thread if needed.
/// </para>
/// </remarks>
public interface ITerminalSessionLifecycle
{
    /// <summary>
    /// Gets the currently attached terminal session.
    /// </summary>
    TerminalSession? CurrentSession { get; }

    /// <summary>
    /// Gets whether this lifecycle manager owns the bridge.
    /// When true, the bridge should be disposed during disconnection.
    /// When false (mirrored session), the bridge is shared and must not be disposed.
    /// </summary>
    bool OwnsBridge { get; }

    /// <summary>
    /// Gets whether the current session is connected.
    /// Returns true if either SSH or Serial connection is active.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Gets the SSH terminal bridge for the current session, if any.
    /// </summary>
    SshTerminalBridge? SshBridge { get; }

    /// <summary>
    /// Gets the serial terminal bridge for the current session, if any.
    /// </summary>
    SerialTerminalBridge? SerialBridge { get; }

    /// <summary>
    /// Attaches to an existing connected session (for session mirroring in split panes).
    /// </summary>
    /// <param name="session">The session to attach to.</param>
    /// <param name="terminal">The WebTerminalControl to initialize and use.</param>
    /// <param name="connectionHandler">The connection handler for bridge attachment.</param>
    /// <returns>Task that completes when attachment is done.</returns>
    /// <remarks>
    /// <para>
    /// When attaching to an existing session:
    /// - If the session is connected, the terminal is initialized and bridge events are wired
    /// - If a bridge already exists on the session, it is reused (OwnsBridge = false)
    /// - If no bridge exists, a new one is created (OwnsBridge = true)
    /// </para>
    /// <para>
    /// The attachment process:
    /// 1. Initializes the WebTerminalControl
    /// 2. Uses ITerminalConnectionHandler to attach to the session
    /// 3. Wires up data received events
    /// 4. Starts bridge reading if needed
    /// 5. Raises SessionAttached event
    /// </para>
    /// </remarks>
    Task AttachToSessionAsync(
        TerminalSession session,
        WebTerminalControl terminal,
        ITerminalConnectionHandler connectionHandler);

    /// <summary>
    /// Attaches to an existing connected session using a pre-configured data handler.
    /// </summary>
    /// <param name="session">The session to attach to.</param>
    /// <param name="terminal">The WebTerminalControl to initialize and use.</param>
    /// <param name="connectionHandler">The connection handler for bridge attachment.</param>
    /// <param name="onSshDataReceived">Callback invoked when SSH data is received.</param>
    /// <returns>Task that completes when attachment is done.</returns>
    Task AttachToSessionAsync(
        TerminalSession session,
        WebTerminalControl terminal,
        ITerminalConnectionHandler connectionHandler,
        Action<byte[]> onSshDataReceived);

    /// <summary>
    /// Sets the current session without performing full attachment.
    /// Used when a new connection is established directly via connectors.
    /// </summary>
    /// <param name="session">The session to set as current.</param>
    /// <param name="sshBridge">The SSH bridge for the session.</param>
    /// <param name="ownsBridge">Whether this lifecycle manager owns the bridge.</param>
    void SetSession(TerminalSession? session, SshTerminalBridge? sshBridge, bool ownsBridge);

    /// <summary>
    /// Sets the current session for a serial connection.
    /// </summary>
    /// <param name="session">The session to set as current.</param>
    /// <param name="serialBridge">The serial bridge for the session.</param>
    /// <param name="ownsBridge">Whether this lifecycle manager owns the bridge.</param>
    void SetSerialSession(TerminalSession? session, SerialTerminalBridge? serialBridge, bool ownsBridge);

    /// <summary>
    /// Detaches from the current session without disconnecting it.
    /// Used when a split pane is closed but the session should continue.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Detachment:
    /// - Unwires data received events from the bridge
    /// - If OwnsBridge is true, disposes the bridge and connection
    /// - Clears session reference
    /// - Raises SessionDetached event
    /// </para>
    /// </remarks>
    void Detach();

    /// <summary>
    /// Disconnects from the current session and cleans up all resources.
    /// </summary>
    /// <param name="sshConnector">The SSH session connector for cleanup.</param>
    /// <param name="serialConnector">The serial session connector for cleanup.</param>
    /// <remarks>
    /// <para>
    /// Full disconnection process:
    /// 1. Unwires bridge events via connectors
    /// 2. Disposes bridges if owned
    /// 3. Disposes SSH/Serial connections
    /// 4. Clears session references
    /// 5. Raises SessionDetached event
    /// </para>
    /// </remarks>
    void Disconnect(
        Connection.ISshSessionConnector? sshConnector,
        Connection.ISerialSessionConnector? serialConnector);

    /// <summary>
    /// Disconnects from the current session, clearing state without connector cleanup.
    /// Use this when the control manages its own bridge cleanup.
    /// </summary>
    /// <remarks>
    /// This is a simpler version of Disconnect that just clears the lifecycle state.
    /// The calling code is responsible for disposing bridges and connections.
    /// Raises SessionDetached event.
    /// </remarks>
    void Disconnect();

    /// <summary>
    /// Wires up disconnection event handlers for the current session.
    /// </summary>
    /// <param name="sshConnector">The SSH connector (provides bridge disconnection events).</param>
    /// <param name="onDisconnected">Callback when disconnection is detected.</param>
    void WireDisconnectionHandlers(
        Connection.ISshSessionConnector sshConnector,
        Action onDisconnected);

    /// <summary>
    /// Unwires disconnection event handlers.
    /// </summary>
    /// <param name="sshConnector">The SSH connector to unwire from.</param>
    void UnwireDisconnectionHandlers(Connection.ISshSessionConnector sshConnector);

    /// <summary>
    /// Event raised when a session is successfully attached.
    /// </summary>
    event EventHandler? SessionAttached;

    /// <summary>
    /// Event raised when a session is detached or disconnected.
    /// </summary>
    event EventHandler? SessionDetached;
}
