using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.App.Services.Validation;
using SshManager.App.ViewModels.HostEdit;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the host edit dialog that orchestrates child ViewModels
/// for SSH, Serial, Metadata, and Environment Variable settings.
/// </summary>
public partial class HostDialogViewModel : ObservableObject
{
    private readonly IHostValidationService _validationService;
    private readonly ILogger<HostDialogViewModel> _logger;
    private readonly HostEntry _originalHost;

    #region Child ViewModels

    /// <summary>
    /// ViewModel for SSH connection settings.
    /// </summary>
    public SshConnectionSettingsViewModel SshSettings { get; }

    /// <summary>
    /// ViewModel for serial connection settings.
    /// </summary>
    public SerialConnectionSettingsViewModel SerialSettings { get; }

    /// <summary>
    /// ViewModel for host metadata (display name, notes, group, tags).
    /// </summary>
    public HostMetadataViewModel Metadata { get; }

    /// <summary>
    /// ViewModel for environment variables.
    /// </summary>
    public EnvironmentVariablesViewModel EnvironmentVariables { get; }

    #endregion

    #region Connection Type Properties

    /// <summary>
    /// Gets or sets whether this is an SSH connection.
    /// </summary>
    [ObservableProperty]
    private bool _isSshConnection = true;

    /// <summary>
    /// Gets or sets whether this is a serial connection.
    /// </summary>
    [ObservableProperty]
    private bool _isSerialConnection = false;

    #endregion

    #region Dialog State Properties

    /// <summary>
    /// Gets or sets whether this is a new host being created.
    /// </summary>
    [ObservableProperty]
    private bool _isNewHost;

    /// <summary>
    /// Gets or sets the current validation error message.
    /// </summary>
    [ObservableProperty]
    private string? _validationError;

    #endregion

    #region Computed Properties

    /// <summary>
    /// Gets the dialog title based on whether this is a new or existing host.
    /// </summary>
    public string Title => IsNewHost ? "Add Host" : "Edit Host";

    #endregion

    #region Dialog Result

    /// <summary>
    /// Gets the dialog result (true = saved, false = cancelled, null = not closed).
    /// </summary>
    public bool? DialogResult { get; private set; }

    /// <summary>
    /// Event raised when the dialog should be closed.
    /// </summary>
    public event Action? RequestClose;

    #endregion

    #region Events for Parent Window

    /// <summary>
    /// Event raised when the user wants to manage ProxyJump profiles.
    /// Forwarded from SshConnectionSettingsViewModel.
    /// </summary>
    public event EventHandler? ManageProxyJumpProfilesRequested;

    /// <summary>
    /// Event raised when the user wants to manage port forwarding for this host.
    /// Forwarded from SshConnectionSettingsViewModel.
    /// </summary>
    public event EventHandler? ManagePortForwardingRequested;

    #endregion

    /// <summary>
    /// Creates a new instance of the HostDialogViewModel.
    /// </summary>
    /// <param name="validationService">Service for validating host settings.</param>
    /// <param name="secretProtector">Service for password encryption/decryption.</param>
    /// <param name="serialConnectionService">Service for serial port operations.</param>
    /// <param name="agentDiagnosticsService">Optional SSH agent diagnostics service.</param>
    /// <param name="kerberosAuthService">Optional Kerberos authentication service.</param>
    /// <param name="hostProfileRepo">Optional host profile repository.</param>
    /// <param name="proxyJumpRepo">Optional proxy jump profile repository.</param>
    /// <param name="portForwardingRepo">Optional port forwarding profile repository.</param>
    /// <param name="tagRepo">Optional tag repository.</param>
    /// <param name="envVarRepo">Optional environment variable repository.</param>
    /// <param name="host">Optional host entry to edit (null for new host).</param>
    /// <param name="groups">Optional available groups for selection.</param>
    /// <param name="logger">Optional logger.</param>
    public HostDialogViewModel(
        IHostValidationService validationService,
        ISecretProtector secretProtector,
        ISerialConnectionService serialConnectionService,
        IAgentDiagnosticsService? agentDiagnosticsService = null,
        IKerberosAuthService? kerberosAuthService = null,
        IHostProfileRepository? hostProfileRepo = null,
        IProxyJumpProfileRepository? proxyJumpRepo = null,
        IPortForwardingProfileRepository? portForwardingRepo = null,
        ITagRepository? tagRepo = null,
        IHostEnvironmentVariableRepository? envVarRepo = null,
        HostEntry? host = null,
        IEnumerable<HostGroup>? groups = null,
        ILogger<HostDialogViewModel>? logger = null)
    {
        _validationService = validationService;
        _logger = logger ?? NullLogger<HostDialogViewModel>.Instance;
        _originalHost = host ?? new HostEntry();
        IsNewHost = host == null;

        // Initialize connection type from host
        IsSshConnection = _originalHost.ConnectionType == ConnectionType.Ssh;
        IsSerialConnection = _originalHost.ConnectionType == ConnectionType.Serial;

        // Create child ViewModels
        SshSettings = new SshConnectionSettingsViewModel(
            secretProtector,
            agentDiagnosticsService,
            kerberosAuthService,
            hostProfileRepo,
            proxyJumpRepo,
            portForwardingRepo,
            host,
            logger: null);

        SerialSettings = new SerialConnectionSettingsViewModel(
            serialConnectionService,
            host);

        Metadata = new HostMetadataViewModel(
            secretProtector,
            tagRepo,
            host,
            groups,
            logger: null);

        EnvironmentVariables = new EnvironmentVariablesViewModel(
            envVarRepo,
            host,
            logger: null);

        // Wire up events from child VMs
        SshSettings.ManageProxyJumpProfilesRequested += (s, e) => ManageProxyJumpProfilesRequested?.Invoke(this, e);
        SshSettings.ManagePortForwardingRequested += (s, e) => ManagePortForwardingRequested?.Invoke(this, e);

        _logger.LogDebug("HostDialogViewModel initialized for {Mode} host", IsNewHost ? "new" : "editing");
    }

    #region Async Load Methods

    /// <summary>
    /// Loads all async data for the dialog.
    /// Call this after constructing the ViewModel and showing the dialog.
    /// </summary>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadDataAsync(CancellationToken ct = default)
    {
        // Load all child VM data in parallel
        var tasks = new List<Task>
        {
            SshSettings.LoadAsync(ct),
            Metadata.LoadAsync(ct)
        };

        // Only load environment variables for existing hosts
        if (!IsNewHost)
        {
            tasks.Add(EnvironmentVariables.LoadAsync(ct));
        }

        await Task.WhenAll(tasks);

        _logger.LogDebug("Loaded all async data for host dialog");
    }

    #endregion

    #region Connection Type Toggle

    /// <summary>
    /// When IsSshConnection changes, toggle IsSerialConnection to act like radio buttons.
    /// </summary>
    partial void OnIsSshConnectionChanged(bool value)
    {
        if (value && IsSerialConnection)
        {
            IsSerialConnection = false;
        }
        else if (!value && !IsSerialConnection)
        {
            IsSerialConnection = true;
        }
    }

    /// <summary>
    /// When IsSerialConnection changes, toggle IsSshConnection to act like radio buttons.
    /// </summary>
    partial void OnIsSerialConnectionChanged(bool value)
    {
        if (value && IsSshConnection)
        {
            IsSshConnection = false;
        }
        else if (!value && !IsSshConnection)
        {
            IsSshConnection = true;
        }
    }

    #endregion

    #region Commands

    /// <summary>
    /// Validates and saves the host configuration.
    /// </summary>
    [RelayCommand]
    private void Save()
    {
        ValidationError = null;

        // Validate based on connection type using the validation service
        List<string> errors;
        if (IsSerialConnection)
        {
            errors = _validationService.ValidateSerialConnection(
                SerialSettings.SerialPortName,
                SerialSettings.SerialBaudRate,
                SerialSettings.SerialDataBits);
        }
        else
        {
            errors = _validationService.ValidateSshConnection(
                SshSettings.Hostname,
                SshSettings.Port,
                SshSettings.Username,
                SshSettings.AuthType,
                SshSettings.PrivateKeyPath,
                SshSettings.Password);
        }

        if (errors.Count > 0)
        {
            ValidationError = string.Join("\n", errors);
            _logger.LogWarning("Host validation failed: {Errors}", string.Join("; ", errors));
            return;
        }

        // Auto-set display name if not provided
        if (string.IsNullOrWhiteSpace(Metadata.DisplayName))
        {
            Metadata.DisplayName = IsSerialConnection
                ? SerialSettings.SerialPortName ?? "Serial"
                : SshSettings.Hostname;
        }

        if (IsSerialConnection)
        {
            _logger.LogInformation("Host validation passed, saving serial connection {DisplayName} ({SerialPortName})",
                Metadata.DisplayName, SerialSettings.SerialPortName);
        }
        else
        {
            _logger.LogInformation("Host validation passed, saving {DisplayName} ({Hostname}:{Port})",
                Metadata.DisplayName, SshSettings.Hostname, SshSettings.Port);
        }

        DialogResult = true;
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Cancels the dialog and discards changes.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    #endregion

    #region Host Assembly Methods

    /// <summary>
    /// Assembles and returns the HostEntry with all settings from child ViewModels.
    /// </summary>
    /// <returns>The updated HostEntry.</returns>
    public HostEntry GetHost()
    {
        // Set connection type
        _originalHost.ConnectionType = IsSerialConnection ? ConnectionType.Serial : ConnectionType.Ssh;
        _originalHost.UpdatedAt = DateTimeOffset.UtcNow;

        // Populate from appropriate connection settings based on type
        if (IsSerialConnection)
        {
            SerialSettings.PopulateHost(_originalHost);
            // For serial connections, set minimal SSH fields
            _originalHost.Hostname = SerialSettings.SerialPortName ?? "";
            _originalHost.Port = 0;
            _originalHost.Username = "";
        }
        else
        {
            SshSettings.PopulateHost(_originalHost);
            // Also populate serial settings to preserve them if switching types later
            SerialSettings.PopulateHost(_originalHost);
        }

        // Always populate metadata
        Metadata.PopulateHost(_originalHost);

        return _originalHost;
    }

    /// <summary>
    /// Gets the environment variables for saving.
    /// The caller should use IHostEnvironmentVariableRepository.SetForHostAsync() to save these.
    /// </summary>
    /// <returns>Collection of environment variables for the host.</returns>
    public IEnumerable<HostEnvironmentVariable> GetEnvironmentVariables()
    {
        return EnvironmentVariables.GetEnvironmentVariables(_originalHost.Id);
    }

    #endregion

    #region Legacy Properties for Backward Compatibility

    // These properties delegate to child ViewModels for backward compatibility
    // with existing XAML bindings that haven't been updated yet.

    /// <summary>
    /// Gets the available authentication types from SSH settings.
    /// </summary>
    public IEnumerable<AuthType> AuthTypes => SshConnectionSettingsViewModel.AuthTypes;

    /// <summary>
    /// Gets the available shell types from SSH settings.
    /// </summary>
    public IEnumerable<ShellType> ShellTypes => SshConnectionSettingsViewModel.ShellTypes;

    /// <summary>
    /// Gets the baud rate options from serial settings.
    /// </summary>
    public static int[] BaudRateOptions => SerialConnectionSettingsViewModel.BaudRateOptions;

    /// <summary>
    /// Gets the data bits options from serial settings.
    /// </summary>
    public static int[] DataBitsOptions => SerialConnectionSettingsViewModel.DataBitsOptions;

    /// <summary>
    /// Gets the stop bits options from serial settings.
    /// </summary>
    public static System.IO.Ports.StopBits[] StopBitsOptions => SerialConnectionSettingsViewModel.StopBitsOptions;

    /// <summary>
    /// Gets the parity options from serial settings.
    /// </summary>
    public static System.IO.Ports.Parity[] ParityOptions => SerialConnectionSettingsViewModel.ParityOptions;

    /// <summary>
    /// Gets the handshake options from serial settings.
    /// </summary>
    public static System.IO.Ports.Handshake[] HandshakeOptions => SerialConnectionSettingsViewModel.HandshakeOptions;

    /// <summary>
    /// Gets the line ending options from serial settings.
    /// </summary>
    public static string[] LineEndingOptions => SerialConnectionSettingsViewModel.LineEndingOptions;

    #endregion
}
