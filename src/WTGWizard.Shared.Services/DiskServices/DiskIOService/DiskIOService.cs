using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Shared.Services.DiskServices;

/// <summary>
/// 磁盘 I/O 服务实现 — 委托给 DiskIOReader（读）、DiskIOWriter（写）、DiskIOWatcher（监视）。
/// </summary>
public sealed class DiskIOService : IDiskIOService
{
    private readonly DiskIOReader _reader;
    private readonly DiskIOWriter _writer;
    private readonly DiskIOWatcher _watcher;

    public DiskIOService(ILoggerService logger)
    {
        _reader = new DiskIOReader(logger);
        _writer = new DiskIOWriter(logger);
        _watcher = new DiskIOWatcher(logger);
    }

    // ═══ 读操作（委托给 Reader）═══

    /// <inheritdoc/>
    public Task<IReadOnlyList<DiskBasicInfo>> EnumerateExternalDisksAsync(CancellationToken ct = default)
        => _reader.EnumerateExternalDisksAsync(ct);

    /// <inheritdoc/>
    public Task<string?> CheckDiskSafetyAsync(string diskDeviceId, CancellationToken ct = default)
        => _reader.CheckDiskSafetyAsync(diskDeviceId, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<PartitionBasicInfo>> GetPartitionsAsync(uint diskIndex, bool skipEsp = true, CancellationToken ct = default)
        => _reader.GetPartitionsAsync(diskIndex, skipEsp, ct);

    // ═══ Watcher（委托给 Watcher）═══

    /// <inheritdoc/>
    public event Action? DisksChanged
    {
        add => _watcher.DisksChanged += value;
        remove => _watcher.DisksChanged -= value;
    }

    /// <inheritdoc/>
    public void StartWatcher() => _watcher.Start();

    /// <inheritdoc/>
    public void StopWatcher() => _watcher.Stop();
}
