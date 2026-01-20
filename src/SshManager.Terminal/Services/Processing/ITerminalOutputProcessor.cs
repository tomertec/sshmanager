using SshManager.Terminal.Services.Recording;

namespace SshManager.Terminal.Services.Processing;

/// <summary>
/// Processes terminal output data by handling UTF-8 decoding and managing the output buffer.
/// </summary>
/// <remarks>
/// This service encapsulates two key responsibilities:
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
/// The service is thread-safe and uses <see cref="System.Buffers.ArrayPool{T}"/> for efficient
/// memory management during UTF-8 decoding.
/// </para>
/// </remarks>
public interface ITerminalOutputProcessor
{
    /// <summary>
    /// Gets the total number of lines currently stored in the output buffer.
    /// </summary>
    int TotalLines { get; }

    /// <summary>
    /// Gets or sets the maximum number of lines to retain across all storage segments.
    /// When this limit is exceeded, the oldest lines are discarded.
    /// </summary>
    int MaxLines { get; set; }

    /// <summary>
    /// Gets or sets the maximum number of lines to keep in memory.
    /// Older lines are compressed and archived to disk to reduce memory usage.
    /// </summary>
    int MaxLinesInMemory { get; set; }

    /// <summary>
    /// Processes raw byte data from the terminal connection.
    /// </summary>
    /// <param name="data">Raw bytes received from SSH or serial connection.</param>
    /// <returns>The decoded UTF-8 text, or empty string if the data contains only partial UTF-8 sequences.</returns>
    /// <remarks>
    /// <para>
    /// This method uses a stateful UTF-8 decoder to correctly handle multi-byte character sequences
    /// that may be split across network packets. For example, the Chinese character 中 (U+4E2D) is
    /// encoded as bytes [E4 B8 AD]. If a packet ends with [E4 B8] and the next packet starts with [AD],
    /// a stateless decoder would produce garbage, but this decoder maintains state between calls.
    /// </para>
    /// <para>
    /// The method is thread-safe and uses <see cref="System.Buffers.ArrayPool{T}"/> for efficient
    /// character buffer allocation.
    /// </para>
    /// </remarks>
    string ProcessData(byte[] data);

    /// <summary>
    /// Appends decoded text to the output buffer.
    /// ANSI escape sequences are stripped and lines are stored as plain text.
    /// </summary>
    /// <param name="text">The decoded terminal output text (may contain ANSI sequences).</param>
    /// <remarks>
    /// The text is stored in a tiered buffer: recent lines are kept in memory for fast access,
    /// while older lines are automatically compressed and archived to disk to reduce memory usage.
    /// </remarks>
    void AppendToBuffer(string text);

    /// <summary>
    /// Gets all text from the output buffer as a single string.
    /// </summary>
    /// <returns>The complete terminal output text with newlines.</returns>
    /// <remarks>
    /// <b>Warning:</b> This may be memory-intensive for large buffers as it loads all segments
    /// from disk into memory. Use sparingly for export/logging operations.
    /// </remarks>
    string GetAllText();

    /// <summary>
    /// Clears all lines from the output buffer and disposes all storage segments.
    /// </summary>
    /// <remarks>
    /// This frees memory and deletes any temporary files used for archived segments.
    /// </remarks>
    void Clear();

    /// <summary>
    /// Records the raw output data to a session recorder if provided.
    /// </summary>
    /// <param name="recorder">The session recorder to write to, or null to skip recording.</param>
    /// <param name="data">The raw bytes to record.</param>
    /// <remarks>
    /// This is a convenience method that delegates to the recorder's <c>RecordOutput</c> method.
    /// It's provided here to keep all output processing in one place.
    /// </remarks>
    void RecordOutput(SessionRecorder? recorder, byte[] data);
}
