using CommunityToolkit.Mvvm.ComponentModel;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel representing a single environment variable for a host.
/// Used in the HostDialogViewModel for editing environment variables.
/// </summary>
public partial class HostEnvironmentVariableViewModel : ObservableObject
{
    /// <summary>
    /// The environment variable name (e.g., "TERM", "LANG").
    /// </summary>
    [ObservableProperty]
    private string _name = string.Empty;

    /// <summary>
    /// The environment variable value (e.g., "xterm-256color", "en_US.UTF-8").
    /// </summary>
    [ObservableProperty]
    private string _value = string.Empty;

    /// <summary>
    /// Whether this environment variable should be applied when connecting.
    /// </summary>
    [ObservableProperty]
    private bool _isEnabled = true;
}
