namespace WTGWizard.Main.DeploymentCore.Models;

/// <summary>
/// 步骤执行结果 — 以 <see cref="TaskOutcome"/> 表达结局；
/// <see cref="NonFatal"/> 表示失败但不终止部署（非致命语义显式化）。
/// </summary>
public sealed record StepResult(TaskOutcome Outcome, string? ErrorMessage = null, bool NonFatal = false)
{
    /// <summary>是否成功（兼容旧判断）。</summary>
    public bool IsSuccess => Outcome == TaskOutcome.Success;

    /// <summary>是否取消。</summary>
    public bool IsCancelled => Outcome == TaskOutcome.Cancelled;

    public static StepResult Ok() => new(TaskOutcome.Success);

    public static StepResult Fail(string msg) => new(TaskOutcome.Failed, msg);

    /// <summary>非致命失败：任务标记失败但部署继续（如 Cleanup 盘符移除失败）。</summary>
    public static StepResult NonFatalFail(string msg) => new(TaskOutcome.Failed, msg, NonFatal: true);
}
