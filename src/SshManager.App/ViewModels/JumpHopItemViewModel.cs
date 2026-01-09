using CommunityToolkit.Mvvm.ComponentModel;
using SshManager.Core.Models;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel representing a single hop in a ProxyJump chain for UI display.
/// </summary>
public partial class JumpHopItemViewModel : ObservableObject
{
    /// <summary>
    /// The underlying hop entity ID (null for new hops not yet persisted).
    /// </summary>
    public Guid? HopId { get; init; }

    /// <summary>
    /// The host entry ID used as a jump host.
    /// </summary>
    public Guid HostId { get; init; }

    /// <summary>
    /// Display name of the jump host.
    /// </summary>
    [ObservableProperty]
    private string _hostDisplayName = string.Empty;

    /// <summary>
    /// Connection address in format "user@host:port".
    /// </summary>
    [ObservableProperty]
    private string _hostAddress = string.Empty;

    /// <summary>
    /// Order of this hop in the chain (0-based).
    /// </summary>
    [ObservableProperty]
    private int _sortOrder;

    /// <summary>
    /// Creates a JumpHopItemViewModel from a ProxyJumpHop entity.
    /// </summary>
    public static JumpHopItemViewModel FromHop(ProxyJumpHop hop)
    {
        var jumpHost = hop.JumpHost;
        return new JumpHopItemViewModel
        {
            HopId = hop.Id,
            HostId = hop.JumpHostId,
            HostDisplayName = jumpHost?.DisplayName ?? "Unknown Host",
            HostAddress = jumpHost != null
                ? $"{jumpHost.Username}@{jumpHost.Hostname}:{jumpHost.Port}"
                : "unknown",
            SortOrder = hop.SortOrder
        };
    }

    /// <summary>
    /// Creates a JumpHopItemViewModel from a HostEntry.
    /// </summary>
    public static JumpHopItemViewModel FromHost(HostEntry host, int sortOrder)
    {
        return new JumpHopItemViewModel
        {
            HopId = null,
            HostId = host.Id,
            HostDisplayName = host.DisplayName,
            HostAddress = $"{host.Username}@{host.Hostname}:{host.Port}",
            SortOrder = sortOrder
        };
    }

    /// <summary>
    /// Converts this ViewModel to a ProxyJumpHop entity.
    /// </summary>
    public ProxyJumpHop ToHop(Guid profileId)
    {
        return new ProxyJumpHop
        {
            Id = HopId ?? Guid.NewGuid(),
            ProxyJumpProfileId = profileId,
            JumpHostId = HostId,
            SortOrder = SortOrder
        };
    }
}
