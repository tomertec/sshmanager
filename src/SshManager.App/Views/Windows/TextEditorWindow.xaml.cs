using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ICSharpCode.AvalonEdit.Search;
using SshManager.App.Services;
using SshManager.App.ViewModels;
using Wpf.Ui.Controls;

namespace SshManager.App.Views.Windows;

/// <summary>
/// Text editor window with AvalonEdit control and search/replace functionality.
/// </summary>
public partial class TextEditorWindow : FluentWindow
{
    private readonly TextEditorViewModel _viewModel;
    private readonly IEditorThemeService _themeService;
    private SearchPanel? _searchPanel;

    public TextEditorWindow(TextEditorViewModel viewModel, IEditorThemeService themeService)
    {
        _viewModel = viewModel;
        _themeService = themeService;
        DataContext = viewModel;

        InitializeComponent();

        // Apply dark theme
        _themeService.ApplyDarkTheme(EditorControl);

        // Set up search panel
        _searchPanel = SearchPanel.Install(EditorControl.TextArea);

        // Wire up events
        _viewModel.RequestClose += OnRequestClose;
        _viewModel.MessageRequested += OnMessageRequested;
        _viewModel.SaveChangesRequested += OnSaveChangesRequested;

        // Track caret position changes
        EditorControl.TextArea.Caret.PositionChanged += Caret_PositionChanged;

        // Track text changes for dirty detection
        EditorControl.TextChanged += EditorControl_TextChanged;

        // Handle keyboard shortcuts
        EditorControl.PreviewKeyDown += EditorControl_PreviewKeyDown;
    }

    private void EditorControl_TextChanged(object? sender, EventArgs e)
    {
        _viewModel.MarkDirty();
    }

    private void Caret_PositionChanged(object? sender, EventArgs e)
    {
        var caret = EditorControl.TextArea.Caret;
        _viewModel.UpdateCaretPosition(caret.Line, caret.Column);
    }

    private void EditorControl_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Handle Ctrl+F for Find
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenFindPanel();
            e.Handled = true;
        }
        // Handle Ctrl+H for Replace
        else if (e.Key == Key.H && Keyboard.Modifiers == ModifierKeys.Control)
        {
            OpenReplacePanel();
            e.Handled = true;
        }
        // Handle Ctrl+G for Go to Line
        else if (e.Key == Key.G && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowGoToLineDialog();
            e.Handled = true;
        }
        // Handle Escape to close search panel
        else if (e.Key == Key.Escape && _searchPanel?.IsClosed == false)
        {
            _searchPanel.Close();
            EditorControl.Focus();
            e.Handled = true;
        }
    }

    private void FindButton_Click(object sender, RoutedEventArgs e)
    {
        OpenFindPanel();
    }

    private void ReplaceButton_Click(object sender, RoutedEventArgs e)
    {
        OpenReplacePanel();
    }

    private void GoToLineButton_Click(object sender, RoutedEventArgs e)
    {
        ShowGoToLineDialog();
    }

    private void WordWrapButton_Click(object sender, RoutedEventArgs e)
    {
        EditorControl.WordWrap = !EditorControl.WordWrap;
    }

    private void OpenFindPanel()
    {
        if (_searchPanel != null)
        {
            _searchPanel.Open();
            if (!string.IsNullOrEmpty(EditorControl.SelectedText))
            {
                _searchPanel.SearchPattern = EditorControl.SelectedText;
            }
        }
    }

    private void OpenReplacePanel()
    {
        // AvalonEdit's SearchPanel doesn't have built-in replace in older versions
        // Open search panel as fallback
        OpenFindPanel();
    }

    private void ShowGoToLineDialog()
    {
        var dialog = new TextInputDialog("Go to Line", $"Enter line number (1-{_viewModel.TotalLines}):", _viewModel.LineNumber.ToString());
        dialog.Owner = this;

        if (dialog.ShowDialog() == true && int.TryParse(dialog.InputText, out var lineNumber))
        {
            if (lineNumber >= 1 && lineNumber <= EditorControl.LineCount)
            {
                EditorControl.ScrollToLine(lineNumber);
                var line = EditorControl.Document.GetLineByNumber(lineNumber);
                EditorControl.CaretOffset = line.Offset;
                EditorControl.TextArea.Caret.BringCaretToView();
            }
        }
    }

    private void OnRequestClose()
    {
        DialogResult = _viewModel.DialogResult;
        Close();
    }

    private void OnMessageRequested(string title, string message)
    {
        System.Windows.MessageBox.Show(this, message, title, System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
    }

    private bool? OnSaveChangesRequested()
    {
        var result = System.Windows.MessageBox.Show(
            this,
            "Do you want to save changes to this file?",
            "Unsaved Changes",
            System.Windows.MessageBoxButton.YesNoCancel,
            System.Windows.MessageBoxImage.Question);

        return result switch
        {
            System.Windows.MessageBoxResult.Yes => true,
            System.Windows.MessageBoxResult.No => false,
            _ => null
        };
    }

    private void Window_Closing(object sender, CancelEventArgs e)
    {
        if (!_viewModel.TryClose())
        {
            e.Cancel = true;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _viewModel.RequestClose -= OnRequestClose;
        _viewModel.MessageRequested -= OnMessageRequested;
        _viewModel.SaveChangesRequested -= OnSaveChangesRequested;

        EditorControl.TextArea.Caret.PositionChanged -= Caret_PositionChanged;
        EditorControl.TextChanged -= EditorControl_TextChanged;
        EditorControl.PreviewKeyDown -= EditorControl_PreviewKeyDown;

        base.OnClosed(e);
    }

    /// <summary>
    /// Gets the view model for external access.
    /// </summary>
    public TextEditorViewModel ViewModel => _viewModel;
}

/// <summary>
/// Simple text input dialog for Go to Line functionality.
/// </summary>
internal class TextInputDialog : Window
{
    private readonly System.Windows.Controls.TextBox _textBox;

    public string InputText => _textBox.Text;

    public TextInputDialog(string title, string prompt, string defaultValue = "")
    {
        Title = title;
        Width = 300;
        Height = 150;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        WindowStyle = WindowStyle.ToolWindow;

        var grid = new System.Windows.Controls.Grid();
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = System.Windows.GridLength.Auto });
        grid.Margin = new Thickness(16);

        var label = new System.Windows.Controls.TextBlock
        {
            Text = prompt,
            Margin = new Thickness(0, 0, 0, 8)
        };
        System.Windows.Controls.Grid.SetRow(label, 0);
        grid.Children.Add(label);

        _textBox = new System.Windows.Controls.TextBox
        {
            Text = defaultValue,
            Margin = new Thickness(0, 0, 0, 16)
        };
        _textBox.SelectAll();
        System.Windows.Controls.Grid.SetRow(_textBox, 1);
        grid.Children.Add(_textBox);

        var buttonPanel = new System.Windows.Controls.StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right
        };
        System.Windows.Controls.Grid.SetRow(buttonPanel, 2);

        var okButton = new System.Windows.Controls.Button
        {
            Content = "OK",
            Width = 75,
            IsDefault = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        okButton.Click += (s, e) =>
        {
            DialogResult = true;
            Close();
        };
        buttonPanel.Children.Add(okButton);

        var cancelButton = new System.Windows.Controls.Button
        {
            Content = "Cancel",
            Width = 75,
            IsCancel = true
        };
        buttonPanel.Children.Add(cancelButton);

        grid.Children.Add(buttonPanel);
        Content = grid;

        Loaded += (s, e) => _textBox.Focus();
    }
}
