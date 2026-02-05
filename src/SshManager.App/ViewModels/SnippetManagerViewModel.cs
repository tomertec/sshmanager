using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.App.Views.Dialogs;
using SshManager.Core.Models;
using SshManager.Data.Repositories;

namespace SshManager.App.ViewModels;

public partial class SnippetManagerViewModel : ObservableObject
{
    private readonly ISnippetRepository _snippetRepo;
    private CancellationTokenSource? _searchCts;

    [ObservableProperty]
    private ObservableCollection<CommandSnippet> _snippets = [];

    [ObservableProperty]
    private ObservableCollection<string> _categories = [];

    [ObservableProperty]
    private CommandSnippet? _selectedSnippet;

    [ObservableProperty]
    private string? _selectedCategory;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _isLoading;

    public event Action? RequestClose;
    public event Action<CommandSnippet>? OnExecuteSnippet;

    public SnippetManagerViewModel(ISnippetRepository snippetRepo)
    {
        _snippetRepo = snippetRepo;
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        try
        {
            var snippets = await _snippetRepo.GetAllAsync();
            Snippets = new ObservableCollection<CommandSnippet>(snippets);

            var categories = await _snippetRepo.GetCategoriesAsync();
            Categories = new ObservableCollection<string>(categories);
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSearchTextChanged(string value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        _ = SearchAsync(_searchCts.Token).ContinueWith(t =>
            System.Diagnostics.Debug.WriteLine($"Search error: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    partial void OnSelectedCategoryChanged(string? value)
    {
        _searchCts?.Cancel();
        _searchCts?.Dispose();
        _searchCts = new CancellationTokenSource();
        _ = FilterAsync(_searchCts.Token).ContinueWith(t =>
            System.Diagnostics.Debug.WriteLine($"Filter error: {t.Exception}"),
            TaskContinuationOptions.OnlyOnFaulted);
    }

    private async Task SearchAsync(CancellationToken ct = default)
    {
        IsLoading = true;
        try
        {
            List<CommandSnippet> snippets;

            if (string.IsNullOrWhiteSpace(SearchText))
            {
                snippets = SelectedCategory != null
                    ? await _snippetRepo.GetByCategoryAsync(SelectedCategory, ct)
                    : await _snippetRepo.GetAllAsync(ct);
            }
            else
            {
                snippets = await _snippetRepo.SearchAsync(SearchText, ct);
                if (SelectedCategory != null)
                {
                    snippets = snippets.Where(s => s.Category == SelectedCategory).ToList();
                }
            }

            if (!ct.IsCancellationRequested)
            {
                Snippets = new ObservableCollection<CommandSnippet>(snippets);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when search is cancelled, ignore
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task FilterAsync(CancellationToken ct = default)
    {
        await SearchAsync(ct);
    }

    [RelayCommand]
    private void ClearFilter()
    {
        SelectedCategory = null;
        SearchText = "";
    }

    [RelayCommand]
    private async Task AddSnippetAsync()
    {
        var categories = await _snippetRepo.GetCategoriesAsync();
        var viewModel = new SnippetEditViewModel(null, categories);
        var dialog = new SnippetEditDialog(viewModel);
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var snippet = dialog.GetSnippet();
            await _snippetRepo.AddAsync(snippet);
            Snippets.Add(snippet);

            // Refresh categories if a new one was added
            if (!string.IsNullOrEmpty(snippet.Category) && !Categories.Contains(snippet.Category))
            {
                Categories.Add(snippet.Category);
            }
        }
    }

    [RelayCommand]
    private async Task EditSnippetAsync(CommandSnippet? snippet)
    {
        if (snippet == null) return;

        var categories = await _snippetRepo.GetCategoriesAsync();
        var viewModel = new SnippetEditViewModel(snippet, categories);
        var dialog = new SnippetEditDialog(viewModel);
        dialog.Owner = Application.Current.MainWindow;

        if (dialog.ShowDialog() == true)
        {
            var updatedSnippet = dialog.GetSnippet();
            await _snippetRepo.UpdateAsync(updatedSnippet);

            // Refresh the list to show updated data
            var index = Snippets.IndexOf(snippet);
            if (index >= 0)
            {
                Snippets[index] = updatedSnippet;
            }

            // Refresh categories
            var newCategories = await _snippetRepo.GetCategoriesAsync();
            Categories = new ObservableCollection<string>(newCategories);
        }
    }

    [RelayCommand]
    private async Task DeleteSnippetAsync(CommandSnippet? snippet)
    {
        if (snippet == null) return;

        var result = MessageBox.Show(
            $"Are you sure you want to delete the snippet '{snippet.Name}'?",
            "Delete Snippet",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
        {
            await _snippetRepo.DeleteAsync(snippet.Id);
            Snippets.Remove(snippet);

            // Refresh categories
            var newCategories = await _snippetRepo.GetCategoriesAsync();
            Categories = new ObservableCollection<string>(newCategories);
        }
    }

    [RelayCommand]
    private void ExecuteSnippet(CommandSnippet? snippet)
    {
        if (snippet == null) return;
        OnExecuteSnippet?.Invoke(snippet);
        // Close the dialog so user can interact with the terminal (e.g., type password for sudo)
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Close()
    {
        RequestClose?.Invoke();
    }
}
