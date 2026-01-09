using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the passphrase prompt dialog.
/// </summary>
public partial class PassphrasePromptViewModel : ObservableObject
{
    [ObservableProperty]
    private string _title = "Enter Passphrase";

    [ObservableProperty]
    private string _message = "This key is encrypted. Please enter the passphrase to decrypt it.";

    [ObservableProperty]
    private string _passphrase = "";

    [ObservableProperty]
    private string? _validationError;

    public bool? DialogResult { get; private set; }
    public event Action? RequestClose;

    public PassphrasePromptViewModel()
    {
    }

    public PassphrasePromptViewModel(string title, string message)
    {
        Title = title;
        Message = message;
    }

    [RelayCommand]
    private void Ok()
    {
        ValidationError = null;

        if (string.IsNullOrEmpty(Passphrase))
        {
            ValidationError = "Passphrase cannot be empty";
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
}
