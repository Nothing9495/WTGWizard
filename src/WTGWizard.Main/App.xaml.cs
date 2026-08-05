using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml;
using System;
using System.Threading.Tasks;
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

        // WinUI 未处理异常：记录完整异常后继续运行（避免丢失现场）
        UnhandledException += OnUnhandledException;

        // 进程级致命异常（非 WinUI 通道）与异步任务未观察异常：记录后由系统默认处理
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        Services = ConfigureServices();
        Services.GetService<ILoggerService>()?.LogSessionStart("WTGWizard.Main");
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
        logger?.LogSessionEnd("WTGWizard.Main");
        logger?.Shutdown();

        // 清理临时目录（部署中断残留兜底）
        TempFileManager.CleanupAll();

        // 释放映像句柄占用（进程退出 OS 亦会回收，显式释放保持整洁）
        ImageFileGuard.Release();

        // TODO: 程序关闭后有概率触发Access violation异常，等待进一步排查。
    }

    /// <summary>
    /// WinUI 未处理异常：记录完整异常（含堆栈）后继续运行。
    /// </summary>
    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        try
        {
            Services?.GetService<ILoggerService>()?.Error("App",
                "Unhandled WinUI exception - ({Error}).", e.Exception.ToString());
        }
        catch
        {
            // 日志不可用时静默（应用即将进入不可靠状态，尽力而为）
        }
        e.Handled = true;
    }

    /// <summary>
    /// 进程级未处理异常（AppDomain 通道）：记录后不拦截（系统将终止进程）。
    /// </summary>
    private void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
    {
        try
        {
            var ex = e.ExceptionObject as Exception;
            Services?.GetService<ILoggerService>()?.Error("App",
                "Unhandled AppDomain exception - ({Error}).", ex?.ToString() ?? e.ExceptionObject?.ToString() ?? "Unknown");
        }
        catch
        {
            // 尽力而为
        }
    }

    /// <summary>
    /// 异步任务未观察异常：记录后标记已观察（避免进程被终止）。
    /// </summary>
    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        try
        {
            Services?.GetService<ILoggerService>()?.Error("App",
                "Unobserved task exception - ({Error}).", e.Exception.ToString());
        }
        catch
        {
            // 尽力而为
        }
        e.SetObserved();
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
