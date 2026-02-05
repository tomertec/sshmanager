using System.Buffers;
using System.Text;

namespace SshManager.Terminal.Services;

/// <summary>
/// Helper class for thread-safe UTF-8 decoding of byte arrays to strings.
/// Uses ArrayPool for efficient character buffer management.
/// </summary>
internal sealed class Utf8DecoderHelper : IDisposable
{
    private readonly Decoder _decoder = Encoding.UTF8.GetDecoder();
    private readonly object _lock = new();

    /// <summary>
    /// Decodes a byte array to a UTF-8 string using a stateful decoder.
    /// Thread-safe and handles multi-byte sequences split across packets.
    /// </summary>
    /// <param name="buffer">The byte array to decode.</param>
    /// <param name="offset">The starting offset in the buffer.</param>
    /// <param name="count">The number of bytes to decode.</param>
    /// <returns>The decoded UTF-8 string.</returns>
    public string Decode(byte[] buffer, int offset, int count)
    {
        lock (_lock)
        {
            var charCount = _decoder.GetCharCount(buffer, offset, count);
            var chars = ArrayPool<char>.Shared.Rent(charCount);
            try
            {
                var actualChars = _decoder.GetChars(buffer, offset, count, chars, 0);
                return new string(chars, 0, actualChars);
            }
            finally
            {
                ArrayPool<char>.Shared.Return(chars);
            }
        }
    }

    /// <summary>
    /// Disposes the helper. The decoder itself has no resources to dispose.
    /// </summary>
    public void Dispose()
    {
        // Decoder has no unmanaged resources to dispose
    }
}
