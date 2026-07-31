using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using System;
using System.IO;
using Windows.Graphics;

namespace WTGWizard.Helpers;

/// <summary>
/// 窗口管理辅助类 — 默认尺寸、最小尺寸、DPI 感知、窗口图标。
/// </summary>
internal static class WindowHelper
{
    /// <summary>
    /// 设置窗口图标（任务栏、Alt+Tab、任务栏缩略图角落图标）。
    /// 使用 AppWindow.SetIcon(iconPath) 注册完整多尺寸图标集，
    /// 覆盖任务栏、缩略图角落、Alt+Tab/多任务视图等各尺寸消费方。
    /// 图标文件缺失时自动从 exe 内嵌资源（EmbeddedResource）解出，无磁盘文件依赖。
    /// </summary>
    /// <remarks>
    /// 图标资源加载思路参考 Starward 项目 (https://github.com/Scighost/Starward)
    /// src/Starward/Frameworks/WindowEx.cs 的 SetIcon 方法。
    /// 实验结论（WinAppSDK 2.3）：SetIcon(IconId) 无论图标来自 LoadIcon 还是
    /// ExtractAssociatedIcon（MAUI 同款），都仅注册单尺寸图标，导致
    /// 任务栏缩略图角落/多任务视图无图标；WM_SETICON 对 WinUI 组合窗口无效。
    /// 实测仅 AppWindow.SetIcon(iconPath) 能注册完整多尺寸图标集并覆盖所有位置。
    /// </remarks>
    public static void SetIcon(Window window)
    {
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppLogo.ico");
            if (!File.Exists(iconPath))
            {
                // 自恢复：从 exe 内嵌资源解出图标（源文件被删除后重启即重建）
                Directory.CreateDirectory(Path.GetDirectoryName(iconPath)!);
                using var stream = typeof(WindowHelper).Assembly.GetManifestResourceStream("WTGWizard.Assets.AppLogo.ico");
                if (stream is null)
                    return;
                using var file = File.Create(iconPath);
                stream.CopyTo(file);
            }
            window.AppWindow.SetIcon(iconPath);
        }
        catch { }
    }

    /// <summary>
    /// 设置窗口默认尺寸（自动考虑 DPI 缩放）。
    /// 应在窗口加载后调用（需 XamlRoot 可用）。
    /// </summary>
    public static void SetWindowSize(Window window, double width, double height)
    {
        if (window.Content is not FrameworkElement windowContent)
            return;
        if (windowContent.XamlRoot is null)
            return;

        var scale = windowContent.XamlRoot.RasterizationScale;
        window.AppWindow.Resize(new SizeInt32(
            (int)(width * scale),
            (int)(height * scale)));
    }

    /// <summary>
    /// 设置窗口最小尺寸（自动考虑 DPI 缩放）。
    /// 应在窗口加载后调用（需 XamlRoot 可用）。
    /// </summary>
    public static void SetWindowMinSize(Window window, double width, double height)
    {
        if (window.Content is not FrameworkElement windowContent)
            return;
        if (windowContent.XamlRoot is null)
            return;
        if (window.AppWindow.Presenter is not OverlappedPresenter presenter)
            return;

        var scale = windowContent.XamlRoot.RasterizationScale;
        presenter.PreferredMinimumWidth = (int)(width * scale);
        presenter.PreferredMinimumHeight = (int)(height * scale);
    }
}
