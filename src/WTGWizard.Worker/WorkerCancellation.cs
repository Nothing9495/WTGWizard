using System.Threading;

namespace WTGWizard.Worker;

/// <summary>
/// Worker 全局取消源 — 收到主进程 task_cancel 指令时触发。
/// 各命令将 Token 传入 ProcessRunner / WimService 实现可取消执行。
/// </summary>
internal static class WorkerCancellation
{
    public static CancellationTokenSource Cts { get; } = new();

    public static CancellationToken Token => Cts.Token;
}
