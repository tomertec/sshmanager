using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Renci.SshNet;

namespace SshManager.Terminal.Services;

/// <summary>
/// Handles terminal resize operations with graceful fallback.
/// WARNING: Uses reflection to access SSH.NET internals.
/// This implementation depends on SSH.NET version 2024.2.0 internal structure.
/// </summary>
public interface ITerminalResizeService
{
    /// <summary>
    /// Attempts to resize the terminal.
    /// </summary>
    /// <param name="shellStream">The SSH shell stream.</param>
    /// <param name="columns">Number of columns.</param>
    /// <param name="rows">Number of rows.</param>
    /// <returns>True if resize succeeded, false otherwise.</returns>
    bool TryResize(ShellStream shellStream, uint columns, uint rows);

    /// <summary>
    /// Indicates whether terminal resize is supported with the current SSH.NET version.
    /// </summary>
    bool IsResizeSupported { get; }
}

public class TerminalResizeService : ITerminalResizeService
{
    private readonly ILogger<TerminalResizeService> _logger;
    private readonly bool _reflectionApiAvailable;
    private readonly MethodInfo? _sendWindowChangeMethod;
    private readonly FieldInfo? _channelField;

    // SSH.NET version this implementation was tested against
    private const string TestedSshNetVersion = "2024.2.0";

    public TerminalResizeService(ILogger<TerminalResizeService>? logger = null)
    {
        _logger = logger ?? NullLogger<TerminalResizeService>.Instance;

        // Validate at startup rather than per-resize
        (_reflectionApiAvailable, _channelField, _sendWindowChangeMethod) = ValidateReflectionApi();

        if (!_reflectionApiAvailable)
        {
            _logger.LogWarning(
                "Terminal resize via reflection is not available. " +
                "This may be due to an SSH.NET version change. Tested version: {Version}",
                TestedSshNetVersion);
        }
    }

    public bool IsResizeSupported => _reflectionApiAvailable;

    public bool TryResize(ShellStream shellStream, uint columns, uint rows)
    {
        if (!_reflectionApiAvailable)
        {
            _logger.LogDebug("Resize requested but reflection API not available");
            return false;
        }

        try
        {
            var channel = _channelField!.GetValue(shellStream);
            if (channel == null)
            {
                _logger.LogWarning("Channel instance is null");
                return false;
            }

            // SendWindowChangeRequest(columns, rows, width_pixels, height_pixels)
            _sendWindowChangeMethod!.Invoke(channel, new object[] { columns, rows, 0u, 0u });

            _logger.LogDebug("Terminal resized to {Cols}x{Rows}", columns, rows);
            return true;
        }
        catch (TargetInvocationException ex)
        {
            _logger.LogError(ex.InnerException ?? ex, "Failed to send window change request");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error during terminal resize");
            return false;
        }
    }

    private (bool available, FieldInfo? channelField, MethodInfo? method) ValidateReflectionApi()
    {
        try
        {
            var shellStreamType = typeof(ShellStream);

            // Find _channel field
            var channelField = shellStreamType.GetField("_channel",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (channelField == null)
            {
                _logger.LogDebug("_channel field not found in ShellStream");
                return (false, null, null);
            }

            // Get channel type and find SendWindowChangeRequest method
            var channelType = channelField.FieldType;
            var method = channelType.GetMethod("SendWindowChangeRequest",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(uint), typeof(uint), typeof(uint), typeof(uint) },
                null);

            if (method == null)
            {
                _logger.LogDebug("SendWindowChangeRequest method not found on channel type {Type}",
                    channelType.Name);
                return (false, null, null);
            }

            _logger.LogInformation("Terminal resize API validated successfully");
            return (true, channelField, method);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate reflection API for terminal resize");
            return (false, null, null);
        }
    }
}
