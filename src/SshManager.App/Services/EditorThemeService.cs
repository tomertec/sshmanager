using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;

namespace SshManager.App.Services;

/// <summary>
/// Service for configuring AvalonEdit editor with dark theme matching WPF-UI.
/// </summary>
public class EditorThemeService : IEditorThemeService
{
    // Dark theme colors matching WPF-UI
    private static readonly Color BackgroundColor = Color.FromRgb(30, 30, 30);        // #1E1E1E
    private static readonly Color ForegroundColor = Color.FromRgb(212, 212, 212);     // #D4D4D4
    private static readonly Color LineNumberColor = Color.FromRgb(133, 133, 133);     // #858585
    private static readonly Color SelectionColor = Color.FromRgb(38, 79, 120);        // #264F78
    private static readonly Color CurrentLineColor = Color.FromRgb(40, 40, 40);       // #282828

    // Extension to highlighting name mappings
    private static readonly Dictionary<string, string> ExtensionToHighlighting = new(StringComparer.OrdinalIgnoreCase)
    {
        // C# / .NET
        [".cs"] = "C#",
        [".csx"] = "C#",

        // Web
        [".html"] = "HTML",
        [".htm"] = "HTML",
        [".xhtml"] = "HTML",
        [".css"] = "CSS",
        [".js"] = "JavaScript",
        [".jsx"] = "JavaScript",
        [".ts"] = "JavaScript",
        [".tsx"] = "JavaScript",
        [".json"] = "JavaScript",

        // XML variants
        [".xml"] = "XML",
        [".xaml"] = "XML",
        [".xslt"] = "XML",
        [".xsd"] = "XML",
        [".csproj"] = "XML",
        [".fsproj"] = "XML",
        [".vbproj"] = "XML",
        [".sln"] = "XML",
        [".props"] = "XML",
        [".targets"] = "XML",
        [".nuspec"] = "XML",
        [".config"] = "XML",
        [".svg"] = "XML",

        // C/C++
        [".c"] = "C++",
        [".cpp"] = "C++",
        [".cc"] = "C++",
        [".cxx"] = "C++",
        [".h"] = "C++",
        [".hpp"] = "C++",
        [".hxx"] = "C++",

        // Java
        [".java"] = "Java",

        // Python
        [".py"] = "Python",
        [".pyw"] = "Python",
        [".pyx"] = "Python",

        // PHP
        [".php"] = "PHP",
        [".php3"] = "PHP",
        [".php4"] = "PHP",
        [".php5"] = "PHP",
        [".phtml"] = "PHP",

        // VB.NET
        [".vb"] = "VB",
        [".vbs"] = "VB",

        // SQL
        [".sql"] = "TSQL",

        // PowerShell (fallback to text since AvalonEdit doesn't have built-in PowerShell)
        [".ps1"] = "Patch",
        [".psm1"] = "Patch",
        [".psd1"] = "Patch",

        // Shell scripts (use Patch as closest approximation)
        [".sh"] = "Patch",
        [".bash"] = "Patch",
        [".zsh"] = "Patch",
        [".fish"] = "Patch",

        // Batch
        [".bat"] = "Patch",
        [".cmd"] = "Patch",

        // F#
        [".fs"] = "F#",
        [".fsx"] = "F#",
        [".fsi"] = "F#",

        // Markdown
        [".md"] = "MarkDown",
        [".markdown"] = "MarkDown",

        // ASP.NET
        [".aspx"] = "ASP/XHTML",
        [".ascx"] = "ASP/XHTML",
        [".master"] = "ASP/XHTML",

        // Config files (YAML-like, use Patch)
        [".yaml"] = "Patch",
        [".yml"] = "Patch",
        [".toml"] = "Patch",
        [".ini"] = "Patch",
        [".cfg"] = "Patch",
        [".conf"] = "Patch",
        [".env"] = "Patch",

        // Docker
        [".dockerfile"] = "Patch",

        // Git
        [".gitignore"] = "Patch",
        [".gitattributes"] = "Patch",
        [".gitmodules"] = "Patch",

        // Other
        [".tex"] = "TeX",
        [".log"] = "Patch",
        [".txt"] = "Patch",
        [".editorconfig"] = "Patch",
        [".makefile"] = "Patch",

        // Go
        [".go"] = "C++",

        // Rust
        [".rs"] = "C++",

        // Ruby
        [".rb"] = "Python",
        [".rake"] = "Python",
        [".gemspec"] = "Python",
    };

    /// <inheritdoc/>
    public void ApplyDarkTheme(TextEditor editor)
    {
        // Background and foreground
        editor.Background = new SolidColorBrush(BackgroundColor);
        editor.Foreground = new SolidColorBrush(ForegroundColor);

        // Line number styling
        editor.LineNumbersForeground = new SolidColorBrush(LineNumberColor);
        editor.ShowLineNumbers = true;

        // Font settings - prefer Cascadia Code, fallback to Consolas
        editor.FontFamily = new FontFamily("Cascadia Code, Consolas, Courier New");
        editor.FontSize = 14;

        // Text area settings
        editor.TextArea.TextView.LinkTextForegroundBrush = new SolidColorBrush(Color.FromRgb(86, 156, 214)); // #569CD6
        editor.TextArea.TextView.LinkTextUnderline = true;

        // Selection brush
        editor.TextArea.SelectionBrush = new SolidColorBrush(SelectionColor);
        editor.TextArea.SelectionBorder = null;
        editor.TextArea.SelectionCornerRadius = 0;
        editor.TextArea.SelectionForeground = null; // Use default foreground

        // Current line highlighting
        editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(CurrentLineColor);
        editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.FromRgb(50, 50, 50)), 1);

        // Caret color
        editor.TextArea.Caret.CaretBrush = new SolidColorBrush(ForegroundColor);

        // Padding
        editor.Padding = new Thickness(4, 4, 4, 4);

        // Word wrap off by default for code
        editor.WordWrap = false;

        // Enable virtual space at end of lines
        editor.Options.EnableVirtualSpace = false;

        // Show end of line markers (optional, disabled for cleaner look)
        editor.Options.ShowEndOfLine = false;

        // Convert tabs to spaces
        editor.Options.ConvertTabsToSpaces = true;
        editor.Options.IndentationSize = 4;

        // Allow scrolling below document
        editor.Options.AllowScrollBelowDocument = true;

        // Highlight current line
        editor.Options.HighlightCurrentLine = true;

        // Enable rectangular selection with Alt+drag
        editor.Options.EnableRectangularSelection = true;

        // Column ruler (optional - disabled by default)
        editor.Options.ShowColumnRuler = false;
        editor.Options.ColumnRulerPosition = 120;
    }

    /// <inheritdoc/>
    public IHighlightingDefinition? GetHighlightingForExtension(string extension)
    {
        if (string.IsNullOrEmpty(extension))
            return null;

        // Ensure extension starts with dot
        if (!extension.StartsWith('.'))
            extension = "." + extension;

        // Try to get from our mapping first
        if (ExtensionToHighlighting.TryGetValue(extension, out var highlightingName))
        {
            return HighlightingManager.Instance.GetDefinition(highlightingName);
        }

        // Fallback to AvalonEdit's built-in extension detection
        return HighlightingManager.Instance.GetDefinitionByExtension(extension);
    }

    /// <inheritdoc/>
    public bool IsHighlightingSupported(string extension)
    {
        return GetHighlightingForExtension(extension) != null;
    }
}
