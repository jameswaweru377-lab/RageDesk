using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>Safe, locally stored new-download rules. No shell commands, external
/// processes, hidden browser access, or moving pre-existing user files.</summary>
public sealed record DownloadAutomationRule(
    string Name, string HostSuffix, string Extension, string Category,
    string Label, string Subfolder)
{
    private static readonly HashSet<string> Categories =
        new(["Other", "Software", "Videos", "Documents", "Archives", "Images"],
            StringComparer.OrdinalIgnoreCase);

    public DownloadAutomationRule Validate()
    {
        string name = (Name ?? "").Trim();
        string host = (HostSuffix ?? "").Trim().TrimEnd('.').ToLowerInvariant();
        string extension = (Extension ?? "").Trim().ToLowerInvariant();
        string category = (Category ?? "").Trim();
        string label = (Label ?? "").Trim();
        string subfolder = (Subfolder ?? "").Trim();

        if (name.Length is < 1 or > 40 || name.Any(char.IsControl))
            throw new ArgumentException("Rule name must be 1–40 printable characters.");
        if (host.Length > 0 && (host.Length > 253 || !host.Contains('.') ||
            Uri.CheckHostName(host) != UriHostNameType.Dns))
            throw new ArgumentException("Domain must be empty or a valid DNS hostname.");
        if (extension.Length > 0 &&
            (extension.Length is < 2 or > 13 || extension[0] != '.' ||
             extension.Skip(1).Any(c => !char.IsAsciiLetterOrDigit(c))))
            throw new ArgumentException("File extension must be like .zip or .pdf.");
        if (host.Length == 0 && extension.Length == 0)
            throw new ArgumentException("Provide a domain and/or extension to match.");
        if (!Categories.Contains(category))
            throw new ArgumentException("Choose a recognized category.");
        if (label.Length > 32 || label.Any(c => char.IsControl(c) || c is ',' or ';'))
            throw new ArgumentException("Use one label (up to 32 characters, no separators).");
        if (subfolder.Length > 0 &&
            (subfolder is "." or ".." ||
             subfolder.Length > 48 ||
             subfolder.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
             subfolder.Contains('/') || subfolder.Contains('\\') ||
             subfolder.EndsWith(' ') || subfolder.EndsWith('.')))
            throw new ArgumentException("Folder must be one safe folder name, not a path.");
        return this with { Name=name, HostSuffix=host, Extension=extension,
            Category=category, Label=label, Subfolder=subfolder };
    }

    public bool Matches(string url, string fileName)
    {
        var rule = Validate();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("https" or "http")) return false;
        return (rule.HostSuffix.Length == 0 ||
                 uri.Host.Equals(rule.HostSuffix, StringComparison.OrdinalIgnoreCase) ||
                 uri.Host.EndsWith("." + rule.HostSuffix, StringComparison.OrdinalIgnoreCase))
            && (rule.Extension.Length == 0 ||
                Path.GetExtension(fileName).Equals(rule.Extension, StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record DownloadAutomationDecision(string Destination, string Category,
    string Labels, string? MatchedRule);

public static class DownloadAutomationPolicy
{
    public static DownloadAutomationDecision Apply(string url, string destination,
        string category, string labels, string defaultFolder,
        IEnumerable<DownloadAutomationRule> rules)
    {
        // The caller's chosen destination wins unless it is a normal default
        // download-folder path. A rule cannot escape the configured root.
        var folder = Path.GetFullPath(defaultFolder);
        string original = Path.GetFullPath(destination);
        var rule = rules.Select(r => r.Validate())
            .FirstOrDefault(r => r.Matches(url, Path.GetFileName(original)));
        if (rule is null)
            return new(original, category, labels, null);

        string selectedCategory = rule.Category == "Other" ? category : rule.Category;
        string selectedLabels = labels;
        if (!string.IsNullOrWhiteSpace(rule.Label))
        {
            var parts = labels.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (!parts.Contains(rule.Label, StringComparer.OrdinalIgnoreCase))
                selectedLabels = string.IsNullOrWhiteSpace(labels) ? rule.Label : labels + ", " + rule.Label;
        }
        string selectedPath = original;
        if (!string.IsNullOrWhiteSpace(rule.Subfolder) &&
            string.Equals(Path.GetDirectoryName(original), folder, StringComparison.OrdinalIgnoreCase))
        {
            selectedPath = Path.Combine(folder, rule.Subfolder, Path.GetFileName(original));
            if (!string.Equals(Path.GetFullPath(Path.GetDirectoryName(selectedPath)!), 
                 Path.Combine(folder,rule.Subfolder), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Rule attempted to write outside the selected subfolder.");
        }
        return new(selectedPath, selectedCategory, selectedLabels, rule.Name);
    }
}

public static class DownloadAutomationRuleStore
{
    public static IReadOnlyList<DownloadAutomationRule> Load(string file)
    {
        if (!File.Exists(file)) return [];
        var records = JsonSerializer.Deserialize<List<DownloadAutomationRule>>(File.ReadAllText(file))
            ?? throw new InvalidDataException("The automation rules file is empty.");
        ValidateAll(records);
        return records.Select(r => r.Validate()).ToArray();
    }

    public static void Save(string file, IEnumerable<DownloadAutomationRule> input)
    {
        var rules = input.Select(r => r.Validate()).ToArray();
        ValidateAll(rules);
        var destination = Path.GetFullPath(file);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(rules,
                new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temporary, destination, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void ValidateAll(IReadOnlyList<DownloadAutomationRule> rules)
    {
        if (rules.Count > 100) throw new InvalidDataException("Too many rules.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rules)
            if (!names.Add(r.Validate().Name))
                throw new InvalidDataException("Automation rule names must be unique.");
    }
}
