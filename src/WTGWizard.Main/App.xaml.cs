using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using WTGWizard.Main.DeploymentCore.Builders;
using WTGWizard.Shared.Services.DiskServices;
using WTGWizard.Shared.Services.Logger;
using WTGWizard.Shared.Services.WimService;
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
        // 兜底：强制终止残留 Worker 进程（异常路径防泄漏，正常路径已由关闭流程处理）
        foreach (var w in System.Diagnostics.Process.GetProcessesByName("WTGWizard.Worker"))
        {
            try { w.Kill(entireProcessTree: true); } catch { /* best effort */ }
        }

        WimService.Cleanup();

        var logger = Services.GetService<ILoggerService>();
        logger?.Shutdown();

        // 清理临时目录（部署中断残留兜底）
        TempFileManager.CleanupAll();

        // 释放映像句柄占用（进程退出 OS 亦会回收，显式释放保持整洁）
        ImageFileGuard.Release();

        // TODO: 程序关闭后有概率触发Access violation异常，等待进一步排查。
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
        services.AddSingleton<IDiskIOService, DiskIOService>();

        // 3. WIM 服务
        services.AddSingleton<IWimService, WimService>();

        // 4. 盘符分配服务 (依赖 IDiskIOService + ILoggerService)
        services.AddSingleton<IDriveLetterService, DriveLetterService>();

        // 5. ViewModel
        services.AddSingleton<WizardViewModel>();

        return services.BuildServiceProvider();
    }
}
