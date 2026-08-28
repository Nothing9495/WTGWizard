using System;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using WTGWizard.Main;
using WTGWizard.Main.DeploymentCore.Worker;
using WTGWizard.Main.Language;

namespace WTGWizard.Pages;

/// <summary>
/// 设置页面。
/// </summary>
public sealed partial class SettingsPage : Page
{
    public string AboutDesc1 => string.Format(
        Lang.Page_Settings_AboutSection_Desc1, GetProductVersion());

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

    private static string GetProductVersion()
    {
        var info = typeof(App).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(info)) return "?";
        var plus = info.IndexOf('+', StringComparison.Ordinal);
        return plus >= 0 ? info[..plus] : info;
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
