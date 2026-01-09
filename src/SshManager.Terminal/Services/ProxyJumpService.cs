using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Terminal.Models;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service implementation for resolving and validating ProxyJump connection chains.
/// </summary>
public sealed class ProxyJumpService : IProxyJumpService
{
    private readonly IProxyJumpProfileRepository _profileRepository;
    private readonly IHostRepository _hostRepository;
    private readonly ILogger<ProxyJumpService> _logger;

    public ProxyJumpService(
        IProxyJumpProfileRepository profileRepository,
        IHostRepository hostRepository,
        ILogger<ProxyJumpService>? logger = null)
    {
        _profileRepository = profileRepository;
        _hostRepository = hostRepository;
        _logger = logger ?? NullLogger<ProxyJumpService>.Instance;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<TerminalConnectionInfo>> ResolveConnectionChainAsync(
        HostEntry targetHost,
        IReadOnlyDictionary<Guid, string>? decryptedPasswords = null,
        CancellationToken ct = default)
    {
        if (targetHost.ProxyJumpProfileId is null)
        {
            _logger.LogDebug("No proxy jump profile configured for host {HostId}", targetHost.Id);
            return [];
        }

        var profile = await _profileRepository.GetByIdWithHopsAsync(targetHost.ProxyJumpProfileId.Value, ct);
        if (profile is null)
        {
            _logger.LogWarning("ProxyJump profile {ProfileId} not found for host {HostId}",
                targetHost.ProxyJumpProfileId.Value, targetHost.Id);
            return [];
        }

        if (!profile.IsEnabled)
        {
            _logger.LogInformation("ProxyJump profile {ProfileName} is disabled, skipping proxy chain",
                profile.DisplayName);
            return [];
        }

        if (profile.JumpHops.Count == 0)
        {
            _logger.LogDebug("ProxyJump profile {ProfileName} has no hops configured", profile.DisplayName);
            return [];
        }

        var chain = new List<TerminalConnectionInfo>();
        var orderedHops = profile.JumpHops.OrderBy(h => h.SortOrder).ToList();

        _logger.LogInformation("Resolving proxy chain with {HopCount} hops for target {TargetHost}",
            orderedHops.Count, targetHost.Hostname);

        foreach (var hop in orderedHops)
        {
            var jumpHost = hop.JumpHost;

            // If JumpHost navigation property isn't loaded, fetch it
            if (jumpHost is null)
            {
                jumpHost = await _hostRepository.GetByIdAsync(hop.JumpHostId, ct);
                if (jumpHost is null)
                {
                    _logger.LogError("Jump host {JumpHostId} not found in hop {HopId}",
                        hop.JumpHostId, hop.Id);
                    throw new InvalidOperationException(
                        $"Jump host with ID {hop.JumpHostId} not found. The proxy chain cannot be established.");
                }
            }

            // Get decrypted password if available
            string? password = null;
            if (decryptedPasswords?.TryGetValue(jumpHost.Id, out var decryptedPassword) == true)
            {
                password = decryptedPassword;
            }

            var connectionInfo = TerminalConnectionInfo.FromHostEntry(jumpHost, password);
            chain.Add(connectionInfo);

            _logger.LogDebug("Added jump host to chain: {Host}:{Port} (hop {Order})",
                jumpHost.Hostname, jumpHost.Port, hop.SortOrder);
        }

        // Add target host at the end
        string? targetPassword = null;
        if (decryptedPasswords?.TryGetValue(targetHost.Id, out var targetDecryptedPassword) == true)
        {
            targetPassword = targetDecryptedPassword;
        }

        chain.Add(TerminalConnectionInfo.FromHostEntry(targetHost, targetPassword));

        _logger.LogInformation("Resolved connection chain: {Chain}",
            string.Join(" -> ", chain.Select(c => $"{c.Hostname}:{c.Port}")));

        return chain;
    }

    /// <inheritdoc />
    public async Task<ProxyJumpValidationResult> ValidateProfileAsync(
        ProxyJumpProfile profile,
        CancellationToken ct = default)
    {
        var issues = new List<string>();

        // Check for empty chain
        if (profile.JumpHops.Count == 0)
        {
            issues.Add("The jump chain is empty. At least one jump host is required.");
        }

        // Check for duplicate hosts in chain
        var hostIds = profile.JumpHops.Select(h => h.JumpHostId).ToList();
        var duplicates = hostIds.GroupBy(id => id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();

        if (duplicates.Count > 0)
        {
            issues.Add($"The chain contains duplicate hosts. Each host can only appear once in the chain.");
        }

        // Check that all jump hosts exist and are accessible
        foreach (var hop in profile.JumpHops)
        {
            var jumpHost = hop.JumpHost ?? await _hostRepository.GetByIdAsync(hop.JumpHostId, ct);

            if (jumpHost is null)
            {
                issues.Add($"Jump host with ID {hop.JumpHostId} (hop {hop.SortOrder + 1}) does not exist.");
            }
        }

        // Check for circular references (host referencing itself)
        // This is more relevant when we check against a target host

        if (issues.Count > 0)
        {
            _logger.LogWarning("ProxyJump profile validation failed with {IssueCount} issues: {Issues}",
                issues.Count, string.Join("; ", issues));

            return ProxyJumpValidationResult.Failure(
                $"Profile validation failed with {issues.Count} issue(s).",
                issues);
        }

        _logger.LogDebug("ProxyJump profile {ProfileName} passed validation", profile.DisplayName);
        return ProxyJumpValidationResult.Success();
    }

    /// <inheritdoc />
    public async Task<ProxyJumpValidationResult> ValidateProfileForHostAsync(
        ProxyJumpProfile profile,
        Guid targetHostId,
        CancellationToken ct = default)
    {
        // First, run the general validation
        var baseResult = await ValidateProfileAsync(profile, ct);
        if (!baseResult.IsValid)
        {
            return baseResult;
        }

        var issues = new List<string>();

        // Check if target host is part of its own jump chain (circular reference)
        var hostIdsInChain = profile.JumpHops.Select(h => h.JumpHostId).ToHashSet();
        if (hostIdsInChain.Contains(targetHostId))
        {
            issues.Add("The target host cannot be part of its own proxy jump chain.");
        }

        // Check for nested proxy chains that might create cycles
        // (A jump host that itself uses a proxy chain containing the target)
        foreach (var hop in profile.JumpHops)
        {
            var jumpHost = hop.JumpHost ?? await _hostRepository.GetByIdAsync(hop.JumpHostId, ct);
            if (jumpHost?.ProxyJumpProfileId is not null)
            {
                var nestedProfile = await _profileRepository.GetByIdWithHopsAsync(
                    jumpHost.ProxyJumpProfileId.Value, ct);

                if (nestedProfile is not null)
                {
                    var nestedHostIds = nestedProfile.JumpHops.Select(h => h.JumpHostId).ToHashSet();
                    if (nestedHostIds.Contains(targetHostId))
                    {
                        issues.Add(
                            $"Jump host '{jumpHost.DisplayName ?? jumpHost.Hostname}' has a nested proxy chain " +
                            $"that contains the target host, which would create a circular reference.");
                    }
                }
            }
        }

        if (issues.Count > 0)
        {
            _logger.LogWarning("ProxyJump profile validation for host {HostId} failed: {Issues}",
                targetHostId, string.Join("; ", issues));

            return ProxyJumpValidationResult.Failure(
                "Profile validation for this host failed.",
                issues);
        }

        return ProxyJumpValidationResult.Success();
    }

    /// <inheritdoc />
    public string BuildChainDisplayString(ProxyJumpProfile profile, string? targetHostName = null)
    {
        var sb = new StringBuilder();
        sb.Append("You");

        var orderedHops = profile.JumpHops.OrderBy(h => h.SortOrder).ToList();

        foreach (var hop in orderedHops)
        {
            sb.Append(" → ");

            var displayName = hop.JumpHost?.DisplayName;
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = hop.JumpHost?.Hostname ?? $"Host {hop.JumpHostId:N}";
            }

            sb.Append(displayName);
        }

        sb.Append(" → ");
        sb.Append(string.IsNullOrWhiteSpace(targetHostName) ? "[Target]" : targetHostName);

        return sb.ToString();
    }
}
