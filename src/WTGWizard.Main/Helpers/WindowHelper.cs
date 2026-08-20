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
    /// 设置窗口尺寸。setInitialSize=true（首次启动）：设置初始宽高（基准 × 可用缩放，
    /// 默认保持基准全尺寸，屏幕放不下才缩小）、最小宽高并居中；
    /// setInitialSize=false（运行时 DPI 变换）：仅重新计算并设置最小宽高，
    /// 不主动修改当前宽高（由系统 DPI 缩放处理）与窗口位置。
    /// 应在窗口加载后调用（需 XamlRoot 可用）。
    /// </summary>
    public static void SetWindowSize(Window window, double baseWidth, double baseHeight,
        double minScale = 0.85, double screenRatio = 0.9, bool setInitialSize = true)
    {
        if (window.Content is not FrameworkElement windowContent)
            return;
        if (windowContent.XamlRoot is null)
            return;
        if (window.AppWindow.Presenter is not OverlappedPresenter presenter)
            return;

        var display = DisplayArea.GetFromWindowId(window.AppWindow.Id, DisplayAreaFallback.Nearest);
        var workArea = display.WorkArea;

        var scale = windowContent.XamlRoot.RasterizationScale;

        // 基准尺寸（DIP）→ 物理像素；防除零
        double basePxW = baseWidth * scale;
        double basePxH = baseHeight * scale;
        if (basePxW <= 0 || basePxH <= 0)
            return;

        // 最小宽高：基准 × min(minScale, 可用缩放)，极小屏进一步降低
        double availableScale = Math.Min(
            workArea.Width * screenRatio / basePxW,
            workArea.Height * screenRatio / basePxH);
        double effectiveMinScale = Math.Min(minScale, availableScale);
        presenter.PreferredMinimumWidth = Math.Max(1, (int)(basePxW * effectiveMinScale));
        presenter.PreferredMinimumHeight = Math.Max(1, (int)(basePxH * effectiveMinScale));

        if (!setInitialSize)
            return; // DPI 变换：仅刷新最小宽高，不触碰当前尺寸与位置

        // 首次启动：初始宽高（默认保持基准全尺寸，屏幕放不下才缩小）+ 居中
        double fitScale = Math.Min(1.0, availableScale);
        int width = Math.Max(1, (int)(basePxW * fitScale));
        int height = Math.Max(1, (int)(basePxH * fitScale));
        int x = workArea.X + (workArea.Width - width) / 2;
        int y = workArea.Y + (workArea.Height - height) / 2;

        window.AppWindow.MoveAndResize(new RectInt32(x, y, width, height));
    }
}
