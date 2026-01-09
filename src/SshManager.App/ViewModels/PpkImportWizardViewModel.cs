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
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the PPK Import Wizard dialog.
/// Guides users through a multi-step process for importing PPK files.
/// </summary>
public partial class PpkImportWizardViewModel : ObservableObject
{
    private readonly IPpkConverter _ppkConverter;
    private readonly ISshKeyManager _keyManager;
    private readonly IManagedKeyRepository _managedKeyRepo;
    private readonly IKeyEncryptionService? _encryptionService;
    private readonly IAgentKeyService? _agentKeyService;
    private readonly ILogger<PpkImportWizardViewModel> _logger;

    [ObservableProperty]
    private int _currentStep = 1;

    [ObservableProperty]
    private ObservableCollection<PpkImportItem> _items = [];

    [ObservableProperty]
    private PpkImportItem? _selectedItem;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusMessage;

    // Step 3: Options
    [ObservableProperty]
    private string _outputDirectory;

    [ObservableProperty]
    private bool _reEncryptKeys;

    [ObservableProperty]
    private string? _newPassphrase;

    [ObservableProperty]
    private bool _addToAgent;

    [ObservableProperty]
    private bool _trackInDatabase = true;

    // Progress tracking
    [ObservableProperty]
    private int _progressValue;

    [ObservableProperty]
    private int _progressMaximum = 100;

    [ObservableProperty]
    private string? _progressStatus;

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    // Computed properties
    public bool CanGoNext => CurrentStep < 4 &&
        (CurrentStep != 1 || Items.Any(i => i.IsSelected));

    public bool CanGoPrevious => CurrentStep > 1 && CurrentStep < 4;

    public bool CanImport => CurrentStep == 3;

    public bool IsStep1 => CurrentStep == 1;
    public bool IsStep2 => CurrentStep == 2;
    public bool IsStep3 => CurrentStep == 3;
    public bool IsStep4 => CurrentStep == 4;

    public int EncryptedFileCount => Items.Count(i => i.Info?.IsEncrypted == true && i.IsSelected);
    public int SelectedFileCount => Items.Count(i => i.IsSelected);

    public bool HasEncryptedFiles => Items.Any(i => i.Info?.IsEncrypted == true && i.IsSelected && string.IsNullOrEmpty(i.Passphrase));

    public PpkImportWizardViewModel(
        IPpkConverter ppkConverter,
        ISshKeyManager keyManager,
        IManagedKeyRepository managedKeyRepo,
        IKeyEncryptionService? encryptionService = null,
        IAgentKeyService? agentKeyService = null,
        ILogger<PpkImportWizardViewModel>? logger = null)
    {
        _ppkConverter = ppkConverter;
        _keyManager = keyManager;
        _managedKeyRepo = managedKeyRepo;
        _encryptionService = encryptionService;
        _agentKeyService = agentKeyService;
        _logger = logger ?? NullLogger<PpkImportWizardViewModel>.Instance;

        // Default output directory to ~/.ssh/
        _outputDirectory = _keyManager.GetDefaultSshDirectory();
    }

    [RelayCommand]
    private void BrowseFiles()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Select PuTTY Private Key Files",
            Filter = "PuTTY Private Key (*.ppk)|*.ppk|All Files (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        AddFiles(dialog.FileNames);
    }

    public async void AddFiles(string[] filePaths)
    {
        try
        {
            IsLoading = true;
            StatusMessage = "Analyzing PPK files...";

            foreach (var filePath in filePaths)
            {
                // Check if already added
                if (Items.Any(i => i.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                // Verify it's a PPK file
                if (!_ppkConverter.IsPpkFile(filePath))
                {
                    _logger.LogWarning("File is not a valid PPK file: {Path}", filePath);
                    continue;
                }

                // Get PPK info
                var info = await _ppkConverter.GetPpkInfoAsync(filePath);

                var item = new PpkImportItem
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Info = info,
                    IsSelected = true,
                    Status = ImportStatus.Pending
                };

                Items.Add(item);
            }

            StatusMessage = $"{Items.Count} PPK files loaded";
            OnPropertyChanged(nameof(SelectedFileCount));
            OnPropertyChanged(nameof(EncryptedFileCount));
            OnPropertyChanged(nameof(CanGoNext));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add PPK files");
            MessageBox.Show(
                $"Failed to analyze some PPK files: {ex.Message}",
                "Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void RemoveSelectedFile()
    {
        if (SelectedItem != null)
        {
            Items.Remove(SelectedItem);
            OnPropertyChanged(nameof(SelectedFileCount));
            OnPropertyChanged(nameof(EncryptedFileCount));
            OnPropertyChanged(nameof(CanGoNext));
        }
    }

    [RelayCommand]
    private void RemoveAllFiles()
    {
        Items.Clear();
        OnPropertyChanged(nameof(SelectedFileCount));
        OnPropertyChanged(nameof(EncryptedFileCount));
        OnPropertyChanged(nameof(CanGoNext));
    }

    [RelayCommand]
    private void BrowseOutputDirectory()
    {
        var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select output directory for converted keys",
            SelectedPath = OutputDirectory,
            ShowNewFolderButton = true
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            OutputDirectory = dialog.SelectedPath;
        }
    }

    [RelayCommand]
    private void NextStep()
    {
        if (CurrentStep == 1)
        {
            // Validate at least one file is selected
            if (!Items.Any(i => i.IsSelected))
            {
                MessageBox.Show(
                    "Please select at least one PPK file to import.",
                    "No Files Selected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Check if we have encrypted files that need passphrases
            var encryptedFiles = Items.Where(i => i.IsSelected && i.Info?.IsEncrypted == true).ToList();
            if (encryptedFiles.Any())
            {
                CurrentStep = 2;
            }
            else
            {
                // Skip step 2 if no encrypted files
                CurrentStep = 3;
            }
        }
        else if (CurrentStep == 2)
        {
            // Validate all encrypted files have passphrases
            var missingPassphrases = Items
                .Where(i => i.IsSelected && i.Info?.IsEncrypted == true && string.IsNullOrEmpty(i.Passphrase))
                .ToList();

            if (missingPassphrases.Any())
            {
                MessageBox.Show(
                    $"Please provide passphrases for all encrypted files.\n\n{missingPassphrases.Count} file(s) missing passphrase.",
                    "Missing Passphrases",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            CurrentStep = 3;
        }

        UpdateStepVisibility();
    }

    [RelayCommand]
    private void PreviousStep()
    {
        if (CurrentStep == 3)
        {
            // Go back to step 2 if there are encrypted files, otherwise step 1
            var hasEncryptedFiles = Items.Any(i => i.IsSelected && i.Info?.IsEncrypted == true);
            CurrentStep = hasEncryptedFiles ? 2 : 1;
        }
        else if (CurrentStep == 2)
        {
            CurrentStep = 1;
        }

        UpdateStepVisibility();
    }

    [RelayCommand]
    private async Task ImportAsync()
    {
        try
        {
            // Validate options
            if (string.IsNullOrWhiteSpace(OutputDirectory))
            {
                MessageBox.Show(
                    "Please select an output directory.",
                    "Invalid Options",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (ReEncryptKeys && string.IsNullOrWhiteSpace(NewPassphrase))
            {
                MessageBox.Show(
                    "Please provide a passphrase for re-encryption or disable the option.",
                    "Invalid Options",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            // Move to progress step
            CurrentStep = 4;
            UpdateStepVisibility();

            // Ensure output directory exists
            Directory.CreateDirectory(OutputDirectory);

            var selectedItems = Items.Where(i => i.IsSelected).ToList();
            ProgressMaximum = selectedItems.Count;
            ProgressValue = 0;

            var successCount = 0;
            var failureCount = 0;

            foreach (var item in selectedItems)
            {
                try
                {
                    item.Status = ImportStatus.InProgress;
                    ProgressStatus = $"Converting {item.FileName}...";

                    // Convert the PPK file
                    var result = await _ppkConverter.ConvertToOpenSshAsync(
                        item.FilePath,
                        item.Passphrase);

                    if (!result.Success || result.OpenSshPrivateKey == null || result.OpenSshPublicKey == null)
                    {
                        item.Status = ImportStatus.Failed;
                        item.ErrorMessage = result.ErrorMessage ?? "Unknown error";
                        failureCount++;
                        _logger.LogWarning("Failed to convert {File}: {Error}", item.FileName, item.ErrorMessage);
                        continue;
                    }

                    // Determine output file path
                    var baseFileName = Path.GetFileNameWithoutExtension(item.FilePath);
                    var outputPath = Path.Combine(OutputDirectory, baseFileName);

                    // Ensure unique filename
                    var counter = 1;
                    while (File.Exists(outputPath) || File.Exists($"{outputPath}.pub") ||
                           await _managedKeyRepo.ExistsByPathAsync(outputPath))
                    {
                        outputPath = Path.Combine(OutputDirectory, $"{baseFileName}_{counter}");
                        counter++;
                    }

                    // Re-encrypt if requested
                    string privateKeyContent = result.OpenSshPrivateKey;
                    if (ReEncryptKeys && !string.IsNullOrEmpty(NewPassphrase) && _encryptionService != null)
                    {
                        ProgressStatus = $"Re-encrypting {item.FileName}...";
                        privateKeyContent = await _encryptionService.EncryptKeyContentAsync(
                            result.OpenSshPrivateKey,
                            NewPassphrase);
                    }

                    // Save the converted key
                    await File.WriteAllTextAsync(outputPath, privateKeyContent);
                    await File.WriteAllTextAsync($"{outputPath}.pub", result.OpenSshPublicKey);

                    item.SavedPath = outputPath;

                    // Track in database if requested
                    if (TrackInDatabase)
                    {
                        ProgressStatus = $"Tracking {item.FileName} in database...";

                        var managedKey = new ManagedSshKey
                        {
                            DisplayName = result.Comment ?? baseFileName,
                            PrivateKeyPath = outputPath,
                            KeyType = ParseKeyType(result.KeyType ?? ""),
                            KeySize = GetKeySizeFromType(result.KeyType ?? ""),
                            Fingerprint = result.Fingerprint ?? "Unknown",
                            Comment = result.Comment,
                            IsEncrypted = ReEncryptKeys && !string.IsNullOrEmpty(NewPassphrase)
                        };

                        await _managedKeyRepo.AddAsync(managedKey);
                    }

                    // Add to SSH agent if requested
                    if (AddToAgent && _agentKeyService != null)
                    {
                        ProgressStatus = $"Adding {item.FileName} to SSH agent...";

                        try
                        {
                            await _agentKeyService.AddKeyToAgentAsync(
                                outputPath,
                                ReEncryptKeys ? NewPassphrase : null);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to add key to agent: {Path}", outputPath);
                            // Don't fail the whole import if agent add fails
                        }
                    }

                    item.Status = ImportStatus.Success;
                    successCount++;
                    _logger.LogInformation("Successfully imported PPK: {File} -> {Output}", item.FileName, outputPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to import {File}", item.FileName);
                    item.Status = ImportStatus.Failed;
                    item.ErrorMessage = ex.Message;
                    failureCount++;
                }
                finally
                {
                    ProgressValue++;
                }
            }

            ProgressStatus = $"Import complete: {successCount} succeeded, {failureCount} failed";

            MessageBox.Show(
                $"Import completed.\n\nSuccessful: {successCount}\nFailed: {failureCount}",
                "Import Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Import operation failed");
            MessageBox.Show(
                $"Import failed: {ex.Message}",
                "Import Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void Close()
    {
        var hasInProgress = Items.Any(i => i.Status == ImportStatus.InProgress);
        if (hasInProgress)
        {
            var result = MessageBox.Show(
                "Import is in progress. Are you sure you want to cancel?",
                "Confirm Cancel",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
            {
                return;
            }
        }

        DialogResult = CurrentStep == 4 && Items.Any(i => i.Status == ImportStatus.Success);
        RequestClose?.Invoke();
    }

    private void UpdateStepVisibility()
    {
        OnPropertyChanged(nameof(IsStep1));
        OnPropertyChanged(nameof(IsStep2));
        OnPropertyChanged(nameof(IsStep3));
        OnPropertyChanged(nameof(IsStep4));
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanGoPrevious));
        OnPropertyChanged(nameof(CanImport));
    }

    partial void OnCurrentStepChanged(int value)
    {
        UpdateStepVisibility();
    }

    partial void OnItemsChanged(ObservableCollection<PpkImportItem> value)
    {
        // Subscribe to item property changes
        foreach (var item in value)
        {
            item.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PpkImportItem.IsSelected))
                {
                    OnPropertyChanged(nameof(SelectedFileCount));
                    OnPropertyChanged(nameof(EncryptedFileCount));
                    OnPropertyChanged(nameof(CanGoNext));
                    OnPropertyChanged(nameof(HasEncryptedFiles));
                }
            };
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
            "ssh-rsa" => 2048,
            "ssh-ed25519" => 256,
            "ecdsa-sha2-nistp256" => 256,
            "ecdsa-sha2-nistp384" => 384,
            "ecdsa-sha2-nistp521" => 521,
            _ => 0
        };
    }
}

/// <summary>
/// Represents a single PPK file to be imported in the wizard.
/// </summary>
public partial class PpkImportItem : ObservableObject
{
    [ObservableProperty]
    private string _filePath = "";

    [ObservableProperty]
    private string _fileName = "";

    [ObservableProperty]
    private PpkFileInfo? _info;

    [ObservableProperty]
    private string? _passphrase;

    [ObservableProperty]
    private bool _isSelected = true;

    [ObservableProperty]
    private ImportStatus _status = ImportStatus.Pending;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string? _savedPath;

    public string DisplayKeyType => Info?.KeyType ?? "Unknown";

    public string DisplayEncrypted => Info?.IsEncrypted == true ? "Yes" : "No";

    public string DisplayComment => Info?.Comment ?? "";

    public string DisplayStatus => Status switch
    {
        ImportStatus.Pending => "Pending",
        ImportStatus.InProgress => "Converting...",
        ImportStatus.Success => "Success",
        ImportStatus.Failed => $"Failed: {ErrorMessage}",
        _ => ""
    };
}

/// <summary>
/// Status of a PPK import operation.
/// </summary>
public enum ImportStatus
{
    Pending,
    InProgress,
    Success,
    Failed
}
