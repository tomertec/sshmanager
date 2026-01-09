using SshManager.Core.Models;
using Wpf.Ui.Controls;

namespace SshManager.App.Models;

/// <summary>
/// Represents an item in the group filter dropdown.
/// </summary>
public class GroupFilterItem
{
    public string Name { get; set; } = "";
    public SymbolRegular Icon { get; set; } = SymbolRegular.Grid24;
    public int Count { get; set; }
    public bool HasCount { get; set; }
    public HostGroup? Group { get; set; }

    /// <summary>
    /// Creates the "All Groups" filter item.
    /// </summary>
    public static GroupFilterItem CreateAllItem(int totalCount) => new()
    {
        Name = "All Groups",
        Icon = SymbolRegular.Grid24,
        Count = totalCount,
        HasCount = true,
        Group = null
    };

    /// <summary>
    /// Creates a filter item for a specific group.
    /// </summary>
    public static GroupFilterItem CreateGroupItem(HostGroup group, int hostCount) => new()
    {
        Name = group.Name,
        Icon = SymbolRegular.Folder24,
        Count = hostCount,
        HasCount = true,
        Group = group
    };
}
