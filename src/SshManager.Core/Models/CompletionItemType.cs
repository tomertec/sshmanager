namespace SshManager.Core.Models;

/// <summary>
/// Specifies the type of autocompletion item.
/// </summary>
public enum CompletionItemType
{
    /// <summary>
    /// A shell command or executable.
    /// </summary>
    Command = 0,

    /// <summary>
    /// A file path.
    /// </summary>
    FilePath = 1,

    /// <summary>
    /// A directory path.
    /// </summary>
    Directory = 2,

    /// <summary>
    /// A command argument or option.
    /// </summary>
    Argument = 3,

    /// <summary>
    /// An item from command history.
    /// </summary>
    History = 4
}
