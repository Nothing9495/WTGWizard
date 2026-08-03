using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
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
    }

    private void DebugToggle_Toggled(object sender, RoutedEventArgs e)
    {
        WorkerSettings.EnableDebugOutput = DebugToggle.IsOn;
    }
}
