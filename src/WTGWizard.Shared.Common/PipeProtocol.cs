using System;
using System.Globalization;

namespace WTGWizard.Shared.Common;

/// <summary>
/// Pipe 协议定义 — 主进程与子进程共享的协议常量和消息构建方法。
/// </summary>
public static class PipeProtocol
{
    // ── Pipe 配置 ──
    public const int ConnectTimeoutMs = 15000;  // 子进程 15s 超时自动退出
    public const string NewLine = "\n";

    /// <summary>
    /// Worker 终态回报静默兜底：Main 等待「completed / failed / cancelled」的最长静默时间。
    /// 任何来自 Worker 的消息（running / progress / 终态）都会刷新该窗口，
    /// 因此它约束的是「Worker 存活却不再说话」（管道写失败、进程假死），而**不是任务耗时**
    /// 只要进度消息仍在流动，提取数十 GB 镜像也不会触发。
    /// </summary>
    public const int ReportIdleTimeoutMs = 30 * 60 * 1000;

    /// <summary>
    /// Worker 心跳间隔：命令执行期间由 <c>PipeWriter</c> 定时发送 <see cref="KeepAlive"/>。
    /// 必须显著小于 <see cref="ReportIdleTimeoutMs"/>（约 1/30），使静默兜底只在真正假死时触发。
    /// </summary>
    /// <remarks>
    /// 并非每条命令都会持续上报进度。比如DISM方式的映像部署全程不发任何进度消息。
    /// 没有心跳时，一个正常推进但长时间无消息的长任务会被静默兜底误判为假死。
    /// </remarks>
    public const int KeepAliveIntervalMs = 60_000;

    // ── 消息类型常量 ──
    public const string TaskRunning = "task_running";
    public const string TaskProgress = "task_progress";
    public const string TaskCompleted = "task_completed";
    public const string TaskFailed = "task_failed";
    public const string TaskCancel = "task_cancel";
    public const string TaskCancelled = "task_cancelled";

    /// <summary>心跳 —— Worker 存活信号，不携带业务语义，仅用于刷新 Main 侧的静默兜底窗口。</summary>
    public const string KeepAlive = "keepalive";

    // ── 握手消息类型（Main → Worker 三次握手中的控制消息）──
    public const string HandshakeReady = "ready";
    public const string HandshakeAck = "ack";

    // ── Pipe 名称 ──
    public static string GeneratePipeName()
        => $"WTGWizardWorker_{Environment.ProcessId}";

    // ── 消息构建（手动拼接，AOT 兼容） ──

    public static string BuildRunning(string task, string? description = null)
    {
        if (description is not null)
            return $"{{\"type\":\"{TaskRunning}\",\"task\":\"{task}\",\"description\":\"{Escape(description)}\"}}";
        return $"{{\"type\":\"{TaskRunning}\",\"task\":\"{task}\"}}";
    }

    public static string BuildProgress(string task, double percent)
        => $"{{\"type\":\"{TaskProgress}\",\"task\":\"{task}\",\"percent\":{percent.ToString("F1", CultureInfo.InvariantCulture)}}}";

    public static string BuildCompleted(string task, int returnCode)
        => $"{{\"type\":\"{TaskCompleted}\",\"task\":\"{task}\",\"returnCode\":{returnCode}}}";

    public static string BuildFailed(string task, int returnCode, string? message = null)
    {
        if (message is not null)
            return $"{{\"type\":\"{TaskFailed}\",\"task\":\"{task}\",\"returnCode\":{returnCode},\"message\":\"{Escape(message)}\"}}";
        return $"{{\"type\":\"{TaskFailed}\",\"task\":\"{task}\",\"returnCode\":{returnCode}}}";
    }

    public static string BuildCancel()
        => $"{{\"type\":\"{TaskCancel}\"}}";

    /// <summary>Worker 回报任务已取消（与指令 task_cancel 分离的专用上报）。</summary>
    public static string BuildCancelled(string task)
        => $"{{\"type\":\"{TaskCancelled}\",\"task\":\"{task}\"}}";

    /// <summary>心跳消息（Worker → Main）。</summary>
    public static string BuildKeepAlive()
        => $"{{\"type\":\"{KeepAlive}\"}}";

    public static string BuildReady()
        => $"{{\"type\":\"{HandshakeReady}\"}}";

    public static string BuildAck()
        => $"{{\"type\":\"{HandshakeAck}\"}}";

    // ── JSON 字符串转义 ──
    public static string Escape(string s)
    {
        return s
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r")
            .Replace("\t", "\\t");
    }
}
