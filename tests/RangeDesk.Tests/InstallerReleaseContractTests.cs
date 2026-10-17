using Xunit;

namespace RangeDesk.Tests;

/// <summary>
/// Static release safety checks; Windows CI also compiles and actually
/// installs/uninstalls the resulting installer executable.
/// </summary>
public sealed class InstallerReleaseContractTests
{
    private static string Source(string path)
    {
        foreach (var root in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (DirectoryInfo? dir = new(root); dir is not null; dir = dir.Parent)
            {
                string candidate = Path.Combine(dir.FullName,
                    path.Replace('/', Path.DirectorySeparatorChar));
                if (File.Exists(candidate)) return File.ReadAllText(candidate);
            }
        }
        throw new FileNotFoundException("Missing release source: " + path);
    }

    [Fact]
    public void InstallerSupportsTwoLanguagesRealLicensesAndWindowsUninstall()
    {
        string setup = Source("installer/RangeDesk.iss");
        Assert.Contains("AppId={{F7B609F5-0C3F-4E1E-8D9C-70214C371CF4}", setup);
        Assert.Contains("Name: \"en\"; MessagesFile:", setup);
        Assert.Contains("Name: \"sw\"; MessagesFile:", setup);
        Assert.Contains("sw.LanguageName=Kiswahili", setup);
        Assert.Contains("LicenseFile: \"licenses\\License-en.txt\"", setup);
        Assert.Contains("LicenseFile: \"licenses\\License-sw.txt\"", setup);
        Assert.Contains("PrivilegesRequiredOverridesAllowed=dialog", setup);
        Assert.Contains("PrivilegesRequired=admin", setup);
        Assert.Contains("UsePreviousPrivileges=yes", setup);
        Assert.Contains("--wpf-classic", setup);
        Assert.Contains("DefaultDirName={autopf}\\RangeDesk", setup);
        Assert.Contains("UninstallDisplayName=RangeDesk", setup);
        Assert.Contains("Name: \"{group}\\RangeDesk\";", setup);
        Assert.Contains("RageDesk - New UI Preview", setup);
        Assert.Contains("Parameters: \"--react-preview\"", setup);
        Assert.Contains("Filename: \"{uninstallexe}\"", setup);
        Assert.Contains("Flags: nowait postinstall skipifsilent", setup);
        Assert.DoesNotContain("[UninstallDelete]", setup);
        Assert.Contains("Preview", Source("installer/licenses/License-en.txt"));
        Assert.Contains("Masharti", Source("installer/licenses/License-sw.txt"), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GitHubReleaseRequiresExplicitSuccessfulMainWorkflowAndHonestPreviewNotes()
    {
        string workflow = Source(".github/workflows/publish-preview-release.yml");
        Assert.Contains("workflow_run:", workflow);
        Assert.Contains("conclusion == 'success'", workflow);
        Assert.Contains("head_branch == 'main'", workflow);
        Assert.Contains("[publish-preview]", workflow);
        Assert.Contains("run-id:", workflow);
        Assert.Contains("--prerelease", workflow);
        Assert.Contains("SHA256SUMS.txt", workflow);
        Assert.Contains("Uninstall", Source("releases/v0.8.0-preview.4.md"),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("<Version>0.8.0-preview.4</Version>",
            Source("src/RangeDesk.App/RangeDesk.App.csproj"));
    }
}
