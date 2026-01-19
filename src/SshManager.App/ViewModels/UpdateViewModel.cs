using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.App.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for managing application updates via Velopack.
/// </summary>
public partial class UpdateViewModel : ObservableObject
{
    private readonly IUpdateService _updateService;

    [ObservableProperty]
    private bool _isCheckingForUpdate;

    [ObservableProperty]
    private bool _isDownloadingUpdate;

    [ObservableProperty]
    private bool _updateAvailable;

    [ObservableProperty]
    private UpdateInfo? _availableUpdate;

    [ObservableProperty]
    private int _downloadProgress;

    [ObservableProperty]
    private bool _isUpdateReadyToInstall;

    [ObservableProperty]
    private string _currentVersion = "";

    public UpdateViewModel(IUpdateService updateService)
    {
        _updateService = updateService;
        CurrentVersion = _updateService.GetCurrentVersion();
    }

    [RelayCommand]
    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingForUpdate || IsDownloadingUpdate)
            return;

        IsCheckingForUpdate = true;
        UpdateAvailable = false;
        AvailableUpdate = null;

        try
        {
            var update = await _updateService.CheckForUpdateAsync();

            if (update != null)
            {
                UpdateAvailable = true;
                AvailableUpdate = update;
            }
            else
            {
                MessageBox.Show(
                    "You are running the latest version of SshManager.",
                    "No Updates Available",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to check for updates:\n\n{ex.Message}",
                "Update Check Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsCheckingForUpdate = false;
        }
    }

    [RelayCommand]
    private async Task DownloadUpdateAsync()
    {
        if (AvailableUpdate == null || IsDownloadingUpdate)
            return;

        IsDownloadingUpdate = true;
        DownloadProgress = 0;

        try
        {
            var progress = new Progress<int>(p => DownloadProgress = p);

            await _updateService.DownloadUpdateAsync(AvailableUpdate, progress);

            IsUpdateReadyToInstall = true;

            var result = MessageBox.Show(
                "Update downloaded successfully!\n\nWould you like to restart and install the update now?",
                "Update Ready",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                await ApplyUpdateAsync();
            }
        }
        catch (OperationCanceledException)
        {
            MessageBox.Show(
                "Update download was cancelled.",
                "Download Cancelled",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to download update:\n\n{ex.Message}",
                "Download Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsDownloadingUpdate = false;
        }
    }

    [RelayCommand]
    private async Task ApplyUpdateAsync()
    {
        if (!IsUpdateReadyToInstall)
            return;

        var result = MessageBox.Show(
            "The application will now restart to apply the update.\n\nAny unsaved work will be lost. Continue?",
            "Confirm Update",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes)
            return;

        try
        {
            await _updateService.ApplyUpdateAndRestartAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to apply update:\n\n{ex.Message}",
                "Update Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private void DismissUpdate()
    {
        UpdateAvailable = false;
        AvailableUpdate = null;
        IsUpdateReadyToInstall = false;
        DownloadProgress = 0;
    }
}
