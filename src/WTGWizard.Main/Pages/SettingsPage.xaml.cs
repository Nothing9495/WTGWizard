using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WTGWizard.Main;
using WTGWizard.Main.DeploymentCore.Worker;

namespace WTGWizard.Pages;

/// <summary>
/// 设置页面。
/// </summary>
public sealed partial class SettingsPage : Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DebugToggle.IsOn = WorkerSettings.EnableDebugOutput;
        var targetTag = (App.MainWindow?.Content is FrameworkElement root)
            ? root.RequestedTheme switch
            {
                ElementTheme.Light => "light",
                ElementTheme.Dark => "dark",
                _ => "default"
            }
            : "default";
        foreach (var item in ThemeComboBox.Items)
        {
            if (item is ComboBoxItem { Tag: string tag } && tag == targetTag)
            {
                ThemeComboBox.SelectedItem = item;
                break;
            }
        }
    }

    private void DebugToggle_Toggled(object sender, RoutedEventArgs e)
    {
        WorkerSettings.EnableDebugOutput = DebugToggle.IsOn;
    }

    private void ThemeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ThemeComboBox.SelectedItem is not ComboBoxItem item || item.Tag is not string tag) return;
        if (App.MainWindow?.Content is not FrameworkElement root) return;
        root.RequestedTheme = tag switch
        {
            "dark" => ElementTheme.Dark,
            "light" => ElementTheme.Light,
            _ => ElementTheme.Default
        };
    }
}
