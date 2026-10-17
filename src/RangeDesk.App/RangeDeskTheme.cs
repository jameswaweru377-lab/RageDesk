using System.Windows;
using System.Windows.Media;

namespace RangeDesk.App;

/// <summary>
/// Single source of truth for the Cobalt Flow and Arctic Light palettes.
/// Resources are DynamicResource references: replacing each shared resource
/// updates already-rendered panels and reusable WPF control templates.
/// </summary>
public static class RangeDeskTheme
{
    public const string Cobalt = "Cobalt";
    public const string Arctic = "Arctic";

    private static readonly Dictionary<string, (string light, string dark, bool color)> Colors = new()
    {
        ["V5CanvasBrush"] = ("#F2F7FE", "#05172F", false),
        ["V5SidebarBrush"] = ("#F8FBFF", "#071A33", false),
        ["V5SurfaceBrush"] = ("#FFFFFF", "#0B213D", false),
        ["V5ElevatedBrush"] = ("#F8FBFF", "#0F2B4D", false),
        ["V5SelectedBrush"] = ("#E5F2FF", "#153C6D", false),
        ["V5OutlineBrush"] = ("#D7E5F4", "#21476E", false),
        ["V5AccentBrush"] = ("#147DF3", "#147DF3", false),
        ["V5AccentLightBrush"] = ("#166CC7", "#58B8FF", false),
        ["V5TextBrush"] = ("#192F49", "#F0F6FF", false),
        ["V5MutedBrush"] = ("#63809D", "#9AB5D2", false),
        ["V5SuccessBrush"] = ("#0C9075", "#43D9A4", false),
        ["V5WarningBrush"] = ("#BC7717", "#F0BE73", false),
        ["V5DangerBrush"] = ("#C4445C", "#F18BA0", false),
        ["V5LightCanvas"] = ("#F2F7FE", "#071B36", false),
        ["V5LightSurface"] = ("#FFFFFF", "#0B213D", false),
        ["V5LightBorder"] = ("#D7E5F4", "#21476E", false),
        ["V5LightText"] = ("#192F49", "#F0F6FF", false),
        ["V5LightMuted"] = ("#63809D", "#9AB5D2", false),
        ["V5LightSelected"] = ("#E5F2FF", "#153C6D", false),
        ["V5LightBlue"] = ("#147DF3", "#319DFF", false),
        ["FlowForeground7798C4"] = ("#7798C4", "#A5BFDD", false),
        ["FlowForeground5E7E9E"] = ("#5E7E9E", "#C4D9F2", false),
        ["FlowForeground2B638F"] = ("#2B638F", "#59B5FF", false),
        ["FlowForeground44ABFF"] = ("#44ABFF", "#59B5FF", false),
        ["FlowBackgroundE6F2FE"] = ("#E6F2FE", "#112D50", false),
        ["FlowForeground2C6FB8"] = ("#2C6FB8", "#59B5FF", false),
        ["FlowForeground8CC9FF"] = ("#8CC9FF", "#59B5FF", false),
        ["FlowForegroundAFD1FA"] = ("#AFD1FA", "#E8F2FF", false),
        ["FlowForegroundA3C4F5"] = ("#A3C4F5", "#59B5FF", false),
        ["FlowForeground4E6D8E"] = ("#4E6D8E", "#C4D9F2", false),
        ["FlowForeground30AFFF"] = ("#30AFFF", "#59B5FF", false),
        ["FlowForeground37B0FC"] = ("#37B0FC", "#59B5FF", false),
        ["FlowForegroundAF6FFF"] = ("#AF6FFF", "#59B5FF", false),
        ["FlowForeground79AAFF"] = ("#79AAFF", "#59B5FF", false),
        ["FlowForegroundFFB33C"] = ("#FFB33C", "#FFB33C", false),
        ["FlowForeground34D7A0"] = ("#34D7A0", "#34D7A0", false),
        ["FlowFill229BFA"] = ("#229BFA", "#183B60", false),
        ["FlowFill985DF5"] = ("#985DF5", "#183B60", false),
        ["FlowFill37DDA3"] = ("#37DDA3", "#12493F", false),
        ["FlowFillFF9E31"] = ("#FF9E31", "#22436A", false),
        ["FlowFill2CB5FF"] = ("#2CB5FF", "#183B60", false),
        ["FlowBackgroundDDEFFF"] = ("#DDEFFF", "#112D50", false),
        ["FlowForeground192F49"] = ("#192F49", "#F0F6FF", false),
        ["FlowFillFDC443"] = ("#FDC443", "#173759", false),
        ["FlowFill19BF78"] = ("#19BF78", "#12493F", false),
        ["FlowFill38A5FF"] = ("#38A5FF", "#183B60", false),
        ["FlowBackgroundF7FBFF"] = ("#F7FBFF", "#0E2541", false),
        ["FlowForeground99C8F4"] = ("#99C8F4", "#59B5FF", false),
        ["FlowForeground9EBDE2"] = ("#9EBDE2", "#A5BFDD", false),
        ["FlowBackgroundFFFFFF"] = ("#FFFFFF", "#0B213D", false),
        ["FlowBackgroundE9F2FC"] = ("#E9F2FC", "#112D50", false),
        ["FlowBackgroundE7F2FD"] = ("#E7F2FD", "#112D50", false),
        ["FlowForeground49AEFF"] = ("#49AEFF", "#59B5FF", false),
        ["FlowBorderBrushD3E5F9"] = ("#D3E5F9", "#224A70", false),
        ["FlowColorECF7FF"] = ("#ECF7FF", "#071B37", true),
        ["FlowColorDDEFFF"] = ("#DDEFFF", "#0C2F60", true),
        ["FlowColorCBE5FF"] = ("#CBE5FF", "#154680", true),
        ["FlowFillB6D8FB"] = ("#B6D8FB", "#173759", false),
        ["FlowFillD9EAFC"] = ("#D9EAFC", "#112D50", false),
        ["FlowFillB8D9F7"] = ("#B8D9F7", "#173759", false),
        ["FlowStroke0879DC"] = ("#0879DC", "#59B5FF", false),
        ["FlowForeground577795"] = ("#577795", "#C4D9F2", false),
        ["FlowForeground587C9E"] = ("#587C9E", "#C4D9F2", false),
        ["FlowForeground107AF6"] = ("#107AF6", "#59B5FF", false),
        ["FlowForeground137CF9"] = ("#137CF9", "#59B5FF", false),
        ["FlowForeground2C6BB1"] = ("#2C6BB1", "#59B5FF", false),
        ["FlowForeground39ACFF"] = ("#39ACFF", "#59B5FF", false),
        ["FlowForegroundFDBD58"] = ("#FDBD58", "#FDBD58", false),
        ["FlowForegroundB7A0FC"] = ("#B7A0FC", "#59B5FF", false),
        ["FlowForeground4BE3B3"] = ("#4BE3B3", "#4BE3B3", false),
        ["FlowForegroundFA8496"] = ("#FA8496", "#FA8496", false),
        ["FlowAlternatingRowBackgroundFAFCFF"] = ("#FAFCFF", "#0E2541", false),
        ["FlowColorDDECFF"] = ("#DDECFF", "#112D50", true),
        ["FlowColorF0F6FF"] = ("#F0F6FF", "#0E2541", true),
        ["FlowBackgroundEEF6FF"] = ("#EEF6FF", "#112D50", false),
        ["FlowForeground256EBB"] = ("#256EBB", "#59B5FF", false),
        ["FlowBackgroundE8F3FF"] = ("#E8F3FF", "#112D50", false),
        ["FlowForeground1686F4"] = ("#1686F4", "#59B5FF", false),
        ["FlowBackground930C2037"] = ("#930C2037", "#930C2037", false),
        ["FlowBackgroundE8F2FF"] = ("#E8F2FF", "#112D50", false),
        ["FlowBackgroundE4F3FF"] = ("#E4F3FF", "#112D50", false),
        ["FlowForeground255BA0"] = ("#255BA0", "#59B5FF", false),
        ["FlowBackgroundE5EEF9"] = ("#E5EEF9", "#112D50", false),
        ["FlowBackgroundF1F7FE"] = ("#F1F7FE", "#0E2541", false),
        ["FlowBorderBrushE2EAF5"] = ("#E2EAF5", "#224A70", false),
        ["FlowBackgroundF2F8FF"] = ("#F2F8FF", "#0E2541", false),
        ["FlowBackgroundF5F9FE"] = ("#F5F9FE", "#0E2541", false),
    };

    public static void Apply(string theme)
    {
        if (theme is not (Cobalt or Arctic))
            throw new ArgumentException("Theme must be Cobalt or Arctic.", nameof(theme));

        var resources = Application.Current.Resources.MergedDictionaries[0];
        foreach (var (key, palette) in Colors)
        {
            var hex = theme == Cobalt ? palette.dark : palette.light;
            var color = (Color)ColorConverter.ConvertFromString(hex)!;
            resources[key] = palette.color ? color : new SolidColorBrush(color);
        }
    }
}
