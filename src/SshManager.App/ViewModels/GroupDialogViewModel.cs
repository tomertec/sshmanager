using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SshManager.Core;
using SshManager.Core.Models;

namespace SshManager.App.ViewModels;

public partial class GroupDialogViewModel : ObservableObject
{
    private readonly HostGroup _originalGroup;

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string? _icon;

    [ObservableProperty]
    private int _statusCheckIntervalSeconds = 30;

    [ObservableProperty]
    private bool _isNewGroup;

    [ObservableProperty]
    private GroupColors.ColorOption _selectedColor;

    public string Title => IsNewGroup ? "Add Group" : "Edit Group";

    public bool? DialogResult { get; private set; }

    public event Action? RequestClose;

    // Available icons for groups
    public IReadOnlyList<string> AvailableIcons { get; } = new[]
    {
        "Folder24",
        "FolderOpen24",
        "Cloud24",
        "Server24",
        "Desktop24",
        "Database24",
        "Globe24",
        "Home24",
        "Building24",
        "Organization24"
    };

    /// <summary>
    /// Gets all available color options for groups.
    /// </summary>
    public IReadOnlyList<GroupColors.ColorOption> AvailableColors { get; } = GroupColors.All;

    public GroupDialogViewModel(HostGroup? group = null)
    {
        _originalGroup = group ?? new HostGroup();
        IsNewGroup = group == null;

        Name = _originalGroup.Name;
        Icon = _originalGroup.Icon ?? "Folder24";
        StatusCheckIntervalSeconds = _originalGroup.StatusCheckIntervalSeconds > 0
            ? _originalGroup.StatusCheckIntervalSeconds
            : 30;

        // Initialize selected color from the group
        _selectedColor = GroupColors.GetByHexValue(_originalGroup.Color);
    }

    [RelayCommand]
    private void Save()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            return;
        }

        DialogResult = true;
        RequestClose?.Invoke();
    }

    [RelayCommand]
    private void Cancel()
    {
        DialogResult = false;
        RequestClose?.Invoke();
    }

    public HostGroup GetGroup()
    {
        _originalGroup.Name = Name.Trim();
        _originalGroup.Icon = Icon;
        _originalGroup.StatusCheckIntervalSeconds = Math.Max(StatusCheckIntervalSeconds, 5);
        _originalGroup.Color = SelectedColor.HexValue;
        return _originalGroup;
    }
}
