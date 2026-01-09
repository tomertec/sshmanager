using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Terminal.Services;
using SshManager.Terminal.Services.Recording;
using Wpf.Ui;
using Wpf.Ui.Controls;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for browsing and managing session recordings.
/// </summary>
public partial class RecordingBrowserViewModel : ObservableObject
{
    private readonly ISessionRecordingRepository _recordingRepository;
    private readonly ISessionRecordingService _recordingService;
    private readonly ISnackbarService _snackbarService;
    private readonly IContentDialogService _dialogService;

    [ObservableProperty]
    private ObservableCollection<SessionRecording> _recordings = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayCommand))]
    [NotifyCanExecuteChangedFor(nameof(ExportCommand))]
    [NotifyCanExecuteChangedFor(nameof(DeleteCommand))]
    private SessionRecording? _selectedRecording;

    [ObservableProperty]
    private string _searchText = string.Empty;

    private ICollectionView? _filteredView;

    public ICollectionView FilteredRecordings
    {
        get
        {
            if (_filteredView == null)
            {
                _filteredView = CollectionViewSource.GetDefaultView(Recordings);
                _filteredView.Filter = FilterRecording;
            }
            return _filteredView;
        }
    }

    public RecordingBrowserViewModel(
        ISessionRecordingRepository recordingRepository,
        ISessionRecordingService recordingService,
        ISnackbarService snackbarService,
        IContentDialogService dialogService)
    {
        _recordingRepository = recordingRepository;
        _recordingService = recordingService;
        _snackbarService = snackbarService;
        _dialogService = dialogService;
    }

    partial void OnSearchTextChanged(string value)
    {
        FilteredRecordings.Refresh();
    }

    /// <summary>
    /// Loads all session recordings from the database.
    /// </summary>
    public async Task LoadRecordingsAsync()
    {
        try
        {
            var recordings = await _recordingRepository.GetAllAsync();
            Recordings.Clear();
            foreach (var recording in recordings)
            {
                Recordings.Add(recording);
            }
        }
        catch (Exception ex)
        {
            _snackbarService.Show(
                "Error",
                $"Failed to load recordings: {ex.Message}",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(3));
        }
    }

    /// <summary>
    /// Refreshes the recordings list.
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        await LoadRecordingsAsync();
        _snackbarService.Show(
            "Refreshed",
            "Recordings list updated",
            ControlAppearance.Success,
            null,
            TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// Opens the selected recording for playback.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteRecordingCommand))]
    private Task PlayAsync()
    {
        if (SelectedRecording == null)
            return Task.CompletedTask;

        try
        {
            // Open playback dialog
            var playbackViewModel = new RecordingPlaybackViewModel(
                _recordingService,
                SelectedRecording);

            var dialog = new Views.Dialogs.RecordingPlaybackDialog(playbackViewModel);
            dialog.ShowDialog();
        }
        catch (Exception ex)
        {
            _snackbarService.Show(
                "Error",
                $"Failed to play recording: {ex.Message}",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(3));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Exports the selected recording to a file.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteRecordingCommand))]
    private Task ExportAsync()
    {
        if (SelectedRecording == null)
            return Task.CompletedTask;

        try
        {
            var saveDialog = new SaveFileDialog
            {
                Filter = "TTYREC Files (*.ttyrec)|*.ttyrec|All Files (*.*)|*.*",
                FileName = $"{SelectedRecording.Title}_{SelectedRecording.StartedAt:yyyyMMdd_HHmmss}.cast",
                Title = "Export Recording"
            };

            if (saveDialog.ShowDialog() == true)
            {
                var sourceFile = Path.Combine(_recordingService.RecordingsDirectory, SelectedRecording.FileName);
                if (File.Exists(sourceFile))
                {
                    File.Copy(sourceFile, saveDialog.FileName, overwrite: true);

                    _snackbarService.Show(
                        "Exported",
                        $"Recording exported to {Path.GetFileName(saveDialog.FileName)}",
                        ControlAppearance.Success,
                        null,
                        TimeSpan.FromSeconds(3));
                }
                else
                {
                    _snackbarService.Show(
                        "Error",
                        "Recording file not found",
                        ControlAppearance.Danger,
                        null,
                        TimeSpan.FromSeconds(3));
                }
            }
        }
        catch (Exception ex)
        {
            _snackbarService.Show(
                "Error",
                $"Failed to export recording: {ex.Message}",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(3));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Deletes the selected recording after confirmation.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanExecuteRecordingCommand))]
    private async Task DeleteAsync()
    {
        if (SelectedRecording == null)
            return;

        try
        {
            // Show confirmation dialog
            var confirmResult = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete the recording '{SelectedRecording.Title}'? This action cannot be undone.",
                "Delete Recording",
                System.Windows.MessageBoxButton.YesNo,
                System.Windows.MessageBoxImage.Question);

            if (confirmResult == System.Windows.MessageBoxResult.Yes)
            {
                await _recordingRepository.DeleteAsync(SelectedRecording.Id);

                // Delete physical file
                var filePath = Path.Combine(_recordingService.RecordingsDirectory, SelectedRecording.FileName);
                if (File.Exists(filePath))
                {
                    File.Delete(filePath);
                }

                Recordings.Remove(SelectedRecording);

                _snackbarService.Show(
                    "Deleted",
                    "Recording deleted successfully",
                    ControlAppearance.Success,
                    null,
                    TimeSpan.FromSeconds(2));
            }
        }
        catch (Exception ex)
        {
            _snackbarService.Show(
                "Error",
                $"Failed to delete recording: {ex.Message}",
                ControlAppearance.Danger,
                null,
                TimeSpan.FromSeconds(3));
        }
    }

    private bool CanExecuteRecordingCommand() => SelectedRecording != null;

    private bool FilterRecording(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
            return true;

        if (obj is not SessionRecording recording)
            return false;

        var searchLower = SearchText.ToLower();
        return recording.Title.ToLower().Contains(searchLower) ||
               (recording.Host?.DisplayName?.ToLower().Contains(searchLower) ?? false);
    }
}
