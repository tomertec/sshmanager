using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Security;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the SSH Key Manager dialog.
/// </summary>
public partial class SshKeyManagerViewModel : ObservableObject
{
    private readonly ISshKeyManager _keyManager;
    private readonly IManagedKeyRepository _managedKeyRepo;
    private readonly IPpkConverter? _ppkConverter;
    private readonly ILogger<SshKeyManagerViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<SshKeyDisplayItem> _keys = [];

    [ObservableProperty]
    private SshKeyDisplayItem? _selectedKey;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;
    public event Func<Task>? RequestGenerateKey;
    public event Func<Task>? RequestImportPpk;

    public SshKeyManagerViewModel(
        ISshKeyManager keyManager,
        IManagedKeyRepository managedKeyRepo,
        IPpkConverter? ppkConverter = null,
        ILogger<SshKeyManagerViewModel>? logger = null)
    {
        _keyManager = keyManager;
        _managedKeyRepo = managedKeyRepo;
        _ppkConverter = ppkConverter;
        _logger = logger ?? NullLogger<SshKeyManagerViewModel>.Instance;
    }

    public async Task LoadKeysAsync()
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Loading keys...";

            Keys.Clear();

            // Get existing keys from the file system
            var existingKeys = await _keyManager.GetExistingKeysAsync();

            // Get tracked keys from the database
            var trackedKeys = await _managedKeyRepo.GetAllAsync();
            var trackedPaths = trackedKeys.ToDictionary(k => k.PrivateKeyPath, StringComparer.OrdinalIgnoreCase);

            foreach (var keyInfo in existingKeys)
            {
                var isTracked = trackedPaths.TryGetValue(keyInfo.PrivateKeyPath, out var trackedKey);

                Keys.Add(new SshKeyDisplayItem
                {
                    KeyInfo = keyInfo,
                    ManagedKey = trackedKey,
                    IsTracked = isTracked,
                    DisplayName = trackedKey?.DisplayName ?? keyInfo.DisplayName,
                    KeyTypeDisplay = keyInfo.KeyTypeString,
                    Fingerprint = keyInfo.Fingerprint ?? "Unknown",
                    IsEncrypted = keyInfo.IsEncrypted,
                    PrivateKeyPath = keyInfo.PrivateKeyPath
                });
            }

            StatusMessage = $"Found {Keys.Count} SSH keys";
            _logger.LogInformation("Loaded {Count} SSH keys", Keys.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load SSH keys");
            StatusMessage = $"Error loading keys: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task GenerateNewKeyAsync()
    {
        if (RequestGenerateKey != null)
        {
            await RequestGenerateKey.Invoke();
            await LoadKeysAsync();
        }
    }

    [RelayCommand]
    private async Task CopyPublicKeyAsync()
    {
        if (SelectedKey == null) return;

        try
        {
            var publicKey = await _keyManager.GetPublicKeyAsync(SelectedKey.PrivateKeyPath);
            Clipboard.SetText(publicKey);
            StatusMessage = "Public key copied to clipboard";
            _logger.LogDebug("Copied public key to clipboard: {Path}", SelectedKey.PrivateKeyPath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to copy public key");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ViewPublicKeyAsync()
    {
        if (SelectedKey == null) return;

        try
        {
            var publicKey = await _keyManager.GetPublicKeyAsync(SelectedKey.PrivateKeyPath);
            MessageBox.Show(
                publicKey,
                $"Public Key - {SelectedKey.DisplayName}",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to view public key");
            MessageBox.Show(
                $"Failed to read public key: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task DeleteKeyAsync()
    {
        if (SelectedKey == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the key '{SelectedKey.DisplayName}'?\n\nThis will delete both the private and public key files. This action cannot be undone.",
            "Confirm Delete",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            // Delete from file system
            await _keyManager.DeleteKeyAsync(SelectedKey.PrivateKeyPath);

            // Delete from database if tracked
            if (SelectedKey.ManagedKey != null)
            {
                await _managedKeyRepo.DeleteAsync(SelectedKey.ManagedKey.Id);
            }

            StatusMessage = $"Deleted key: {SelectedKey.DisplayName}";
            _logger.LogInformation("Deleted SSH key: {Path}", SelectedKey.PrivateKeyPath);

            await LoadKeysAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete key");
            MessageBox.Show(
                $"Failed to delete key: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task TrackKeyAsync()
    {
        if (SelectedKey == null || SelectedKey.IsTracked) return;

        try
        {
            var managedKey = new ManagedSshKey
            {
                DisplayName = SelectedKey.KeyInfo?.Comment ?? SelectedKey.KeyInfo?.FileName ?? "Unnamed Key",
                PrivateKeyPath = SelectedKey.PrivateKeyPath,
                KeyType = (int)(SelectedKey.KeyInfo?.KeyType ?? 0),
                KeySize = SelectedKey.KeyInfo?.KeySize ?? 0,
                Fingerprint = SelectedKey.Fingerprint,
                Comment = SelectedKey.KeyInfo?.Comment,
                IsEncrypted = SelectedKey.IsEncrypted
            };

            await _managedKeyRepo.AddAsync(managedKey);
            StatusMessage = $"Now tracking: {managedKey.DisplayName}";
            _logger.LogInformation("Added tracked key: {Path}", SelectedKey.PrivateKeyPath);

            await LoadKeysAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to track key");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UntrackKeyAsync()
    {
        if (SelectedKey == null || !SelectedKey.IsTracked || SelectedKey.ManagedKey == null) return;

        try
        {
            await _managedKeyRepo.DeleteAsync(SelectedKey.ManagedKey.Id);
            StatusMessage = $"Stopped tracking: {SelectedKey.DisplayName}";
            _logger.LogInformation("Removed tracked key: {Path}", SelectedKey.PrivateKeyPath);

            await LoadKeysAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to untrack key");
            StatusMessage = $"Error: {ex.Message}";
        }
    }

    [RelayCommand]
    private async Task ImportPpkAsync()
    {
        if (_ppkConverter == null)
        {
            MessageBox.Show(
                "PPK import is not available. The PPK converter service is not registered.",
                "Feature Not Available",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (RequestImportPpk != null)
        {
            await RequestImportPpk.Invoke();
            await LoadKeysAsync();
        }
    }

    private static int ParseKeyType(string keyTypeString)
    {
        return keyTypeString?.ToLowerInvariant() switch
        {
            "ssh-rsa" => (int)SshKeyType.Rsa2048,
            "ssh-ed25519" => (int)SshKeyType.Ed25519,
            "ecdsa-sha2-nistp256" => (int)SshKeyType.Ecdsa256,
            "ecdsa-sha2-nistp384" => (int)SshKeyType.Ecdsa384,
            "ecdsa-sha2-nistp521" => (int)SshKeyType.Ecdsa521,
            _ => 0
        };
    }

    private static int GetKeySizeFromType(string keyTypeString)
    {
        return keyTypeString?.ToLowerInvariant() switch
        {
            "ssh-rsa" => 2048, // Default RSA size, actual may vary
            "ssh-ed25519" => 256,
            "ecdsa-sha2-nistp256" => 256,
            "ecdsa-sha2-nistp384" => 384,
            "ecdsa-sha2-nistp521" => 521,
            _ => 0
        };
    }

    [RelayCommand]
    private void OpenSshDirectory()
    {
        try
        {
            var sshDir = _keyManager.GetDefaultSshDirectory();
            if (Directory.Exists(sshDir))
            {
                System.Diagnostics.Process.Start("explorer.exe", sshDir);
            }
            else
            {
                MessageBox.Show(
                    $"SSH directory does not exist: {sshDir}",
                    "Directory Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open SSH directory");
        }
    }

    [RelayCommand]
    private void Close()
    {
        DialogResult = true;
        RequestClose?.Invoke();
    }
}

/// <summary>
/// Display item for SSH keys in the list.
/// </summary>
public partial class SshKeyDisplayItem : ObservableObject
{
    public SshKeyInfo? KeyInfo { get; init; }
    public ManagedSshKey? ManagedKey { get; init; }

    [ObservableProperty]
    private bool _isTracked;

    [ObservableProperty]
    private string _displayName = "";

    [ObservableProperty]
    private string _keyTypeDisplay = "";

    [ObservableProperty]
    private string _fingerprint = "";

    [ObservableProperty]
    private bool _isEncrypted;

    [ObservableProperty]
    private string _privateKeyPath = "";
}
