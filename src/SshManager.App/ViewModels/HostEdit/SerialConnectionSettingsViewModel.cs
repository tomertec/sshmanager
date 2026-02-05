using System.IO.Ports;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Core.Models;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels.HostEdit;

/// <summary>
/// ViewModel for serial connection settings in the host edit dialog.
/// Contains all serial port configuration properties.
/// </summary>
public partial class SerialConnectionSettingsViewModel : ObservableObject
{
    private readonly ISerialConnectionService _serialConnectionService;

    // Serial Port Connection Properties
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

    // Static arrays for ComboBox options
    public static int[] BaudRateOptions { get; } = [300, 1200, 2400, 4800, 9600, 19200, 38400, 57600, 115200, 230400];
    public static int[] DataBitsOptions { get; } = [5, 6, 7, 8];
    public static StopBits[] StopBitsOptions { get; } = [StopBits.One, StopBits.OnePointFive, StopBits.Two];
    public static Parity[] ParityOptions { get; } = [Parity.None, Parity.Even, Parity.Odd, Parity.Mark, Parity.Space];
    public static Handshake[] HandshakeOptions { get; } = [Handshake.None, Handshake.XOnXOff, Handshake.RequestToSend, Handshake.RequestToSendXOnXOff];
    public static string[] LineEndingOptions { get; } = ["\r\n", "\n", "\r"];

    /// <summary>
    /// Creates a new instance of the SerialConnectionSettingsViewModel.
    /// </summary>
    /// <param name="serialConnectionService">Service for serial port operations.</param>
    /// <param name="host">Optional host entry to load settings from.</param>
    public SerialConnectionSettingsViewModel(ISerialConnectionService serialConnectionService, HostEntry? host = null)
    {
        _serialConnectionService = serialConnectionService;

        // Load settings from host if provided
        if (host != null)
        {
            LoadFromHost(host);
        }

        // Initialize available ports list
        RefreshPorts();
    }

    /// <summary>
    /// Loads serial connection settings from a HostEntry.
    /// </summary>
    /// <param name="host">The host entry to load settings from.</param>
    public void LoadFromHost(HostEntry host)
    {
        SerialPortName = host.SerialPortName;
        SerialBaudRate = host.SerialBaudRate;
        SerialDataBits = host.SerialDataBits;
        SerialStopBits = host.SerialStopBits;
        SerialParity = host.SerialParity;
        SerialHandshake = host.SerialHandshake;
        SerialDtrEnable = host.SerialDtrEnable;
        SerialRtsEnable = host.SerialRtsEnable;
        SerialLocalEcho = host.SerialLocalEcho;
        SerialLineEnding = host.SerialLineEnding;
    }

    /// <summary>
    /// Populates a HostEntry with the current serial connection settings.
    /// </summary>
    /// <param name="host">The host entry to populate.</param>
    public void PopulateHost(HostEntry host)
    {
        host.SerialPortName = SerialPortName;
        host.SerialBaudRate = SerialBaudRate;
        host.SerialDataBits = SerialDataBits;
        host.SerialStopBits = SerialStopBits;
        host.SerialParity = SerialParity;
        host.SerialHandshake = SerialHandshake;
        host.SerialDtrEnable = SerialDtrEnable;
        host.SerialRtsEnable = SerialRtsEnable;
        host.SerialLocalEcho = SerialLocalEcho;
        host.SerialLineEnding = SerialLineEnding;
    }

    /// <summary>
    /// Validates the serial connection settings.
    /// </summary>
    /// <returns>A list of validation error messages, empty if valid.</returns>
    public List<string> Validate()
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
    }
}
