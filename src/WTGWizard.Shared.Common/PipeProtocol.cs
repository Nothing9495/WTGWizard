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

    // ── 消息类型常量 ──
    public const string TaskRunning = "task_running";
    public const string TaskProgress = "task_progress";
    public const string TaskCompleted = "task_completed";
    public const string TaskFailed = "task_failed";
    public const string TaskCancel = "task_cancel";

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
