#if DEBUG
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.App.ViewModels;
using SshManager.App.Views.Windows;
using SshManager.Core.Models;
using SshManager.Terminal;

namespace SshManager.App.Services.Testing;

/// <summary>
/// Handles test automation commands by interacting with the WPF UI.
/// All UI operations are dispatched to the UI thread.
/// </summary>
public class TestCommandHandler : ITestCommandHandler
{
    private readonly MainWindow _mainWindow;
    private readonly MainWindowViewModel _viewModel;
    private readonly ITerminalSessionManager _sessionManager;
    private readonly ILogger<TestCommandHandler> _logger;

    public TestCommandHandler(
        MainWindow mainWindow,
        MainWindowViewModel viewModel,
        ITerminalSessionManager sessionManager,
        ILogger<TestCommandHandler>? logger = null)
    {
        _mainWindow = mainWindow;
        _viewModel = viewModel;
        _sessionManager = sessionManager;
        _logger = logger ?? NullLogger<TestCommandHandler>.Instance;
    }

    public async Task<TestResponse> HandleCommandAsync(TestCommand command, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Handling command: {Action}", command.Action);

        try
        {
            return command.Action.ToLowerInvariant() switch
            {
                TestActions.Ping => HandlePing(),
                TestActions.GetState => await HandleGetStateAsync(),
                TestActions.Screenshot => await HandleScreenshotAsync(),
                TestActions.ScreenshotElement => await HandleScreenshotElementAsync(command),
                TestActions.ListElements => await HandleListElementsAsync(command),
                TestActions.GetElement => await HandleGetElementAsync(command),
                TestActions.FindElement => await HandleFindElementAsync(command),
                TestActions.GetVisualTree => await HandleGetVisualTreeAsync(command),
                TestActions.Click => await HandleClickAsync(command),
                TestActions.DoubleClick => await HandleDoubleClickAsync(command),
                TestActions.RightClick => await HandleRightClickAsync(command),
                TestActions.Type => await HandleTypeAsync(command),
                TestActions.Clear => await HandleClearAsync(command),
                TestActions.Focus => await HandleFocusAsync(command),
                TestActions.SendKeys => await HandleSendKeysAsync(command),
                TestActions.GetProperty => await HandleGetPropertyAsync(command),
                TestActions.SetProperty => await HandleSetPropertyAsync(command),
                TestActions.GetText => await HandleGetTextAsync(command),
                TestActions.InvokeCommand => await HandleInvokeCommandAsync(command),
                TestActions.InvokeButton => await HandleInvokeButtonAsync(command),
                TestActions.GetHosts => HandleGetHosts(),
                TestActions.SelectHost => await HandleSelectHostAsync(command),
                TestActions.ConnectHost => await HandleConnectHostAsync(command),
                TestActions.DisconnectSession => await HandleDisconnectSessionAsync(command),
                TestActions.GetSessions => HandleGetSessions(),
                TestActions.SelectSession => await HandleSelectSessionAsync(command),
                TestActions.SendToTerminal => await HandleSendToTerminalAsync(command),
                TestActions.GetTerminalOutput => await HandleGetTerminalOutputAsync(command),
                TestActions.Wait => await HandleWaitAsync(command, cancellationToken),
                TestActions.WaitForElement => await HandleWaitForElementAsync(command, cancellationToken),
                TestActions.OpenDialog => await HandleOpenDialogAsync(command),
                TestActions.CloseDialog => await HandleCloseDialogAsync(command),
                TestActions.GetDialogs => await HandleGetDialogsAsync(),
                TestActions.SelectTab => await HandleSelectTabAsync(command),
                _ => TestResponse.Fail($"Unknown action: {command.Action}")
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling command {Action}", command.Action);
            return TestResponse.Fail($"Error: {ex.Message}");
        }
    }

    #region Connection & Health

    private TestResponse HandlePing()
    {
        return TestResponse.Ok(new { message = "pong", version = "1.0.0", pipeName = TestServer.DefaultPipeName });
    }

    private async Task<TestResponse> HandleGetStateAsync()
    {
        return await RunOnUIThreadAsync(() =>
        {
            var state = new AppStateInfo
            {
                IsReady = _mainWindow.IsLoaded,
                MainWindowTitle = _mainWindow.Title,
                HostCount = _viewModel.HostManagement.Hosts.Count,
                SessionCount = _viewModel.Session.Sessions.Count,
                ActiveSessionId = _viewModel.Session.CurrentSession?.Id,
                SelectedHostId = _viewModel.HostManagement.SelectedHost?.Id,
                IsConnecting = _viewModel.Session.IsConnecting,
                OpenDialogs = GetOpenDialogNames()
            };
            return TestResponse.Ok(state);
        });
    }

    #endregion

    #region Screenshots

    private async Task<TestResponse> HandleScreenshotAsync()
    {
        return await RunOnUIThreadAsync(() =>
        {
            var screenshot = CaptureWindow(_mainWindow);
            return TestResponse.WithScreenshot(screenshot);
        });
    }

    private async Task<TestResponse> HandleScreenshotElementAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Target element is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            var element = FindElementByIdentifier(command.Target);
            if (element == null)
            {
                return TestResponse.Fail($"Element not found: {command.Target}");
            }

            var screenshot = CaptureElement(element);
            return TestResponse.WithScreenshot(screenshot);
        });
    }

    private string CaptureWindow(Window window)
    {
        var bounds = new Rect(
            window.Left,
            window.Top,
            window.ActualWidth,
            window.ActualHeight);

        // Account for DPI scaling
        var source = PresentationSource.FromVisual(window);
        if (source?.CompositionTarget != null)
        {
            var dpiX = source.CompositionTarget.TransformToDevice.M11;
            var dpiY = source.CompositionTarget.TransformToDevice.M22;
            bounds = new Rect(
                bounds.X * dpiX,
                bounds.Y * dpiY,
                bounds.Width * dpiX,
                bounds.Height * dpiY);
        }

        return CaptureScreenRegion(bounds);
    }

    private string CaptureElement(FrameworkElement element)
    {
        var renderTarget = new RenderTargetBitmap(
            (int)element.ActualWidth,
            (int)element.ActualHeight,
            96, 96,
            PixelFormats.Pbgra32);

        renderTarget.Render(element);

        using var stream = new MemoryStream();
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderTarget));
        encoder.Save(stream);

        return Convert.ToBase64String(stream.ToArray());
    }

    private string CaptureScreenRegion(Rect bounds)
    {
        using var bitmap = new Bitmap((int)bounds.Width, (int)bounds.Height);
        using var graphics = Graphics.FromImage(bitmap);

        graphics.CopyFromScreen(
            (int)bounds.X,
            (int)bounds.Y,
            0, 0,
            new System.Drawing.Size((int)bounds.Width, (int)bounds.Height));

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return Convert.ToBase64String(stream.ToArray());
    }

    #endregion

    #region UI Element Discovery

    private async Task<TestResponse> HandleListElementsAsync(TestCommand command)
    {
        return await RunOnUIThreadAsync(() =>
        {
            var maxDepth = command.Params?.TryGetValue("depth", out var depthObj) == true
                ? Convert.ToInt32(depthObj)
                : 3;

            var elements = new List<ElementInfo>();
            CollectElementInfo(_mainWindow, elements, maxDepth, 0, "Window");

            return TestResponse.Ok(elements);
        });
    }

    private async Task<TestResponse> HandleGetElementAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Target element is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            var element = FindElementByIdentifier(command.Target);
            if (element == null)
            {
                return TestResponse.Fail($"Element not found: {command.Target}");
            }

            var info = CreateElementInfo(element, command.Target);
            return TestResponse.Ok(info);
        });
    }

    private async Task<TestResponse> HandleFindElementAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Value))
        {
            return TestResponse.Fail("Search value is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            var searchType = command.Params?.TryGetValue("type", out var typeObj) == true
                ? typeObj?.ToString()
                : "any";

            var elements = FindElementsByText(command.Value, searchType);
            var infos = elements.Select(e => CreateElementInfo(e, e.Name ?? e.GetType().Name)).ToList();

            return TestResponse.Ok(infos);
        });
    }

    private async Task<TestResponse> HandleGetVisualTreeAsync(TestCommand command)
    {
        return await RunOnUIThreadAsync(() =>
        {
            var maxDepth = command.Params?.TryGetValue("depth", out var depthObj) == true
                ? Convert.ToInt32(depthObj)
                : 10;

            FrameworkElement root = _mainWindow;
            if (!string.IsNullOrEmpty(command.Target))
            {
                root = FindElementByIdentifier(command.Target) ?? _mainWindow;
            }

            var tree = BuildVisualTree(root, maxDepth, 0, "");
            return TestResponse.Ok(tree);
        });
    }

    private ElementInfo BuildVisualTree(DependencyObject obj, int maxDepth, int currentDepth, string path)
    {
        var info = new ElementInfo();

        if (obj is FrameworkElement fe)
        {
            info.Name = fe.Name;
            info.ClassName = fe.GetType().Name;
            info.IsVisible = fe.Visibility == Visibility.Visible;
            info.IsEnabled = fe.IsEnabled;
            info.Path = path;

            if (fe is Control control)
            {
                info.ControlType = control.GetType().Name;
            }

            // Get AutomationId if set
            var automationId = AutomationProperties.GetAutomationId(fe);
            if (!string.IsNullOrEmpty(automationId))
            {
                info.AutomationId = automationId;
            }

            // Get text content
            info.Text = GetElementText(fe);

            // Get bounds
            var topLeft = fe.PointToScreen(new System.Windows.Point(0, 0));
            info.Bounds = new BoundsInfo
            {
                X = topLeft.X,
                Y = topLeft.Y,
                Width = fe.ActualWidth,
                Height = fe.ActualHeight
            };
        }
        else
        {
            info.ClassName = obj.GetType().Name;
            info.Path = path;
        }

        if (currentDepth < maxDepth)
        {
            var children = new List<ElementInfo>();
            var childCount = VisualTreeHelper.GetChildrenCount(obj);

            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                var childPath = string.IsNullOrEmpty(path)
                    ? $"[{i}]"
                    : $"{path}[{i}]";

                children.Add(BuildVisualTree(child, maxDepth, currentDepth + 1, childPath));
            }

            if (children.Count > 0)
            {
                info.Children = children;
            }
        }

        return info;
    }

    private void CollectElementInfo(DependencyObject obj, List<ElementInfo> elements, int maxDepth, int currentDepth, string path)
    {
        if (obj is FrameworkElement fe)
        {
            // Only include elements that have a name or AutomationId
            var name = fe.Name;
            var automationId = AutomationProperties.GetAutomationId(fe);

            if (!string.IsNullOrEmpty(name) || !string.IsNullOrEmpty(automationId))
            {
                var info = CreateElementInfo(fe, path);
                elements.Add(info);
            }
        }

        if (currentDepth < maxDepth)
        {
            var childCount = VisualTreeHelper.GetChildrenCount(obj);
            for (int i = 0; i < childCount; i++)
            {
                var child = VisualTreeHelper.GetChild(obj, i);
                var childPath = obj is FrameworkElement el && !string.IsNullOrEmpty(el.Name)
                    ? $"{path}.{el.Name}"
                    : path;

                CollectElementInfo(child, elements, maxDepth, currentDepth + 1, childPath);
            }
        }
    }

    private ElementInfo CreateElementInfo(FrameworkElement element, string path)
    {
        var info = new ElementInfo
        {
            Name = element.Name,
            AutomationId = AutomationProperties.GetAutomationId(element),
            ClassName = element.GetType().Name,
            IsEnabled = element.IsEnabled,
            IsVisible = element.Visibility == Visibility.Visible,
            IsFocused = element.IsFocused,
            Path = path,
            Text = GetElementText(element)
        };

        if (element is Control control)
        {
            info.ControlType = control.GetType().Name;
        }

        try
        {
            var topLeft = element.PointToScreen(new System.Windows.Point(0, 0));
            info.Bounds = new BoundsInfo
            {
                X = topLeft.X,
                Y = topLeft.Y,
                Width = element.ActualWidth,
                Height = element.ActualHeight
            };
        }
        catch
        {
            // Element might not be connected to visual tree
        }

        return info;
    }

    private string? GetElementText(FrameworkElement element)
    {
        return element switch
        {
            TextBlock tb => tb.Text,
            TextBox tb => tb.Text,
            ContentControl cc when cc.Content is string s => s,
            HeaderedContentControl hcc when hcc.Header is string s => s,
            _ => null
        };
    }

    private FrameworkElement? FindElementByIdentifier(string identifier)
    {
        // Try by AutomationId first
        var element = FindElementByAutomationId(_mainWindow, identifier);
        if (element != null) return element;

        // Try by Name
        element = FindElementByName(_mainWindow, identifier);
        if (element != null) return element;

        // Try by ClassName
        element = FindElementByClassName(_mainWindow, identifier);
        return element;
    }

    private FrameworkElement? FindElementByAutomationId(DependencyObject parent, string automationId)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is FrameworkElement fe)
            {
                var id = AutomationProperties.GetAutomationId(fe);
                if (id == automationId)
                {
                    return fe;
                }
            }

            var result = FindElementByAutomationId(child, automationId);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private FrameworkElement? FindElementByName(DependencyObject parent, string name)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is FrameworkElement fe && fe.Name == name)
            {
                return fe;
            }

            var result = FindElementByName(child, name);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private FrameworkElement? FindElementByClassName(DependencyObject parent, string className)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is FrameworkElement fe && fe.GetType().Name == className)
            {
                return fe;
            }

            var result = FindElementByClassName(child, className);
            if (result != null)
            {
                return result;
            }
        }
        return null;
    }

    private List<FrameworkElement> FindElementsByText(string text, string? searchType)
    {
        var results = new List<FrameworkElement>();
        FindElementsByTextRecursive(_mainWindow, text, searchType, results);
        return results;
    }

    private void FindElementsByTextRecursive(DependencyObject parent, string text, string? searchType, List<FrameworkElement> results)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(parent);
        for (int i = 0; i < childCount; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is FrameworkElement fe)
            {
                var elementText = GetElementText(fe);
                if (elementText != null && elementText.Contains(text, StringComparison.OrdinalIgnoreCase))
                {
                    if (searchType == "any" ||
                        string.IsNullOrEmpty(searchType) ||
                        fe.GetType().Name.Equals(searchType, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(fe);
                    }
                }
            }

            FindElementsByTextRecursive(child, text, searchType, results);
        }
    }

    #endregion

    #region UI Interaction

    private async Task<TestResponse> HandleClickAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Target element is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            var element = FindElementByIdentifier(command.Target);
            if (element == null)
            {
                return TestResponse.Fail($"Element not found: {command.Target}");
            }

            if (element is Button button)
            {
                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return TestResponse.Ok(new { clicked = command.Target });
            }

            if (element is System.Windows.Controls.Primitives.ButtonBase buttonBase)
            {
                buttonBase.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                return TestResponse.Ok(new { clicked = command.Target });
            }

            // For other elements, try to invoke click via automation
            element.RaiseEvent(new RoutedEventArgs(UIElement.PreviewMouseLeftButtonDownEvent));
            element.RaiseEvent(new RoutedEventArgs(UIElement.MouseLeftButtonDownEvent));
            element.RaiseEvent(new RoutedEventArgs(UIElement.PreviewMouseLeftButtonUpEvent));
            element.RaiseEvent(new RoutedEventArgs(UIElement.MouseLeftButtonUpEvent));

            return TestResponse.Ok(new { clicked = command.Target });
        });
    }

    private async Task<TestResponse> HandleDoubleClickAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Target element is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            var element = FindElementByIdentifier(command.Target);
            if (element == null)
            {
                return TestResponse.Fail($"Element not found: {command.Target}");
            }

            var args = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
            {
                RoutedEvent = Control.MouseDoubleClickEvent
            };
            element.RaiseEvent(args);

            return TestResponse.Ok(new { doubleClicked = command.Target });
        });
    }

    private async Task<TestResponse> HandleRightClickAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Target element is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            var element = FindElementByIdentifier(command.Target);
            if (element == null)
            {
                return TestResponse.Fail($"Element not found: {command.Target}");
            }

            element.RaiseEvent(new RoutedEventArgs(UIElement.PreviewMouseRightButtonDownEvent));
            element.RaiseEvent(new RoutedEventArgs(UIElement.MouseRightButtonDownEvent));
            element.RaiseEvent(new RoutedEventArgs(UIElement.PreviewMouseRightButtonUpEvent));
            element.RaiseEvent(new RoutedEventArgs(UIElement.MouseRightButtonUpEvent));

            return TestResponse.Ok(new { rightClicked = command.Target });
        });
    }

    private async Task<TestResponse> HandleTypeAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Target element is required");
        }

        if (command.Value == null)
        {
            return TestResponse.Fail("Value to type is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            var element = FindElementByIdentifier(command.Target);
            if (element == null)
            {
                return TestResponse.Fail($"Element not found: {command.Target}");
            }

            if (element is TextBox textBox)
            {
                textBox.Focus();
                textBox.Text = command.Value;
                textBox.CaretIndex = textBox.Text.Length;
                return TestResponse.Ok(new { typed = command.Value, target = command.Target });
            }

            if (element is PasswordBox passwordBox)
            {
                passwordBox.Focus();
                passwordBox.Password = command.Value;
                return TestResponse.Ok(new { typed = "***", target = command.Target });
            }

            return TestResponse.Fail($"Element {command.Target} does not support typing");
        });
    }

    private async Task<TestResponse> HandleClearAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Target element is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            var element = FindElementByIdentifier(command.Target);
            if (element == null)
            {
                return TestResponse.Fail($"Element not found: {command.Target}");
            }

            if (element is TextBox textBox)
            {
                textBox.Clear();
                return TestResponse.Ok(new { cleared = command.Target });
            }

            if (element is PasswordBox passwordBox)
            {
                passwordBox.Clear();
                return TestResponse.Ok(new { cleared = command.Target });
            }

            return TestResponse.Fail($"Element {command.Target} does not support clearing");
        });
    }

    private async Task<TestResponse> HandleFocusAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Target element is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            var element = FindElementByIdentifier(command.Target);
            if (element == null)
            {
                return TestResponse.Fail($"Element not found: {command.Target}");
            }

            element.Focus();
            return TestResponse.Ok(new { focused = command.Target });
        });
    }

    private async Task<TestResponse> HandleSendKeysAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Value))
        {
            return TestResponse.Fail("Key sequence is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            // Use SendKeys to send keyboard input
            System.Windows.Forms.SendKeys.SendWait(command.Value);
            return TestResponse.Ok(new { sentKeys = command.Value });
        });
    }

    #endregion

    #region Property Access

    private async Task<TestResponse> HandleGetPropertyAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Target is required");
        }

        if (string.IsNullOrEmpty(command.Value))
        {
            return TestResponse.Fail("Property name is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            // Try ViewModel property first
            var vmProp = _viewModel.GetType().GetProperty(command.Value);
            if (vmProp != null)
            {
                var value = vmProp.GetValue(_viewModel);
                return TestResponse.Ok(new { property = command.Value, value = value?.ToString() });
            }

            // Try element property
            var element = FindElementByIdentifier(command.Target);
            if (element == null)
            {
                return TestResponse.Fail($"Element not found: {command.Target}");
            }

            var prop = element.GetType().GetProperty(command.Value);
            if (prop == null)
            {
                return TestResponse.Fail($"Property not found: {command.Value}");
            }

            var propValue = prop.GetValue(element);
            return TestResponse.Ok(new { property = command.Value, value = propValue?.ToString() });
        });
    }

    private async Task<TestResponse> HandleSetPropertyAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Target element is required");
        }

        if (command.Params == null ||
            !command.Params.TryGetValue("property", out var propNameObj) ||
            !command.Params.TryGetValue("value", out var valueObj))
        {
            return TestResponse.Fail("Property name and value are required in params");
        }

        return await RunOnUIThreadAsync(() =>
        {
            var element = FindElementByIdentifier(command.Target);
            if (element == null)
            {
                return TestResponse.Fail($"Element not found: {command.Target}");
            }

            var propName = propNameObj?.ToString();
            var prop = element.GetType().GetProperty(propName!);
            if (prop == null || !prop.CanWrite)
            {
                return TestResponse.Fail($"Property not found or not writable: {propName}");
            }

            var convertedValue = Convert.ChangeType(valueObj, prop.PropertyType);
            prop.SetValue(element, convertedValue);

            return TestResponse.Ok(new { property = propName, value = valueObj?.ToString() });
        });
    }

    private async Task<TestResponse> HandleGetTextAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Target element is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            var element = FindElementByIdentifier(command.Target);
            if (element == null)
            {
                return TestResponse.Fail($"Element not found: {command.Target}");
            }

            var text = GetElementText(element);
            return TestResponse.Ok(new { target = command.Target, text = text });
        });
    }

    #endregion

    #region Commands & Actions

    private async Task<TestResponse> HandleInvokeCommandAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Command name is required");
        }

        return await RunOnUIThreadAsync(async () =>
        {
            // Find command property on ViewModel
            var prop = _viewModel.GetType().GetProperty(command.Target);
            if (prop == null)
            {
                return TestResponse.Fail($"Command not found: {command.Target}");
            }

            var cmdValue = prop.GetValue(_viewModel);

            if (cmdValue is ICommand cmd)
            {
                object? parameter = null;
                if (command.Params?.TryGetValue("parameter", out var paramObj) == true)
                {
                    parameter = paramObj;
                }

                if (!cmd.CanExecute(parameter))
                {
                    return TestResponse.Fail($"Command {command.Target} cannot execute");
                }

                cmd.Execute(parameter);
                return TestResponse.Ok(new { invoked = command.Target });
            }

            // Check if it's an async relay command
            if (cmdValue != null)
            {
                var executeMethod = cmdValue.GetType().GetMethod("ExecuteAsync");
                if (executeMethod != null)
                {
                    object? parameter = null;
                    if (command.Params?.TryGetValue("parameter", out var paramObj) == true)
                    {
                        parameter = paramObj;
                    }

                    var task = executeMethod.Invoke(cmdValue, new[] { parameter }) as Task;
                    if (task != null)
                    {
                        await task;
                    }
                    return TestResponse.Ok(new { invoked = command.Target });
                }
            }

            return TestResponse.Fail($"{command.Target} is not a valid command");
        });
    }

    private async Task<TestResponse> HandleInvokeButtonAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Button identifier is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            var element = FindElementByIdentifier(command.Target);
            if (element == null)
            {
                return TestResponse.Fail($"Button not found: {command.Target}");
            }

            if (element is Button button)
            {
                if (button.Command != null && button.Command.CanExecute(button.CommandParameter))
                {
                    button.Command.Execute(button.CommandParameter);
                    return TestResponse.Ok(new { invoked = command.Target });
                }

                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                return TestResponse.Ok(new { clicked = command.Target });
            }

            return TestResponse.Fail($"Element {command.Target} is not a button");
        });
    }

    #endregion

    #region Host Management

    private TestResponse HandleGetHosts()
    {
        var hosts = _viewModel.HostManagement.Hosts.Select(h => new HostInfo
        {
            Id = h.Id,
            DisplayName = h.DisplayName,
            Hostname = h.Hostname,
            Port = h.Port,
            Username = h.Username,
            ConnectionType = h.ConnectionType.ToString(),
            GroupId = h.GroupId,
            GroupName = _viewModel.HostManagement.Groups.FirstOrDefault(g => g.Id == h.GroupId)?.Name
        }).ToList();

        return TestResponse.Ok(hosts);
    }

    private async Task<TestResponse> HandleSelectHostAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Host ID or name is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            HostEntry? host = null;

            // Try by ID
            if (Guid.TryParse(command.Target, out var hostId))
            {
                host = _viewModel.HostManagement.Hosts.FirstOrDefault(h => h.Id == hostId);
            }

            // Try by display name
            host ??= _viewModel.HostManagement.Hosts.FirstOrDefault(h =>
                h.DisplayName.Equals(command.Target, StringComparison.OrdinalIgnoreCase));

            if (host == null)
            {
                return TestResponse.Fail($"Host not found: {command.Target}");
            }

            _viewModel.HostManagement.SelectedHost = host;
            return TestResponse.Ok(new { selected = host.DisplayName, id = host.Id });
        });
    }

    private async Task<TestResponse> HandleConnectHostAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Host ID or name is required");
        }

        HostEntry? host = null;

        // Try by ID
        if (Guid.TryParse(command.Target, out var hostId))
        {
            host = _viewModel.HostManagement.Hosts.FirstOrDefault(h => h.Id == hostId);
        }

        // Try by display name
        host ??= _viewModel.HostManagement.Hosts.FirstOrDefault(h =>
            h.DisplayName.Equals(command.Target, StringComparison.OrdinalIgnoreCase));

        if (host == null)
        {
            return TestResponse.Fail($"Host not found: {command.Target}");
        }

        try
        {
            await _viewModel.Session.ConnectCommand.ExecuteAsync(host);
            return TestResponse.Ok(new { connecting = host.DisplayName, id = host.Id });
        }
        catch (Exception ex)
        {
            return TestResponse.Fail($"Failed to connect: {ex.Message}");
        }
    }

    private async Task<TestResponse> HandleDisconnectSessionAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            // Disconnect current session
            var current = _viewModel.Session.CurrentSession;
            if (current == null)
            {
                return TestResponse.Fail("No active session");
            }

            _viewModel.Session.CloseSessionCommand.Execute(current);
            return TestResponse.Ok(new { disconnected = current.Title });
        }

        // Find session by ID
        if (!Guid.TryParse(command.Target, out var sessionId))
        {
            return TestResponse.Fail("Invalid session ID");
        }

        var session = _viewModel.Session.Sessions.FirstOrDefault(s => s.Id == sessionId);
        if (session == null)
        {
            return TestResponse.Fail($"Session not found: {command.Target}");
        }

        await RunOnUIThreadAsync(() =>
        {
            _viewModel.Session.CloseSessionCommand.Execute(session);
            return TestResponse.Ok();
        });

        return TestResponse.Ok(new { disconnected = session.Title, id = session.Id });
    }

    #endregion

    #region Session Management

    private TestResponse HandleGetSessions()
    {
        var sessions = _viewModel.Session.Sessions.Select(s => new SessionInfo
        {
            Id = s.Id,
            Title = s.Title,
            HostId = s.Host?.Id,
            Hostname = s.Host?.Hostname,
            IsConnected = s.IsConnected,
            ConnectionType = s.Host?.ConnectionType.ToString(),
            CreatedAt = s.CreatedAt
        }).ToList();

        return TestResponse.Ok(sessions);
    }

    private async Task<TestResponse> HandleSelectSessionAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Session ID is required");
        }

        if (!Guid.TryParse(command.Target, out var sessionId))
        {
            return TestResponse.Fail("Invalid session ID");
        }

        return await RunOnUIThreadAsync(() =>
        {
            var session = _viewModel.Session.Sessions.FirstOrDefault(s => s.Id == sessionId);
            if (session == null)
            {
                return TestResponse.Fail($"Session not found: {command.Target}");
            }

            _viewModel.Session.CurrentSession = session;
            return TestResponse.Ok(new { selected = session.Title, id = session.Id });
        });
    }

    private Task<TestResponse> HandleSendToTerminalAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Value))
        {
            return Task.FromResult(TestResponse.Fail("Text to send is required"));
        }

        var session = _viewModel.Session.CurrentSession;
        if (!string.IsNullOrEmpty(command.Target) && Guid.TryParse(command.Target, out var sessionId))
        {
            session = _viewModel.Session.Sessions.FirstOrDefault(s => s.Id == sessionId);
        }

        if (session == null)
        {
            return Task.FromResult(TestResponse.Fail("No target session"));
        }

        if (!session.IsConnected)
        {
            return Task.FromResult(TestResponse.Fail("Session is not connected"));
        }

        try
        {
            // Send data through the appropriate bridge
            var data = System.Text.Encoding.UTF8.GetBytes(command.Value);

            if (session.Bridge != null)
            {
                // SSH session - send through the bridge's shell stream
                session.Bridge.SendData(data);
            }
            else if (session.SerialBridge != null)
            {
                // Serial session - send through serial bridge
                session.SerialBridge.SendData(data);
            }
            else
            {
                return Task.FromResult(TestResponse.Fail("No active bridge for session"));
            }

            return Task.FromResult(TestResponse.Ok(new { sent = command.Value.Length, sessionId = session.Id }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(TestResponse.Fail($"Failed to send: {ex.Message}"));
        }
    }

    private Task<TestResponse> HandleGetTerminalOutputAsync(TestCommand command)
    {
        var session = _viewModel.Session.CurrentSession;
        if (!string.IsNullOrEmpty(command.Target) && Guid.TryParse(command.Target, out var sessionId))
        {
            session = _viewModel.Session.Sessions.FirstOrDefault(s => s.Id == sessionId);
        }

        if (session == null)
        {
            return Task.FromResult(TestResponse.Fail("No target session"));
        }

        // Get recent output preview from the session
        var output = session.LastOutputPreview;
        if (string.IsNullOrEmpty(output))
        {
            output = "(No recent output)";
        }

        return Task.FromResult(TestResponse.Ok(new
        {
            sessionId = session.Id,
            output = output,
            title = session.Title,
            isConnected = session.IsConnected,
            bytesSent = session.TotalBytesSent,
            bytesReceived = session.TotalBytesReceived
        }));
    }

    #endregion

    #region Wait & Sync

    private async Task<TestResponse> HandleWaitAsync(TestCommand command, CancellationToken cancellationToken)
    {
        var milliseconds = command.Timeout;
        if (command.Params?.TryGetValue("ms", out var msObj) == true)
        {
            milliseconds = Convert.ToInt32(msObj);
        }

        await Task.Delay(milliseconds, cancellationToken);
        return TestResponse.Ok(new { waited = milliseconds });
    }

    private async Task<TestResponse> HandleWaitForElementAsync(TestCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Target element is required");
        }

        var timeout = TimeSpan.FromMilliseconds(command.Timeout);
        var pollInterval = TimeSpan.FromMilliseconds(100);
        var sw = System.Diagnostics.Stopwatch.StartNew();

        while (sw.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var found = await RunOnUIThreadAsync(() =>
            {
                var element = FindElementByIdentifier(command.Target);
                return element != null && element.Visibility == Visibility.Visible;
            });

            if (found)
            {
                return TestResponse.Ok(new { found = command.Target, elapsedMs = sw.ElapsedMilliseconds });
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return TestResponse.Fail($"Timeout waiting for element: {command.Target}");
    }

    #endregion

    #region Dialogs

    private async Task<TestResponse> HandleOpenDialogAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Dialog type is required");
        }

        return await RunOnUIThreadAsync(async () =>
        {
            // Map dialog names to commands
            var result = command.Target.ToLowerInvariant() switch
            {
                "settings" => await InvokeSettingsDialogAsync(),
                "addhost" => await InvokeAddHostDialogAsync(),
                "edithost" => await InvokeEditHostDialogAsync(),
                _ => TestResponse.Fail($"Unknown dialog: {command.Target}")
            };

            return result;
        });
    }

    private async Task<TestResponse> InvokeSettingsDialogAsync()
    {
        // Find and invoke settings command/button
        var settingsButton = FindElementByIdentifier("SettingsButton") ?? FindElementByName(_mainWindow, "SettingsButton");
        if (settingsButton is Button btn)
        {
            btn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(100); // Give dialog time to open
            return TestResponse.Ok(new { opened = "settings" });
        }
        return TestResponse.Fail("Settings button not found");
    }

    private async Task<TestResponse> InvokeAddHostDialogAsync()
    {
        if (_viewModel.HostManagement.AddHostCommand.CanExecute(null))
        {
            await _viewModel.HostManagement.AddHostCommand.ExecuteAsync(null);
            return TestResponse.Ok(new { opened = "addhost" });
        }
        return TestResponse.Fail("Cannot open add host dialog");
    }

    private async Task<TestResponse> InvokeEditHostDialogAsync()
    {
        if (_viewModel.HostManagement.SelectedHost == null)
        {
            return TestResponse.Fail("No host selected");
        }

        if (_viewModel.HostManagement.EditHostCommand.CanExecute(_viewModel.HostManagement.SelectedHost))
        {
            await _viewModel.HostManagement.EditHostCommand.ExecuteAsync(_viewModel.HostManagement.SelectedHost);
            return TestResponse.Ok(new { opened = "edithost", host = _viewModel.HostManagement.SelectedHost.DisplayName });
        }
        return TestResponse.Fail("Cannot open edit host dialog");
    }

    private async Task<TestResponse> HandleCloseDialogAsync(TestCommand command)
    {
        return await RunOnUIThreadAsync(() =>
        {
            var dialogs = GetOpenDialogs();
            foreach (var dialog in dialogs)
            {
                if (string.IsNullOrEmpty(command.Target) ||
                    dialog.GetType().Name.Contains(command.Target, StringComparison.OrdinalIgnoreCase))
                {
                    dialog.Close();
                    return TestResponse.Ok(new { closed = dialog.GetType().Name });
                }
            }

            return string.IsNullOrEmpty(command.Target)
                ? TestResponse.Fail("No dialogs open")
                : TestResponse.Fail($"Dialog not found: {command.Target}");
        });
    }

    private async Task<TestResponse> HandleGetDialogsAsync()
    {
        return await RunOnUIThreadAsync(() =>
        {
            var dialogNames = GetOpenDialogNames();
            return TestResponse.Ok(new { dialogs = dialogNames, count = dialogNames.Count });
        });
    }

    private List<Window> GetOpenDialogs()
    {
        var dialogs = new List<Window>();
        foreach (Window window in Application.Current.Windows)
        {
            if (window != _mainWindow && window.IsVisible)
            {
                dialogs.Add(window);
            }
        }
        return dialogs;
    }

    private List<string> GetOpenDialogNames()
    {
        return GetOpenDialogs().Select(d => d.GetType().Name).ToList();
    }

    private async Task<TestResponse> HandleSelectTabAsync(TestCommand command)
    {
        if (string.IsNullOrEmpty(command.Target))
        {
            return TestResponse.Fail("Tab identifier is required");
        }

        return await RunOnUIThreadAsync(() =>
        {
            // Try to find TabControl and select tab
            var tabControl = FindElementByClassName(_mainWindow, "TabControl") as TabControl;
            if (tabControl == null)
            {
                return TestResponse.Fail("No TabControl found");
            }

            // Try by index
            if (int.TryParse(command.Target, out var index))
            {
                if (index >= 0 && index < tabControl.Items.Count)
                {
                    tabControl.SelectedIndex = index;
                    return TestResponse.Ok(new { selectedTab = index });
                }
                return TestResponse.Fail($"Tab index out of range: {index}");
            }

            // Try by header text
            for (int i = 0; i < tabControl.Items.Count; i++)
            {
                if (tabControl.Items[i] is TabItem tabItem)
                {
                    var header = tabItem.Header?.ToString();
                    if (header?.Contains(command.Target, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        tabControl.SelectedIndex = i;
                        return TestResponse.Ok(new { selectedTab = header });
                    }
                }
            }

            return TestResponse.Fail($"Tab not found: {command.Target}");
        });
    }

    #endregion

    #region Helpers

    private async Task<TestResponse> RunOnUIThreadAsync(Func<TestResponse> action)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return action();
        }

        return await Application.Current.Dispatcher.InvokeAsync(action);
    }

    private async Task<TestResponse> RunOnUIThreadAsync(Func<Task<TestResponse>> action)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return await action();
        }

        return await Application.Current.Dispatcher.InvokeAsync(action).Task.Unwrap();
    }

    private async Task<T> RunOnUIThreadAsync<T>(Func<T> action)
    {
        if (Application.Current.Dispatcher.CheckAccess())
        {
            return action();
        }

        return await Application.Current.Dispatcher.InvokeAsync(action);
    }

    #endregion
}
#endif
