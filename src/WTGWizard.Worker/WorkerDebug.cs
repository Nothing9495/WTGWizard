using System;

namespace WTGWizard.Worker;

/// <summary>
/// Worker 调试输出 — stdout 输出关键字段，仅当 --debug 参数或 DEBUG 构建标志启用。
/// </summary>
internal static class WorkerDebug
{
    public static bool Enabled { get; private set; }

    public static void Initialize(string[] args)
    {
#if DEBUG
        Enabled = true;   // DEBUG 构建默认开启
#else
        Enabled = false;
#endif
        // --debug 参数可强制开启（Release 构建手动调试）
        if (Array.Exists(args, a => a.Equals("--debug", StringComparison.OrdinalIgnoreCase)))
            Enabled = true;
    }

    public static void Write(string message)
    {
        if (!Enabled) return;
        Console.WriteLine($"[DBG] {message}");
    }

    /// <summary>
    /// 超时值的日志文本：&lt;= 0 表示不设超时，显示 "none"（而不是容易被读成"瞬间超时"的 "0ms"）。
    /// </summary>
    public static string TimeoutLabel(int timeoutMs) => timeoutMs > 0 ? $"{timeoutMs}ms" : "none";
}
