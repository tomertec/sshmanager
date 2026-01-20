namespace SshManager.Core.Models;

/// <summary>
/// Specifies the mode for terminal autocompletion.
/// </summary>
public enum AutocompletionMode
{
    /// <summary>
    /// Use remote shell's completion (compgen) - real-time remote completions.
    /// </summary>
    RemoteShell = 0,

    /// <summary>
    /// Use locally stored command history for suggestions.
    /// </summary>
    LocalHistory = 1,

    /// <summary>
    /// Combine remote shell and local history, ranked by relevance.
    /// </summary>
    Hybrid = 2
}
