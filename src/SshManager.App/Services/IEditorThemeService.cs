using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;

namespace SshManager.App.Services;

/// <summary>
/// Service for configuring AvalonEdit editor theme and appearance.
/// </summary>
public interface IEditorThemeService
{
    /// <summary>
    /// Applies the dark theme to the specified AvalonEdit editor.
    /// </summary>
    /// <param name="editor">The AvalonEdit editor control.</param>
    void ApplyDarkTheme(TextEditor editor);

    /// <summary>
    /// Gets the syntax highlighting definition for a file extension.
    /// </summary>
    /// <param name="extension">The file extension (including the dot, e.g., ".cs").</param>
    /// <returns>The highlighting definition, or null if not found.</returns>
    IHighlightingDefinition? GetHighlightingForExtension(string extension);

    /// <summary>
    /// Checks if a file extension is supported for syntax highlighting.
    /// </summary>
    /// <param name="extension">The file extension (including the dot, e.g., ".cs").</param>
    /// <returns>True if syntax highlighting is available for this extension.</returns>
    bool IsHighlightingSupported(string extension);
}
