namespace WTGWizard.Main.DeploymentCore.Models;

/// <summary>
/// 部署执行结局 — 从 Worker 到 UI 统一使用的结局语义源。
/// </summary>
public enum TaskOutcome
{
    /// <summary>成功完成。</summary>
    Success,

    /// <summary>被取消（主动终止 / 软取消 / 硬中断）。</summary>
    Cancelled,

    /// <summary>失败（执行错误 / 超时 / 异常）。</summary>
    Failed,
}
