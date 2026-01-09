using System.IO;
using System.IO.Compression;
using System.Text;

namespace SshManager.Terminal;

/// <summary>
/// File-backed segment for cold terminal output lines.
/// Lines are compressed with GZip and stored on disk to reduce memory usage.
/// Supports lazy loading - data is loaded on first access and can be unloaded to free memory.
/// </summary>
public sealed class FileTerminalOutputSegment : ITerminalOutputSegment
{
    private readonly string _filePath;
    private readonly int _lineCount;
    private readonly int _startLineIndex;
    private readonly SemaphoreSlim _loadLock = new(1, 1);
    private List<string>? _lines;
    private bool _disposed;

    private FileTerminalOutputSegment(string filePath, int lineCount, int startLineIndex)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
        _lineCount = lineCount;
        _startLineIndex = startLineIndex;
    }

    /// <inheritdoc />
    public int LineCount => _lineCount;

    /// <inheritdoc />
    public int StartLineIndex => _startLineIndex;

    /// <inheritdoc />
    public bool IsLoaded => _lines != null;

    /// <summary>
    /// Creates a new file-backed segment by archiving lines to disk.
    /// </summary>
    /// <param name="lines">The lines to archive.</param>
    /// <param name="startLineIndex">The starting line index in the overall buffer.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A new FileTerminalOutputSegment.</returns>
    public static async Task<FileTerminalOutputSegment> CreateAsync(
        IReadOnlyList<string> lines,
        int startLineIndex,
        CancellationToken ct = default)
    {
        if (lines == null || lines.Count == 0)
        {
            throw new ArgumentException("Lines cannot be null or empty", nameof(lines));
        }

        // Create temp directory for terminal buffer files
        var tempDir = Path.Combine(Path.GetTempPath(), "SshManager", "TerminalBuffer");
        Directory.CreateDirectory(tempDir);

        // Generate unique filename
        var fileName = $"segment_{Guid.NewGuid():N}.gz";
        var filePath = Path.Combine(tempDir, fileName);

        // Write lines to compressed file
        await using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
        await using (var gzipStream = new GZipStream(fileStream, CompressionLevel.Optimal))
        await using (var writer = new StreamWriter(gzipStream, Encoding.UTF8))
        {
            foreach (var line in lines)
            {
                ct.ThrowIfCancellationRequested();
                await writer.WriteLineAsync(line);
            }
        }

        return new FileTerminalOutputSegment(filePath, lines.Count, startLineIndex);
    }

    /// <inheritdoc />
    public async Task LoadAsync(CancellationToken ct = default)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FileTerminalOutputSegment));
        }

        // Fast path: already loaded
        if (_lines != null) return;

        // Acquire lock to ensure only one load operation at a time
        await _loadLock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring lock
            if (_lines != null) return;

            // Load from file
            var lines = new List<string>(_lineCount);

            using (var fileStream = new FileStream(_filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
            using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
            using (var reader = new StreamReader(gzipStream, Encoding.UTF8))
            {
                string? line;
                while ((line = await reader.ReadLineAsync()) != null)
                {
                    ct.ThrowIfCancellationRequested();
                    lines.Add(line);
                }
            }

            _lines = lines;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <inheritdoc />
    public void Unload()
    {
        if (_disposed) return;

        _loadLock.Wait();
        try
        {
            _lines = null;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    /// <inheritdoc />
    public string GetLine(int relativeIndex)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FileTerminalOutputSegment));
        }

        // Ensure loaded
        if (_lines == null)
        {
            // Synchronous load - blocks the calling thread
            LoadAsync().GetAwaiter().GetResult();
        }

        if (relativeIndex >= 0 && relativeIndex < _lines!.Count)
        {
            return _lines[relativeIndex];
        }
        return string.Empty;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetLines(int relativeStartIndex, int count)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(FileTerminalOutputSegment));
        }

        // Ensure loaded
        if (_lines == null)
        {
            // Synchronous load - blocks the calling thread
            LoadAsync().GetAwaiter().GetResult();
        }

        var result = new List<string>();
        var end = Math.Min(relativeStartIndex + count, _lines!.Count);

        for (var i = relativeStartIndex; i < end; i++)
        {
            if (i >= 0 && i < _lines.Count)
            {
                result.Add(_lines[i]);
            }
        }

        return result;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;

        _loadLock.Wait();
        try
        {
            _disposed = true;
            _lines = null;

            // Delete the temp file
            try
            {
                if (File.Exists(_filePath))
                {
                    File.Delete(_filePath);
                }
            }
            catch
            {
                // Ignore file deletion errors
            }
        }
        finally
        {
            _loadLock.Release();
            _loadLock.Dispose();
        }
    }
}
