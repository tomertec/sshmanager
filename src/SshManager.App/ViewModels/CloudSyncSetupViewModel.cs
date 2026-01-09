using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.App.Services;
using SshManager.Data.Repositories;

namespace SshManager.App.ViewModels;

public partial class CloudSyncSetupViewModel : ObservableObject
{
    private readonly ICloudSyncService _cloudSyncService;
    private readonly CloudSyncHostedService _cloudSyncHostedService;
    private readonly ISettingsRepository _settingsRepo;
    private readonly IOneDrivePathDetector _oneDriveDetector;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _loadingMessage = "";

    [ObservableProperty]
    private bool _isSyncEnabled;

    [ObservableProperty]
    private bool _isUnlocked;

    [ObservableProperty]
    private string _syncFolderPath = "";

    [ObservableProperty]
    private string _lastSyncText = "";

    [ObservableProperty]
    private string _deviceInfo = "";

    [ObservableProperty]
    private int _syncIntervalMinutes = 5;

    [ObservableProperty]
    private bool _passphraseMismatch;

    [ObservableProperty]
    private bool _showConfirmPassphrase;

    public bool IsSyncNotEnabled => !IsSyncEnabled;
    public bool IsOneDriveUnavailable => !_oneDriveDetector.IsOneDriveAvailable();
    public bool NeedsUnlock => IsSyncEnabled && !IsUnlocked;

    public event Action? RequestClose;
    public Func<string>? GetPassphrase { get; set; }
    public Func<string>? GetConfirmPassphrase { get; set; }

    public CloudSyncSetupViewModel(
        ICloudSyncService cloudSyncService,
        CloudSyncHostedService cloudSyncHostedService,
        ISettingsRepository settingsRepo,
        IOneDrivePathDetector oneDriveDetector)
    {
        _cloudSyncService = cloudSyncService;
        _cloudSyncHostedService = cloudSyncHostedService;
        _settingsRepo = settingsRepo;
        _oneDriveDetector = oneDriveDetector;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        LoadingMessage = "Loading sync settings...";

        try
        {
            var settings = await _settingsRepo.GetAsync();

            IsSyncEnabled = settings.EnableCloudSync;
            SyncIntervalMinutes = settings.SyncIntervalMinutes;
            IsUnlocked = _cloudSyncHostedService.HasSessionPassphrase;

            // Set sync folder path
            if (!string.IsNullOrEmpty(settings.SyncFolderPath))
            {
                SyncFolderPath = settings.SyncFolderPath;
            }
            else
            {
                SyncFolderPath = _oneDriveDetector.GetDefaultSyncFolderPath() ?? "OneDrive not available";
            }

            // Set last sync text
            if (settings.LastSyncTime.HasValue)
            {
                var timeSince = DateTimeOffset.UtcNow - settings.LastSyncTime.Value;
                if (timeSince.TotalMinutes < 1)
                {
                    LastSyncText = "Last sync: Just now";
                }
                else if (timeSince.TotalHours < 1)
                {
                    LastSyncText = $"Last sync: {(int)timeSince.TotalMinutes} minutes ago";
                }
                else if (timeSince.TotalDays < 1)
                {
                    LastSyncText = $"Last sync: {(int)timeSince.TotalHours} hours ago";
                }
                else
                {
                    LastSyncText = $"Last sync: {settings.LastSyncTime.Value:g}";
                }
            }
            else
            {
                LastSyncText = "Last sync: Never";
            }

            // Set device info
            DeviceInfo = $"Device: {settings.SyncDeviceName ?? Environment.MachineName}";

            await UpdateDerivedPropertiesAsync();
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SetupSyncAsync()
    {
        var passphrase = GetPassphrase?.Invoke();
        var confirmPassphrase = GetConfirmPassphrase?.Invoke();

        if (string.IsNullOrEmpty(passphrase))
        {
            MessageBox.Show(
                "Please enter a passphrase.",
                "Passphrase Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (passphrase.Length < 8)
        {
            MessageBox.Show(
                "Passphrase must be at least 8 characters long.",
                "Passphrase Too Short",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (passphrase != confirmPassphrase)
        {
            PassphraseMismatch = true;
            return;
        }
        PassphraseMismatch = false;

        // Show final warning
        var result = MessageBox.Show(
            "You are about to enable cloud sync.\n\n" +
            "IMPORTANT: If you forget your passphrase, your synced data cannot be recovered.\n\n" +
            "Make sure you remember this passphrase!\n\n" +
            "Do you want to continue?",
            "Enable Cloud Sync",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;
        LoadingMessage = "Setting up cloud sync...";

        try
        {
            await _cloudSyncService.SetupSyncAsync(passphrase);

            // Set the session passphrase for background sync
            _cloudSyncHostedService.SetSessionPassphrase(passphrase);

            IsSyncEnabled = true;
            IsUnlocked = true;
            await UpdateDerivedPropertiesAsync();

            MessageBox.Show(
                "Cloud sync has been enabled successfully!\n\n" +
                "Your data will now sync automatically.",
                "Sync Enabled",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to set up cloud sync:\n\n{ex.Message}",
                "Setup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task UnlockSyncAsync()
    {
        var passphrase = GetPassphrase?.Invoke();

        if (string.IsNullOrEmpty(passphrase))
        {
            MessageBox.Show(
                "Please enter your passphrase.",
                "Passphrase Required",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        LoadingMessage = "Validating passphrase...";

        try
        {
            var isValid = await _cloudSyncService.ValidatePassphraseAsync(passphrase);

            if (!isValid)
            {
                MessageBox.Show(
                    "Invalid passphrase. Please try again.",
                    "Invalid Passphrase",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            // Set the session passphrase for background sync
            _cloudSyncHostedService.SetSessionPassphrase(passphrase);
            IsUnlocked = true;
            await UpdateDerivedPropertiesAsync();

            // Perform initial sync
            LoadingMessage = "Syncing...";
            await _cloudSyncService.SyncAsync(passphrase);

            MessageBox.Show(
                "Sync unlocked successfully!",
                "Unlocked",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to unlock sync:\n\n{ex.Message}",
                "Unlock Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SyncNowAsync()
    {
        if (!IsUnlocked)
        {
            MessageBox.Show(
                "Please unlock sync first by entering your passphrase.",
                "Sync Locked",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;
        LoadingMessage = "Syncing...";

        try
        {
            await _cloudSyncHostedService.TriggerSyncAsync();

            // Update settings with new interval if changed
            var settings = await _settingsRepo.GetAsync();
            if (settings.SyncIntervalMinutes != SyncIntervalMinutes)
            {
                settings.SyncIntervalMinutes = SyncIntervalMinutes;
                await _settingsRepo.UpdateAsync(settings);
            }

            MessageBox.Show(
                "Sync completed successfully!",
                "Sync Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Sync failed:\n\n{ex.Message}",
                "Sync Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DisableSyncAsync()
    {
        var result = MessageBox.Show(
            "Are you sure you want to disable cloud sync?\n\n" +
            "Your local data will be preserved, but sync will stop.",
            "Disable Sync",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        var deleteFile = MessageBox.Show(
            "Do you also want to delete the encrypted sync file from OneDrive?\n\n" +
            "Choose 'Yes' to remove the file completely.\n" +
            "Choose 'No' to keep the file (you can re-enable sync later).",
            "Delete Sync File?",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        IsLoading = true;
        LoadingMessage = "Disabling sync...";

        try
        {
            await _cloudSyncService.DisableSyncAsync(deleteFile == MessageBoxResult.Yes);
            _cloudSyncHostedService.ClearSessionPassphrase();

            IsSyncEnabled = false;
            IsUnlocked = false;
            await UpdateDerivedPropertiesAsync();

            MessageBox.Show(
                "Cloud sync has been disabled.",
                "Sync Disabled",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to disable sync:\n\n{ex.Message}",
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
    private void Close()
    {
        RequestClose?.Invoke();
    }

    private async Task UpdateDerivedPropertiesAsync()
    {
        // Update ShowConfirmPassphrase asynchronously to avoid blocking
        var syncFileExists = await _cloudSyncService.SyncFileExistsAsync().ConfigureAwait(false);
        ShowConfirmPassphrase = !IsSyncEnabled || !syncFileExists;

        OnPropertyChanged(nameof(IsSyncNotEnabled));
        OnPropertyChanged(nameof(NeedsUnlock));
        OnPropertyChanged(nameof(IsOneDriveUnavailable));
    }
}
