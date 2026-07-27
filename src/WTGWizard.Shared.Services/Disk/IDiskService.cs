using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace WTGWizard.Shared.Services.Disk;

/// <summary>
/// 磁盘服务接口 — 提供磁盘枚举、安全检测、分区查询等功能。
/// </summary>
public interface IDiskService
{
    /// <summary>枚举外部磁盘。</summary>
    Task<IReadOnlyList<DiskBasicInfo>> EnumerateExternalDisksAsync(CancellationToken ct = default);

    /// <summary>检查磁盘安全性（系统盘、页面文件等）。</summary>
    Task<string?> CheckDiskSafetyAsync(string diskDeviceId, CancellationToken ct = default);

    /// <summary>获取磁盘分区列表。</summary>
    Task<IReadOnlyList<PartitionBasicInfo>> GetPartitionsAsync(uint diskIndex, bool skipEsp = true, CancellationToken ct = default);
}
