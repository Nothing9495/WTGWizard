using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace WTGWizard.Helpers;

/// <summary>
/// 窗口管理辅助类 — 默认尺寸、最小尺寸、DPI 感知。
/// </summary>
internal static class WindowHelper
{
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
