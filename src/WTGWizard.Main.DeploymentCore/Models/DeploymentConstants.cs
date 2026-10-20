namespace WTGWizard.Main.DeploymentCore.Models;

/// <summary>
/// 部署执行参数 — Worker 命令超时值。
/// 磁盘布局常量见 <see cref="WTGWizard.Shared.Services.DiskServices.DiskConstants"/>。
/// </summary>
public static class DeploymentConstants
{
    /// <summary>
    /// “不设超时”即无限等待进程<b>自身</b>退出。
    /// 只用于耗时随数据量增长、且活锁另由 Main 侧“静默兜底”和“心跳协议”承担的操作。
    /// 目前仅 DISM /Apply-Image（ExtractStep），全量展开的耗时由映像大小与目标盘速度决定，
    /// 任何固定上限都会在慢盘/大镜像上误杀一次实际成功的部署。
    /// </summary>
    
    // TODO: 使超时配置可以在SettingsOage查看和设置
    public const int TimeoutNone = 0;
    public const int TimeoutPartitionMs = 300_000;   // 5 分钟（pwsh）
    public const int TimeoutDriverMs = 600_000;      // 10 分钟（DISM /Add-Driver）
    public const int TimeoutApplyWtgMs = 300_000;    // 5 分钟（DISM /Apply-Unattend）
    public const int TimeoutBcdbootMs = 180_000;     // 3 分钟（bcdboot）
    public const int TimeoutCleanupMs = 300_000;     // 5 分钟（pwsh）
}
