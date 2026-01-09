using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.App.Services;

namespace SshManager.App.ViewModels;

public partial class BackupRestoreViewModel : ObservableObject
{
    private readonly IBackupService _backupService;

    [ObservableProperty]
    private ObservableCollection<BackupInfo> _backups = [];

    [ObservableProperty]
    private BackupInfo? _selectedBackup;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _backupDirectory = "";

    public bool HasNoBackups => !IsLoading && Backups.Count == 0;

    public event Action? RequestClose;
    public event Action? OnRestoreCompleted;

    public BackupRestoreViewModel(IBackupService backupService)
    {
        _backupService = backupService;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            BackupDirectory = await _backupService.GetBackupDirectoryAsync();
            var backups = await _backupService.GetBackupsAsync();

            Backups.Clear();
            foreach (var backup in backups)
            {
                Backups.Add(backup);
            }
        }
        finally
        {
            IsLoading = false;
            OnPropertyChanged(nameof(HasNoBackups));
        }
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        IsLoading = true;
        try
        {
            var backup = await _backupService.CreateBackupAsync();
            Backups.Insert(0, backup);
            OnPropertyChanged(nameof(HasNoBackups));

            MessageBox.Show(
                $"Backup created successfully!\n\n" +
                $"File: {backup.FileName}\n" +
                $"Hosts: {backup.HostCount}\n" +
                $"Groups: {backup.GroupCount}",
                "Backup Created",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to create backup:\n\n{ex.Message}",
                "Backup Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task RestoreBackupAsync(BackupInfo? backup)
    {
        if (backup == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to restore from this backup?\n\n" +
            $"File: {backup.FileName}\n" +
            $"Created: {backup.CreatedAt:g}\n" +
            $"Hosts: {backup.HostCount}\n" +
            $"Groups: {backup.GroupCount}\n\n" +
            "This will ADD the hosts and groups from the backup to your existing data. " +
            "Duplicate hosts will be created with new IDs.",
            "Confirm Restore",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result != MessageBoxResult.Yes) return;

        IsLoading = true;
        try
        {
            var (hostCount, groupCount) = await _backupService.RestoreBackupAsync(backup.FilePath);

            MessageBox.Show(
                $"Restore completed successfully!\n\n" +
                $"Restored {hostCount} hosts and {groupCount} groups.",
                "Restore Complete",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            OnRestoreCompleted?.Invoke();
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to restore backup:\n\n{ex.Message}",
                "Restore Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task DeleteBackupAsync(BackupInfo? backup)
    {
        if (backup == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete this backup?\n\n{backup.FileName}\n\nThis cannot be undone.",
            "Delete Backup",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _backupService.DeleteBackupAsync(backup.FilePath);
            Backups.Remove(backup);
            OnPropertyChanged(nameof(HasNoBackups));
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Failed to delete backup:\n\n{ex.Message}",
                "Delete Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    [RelayCommand]
    private async Task OpenBackupDirectoryAsync()
    {
        await _backupService.OpenBackupDirectoryAsync().ConfigureAwait(false);
    }

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke();
    }
}
