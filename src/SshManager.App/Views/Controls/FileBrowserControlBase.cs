using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SshManager.App.Behaviors;
using SshManager.App.ViewModels;
using SshManager.App.Views.Dialogs;

namespace SshManager.App.Views.Controls;

/// <summary>
/// Abstract base class for file browser controls.
/// Provides common drag/drop, keyboard navigation, and context menu handling.
/// </summary>
public abstract class FileBrowserControlBase : UserControl
{
    private Point _dragStartPoint;
    private bool _isDragging;
    private FileDragAdorner? _dragAdorner;
    private AdornerLayer? _adornerLayer;
    private UIElement? _adornedElement;

    // P/Invoke to get cursor position during drag
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    /// <summary>
    /// Event raised when files are dragged from this control.
    /// </summary>
    public event EventHandler<FilesDraggedEventArgs>? FilesDragged;

    /// <summary>
    /// Event raised when delete key is pressed on selected items.
    /// </summary>
    public event EventHandler? DeleteRequested;

    /// <summary>
    /// Event raised when edit is requested for a file.
    /// </summary>
    public event EventHandler<FileEditRequestedEventArgs>? EditRequested;

    /// <summary>
    /// Event raised when upload is requested for selected files.
    /// </summary>
    public event EventHandler<FilesTransferRequestedEventArgs>? UploadRequested;

    /// <summary>
    /// Event raised when download is requested for selected files.
    /// </summary>
    public event EventHandler<FilesTransferRequestedEventArgs>? DownloadRequested;

    /// <summary>
    /// Gets the data key used for drag operations (e.g., "LocalFilePaths" or "RemoteFilePaths").
    /// </summary>
    protected abstract string DragDataKey { get; }

    /// <summary>
    /// Gets the source type identifier (e.g., "Local" or "Remote").
    /// </summary>
    protected abstract string SourceType { get; }

    /// <summary>
    /// Gets the source type from which this control accepts drops.
    /// </summary>
    protected abstract string AcceptDropFromSourceType { get; }

    /// <summary>
    /// Gets the highlight color for drag enter visual feedback.
    /// </summary>
    protected abstract Color DragEnterHighlightColor { get; }

    /// <summary>
    /// Gets the ListView control used for file listing.
    /// </summary>
    protected abstract ListView FileListView { get; }

    /// <summary>
    /// Gets the ViewModel as IFileBrowserViewModel.
    /// </summary>
    protected abstract IFileBrowserViewModel? GetViewModel();

    /// <summary>
    /// Handles the file drop event. Override in derived classes for specific drop handling.
    /// </summary>
    protected abstract void HandleFileDrop(DragEventArgs e);

    /// <summary>
    /// Handles mouse double-click on file list items.
    /// </summary>
    protected void FileListView_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (FileListView.SelectedItem is FileItemViewModel item && GetViewModel() is { } vm)
        {
            vm.OpenItemCommand.Execute(item);
        }
    }

    /// <summary>
    /// Handles keyboard navigation in the file list.
    /// </summary>
    protected void FileListView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (GetViewModel() is not { } vm) return;

        switch (e.Key)
        {
            case Key.Enter:
                if (FileListView.SelectedItem is FileItemViewModel item)
                {
                    vm.OpenItemCommand.Execute(item);
                    e.Handled = true;
                }
                break;

            case Key.Back:
                if (vm.CanGoUp)
                {
                    vm.GoUpCommand.Execute(null);
                    e.Handled = true;
                }
                break;

            case Key.Delete:
                DeleteRequested?.Invoke(this, EventArgs.Empty);
                e.Handled = true;
                break;

            case Key.F5:
                vm.RefreshCommand.Execute(null);
                e.Handled = true;
                break;

            case Key.F2:
                HandleRenameRequest();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Records the drag start point.
    /// </summary>
    protected void FileListView_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStartPoint = e.GetPosition(null);
        _isDragging = false;
    }

    /// <summary>
    /// Initiates drag operation when mouse moves with button pressed.
    /// </summary>
    protected void FileListView_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _isDragging)
            return;

        var position = e.GetPosition(null);
        var diff = _dragStartPoint - position;

        if (Math.Abs(diff.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(diff.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        // Get selected file items (not directories)
        var selectedItems = FileListView.SelectedItems
            .Cast<FileItemViewModel>()
            .Where(i => !i.IsParentDirectory && !i.IsDirectory)
            .ToList();

        if (selectedItems.Count == 0)
            return;

        _isDragging = true;

        var filePaths = selectedItems.Select(i => i.FullPath).ToArray();
        var data = new DataObject();
        data.SetData(DragDataKey, filePaths);
        data.SetData("SourceType", SourceType);
        data.SetData("FileCount", selectedItems.Count);

        FilesDragged?.Invoke(this, new FilesDraggedEventArgs(filePaths));

        // Show drag adorner
        ShowDragAdorner(filePaths);

        try
        {
            DragDrop.DoDragDrop(FileListView, data, DragDropEffects.Copy);
        }
        finally
        {
            HideDragAdorner();
            _isDragging = false;
        }
    }

    /// <summary>
    /// Shows the file drag adorner.
    /// </summary>
    private void ShowDragAdorner(string[] filePaths)
    {
        // Get adorner layer from the main window for proper positioning
        var window = Window.GetWindow(this);
        if (window == null) return;

        _adornedElement = window.Content as UIElement ?? this;
        _adornerLayer = AdornerLayer.GetAdornerLayer(_adornedElement);
        if (_adornerLayer == null) return;

        var direction = SourceType == "Local"
            ? Behaviors.TransferDirection.Upload
            : Behaviors.TransferDirection.Download;

        _dragAdorner = new FileDragAdorner(_adornedElement, filePaths, direction);
        _adornerLayer.Add(_dragAdorner);

        // Set initial position
        UpdateAdornerPosition();
    }

    /// <summary>
    /// Hides the file drag adorner.
    /// </summary>
    private void HideDragAdorner()
    {
        if (_adornerLayer != null && _dragAdorner != null)
        {
            _adornerLayer.Remove(_dragAdorner);
            _dragAdorner = null;
            _adornerLayer = null;
            _adornedElement = null;
        }
    }

    /// <summary>
    /// Updates the adorner position based on current cursor location.
    /// </summary>
    private void UpdateAdornerPosition()
    {
        if (_dragAdorner == null || _adornedElement == null) return;

        if (GetCursorPos(out var screenPoint))
        {
            var relativePoint = _adornedElement.PointFromScreen(new Point(screenPoint.X, screenPoint.Y));
            _dragAdorner.UpdatePosition(relativePoint);
        }
    }

    /// <summary>
    /// Handles GiveFeedback to update adorner position during drag.
    /// </summary>
    protected void FileListView_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        UpdateAdornerPosition();
        e.UseDefaultCursors = true;
        e.Handled = true;
    }

    /// <summary>
    /// Handles drag over to determine if drop is allowed.
    /// </summary>
    protected virtual void FileListView_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetData("SourceType") is string sourceType && sourceType == AcceptDropFromSourceType)
        {
            e.Effects = DragDropEffects.Copy;
        }
        else if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
        {
            e.Effects = DragDropEffects.None;
        }
        e.Handled = true;
    }

    /// <summary>
    /// Handles the drop event.
    /// </summary>
    protected void FileListView_Drop(object sender, DragEventArgs e)
    {
        // Reset visual feedback
        FileListView.BorderBrush = null;
        FileListView.BorderThickness = new Thickness(0);

        HandleFileDrop(e);
        e.Handled = true;
    }

    /// <summary>
    /// Syncs ListView selection to ViewModel.
    /// </summary>
    protected void FileListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GetViewModel() is not { } vm) return;

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

    /// <summary>
    /// Provides visual feedback when drag enters the control.
    /// </summary>
    protected virtual void FileListView_DragEnter(object sender, DragEventArgs e)
    {
        var isValidDrop = ShouldHighlightForDrag(e);
        var hasSourceType = e.Data.GetData("SourceType") is string;

        if (isValidDrop)
        {
            // Valid drop zone - show accent color
            FileListView.BorderBrush = new SolidColorBrush(DragEnterHighlightColor);
            FileListView.BorderThickness = new Thickness(2);
        }
        else if (hasSourceType)
        {
            // Invalid drop zone (e.g., same source type) - show red border
            FileListView.BorderBrush = new SolidColorBrush(Color.FromRgb(231, 76, 60));
            FileListView.BorderThickness = new Thickness(2);
        }
    }

    /// <summary>
    /// Determines if the control should highlight for the given drag event.
    /// </summary>
    protected virtual bool ShouldHighlightForDrag(DragEventArgs e)
    {
        return e.Data.GetData("SourceType") is string sourceType &&
               sourceType == AcceptDropFromSourceType;
    }

    /// <summary>
    /// Removes visual feedback when drag leaves the control.
    /// </summary>
    protected void FileListView_DragLeave(object sender, DragEventArgs e)
    {
        FileListView.BorderBrush = null;
        FileListView.BorderThickness = new Thickness(0);
    }

    /// <summary>
    /// Opens the selected item.
    /// </summary>
    protected void ContextMenu_Open_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItemViewModel item && GetViewModel() is { } vm)
        {
            vm.OpenItemCommand.Execute(item);
        }
    }

    /// <summary>
    /// Requests edit for the selected file.
    /// </summary>
    protected void ContextMenu_Edit_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItemViewModel item && item.IsEditable)
        {
            EditRequested?.Invoke(this, new FileEditRequestedEventArgs(item));
        }
    }

    /// <summary>
    /// Copies the path of the selected item to clipboard.
    /// </summary>
    protected void ContextMenu_CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (FileListView.SelectedItem is FileItemViewModel item && GetViewModel() is { } vm)
        {
            vm.CopyPathCommand.Execute(item);
        }
    }

    /// <summary>
    /// Copies the current directory path to clipboard.
    /// </summary>
    protected void CopyPath_Click(object sender, RoutedEventArgs e)
    {
        if (GetViewModel() is { CurrentPath: { } path } && !string.IsNullOrEmpty(path))
        {
            try
            {
                Clipboard.SetText(path);
            }
            catch
            {
                // Clipboard access can fail in some scenarios
            }
        }
    }

    /// <summary>
    /// Handles the rename request for the selected item.
    /// </summary>
    protected async void HandleRenameRequest()
    {
        if (FileListView.SelectedItem is not FileItemViewModel item || item.IsParentDirectory)
            return;

        if (GetViewModel() is not { } vm)
            return;

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

    /// <summary>
    /// Initiates rename for the selected item.
    /// </summary>
    protected void ContextMenu_Rename_Click(object sender, RoutedEventArgs e)
    {
        HandleRenameRequest();
    }

    /// <summary>
    /// Requests delete for the selected items.
    /// </summary>
    protected void ContextMenu_Delete_Click(object sender, RoutedEventArgs e)
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Requests upload for the selected files.
    /// </summary>
    protected void ContextMenu_Upload_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = FileListView.SelectedItems
            .Cast<FileItemViewModel>()
            .Where(i => !i.IsParentDirectory && !i.IsDirectory)
            .ToList();

        if (selectedItems.Count > 0)
        {
            var filePaths = selectedItems.Select(i => i.FullPath).ToArray();
            UploadRequested?.Invoke(this, new FilesTransferRequestedEventArgs(filePaths));
        }
    }

    /// <summary>
    /// Requests download for the selected files.
    /// </summary>
    protected void ContextMenu_Download_Click(object sender, RoutedEventArgs e)
    {
        var selectedItems = FileListView.SelectedItems
            .Cast<FileItemViewModel>()
            .Where(i => !i.IsParentDirectory && !i.IsDirectory)
            .ToList();

        if (selectedItems.Count > 0)
        {
            var filePaths = selectedItems.Select(i => i.FullPath).ToArray();
            DownloadRequested?.Invoke(this, new FilesTransferRequestedEventArgs(filePaths));
        }
    }

    /// <summary>
    /// Raises the FilesDragged event.
    /// </summary>
    protected void OnFilesDragged(FilesDraggedEventArgs e)
    {
        FilesDragged?.Invoke(this, e);
    }

    /// <summary>
    /// Raises the DeleteRequested event.
    /// </summary>
    protected void OnDeleteRequested()
    {
        DeleteRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Raises the EditRequested event.
    /// </summary>
    protected void OnEditRequested(FileEditRequestedEventArgs e)
    {
        EditRequested?.Invoke(this, e);
    }
}

/// <summary>
/// Event args for when a file edit is requested.
/// </summary>
public class FileEditRequestedEventArgs : EventArgs
{
    public FileItemViewModel Item { get; }

    public FileEditRequestedEventArgs(FileItemViewModel item)
    {
        Item = item;
    }
}

/// <summary>
/// Event args for when files are dropped.
/// </summary>
public class FilesDroppedEventArgs : EventArgs
{
    public IReadOnlyList<string> FilePaths { get; }

    public FilesDroppedEventArgs(IReadOnlyList<string> filePaths)
    {
        FilePaths = filePaths;
    }
}

/// <summary>
/// Event args for when files are dragged.
/// </summary>
public class FilesDraggedEventArgs : EventArgs
{
    public IReadOnlyList<string> FilePaths { get; }

    public FilesDraggedEventArgs(IReadOnlyList<string> filePaths)
    {
        FilePaths = filePaths;
    }
}

/// <summary>
/// Event args for when a file transfer (upload/download) is requested.
/// </summary>
public class FilesTransferRequestedEventArgs : EventArgs
{
    public IReadOnlyList<string> FilePaths { get; }

    public FilesTransferRequestedEventArgs(IReadOnlyList<string> filePaths)
    {
        FilePaths = filePaths;
    }
}
