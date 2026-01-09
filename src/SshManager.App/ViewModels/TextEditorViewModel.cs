using System.IO;
using System.Security.Cryptography;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Highlighting;
using SshManager.App.Services;
using SshManager.Terminal.Services;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the text editor window with dirty tracking and remote file support.
/// </summary>
public partial class TextEditorViewModel : ObservableObject
{
    private readonly IEditorThemeService _themeService;
    private ISftpSession? _sftpSession;
    private string? _originalContentHash;

    /// <summary>
    /// The file path (local temp file for remote files, actual path for local files).
    /// </summary>
    [ObservableProperty]
    private string _filePath = "";

    /// <summary>
    /// The remote file path (null for local files).
    /// </summary>
    [ObservableProperty]
    private string? _remotePath;

    /// <summary>
    /// The host display name (for remote files).
    /// </summary>
    [ObservableProperty]
    private string? _hostName;

    /// <summary>
    /// True if this is a remote file.
    /// </summary>
    public bool IsRemoteFile => !string.IsNullOrEmpty(RemotePath);

    /// <summary>
    /// The file name for display.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    private string _fileName = "";

    /// <summary>
    /// True if the content has been modified.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WindowTitle))]
    [NotifyPropertyChangedFor(nameof(ModifiedIndicator))]
    private bool _isDirty;

    /// <summary>
    /// The text document for AvalonEdit binding.
    /// </summary>
    [ObservableProperty]
    private TextDocument? _document;

    /// <summary>
    /// Current line number (1-based).
    /// </summary>
    [ObservableProperty]
    private int _lineNumber = 1;

    /// <summary>
    /// Current column number (1-based).
    /// </summary>
    [ObservableProperty]
    private int _columnNumber = 1;

    /// <summary>
    /// Total number of lines in the document.
    /// </summary>
    [ObservableProperty]
    private int _totalLines = 1;

    /// <summary>
    /// The syntax highlighting definition.
    /// </summary>
    [ObservableProperty]
    private IHighlightingDefinition? _highlightingDefinition;

    /// <summary>
    /// True if the editor is currently loading or saving.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isBusy;

    /// <summary>
    /// Status message for the status bar.
    /// </summary>
    [ObservableProperty]
    private string _statusMessage = "";

    /// <summary>
    /// The file encoding.
    /// </summary>
    [ObservableProperty]
    private Encoding _encoding = Encoding.UTF8;

    /// <summary>
    /// Window title including file name and dirty indicator.
    /// </summary>
    public string WindowTitle
    {
        get
        {
            var title = FileName;
            if (IsDirty)
                title = "* " + title;
            if (IsRemoteFile && !string.IsNullOrEmpty(HostName))
                title += $" [{HostName}]";
            return title + " - Text Editor";
        }
    }

    /// <summary>
    /// Modified indicator for status bar.
    /// </summary>
    public string ModifiedIndicator => IsDirty ? "Modified" : "";

    /// <summary>
    /// Status text for the status bar.
    /// </summary>
    public string StatusText => IsBusy ? StatusMessage : (IsRemoteFile ? $"Remote: {RemotePath}" : FilePath);

    /// <summary>
    /// Result indicating whether changes were saved.
    /// </summary>
    public bool? DialogResult { get; private set; }

    /// <summary>
    /// Event raised when the editor should be closed.
    /// </summary>
    public event Action? RequestClose;

    /// <summary>
    /// Event raised when a message should be shown to the user.
    /// </summary>
    public event Action<string, string>? MessageRequested;

    /// <summary>
    /// Event raised when the user should be asked to save changes.
    /// Returns true to save, false to discard, null to cancel.
    /// </summary>
    public event Func<bool?>? SaveChangesRequested;

    public TextEditorViewModel(IEditorThemeService themeService)
    {
        _themeService = themeService;
    }

    /// <summary>
    /// Loads a local file for editing.
    /// </summary>
    public async Task LoadLocalFileAsync(string filePath, CancellationToken ct = default)
    {
        IsBusy = true;
        StatusMessage = "Loading file...";

        try
        {
            FilePath = filePath;
            FileName = Path.GetFileName(filePath);
            RemotePath = null;
            HostName = null;
            _sftpSession = null;

            var content = await File.ReadAllTextAsync(filePath, ct);
            Document = new TextDocument(content);
            _originalContentHash = ComputeHash(content);

            // Detect syntax highlighting
            var extension = Path.GetExtension(filePath);
            HighlightingDefinition = _themeService.GetHighlightingForExtension(extension);

            TotalLines = Document.LineCount;
            IsDirty = false;
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            MessageRequested?.Invoke("Error", $"Failed to load file: {ex.Message}");
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Loads a remote file for editing via an existing SFTP session.
    /// </summary>
    /// <param name="session">The active SFTP session.</param>
    /// <param name="remotePath">The remote file path.</param>
    /// <param name="hostName">The host display name.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task LoadRemoteFileAsync(ISftpSession session, string remotePath, string hostName, CancellationToken ct = default)
    {
        IsBusy = true;
        StatusMessage = "Downloading remote file...";

        try
        {
            _sftpSession = session;
            RemotePath = remotePath;
            HostName = hostName;
            FileName = Path.GetFileName(remotePath);

            // Download to temp file
            var tempDir = Path.Combine(Path.GetTempPath(), "SshManager", "EditTemp");
            Directory.CreateDirectory(tempDir);

            // Create a unique temp file name
            var safeFileName = string.Join("_", FileName.Split(Path.GetInvalidFileNameChars()));
            var tempFile = Path.Combine(tempDir, $"{Guid.NewGuid():N}_{safeFileName}");
            FilePath = tempFile;

            // Download the file content
            var content = await session.ReadAllBytesAsync(remotePath, ct);

            // Detect encoding (default to UTF-8)
            Encoding = DetectEncoding(content);
            var text = Encoding.GetString(content);

            // Save to temp file
            await File.WriteAllTextAsync(tempFile, text, Encoding, ct);

            Document = new TextDocument(text);
            _originalContentHash = ComputeHash(text);

            // Detect syntax highlighting
            var extension = Path.GetExtension(remotePath);
            HighlightingDefinition = _themeService.GetHighlightingForExtension(extension);

            TotalLines = Document.LineCount;
            IsDirty = false;
            StatusMessage = "";
        }
        catch (Exception ex)
        {
            MessageRequested?.Invoke("Error", $"Failed to download remote file: {ex.Message}");
            throw;
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>
    /// Updates the current caret position (called from view).
    /// </summary>
    public void UpdateCaretPosition(int line, int column)
    {
        LineNumber = line;
        ColumnNumber = column;
    }

    /// <summary>
    /// Marks the content as modified (called when document text changes).
    /// </summary>
    public void MarkDirty()
    {
        if (Document == null) return;

        var currentHash = ComputeHash(Document.Text);
        IsDirty = currentHash != _originalContentHash;
        TotalLines = Document.LineCount;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (Document == null) return;

        IsBusy = true;
        StatusMessage = IsRemoteFile ? "Uploading to remote..." : "Saving...";

        try
        {
            var content = Document.Text;

            if (IsRemoteFile && _sftpSession != null && _sftpSession.IsConnected && !string.IsNullOrEmpty(RemotePath))
            {
                // Save to temp file first
                await File.WriteAllTextAsync(FilePath, content, Encoding);

                // Upload to remote
                var bytes = Encoding.GetBytes(content);
                await _sftpSession.WriteAllBytesAsync(RemotePath, bytes);

                StatusMessage = "Uploaded successfully";
            }
            else
            {
                // Save locally
                await File.WriteAllTextAsync(FilePath, content, Encoding);
                StatusMessage = "Saved successfully";
            }

            _originalContentHash = ComputeHash(content);
            IsDirty = false;

            // Clear status after a delay
            await Task.Delay(2000);
            if (StatusMessage == "Saved successfully" || StatusMessage == "Uploaded successfully")
                StatusMessage = "";
        }
        catch (Exception ex)
        {
            MessageRequested?.Invoke("Error", $"Failed to save: {ex.Message}");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (IsDirty)
        {
            var result = SaveChangesRequested?.Invoke();
            if (result == null) return; // Cancel
            if (result == true) await SaveAsync(); // Save first
        }

        try
        {
            if (IsRemoteFile && _sftpSession != null && _sftpSession.IsConnected && !string.IsNullOrEmpty(RemotePath))
            {
                await LoadRemoteFileAsync(_sftpSession, RemotePath, HostName ?? "");
            }
            else
            {
                await LoadLocalFileAsync(FilePath);
            }
        }
        catch
        {
            // Error already shown in load methods
        }
    }

    [RelayCommand]
    private void Close()
    {
        if (IsDirty)
        {
            var result = SaveChangesRequested?.Invoke();
            if (result == null) return; // Cancel

            if (result == true)
            {
                // Save synchronously (or fire and forget the async version)
                Task.Run(async () => await SaveAsync()).Wait();
            }
        }

        // Clean up temp file for remote files
        CleanupTempFile();

        DialogResult = true;
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Attempts to close the editor, returns false if cancelled.
    /// </summary>
    public bool TryClose()
    {
        if (IsDirty)
        {
            var result = SaveChangesRequested?.Invoke();
            if (result == null) return false; // Cancel

            if (result == true)
            {
                Task.Run(async () => await SaveAsync()).Wait();
            }
        }

        CleanupTempFile();
        return true;
    }

    private void CleanupTempFile()
    {
        if (IsRemoteFile && !string.IsNullOrEmpty(FilePath) && File.Exists(FilePath))
        {
            try
            {
                File.Delete(FilePath);
            }
            catch
            {
                // Ignore cleanup failures
            }
        }
    }

    private static string ComputeHash(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);
        var hash = SHA256.HashData(bytes);
        return Convert.ToBase64String(hash);
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        // Check for BOM
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode; // UTF-16 LE
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode; // UTF-16 BE

        // Default to UTF-8
        return Encoding.UTF8;
    }
}
