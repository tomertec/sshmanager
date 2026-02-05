using RJCP.IO.Ports;

namespace SshManager.Terminal.Services;

/// <summary>
/// Helper class for formatting serial port settings into display strings.
/// Provides consistent formatting across the application.
/// </summary>
internal static class SerialPortFormatHelper
{
    /// <summary>
    /// Gets a single character representation of the parity setting.
    /// </summary>
    /// <param name="parity">The parity setting.</param>
    /// <returns>A single character: 'N' for None, 'O' for Odd, 'E' for Even, 'M' for Mark, 'S' for Space.</returns>
    public static char GetParityChar(Parity parity)
    {
        return parity switch
        {
            Parity.None => 'N',
            Parity.Odd => 'O',
            Parity.Even => 'E',
            Parity.Mark => 'M',
            Parity.Space => 'S',
            _ => '?'
        };
    }

    /// <summary>
    /// Gets a string representation of the stop bits setting.
    /// </summary>
    /// <param name="stopBits">The stop bits setting.</param>
    /// <returns>A string: "1", "1.5", or "2".</returns>
    public static string GetStopBitsString(StopBits stopBits)
    {
        return stopBits switch
        {
            StopBits.One => "1",
            StopBits.One5 => "1.5",
            StopBits.Two => "2",
            _ => "?"
        };
    }

    /// <summary>
    /// Gets a display string for handshake/flow control mode.
    /// </summary>
    /// <param name="handshake">The handshake mode.</param>
    /// <returns>A display string: "None", "XON/XOFF", "RTS/CTS", or "RTS+XON".</returns>
    public static string GetHandshakeString(Handshake handshake)
    {
        return handshake switch
        {
            Handshake.None => "None",
            Handshake.XOn => "XON/XOFF",
            Handshake.Rts => "RTS/CTS",
            Handshake.RtsXOn => "RTS+XON",
            _ => "?"
        };
    }
}
