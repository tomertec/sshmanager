using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the SSH Config Export dialog.
/// Handles configuration options, preview generation, and file export.
/// </summary>
public partial class SshConfigExportDialogViewModel : ObservableObject
{
    private readonly ISshConfigExportService _exportService;
    private readonly IHostRepository _hostRepository;
    private List<HostEntry> _hosts = [];

    [ObservableProperty]
    private string _filePath = string.Empty;

    [ObservableProperty]
    private bool _includeComments = true;

    [ObservableProperty]
    private bool _includeGroups = true;

    [ObservableProperty]
    private bool _includePortForwarding = true;

    [ObservableProperty]
    private bool _useProxyJump = true;

    [ObservableProperty]
    private string _preview = string.Empty;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int _hostCount;

    [ObservableProperty]
    private bool? _dialogResult;

    /// <summary>
    /// Event raised when the dialog should be closed.
    /// </summary>
    public event Action? RequestClose;

    public SshConfigExportDialogViewModel(
        ISshConfigExportService exportService,
        IHostRepository hostRepository)
    {
        _exportService = exportService ?? throw new ArgumentNullException(nameof(exportService));
        _hostRepository = hostRepository ?? throw new ArgumentNullException(nameof(hostRepository));

        // Set default file path to user's .ssh/config
        FilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".ssh",
            "config");
    }

    /// <summary>
    /// Initializes the ViewModel by loading hosts and generating preview.
    /// Should be called after the dialog is shown.
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            _hosts = await _hostRepository.GetAllAsync();
            HostCount = _hosts.Count;
            await RefreshPreviewAsync();
        }
        catch (Exception ex)
        {
            Preview = $"Error loading hosts: {ex.Message}";
        }
    }

    /// <summary>
    /// Opens a SaveFileDialog to select the export file path.
    /// </summary>
    [RelayCommand]
    private void Browse()
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export SSH Config",
            Filter = "SSH Config|config|All Files|*.*",
            FileName = Path.GetFileName(FilePath),
            InitialDirectory = Path.GetDirectoryName(FilePath),
            DefaultExt = "",
            AddExtension = false
        };

        if (dialog.ShowDialog() == true)
        {
            FilePath = dialog.FileName;
        }
    }

    /// <summary>
    /// Refreshes the preview by generating a sample of the SSH config output.
    /// </summary>
    [RelayCommand]
    private Task RefreshPreviewAsync()
    {
        try
        {
            if (_hosts.Count == 0)
            {
                Preview = "No hosts available to export.";
                return Task.CompletedTask;
            }

            var options = new SshConfigExportOptions
            {
                IncludeComments = IncludeComments,
                IncludeGroups = IncludeGroups,
                IncludePortForwarding = IncludePortForwarding,
                UseProxyJump = UseProxyJump
            };

            // Generate the full config content
            var fullConfig = _exportService.GenerateConfig(_hosts, options);

            // Take first 50 lines for preview
            var lines = fullConfig.Split('\n');
            var previewLines = lines.Take(50).ToList();

            if (lines.Length > 50)
            {
                previewLines.Add("");
                previewLines.Add($"... ({lines.Length - 50} more lines)");
            }

            Preview = string.Join('\n', previewLines);
        }
        catch (Exception ex)
        {
            Preview = $"Error generating preview: {ex.Message}";
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Exports the SSH config to the specified file path.
    /// </summary>
    [RelayCommand]
    private async Task ExportAsync()
    {
        if (string.IsNullOrWhiteSpace(FilePath))
        {
            MessageBox.Show(
                "Please specify a file path.",
                "Export SSH Config",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        IsLoading = true;

        try
        {
            var options = new SshConfigExportOptions
            {
                IncludeComments = IncludeComments,
                IncludeGroups = IncludeGroups,
                IncludePortForwarding = IncludePortForwarding,
                UseProxyJump = UseProxyJump
            };

            await _exportService.ExportToFileAsync(FilePath, _hosts, options);

            MessageBox.Show(
                $"SSH config exported successfully to:\n{FilePath}\n\nExported {_hosts.Count} host(s).",
                "Export Successful",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            // Close the dialog with success
            DialogResult = true;
            RequestClose?.Invoke();
        }
        catch (UnauthorizedAccessException ex)
        {
            MessageBox.Show(
                $"Access denied to file:\n{FilePath}\n\nError: {ex.Message}",
                "Export Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (IOException ex)
        {
            MessageBox.Show(
                $"Failed to write to file:\n{FilePath}\n\nError: {ex.Message}",
                "Export Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"An error occurred during export:\n{ex.Message}",
                "Export Failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Cancels the dialog and closes it.
    /// </summary>
    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Triggered when IncludeComments property changes.
    /// Refreshes the preview to reflect the change.
    /// </summary>
    partial void OnIncludeCommentsChanged(bool value)
    {
        _ = RefreshPreviewAsync().ContinueWith(t =>
            System.Diagnostics.Debug.WriteLine($"Preview refresh error: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Triggered when IncludeGroups property changes.
    /// Refreshes the preview to reflect the change.
    /// </summary>
    partial void OnIncludeGroupsChanged(bool value)
    {
        _ = RefreshPreviewAsync().ContinueWith(t =>
            System.Diagnostics.Debug.WriteLine($"Preview refresh error: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Triggered when IncludePortForwarding property changes.
    /// Refreshes the preview to reflect the change.
    /// </summary>
    partial void OnIncludePortForwardingChanged(bool value)
    {
        _ = RefreshPreviewAsync().ContinueWith(t =>
            System.Diagnostics.Debug.WriteLine($"Preview refresh error: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    /// <summary>
    /// Triggered when UseProxyJump property changes.
    /// Refreshes the preview to reflect the change.
    /// </summary>
    partial void OnUseProxyJumpChanged(bool value)
    {
        _ = RefreshPreviewAsync().ContinueWith(t =>
            System.Diagnostics.Debug.WriteLine($"Preview refresh error: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }
}
