using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SshManager.App.ViewModels;
using SshManager.App.Views.Dialogs;

namespace SshManager.App.Views.Controls;

/// <summary>
/// Interaction logic for RemoteFileBrowserControl.xaml
/// </summary>
public partial class RemoteFileBrowserControl : UserControl
{
    private Point _dragStartPoint;
    private bool _isDragging;

    /// <summary>
    /// Event raised when files should be downloaded from remote.
    /// </summary>
    public event EventHandler<FilesDroppedEventArgs>? FilesDroppedFromLocal;

    /// <summary>
    /// Event raised when remote files are dragged for download.
    /// </summary>
    public event EventHandler<FilesDraggedEventArgs>? FilesDragged;

    public RemoteFileBrowserControl()
    {
        InitializeComponent();
    }

    private void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is FileItemViewModel item && DataContext is RemoteFileBrowserViewModel vm)
        {
            vm.OpenItemCommand.Execute(item);
        }
    }

    private void FileListView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not RemoteFileBrowserViewModel vm) return;

        switch (e.Key)
        {
            case Key.Enter:
                // Enter to open folder
                if (FileListView.SelectedItem is FileItemViewModel item)
                {
                    vm.OpenItemCommand.Execute(item);
                    e.Handled = true;
                }
                break;

            case Key.Back:
                // Backspace to go up one directory
                if (vm.CanGoUp)
                {
                    vm.GoUpCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            case Key.Delete:
                // Delete to delete selected item(s)
                DeleteRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.F5:
                // F5 to refresh
                vm.RefreshCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F2:
                // F2 to rename
                ContextMenu_Rename_Click(sender, e);
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Event raised when delete key is pressed on selected items.
    /// </summary>
    public event EventHandler? DeleteRequested;

    private void FileListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }

    private void FileListView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging)
            return;

        var position = e.GetPosition(null);
        var diff = _dragStartPoint - position;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // Get selected items
        var selectedItems = FileListView.SelectedItems
            .Cast<FileItemViewModel>()
            .Where(i => !i.IsParentDirectory && !i.IsDirectory)
            .ToList();

        if (selectedItems.Count == 0)
            return;

        _isDragging = true;

        var filePaths = selectedItems.Select(i => i.FullPath).ToArray();
        var data = new DataObject();
        data.SetData("RemoteFilePaths", filePaths);
        data.SetData("SourceType", "Remote");

        // Notify that files are being dragged
        FilesDragged?.Invoke(this, new FilesDraggedEventArgs(filePaths));

        DragDrop.DoDragDrop(FileListView, data, DragDropEffects.Copy);
        _isDragging = false;
    }

    private void FileListView_DragOver(object sender, DragEventArgs e)
    {
        // Only accept drops from local
        if (e.Data.GetData("SourceType") is string sourceType && sourceType == "Local")
        {
            e.Effects = DragDropEffects.Copy;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            // Accept drops from Windows Explorer
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void FileListView_Drop(object sender, DragEventArgs e)
    {
        // Reset visual feedback
        FileListView.BorderBrush = null;
        FileListView.BorderThickness = new Thickness(0);

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
        e.Handled = true;
    }

    private void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not RemoteFileBrowserViewModel vm) return;

        // Sync selection changes to the ViewModel
        foreach (var item in e.RemovedItems.OfType<FileItemViewModel>())
        {
            vm.SelectedItems.Remove(item);
        }

        foreach (var item in e.AddedItems.OfType<FileItemViewModel>())
        {
            if (!vm.SelectedItems.Contains(item))
            {
                vm.SelectedItems.Add(item);
            }
        }
    }

    private void FileListView_DragEnter(object sender, DragEventArgs e)
    {
        // Visual feedback when drag enters - highlight drop zone for uploads
        if ((e.Data.GetData("SourceType") is string sourceType && sourceType == "Local") ||
            e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            FileListView.BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(46, 204, 113)); // Green highlight for uploads
            FileListView.BorderThickness = new Thickness(2);
        }
    }

    private void FileListView_DragLeave(object sender, DragEventArgs e)
    {
        // Remove visual feedback when drag leaves
        FileListView.BorderBrush = null;
        FileListView.BorderThickness = new Thickness(0);
    }

    #region Context Menu Handlers

    private void ContextMenu_Open_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItemViewModel item && DataContext is RemoteFileBrowserViewModel vm)
        {
            vm.OpenItemCommand.Execute(item);
        }
    }

    private void ContextMenu_Edit_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItemViewModel item && item.IsEditable)
        {
            EditRequested?.Invoke(this, new FileEditRequestedEventArgs(item));
        }
    }

    private void ContextMenu_CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItemViewModel item && DataContext is RemoteFileBrowserViewModel vm)
        {
            vm.CopyPathCommand.Execute(item);
        }
    }

    private async void ContextMenu_Rename_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is not FileItemViewModel item || item.IsParentDirectory)
            return;

        if (DataContext is not RemoteFileBrowserViewModel vm)
            return;

        // Get existing sibling names for validation
        var siblingNames = vm.Items
            .Where(i => !i.IsParentDirectory && i != item)
            .Select(i => i.Name)
            .ToList();

        var renameVm = new RenameDialogViewModel(item.Name, item.IsDirectory, siblingNames);
        var dialog = new RenameDialog(renameVm)
        {
            Owner = Window.GetWindow(this)
        };

        if (dialog.ShowDialog() == true)
        {
            var newName = dialog.GetNewName();
            if (!string.Equals(newName, item.Name, StringComparison.Ordinal))
            {
                await vm.RenameAsync(item, newName);
            }
        }
    }

    private void ContextMenu_Delete_Click(object sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Event raised when edit is requested for a file.
    /// </summary>
    public event EventHandler<FileEditRequestedEventArgs>? EditRequested;

    #endregion
}
