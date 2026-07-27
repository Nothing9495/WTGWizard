using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using WTGWizard.ViewModels;

namespace WTGWizard.Main;

public partial class App : Application
{
    private Window? _window;
    public static MainWindow? MainWindow { get; private set; }
    internal static IServiceProvider Services { get; private set; } = null!;

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = ConfigureServices();
        _window = new MainWindow();
        MainWindow = _window as MainWindow;
        _window.Closed += OnMainWindowClosed;
        _window.Activate();
    }

    private void OnMainWindowClosed(object sender, WindowEventArgs args)
    {
        // 应用关闭时的清理逻辑
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        // 未处理异常：可以添加崩溃报告逻辑
    }
    
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton<WizardViewModel>();
        return services.BuildServiceProvider();
    }
}
