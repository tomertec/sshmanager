using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Security;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the SSH key generation dialog.
/// </summary>
public partial class KeyGenerationViewModel : ObservableObject
{
    private readonly ISshKeyManager _keyManager;
    private readonly ILogger<KeyGenerationViewModel> _logger;

    [ObservableProperty]
    private SshKeyType _selectedKeyType = SshKeyType.Ed25519;

    [ObservableProperty]
    private string _keyName = "";

    [ObservableProperty]
    private string _comment = "";

    [ObservableProperty]
    private string _passphrase = "";

    [ObservableProperty]
    private string _confirmPassphrase = "";

    [ObservableProperty]
    private string _savePath = "";

    [ObservableProperty]
    private bool _isGenerating;

    [ObservableProperty]
    private string? _validationError;

    [ObservableProperty]
    private SshKeyPair? _generatedKey;

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    public IEnumerable<SshKeyType> AvailableKeyTypes => Enum.GetValues<SshKeyType>();

    public KeyGenerationViewModel(ISshKeyManager keyManager, ILogger<KeyGenerationViewModel>? logger = null)
    {
        _keyManager = keyManager;
        _logger = logger ?? NullLogger<KeyGenerationViewModel>.Instance;

        // Set default save path
        var sshDir = _keyManager.GetDefaultSshDirectory();
        SavePath = Path.Combine(sshDir, "id_ed25519");

        // Default comment to user@hostname
        Comment = $"{Environment.UserName}@{Environment.MachineName}";
    }

    partial void OnSelectedKeyTypeChanged(SshKeyType value)
    {
        // Update default file name based on key type
        var sshDir = _keyManager.GetDefaultSshDirectory();
        var fileName = value switch
        {
            SshKeyType.Rsa2048 => "id_rsa",
            SshKeyType.Rsa4096 => "id_rsa",
            SshKeyType.Ed25519 => "id_ed25519",
            SshKeyType.Ecdsa256 => "id_ecdsa",
            SshKeyType.Ecdsa384 => "id_ecdsa",
            SshKeyType.Ecdsa521 => "id_ecdsa",
            _ => "id_key"
        };

        // Only update if the user hasn't customized the path
        var currentFileName = Path.GetFileName(SavePath);
        if (currentFileName.StartsWith("id_"))
        {
            SavePath = Path.Combine(sshDir, fileName);
        }
    }

    [RelayCommand]
    private void BrowseSavePath()
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Save Private Key",
            FileName = Path.GetFileName(SavePath),
            InitialDirectory = Path.GetDirectoryName(SavePath) ?? _keyManager.GetDefaultSshDirectory(),
            Filter = "All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            SavePath = dialog.FileName;
        }
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        ValidationError = null;

        // Validate inputs
        var errors = ValidateInputs();
        if (errors.Count > 0)
        {
            ValidationError = string.Join("\n", errors);
            return;
        }

        try
        {
            IsGenerating = true;

            // Check if file already exists
            if (File.Exists(SavePath))
            {
                ValidationError = $"A key already exists at this path. Please choose a different name or delete the existing key.";
                return;
            }

            // Generate the key
            var passphrase = string.IsNullOrEmpty(Passphrase) ? null : Passphrase;
            var comment = string.IsNullOrEmpty(Comment) ? null : Comment;

            _logger.LogInformation("Generating {KeyType} key at {Path}", SelectedKeyType, SavePath);

            GeneratedKey = await _keyManager.GenerateKeyAsync(SelectedKeyType, passphrase, comment);

            // Save the key
            await _keyManager.SaveKeyPairAsync(GeneratedKey, SavePath);

            _logger.LogInformation("Key generated and saved successfully");

            DialogResult = true;
            RequestClose?.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate key");
            ValidationError = $"Failed to generate key: {ex.Message}";
        }
        finally
        {
            IsGenerating = false;
        }
    }

    private List<string> ValidateInputs()
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(SavePath))
        {
            errors.Add("Save path is required");
        }
        else
        {
            var directory = Path.GetDirectoryName(SavePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                try
                {
                    Directory.CreateDirectory(directory);
                }
                catch
                {
                    errors.Add($"Cannot create directory: {directory}");
                }
            }
        }

        if (!string.IsNullOrEmpty(Passphrase) && Passphrase != ConfirmPassphrase)
        {
            errors.Add("Passphrases do not match");
        }

        if (!string.IsNullOrEmpty(Passphrase) && Passphrase.Length < 5)
        {
            errors.Add("Passphrase must be at least 5 characters");
        }

        return errors;
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Gets a description for the selected key type.
    /// </summary>
    public string KeyTypeDescription => SelectedKeyType switch
    {
        SshKeyType.Rsa2048 => "RSA 2048-bit - Good compatibility, standard security",
        SshKeyType.Rsa4096 => "RSA 4096-bit - Good compatibility, higher security, slower",
        SshKeyType.Ed25519 => "Ed25519 - Recommended, modern, fast, highly secure",
        SshKeyType.Ecdsa256 => "ECDSA 256-bit (NIST P-256) - Good performance and security",
        SshKeyType.Ecdsa384 => "ECDSA 384-bit (NIST P-384) - Higher security than 256",
        SshKeyType.Ecdsa521 => "ECDSA 521-bit (NIST P-521) - Highest ECDSA security",
        _ => ""
    };
}
