using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SshManager.App.ViewModels;

namespace SshManager.App.Views.Controls;

/// <summary>
/// Interaction logic for LocalFileBrowserControl.xaml
/// </summary>
public partial class LocalFileBrowserControl : FileBrowserControlBase
{
    /// <summary>
    /// Event raised when files are dropped from the remote browser (for download).
    /// </summary>
    public event EventHandler<FilesDroppedEventArgs>? FilesDroppedFromRemote;

    /// <inheritdoc />
    protected override string DragDataKey => "LocalFilePaths";

    /// <inheritdoc />
    protected override string SourceType => "Local";

    /// <inheritdoc />
    protected override string AcceptDropFromSourceType => "Remote";

    /// <inheritdoc />
    protected override Color DragEnterHighlightColor => Color.FromRgb(52, 152, 219); // Blue

    /// <inheritdoc />
    protected override ListView FileListView => FileListViewControl;

    /// <inheritdoc />
    protected override IFileBrowserViewModel? GetViewModel()
        => DataContext as LocalFileBrowserViewModel;

    public LocalFileBrowserControl()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override void HandleFileDrop(DragEventArgs e)
    {
        // Handle drop from remote (download)
        if (e.Data.GetData("RemoteFilePaths") is string[] remotePaths &&
            e.Data.GetData("SourceType") is string sourceType &&
            sourceType == "Remote")
        {
            FilesDroppedFromRemote?.Invoke(this, new FilesDroppedEventArgs(remotePaths));
        }
    }
}
