using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Core.Models;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the autocompletion popup control.
/// Manages the list of completion suggestions and keyboard navigation.
/// </summary>
public partial class CompletionPopupViewModel : ObservableObject
{
    /// <summary>
    /// Event raised when a completion item is selected via Enter or Tab.
    /// </summary>
    public event EventHandler<CompletionItem>? ItemSelected;

    /// <summary>
    /// Event raised when the popup should be closed (e.g., Escape pressed).
    /// </summary>
    public event EventHandler? CloseRequested;

    [ObservableProperty]
    private ObservableCollection<CompletionItem> _items = new();

    [ObservableProperty]
    private int _selectedIndex = 0;

    [ObservableProperty]
    private bool _isVisible = false;

    /// <summary>
    /// Gets the currently selected completion item, or null if none selected.
    /// </summary>
    public CompletionItem? SelectedItem => Items.Count > 0 && SelectedIndex >= 0 && SelectedIndex < Items.Count
        ? Items[SelectedIndex]
        : null;

    /// <summary>
    /// Shows the popup with the specified completion items.
    /// </summary>
    /// <param name="items">The completion items to display.</param>
    public void Show(IEnumerable<CompletionItem> items)
    {
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        SelectedIndex = Items.Count > 0 ? 0 : -1;
        IsVisible = Items.Count > 0;
    }

    /// <summary>
    /// Hides the popup and clears the items.
    /// </summary>
    [RelayCommand]
    public void Hide()
    {
        IsVisible = false;
        Items.Clear();
        SelectedIndex = -1;
    }

    /// <summary>
    /// Moves selection to the next item in the list.
    /// Wraps around to the first item if at the end.
    /// </summary>
    [RelayCommand]
    public void SelectNext()
    {
        if (Items.Count == 0) return;

        SelectedIndex = (SelectedIndex + 1) % Items.Count;
    }

    /// <summary>
    /// Moves selection to the previous item in the list.
    /// Wraps around to the last item if at the beginning.
    /// </summary>
    [RelayCommand]
    public void SelectPrevious()
    {
        if (Items.Count == 0) return;

        SelectedIndex = SelectedIndex <= 0 ? Items.Count - 1 : SelectedIndex - 1;
    }

    /// <summary>
    /// Confirms the current selection and raises the ItemSelected event.
    /// </summary>
    [RelayCommand]
    public void ConfirmSelection()
    {
        if (SelectedItem != null)
        {
            ItemSelected?.Invoke(this, SelectedItem);
            Hide();
        }
    }

    /// <summary>
    /// Closes the popup and raises the CloseRequested event.
    /// </summary>
    [RelayCommand]
    public void Close()
    {
        Hide();
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Gets the currently selected completion item.
    /// </summary>
    /// <returns>The selected item, or null if no item is selected.</returns>
    public CompletionItem? GetSelectedItem()
    {
        return SelectedItem;
    }

    partial void OnSelectedIndexChanged(int value)
    {
        OnPropertyChanged(nameof(SelectedItem));
    }
}
