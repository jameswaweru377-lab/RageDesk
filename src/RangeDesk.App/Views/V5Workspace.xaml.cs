using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RangeDesk.App.ViewModels;
namespace RangeDesk.App.Views;

public partial class V5Workspace : UserControl
{
    public V5Workspace() => InitializeComponent();

    private void CollapseNav_Click(object sender, RoutedEventArgs e)
    {
        NavigationColumn.Width = NavigationColumn.Width.Value < 100 ? new GridLength(210) : new GridLength(65);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        var dialog = new SaveFileDialog { Title = "Choose where to save download", FileName = Path.GetFileName(vm.Destination), OverwritePrompt = false };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true) vm.Destination = dialog.FileName;
    }
}