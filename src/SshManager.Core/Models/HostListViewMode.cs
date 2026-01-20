namespace SshManager.Core.Models;

/// <summary>
/// View mode for the host list display.
/// </summary>
public enum HostListViewMode
{
    /// <summary>
    /// Compact view with minimal information.
    /// </summary>
    Compact,
    
    /// <summary>
    /// Normal view with standard information.
    /// </summary>
    Normal,
    
    /// <summary>
    /// Detailed view with additional statistics and information.
    /// </summary>
    Detailed
}
