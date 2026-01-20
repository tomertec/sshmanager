using System.Buffers;
using System.Text;
using SshManager.Terminal.Services.Recording;

namespace SshManager.Terminal.Services.Processing;

/// <summary>
/// Processes terminal output data by handling UTF-8 decoding and managing the output buffer.
/// </summary>
/// <remarks>
/// <para>
/// This service encapsulates two key responsibilities:
/// </para>
/// <list type="number">
/// <item><description>
/// <b>Stateful UTF-8 Decoding:</b> Handles multi-byte UTF-8 sequences that may be split across
/// network packets. Uses a stateful decoder to correctly handle partial sequences.
/// </description></item>
/// <item><description>
/// <b>Output Buffer Management:</b> Maintains a searchable text buffer of terminal output
/// using a tiered storage strategy (recent lines in memory, older lines compressed to disk).
/// </description></item>
/// </list>
/// <para>
/// <b>Thread Safety:</b> This class is thread-safe for all public methods. The UTF-8 decoder
/// is protected by a lock to ensure that multi-byte sequences are processed correctly even
/// when called from multiple threads.
/// </para>
/// <para>
/// <b>Memory Efficiency:</b> Uses <see cref="ArrayPool{T}"/> for character buffer allocation
/// during UTF-8 decoding, and the output buffer uses tiered storage to minimize memory usage
/// during long sessions.
/// </para>
/// </remarks>
public sealed class TerminalOutputProcessor : ITerminalOutputProcessor, IDisposable
{
    private readonly TerminalOutputBuffer _outputBuffer;
    private readonly Decoder _decoder;
    private readonly object _decoderLock = new();
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="TerminalOutputProcessor"/> class.
    /// </summary>
    /// <param name="maxLines">Maximum number of lines to retain (default: 10000).</param>
    /// <param name="maxLinesInMemory">Maximum number of lines to keep in memory (default: 5000).</param>
    public TerminalOutputProcessor(int maxLines = 10000, int maxLinesInMemory = 5000)
    {
        _outputBuffer = new TerminalOutputBuffer(maxLines, maxLinesInMemory);
        _decoder = Encoding.UTF8.GetDecoder();
    }

    /// <inheritdoc />
    public int TotalLines => _outputBuffer.TotalLineCount;

    /// <inheritdoc />
    public int MaxLines
    {
        get => _outputBuffer.MaxLines;
        set => _outputBuffer.MaxLines = value;
    }

    /// <inheritdoc />
    public int MaxLinesInMemory
    {
        get => _outputBuffer.MaxLinesInMemory;
        set => _outputBuffer.MaxLinesInMemory = value;
    }

    /// <inheritdoc />
    public string ProcessData(byte[] data)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalOutputProcessor));
        }

        if (data == null || data.Length == 0)
        {
            return string.Empty;
        }

        // CRITICAL: Use stateful decoder to handle multi-byte UTF-8 sequences correctly.
        // SSH data arrives as raw bytes in arbitrary chunks that may split UTF-8 characters.
        // Example: Chinese character 中 (U+4E2D) = bytes [E4 B8 AD]
        // If packet 1 contains [E4 B8] and packet 2 contains [AD ...], a stateless
        // decoder would produce garbage. The stateful decoder remembers partial sequences.
        lock (_decoderLock)
        {
            // Rent buffer from array pool for efficiency
            // Add 4 extra bytes to handle potential multi-byte sequences
            var charBuffer = ArrayPool<char>.Shared.Rent(data.Length + 4);
            try
            {
                // Decode bytes to characters using stateful decoder
                // flush: false - preserve partial multi-byte sequences for next call
                var charCount = _decoder.GetChars(data, 0, data.Length, charBuffer, 0, flush: false);

                // Return decoded string or empty if no complete characters were decoded
                return charCount > 0 ? new string(charBuffer, 0, charCount) : string.Empty;
            }
            finally
            {
                // Always return buffer to pool
                ArrayPool<char>.Shared.Return(charBuffer);
            }
        }
    }

    /// <inheritdoc />
    public void AppendToBuffer(string text)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalOutputProcessor));
        }

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        // Delegate to output buffer - it handles ANSI stripping and tiered storage
        _outputBuffer.AppendOutput(text);
    }

    /// <inheritdoc />
    public string GetAllText()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalOutputProcessor));
        }

        return _outputBuffer.GetAllText();
    }

    /// <inheritdoc />
    public void Clear()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalOutputProcessor));
        }

        _outputBuffer.Clear();
    }

    /// <inheritdoc />
    public void RecordOutput(SessionRecorder? recorder, byte[] data)
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalOutputProcessor));
        }

        // Record raw output to session recorder if present
        // This is a convenience method to keep all output processing centralized
        recorder?.RecordOutput(data);
    }

    /// <summary>
    /// Gets the internal output buffer for advanced scenarios like search.
    /// </summary>
    /// <returns>The underlying <see cref="TerminalOutputBuffer"/> instance.</returns>
    /// <remarks>
    /// This method is provided for backward compatibility with existing code that needs
    /// direct access to the buffer (e.g., for search functionality). In general, prefer
    /// using the processor's public methods instead of accessing the buffer directly.
    /// </remarks>
    internal TerminalOutputBuffer GetOutputBuffer()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(TerminalOutputProcessor));
        }

        return _outputBuffer;
    }

    /// <summary>
    /// Disposes the processor and releases all resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Dispose the output buffer (cleans up temp files)
        _outputBuffer.Dispose();
    }
}
