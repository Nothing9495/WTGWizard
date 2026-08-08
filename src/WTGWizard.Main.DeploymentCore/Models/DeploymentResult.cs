namespace WTGWizard.Main.DeploymentCore.Models;

/// <summary>
/// 部署整体结局 — 以 <see cref="TaskOutcome"/> 单一判定（Success / Cancelled / Failed）。
/// </summary>
public sealed record DeploymentResult(TaskOutcome Outcome, DeployTaskId? FailedAt = null, string? ErrorMessage = null)
{
    /// <summary>是否成功（兼容旧判断）。</summary>
    public bool IsSuccess => Outcome == TaskOutcome.Success;

    /// <summary>是否取消。</summary>
    public bool IsCancelled => Outcome == TaskOutcome.Cancelled;

    public static DeploymentResult Ok() => new(TaskOutcome.Success);

    public static DeploymentResult Failed(DeployTaskId? step, string error) => new(TaskOutcome.Failed, step, error);

    public static DeploymentResult Cancelled() => new(TaskOutcome.Cancelled, null, "Cancelled");
}
