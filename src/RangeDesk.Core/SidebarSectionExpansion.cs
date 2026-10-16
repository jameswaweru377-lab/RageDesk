namespace RangeDesk.Core;

/// <summary>
/// Presentation-only navigation preferences. Expanding or collapsing a group
/// never changes the active download filter or any transfer state.
/// </summary>
public sealed record SidebarSectionExpansion(
    bool Categories = true, bool Labels = true, bool Sites = true)
{
    public bool IsExpanded(string section) => section switch
    {
        "categories" => Categories,
        "labels" => Labels,
        "sites" => Sites,
        _ => throw new ArgumentOutOfRangeException(nameof(section))
    };

    public SidebarSectionExpansion Toggle(string section) => section switch
    {
        "categories" => this with { Categories = !Categories },
        "labels" => this with { Labels = !Labels },
        "sites" => this with { Sites = !Sites },
        _ => throw new ArgumentOutOfRangeException(nameof(section))
    };

    public bool IsVisible(string section, bool sidebarCollapsed) =>
        !sidebarCollapsed && IsExpanded(section);
}
