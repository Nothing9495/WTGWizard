using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using WTGWizard.Shared.Services.Logger;

namespace WTGWizard.Shared.Services.DiskServices;

/// <summary>
/// 磁盘 I/O 服务实现 — 委托给 DiskIOReader（读）和 DiskIOWriter（写）。
/// </summary>
public sealed class DiskIOService : IDiskIOService
{
    private readonly DiskIOReader _reader;
    private readonly DiskIOWriter _writer;

    public DiskIOService(ILoggerService logger)
    {
        _reader = new DiskIOReader(logger);
        _writer = new DiskIOWriter(logger);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<DiskBasicInfo>> EnumerateExternalDisksAsync(CancellationToken ct = default)
        => _reader.EnumerateExternalDisksAsync(ct);

    /// <inheritdoc/>
    public Task<string?> CheckDiskSafetyAsync(string diskDeviceId, CancellationToken ct = default)
        => _reader.CheckDiskSafetyAsync(diskDeviceId, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<PartitionBasicInfo>> GetPartitionsAsync(uint diskIndex, bool skipEsp = true, CancellationToken ct = default)
        => _reader.GetPartitionsAsync(diskIndex, skipEsp, ct);
}
