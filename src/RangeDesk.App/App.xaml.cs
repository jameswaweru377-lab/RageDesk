using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace RangeDesk.App;

public partial class App : Application
{
    private static readonly string DiagnosticDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RangeDesk", "diagnostics");

    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex)
                RecordException("fatal", ex);
        };
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            RecordException("unobserved-task", args.Exception);
            args.SetObserved();
        };
    }

    private static void OnDispatcherUnhandledException(object sender,
        DispatcherUnhandledExceptionEventArgs args)
    {
        string location = RecordException("ui", args.Exception);
        MessageBox.Show(
            "RangeDesk encountered an unexpected problem. Please keep your partial download files. " +
            "Technical information was saved locally at:\n" + location +
            "\n\nYou can share the error type and stack trace without sharing download URLs or private files.",
            "RangeDesk problem",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        // Avoid silently closing the entire window for a recoverable UI exception.
        // Serious process-level exceptions are still handled by the runtime.
        args.Handled = true;
    }

    private static string RecordException(string category, Exception ex)
    {
        try
        {
            Directory.CreateDirectory(DiagnosticDirectory);
            string file = Path.Combine(DiagnosticDirectory, "crashes.log");
            // Exception messages may contain signed URLs or other secrets;
            // persist type and stack only, never request headers or query strings.
            string text = $"{DateTimeOffset.UtcNow:O} [{category}] {ex.GetType().FullName}{Environment.NewLine}" +
                (ex.StackTrace ?? "No stack available") + Environment.NewLine + Environment.NewLine;
            File.AppendAllText(file, text);
            return file;
        }
        catch
        {
            return Path.Combine(DiagnosticDirectory, "crashes.log");
        }
    }
}
