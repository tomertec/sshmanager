using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.App.ViewModels;

namespace SshManager.App.Views.Controls;

/// <summary>
/// Interaction logic for SftpBrowserControl.xaml
/// </summary>
public partial class SftpBrowserControl : UserControl
{
    private readonly ILogger<SftpBrowserControl> _logger;

    public SftpBrowserControl() : this(null)
    {
    }

    public SftpBrowserControl(ILogger<SftpBrowserControl>? logger = null)
    {
        _logger = logger ?? NullLogger<SftpBrowserControl>.Instance;
        InitializeComponent();
    }

    private void LocalBrowser_FilesDroppedFromRemote(object? sender, FilesDroppedEventArgs e)
    {
        // Download files from remote to local
        if (DataContext is SftpBrowserViewModel vm)
        {
            vm.DownloadFiles(e.FilePaths);
        }
    }

    private void RemoteBrowser_FilesDroppedFromLocal(object? sender, FilesDroppedEventArgs e)
    {
        // Upload files from local to remote
        if (DataContext is SftpBrowserViewModel vm)
        {
            vm.UploadFiles(e.FilePaths);
        }
    }

    private void DismissError_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is SftpBrowserViewModel vm)
        {
            vm.ErrorMessage = null;
        }
    }

    private void LocalBrowser_DeleteRequested(object? sender, EventArgs e)
    {
        // Delete selected local items
        if (DataContext is SftpBrowserViewModel vm)
        {
            vm.DeleteLocalCommand.Execute(null);
        }
    }

    private void RemoteBrowser_DeleteRequested(object? sender, EventArgs e)
    {
        // Delete selected remote items
        if (DataContext is SftpBrowserViewModel vm)
        {
            vm.DeleteRemoteCommand.Execute(null);
        }
    }

    private async void LocalBrowser_EditRequested(object? sender, FileEditRequestedEventArgs e)
    {
        try
        {
            // Open local file in text editor
            if (DataContext is SftpBrowserViewModel vm)
            {
                var ownerWindow = Window.GetWindow(this);
                if (ownerWindow != null)
                {
                    await vm.EditLocalFileAsync(e.Item, ownerWindow);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SftpBrowserControl.LocalBrowser_EditRequested");
        }
    }

    private async void RemoteBrowser_EditRequested(object? sender, FileEditRequestedEventArgs e)
    {
        try
        {
            // Open remote file in text editor
            if (DataContext is SftpBrowserViewModel vm)
            {
                var ownerWindow = Window.GetWindow(this);
                if (ownerWindow != null)
                {
                    await vm.EditRemoteFileAsync(e.Item, ownerWindow);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in SftpBrowserControl.RemoteBrowser_EditRequested");
        }
    }

    private void LocalBrowser_UploadRequested(object? sender, FilesTransferRequestedEventArgs e)
    {
        // Upload selected local files to remote
        if (DataContext is SftpBrowserViewModel vm)
        {
            vm.UploadFiles(e.FilePaths);
        }
    }

    private void RemoteBrowser_DownloadRequested(object? sender, FilesTransferRequestedEventArgs e)
    {
        // Download selected remote files to local
        if (DataContext is SftpBrowserViewModel vm)
        {
            vm.DownloadFiles(e.FilePaths);
        }
    }
}
