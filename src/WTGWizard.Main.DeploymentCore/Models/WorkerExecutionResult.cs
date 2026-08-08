namespace WTGWizard.Main.DeploymentCore.Models;

/// <summary>
/// Worker 命令执行结果 — 以 <see cref="TaskOutcome"/> 表达结局，兼容保留 Success 计算属性。
/// </summary>
public sealed record WorkerExecutionResult(TaskOutcome Outcome, int ExitCode = 0, string? ErrorMessage = null)
{
    /// <summary>是否成功（兼容旧判断）。</summary>
    public bool Success => Outcome == TaskOutcome.Success;

    /// <summary>是否取消。</summary>
    public bool IsCancelled => Outcome == TaskOutcome.Cancelled;

    public static WorkerExecutionResult Ok(int exitCode = 0)
        => new(TaskOutcome.Success, exitCode);

    public static WorkerExecutionResult Fail(int exitCode, string error)
        => new(TaskOutcome.Failed, exitCode, error);

    public static WorkerExecutionResult Cancelled()
        => new(TaskOutcome.Cancelled, -1, "Cancelled");
}
