using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using SshManager.Terminal.Services;

namespace SshManager.Terminal.Controls;

/// <summary>
/// WPF UserControl that hosts a WebView2-based terminal using xterm.js.
/// Provides a modern web-based terminal interface with full VT100/ANSI support.
/// </summary>
public partial class WebTerminalControl : UserControl, IDisposable
{
    private readonly ILogger<WebTerminalControl> _logger;
    private readonly ILoggerFactory? _loggerFactory;
    private WebTerminalBridge? _bridge;
    private bool _disposed;
    private bool _isInitialized;

    /// <summary>
    /// Gets the bridge that manages communication between C# and the JavaScript terminal.
    /// </summary>
    public WebTerminalBridge? Bridge => _bridge;

    /// <summary>
    /// Gets whether the terminal is ready to receive commands.
    /// </summary>
    public bool IsTerminalReady => _bridge?.IsReady ?? false;

    /// <summary>
    /// Gets the current number of columns in the terminal.
    /// </summary>
    public int Columns => _bridge?.Columns ?? 80;

    /// <summary>
    /// Gets the current number of rows in the terminal.
    /// </summary>
    public int Rows => _bridge?.Rows ?? 24;

    /// <summary>
    /// Event raised when user types in the terminal.
    /// </summary>
    public event Action<string>? InputReceived;

    /// <summary>
    /// Event raised when the terminal is initialized and ready.
    /// </summary>
    public event Action? TerminalReady;

    /// <summary>
    /// Event raised when the terminal is resized.
    /// Parameters are (columns, rows).
    /// </summary>
    public event Action<int, int>? TerminalResized;

    /// <summary>
    /// Default constructor for XAML.
    /// </summary>
    public WebTerminalControl() : this(null, null)
    {
    }

    /// <summary>
    /// Constructor with optional logger.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    public WebTerminalControl(ILogger<WebTerminalControl>? logger)
        : this(logger, null)
    {
    }

    /// <summary>
    /// Constructor with optional logger and logger factory.
    /// </summary>
    /// <param name="logger">Optional logger for diagnostics.</param>
    /// <param name="loggerFactory">Optional logger factory for creating child component loggers.</param>
    public WebTerminalControl(ILogger<WebTerminalControl>? logger, ILoggerFactory? loggerFactory)
    {
        _logger = logger ?? NullLogger<WebTerminalControl>.Instance;
        _loggerFactory = loggerFactory;
        InitializeComponent();
    }

    /// <summary>
    /// Initializes the WebView2 control and loads the terminal HTML.
    /// </summary>
    public async Task InitializeAsync()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WebTerminalControl));
        }

        if (_isInitialized)
        {
            _logger.LogWarning("InitializeAsync called but control already initialized");
            return;
        }

        try
        {
            _logger.LogDebug("Initializing WebTerminalControl");

            // Ensure WebView2 runtime is initialized
            await WebViewControl.EnsureCoreWebView2Async();

            // Create and initialize the bridge with correctly typed logger
            var bridgeLogger = _loggerFactory?.CreateLogger<WebTerminalBridge>()
                ?? NullLogger<WebTerminalBridge>.Instance;
            _bridge = new WebTerminalBridge(bridgeLogger);
            await _bridge.InitializeAsync(WebViewControl);

            // Forward bridge events
            _bridge.InputReceived += OnBridgeInputReceived;
            _bridge.TerminalReady += OnBridgeTerminalReady;
            _bridge.TerminalResized += OnBridgeTerminalResized;

            // Load terminal HTML
            await LoadTerminalHtmlAsync();

            _isInitialized = true;
            _logger.LogInformation("WebTerminalControl initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize WebTerminalControl");
            throw;
        }
    }

    /// <summary>
    /// Sends data to the terminal for display.
    /// </summary>
    /// <param name="data">The text data to write to the terminal.</param>
    public void WriteData(string data)
    {
        if (_disposed)
        {
            _logger.LogWarning("WriteData called on disposed control");
            return;
        }

        _bridge?.WriteData(data);
    }

    /// <summary>
    /// Sets the terminal theme colors.
    /// </summary>
    /// <param name="theme">Dictionary containing xterm.js theme properties.</param>
    public void SetTheme(Dictionary<string, string> theme)
    {
        if (_disposed)
        {
            _logger.LogWarning("SetTheme called on disposed control");
            return;
        }

        if (theme == null)
        {
            throw new ArgumentNullException(nameof(theme));
        }

        _bridge?.SetTheme(theme);
    }

    /// <summary>
    /// Focuses the terminal for keyboard input.
    /// </summary>
    public new void Focus()
    {
        if (_disposed)
        {
            return;
        }

        _bridge?.Focus();
    }

    /// <summary>
    /// Clears the terminal display.
    /// </summary>
    public void Clear()
    {
        if (_disposed)
        {
            return;
        }

        _bridge?.Clear();
    }

    private async Task LoadTerminalHtmlAsync()
    {
        try
        {
            // Get the embedded resource stream
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "SshManager.Terminal.Resources.Terminal.terminal.html";

            // Debug: List all embedded resources to verify the correct name
            var resourceNames = assembly.GetManifestResourceNames();
            _logger.LogDebug("Available embedded resources: {Resources}", string.Join(", ", resourceNames));

            // Find the terminal.html resource (case-insensitive search)
            var actualResourceName = resourceNames.FirstOrDefault(r =>
                r.EndsWith("terminal.html", StringComparison.OrdinalIgnoreCase));

            if (actualResourceName == null)
            {
                throw new FileNotFoundException(
                    $"Embedded resource 'terminal.html' not found. Available resources: {string.Join(", ", resourceNames)}");
            }

            _logger.LogDebug("Loading terminal HTML from resource: {ResourceName}", actualResourceName);

            using var stream = assembly.GetManifestResourceStream(actualResourceName);
            if (stream == null)
            {
                throw new FileNotFoundException($"Failed to load embedded resource: {actualResourceName}");
            }

            using var reader = new StreamReader(stream);
            var html = await reader.ReadToEndAsync();

            // Navigate to the HTML content
            WebViewControl.NavigateToString(html);
            _logger.LogDebug("Terminal HTML loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load terminal HTML");
            throw;
        }
    }

    private void UserControl_Loaded(object sender, RoutedEventArgs e)
    {
        _logger.LogDebug("WebTerminalControl loaded");
    }

    private void UserControl_Unloaded(object sender, RoutedEventArgs e)
    {
        // Note: Do NOT dispose here. WPF fires Unloaded when switching tabs,
        // which would destroy the terminal state. Disposal should only happen
        // when the session is explicitly closed via Dispose().
        _logger.LogDebug("WebTerminalControl unloaded (keeping state)");
    }

    private void OnBridgeInputReceived(string data)
    {
        InputReceived?.Invoke(data);
    }

    private void OnBridgeTerminalReady()
    {
        TerminalReady?.Invoke();
    }

    private void OnBridgeTerminalResized(int cols, int rows)
    {
        TerminalResized?.Invoke(cols, rows);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _logger.LogDebug("Disposing WebTerminalControl");

        if (_bridge != null)
        {
            _bridge.InputReceived -= OnBridgeInputReceived;
            _bridge.TerminalReady -= OnBridgeTerminalReady;
            _bridge.TerminalResized -= OnBridgeTerminalResized;
            _bridge.Dispose();
            _bridge = null;
        }

        if (WebViewControl?.CoreWebView2 != null)
        {
            try
            {
                WebViewControl.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing WebView2");
            }
        }
    }
}
