#if DEBUG
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace SshManager.App.Services.Testing;

/// <summary>
/// Named pipe server for test automation.
/// Listens for JSON commands and returns JSON responses.
/// </summary>
public class TestServer : ITestServer
{
    private readonly ITestCommandHandler _commandHandler;
    private readonly ILogger<TestServer> _logger;
    private readonly CancellationTokenSource _cts = new();
    private readonly ManualResetEventSlim _pipeReadyEvent = new(false);
    private Task? _listenerTask;
    private bool _disposed;

    public const string DefaultPipeName = "SshManagerTestPipe";

    public bool IsRunning { get; private set; }
    public string PipeName { get; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public TestServer(
        ITestCommandHandler commandHandler,
        ILogger<TestServer>? logger = null,
        string? pipeName = null)
    {
        _commandHandler = commandHandler;
        _logger = logger ?? NullLogger<TestServer>.Instance;
        PipeName = pipeName ?? DefaultPipeName;
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            _logger.LogWarning("Test server is already running");
            return Task.CompletedTask;
        }

        _logger.LogInformation("Starting test server on pipe: {PipeName}", PipeName);
        IsRunning = true;

        // Link external cancellation token
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token, cancellationToken);

        _listenerTask = Task.Run(() => ListenAsync(linkedCts.Token), linkedCts.Token);

        // Wait for the pipe to be ready (with timeout)
        var ready = _pipeReadyEvent.Wait(TimeSpan.FromSeconds(5));
        if (!ready)
        {
            _logger.LogWarning("Timeout waiting for pipe to be ready");
        }

        _logger.LogInformation("Test server started successfully");
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        if (!IsRunning)
        {
            return;
        }

        _logger.LogInformation("Stopping test server");
        IsRunning = false;

        try
        {
            _cts.Cancel();

            if (_listenerTask != null)
            {
                // Give it a moment to complete gracefully
                await Task.WhenAny(_listenerTask, Task.Delay(2000));
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during test server shutdown");
        }

        _logger.LogInformation("Test server stopped");
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        bool firstPipe = true;

        while (!cancellationToken.IsCancellationRequested && IsRunning)
        {
            NamedPipeServerStream? pipeServer = null;
            try
            {
                pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                // Signal that the pipe is ready (only for the first pipe)
                if (firstPipe)
                {
                    firstPipe = false;
                    _pipeReadyEvent.Set();
                }

                _logger.LogDebug("Waiting for connection on pipe: {PipeName}", PipeName);

                await pipeServer.WaitForConnectionAsync(cancellationToken);
                _logger.LogDebug("Client connected");

                // Handle the connection in a separate task to allow new connections
                _ = HandleConnectionAsync(pipeServer, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Normal shutdown
                pipeServer?.Dispose();
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in pipe listener");
                pipeServer?.Dispose();

                // Brief delay before retry
                try
                {
                    await Task.Delay(100, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleConnectionAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        try
        {
            // Use UTF8 without BOM to avoid blocking issues on named pipes
            var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            using var reader = new StreamReader(pipeServer, utf8NoBom, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            using var writer = new StreamWriter(pipeServer, utf8NoBom, bufferSize: 1024, leaveOpen: true);
            writer.AutoFlush = true;

            while (pipeServer.IsConnected && !cancellationToken.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cancellationToken);

                if (string.IsNullOrEmpty(line))
                {
                    break;
                }

                _logger.LogDebug("Received command: {Command}", line.Length > 200 ? line[..200] + "..." : line);

                var response = await ProcessCommandAsync(line, cancellationToken);
                var responseJson = JsonSerializer.Serialize(response, JsonOptions);

                await writer.WriteLineAsync(responseJson);
                _logger.LogDebug("Sent response: success={Success}", response.Success);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown
        }
        catch (IOException)
        {
            // Client disconnected - expected behavior
            _logger.LogDebug("Client disconnected");
        }
        catch (ObjectDisposedException)
        {
            // Pipe was closed - expected during cleanup
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling connection");
        }
        finally
        {
            try
            {
                pipeServer.Dispose();
            }
            catch
            {
                // Ignore disposal errors
            }
        }
    }

    private async Task<TestResponse> ProcessCommandAsync(string commandJson, CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        TestResponse response;

        try
        {
            var command = JsonSerializer.Deserialize<TestCommand>(commandJson, JsonOptions);
            if (command == null)
            {
                response = TestResponse.Fail("Failed to parse command");
            }
            else
            {
                response = await _commandHandler.HandleCommandAsync(command, cancellationToken);
            }
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid JSON command received");
            response = TestResponse.Fail($"Invalid JSON: {ex.Message}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing command");
            response = TestResponse.Fail($"Internal error: {ex.Message}");
        }

        sw.Stop();
        response.ExecutionTimeMs = sw.ElapsedMilliseconds;
        return response;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopAsync().GetAwaiter().GetResult();
        _cts.Dispose();
        _pipeReadyEvent.Dispose();
    }
}
#endif
