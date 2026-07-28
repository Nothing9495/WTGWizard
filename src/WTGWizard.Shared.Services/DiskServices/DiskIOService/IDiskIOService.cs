using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WTGWizard.Shared.Services.DiskServices;

/// <summary>
/// 磁盘 I/O 服务接口 — 提供磁盘枚举、安全检测、分区查询、设备监视等功能。
/// </summary>
public interface IDiskIOService
{
    // ═══ 读操作 ═══

    /// <summary>枚举外部磁盘。</summary>
    Task<IReadOnlyList<DiskBasicInfo>> EnumerateExternalDisksAsync(CancellationToken ct = default);

    /// <summary>检查磁盘安全性（系统盘、页面文件等）。</summary>
    Task<string?> CheckDiskSafetyAsync(string diskDeviceId, CancellationToken ct = default);

    /// <summary>获取磁盘分区列表。</summary>
    Task<IReadOnlyList<PartitionBasicInfo>> GetPartitionsAsync(uint diskIndex, bool skipEsp = true, CancellationToken ct = default);

    // ═══ Watcher ═══

    /// <summary>磁盘或盘符发生变化时触发。</summary>
    event Action? DisksChanged;

    /// <summary>启动磁盘监视。</summary>
    void StartWatcher();

    /// <summary>停止磁盘监视。</summary>
    void StopWatcher();
}
