namespace SshManager.Security.OnePassword;

/// <summary>
/// Service for interacting with 1Password via the CLI (op command).
/// Supports fetching passwords and SSH keys using op:// secret references.
/// </summary>
public interface IOnePasswordService
{
    /// <summary>
    /// Checks whether the 1Password CLI (op) is installed and available on the system PATH.
    /// </summary>
    Task<bool> IsInstalledAsync(CancellationToken ct = default);

    /// <summary>
    /// Checks whether the user is currently authenticated with 1Password.
    /// Requires desktop app integration (Windows Hello / biometric).
    /// </summary>
    Task<bool> IsAuthenticatedAsync(CancellationToken ct = default);

    /// <summary>
    /// Gets the full status of the 1Password CLI including installation and authentication state.
    /// </summary>
    Task<OnePasswordStatus> GetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// Reads a secret value using an op:// secret reference (e.g., "op://vault/item/field").
    /// Typically used for passwords.
    /// </summary>
    Task<string?> ReadSecretAsync(string secretReference, CancellationToken ct = default);

    /// <summary>
    /// Reads an SSH private key using an op:// secret reference in OpenSSH format.
    /// </summary>
    Task<string?> ReadSshKeyAsync(string secretReference, CancellationToken ct = default);

    /// <summary>
    /// Lists all accessible vaults.
    /// </summary>
    Task<IReadOnlyList<OnePasswordVault>> ListVaultsAsync(CancellationToken ct = default);

    /// <summary>
    /// Lists items in a vault, optionally filtered by vault ID and search query.
    /// Only returns items in relevant categories (Login, SSH Key, Password, Server).
    /// </summary>
    Task<IReadOnlyList<OnePasswordItem>> ListItemsAsync(
        string? vaultId = null,
        string? query = null,
        CancellationToken ct = default);

    /// <summary>
    /// Gets detailed item information including all fields.
    /// </summary>
    Task<OnePasswordItemDetail?> GetItemAsync(
        string itemId,
        string? vaultId = null,
        CancellationToken ct = default);
}
