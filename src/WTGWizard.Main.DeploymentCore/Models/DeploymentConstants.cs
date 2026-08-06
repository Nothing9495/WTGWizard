namespace WTGWizard.Main.DeploymentCore.Models;

/// <summary>
/// 部署执行参数 — Worker 命令超时值。
/// 磁盘布局常量见 <see cref="WTGWizard.Shared.Services.DiskServices.DiskConstants"/>。
/// </summary>
public static class DeploymentConstants
{
    public const int TimeoutPartitionMs = 300_000;   // 5 分钟（pwsh）
    public const int TimeoutDriverMs = 600_000;      // 10 分钟（DISM /Add-Driver）
    public const int TimeoutApplyWtgMs = 120_000;    // 2 分钟（DISM /Apply-Unattend）
    public const int TimeoutBcdbootMs = 120_000;     // 2 分钟（bcdboot）
    public const int TimeoutCleanupMs = 300_000;     // 5 分钟（pwsh）
}
