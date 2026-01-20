using System.Windows.Controls;

namespace SshManager.App.Views.Controls.HostEdit;

/// <summary>
/// Serial port connection settings section: COM port, baud rate, data bits,
/// stop bits, parity, flow control, line ending, and hardware options.
/// </summary>
public partial class SerialConnectionSection : UserControl
{
    public SerialConnectionSection()
    {
        InitializeComponent();
    }
}
