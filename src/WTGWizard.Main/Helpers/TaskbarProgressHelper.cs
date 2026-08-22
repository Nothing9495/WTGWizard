using System;
using System.Runtime.InteropServices;
using WinRT.Interop;
using WTGWizard.Main;

namespace WTGWizard.Helpers;

/// <summary>
/// 任务栏进度指示 — ITaskbarList3（COM）+ FlashWindowEx（user32）。
/// 所有调用均静默降级：平台不支持或环境异常时不崩溃。
/// </summary>
internal static class TaskbarProgressHelper
{
    private static ITaskbarList3? _taskbar;
    private static bool _checked;
    private static bool _supported;

    private static bool Supported
    {
        get
        {
            if (_checked) return _supported;
            _checked = true;
            try
            {
                _taskbar = (ITaskbarList3)new TaskbarList();
                _taskbar.HrInit();
                _supported = true;
            }
            catch
            {
                _taskbar = null;
                _supported = false;
            }
            return _supported;
        }
    }

    /// <summary>部署进行中：任务栏进度条保持 Active 动画。</summary>
    public static void SetIndeterminate(MainWindow? window) => SetState(window, TBPFLAG.INDETERMINATE);

    /// <summary>失败：红色满条（determinate 需进度值才能稳定渲染）。</summary>
    public static void SetError(MainWindow? window) => SetDeterminate(window, TBPFLAG.ERROR);

    /// <summary>中止/暂停：黄色满条。</summary>
    public static void SetPaused(MainWindow? window) => SetDeterminate(window, TBPFLAG.PAUSED);

    /// <summary>清除任务栏进度。</summary>
    public static void Clear(MainWindow? window) => SetState(window, TBPFLAG.NOPROGRESS);

    /// <summary>determinate 状态（红/黄）：先设状态再设满值，避免过渡期显示 NORMAL。</summary>
    private static void SetDeterminate(MainWindow? window, TBPFLAG flag)
    {
        if (window is null || !Supported) return;
        try
        {
            var hwnd = GetHwnd(window);
            _taskbar!.SetProgressState(hwnd, flag);
            _taskbar.SetProgressValue(hwnd, 100, 100);
        }
        catch
        {
            // 平台不支持时静默降级
        }
    }

    /// <summary>任务栏按钮高亮闪烁（系统标准闪烁）。</summary>
    public static void Flash(MainWindow? window, uint count = 3, uint timeoutMs = 500)
    {
        if (window is null) return;
        try
        {
            var info = new FLASHWINFO
            {
                cbSize = (uint)Marshal.SizeOf<FLASHWINFO>(),
                hwnd = GetHwnd(window),
                dwFlags = FLASHW_ALL,
                uCount = count,
                dwTimeout = timeoutMs
            };
            FlashWindowEx(ref info);
        }
        catch
        {
            // 闪烁失败不影响主流程
        }
    }

    private static void SetState(MainWindow? window, TBPFLAG flag)
    {
        if (window is null || !Supported) return;
        try
        {
            _taskbar!.SetProgressState(GetHwnd(window), flag);
        }
        catch
        {
            // 平台不支持时静默降级
        }
    }

    private static nint GetHwnd(MainWindow? window) => WindowNative.GetWindowHandle(window!);

    // ═══ ITaskbarList3 (COM, shobjidl_core) ═══

    [Flags]
    private enum TBPFLAG
    {
        NOPROGRESS = 0,
        INDETERMINATE = 0x1,
        NORMAL = 0x2,
        ERROR = 0x4,
        PAUSED = 0x8,
    }

    [ComImport]
    [Guid("ea1afb91-9e28-4b86-90e9-9e9f8a5eefaf")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ITaskbarList3
    {
        // ITaskbarList
        void HrInit();
        // ITaskbarList2
        void AddTab(IntPtr hwnd);
        void DeleteTab(IntPtr hwnd);
        void ActivateTab(IntPtr hwnd);
        void SetActiveAlt(IntPtr hwnd);
        // ITaskbarList3
        void MarkFullscreenWindow(IntPtr hwnd, [MarshalAs(UnmanagedType.Bool)] bool fullscreen);
        void SetProgressValue(IntPtr hwnd, ulong ullCompleted, ulong ullTotal);
        void SetProgressState(IntPtr hwnd, TBPFLAG flag);
        void RegisterTab(IntPtr hwndTab, IntPtr hwndMDI);
        void UnregisterTab(IntPtr hwndTab);
        void SetTabOrder(IntPtr hwndTab, IntPtr hwndInsertBefore);
        void SetTabActive(IntPtr hwndTab, IntPtr hwndMDI);
        void ThumbBarAddButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
        void ThumbBarUpdateButtons(IntPtr hwnd, uint cButtons, IntPtr pButton);
        void ThumbBarSetImageList(IntPtr hwnd, IntPtr himl);
        void SetOverlayIcon(IntPtr hwnd, IntPtr icon, [MarshalAs(UnmanagedType.LPWStr)] string pszDescription);
        void SetThumbnailTooltip(IntPtr hwnd, [MarshalAs(UnmanagedType.LPWStr)] string pszTip);
        void SetThumbnailClip(IntPtr hwnd, IntPtr prcClip);
    }

    [ComImport]
    [Guid("56fdf344-fd6d-11d0-958a-006097c9a090")]
    private class TaskbarList { }

    // ═══ FlashWindowEx (user32) ═══

    private const uint FLASHW_ALL = 0x00000003;

    [StructLayout(LayoutKind.Sequential)]
    private struct FLASHWINFO
    {
        public uint cbSize;
        public IntPtr hwnd;
        public uint dwFlags;
        public uint uCount;
        public uint dwTimeout;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlashWindowEx(ref FLASHWINFO pwfi);
}
