using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SshManager.Security.OnePassword;

namespace SshManager.App.ViewModels;

/// <summary>
/// ViewModel for the 1Password vault browser dialog.
/// Allows users to browse vaults, search items, and select an op:// secret reference.
/// </summary>
public partial class OnePasswordBrowserViewModel : ObservableObject
{
    private readonly IOnePasswordService _onePasswordService;
    private readonly ILogger<OnePasswordBrowserViewModel> _logger;

    [ObservableProperty]
    private ObservableCollection<OnePasswordVault> _vaults = [];

    [ObservableProperty]
    private OnePasswordVault? _selectedVault;

    [ObservableProperty]
    private ObservableCollection<OnePasswordItem> _items = [];

    [ObservableProperty]
    private OnePasswordItem? _selectedItem;

    [ObservableProperty]
    private ObservableCollection<OnePasswordField> _fields = [];

    [ObservableProperty]
    private OnePasswordField? _selectedField;

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string? _statusText;

    [ObservableProperty]
    private string? _selectedReference;

    [ObservableProperty]
    private bool? _dialogResult;

    public event Action? RequestClose;

    public OnePasswordBrowserViewModel(
        IOnePasswordService onePasswordService,
        ILogger<OnePasswordBrowserViewModel>? logger = null)
    {
        _onePasswordService = onePasswordService;
        _logger = logger ?? NullLogger<OnePasswordBrowserViewModel>.Instance;
    }

    /// <summary>
    /// Loads vaults on dialog open.
    /// </summary>
    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusText = "Loading vaults...";

        try
        {
            var status = await _onePasswordService.GetStatusAsync();
            if (!status.IsInstalled)
            {
                StatusText = "1Password CLI not found. Install from https://1password.com/downloads/command-line/";
                return;
            }

            if (!status.IsAuthenticated)
            {
                StatusText = "Not authenticated. Open the 1Password desktop app and try again.";
                return;
            }

            var vaults = await _onePasswordService.ListVaultsAsync();
            Vaults = new ObservableCollection<OnePasswordVault>(vaults);

            StatusText = vaults.Count == 0
                ? "No vaults found"
                : $"{vaults.Count} vault(s) available";

            _logger.LogDebug("Loaded {VaultCount} vaults from 1Password", vaults.Count);
        }
        catch (Exception ex)
        {
            StatusText = "Error loading vaults from 1Password";
            _logger.LogError(ex, "Failed to load 1Password vaults");
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedVaultChanged(OnePasswordVault? value)
    {
        _ = LoadItemsAsync();
    }

    partial void OnSelectedItemChanged(OnePasswordItem? value)
    {
        if (value != null)
        {
            _ = LoadFieldsAsync(value);
        }
        else
        {
            Fields.Clear();
            SelectedField = null;
        }
    }

    [RelayCommand]
    private async Task SearchAsync()
    {
        await LoadItemsAsync();
    }

    private async Task LoadItemsAsync()
    {
        IsLoading = true;
        StatusText = "Loading items...";

        try
        {
            var items = await _onePasswordService.ListItemsAsync(
                SelectedVault?.Id,
                string.IsNullOrWhiteSpace(SearchQuery) ? null : SearchQuery.Trim());

            Items = new ObservableCollection<OnePasswordItem>(items);
            SelectedItem = null;
            Fields.Clear();
            SelectedField = null;

            StatusText = items.Count == 0
                ? "No items found"
                : $"{items.Count} item(s)";
        }
        catch (Exception ex)
        {
            StatusText = "Error loading items";
            _logger.LogWarning(ex, "Failed to load 1Password items");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadFieldsAsync(OnePasswordItem item)
    {
        IsLoading = true;

        try
        {
            var detail = await _onePasswordService.GetItemAsync(item.Id, item.Vault.Id);
            if (detail == null)
            {
                Fields.Clear();
                StatusText = "Failed to load item details";
                return;
            }

            // Show fields that have references (usable as op:// references)
            // Replace item name with item ID in references to avoid ambiguity
            // when multiple items share the same title (op read fails otherwise)
            var usableFields = detail.Fields
                .Where(f => !string.IsNullOrEmpty(f.Reference))
                .Select(f => ReplaceItemNameWithId(f, item.Id))
                .ToList();

            Fields = new ObservableCollection<OnePasswordField>(usableFields);

            StatusText = usableFields.Count == 0
                ? "No referenceable fields in this item"
                : $"{usableFields.Count} field(s) available";
        }
        catch (Exception ex)
        {
            StatusText = "Error loading item details";
            _logger.LogWarning(ex, "Failed to load 1Password item details for {ItemId}", item.Id);
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand]
    private void SelectField()
    {
        if (SelectedField?.Reference == null) return;

        SelectedReference = SelectedField.Reference;
        DialogResult = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    /// <summary>
    /// Replaces the item name in an op:// reference with the item ID to avoid
    /// ambiguity when multiple items share the same title.
    /// Reference format: op://vault/item/[section/]field
    /// </summary>
    private static OnePasswordField ReplaceItemNameWithId(OnePasswordField field, string itemId)
    {
        if (string.IsNullOrEmpty(field.Reference) || !field.Reference.StartsWith("op://"))
            return field;

        // Parse: op://vault/item/[section/]field
        var path = field.Reference["op://".Length..];
        var segments = path.Split('/');
        if (segments.Length < 3)
            return field;

        // Replace segment[1] (item name) with item ID
        segments[1] = itemId;
        var newReference = "op://" + string.Join('/', segments);

        return field with { Reference = newReference };
    }
}
