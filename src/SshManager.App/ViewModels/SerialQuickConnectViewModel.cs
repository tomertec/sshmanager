using System.IO.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the Serial Quick Connect dialog.
/// Allows connecting to a serial port without saving it to the hosts list.
/// </summary>
public partial class SerialQuickConnectViewModel : ObservableObject
{
    private readonly ISerialConnectionService _serialConnectionService;
    private readonly ILogger<SerialQuickConnectViewModel> _logger;

    [ObservableProperty]
    private string[] _availablePorts = [];

    [ObservableProperty]
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

    [ObservableProperty]
    private string? _displayName;

    [ObservableProperty]
    private string? _validationError;

    // Static arrays for ComboBox options
    public static int[] BaudRateOptions { get; } = [300, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400];
    public static int[] DataBitsOptions { get; } = [5, 6, 7, 8];
    public static StopBits[] StopBitsOptions { get; } = [StopBits.One, StopBits.OnePointFive, StopBits.Two];
    public static Parity[] ParityOptions { get; } = [Parity.None, Parity.Even, Parity.Odd, Parity.Mark, Parity.Space];
    public static Handshake[] HandshakeOptions { get; } = [Handshake.None, Handshake.XOnXOff, Handshake.RequestToSend, Handshake.RequestToSendXOnXOff];
    public static string[] LineEndingOptions { get; } = ["\r\n", "\n", "\r"];

    public bool? DialogResult { get; private set; }
    public bool ShouldSaveToHosts { get; private set; }

    public event Action? RequestClose;

    public SerialQuickConnectViewModel(
        ISerialConnectionService serialConnectionService,
        ILogger<SerialQuickConnectViewModel>? logger = null)
    {
        _serialConnectionService = serialConnectionService;
        _logger = logger ?? NullLogger<SerialQuickConnectViewModel>.Instance;

        RefreshPorts();
        _logger.LogDebug("SerialQuickConnectViewModel initialized");
    }

    /// <summary>
    /// Refreshes the list of available serial ports.
    /// </summary>
    [RelayCommand]
    private void RefreshPorts()
    {
        AvailablePorts = _serialConnectionService.GetAvailablePorts();
        if (AvailablePorts.Length > 0 && string.IsNullOrEmpty(SerialPortName))
        {
            SerialPortName = AvailablePorts[0];
        }
        _logger.LogDebug("Refreshed serial ports, found {PortCount} ports", AvailablePorts.Length);
    }

    /// <summary>
    /// Validates the serial port settings and connects without saving.
    /// </summary>
    [RelayCommand]
    private void Connect()
    {
        ValidationError = null;
        var errors = Validate();
        if (errors.Count > 0)
        {
            ValidationError = string.Join("\n", errors);
            _logger.LogWarning("Validation failed: {Errors}", string.Join("; ", errors));
            return;
        }

        ShouldSaveToHosts = false;
        DialogResult = true;
        _logger.LogInformation("Quick connecting to {PortName} at {BaudRate} baud", SerialPortName, SerialBaudRate);
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Validates the serial port settings and saves to hosts before connecting.
    /// </summary>
    [RelayCommand]
    private void ConnectAndSave()
    {
        ValidationError = null;
        var errors = Validate();
        if (errors.Count > 0)
        {
            ValidationError = string.Join("\n", errors);
            _logger.LogWarning("Validation failed: {Errors}", string.Join("; ", errors));
            return;
        }

        ShouldSaveToHosts = true;
        DialogResult = true;
        _logger.LogInformation("Connecting and saving {PortName} at {BaudRate} baud", SerialPortName, SerialBaudRate);
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Cancels the dialog.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    private List<string> Validate()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(SerialPortName))
        {
            errors.Add("COM Port is required");
        }

        if (SerialBaudRate <= 0)
        {
            errors.Add("Baud rate must be a positive number");
        }

        if (SerialDataBits < 5 || SerialDataBits > 8)
        {
            errors.Add("Data bits must be between 5 and 8");
        }

        return errors;
    }

    /// <summary>
    /// Creates a transient HostEntry from the current settings.
    /// </summary>
    public static HostEntry CreateSerialHostEntry(
        string? displayName,
        string? serialPortName,
        int serialBaudRate,
        int serialDataBits,
        StopBits serialStopBits,
        Parity serialParity,
        Handshake serialHandshake,
        bool serialDtrEnable,
        bool serialRtsEnable,
        bool serialLocalEcho,
        string serialLineEnding)
    {
        var effectiveDisplayName = string.IsNullOrWhiteSpace(displayName)
            ? serialPortName ?? "Serial"
            : displayName;

        return new HostEntry
        {
            Id = Guid.NewGuid(),
            DisplayName = effectiveDisplayName,
            ConnectionType = ConnectionType.Serial,
            SerialPortName = serialPortName,
            SerialBaudRate = serialBaudRate,
            SerialDataBits = serialDataBits,
            SerialStopBits = serialStopBits,
            SerialParity = serialParity,
            SerialHandshake = serialHandshake,
            SerialDtrEnable = serialDtrEnable,
            SerialRtsEnable = serialRtsEnable,
            SerialLocalEcho = serialLocalEcho,
            SerialLineEnding = serialLineEnding,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Creates a transient HostEntry from the current settings.
    /// </summary>
    public HostEntry CreateHostEntry()
    {
        return CreateSerialHostEntry(
            DisplayName,
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
    }
}
