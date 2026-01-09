using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Core.Models;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the host key verification dialog.
/// </summary>
public partial class HostKeyVerificationViewModel : ObservableObject
{
    [ObservableProperty]
    private string _hostname = "";

    [ObservableProperty]
    private int _port = 22;

    [ObservableProperty]
    private string _algorithm = "";

    [ObservableProperty]
    private string _fingerprint = "";

    [ObservableProperty]
    private string? _previousFingerprint;

    [ObservableProperty]
    private DateTimeOffset? _firstSeen;

    [ObservableProperty]
    private bool _isNewHost = true;

    [ObservableProperty]
    private bool _isFingerprintChanged;

    public bool? DialogResult { get; private set; }

    public event Action? RequestClose;

    /// <summary>
    /// Title shown in the dialog.
    /// </summary>
    public string Title => IsNewHost ? "New Host Key" : "Host Key Changed";

    /// <summary>
    /// Warning message shown in the dialog.
    /// </summary>
    public string WarningMessage => IsNewHost
        ? "The authenticity of this host can't be established. This is the first time connecting to this server."
        : "WARNING: The host key for this server has changed! This could indicate a man-in-the-middle attack, or the server's key may have been legitimately regenerated.";

    /// <summary>
    /// Icon type to show in the dialog.
    /// </summary>
    public string IconType => IsNewHost ? "Question" : "Warning";

    public void Initialize(
        string hostname,
        int port,
        string algorithm,
        string fingerprint,
        HostFingerprint? existingFingerprint)
    {
        Hostname = hostname;
        Port = port;
        Algorithm = algorithm;
        Fingerprint = fingerprint;

        if (existingFingerprint != null)
        {
            IsNewHost = false;
            IsFingerprintChanged = existingFingerprint.Fingerprint != fingerprint;
            PreviousFingerprint = existingFingerprint.Fingerprint;
            FirstSeen = existingFingerprint.FirstSeen;
        }
        else
        {
            IsNewHost = true;
            IsFingerprintChanged = false;
        }

        OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(WarningMessage));
        OnPropertyChanged(nameof(IconType));
    }

    [RelayCommand]
    private void Accept()
    {
        DialogResult = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Reject()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }
}
