using Microsoft.Extensions.Logging;
using Renci.SshNet;

namespace SshManager.Terminal.Services;

/// <summary>
/// Static utility class for configuring SSH algorithm preferences.
/// Reorders algorithms to prioritize modern, secure options for better server compatibility.
/// </summary>
public static class AlgorithmConfigurator
{
    /// <summary>
    /// Preferred key exchange algorithms in order of preference.
    /// Modern algorithms like curve25519 are prioritized.
    /// </summary>
    private static readonly string[] PreferredKeyExchangeAlgorithms =
    {
        "curve25519-sha256",
        "curve25519-sha256@libssh.org",
        "ecdh-sha2-nistp521",
        "ecdh-sha2-nistp384",
        "ecdh-sha2-nistp256",
        "diffie-hellman-group-exchange-sha256",
        "diffie-hellman-group16-sha512",
        "diffie-hellman-group14-sha256",
        "diffie-hellman-group14-sha1",
        "diffie-hellman-group-exchange-sha1",
        "diffie-hellman-group1-sha1"
    };

    /// <summary>
    /// Configures key exchange, encryption, and MAC algorithms to maximize server compatibility.
    /// SSH.NET may not offer all algorithms by default, especially newer ones.
    /// </summary>
    /// <param name="connInfo">The SSH.NET ConnectionInfo to configure.</param>
    /// <param name="logger">Optional logger for diagnostic output.</param>
    public static void ConfigureAlgorithms(ConnectionInfo connInfo, ILogger? logger = null)
    {
        // Log the available algorithms for debugging
        logger?.LogDebug("Available key exchange algorithms: {Algorithms}",
            string.Join(", ", connInfo.KeyExchangeAlgorithms.Keys));
        logger?.LogDebug("Available encryption algorithms: {Algorithms}",
            string.Join(", ", connInfo.Encryptions.Keys));
        logger?.LogDebug("Available host key algorithms: {Algorithms}",
            string.Join(", ", connInfo.HostKeyAlgorithms.Keys));
        logger?.LogDebug("Available HMAC algorithms: {Algorithms}",
            string.Join(", ", connInfo.HmacAlgorithms.Keys));

        // SSH.NET 2024.x should support modern algorithms, but some servers require
        // specific algorithm ordering or may reject certain older algorithms.
        // We reorder to prioritize modern, secure algorithms.

        // Reorder key exchange algorithms to prioritize modern ones
        ReorderAlgorithms(connInfo.KeyExchangeAlgorithms, PreferredKeyExchangeAlgorithms);
    }

    /// <summary>
    /// Reorders algorithms in the dictionary to match preferred order.
    /// Algorithms not in the preferred list are kept at the end in their original order.
    /// </summary>
    /// <typeparam name="T">The type of algorithm factory/handler.</typeparam>
    /// <param name="algorithms">The algorithm dictionary to reorder.</param>
    /// <param name="preferredOrder">Array of algorithm names in preferred order.</param>
    internal static void ReorderAlgorithms<T>(IDictionary<string, T> algorithms, string[] preferredOrder)
    {
        // Create a copy of current algorithms
        var currentAlgorithms = algorithms.ToList();

        // Clear and re-add in preferred order
        algorithms.Clear();

        // First add algorithms in preferred order
        foreach (var name in preferredOrder)
        {
            var match = currentAlgorithms.FirstOrDefault(a =>
                a.Key.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (match.Value != null)
            {
                algorithms[match.Key] = match.Value;
                currentAlgorithms.Remove(match);
            }
        }

        // Then add remaining algorithms that weren't in the preferred list
        foreach (var kvp in currentAlgorithms)
        {
            algorithms[kvp.Key] = kvp.Value;
        }
    }
}
