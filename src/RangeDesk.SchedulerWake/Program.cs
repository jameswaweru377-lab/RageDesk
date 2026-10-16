using System.Diagnostics;
using System.Text.Json;
using RangeDesk.Core;

// Explicit opt-in Windows Task Scheduler companion. It does not download,
// communicate remotely, read cookies, or launch downloaded files.
if (!OperatingSystem.IsWindows()) return;
if (args.Length != 0) return; // no additional command interface

using var exclusive = new Mutex(false, @"Local\RangeDesk.SchedulerWake");
bool held=false;
try
{
    try { held = exclusive.WaitOne(0); }
    catch (AbandonedMutexException) { held=true; }
    if (!held) return;

    string profile=Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"RangeDesk");
    string scheduleFile=Path.Combine(profile,"schedules-v5.json");
    string catalogFile=Path.Combine(profile,"catalog-v5.json");
    if (!File.Exists(scheduleFile) || !File.Exists(catalogFile)) return;

    var tracked=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using (var doc=JsonDocument.Parse(await File.ReadAllTextAsync(catalogFile)))
    {
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return;
        foreach (var row in doc.RootElement.EnumerateArray())
            if (row.TryGetProperty("Destination",out var dest) &&
                dest.ValueKind==JsonValueKind.String &&
                dest.GetString() is string path && Path.IsPathFullyQualified(path))
                tracked.Add(Path.GetFullPath(path));
    }
    var schedules=ScheduledQueueStore.Load(scheduleFile);
    bool running=Process.GetProcessesByName("RangeDesk").Length>0;
    if (!ScheduledWakeDecision.ShouldWake(schedules,tracked,File.Exists,
        DateTimeOffset.Now,running)) return;
    string gui=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","RangeDesk.exe"));
    if (!File.Exists(gui)) return;
    Process.Start(new ProcessStartInfo(gui) { UseShellExecute=true,
        WorkingDirectory=Path.GetDirectoryName(gui)! });
}
catch (Exception)
{
    // Task Scheduler reports exit status. Do not print download URLs or paths.
    Environment.ExitCode=1;
}
finally {if(held) exclusive.ReleaseMutex();}
