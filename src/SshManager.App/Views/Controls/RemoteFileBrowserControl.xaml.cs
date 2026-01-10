using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using SshManager.App.ViewModels;
using SshManager.App.Views.Dialogs;

namespace SshManager.App.Views.Controls;

/// <summary>
/// Interaction logic for RemoteFileBrowserControl.xaml
/// </summary>
public partial class RemoteFileBrowserControl : FileBrowserControlBase
{
    /// <summary>
    /// Event raised when files are dropped from the local browser or Windows Explorer (for upload).
    /// </summary>
    public event EventHandler<FilesDroppedEventArgs>? FilesDroppedFromLocal;

    /// <inheritdoc />
    protected override string DragDataKey => "RemoteFilePaths";

    /// <inheritdoc />
    protected override string SourceType => "Remote";

    /// <inheritdoc />
    protected override string AcceptDropFromSourceType => "Local";

    /// <inheritdoc />
    protected override Color DragEnterHighlightColor => Color.FromRgb(46, 204, 113); // Green

    /// <inheritdoc />
    protected override ListView FileListView => FileListViewControl;

    /// <inheritdoc />
    protected override IFileBrowserViewModel? GetViewModel()
        => DataContext as RemoteFileBrowserViewModel;

    public RemoteFileBrowserControl()
    {
        InitializeComponent();
    }

    /// <inheritdoc />
    protected override bool ShouldHighlightForDrag(DragEventArgs e)
    {
        // Remote also accepts Windows Explorer drops
        return base.ShouldHighlightForDrag(e) || e.Data.GetDataPresent(DataFormats.FileDrop);
    }

    /// <inheritdoc />
    protected override void HandleFileDrop(DragEventArgs e)
    {
        // Handle drop from local browser (upload)
        if (e.Data.GetData("LocalFilePaths") is string[] localPaths &&
            e.Data.GetData("SourceType") is string sourceType &&
            sourceType == "Local")
        {
            FilesDroppedFromLocal?.Invoke(this, new FilesDroppedEventArgs(localPaths));
        }
        // Handle drop from Windows Explorer
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                // Filter to only files (not directories)
                var fileOnly = files.Where(f => System.IO.File.Exists(f)).ToArray();
                if (fileOnly.Length > 0)
                {
                    FilesDroppedFromLocal?.Invoke(this, new FilesDroppedEventArgs(fileOnly));
                }
            }
        }
    }

    /// <summary>
    /// Opens the file properties dialog (remote-only feature).
    /// </summary>
    private void ContextMenu_Properties_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not FileItemViewModel item || item.IsParentDirectory)
            return;

        if (DataContext is not RemoteFileBrowserViewModel vm)
            return;

        var session = vm.GetSession();
        var propertiesVm = new FilePropertiesDialogViewModel(item, session);
        var dialog = new FilePropertiesDialog(propertiesVm)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            // Permissions may have changed, refresh the display
            // The item's Permissions property is already updated by the dialog
        }
    }
}
