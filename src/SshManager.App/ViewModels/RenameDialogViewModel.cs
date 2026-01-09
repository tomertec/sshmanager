using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SshManager.App.ViewModels;

public partial class RenameDialogViewModel : ObservableObject
{
    private readonly string _originalName;
    private readonly bool _isDirectory;
    private readonly HashSet<string> _existingSiblingNames;

    [ObservableProperty]
    private string _newName = "";

    [ObservableProperty]
    private string _validationError = "";

    public string DialogTitle => _isDirectory ? "Rename Folder" : "Rename File";

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    public RenameDialogViewModel(string originalName, bool isDirectory, IEnumerable<string>? existingSiblingNames = null)
    {
        _originalName = originalName;
        _isDirectory = isDirectory;
        _existingSiblingNames = existingSiblingNames != null
            ? new HashSet<string>(existingSiblingNames, StringComparer.OrdinalIgnoreCase)
            : [];

        NewName = originalName;
    }

    partial void OnNewNameChanged(string value)
    {
        Validate();
    }

    private bool Validate()
    {
        ValidationError = "";

        if (string.IsNullOrWhiteSpace(NewName))
        {
            ValidationError = "Name cannot be empty";
            return false;
        }

        // Check for invalid characters (Windows-specific + common Unix invalid chars)
        var invalidChars = Path.GetInvalidFileNameChars();
        if (NewName.IndexOfAny(invalidChars) >= 0)
        {
            ValidationError = "Name contains invalid characters";
            return false;
        }

        // Check for reserved names
        if (NewName == "." || NewName == "..")
        {
            ValidationError = "Name is reserved";
            return false;
        }

        // Check if name already exists (skip if same as original)
        if (!string.Equals(NewName, _originalName, StringComparison.OrdinalIgnoreCase)
            && _existingSiblingNames.Contains(NewName))
        {
            ValidationError = "An item with this name already exists";
            return false;
        }

        return true;
    }

    [RelayCommand]
    private void Save()
    {
        if (!Validate())
        {
            return;
        }

        DialogResult = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    public string GetNewName() => NewName.Trim();
}
