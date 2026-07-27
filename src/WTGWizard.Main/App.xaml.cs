using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using WTGWizard.Shared.Services.Disk;
using WTGWizard.Shared.Services.Logger;
using WTGWizard.Shared.Services.Wim;
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
        // TODO: WimService.Cleanup();导致Access violation问题，等待后续排查。
        //WimService.Cleanup();

        var logger = Services.GetService<ILoggerService>();
        logger?.Shutdown();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        // 未处理异常：可以添加崩溃报告逻辑
    }
    
    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        // 1. 基础日志服务 (工厂注册, 因为构造函数参数不是 DI 服务)
        services.AddSingleton<ILoggerService>(sp => new LoggerService());

        // 2. 磁盘服务
        services.AddSingleton<IDiskService, DiskService>();

        // 3. WIM 服务
        services.AddSingleton<IWimService, WimService>();

        // 4. 盘符分配服务 (依赖 IDiskService + ILoggerService)
        services.AddSingleton<IDriveLetterService, DriveLetterService>();

        // 5. 磁盘监视器
        services.AddSingleton<DiskWatcherService>();

        // 6. ViewModel
        services.AddSingleton<WizardViewModel>();

        return services.BuildServiceProvider();
    }
}
