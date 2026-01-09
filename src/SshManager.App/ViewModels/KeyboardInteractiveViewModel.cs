using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Terminal.Models;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for keyboard-interactive authentication dialog (2FA/TOTP).
/// </summary>
public partial class KeyboardInteractiveViewModel : ObservableObject
{
    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _instruction = "";

    [ObservableProperty]
    private ObservableCollection<PromptViewModel> _prompts = [];

    [ObservableProperty]
    private bool _hasInstruction;

    public bool? DialogResult { get; private set; }

    public event Action? RequestClose;

    /// <summary>
    /// Title shown in the dialog.
    /// </summary>
    public string Title => string.IsNullOrWhiteSpace(Name) ? "Authentication Required" : Name;

    /// <summary>
    /// Initializes the dialog with an authentication request.
    /// </summary>
    public void Initialize(AuthenticationRequest request)
    {
        Name = request.Name;
        Instruction = request.Instruction;
        HasInstruction = !string.IsNullOrWhiteSpace(request.Instruction);

        Prompts.Clear();
        foreach (var prompt in request.Prompts)
        {
            Prompts.Add(new PromptViewModel(prompt));
        }

        OnPropertyChanged(nameof(Title));
    }

    /// <summary>
    /// Gets the authentication request with responses filled in.
    /// </summary>
    public AuthenticationRequest GetResponseRequest()
    {
        var prompts = Prompts.Select(p => new AuthenticationPrompt
        {
            Prompt = p.Prompt,
            IsPassword = p.IsPassword,
            Response = p.Response
        }).ToList();

        return new AuthenticationRequest
        {
            Name = Name,
            Instruction = Instruction,
            Prompts = prompts
        };
    }

    [RelayCommand]
    private void Submit()
    {
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

/// <summary>
/// ViewModel for a single authentication prompt.
/// </summary>
public partial class PromptViewModel : ObservableObject
{
    public PromptViewModel(AuthenticationPrompt prompt)
    {
        Prompt = prompt.Prompt;
        IsPassword = prompt.IsPassword;
        Response = prompt.Response ?? "";
    }

    /// <summary>
    /// The prompt text to display.
    /// </summary>
    public string Prompt { get; }

    /// <summary>
    /// Whether the input should be masked.
    /// </summary>
    public bool IsPassword { get; }

    /// <summary>
    /// Whether the input should be shown as plain text.
    /// </summary>
    public bool IsPlainText => !IsPassword;

    /// <summary>
    /// The user's response.
    /// </summary>
    [ObservableProperty]
    private string _response = "";
}
