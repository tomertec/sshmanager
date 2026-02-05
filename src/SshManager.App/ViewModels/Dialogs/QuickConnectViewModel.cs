using System.IO.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Core.Models;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels.Dialogs;

/// <summary>
/// ViewModel for the Quick Connect dialog.
/// Supports both SSH and Serial port connections.
/// </summary>
public partial class QuickConnectViewModel : ObservableObject
{
    private readonly ISerialConnectionService? _serialConnectionService;

    /// <summary>
    /// Event raised when the dialog should close.
    /// </summary>
    public event Action? RequestClose;

    // Connection Type
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSshMode))]
    [NotifyPropertyChangedFor(nameof(IsSerialMode))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private ConnectionType _connectionType = ConnectionType.Ssh;

    public bool IsSshMode => ConnectionType == ConnectionType.Ssh;
    public bool IsSerialMode => ConnectionType == ConnectionType.Serial;

    // SSH Properties
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _hostname = "";

    [ObservableProperty]
    private int _port = 22;

    [ObservableProperty]
    private string _username = "";

    [ObservableProperty]
    private string _password = "";

    // Serial Port Properties
    [ObservableProperty]
    private string[] _availablePorts = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string? _serialPortName;

    [ObservableProperty]
    private int _serialBaudRate = 9600;

    [ObservableProperty]
    private int _serialDataBits = 8;

    [ObservableProperty]
    private StopBits _serialStopBits = StopBits.One;

    [ObservableProperty]
    private Parity _serialParity = Parity.None;

    [ObservableProperty]
    private Handshake _serialHandshake = Handshake.None;

    [ObservableProperty]
    private bool _serialDtrEnable = true;

    [ObservableProperty]
    private bool _serialRtsEnable = true;

    [ObservableProperty]
    private bool _serialLocalEcho = false;

    [ObservableProperty]
    private string _serialLineEnding = "\r\n";

    // Static arrays for Serial ComboBox options
    public static int[] BaudRateOptions { get; } = [300, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400];
    public static int[] DataBitsOptions { get; } = [5, 6, 7, 8];
    public static StopBits[] StopBitsOptions { get; } = [StopBits.One, StopBits.OnePointFive, StopBits.Two];
    public static Parity[] ParityOptions { get; } = [Parity.None, Parity.Even, Parity.Odd, Parity.Mark, Parity.Space];
    public static Handshake[] HandshakeOptions { get; } = [Handshake.None, Handshake.XOnXOff, Handshake.RequestToSend, Handshake.RequestToSendXOnXOff];
    public static string[] LineEndingOptions { get; } = ["\r\n", "\n", "\r"];

    /// <summary>
    /// Gets or sets the dialog result.
    /// </summary>
    public bool? DialogResult { get; private set; }

    /// <summary>
    /// Gets the created host entry after successful connection request.
    /// This is a temporary (non-persisted) host entry used for the connection.
    /// </summary>
    public HostEntry? CreatedHostEntry { get; private set; }

    /// <summary>
    /// Gets whether the user provided credentials (username and/or password).
    /// If false, the connection will use SSH Agent authentication.
    /// </summary>
    public bool HasCredentials => !string.IsNullOrWhiteSpace(Username) || !string.IsNullOrWhiteSpace(Password);

    /// <summary>
    /// Gets whether only hostname was provided (no credentials).
    /// In this case, the SSH connection will prompt for credentials interactively.
    /// </summary>
    public bool HostnameOnly => !HasCredentials;

    public QuickConnectViewModel() { }

    public QuickConnectViewModel(ISerialConnectionService? serialConnectionService)
    {
        _serialConnectionService = serialConnectionService;
        RefreshPorts();
    }

    private bool CanConnect => ConnectionType switch
    {
        ConnectionType.Ssh => !string.IsNullOrWhiteSpace(Hostname),
        ConnectionType.Serial => !string.IsNullOrWhiteSpace(SerialPortName),
        _ => false
    };

    [RelayCommand]
    private void SetSshMode()
    {
        ConnectionType = ConnectionType.Ssh;
    }

    [RelayCommand]
    private void SetSerialMode()
    {
        ConnectionType = ConnectionType.Serial;
        RefreshPorts();
    }

    [RelayCommand]
    private void RefreshPorts()
    {
        if (_serialConnectionService != null)
        {
            AvailablePorts = _serialConnectionService.GetAvailablePorts();
        }
        else
        {
            AvailablePorts = SerialPort.GetPortNames();
        }

        if (AvailablePorts.Length > 0 && string.IsNullOrEmpty(SerialPortName))
        {
            SerialPortName = AvailablePorts[0];
        }
    }

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private void Connect()
    {
        if (!CanConnect) return;

        if (ConnectionType == ConnectionType.Serial)
        {
            ConnectSerial();
        }
        else
        {
            ConnectSsh();
        }
    }

    private void ConnectSsh()
    {
        // Parse hostname:port format if user entered it that way
        var hostname = Hostname.Trim();
        var port = Port;

        // Check for host:port format
        var colonIndex = hostname.LastIndexOf(':');
        if (colonIndex > 0)
        {
            var portPart = hostname[(colonIndex + 1)..];
            if (int.TryParse(portPart, out var parsedPort) && parsedPort > 0 && parsedPort <= 65535)
            {
                hostname = hostname[..colonIndex];
                port = parsedPort;
            }
        }

        // Determine auth type based on provided credentials
        AuthType authType;
        if (!string.IsNullOrWhiteSpace(Password))
        {
            authType = AuthType.Password;
        }
        else
        {
            // Use SSH Agent for key-based auth or keyboard-interactive
            authType = AuthType.SshAgent;
        }

        // Create a temporary host entry for the connection
        CreatedHostEntry = new HostEntry
        {
            Id = Guid.NewGuid(),
            DisplayName = string.IsNullOrWhiteSpace(Username)
                ? hostname
                : $"{Username}@{hostname}",
            Hostname = hostname,
            Port = port,
            Username = string.IsNullOrWhiteSpace(Username) ? Environment.UserName : Username.Trim(),
            AuthType = authType,
            ConnectionType = ConnectionType.Ssh,
            // Note: Password is not stored in PasswordProtected since this is temporary
            // It will be passed separately for the connection
            Notes = "Quick Connect (temporary)"
        };

        DialogResult = true;
        RequestClose?.Invoke();
    }

    private void ConnectSerial()
    {
        CreatedHostEntry = SerialQuickConnectViewModel.CreateSerialHostEntry(
            null,
            SerialPortName,
            SerialBaudRate,
            SerialDataBits,
            SerialStopBits,
            SerialParity,
            SerialHandshake,
            SerialDtrEnable,
            SerialRtsEnable,
            SerialLocalEcho,
            SerialLineEnding);
        CreatedHostEntry.Notes = "Quick Connect (temporary)";

        DialogResult = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }
}
