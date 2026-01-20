namespace SshManager.Core.Models;

/// <summary>
/// Represents a single autocompletion suggestion item.
/// </summary>
public class CompletionItem
{
    /// <summary>
    /// Gets or sets the type of completion item.
    /// </summary>
    public CompletionItemType Type { get; set; }

    /// <summary>
    /// Gets or sets the text to display in the completion popup.
    /// </summary>
    public string DisplayText { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the text to insert when this item is selected.
    /// If null or empty, DisplayText will be used.
    /// </summary>
    public string? InsertText { get; set; }

    /// <summary>
    /// Gets or sets optional additional information about this item.
    /// </summary>
    public string? Description { get; set; }

    /// <summary>
    /// Gets or sets the relevance score for sorting (higher is more relevant).
    /// </summary>
    public int Score { get; set; }

    /// <summary>
    /// Gets the icon glyph for this completion item type.
    /// Uses Segoe Fluent Icons Unicode values.
    /// </summary>
    public string IconGlyph => Type switch
    {
        CompletionItemType.Command => "\uE756",      // CommandPrompt
        CompletionItemType.FilePath => "\uE8A5",     // Document
        CompletionItemType.Directory => "\uE8B7",    // Folder
        CompletionItemType.Argument => "\uE8AC",     // Tag
        CompletionItemType.History => "\uE81C",      // History
        _ => "\uE8AC"                                // Default to Tag icon
    };
}
