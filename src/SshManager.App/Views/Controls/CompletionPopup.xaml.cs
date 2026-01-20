using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SshManager.App.ViewModels;
using SshManager.Core.Models;

namespace SshManager.App.Views.Controls;

/// <summary>
/// Interaction logic for CompletionPopup.xaml.
/// A lightweight popup control for displaying autocompletion suggestions.
/// </summary>
public partial class CompletionPopup : UserControl
{
    private CompletionPopupViewModel? _viewModel;

    /// <summary>
    /// Dependency property for the Items collection.
    /// </summary>
    public static readonly DependencyProperty ItemsProperty =
        DependencyProperty.Register(
            nameof(Items),
            typeof(ObservableCollection<CompletionItem>),
            typeof(CompletionPopup),
            new PropertyMetadata(null, OnItemsChanged));

    /// <summary>
    /// Dependency property for the SelectedIndex.
    /// </summary>
    public static readonly DependencyProperty SelectedIndexProperty =
        DependencyProperty.Register(
            nameof(SelectedIndex),
            typeof(int),
            typeof(CompletionPopup),
            new PropertyMetadata(0, OnSelectedIndexChanged));

    /// <summary>
    /// Dependency property for the IsPopupVisible flag.
    /// </summary>
    public static readonly DependencyProperty IsPopupVisibleProperty =
        DependencyProperty.Register(
            nameof(IsPopupVisible),
            typeof(bool),
            typeof(CompletionPopup),
            new PropertyMetadata(false, OnIsPopupVisibleChanged));

    /// <summary>
    /// Event raised when a completion item is selected via Enter or Tab.
    /// </summary>
    public event EventHandler<CompletionItem>? ItemSelected;

    /// <summary>
    /// Event raised when the popup should be closed (e.g., Escape pressed).
    /// </summary>
    public event EventHandler? CloseRequested;

    /// <summary>
    /// Gets or sets the collection of completion items to display.
    /// </summary>
    public ObservableCollection<CompletionItem> Items
    {
        get => (ObservableCollection<CompletionItem>)GetValue(ItemsProperty);
        set => SetValue(ItemsProperty, value);
    }

    /// <summary>
    /// Gets or sets the index of the currently selected item.
    /// </summary>
    public int SelectedIndex
    {
        get => (int)GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the popup is visible.
    /// </summary>
    public bool IsPopupVisible
    {
        get => (bool)GetValue(IsPopupVisibleProperty);
        set => SetValue(IsPopupVisibleProperty, value);
    }

    public CompletionPopup()
    {
        InitializeComponent();
        Items = new ObservableCollection<CompletionItem>();
        DataContext = _viewModel = new CompletionPopupViewModel();

        // Subscribe to ViewModel events
        _viewModel.ItemSelected += (s, e) => ItemSelected?.Invoke(this, e);
        _viewModel.CloseRequested += (s, e) => CloseRequested?.Invoke(this, e);
    }

    /// <summary>
    /// Shows the popup with the specified completion items at the current position.
    /// </summary>
    /// <param name="items">The completion items to display.</param>
    public void Show(IEnumerable<CompletionItem> items)
    {
        _viewModel?.Show(items);
    }

    /// <summary>
    /// Shows the popup with the specified completion items at the specified position.
    /// </summary>
    /// <param name="items">The completion items to display.</param>
    /// <param name="position">The position to display the popup at (relative to parent).</param>
    public void Show(IEnumerable<CompletionItem> items, Point position)
    {
        _viewModel?.Show(items);

        // Position the popup
        Canvas.SetLeft(this, position.X);
        Canvas.SetTop(this, position.Y);
    }

    /// <summary>
    /// Hides the popup and clears the items.
    /// </summary>
    public void HidePopup()
    {
        _viewModel?.Hide();
    }

    /// <summary>
    /// Moves selection to the next item in the list.
    /// </summary>
    public void SelectNext()
    {
        _viewModel?.SelectNext();
    }

    /// <summary>
    /// Moves selection to the previous item in the list.
    /// </summary>
    public void SelectPrevious()
    {
        _viewModel?.SelectPrevious();
    }

    /// <summary>
    /// Gets the currently selected completion item.
    /// </summary>
    /// <returns>The selected item, or null if no item is selected.</returns>
    public CompletionItem? GetSelectedItem()
    {
        return _viewModel?.GetSelectedItem();
    }

    private static void OnItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CompletionPopup popup && popup._viewModel != null)
        {
            popup._viewModel.Items = e.NewValue as ObservableCollection<CompletionItem> ?? new ObservableCollection<CompletionItem>();
        }
    }

    private static void OnSelectedIndexChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CompletionPopup popup && popup._viewModel != null && e.NewValue is int index)
        {
            popup._viewModel.SelectedIndex = index;
        }
    }

    private static void OnIsPopupVisibleChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CompletionPopup popup && popup._viewModel != null && e.NewValue is bool isVisible)
        {
            popup._viewModel.IsVisible = isVisible;
        }
    }

    private void CompletionListBox_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
            case Key.Tab:
                // Confirm selection
                _viewModel?.ConfirmSelection();
                e.Handled = true;
                break;

            case Key.Escape:
                // Close popup
                _viewModel?.Close();
                e.Handled = true;
                break;

            case Key.Down:
                // Move to next item
                _viewModel?.SelectNext();
                e.Handled = true;
                break;

            case Key.Up:
                // Move to previous item
                _viewModel?.SelectPrevious();
                e.Handled = true;
                break;
        }
    }

    private void CompletionListBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Handle Tab key in PreviewKeyDown to prevent focus change
        if (e.Key == Key.Tab)
        {
            _viewModel?.ConfirmSelection();
            e.Handled = true;
        }
    }
}
