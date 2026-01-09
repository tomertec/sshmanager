using CommunityToolkit.Mvvm.ComponentModel;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the SFTP browser window.
/// </summary>
public partial class SftpBrowserWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _windowTitle = "SFTP Browser";

    [ObservableProperty]
    private SftpBrowserViewModel _sftpBrowser;

    public SftpBrowserWindowViewModel(SftpBrowserViewModel sftpBrowser, string hostName)
    {
        _sftpBrowser = sftpBrowser;
        WindowTitle = $"SFTP - {hostName}";
    }
}
