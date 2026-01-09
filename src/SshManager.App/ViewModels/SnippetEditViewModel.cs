using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Core.Models;

namespace SshManager.App.ViewModels;

public partial class SnippetEditViewModel : ObservableObject
{
    private readonly CommandSnippet? _existingSnippet;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _command = "";

    [ObservableProperty]
    private string _description = "";

    [ObservableProperty]
    private string _category = "";

    [ObservableProperty]
    private List<string> _availableCategories = [];

    [ObservableProperty]
    private string _validationError = "";

    public bool IsEditing => _existingSnippet != null;
    public string DialogTitle => IsEditing ? "Edit Snippet" : "Add Snippet";

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    public SnippetEditViewModel(CommandSnippet? snippet, List<string> existingCategories)
    {
        _existingSnippet = snippet;
        AvailableCategories = existingCategories;

        if (snippet != null)
        {
            Name = snippet.Name;
            Command = snippet.Command;
            Description = snippet.Description ?? "";
            Category = snippet.Category ?? "";
        }
    }

    [RelayCommand]
    private void Save()
    {
        // Validate
        ValidationError = "";

        if (string.IsNullOrWhiteSpace(Name))
        {
            ValidationError = "Name is required";
            return;
        }

        if (string.IsNullOrWhiteSpace(Command))
        {
            ValidationError = "Command is required";
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

    public CommandSnippet GetSnippet()
    {
        if (_existingSnippet != null)
        {
            _existingSnippet.Name = Name.Trim();
            _existingSnippet.Command = Command;
            _existingSnippet.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
            _existingSnippet.Category = string.IsNullOrWhiteSpace(Category) ? null : Category.Trim();
            _existingSnippet.UpdatedAt = DateTimeOffset.UtcNow;
            return _existingSnippet;
        }

        return new CommandSnippet
        {
            Name = Name.Trim(),
            Command = Command,
            Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim(),
            Category = string.IsNullOrWhiteSpace(Category) ? null : Category.Trim()
        };
    }
}
