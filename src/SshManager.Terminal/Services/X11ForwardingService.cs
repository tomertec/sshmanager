using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.Terminal.Services;

/// <summary>
/// Service implementation for managing X11 forwarding on SSH connections.
/// </summary>
/// <remarks>
/// <para>
/// This service provides functionality for:
/// - Detecting running X11 servers on Windows (VcXsrv, Xming, X410, Cygwin/X)
/// - Launching X servers with appropriate arguments
/// - Generating DISPLAY environment variable values
/// </para>
/// <para>
/// <b>Detection Strategy:</b> The service checks for X servers using multiple methods:
/// <list type="number">
/// <item>Process detection: Scans for known X server process names</item>
/// <item>Port detection: Checks if TCP port 6000+displayNumber is listening</item>
/// </list>
/// </para>
/// <para>
/// <b>Supported X Servers:</b>
/// - VcXsrv (recommended)
/// - Xming
/// - X410
/// - Cygwin/X (XWin)
/// </para>
/// </remarks>
public sealed class X11ForwardingService : IX11ForwardingService
{
    private readonly ILogger<X11ForwardingService> _logger;

    /// <summary>
    /// Known X server process names (case-insensitive).
    /// </summary>
    private static readonly string[] KnownXServerProcesses =
    {
        "vcxsrv",
        "xming",
        "x410",
        "xwin",
        "xlaunch"
    };

    /// <summary>
    /// Mapping of process names to X server display names.
    /// </summary>
    private static readonly Dictionary<string, string> ProcessToServerName = new(StringComparer.OrdinalIgnoreCase)
    {
        { "vcxsrv", "VcXsrv" },
        { "xming", "Xming" },
        { "x410", "X410" },
        { "xwin", "Cygwin/X" },
        { "xlaunch", "VcXsrv (XLaunch)" }
    };

    /// <summary>
    /// Default arguments for different X server types.
    /// </summary>
    private static readonly Dictionary<string, string> DefaultServerArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        { "vcxsrv", "-multiwindow -clipboard -ac" },
        { "xming", "-multiwindow -clipboard" },
        { "x410", "" }, // X410 uses modern Windows app model, minimal args needed
        { "xwin", "-multiwindow -clipboard" }
    };

    /// <summary>
    /// Initializes a new instance of the <see cref="X11ForwardingService"/> class.
    /// </summary>
    /// <param name="logger">Logger for diagnostic output.</param>
    public X11ForwardingService(ILogger<X11ForwardingService>? logger = null)
    {
        _logger = logger ?? NullLogger<X11ForwardingService>.Instance;
    }

    /// <inheritdoc />
    public async Task<X11ServerStatus> DetectXServerAsync(CancellationToken ct = default)
    {
        _logger.LogDebug("Detecting X11 server on local machine");

        return await Task.Run(() =>
        {
            // First, check for running X server processes
            var detectedServerName = DetectRunningXServer();
            if (detectedServerName != null)
            {
                _logger.LogInformation("Detected running X server: {ServerName}", detectedServerName);

                // Check if port 6000 is listening
                var displayNumber = DetectDisplayNumber();
                var displayAddress = GetDisplayValue(displayNumber);

                return new X11ServerStatus(
                    IsAvailable: true,
                    DisplayAddress: displayAddress,
                    DisplayNumber: displayNumber,
                    ServerName: detectedServerName);
            }

            // Check if port 6000 is listening even without a recognized process
            for (int display = 0; display <= 10; display++)
            {
                int port = 6000 + display;
                if (IsPortListening(port))
                {
                    _logger.LogInformation("Detected X server on port {Port} (display {Display})", port, display);
                    return new X11ServerStatus(
                        IsAvailable: true,
                        DisplayAddress: GetDisplayValue(display),
                        DisplayNumber: display,
                        ServerName: "Unknown X Server");
                }
            }

            _logger.LogDebug("No X server detected");
            return new X11ServerStatus(
                IsAvailable: false,
                DisplayAddress: GetDisplayValue(0),
                DisplayNumber: 0,
                ServerName: null);

        }, ct);
    }

    /// <inheritdoc />
    public async Task<bool> LaunchXServerAsync(string path, int displayNumber = 0, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            _logger.LogWarning("X server path is null or empty");
            return false;
        }

        if (!File.Exists(path))
        {
            _logger.LogError("X server executable not found at path: {Path}", path);
            return false;
        }

        if (displayNumber < 0 || displayNumber > 63)
        {
            _logger.LogError("Invalid display number {DisplayNumber}. Must be between 0 and 63", displayNumber);
            return false;
        }

        _logger.LogInformation("Launching X server from {Path} with display {DisplayNumber}", path, displayNumber);

        try
        {
            var arguments = GetXServerArguments(path, displayNumber);
            _logger.LogDebug("X server arguments: {Arguments}", arguments);

            var startInfo = new ProcessStartInfo
            {
                FileName = path,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = false,
                WindowStyle = ProcessWindowStyle.Normal
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogError("Failed to start X server process");
                return false;
            }

            _logger.LogDebug("X server process started with PID {ProcessId}", process.Id);

            // Wait briefly for the server to start
            await Task.Delay(2000, ct);

            // Verify the server is running
            if (process.HasExited)
            {
                _logger.LogError("X server process exited immediately with code {ExitCode}", process.ExitCode);
                return false;
            }

            // Check if the expected port is now listening
            var expectedPort = 6000 + displayNumber;
            var isListening = IsPortListening(expectedPort);

            if (isListening)
            {
                _logger.LogInformation("X server successfully launched on display {DisplayNumber}", displayNumber);
                return true;
            }
            else
            {
                _logger.LogWarning("X server process is running but port {Port} is not listening", expectedPort);
                // Return true anyway since the process started - some X servers may take longer to open the port
                return true;
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Failed to launch X server from {Path}", path);
            return false;
        }
    }

    /// <inheritdoc />
    public string GetDisplayValue(int displayNumber)
    {
        if (displayNumber < 0 || displayNumber > 63)
        {
            throw new ArgumentOutOfRangeException(nameof(displayNumber), displayNumber,
                "Display number must be between 0 and 63");
        }

        return $"localhost:{displayNumber}.0";
    }

    /// <summary>
    /// Checks if a TCP port is currently listening.
    /// </summary>
    /// <param name="port">The port number to check.</param>
    /// <returns>True if the port is listening, false otherwise.</returns>
    private bool IsPortListening(int port)
    {
        try
        {
            using var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return false; // Port is available, so nothing is listening
        }
        catch (SocketException)
        {
            return true; // Port is in use (something is listening)
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking if port {Port} is listening", port);
            return false;
        }
    }

    /// <summary>
    /// Detects running X server processes.
    /// </summary>
    /// <returns>The name of the detected X server, or null if none found.</returns>
    private string? DetectRunningXServer()
    {
        try
        {
            var allProcesses = Process.GetProcesses();

            foreach (var processName in KnownXServerProcesses)
            {
                var matchingProcess = allProcesses.FirstOrDefault(p =>
                    p.ProcessName.Equals(processName, StringComparison.OrdinalIgnoreCase));

                if (matchingProcess != null)
                {
                    _logger.LogDebug("Found running X server process: {ProcessName} (PID: {ProcessId})",
                        matchingProcess.ProcessName, matchingProcess.Id);

                    // Return the friendly server name
                    if (ProcessToServerName.TryGetValue(matchingProcess.ProcessName, out var serverName))
                    {
                        return serverName;
                    }

                    return matchingProcess.ProcessName;
                }
            }

            _logger.LogDebug("No known X server processes found");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error detecting running X server processes");
            return null;
        }
    }

    /// <summary>
    /// Detects the display number by checking which X11 ports are listening.
    /// </summary>
    /// <returns>The display number of the first listening X11 port, or 0 if none found.</returns>
    private int DetectDisplayNumber()
    {
        // Check display numbers 0-10 (ports 6000-6010)
        for (int display = 0; display <= 10; display++)
        {
            int port = 6000 + display;
            if (IsPortListening(port))
            {
                _logger.LogDebug("Detected X11 port {Port} listening (display {Display})", port, display);
                return display;
            }
        }

        _logger.LogDebug("No X11 ports found listening, defaulting to display 0");
        return 0;
    }

    /// <summary>
    /// Gets the appropriate command-line arguments for an X server.
    /// </summary>
    /// <param name="serverPath">Path to the X server executable.</param>
    /// <param name="displayNumber">Display number to use.</param>
    /// <returns>Command-line arguments for the X server.</returns>
    private string GetXServerArguments(string serverPath, int displayNumber)
    {
        var fileName = Path.GetFileNameWithoutExtension(serverPath);

        // Check if we have default arguments for this server type
        if (DefaultServerArguments.TryGetValue(fileName, out var args))
        {
            // VcXsrv and Xming support the :{display} syntax
            if (fileName.Equals("vcxsrv", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("xming", StringComparison.OrdinalIgnoreCase) ||
                fileName.Equals("xwin", StringComparison.OrdinalIgnoreCase))
            {
                // Add display number to arguments
                return $":{displayNumber} {args}".Trim();
            }

            return args;
        }

        // Unknown X server - use minimal arguments
        _logger.LogDebug("Unknown X server type '{FileName}', using minimal arguments", fileName);
        return $":{displayNumber}";
    }
}
