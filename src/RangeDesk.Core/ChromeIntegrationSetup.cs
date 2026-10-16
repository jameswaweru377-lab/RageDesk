using Microsoft.Win32;
using System.Runtime.Versioning;
using System.Text.Json;

namespace RangeDesk.Core;

/// <summary>
/// Registers only RangeDesk's own Chrome native-messaging bridge for the
/// current Windows user. It does NOT install or enable a Chrome extension.
/// The preview's pinned public manifest key gives unpacked installations
/// a stable ID across folders; no private key is distributed.
/// </summary>
public static class ChromeIntegrationSetup
{
    public const string ExtensionId = "pknbhiklbdmhgdohieapjoipkdklahlp";
    public const string HostName = "com.rangedesk.capture";
    public const string ChromeExtensionsPage = "chrome://extensions";
    private const string RegistryPath = @"Software\Google\Chrome\NativeMessagingHosts\com.rangedesk.capture";

    public static string ManifestPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RangeDesk", "native-host-manifests", HostName + "-chrome.json");

    public static string ExtensionFolder(string appFolder) =>
        Path.Combine(Path.GetFullPath(appFolder), "BrowserCapture", "chromium");

    public static string HostExecutable(string appFolder) =>
        Path.Combine(Path.GetFullPath(appFolder), "NativeHost", "RangeDesk.NativeHost.exe");

    public static string BuildManifest(string hostExecutable)
    {
        if (string.IsNullOrWhiteSpace(hostExecutable) || !Path.IsPathFullyQualified(hostExecutable))
            throw new ArgumentException("A full native-host executable path is required.");
        return JsonSerializer.Serialize(new
        {
            name = HostName,
            description = "RangeDesk user-approved Chrome direct-link handoff",
            path = Path.GetFullPath(hostExecutable),
            type = "stdio",
            allowed_origins = new[] { "chrome-extension://" + ExtensionId + "/" }
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    [SupportedOSPlatform("windows")]
    public static void Register(string appFolder)
    {
        string exe = HostExecutable(appFolder);
        if (!File.Exists(exe))
            throw new FileNotFoundException("The RangeDesk native host is missing. Extract the complete Windows preview ZIP.");
        if (!File.Exists(Path.Combine(ExtensionFolder(appFolder), "manifest.json")))
            throw new FileNotFoundException("The Chrome extension folder is missing. Extract the complete Windows preview ZIP.");

        var manifest = ManifestPath;
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        string temp = manifest + ".tmp";
        try
        {
            File.WriteAllText(temp, BuildManifest(exe));
            File.Move(temp, manifest, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }

        using var key = Registry.CurrentUser.CreateSubKey(RegistryPath, writable: true)
            ?? throw new IOException("Could not write the current-user Chrome native host registration.");
        key.SetValue("", manifest, RegistryValueKind.String);
    }

    /// <summary>
    /// Local manifest/registry readiness only. The installed Chrome extension
    /// must independently ping the host; don't present this as proof it is loaded.
    /// </summary>
    [SupportedOSPlatform("windows")]
    public static bool IsRegistered(string appFolder)
    {
        using var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false);
        string manifest = ManifestPath;
        if (key?.GetValue("") is not string registered ||
            !string.Equals(Path.GetFullPath(registered), manifest, StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(manifest) || !File.Exists(HostExecutable(appFolder))) return false;
        try
        {
            using var data = JsonDocument.Parse(File.ReadAllText(manifest));
            var root = data.RootElement;
            if (root.GetProperty("name").GetString() != HostName ||
                root.GetProperty("type").GetString() != "stdio" ||
                !string.Equals(Path.GetFullPath(root.GetProperty("path").GetString()!),
                    HostExecutable(appFolder), StringComparison.OrdinalIgnoreCase)) return false;
            var origins = root.GetProperty("allowed_origins");
            return origins.ValueKind == JsonValueKind.Array && origins.GetArrayLength() == 1 &&
                   origins[0].GetString() == "chrome-extension://" + ExtensionId + "/";
        }
        catch (Exception ex) when (ex is JsonException or IOException or ArgumentException
            or KeyNotFoundException or NullReferenceException or InvalidOperationException) { return false; }
    }

    [SupportedOSPlatform("windows")]
    public static bool Disconnect()
    {
        string manifest = ManifestPath;
        using (var key = Registry.CurrentUser.OpenSubKey(RegistryPath, writable: false))
        {
            // Never remove a different application's or user's registration.
            if (key?.GetValue("") is not string registered ||
                !string.Equals(registered, manifest, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        Registry.CurrentUser.DeleteSubKeyTree(RegistryPath, throwOnMissingSubKey: false);
        if (File.Exists(manifest)) File.Delete(manifest);
        return true;
    }
}
