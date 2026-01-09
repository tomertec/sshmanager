namespace SshManager.Core.Models;

/// <summary>
/// Represents a single hop in a ProxyJump chain.
/// This is a junction table that defines the ordered list of jump hosts in a profile.
/// </summary>
public sealed class ProxyJumpHop
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// The profile this hop belongs to.
    /// </summary>
    public Guid ProxyJumpProfileId { get; set; }

    /// <summary>
    /// Navigation property to the profile.
    /// </summary>
    public ProxyJumpProfile? Profile { get; set; }

    /// <summary>
    /// The host entry used as a jump host for this hop.
    /// </summary>
    public Guid JumpHostId { get; set; }

    /// <summary>
    /// Navigation property to the jump host.
    /// </summary>
    public HostEntry? JumpHost { get; set; }

    /// <summary>
    /// Determines the order of this hop in the chain.
    /// 0 = first hop, 1 = second hop, etc.
    /// </summary>
    public int SortOrder { get; set; }
}
