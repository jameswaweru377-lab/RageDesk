using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using RangeDesk.Core;

namespace RangeDesk.App;

/// <summary>
/// Offline visual identity and file-aware preview for the download progress window.
/// Never fetches remote art or exposes URL credentials to external services.
/// </summary>
internal static class RageDeskPopupArtwork
{
    private static readonly Brush Blue = Gradient("#23D5FF", "#0960EF");
    private static readonly Brush Face = Gradient("#42E9FF", "#157BFF");
    private static readonly Brush Back = Gradient("#137BFF", "#092A9E");
    private static Brush Gradient(string from, string to)
    {
        var brush = new LinearGradientBrush(
            (Color)ColorConverter.ConvertFromString(from)!,
            (Color)ColorConverter.ConvertFromString(to)!,
            new Point(0, 0), new Point(1, 1));
        brush.Freeze();
        return brush;
    }
    private static System.Windows.Shapes.Path Vector(string data, Brush fill) =>
        new() { Data = Geometry.Parse(data), Fill = fill, Stretch = Stretch.Uniform,
                IsHitTestVisible = false };

    // Custom faceted R. Drawn locally as vector geometry and never dependent on a bitmap asset.
    internal static FrameworkElement BrandMark(double size)
    {
        var canvas = new Canvas { Width = 64, Height = 68 };
        canvas.Children.Add(Vector("M 4,14 L 27,2 L 57,16 L 57,29 L 46,35 L 46,23 L 27,14 L 15,20 Z", Back));
        canvas.Children.Add(Vector("M 4,14 L 15,20 L 15,42 L 29,35 L 45,46 L 61,65 L 40,65 L 24,48 L 15,53 L 15,65 L 4,65 Z", Blue));
        canvas.Children.Add(Vector("M 15,28 L 46,15 L 57,21 L 57,29 L 26,46 L 15,42 L 15,34 L 45,23 L 27,14 Z", Face));
        var view = new Viewbox { Width = size, Height = size * 1.06, Stretch = Stretch.Uniform,
                                Child = canvas };
        view.Effect = new DropShadowEffect { Color = Color.FromRgb(0, 146, 255),
            BlurRadius = 15, ShadowDepth = 0, Opacity = 0.38 };
        return view;
    }

    internal static ImageSource WindowIcon()
    {
        // Render the exact same local vector mark into the native Windows title-bar icon.
        var mark = BrandMark(48);
        mark.Measure(new Size(54, 58));
        mark.Arrange(new Rect(0, 0, 54, 58));
        var bitmap = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(mark);
        bitmap.Freeze();
        return bitmap;
    }

    internal static string RecognizedSource(DownloadEntry item)
    {
        string name = item.Name;
        string host = item.Host.ToLowerInvariant();
        if (name.StartsWith("GitHubDesktopSetup", StringComparison.OrdinalIgnoreCase) &&
            (host is "desktop.githubusercontent.com" or "github.com" or "objects.githubusercontent.com"))
            return "GitHub Desktop";
        if (name.StartsWith("GoogleDriveSetup", StringComparison.OrdinalIgnoreCase) &&
            host is "dl.google.com" or "drive.google.com")
            return "Google Drive";
        return "";
    }

    internal static FrameworkElement PreviewGraphic(DownloadEntry item, string kind)
    {
        string provider = RecognizedSource(item);
        var grid = new Grid { Width = 150, Height = 117 };
        if (provider == "Google Drive")
        {
            // Locally drawn distinctive tricolor Drive mark; no remote logo requests.
            var canvas = new Canvas { Width = 100, Height = 89 };
            canvas.Children.Add(Vector("M 30,5 L 55,5 L 96,76 L 72,76 Z",
                new SolidColorBrush(Color.FromRgb(255, 194, 23))));
            canvas.Children.Add(Vector("M 30,5 L 55,5 L 18,76 L 2,55 Z",
                new SolidColorBrush(Color.FromRgb(32, 174, 103))));
            canvas.Children.Add(Vector("M 18,76 L 72,76 L 96,76 L 82,56 L 31,56 Z",
                new SolidColorBrush(Color.FromRgb(66, 133, 244))));
            grid.Children.Add(new Viewbox { Width = 104, Height = 93,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Child = canvas });
            return grid;
        }
        var plaque = new Border { Width = 115, Height = 115, CornerRadius = new CornerRadius(16),
            Background = Gradient("#1058C7", "#071B4D"), BorderBrush = Blue, BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center };
        var interior = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center };
        string glyph = kind switch
        {
            "Archive" => "ZIP", "Application" => "EXE", "Video" => "▶",
            "Music" => "♫", "Image" => "▧", "Document" => "▤", _ => "↓"
        };
        if (provider == "GitHub Desktop") glyph = "GH";
        interior.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe UI"),
            FontWeight = FontWeights.Bold, FontSize = glyph.Length > 1 ? 29 : 55,
            Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
        plaque.Child = interior;
        plaque.Effect = new DropShadowEffect { Color = Color.FromRgb(4, 117, 255),
            BlurRadius = 20, ShadowDepth = 0, Opacity = 0.36 };
        grid.Children.Add(plaque);
        return grid;
    }

    internal static async Task<ImageSource?> TryReadLocalExecutableIconAsync(DownloadEntry item)
    {
        if (item.Status != DownloadStatus.Completed ||
            Path.GetExtension(item.Destination).ToLowerInvariant() is not (".exe" or ".msi") ||
            !File.Exists(item.Destination))
            return null;

        string path = item.Destination;
        // The shell extracts the icon from an existing local file; it is never executed.
        return await Task.Run(() =>
        {
            SHFILEINFO info = new();
            try
            {
                IntPtr result = SHGetFileInfo(path, 0, ref info,
                    (uint)Marshal.SizeOf<SHFILEINFO>(), 0x00000100u | 0x00000000u);
                if (result == IntPtr.Zero || info.hIcon == IntPtr.Zero) return null;
                var bitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(info.hIcon,
                    Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(96, 96));
                bitmap.Freeze();
                return (ImageSource)bitmap;
            }
            catch (Exception ex) when (ex is ExternalException or IOException or ArgumentException)
            {
                return null;
            }
            finally
            {
                if (info.hIcon != IntPtr.Zero) DestroyIcon(info.hIcon);
            }
        });
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFO
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)]
        public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfo(string path, uint attributes,
        ref SHFILEINFO fileInfo, uint fileInfoSize, uint flags);
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyIcon(IntPtr handle);
}
