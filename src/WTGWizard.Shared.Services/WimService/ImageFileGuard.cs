using System;
using System.IO;

namespace WTGWizard.Shared.Services.WimService;

/// <summary>
/// 映像文件句柄占用（程序生命周期）— 防止映像在程序运行期间被更名/移动/写入。
/// FileAccess.Read + FileShare.Read：本进程只读，仅允许其他进程读；
/// 不含 FileShare.Delete → 阻止其他进程删除/更名；Worker（wimlib _wopen 只读）兼容。
/// 程序自身绝不会删除或修改该文件。
/// </summary>
public static class ImageFileGuard
{
    private static readonly object _lock = new();
    private static FileStream? _handle;

    /// <summary>是否已占用（当前无调用方，作为 Guard API 预留——未来部署流程可校验源文件占用状态）。</summary>
    public static bool IsAcquired
    {
        get { lock (_lock) return _handle is not null; }
    }

    /// <summary>占用映像文件（先释放旧句柄）。失败返回错误消息。</summary>
    public static bool Acquire(string path, out string? error)
    {
        Release();
        try
        {
            lock (_lock)
                _handle = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    /// <summary>释放句柄（应用关闭时显式调用；进程退出 OS 亦会回收）。</summary>
    public static void Release()
    {
        lock (_lock)
        {
            _handle?.Dispose();
            _handle = null;
        }
    }
}
