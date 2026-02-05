using System.Windows;
using Microsoft.Extensions.Logging;
using SshManager.App.Views.Dialogs;
using SshManager.Core.Models;
using SshManager.Data.Repositories;
using SshManager.Terminal.Services;

namespace SshManager.App.Services;

/// <summary>
/// Helper class for creating host key verification callbacks.
/// Centralizes the logic for verifying SSH host keys and managing fingerprints.
/// </summary>
public static class HostKeyVerificationHelper
{
    /// <summary>
    /// Creates a host key verification callback for the specified host.
    /// Supports multiple key algorithms per host (RSA, ED25519, ECDSA, etc.).
    /// </summary>
    /// <param name="hostId">The ID of the host entry being connected to.</param>
    /// <param name="fingerprintRepo">Repository for managing host fingerprints.</param>
    /// <param name="logger">Logger for diagnostics.</param>
    /// <returns>A callback function that verifies host keys and stores accepted fingerprints.</returns>
    public static HostKeyVerificationCallback CreateCallback(
        Guid hostId,
        IHostFingerprintRepository fingerprintRepo,
        ILogger logger)
    {
        return async (hostname, port, algorithm, fingerprint, keyBytes) =>
        {
            logger.LogDebug("Verifying host key for {Hostname}:{Port} - {Algorithm}", hostname, port, algorithm);

            // Look up fingerprint by host AND algorithm (supports multiple key types per host)
            var existingFingerprint = await fingerprintRepo.GetByHostAndAlgorithmAsync(hostId, algorithm);

            // Check if fingerprint matches for this specific algorithm
            if (existingFingerprint != null && existingFingerprint.Fingerprint == fingerprint)
            {
                // Fingerprint matches - update last seen and trust
                await fingerprintRepo.UpdateLastSeenAsync(existingFingerprint.Id);
                logger.LogDebug("Host key verified - fingerprint matches stored value for {Algorithm}", algorithm);
                return true;
            }

            // Check if application is available before showing dialog
            if (Application.Current?.Dispatcher == null)
            {
                logger.LogWarning("Cannot show host key verification dialog - application is shutting down");
                return false;
            }

            // Show verification dialog on UI thread
            // Pass existingFingerprint to show if key changed for this algorithm
            var accepted = await Application.Current.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new HostKeyVerificationDialog();
                dialog.Owner = Application.Current.MainWindow;
                dialog.Initialize(hostname, port, algorithm, fingerprint, existingFingerprint);
                dialog.ShowDialog();
                return dialog.IsAccepted;
            });

            if (accepted)
            {
                if (existingFingerprint != null)
                {
                    // Update existing fingerprint for this algorithm (key changed)
                    existingFingerprint.Fingerprint = fingerprint;
                    existingFingerprint.LastSeen = DateTimeOffset.UtcNow;
                    existingFingerprint.IsTrusted = true;
                    await fingerprintRepo.UpdateAsync(existingFingerprint);
                    logger.LogInformation("Updated host key fingerprint for {Hostname}:{Port} ({Algorithm})", hostname, port, algorithm);
                }
                else
                {
                    // Add new fingerprint for this algorithm
                    // This allows storing multiple algorithms per host (RSA, ED25519, ECDSA, etc.)
                    var newFingerprint = new HostFingerprint
                    {
                        HostId = hostId,
                        Algorithm = algorithm,
                        Fingerprint = fingerprint,
                        FirstSeen = DateTimeOffset.UtcNow,
                        LastSeen = DateTimeOffset.UtcNow,
                        IsTrusted = true
                    };
                    await fingerprintRepo.AddAsync(newFingerprint);
                    logger.LogInformation("Stored new host key fingerprint for {Hostname}:{Port} ({Algorithm})", hostname, port, algorithm);
                }
            }
            else
            {
                logger.LogWarning("Host key rejected by user for {Hostname}:{Port} ({Algorithm})", hostname, port, algorithm);
            }

            return accepted;
        };
    }
}
