using SshManager.Core.Models;

namespace SshManager.App.Services;

/// <summary>
/// Service for resolving conflicts between local and remote sync data.
/// </summary>
public interface ISyncConflictResolver
{
    /// <summary>
    /// Merges local and remote sync data using last-modified-wins strategy.
    /// </summary>
    /// <param name="local">The local sync data.</param>
    /// <param name="remote">The remote sync data from the cloud.</param>
    /// <returns>Merged sync data.</returns>
    SyncData Resolve(SyncData local, SyncData remote);
}
